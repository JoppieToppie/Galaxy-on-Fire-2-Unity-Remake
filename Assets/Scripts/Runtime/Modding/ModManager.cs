// ModManager.cs
// The installed mods and which of them are on. Mods are data only (the game is IL2CPP: no code is ever loaded from a
// mod), read from the Mods folders:
//   Application.persistentDataPath/Mods  every platform (Android: Android/data/com.joppietoppie.gof2remake/files/Mods,
//                                        reachable over USB; UWP: the package's LocalState/Mods)
//   <the game's folder>/Mods             Windows / Linux / macOS players
//   <the project>/Mods                   the Editor (git-ignored)
// Each mod is a folder or a .zip archive with a mod.json (ModManifest). The player turns mods on and off in
// the main menu's mod browser (PlayerPrefs "mods_enabled": the ids in load order, one per line); new mods start off.
// What is on is Active: the enabled, unbroken mods whose dependencies are on, in load order with dependencies first.
// A multiplayer session replaces it (BeginSession): a session without modded content has none, one that allows it has
// exactly the host's. Every change bumps Revision, so ModContent re-reads the content and Database.Load merges it.
// The Mod Builder (ModBuilder) replaces it too while a project is open: the project's folder is the only active mod
// (SetBuilderProject), so it is tested on its own and its ship models reload quickly.
// BaseContent is always in Active, ahead of them: what the remake adds to the game, read like a mod so that it is numbered
// by a key (ModRegistry) and not in a table the mods' numbers follow. It is no mod to the player: not installed, never
// off, in no session's list and no save's; UserMods is Active without it.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace GoF2Remake.Modding
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class ModManager
    {
        const string PrefsKey = "mods_enabled";
        /// <summary>BaseContent's id, so "gof2:<id>" is a base item's key. No mod may use it.</summary>
        public const string BaseId = "gof2";

        static List<ModInfo> installed;
        static List<ModInfo> active;
        static List<string> sessionIds;   // null = single player (the enabled mods)
        static ModInfo builderProject;    // the Mod Builder's open project (ModBuilder), null = none
        static ModInfo baseContent;

        static bool baseMissed;

        /// <summary>The remake's own additions (Resources/GoF2Base, see the file comment). Null while its files can't be read
        /// (Resources not ready in a fresh Editor, a build without the folder): the game goes on without them, Active isn't kept
        /// meanwhile, and the read that finds them bumps Revision, so the numbering and the tables are made again with them.</summary>
        public static ModInfo BaseContent
        {
            get
            {
                if (baseContent != null) return baseContent;
                var source = new BaseContentSource();
                try { baseContent = new ModInfo(ModManifest.Read(source), source); }
                catch (ModJsonException e)
                {
                    if (!baseMissed) Debug.LogWarning($"ModManager: the game's own content can't be read yet ({e.Message})");
                    baseMissed = true;
                    return null;
                }
                if (baseMissed) { baseMissed = false; Revision++; }
                return baseContent;
            }
        }

        /// <summary>The Mod Builder has a project open and its throwaway game runs (ModBuilder.Active; never saved).</summary>
        public static bool BuilderOpen { get; internal set; }

        /// <summary>Bumped whenever the active mods may have changed (a scan, a toggle, the order, a session).</summary>
        public static int Revision { get; private set; }

        public static event Action Changed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            if (installed != null) foreach (var m in installed) m.Release();
            installed = null; active = null; sessionIds = null;
            builderProject?.Release();
            builderProject = null;
            Revision++;
        }

#if UNITY_EDITOR
        // A code reload drops these lists without closing the zips they read, and the Editor then kept a lock on every zip mod
        // until it quit (an export couldn't replace its own mod): they are closed before the reload.
        [UnityEditor.InitializeOnLoadMethod]
        static void CloseZipsBeforeReload() => UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += () =>
        {
            if (installed != null) foreach (var m in installed) m.Release();
            builderProject?.Release();
        };
