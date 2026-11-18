// GamePaths.cs
// The folder everything the game keeps between runs lives in (save games, the mod registry, ship presets, mod projects,
// the mod caches, the events and server folders): Application.persistentDataPath, or a sandbox folder while the edit /
// play mode tests run (Scripts/Tests: TestSandbox), so they never touch the player's own saves and mods.

using UnityEngine;

namespace GoF2Remake
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class GamePaths
    {
        /// <summary>A folder in place of persistentDataPath (tests only); null = the real one.</summary>
        public static string Override;

        /// <summary>Application.persistentDataPath unless a test sandbox replaces it (main thread only, like it).</summary>
        public static string Data => !string.IsNullOrEmpty(Override) ? Override : DevData ?? Application.persistentDataPath;

        /// <summary>Development builds only: $GOF2_DATA names another data folder (PlayerAssetsCheck: kept packs, saves and
        /// settings of its own, the player's untouched). A runtime check: Unity 7 defines no DEVELOPMENT_BUILD for scripts.</summary>
        static string DevData
        {
            get
            {
                if (Application.isEditor || !Debug.isDebugBuild) return null;
                string d = System.Environment.GetEnvironmentVariable("GOF2_DATA");
                return string.IsNullOrEmpty(d) ? null : d;
            }
        }
    }
}
