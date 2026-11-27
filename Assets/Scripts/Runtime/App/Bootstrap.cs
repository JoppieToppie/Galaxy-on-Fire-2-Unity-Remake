// Bootstrap.cs
// Process-wide startup settings, applied before the first scene loads, and the options that act on the whole process
// (Settings), re-applied whenever the settings change: frame rate, master volume, window mode and resolution, render
// scale, upscaler and MSAA (the URP asset), the post-process AA (ScreenAntialiasing), the Quality option's detail (LOD bias) and fog, the stick dead zone, and
// bloom / brightness on every scene's global post-processing volume.
// The URP assets themselves are saved with STP selected: URP strips STP's compute shaders from a player build unless a
// pipeline asset uses it (STPResourceStripper). Players never see that value: the upscaler option replaces it before the
// first scene, "off" being URP's automatic filter.
// Mobile players default to 30 fps and are always synced to the display, so there "V-Sync" means the
// display's refresh rate (120 Hz on the S24) and "Uncapped" can't go beyond it either.

using System.Collections.Generic;
using System.Linq;
using GoF2Remake.Data;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace GoF2Remake
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class Bootstrap
    {
        // The project's own values, the "default" of the render scale / MSAA options and the base of the LOD bias.
        static AnisotropicFiltering defaultAniso = AnisotropicFiltering.Enable;
        static float defaultRenderScale = 1f, defaultLodBias = 1f, defaultDeadzone = Settings.DefaultDeadzone;
        static int defaultMsaa = 1, defaultMipLimit;
#if !ENABLE_UPSCALER_FRAMEWORK
        static UpscalingFilterSelection defaultUpscaling = UpscalingFilterSelection.Auto;
#endif
        static UniversalRenderPipelineAsset urp;

        /// <summary>The platform's render scale and MSAA samples (what the options' 0 = default stands for).</summary>
        public static float DefaultRenderScale => defaultRenderScale;
        public static int DefaultMsaa => defaultMsaa;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
#if UNITY_EDITOR
            // Cloud Diagnostics: no exceptions from Play mode in an Editor (contributors' clones without the asset pack's
            // textures sent their Editors' errors to the project's dashboard, 2026-10-11); the players' builds still report.
            UnityEngine.CrashReportHandler.CrashReportHandler.enableCaptureExceptions = false;
#endif
            // A dedicated server (-server): no menu, rendering options, sound or desktop extras, only the server.
            if (Multiplayer.DedicatedServer.Enabled) { Multiplayer.DedicatedServer.Boot(); return; }
            // Remake: an install played before the Original experience became the default keeps its options (once).
            Settings.MigrateExperienceDefaults();
            if (!Application.isEditor) Settings.GuardStartup();   // a start that hung before the menu: the skies back (#371)
            int editorVSync = QualitySettings.vSyncCount;
            if (Application.isMobilePlatform) Screen.sleepTimeout = SleepTimeout.NeverSleep;   // no screen dimming while playing
            urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urp != null)
            {
                defaultRenderScale = urp.renderScale;
                defaultMsaa = urp.msaaSampleCount;
#if !ENABLE_UPSCALER_FRAMEWORK
                defaultUpscaling = urp.upscalingFilter;
#endif
            }
            defaultLodBias = QualitySettings.lodBias;
            defaultAniso = QualitySettings.anisotropicFiltering;
            defaultMipLimit = QualitySettings.globalTextureMipmapLimit;
            defaultDeadzone = InputSystem.settings.defaultDeadzoneMin;

            ApplyAll();
            ApplyDisplay();
#if UNITY_WSA && ENABLE_WINMD_SUPPORT && !UNITY_EDITOR
            UwpDisplay.Install();                 // UWP: the display's real pixel size (a 4K TV, an Xbox), not its view pixels
