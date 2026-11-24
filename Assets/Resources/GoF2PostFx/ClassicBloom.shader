// The original's bloom (AbyssEngine::BloomShader, libgof2hdaa.so 0x942fc / RenderEffect 0x94740), in the remake's
// "Original" bloom option (ClassicBloomPass). The scene is sampled into 256 x 256 (fboLuma) through the bright pass,
// blurred 6 times by the horizontal pass (the phone's offsets are vec2(offset[i]), a diagonal blur; Modified: horizontal
// as the PC version's) and the vertical pass (fboBlurH / fboBlurV, texSize 256), then added onto the scene (Engine::switchBloom = 3: base + blurV). The original
// works on gamma-space LDR colours, so the passes convert from / to the linear camera colour.
Shader "Hidden/GoF2/ClassicBloom"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZTest Always ZWrite Off Cull Off Blend Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

        TEXTURE2D(_BloomTex);

        static const float TexSize = 256.0;   // FBOContainer::Create(0x100)
        static const float Offsets[3] = { 0.0, 1.3846153846, 3.2307692308 };
        static const float Weights[3] = { 0.2270270270, 0.3162162162, 0.0702702703 };

        float3 ToGamma(float3 c)
        {
        #if UNITY_COLORSPACE_GAMMA
            return saturate(c);
        #else
            return LinearToSRGB(saturate(c));
        #endif
        }

        float3 ToLinear(float3 c)
        {
        #if UNITY_COLORSPACE_GAMMA
            return c;
        #else
            return SRGBToLinear(c);
        #endif
        }

        // The bright pass: Luminance 0.08, fMiddleGray 0.005, fWhiteCutoff 0.014; about everything above half brightness.
        float4 FragBright(Varyings input) : SV_Target
        {
            float3 c = ToGamma(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord).rgb);
            c *= 0.005 / (0.08 + 0.001);
            c *= 1.0 + c / (0.014 * 0.014);
            c -= 5.0;
            c = max(c, 0.0);
            c /= 10.0 + c;
            return float4(c, 1.0);
        }

        float3 Blur(float2 uv, float2 dir)
        {
            float3 tc = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb * Weights[0];
            [unroll] for (int i = 1; i < 3; i++)
            {
                tc += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + dir * Offsets[i] / TexSize).rgb * Weights[i];
                tc += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv - dir * Offsets[i] / TexSize).rgb * Weights[i];
            }
            return tc;
        }

        // Modified: truly horizontal, as the PC version's BloomShader.fx (float2(offset / texSize, 0); the user's files,
        // 2026-11-17); the phone's vec2(offset[i]) blurred diagonally.
        float4 FragBlurH(Varyings input) : SV_Target { return float4(Blur(input.texcoord, float2(1.0, 0.0)), 1.0); }
        float4 FragBlurV(Varyings input) : SV_Target { return float4(Blur(input.texcoord, float2(0.0, 1.0)), 1.0); }

        // clamp(bloom + base) (the original's luminance mix of that value with itself changes nothing).
        float4 FragComposite(Varyings input) : SV_Target
        {
            float3 baseLinear = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord).rgb;
            float3 glow = SAMPLE_TEXTURE2D(_BloomTex, sampler_LinearClamp, input.texcoord).rgb;
            float3 c = saturate(ToGamma(baseLinear) + glow);
            return float4(ToLinear(c), 1.0);
        }
        ENDHLSL

        Pass
        {
            Name "Bright"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragBright
            ENDHLSL
        }
        Pass
        {
            Name "BlurH"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragBlurH
            ENDHLSL
        }
        Pass
        {
            Name "BlurV"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragBlurV
            ENDHLSL
        }
        Pass
        {
            Name "Composite"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragComposite
            ENDHLSL
        }
    }
}
