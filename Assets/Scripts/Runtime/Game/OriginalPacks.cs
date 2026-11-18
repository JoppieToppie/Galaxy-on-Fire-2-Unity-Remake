// OriginalPacks.cs
// Feature: docs/notes/player-assets.md "Packs": what the player's copy doesn't provide, distributed separately (never on
// GitHub): the common pack, an edition's gap pack, the HD pack (the HD textures, the Real-ESRGAN skies and their sky
// patches, the 3D planets). A pack is a zip of files at their converted copy's paths ("Textures/main/x.png",
// "Resources/GoF2Planets3D/009/albedo.png") and a gof2pack.json manifest (name, title, version, order: common 0, gap 1,
// HD 2), made by Reference/tools/player_assets/make_packs.py. Picked on the setup screen (or put in GamePaths.Data/Packs),
// a pack is kept in Packs and its files written over the converted copy (later packs win), recorded in the copy's
// _packs.tsv; every kept pack is written again after a conversion (OriginalSetup), and one not yet applied at the next
// start (StartupSplash).
// The HD pack is installed apart (docs/notes/player-assets.md "HD resources"): into a folder of its own (GameContent.HdRoot)
// that the content layer reads before the copy while Options > Graphics "HD resources" is on, or, for a player short of
// space, written over the copy (no way back but converting it again). Either way its file isn't kept (2.3 GB).

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using UnityEngine;

