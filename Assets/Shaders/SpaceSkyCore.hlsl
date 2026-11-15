// SpaceSkyCore.hlsl: GoF2/SpaceSky's sky and warp, shared with GoF2/KhadorRift (World.KhadorFx), whose hole in space shows the
// warp the next level opens on: the same code, so the two match to the pixel. A material of either has the same properties.
#ifndef GOF2_SPACE_SKY_CORE
#define GOF2_SPACE_SKY_CORE

TEXTURECUBE(_Stars);  SAMPLER(sampler_Stars);
TEXTURECUBE(_Nebula); SAMPLER(sampler_Nebula);
TEXTURE2D(_StarsMap);  SAMPLER(sampler_StarsMap);
TEXTURE2D(_NebulaMap); SAMPLER(sampler_NebulaMap);
TEXTURE2D(_WarpNoise);  SAMPLER(sampler_WarpNoise);
TEXTURE2D(_WarpDetail); SAMPLER(sampler_WarpDetail);
TEXTURECUBE(_BlendStars);  SAMPLER(sampler_BlendStars);
TEXTURECUBE(_BlendNebula); SAMPLER(sampler_BlendNebula);
TEXTURE2D(_BlendStarsMap);  SAMPLER(sampler_BlendStarsMap);
TEXTURE2D(_BlendNebulaMap); SAMPLER(sampler_BlendNebulaMap);
TEXTURE2D(_ChorusStarsMap); SAMPLER(sampler_ChorusStarsMap);
TEXTURE2D(_ChorusLayer1); SAMPLER(sampler_ChorusLayer1);
TEXTURE2D(_ChorusLayer2); SAMPLER(sampler_ChorusLayer2);
TEXTURE2D(_ChorusLayer3); SAMPLER(sampler_ChorusLayer3);
TEXTURE2D(_ChorusLayer4); SAMPLER(sampler_ChorusLayer4);
float _WarpLevel, _WarpTime, _WarpKhador;
float3 _WarpAxis;
float _SkyBlend, _BlendStarsGain, _BlendNebulaGain, _BlendExposure, _BlendStarsMode, _BlendNebulaMode;
float4x4 _BlendRotation;
float4 _BlendStarsTexel, _BlendNebulaTexel;

CBUFFER_START(UnityPerMaterial)
    float4x4 _SkyRotation;   // world direction -> baked cube direction, set by OrbitBuilder.SetupSky
    float4 _StarsMap_TexelSize;
    float4 _NebulaMap_TexelSize;
    half _Exposure, _StarsGain, _NebulaGain;
    // Chorus's sky (_CHORUS_SKY, ChorusSky below; OrbitBuilder.SetChorusSky fills them from a systems.json skybox "chorus")
    float4 _ChorusGradUp;        // the gradient's up (Unreal axes, normalized), its bias
    float4 _ChorusGradA, _ChorusGradB;   // colour A, gamma; colour B, opacity (0: no gradient)
    float4 _ChorusStars;         // tiling, offset u, offset v, brightness
    float4 _ChorusCurve0, _ChorusCurve1;   // the stars' curve: rgb at its first key, its time; rgb at its last, its time
    float4 _ChorusMisc;          // stars exponent, overall emissive, layer count
    float4 _ChorusL1A, _ChorusL1B, _ChorusL1C, _ChorusL2A, _ChorusL2B, _ChorusL2C;   // per layer: sin / cos heading, sin / cos
    float4 _ChorusL3A, _ChorusL3B, _ChorusL3C, _ChorusL4A, _ChorusL4B, _ChorusL4C;   // elevation; scale, cos / sin rotation,
                                                                                    // emissive; exponent, opacity
CBUFFER_END

// Equirectangular: u around (0.5 = +Z, 0.75 = +X), v up (0.5 = the horizon).
float2 PanoramaUV(float3 d)
{
    return float2(atan2(d.x, d.z) * (0.5 / PI) + 0.5, asin(clamp(d.y, -1.0, 1.0)) / PI + 0.5);
}

