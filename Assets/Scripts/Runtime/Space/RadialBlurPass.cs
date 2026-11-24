// RadialBlurPass.cs
// The original's boost blur (post effect 0x1400002; MGame::OnUpdate: while PlayerEgo::boosting and not docking to a planet
// it switches the effect on, sets its strength to getBoostPercentage (Engine +0x3b8, never below 0) and its origin to the
// player's ship on screen (PaintCanvas::GetScreenPosition / the display size, y = 1 - y; Engine +0x3bc); RenderEffect
// draws Resources/GoF2PostFx/RadialBlur over the frame). Remake, 2026-11-17 (the user's request): a URP render-graph pass
// after the post-processing on the camera following a boosting ship (ChaseCamera), always on. ChaseCamera's comment had
// it "not reproduced", the notes "never switched on".

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;
using GoF2Remake.Flight;

namespace GoF2Remake.Visuals
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public sealed class RadialBlurPass : ScriptableRenderPass
    {
        static readonly int OriginId = Shader.PropertyToID("_RadialOrigin"), StrengthId = Shader.PropertyToID("_BlurStrength");
        static RadialBlurPass instance;
        static bool installed;
        Material material;

        /// <summary>Queues the pass for the chase camera while its ship boosts (Bootstrap).</summary>
        public static void Install()
        {
            if (installed) return;
            installed = true;
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
        }

        static void OnBeginCamera(ScriptableRenderContext context, Camera cam)
        {
            if (cam.cameraType != CameraType.Game || cam.targetTexture != null) return;
            var chase = cam.GetComponent<ChaseCamera>();
            if (chase == null || !chase.isActiveAndEnabled || chase.target == null || chase.target.Model == null) return;
            var model = chase.target.Model;
            float strength = model.IsBoosting ? Mathf.Max(model.BoostVisualPercent, 0f) : 0f;
            if (strength <= 0f) return;
            var data = cam.GetUniversalAdditionalCameraData();
            if (data == null || !data.renderPostProcessing || data.renderType != CameraRenderType.Base || data.scriptableRenderer == null) return;
            instance ??= new RadialBlurPass();
            if (instance.material == null)
            {
                var shader = Resources.Load<Shader>("GoF2PostFx/RadialBlur");
                if (shader == null || !shader.isSupported) return;
                instance.material = CoreUtils.CreateEngineMaterial(shader);
            }
            Vector3 vp = cam.WorldToViewportPoint(chase.target.transform.position);
            if (vp.z <= 0f) return;   // behind the camera: no origin to blur toward
            instance.material.SetVector(OriginId, new Vector4(vp.x, vp.y, 0f, 0f));
            instance.material.SetFloat(StrengthId, strength);
            data.scriptableRenderer.EnqueuePass(instance);
        }

        RadialBlurPass()
        {
            renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
            requiresIntermediateTexture = true;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resources = frameData.Get<UniversalResourceData>();
            if (resources.isActiveTargetBackBuffer || material == null) return;
            var source = resources.activeColorTexture;
            var desc = renderGraph.GetTextureDesc(source);
            desc.name = "RadialBlur Scene";
            desc.clearBuffer = false;
            desc.msaaSamples = MSAASamples.None;
            desc.depthBufferBits = DepthBits.None;
            var copy = renderGraph.CreateTexture(desc);
            renderGraph.AddBlitPass(source, copy, Vector2.one, Vector2.zero, passName: "RadialBlur Copy");
            renderGraph.AddBlitPass(new RenderGraphUtils.BlitMaterialParameters(copy, source, material, 0), "RadialBlur");
        }
    }
}
