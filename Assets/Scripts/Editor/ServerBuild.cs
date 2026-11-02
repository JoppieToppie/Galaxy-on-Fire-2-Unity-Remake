// ServerBuild.cs
// GoF2 > Build > Linux / Windows Dedicated Server: a headless server build without the game's graphics and sound. Unity's
// Dedicated Server build (Standalone, subtarget Server; the Unity Hub module "Linux Dedicated Server Build Support" or
// "Windows Dedicated Server Build Support") with the dedicated server optimizations on: textures, audio and shader data are
// left out (the server draws and plays nothing), meshes keep only what isn't drawing data. Only the first scene goes in (the
// main menu, which the server switches off as it wakes: DedicatedServer.ShutOff); Resources/GoF2Data, the text tables, the
// web admin page, the events and the network prefabs stay, so the server runs exactly as the full build's -server mode. It
// starts as a server with no -server flag (UNITY_SERVER: DedicatedServer.Enabled), and the post-build step writes
// start-server.sh / Start Dedicated Server.bat next to it (DedicatedServerLaunchers). Same code = same multiplayer
// fingerprint, so it serves the normal builds.
// Output: Build/LinuxServer/GoF2Server.x86_64, Build/WindowsServer/GoF2Server.exe (a console program: the log and the
// commands in its own window, WinConsole). The active build target is switched to it for the build (BuildTargetGuard and
// URP want the build's own target active) and put back afterwards, the standalone subtarget too.
// GoF2 > Build > ... Dedicated Server (Release Zip): the same build zipped as Build/Release/GoF2Remake-LinuxServer-<tag>.zip
// / GoF2Remake-WindowsServer-<tag>.zip (top folder GoF2Server; Linux: the executables marked executable), built with the
// release's other platforms under the same version (GOF2_BUILD_VERSION, else the commit's), whose date is the tag. The Linux
// zip attached to the GitHub release is what the server image workflow (.github/workflows/server-image.yml) puts in the
// Docker image, so the image and the release are one build.
// Linux: the IL2CPP toolchain packages (com.unity.toolchain.win-x86_64-linux, com.unity.sdk.linux-x86_64) only join the
// build while the Editor's scripts are compiled for a Linux target (their UnityEditor.Sysroot base class sits behind
// UNITY_STANDALONE_LINUX_API), so the menu items switch the target first, let the Editor recompile and build after the
// domain reload (SessionState carries the job across it); built in the same call as the switch, IL2CPP found no toolchain
// ("No Linux sysroot found for x64"). The command line (docker/build-server.sh) starts the Editor on the Linux target and
// builds at once. Windows needs no such wait (the Editor's own platform). The server build leaves the audio out
// (ServerAudioStrip: 171 MB) and uses Minimal managed stripping with IL2CPP's size-optimised code generation (the server
// target had stripping off: whole assemblies went in).

