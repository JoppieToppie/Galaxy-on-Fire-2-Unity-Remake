// FrameGenerationPass.cs
// Feature: docs/notes/options.md "Frame generation": what DLSS Frame Generation needs from the camera each frame (the
// native plugin gof2_streamline.dll tags it for Streamline; FrameGeneration is the rest of the managed side).
// A render-graph pass after everything of the camera, the final blit included (RenderPassEvent.AfterRendering: URP draws
// the interface's overlay after these passes), on the first full-screen camera a frame draws to the screen:
//   - the frame's inputs (RecordInputs; NeuralRenderingPass records them earlier when it runs): the camera's depth and
//     motion vectors copied into two textures of our own, the way up the back buffer is (FrameGenInputs.shader: on
//     Direct3D URP's render textures are usually the other way up from the back buffer DLSS-G interpolates). Ours, so
//     their native pointers are known once and nothing else writes them before the frame is presented. Their tag event
//     gives Streamline those two and the camera's matrices (this frame's and the one before),
//   - the back buffer as it is then, before the interface: the HUD-less colour, so the HUD isn't smeared along with the
//     scene behind it,
//   - the interface's coverage (RecordInterface, "Separate HUD layer"): the overlay drawn once more into a texture of
//     its own, its alpha copied the way up the back buffer is and tagged as the UI alpha. With it DLSS-G interpolates
//     the scene and the interface apart and puts them together again (DLSSGOptions.enableUserInterfaceRecomposition).
// Asking for the motion vectors makes URP draw them (FrameGeneration.WantsTags: only while they are used).

