// PhotoZoomPass.cs
// Remake (a player's request, 2026-11, with a picture of a station before a red nebula streaking out behind it): the
// photo tools' Zoom blur (Action Freeze and Inspect ship's Camera settings, UI.PhotoTools), the look of a lens zoomed
// while the shutter is open. The background streaks out from the ship on the screen and the ship stays sharp (the
// depth decides, as in PhotoBlurPass); unlike the Shutter angle's motion blur (PhotoMotionPass) it is no physical
// motion, so the sky and the nebulae streak too. A render-graph pass before the post-processing (URP's bloom and
// grading follow), on the one camera the photo tools gave it. Shader: Resources/GoF2PostFx/PhotoZoom.

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace GoF2Remake.Visuals
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public sealed class PhotoZoomPass : ScriptableRenderPass
    {
        static readonly int DepthTex = Shader.PropertyToID("_DepthTex"), Far = Shader.PropertyToID("_Far"),
            Centre = Shader.PropertyToID("_Centre"), Length = Shader.PropertyToID("_Length"), Size = Shader.PropertyToID("_Size");

        static PhotoZoomPass instance;
        static bool installed;
        static Camera target;
        static float strength, farStart, farEnd;
        static Vector2 centre;

        // Play mode runs without a domain reload: a photo mode open when it ended would keep blurring the next camera.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { target = null; strength = 0f; }

        Material material;

        /// <summary>Streaks what 'cam' sees past 'start' metres (fully from 'end') out from 'viewportCentre' (0..1, y up),
        /// by 'amount' (0 = off, 1 = the most).</summary>
        public static void Set(Camera cam, float amount, Vector2 viewportCentre, float start, float end)
        {
            Install();
            target = cam;
            strength = Mathf.Clamp01(amount);
            centre = viewportCentre;
            farStart = Mathf.Max(0f, start);
            farEnd = Mathf.Max(farStart + 0.01f, end);
        }

        public static void Clear() { target = null; strength = 0f; }

        static void Install()
        {
            if (installed) return;
            installed = true;
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
        }

        static void OnBeginCamera(ScriptableRenderContext context, Camera cam)
        {
            if (strength <= 0f || target == null || cam != target) return;
            var data = cam.GetUniversalAdditionalCameraData();
            if (data == null || data.scriptableRenderer == null) return;
            instance ??= new PhotoZoomPass();
            if (instance.material == null)
            {
                var shader = Resources.Load<Shader>("GoF2PostFx/PhotoZoom");
                if (shader == null || !shader.isSupported) return;
                instance.material = CoreUtils.CreateEngineMaterial(shader);
            }
            data.scriptableRenderer.EnqueuePass(instance);
        }

        PhotoZoomPass()
        {
            renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing - 1;   // with the motion blur, before PhotoBlurPass
            requiresIntermediateTexture = true;
            ConfigureInput(ScriptableRenderPassInput.Depth);
        }

        class PassData
        {
            public TextureHandle source, depth;
            public Material material;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resources = frameData.Get<UniversalResourceData>();
            if (resources.isActiveTargetBackBuffer || material == null) return;
            var source = resources.activeColorTexture;
            var depth = resources.cameraDepthTexture;
            if (!source.IsValid() || !depth.IsValid()) return;

            var sceneDesc = renderGraph.GetTextureDesc(source);
            material.SetVector(Far, new Vector4(farStart, farEnd, 0f, 0f));
            // The texture's v runs down where the platform's uv starts at the top, the viewport's y always up.
            float cy = SystemInfo.graphicsUVStartsAtTop ? 1f - centre.y : centre.y;
            material.SetVector(Centre, new Vector4(centre.x, cy, 0f, 0f));
            material.SetFloat(Length, strength * 0.45f);
            material.SetVector(Size, new Vector4(sceneDesc.width, sceneDesc.height, 0f, 0f));

            sceneDesc.name = "PhotoZoom Scene";
            sceneDesc.clearBuffer = false;
            sceneDesc.msaaSamples = MSAASamples.None;
            sceneDesc.depthBufferBits = DepthBits.None;
            var scene = renderGraph.CreateTexture(sceneDesc);
            renderGraph.AddBlitPass(source, scene, Vector2.one, Vector2.zero, passName: "PhotoZoom Copy");

            using var builder = renderGraph.AddRasterRenderPass<PassData>("PhotoZoom", out var data);
            data.source = scene;
            data.depth = depth;
            data.material = material;
            builder.UseTexture(scene);
            builder.UseTexture(depth);
            builder.SetRenderAttachment(source, 0);
            builder.SetRenderFunc((PassData d, RasterGraphContext ctx) =>
            {
                d.material.SetTexture(DepthTex, d.depth);
                Blitter.BlitTexture(ctx.cmd, d.source, new Vector4(1f, 1f, 0f, 0f), d.material, 0);
            });
        }
    }
}
