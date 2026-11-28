// ModProjectHistory.cs
// Remake-only: the Mod Builder's undo / redo for an open project (ModBuilder.History). A step is the project's state: its
// JSON (ModProject.CaptureState: the mod, ships with their hardpoints, items, characters, builder.json) and the asset files
// the builder writes (models/, textures/, icons/, portraits/, fx/, sky/, cards/, music/, sounds/, voices/: path -> content hash). Each file's content is kept once, by
// its hash, in a store outside the project (persistentDataPath/ModBuilderHistory/<session>), so an undo can bring back a
// file the builder deleted or overwrote (a replaced skin texture, a removed model). The builder records a step at its
// save points (Commit: the 0.4 s debounced save after an edit, a reload), so a drag or a typed value is one step; nothing
// is recorded while the state is the same. Undo / Redo put a step back into the project (in memory and its files) and
// save it; the caller reads the project again. The store goes when the builder closes (Dispose) and, left over from a
// crash, when the next one opens. Plain C#.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using GoF2Remake.Data;
using UnityEngine;

namespace GoF2Remake.Modding
{
    public sealed class ModProjectHistory : IDisposable
    {
        /// <summary>The project folders whose files are part of a step (what the builder copies, writes and deletes).</summary>
        public static readonly string[] TrackedFolders = { ModProject.ModelsFolder, ModContent.TexturesFolder, "icons", "portraits", "fx", "sky", "cards",
                                                            ModProject.MusicFolder, ModProject.SoundsFolder, ModProject.VoicesFolder, ProjectQuest.Folder, "text", "tutorials", ModScripts.Folder };
        /// <summary>The most steps kept back (the oldest go first).</summary>
        public const int MaxSteps = 100;

        /// <summary>Where the stores live (one folder per open project).</summary>
        public static string StoreRoot => Path.Combine(GamePaths.Data, "ModBuilderHistory");

        sealed class State
        {
            public JObject json;
            public Dictionary<string, string> files;   // project-relative path ('/') -> content hash
            public string text;                        // json + files, for comparing states
        }

        readonly ModProject project;
        readonly string store;
        readonly List<State> undo = new List<State>(), redo = new List<State>();
        State current;
        /// <summary>A file's hash by its path, size and change time: unchanged files aren't read again at every step.</summary>
        readonly Dictionary<string, (long size, DateTime time, string hash)> hashes = new Dictionary<string, (long, DateTime, string)>();

        public ModProjectHistory(ModProject project)
        {
            this.project = project;
            store = Path.Combine(StoreRoot, $"{project.Id}_{DateTime.UtcNow.Ticks}");
            Directory.CreateDirectory(store);
            current = Capture();
        }

        public bool CanUndo => undo.Count > 0;
        public bool CanRedo => redo.Count > 0;
        public int UndoCount => undo.Count;
        public int RedoCount => redo.Count;

        /// <summary>The project's state now becomes a step when it differs from the last one (a new edit drops the redo
        /// steps). True when one was recorded.</summary>
        public bool Commit()
        {
            var now = Capture();
            if (now.text == current.text) return false;
            undo.Add(current);
            if (undo.Count > MaxSteps) undo.RemoveAt(0);
            redo.Clear();
            current = now;
            return true;
        }

        /// <summary>The project's state now joins the last step without making one of its own: what the builder makes by
        /// itself (a ship's generated shop icon), which an undo shouldn't take back on its own.</summary>
        public void Absorb() => current = Capture();

        /// <summary>Back one step (what isn't recorded yet is recorded first, so Redo brings it back). False: nothing to undo.</summary>
        public bool Undo() => Step(undo, redo);

        /// <summary>Forward one step. False: nothing to redo.</summary>
        public bool Redo() => Step(redo, undo);

        bool Step(List<State> from, List<State> to)
        {
            project.RefreshProtectedConfigurations();
            Commit();
            if (from.Count == 0) return false;
            var target = from[from.Count - 1];
            var previous = current;
            try { Apply(target); }
            catch
            {
                // Do not consume the history step if its save failed.
                project.RestoreState(previous.json);
                throw;
            }
            from.RemoveAt(from.Count - 1);
            to.Add(previous);
            current = target;
            return true;
        }