#endif

        /// <summary>The folder every platform reads (created on first use).</summary>
        public static string UserFolder => Path.Combine(GoF2Remake.GamePaths.Data, "Mods");

        /// <summary>Every folder mods are read from, the user folder first.</summary>
        public static IEnumerable<string> Folders
        {
            get
            {
                yield return UserFolder;
                if (!string.IsNullOrEmpty(GoF2Remake.GamePaths.Override)) yield break;   // a test sandbox: its own folder only
#if UNITY_EDITOR
                yield return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Mods"));
#elif UNITY_STANDALONE
                yield return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Mods"));
#endif
            }
        }

        /// <summary>Every mod found, sorted by load order (the enabled ones in their order, then the rest by name).</summary>
        public static IReadOnlyList<ModInfo> Installed { get { if (installed == null) Scan(); return installed; } }

        public static ModInfo Find(string id) =>
            builderProject != null && string.Equals(builderProject.Id, id, StringComparison.OrdinalIgnoreCase) ? builderProject
            : Installed.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase));

        /// <summary>Reads the Mods folders again (the browser's Refresh, entering the main menu).</summary>
        public static void Scan()
        {
            if (installed != null) foreach (var m in installed) m.Release();
            installed = new List<ModInfo>();
            try { Directory.CreateDirectory(UserFolder); } catch (Exception e) { Debug.LogWarning($"Mods: can't create {UserFolder}: {e.Message}"); }
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var folder in Folders.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!Directory.Exists(folder)) continue;
                var entries = Directory.GetDirectories(folder).Concat(Directory.GetFiles(folder))
                    .Where(p => !Path.GetFileName(p).StartsWith(".")).OrderBy(p => p, StringComparer.OrdinalIgnoreCase);
                foreach (var path in entries)
                {
                    var src = ModSource.Open(path);
                    if (src == null) continue;
                    ModInfo info;
                    try { info = new ModInfo(ModManifest.Read(src), src); }
                    catch (ModJsonException e) { info = new ModInfo(null, src); info.Errors.Add(e.Message); }
                    if (info.Manifest != null && string.Equals(info.Id, BaseId, StringComparison.OrdinalIgnoreCase))
                        info.Errors.Add($"the id \"{BaseId}\" is the game's own");
                    if (info.Manifest != null && !seen.Add(info.Id))
                        info.Errors.Add($"another mod already uses the id \"{info.Id}\" (only the first one found loads)");
                    installed.Add(info);
                }
            }
            ModContent.ClearParsed();
            foreach (var m in installed) if (!m.Broken) ModContent.Parse(m);   // a broken content file shows in the browser
            SortInstalled();
            Data.GameHooks.ModsScanned?.Invoke(installed.Where(m => m.Manifest != null).Select(m => m.Id));   // removed mods' hangar shadows
            ModTextureCache.PruneCache(installed.Where(m => m.Manifest != null).Select(m => m.Id));   // and their cached textures
            Debug.Log($"Mods: {installed.Count} installed ({string.Join(", ", installed.Select(m => m.Id + (IsEnabled(m.Id) ? " on" : "")))})");
            Invalidate();
        }

        static List<string> EnabledIds
        {
            get
            {
                var s = PlayerPrefs.GetString(PrefsKey, "");
                return s.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).Distinct().ToList();
            }
            set { PlayerPrefs.SetString(PrefsKey, string.Join("\n", value)); PlayerPrefs.Save(); }
        }

        public static bool IsEnabled(string id) => EnabledIds.Contains(id, StringComparer.OrdinalIgnoreCase);

        /// <summary>Turns a mod on (at the end of the load order) or off; on also turns on what it depends on (and what
        /// those depend on), off also turns off the mods that need it.</summary>
        public static void SetEnabled(string id, bool on)
        {
            var ids = EnabledIds;
            ids.RemoveAll(x => string.Equals(x, id, StringComparison.OrdinalIgnoreCase));
            if (on)
            {
                void AddDependencies(ModInfo m, int depth)
                {
                    if (m?.Manifest == null || depth > 32) return;
                    foreach (var dep in m.Manifest.dependencies)
                    {
                        var dm = Find(dep);
                        if (dm == null || ids.Contains(dep, StringComparer.OrdinalIgnoreCase)) continue;
                        AddDependencies(dm, depth + 1);
                        ids.Add(dm.Id);
                    }
                }
                AddDependencies(Find(id), 0);
                ids.Add(id);
            }
            else
                foreach (var d in NeededBy(id, ids)) ids.RemoveAll(x => string.Equals(x, d.Id, StringComparison.OrdinalIgnoreCase));
            EnabledIds = ids;
            SortInstalled();
            Invalidate();
        }

        /// <summary>Moves an enabled mod one place earlier (-1) or later (+1) in the load order.</summary>
        public static void Move(string id, int delta)
        {
            var ids = EnabledIds;
            int i = ids.FindIndex(x => string.Equals(x, id, StringComparison.OrdinalIgnoreCase));
            int j = i + delta;
            if (i < 0 || j < 0 || j >= ids.Count) return;
            (ids[i], ids[j]) = (ids[j], ids[i]);
            EnabledIds = ids;
            SortInstalled();
            Invalidate();
        }

        static void SortInstalled()
        {
            if (installed == null) return;
            var ids = EnabledIds;
            installed = installed
                .OrderBy(m => { int i = ids.FindIndex(x => string.Equals(x, m.Id, StringComparison.OrdinalIgnoreCase)); return i < 0 ? int.MaxValue : i; })
                .ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>The mods whose content is in the game now, in the order it is applied.</summary>
        public static IReadOnlyList<ModInfo> Active
        {
            get
            {
                if (active != null) return active;
                var list = WithBase(builderProject != null ? BuilderActive() : Resolve(sessionIds ?? EnabledIds));
                if (baseContent != null) active = list;   // without it, asked again until it can be read (BaseContent)
                return list;
            }
        }

        /// <summary>Active without BaseContent: the mods as the player, a session and a save know them.</summary>
        public static List<ModInfo> UserMods => Active.Where(m => !m.IsBase).ToList();

        static List<ModInfo> WithBase(List<ModInfo> mods)
        {
            if (BaseContent is ModInfo own && !own.Broken) mods.Insert(0, own);
            return mods;
        }

        /// <summary>The Mod Builder's project (its folder read as a mod), null = none open.</summary>
        public static ModInfo BuilderProject => builderProject;

        /// <summary>The Mod Builder opens a project (its folder, read again: call it after every change to its files) or
        /// closes it (null): while open the project is the only active mod (BuilderActive).</summary>
        public static void SetBuilderProject(string folder)
        {
            builderProject?.Release();
            builderProject = null;
            if (folder != null)
            {
                var src = ModSource.Open(folder);
                if (src == null) Debug.LogWarning($"Mods: the Mod Builder project {folder} has no mod.json");
                else
                {
                    try { builderProject = new ModInfo(ModManifest.Read(src), src); }
                    catch (ModJsonException e) { builderProject = new ModInfo(null, src); builderProject.Errors.Add(e.Message); }
                    ModContent.ClearParsed();
                    if (!builderProject.Broken) ModContent.Parse(builderProject);   // its errors show in the builder
                }
            }
            Invalidate();
        }

        /// <summary>The builder's project alone (with the installed mods it depends on); none while it has errors.</summary>
        static List<ModInfo> BuilderActive()
        {
            if (builderProject.Broken) return new List<ModInfo>();
            var list = Resolve(builderProject.Manifest.dependencies.ToList());
            list.Add(builderProject);
            return list;
        }

        /// <summary>The enabled mods that need 'id', directly or through another (turning it off turns them off).</summary>
        public static List<ModInfo> NeededBy(string id, List<string> enabled = null)
        {
            enabled ??= EnabledIds;
            var result = new List<ModInfo>();
            var queue = new Queue<string>();
            queue.Enqueue(id);
            while (queue.Count > 0)
            {
                string cur = queue.Dequeue();
                foreach (var m in Installed)
                {
                    if (m.Manifest == null || result.Contains(m) || !enabled.Contains(m.Id, StringComparer.OrdinalIgnoreCase)) continue;
                    if (!m.Manifest.dependencies.Contains(cur, StringComparer.OrdinalIgnoreCase)) continue;
                    result.Add(m);
                    queue.Enqueue(m.Id);
                }
            }
            return result;
        }

        /// <summary>Why an enabled mod isn't active (a missing or broken dependency), null = it is.</summary>
        public static string InactiveReason(ModInfo m)
        {
            if (m.Broken) return m.Errors.FirstOrDefault() ?? "broken";
            foreach (var dep in m.Manifest.dependencies)
            {
                var d = Find(dep);
                if (d == null) return $"needs the mod \"{dep}\", which isn't installed";
                if (d.Broken) return $"needs the mod \"{d.Name}\", which has errors";
                if (m.Manifest.TooOld(d) is string need) return $"needs the mod \"{d.Name}\" version {need} or later (installed: {d.Version})";
                if (sessionIds == null && !IsEnabled(dep)) return $"needs the mod \"{d.Name}\" on too";
            }
            return null;
        }

        static List<ModInfo> Resolve(List<string> ids)
        {
            var wanted = ids.Select(Find).Where(m => m != null && !m.Broken).ToList();
            // Drop mods with a missing dependency (repeat: a drop can strand another).
            bool dropped = true;
            while (dropped)
            {
                dropped = false;
                foreach (var m in wanted.ToList())
                    if (m.Manifest.dependencies.Any(d => !wanted.Exists(w => string.Equals(w.Id, d, StringComparison.OrdinalIgnoreCase) && m.Manifest.TooOld(w) == null)))
                    { wanted.Remove(m); dropped = true; }
            }
            // Dependencies first, else the player's order.
            var order = new List<ModInfo>();
            void Visit(ModInfo m, int depth)
            {
                if (order.Contains(m) || depth > 32) return;
                foreach (var d in m.Manifest.dependencies)
                {
                    var dm = wanted.Find(w => string.Equals(w.Id, d, StringComparison.OrdinalIgnoreCase));
                    if (dm != null) Visit(dm, depth + 1);
                }
                order.Add(m);
            }
            foreach (var m in wanted) Visit(m, 0);
            return order;
        }

        /// <summary>The enabled, usable mods in load order, whatever a session runs (hosting with modded content allowed).</summary>
        public static List<ModInfo> SinglePlayerActive => Resolve(EnabledIds);

        /// <summary>"id@version#hash" of every active mod in order, joined with ';' ("" = none).</summary>
        public static string ActiveSignature => string.Join(";", UserMods.Select(m => m.Signature));

        /// <summary>A multiplayer session uses exactly these mods (ids; empty = the original game), not the enabled ones.</summary>
        public static void BeginSession(IEnumerable<string> ids)
        {
            sessionIds = ids?.ToList() ?? new List<string>();
            Invalidate();
        }

        /// <summary>Back to the enabled mods (the session ended).</summary>
        public static void EndSession()
        {
            if (sessionIds == null) return;
            sessionIds = null;
            Invalidate();
        }

        public static bool InSession => sessionIds != null;

        static void Invalidate()
        {
            active = null;
            Revision++;
            Changed?.Invoke();
        }

        /// <summary>The Mods folder a player is pointed at: the user folder, in the Editor the project's Mods folder.</summary>
        public static string MainFolder
        {
            get
            {
#if UNITY_EDITOR
                return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Mods"));
#else
                return UserFolder;
#endif
            }
        }

        /// <summary>The folder a mod is installed in (the Mods folder holding its folder or zip).</summary>
        public static string FolderOf(ModInfo mod) => mod?.Source != null ? Path.GetDirectoryName(mod.Source.Location) : MainFolder;

        /// <summary>Opens a Mods folder in the system's file browser (desktop): the selected mod's, else the main one.</summary>
        public static void OpenFolder(ModInfo mod = null)
        {
            string folder = mod != null ? FolderOf(mod) : MainFolder;
            try { Directory.CreateDirectory(folder); } catch { }
            Application.OpenURL("file:///" + folder.Replace('\\', '/'));
        }
    }
}