using System;
using System.IO;
using System.IO.Compression;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace GoF2Remake.EditorTools
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class ServerBuild
    {
        const string FirstScene = "Assets/Scenes/MainMenu.unity";

        /// <summary>A server platform: its target, output and the name in the zip, menus and messages.</summary>
        sealed class Platform
        {
            public string Id, Label, Output, ZipName, HubModules;
            public BuildTarget Target;
            public bool Unix;
        }

        static readonly Platform Linux = new Platform
        {
            Id = "linux", Label = "Linux", Target = BuildTarget.StandaloneLinux64, Unix = true,
            Output = "Build/LinuxServer/GoF2Server.x86_64", ZipName = "LinuxServer",
            HubModules = "\"Linux Build Support (IL2CPP)\" and \"Linux Dedicated Server Build Support\"",
        };

        static readonly Platform Windows = new Platform
        {
            Id = "windows", Label = "Windows", Target = BuildTarget.StandaloneWindows64, Unix = false,
            Output = "Build/WindowsServer/GoF2Server.exe", ZipName = "WindowsServer",
            HubModules = "\"Windows Build Support (IL2CPP)\" and \"Windows Dedicated Server Build Support\"",
        };

        static Platform Find(string id) => id == Windows.Id ? Windows : id == Linux.Id ? Linux : null;

        // The job across the domain reload: "<platform>:build" / "<platform>:zip" while it waits for the platform's scripts,
        // the settings to put back, and the result (the build's or zip's path, empty when it failed).
        const string JobKey = "GoF2.ServerBuild.Job";
        const string ResultKey = "GoF2.ServerBuild.Result";
        const string PrevTargetKey = "GoF2.ServerBuild.PrevTarget";
        const string PrevSubtargetKey = "GoF2.ServerBuild.PrevSubtarget";
        const string PrevBackendKey = "GoF2.ServerBuild.PrevBackend";
        const string PrevOptimizationsKey = "GoF2.ServerBuild.PrevOptimizations";
        const string PrevStrippingKey = "GoF2.ServerBuild.PrevStripping";
        const string PrevCodeGenKey = "GoF2.ServerBuild.PrevCodeGen";
        const double CompileTimeoutSeconds = 600;

#if UNITY_STANDALONE_LINUX_API
        static readonly bool CompiledForLinux = true;
#else
        static readonly bool CompiledForLinux = false;
#endif

        static bool running;
        static double waitStarted;

        [MenuItem("GoF2/Build/Linux Dedicated Server", priority = 230)]
        static void BuildLinux() => Start(Linux, false);

        [MenuItem("GoF2/Build/Linux Dedicated Server (Release Zip)", priority = 231)]
        static void BuildLinuxReleaseZip() => Start(Linux, true);

        [MenuItem("GoF2/Build/Windows Dedicated Server", priority = 232)]
        static void BuildWindows() => Start(Windows, false);

        [MenuItem("GoF2/Build/Windows Dedicated Server (Release Zip)", priority = 233)]
        static void BuildWindowsReleaseZip() => Start(Windows, true);

        /// <summary>A menu item's job is switching, compiling or building.</summary>
        public static bool Busy => running || SessionState.GetString(JobKey, "") != "";

        /// <summary>The last menu job's result: the zip's (or the build's) full path, empty when it failed or none ran.</summary>
        public static string LastResult => SessionState.GetString(ResultKey, "");

        /// <summary>The command line (docker/build-server.sh, which starts the Editor with -buildTarget Linux64
        /// -standaloneBuildSubtarget Server): -executeMethod GoF2Remake.EditorTools.ServerBuild.Batch; builds the Linux server,
        /// exits 1 when the build fails.</summary>
        public static void Batch()
        {
            EditorUserBuildSettings.standaloneBuildSubtarget = StandaloneBuildSubtarget.Server;
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneLinux64)
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, BuildTarget.StandaloneLinux64);
            EditorApplication.Exit(BuildNow(Linux, false) != null ? 0 : 1);
        }

        /// <summary>The Linux release zip from a script (the release builds).</summary>
        public static void StartLinuxReleaseZip() => Start(Linux, true);

        /// <summary>The Windows release zip from a script (the release builds).</summary>
        public static void StartWindowsReleaseZip() => Start(Windows, true);

        /// <summary>The menu items: notes the settings to put back, switches to the platform's server target and builds once
        /// the scripts are compiled for it (at once when they already are).</summary>
        static void Start(Platform platform, bool zip)
        {
            if (Busy) { Debug.LogWarning("GoF2: a dedicated server build is already running."); return; }
            SessionState.SetInt(PrevTargetKey, (int)EditorUserBuildSettings.activeBuildTarget);
            SessionState.SetInt(PrevSubtargetKey, (int)EditorUserBuildSettings.standaloneBuildSubtarget);
            SessionState.SetInt(PrevBackendKey, (int)PlayerSettings.GetScriptingBackend(NamedBuildTarget.Server));
            SessionState.SetBool(PrevOptimizationsKey, ServerOptimizations);
            SessionState.SetInt(PrevStrippingKey, (int)PlayerSettings.GetManagedStrippingLevel(NamedBuildTarget.Server));
            SessionState.SetInt(PrevCodeGenKey, (int)PlayerSettings.GetIl2CppCodeGeneration(NamedBuildTarget.Server));
            SessionState.SetString(ResultKey, "");
            SessionState.SetString(JobKey, platform.Id + (zip ? ":zip" : ":build"));

            EditorUserBuildSettings.standaloneBuildSubtarget = StandaloneBuildSubtarget.Server;
            if (EditorUserBuildSettings.activeBuildTarget != platform.Target &&
                !EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, platform.Target))
            {
                SessionState.EraseString(JobKey);
                Restore();
                EditorUtility.DisplayDialog($"{platform.Label} Dedicated Server",
                    $"Couldn't switch to the {platform.Label} server target. Install the Unity Hub modules {platform.HubModules} " +
                    "for this Editor version, then try again.", "OK");
                return;
            }
            Debug.Log($"GoF2: {platform.Label} dedicated server: the scripts compile for {platform.Label}, then the build starts.");
            Wait();
        }

        [InitializeOnLoadMethod]
        static void ResumeAfterReload()
        {
            if (SessionState.GetString(JobKey, "") != "") Wait();
        }

        static void Wait()
        {
            waitStarted = EditorApplication.timeSinceStartup;
            EditorApplication.update -= WaitThenBuild;
            EditorApplication.update += WaitThenBuild;
        }

        /// <summary>Builds once the Editor has finished compiling and its scripts are the platform's (Linux: the domain before
        /// the switch's reload still runs this, compiled for the old target: it keeps waiting).</summary>
        static void WaitThenBuild()
        {
            if (running) return;
            string job = SessionState.GetString(JobKey, "");
            if (job == "") { EditorApplication.update -= WaitThenBuild; return; }
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            string[] parts = job.Split(':');
            var platform = Find(parts[0]);
            if (platform == null)
            {
                EditorApplication.update -= WaitThenBuild;
                SessionState.EraseString(JobKey);
                Restore();
                return;
            }
            bool ready = EditorUserBuildSettings.activeBuildTarget == platform.Target && (platform != Linux || CompiledForLinux);
            if (!ready)
            {
                if (EditorApplication.timeSinceStartup - waitStarted < CompileTimeoutSeconds) return;
                EditorApplication.update -= WaitThenBuild;
                SessionState.EraseString(JobKey);
                Debug.LogError(platform == Linux
                    ? "GoF2: the scripts never compiled for the Linux target (UNITY_STANDALONE_LINUX_API), so IL2CPP has no " +
                      "toolchain; the Linux dedicated server wasn't built."
                    : $"GoF2: the Editor never switched to the {platform.Label} target; the {platform.Label} dedicated server wasn't built.");
                Restore();
                return;
            }
            EditorApplication.update -= WaitThenBuild;
            running = true;
            string result = null;
            try { result = BuildNow(platform, parts.Length > 1 && parts[1] == "zip"); }
            catch (Exception e) { Debug.LogException(e); }
            finally
            {
                running = false;
                SessionState.SetString(ResultKey, result ?? "");
                SessionState.EraseString(JobKey);
                Restore();
            }
        }

        /// <summary>The build on the active target, and with 'zip' the release zip: its path, or null when it failed.</summary>
        static string BuildNow(Platform platform, bool zip)
        {
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Server, ScriptingImplementation.IL2CPP);   // like the players
            ServerOptimizations = true;
            // The game builds' stripping (Minimal: the engine and .NET libraries' unused code, never the game's), and IL2CPP's
            // shared generic code: a smaller GameAssembly for slightly slower generic code, which an idle server never feels.
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.Server, ManagedStrippingLevel.Minimal);
            PlayerSettings.SetIl2CppCodeGeneration(NamedBuildTarget.Server, Il2CppCodeGeneration.OptimizeSize);
            Directory.CreateDirectory(Path.GetDirectoryName(platform.Output));
            var options = new BuildPlayerOptions
            {
                scenes = new[] { FirstScene },
                locationPathName = platform.Output,
                target = platform.Target,
                subtarget = (int)StandaloneBuildSubtarget.Server,
                options = BuildOptions.None,
            };
            BuildSummary s;
            ServerAudioStrip.Strip();
            ServerAudioStrip.Active = true;
            try { s = BuildPipeline.BuildPlayer(options).summary; }
            finally
            {
                ServerAudioStrip.Active = false;
                ServerAudioStrip.Restore();
            }
            if (s.result != BuildResult.Succeeded)
            {
                Debug.LogError($"GoF2: the {platform.Label} dedicated server build {s.result} ({s.totalErrors} errors; see the Console).");
                return null;
            }
            Debug.Log($"GoF2: {platform.Label} dedicated server built: {Path.GetFullPath(platform.Output)} ({s.totalSize / (1024 * 1024)} MB, {s.totalTime:mm\\:ss}).");
            if (!zip) return Path.GetFullPath(platform.Output);

            string version = Environment.GetEnvironmentVariable(BuildVersionStamp.OverrideVariable);
            if (string.IsNullOrWhiteSpace(version)) version = BuildVersionStamp.CommitStamp();
            string tag = !string.IsNullOrWhiteSpace(version) && version.Trim().Length >= 10
                ? version.Trim().Substring(0, 10)
                : DateTime.UtcNow.ToString("yyyy.MM.dd", System.Globalization.CultureInfo.InvariantCulture);
            string zipPath = Path.GetFullPath($"Build/Release/GoF2Remake-{platform.ZipName}-{tag}.zip");
            Zip(Path.GetDirectoryName(Path.GetFullPath(platform.Output)), zipPath, "GoF2Server", platform.Unix);
            Debug.Log($"GoF2: {platform.Label} dedicated server zipped: {zipPath} ({new FileInfo(zipPath).Length / (1024 * 1024)} MB).");
            return zipPath;
        }

        /// <summary>The settings from before the job, and the build target it found (another script compile).</summary>
        static void Restore()
        {
            ServerOptimizations = SessionState.GetBool(PrevOptimizationsKey, ServerOptimizations);
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.Server,
                (ManagedStrippingLevel)SessionState.GetInt(PrevStrippingKey, (int)PlayerSettings.GetManagedStrippingLevel(NamedBuildTarget.Server)));
            PlayerSettings.SetIl2CppCodeGeneration(NamedBuildTarget.Server,
                (Il2CppCodeGeneration)SessionState.GetInt(PrevCodeGenKey, (int)PlayerSettings.GetIl2CppCodeGeneration(NamedBuildTarget.Server)));
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Server,
                (ScriptingImplementation)SessionState.GetInt(PrevBackendKey, (int)PlayerSettings.GetScriptingBackend(NamedBuildTarget.Server)));
            EditorUserBuildSettings.standaloneBuildSubtarget =
                (StandaloneBuildSubtarget)SessionState.GetInt(PrevSubtargetKey, (int)EditorUserBuildSettings.standaloneBuildSubtarget);
            var previous = (BuildTarget)SessionState.GetInt(PrevTargetKey, (int)EditorUserBuildSettings.activeBuildTarget);
            if (EditorUserBuildSettings.activeBuildTarget != previous)
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildPipeline.GetBuildTargetGroup(previous), previous);
        }

        /// <summary>'folder' into 'zipPath' under 'top', without IL2CPP's and Burst's debug folders. 'unix': Unix modes in the
        /// entries (rwxr-xr-x for the executables, .so libraries and scripts, rw-r--r-- else), which unzip keeps on Linux.</summary>
        static void Zip(string folder, string zipPath, string top, bool unix)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(zipPath));
            if (File.Exists(zipPath)) File.Delete(zipPath);
            var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
            foreach (string file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(folder, file).Replace('\\', '/');
                if (rel.Contains("_BackUpThisFolder_ButDontShipItWithYourGame") || rel.Contains("_DoNotShip")) continue;
                var entry = archive.CreateEntryFromFile(file, top + "/" + rel, System.IO.Compression.CompressionLevel.Optimal);
                if (!unix) continue;
                bool exec = rel.EndsWith(".x86_64") || rel.EndsWith(".so") || rel.EndsWith(".sh");
                entry.ExternalAttributes = (exec ? 0x81ED : 0x81A4) << 16;   // S_IFREG | 0755 / 0644
            }
            archive.Dispose();
            if (unix) MarkMadeOnUnix(zipPath);
        }

        /// <summary>Info-ZIP's unzip applies an entry's Unix mode only when its "version made by" names Unix (3); .NET writes
        /// the platform it runs on (0 on Windows) and has no setting for it, so each central directory entry's host byte is
        /// set afterwards. A zip64 archive (over 4 GB) is left as it is.</summary>
        static void MarkMadeOnUnix(string zipPath)
        {
            using var file = new FileStream(zipPath, FileMode.Open, FileAccess.ReadWrite);
            int tailLength = (int)Math.Min(file.Length, 22 + 65535);
            var tail = new byte[tailLength];
            file.Seek(-tailLength, SeekOrigin.End);
            ReadFully(file, tail);
            int eocd = -1;
            for (int i = tailLength - 22; i >= 0; i--)
                if (BitConverter.ToUInt32(tail, i) == 0x06054b50) { eocd = i; break; }
            if (eocd < 0) { Debug.LogWarning("GoF2: server zip without an end record; its Unix modes need chmod after unzip."); return; }
            int count = BitConverter.ToUInt16(tail, eocd + 10);
            uint size = BitConverter.ToUInt32(tail, eocd + 12), offset = BitConverter.ToUInt32(tail, eocd + 16);
            if (offset == 0xFFFFFFFF || size == 0xFFFFFFFF || count == 0xFFFF) { Debug.LogWarning("GoF2: zip64 server zip; its Unix modes need chmod after unzip."); return; }
            var directory = new byte[size];
            file.Seek(offset, SeekOrigin.Begin);
            ReadFully(file, directory);
            int p = 0;
            for (int n = 0; n < count && p + 46 <= directory.Length && BitConverter.ToUInt32(directory, p) == 0x02014b50; n++)
            {
                directory[p + 5] = 3;   // version made by: high byte = host system, 3 = Unix
                p += 46 + BitConverter.ToUInt16(directory, p + 28) + BitConverter.ToUInt16(directory, p + 30) + BitConverter.ToUInt16(directory, p + 32);
            }
            file.Seek(offset, SeekOrigin.Begin);
            file.Write(directory);
        }

        static void ReadFully(Stream s, byte[] buffer)
        {
            for (int read = 0, n; read < buffer.Length; read += n)
                if ((n = s.Read(buffer, read, buffer.Length - read)) == 0) throw new EndOfStreamException();
        }

        /// <summary>Player Settings > Dedicated Server > "Enable Dedicated Server optimizations"
        /// (PlayerSettings.dedicatedServerOptimizations), read and written by reflection so an Editor without it still
        /// compiles (it then just builds without them).</summary>
        static bool ServerOptimizations
        {
            get
            {
                var p = typeof(PlayerSettings).GetProperty("dedicatedServerOptimizations");
                return p != null && p.GetValue(null) is bool b && b;
            }
            set
            {
                var p = typeof(PlayerSettings).GetProperty("dedicatedServerOptimizations");
                if (p != null && p.CanWrite) p.SetValue(null, value);
                else if (value) Debug.LogWarning("GoF2: this Editor has no dedicated server optimizations setting; the server build keeps the textures and audio.");
            }
        }
    }
}
