// HullWreck.hlsl
// GoF2 remake (2026-11-11, after Chorus's wreck materials: MF_WreckBurn's Burn_Amount / Burn_Temperature / Burn_Tiling and
// the masters' Dissolve / DissolveWidth / DissolveBoost, which its wrecks' anim blueprints drive over the wreck's life):
// a wreck piece's burn and disintegration (World.WreckBreakup sets _WreckFx / _WreckBurnColor per piece with a property
// block). The pattern is value noise in the piece's object space (it sticks to the piece as it turns), _WreckFx.z cells a
// unit. Burn: _WreckFx.x of the piece glows where the noise is high (more of it as the amount rises), in _WreckBurnColor.
// Disintegration: what the noise leaves under _WreckFx.y is cut away (every depth pass alike), its edge glowing over
// _WreckFx.w of the noise at twice the burn colour (DissolveBoost 2). The hot spots, the noise's highs, go last.
// With a burn map (_WreckBurn.z > 0; 2026-11-11, MF_WreckBurn's compiled base pass, M_WreckBurn / MI_MountWreck_Burn01):
// the burn is Chorus's own: b = saturate((saturate(the map's R at UV0 (or UV1) x Burn_NoiseTiling + Burn_Noise_Offset) x
// saturate(1 - the vertex colour's red) - (0.975 - Burn_Amount)) x Burn_Falloff) (Burn_Amount _WreckFx.x, falling as it
// cools: the burn shrinks to the map's highs); its glow the blackbody at b x Burn_Temperature (Chorus's Planckian-locus
// fit) x (b T / 2500)^4, scaled so a full burn's brightest channel is _WreckBurnColor's; the burnt part rough (roughness
// lerp(r, 1, b)). The cut keeps the value noise (the depth passes carry no UVs).
#ifndef GOF2_HULL_WRECK_INCLUDED
#define GOF2_HULL_WRECK_INCLUDED

float HullWreckHash(float3 p)
{
    p = frac(p * 0.3183099 + 0.1);
    p *= 17.0;
    return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
}

float HullWreckNoise(float3 x)
{
    float3 i = floor(x);
    float3 f = frac(x);
    f = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(lerp(HullWreckHash(i), HullWreckHash(i + float3(1, 0, 0)), f.x),
                     lerp(HullWreckHash(i + float3(0, 1, 0)), HullWreckHash(i + float3(1, 1, 0)), f.x), f.y),
                lerp(lerp(HullWreckHash(i + float3(0, 0, 1)), HullWreckHash(i + float3(1, 0, 1)), f.x),
                     lerp(HullWreckHash(i + float3(0, 1, 1)), HullWreckHash(i + float3(1, 1, 1)), f.x), f.y), f.z);
}

// 0..1, two octaves, in the piece's object space
float HullWreckPattern(float3 positionWS)
{
    float3 p = TransformWorldToObject(positionWS) * _WreckFx.z;
    return HullWreckNoise(p) * 0.65 + HullWreckNoise(p * 2.7 + 17.0) * 0.35;
}

// the disintegrated part cut away
void HullWreckClip(float3 positionWS)
{
    if (_WreckFx.y > 0.0) clip(HullWreckPattern(positionWS) - _WreckFx.y);
}

// Chorus's blackbody (MF_WreckBurn's: the Planckian locus's uv at 'k' kelvin, to xy, to linear sRGB at luminance 1)
float3 HullWreckBlackbody(float k)
{
    float k2 = k * k;
    float u = (0.860117757 + 1.54118254e-4 * k + 1.28641212e-7 * k2) / (1.0 + 8.42420235e-4 * k + 7.08145163e-7 * k2);
    float v = (0.317398726 + 4.22806245e-5 * k + 4.20481691e-8 * k2) / (1.0 - 2.89741816e-5 * k + 1.61456053e-7 * k2);
    float d = 2.0 * u - 8.0 * v + 4.0;
    float x = 3.0 * u / d, y = 2.0 * v / d;
    float3 xyz = float3(x / y, 1.0, (1.0 - x - y) / y);
    return float3(dot(float3(3.2404542, -1.5371385, -0.4985314), xyz), dot(float3(-0.969266, 1.8760108, 0.041556), xyz),
                  dot(float3(0.0556434, -0.2040259, 1.0572252), xyz)) * pow(k * 4e-4, 4.0);
}

// the share of the piece burning here (the burn map; see the header)
half HullWreckBurn(float2 uv0, float2 uv1, half vertexRed)
{
    float flags = _WreckBurn.w;
    half n = SAMPLE_TEXTURE2D(_WreckBurnMap, sampler_LinearRepeat, (flags >= 1.5 ? uv1 : uv0) * _WreckBurn.x).r;
    half keep = fmod(flags, 2.0) > 0.5 ? saturate(1.0h - vertexRed) : 1.0h;
    return saturate((saturate(n + (half)_WreckBurn.y) * keep - (0.975h - (half)_WreckFx.x)) * (half)_WreckBurn.z);
}

// its glow: the blackbody at b x the temperature, a full burn's brightest channel at _WreckBurnColor's
half3 HullWreckBurnGlow(half b)
{
    if (b <= 0.0h) return 0;
    float t = max(_WreckBurnColor.a, 1.0);
    float3 full = max(HullWreckBlackbody(t), 0.0);
    float top = max(max(full.r, full.g), max(full.b, 1e-6));
    float peak = max(max(_WreckBurnColor.r, _WreckBurnColor.g), _WreckBurnColor.b);
    return (half3)(max(HullWreckBlackbody(b * t), 0.0) * (peak / top));
}

// the burn's and the cut edge's glow ('noiseBurn' false: the edge only, the burn map making the burn)
half3 HullWreckEmission(float3 positionWS, bool noiseBurn = true)
{
    if (_WreckFx.x <= 0.0 && _WreckFx.y <= 0.0) return 0;
    float n = HullWreckPattern(positionWS);
    half heat = noiseBurn ? (half)(smoothstep(0.75 - 0.35 * _WreckFx.x, 0.95, n) * _WreckFx.x) : 0.0h;
    half edge = _WreckFx.y > 0.0 ? (half)(1.0 - saturate((n - _WreckFx.y) / max(_WreckFx.w, 1e-3))) : 0;
    return (half3)_WreckBurnColor.rgb * (heat + edge * 2.0h);
}

// a depth pass's world position from its pixel (their varyings carry none)
float3 HullWreckPixelToWorld(float4 positionCS)
{
    return ComputeWorldSpacePosition(positionCS.xy / _ScaledScreenParams.xy, positionCS.z, UNITY_MATRIX_I_VP);
}

#endif
