// The HDR calibration screen's test pattern (UI.HdrCalibration, docs/notes/options.md "HDR"): drawn over the scene before
// the post-processing while the screen is open, with HDR tonemapping off, so a value of 1 is paper white and a value of
// nits / paper white leaves the display at that many nits.
//   - left: a block at 10,000 nits (the display shows its own brightest there) with a diamond at the peak brightness
//     option's value: the diamond disappears into the block once the option reaches what the display can show,
//   - right: bands at paper white and 50 %, 25 %, 10 % and 3 % of it: the top one is the white of the interface.
// _Nits: x paper white, y peak.
Shader "Hidden/GoF2/HdrCalibration"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZTest Always ZWrite Off Cull Off Blend Off

        Pass
        {
            Name "Pattern"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float4 _Nits;

            float4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                float paper = max(_Nits.x, 1.0);
                float value = 0.0;
                // Left block and its diamond (the same shape either way up)
                float2 block = float2(0.27, 0.58), half = float2(0.19, 0.27);
                if (abs(uv.x - block.x) < half.x && abs(uv.y - block.y) < half.y)
                {
                    float2 d = abs(uv - block) / half;
                    value = (d.x + d.y < 0.55 ? _Nits.y : 10000.0) / paper;
                }
                // Right bands
                float2 bands = float2(0.73, 0.58);
                if (abs(uv.x - bands.x) < half.x && abs(uv.y - bands.y) < half.y)
                {
                    float t = (uv.y - (bands.y - half.y)) / (half.y * 2.0);
                    #if UNITY_UV_STARTS_AT_TOP
                    t = 1.0 - t;
                    #endif
                    int band = (int)floor(saturate(t) * 4.999);
                    value = band == 4 ? 1.0 : band == 3 ? 0.5 : band == 2 ? 0.25 : band == 1 ? 0.1 : 0.03;
                }
                return float4(value, value, value, 1.0);
            }
            ENDHLSL
        }
    }
}