namespace GoF2Remake.Data
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class OriginalPacks
    {
        public const string ManifestName = "gof2pack.json";
        public const string Extension = ".gof2pack";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => LastAdded = null;

        [Serializable]
        public sealed class Info
        {
            public string name, title, edition;
            public int version, order, files;
            public long bytes;
            [NonSerialized] public string file;
        }

        /// <summary>The HD pack (order 2): installed apart, InstallHd.</summary>
        public static bool IsHd(Info info) => info != null && (info.order >= 2 || info.name == "hd");

        /// <summary>PlayerPrefs: how the HD pack is installed (0: in its own folder, the default; 1: over the copy).</summary>
        public const string HdModePref = "hdMode";

        /// <summary>Where kept packs live (main thread: GamePaths).</summary>
        public static string Folder => Path.Combine(GamePaths.Data, "Packs");

        /// <summary>A pack's manifest, or null when 'path' isn't a pack (any thread).</summary>
        public static Info Read(string path)
        {
            try
            {
                using var z = ZipFile.OpenRead(path);
                var e = z.GetEntry(ManifestName);
                if (e == null) return null;
                using var r = new StreamReader(e.Open());
                var info = JsonUtility.FromJson<Info>(r.ReadToEnd());
                if (info == null || string.IsNullOrEmpty(info.name)) return null;
                info.file = path;
                return info;
            }
            catch (Exception e) when (e is IOException || e is InvalidDataException || e is UnauthorizedAccessException || e is ArgumentException) { return null; }
        }

        /// <summary>The kept packs, in the order they are written (common, gap, HD; then by name).</summary>
        public static List<Info> Kept(string folder)
        {
            var list = new List<Info>();
            if (Directory.Exists(folder))
                foreach (var f in Directory.GetFiles(folder))
                {
                    var i = Read(f);
                    if (i != null) list.Add(i);
                }
            return list.OrderBy(i => i.order).ThenBy(i => i.name, StringComparer.Ordinal).ToList();
        }

        /// <summary>Kept packs not yet written into the converted copy (or at another version); the HD pack counts as written
        /// when the HD folder 'hdRoot' has it too.</summary>
        public static List<Info> Pending(string folder, string root, string hdRoot = null)
        {
            var applied = Applied(root);
            var hd = hdRoot != null ? Applied(hdRoot) : new Dictionary<string, int>();
            bool Has(Dictionary<string, int> d, Info p) => d.TryGetValue(p.name, out int v) && v == p.version;
            return Kept(folder).Where(p => !Has(applied, p) && !(IsHd(p) && Has(hd, p))).ToList();
        }

        public static Dictionary<string, int> Applied(string root)
        {
            var d = new Dictionary<string, int>(StringComparer.Ordinal);
            string f = Path.Combine(root, "_packs.tsv");
            if (File.Exists(f))
                foreach (var line in File.ReadAllLines(f))
                {
                    var p = line.Split('\t');
                    if (p.Length >= 2 && int.TryParse(p[1], out int v)) d[p[0]] = v;
                }
            return d;
        }

        /// <summary>Keeps a picked pack in 'folder' under its own name (replacing an older one of that name); its new path.</summary>
        public static string Keep(Info info, string folder, bool move)
        {
            Directory.CreateDirectory(folder);
            foreach (var old in Kept(folder).Where(p => p.name == info.name && p.file != info.file)) File.Delete(old.file);
            string to = Path.Combine(folder, info.name + Extension);
            if (Path.GetFullPath(to) == Path.GetFullPath(info.file)) return to;
            if (File.Exists(to)) File.Delete(to);
            if (move) File.Move(info.file, to); else File.Copy(info.file, to);
            info.file = to;
            return to;
        }

        /// <summary>The last Add's outcome for the options row: what was added, or why not (null: nothing yet).</summary>
        public static string LastAdded { get; private set; }

        /// <summary>Options > Graphics "Add a pack…": the system's file picker, the picked pack kept (written over the copy at
        /// the next start, StartupSplash); 'done' with the outcome line. The HD pack isn't kept: 'done' gets it (line null)
        /// for the HD window to install (UI.HdWindow).</summary>
        public static async void Add(string title, Action<string, Info> done)
        {
            string line;
            try
            {
                var poll = Modding.ModImport.Pick(title, "Galaxy on Fire 2 packs", new[] { Extension.TrimStart('.'), "zip" },
                                                  copyIn: Application.isMobilePlatform);
                Modding.ModImport.Picked picked;
                while ((picked = poll()) == null) await Awaitable.NextFrameAsync();
                if (picked.cancelled) { done?.Invoke(null, null); return; }
                if (picked.error != null) line = picked.error;
                else
                {
                    var info = Read(picked.path);
                    if (info == null) line = Localization.Extra("packNotPack", "That isn't a Galaxy on Fire 2 pack (no gof2pack.json in it).");
                    else if (IsHd(info)) { done?.Invoke(null, info); return; }
                    else
                    {
                        bool copy = picked.path.StartsWith(Application.temporaryCachePath, StringComparison.Ordinal);
                        Keep(info, Folder, move: copy);
                        line = string.Format(Localization.Extra("packAdded", "{0} added: it is installed at the next start."), info.title ?? info.name);
                    }
                }
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { line = e.Message; }
            LastAdded = line;
            done?.Invoke(line, null);
        }

        /// <summary>Writes the pack's files over the converted copy at 'root' and records it (a worker thread may call it).</summary>
        public static void Apply(Info pack, string root, Action<int, int, string> progress)
        {
            string full = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            using (var z = ZipFile.OpenRead(pack.file))
            {
                int done = 0, total = z.Entries.Count;
                foreach (var e in z.Entries)
                {
                    done++;
                    if (e.FullName == ManifestName || e.FullName.EndsWith("/")) continue;
                    string target = Path.GetFullPath(Path.Combine(root, e.FullName));
                    if (!target.StartsWith(full, StringComparison.Ordinal)) throw new InvalidDataException($"{pack.name}: {e.FullName} points outside the copy");
                    if (e.FullName.StartsWith("_", StringComparison.Ordinal)) continue;   // (the copy's own bookkeeping: never a pack's)
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    e.ExtractToFile(target, true);
                    progress?.Invoke(done, total, e.FullName);
                }
            }
            var applied = Applied(root);
            applied[pack.name] = pack.version;
            File.WriteAllLines(Path.Combine(root, "_packs.tsv"), applied.Select(kv => kv.Key + "\t" + kv.Value));
        }

        // ---- the HD pack

        /// <summary>The disk space the HD pack takes in its own folder ('alongside', its files' size) and what writing it over
        /// the copy at 'root' takes instead ('over': the files it replaces there are gone); bytes. Reads the zip's directory
        /// only.</summary>
        public static (long alongside, long over) HdSpace(Info pack, string root)
        {
            long alongside = 0, replaced = 0;
            using (var z = ZipFile.OpenRead(pack.file))
                foreach (var e in z.Entries)
                {
                    if (e.FullName == ManifestName || e.FullName.EndsWith("/") || e.FullName.StartsWith("_", StringComparison.Ordinal)) continue;
                    alongside += e.Length;
                    var f = new FileInfo(Path.Combine(root, e.FullName));
                    if (f.Exists) replaced += f.Length;
                }
            return (alongside, alongside - replaced);
        }

        /// <summary>Installs the HD pack (a worker thread may call it): into the HD folder 'hdRoot' (made beside it as
        /// "&lt;folder&gt;.new" and swapped in, so a failure leaves the old one), or with 'over' written over the copy at
        /// 'root' (an HD folder there was is dropped: its files are the copy's now).</summary>
        public static void InstallHd(Info pack, bool over, string root, string hdRoot, Action<int, int, string> progress)
        {
            string final = hdRoot.TrimEnd('/', '\\');
            if (over)
            {
                Apply(pack, root, progress);
                if (Directory.Exists(final)) Directory.Delete(final, true);
                return;
            }
            string work = final + ".new";
            if (Directory.Exists(work)) Directory.Delete(work, true);
            try { Apply(pack, work, progress); }
            catch { try { if (Directory.Exists(work)) Directory.Delete(work, true); } catch (Exception) { } throw; }
            if (Directory.Exists(final)) Directory.Delete(final, true);
            Directory.Move(work, final);
        }

        /// <summary>The HD folder's files moved over the copy (the space of the originals they replace freed; no way back but
        /// converting the copy again), recorded in its _packs.tsv, the folder gone. A worker thread may call it.</summary>
        public static void MergeHd(string root, string hdRoot, Action<int, int, string> progress)
        {
            string from = Path.GetFullPath(hdRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var files = Directory.GetFiles(from, "*", SearchOption.AllDirectories);
            int done = 0;
            foreach (var f in files)
            {
                done++;
                string rel = f.Substring(from.Length);
                if (rel.StartsWith("_", StringComparison.Ordinal)) continue;   // (its own bookkeeping)
                string to = Path.Combine(root, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(to));
                if (File.Exists(to)) File.Delete(to);
                File.Move(f, to);
                progress?.Invoke(done, files.Length, rel.Replace(Path.DirectorySeparatorChar, '/'));
            }
            var applied = Applied(root);
            foreach (var kv in Applied(hdRoot)) applied[kv.Key] = kv.Value;
            File.WriteAllLines(Path.Combine(root, "_packs.tsv"), applied.Select(kv => kv.Key + "\t" + kv.Value));
            Directory.Delete(hdRoot, true);
        }

        /// <summary>The HD pack is part of the copy at 'root' (written over it).</summary>
        public static bool HdInCopy(string root) => Applied(root).Keys.Any(k => k == "hd");
    }
}
