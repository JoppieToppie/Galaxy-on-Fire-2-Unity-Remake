// PatchInstall.cs
// Remake-only (the in-game updater, CLAUDE.md "Update patcher" in docs/notes/platforms.md): turns the game's folder into
// the build a PatchManifest describes, without touching a file the running game uses:
// 1. Scan: cuts every file of the new build the folder already has (PatchChunker) and lists which files differ and which
//    chunks no local file holds (the download). Chunks downloaded by an earlier, interrupted try are kept and count too.
// 2. Accept: each downloaded chunk is unpacked, checked against its id and kept in _update/chunks.
// 3. Assemble: every changed file is written whole into _update/files from local and downloaded chunks, each chunk
//    checked again on the way.
// 4. WriteApplyScript: a .cmd (Windows) / .sh (Linux) that waits for the game to quit, moves the new files over the old,
//    deletes the files the new version dropped, removes _update and optionally starts the game again.
// Plain C# on System.IO (the downloads are the App's GamePatcher), threads allowed: Scan and Assemble run off the main
// thread; Done / Total are their progress. After Scan, Ranges are the downloads: the needed chunks grouped by pack, with
// neighbours (a gap up to MaxGap) joined into one request of at most MaxRange bytes.
// Android: the manifest's one file is the APK; it is read from the installed APK ('localPath'), and the work folder is in
// the app's own data ('updateDir'): the rebuilt APK goes to Android's installer instead of an apply script.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace GoF2Remake.UI
{
    public sealed class PatchInstall
    {
        /// <summary>The update's working folder inside the game's folder (same drive: applying is a rename).</summary>
        public const string FolderName = "_update";
        const string StateFile = "state.json";

        public readonly string InstallDir, UpdateDir, ChunkDir, FilesDir;
        public readonly PatchManifest Manifest;

        /// <summary>Files whose content differs from the manifest's (or that are missing), after Scan.</summary>
        public readonly List<PatchFile> Changed = new List<PatchFile>();
        /// <summary>Chunk indexes to download, after Scan.</summary>
        public readonly List<int> Needed = new List<int>();
        /// <summary>Removed files the folder still has, after Scan.</summary>
        public readonly List<string> Removing = new List<string>();
        /// <summary>The downloads, after Scan.</summary>
        public readonly List<PatchRange> Ranges = new List<PatchRange>();

        /// <summary>Two needed chunks this close in a pack come in one request (the bytes between downloaded too).</summary>
        public const int MaxGap = 256 * 1024;
        /// <summary>No request is longer (it is held in memory).</summary>
        public const int MaxRange = 16 * 1024 * 1024;
        /// <summary>Bytes to download, after Scan.</summary>
        public long DownloadBytes { get; private set; }
        /// <summary>Unpacked size of the changed files, after Scan (the disk space Assemble needs).</summary>
        public long ChangedBytes { get; private set; }
        /// <summary>Nothing to download or move: the folder already is that build.</summary>
        public bool UpToDate => Changed.Count == 0 && Removing.Count == 0;

        /// <summary>Progress of Scan / Assemble in bytes (read from any thread).</summary>
        public long Done => Interlocked.Read(ref done);
        public long Total => Interlocked.Read(ref total);
        long done, total;

        /// <summary>Where each chunk id is found locally: a file of the old build, or (path null) _update/chunks.</summary>
        readonly Dictionary<string, (string path, long offset, int size)> local = new Dictionary<string, (string, long, int)>();
        readonly Dictionary<string, int> chunkIndex = new Dictionary<string, int>();

        [Serializable]
        class State
        {
            public string version;
            public bool ready;
        }

        readonly Func<string, string> localPath;

        /// <summary>'installDir': the game's folder (null on Android). 'updateDir': the work folder (default
        /// installDir/_update). 'localPath': where a manifest path's current file is (default inside installDir).</summary>
        public PatchInstall(string installDir, PatchManifest manifest, string updateDir = null, Func<string, string> localPath = null)
        {
            InstallDir = installDir == null ? null : Path.GetFullPath(installDir);
            UpdateDir = Path.GetFullPath(updateDir ?? Path.Combine(InstallDir, FolderName));
            ChunkDir = Path.Combine(UpdateDir, "chunks");
            FilesDir = Path.Combine(UpdateDir, "files");
            Manifest = manifest;
            this.localPath = localPath ?? (p => Native(InstallDir, p));
            for (int i = 0; i < manifest.chunks.Length; i++) chunkIndex[manifest.chunks[i].id] = i;
        }

        static string Native(string root, string path) => Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar));

        /// <summary>Where the current file for manifest path 'path' is (null: none).</summary>
        string Local(string path) => localPath(path);

        /// <summary>Where the staged new file for manifest path 'path' is, after Assemble.</summary>
        public string StagedPath(string path) => Native(FilesDir, path);

        static FileStream OpenShared(string path) =>
            new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16);

        // ---- 1. scan --------------------------------------------------------------------------------------------------

        public void Scan(CancellationToken cancel)
        {
            Changed.Clear(); Needed.Clear(); Removing.Clear(); Ranges.Clear(); local.Clear();
            DownloadBytes = ChangedBytes = 0;
            Directory.CreateDirectory(UpdateDir);
            // An earlier try for another version: its staged files are of no use; its chunks may be.
            if (ReadState()?.version != Manifest.version && Directory.Exists(FilesDir)) Directory.Delete(FilesDir, true);
            WriteState(false);

            var present = new List<PatchFile>();
            long sum = 0;
            foreach (var f in Manifest.files)
            {
                var p = Local(f.path);
                if (p != null && File.Exists(p)) { present.Add(f); sum += new FileInfo(p).Length; }
            }
            Interlocked.Exchange(ref total, sum);
            Interlocked.Exchange(ref done, 0);

            var localLists = new Dictionary<string, List<string>>();
            using (var sha = SHA256.Create())
            {
                foreach (var f in present)
                {
                    var ids = new List<string>();
                    long before = Done;
                    using (var s = OpenShared(Local(f.path)))
                        PatchChunker.Split(s, (offset, buffer, start, length) =>
                        {
                            string id = PatchChunker.Id(sha, buffer, start, length);
                            ids.Add(id);
                            if (!local.ContainsKey(id)) local[id] = (f.path, offset, length);
                        }, read => Interlocked.Exchange(ref done, before + read), cancel, f.Bits);
                    localLists[f.path] = ids;
                }
            }

            if (Directory.Exists(ChunkDir))
                foreach (var file in Directory.GetFiles(ChunkDir))
                {
                    string id = Path.GetFileName(file);
                    if (chunkIndex.TryGetValue(id, out int i) && new FileInfo(file).Length == Manifest.chunks[i].size)
                    {
                        if (!local.ContainsKey(id)) local[id] = (null, 0, Manifest.chunks[i].size);
                    }
                    else TryDelete(file);   // another version's, or cut short
                }

            var needed = new HashSet<int>();
            foreach (var f in Manifest.files)
            {
                if (localLists.TryGetValue(f.path, out var have) && SameChunks(f, have)) continue;
                Changed.Add(f);
                ChangedBytes += f.size;
                foreach (int i in f.chunks)
                    if (!local.ContainsKey(Manifest.chunks[i].id) && needed.Add(i)) Needed.Add(i);
            }
            if (Manifest.removed != null)
                foreach (var r in Manifest.removed)
                {
                    string p = Local(r);
                    if (p != null && File.Exists(p)) Removing.Add(r);
                }
            Ranges.AddRange(PlanRanges(Manifest, Needed));
            foreach (var r in Ranges) DownloadBytes += r.Length;
        }

        /// <summary>The needed chunks as downloads: by pack, in pack order, neighbours joined (MaxGap, MaxRange).</summary>
        public static List<PatchRange> PlanRanges(PatchManifest manifest, IEnumerable<int> needed, int maxGap = MaxGap, int maxRange = MaxRange)
        {
            var sorted = new List<int>(needed);
            sorted.Sort((a, b) =>
            {
                var ca = manifest.chunks[a];
                var cb = manifest.chunks[b];
                return ca.pack != cb.pack ? ca.pack.CompareTo(cb.pack) : ca.offset.CompareTo(cb.offset);
            });
            var ranges = new List<PatchRange>();
            PatchRange current = null;
            foreach (int i in sorted)
            {
                var c = manifest.chunks[i];
                long end = c.offset + c.DownloadSize;
                if (current != null && current.pack == c.pack && c.offset >= current.start && c.offset - current.end <= maxGap
                    && end - current.start <= maxRange)
                {
                    current.end = Math.Max(current.end, end);
                    current.chunks.Add(i);
                    continue;
                }
                current = new PatchRange { pack = c.pack, start = c.offset, end = end };
                current.chunks.Add(i);
                ranges.Add(current);
            }
            return ranges;
        }

        bool SameChunks(PatchFile f, List<string> have)
        {
            if (have.Count != f.chunks.Length) return false;
            for (int i = 0; i < have.Count; i++)
                if (have[i] != Manifest.chunks[f.chunks[i]].id) return false;
            return true;
        }

        // ---- 2. downloaded chunks -------------------------------------------------------------------------------------

        /// <summary>The download of chunk 'index' (packed as on the server): unpacked, checked and kept. False when it
        /// doesn't match its id (download again).</summary>
        public bool Accept(int index, byte[] data) => Accept(index, data, 0, data?.Length ?? 0);

        /// <summary>Accept for the chunk at data[start..start+length] (a range download holds several).</summary>
        public bool Accept(int index, byte[] data, int start, int length)
        {
            var c = Manifest.chunks[index];
            if (data == null || start < 0 || length != c.DownloadSize || start + length > data.Length) return false;
            byte[] raw;
            try { raw = c.packed > 0 ? Inflate(data, start, length, c.size) : data; }
            catch (InvalidDataException) { return false; }
            int rawStart = c.packed > 0 ? 0 : start;
            if (raw == null) return false;
            using (var sha = SHA256.Create())
                if (PatchChunker.Id(sha, raw, rawStart, c.size) != c.id) return false;
            Directory.CreateDirectory(ChunkDir);
            string path = Path.Combine(ChunkDir, c.id);
            using (var f = File.Create(path + ".part")) f.Write(raw, rawStart, c.size);
            if (File.Exists(path)) File.Delete(path);
            File.Move(path + ".part", path);
            lock (local) local[c.id] = (null, 0, c.size);
            return true;
        }

        public static byte[] Inflate(byte[] packed, int size) => Inflate(packed, 0, packed.Length, size);

        public static byte[] Inflate(byte[] packed, int start, int length, int size)
        {
            var raw = new byte[size];
            using (var d = new DeflateStream(new MemoryStream(packed, start, length), CompressionMode.Decompress))
            {
                int n = 0;
                while (n < size)
                {
                    int read = d.Read(raw, n, size - n);
                    if (read <= 0) return null;
                    n += read;
                }
                if (d.ReadByte() >= 0) return null;   // longer than it says
            }
            return raw;
        }

        public static byte[] Deflate(byte[] raw, int start, int length)
        {
            var ms = new MemoryStream();
            using (var d = new DeflateStream(ms, CompressionLevel.Optimal, true)) d.Write(raw, start, length);
            return ms.ToArray();
        }

        // ---- 3. assemble ----------------------------------------------------------------------------------------------

        /// <summary>Writes every changed file into _update/files. Throws InvalidDataException when a chunk doesn't match
        /// (a local file changed since Scan, a damaged download: that chunk is dropped, scan again).</summary>
        public void Assemble(CancellationToken cancel)
        {
            Interlocked.Exchange(ref total, ChangedBytes);
            Interlocked.Exchange(ref done, 0);
            var open = new Dictionary<string, FileStream>();
            var buffer = new byte[PatchChunker.MaxSize];
            try
            {
                using (var sha = SHA256.Create())
                    foreach (var f in Changed)
                    {
                        string target = Native(FilesDir, f.path);
                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                        using (var output = new FileStream(target + ".part", FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
                            foreach (int i in f.chunks)
                            {
                                cancel.ThrowIfCancellationRequested();
                                var c = Manifest.chunks[i];
                                (string path, long offset, int size) where;
                                lock (local)
                                    if (!local.TryGetValue(c.id, out where)) throw new InvalidDataException("chunk missing " + c.id);
                                string chunkFile = where.path == null ? Path.Combine(ChunkDir, c.id) : null;
                                if (chunkFile != null)
                                {
                                    using (var s = OpenShared(chunkFile)) ReadFully(s, buffer, c.size);
                                }
                                else
                                {
                                    if (!open.TryGetValue(where.path, out var s)) open[where.path] = s = OpenShared(Local(where.path));
                                    s.Position = where.offset;
                                    ReadFully(s, buffer, c.size);
                                }
                                if (PatchChunker.Id(sha, buffer, 0, c.size) != c.id)
                                {
                                    lock (local) local.Remove(c.id);
                                    if (chunkFile != null) TryDelete(chunkFile);
                                    throw new InvalidDataException("chunk changed " + c.id);
                                }
                                output.Write(buffer, 0, c.size);
                                Interlocked.Add(ref done, c.size);
                            }
                        if (File.Exists(target)) File.Delete(target);
                        File.Move(target + ".part", target);
                    }
            }
            finally
            {
                foreach (var s in open.Values) s.Dispose();
            }
            if (Directory.Exists(ChunkDir)) Directory.Delete(ChunkDir, true);   // in the files now; frees the space
            WriteState(true);
        }

        static void ReadFully(Stream s, byte[] buffer, int count)
        {
            int n = 0;
            while (n < count)
            {
                int read = s.Read(buffer, n, count - n);
                if (read <= 0) throw new InvalidDataException("file shorter than expected");
                n += read;
            }
        }

        // ---- 4. apply -------------------------------------------------------------------------------------------------

        /// <summary>Writes the apply script into _update and returns its path. It waits for process 'pid' to end, moves
        /// the staged files in, deletes the dropped ones, removes _update and, run with the argument "restart", starts
        /// 'exeName' (relative to the game's folder) again.</summary>
        public string WriteApplyScript(bool windows, int pid, string exeName)
        {
            var moves = new List<string>();
            foreach (var f in Changed) moves.Add(f.path);
            string script = windows ? WindowsScript(pid, exeName, moves, Removing) : LinuxScript(pid, exeName, moves, Removing, ExecPaths());
            string path = Path.Combine(UpdateDir, windows ? "apply.cmd" : "apply.sh");
            File.WriteAllText(path, script, new UTF8Encoding(false));
            return path;
        }

        List<string> ExecPaths()
        {
            var list = new List<string>();
            foreach (var f in Manifest.files) if (f.exec) list.Add(f.path);
            return list;
        }

        public static string WindowsScript(int pid, string exeName, List<string> moves, List<string> removing)
        {
            var sb = new StringBuilder();
            sb.Append("@echo off\r\n");
            sb.Append("rem Galaxy on Fire 2 Remake update: waits for the game to quit, moves the new files in.\r\n");
            sb.Append("cd /d \"%~dp0\"\r\n");
            sb.Append(":wait\r\n");
            sb.Append($"tasklist /FI \"PID eq {pid}\" /NH 2>nul | find \"{pid}\" >nul && (ping -n 2 127.0.0.1 >nul & goto wait)\r\n");
            sb.Append("set failed=0\r\n");
            foreach (var m in moves)
            {
                string win = m.Replace('/', '\\');
                string dir = Path.GetDirectoryName(win);
                if (!string.IsNullOrEmpty(dir)) sb.Append($"if not exist \"..\\{dir}\" mkdir \"..\\{dir}\"\r\n");
                sb.Append($"call :move \"files\\{win}\" \"..\\{win}\"\r\n");
            }
            foreach (var r in removing) sb.Append($"del /f /q \"..\\{r.Replace('/', '\\')}\" >nul 2>&1\r\n");
            sb.Append("cd /d \"%~dp0..\"\r\n");
            sb.Append($"if \"%~1\"==\"restart\" start \"\" \"{exeName.Replace('/', '\\')}\"\r\n");
            // A file that wouldn't move keeps the folder (and the game tries again on its next update).
            sb.Append($"if %failed%==0 (goto) 2>nul & rmdir /s /q \"{FolderName}\"\r\n");
            sb.Append("exit /b 0\r\n");
            sb.Append(":move\r\n");
            sb.Append("set tries=0\r\n");
            sb.Append(":again\r\n");
            sb.Append("move /y %1 %2 >nul 2>&1 && exit /b 0\r\n");
            sb.Append("set /a tries+=1\r\n");
            sb.Append("if %tries% geq 30 (set failed=1 & exit /b 1)\r\n");
            sb.Append("ping -n 2 127.0.0.1 >nul\r\n");
            sb.Append("goto again\r\n");
            return sb.ToString();
        }

        public static string LinuxScript(int pid, string exeName, List<string> moves, List<string> removing, List<string> exec)
        {
            var sb = new StringBuilder();
            sb.Append("#!/bin/sh\n");
            sb.Append("# Galaxy on Fire 2 Remake update: waits for the game to quit, moves the new files in.\n");
            sb.Append("cd \"$(dirname \"$0\")\" || exit 1\n");
            sb.Append($"while kill -0 {pid} 2>/dev/null; do sleep 0.5; done\n");
            sb.Append("failed=0\n");
            sb.Append("move() { mkdir -p \"$(dirname \"$2\")\"; i=0; until mv -f \"$1\" \"$2\"; do i=$((i+1)); " +
                      "[ $i -ge 30 ] && { failed=1; return 1; }; sleep 1; done; }\n");
            foreach (var m in moves) sb.Append($"move \"files/{m}\" \"../{m}\"\n");
            foreach (var e in exec) sb.Append($"[ -f \"../{e}\" ] && chmod +x \"../{e}\"\n");
            foreach (var r in removing) sb.Append($"rm -f \"../{r}\"\n");
            sb.Append("cd .. || exit 1\n");
            sb.Append($"[ $failed = 0 ] && rm -rf \"{FolderName}\"\n");
            sb.Append($"[ \"$1\" = restart ] && nohup \"./{exeName}\" >/dev/null 2>&1 &\n");
            sb.Append("exit 0\n");
            return sb.ToString();
        }

        // ---- state ----------------------------------------------------------------------------------------------------

        State ReadState()
        {
            try
            {
                string p = Path.Combine(UpdateDir, StateFile);
                return File.Exists(p) ? UnityEngine.JsonUtility.FromJson<State>(File.ReadAllText(p)) : null;
            }
            catch (Exception) { return null; }
        }

        void WriteState(bool ready) =>
            File.WriteAllText(Path.Combine(UpdateDir, StateFile), UnityEngine.JsonUtility.ToJson(new State { version = Manifest.version, ready = ready }));

        /// <summary>On start: an _update folder left by an update to this very version (applied, but the script couldn't
        /// remove its own folder) is deleted. Returns true when it was.</summary>
        public static bool CleanUpApplied(string installDir, string runningVersion) =>
            CleanUpAppliedIn(Path.Combine(installDir, FolderName), runningVersion);

        /// <summary>CleanUpApplied for the work folder 'dir' itself (Android's is in the app's data).</summary>
        public static bool CleanUpAppliedIn(string dir, string runningVersion)
        {
            string state = Path.Combine(dir, StateFile);
            try
            {
                if (!File.Exists(state)) return false;
                var s = UnityEngine.JsonUtility.FromJson<State>(File.ReadAllText(state));
                if (s == null || s.version != runningVersion) return false;
                Directory.Delete(dir, true);
                return true;
            }
            catch (Exception) { return false; }
        }

        static void TryDelete(string path)
        {
            try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
