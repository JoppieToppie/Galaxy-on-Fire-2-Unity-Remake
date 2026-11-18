// LinuxFsrUpscaler.cs
// Feature: docs/notes/options.md "FSR on Linux": AMD FSR 4 / 3 / 2 in Linux players on Vulkan, through URP's upscaler
// framework like the Windows build's (Unity's AMD module, Windows only) and under the same ids (amd.fsr4 / fsr3 / fsr2), so
// UpscalerFramework and the Upscaler option treat them alike. The work is done by the native plugin libgof2_fsr.so
// (Native/FsrLinux): preloaded, it raises Unity's Vulkan version and device features for FSR; it runs AMD's FidelityFX
// Vulkan DLLs natively with ffx-vk-linux. The DLLs are the player's: amd_fidelityfx_loader_vk.dll and
// amd_fidelityfx_upscaler_vk.dll from AMD's FidelityFX SDK (FSR 3 / 2), plus amdvkffx64.dll from AMD's Windows driver for FSR
// 4, in GamePaths.Data/FidelityFX ($FFXVK_DLL_DIR elsewhere). Without them, another graphics API or a GPU without Vulkan 1.2,
// none is supported and the option offers no FSR.
// Contexts and dispatches are plugin events on the render graph's command buffer (create / dispatch / destroy), with
// their data in unmanaged blocks the render thread reads later (a ring: a few frames of slack).
// Windows on Vulkan too (gof2_fsr.dll: AMD's Vulkan DLLs loaded as they are; a comparison with the Linux path, e.g. under
// Proton), under the ids ffxvk.fsr4 / fsr3 / fsr2 there (Unity's AMD module has amd.*).