// A 6:1 strip: the face and its coordinates (u to the right, v up as seen from inside), kept half a texel in.
float2 StripUV(float3 d, float4 texel)
{
    float3 a = abs(d);
    float face; float2 f;
    if (a.x >= a.y && a.x >= a.z)
    {
        if (d.x > 0) { face = 0; f = float2(-d.z, d.y) / a.x; }   // right: its right is -Z
        else { face = 1; f = float2(d.z, d.y) / a.x; }            // left: its right is +Z
    }
    else if (a.y >= a.z)
    {
        if (d.y > 0) { face = 2; f = float2(d.x, -d.z) / a.y; }   // up: the front at the bottom
        else { face = 3; f = float2(d.x, d.z) / a.y; }            // down: the front at the top
    }
    else
    {
        if (d.z > 0) { face = 4; f = float2(d.x, d.y) / a.z; }    // front
        else { face = 5; f = float2(-d.x, d.y) / a.z; }           // back: its right is -X
    }
    float2 uv = f * 0.5 + 0.5;
    float inset = 0.5 * texel.y;   // half a texel of the face (its height is the image's)
    uv = clamp(uv, inset, 1.0 - inset);
    return float2((face + uv.x) / 6.0, uv.y);
}

half3 Layer(TEXTURECUBE_PARAM(cube, cubeSampler), TEXTURE2D_PARAM(map, mapSampler), float4 texel, float3 d, int mode)
{
    if (mode == 1) return SAMPLE_TEXTURE2D_LOD(map, mapSampler, PanoramaUV(normalize(d)), 0).rgb;
    if (mode == 2) return SAMPLE_TEXTURE2D_LOD(map, mapSampler, StripUV(d, texel), 0).rgb;
    return SAMPLE_TEXTURECUBE(cube, cubeSampler, d).rgb;
}


// Chorus's sky, as its M_Skybox_MASTER draws it (its compiled shader, MI_Skybox_LocRSA's pixel shader and its variants with a
// fourth layer / no gradient; the importer's tools/sky.py, the same formula in numpy for the baked panorama): a gradient
// along an up vector, a tiled star mask through a curve, then up to four nebula / galaxy images, each a patch of the sky
// placed by heading, elevation, scale and rotation, laid over what is under it by its alpha. Worked in Unreal's axes (x
// forward, y right, z up: Unity's z, x, y, no mirror). Chorus's exposure compensation only cancels its auto exposure,
// which the remake hasn't: left out. Its sky sphere (SM_Sphere) maps the stars by its 16 x 16 UV sphere about its own
// axis (Unreal's y): here the exact sphere, not its facets.
half ChorusStarMask(float3 v)   // the star mask toward v: the sky sphere's UV (Unreal's, v down), offset and tiled
{
    float a = 0.75 - atan2(v.z, v.x) * (0.5 / PI);
    float2 uv = (float2(frac(a), 0.5 + asin(clamp(v.y, -1.0, 1.0)) / PI) + _ChorusStars.yz) * _ChorusStars.x;
    // the sphere's seam (u 1 -> 0) has its own vertices in Chorus: its gradients here from u unwrapped where that is smoother
    float2 ga = float2(ddx(a), ddy(a)), gb = float2(ddx(frac(a)), ddy(frac(a)));
    float2 du = float2(abs(ga.x) <= abs(gb.x) ? ga.x : gb.x, abs(ga.y) <= abs(gb.y) ? ga.y : gb.y) * _ChorusStars.x;
    float2 dv = float2(ddx(uv.y), ddy(uv.y));
    return SAMPLE_TEXTURE2D_GRAD(_ChorusStarsMap, sampler_ChorusStarsMap, float2(uv.x, 1.0 - uv.y), float2(du.x, -dv.x), float2(du.y, -dv.y)).r;
}

