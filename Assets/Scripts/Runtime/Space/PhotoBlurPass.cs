// PhotoBlurPass.cs
// Remake (a player's request, 2026-11): more blur than URP's depth of field gives, for the photo tools (Action Freeze and
// Inspect ship's Camera settings, UI.PhotoTools). URP caps its Gaussian radius at 1.5 (2 px of its half-size image) and
// its Bokeh at 14 px, both reached at the panel's Blur 100 %; 100-500 % keeps URP's at its most and adds this pass:
// what lies past the focus is blurred again at a quarter of the screen's size and mixed in by how far past it is. A
// render-graph pass before the post-processing (URP's depth of field, bloom and grading still follow), on the one camera
// the photo tools gave it. Shader: Resources/GoF2PostFx/PhotoBlur.

using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace GoF2Remake.Visuals
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public sealed class PhotoBlurPass : ScriptableRenderPass
    {
        const int PassPrefilter = 0, PassBlurH = 1, PassBlurV = 2, PassComposite = 3;
        static readonly int DepthTex = Shader.PropertyToID("_DepthTex"), BlurTex = Shader.PropertyToID("_BlurTex"),
            Far = Shader.PropertyToID("_Far"), Step = Shader.PropertyToID("_Step"), Strength = Shader.PropertyToID("_Strength"),
            SceneTexel = Shader.PropertyToID("_SceneTexel"), BlurTexel = Shader.PropertyToID("_BlurTexel");

        static PhotoBlurPass instance;
        static bool installed;
        static Camera target;
        static float strength, farStart, farEnd;

        // Play mode runs without a domain reload: a photo mode open when it ended would keep blurring the next camera.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { target = null; strength = 0f; }

        Material material;

        /// <summary>Blurs what 'cam' sees past 'start' metres, fully from 'end', by 'amount' (0 = off, 1 = the most).</summary>
        public static void Set(Camera cam, float amount, float start, float end)
        {
            Install();
            target = cam;
            strength = Mathf.Clamp01(amount);
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
            instance ??= new PhotoBlurPass();
            if (instance.material == null)
            {
                var shader = Resources.Load<Shader>("GoF2PostFx/PhotoBlur");
                if (shader == null || !shader.isSupported) return;
                instance.material = CoreUtils.CreateEngineMaterial(shader);
            }
            data.scriptableRenderer.EnqueuePass(instance);
        }

        PhotoBlurPass()
        {
            renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
            requiresIntermediateTexture = true;
            ConfigureInput(ScriptableRenderPassInput.Depth);
        }

        class PassData
        {
            public TextureHandle source, depth, blur;
            public Material material;
            public int pass;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resources = frameData.Get<UniversalResourceData>();
            if (resources.isActiveTargetBackBuffer || material == null) return;
            var source = resources.activeColorTexture;
            var depth = resources.cameraDepthTexture;
            if (!source.IsValid() || !depth.IsValid()) return;

            var sceneDesc = renderGraph.GetTextureDesc(source);
            int w = Mathf.Max(1, sceneDesc.width / 4), h = Mathf.Max(1, sceneDesc.height / 4);
            material.SetVector(Far, new Vector4(farStart, farEnd, 0f, 0f));
            material.SetFloat(Strength, Mathf.Clamp01(strength * 12f));   // fades in over 100-133 %
            // Wider taps and more rounds as it grows (the radius about step x sqrt(rounds)); the same look at any size.
            material.SetFloat(Step, Mathf.Lerp(1.5f, 6f, strength) * sceneDesc.height / 1080f);
            int iterations = 2 + Mathf.RoundToInt(strength * 3f);
            material.SetVector(SceneTexel, new Vector4(1f / sceneDesc.width, 1f / sceneDesc.height, 0f, 0f));
            material.SetVector(BlurTexel, new Vector4(1f / w, 1f / h, 0f, 0f));

            var small = new TextureDesc(w, h)
            {
                colorFormat = GraphicsFormat.R16G16B16A16_SFloat,   // premultiplied HDR colour, the weight in alpha
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                name = "PhotoBlur A",
            };
            var a = renderGraph.CreateTexture(small);
            small.name = "PhotoBlur B";
            var b = renderGraph.CreateTexture(small);

            Raster(renderGraph, "PhotoBlur Prefilter", source, depth, default, a, PassPrefilter);
            for (int i = 0; i < iterations; i++)
            {
                renderGraph.AddBlitPass(new RenderGraphUtils.BlitMaterialParameters(a, b, material, PassBlurH), "PhotoBlur BlurH");
                renderGraph.AddBlitPass(new RenderGraphUtils.BlitMaterialParameters(b, a, material, PassBlurV), "PhotoBlur BlurV");
            }

            // The scene is read and written by the composite: copy it first.
            sceneDesc.name = "PhotoBlur Scene";
            sceneDesc.clearBuffer = false;
            sceneDesc.msaaSamples = MSAASamples.None;
            sceneDesc.depthBufferBits = DepthBits.None;
            var scene = renderGraph.CreateTexture(sceneDesc);
            renderGraph.AddBlitPass(source, scene, Vector2.one, Vector2.zero, passName: "PhotoBlur Copy");
            Raster(renderGraph, "PhotoBlur Composite", scene, depth, a, source, PassComposite);
        }

        void Raster(RenderGraph renderGraph, string name, TextureHandle from, TextureHandle depth, TextureHandle blur, TextureHandle to, int pass)
        {
            using var builder = renderGraph.AddRasterRenderPass<PassData>(name, out var data);
            data.source = from;
            data.depth = depth;
            data.blur = blur;
            data.material = material;
            data.pass = pass;
            builder.UseTexture(from);
            builder.UseTexture(depth);
            if (blur.IsValid()) builder.UseTexture(blur);
            builder.SetRenderAttachment(to, 0);
            builder.SetRenderFunc((PassData d, RasterGraphContext ctx) =>
            {
                d.material.SetTexture(DepthTex, d.depth);
                if (d.blur.IsValid()) d.material.SetTexture(BlurTex, d.blur);
                Blitter.BlitTexture(ctx.cmd, d.source, new Vector4(1f, 1f, 0f, 0f), d.material, d.pass);
            });
        }
    }
}
