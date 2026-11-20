// DlssRedist.cs
// After a Windows player build: a newer DLSS upscaler library in place of the one Unity ships. Unity's NVIDIA module puts
// its own nvngx_dlss.dll next to the game (310.5.3 with Unity 7000.0.0a16); a newer one (the same 310 series: NVIDIA's
// drop-in update, newer models) in Native/Streamline/redist/ (git-ignored, like the Streamline SDK there) replaces it in
// the build. Without that file, or with an older one, the build keeps Unity's. docs/notes/options.md "Frame generation".

using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace GoF2Remake.EditorTools
{
    public class DlssRedist : IPostprocessBuildWithReport
    {
        public const string Source = "Native/Streamline/redist/nvngx_dlss.dll";

        public int callbackOrder => 0;

        public void OnPostprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.StandaloneWindows64) return;
            if (report.summary.result == BuildResult.Failed || report.summary.result == BuildResult.Cancelled) return;
            string folder = Path.GetDirectoryName(report.summary.outputPath);
            if (string.IsNullOrEmpty(folder)) return;
            CopyPictureFilters(report.summary.outputPath);
            CopyXess(report.summary.outputPath);
            CopyFsrFrameGeneration(report.summary.outputPath);
            CopyNeural(report.summary.outputPath);
            string built = Path.Combine(folder, "nvngx_dlss.dll");
            if (!File.Exists(Source) || !File.Exists(built)) return;
            Version ours = VersionOf(Source), theirs = VersionOf(built);
            if (ours == null || theirs == null || ours <= theirs) return;
            File.Copy(Source, built, true);
            UnityEngine.Debug.Log($"DLSS: nvngx_dlss.dll {theirs} replaced with {ours} in the build");
        }

        /// <summary>DLSS neural rendering's two DLLs (sl.dlss_nr.dll, nvngx_dlssnr.dll), where the builder has them in
        /// Native/Streamline/redist (not in the repository: NVIDIA has not published them), go beside the plugin, where
        /// gof2_streamline.dll looks for them. Without them the build has no neural rendering option.</summary>
        static void CopyNeural(string exe)
        {
            string from = Path.GetDirectoryName(Source);
            string plugins = Path.Combine(Path.GetDirectoryName(exe), Path.GetFileNameWithoutExtension(exe) + "_Data", "Plugins", "x86_64");
            string[] names = { "sl.dlss_nr.dll", "nvngx_dlssnr.dll" };
            if (!Directory.Exists(plugins) || !Array.TrueForAll(names, n => File.Exists(Path.Combine(from, n)))) return;
            foreach (string n in names) File.Copy(Path.Combine(from, n), Path.Combine(plugins, n), true);
            UnityEngine.Debug.Log("DLSS: neural rendering's DLLs copied into the build");
        }

        /// <summary>NVIDIA Image Scaling's and RTX Dynamic Vibrance's DLLs (Streamline's release: sl.nis.dll;
        /// sl.deepdvc.dll with nvngx_deepdvc.dll), where the builder has them in Native/Streamline/redist, go beside
        /// the plugin the same way. Without them the build has no such options.</summary>
        static void CopyPictureFilters(string exe)
        {
            string from = Path.GetDirectoryName(Source);
            string plugins = Path.Combine(Path.GetDirectoryName(exe), Path.GetFileNameWithoutExtension(exe) + "_Data", "Plugins", "x86_64");
            if (!Directory.Exists(plugins)) return;
            foreach (string[] names in new[] { new[] { "sl.nis.dll" }, new[] { "sl.deepdvc.dll", "nvngx_deepdvc.dll" } })
            {
                if (!Array.TrueForAll(names, n => File.Exists(Path.Combine(from, n)))) continue;
                foreach (string n in names) File.Copy(Path.Combine(from, n), Path.Combine(plugins, n), true);
                UnityEngine.Debug.Log("Streamline: " + names[0] + " copied into the build");
            }
        }

        /// <summary>Intel's libxess.dll (the XeSS SDK's, Native/XeSS/sdk/bin: git-ignored, Intel's licence lets it be
        /// redistributed as it is) goes beside gof2_xess.dll, which loads it from there. Without it the build offers
        /// no XeSS.</summary>
        static void CopyXess(string exe)
        {
            const string from = "Native/XeSS/sdk/bin/libxess.dll";
            string plugins = Path.Combine(Path.GetDirectoryName(exe), Path.GetFileNameWithoutExtension(exe) + "_Data", "Plugins", "x86_64");
            if (!Directory.Exists(plugins) || !File.Exists(from) || !File.Exists(Path.Combine(plugins, "gof2_xess.dll"))) return;
            File.Copy(from, Path.Combine(plugins, "libxess.dll"), true);
            UnityEngine.Debug.Log("XeSS: libxess.dll copied into the build");
        }

        /// <summary>AMD's frame generation DLL (the FidelityFX SDK's signed amd_fidelityfx_framegeneration_dx12.dll,
        /// Native/FsrFrameGen/sdk: git-ignored) goes beside the loader Unity's AMD module put in the build, where that
        /// loader looks for it; only when the two are of one SDK build (their file versions' last number). Without it
        /// the build has no FSR frame generation.</summary>
        static void CopyFsrFrameGeneration(string exe)
        {
            const string from = "Native/FsrFrameGen/sdk/Kits/FidelityFX/signedbin";
            string folder = Path.GetDirectoryName(exe);
            string loader = Path.Combine(folder, "amd_fidelityfx_loader_dx12.dll");
            string source = Path.Combine(from, "amd_fidelityfx_framegeneration_dx12.dll");
            if (!File.Exists(loader) || !File.Exists(source)) return;
            Version built = VersionOf(loader), ours = VersionOf(Path.Combine(from, "amd_fidelityfx_loader_dx12.dll"));
            if (built == null || ours == null || built != ours)
            {
                UnityEngine.Debug.LogWarning($"FSR frame generation: the build's FidelityFX loader is {built}, the SDK in {from} is {ours}: not copied");
                return;
            }
            File.Copy(source, Path.Combine(folder, "amd_fidelityfx_framegeneration_dx12.dll"), true);
            UnityEngine.Debug.Log("FSR frame generation: amd_fidelityfx_framegeneration_dx12.dll copied into the build");
        }

        /// <summary>A DLL's file version ("310,9,16,0" in NVIDIA's resources); null when it has none.</summary>
        public static Version VersionOf(string path)
        {
            try
            {
                var info = FileVersionInfo.GetVersionInfo(path);
                return new Version(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart, info.FilePrivatePart);
            }
            catch (Exception) { return null; }
        }
    }
}