half3 ChorusLayer(TEXTURE2D_PARAM(map, s), float3 d, float4 a, float4 b, float4 c, half3 under)
{
    // heading: about the sphere's axis, as the material's RotateAboutAxis nodes in its own order
    float3 p = float3(d.y * a.x + d.z * a.y, d.x, d.y * a.y - d.z * a.x);
    float3 q = normalize(float3(p.z, p.x * a.w - p.y * a.z, p.x * a.z + p.y * a.w));
    float u = (atan2(q.x, -q.y) / PI + 1.0) * 0.5;
    float v = acos(clamp(q.z, -1.0, 1.0)) / PI;
    float2 k = float2((2.0 * u - 1.0) / b.x, (v - 0.5) / b.x);
    float2 r = float2(k.x * b.y - k.y * b.z, k.x * b.z + k.y * b.y);
    half4 t = SAMPLE_TEXTURE2D(map, s, float2(r.x + 0.5, 0.5 + r.y));
    half3 e = t.rgb * b.w;
    half3 col = t.rgb + pow(max(e, 1e-6), c.x) * step(1e-6, e);   // (pow of 0 or less: 0)
    float inside = saturate((1.0 - 2.0 * abs(r.x)) * 1e5) * saturate((1.0 - 2.0 * abs(r.y)) * 1e5);
    return lerp(under, col, t.a * c.y * inside);
}

half3 ChorusSky(float3 w)
{
    float3 v = normalize(float3(w.z, w.x, w.y));   // Unreal's axes
    float3 d = -v;   // the sphere's inward normal: what the gradient and the layers read
    half3 c = 0;
    if (_ChorusGradB.w > 0)
    {
        float g = dot(d, _ChorusGradUp.xyz) + _ChorusGradUp.w;
        g = g <= 0 ? 0 : pow(g, _ChorusGradA.w);
        c = lerp(_ChorusGradA.rgb, _ChorusGradB.rgb, min(g, 1.0)) * g * _ChorusGradB.w;
    }
    float m = ChorusStarMask(v);
    float3 curve = lerp(_ChorusCurve0.rgb, _ChorusCurve1.rgb, saturate((m - _ChorusCurve0.w) / max(_ChorusCurve1.w - _ChorusCurve0.w, 1e-5)));
    float3 stars = pow(max(curve, 1e-6), _ChorusMisc.x) * step(1e-6, curve);
    c += dot(stars * _ChorusStars.w, 1.0);   // (the sphere's vertex colours: none, white)
    int n = (int)_ChorusMisc.z;
    if (n > 0) c = ChorusLayer(TEXTURE2D_ARGS(_ChorusLayer1, sampler_ChorusLayer1), d, _ChorusL1A, _ChorusL1B, _ChorusL1C, c);
    if (n > 1) c = ChorusLayer(TEXTURE2D_ARGS(_ChorusLayer2, sampler_ChorusLayer2), d, _ChorusL2A, _ChorusL2B, _ChorusL2C, c);
    if (n > 2) c = ChorusLayer(TEXTURE2D_ARGS(_ChorusLayer3, sampler_ChorusLayer3), d, _ChorusL3A, _ChorusL3B, _ChorusL3C, c);
    if (n > 3) c = ChorusLayer(TEXTURE2D_ARGS(_ChorusLayer4, sampler_ChorusLayer4), d, _ChorusL4A, _ChorusL4B, _ChorusL4C, c);
    return max(c, 0) * _ChorusMisc.y;
}

