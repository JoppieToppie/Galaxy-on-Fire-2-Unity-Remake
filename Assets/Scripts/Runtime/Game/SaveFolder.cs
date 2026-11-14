// SaveFolder.cs
// Remake-only (a player's request, Android): the save games (and the ship presets) also kept in a folder the player
// picks, which file managers and sync tools such as Syncthing reach, so a game moves between devices without the
// manual export / import. Options > Gameplay "Save folder", main menu only; default: none (the saves stay in
// persistentDataPath only, as before).
// The design is a mirror: the game always reads and writes its own Saves folder (persistentDataPath, SaveGame) and the
// chosen folder is kept in step with it (Sync): after every save, deletion and import, on entering the main menu and
// when a save list opens. So a folder that can't be reached (a drive unplugged, a revoked grant, a deleted folder)
// never costs a save: the game goes on with its own copies (a notice says so) and catches up once it is back.
// Android: scoped storage keeps other apps out of Android/data, and a plain path outside it isn't readable for files
// other apps wrote (Syncthing's) without MANAGE_EXTERNAL_STORAGE ("All files access", which Google Play restricts and
// which would have to be granted in the system settings); so the folder there is a Storage Access Framework tree
// (SaveFolderActivity.java: ACTION_OPEN_DOCUMENT_TREE with a persistable grant), its files read and written through the
// ContentResolver. iOS: the document picker's folder kept as a security-scoped bookmark (Plugins/iOS/SaveFolderPicker.mm),
// then plain file calls while its access is held. UWP: FolderPicker's folder in the FutureAccessList, its files through
// Windows.Storage. Desktop players: a plain folder (System.IO).
// Sync, per file, against what both sides held at the last sync (its hash, kept in save_folder_sync.json beside the
// Saves folder): a side that changed since wins; one deleted since is deleted on the other side (a local copy goes to
// Saves/Deleted first); both changed, or no common past (the first sync with a folder): the newer save (its savedAtUtc;
// other files the newer file time) wins and the other one is kept in Saves/Deleted. Two devices writing the same slot
// therefore never lose either version.

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace GoF2Remake.Data
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class SaveFolder
    {
        const string TargetKey = "save_folder", NameKey = "save_folder_name";
        const string TreePrefix = "content://";
        /// <summary>iOS: a security-scoped bookmark (base64) after this; UWP: a FutureAccessList token.</summary>
        public const string IosPrefix = "ios-bookmark:", UwpPrefix = "uwp-folder:";
        public const string PresetsFile = "ship_presets.json";

        /// <summary>The folder while the tests run (GamePaths.Override set): the player's own choice is never used then.</summary>
        public static string TestTarget;

        /// <summary>Bumped whenever a sync changes a local file (ShipPresets reads its file again).</summary>
        public static int Revision { get; private set; }

        /// <summary>Why the last sync couldn't reach the folder (null: it could, or no folder is chosen).</summary>
        public static string LastProblem { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { LastProblem = null; TestTarget = null; }

        /// <summary>This platform can keep the saves in another folder (desktop players, the Editor, Android).</summary>
        public static bool Supported
        {
            get
            {
#if UNITY_EDITOR || UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX || UNITY_STANDALONE_OSX || UNITY_ANDROID || UNITY_IOS || (UNITY_WSA && ENABLE_WINMD_SUPPORT)
                return !Application.isBatchMode || Application.isEditor;   // never for a dedicated server
#else
                return false;
#endif
            }
        }

        static bool Sandboxed => !string.IsNullOrEmpty(GamePaths.Override);

        /// <summary>The chosen folder: a path, an Android content:// tree, an iOS bookmark or a UWP access token; "" = none.</summary>
        public static string Target => Sandboxed ? TestTarget ?? "" : PlayerPrefs.GetString(TargetKey, "");

        public static bool Chosen => !string.IsNullOrEmpty(Target);

        /// <summary>The folder as the player knows it ("Documents/GoF2" for an Android tree).</summary>
        public static string DisplayName
        {
            get
            {
                if (!Chosen) return null;
                if (Sandboxed) return Target;
                string name = PlayerPrefs.GetString(NameKey, "");
                return string.IsNullOrEmpty(name) ? Target : name;
            }
        }

        public static string PresetsPath => Path.Combine(GamePaths.Data, PresetsFile);
        static string StatePath => Path.Combine(GamePaths.Data, "save_folder_sync.json");
        static string LocalSaves => SaveGame.Folder;

        /// <summary>The files kept in the folder: the slots and the ship presets (remote name, local path, slot or -1).</summary>
        static IEnumerable<(string name, string local, int slot)> Files()
        {
            foreach (GameMode mode in Enum.GetValues(typeof(GameMode)))
                for (int i = 0; i < SaveGame.SlotCount; i++)
                {
                    string path = SaveGame.SlotPath(i, mode);
                    yield return (Path.GetFileName(path), path, i);
                }
            yield return (PresetsFile, PresetsPath, -1);
        }

        // ---- the folder itself ---------------------------------------------------------------------------------------

        public struct RemoteFile { public long size, modified; }   // modified: ms since 1970 (UTC)

        /// <summary>A place the files are mirrored to.</summary>
        public interface IStore
        {
            /// <summary>Null when it can be used, else why not.</summary>
            string Check();
            Dictionary<string, RemoteFile> List();
            /// <summary>The file's text, null when it isn't there.</summary>
            string Read(string name);
            void Write(string name, string text);
            void Delete(string name);
        }

        sealed class DirectoryStore : IStore
        {
            readonly string dir;
            public DirectoryStore(string dir) { this.dir = dir; }

            // Never made here: a missing drive's mount point (Linux) would get the saves on the wrong disk.
            public string Check() => Directory.Exists(dir) ? null : "the folder isn't there";

            public Dictionary<string, RemoteFile> List()
            {
                var list = new Dictionary<string, RemoteFile>();
                foreach (var f in new DirectoryInfo(dir).GetFiles("*.json"))
                    list[f.Name] = new RemoteFile { size = f.Length, modified = Ms(f.LastWriteTimeUtc) };
                return list;
            }

            public string Read(string name)
            {
                string p = Path.Combine(dir, name);
                return File.Exists(p) ? File.ReadAllText(p) : null;
            }

            public void Write(string name, string text)
            {
                string p = Path.Combine(dir, name), tmp = Path.Combine(dir, "." + name + ".tmp");
                File.WriteAllText(tmp, text);
                if (File.Exists(p)) File.Delete(p);
                File.Move(tmp, p);
            }

            public void Delete(string name)
            {
                string p = Path.Combine(dir, name);
                if (File.Exists(p)) File.Delete(p);
            }
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        sealed class TreeStore : IStore
        {
            const string Helper = "com.joppietoppie.gof2remake.SaveFolderActivity";
            readonly string tree;
            public TreeStore(string tree) { this.tree = tree; }

            static T Call<T>(string method, params object[] args)
            {
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                using var helper = new AndroidJavaClass(Helper);
                return helper.CallStatic<T>(method, With(activity, args));
            }

            static void Call(string method, params object[] args)
            {
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                using var helper = new AndroidJavaClass(Helper);
                helper.CallStatic(method, With(activity, args));
            }

            static object[] With(AndroidJavaObject activity, object[] args)
            {
                var all = new object[args.Length + 1];
                all[0] = activity;
                Array.Copy(args, 0, all, 1, args.Length);
                return all;
            }

            public string Check()
            {
                try { return Call<bool>("granted", tree) ? null : "the game may no longer use the folder (deleted or access taken back)"; }
                catch (Exception e) { return e.Message; }
            }

            public Dictionary<string, RemoteFile> List()
            {
                var list = new Dictionary<string, RemoteFile>();
                foreach (var line in (Call<string>("list", tree) ?? "").Split('\n'))
                {
                    var p = line.Split('\t');
                    if (p.Length < 3 || p[0].Length == 0) continue;
                    long.TryParse(p[1], out long size);
                    long.TryParse(p[2], out long modified);
                    list[p[0]] = new RemoteFile { size = size, modified = modified };
                }
                return list;
            }

            public string Read(string name) => Call<string>("read", tree, name);
            public void Write(string name, string text) => Call("write", tree, name, text);
            public void Delete(string name) => Call<bool>("delete", tree, name);
        }
#endif

#if UNITY_IOS && !UNITY_EDITOR
        /// <summary>iOS: the picked folder's bookmark opened (SaveFolderPicker.mm holds its security-scoped access), then
        /// plain file calls on its path.</summary>
        sealed class BookmarkStore : IStore
        {
            [System.Runtime.InteropServices.DllImport("__Internal")] static extern string GoF2SaveFolder_Open(string bookmark);
            readonly string bookmark;
            DirectoryStore dir;
            public BookmarkStore(string target) { bookmark = target.Substring(IosPrefix.Length); }

            public string Check()
            {
                string path = GoF2SaveFolder_Open(bookmark);
                if (string.IsNullOrEmpty(path)) return "the game may no longer use the folder (moved, deleted or access taken back)";
                dir = new DirectoryStore(path);
                return dir.Check();
            }

            DirectoryStore Dir => dir ?? throw new IOException("the folder isn't open");
            public Dictionary<string, RemoteFile> List() => Dir.List();
            public string Read(string name) => Dir.Read(name);
            public void Write(string name, string text) => Dir.Write(name, text);
            public void Delete(string name) => Dir.Delete(name);
        }
#endif

#if UNITY_WSA && ENABLE_WINMD_SUPPORT && !UNITY_EDITOR
        /// <summary>UWP: the picked folder from the FutureAccessList (FolderPicker's grant kept across launches), its files
        /// through Windows.Storage (an app may not open them with plain file calls). Called on Unity's app thread, not the
        /// UI thread, so waiting on the asynchronous calls is safe.</summary>
        sealed class StorageFolderStore : IStore
        {
            readonly string token;
            Windows.Storage.StorageFolder folder;
            public StorageFolderStore(string target) { token = target.Substring(UwpPrefix.Length); }

            static T Wait<T>(Windows.Foundation.IAsyncOperation<T> op) => System.WindowsRuntimeSystemExtensions.AsTask(op).GetAwaiter().GetResult();
            static void Wait(Windows.Foundation.IAsyncAction op) => System.WindowsRuntimeSystemExtensions.AsTask(op).GetAwaiter().GetResult();

            public string Check()
            {
                try
                {
                    var list = Windows.Storage.AccessCache.StorageApplicationPermissions.FutureAccessList;
                    if (!list.ContainsItem(token)) return "the game may no longer use the folder";
                    folder = Wait(list.GetFolderAsync(token));
                    return folder == null ? "the folder isn't there" : null;
                }
                catch (Exception e) { return e.Message; }
            }

            Windows.Storage.StorageFolder Folder => folder ?? throw new IOException("the folder isn't open");

            public Dictionary<string, RemoteFile> List()
            {
                var list = new Dictionary<string, RemoteFile>();
                foreach (var f in Wait(Folder.GetFilesAsync()))
                {
                    if (!f.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
                    var props = Wait(f.GetBasicPropertiesAsync());
                    list[f.Name] = new RemoteFile { size = (long)props.Size, modified = props.DateModified.ToUnixTimeMilliseconds() };
                }
                return list;
            }

            Windows.Storage.StorageFile Find(string name) => Wait(Folder.TryGetItemAsync(name)) as Windows.Storage.StorageFile;

            public string Read(string name)
            {
                var f = Find(name);
                return f == null ? null : Wait(Windows.Storage.FileIO.ReadTextAsync(f));
            }

            public void Write(string name, string text)
            {
                var f = Wait(Folder.CreateFileAsync(name, Windows.Storage.CreationCollisionOption.ReplaceExisting));
                Wait(Windows.Storage.FileIO.WriteTextAsync(f, text));
            }

            public void Delete(string name)
            {
                var f = Find(name);
                if (f != null) Wait(f.DeleteAsync(Windows.Storage.StorageDeleteOption.PermanentDelete));
            }
        }
#endif

        /// <summary>The store for a target (a path, or an Android tree); tests may hand their own (StoreOverride).</summary>
        public static Func<string, IStore> StoreOverride;

        static IStore StoreFor(string target)
        {
            if (StoreOverride != null) return StoreOverride(target);
#if UNITY_ANDROID && !UNITY_EDITOR
            if (target.StartsWith(TreePrefix, StringComparison.Ordinal)) return new TreeStore(target);
#endif
#if UNITY_IOS && !UNITY_EDITOR
            if (target.StartsWith(IosPrefix, StringComparison.Ordinal)) return new BookmarkStore(target);
#endif
#if UNITY_WSA && ENABLE_WINMD_SUPPORT && !UNITY_EDITOR
            if (target.StartsWith(UwpPrefix, StringComparison.Ordinal)) return new StorageFolderStore(target);
#endif
            return new DirectoryStore(target);
        }

        // ---- the sync ------------------------------------------------------------------------------------------------

        [Serializable]
        class State
        {
            public string target;
            public List<Entry> files = new List<Entry>();
        }

        /// <summary>What both sides held at the last sync, with the file sizes / times then (unchanged: not read again).</summary>
        [Serializable]
        class Entry
        {
            public string name, hash;
            public long localSize, localTicks, remoteSize, remoteModified;
        }

        public enum Outcome { Off, Done, Unreachable, Failed }

        public struct Result
        {
            public Outcome outcome;
            public int pulled, pushed, removed, conflicts;
            public string problem;
            public bool Changed => pulled + pushed + removed + conflicts > 0;
        }

        static long Ms(DateTime utc) => (long)(utc - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;

        static string Hash(string text)
        {
            using var sha = SHA256.Create();
            var h = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
            var sb = new StringBuilder(24);
            for (int i = 0; i < 12; i++) sb.Append(h[i].ToString("x2"));
            return sb.ToString();
        }

        static State LoadState(string target)
        {
            try
            {
                if (File.Exists(StatePath))
                {
                    var s = JsonUtility.FromJson<State>(File.ReadAllText(StatePath));
                    if (s != null && s.target == target) { s.files ??= new List<Entry>(); return s; }
                }
            }
            catch (Exception e) { Debug.LogWarning("SaveFolder: sync state unreadable, starting afresh: " + e.Message); }
            return new State { target = target };
        }

        static void SaveState(State s)
        {
            try { File.WriteAllText(StatePath, JsonUtility.ToJson(s, true)); }
            catch (Exception e) { Debug.LogWarning("SaveFolder: couldn't write the sync state: " + e.Message); }
        }

        static void ForgetState()
        {
            try { if (File.Exists(StatePath)) File.Delete(StatePath); } catch (Exception) { }
        }

        /// <summary>When a save was made (its savedAtUtc), or a file's own time; MinValue: unknown.</summary>
        [Serializable] class Stamp { public string savedAtUtc; }

        static DateTime SavedAt(string json, DateTime fallback)
        {
            try
            {
                var s = JsonUtility.FromJson<Stamp>(json);
                if (s != null && DateTime.TryParse(s.savedAtUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out var t))
                    return t.ToUniversalTime();
            }
            catch (Exception) { }
            return fallback;
        }

        /// <summary>A whole file: a save with its savedAtUtc, or (the presets) any JSON object.</summary>
        static bool Readable(int slot, string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            string t = text.Trim();
            if (!t.StartsWith("{") || !t.EndsWith("}")) return false;
            try { JsonUtility.FromJson<Stamp>(t); }
            catch (Exception) { return false; }
            return slot < 0 || SavedAt(t, DateTime.MinValue) != DateTime.MinValue;
        }

        /// <summary>Keeps the folder and the game's own files in step (see the header). Does nothing without a folder.</summary>
        public static Result Sync()
        {
            var r = new Result { outcome = Outcome.Off };
            if (!Supported && StoreOverride == null || !Chosen) { LastProblem = null; return r; }
            string target = Target;
            var store = StoreFor(target);
            string problem;
            try { problem = store.Check(); }
            catch (Exception e) { problem = e.Message; }
            if (problem != null) return Unreachable(problem);

            var state = LoadState(target);
            try
            {
                var remote = store.List();
                var pushed = new List<Entry>();
                foreach (var (name, local, slot) in Files())
                {
                    var entry = state.files.Find(e => e.name == name);
                    var localInfo = new FileInfo(local);
                    bool hasL = localInfo.Exists, hasR = remote.TryGetValue(name, out var rf);
                    string lText = null, rText = null;
                    string lHash = !hasL ? null
                        : entry != null && entry.localSize == localInfo.Length && entry.localTicks == localInfo.LastWriteTimeUtc.Ticks ? entry.hash
                        : Hash(lText = File.ReadAllText(local));
                    string rHash = !hasR ? null
                        : entry != null && entry.remoteSize == rf.size && entry.remoteModified == rf.modified ? entry.hash
                        : Hash(rText = store.Read(name) ?? "");
                    // A file a sync tool is still writing (or a broken one) is left alone until it reads as a whole.
                    if (rText != null && !Readable(slot, rText))
                    {
                        Debug.LogWarning($"SaveFolder: {name} in {DisplayName} isn't a readable save yet; left alone this time");
                        continue;
                    }

                    if (lHash == rHash)
                    {
                        if (lHash == null) state.files.Remove(entry);
                        else Record(state, ref entry, name, lHash, local, hasR ? rf : default);
                        continue;
                    }

                    bool known = entry != null;
                    if (known && rHash == entry.hash)        // only this device changed it
                    {
                        if (lHash == null) { store.Delete(name); state.files.Remove(entry); r.removed++; }
                        else { Push(store, name, lText ?? File.ReadAllText(local)); Record(state, ref entry, name, lHash, local, default); pushed.Add(entry); r.pushed++; }
                    }
                    else if (known && lHash == entry.hash)   // only the folder changed it
                    {
                        if (rHash == null) { Bin(local, name, "removed"); File.Delete(local); state.files.Remove(entry); r.removed++; Revision++; }
                        else { Pull(local, slot, rText ?? store.Read(name), rf); Record(state, ref entry, name, rHash, local, rf); r.pulled++; }
                    }
                    else if (lHash == null)                  // only the folder has it
                    {
                        Pull(local, slot, rText ?? store.Read(name), rf); Record(state, ref entry, name, rHash, local, rf); r.pulled++;
                    }
                    else if (rHash == null)                  // only this device has it
                    {
                        Push(store, name, lText ?? File.ReadAllText(local)); Record(state, ref entry, name, lHash, local, default); pushed.Add(entry); r.pushed++;
                    }
                    else                                     // both changed: the newer one wins, the other is kept
                    {
                        lText ??= File.ReadAllText(local);
                        rText ??= store.Read(name);
                        var lTime = slot >= 0 ? SavedAt(lText, localInfo.LastWriteTimeUtc) : localInfo.LastWriteTimeUtc;
                        var rRemote = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(rf.modified);
                        var rTime = slot >= 0 ? SavedAt(rText, rRemote) : rRemote;
                        r.conflicts++;
                        if (rTime > lTime)
                        {
                            Bin(local, name, "conflict");
                            Pull(local, slot, rText, rf); Record(state, ref entry, name, rHash, local, rf);
                            Debug.Log($"SaveFolder: {name} changed on both sides, the folder's (newer) taken, this device's kept in Saves/Deleted");
                        }
                        else
                        {
                            BinText(rText, name, "conflict");
                            Push(store, name, lText); Record(state, ref entry, name, lHash, local, default); pushed.Add(entry);
                            Debug.Log($"SaveFolder: {name} changed on both sides, this device's (newer) kept, the folder's kept in Saves/Deleted");
                        }
                    }
                }
                if (pushed.Count > 0)
                {
                    // What the folder made of the written files (its sizes and times), so they aren't read again next time.
                    var after = store.List();
                    foreach (var e in pushed)
                        if (after.TryGetValue(e.name, out var f)) { e.remoteSize = f.size; e.remoteModified = f.modified; }
                }
                SaveState(state);
            }
            catch (Exception e)
            {
                SaveState(state);
                return Unreachable(e.Message);
            }
            LastProblem = null;
            r.outcome = Outcome.Done;
            if (r.Changed) Debug.Log($"SaveFolder: synced with {DisplayName}: {r.pulled} in, {r.pushed} out, {r.removed} removed, {r.conflicts} conflicts");
            return r;
        }

        static Result Unreachable(string problem)
        {
            if (LastProblem != problem) Debug.LogWarning($"SaveFolder: {DisplayName} can't be reached ({problem}); the saves stay on this device until it can");
            LastProblem = problem;
            return new Result { outcome = Outcome.Unreachable, problem = problem };
        }

        static void Record(State state, ref Entry entry, string name, string hash, string local, RemoteFile rf)
        {
            if (entry == null) { entry = new Entry { name = name }; state.files.Add(entry); }
            entry.hash = hash;
            var li = new FileInfo(local);
            entry.localSize = li.Exists ? li.Length : -1;
            entry.localTicks = li.Exists ? li.LastWriteTimeUtc.Ticks : 0;
            entry.remoteSize = rf.size;
            entry.remoteModified = rf.modified;
        }

        static void Push(IStore store, string name, string text) => store.Write(name, text);

        static void Pull(string local, int slot, string text, RemoteFile rf)
        {
            if (text == null) throw new IOException("the folder's file went while it was read");
            Directory.CreateDirectory(Path.GetDirectoryName(local));
            string tmp = local + ".tmp";
            File.WriteAllText(tmp, text);
            if (File.Exists(local)) File.Delete(local);
            File.Move(tmp, local);
            // The save's own time, so the main menu's Resume (the newest slot) picks the newest game, not the newest copy.
            var when = slot >= 0 ? SavedAt(text, DateTime.MinValue) : DateTime.MinValue;
            if (when == DateTime.MinValue && rf.modified > 0) when = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(rf.modified);
            if (when != DateTime.MinValue && when <= DateTime.UtcNow.AddDays(1)) File.SetLastWriteTimeUtc(local, when);
            Revision++;
        }

        /// <summary>A local file kept in Saves/Deleted before it is replaced or removed.</summary>
        static void Bin(string local, string name, string why)
        {
            if (File.Exists(local)) BinText(File.ReadAllText(local), name, why);
        }

        static void BinText(string text, string name, string why)
        {
            if (text == null) return;
            string bin = Path.Combine(LocalSaves, "Deleted");
            Directory.CreateDirectory(bin);
            string to = Path.Combine(bin, $"{Path.GetFileNameWithoutExtension(name)}_{why}_{DateTime.Now:yyyyMMdd-HHmmss}.json");
            for (int n = 2; File.Exists(to); n++) to = Path.Combine(bin, $"{Path.GetFileNameWithoutExtension(name)}_{why}_{DateTime.Now:yyyyMMdd-HHmmss}_{n}.json");
            File.WriteAllText(to, text);
            Prune(bin, Path.GetFileNameWithoutExtension(name).StartsWith("slot_") ? "slot_*.json" : Path.GetFileNameWithoutExtension(name) + "_*.json");
        }

        /// <summary>SaveGame.Delete's rule: the newest 20 of the kind kept.</summary>
        static void Prune(string bin, string pattern)
        {
            var old = new DirectoryInfo(bin).GetFiles(pattern);
            Array.Sort(old, (a, b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));
            for (int i = 20; i < old.Length; i++) old[i].Delete();
        }

        /// <summary>After a save, a deletion or an import: the folder catches up (a no-op without one).</summary>
        public static void AfterLocalChange()
        {
            if (!Chosen) return;
            try { Sync(); }
            catch (Exception e) { Debug.LogWarning("SaveFolder: " + e.Message); }
        }

        // ---- choosing --------------------------------------------------------------------------------------------------

        /// <summary>What a folder holds before it is taken: its saves and this device's (counts), or why it can't be used.</summary>
        public struct Survey { public int folderSaves, localSaves; public string problem; }

        public static Survey Inspect(string target)
        {
            var s = new Survey();
            for (int i = 0; i < SaveGame.SlotCount; i++) if (SaveGame.Exists(i)) s.localSaves++;
            try
            {
                var store = StoreFor(target);
                s.problem = store.Check();
                if (s.problem != null) return s;
                var list = store.List();
                for (int i = 0; i < SaveGame.SlotCount; i++) if (list.ContainsKey($"slot_{i:00}.json")) s.folderSaves++;
            }
            catch (Exception e) { s.problem = e.Message; }
            return s;
        }

        /// <summary>How a folder is taken: both sides kept (the newer save of each slot wins, the other in Saves/Deleted; an
        /// empty folder gets this device's saves), or only the folder's (this device's go to Saves/BeforeFolder first; an
        /// empty folder = starting with no saves).</summary>
        public enum Start { Merge, FolderOnly }

        /// <summary>Takes a folder: remembered, the start applied and the first sync run.</summary>
        public static Result Choose(string target, string displayName, Start start)
        {
            string old = Target;
            if (!string.IsNullOrEmpty(old) && old != target) ReleaseGrant(old);
            ForgetState();
            if (start == Start.FolderOnly) SetAside();
            SetTarget(target, displayName);
            var r = Sync();
            Debug.Log($"SaveFolder: saves now also in {DisplayName} ({start}): {r.outcome}");
            return r;
        }

        /// <summary>Back to the default (this device only): the game's own copies are all there already.</summary>
        public static void UseDefault()
        {
            if (!Chosen) return;
            Sync();   // the last changes out, while it can
            ReleaseGrant(Target);
            ForgetState();
            SetTarget("", "");
            LastProblem = null;
            Debug.Log("SaveFolder: saves on this device only again");
        }

        static void SetTarget(string target, string displayName)
        {
            if (Sandboxed) { TestTarget = target; return; }
            PlayerPrefs.SetString(TargetKey, target ?? "");
            PlayerPrefs.SetString(NameKey, displayName ?? "");
            PlayerPrefs.Save();
        }

        /// <summary>This device's mirrored files moved to Saves/BeforeFolder/&lt;time&gt; (kept, out of the way).</summary>
        static void SetAside()
        {
            string to = Path.Combine(LocalSaves, "BeforeFolder", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            foreach (var (name, local, _) in Files())
            {
                if (!File.Exists(local)) continue;
                Directory.CreateDirectory(to);
                File.Move(local, Path.Combine(to, name));
                Revision++;
            }
        }

        static void ReleaseGrant(string target)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!target.StartsWith(TreePrefix, StringComparison.Ordinal)) return;
            try
            {
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                using var helper = new AndroidJavaClass("com.joppietoppie.gof2remake.SaveFolderActivity");
                helper.CallStatic("release", activity, target);
            }
            catch (Exception e) { Debug.LogWarning("SaveFolder: " + e.Message); }
#elif UNITY_IOS && !UNITY_EDITOR
            if (target.StartsWith(IosPrefix, StringComparison.Ordinal)) GoF2SaveFolder_Close();
#elif UNITY_WSA && ENABLE_WINMD_SUPPORT && !UNITY_EDITOR
            if (!target.StartsWith(UwpPrefix, StringComparison.Ordinal)) return;
            try { Windows.Storage.AccessCache.StorageApplicationPermissions.FutureAccessList.Remove(target.Substring(UwpPrefix.Length)); }
            catch (Exception e) { Debug.LogWarning("SaveFolder: " + e.Message); }
#endif
        }

#if UNITY_IOS && !UNITY_EDITOR
        [System.Runtime.InteropServices.DllImport("__Internal")] static extern void GoF2SaveFolder_Close();
#endif
    }
}
