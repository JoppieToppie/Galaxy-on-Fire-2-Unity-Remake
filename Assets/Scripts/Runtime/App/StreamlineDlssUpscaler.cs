// StreamlineDlssUpscaler.cs
// Feature: docs/notes/options.md "DLSS with Streamline": DLSS Super Resolution through NVIDIA Streamline (sl.dlss), for
// the runs in which Streamline is started (frame generation, Reflex or Image Scaling on). Unity's own DLSS upscaler (its
// NVIDIA module, nvidia.dlss4) starts NGX itself and NGX is Streamline's then: the module logs "DLSS not available on the
// current NVIDIA graphics card" and the Upscaler option lost DLSS (a player's report, 2026-11-20). This one takes its
// place under the same option (UpscalerFramework.DlssId): the same inputs Unity gives its module (the camera colour,
// depth and motion vectors at the render resolution, the jitter, the motion vector scale), handed to the plugin
// gof2_streamline.dll in a render thread event; a Streamline viewport of its own per camera context (1 .. 16: the frame
// generation's tags are viewport 0's). Without Streamline in the process it is not supported and Unity's own runs.

#if UNITY_STANDALONE_WIN && ENABLE_UPSCALER_FRAMEWORK
using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace GoF2Remake
{
    /// <summary>One camera's DLSS viewport in Streamline.</summary>
    public sealed class StreamlineDlssContext : IUpscalerContext
    {
        static readonly bool[] taken = new bool[17];   // viewports 1 .. 16 (0 is the frame generation's)
        readonly int quality, preset;

        /// <summary>The Streamline viewport, 0 when none was free.</summary>
        public int Viewport { get; private set; }

        public StreamlineDlssContext(StreamlineDlssOptions options, Vector2Int displayResolution)
        {
            quality = options.qualityMode;
            preset = options.preset;
            createdForDisplayResolution = displayResolution;
            for (int i = 1; i < taken.Length; i++)
                if (!taken[i]) { taken[i] = true; Viewport = i; break; }
        }

        public Vector2Int createdForDisplayResolution { get; }
        public int lastUsedFrame { get; set; }
        public bool IsValidForOptions(UpscalerOptions options) => options is StreamlineDlssOptions o && o.qualityMode == quality && o.preset == preset;

        public void Cleanup(CommandBuffer cmd)
        {
            if (Viewport <= 0) return;
            IntPtr slot = FrameGeneration.EventSlot();
            for (int i = 0; i < 40; i += 8) Marshal.WriteInt64(slot, i, 0);   // (no textures)
            Marshal.WriteInt32(slot, 44, Viewport);
            cmd.IssuePluginEventAndData(FrameGeneration.EventFunc, FrameGeneration.EventUpscaleFree, slot);
            taken[Viewport] = false;
            Viewport = 0;
        }
    }

    /// <summary>NVIDIA DLSS through Streamline (FrameGeneration's native plugin).</summary>
    public sealed class StreamlineDlssUpscaler : AbstractUpscaler
    {
        public const string Id = UpscalerFramework.StreamlineDlss;

        public override string upscalerId => Id;
        public override string name => "NVIDIA DLSS (Streamline)";
        public override bool isSupportedOnDevice => FrameGeneration.StreamlineDlss;
        public override bool isTemporal => true;
        public override bool supportsSharpening => false;
        public override bool hasQualityMode => true;

        public override IUpscalerContext CreateContext(UpscalerOptions options, Vector2Int displayResolution) =>
            isSupportedOnDevice && options is StreamlineDlssOptions o ? new StreamlineDlssContext(o, displayResolution) : null;

        /// <summary>DLSS's Halton (2, 3) pattern, 8 x ratio² phases, in [-0.5, 0.5].</summary>
        public override void CalculateJitter(int frameIndex, float upscaleRatio, out Vector2 jitter, out bool allowScaling)
        {
            allowScaling = false;
            int phases = Mathf.Max(1, Mathf.FloorToInt(8f * upscaleRatio * upscaleRatio + 0.5f));
            int i = frameIndex % phases + 1;
            jitter = new Vector2(HaltonSequence.Get(i, 2) - 0.5f, HaltonSequence.Get(i, 3) - 0.5f);
        }

        public override UpscalerResolutionInfo GetResolutionInfo(Vector2Int displayResolution, UpscalerOptions options)
        {
            if (!isSupportedOnDevice || options is not StreamlineDlssOptions o) return UpscalerResolutionInfo.Fixed(displayResolution);
            if (o.resolutionMode == UpscalerResolutionMode.CustomScaling) return base.GetResolutionInfo(displayResolution, options);
            return UpscalerResolutionInfo.Fixed(FrameGeneration.DlssRenderSize(o.qualityMode, displayResolution));
        }

        /// <summary>NVIDIA's guide: the textures' mip bias is log2 of the render size over the display size, less one.</summary>
        public override float CalculateMipBias(Vector2Int pre, Vector2Int post) =>
            Mathf.Min(Mathf.Log((float)pre.x / post.x, 2f), Mathf.Log((float)pre.y / post.y, 2f)) - 1f;

        // The plugin's UpscaleData
        [StructLayout(LayoutKind.Sequential, Pack = 8)]
        struct UpscaleData
        {
            public IntPtr color, depth, depthBuffer, motion, output;
            public uint frame, viewport, renderWidth, renderHeight, outputWidth, outputHeight, flags, mode, preset;
            public float jitterX, jitterY, mvScaleX, mvScaleY, preExposure, cameraNear, cameraFar, fovVertical;
        }

        sealed class PassData
        {
            public UpscaleData data;
            public TextureHandle color, depth, motion, output;
        }

        /// <summary>A preset's letter for the plugin ("Preset_K" -> 'K'), 0 for the default.</summary>
        static uint PresetLetter(int preset)
        {
            string name = UpscalerFramework.DlssPresetName(preset);
            return !string.IsNullOrEmpty(name) && name.Length == 1 ? name[0] : 0u;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var io = frameData.Get<UpscalingIO>();
            if (io.context is not StreamlineDlssContext context || context.Viewport <= 0) return;
            var options = io.options as StreamlineDlssOptions;

            var desc = io.cameraColor.GetDescriptor(renderGraph);
            desc.width = io.postUpscaleResolution.x;
            desc.height = io.postUpscaleResolution.y;
            desc.msaaSamples = MSAASamples.None;
            desc.useMipMap = false;
            desc.autoGenerateMips = false;
            desc.useDynamicScale = false;
            desc.anisoLevel = 0;
            desc.discardBuffer = false;
            desc.enableRandomWrite = true;
            desc.clearBuffer = false;
            desc.filterMode = FilterMode.Bilinear;
            desc.name = "_StreamlineDlssOutput";
            TextureHandle output = renderGraph.CreateTexture(desc);

            using (var builder = renderGraph.AddUnsafePass<PassData>("DLSS (Streamline)", out var pass, new ProfilingSampler("DLSS (Streamline)")))
            {
                // (as Unity's own DLSSIUpscaler feeds its module; Streamline wants the motion in screen fractions)
                float sign = io.motionVectorDirection == UpscalingIO.MotionVectorDirection.PreviousFrameToCurrentFrame ? -1f : 1f;
                bool ndc = io.motionVectorDomain == UpscalingIO.MotionVectorDomain.NDC;
                uint flags = (io.resetHistory ? 1u : 0u) | (io.hdrInput ? 2u : 0u) | (io.invertedDepth ? 4u : 0u) | (io.jitteredMotionVectors ? 8u : 0u);
                pass.data = new UpscaleData
                {
                    frame = FrameGeneration.Frame, viewport = (uint)context.Viewport,
                    renderWidth = (uint)io.preUpscaleResolution.x, renderHeight = (uint)io.preUpscaleResolution.y,
                    outputWidth = (uint)io.postUpscaleResolution.x, outputHeight = (uint)io.postUpscaleResolution.y,
                    flags = flags, mode = (uint)(options != null ? options.qualityMode : 1), preset = options != null ? PresetLetter(options.preset) : 0u,
                    jitterX = io.subpixelJitter.x, jitterY = io.subpixelJitter.y,
                    mvScaleX = sign * (ndc ? 1f : 1f / Mathf.Max(1, io.motionVectorTextureSize.x)),
                    mvScaleY = sign * (ndc ? 1f : 1f / Mathf.Max(1, io.motionVectorTextureSize.y)),
                    preExposure = Mathf.Clamp(io.preExposureValue, 0.2f, 2f),
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
                    RenderTexture color = d.color, depth = d.depth, motion = d.motion, outRt = d.output;
                    if (color == null || outRt == null) return;
                    var data = d.data;
                    data.color = color.GetNativeTexturePtr();
                    data.depth = depth != null ? depth.GetNativeTexturePtr() : IntPtr.Zero;
                    data.depthBuffer = depth != null ? depth.depthBuffer.GetNativeRenderBufferPtr() : IntPtr.Zero;
                    data.motion = motion != null ? motion.GetNativeTexturePtr() : IntPtr.Zero;
                    data.output = outRt.GetNativeTexturePtr();
                    IntPtr slot = FrameGeneration.EventSlot();
                    Marshal.StructureToPtr(data, slot, false);
                    CommandBufferHelpers.GetNativeCommandBuffer(ctx.cmd).IssuePluginEventAndData(FrameGeneration.EventFunc, FrameGeneration.EventUpscale, slot);
                });
            }
            io.cameraColor = output;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Direct3D12) return;
            FrameGeneration.Install();   // (the plugin's state: Streamline is in this run, or not)
            if (FrameGeneration.Bound) UpscalerRegistry.Register<StreamlineDlssUpscaler, StreamlineDlssOptions>(Id, "NVIDIA DLSS (Streamline)");
        }
    }
}
#endif