// The sky toward the world direction 'world'.
half3 Sky(float3 world)
{
    float3 d = mul((float3x3)_SkyRotation, world);
    #if defined(_CHORUS_SKY)
    half3 c = ChorusSky(d);
    #else
    #if defined(_STARS_PANORAMA)
        const int starsMode = 1;
    #elif defined(_STARS_STRIP)
        const int starsMode = 2;
    #else
        const int starsMode = 0;
    #endif
    #if defined(_NEBULA_PANORAMA)
        const int nebulaMode = 1;
    #elif defined(_NEBULA_STRIP)
        const int nebulaMode = 2;
    #else
        const int nebulaMode = 0;
    #endif
    half3 c = Layer(TEXTURECUBE_ARGS(_Stars, sampler_Stars), TEXTURE2D_ARGS(_StarsMap, sampler_StarsMap), _StarsMap_TexelSize, d, starsMode) * _StarsGain
            + Layer(TEXTURECUBE_ARGS(_Nebula, sampler_Nebula), TEXTURE2D_ARGS(_NebulaMap, sampler_NebulaMap), _NebulaMap_TexelSize, d, nebulaMode) * _NebulaGain;
    #endif
    if (_SkyBlend > 0.001)
    {
        float3 e = mul((float3x3)_BlendRotation, world);
        half3 other = Layer(TEXTURECUBE_ARGS(_BlendStars, sampler_BlendStars), TEXTURE2D_ARGS(_BlendStarsMap, sampler_BlendStarsMap), _BlendStarsTexel, e, (int)_BlendStarsMode) * _BlendStarsGain
                    + Layer(TEXTURECUBE_ARGS(_BlendNebula, sampler_BlendNebula), TEXTURE2D_ARGS(_BlendNebulaMap, sampler_BlendNebulaMap), _BlendNebulaTexel, e, (int)_BlendNebulaMode) * _BlendNebulaGain;
        c = lerp(c, other * _BlendExposure / max(_Exposure, 0.001), _SkyBlend);
    }
    return c;
}

half3 Warp(float3 view, float3 axis, float level, float khador)
{
    float3 x = normalize(cross(axis, abs(axis.y) < 0.9 ? float3(0, 1, 0) : float3(1, 0, 0)));
    float3 y = cross(axis, x);
    float ring = acos(clamp(dot(view, axis), -1.0, 1.0)) / PI;   // 0 ahead, 1 behind
    float turn = atan2(dot(view, y), dot(view, x));
    float around = turn / (2.0 * PI) + 0.5;
    float3 out_ = x * cos(turn) + y * sin(turn);
    float amount = smoothstep(0.02, 0.4, ring) * level;     // the calm centre
    half3 sky = 0;
    for (int k = 0; k < 2; k++)
    {
        float phase = frac(_WarpTime * 0.35 + k * 0.5);
        float weight = 1.0 - abs(2.0 * phase - 1.0);
        float jitter = SAMPLE_TEXTURE2D_LOD(_WarpNoise, sampler_WarpNoise, float2(around * 2.0, 0.13 + k * 0.37), 0).r;
        float stretched = ring * 0.16 + 0.12 + (phase - 0.5) * 0.3 + (jitter - 0.5) * 0.18;
        float angle = lerp(ring, stretched, amount) * PI;
        sky += Sky(axis * cos(angle) + out_ * sin(angle)) * weight;
    }
    float bands = SAMPLE_TEXTURE2D_LOD(_WarpNoise, sampler_WarpNoise, float2(around * 6.0, ring * 1.1 - _WarpTime * 0.5), 0).r
                * SAMPLE_TEXTURE2D_LOD(_WarpDetail, sampler_WarpDetail, float2(around * 3.0 + 0.3, ring * 0.7 - _WarpTime * 0.8), 0).r;
    sky *= lerp(1.0, 0.6 + 0.4 * smoothstep(0.08, 0.5, bands), amount);   // shade only: never brighter than the sky
    sky *= 1.0 - level * 0.4 * (1.0 - smoothstep(0.0, 0.2, ring));
    sky = lerp(sky, dot(sky, half3(0.3, 0.6, 0.1)) * half3(0.62, 0.8, 1.25), amount * 0.4 * khador);   // the Khador fx's ice blue
    return sky;
}

#endif