#if (UNITY_STANDALONE_LINUX || UNITY_STANDALONE_WIN) && ENABLE_UPSCALER_FRAMEWORK
using System;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace GoF2Remake
{
    /// <summary>The native plugin: AMD's DLLs loaded once, the providers, the events.</summary>
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]   // (the native state lives as long as the process)
    public static class LinuxFsr
    {
        const string Lib = "gof2_fsr";
        public const int EventCreate = 0x46535201, EventDispatch = 0x46535202, EventDestroy = 0x46535203;

        [DllImport(Lib)] static extern int Gof2Fsr_Init(string directory);
        [DllImport(Lib)] static extern IntPtr Gof2Fsr_Provider(int major);
        [DllImport(Lib)] static extern IntPtr Gof2Fsr_Status();
        [DllImport(Lib)] static extern IntPtr Gof2Fsr_EventFunc();
        [DllImport(Lib)] static extern void Gof2Fsr_Timings(out double gpuMs, out double cpuMs, out int samples, int reset);
        [DllImport(Lib)] static extern int Gof2Fsr_LiteAvailable();

        // The main thread's part (the render function: the context, the texture pointers, the event), for TimingReport.
        static readonly System.Diagnostics.Stopwatch mainWatch = new System.Diagnostics.Stopwatch();
        static int mainCalls;
        public static void MainBegin() => mainWatch.Start();
        public static void MainEnd() { mainWatch.Stop(); mainCalls++; }

        /// <summary>"FSR GPU x ms, render thread y ms, main thread z ms" per dispatch since the last reset ("" without dispatches).</summary>
        public static string TimingReport(bool reset)
        {
            try
            {
                Gof2Fsr_Timings(out double gpu, out double cpu, out int n, reset ? 1 : 0);
                double main = mainCalls > 0 ? mainWatch.Elapsed.TotalMilliseconds / mainCalls : 0;
                if (reset) { mainWatch.Reset(); mainCalls = 0; }
                return n == 0 ? "" : $"FSR GPU {gpu:0.000} ms, render thread {cpu:0.000} ms, main thread {main:0.000} ms per dispatch";
            }
            catch (Exception e) when (e is DllNotFoundException || e is EntryPointNotFoundException) { return ""; }
        }

        /// <summary>A render texture's native texture (the depth one's render buffer). GetNativeTexturePtr waits for the render
        /// thread; caching the pointers saved nothing measurable (the main thread's part is ~0.01 ms a frame) and a texture
        /// released and made again at the same size would leave a stale one.</summary>
        public static IntPtr Ptr(RenderTexture rt, bool depthBuffer = false) =>
            rt == null ? IntPtr.Zero : depthBuffer ? rt.depthBuffer.GetNativeRenderBufferPtr() : rt.GetNativeTexturePtr();

        [StructLayout(LayoutKind.Sequential, Pack = 8)]
        public struct CreateData
        {
            public int handle, major;
            public uint maxRenderWidth, maxRenderHeight, displayWidth, displayHeight, flags;
            public int lite;   // FSR 4: 1 = FSR 4 Lite
        }

        [StructLayout(LayoutKind.Sequential, Pack = 8)]
        public struct DispatchData
        {
            public int handle, reset;
            public IntPtr color, depth, depthBuffer, motion, output;
            public float jitterX, jitterY, motionScaleX, motionScaleY;
            public uint renderWidth, renderHeight, upscaleWidth, upscaleHeight;
            public int sharpen;
            public float sharpness, frameTimeMs, preExposure, cameraNear, cameraFar, fovVertical;
        }

        // FFX_UPSCALE_ENABLE_*
        public const uint FlagHdr = 1 << 0, FlagDisplayResMotionVectors = 1 << 1, FlagJitteredMotionVectors = 1 << 2,
            FlagDepthInverted = 1 << 3, FlagDynamicResolution = 1 << 6;

        static bool tried, ready, liteAvailable;
        static string[] providers = new string[5];
        static IntPtr eventFunc, ring;
        static int ringNext;
        const int RingSlots = 512;
        static readonly int SlotSize = Math.Max(Marshal.SizeOf<CreateData>(), Marshal.SizeOf<DispatchData>());
        static readonly bool[] handles = new bool[64];

        /// <summary>The DLLs' folder: $FFXVK_DLL_DIR, else GamePaths.Data/FidelityFX.</summary>
        public static string DllFolder
        {
            get
            {
                string env = Environment.GetEnvironmentVariable("FFXVK_DLL_DIR");
                return !string.IsNullOrEmpty(env) ? env : Path.Combine(GamePaths.Data, "FidelityFX");
            }
        }

        /// <summary>Loads AMD's DLLs (once; Unity's Vulkan device exists by then) and logs what runs.</summary>
        public static void Init()
        {
            if (tried) return;
            tried = true;
            try
            {
                string dir = DllFolder;
                ready = Gof2Fsr_Init(dir) != 0;
                for (int major = 2; major <= 4; major++) providers[major] = Marshal.PtrToStringAnsi(Gof2Fsr_Provider(major));
                eventFunc = Gof2Fsr_EventFunc();
                try { liteAvailable = ready && Gof2Fsr_LiteAvailable() != 0; }
                catch (EntryPointNotFoundException) { liteAvailable = false; }   // (a plugin built before FSR 4 Lite)
                Debug.Log($"FSR (Linux): {Status} ({dir})");
            }
            catch (Exception e) when (e is DllNotFoundException || e is EntryPointNotFoundException)
            {
                Debug.Log("FSR (Linux): no plugin (" + e.Message + ")");
                ready = false;
            }
        }

        /// <summary>The plugin's own account: Vulkan version and features, the DLLs, the providers.</summary>
        public static string Status
        {
            get
            {
                try { return Marshal.PtrToStringAnsi(Gof2Fsr_Status()); }
                catch (Exception e) when (e is DllNotFoundException || e is EntryPointNotFoundException) { return "no plugin"; }
            }
        }

        /// <summary>The provider of FSR 'major' (4 / 3 / 2) this device runs, e.g. "4.1.2"; null when none.</summary>
        public static string Provider(int major) => ready && !string.IsNullOrEmpty(providers[major]) ? providers[major] : null;

        /// <summary>FSR 4 Lite can be asked for (the Graphics option "FSR 4 Lite"): FSR 4 runs here through ffx-vk-linux,
        /// which keeps an instance of AMD's driver DLL per model. Linux only.</summary>
        public static bool LiteAvailable => liteAvailable && Provider(4) != null;

        public static int NewHandle()
        {
            for (int i = 0; i < handles.Length; i++) if (!handles[i]) { handles[i] = true; return i; }
            return -1;
        }

        public static void Issue<T>(CommandBuffer cmd, int eventId, T data) where T : struct
        {
            if (ring == IntPtr.Zero) ring = Marshal.AllocHGlobal(SlotSize * RingSlots);
            IntPtr slot = ring + SlotSize * ringNext;
            ringNext = (ringNext + 1) % RingSlots;
            Marshal.StructureToPtr(data, slot, false);
            cmd.IssuePluginEventAndData(eventFunc, eventId, slot);
        }

        public static void Destroy(CommandBuffer cmd, int handle)
        {
            if (handle < 0) return;
            Issue(cmd, EventDestroy, new CreateData { handle = handle });   // (the native side reads the handle only)
            handles[handle] = false;
        }
    }

    /// <summary>One camera's FSR context: made on first use, again when the render size limit or flags change.</summary>
    public sealed class LinuxFsrContext : IUpscalerContext
    {
        readonly int major, quality;
        readonly bool lite;   // FSR 4 Lite (the model is the context's: a changed option makes a new one)
        int handle = -1;
        LinuxFsr.CreateData made;

        public LinuxFsrContext(int major, LinuxFsrOptions options, Vector2Int displayResolution)
        {
            this.major = major;
            quality = options.qualityMode;
            lite = major == 4 && options.lite && LinuxFsr.LiteAvailable;
            createdForDisplayResolution = displayResolution;
        }

        public Vector2Int createdForDisplayResolution { get; }
        public int lastUsedFrame { get; set; }
        public bool IsValidForOptions(UpscalerOptions options) =>
            options is LinuxFsrOptions o && o.qualityMode == quality && (major == 4 && o.lite && LinuxFsr.LiteAvailable) == lite;

        public void Cleanup(CommandBuffer cmd)
        {
            LinuxFsr.Destroy(cmd, handle);
            handle = -1;
        }

        /// <summary>The native context for 'want' (a new one when its sizes or flags differ); -1 when none is free.</summary>
        public int Ensure(CommandBuffer cmd, LinuxFsr.CreateData want)
        {
            if (handle >= 0 && made.maxRenderWidth == want.maxRenderWidth && made.maxRenderHeight == want.maxRenderHeight &&
                made.displayWidth == want.displayWidth && made.displayHeight == want.displayHeight && made.flags == want.flags)
                return handle;
            Cleanup(cmd);
            handle = LinuxFsr.NewHandle();
            if (handle < 0) return -1;
            want.handle = handle;
            want.major = major;
            want.lite = lite ? 1 : 0;
            LinuxFsr.Issue(cmd, LinuxFsr.EventCreate, want);
            made = want;
            return handle;
        }
    }

    /// <summary>FSR 'Major' on Linux Vulkan (LinuxFsr's native plugin).</summary>
    public abstract class LinuxFsrUpscaler : AbstractUpscaler
    {
        protected abstract int Major { get; }

        public override string name => "FidelityFX Super Resolution " + Major + " (Linux)";
        public override bool isSupportedOnDevice => LinuxFsr.Provider(Major) != null;
        public override bool isTemporal => true;
        public override bool supportsSharpening => true;
        public override bool hasQualityMode => true;

        /// <summary>FSR's per-dimension ratio of a quality mode (FFX_UPSCALE_QUALITY_MODE_*).</summary>
        public static float Ratio(int quality) => quality switch { 0 => 1f, 1 => 1.5f, 2 => 1.7f, 3 => 2f, _ => 3f };

        public override IUpscalerContext CreateContext(UpscalerOptions options, Vector2Int displayResolution) =>
            isSupportedOnDevice && options is LinuxFsrOptions o ? new LinuxFsrContext(Major, o, displayResolution) : null;

        /// <summary>FSR's Halton (2, 3) pattern, 8 x ratio² phases (as FSRUtils.CalculateJitter).</summary>
        public override void CalculateJitter(int frameIndex, float upscaleRatio, out Vector2 jitter, out bool allowScaling)
        {
            allowScaling = false;
            int phases = Mathf.Max(1, Mathf.FloorToInt(8f * upscaleRatio * upscaleRatio + 0.5f));
            int i = frameIndex % phases + 1;
            jitter = new Vector2(HaltonSequence.Get(i, 2) - 0.5f, HaltonSequence.Get(i, 3) - 0.5f);
        }

        public override UpscalerResolutionInfo GetResolutionInfo(Vector2Int displayResolution, UpscalerOptions options)
        {
            if (!isSupportedOnDevice || options is not LinuxFsrOptions o) return UpscalerResolutionInfo.Fixed(displayResolution);
            if (o.resolutionMode == UpscalerResolutionMode.CustomScaling) return base.GetResolutionInfo(displayResolution, options);
            float r = Ratio(o.qualityMode);
            return UpscalerResolutionInfo.Fixed(new Vector2Int(Mathf.Max(1, (int)(displayResolution.x / r)), Mathf.Max(1, (int)(displayResolution.y / r))));
        }

        public override float CalculateMipBias(Vector2Int pre, Vector2Int post) =>
            Mathf.Min(Mathf.Log((float)pre.x / post.x, 2f), Mathf.Log((float)pre.y / post.y, 2f)) - 1f;

        sealed class PassData
        {
            public LinuxFsrContext context;
            public LinuxFsr.CreateData create;
            public LinuxFsr.DispatchData dispatch;
            public TextureHandle color, depth, motion, output;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var io = frameData.Get<UpscalingIO>();
            if (io.context is not LinuxFsrContext context) return;
            var options = io.options as LinuxFsrOptions;

            var desc = io.cameraColor.GetDescriptor(renderGraph);
            desc.width = io.postUpscaleResolution.x;
            desc.height = io.postUpscaleResolution.y;
            desc.format = UnityEngine.Experimental.Rendering.GraphicsFormatUtility.GetLinearFormat(desc.format);
            desc.msaaSamples = MSAASamples.None;
            desc.useMipMap = false;
            desc.autoGenerateMips = false;
            desc.useDynamicScale = false;
            desc.anisoLevel = 0;
            desc.discardBuffer = false;
            desc.enableRandomWrite = true;
            desc.clearBuffer = false;
            desc.filterMode = FilterMode.Bilinear;
            desc.name = "_FsrLinuxOutput";
            TextureHandle output = renderGraph.CreateTexture(desc);

            using (var builder = renderGraph.AddUnsafePass<PassData>("FSR " + Major + " (Linux)", out var pass, new ProfilingSampler("FSR Linux")))
            {
                bool displayResMv = io.motionVectorTextureSize == io.postUpscaleResolution;
                uint flags = 0;
                if (io.hdrInput) flags |= LinuxFsr.FlagHdr;
                if (displayResMv) flags |= LinuxFsr.FlagDisplayResMotionVectors;
                if (io.jitteredMotionVectors) flags |= LinuxFsr.FlagJitteredMotionVectors;
                if (io.invertedDepth) flags |= LinuxFsr.FlagDepthInverted;
                if (io.dynamicResolution.HasValue) flags |= LinuxFsr.FlagDynamicResolution;
                pass.context = context;
                pass.create = new LinuxFsr.CreateData
                {
                    maxRenderWidth = (uint)io.maxPreUpscaleResolution.x, maxRenderHeight = (uint)io.maxPreUpscaleResolution.y,
                    displayWidth = (uint)io.postUpscaleResolution.x, displayHeight = (uint)io.postUpscaleResolution.y, flags = flags,
                };
                // (as FSRUtils.PopulateExecutionData)
                float sign = io.motionVectorDirection == UpscalingIO.MotionVectorDirection.PreviousFrameToCurrentFrame ? -1f : 1f;
                bool ndc = io.motionVectorDomain == UpscalingIO.MotionVectorDomain.NDC;
                pass.dispatch = new LinuxFsr.DispatchData
                {
                    reset = io.resetHistory ? 1 : 0,
                    jitterX = io.subpixelJitter.x, jitterY = io.subpixelJitter.y,
                    motionScaleX = sign * (ndc ? io.motionVectorTextureSize.x : 1f), motionScaleY = sign * (ndc ? io.motionVectorTextureSize.y : 1f),
                    renderWidth = (uint)io.preUpscaleResolution.x, renderHeight = (uint)io.preUpscaleResolution.y,
                    upscaleWidth = (uint)io.postUpscaleResolution.x, upscaleHeight = (uint)io.postUpscaleResolution.y,
                    sharpen = options != null && options.sharpening ? 1 : 0, sharpness = options != null ? options.sharpness : 0f,
                    frameTimeMs = io.deltaTime * 1000f, preExposure = 1f,
                    cameraNear = io.nearClipPlane, cameraFar = io.farClipPlane, fovVertical = io.fieldOfViewDegrees * Mathf.Deg2Rad,
                };
                builder.UseTexture(io.cameraColor);
                builder.UseTexture(io.cameraDepth);
                builder.UseTexture(io.motionVectorColor);
                builder.UseTexture(output, AccessFlags.Write);
                pass.color = io.cameraColor;
                pass.depth = io.cameraDepth;
                pass.motion = io.motionVectorColor;
                pass.output = output;
                builder.AllowPassCulling(false);

                builder.SetRenderFunc((PassData d, UnsafeGraphContext ctx) =>
                {
                    LinuxFsr.MainBegin();
                    CommandBuffer cmd = CommandBufferHelpers.GetNativeCommandBuffer(ctx.cmd);
                    int handle = d.context.Ensure(cmd, d.create);
                    if (handle < 0) { LinuxFsr.MainEnd(); return; }
                    RenderTexture color = d.color, depth = d.depth, motion = d.motion, outRt = d.output;
                    var data = d.dispatch;
                    data.handle = handle;
                    data.color = LinuxFsr.Ptr(color);
                    data.depth = LinuxFsr.Ptr(depth);
                    data.depthBuffer = LinuxFsr.Ptr(depth, depthBuffer: true);
                    data.motion = LinuxFsr.Ptr(motion);
                    data.output = LinuxFsr.Ptr(outRt);
                    LinuxFsr.Issue(cmd, LinuxFsr.EventDispatch, data);
                    LinuxFsr.MainEnd();
                });
            }
            io.cameraColor = output;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Vulkan) return;
            LinuxFsr.Init();
            UpscalerRegistry.Register<LinuxFsr4Upscaler, LinuxFsrOptions>(LinuxFsr4Upscaler.Id, "FidelityFX Super Resolution 4");
            UpscalerRegistry.Register<LinuxFsr3Upscaler, LinuxFsrOptions>(LinuxFsr3Upscaler.Id, "FidelityFX Super Resolution 3");
            UpscalerRegistry.Register<LinuxFsr2Upscaler, LinuxFsrOptions>(LinuxFsr2Upscaler.Id, "FidelityFX Super Resolution 2");
        }
    }

    public sealed class LinuxFsr4Upscaler : LinuxFsrUpscaler
    {
#if UNITY_STANDALONE_WIN
        public const string Id = UpscalerFramework.VkFsr4;
#else
        public const string Id = UpscalerFramework.Fsr4;
#endif
        protected override int Major => 4;
        public override string upscalerId => Id;
    }

    public sealed class LinuxFsr3Upscaler : LinuxFsrUpscaler
    {
#if UNITY_STANDALONE_WIN
        public const string Id = UpscalerFramework.VkFsr3;
#else
        public const string Id = UpscalerFramework.Fsr3;
#endif
        protected override int Major => 3;
        public override string upscalerId => Id;
    }

    public sealed class LinuxFsr2Upscaler : LinuxFsrUpscaler
    {
#if UNITY_STANDALONE_WIN
        public const string Id = UpscalerFramework.VkFsr2;
#else
        public const string Id = UpscalerFramework.Fsr2;
#endif
        protected override int Major => 2;
        public override string upscalerId => Id;
    }
}
#endif
