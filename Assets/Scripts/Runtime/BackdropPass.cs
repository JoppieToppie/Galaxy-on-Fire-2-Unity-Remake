// BackdropPass.cs
// The far-plane backdrop (GoF2/Backdrop: the sun and planets, GoF2/SkyLayer: the ring sky, supernova flares, storms, the
// asteroid belt) drawn right after the skybox, before URP copies the camera's opaque texture, the way the original draws them
// in its background pass (StarSystem::render, after the sky and before the scene). Their shaders' pass has the LightMode
// "GoF2Backdrop", which URP's own passes don't draw. Drawn in URP's transparent pass (queue Transparent-100), they were
// missing from the opaque texture, and the cloak and the shield bubble, which refract it, showed black where a planet was.
// Enqueued from beginCameraRendering on every game and Scene view camera (no renderer-asset change), like ClassicBloomPass.

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace GoF2Remake.Visuals
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public sealed class BackdropPass : ScriptableRenderPass
    {
        static readonly ShaderTagId Tag = new ShaderTagId("GoF2Backdrop");
        static BackdropPass instance;
        static bool installed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            if (installed) return;
            installed = true;
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
        }

#if UNITY_EDITOR
        // The Scene view draws the backdrop too (also outside Play mode).
        [UnityEditor.InitializeOnLoadMethod]
        static void InstallInEditor() => Install();
#endif

        static void OnBeginCamera(ScriptableRenderContext context, Camera cam)
        {
            if (cam.cameraType != CameraType.Game && cam.cameraType != CameraType.SceneView) return;
            var data = cam.GetUniversalAdditionalCameraData();
            if (data == null || data.scriptableRenderer == null) return;
            instance ??= new BackdropPass();
            data.scriptableRenderer.EnqueuePass(instance);
        }

        BackdropPass() => renderPassEvent = RenderPassEvent.AfterRenderingSkybox;

        class PassData
        {
            public RendererListHandle list;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resources = frameData.Get<UniversalResourceData>();
            var rendering = frameData.Get<UniversalRenderingData>();
            var camera = frameData.Get<UniversalCameraData>();
            var lights = frameData.Get<UniversalLightData>();
            // Back to front within the queue, then the render queue's painter's order (as in the transparent pass).
            var drawing = RenderingUtils.CreateDrawingSettings(Tag, rendering, camera, lights, SortingCriteria.CommonTransparent);
            var filtering = new FilteringSettings(RenderQueueRange.all);
            var list = renderGraph.CreateRendererList(new RendererListParams(rendering.cullResults, drawing, filtering));
            using (var builder = renderGraph.AddRasterRenderPass<PassData>("GoF2 Backdrop", out var data))
            {
                data.list = list;
                builder.UseRendererList(list);
                builder.SetRenderAttachment(resources.activeColorTexture, 0);
                builder.SetRenderAttachmentDepth(resources.activeDepthTexture, AccessFlags.Read);
                builder.SetRenderFunc((PassData d, RasterGraphContext ctx) => ctx.cmd.DrawRendererList(d.list));
            }
        }
    }
}
