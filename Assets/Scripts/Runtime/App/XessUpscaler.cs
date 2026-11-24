// XessUpscaler.cs
// Feature: docs/notes/options.md "XeSS": Intel XeSS Super Resolution in the Windows Direct3D 12 player, through URP's
// upscaler framework like DLSS and FSR (id intel.xess: UpscalerFramework and the Upscaler option treat it alike). XeSS-SR
// runs on any GPU with Shader Model 6.4, whoever made it, so it is the one AI upscaler every recent card has. The work
// is the native plugin gof2_xess.dll's (Native/XeSS) with Intel's libxess.dll beside it (the XeSS SDK's, copied into a
// build by DlssRedist where the builder has the SDK); without either, or on another graphics API, it is not offered.
// Contexts and dispatches are plugin events on the render graph's command buffer (create / dispatch / destroy), with
// their data in unmanaged blocks the render thread reads later (a ring: a few frames of slack), as LinuxFsrUpscaler's.

#if UNITY_STANDALONE_WIN && ENABLE_UPSCALER_FRAMEWORK
using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace GoF2Remake
{
    /// <summary>The native plugin: Intel's DLL loaded once, the events.</summary>
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]   // (the native state lives as long as the process)
    public static class Xess
    {
        const string Lib = "gof2_xess";
        public const int EventCreate = 0x58455301, EventDispatch = 0x58455302, EventDestroy = 0x58455303;
        // XESS_INIT_FLAG_*
        public const uint FlagHighResMotionVectors = 1 << 0, FlagInvertedDepth = 1 << 1, FlagLdrInput = 1 << 6, FlagJitteredMotionVectors = 1 << 7;

#if !UNITY_EDITOR
        [DllImport(Lib)] static extern int Gof2Xess_Init();
        [DllImport(Lib)] static extern IntPtr Gof2Xess_Status();
        [DllImport(Lib)] static extern int Gof2Xess_InputResolution(int quality, uint displayWidth, uint displayHeight, out uint width, out uint height);
        [DllImport(Lib)] static extern IntPtr Gof2Xess_EventFunc();
#else
        // (the Editor has no plugin: Unity gives one no command list there)
        static int Gof2Xess_Init() => throw new DllNotFoundException();
        static IntPtr Gof2Xess_Status() => IntPtr.Zero;
        static int Gof2Xess_InputResolution(int quality, uint displayWidth, uint displayHeight, out uint width, out uint height) { width = height = 0; return 0; }
        static IntPtr Gof2Xess_EventFunc() => IntPtr.Zero;
#endif

        [StructLayout(LayoutKind.Sequential, Pack = 8)]
        public struct CreateData
        {
            public int handle, quality;
            public uint displayWidth, displayHeight, flags;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 8)]
        public struct DispatchData
        {
            public int handle, reset;
            public IntPtr color, depth, depthBuffer, motion, output;
            public float jitterX, jitterY, motionScaleX, motionScaleY;
            public uint renderWidth, renderHeight;
        }

        static bool tried, ready;
        static IntPtr eventFunc, ring;
        static int ringNext;
        const int RingSlots = 256;
        static readonly int SlotSize = Math.Max(Marshal.SizeOf<CreateData>(), Marshal.SizeOf<DispatchData>());
        static readonly bool[] handles = new bool[64];

        /// <summary>XeSS runs on this device (the plugin and libxess.dll are in the build, Direct3D 12, a GPU it takes).</summary>
        public static bool Supported => ready;

        /// <summary>Loads Intel's DLL (once; Unity's device exists by then) and logs what it says.</summary>
        public static void Init()
        {
            if (tried) return;
            tried = true;
            try
            {
                ready = Gof2Xess_Init() != 0;
                eventFunc = Gof2Xess_EventFunc();
                Debug.Log("XeSS: " + (Marshal.PtrToStringAnsi(Gof2Xess_Status()) ?? ""));
            }
            catch (Exception e) when (e is DllNotFoundException || e is EntryPointNotFoundException)
            {
                ready = false;
            }
        }

        /// <summary>XeSS's own ratio per dimension of a quality mode, where the DLL can't be asked.</summary>
        static float Ratio(int quality) => quality switch { 0 => 1f, 1 => 1.7f, 2 => 2f, 3 => 2.3f, _ => 3f };

        /// <summary>The render resolution of a quality mode at this display resolution (XeSS's own answer).</summary>
        public static Vector2Int InputResolution(int quality, Vector2Int display)
        {
            try
            {
                if (ready && Gof2Xess_InputResolution(quality, (uint)display.x, (uint)display.y, out uint w, out uint h) != 0)
                    return new Vector2Int(Mathf.Clamp((int)w, 1, display.x), Mathf.Clamp((int)h, 1, display.y));
            }
            catch (EntryPointNotFoundException) { }
            float r = Ratio(quality);
            return new Vector2Int(Mathf.Max(1, (int)(display.x / r)), Mathf.Max(1, (int)(display.y / r)));
        }

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

        public static IntPtr Ptr(RenderTexture rt, bool depthBuffer = false) =>
            rt == null ? IntPtr.Zero : depthBuffer ? rt.depthBuffer.GetNativeRenderBufferPtr() : rt.GetNativeTexturePtr();
    }

    /// <summary>One camera's XeSS context: made on first use, again when the display size or its flags change.</summary>
    public sealed class XessContext : IUpscalerContext
    {
        readonly int quality;
        int handle = -1;
        Xess.CreateData made;

        public XessContext(XessOptions options, Vector2Int displayResolution)
        {
            quality = options.qualityMode;
            createdForDisplayResolution = displayResolution;
        }

        public Vector2Int createdForDisplayResolution { get; }
        public int lastUsedFrame { get; set; }
        public bool IsValidForOptions(UpscalerOptions options) => options is XessOptions o && o.qualityMode == quality;

        public void Cleanup(CommandBuffer cmd)
        {
            Xess.Destroy(cmd, handle);
            handle = -1;
        }

        /// <summary>The native context for 'want' (a new one when its size or flags differ); -1 when none is free.</summary>
        public int Ensure(CommandBuffer cmd, Xess.CreateData want)
        {
            if (handle >= 0 && made.displayWidth == want.displayWidth && made.displayHeight == want.displayHeight && made.flags == want.flags)
                return handle;
            Cleanup(cmd);
            handle = Xess.NewHandle();
            if (handle < 0) return -1;
            want.handle = handle;
            want.quality = quality;
            Xess.Issue(cmd, Xess.EventCreate, want);
            made = want;
            return handle;
        }
    }

    /// <summary>Intel XeSS Super Resolution (Xess's native plugin).</summary>
    public sealed class XessUpscaler : AbstractUpscaler
    {
        public const string Id = UpscalerFramework.Xess;

        public override string upscalerId => Id;
        public override string name => "Intel XeSS";
        public override bool isSupportedOnDevice => Xess.Supported;
        public override bool isTemporal => true;
        public override bool supportsSharpening => false;
        public override bool hasQualityMode => true;

        public override IUpscalerContext CreateContext(UpscalerOptions options, Vector2Int displayResolution) =>
            isSupportedOnDevice && options is XessOptions o ? new XessContext(o, displayResolution) : null;

        /// <summary>A Halton (2, 3) pattern of 8 x ratio² phases (XeSS's guide: at least that long), in [-0.5, 0.5].</summary>
        public override void CalculateJitter(int frameIndex, float upscaleRatio, out Vector2 jitter, out bool allowScaling)
        {
            allowScaling = false;
            int phases = Mathf.Max(1, Mathf.FloorToInt(8f * upscaleRatio * upscaleRatio + 0.5f));
            int i = frameIndex % phases + 1;
            jitter = new Vector2(HaltonSequence.Get(i, 2) - 0.5f, HaltonSequence.Get(i, 3) - 0.5f);
        }

        public override UpscalerResolutionInfo GetResolutionInfo(Vector2Int displayResolution, UpscalerOptions options)
        {
            if (!isSupportedOnDevice || options is not XessOptions o) return UpscalerResolutionInfo.Fixed(displayResolution);
            if (o.resolutionMode == UpscalerResolutionMode.CustomScaling) return base.GetResolutionInfo(displayResolution, options);
            return UpscalerResolutionInfo.Fixed(Xess.InputResolution(o.qualityMode, displayResolution));
        }

        /// <summary>XeSS's guide: the textures' mip bias is log2 of the render size over the display size.</summary>
        public override float CalculateMipBias(Vector2Int pre, Vector2Int post) =>
            Mathf.Min(Mathf.Log((float)pre.x / post.x, 2f), Mathf.Log((float)pre.y / post.y, 2f));

        sealed class PassData
        {
            public XessContext context;
            public Xess.CreateData create;
            public Xess.DispatchData dispatch;
            public TextureHandle color, depth, motion, output;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var io = frameData.Get<UpscalingIO>();
            if (io.context is not XessContext context) return;

            // The result: the display's size, the colour's own (linear) format (XeSS: the same format as its input)
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
            desc.name = "_XessOutput";
            TextureHandle output = renderGraph.CreateTexture(desc);

            using (var builder = renderGraph.AddUnsafePass<PassData>("XeSS", out var pass, new ProfilingSampler("XeSS")))
            {
                uint flags = 0;
                if (!io.hdrInput) flags |= Xess.FlagLdrInput;
                if (io.motionVectorTextureSize == io.postUpscaleResolution && io.postUpscaleResolution != io.preUpscaleResolution) flags |= Xess.FlagHighResMotionVectors;
                if (io.jitteredMotionVectors) flags |= Xess.FlagJitteredMotionVectors;
                if (io.invertedDepth) flags |= Xess.FlagInvertedDepth;
                pass.context = context;
                pass.create = new Xess.CreateData
                {
                    displayWidth = (uint)io.postUpscaleResolution.x, displayHeight = (uint)io.postUpscaleResolution.y, flags = flags,
                };
                // XeSS wants the motion from this frame to the one before, in pixels (as FSR: FSRUtils.PopulateExecutionData)
                float sign = io.motionVectorDirection == UpscalingIO.MotionVectorDirection.PreviousFrameToCurrentFrame ? -1f : 1f;
                bool ndc = io.motionVectorDomain == UpscalingIO.MotionVectorDomain.NDC;
                pass.dispatch = new Xess.DispatchData
                {
                    reset = io.resetHistory ? 1 : 0,
                    jitterX = io.subpixelJitter.x, jitterY = io.subpixelJitter.y,
                    motionScaleX = sign * (ndc ? io.motionVectorTextureSize.x : 1f), motionScaleY = sign * (ndc ? io.motionVectorTextureSize.y : 1f),
                    renderWidth = (uint)io.preUpscaleResolution.x, renderHeight = (uint)io.preUpscaleResolution.y,
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
                    CommandBuffer cmd = CommandBufferHelpers.GetNativeCommandBuffer(ctx.cmd);
                    int handle = d.context.Ensure(cmd, d.create);
                    if (handle < 0) return;
                    RenderTexture color = d.color, depth = d.depth, motion = d.motion, outRt = d.output;
                    var data = d.dispatch;
                    data.handle = handle;
                    data.color = Xess.Ptr(color);
                    data.depth = Xess.Ptr(depth);
                    data.depthBuffer = Xess.Ptr(depth, depthBuffer: true);
                    data.motion = Xess.Ptr(motion);
                    data.output = Xess.Ptr(outRt);
                    Xess.Issue(cmd, Xess.EventDispatch, data);
                });
            }
            io.cameraColor = output;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Direct3D12) return;
            Xess.Init();
            if (Xess.Supported) UpscalerRegistry.Register<XessUpscaler, XessOptions>(Id, "Intel XeSS");
        }
    }
}
#endif