#endif
            Visuals.ClassicBloomPass.Install();   // the "Original" bloom option
            Visuals.RadialBlurPass.Install();     // Modified: the original's boost blur (post effect 0x1400002), always on
            Visuals.PreTransformGuard.Install();  // Android Vulkan: the scene never drawn straight to the pre-rotated back buffer
            FrameGeneration.Install();            // Windows D3D12, NVIDIA: DLSS Frame Generation and Reflex (Streamline)
            HitchLogger.Install();                // development builds: frame hitches to hitches.log
            UI.ScreenshotKey.Install();           // F12: a screenshot to the pictures library
            UI.IssueReport.Install();               // keeps the last log lines for an issue report (the pause menu's)
            UI.DiscordPresence.Install();         // desktop: Discord Rich Presence
            Flight.Haptics.Install();             // controller rumble and phone vibration
            UI.FpsCounter.Install();              // the frame rate option (Settings.ShowFps)
            UI.StoryStepLabel.Install();          // the story step and station, bottom right (for bug screenshots)
            UI.SpeedrunOverlay.Install();         // the speedrun timer and its splits (Settings.SpeedrunTimer)
            Vr.VrMode.Start();                    // -vr / -vrsim: OpenXR and the VR rig per scene
            if (Vr.VrMode.Headset) Vr.VrPad.Install();   // the VR controllers as a gamepad, the laser as a mouse
            Settings.Changed -= ApplyAll;
            Settings.Changed += ApplyAll;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            Application.focusChanged -= OnFocusChanged;
            Application.focusChanged += OnFocusChanged;
            // URP resolves the framework's upscaler once per pipeline instance (UpscalerFramework): again for each new one.
            RenderPipelineManager.activeRenderPipelineCreated -= ApplyUpscaling;
            RenderPipelineManager.activeRenderPipelineCreated += ApplyUpscaling;
            // The upscaler contexts URP drops are destroyed for real (UpscalerFramework.FlushContexts: a VRAM leak otherwise).
            RenderPipelineManager.endCameraRendering -= UpscalerFramework.FlushContexts;
            RenderPipelineManager.endCameraRendering += UpscalerFramework.FlushContexts;
#if UNITY_EDITOR
            // Leaving Play mode (Application.quitting in the Editor): restore the Editor's values, or the Play-mode ones would
            // stick to QualitySettings.asset and the URP asset, and unhook, because with domain reload off the subscriptions
            // would survive into edit mode.
            System.Action restore = null;
            restore = () =>
            {
                Settings.Changed -= ApplyAll;
                SceneManager.sceneLoaded -= OnSceneLoaded;
                Application.focusChanged -= OnFocusChanged;
                RenderPipelineManager.activeRenderPipelineCreated -= ApplyUpscaling;
                RenderPipelineManager.endCameraRendering -= UpscalerFramework.FlushContexts;
                QualitySettings.vSyncCount = editorVSync;
                QualitySettings.lodBias = defaultLodBias;
                QualitySettings.anisotropicFiltering = defaultAniso;
                Texture.SetGlobalAnisotropicFilteringLimits(-1, -1);
                QualitySettings.globalTextureMipmapLimit = defaultMipLimit;
                if (urp != null)
                {
                    urp.renderScale = defaultRenderScale;
                    urp.msaaSampleCount = defaultMsaa;
#if !ENABLE_UPSCALER_FRAMEWORK
                    urp.upscalingFilter = defaultUpscaling;
#endif
                }
                InputSystem.settings.defaultDeadzoneMin = defaultDeadzone;
                AudioListener.volume = 1f;
                Application.quitting -= restore;
            };
            Application.quitting += restore;
