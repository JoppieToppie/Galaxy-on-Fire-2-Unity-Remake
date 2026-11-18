// PlayerAssetsBuild.cs
// Feature: docs/notes/player-assets.md "Builds without the originals": a player build made from a checkout without the
// original's assets (the test bed, a sparse checkout: exactly what such a build ships) for checking the content layer in a
// real player. Batch mode, development build (so $GOF2_ORIGINAL_CACHE can point it at a converted copy):
//   Unity -batchmode -buildTarget Linux64 -projectPath <checkout> -executeMethod
//     GoF2Remake.EditorTools.PlayerAssetsBuild.LinuxDevelopment   ($GOF2_BUILD_OUT: the folder, else Build/PlayerAssets/Linux)
// WindowsDevelopment (-buildTarget Win64): the same for Windows on Direct3D 12 only, CoreCLR (Unity's own DLSS / FSR 3 / 4 need it),
// e.g. to compare with the Linux build's FSR under Proton (docs/notes/options.md "FSR on Linux").

using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace GoF2Remake.EditorTools
{
    public static class PlayerAssetsBuild
    {
        public static void LinuxDevelopment() => Development(BuildTarget.StandaloneLinux64, "Linux", "GoF2Remake.x86_64");

        public static void WindowsDevelopment()
        {
            var target = BuildTarget.StandaloneWindows64;
            bool automatic = PlayerSettings.GetUseDefaultGraphicsAPIs(target);
            var apis = PlayerSettings.GetGraphicsAPIs(target);
            // CoreCLR: IL2CPP for Windows can't be built on Linux (only the CoreCLR Windows player is there)
            var standalone = UnityEditor.Build.NamedBuildTarget.Standalone;
            var backend = PlayerSettings.GetScriptingBackend(standalone);
            PlayerSettings.SetScriptingBackend(standalone, ScriptingImplementation.CoreCLR);
            PlayerSettings.SetUseDefaultGraphicsAPIs(target, false);
            // ($GOF2_BUILD_API=Vulkan: Vulkan instead, for LinuxFsrUpscaler's plugin on Windows)
            var api = Environment.GetEnvironmentVariable("GOF2_BUILD_API") == "Vulkan" ? UnityEngine.Rendering.GraphicsDeviceType.Vulkan : UnityEngine.Rendering.GraphicsDeviceType.Direct3D12;
            PlayerSettings.SetGraphicsAPIs(target, new[] { api });
            try { Development(target, "Windows", "GoF2Remake.exe", exit: false); }
            finally
            {
                PlayerSettings.SetGraphicsAPIs(target, apis);
                PlayerSettings.SetUseDefaultGraphicsAPIs(target, automatic);
                PlayerSettings.SetScriptingBackend(standalone, backend);
                AssetDatabase.SaveAssets();
            }
            if (Application.isBatchMode) EditorApplication.Exit(lastResult == BuildResult.Succeeded ? 0 : 1);
        }

        static BuildResult lastResult;

        static void Development(BuildTarget target, string name, string file, bool exit = true)
        {
            string dir = Environment.GetEnvironmentVariable("GOF2_BUILD_OUT");
            if (string.IsNullOrEmpty(dir)) dir = Path.GetFullPath("Build/PlayerAssets/" + name);
            Directory.CreateDirectory(dir);
            var options = new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                locationPathName = Path.Combine(dir, file),
                target = target,
                options = BuildOptions.Development,
            };
            // The stylesheets again: imported while their images were left out (stripped_checkout's placeholders came after),
            // they kept no reference, and UiImageSlots had no texture to fill.
            foreach (var guid in AssetDatabase.FindAssets("t:StyleSheet", new[] { "Assets" }).Concat(AssetDatabase.FindAssets("t:ThemeStyleSheet", new[] { "Assets" })))
                AssetDatabase.ImportAsset(AssetDatabase.GUIDToAssetPath(guid), ImportAssetOptions.ForceUpdate);
            // (BuildVersionStamp puts the version back after the build in a delayCall, which an exit right after never runs)
            string version = PlayerSettings.bundleVersion;
            bool frameTiming = PlayerSettings.enableFrameTimingStats;
            PlayerSettings.enableFrameTimingStats = true;   // (PlayerAssetsCheck's GPU frame times)
            var report = BuildPipeline.BuildPlayer(options);
            PlayerSettings.enableFrameTimingStats = frameTiming;
            PlayerSettings.bundleVersion = version;
            AssetDatabase.SaveAssets();
            var s = report.summary;
            Debug.Log($"GoF2: player-assets build {s.result}: {s.totalSize / 1048576} MB, {s.totalTime.TotalMinutes:0.0} min, {s.totalErrors} errors -> {dir}");
            lastResult = s.result;
            if (exit && Application.isBatchMode) EditorApplication.Exit(s.result == BuildResult.Succeeded ? 0 : 1);
        }
    }
}
