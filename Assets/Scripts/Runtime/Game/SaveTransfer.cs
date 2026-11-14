// SaveTransfer.cs
// Remake-only: every save slot exported to one file and imported back (Options > Gameplay in the main menu), to move a
// game to another PC or phone or keep a copy. The file (.gof2saves) is JSON: a format tag, the exporting build's version
// and each slot's save file as it is on disk. Importing replaces every slot: all of the file is read and checked first
// (the format, each save's version, every station / ship / item / system index it names against the game's tables), and
// only a file that passes all of it is written; the slots it replaces are moved to Saves/BeforeImport first.
// Where the file goes: Windows (and the Editor) asks with the system's save / open dialog; elsewhere (Linux, Android) it
// is the Transfer folder (Documents/GoF2 Remake on Linux, the app's own folder on Android, which a PC reaches over USB
// at Android/data/com.joppietoppie.gof2remake/files/Transfer), and an import reads the newest .gof2saves file there.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace GoF2Remake.Data
{
    public static class SaveTransfer
    {
        public const string Extension = "gof2saves";
        const string Format = "gof2remake-saves";
        const int FormatVersion = 1;
        /// <summary>Larger files are refused unread (a full set of twelve saves is a few MB at most).</summary>
        const long MaxFileBytes = 64L * 1024 * 1024;

        [Serializable]
        class Bundle
        {
            public string format;
            public int formatVersion;
            public string gameVersion;
            public int gameMode;
            public string exportedUtc;
            public List<Slot> slots = new List<Slot>();
        }

        [Serializable]
        class Slot
        {
            public int slot;
            public string json;   // the slot's save file, unchanged
        }

        public enum Outcome { Done, Cancelled, NothingToExport, NoFile, Invalid, Failed }

        public struct Result
        {
            public Outcome outcome;
            public string path;      // the file written / read
            public int slots;        // how many save slots it held
            public string problem;   // why it was refused / failed (English, for the notice and the log)
        }

        /// <summary>The folder used where there is no file dialog (Linux, Android).</summary>
        public static string TransferFolder
        {
            get
            {
                if (Application.isMobilePlatform) return Path.Combine(GoF2Remake.GamePaths.Data, "Transfer");
                string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                if (string.IsNullOrEmpty(docs)) return Path.Combine(GoF2Remake.GamePaths.Data, "Transfer");
                // Linux: MyDocuments is the home folder; use ~/Documents when it exists.
                string sub = Path.Combine(docs, "Documents");
                if (Application.platform == RuntimePlatform.LinuxPlayer && Directory.Exists(sub)) docs = sub;
                return Path.Combine(docs, "GoF2 Remake");
            }
        }

        /// <summary>A file dialog picks the file (Windows, the Editor); else the Transfer folder is used.</summary>
        public static bool HasFileDialog => FileDialog.Available;

        static string DefaultName => $"GoF2 saves {DateTime.Now:yyyy-MM-dd HHmm}.{Extension}";

        // ---- export ------------------------------------------------------------------------------------------------

        public static Result Export()
        {
            var bundle = new Bundle
            {
                format = Format,
                formatVersion = FormatVersion,
                gameVersion = Application.version,
                gameMode = (int)SaveGame.SelectedMode,
                exportedUtc = DateTime.UtcNow.ToString("o"),
            };
            try
            {
                for (int i = 0; i < SaveGame.SlotCount; i++)
                    if (SaveGame.Exists(i)) bundle.slots.Add(new Slot { slot = i, json = File.ReadAllText(SaveGame.SlotPath(i)) });
            }
            catch (Exception e) { return Fail("reading the saves failed: " + e.Message); }
            if (bundle.slots.Count == 0) return new Result { outcome = Outcome.NothingToExport };

            string path;
            if (FileDialog.Available)
            {
                path = FileDialog.Save(DefaultName, Extension);
                if (string.IsNullOrEmpty(path)) return new Result { outcome = Outcome.Cancelled };
                if (!path.EndsWith("." + Extension, StringComparison.OrdinalIgnoreCase)) path += "." + Extension;
            }
            else path = Path.Combine(TransferFolder, DefaultName);

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, JsonUtility.ToJson(bundle, true));
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
            }
            catch (Exception e) { return Fail("writing the file failed: " + e.Message, path); }
            Debug.Log($"SaveTransfer: {bundle.slots.Count} saves exported to {path}");
            return new Result { outcome = Outcome.Done, path = path, slots = bundle.slots.Count };
        }

        // ---- import ------------------------------------------------------------------------------------------------

        /// <summary>Asks for the file (or finds the newest one in the Transfer folder). Null: cancelled / none there.</summary>
        public static string PickImportFile()
        {
            if (FileDialog.Available) return FileDialog.Open(Extension);
            try
            {
                var dir = new DirectoryInfo(TransferFolder);
                if (!dir.Exists) return null;
                return dir.GetFiles("*." + Extension).OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault()?.FullName;
            }
            catch (Exception e) { Debug.LogWarning("SaveTransfer: " + e.Message); return null; }
        }

        /// <summary>Reads and checks the whole file without changing anything (Done: valid, 'slots' saves in it).</summary>
        public static Result Check(string path) => Read(path, out _);

        static Result Read(string path, out Dictionary<int, string> slots)
        {
            slots = null;
            if (string.IsNullOrEmpty(path)) return new Result { outcome = FileDialog.Available ? Outcome.Cancelled : Outcome.NoFile };
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists) return Fail("the file is gone", path);
                if (info.Length > MaxFileBytes) return Invalid("the file is too large to be a save export", path);
                if (!TryReadBundle(File.ReadAllText(path), out slots, out string problem)) return Invalid(problem, path);
            }
            catch (Exception e) { return Fail("reading the file failed: " + e.Message, path); }
            return new Result { outcome = Outcome.Done, path = path, slots = slots.Count };
        }

        /// <summary>Checks the whole file again; only if every save in it is valid, every slot is replaced by the file's.</summary>
        public static Result Import(string path)
        {
            if (NetSession.Active) return Fail("not during a multiplayer session", path);

            // 1. read and check everything; nothing on disk changes until all of it passes
            var read = Read(path, out var slots);
            if (read.outcome != Outcome.Done) return read;

            // 2. the previous import's backup goes (a failure here changes nothing)
            string dir = SaveGame.Folder, backup = Path.Combine(dir, SaveGame.SelectedMode == GameMode.Scavenger ? "BeforeScavengerImport" : "BeforeImport");
            try
            {
                Directory.CreateDirectory(dir);
                if (Directory.Exists(backup)) Directory.Delete(backup, true);
            }
            catch (Exception e) { return Fail("the old backup folder can't be cleared (" + backup + "): " + e.Message, path); }

            // 3. write: the new slots to .import files, the old ones to BeforeImport, then the .import files into place
            try
            {
                foreach (var kv in slots) File.WriteAllText(SaveGame.SlotPath(kv.Key) + ".import", kv.Value);
                for (int i = 0; i < SaveGame.SlotCount; i++)
                {
                    if (!SaveGame.Exists(i)) continue;
                    Directory.CreateDirectory(backup);
                    File.Move(SaveGame.SlotPath(i), Path.Combine(backup, Path.GetFileName(SaveGame.SlotPath(i))));
                }
                foreach (var kv in slots) File.Move(SaveGame.SlotPath(kv.Key) + ".import", SaveGame.SlotPath(kv.Key));
            }
            catch (Exception e)
            {
                Debug.LogError("SaveTransfer: import failed while writing: " + e);
                Restore(backup, slots.Keys);
                return Fail("writing the saves failed (the old saves were put back): " + e.Message, path);
            }
            Debug.Log($"SaveTransfer: {slots.Count} saves imported from {path} (the replaced ones are in {backup})");
            SaveFolder.AfterLocalChange();   // remake: the chosen save folder takes the imported slots
            return new Result { outcome = Outcome.Done, path = path, slots = slots.Count };
        }

        /// <summary>A failed import: drops the half-written slots and moves the backed-up ones back.</summary>
        static void Restore(string backup, IEnumerable<int> imported)
        {
            try
            {
                var written = new HashSet<int>(imported);
                for (int i = 0; i < SaveGame.SlotCount; i++)
                {
                    string p = SaveGame.SlotPath(i);
                    if (File.Exists(p + ".import")) File.Delete(p + ".import");
                    string old = Path.Combine(backup, Path.GetFileName(p));
                    if (File.Exists(old))
                    {
                        if (File.Exists(p)) File.Delete(p);
                        File.Move(old, p);
                    }
                    else if (written.Contains(i) && File.Exists(p)) File.Delete(p);   // a slot that was empty before
                }
            }
            catch (Exception e) { Debug.LogError("SaveTransfer: restoring the old saves failed (they are in " + backup + "): " + e.Message); }
        }

        static bool TryReadBundle(string text, out Dictionary<int, string> slots, out string problem)
        {
            slots = new Dictionary<int, string>();
            Bundle bundle;
            try { bundle = JsonUtility.FromJson<Bundle>(text); }
            catch (Exception e) { problem = "not a save export (" + e.Message + ")"; return false; }
            if (bundle == null || bundle.format != Format) { problem = "not a Galaxy on Fire 2 Remake save export"; return false; }
            if (bundle.formatVersion < 1 || bundle.formatVersion > FormatVersion)
            {
                problem = "exported by a newer version of the game; update it first"; return false;
            }
            if (bundle.gameMode != (int)SaveGame.SelectedMode) { problem = "select the matching game mode in Load game before importing"; return false; }
            if (bundle.slots == null || bundle.slots.Count == 0) { problem = "the file holds no saves"; return false; }
            if (bundle.slots.Count > SaveGame.SlotCount) { problem = "the file holds more saves than there are slots"; return false; }

            var db = Database.Load();
            foreach (var s in bundle.slots)
            {
                if (s == null || s.slot < 0 || s.slot >= SaveGame.SlotCount) { problem = "a save names a slot that doesn't exist"; return false; }
                if (slots.ContainsKey(s.slot)) { problem = $"slot {s.slot} is in the file twice"; return false; }
                if (!SaveGame.TryParse(s.json, db, out var save, out string why)) { problem = $"{SlotName(s.slot)}: {why}"; return false; }
                if (save.gameMode != bundle.gameMode) { problem = "mixed game modes in save export"; return false; }
                slots[s.slot] = s.json;
            }
            problem = null;
            return true;
        }

        static string SlotName(int slot) => slot == SaveGame.AutoSaveSlot ? "the auto-save" : $"save {slot}";

        static Result Fail(string problem, string path = null)
        {
            Debug.LogWarning("SaveTransfer: " + problem);
            return new Result { outcome = Outcome.Failed, problem = problem, path = path };
        }

        static Result Invalid(string problem, string path)
        {
            Debug.LogWarning($"SaveTransfer: {path} refused: {problem}");
            return new Result { outcome = Outcome.Invalid, problem = problem, path = path };
        }
    }
}
