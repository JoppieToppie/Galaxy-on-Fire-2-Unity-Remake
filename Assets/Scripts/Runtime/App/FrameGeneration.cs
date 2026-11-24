// FrameGeneration.cs
// Feature: docs/notes/options.md "Frame generation": NVIDIA DLSS Frame Generation and Reflex in the Windows D3D12 player,
// through NVIDIA Streamline. The work is the native plugin gof2_streamline.dll's (Native/Streamline: preloaded, it starts
// Streamline before Unity makes its device and hands Unity's swap chain to DLSS-G); this is its managed side:
//   - the options (Settings.FrameGeneration / Reflex) and what the Graphics tab says about them,
//   - "armed": the plugin reads from the registry, before any managed code runs, whether to start Streamline at all, so
//     the options are written there on every change, and turning one on for the first time needs a restart (after that
//     the multiplier, off and Reflex's mode change at once),
//   - the frame's steps in the player loop: the Reflex sleep and simulation-start marker first thing in a frame, the
//     simulation-end / render-submit-start markers before the cameras render, and after them a render thread event with
//     the DLSS-G mode (off on a frame FrameGenerationPass tagged nothing for),
//   - the tag event's data (FrameGenerationPass fills it per camera).
// AMD FSR frame generation ("FSR frame generation" in the notes) is the same plugin's other provider, on any vendor's GPU:
// it takes Unity's swap chain in Streamline's place, so a run has one or the other (Settings.FrameGenerationProvider,
// written to the registry like "armed"; UsesFsr), and Reflex and Streamline's other features are not there beside it.
// The Editor has no plugin (and Unity gives a plugin no swap chain there): everything here is off in it.

using System;
using System.Runtime.InteropServices;
using GoF2Remake.Data;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;
using UnityEngine.Rendering;

