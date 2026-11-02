// The photo tools' blur past URP's (PhotoBlurPass, Visuals): URP's depth of field stops at a Gaussian radius of 1.5
// (2 px of its half-size image) and a Bokeh radius of 14 px, which the Camera settings panel's Blur reaches at 100 %;
// 100-500 % adds this one on top. What lies past the focus (by the camera depth) is weighted by how far past it is
// (0 at _Far.x, 1 at _Far.y), so the sharp foreground (the ship) adds nothing to the blur around it:
//   prefilter   the scene at a quarter of its size, colour x weight (premultiplied), weight in alpha
//   blur        a 9-tap Gaussian across and down, _Step texels apart (run twice)
//   composite   the scene mixed towards the blurred background by the weight x _Strength
Shader "Hidden/GoF2/PhotoBlur"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZTest Always ZWrite Off Cull Off Blend Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

        TEXTURE2D_X_FLOAT(_DepthTex);
        TEXTURE2D_X(_BlurTex);
        float2 _Far;        // metres from the camera: blur starts, blur full
        float _Step;        // the blur's tap spacing, in its own texels
        float _Strength;    // 0..1
        float2 _SceneTexel; // 1 / the scene's size
        float2 _BlurTexel;  // 1 / the blur's size

        static const float Weights[5] = { 0.2270270270, 0.1945945946, 0.1216216216, 0.0540540541, 0.0162162162 };

        float Weight(float2 uv)
        {
            float raw = SAMPLE_TEXTURE2D_X(_DepthTex, sampler_PointClamp, uv).r;
            float metres = LinearEyeDepth(raw, _ZBufferParams);
            return saturate((metres - _Far.x) / max(_Far.y - _Far.x, 0.001));
        }

        float4 FragPrefilter(Varyings input) : SV_Target
        {
            float2 uv = input.texcoord;
            float2 t = _SceneTexel;
            float4 sum = 0;
            [unroll] for (int i = 0; i < 4; i++)
            {
                float2 at = uv + t * float2((i & 1) ? 1.0 : -1.0, (i & 2) ? 1.0 : -1.0);
                float w = Weight(at);
                sum += float4(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, at).rgb * w, w);
            }
            return sum * 0.25;
        }

        float4 Blur(float2 uv, float2 dir)
        {
            float2 d = dir * _BlurTexel * _Step;
            float4 c = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv) * Weights[0];
            [unroll] for (int i = 1; i < 5; i++)
            {
                c += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + d * i) * Weights[i];
                c += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv - d * i) * Weights[i];
            }
            return c;
        }

        float4 FragBlurH(Varyings input) : SV_Target { return Blur(input.texcoord, float2(1.0, 0.0)); }
        float4 FragBlurV(Varyings input) : SV_Target { return Blur(input.texcoord, float2(0.0, 1.0)); }

        float4 FragComposite(Varyings input) : SV_Target
        {
            float2 uv = input.texcoord;
            float4 scene = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
            float4 blur = SAMPLE_TEXTURE2D_X(_BlurTex, sampler_LinearClamp, uv);
            float3 background = blur.rgb / max(blur.a, 0.0001);
            // Where the blur saw only the foreground (alpha ~0) its colour means nothing: the scene stays.
            float mixAmount = Weight(uv) * saturate(blur.a * 8.0) * _Strength;
            return float4(lerp(scene.rgb, background, mixAmount), scene.a);
        }
        ENDHLSL

        Pass
        {
            Name "Prefilter"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragPrefilter
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
