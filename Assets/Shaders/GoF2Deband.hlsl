// GoF2Deband.hlsl: the original's sky, fog and planet textures were RGB565 (5-bit red / blue, 6-bit green), so their
// dark gradients are steps of 8 / 4 (of 255): almost every dark texel of nebula_000-002 is 0, 4, 8, 12, 16 or 20. SDR
// keeps those steps in near-black where they don't show; HDR output lifts them into view as hard edges and green patches
// (S24 Ultra, 2026-11-28). While HDR is on (_GoF2Deband = 1, Bootstrap.ApplyHdr) a screen-space triangular dither of one
// step, in the texture's own sRGB terms, breaks the edges into grain finer than a phone screen shows. Pure black stays
// black (the dither fades out over the first step), so empty space stays off on an OLED. Off: the colour as it was.
#ifndef GOF2_DEBAND_INCLUDED
#define GOF2_DEBAND_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

float _GoF2Deband;

half3 GoF2Deband(half3 color, float2 pixel)
{
    if (_GoF2Deband < 0.5) return color;
    // Interleaved gradient noise (Jimenez 2014), two decorrelated samples summed: triangular, -1..1.
    float n1 = frac(52.9829189 * frac(dot(pixel, float2(0.06711056, 0.00583715))));
    float n2 = frac(52.9829189 * frac(dot(pixel + float2(47.0, 17.0), float2(0.06711056, 0.00583715))));
    float3 s = LinearToSRGB(max(float3(color), 0.0));
    float3 step565 = float3(8.0, 4.0, 8.0) / 255.0;
    s += (n1 + n2 - 1.0) * step565 * saturate(s / step565);
    return half3(SRGBToLinear(max(s, 0.0)));
}

#endif
