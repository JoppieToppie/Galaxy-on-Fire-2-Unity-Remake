// WarpWebCore.hlsl: the warp's vein web (GoF2/WarpWeb), shared with GoF2/KhadorRift, whose hole in space shows it already.
#ifndef GOF2_WARP_WEB_CORE
#define GOF2_WARP_WEB_CORE

TEXTURE2D(_WarpArcs); SAMPLER(sampler_WarpArcs);

half3 WarpWeb(float3 view, float3 axis, float level, float khador, float time)
{
    float3 x = normalize(cross(axis, abs(axis.y) < 0.9 ? float3(0, 1, 0) : float3(1, 0, 0)));
    float3 y = cross(axis, x);
    float ring = acos(clamp(dot(view, axis), -1.0, 1.0)) / PI;   // 0 ahead, 1 behind
    float around = atan2(dot(view, y), dot(view, x)) / (2.0 * PI) + 0.5;
    float u = abs(frac(around * 3.0 + time * 0.06) * 2.0 - 1.0);
    float v = abs(frac(ring * 2.2 - time * 0.3) * 2.0 - 1.0);
    half3 web = SAMPLE_TEXTURE2D_LOD(_WarpArcs, sampler_WarpArcs, float2(0.47 + u * 0.19, 0.62 + v * 0.18), 0).rgb;
    web *= smoothstep(0.03, 0.14, ring) * (1.0 - smoothstep(0.32, 0.62, ring));
    return web * smoothstep(0.02, 0.4, ring) * level * 1.4 * khador;
}

#endif
