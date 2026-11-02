// BuildVersionStamp.cs
// Every player build gets the date and time of the git commit it is built from as its version (BuildVersion.Format, UTC,
// e.g. 2026.09.29.2315; the build's own time without git): every platform built from one commit (Windows, Linux, Android)
// shows the same version, wherever and whenever it was built (fork change: upstream stamps the build time). Set into
// PlayerSettings.bundleVersion before the build (Application.version, Android's versionName) and put back once the build
// has finished or failed (EditorApplication.delayCall runs after BuildPipeline.BuildPlayer returns), so the project
// settings keep their own value. UWP builds also get it as their package version (yyyy.M.d.HHmm). A release's builds
// share one version through GOF2_BUILD_VERSION (OverrideVariable). The build also gets the code's fingerprint
// (BuildFingerprint, what multiplayer compares: builds of the same code play together whenever they were built) as
// Resources/GoF2Build/BuildFingerprint.txt, deleted again afterwards. No git branch / commit goes in (it used to: joining a
// game compares the fingerprint, so the commit only cluttered the shown version).

using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace GoF2Remake.EditorTools
{
    public class BuildVersionStamp : IPreprocessBuildWithReport
    {
        // After BuildTargetGuard (int.MinValue), before the rest.
        public int callbackOrder => int.MinValue + 1;

        /// <summary>Set (System.Environment.SetEnvironmentVariable in the Editor) to give several builds the same version.</summary>
        public const string OverrideVariable = "GOF2_BUILD_VERSION";

        public void OnPreprocessBuild(BuildReport report)
        {
            string saved = PlayerSettings.bundleVersion;
            // A release's builds share one version: the Editor process's GOF2_BUILD_VERSION environment variable (it outlives
            // the domain reloads of switching platforms) wins over the build's own date and time.
            string stamp = System.Environment.GetEnvironmentVariable(OverrideVariable);
            if (string.IsNullOrWhiteSpace(stamp)) stamp = CommitStamp();
            if (string.IsNullOrWhiteSpace(stamp))
                stamp = System.DateTime.Now.ToString(GoF2Remake.UI.BuildVersion.Format, System.Globalization.CultureInfo.InvariantCulture);
            PlayerSettings.bundleVersion = stamp;
            string fingerprint = BuildFingerprint.Compute();
            System.IO.Directory.CreateDirectory(BuildFingerprint.Folder);
            System.IO.File.WriteAllText(BuildFingerprint.FilePath, fingerprint);
            AssetDatabase.ImportAsset(BuildFingerprint.FilePath, ImportAssetOptions.ForceSynchronousImport);
            UnityEngine.Debug.Log($"GoF2: build version {stamp}, fingerprint {fingerprint}");
            // UWP: the package version is four numbers up to 65535 each: yyyy.M.d.HHmm (2026.10.02.0008 -> 2026.10.2.8).
            var savedWsa = PlayerSettings.WSA.packageVersion;
            bool wsa = report.summary.platform == BuildTarget.WSAPlayer;
            if (wsa)
            {
                var p = stamp.Split('.');
                if (p.Length == 4 && int.TryParse(p[0], out int y) && int.TryParse(p[1], out int mo) && int.TryParse(p[2], out int d) && int.TryParse(p[3], out int hm))
                    PlayerSettings.WSA.packageVersion = new System.Version(y, mo, d, hm);
            }
            // The build saves the project settings with the stamp in them: put the value back and save them again.
            EditorApplication.delayCall += () =>
            {
                PlayerSettings.bundleVersion = saved;
                if (wsa) PlayerSettings.WSA.packageVersion = savedWsa;
                AssetDatabase.DeleteAsset(BuildFingerprint.Folder);   // only in the build (git-ignored meanwhile)
                AssetDatabase.SaveAssets();
            };
        }

        /// <summary>A git command's output in the project folder; null when git isn't there or it failed.</summary>
        static string Run(string args)
        {
            try
            {
                var info = new System.Diagnostics.ProcessStartInfo("git", args)
                {
                    RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
                    WorkingDirectory = System.IO.Directory.GetCurrentDirectory(),
                };
                using (var p = System.Diagnostics.Process.Start(info))
                {
                    string output = p.StandardOutput.ReadToEnd();
                    if (!p.WaitForExit(5000) || p.ExitCode != 0) return null;
                    return output.Trim();
                }
            }
            catch (System.Exception) { return null; }
        }

        /// <summary>The HEAD commit's time in UTC as yyyy.MM.dd.HHmm (the same on every machine and time zone); null without git.</summary>
        internal static string CommitStamp()
        {
            if (!long.TryParse(Run("log -1 --format=%ct"), out long seconds)) return null;
            return System.DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime
                .ToString(GoF2Remake.UI.BuildVersion.Format, System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
