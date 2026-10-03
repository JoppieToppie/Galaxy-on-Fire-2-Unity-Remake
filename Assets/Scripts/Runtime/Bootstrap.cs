// Bootstrap.cs
// Process-wide startup settings, applied before the first scene loads, and the options that act on the whole process
// (Settings), re-applied whenever the settings change: frame rate, master volume, window mode and resolution, render
// scale, upscaler and MSAA (the URP asset), the Quality option's detail (LOD bias) and fog, the stick dead zone, and
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
        static float defaultRenderScale = 1f, defaultLodBias = 1f, defaultDeadzone = Settings.DefaultDeadzone;
        static int defaultMsaa = 1;
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
            // A dedicated server (-server): no menu, rendering options, sound or desktop extras, only the server.
            if (Multiplayer.DedicatedServer.Enabled) { Multiplayer.DedicatedServer.Boot(); return; }
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
            defaultDeadzone = InputSystem.settings.defaultDeadzoneMin;

            ApplyAll();
            ApplyDisplay();
#if UNITY_WSA && ENABLE_WINMD_SUPPORT && !UNITY_EDITOR
            UwpDisplay.Install();                 // UWP: the display's real pixel size (a 4K TV, an Xbox), not its view pixels
#endif
            Visuals.ClassicBloomPass.Install();   // the "Original" bloom option
            HitchLogger.Install();                // development builds: frame hitches to hitches.log
            UI.ScreenshotKey.Install();           // F12: a screenshot to the pictures library
            UI.DiscordPresence.Install();         // desktop: Discord Rich Presence
            Flight.Haptics.Install();             // controller rumble and phone vibration
            UI.FpsCounter.Install();              // the frame rate option (Settings.ShowFps)
            Vr.VrMode.Start();                    // -vr / -vrsim: OpenXR and the VR rig per scene
            if (Vr.VrMode.Headset) Vr.VrPad.Install();   // the VR controllers as a gamepad, the laser as a mouse
            Settings.Changed -= ApplyAll;
            Settings.Changed += ApplyAll;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            // URP resolves the framework's upscaler once per pipeline instance (UpscalerFramework): again for each new one.
            RenderPipelineManager.activeRenderPipelineCreated -= ApplyUpscaling;
            RenderPipelineManager.activeRenderPipelineCreated += ApplyUpscaling;
