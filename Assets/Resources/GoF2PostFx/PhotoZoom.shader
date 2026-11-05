// The photo tools' zoom blur (PhotoZoomPass, Visuals): the Camera settings panel's Zoom blur, the look of a lens zoomed
// during the exposure (the original's never-used radial blur post effect 0x1400002 is the same idea). What lies past the
// subject (by the camera depth: 0 at _Far.x, 1 at _Far.y) streaks out from _Centre (the ship on the screen); the
// samples are weighted the same way, so the sharp ship doesn't smear into the streaks around it.
Shader "Hidden/GoF2/PhotoZoom"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZTest Always ZWrite Off Cull Off Blend Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Random.hlsl"

        #define SAMPLES 48

        TEXTURE2D_X_FLOAT(_DepthTex);
        float2 _Far;       // metres from the camera: streaks start, streaks full
        float2 _Centre;    // uv
        float _Length;     // the streak's share of the way to the centre
        float2 _Size;      // the scene's pixels

        float Weight(float2 uv)
        {
            float raw = SAMPLE_TEXTURE2D_X_LOD(_DepthTex, sampler_PointClamp, uv, 0).r;
            float metres = LinearEyeDepth(raw, _ZBufferParams);
            return saturate((metres - _Far.x) / max(_Far.y - _Far.x, 0.001));
        }

        float4 Frag(Varyings input) : SV_Target
        {
            float2 uv = input.texcoord;
            float4 scene = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, 0);
            float w = Weight(uv);
            if (w <= 0.0) return scene;
            float2 toCentre = (_Centre - uv) * _Length;
            float jitter = InterleavedGradientNoise(uv * _Size, 0);
            float3 sum = scene.rgb;
            float total = 1.0;
            [loop] for (int i = 0; i < SAMPLES; i++)
            {
                float2 at = uv + toCentre * ((i + jitter) / SAMPLES);
                float ws = Weight(at);
                sum += SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, at, 0).rgb * ws;
                total += ws;
            }
            return float4(lerp(scene.rgb, sum / total, w), scene.a);
        }
        ENDHLSL

        Pass
        {
            Name "Zoom"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }
    }
}