using System;
using GoF2Remake.Data;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace GoF2Remake.Visuals
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public sealed class FrameGenerationPass : ScriptableRenderPass
    {
        public const int PassDepth = 0, PassMotion = 1, PassEncode = 2, PassDecode = 3, PassAlpha = 4;
        static readonly Vector4 TurnedOver = new Vector4(1f, -1f, 0f, 1f);   // the blit's scale / bias: v -> 1 - v
        static readonly Vector4 AsItIs = new Vector4(1f, 1f, 0f, 0f);

        static FrameGenerationPass instance;
        static NeuralRenderingPass neural;
        static PictureFiltersPass picture;
        static bool installed;
        static uint enqueuedFrame = uint.MaxValue, inputsFrame = uint.MaxValue;

        static Material material;
        static RTHandle depthCopy, motionCopy, uiCopy;
        static IntPtr depthPtr, motionPtr, uiPtr;
        // The camera of the frame before, for clip to previous clip
        static Camera lastCamera;
        static uint lastFrame;
        static Matrix4x4 lastView, lastProjection;

        public static void Install()
        {
            if (installed) return;
            installed = true;
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
        }

        static void OnBeginCamera(ScriptableRenderContext context, Camera cam)
        {
            if (!FrameGeneration.WantsTags || enqueuedFrame == FrameGeneration.Frame) return;
            if (cam.cameraType != CameraType.Game || cam.targetTexture != null || cam.orthographic) return;
            var rect = cam.rect;
            if (rect.width < 0.99f || rect.height < 0.99f) return;   // a picture in the picture: the main view's frame data only
            var data = cam.GetUniversalAdditionalCameraData();
            if (data == null || data.scriptableRenderer == null || data.renderType != CameraRenderType.Base) return;
            if (material == null)
            {
                var shader = Resources.Load<Shader>("GoF2PostFx/FrameGenInputs");
                if (shader == null || !shader.isSupported) return;
                material = CoreUtils.CreateEngineMaterial(shader);
            }
            enqueuedFrame = FrameGeneration.Frame;
            if (FrameGeneration.WantsNeural)
            {
                neural ??= new NeuralRenderingPass();
                data.scriptableRenderer.EnqueuePass(neural);
            }
            if (FrameGeneration.WantsPicture)
            {
                picture ??= new PictureFiltersPass();
                data.scriptableRenderer.EnqueuePass(picture);
            }
            if (FrameGeneration.WantsFrames)
            {
                instance ??= new FrameGenerationPass();
                data.scriptableRenderer.EnqueuePass(instance);
            }
        }

        FrameGenerationPass()
        {
            renderPassEvent = RenderPassEvent.AfterRendering;
            ConfigureInput(ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Motion);
        }

        class CopyData
        {
            public TextureHandle source, target;
            public Material material;
            public int pass;
            public bool byTarget;
        }

        class EventData
        {
            public IntPtr data;
            public int eventId;
        }

        class InterfaceData
        {
            public RendererListHandle list;
            public TextureHandle color, depth;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resources = frameData.Get<UniversalResourceData>();
            var cameraData = frameData.Get<UniversalCameraData>();
            // The camera's picture is on the back buffer by now, or this isn't the camera that puts it there.
            if (!cameraData.resolveFinalTarget || !resources.isActiveTargetBackBuffer) return;
            IntPtr ui = RecordInterface(renderGraph, frameData, out var uiTarget);
            if (inputsFrame == FrameGeneration.Frame) Issue(renderGraph, "FrameGen HUD-less", FrameGeneration.TagHudless(ui), uiTarget, default);
            else RecordInputs(renderGraph, frameData, true, ui, uiTarget);
        }

        /// <summary>The interface's coverage for DLSS-G: the overlay (UI Toolkit / uGUI, as URP draws it onto the back
        /// buffer right after this pass) drawn into a texture of the back buffer's size cleared to nothing, and its alpha
        /// copied into a one-channel texture of ours the way up the back buffer is. Returns that texture's native
        /// pointer for the tag event, zero when there is none this frame (HDR output: URP composites the interface
        /// in its final blit, so the back buffer has no HUD-less state; $GOF2_FRAMEGEN_UI=0).</summary>
        static IntPtr RecordInterface(RenderGraph renderGraph, ContextContainer frameData, out TextureHandle target)
        {
            target = default;
            var cameraData = frameData.Get<UniversalCameraData>();
            if (!FrameGeneration.UiLayer || material == null || cameraData.isHDROutputActive || !cameraData.rendersOverlayUI) return IntPtr.Zero;
            int w = Screen.width, h = Screen.height;
            if (w <= 0 || h <= 0) return IntPtr.Zero;
            var copy = new RenderTextureDescriptor(w, h, GraphicsFormat.R8_UNorm, 0) { msaaSamples = 1, useMipMap = false };
            if (RenderingUtils.ReAllocateHandleIfNeeded(ref uiCopy, copy, FilterMode.Point, TextureWrapMode.Clamp, name: "_FrameGenUiAlpha") || uiPtr == IntPtr.Zero)
            {
                uiCopy.rt.Create();
                uiPtr = uiCopy.rt.GetNativeTexturePtr();
            }
            if (uiPtr == IntPtr.Zero) return IntPtr.Zero;

            // (the interface's shaders mask with the stencil: a depth-stencil of its own beside the colour)
            var layer = renderGraph.CreateTexture(new TextureDesc(w, h)
                { format = GraphicsFormat.R8G8B8A8_UNorm, msaaSamples = MSAASamples.None, clearBuffer = true, clearColor = Color.clear, name = "_FrameGenUi" });
            var stencil = renderGraph.CreateTexture(new TextureDesc(w, h)
                { format = CoreUtils.GetDefaultDepthStencilFormat(), msaaSamples = MSAASamples.None, clearBuffer = true, name = "_FrameGenUiDepth" });
            // An unsafe pass with the target bound by hand, as URP's own off-screen overlay with a backdrop filter: UI
            // Toolkit reads the active render target when a panel is drawn (in a raster pass two panels came out
            // opposite ways up, 2026-11-20).
            using (var builder = renderGraph.AddUnsafePass<InterfaceData>("FrameGen Interface", out var data))
            {
                builder.UseAllGlobalTextures(true);   // (as URP's own overlay pass: the interface's shaders are anyone's)
                builder.UseTexture(layer, AccessFlags.Write);
                builder.UseTexture(stencil, AccessFlags.Write);
                data.color = layer;
                data.depth = stencil;
                data.list = renderGraph.CreateUIOverlayRendererList(cameraData.camera, UISubset.UIToolkit_UGUI);
                builder.UseRendererList(data.list);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (InterfaceData d, UnsafeGraphContext ctx) =>
                {
                    ctx.cmd.SetRenderTarget(d.color, d.depth);
                    ctx.cmd.ClearRenderTarget(true, true, Color.clear);
                    ctx.cmd.DrawRendererList(d.list);
                });
            }
            target = renderGraph.ImportTexture(uiCopy);
            Copy(renderGraph, "FrameGen Interface Alpha", layer, target, PassAlpha, false);
            if (dumpPath != null) Dump();
            return uiPtr;
        }

        // Testing: $GOF2_FRAMEGEN_DUMP=<folder> writes the interface's alpha as ui_alpha.png there once, 600 frames in
        // (beside a screenshot of the same frame, screen.png), to see it lies the way the picture does.
        static readonly string dumpPath = Environment.GetEnvironmentVariable("GOF2_FRAMEGEN_DUMP") is { Length: > 0 } folder ? folder : null;
        static bool dumped;

        static void Dump()
        {
            if (dumped || FrameGeneration.Frame < 1500 || uiCopy == null) return;
            dumped = true;
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dumpPath, "screen.png"));
            int w = uiCopy.rt.width, h = uiCopy.rt.height;
            AsyncGPUReadback.Request(uiCopy.rt, 0, TextureFormat.R8, request =>
            {
                if (request.hasError) { Debug.LogWarning("Frame generation: the interface alpha's readback failed"); return; }
                // The texture's rows as Direct3D stores them (the first is the top); a PNG from Unity's bottom-up rows
                var rows = request.GetData<byte>();
                var texture = new Texture2D(w, h, TextureFormat.R8, false);
                var pixels = new byte[w * h];
                for (int y = 0; y < h; y++) Unity.Collections.NativeArray<byte>.Copy(rows, y * w, pixels, (h - 1 - y) * w, w);
                texture.LoadRawTextureData(pixels);
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(dumpPath, "ui_alpha.png"), texture.EncodeToPNG());
                UnityEngine.Object.Destroy(texture);
                Debug.Log("Frame generation: wrote the interface alpha (" + w + " x " + h + ")");
            });
        }

        /// <summary>The frame's inputs for Streamline, once a frame: the depth and motion vector copies and their tag event
        /// (with the back buffer as the HUD-less colour when 'hudless': only once the picture is on it). False when the
        /// camera has no depth or motion vectors this frame.</summary>
        internal static bool RecordInputs(RenderGraph renderGraph, ContextContainer frameData, bool hudless, IntPtr ui = default,
            TextureHandle uiTarget = default)
        {
            var resources = frameData.Get<UniversalResourceData>();
            var cameraData = frameData.Get<UniversalCameraData>();
            var depth = resources.cameraDepthTexture;
            var motion = resources.motionVectorColor;
            if (material == null || !depth.IsValid() || !motion.IsValid()) return false;

            var depthDesc = renderGraph.GetTextureDesc(depth);
            int w = depthDesc.width, h = depthDesc.height;
            if (w <= 0 || h <= 0) return false;
            var copy = new RenderTextureDescriptor(w, h, GraphicsFormat.R32_SFloat, 0) { msaaSamples = 1, useMipMap = false };
            if (RenderingUtils.ReAllocateHandleIfNeeded(ref depthCopy, copy, FilterMode.Point, TextureWrapMode.Clamp, name: "_FrameGenDepth") || depthPtr == IntPtr.Zero)
            {
                depthCopy.rt.Create();
                depthPtr = depthCopy.rt.GetNativeTexturePtr();
            }
            copy.graphicsFormat = GraphicsFormat.R16G16_SFloat;
            if (RenderingUtils.ReAllocateHandleIfNeeded(ref motionCopy, copy, FilterMode.Point, TextureWrapMode.Clamp, name: "_FrameGenMotion") || motionPtr == IntPtr.Zero)
            {
                motionCopy.rt.Create();
                motionPtr = motionCopy.rt.GetNativeTexturePtr();
            }
            if (depthPtr == IntPtr.Zero || motionPtr == IntPtr.Zero) return false;

            var depthTarget = renderGraph.ImportTexture(depthCopy);
            var motionTarget = renderGraph.ImportTexture(motionCopy);
            Copy(renderGraph, "FrameGen Depth", depth, depthTarget, PassDepth, false);
            Copy(renderGraph, "FrameGen Motion", motion, motionTarget, PassMotion, false);

            // The matrices as Direct3D's back buffer sees them: clip space with y up and the depth as rendered (reversed),
            // no jitter. Unity's are for column vectors; FrameGeneration turns them into Streamline's.
            var cam = cameraData.camera;
            Matrix4x4 view = cameraData.GetViewMatrix();
            Matrix4x4 projection = GL.GetGPUProjectionMatrix(cam.projectionMatrix, false);
            uint frame = FrameGeneration.Frame;
            bool reset = lastCamera != cam || frame - lastFrame != 1;
            if (lastCamera != cam) Debug.Log($"Frame generation: tagging camera \"{cam.name}\" ({w} x {h}, screen {Screen.width} x {Screen.height})");
            Matrix4x4 clipToPrevClip = reset
                ? Matrix4x4.identity
                : lastProjection * lastView * view.inverse * projection.inverse;
            lastCamera = cam;
            lastFrame = frame;
            lastView = view;
            lastProjection = projection;
            // The jitter a temporal upscaler put on the projection, as DLSS counts it: pixels of the sample's offset, y down.
            Matrix4x4 jittered = cameraData.GetProjectionMatrix();
            var jitter = new Vector2(-(cam.projectionMatrix.m02 - jittered.m02) * 0.5f * w, (cam.projectionMatrix.m12 - jittered.m12) * 0.5f * h);

            IntPtr data = FrameGeneration.TagInputs(depthPtr, motionPtr, w, h, reset, projection, clipToPrevClip, jitter, cam, hudless, ui);
            Issue(renderGraph, "FrameGen Tag", data, depthTarget, motionTarget, uiTarget);
            inputsFrame = frame;
            return true;
        }

        /// <summary>A plugin event on the render graph's command buffer (after the passes that wrote 'a' and 'b').</summary>
        static void Issue(RenderGraph renderGraph, string name, IntPtr data, TextureHandle a, TextureHandle b, TextureHandle c = default)
        {
            using var builder = renderGraph.AddUnsafePass<EventData>(name, out var pass, new ProfilingSampler(name));
            pass.data = data;
            pass.eventId = FrameGeneration.EventTag;
            if (a.IsValid()) builder.UseTexture(a);
            if (b.IsValid()) builder.UseTexture(b);
            if (c.IsValid()) builder.UseTexture(c);
            builder.AllowPassCulling(false);
            builder.SetRenderFunc((EventData d, UnsafeGraphContext ctx) =>
                CommandBufferHelpers.GetNativeCommandBuffer(ctx.cmd).IssuePluginEventAndData(FrameGeneration.EventFunc, d.eventId, d.data));
        }

        /// <summary>A copy through one of FrameGenInputs' passes, turned over where the one side is the other way up from
        /// the real back buffer (URP's usual render texture on Direct3D): the source's side, or with 'byTarget' the
        /// target's (a picture of ours going back into URP's).</summary>
        internal static void Copy(RenderGraph renderGraph, string name, TextureHandle from, TextureHandle to, int pass, bool byTarget)
        {
            using var builder = renderGraph.AddRasterRenderPass<CopyData>(name, out var data);
            data.source = from;
            data.target = to;
            data.material = material;
            data.pass = pass;
            data.byTarget = byTarget;
            builder.UseTexture(from);
            builder.SetRenderAttachment(to, 0);
            builder.SetRenderFunc((CopyData d, RasterGraphContext ctx) =>
            {
                var backBuffer = SystemInfo.graphicsUVStartsAtTop ? TextureUVOrigin.TopLeft : TextureUVOrigin.BottomLeft;
                bool turn = ctx.GetTextureUVOrigin(d.byTarget ? d.target : d.source) != backBuffer;
                Blitter.BlitTexture(ctx.cmd, d.source, turn ? TurnedOver : AsItIs, d.material, d.pass);
            });
        }
    }
}