#endif
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            ApplyPostProcessing();
            Visuals.ScreenAntialiasing.RegisterScene(scene);   // the Post-process AA option on its screen cameras
        }

        static bool focused = true;

        static void OnFocusChanged(bool focus)
        {
            focused = focus;
            ApplyVolume();
        }

        /// <summary>The master volume, or silence while the window is in the background with "Mute in background" on.</summary>
        static void ApplyVolume() =>
            AudioListener.volume = Settings.MuteInBackground && !focused && !Application.isMobilePlatform ? 0f : Settings.MasterVolume;

        static void ApplyAll()
        {
            if (!Application.isPlaying) return;
            ApplyFrameRate();
            ApplyVolume();
            if (urp != null) urp.renderScale = AppliedRenderScale(PickedRenderScale, Android, IsTemporal(ActiveUpscaler));
            ApplyUpscaling();
            QualitySettings.lodBias = defaultLodBias * Settings.DetailDistanceFactor;
            ApplyTextureFiltering();
            // Texture quality: the mipmap levels skipped (a change reloads the textures: only when it changes).
            if (QualitySettings.globalTextureMipmapLimit != Settings.TextureQuality) QualitySettings.globalTextureMipmapLimit = Settings.TextureQuality;
            if (!Mathf.Approximately(InputSystem.settings.defaultDeadzoneMin, Settings.StickDeadzone))
                InputSystem.settings.defaultDeadzoneMin = Settings.StickDeadzone;
            ApplyFog();
            ApplyPostProcessing();
            ApplyDisplay();
            ApplyHdr();
            // Options > Graphics "Remastered shield and cloak" (#340): the shield bubble's and the cloak's look (ShieldBubble,
            // Cloak shaders); 0 = the phone original's.
            Shader.SetGlobalFloat(RemasteredFxId, Settings.RemasteredEffects ? 1f : 0f);
        }
        static readonly int RemasteredFxId = Shader.PropertyToID("_GoF2RemasteredFx");

        // Android at 100 % render scale (#45, #163): since Vulkan pre-transform went back on (2026.10.23) part of the screen
        // stays black / frozen at exactly 100 % (rotating the phone 180 degrees shows the other part), on Mali, Exynos and
        // Tensor phones; any other scale (90 %, 125 %, 200 %) is fine. PreTransformGuard (the intermediate texture) didn't
        // help, MSAA 2x at 100 % (2026.10.24) helped on some phones and not on others (a Mali-G57). URP's render-scale-1 path
        // is the one that breaks, so on Android 100 % is drawn at 94 % (AndroidFullScale), outside URP's 5 % snap to 1, the
        // same path as every other working scale. The option keeps showing 100 %; MSAA stays as picked.

        static bool Android => Application.platform == RuntimePlatform.Android;
        static float PickedRenderScale => Settings.RenderScale > 0f ? Settings.RenderScale : defaultRenderScale;

        /// <summary>URP draws a scale within 5 % of 1 as exactly 1 (UniversalRenderPipeline's kRenderScaleThreshold 0.05).</summary>
        static bool AtFullScale(float scale) => Mathf.Abs(scale - 1f) < 0.05f;

        /// <summary>The render scale URP gets for the one picked: Android's 100 % is <see cref="AndroidFullScale"/> (5 % or
        /// more off 1, and 0.95f sits on that edge in float), with or without a temporal upscaler.</summary>
        public static float AppliedRenderScale(float picked, bool android, bool temporal) =>
            android && AtFullScale(picked) ? AndroidFullScale : picked;

        public const float AndroidFullScale = 0.94f;

        /// <summary>The MSAA samples URP gets for the ones picked (after the temporal upscalers' 1): as picked. (Android's
        /// 100 % with MSAA off got 2x from 2026.10.24 until AppliedRenderScale took over, #163.)</summary>
        public static int AppliedMsaa(int picked, float pickedScale, bool android, bool temporal) => picked;

        // ---- upscaler ----------------------------------------------------------------------------------------

        /// <summary>The upscaler option and the MSAA it allows: with the upscaler framework (desktop, iOS) the framework's active
        /// upscaler (UpscalerFramework, also called for every new pipeline), else the asset's upscaling filter.</summary>
        static void ApplyUpscaling()
        {
            if (!Application.isPlaying || urp == null) return;
            int upscaler = ActiveUpscaler;
#if ENABLE_UPSCALER_FRAMEWORK
            UpscalerFramework.Apply(UpscalerId(upscaler), Settings.UpscalerQuality, Settings.Fsr4Lite, Settings.DlssPreset);
#else
            // FSR 1 only below 100 % (#45: on Android FSR at 100 % or more left part of the screen frozen; at those scales
            // there is nothing to upscale). The cause was thought to be the scene drawn straight to the pre-rotated back buffer, which
            // PreTransformGuard was meant to prevent (it doesn't: see AppliedRenderScale); this guard stays until FSR at 100 %
            // is checked on a phone. (Android's 100 % is drawn at 94 % now, so FSR 1 upscales those last 6 % there.)
            urp.upscalingFilter = upscaler == Settings.UpscalerFsr && urp.renderScale < 0.999f ? UpscalingFilterSelection.FSR
                : upscaler == Settings.UpscalerStp ? UpscalingFilterSelection.STP : UpscalingFilterSelection.Auto;
#endif
            // STP, DLSS and FSR 2+ are temporal: URP's temporal anti-aliasing path, which needs MSAA off
            // (UniversalCameraData.IsTemporalAAEnabled). Only set when it changes (a change can rebuild render targets).
            bool temporal = IsTemporal(upscaler);
            int msaa = AppliedMsaa(temporal ? 1 : Settings.Msaa > 0 ? Settings.Msaa : defaultMsaa, PickedRenderScale, Android, temporal);
            if (urp.msaaSampleCount != msaa) urp.msaaSampleCount = msaa;
            // The Post-process AA option (SMAA / FXAA / off) on the screen cameras; after the MSAA and the upscaler, which
            // decide whether the station camera can take TAA.
            Visuals.ScreenAntialiasing.ApplyAll();
        }

        /// <summary>The framework's id for an upscaler option (UpscalerFramework).</summary>
        static string UpscalerId(int upscaler) => upscaler switch
        {
            Settings.UpscalerFsr => UpscalerFramework.Fsr1,
            Settings.UpscalerStp => UpscalerFramework.Stp,
            Settings.UpscalerDlss => UpscalerFramework.DlssId,
            Settings.UpscalerFsrTemporal => UpscalerFramework.BestFsr ?? UpscalerFramework.Auto,
            Settings.UpscalerMetalFxSpatial => UpscalerFramework.MetalFxSpatial,
            Settings.UpscalerMetalFxTemporal => UpscalerFramework.MetalFxTemporal,
            Settings.UpscalerXess => UpscalerFramework.Xess,
            _ => UpscalerFramework.Auto,
        };

        /// <summary>STP, DLSS, FSR 2+ and MetalFX Temporal: temporal (anti-aliasing included, MSAA off, the quality mode picks
        /// the resolution of DLSS / FSR).</summary>
        public static bool IsTemporal(int upscaler) =>
            upscaler == Settings.UpscalerStp || upscaler == Settings.UpscalerDlss || upscaler == Settings.UpscalerFsrTemporal
            || upscaler == Settings.UpscalerMetalFxTemporal || upscaler == Settings.UpscalerXess;

        /// <summary>NVIDIA DLSS on this device (desktop builds with the upscaler framework, an RTX GPU, Direct3D 11 / 12 or
        /// Vulkan; known once URP has made its pipeline).</summary>
        public static bool DlssSupported => UpscalerFramework.DlssSupported;

        /// <summary>Intel XeSS on this device (Windows Direct3D 12 builds with its plugin; any GPU with Shader Model 6.4).</summary>
        public static bool XessSupported => UpscalerFramework.XessSupported;

        /// <summary>AMD FSR 2 / 3 / 4 on this device (the newest it runs, UpscalerFramework.BestFsr).</summary>
        public static bool FsrTemporalSupported => UpscalerFramework.BestFsr != null;

        /// <summary>Apple MetalFX Spatial / Temporal on this device (macOS / iOS builds on Metal, UpscalerFramework; known once URP has
        /// made its pipeline).</summary>
        public static bool MetalFxSpatialSupported => UpscalerFramework.MetalFxSpatialSupported;
        public static bool MetalFxTemporalSupported => UpscalerFramework.MetalFxTemporalSupported;

        /// <summary>FSR 1 needs shader model 4.5 (FSRUtils); STP compute shaders and no OpenGL ES (STP.IsSupported), so on
        /// Android it runs on Vulkan only, and its compute shaders in the build (StpResourcesPresent).</summary>
        public static bool FsrSupported => FSRUtils.IsSupported();
        public static bool StpSupported => STP.IsSupported() && StpResourcesPresent;

        static bool? stpResources;

        /// <summary>STP.RuntimeResources (its compute shaders) survived the build: URP's STPResourceStripper drops them when no
        /// pipeline asset had STP selected at build time, and STP then fails every frame (the game froze on Android). The
        /// type is internal, so GraphicsSettings.TryGetRenderPipelineSettings is called through reflection.</summary>
        static bool StpResourcesPresent
        {
            get
            {
                if (stpResources.HasValue) return stpResources.Value;
                bool present = false;
                try
                {
                    var type = typeof(STP).GetNestedType("RuntimeResources", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                    var method = typeof(GraphicsSettings).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                        .FirstOrDefault(m => m.Name == "TryGetRenderPipelineSettings" && m.IsGenericMethodDefinition && m.GetParameters().Length == 1);
                    if (type != null && method != null)
                    {
                        var args = new object[] { null };
                        present = (bool)method.MakeGenericMethod(type).Invoke(null, args) && args[0] != null;
                    }
                }
                catch (System.Exception e) { Debug.LogWarning($"Bootstrap: STP resources check failed ({e.Message})"); }
                if (!present) Debug.LogWarning("Bootstrap: STP's resources are not in this build; the STP upscaler is unavailable.");
                stpResources = present;
                return present;
            }
        }

        /// <summary>The upscaler option as far as this device supports it (else off).</summary>
        public static int ActiveUpscaler => (upscalerOverride ??= ReadUpscalerOverride()) >= 0 ? Supported(upscalerOverride.Value) : Supported(Settings.Upscaler);

        // (testing: $GOF2_UPSCALER = a Settings.Upscaler* number takes the option's place for this run)
        static int? upscalerOverride;
        static int ReadUpscalerOverride() =>
            int.TryParse(System.Environment.GetEnvironmentVariable("GOF2_UPSCALER"), out int v) ? Mathf.Clamp(v, 0, Settings.UpscalerXess) : -1;

        static int Supported(int upscaler) => upscaler switch
        {
            Settings.UpscalerFsr when FsrSupported => Settings.UpscalerFsr,
            Settings.UpscalerStp when StpSupported => Settings.UpscalerStp,
            Settings.UpscalerDlss when DlssSupported => Settings.UpscalerDlss,
            Settings.UpscalerXess when XessSupported => Settings.UpscalerXess,
            // (XeSS where the device has none: the temporal upscaler it does have)
            Settings.UpscalerXess when FsrTemporalSupported => Settings.UpscalerFsrTemporal,
            Settings.UpscalerXess when StpSupported => Settings.UpscalerStp,
            Settings.UpscalerFsrTemporal when FsrTemporalSupported => Settings.UpscalerFsrTemporal,
            Settings.UpscalerMetalFxSpatial when MetalFxSpatialSupported => Settings.UpscalerMetalFxSpatial,
            Settings.UpscalerMetalFxTemporal when MetalFxTemporalSupported => Settings.UpscalerMetalFxTemporal,
            // Remake (2026-11-14): MetalFX where the device has none (a preset's on an older Mac): its counterpart, not
            // a plain stretch
            Settings.UpscalerMetalFxSpatial when FsrSupported => Settings.UpscalerFsr,
            Settings.UpscalerMetalFxTemporal when FsrTemporalSupported => Settings.UpscalerFsrTemporal,
            Settings.UpscalerMetalFxTemporal when FsrSupported => Settings.UpscalerFsr,
            _ => Settings.UpscalerOff,
        };

        // ---- frame rate --------------------------------------------------------------------------------------

        public static int DisplayRefreshRate
        {
            get
            {
                int hz = Mathf.RoundToInt((float)Screen.currentResolution.refreshRateRatio.value);
                return hz > 0 ? hz : 60;
            }
        }

        public static void ApplyFrameRate()
        {
            if (!Application.isPlaying) return;
            bool mobile = Application.isMobilePlatform;
            // A benchmark run measures what the device can do: no limit, no V-Sync (Data.Benchmark).
            switch (Benchmark.Active ? FrameRate.Uncapped : Settings.FrameRate)
            {
                case FrameRate.Uncapped:
                    QualitySettings.vSyncCount = 0;
                    Application.targetFrameRate = mobile ? 1000 : -1;   // -1 on mobile would mean 30 fps
                    break;
                case FrameRate.VSync:
                    QualitySettings.vSyncCount = 1;
                    Application.targetFrameRate = mobile ? Mathf.Max(30, DisplayRefreshRate) : -1;
                    break;
                default: Limit(Settings.FrameRateLimit(Settings.FrameRate)); break;
            }
        }

        static void Limit(int fps)
        {
            QualitySettings.vSyncCount = 0;   // targetFrameRate is ignored while vSyncCount > 0
            // Reflex's own limiter holds the cap while Streamline runs (FrameGeneration.FrameLimitUs: the driver paces it
            // with frame generation); Unity's then stays out of the way.
            Application.targetFrameRate = FrameGeneration.ReflexLimits ? -1 : fps;
        }

        /// <summary>The frame rate cap the frame rate option asks for (0: none, V-Sync or uncapped).</summary>
        public static int FrameRateCap => Settings.FrameRateLimit(Settings.FrameRate);

        /// <summary>The frame rate option's choices on this device (Settings.FrameRateChoices): every limit on a desktop
        /// (above the display's rate one still caps the load without V-Sync), on a phone or tablet those its screen
        /// can show; the one in use always.</summary>
        public static List<FrameRate> FrameRateChoices()
        {
            var list = new List<FrameRate>();
            bool mobile = Application.isMobilePlatform;
            int hz = DisplayRefreshRate;
            foreach (var rate in Settings.FrameRateChoices)
                if (!mobile || Settings.FrameRateLimit(rate) <= hz || rate == Settings.FrameRate) list.Add(rate);
            return list;
        }

        /// <summary>Who holds the frame rate cap changed (FrameGeneration: Reflex's limiter took it, or gave it back).</summary>
        public static void RefreshFrameRate() => ApplyFrameRate();

        // ---- window ------------------------------------------------------------------------------------------

        /// <summary>Window mode and resolution apply to desktop players only (the Editor's Game view keeps its own). Not UWP:
        /// its window belongs to the app model, and Screen.mainWindowDisplayInfo throws there (NotSupportedException), which
        /// stopped Bootstrap.Init and the main menu's options setup: a black screen after the splash.</summary>
#if UNITY_WSA
        public static bool HasDisplayOptions => false;
#else
        public static bool HasDisplayOptions => !Application.isMobilePlatform && !Application.isEditor;
#endif

        /// <summary>The display's resolutions, distinct sizes, smallest first.</summary>
        public static List<Vector2Int> Resolutions()
        {
            var list = Screen.resolutions.Select(r => new Vector2Int(r.width, r.height)).Distinct()
                .Where(r => r.y >= 480).OrderBy(r => r.x * r.y).ToList();
            var native = NativeResolution;
            if (!list.Contains(native)) list.Add(native);
            return list;
        }

        static Vector2Int NativeResolution
        {
            get
            {
                var current = new Vector2Int(Screen.currentResolution.width, Screen.currentResolution.height);
                if (!HasDisplayOptions) return current;   // mainWindowDisplayInfo is Windows / macOS / Linux standalone only
                var d = Screen.mainWindowDisplayInfo;
                return d.width > 0 ? new Vector2Int(d.width, d.height) : current;
            }
        }

        /// <summary>Launched with Unity's own window options (-screen-fullscreen / -screen-width / -screen-height /
        /// -window-mode / -popupwindow, e.g. the multiplayer test client in a small window): those win over the window mode
        /// and resolution options for this run.</summary>
        static readonly bool displayFromCommandLine = System.Array.Exists(System.Environment.GetCommandLineArgs(),
            a => a == "-screen-fullscreen" || a == "-screen-width" || a == "-screen-height" || a == "-window-mode" || a == "-popupwindow");

        static void ApplyDisplay()
        {
            if (!HasDisplayOptions || displayFromCommandLine) return;
            var mode = Settings.DisplayMode switch
            {
                DisplayMode.Fullscreen => FullScreenMode.ExclusiveFullScreen,
                DisplayMode.Windowed => FullScreenMode.Windowed,
                _ => FullScreenMode.FullScreenWindow,
            };
            var size = Settings.Resolution;
            if (size.x <= 0 || size.y <= 0)
            {
                size = NativeResolution;
                if (mode == FullScreenMode.Windowed) size = new Vector2Int(size.x * 4 / 5, size.y * 4 / 5);   // a window that fits
            }
            if (Screen.fullScreenMode == mode && Screen.width == size.x && Screen.height == size.y) return;
            Screen.SetResolution(size.x, size.y, mode);
        }

        // ---- fog (the Quality option) ------------------------------------------------------------------------

        static bool sceneFog;

        /// <summary>The level's fog (the system's, a Vossk hangar's): on only with Quality High ("Fog on", 512).</summary>
        public static void SetSceneFog(bool on)
        {
            sceneFog = on;
            ApplyFog();
        }

        /// <summary>Settings.TextureFiltering: bilinear, the textures' own anisotropic levels, every texture at least 9x
        /// (Unity's forced minimum), or 16x.</summary>
        static void ApplyTextureFiltering()
        {
            int f = Settings.TextureFiltering;
            QualitySettings.anisotropicFiltering = f == 0 ? AnisotropicFiltering.Disable : f == 1 ? AnisotropicFiltering.Enable : AnisotropicFiltering.ForceEnable;
            Texture.SetGlobalAnisotropicFilteringLimits(f == 3 ? 16 : -1, -1);   // -1: Unity's own limits
        }

        static void ApplyFog() => RenderSettings.fog = sceneFog && Settings.Fog;

        // ---- HDR output (remake, 2026-11-20, the user's request; docs/notes/options.md "HDR") --------------------------
        // The player allows and uses HDR display output (10 bit: HDR10, which DLSS frame generation needs; without "Use HDR
        // Display Output" Unity reports no HDR display at all, 2026-11-20); Unity then starts in it on a display with HDR on,
        // so the option (off by default) switches it at every start as well. While it is on, a global
        // volume of our own carries URP's Tonemapping override with the options' values (its HDR parameters: paper white,
        // the display's limits or ours, the tonemapper and its settings); off, the volume is gone and the scenes are as built.

        /// <summary>The display takes HDR now (HDR on in Windows' display settings; desktop players).</summary>
        public static bool HdrAvailable
        {
            get
            {
                if (Application.isMobilePlatform || Application.isEditor) return false;
                LogHdr();
                try { return HDROutputSettings.main != null && HDROutputSettings.main.available; }
                catch (System.Exception) { return false; }
            }
        }

        // (testing: $GOF2_HDR=1 / 0 takes the HDR option's place for this run)
        static int hdrOverride = -2;
        static bool HdrOption
        {
            get
            {
                if (hdrOverride == -2) hdrOverride = System.Environment.GetEnvironmentVariable("GOF2_HDR") switch { "1" => 1, "0" => 0, _ => -1 };
                return hdrOverride >= 0 ? hdrOverride > 0 : Settings.Hdr;
            }
        }

        static string hdrLogged;
        static bool hdrLogLater;

        /// <summary>What Unity says about HDR on this display, in the log whenever it changes (a player's "pretty sure I
        /// have HDR on" with the option greyed out, 2026-11-20: the log has to say why).</summary>
        static void LogHdr()
        {
            string line;
            try
            {
                var display = HDROutputSettings.main;
                bool available = display != null && display.available;
                line = $"HDR: system {SystemInfo.hdrDisplaySupportFlags}, displays {HDROutputSettings.displays.Length}, main available {available}"
                    + (available ? $", active {display.active}, gamut {display.displayColorGamut}, format {display.graphicsFormat}, "
                                   + $"{display.minToneMapLuminance} - {display.maxToneMapLuminance} nits, paper white {display.paperWhiteNits}" : "")
                    + $", window mode {Screen.fullScreenMode}, {Screen.width} x {Screen.height}, frame generation hook {FrameGeneration.Bound || FrameGeneration.FsrRunning}";
            }
            catch (System.Exception e) { line = "HDR: " + e.Message; }
            if (line != hdrLogged) { hdrLogged = line; Debug.Log(line); }
            if (!hdrLogLater) { hdrLogLater = true; LogHdrLater(); }
        }

        /// <summary>Once more a while after the start: the window is on its display and the swap chain settled by then.</summary>
        static async void LogHdrLater()
        {
            try
            {
                await Awaitable.WaitForSecondsAsync(10f);
                LogHdr();
            }
            catch (System.Exception) { }
        }

        /// <summary>HDR output is on (the option, on a display that takes it).</summary>
        public static bool HdrActive => HdrAvailable && HDROutputSettings.main.active;

        static Volume hdrVolume;
        static Tonemapping hdrTonemapping;
        static Bloom hdrBloom;
        static ColorAdjustments hdrColor;

        /// <summary>The HDR volume again (UI.HdrCalibration opened or closed: it wants the picture untouched meanwhile).</summary>
        public static void RefreshHdr() => ApplyHdr();

        static void ApplyHdr()
        {
            if (!Application.isPlaying || Application.isEditor) return;
            LogHdr();
            bool available;
            try { available = HDROutputSettings.main != null && HDROutputSettings.main.available; }
            catch (System.Exception) { return; }
            bool want = available && HdrOption && !Application.isMobilePlatform;
            if (available && HDROutputSettings.main.active != want)
            {
                FrameGeneration.Pause(90);   // the swap chain changes its format: no generated frames across it
                HDROutputSettings.main.RequestHDRModeChange(want);
                Debug.Log("HDR output " + (want ? "on" : "off"));
            }
            if (!want)
            {
                if (hdrVolume != null) hdrVolume.gameObject.SetActive(false);
                return;
            }
            HDROutputSettings.main.automaticHDRTonemapping = false;   // (URP tonemaps for the display itself)
            if (hdrVolume == null)
            {
                var go = new GameObject("HDR tonemapping (remake)");
                Object.DontDestroyOnLoad(go);
                hdrVolume = go.AddComponent<Volume>();
                hdrVolume.isGlobal = true;
                hdrVolume.priority = 1000f;
                var profile = ScriptableObject.CreateInstance<VolumeProfile>();
                hdrVolume.sharedProfile = profile;
                hdrTonemapping = profile.Add<Tonemapping>(true);
                hdrBloom = profile.Add<Bloom>(false);
                hdrColor = profile.Add<ColorAdjustments>(false);
            }
            hdrVolume.gameObject.SetActive(true);
            hdrTonemapping.active = true;
            // The calibration screen's pattern stands for nits: no tonemapping, bloom or brightness exposure while it is open.
            bool calibrating = UI.HdrCalibration.Active;
            hdrBloom.active = calibrating;
            hdrBloom.intensity.Override(0f);
            hdrColor.active = calibrating;
            hdrColor.postExposure.Override(0f);
            int tonemapper = calibrating ? 0 : Settings.HdrTonemapping;
            hdrTonemapping.mode.Override(tonemapper switch { 1 => TonemappingMode.Neutral, 2 => TonemappingMode.ACES, _ => TonemappingMode.None });
            hdrTonemapping.detectPaperWhite.Override(false);
            hdrTonemapping.paperWhite.Override(Settings.HdrPaperWhite);
            hdrTonemapping.detectBrightnessLimits.Override(Settings.HdrDetectLimits);
            hdrTonemapping.minNits.Override(Settings.HdrBlack);
            hdrTonemapping.maxNits.Override(Settings.HdrPeak);
            hdrTonemapping.acesPreset.Override((HDRACESPreset)Mathf.Clamp(Settings.HdrAcesPreset, 0, 2));
            hdrTonemapping.neutralHDRRangeReductionMode.Override(Settings.HdrRangeReduction == 1 ? NeutralRangeReductionMode.Reinhard : NeutralRangeReductionMode.BT2390);
            hdrTonemapping.hueShiftAmount.Override(Settings.HdrHueShift);
        }

        // ---- post-processing ---------------------------------------------------------------------------------

        /// <summary>Bloom and the brightness exposure on every global volume (its runtime copy of the profile).</summary>
        public static void ApplyPostProcessing()
        {
            if (!Application.isPlaying) return;
            foreach (var v in Object.FindObjectsByType<Volume>(FindObjectsInactive.Exclude))
            {
                if (!v.isGlobal || v.sharedProfile == null) continue;
                var p = v.profile;
                if (p.TryGet(out Bloom bloom))
                {
                    bloom.active = Settings.Bloom;
                    // The strength option scales the profile asset's own intensity ('p' is this volume's runtime copy).
                    if (v.sharedProfile.TryGet(out Bloom own)) bloom.intensity.value = own.intensity.value * Settings.BloomStrength;
                }
                if (!p.TryGet(out ColorAdjustments color)) color = p.Add<ColorAdjustments>();
                color.postExposure.overrideState = true;
                color.postExposure.value = Settings.BrightnessExposure;
            }
        }
    }
}