namespace GoF2Remake
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]   // (the native state lives as long as the process)
    public static class FrameGeneration
    {
        public const int EventTag = 0x534C0001, EventFrameEnd = 0x534C0002, EventNeural = 0x534C0003, EventPicture = 0x534C0004,
            EventUpscale = 0x534C0005, EventUpscaleFree = 0x534C0006;
        public const int TagValues = 84;   // the plugin's TagData.values
        // The plugin's StateBits
        const int Armed = 1, DeviceBound = 2, SwapChainProxied = 4, Supported = 8, Latched = 16, FilesPresent = 32, LateLoad = 64,
            NeuralPresent = 128, NeuralRuns = 256, AntiLagBit = 512, NisPresent = 1024, VibrancePresent = 2048, NisRuns = 4096, VibranceRuns = 8192,
            FsrPresent = 1 << 14, FsrArmed = 1 << 15, FsrSwapChain = 1 << 16, DlssPresent = 1 << 18, DlssRuns = 1 << 19;
        // sl::Result
        const int ResultDriverOutOfDate = 2, ResultOsOutOfDate = 3, ResultOsDisabledHws = 4, ResultNoSupportedAdapter = 6, ResultAdapterNotSupported = 7;

        [StructLayout(LayoutKind.Sequential)]
        struct Info
        {
            public int state, supportResult;
            public uint requirementFlags, dlssgStatus, maxFramesToGenerate, vsyncSupported, generating, tagErrors, dynamicSupported, reflexAvailable,
                neuralErrors, neuralRuns, pictureErrors, pictureRuns;
            public float presentedPerFrame, latencyMs;
        }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        const string Lib = "gof2_streamline";
        [DllImport(Lib)] static extern int Gof2Sl_State();
        [DllImport(Lib)] static extern void Gof2Sl_SetArmed(int on);
        [DllImport(Lib)] static extern void Gof2Sl_SetProvider(int provider);
        [DllImport(Lib)] static extern int Gof2Sl_DlssRenderSize(int mode, uint outputWidth, uint outputHeight, out uint width, out uint height);
        [DllImport(Lib)] static extern void Gof2Sl_StartedOk();
        [DllImport(Lib)] static extern int Gof2Sl_FrameStart(uint frame, int reflexMode, uint frameLimitUs);
        [DllImport(Lib)] static extern void Gof2Sl_SimulationEnd(uint frame);
        [DllImport(Lib)] static extern int Gof2Sl_AntiLag(int enable, uint maxFps);
        [DllImport(Lib)] static extern IntPtr Gof2Sl_EventFunc();
        [DllImport(Lib)] static extern void Gof2Sl_GetInfo(out Info info);
        [DllImport(Lib)] static extern IntPtr Gof2Sl_LastError();
#else
        static int Gof2Sl_State() => throw new DllNotFoundException();
        static void Gof2Sl_SetArmed(int on) { }
        static void Gof2Sl_SetProvider(int provider) { }
        static int Gof2Sl_DlssRenderSize(int mode, uint outputWidth, uint outputHeight, out uint width, out uint height) { width = height = 0; return 0; }
        static void Gof2Sl_StartedOk() { }
        static int Gof2Sl_FrameStart(uint frame, int reflexMode, uint frameLimitUs) => 0;
        static void Gof2Sl_SimulationEnd(uint frame) { }
        static int Gof2Sl_AntiLag(int enable, uint maxFps) => 0;
        static IntPtr Gof2Sl_EventFunc() => IntPtr.Zero;
        static void Gof2Sl_GetInfo(out Info info) => info = default;
        static IntPtr Gof2Sl_LastError() => IntPtr.Zero;
#endif

        struct FrameStartStep { }
        struct SimulationEndStep { }
        struct FrameEndStep { }

        static bool installed, plugin, hooked, startReported;
        static int state;
        static Info info;
        static uint frame, taggedFrame, uiFrame;
        static bool uiLayer = true, ignoreMenus;

        /// <summary>The interface's own layer goes to DLSS-G with the frame (FrameGenerationPass.RecordInterface;
        /// $GOF2_FRAMEGEN_UI=0 leaves it out, to compare).</summary>
        public static bool UiLayer => uiLayer && Running;
        static float presented;
        static IntPtr eventFunc, ring;
        static int ringNext;
        const int RingSlots = 64, UiOffset = 32 + TagValues * 4, SlotSize = UiOffset + 8;
        static CommandBuffer endCommands;
        static readonly float[] values = new float[TagValues];

        // Testing without touching the player's options: $GOF2_FRAMEGEN (0..6) / $GOF2_REFLEX (0..2) in their place for this
        // run, $GOF2_SL_VERBOSE a status line every few seconds (and Streamline's own log lines, the plugin's side). With
        // the plugin's -framegen argument Streamline starts whatever the options say.
        static int frameOverride = -1, reflexOverride = -1, neuralOverride = -1, limitOverride = -1;
        static bool verbose;

        static int FrameSetting => frameOverride >= 0 ? frameOverride : Settings.FrameGeneration;
        static int ReflexSetting => reflexOverride >= 0 ? reflexOverride : Settings.Reflex;
        static bool NeuralSetting => neuralOverride >= 0 ? neuralOverride > 0 : Settings.NeuralRendering;

        static int EnvInt(string name, int max) =>
            int.TryParse(Environment.GetEnvironmentVariable(name), out int v) ? Mathf.Clamp(v, 0, max) : -1;

        static string X(string key, string english) => Localization.Extra(key, english);

        /// <summary>The frame's number as Streamline knows it (the tag event's and the markers').</summary>
        public static uint Frame => frame;

        /// <summary>The plugin and Streamline's DLLs are in this build, on an NVIDIA GPU under D3D12: the options are shown.</summary>
        public static bool Offered => plugin && (state & FilesPresent) != 0 && SystemInfo.graphicsDeviceType == GraphicsDeviceType.Direct3D12
            && SystemInfo.graphicsDeviceVendorID == 0x10DE;

        /// <summary>Streamline runs in this process with Unity's device (Reflex and the markers work).</summary>
        public static bool Bound => (state & (Armed | DeviceBound)) == (Armed | DeviceBound);

        /// <summary>And DLSS-G has the swap chain on a GPU that runs it: frames can be generated.</summary>
        public static bool Running => (state & (Armed | DeviceBound | SwapChainProxied | Supported)) == (Armed | DeviceBound | SwapChainProxied | Supported);

        /// <summary>Frames are being generated right now.</summary>
        public static bool Generating => (Running || FsrRunning) && info.generating != 0;

        // ---- DLSS Super Resolution through Streamline (StreamlineDlssUpscaler) ---------------------------------------------

        /// <summary>Streamline runs here with its DLSS plugin on a GPU that takes it: the DLSS upscaler is Streamline's
        /// (Unity's own can't start NGX beside Streamline).</summary>
        public static bool StreamlineDlss => Bound && (state & DlssRuns) != 0;

        /// <summary>The render resolution DLSS wants for a quality mode (UpscalerFramework.Quality*) at this display size.</summary>
        public static Vector2Int DlssRenderSize(int quality, Vector2Int display)
        {
            try
            {
                if (StreamlineDlss && Gof2Sl_DlssRenderSize(quality, (uint)display.x, (uint)display.y, out uint w, out uint h) != 0)
                    return new Vector2Int(Mathf.Clamp((int)w, 1, display.x), Mathf.Clamp((int)h, 1, display.y));
            }
            catch (EntryPointNotFoundException) { }
            float ratio = quality switch { 0 => 1f, 1 => 1.5f, 2 => 1.724f, 3 => 2f, _ => 3f };
            return new Vector2Int(Mathf.Max(1, Mathf.RoundToInt(display.x / ratio)), Mathf.Max(1, Mathf.RoundToInt(display.y / ratio)));
        }

        /// <summary>An unmanaged block for a plugin event of the render thread (the ring the tag events use).</summary>
        public static IntPtr EventSlot() => NextSlot();

        // ---- AMD FSR frame generation -----------------------------------------------------------------------------------

        /// <summary>AMD's FidelityFX loader and its frame generation DLL are in this build, under D3D12: FSR frame
        /// generation can be chosen (any vendor's GPU).</summary>
        public static bool FsrOffered => plugin && (state & FsrPresent) != 0 && SystemInfo.graphicsDeviceType == GraphicsDeviceType.Direct3D12;

        /// <summary>FSR is the provider the options ask for: the only one here, or the player's pick.</summary>
        public static bool UsesFsr => FsrOffered && (!Offered || Settings.FrameGenerationProvider == Settings.FrameGenerationFsr);

        /// <summary>This run has FSR's frame generation swap chain in place of Unity's.</summary>
        public static bool FsrRunning => (state & (FsrArmed | FsrSwapChain)) == (FsrArmed | FsrSwapChain);

        /// <summary>Streamline's features (Reflex, Image Scaling, neural rendering) are not there this run: FSR has the swap chain.</summary>
        public static bool FsrActive => (state & FsrArmed) != 0;

        /// <summary>The Frame generation row is shown: one of the two providers can run here.</summary>
        public static bool AnyOffered => Offered || FsrOffered;

        /// <summary>Frames shown per rendered frame, lately (2 = one generated frame each); 1 when none are generated.</summary>
        public static float PresentedPerFrame => Generating && presented > 1f ? presented : 1f;

        /// <summary>The most generated frames per rendered one this GPU runs (1 = 2x only); 1 until Streamline said.</summary>
        public static int MaxFrames => Running && info.maxFramesToGenerate > 0 ? (int)Mathf.Min(info.maxFramesToGenerate, 5u) : 1;

        public static bool DynamicSupported => Running && info.dynamicSupported != 0;
        public static bool ReflexAvailable => Bound && info.reflexAvailable != 0;

        /// <summary>The option is on but Streamline was not started with the game: it takes a restart.</summary>
        public static bool NeedsRestart
        {
            get
            {
                // FSR frame generation and Streamline are a start's choice: the other one runs now, or neither
                if (WantsFsr) return (state & FsrArmed) == 0;
                if ((state & FsrArmed) != 0) return Settings.FrameGeneration > 0 || Settings.Reflex > 0;
                return Offered && (state & Armed) == 0
                    && (Settings.FrameGeneration > 0 || Settings.Reflex > 0 || Settings.NeuralRendering && NeuralOffered
                        || Settings.NisSharpness > 0f && SharpeningOffered || Settings.DynamicVibrance && VibranceOffered);
            }
        }

        /// <summary>The options ask for FSR's frame generation (it is on, and FSR is the provider).</summary>
        static bool WantsFsr => UsesFsr && Settings.FrameGeneration > 0;

        // ---- NVIDIA Image Scaling's sharpening and RTX Dynamic Vibrance ---------------------------------------------------

        /// <summary>The option is shown: the feature's DLLs are beside the plugin, and once Streamline has the device,
        /// the GPU runs it (Image Scaling: any; Dynamic Vibrance: GeForce RTX).</summary>
        public static bool SharpeningOffered => Offered && (state & NisPresent) != 0 && (!Bound || (state & NisRuns) != 0);
        public static bool VibranceOffered => Offered && (state & VibrancePresent) != 0 && (!Bound || (state & VibranceRuns) != 0);

        /// <summary>PictureFiltersPass should run on this frame's camera (off after repeated failures).</summary>
        public static bool WantsPicture => Bound && info.pictureErrors < 8
            && ((state & NisRuns) != 0 && Sharpness > 0f || (state & VibranceRuns) != 0 && Vibrance);

        // ($GOF2_NIS: the sharpening in per cent, $GOF2_VIBRANCE=1: Dynamic Vibrance on, for a test run)
        static int sharpnessOverride = -1, vibranceOverride = -1;
        static float Sharpness => sharpnessOverride >= 0 ? sharpnessOverride / 100f : Settings.NisSharpness;
        static bool Vibrance => vibranceOverride >= 0 ? vibranceOverride > 0 : Settings.DynamicVibrance;

        /// <summary>The picture event's data (the plugin's PictureData) for a picture of this size, with the options;
        /// FillNeural adds the two textures when the pass runs.</summary>
        public static IntPtr PictureData(int width, int height)
        {
            IntPtr slot = NextSlot();
            Marshal.WriteIntPtr(slot, 0, IntPtr.Zero);
            Marshal.WriteIntPtr(slot, 8, IntPtr.Zero);
            Marshal.WriteInt32(slot, 16, (int)frame);
            Marshal.WriteInt32(slot, 20, width);
            Marshal.WriteInt32(slot, 24, height);
            Marshal.WriteInt32(slot, 28, ((state & NisRuns) != 0 && Sharpness > 0f ? 1 : 0) | ((state & VibranceRuns) != 0 && Vibrance ? 2 : 0));
            values[0] = Sharpness;
            values[1] = Settings.DynamicVibranceIntensity;
            values[2] = Settings.DynamicVibranceSaturation;
            Marshal.Copy(values, 0, slot + 32, 3);
            return slot;
        }

        public static string PictureHelp(string help)
        {
            if (NeedsRestart) return help + " " + X("frameGenerationRestart", "Restart the game to turn it on.");
            if (Bound && info.pictureErrors >= 8) return help + " " + X("neuralRenderingStopped", "It failed to run and was stopped for this session.");
            return help;
        }

        // ---- neural rendering ("DLSS 5") --------------------------------------------------------------------------------

        /// <summary>DLSS neural rendering runs on this GPU (NVIDIA's check, once Streamline has the device: RTX 50 and up).</summary>
        public static bool NeuralSupported => Bound && (state & NeuralRuns) != 0;

        /// <summary>Its options are shown: its two DLLs are beside the plugin and the GPU is not known to lack it. Before
        /// Streamline has run here the card's name decides (GeForce GTX, RTX 20 / 30 / 40 don't run it).</summary>
        public static bool NeuralOffered
        {
            get
            {
                if (!Offered || (state & NeuralPresent) == 0) return false;
                if (Bound) return (state & NeuralRuns) != 0;
                return !System.Text.RegularExpressions.Regex.IsMatch(SystemInfo.graphicsDeviceName ?? "", @"GTX|RTX\s*[234]0\d0|RTX\s*A?\d000\b");
            }
        }

        /// <summary>NeuralRenderingPass should run on this frame's camera (off after repeated failures).</summary>
        public static bool WantsNeural => NeuralSupported && NeuralSetting && info.neuralErrors < 8;

        /// <summary>FrameGenerationPass should tag the back buffer for DLSS-G this frame.</summary>
        public static bool WantsFrames => Wanted(out _) > 0;

        /// <summary>The generated frames per rendered one to ask for now (0 = off), and whether the driver picks (dynamic).</summary>
        static int Wanted(out bool dynamic)
        {
            int setting = FrameSetting;
            dynamic = setting == Settings.FrameGenerationDynamic && DynamicSupported;
            if (!Running && !FsrRunning || setting <= 0 || frame < pausedUntil || menus.Count > 0 && !ignoreMenus) return 0;
            if (FsrRunning) return 1;   // (FSR: one generated frame for each rendered one)
            return dynamic ? MaxFrames : Mathf.Clamp(setting, 1, MaxFrames);
        }

        static uint pausedUntil;
        static readonly System.Collections.Generic.HashSet<string> menus = new System.Collections.Generic.HashSet<string>();

        /// <summary>A full-screen menu opened or closed (the pause menu, the station's system menu, a main menu panel): no
        /// generated frames while one is up (Streamline's guide, "Disabling frame generation": menus are mostly still
        /// interface, which interpolation only harms). DLSS-G keeps its resources, so it is back at once.</summary>
        public static void SetMenu(string menu, bool open)
        {
            if (open) menus.Add(menu); else menus.Remove(menu);
        }

        /// <summary>A menu holds frame generation off right now.</summary>
        public static bool MenuOpen => menus.Count > 0;

        // (the Editor's Play mode keeps the statics: a session left with a menu open left its name here)
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetMenus() => menus.Clear();

        /// <summary>No generated frames for the next 'frames' frames: the swap chain is about to change (HDR on or off), and
        /// DLSS-G must be off across that (Streamline's guide, "DLSS-G and DXGI").</summary>
        public static void Pause(int frames)
        {
            if (hooked) pausedUntil = frame + (uint)Mathf.Max(0, frames);
        }

        /// <summary>The frame's camera has something for Streamline (FrameGenerationPass enqueues its passes).</summary>
        public static bool WantsTags => WantsFrames || WantsNeural || WantsPicture;

        public static void Install()
        {
            if (installed) return;
            installed = true;
            try { state = Gof2Sl_State(); plugin = true; }
            catch (Exception e) when (e is DllNotFoundException || e is EntryPointNotFoundException) { plugin = false; return; }

            if ((state & Latched) != 0)
            {
                // The last start with Streamline never reached the menu: the plugin stayed out this time, and the options go off.
                Debug.LogWarning("Frame generation: the last start with it did not finish, so it and Reflex are turned off.");
                Settings.FrameGeneration = 0;
                Settings.Reflex = 0;
            }
            SyncArmed();
            Settings.Changed -= SyncArmed;
            Settings.Changed += SyncArmed;
            Debug.Log($"Frame generation: plugin state {state} ({StateText()})");
            limitOverride = EnvInt("GOF2_FRAMELIMIT", 1000);
            if ((state & (Armed | FsrArmed)) == 0)
            {
                // AMD Anti-Lag 2 needs none of Streamline: the frame's first step alone.
                if (AntiLagOffered) HookPlayerLoop(false);
                return;
            }
            frameOverride = EnvInt("GOF2_FRAMEGEN", Settings.FrameGenerationDynamic);
            reflexOverride = EnvInt("GOF2_REFLEX", 2);
            neuralOverride = EnvInt("GOF2_NEURAL", 1);
            uiLayer = Environment.GetEnvironmentVariable("GOF2_FRAMEGEN_UI") != "0";
            sharpnessOverride = EnvInt("GOF2_NIS", 100);
            vibranceOverride = EnvInt("GOF2_VIBRANCE", 1);
            ignoreMenus = Environment.GetEnvironmentVariable("GOF2_FRAMEGEN_MENUS") == "1";   // (testing: frames behind a menu too)
            verbose = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GOF2_SL_VERBOSE"));

            // (a scene left with a menu open: its menu is gone with it)
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += (_, _) => menus.Clear();
            eventFunc = Gof2Sl_EventFunc();
            ring = Marshal.AllocHGlobal(SlotSize * RingSlots);
            endCommands = new CommandBuffer { name = "Frame generation: frame end" };
            HookPlayerLoop(true);
            Visuals.FrameGenerationPass.Install();
        }

        /// <summary>The registry flag the plugin reads at the next start.</summary>
        static void SyncArmed()
        {
            if (!plugin) return;
            Gof2Sl_SetArmed(Settings.FrameGeneration > 0 || Settings.Reflex > 0 || Settings.NeuralRendering
                || Settings.NisSharpness > 0f || Settings.DynamicVibrance ? 1 : 0);
            try { Gof2Sl_SetProvider(WantsFsr ? 1 : 0); }
            catch (EntryPointNotFoundException) { }   // (a plugin built before FSR frame generation)
        }

        static string StateText()
        {
            if ((state & FsrArmed) != 0) return "FSR frame generation" + ((state & FsrSwapChain) != 0 ? ", swap chain attached" : ", swap chain not attached");
            if ((state & FilesPresent) == 0) return "Streamline's DLLs are not in the build" + ((state & FsrPresent) != 0 ? " (FSR frame generation's are)" : "");
            if ((state & Armed) == 0) return (state & Latched) != 0 ? "held back after a failed start" : "not started (the options are off, or were at the start)";
            if ((state & LateLoad) != 0) return "started too late for Unity's device";
            if ((state & DeviceBound) == 0) return "started, no device yet";
            return "started" + ((state & SwapChainProxied) != 0 ? ", swap chain attached" : ", swap chain not attached")
                + ((state & Supported) != 0 ? ", DLSS-G supported" : ", DLSS-G not supported (" + info.supportResult + ")")
                + ((state & NeuralPresent) == 0 ? "" : (state & NeuralRuns) != 0 ? ", neural rendering supported" : ", neural rendering not supported");
        }

        // ---- the frame's steps ------------------------------------------------------------------------------------------

        static void HookPlayerLoop(bool streamline)
        {
            if (hooked) return;
            hooked = true;
            var loop = PlayerLoop.GetCurrentPlayerLoop();
            var top = new System.Collections.Generic.List<PlayerLoopSystem>(loop.subSystemList);
            // First thing in a frame, ahead of the time update: Reflex decides here how long the frame waits.
            top.Insert(0, new PlayerLoopSystem { type = typeof(FrameStartStep), updateDelegate = FrameStart });
            for (int i = 0; streamline && i < top.Count; i++)
            {
                if (top[i].type != typeof(PostLateUpdate)) continue;
                var sub = new System.Collections.Generic.List<PlayerLoopSystem>(top[i].subSystemList);
                int render = sub.FindIndex(s => s.type == typeof(PostLateUpdate.FinishFrameRendering));
                if (render < 0) break;
                sub.Insert(render + 1, new PlayerLoopSystem { type = typeof(FrameEndStep), updateDelegate = FrameEnd });
                sub.Insert(render, new PlayerLoopSystem { type = typeof(SimulationEndStep), updateDelegate = SimulationEnd });
                var system = top[i];
                system.subSystemList = sub.ToArray();
                top[i] = system;
                break;
            }
            loop.subSystemList = top.ToArray();
            PlayerLoop.SetPlayerLoop(loop);
        }

        static void FrameStart()
        {
            frame++;
            if ((frame & 31) == 1)
            {
                Gof2Sl_GetInfo(out info);
                if (state != info.state) { state = info.state; Debug.Log($"Frame generation: plugin state {state} ({StateText()})"); }
                if (info.presentedPerFrame > 0f) presented = info.presentedPerFrame;
                // The game runs with Streamline in: this start is not held against it.
                if (!startReported && frame > 120) { startReported = true; Gof2Sl_StartedOk(); }
                if (verbose && (frame & 511) == 1)
                    Debug.Log($"Frame generation: wanted {Wanted(out bool dyn)}{(dyn ? " (dynamic)" : "")}, generating {info.generating}, "
                        + $"shown per frame {presented:0.00}, DLSS-G status {info.dlssgStatus}, max {info.maxFramesToGenerate}, dynamic {info.dynamicSupported}, "
                        + $"vsync {info.vsyncSupported}, tag errors {info.tagErrors}, vSyncCount {QualitySettings.vSyncCount}, "
                        + $"neural {(NeuralSupported ? "supported" : "no")} runs {info.neuralRuns} errors {info.neuralErrors}, "
                        + $"picture filters runs {info.pictureRuns} errors {info.pictureErrors}, "
                        + $"HDR available {Bootstrap.HdrAvailable} active {Bootstrap.HdrActive}, "
                        + $"fps {1f / Mathf.Max(Time.unscaledDeltaTime, 1e-4f):0}, last: {LastError()}");
            }
            // DLSS-G needs Reflex on; the sleep and the markers are sent in every mode.
            int reflex = Mathf.Max(ReflexSetting, Wanted(out _) > 0 ? 1 : 0);
            long waitFrom = System.Diagnostics.Stopwatch.GetTimestamp();
            bool limits = Gof2Sl_FrameStart(frame, reflex, FrameLimitUs()) != 0;
            // AMD Anti-Lag 2 (an AMD GPU: no Reflex there): its delay goes here too, ahead of the frame's input, and its
            // limiter takes the frame rate cap the same way.
            if ((state & AntiLagBit) != 0)
                limits |= Gof2Sl_AntiLag(Settings.AntiLag ? 1 : 0, (uint)Mathf.Max(0, limitOverride >= 0 ? limitOverride : Bootstrap.FrameRateCap)) != 0;
            StartWaitMs = (System.Diagnostics.Stopwatch.GetTimestamp() - waitFrom) * 1000f / System.Diagnostics.Stopwatch.Frequency;
            if (limits != ReflexLimits)
            {
                // The driver's limiter took the frame rate cap (or gave it back): Unity's own goes off (or on again).
                ReflexLimits = limits;
                Bootstrap.RefreshFrameRate();
            }
        }

        /// <summary>How long this frame's start took in Reflex's sleep (or Anti-Lag 2's), ms: the driver holds the main
        /// thread there until the GPU can take the frame (and for its frame rate cap), so that time is a wait, not the
        /// game's work, though Unity counts it in the main thread's frame time. 0 without either.</summary>
        public static float StartWaitMs { get; private set; }

        /// <summary>Reflex's (or Anti-Lag 2's) limiter holds the frame rate cap now (Bootstrap.Limit then sets none of Unity's).</summary>
        public static bool ReflexLimits { get; private set; }

        /// <summary>AMD Anti-Lag 2 is there (an AMD GPU whose driver has it, under D3D12): its option is shown.</summary>
        public static bool AntiLagOffered => plugin && (state & AntiLagBit) != 0;

        /// <summary>The frame rate option's cap as Reflex's limiter takes it: the rendered frame's time in microseconds; 0
        /// without a cap (V-Sync, uncapped) or without Streamline. ($GOF2_FRAMELIMIT: a cap in frames a second, for testing.)</summary>
        static uint FrameLimitUs()
        {
            if (!Bound || Benchmark.Active) return 0;   // (a benchmark run: no limit)
            int fps = limitOverride >= 0 ? limitOverride : Bootstrap.FrameRateCap;
            return fps > 0 ? (uint)(1000000 / fps) : 0u;
        }

        /// <summary>The PC latency Reflex measured lately (simulation start to the end of the GPU's work), in milliseconds;
        /// 0 when it reports none. The frame rate counter shows it.</summary>
        public static float LatencyMs => Bound ? info.latencyMs : 0f;

        static void SimulationEnd() => Gof2Sl_SimulationEnd(frame);

        static void FrameEnd()
        {
            int frames = Wanted(out bool dynamic);
            IntPtr slot = NextSlot();
            Marshal.WriteInt32(slot, 0, (int)frame);
            Marshal.WriteInt32(slot, 4, frames <= 0 ? 0 : dynamic ? 2 : 1);
            Marshal.WriteInt32(slot, 8, Mathf.Max(1, frames));
            Marshal.WriteInt32(slot, 12, taggedFrame == frame ? 1 : 0);
            values[0] = Settings.FrameGenerationTarget;   // dynamic: the frame rate to reach, 0 = the display's
            Marshal.Copy(values, 0, slot + 16, 1);
            Marshal.WriteInt32(slot, 20, uiFrame == frame ? 1 : 0);   // the interface's alpha is tagged: recomposition
            endCommands.Clear();
            endCommands.IssuePluginEventAndData(eventFunc, EventFrameEnd, slot);
            Graphics.ExecuteCommandBuffer(endCommands);
        }

        static IntPtr NextSlot()
        {
            IntPtr slot = ring + SlotSize * ringNext;
            ringNext = (ringNext + 1) % RingSlots;
            return slot;
        }

        // ---- the tag event ----------------------------------------------------------------------------------------------

        /// <summary>A Unity matrix (column vectors) as Streamline's (row-major, row vectors): its rows are our columns.</summary>
        static void Put(int at, in Matrix4x4 m)
        {
            for (int i = 0; i < 4; i++)
            {
                var c = m.GetColumn(i);
                values[at + i * 4] = c.x; values[at + i * 4 + 1] = c.y; values[at + i * 4 + 2] = c.z; values[at + i * 4 + 3] = c.w;
            }
        }

        static void Put(int at, Vector3 v) { values[at] = v.x; values[at + 1] = v.y; values[at + 2] = v.z; }

        /// <summary>The tag event's data for this frame's camera (main thread, while its passes are recorded): our copies
        /// of the depth and the motion vectors, the camera's matrices with the frame before. Returns the block for
        /// IssuePluginEventAndData and counts the frame as tagged.</summary>
        public static IntPtr TagInputs(IntPtr depth, IntPtr motion, int width, int height, bool reset, in Matrix4x4 viewToClip,
            in Matrix4x4 clipToPrevClip, Vector2 jitter, Camera camera, bool hudless, IntPtr ui = default)
        {
            Put(0, viewToClip);
            Put(16, viewToClip.inverse);
            Put(32, clipToPrevClip);
            Put(48, clipToPrevClip.inverse);
            values[64] = jitter.x; values[65] = jitter.y;
            // The motion vectors are current minus previous in the copy's texture coordinates (FrameGenInputs.shader); DLSS
            // wants previous minus current.
            values[66] = -1f; values[67] = -1f;
            var t = camera.transform;
            Put(68, t.position); Put(71, t.up); Put(74, t.right); Put(77, t.forward);
            values[80] = camera.nearClipPlane; values[81] = camera.farClipPlane;
            values[82] = camera.fieldOfView * Mathf.Deg2Rad; values[83] = camera.aspect;

            IntPtr slot = NextSlot();
            Marshal.WriteIntPtr(slot, 0, depth);
            Marshal.WriteIntPtr(slot, 8, motion);
            Marshal.WriteInt32(slot, 16, (int)frame);
            Marshal.WriteInt32(slot, 20, width);
            Marshal.WriteInt32(slot, 24, height);
            // 4: the constants and the depth / motion tags; 2: the back buffer as the HUD-less colour (DLSS-G, once it is drawn)
            Marshal.WriteInt32(slot, 28, (reset ? 1 : 0) | 4 | (hudless && WantsFrames ? 2 : 0));
            Marshal.Copy(values, 0, slot + 32, TagValues);
            Marshal.WriteIntPtr(slot, UiOffset, hudless ? ui : IntPtr.Zero);
            if (hudless) taggedFrame = frame;
            if (hudless && ui != IntPtr.Zero) uiFrame = frame;
            return slot;
        }

        /// <summary>The tag event's data for the HUD-less colour alone (the frame's inputs were tagged earlier, by
        /// NeuralRenderingPass, before the picture was on the back buffer).</summary>
        public static IntPtr TagHudless(IntPtr ui = default)
        {
            IntPtr slot = NextSlot();
            Marshal.WriteIntPtr(slot, 0, IntPtr.Zero);
            Marshal.WriteIntPtr(slot, 8, IntPtr.Zero);
            Marshal.WriteInt32(slot, 16, (int)frame);
            Marshal.WriteInt32(slot, 28, 2);
            Marshal.WriteIntPtr(slot, UiOffset, ui);
            taggedFrame = frame;
            if (ui != IntPtr.Zero) uiFrame = frame;
            return slot;
        }

        /// <summary>The neural rendering event's data (the plugin's NeuralData) for a picture of this size, with the
        /// options; FillNeural adds the two textures when the pass runs.</summary>
        public static IntPtr NeuralData(int width, int height)
        {
            IntPtr slot = NextSlot();
            Marshal.WriteIntPtr(slot, 0, IntPtr.Zero);
            Marshal.WriteIntPtr(slot, 8, IntPtr.Zero);
            Marshal.WriteInt32(slot, 16, (int)frame);
            Marshal.WriteInt32(slot, 20, width);
            Marshal.WriteInt32(slot, 24, height);
            Marshal.WriteInt32(slot, 28, 0);
            values[0] = Settings.NeuralRenderingIntensity;
            values[1] = Settings.NeuralRenderingTone;
            values[2] = Settings.NeuralRenderingStructure;
            values[3] = Settings.NeuralRenderingSkin;
            Marshal.Copy(values, 0, slot + 32, 4);
            Marshal.WriteInt32(slot, 48, Mathf.Clamp(Settings.NeuralRenderingQuality, 0, 2) + 1);   // a DLSS mode: 1 performance .. 3 quality
            return slot;
        }

        public static void FillNeural(IntPtr slot, IntPtr input, IntPtr output)
        {
            Marshal.WriteIntPtr(slot, 0, input);
            Marshal.WriteIntPtr(slot, 8, output);
        }

        public static IntPtr EventFunc => eventFunc;
        public static bool TaggedThisFrame => taggedFrame == frame;

        // ---- the options' texts -------------------------------------------------------------------------------------------

        /// <summary>The Frame generation row's choices: Off, 2x .. what the GPU runs, Dynamic where the driver can.</summary>
        public static string[] Choices()
        {
            if (UsesFsr) return new[] { X("off", "Off"), "2×" };
            int max = Mathf.Max(MaxFrames, Mathf.Min(Settings.FrameGeneration, 5));
            var list = new System.Collections.Generic.List<string> { X("off", "Off") };
            for (int i = 1; i <= max; i++) list.Add((i + 1) + "×");
            if (DynamicSupported || Settings.FrameGeneration == Settings.FrameGenerationDynamic) list.Add(X("frameGenerationDynamic", "Dynamic"));
            return list.ToArray();
        }

        public static int ChoiceIndex()
        {
            int setting = Settings.FrameGeneration, count = Choices().Length;
            return setting == Settings.FrameGenerationDynamic ? count - 1 : Mathf.Clamp(setting, 0, count - 1);
        }

        public static void SetChoice(int index)
        {
            if (UsesFsr) { Settings.FrameGeneration = Mathf.Clamp(index, 0, 1); return; }
            var choices = Choices();
            bool dynamicRow = DynamicSupported || Settings.FrameGeneration == Settings.FrameGenerationDynamic;
            Settings.FrameGeneration = dynamicRow && index == choices.Length - 1 ? Settings.FrameGenerationDynamic : Mathf.Clamp(index, 0, 5);
        }

        /// <summary>Why frame generation can't run here, or null when it can (or it is not known yet).</summary>
        static string Problem()
        {
            if ((state & Latched) != 0) return X("frameGenerationFailed", "The game did not start with it the last time, so it was turned off.");
            if ((state & FsrArmed) != 0)
                return (state & FsrSwapChain) == 0 ? X("frameGenerationNoAttach", "It could not attach to the game's display output.") : null;
            if ((state & Armed) == 0) return null;
            if ((state & DeviceBound) == 0 || (state & Supported) != 0 && (state & SwapChainProxied) == 0)
                return X("frameGenerationNoAttach", "It could not attach to the game's display output.");
            if ((state & Supported) != 0) return null;
            string reason = info.supportResult switch
            {
                ResultDriverOutOfDate => X("frameGenerationDriver", "the NVIDIA driver is too old"),
                ResultOsOutOfDate => X("frameGenerationWindows", "Windows is too old"),
                ResultOsDisabledHws => X("frameGenerationScheduling", "\"Hardware-accelerated GPU scheduling\" is off in Windows' graphics settings"),
                ResultNoSupportedAdapter or ResultAdapterNotSupported => X("frameGenerationGpu", "it needs a GeForce RTX 40 series card or newer"),
                _ => "(" + info.supportResult + ")",
            };
            return string.Format(X("frameGenerationUnsupported", "It does not run on this system: {0}."), reason);
        }

        public static string Help()
        {
            string help = UsesFsr
                ? X("frameGenerationFsrHelp", "AMD FSR Frame Generation: made-up frames between the rendered ones, for a smoother picture. Best from 60 rendered frames a second up.")
                : X("frameGenerationHelp", "NVIDIA DLSS Frame Generation: AI-made frames between the rendered ones, for a smoother picture. Best from 60 rendered frames a second up.");
            if (Settings.FrameGeneration > 0 && !NeedsRestart && Problem() is string problem) return help + " " + problem;
            if (NeedsRestart && Settings.FrameGeneration > 0) return help + " " + X("frameGenerationRestart", "Restart the game to turn it on.");
            // (the options screens are menus themselves: turning it on there showed nothing in the frame rate, a player's
            // "nothing changes when I press Apply", 2026-11-20)
            if (Settings.FrameGeneration > 0 && (Running || FsrRunning) && MenuOpen && !ignoreMenus)
                return help + " " + X("frameGenerationMenuPause", "It pauses while a menu is open: its extra frames show in flight and in the hangar, not here.");
            if (Generating) return help + " " + string.Format(X("frameGenerationNow", "Now: {0:0.0} frames shown for each rendered one."), PresentedPerFrame);
            return help;
        }

        public static string ReflexHelp()
        {
            string help = X("reflexHelp", "NVIDIA Reflex Low Latency: less delay between your input and the picture. Boost keeps the GPU clocked up for a little less still.");
            if (Settings.Reflex > 0 && (state & Latched) != 0) return help + " " + X("frameGenerationFailed", "The game did not start with it the last time, so it was turned off.");
            if (WantsFsr || FsrActive && Settings.FrameGeneration > 0) return help + " " + X("reflexNotWithFsr", "Not together with AMD FSR frame generation.");
            if (NeedsRestart && Settings.Reflex > 0) return help + " " + X("frameGenerationRestart", "Restart the game to turn it on.");
            if (Bound && !ReflexAvailable && frame > 60) return help + " " + X("reflexUnavailable", "Not available on this GPU or driver.");
            if (Settings.Reflex == 0 && Settings.FrameGeneration > 0 && Running) return help + " " + X("reflexByFrameGeneration", "Frame generation keeps it on.");
            return help;
        }

        public static string NeuralHelp()
        {
            string help = X("neuralRenderingHelp", "NVIDIA DLSS neural rendering (DLSS 5): an AI model reworks the lighting and materials of the finished picture. GeForce RTX 50 series and newer. Not with HDR output.");
            if (NeedsRestart && Settings.NeuralRendering) return help + " " + X("frameGenerationRestart", "Restart the game to turn it on.");
            if (NeuralSupported && Settings.NeuralRendering && info.neuralErrors >= 8) return help + " " + X("neuralRenderingStopped", "It failed to run and was stopped for this session.");
            return help;
        }

        public static string LastError()
        {
            if (!plugin) return "";
            IntPtr text = Gof2Sl_LastError();
            return text == IntPtr.Zero ? "" : Marshal.PtrToStringAnsi(text) ?? "";
        }
    }
}
