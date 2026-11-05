// GamePatcher.cs
// Remake-only: the in-game updater (CLAUDE.md "Update patcher" in docs/notes/platforms.md). The main menu's "Update
// available" button updates the game in place where the newest GitHub release (UpdateCheck) carries this platform's way to:
// - Windows / Linux (Kind.Files): GoF2Remake-<platform>-<tag>.patch.json (PatchPublish). Prepare: the manifest from GitHub
//   (so the pack server can only serve bytes it vouches for), then PatchInstall.Scan on a thread (which files differ,
//   which chunks no local file has). Download: those chunks from the pack files on the GitHub releases as range
//   requests, four at a time, each chunk checked; then PatchInstall.Assemble on a thread writes the changed files into
//   _update/files. Apply: the apply script waits for the game to quit, moves the files in and (Restart now) starts the
//   game again; Later runs it as the game quits. Only when the game's folder is writable.
// - Android (Kind.Apk): GoF2Remake-Android-<tag>.patch.json, the same steps on one file: the installed APK
//   (Application.dataPath) is read for the chunks it shares, the new APK is rebuilt byte for byte in the app's data
//   (persistentDataPath/_update) and handed to Android's installer (AndroidUpdateInstaller), which asks the player.
// - UWP (Kind.AppInstaller): GoF2Remake-UWP.appinstaller; Windows' App Installer does the update (it downloads only
//   the package's changed blocks itself) once the game opens that file (UwpAppInstaller).
// An _update folder left by an update to the running version is deleted at start.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace GoF2Remake.UI
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class GamePatcher
    {
        public enum Phase { Idle, Fetching, Scanning, Ready, Downloading, Assembling, Staged, Failed }
        public enum Kind { None, Files, Apk, AppInstaller }

        const int Parallel = 4, Tries = 3, TimeoutSeconds = 120;

        public static Phase State { get; private set; }
        /// <summary>Why it failed (technical, English), when State is Failed.</summary>
        public static string Error { get; private set; }
        /// <summary>Failed for lack of disk space (Error is then the bytes needed).</summary>
        public static bool NoSpace { get; private set; }
        public static PatchInstall Install { get; private set; }
        /// <summary>0..1 of the current phase.</summary>
        public static float Progress { get; private set; }
        public static long Downloaded { get; private set; }
        /// <summary>While downloading: bytes so far (finished requests and those under way).</summary>
        public static long Transferred { get; private set; }
        /// <summary>While downloading: the speed over the last few seconds, bytes a second (0 until known).</summary>
        public static float BytesPerSecond { get; private set; }
        /// <summary>While downloading: seconds left at that speed (-1 until known).</summary>
        public static float SecondsLeft { get; private set; }

        /// <summary>The speed is averaged over this long, so it doesn't jump with each finished request.</summary>
        const float SpeedWindow = 4f;

        public static bool Working => State == Phase.Fetching || State == Phase.Scanning || State == Phase.Downloading || State == Phase.Assembling;

        /// <summary>Editor tests: a build folder to treat as the game's (the Editor's own platform).</summary>
        public static string TestInstallDir;
        /// <summary>Editor tests: the manifest to use instead of the newest release's.</summary>
        public static string TestManifestUrl;
        /// <summary>Editor tests: an APK to treat as the installed one (Android), with TestInstallDir as the app's data.</summary>
        public static string TestApkPath;

        static CancellationTokenSource cancel;
        static bool applyOnQuit;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            State = Phase.Idle;
            Error = null;
            NoSpace = false;
            Install = null;
            Progress = 0f;
            Downloaded = Transferred = 0;
            BytesPerSecond = 0f;
            SecondsLeft = -1f;
            cancel = null;
            applyOnQuit = false;
            TestInstallDir = TestManifestUrl = TestApkPath = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void CleanUpAfterUpdate()
        {
            if (Application.isEditor || (KindHere != Kind.Files && KindHere != Kind.Apk)) return;
            string dir = UpdateDir, version = Application.version;
            Task.Run(() =>
            {
                if (PatchInstall.CleanUpAppliedIn(dir, version)) Debug.Log("GamePatcher: updated to " + version + ", update folder removed");
            });
        }

        /// <summary>This platform's name in the release files ("Windows", "Linux", "Android", "UWP"), or null.</summary>
        public static string PlatformName
        {
            get
            {
                var p = Application.platform;
                if (Application.isEditor && TestApkPath != null) return "Android";
                if (Application.isEditor && TestInstallDir != null)
                    p = p == RuntimePlatform.LinuxEditor ? RuntimePlatform.LinuxPlayer : RuntimePlatform.WindowsPlayer;
                switch (p)
                {
                    case RuntimePlatform.WindowsPlayer: return "Windows";
                    case RuntimePlatform.LinuxPlayer: return "Linux";
                    case RuntimePlatform.Android: return "Android";
                    case RuntimePlatform.WSAPlayerX64:
                    case RuntimePlatform.WSAPlayerX86:
                    case RuntimePlatform.WSAPlayerARM: return "UWP";
                    default: return null;
                }
            }
        }

        /// <summary>How this platform updates.</summary>
        public static Kind KindHere => PlatformName switch
        {
            "Windows" => Kind.Files,
            "Linux" => Kind.Files,
            "Android" => Kind.Apk,
            "UWP" => Kind.AppInstaller,
            _ => Kind.None,
        };

        static bool Windows => PlatformName == "Windows";

        /// <summary>The game's folder (the one holding the player and its _Data folder); null on Android / UWP.</summary>
        public static string InstallDir =>
            Application.isEditor ? TestInstallDir : KindHere == Kind.Files ? Path.GetDirectoryName(Application.dataPath) : null;

        /// <summary>The update's work folder: in the game's folder (Windows / Linux), else in the app's data.</summary>
        public static string UpdateDir =>
            Application.isEditor && TestApkPath != null ? Path.Combine(TestInstallDir, PatchInstall.FolderName)
            : InstallDir != null
            ? Path.Combine(InstallDir, PatchInstall.FolderName)
            : Path.Combine(Application.persistentDataPath, PatchInstall.FolderName);

        /// <summary>The player's file name in the game's folder.</summary>
        public static string ExeName
        {
            get
            {
                if (Application.isEditor) return PatchPublish.Inspect(TestInstallDir)?.Exe;
                string data = Path.GetFileName(Application.dataPath);
                string name = data.EndsWith("_Data") ? data.Substring(0, data.Length - 5) : data;
                return name + (Windows ? ".exe" : ".x86_64");
            }
        }

        static string Tag => UpdateCheck.LatestTag;

        /// <summary>The newest release's manifest for this platform (Windows / Linux / Android), or null.</summary>
        public static string ManifestUrl =>
            Application.isEditor && TestManifestUrl != null ? TestManifestUrl
            : (KindHere != Kind.Files && KindHere != Kind.Apk) || string.IsNullOrEmpty(Tag) ? null
                : UpdateCheck.ReleaseFile(PatchPublish.ManifestAssetName(PlatformName, Tag))?.url;

        /// <summary>The newest release's App Installer file (UWP), or null.</summary>
        public static string AppInstallerUrl =>
            KindHere != Kind.AppInstaller || string.IsNullOrEmpty(Tag) ? null : UpdateCheck.ReleaseFile(PatchPublish.AppInstallerName)?.url;

        /// <summary>The full download it saves (the release's zip / APK), 0 when unknown.</summary>
        public static long FullSize
        {
            get
            {
                if (PlatformName == null || string.IsNullOrEmpty(Tag)) return 0;
                string ext = KindHere == Kind.Apk ? ".apk" : ".zip";
                return UpdateCheck.ReleaseFile($"GoF2Remake-{PlatformName}-{Tag}{ext}")?.size ?? 0;
            }
        }

        /// <summary>The button can update in place (else it opens the release page).</summary>
        public static bool CanPatch
        {
            get
            {
                if (State == Phase.Staged) return true;
                switch (KindHere)
                {
                    case Kind.Files: return ManifestUrl != null && Writable(UpdateDir);
                    case Kind.Apk: return ManifestUrl != null && AndroidUpdateInstaller.Available && Writable(UpdateDir);
                    case Kind.AppInstaller: return AppInstallerUrl != null && UwpAppInstaller.Available;
                    default: return Application.isEditor && TestManifestUrl != null;
                }
            }
        }

        static bool Writable(string folder)
        {
            try
            {
                string probe = Path.Combine(folder, "write-test");
                bool existed = Directory.Exists(folder);
                Directory.CreateDirectory(folder);
                File.WriteAllText(probe, "x");
                File.Delete(probe);
                if (!existed) Directory.Delete(folder);
                return true;
            }
            catch (Exception) { return false; }
        }

        public static void Cancel()
        {
            cancel?.Cancel();
        }

        static void Fail(string error)
        {
            Error = error;
            State = Phase.Failed;
            Debug.LogWarning("GamePatcher: " + error);
        }

        static bool Cancelled(Phase back)
        {
            if (cancel == null || !cancel.IsCancellationRequested) return false;
            State = back;
            Debug.Log("GamePatcher: cancelled");
            return true;
        }

        /// <summary>The PatchInstall for this platform: the game's folder, or (Android) the installed APK.</summary>
        static PatchInstall NewInstall(PatchManifest manifest)
        {
            if (KindHere == Kind.Apk)
            {
                string apk = Application.isEditor ? TestApkPath : Application.dataPath;   // Android: the installed APK itself
                return new PatchInstall(null, manifest, UpdateDir, p => p == PatchManifest.ApkName ? apk : null);
            }
            return new PatchInstall(InstallDir, manifest);
        }

        /// <summary>Fetches the manifest and scans what the game has; ends Ready (Install says what's to download) or Failed.</summary>
        public static IEnumerator Prepare()
        {
            if (Working) yield break;
            cancel = new CancellationTokenSource();
            NoSpace = false;
            Progress = 0f;
            State = Phase.Fetching;
            string url = ManifestUrl;
            PatchManifest manifest = null;
            using (var request = UnityWebRequest.Get(url))
            {
                request.timeout = TimeoutSeconds;
                yield return request.SendWebRequest();
                if (Cancelled(Phase.Idle)) yield break;
                if (request.result != UnityWebRequest.Result.Success) { Fail($"manifest: {request.responseCode} {request.error}"); yield break; }
                try { manifest = JsonUtility.FromJson<PatchManifest>(request.downloadHandler.text); }
                catch (Exception e) { Fail("manifest: " + e.Message); yield break; }
            }
            string problem = manifest == null ? "empty" : manifest.Problem(PlatformName);
            if (problem != null) { Fail("manifest: " + problem); yield break; }

            State = Phase.Scanning;
            var install = NewInstall(manifest);
            var token = cancel.Token;
            var scan = Task.Run(() => install.Scan(token));
            while (!scan.IsCompleted)
            {
                Progress = install.Total > 0 ? (float)install.Done / install.Total : 0f;
                yield return null;
            }
            if (Cancelled(Phase.Idle)) yield break;
            if (scan.IsFaulted) { Fail("scan: " + scan.Exception?.GetBaseException().Message); yield break; }
            Install = install;
            long free = FreeSpace(install.UpdateDir);
            long needed = install.ChangedBytes + install.DownloadBytes + 64L * 1024 * 1024;
            if (free >= 0 && free < needed) { NoSpace = true; Fail(needed.ToString()); yield break; }
            State = Phase.Ready;
            Debug.Log($"GamePatcher: {manifest.version}: {install.Changed.Count} files change, {install.Needed.Count} chunks in " +
                      $"{install.Ranges.Count} downloads / {install.DownloadBytes / 1048576.0:0.0} MB, {install.Removing.Count} removed");
        }

        static long FreeSpace(string dir)
        {
            try { return new DriveInfo(Path.GetPathRoot(Path.GetFullPath(dir))).AvailableFreeSpace; }
            catch (Exception) { return -1; }
        }

        /// <summary>Downloads the chunks and assembles the files; ends Staged or Failed (or Ready, cancelled).</summary>
        public static IEnumerator Download()
        {
            var install = Install;
            if (install == null || State != Phase.Ready) yield break;
            cancel = new CancellationTokenSource();
            State = Phase.Downloading;
            Downloaded = Transferred = 0;
            BytesPerSecond = 0f;
            SecondsLeft = -1f;
            Progress = 0f;
            var samples = new Queue<(float time, long bytes)>();
            var queue = new Queue<PatchRange>(install.Ranges);
            var tries = new Dictionary<PatchRange, int>();
            var running = new List<(UnityWebRequest request, PatchRange range)>();
            string failure = null;
            long total = Math.Max(1, install.DownloadBytes);
            try
            {
                while ((queue.Count > 0 || running.Count > 0) && failure == null)
                {
                    if (cancel.IsCancellationRequested) break;
                    while (running.Count < Parallel && queue.Count > 0)
                    {
                        var range = queue.Dequeue();
                        var request = UnityWebRequest.Get(install.Manifest.PackUrl(range.pack));
                        request.SetRequestHeader("Range", $"bytes={range.start}-{range.end - 1}");
                        request.timeout = TimeoutSeconds;
                        request.SendWebRequest();
                        running.Add((request, range));
                    }
                    long inFlight = 0;
                    for (int r = running.Count - 1; r >= 0; r--)
                    {
                        var (request, range) = running[r];
                        if (!request.isDone) { inFlight += (long)request.downloadedBytes; continue; }
                        running.RemoveAt(r);
                        string problem = AcceptRange(install, range, request);
                        if (problem == null) Downloaded += range.Length;
                        else
                        {
                            tries.TryGetValue(range, out int n);
                            if (n + 1 >= Tries) failure = $"{install.Manifest.packs[range.pack]} {range.start}: {problem}";
                            else { tries[range] = n + 1; queue.Enqueue(range); }
                        }
                        request.Dispose();
                    }
                    Transferred = Math.Min(Downloaded + inFlight, total);
                    Progress = Mathf.Clamp01((float)Transferred / total);
                    UpdateSpeed(samples, total);
                    yield return null;
                }
            }
            finally
            {
                foreach (var (request, _) in running) { request.Abort(); request.Dispose(); }
            }
            if (Cancelled(Phase.Ready)) yield break;
            if (failure != null) { Fail(failure); yield break; }

            State = Phase.Assembling;
            Progress = 0f;
            var token = cancel.Token;
            var assemble = Task.Run(() => install.Assemble(token));
            while (!assemble.IsCompleted)
            {
                Progress = install.Total > 0 ? (float)install.Done / install.Total : 0f;
                yield return null;
            }
            if (Cancelled(Phase.Ready)) yield break;
            if (assemble.IsFaulted)
            {
                // A chunk that didn't check out was dropped: the next try scans and fetches it again.
                Install = null;
                Fail("assemble: " + assemble.Exception?.GetBaseException().Message);
                yield break;
            }
            State = Phase.Staged;
            Progress = 1f;
            Debug.Log("GamePatcher: " + install.Manifest.version + " staged");
        }

        /// <summary>BytesPerSecond / SecondsLeft from the bytes over the last SpeedWindow seconds (real time: a paused or
        /// slowed game doesn't change it).</summary>
        static void UpdateSpeed(Queue<(float time, long bytes)> samples, long total)
        {
            float now = Time.realtimeSinceStartup;
            samples.Enqueue((now, Transferred));
            while (samples.Count > 2 && now - samples.Peek().time > SpeedWindow) samples.Dequeue();
            var (first, firstBytes) = samples.Peek();
            float span = now - first;
            if (span < 0.5f) return;   // too soon to say
            BytesPerSecond = Math.Max(0f, (Transferred - firstBytes) / span);
            SecondsLeft = BytesPerSecond > 1f ? (total - Transferred) / BytesPerSecond : -1f;
        }

        /// <summary>A finished range download: each of its chunks handed to the install. Null, or what was wrong.</summary>
        static string AcceptRange(PatchInstall install, PatchRange range, UnityWebRequest request)
        {
            if (request.result != UnityWebRequest.Result.Success) return $"{request.responseCode} {request.error}";
            var data = request.downloadHandler.data;
            // 206 Partial Content; a server that ignores Range sends the whole pack (200): fine only when that is the range.
            if (request.responseCode != 206 && !(range.start == 0 && data != null && data.Length == range.Length))
                return $"{request.responseCode}: the server ignored the range";
            if (data == null || data.Length != range.Length) return $"{data?.Length ?? 0} bytes instead of {range.Length}";
            foreach (int i in range.chunks)
            {
                var c = install.Manifest.chunks[i];
                if (!install.Accept(i, data, (int)(c.offset - range.start), c.DownloadSize)) return "bad data for chunk " + c.id;
            }
            return null;
        }

        /// <summary>Applies the staged update. Windows / Linux: starts the apply script ('restart': it starts the game again
        /// afterwards), the caller then quits. Android: hands the rebuilt APK to the installer. False when it couldn't (Error
        /// says why).</summary>
        public static bool StartApply(bool restart)
        {
            if (State != Phase.Staged || Install == null) return false;
            if (KindHere == Kind.Apk)
            {
                string error = AndroidUpdateInstaller.Install(Install.StagedPath(PatchManifest.ApkName));
                if (error == null) { Debug.Log($"GamePatcher: {Install.Manifest.version} handed to the installer"); return true; }
                Fail("install: " + error);
                return false;
            }
            try
            {
                int pid = System.Diagnostics.Process.GetCurrentProcess().Id;
                string script = Install.WriteApplyScript(Windows, pid, ExeName);
                string arg = restart ? "restart" : "stay";
                System.Diagnostics.ProcessStartInfo info;
                if (Windows)
                    info = new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c \"\"{script}\" {arg}\"");
                else if (File.Exists("/usr/bin/setsid"))
                    info = new System.Diagnostics.ProcessStartInfo("/usr/bin/setsid", $"/bin/sh \"{script}\" {arg}");
                else
                    info = new System.Diagnostics.ProcessStartInfo("/bin/sh", $"\"{script}\" {arg}");
                info.UseShellExecute = false;
                info.CreateNoWindow = true;
                info.WorkingDirectory = Install.UpdateDir;
                System.Diagnostics.Process.Start(info);
                Debug.Log($"GamePatcher: applying {Install.Manifest.version} after quitting ({arg})");
                return true;
            }
            catch (Exception e)
            {
                Fail("apply: " + e.Message);
                return false;
            }
        }

        /// <summary>The staged update is applied when the game quits (Later; Windows / Linux).</summary>
        public static void ApplyOnQuit()
        {
            if (applyOnQuit || State != Phase.Staged || KindHere != Kind.Files) return;
            applyOnQuit = true;
            Application.quitting += () =>
            {
                if (State == Phase.Staged) StartApply(false);
            };
        }

        /// <summary>UWP: fetches the release's App Installer file and opens it; Windows' App Installer then asks the player
        /// and downloads the package's changed blocks. Ends Idle (opened) or Failed.</summary>
        public static IEnumerator OpenAppInstaller()
        {
            if (Working) yield break;
            cancel = new CancellationTokenSource();
            Progress = 0f;
            State = Phase.Fetching;
            string path = Path.Combine(Application.temporaryCachePath, "GoF2Remake.appinstaller");
            using (var request = UnityWebRequest.Get(AppInstallerUrl))
            {
                request.timeout = TimeoutSeconds;
                yield return request.SendWebRequest();
                if (Cancelled(Phase.Idle)) yield break;
                if (request.result != UnityWebRequest.Result.Success) { Fail($"appinstaller: {request.responseCode} {request.error}"); yield break; }
                try { File.WriteAllBytes(path, request.downloadHandler.data); }
                catch (Exception e) { Fail("appinstaller: " + e.Message); yield break; }
            }
            var launch = UwpAppInstaller.Open(path, AppInstallerUrl);
            while (!launch.IsCompleted) yield return null;
            if (launch.IsFaulted || !launch.Result)
            {
                Fail("appinstaller: " + (launch.Exception?.GetBaseException().Message ?? "Windows didn't open it"));
                yield break;
            }
            State = Phase.Idle;
            Debug.Log("GamePatcher: App Installer opened for " + Tag);
        }
    }
}
