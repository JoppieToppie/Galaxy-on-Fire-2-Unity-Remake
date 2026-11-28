// BuildRequests.cs  (Editor only)
// Player builds asked for from outside the Editor (the unity CLI, scripts): write Library/GoF2BuildRequest.json
//   {"profile": "Assets/Settings/Build Profiles/Android™.asset", "output": "Build/HdrTest/GoF2.apk", "env": ["GOF2_ANDROID_BETA=1"]}
// and the build runs as soon as the Editor is idle (not compiling, importing, building or in Play mode); its result goes to
// Library/GoF2BuildResult.txt ("Succeeded errors 0 00:06:56 ..."). A build queued with EditorApplication.delayCall from an
// eval was lost whenever a code reload came after the eval (2026-11-28, twice in one session); the request file outlives
// reloads. The active build target must already be the profile's (BuildTargetGuard refuses the build otherwise, and the
// result says so).

using System.IO;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEngine;

namespace GoF2Remake.EditorTools
{
    [InitializeOnLoad]
    public static class BuildRequests
    {
        public const string RequestPath = "Library/GoF2BuildRequest.json";
        public const string ResultPath = "Library/GoF2BuildResult.txt";

        [System.Serializable]
        class Request
        {
            public string profile;
            public string output;
            public string[] env;
        }

        static BuildRequests() => EditorApplication.update += Poll;

        static void Poll()
        {
            if (!File.Exists(RequestPath) || EditorApplication.isCompiling || EditorApplication.isUpdating
                || BuildPipeline.isBuildingPlayer || EditorApplication.isPlayingOrWillChangePlaymode) return;
            string json = File.ReadAllText(RequestPath);
            File.Delete(RequestPath);   // first: a build that throws or hangs never runs twice
            string result;
            try
            {
                var request = JsonUtility.FromJson<Request>(json);
                foreach (var pair in request.env ?? new string[0])
                {
                    int eq = pair.IndexOf('=');
                    if (eq > 0) System.Environment.SetEnvironmentVariable(pair.Substring(0, eq), eq + 1 < pair.Length ? pair.Substring(eq + 1) : null);
                }
                var profile = AssetDatabase.LoadAssetAtPath<BuildProfile>(request.profile);
                if (profile == null) throw new FileNotFoundException("No build profile at " + request.profile);
                var report = BuildPipeline.BuildPlayer(new BuildPlayerWithProfileOptions
                    { buildProfile = profile, locationPathName = request.output, options = BuildOptions.None });
                result = $"{report.summary.result} errors {report.summary.totalErrors} {report.summary.totalTime} {request.output}";
            }
            catch (System.Exception e) { result = "Failed " + e; }
            File.WriteAllText(ResultPath, result);
            Debug.Log("BuildRequests: " + result);
        }
    }
}
