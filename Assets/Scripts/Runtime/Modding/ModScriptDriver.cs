// ModScriptDriver.cs
// Drives the mod scripts (ModScripts): a hidden DontDestroyOnLoad object made with the first scene. Every frame the
// running states follow the active mods (ModScripts.Sync, inside Tick), their timers run and "update" is raised with the
// frame's game seconds (0 while paused); a scene load raises "scene" with its name.

using UnityEngine;
using UnityEngine.SceneManagement;

namespace GoF2Remake.Modding
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public class ModScriptDriver : MonoBehaviour
    {
        static ModScriptDriver instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (instance != null) return;
            var go = new GameObject("Mod scripts") { hideFlags = HideFlags.HideInHierarchy };
            DontDestroyOnLoad(go);
            instance = go.AddComponent<ModScriptDriver>();
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (instance != null && mode == LoadSceneMode.Single) ModScripts.Raise("scene", scene.name);
        }

        void Update() => ModScripts.Tick(Time.deltaTime);

        void LateUpdate() => ModScripts.LateTick(Time.deltaTime);

        void OnDestroy()
        {
            if (instance != this) return;
            instance = null;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            ModScripts.StopAll();   // leaving Play mode: every hook put back (no domain reload follows)
        }
    }
}