#if UNITY_EDITOR
            // Leaving Play mode (Application.quitting in the Editor): restore the Editor's values, or the Play-mode ones would
            // stick to QualitySettings.asset and the URP asset, and unhook, because with domain reload off the subscriptions
            // would survive into edit mode.
            System.Action restore = null;
            restore = () =>
            {
                Settings.Changed -= ApplyAll;
                SceneManager.sceneLoaded -= OnSceneLoaded;
                RenderPipelineManager.activeRenderPipelineCreated -= ApplyUpscaling;
                QualitySettings.vSyncCount = editorVSync;
                QualitySettings.lodBias = defaultLodBias;
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

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => ApplyPostProcessing();

        static void ApplyAll()
        {
            if (!Application.isPlaying) return;
            ApplyFrameRate();
            AudioListener.volume = Settings.MasterVolume;
            if (urp != null) urp.renderScale = Settings.RenderScale > 0f ? Settings.RenderScale : defaultRenderScale;
            ApplyUpscaling();
            QualitySettings.lodBias = defaultLodBias * (Settings.Quality >= 2 ? 1f : Settings.Quality == 1 ? 0.6f : 0.35f);
            if (!Mathf.Approximately(InputSystem.settings.defaultDeadzoneMin, Settings.StickDeadzone))
                InputSystem.settings.defaultDeadzoneMin = Settings.StickDeadzone;
            ApplyFog();
            ApplyPostProcessing();
            ApplyDisplay();
        }

        // ---- upscaler ----------------------------------------------------------------------------------------

        /// <summary>The upscaler option and the MSAA it allows: with the upscaler framework (desktop, iOS) the framework's active
        /// upscaler (UpscalerFramework, also called for every new pipeline), else the asset's upscaling filter.</summary>
        static void ApplyUpscaling()
        {
            if (!Application.isPlaying || urp == null) return;
            int upscaler = ActiveUpscaler;
#if ENABLE_UPSCALER_FRAMEWORK
            UpscalerFramework.Apply(UpscalerId(upscaler), Settings.UpscalerQuality);
#else
            urp.upscalingFilter = upscaler == Settings.UpscalerFsr ? UpscalingFilterSelection.FSR
                : upscaler == Settings.UpscalerStp ? UpscalingFilterSelection.STP : UpscalingFilterSelection.Auto;
#endif
            // STP, DLSS and FSR 2+ are temporal: URP's temporal anti-aliasing path, which needs MSAA off
            // (UniversalCameraData.IsTemporalAAEnabled). Only set when it changes (a change can rebuild render targets).
            int msaa = IsTemporal(upscaler) ? 1 : Settings.Msaa > 0 ? Settings.Msaa : defaultMsaa;
            if (urp.msaaSampleCount != msaa) urp.msaaSampleCount = msaa;
        }

        /// <summary>The framework's id for an upscaler option (UpscalerFramework).</summary>
        static string UpscalerId(int upscaler) => upscaler switch
        {
            Settings.UpscalerFsr => UpscalerFramework.Fsr1,
            Settings.UpscalerStp => UpscalerFramework.Stp,
            Settings.UpscalerDlss => UpscalerFramework.Dlss,
            Settings.UpscalerFsrTemporal => UpscalerFramework.BestFsr ?? UpscalerFramework.Auto,
            Settings.UpscalerMetalFxSpatial => UpscalerFramework.MetalFxSpatial,
            Settings.UpscalerMetalFxTemporal => UpscalerFramework.MetalFxTemporal,
            _ => UpscalerFramework.Auto,
        };

        /// <summary>STP, DLSS, FSR 2+ and MetalFX Temporal: temporal (anti-aliasing included, MSAA off, the quality mode picks
        /// the resolution of DLSS / FSR).</summary>
        public static bool IsTemporal(int upscaler) =>
            upscaler == Settings.UpscalerStp || upscaler == Settings.UpscalerDlss || upscaler == Settings.UpscalerFsrTemporal
            || upscaler == Settings.UpscalerMetalFxTemporal;

        /// <summary>NVIDIA DLSS on this device (desktop builds with the upscaler framework, an RTX GPU, Direct3D 11 / 12 or
        /// Vulkan; known once URP has made its pipeline).</summary>
        public static bool DlssSupported => UpscalerFramework.DlssSupported;

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
        public static int ActiveUpscaler => Settings.Upscaler switch
        {
            Settings.UpscalerFsr when FsrSupported => Settings.UpscalerFsr,
            Settings.UpscalerStp when StpSupported => Settings.UpscalerStp,
            Settings.UpscalerDlss when DlssSupported => Settings.UpscalerDlss,
            Settings.UpscalerFsrTemporal when FsrTemporalSupported => Settings.UpscalerFsrTemporal,
            Settings.UpscalerMetalFxSpatial when MetalFxSpatialSupported => Settings.UpscalerMetalFxSpatial,
            Settings.UpscalerMetalFxTemporal when MetalFxTemporalSupported => Settings.UpscalerMetalFxTemporal,
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
            switch (Settings.FrameRate)
            {
                case FrameRate.Fps30: Limit(30); break;
                case FrameRate.Fps60: Limit(60); break;
                case FrameRate.Fps120: Limit(120); break;
                case FrameRate.Uncapped:
                    QualitySettings.vSyncCount = 0;
                    Application.targetFrameRate = mobile ? 1000 : -1;   // -1 on mobile would mean 30 fps
                    break;
                default:
                    QualitySettings.vSyncCount = 1;
                    Application.targetFrameRate = mobile ? Mathf.Max(30, DisplayRefreshRate) : -1;
                    break;
            }
        }

        static void Limit(int fps)
        {
            QualitySettings.vSyncCount = 0;   // targetFrameRate is ignored while vSyncCount > 0
            Application.targetFrameRate = fps;
        }

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

        static void ApplyFog() => RenderSettings.fog = sceneFog && Settings.QualityEffects;

        // ---- post-processing ---------------------------------------------------------------------------------

        /// <summary>Bloom and the brightness exposure on every global volume (its runtime copy of the profile).</summary>
        public static void ApplyPostProcessing()
        {
            if (!Application.isPlaying) return;
            foreach (var v in Object.FindObjectsByType<Volume>(FindObjectsInactive.Exclude))
            {
                if (!v.isGlobal || v.sharedProfile == null) continue;
                var p = v.profile;
                if (p.TryGet(out Bloom bloom)) bloom.active = Settings.Bloom;
                if (!p.TryGet(out ColorAdjustments color)) color = p.Add<ColorAdjustments>();
                color.postExposure.overrideState = true;
                color.postExposure.value = Settings.BrightnessExposure;
            }
        }
    }
}
