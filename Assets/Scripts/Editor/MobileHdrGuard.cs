// MobileHdrGuard.cs  (Editor only)
// HDR display output is for the desktop players (CLAUDE.md "HDR" in docs/notes/options.md): the project settings allow and
// use it so Unity finds HDR displays on Windows, and those settings are global, so the Android build of 2026.11.21 started
// in HDR on HDR-capable phones (Discord, a OnePlus 7T: the picture very dark, half the frame rate; Bootstrap.ApplyHdr's
// switch-off doesn't take there). Phone builds (Android, iOS) get both off, put back once the build has finished or failed
// (EditorApplication.delayCall runs after BuildPipeline.BuildPlayer returns, as in BuildVersionStamp).

using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace GoF2Remake.EditorTools
{
    public class MobileHdrGuard : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        /// <summary>Whether a build for 'target' may start in HDR: only the desktop players.</summary>
        public static bool HdrAllowed(BuildTarget target) => target != BuildTarget.Android && target != BuildTarget.iOS;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (HdrAllowed(report.summary.platform)) return;
            bool allow = PlayerSettings.allowHDRDisplaySupport, use = PlayerSettings.useHDRDisplay;
            if (!allow && !use) return;
            PlayerSettings.useHDRDisplay = false;
            PlayerSettings.allowHDRDisplaySupport = false;
            UnityEngine.Debug.Log("MobileHdrGuard: HDR display output off for this phone build.");
            EditorApplication.delayCall += () =>
            {
                PlayerSettings.allowHDRDisplaySupport = allow;
                PlayerSettings.useHDRDisplay = use;
                AssetDatabase.SaveAssets();
            };
        }
    }
}
