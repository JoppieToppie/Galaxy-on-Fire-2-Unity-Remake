// StartupSplash.cs
// Modified: the startup logos (MTitle: FISHLABS, then "Made with Unity" where the original showed ABYSS ENGINE), one after
// the other, each fading in, held and fading out, in the Splash scene (build index 0) instead of Unity's own splash
// screen: the menu theme starts with the first logo (SplashMusic, as MTitle does), the mods start loading
// (ModLoading.Preload) and the MainMenu scene loads behind the logos. The menu builds its backdrop orbit as it wakes
// (MenuBackground: a frozen frame of seconds in the Editor), so its scene comes in while a logo is held (fully shown, its
// zoom stops for that moment) rather than on black after the logos: the splash stays on top of it (DontDestroyOnLoad, its
// panel sorted over the menu's), and the menu waits for it (Running) before its title. Only the theme is heard meanwhile
// (MuteAllButMusic: the menu's sounds stay muted until the logos are gone). Any key or button skips to the
// next logo. Straight to the menu for a -mpjoin / -mphost test and in a VR headset (the panel draws on the screen only);
// a dedicated server switches the scene off (DedicatedServer.ShutOff). The panel is built in code (no UXML).

using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace GoF2Remake.UI
{
    [RequireComponent(typeof(PanelRenderer))]
    [DefaultExecutionOrder(-1000)]   // register for the UI load before PanelRenderer loads its tree
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public class StartupSplash : MonoBehaviour
    {
        [Tooltip("MTitle image 7001 (FISHLABS), then the remake's Made with Unity logo.")]
        public Texture2D[] logos;
        public string menuScene = "MainMenu";
        [Tooltip("Seconds each logo is fully shown, between its fade in and fade out.")]
        public float holdSeconds = 2f;
        public float fadeSeconds = 0.5f;

        /// <summary>The logos were shown this run (here or by the menu itself when Play mode starts in MainMenu): the menu
        /// goes straight on to the title.</summary>
        public static bool LogosShown;
        /// <summary>The logos are still on screen over the menu, which waits for them before its title.</summary>
        public static bool Running;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => LogosShown = Running = false;

        PanelRenderer panelRenderer;
        PanelSettings runtimePanel;
        VisualTreeAsset emptyTree;
        VisualElement logo;
        Camera cam;
        AsyncOperation menuLoad;
        System.IDisposable anyKey;
        bool skip, held, activated;
        /// <summary>Every source muted while the logos show (MuteAllButMusic), unmuted as the splash goes.</summary>
        readonly System.Collections.Generic.List<AudioSource> muted = new();
        MainMenu menu;

        void Awake()
        {
            if (GoF2Remake.Multiplayer.DedicatedServer.ShutOff(gameObject.scene)) return;
            LogosShown = Running = true;
            DontDestroyOnLoad(gameObject);   // over the menu scene until the last logo is gone
            // the player's copy first (OriginalSetupView), or the packs added since: no music, no mods yet
            // (or the HD question, once: docs/notes/player-assets.md "HD resources")
            setup = GoF2Remake.Data.OriginalSetup.Needed || GoF2Remake.Data.OriginalSetup.PacksPending || GoF2Remake.Data.OriginalSetup.HdPromptDue;
            if (!setup) BeginLoading();
            // Nothing to draw but the panel: until the menu's own, a camera that only clears the screen (black).
            cam = new GameObject("Splash camera").AddComponent<Camera>();
            cam.transform.SetParent(transform, false);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.cullingMask = 0;
            panelRenderer = GetComponent<PanelRenderer>();
            if (panelRenderer.panelSettings != null)
            {
                runtimePanel = Instantiate(panelRenderer.panelSettings);
                runtimePanel.sortingOrder += 50;   // over the menu's panel (the same settings)
                panelRenderer.panelSettings = runtimePanel;
            }
            emptyTree = ScriptableObject.CreateInstance<VisualTreeAsset>();
            panelRenderer.RegisterUIReloadCallback(OnUIReload);
            panelRenderer.visualTreeAsset = emptyTree;
            anyKey = InputSystem.onAnyButtonPress.Call(_ => skip = true);
        }

        bool setup;
        VisualElement uiRoot;

        void BeginLoading()
        {
            SplashMusic.Begin();
            Modding.ModLoading.Preload();   // the mods load while the logos show (the menu waits for the rest)
        }

        void OnDestroy()
        {
            anyKey?.Dispose();
            panelRenderer?.UnregisterUIReloadCallback(OnUIReload);
            if (emptyTree != null) Destroy(emptyTree);
            if (runtimePanel != null) Destroy(runtimePanel);
            foreach (var s in muted)
                if (s != null) s.mute = false;
            muted.Clear();
            Running = false;
        }

        void LateUpdate() => MuteAllButMusic();

        /// <summary>Modified: only the menu theme is heard while the logos show. The menu scene loads (and plays its
        /// backdrop orbit's sounds) behind them, so every other source is muted as it appears (before the audio update
        /// that would play it), the theme (SplashMusic's source, then the menu's music source once it takes it) excepted.
        /// Muted, not paused: a one-shot runs out unheard instead of all of them playing at once when the logos are gone.</summary>
        void MuteAllButMusic()
        {
            if (menu == null)
            {
                var menus = FindObjectsByType<MainMenu>(FindObjectsInactive.Exclude);
                if (menus.Length > 0) menu = menus[0];
            }
            var theme = SplashMusic.Playing;
            var menuMusic = menu != null ? menu.musicSource : null;
            foreach (var s in FindObjectsByType<AudioSource>(FindObjectsInactive.Exclude))
            {
                if (s.mute || s == theme || s == menuMusic) continue;
                s.mute = true;
                muted.Add(s);
            }
        }

        void OnUIReload(PanelRenderer renderer, VisualElement root, int version)
        {
            uiRoot = root;
            root.pickingMode = PickingMode.Ignore;
            root.style.position = Position.Absolute;
            root.style.left = root.style.top = root.style.right = root.style.bottom = 0;
            root.style.backgroundColor = Color.black;
            root.style.justifyContent = Justify.Center;
            root.style.alignItems = Align.Center;
            // The menu's own splash logo (MainMenu.uss .splash-logo): a third of the width, scaled to fit.
            logo = new VisualElement { pickingMode = PickingMode.Ignore, usageHints = UsageHints.DynamicTransform };
            logo.style.width = Length.Percent(34);
            logo.style.height = Length.Percent(44);
            logo.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
            logo.style.opacity = 0f;
            root.Add(logo);
        }

        IEnumerator Start()
        {
            // The menu loads behind the logos from the first frame on: started as the first scene wakes, the load ignored
            // allowSceneActivation and replaced this scene before its first frame.
            yield return null;
            if (setup)
            {
                // a build without the original's content: the player's copy converted before anything else
                while (uiRoot == null || logo == null) yield return null;
                logo.style.display = DisplayStyle.None;   // (its 44 % of the height would push the card down)
                var start = GoF2Remake.Data.OriginalSetup.Needed ? OriginalSetupView.Start.Copy
                          : GoF2Remake.Data.OriginalSetup.PacksPending ? OriginalSetupView.Start.Packs : OriginalSetupView.Start.Hd;
                yield return new OriginalSetupView(uiRoot).Run(start);
                logo.style.display = DisplayStyle.Flex;
                setup = false;
                BeginLoading();
            }
            menuLoad = SceneManager.LoadSceneAsync(menuScene);
            if (menuLoad == null) { Destroy(gameObject); yield break; }
            menuLoad.allowSceneActivation = false;
            bool straight = GoF2Remake.Multiplayer.NetGame.AutoJoinAddress != null || GoF2Remake.Multiplayer.NetGame.TestHost
                            || Vr.VrMode.Headset;
            if (!straight)
            {
                while (logo == null) yield return null;
                for (int i = 0; i < logos.Length; i++)
                {
                    // (the FISHLABS logo, an original's image: a build without the originals cuts it from the player's copy)
                    var tex = logos[i] != null ? logos[i] : i == 0 ? CopysFishlabsLogo() : null;
                    if (tex != null) yield return Show(tex);
                }
            }
            Activate();   // not in yet (a slow load): now, on black (the menu's own splash is black too)
            while (!menuLoad.isDone) yield return null;
            yield return null;   // the menu's first frame under the black panel
            Destroy(gameObject);
        }

        /// <summary>The FISHLABS logo of a build without the originals: the HD pack's large one, else the copy's atlas cut.</summary>
        static Texture2D CopysFishlabsLogo()
        {
            var png = UiImageSlots.ImageOf("logo_fishlabs");
            if (png == null) return null;
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = "logo_fishlabs" };
            return t.LoadImage(png, true) ? t : null;
        }

        void Update()
        {
            if (setup) return;   // (no game data before the player's copy is converted: the mods wait for it)
            _ = Modding.ModLoading.Busy;   // polled as the menu's loading screen does (the hangar shadows)
            if (held && menuLoad != null && menuLoad.progress >= 0.9f) Activate();
        }

        void Activate()
        {
            if (activated || menuLoad == null) return;
            activated = true;
            menuLoad.allowSceneActivation = true;
            SceneManager.sceneLoaded += DropCamera;
        }

        /// <summary>The menu's camera draws from now on (under the panel).</summary>
        void DropCamera(Scene scene, LoadSceneMode mode)
        {
            SceneManager.sceneLoaded -= DropCamera;
            if (cam != null) Destroy(cam.gameObject);
        }

        /// <summary>One logo: fades in, is held, fades out, under a slow zoom (Unity's splash had the Dolly animation). A
        /// press skips to the fade out. While it is held the menu may come in (Update).</summary>
        IEnumerator Show(Texture2D tex)
        {
            logo.style.backgroundImage = new StyleBackground(tex);
            skip = false;
            float total = fadeSeconds * 2f + holdSeconds, t = 0f, alpha = 0f;
            while (t < fadeSeconds + holdSeconds && !skip)
            {
                alpha = fadeSeconds > 0f ? Mathf.Clamp01(t / fadeSeconds) : 1f;
                Place(alpha, t / total);
                held = alpha >= 1f && t < fadeSeconds + holdSeconds - 0.25f;   // not right before the fade out
                yield return null;
                t += Step();
            }
            held = false;
            // The fade out from wherever the fade in was.
            for (float from = alpha, f = 0f; f < fadeSeconds; f += Step())
            {
                Place(from * (1f - f / fadeSeconds), (t + f) / total);
                yield return null;
            }
            Place(0f, 1f);
            yield return null;
        }

        /// <summary>Capped: a long frame (the start, the menu coming in) doesn't skip a fade or shorten a logo.</summary>
        static float Step() => Mathf.Min(Time.unscaledDeltaTime, 0.05f);

        void Place(float alpha, float progress)
        {
            logo.style.opacity = Mathf.SmoothStep(0f, 1f, alpha);
            float k = Mathf.Lerp(0.96f, 1.02f, progress);
            logo.style.scale = new Scale(new Vector2(k, k));
        }
    }
}
