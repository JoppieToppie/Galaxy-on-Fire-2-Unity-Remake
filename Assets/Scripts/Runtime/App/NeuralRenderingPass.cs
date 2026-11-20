// NeuralRenderingPass.cs
// Feature: docs/notes/options.md "Neural rendering": NVIDIA DLSS neural rendering ("DLSS 5", Streamline's sl.dlss_nr) on
// the finished picture, where the GPU runs it (RTX 50 and up, by NVIDIA's own check: FrameGeneration.NeuralSupported).
// Enqueued by FrameGenerationPass on the same camera, after the post-processing and before the final blit:
//   - the frame's inputs first (FrameGenerationPass.RecordInputs: the constants, the depth and motion vector copies),
//   - the picture, which is linear SDR here, sRGB-encoded into a 10-bit texture the way up the back buffer is (DLSS-NR
//     takes the tonemapped SDR picture at the output resolution, like the depth and motion vectors beside it),
//   - the plugin's event: slEvaluateFeature writes the result into a second such texture (a failure copies it through),
//   - back into a camera colour texture, linear again, for the final blit.
// Not with HDR output (the picture is not SDR then). Written from public code, with no RTX 50 card to run it on: see the
// notes' "Not checked".

using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace GoF2Remake.Visuals
{
    public sealed class NeuralRenderingPass : ScriptableRenderPass
    {
        public NeuralRenderingPass()
        {
            renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
            requiresIntermediateTexture = true;
            ConfigureInput(ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Motion);
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
            if (!FrameGenerationPass.RecordInputs(renderGraph, frameData, false)) return;

            var sourceDesc = renderGraph.GetTextureDesc(source);
            var desc = new TextureDesc(sourceDesc.width, sourceDesc.height)
            {
                colorFormat = GraphicsFormat.A2B10G10R10_UNormPack32,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = "_NeuralInput",
            };
            var input = renderGraph.CreateTexture(desc);
            desc.enableRandomWrite = true;   // DLSS-NR writes it as an unordered access view
            desc.name = "_NeuralOutput";
            var output = renderGraph.CreateTexture(desc);
            FrameGenerationPass.Copy(renderGraph, "Neural Encode", source, input, FrameGenerationPass.PassEncode, false);

            using (var builder = renderGraph.AddUnsafePass<EvaluateData>("Neural Rendering", out var pass, new ProfilingSampler("Neural Rendering")))
            {
                pass.input = input;
                pass.output = output;
                pass.data = FrameGeneration.NeuralData(sourceDesc.width, sourceDesc.height);
                builder.UseTexture(input);
                builder.UseTexture(output, AccessFlags.Write);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc((EvaluateData d, UnsafeGraphContext ctx) =>
                {
                    RenderTexture a = d.input, b = d.output;
                    if (a == null || b == null) return;
                    FrameGeneration.FillNeural(d.data, a.GetNativeTexturePtr(), b.GetNativeTexturePtr());
                    CommandBufferHelpers.GetNativeCommandBuffer(ctx.cmd).IssuePluginEventAndData(FrameGeneration.EventFunc, FrameGeneration.EventNeural, d.data);
                });
            }

            sourceDesc.name = "_NeuralColor";
            sourceDesc.clearBuffer = false;
            sourceDesc.msaaSamples = MSAASamples.None;
            sourceDesc.depthBufferBits = DepthBits.None;
            var result = renderGraph.CreateTexture(sourceDesc);
            FrameGenerationPass.Copy(renderGraph, "Neural Decode", output, result, FrameGenerationPass.PassDecode, true);
            resources.cameraColor = result;
        }
    }
}
