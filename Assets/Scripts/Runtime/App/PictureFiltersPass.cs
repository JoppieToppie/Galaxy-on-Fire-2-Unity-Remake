// PictureFiltersPass.cs
// Feature: docs/notes/options.md "Image Scaling and Dynamic Vibrance": Streamline's two filters of the finished picture,
// NVIDIA Image Scaling as a sharpener (sl.nis, any GPU) and RTX Dynamic Vibrance (sl.deepdvc, GeForce RTX), where their
// DLLs are in the build (FrameGeneration.WantsPicture). Enqueued by FrameGenerationPass on the frame's camera, after the
// post-processing and before the final blit, the way NeuralRenderingPass is (and after it when both run):
//   - the picture, linear SDR here, sRGB-encoded into a 10-bit texture the way up the back buffer is (both filters take
//     the tonemapped picture at its final size),
//   - the plugin's event (gof2_streamline.dll's OnPicture): the sharpening writes a second such texture, the vibrance
//     works on that one in place; a filter that is off or fails leaves the picture as it was,
//   - back into a camera colour texture, linear again, for the final blit.
// Neither needs the frame's depth, motion vectors or constants. Not with HDR output (the picture is not SDR then).

using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace GoF2Remake.Visuals
{
    public sealed class PictureFiltersPass : ScriptableRenderPass
    {
        public PictureFiltersPass()
        {
            renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing + 1;   // (after neural rendering)
            requiresIntermediateTexture = true;
        }

        class EvaluateData
        {
            public TextureHandle input, output;
            public IntPtr data;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resources = frameData.Get<UniversalResourceData>();
            var cameraData = frameData.Get<UniversalCameraData>();
            if (!cameraData.resolveFinalTarget || resources.isActiveTargetBackBuffer || cameraData.isHDROutputActive) return;
            var source = resources.activeColorTexture;
            if (!source.IsValid()) return;

            var sourceDesc = renderGraph.GetTextureDesc(source);
            var desc = new TextureDesc(sourceDesc.width, sourceDesc.height)
            {
                colorFormat = GraphicsFormat.A2B10G10R10_UNormPack32,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = "_PictureFiltersInput",
            };
            var input = renderGraph.CreateTexture(desc);
            desc.enableRandomWrite = true;   // the filters write it as an unordered access view
            desc.name = "_PictureFiltersOutput";
            var output = renderGraph.CreateTexture(desc);
            FrameGenerationPass.Copy(renderGraph, "Picture Filters Encode", source, input, FrameGenerationPass.PassEncode, false);

            using (var builder = renderGraph.AddUnsafePass<EvaluateData>("Picture Filters", out var pass, new ProfilingSampler("Picture Filters")))
            {
                pass.input = input;
                pass.output = output;
                pass.data = FrameGeneration.PictureData(sourceDesc.width, sourceDesc.height);
                builder.UseTexture(input);
                builder.UseTexture(output, AccessFlags.Write);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc((EvaluateData d, UnsafeGraphContext ctx) =>
                {
                    RenderTexture a = d.input, b = d.output;
                    if (a == null || b == null) return;
                    FrameGeneration.FillNeural(d.data, a.GetNativeTexturePtr(), b.GetNativeTexturePtr());
                    CommandBufferHelpers.GetNativeCommandBuffer(ctx.cmd).IssuePluginEventAndData(FrameGeneration.EventFunc, FrameGeneration.EventPicture, d.data);
                });
            }

            sourceDesc.name = "_PictureFiltersColor";
            sourceDesc.clearBuffer = false;
            sourceDesc.msaaSamples = MSAASamples.None;
            sourceDesc.depthBufferBits = DepthBits.None;
            var result = renderGraph.CreateTexture(sourceDesc);
            FrameGenerationPass.Copy(renderGraph, "Picture Filters Decode", output, result, FrameGenerationPass.PassDecode, true);
            resources.cameraColor = result;
        }
    }
}
