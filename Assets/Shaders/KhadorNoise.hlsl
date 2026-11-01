// KhadorNoise.hlsl: the noise of the Khador Drive's animation (GoF2/KhadorFx, GoF2/KhadorRift).
#ifndef GOF2_KHADOR_NOISE
#define GOF2_KHADOR_NOISE

float KhadorHash(float2 p)
{
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return frac(p.x * p.y);
}

float KhadorNoise(float2 p)
{
    float2 i = floor(p), f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(KhadorHash(i), KhadorHash(i + float2(1, 0)), f.x),
                lerp(KhadorHash(i + float2(0, 1)), KhadorHash(i + float2(1, 1)), f.x), f.y);
}

float KhadorFbm(float2 p)
{
    return KhadorNoise(p) * 0.55 + KhadorNoise(p * 2.13 + 3.7) * 0.3 + KhadorNoise(p * 4.41 + 9.2) * 0.15;
}

// How far the ring's edge strays at 'turn' (0..1 round it), -1..1: wraps round the circle, and leaps as lightning does.
float KhadorJag(float turn, float seconds)
{
    float2 round = float2(cos(turn * 6.2831853), sin(turn * 6.2831853));
    float step = floor(seconds * 18.0);
    return (KhadorFbm(round * 2.2 + float2(step * 0.37, seconds * 1.4)) - 0.5) * 2.0;
}

// The tear's own shape: its edge at 'turn', as a share of its radius: slow torn lobes and the lightning's leaping on them, so
// the hole, the ring and the web's cut-out all follow one rim. (World.KhadorFx.RimLeast and RimMost hold its reach.)
float KhadorRim(float turn, float seconds)
{
    float2 round = float2(cos(turn * 6.2831853), sin(turn * 6.2831853));
    float lobes = clamp((KhadorFbm(round * 0.95 + float2(seconds * 0.22, 5.1)) - 0.5) * 2.6, -1.0, 1.0);
    return 1.0 + 0.16 * lobes + 0.06 * KhadorJag(turn, seconds);
}

#endif