        void Apply(State s)
        {
            project.RestoreState(s.json);
            project.Save();
            // The files: what the step has (brought back from the store where missing or different), and nothing else.
            var now = Files();
            foreach (var kv in now)
                if (!s.files.ContainsKey(kv.Key))
                {
                    Keep(kv.Key, kv.Value);
                    try { File.Delete(Full(kv.Key)); } catch (Exception e) { Debug.LogWarning($"Mod Builder: undo couldn't delete {kv.Key}: {e.Message}"); }
                }
            foreach (var kv in s.files)
            {
                if (now.TryGetValue(kv.Key, out var h) && h == kv.Value) continue;
                string src = Path.Combine(store, kv.Value), dst = Full(kv.Key);
                if (!File.Exists(src)) { Debug.LogWarning($"Mod Builder: undo has no copy of {kv.Key}"); continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(dst));
                ModProject.SafeWriteBytes(dst, File.ReadAllBytes(src));
                hashes.Remove(dst);
            }
            // Folders left empty (a skin's own textures/<model>/).
            foreach (var folder in TrackedFolders)
            {
                string dir = Path.Combine(project.Folder, folder);
                if (!Directory.Exists(dir)) continue;
                foreach (var d in Directory.GetDirectories(dir, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length))
                    if (!Directory.EnumerateFileSystemEntries(d).Any()) try { Directory.Delete(d); } catch { }
            }
        }

        State Capture()
        {
            var json = project.CaptureState();
            var files = Files();
            foreach (var kv in files) Keep(kv.Key, kv.Value);
            string text = json.ToString(Newtonsoft.Json.Formatting.None) + "|" + string.Join(";", files.OrderBy(k => k.Key, StringComparer.Ordinal).Select(k => k.Key + "=" + k.Value));
            return new State { json = json, files = files, text = text };
        }

        /// <summary>The tracked files now, by project-relative path, with their content hashes.</summary>
        Dictionary<string, string> Files()
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var folder in TrackedFolders)
            {
                string dir = Path.Combine(project.Folder, folder);
                if (!Directory.Exists(dir)) continue;
                foreach (var f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                {
                    if (f.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) continue;
                    string rel = f.Substring(project.Folder.Length).TrimStart('/', '\\').Replace('\\', '/');
                    if (string.Equals(rel, ModNpcFactionWar.StagedFile, StringComparison.OrdinalIgnoreCase)) continue; // managed as configuration JSON
                    string h = Hash(f);
                    if (h != null) d[rel] = h;
                }
            }
            // Keep the race origins file in history without depending on the multiplayer assembly.
            foreach (string rel in new[] { ModPvpPiracy.File, "raceOrigins.json" })
            {
                string path = Full(rel);
                if (File.Exists(path) && Hash(path) is string hash) d[rel] = hash;
            }
            return d;
        }

        string Full(string rel) => Path.Combine(project.Folder, rel.Replace('/', Path.DirectorySeparatorChar));

        /// <summary>The file's content in the store (once per content).</summary>
        void Keep(string rel, string hash)
        {
            string dst = Path.Combine(store, hash);
            if (File.Exists(dst)) return;
            try { File.Copy(Full(rel), dst); }
            catch (Exception e) { Debug.LogWarning($"Mod Builder: undo couldn't keep {rel}: {e.Message}"); }
        }

        string Hash(string path)
        {
            try
            {
                var info = new FileInfo(path);
                if (hashes.TryGetValue(path, out var c) && c.size == info.Length && c.time == info.LastWriteTimeUtc) return c.hash;
                using var stream = File.OpenRead(path);
                using var sha = System.Security.Cryptography.SHA1.Create();
                string h = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
                hashes[path] = (info.Length, info.LastWriteTimeUtc, h);
                return h;
            }
            catch (Exception e) { Debug.LogWarning($"Mod Builder: undo couldn't read {path}: {e.Message}"); return null; }
        }

        /// <summary>The store goes (the builder closed).</summary>
        public void Dispose()
        {
            try { if (Directory.Exists(store)) Directory.Delete(store, true); } catch { }
        }

        /// <summary>Stores a crashed or killed builder left behind.</summary>
        public static void PruneStores()
        {
            try { if (Directory.Exists(StoreRoot)) Directory.Delete(StoreRoot, true); } catch { }
        }
    }
}
