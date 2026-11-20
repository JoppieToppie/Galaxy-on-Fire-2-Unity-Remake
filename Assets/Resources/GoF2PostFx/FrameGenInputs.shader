// Frame generation's inputs (FrameGenerationPass, docs/notes/options.md "Frame generation"): DLSS Frame Generation reads
// the depth and the motion vectors beside the back buffer, which on Direct3D is the other way up from URP's usual render
// texture (drawn with a flipped projection, turned over in the final blit: the render graph's TextureUVOrigin says which
// way a texture is). These two passes copy the camera's depth and motion vectors into textures of our own, the way up
// the back buffer is: turned over by the blit's scale / bias (1, -1, 0, 1) where the source is the other way (the pass
// decides). The raw depth as it is; the motion vectors (current minus previous, in texture coordinates) with their y
// negated when turned over, so they always count in the copy's own coordinates.
Shader "Hidden/GoF2/FrameGenInputs"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZTest Always ZWrite Off Cull Off Blend Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

        float4 FragDepth(Varyings input) : SV_Target
        {
            return float4(SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, input.texcoord, 0).r, 0, 0, 0);
        }

        float4 FragMotion(Varyings input) : SV_Target
        {
            float2 motion = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, input.texcoord, 0).rg;
            // The motion is in the source's texture coordinates: turned over, its y counts the other way.
            if (_BlitScaleBias.y < 0.0) motion.y = -motion.y;
            return float4(motion, 0, 0);
        }
        // DLSS neural rendering takes the finished picture as tonemapped SDR: sRGB-encoded going in (NeuralRenderingPass),
        // linear again coming back.
        float4 FragEncode(Varyings input) : SV_Target
        {
            float3 colour = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, input.texcoord, 0).rgb;
            return float4(LinearToSRGB(saturate(colour)), 1);
        }

        float4 FragDecode(Varyings input) : SV_Target
        {
            float3 colour = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, input.texcoord, 0).rgb;
            return float4(SRGBToLinear(colour), 1);
        }

        // The interface's coverage (FrameGenerationPass.RecordInterface): the alpha of the overlay drawn on its own.
        float4 FragAlpha(Varyings input) : SV_Target
        {
            return float4(SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, input.texcoord, 0).a, 0, 0, 0);
        }
        ENDHLSL

        Pass
        {
            Name "Depth"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragDepth
            ENDHLSL
        }

        Pass
        {
            Name "Motion"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragMotion
            ENDHLSL
        }

        Pass
        {
            Name "EncodeSdr"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragEncode
            ENDHLSL
        }

        Pass
        {
            Name "DecodeSdr"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragDecode
            ENDHLSL
        }

        Pass
        {
            Name "Alpha"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragAlpha
            ENDHLSL
        }
    }
}
