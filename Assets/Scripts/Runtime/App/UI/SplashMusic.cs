// SplashMusic.cs
// Modified: the menu theme starts with the FISHLABS logo, as MTitle does (the original plays it from the first logo). The
// logos are the Splash scene's (StartupSplash, before any other scene), which starts the music here from
// Resources/GoF2Music/MenuTheme (a SplashTheme that references event 145 Space_NoCombat_Void, so the clip ships once);
// MainMenu adopts the playing source as its music source (Take), so the theme carries on without a second stream. When
// Play mode starts in MainMenu itself (the Editor) the menu draws the logos and starts the music; nothing plays in batch
// mode or on a dedicated server (DedicatedServer.Enabled).

using GoF2Remake.Data;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GoF2Remake.UI
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class SplashMusic
    {
        static AudioSource source;
        static AudioListener listener;

        /// <summary>The theme's source until the menu takes it (StartupSplash keeps it audible while it mutes the rest).</summary>
        public static AudioSource Playing => source;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            source = null;
            listener = null;
        }

        /// <summary>The theme from the start (the Splash scene as it wakes; Bootstrap has applied the volumes).</summary>
        public static void Begin()
        {
            if (source != null || Application.isBatchMode || GoF2Remake.Multiplayer.DedicatedServer.Enabled) return;
            var theme = Resources.Load<SplashTheme>("GoF2Music/MenuTheme");
            var clip = theme != null ? theme.clip : null;
            if (clip == null) return;
            var go = new GameObject("Splash music");
            Object.DontDestroyOnLoad(go);
            // The Splash scene has no listener: one of its own until the menu's camera brings one.
            listener = go.AddComponent<AudioListener>();
            source = go.AddComponent<AudioSource>();
            source.clip = clip;
            source.loop = true;
            source.spatialBlend = 0f;
            source.ignoreListenerPause = true;
            source.volume = Settings.MusicVolume;
            source.Play();
            SceneManager.sceneLoaded -= DropListener;
            SceneManager.sceneLoaded += DropListener;
        }

        /// <summary>The first scene with a listener of its own takes over (two listeners log a warning every frame): the
        /// menu's, not the Splash scene's, which has none.</summary>
        static void DropListener(Scene scene, LoadSceneMode mode)
        {
            DropOwnListener();
            if (listener == null) SceneManager.sceneLoaded -= DropListener;
        }

        static void DropOwnListener()
        {
            if (listener == null) return;
            foreach (var l in Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Exclude))
                if (l != listener) { Object.Destroy(listener); listener = null; return; }
        }

        /// <summary>Modified: the splash's theme, still playing, handed over to 'scene' (moved out of DontDestroyOnLoad, so it
        /// goes with that scene), or null when it isn't playing (then it is gone either way).</summary>
        public static AudioSource Take(Scene scene)
        {
            var s = source;
            source = null;
            if (s == null) { listener = null; return null; }
            if (!s.isPlaying || s.clip == null)
            {
                Object.Destroy(s.gameObject);
                listener = null;
                SceneManager.sceneLoaded -= DropListener;
                return null;
            }
            DropOwnListener();
            listener = null;
            SceneManager.sceneLoaded -= DropListener;
            if (scene.IsValid()) SceneManager.MoveGameObjectToScene(s.gameObject, scene);
            return s;
        }
    }
}
