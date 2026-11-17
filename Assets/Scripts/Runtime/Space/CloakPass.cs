// CloakPass.cs
// The cloaked hulls (GoF2/Cloak, LightMode "GoF2Cloak") drawn after URP's transparent pass from a copy of the screen as it is
// then, the way the original does it: PaintCanvas::End3d draws every opaque, alpha and additive material first, then
// Engine::CopyFBO copies the frame into the refraction FBO (DrawFBOShader: a plain full-size copy), and only then the
// cloak's material type 0xe (BumpShaderCloak). So the original's cloaked hull refracts the ship's own lights, engine glow,
// shots and the sun's glare, displaced by up to 100 px by the normal map and moving with the wobble: the red patches that
// crawl over a cloaked ship (#47). The remake refracted URP's opaque texture, taken before the transparents, so the hull
// showed only the background. Only while something is cloaked (Request); enqueued from beginCameraRendering like BackdropPass.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace GoF2Remake.Visuals
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public sealed class CloakPass : ScriptableRenderPass
    {
        static readonly ShaderTagId Tag = new ShaderTagId("GoF2Cloak");
        static readonly int SceneId = Shader.PropertyToID("_GoF2CloakScene");
        static readonly HashSet<object> owners = new HashSet<object>();
        static CloakPass instance;
        static bool installed;

        /// <summary>A cloak in use ('on') or done: the pass runs while any is.</summary>
        public static void Request(object owner, bool on)
        {
            if (owner == null) return;
            if (on) owners.Add(owner); else owners.Remove(owner);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOwners() => owners.Clear();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            if (installed) return;
            installed = true;
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
        }

        static void OnBeginCamera(ScriptableRenderContext context, Camera cam)
        {
            if (owners.Count == 0) return;
            if (cam.cameraType != CameraType.Game && cam.cameraType != CameraType.SceneView) return;
            var data = cam.GetUniversalAdditionalCameraData();
            if (data == null || data.scriptableRenderer == null) return;
            instance ??= new CloakPass();
            depth ??= new DepthPass();
            data.scriptableRenderer.EnqueuePass(depth);
            data.scriptableRenderer.EnqueuePass(instance);
        }

        static DepthPass depth;

        /// <summary>Modified: the cloaked hulls' depth (LightMode "GoF2CloakDepth") before URP's transparents, so the ship's
        /// own lights and engine glow behind the hull stay hidden as on an uncloaked ship (the wing tips' light strips showed
        /// through the wings); after the skybox and the backdrop, which the hull's depth would otherwise hide. Only where
        /// the hull hasn't dissolved yet (GoF2/Cloak clips by the dissolve map against _AnimValue): a fully cloaked ship
        /// writes none, so the transparents behind it (shots, explosions, smoke, flames) are drawn into the copy it
        /// refracts as in the original, which draws the hull last with no depth before it, instead of a hole.</summary>
        sealed class DepthPass : ScriptableRenderPass
        {
            static readonly ShaderTagId DepthTag = new ShaderTagId("GoF2CloakDepth");

            public DepthPass()
            {
                renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;
                requiresIntermediateTexture = true;   // see the CloakPass constructor
            }

            class Data { public RendererListHandle list; }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var resources = frameData.Get<UniversalResourceData>();
                var rendering = frameData.Get<UniversalRenderingData>();
                var camera = frameData.Get<UniversalCameraData>();
                var lights = frameData.Get<UniversalLightData>();
                if (resources.isActiveTargetBackBuffer) return;
                var drawing = RenderingUtils.CreateDrawingSettings(DepthTag, rendering, camera, lights, SortingCriteria.CommonOpaque);
                var list = renderGraph.CreateRendererList(new RendererListParams(rendering.cullResults, drawing, new FilteringSettings(RenderQueueRange.all)));
                using (var builder = renderGraph.AddRasterRenderPass<Data>("GoF2 Cloak Depth", out var data))
                {
                    data.list = list;
                    builder.UseRendererList(list);
                    builder.SetRenderAttachment(resources.activeColorTexture, 0);
                    builder.SetRenderAttachmentDepth(resources.activeDepthTexture, AccessFlags.ReadWrite);
                    builder.SetRenderFunc((Data d, RasterGraphContext ctx) => ctx.cmd.DrawRendererList(d.list));
                }
            }
        }

        /// <summary>Remake (#224): the pass asks for URP's intermediate colour texture (PreTransformGuard's way). Without it URP
        /// draws straight to the back buffer whenever nothing else needs that texture (render scale 100 % without MSAA: the
        /// usual phone settings when the Vulkan pre-transform guard isn't active, e.g. OpenGL ES), both passes return at their
        /// isActiveTargetBackBuffer check, and the cloaked hull and the emergency system's bubble were never drawn. Only while
        /// something is cloaked or shielded, so the extra blit costs nothing otherwise.</summary>
        CloakPass()
        {
            renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
            requiresIntermediateTexture = true;
        }

        class PassData
        {
            public RendererListHandle list;
            public TextureHandle scene;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resources = frameData.Get<UniversalResourceData>();
            var rendering = frameData.Get<UniversalRenderingData>();
            var camera = frameData.Get<UniversalCameraData>();
            var lights = frameData.Get<UniversalLightData>();
            if (resources.isActiveTargetBackBuffer) return;

            // Engine::CopyFBO: the frame so far, full size.
            var desc = renderGraph.GetTextureDesc(resources.activeColorTexture);
            desc.name = "GoF2 Cloak Scene";
            desc.msaaSamples = MSAASamples.None;
            desc.depthBufferBits = DepthBits.None;
            desc.clearBuffer = false;
            var scene = renderGraph.CreateTexture(desc);
            renderGraph.AddBlitPass(resources.activeColorTexture, scene, Vector2.one, Vector2.zero, passName: "GoF2 Cloak Copy");

            var drawing = RenderingUtils.CreateDrawingSettings(Tag, rendering, camera, lights, SortingCriteria.CommonTransparent);
            var filtering = new FilteringSettings(RenderQueueRange.all);
            var list = renderGraph.CreateRendererList(new RendererListParams(rendering.cullResults, drawing, filtering));
            using (var builder = renderGraph.AddRasterRenderPass<PassData>("GoF2 Cloak", out var data))
            {
                data.list = list;
                data.scene = scene;
                builder.UseRendererList(list);
                builder.UseTexture(scene);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderAttachment(resources.activeColorTexture, 0);
                builder.SetRenderAttachmentDepth(resources.activeDepthTexture, AccessFlags.ReadWrite);
                builder.SetRenderFunc((PassData d, RasterGraphContext ctx) =>
                {
                    ctx.cmd.SetGlobalTexture(SceneId, d.scene);
                    ctx.cmd.DrawRendererList(d.list);
                });
            }
        }
    }
}
