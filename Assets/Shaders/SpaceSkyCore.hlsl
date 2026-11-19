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
TEXTURE2D(_ChorusLayer5); SAMPLER(sampler_ChorusLayer5);
TEXTURE2D(_ChorusLayer6); SAMPLER(sampler_ChorusLayer6);
TEXTURE2D(_StarIndex); SAMPLER(sampler_StarIndex);
TEXTURE2D(_StarData);  SAMPLER(sampler_StarData);
TEXTURE2D(_BlendStarIndex); SAMPLER(sampler_BlendStarIndex);
TEXTURE2D(_BlendStarData);  SAMPLER(sampler_BlendStarData);
float _WarpLevel, _WarpTime, _WarpKhador;
float3 _WarpAxis;
float _SkyBlend, _BlendStarsGain, _BlendNebulaGain, _BlendExposure, _BlendStarsMode, _BlendNebulaMode;
float4x4 _BlendRotation;
float4 _BlendStarsTexel, _BlendNebulaTexel;
float4 _BlendStarGrid;   // the other sky's _StarGrid (InterstellarField.LoadBlendSky)

CBUFFER_START(UnityPerMaterial)
    float4x4 _SkyRotation;   // world direction -> baked cube direction, set by OrbitBuilder.SetupSky
    float4 _StarsMap_TexelSize;
    float4 _NebulaMap_TexelSize;
    half _Exposure, _StarsGain, _NebulaGain;
    float4 _StarGrid;            // Enhanced skies' stars (StarPoints, World.SkyStars): grid cells a face, data width; 0: off
    // Chorus's sky (_CHORUS_SKY, ChorusSky below; OrbitBuilder.SetChorusSky fills them from a systems.json skybox "chorus")
    float4 _ChorusGradUp;        // the gradient's up (Unreal axes, normalized), its bias
    float4 _ChorusGradA, _ChorusGradB;   // colour A, gamma; colour B, opacity (0: no gradient)
    float4 _ChorusStars;         // tiling, offset u, offset v, brightness
    float4 _ChorusCurve0, _ChorusCurve1;   // the stars' curve: rgb at its first key, its time; rgb at its last, its time
    float4 _ChorusMisc;          // stars exponent, overall emissive, layer count
    float4 _ChorusStarsB;        // the stars' v tiling (0: as u), ES2's clustering noise scale (0: none) and exponent, 1: the
                                 // game's / the images' layers under it all (Sky's base)
    float4 _ChorusStarsW;        // the star mask's channel weights (Chorus: r; ES2: r + g / 2 + b / 4)
    float4 _ChorusSun;           // toward the sun (Unreal axes, the sky's frame); _ChorusSunColor: its colour
    float4 _ChorusSunColor;
    // per layer (ChorusLayer): A, B, C, D, E; D.x its kind (0 image patch, 1 tinted mask, 2 gradient)
    float4 _ChorusL1A, _ChorusL1B, _ChorusL1C, _ChorusL1D, _ChorusL1E, _ChorusL2A, _ChorusL2B, _ChorusL2C, _ChorusL2D, _ChorusL2E;
    float4 _ChorusL3A, _ChorusL3B, _ChorusL3C, _ChorusL3D, _ChorusL3E, _ChorusL4A, _ChorusL4B, _ChorusL4C, _ChorusL4D, _ChorusL4E;
    float4 _ChorusL5A, _ChorusL5B, _ChorusL5C, _ChorusL5D, _ChorusL5E, _ChorusL6A, _ChorusL6B, _ChorusL6C, _ChorusL6D, _ChorusL6E;
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


// Remake: the Enhanced skies' stars (Settings.EnhancedSkies, World.SkyStars): the point stars and specks lifted out of the
// baked layers into star lists (Reference/tools/sky/enhance_skies.py), each a gaussian of its own size, drawn sharper (0.6 of
// it) down to 0.6 of a screen pixel, so sharp at any resolution; a star drawn smaller than its source is brighter by the
// area it lost, at most twice (a pin-sharp star keeps its light, but doesn't flare into the bloom). The grid's 2 x 2 cells
// round the pixel hold every star within half a cell (SkyStars.Cells); at a face's edge the clamped cell is read once.
half3 StarPoints(TEXTURE2D_PARAM(index, si), TEXTURE2D_PARAM(data, sd), float4 grid, float3 d, half starsGain, half nebulaGain)
{
    float3 n = normalize(d);
    float pix = max(max(length(ddx(n)), length(ddy(n))), 1e-6);
    float3 a = abs(n);
    int face; float2 f;
    if (a.x >= a.y && a.x >= a.z) { face = n.x > 0 ? 0 : 1; f = float2(n.x > 0 ? -n.z : n.z, n.y) / a.x; }
    else if (a.y >= a.z) { face = n.y > 0 ? 2 : 3; f = float2(n.x, n.y > 0 ? -n.z : n.z) / a.y; }
    else { face = n.z > 0 ? 4 : 5; f = float2(n.z > 0 ? n.x : -n.x, n.y) / a.z; }
    int g = (int)grid.x, w = (int)grid.y;
    int2 b = (int2)floor((f * 0.5 + 0.5) * grid.x - 0.5);
    int2 c0 = clamp(b, 0, g - 1), c1 = clamp(b + 1, 0, g - 1);
    half3 c = 0;
    [unroll] for (int k = 0; k < 4; k++)
    {
        bool right = (k & 1) != 0, upper = (k >> 1) != 0;
        if ((right && c1.x == c0.x) || (upper && c1.y == c0.y)) continue;
        int2 cell = int2(right ? c1.x : c0.x, upper ? c1.y : c0.y);
        float4 e = LOAD_TEXTURE2D(index, int2(face * g + cell.x, cell.y)) * 255.0 + 0.5;
        int2 ids = int2((int)e.r * 256 + (int)e.g, (int)e.b * 256 + (int)e.a);
        [unroll] for (int j = 0; j < 2; j++)
        {
            int id = j == 0 ? ids.x : ids.y;
            if (id <= 0) continue;
            int t = (id - 1) * 2;
            int2 at = int2(t % w, t / w);
            float4 s = LOAD_TEXTURE2D(data, at);
            float4 col = LOAD_TEXTURE2D(data, at + int2(1, 0));
            float sg = max(s.w * 0.6, pix * 0.6);
            float3 off = n - s.xyz;
            float lit = exp(-dot(off, off) / (2.0 * sg * sg)) * min(s.w * s.w / (sg * sg), 2.0);
            c += col.rgb * lit * (col.a > 0.5 ? nebulaGain : starsGain);
        }
    }
    return c;
}

// The layered sky (_CHORUS_SKY; OrbitBuilder.SetChorusSky from a systems.json skybox "chorus" block): Chorus's sky as its
// M_Skybox_MASTER draws it, and EVERSPACE 2's sky methods with it (2026-11-14, the user: "combine es2s methods with chorus
// methods for the skies"). Chorus (its compiled shader, MI_Skybox_LocRSA's pixel shader and its variants with a fourth layer /
// no gradient; the importer's tools/sky.py, the same formula in numpy for the baked panorama): a gradient along an up vector,
// a tiled star mask through a curve, then nebula / galaxy images, each a patch of the sky placed by heading, elevation, scale
// and rotation, laid over what is under it by its alpha. Worked in Unreal's axes (x forward, y right, z up: Unity's z, x,
// y, no mirror). Chorus's exposure compensation only cancels its auto exposure, which the remake hasn't: left out. Its sky
// sphere (SM_Sphere) maps the stars by its 16 x 16 UV sphere about its own axis (Unreal's y): here the exact sphere.
// EVERSPACE 2 (its compiled materials, 2026-11-14): its star mask's three channels are three star layers (r + g / 2 + b / 4,
// M_Skybox_Stars) thinned by a 3D noise over the sky (^2.5), so they cluster; its nebulae are greyscale masks tinted by the
// system's colour and brightened toward the sun (M_Nebula_Hemisphere_Parent, M_Nebula_Local_Parent: kind 1 below, placed as
// Chorus's patches); its gradient (M_Skybox_Gradient) a smoothstep along an axis, folded (Absolute) or flipped (Invert),
// through Schlick's bias and a power (kind 2). Up to 6 layers; _ChorusStarsB.w keeps the game's / the images' layers under.
float3 NoiseHash(float3 p)
{
    p = float3(dot(p, float3(127.1, 311.7, 74.7)), dot(p, float3(269.5, 183.3, 246.1)), dot(p, float3(113.5, 271.9, 124.6)));
    return frac(sin(p) * 43758.5453) * 2.0 - 1.0;
}

float GradientNoise(float3 p)   // about -1..1, as Unreal's gradient noise
{
    float3 i = floor(p), f = frac(p);
    float3 u = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);
    float n000 = dot(NoiseHash(i), f), n100 = dot(NoiseHash(i + float3(1, 0, 0)), f - float3(1, 0, 0));
    float n010 = dot(NoiseHash(i + float3(0, 1, 0)), f - float3(0, 1, 0)), n110 = dot(NoiseHash(i + float3(1, 1, 0)), f - float3(1, 1, 0));
    float n001 = dot(NoiseHash(i + float3(0, 0, 1)), f - float3(0, 0, 1)), n101 = dot(NoiseHash(i + float3(1, 0, 1)), f - float3(1, 0, 1));
    float n011 = dot(NoiseHash(i + float3(0, 1, 1)), f - float3(0, 1, 1)), n111 = dot(NoiseHash(i + float3(1, 1, 1)), f - float3(1, 1, 1));
    return lerp(lerp(lerp(n000, n100, u.x), lerp(n010, n110, u.x), u.y), lerp(lerp(n001, n101, u.x), lerp(n011, n111, u.x), u.y), u.z) * 1.6;
}

half ChorusStarMask(float3 v)   // the star mask toward v: the sky sphere's UV (Unreal's, v down), offset and tiled
{
    float a = 0.75 - atan2(v.z, v.x) * (0.5 / PI);
    float2 tiling = float2(_ChorusStars.x, _ChorusStarsB.x > 0 ? _ChorusStarsB.x : _ChorusStars.x);
    float2 uv = (float2(frac(a), 0.5 + asin(clamp(v.y, -1.0, 1.0)) / PI) + _ChorusStars.yz) * tiling;
    // the sphere's seam (u 1 -> 0) has its own vertices in Chorus: its gradients here from u unwrapped where that is smoother
    float2 ga = float2(ddx(a), ddy(a)), gb = float2(ddx(frac(a)), ddy(frac(a)));
    float2 du = float2(abs(ga.x) <= abs(gb.x) ? ga.x : gb.x, abs(ga.y) <= abs(gb.y) ? ga.y : gb.y) * tiling.x;
    float2 dv = float2(ddx(uv.y), ddy(uv.y));
    half3 t = SAMPLE_TEXTURE2D_GRAD(_ChorusStarsMap, sampler_ChorusStarsMap, float2(uv.x, 1.0 - uv.y), float2(du.x, -dv.x), float2(du.y, -dv.y)).rgb;
    half m = saturate(dot(t, (half3)_ChorusStarsW.rgb));
    if (_ChorusStarsB.y > 0)   // ES2: thinned by a noise over the sky, so the stars cluster
    {
        float n = GradientNoise(v * _ChorusStarsB.y) * 0.5 + 0.5;
        m *= pow(saturate(n), _ChorusStarsB.z);
    }
    return m;
}

half3 ChorusLayer(TEXTURE2D_PARAM(map, s), float3 d, float4 a, float4 b, float4 c, float4 e, float4 f, half3 under)
{
    if (e.x > 1.5)   // ES2's gradient: a = its axis, offset; b = min, max, Schlick's (1 / bias - 2), power; c = colour, opacity;
    {                // e.y Absolute, e.z Invert, e.w added instead. Read along the view (-d, the sphere's outward normal, as ES2's sky sphere).
        float g = saturate((dot(-d, a.xyz) + 1.0) * 0.5 + a.w);
        g = lerp(g, 1.0 - abs(2.0 * g - 1.0), e.y);
        g = lerp(g, 1.0 - g, e.z);
        g = saturate((g - b.x) / max(b.y - b.x, 1e-5));
        g = g * g * (3.0 - 2.0 * g);
        g = g / ((1.0 - g) * b.z + 1.0);
        g = g <= 0 ? 0 : min(pow(g, b.w), 1.0);
        if (e.w > 0.5) return under + (half3)c.rgb * (g * c.w);   // (added: a glowing band)
        return lerp(under, (half3)c.rgb, g * c.w);
    }
    // heading: about the sphere's axis, as Chorus's RotateAboutAxis nodes in its own order
    float3 p = float3(d.y * a.x + d.z * a.y, d.x, d.y * a.y - d.z * a.x);
    float3 q = normalize(float3(p.z, p.x * a.w - p.y * a.z, p.x * a.z + p.y * a.w));
    float u = (atan2(q.x, -q.y) / PI + 1.0) * 0.5;
    float v = acos(clamp(q.z, -1.0, 1.0)) / PI;
    float2 k = float2((2.0 * u - 1.0) / b.x, (v - 0.5) / b.x);
    float2 r = float2(k.x * b.y - k.y * b.z, k.x * b.z + k.y * b.y);
    float inside = saturate((1.0 - 2.0 * abs(r.x)) * 1e5) * saturate((1.0 - 2.0 * abs(r.y)) * 1e5);
    if (e.x < 0.5)   // Chorus's image patch: b.w its emissive, c.x exponent, c.y opacity
    {
        half4 t = SAMPLE_TEXTURE2D(map, s, float2(r.x + 0.5, 0.5 + r.y));
        half3 em = t.rgb * b.w;
        half3 col = t.rgb + pow(max(em, 1e-6), c.x) * step(1e-6, em);   // (pow of 0 or less: 0)
        return lerp(under, col, t.a * c.y * inside);
    }
    // ES2's tinted mask (added): the patch's image tiled f.xy times, offset f.zw, its channel e.y (0-3; 4: no image, 1), ^c.w,
    // times colour c.rgb and intensity b.w; e.z: a falloff round the patch's middle (radians, 0: none); e.w: how much
    // brighter toward the sun (ES2's forward scattering, ^32 of the sun's side)
    float2 tuv = float2(r.x + 0.5, 0.5 + r.y) * f.xy + f.zw;
    float2 gx = ddx(tuv), gy = ddy(tuv);
    gx.x = abs(gx.x) > 0.25 * abs(f.x) ? 0 : gx.x;   // (the patch's wrap: no jump in the mip level)
    gy.x = abs(gy.x) > 0.25 * abs(f.x) ? 0 : gy.x;
    half m = 1;
    if (e.y < 3.5)
    {
        half4 t = SAMPLE_TEXTURE2D_GRAD(map, s, tuv, gx, gy);
        m = e.y < 0.5 ? t.r : e.y < 1.5 ? t.g : e.y < 2.5 ? t.b : t.a;
    }
    m = m <= 0 ? 0 : pow(m, c.w);
    if (e.z > 0)
    {
        float off = acos(clamp(-q.y, -1.0, 1.0));   // (the patch's middle: q = (0, -1, 0))
        m *= exp(-(off / e.z) * (off / e.z));
    }
    half3 col = (half3)c.rgb * (m * b.w * inside);
    float toSun = saturate((dot(-d, _ChorusSun.xyz) + 1.0) * 0.5);
    col *= 1.0 + e.w * pow(toSun, 32.0) * (half3)_ChorusSunColor.rgb;
    return under + col;
}

half3 ChorusStars(float3 v)   // the star mask through its curve
{
    float m = ChorusStarMask(v);
    float3 curve = lerp(_ChorusCurve0.rgb, _ChorusCurve1.rgb, saturate((m - _ChorusCurve0.w) / max(_ChorusCurve1.w - _ChorusCurve0.w, 1e-5)));
    float3 stars = pow(max(curve, 1e-6), _ChorusMisc.x) * step(1e-6, curve);
    return dot(stars * _ChorusStars.w, 1.0);   // (the sphere's vertex colours: none, white)
}

// 'base': what lies under it all (the combined sky: the game's nebula layer); 'starsLast': its star mask over the layers
// instead of under them (Chorus's order is stars, then its images over them; over a painted sky the patches would cover them).
half3 ChorusSky(float3 w, half3 base, bool starsLast)
{
    float3 v = normalize(float3(w.z, w.x, w.y));   // Unreal's axes
    float3 d = -v;   // the sphere's inward normal: what the gradient and the layers read
    half3 c = base;
    if (_ChorusGradB.w > 0)
    {
        float g = dot(d, _ChorusGradUp.xyz) + _ChorusGradUp.w;
        g = g <= 0 ? 0 : pow(g, _ChorusGradA.w);
        c += lerp(_ChorusGradA.rgb, _ChorusGradB.rgb, min(g, 1.0)) * g * _ChorusGradB.w;
    }
    if (_ChorusStars.w > 0 && !starsLast) c += ChorusStars(v);
    int n = (int)_ChorusMisc.z;
    if (n > 0) c = ChorusLayer(TEXTURE2D_ARGS(_ChorusLayer1, sampler_ChorusLayer1), d, _ChorusL1A, _ChorusL1B, _ChorusL1C, _ChorusL1D, _ChorusL1E, c);
    if (n > 1) c = ChorusLayer(TEXTURE2D_ARGS(_ChorusLayer2, sampler_ChorusLayer2), d, _ChorusL2A, _ChorusL2B, _ChorusL2C, _ChorusL2D, _ChorusL2E, c);
    if (n > 2) c = ChorusLayer(TEXTURE2D_ARGS(_ChorusLayer3, sampler_ChorusLayer3), d, _ChorusL3A, _ChorusL3B, _ChorusL3C, _ChorusL3D, _ChorusL3E, c);
    if (n > 3) c = ChorusLayer(TEXTURE2D_ARGS(_ChorusLayer4, sampler_ChorusLayer4), d, _ChorusL4A, _ChorusL4B, _ChorusL4C, _ChorusL4D, _ChorusL4E, c);
    if (n > 4) c = ChorusLayer(TEXTURE2D_ARGS(_ChorusLayer5, sampler_ChorusLayer5), d, _ChorusL5A, _ChorusL5B, _ChorusL5C, _ChorusL5D, _ChorusL5E, c);
    if (n > 5) c = ChorusLayer(TEXTURE2D_ARGS(_ChorusLayer6, sampler_ChorusLayer6), d, _ChorusL6A, _ChorusL6B, _ChorusL6C, _ChorusL6D, _ChorusL6E, c);
    if (_ChorusStars.w > 0 && starsLast) c += ChorusStars(v);
    return max(c, 0) * _ChorusMisc.y;
}

// The game's nebula layer (or a mod's image) toward the sky direction d.
half3 BaseNebula(float3 d)
{
    #if defined(_NEBULA_PANORAMA)
        const int nebulaMode = 1;
    #elif defined(_NEBULA_STRIP)
        const int nebulaMode = 2;
    #else
        const int nebulaMode = 0;
    #endif
    return Layer(TEXTURECUBE_ARGS(_Nebula, sampler_Nebula), TEXTURE2D_ARGS(_NebulaMap, sampler_NebulaMap), _NebulaMap_TexelSize, d, nebulaMode) * _NebulaGain;
}

// The game's star layer (or a mod's image) and the Enhanced skies' stars toward the sky direction d.
half3 BaseStars(float3 d)
{
    #if defined(_STARS_PANORAMA)
        const int starsMode = 1;
    #elif defined(_STARS_STRIP)
        const int starsMode = 2;
    #else
        const int starsMode = 0;
    #endif
    half3 c = Layer(TEXTURECUBE_ARGS(_Stars, sampler_Stars), TEXTURE2D_ARGS(_StarsMap, sampler_StarsMap), _StarsMap_TexelSize, d, starsMode) * _StarsGain;
    if (_StarGrid.x > 0) c += StarPoints(TEXTURE2D_ARGS(_StarIndex, sampler_StarIndex), TEXTURE2D_ARGS(_StarData, sampler_StarData), _StarGrid, d, _StarsGain, _NebulaGain);
    return c;
}

// The sky toward the world direction 'world'.
half3 Sky(float3 world)
{
    float3 d = mul((float3x3)_SkyRotation, world);
    #if defined(_CHORUS_SKY)
    // "base" (the combined sky: GoF2's Enhanced skies, a mod's sky over its images): the nebula layer under the layers, the
    // star layer over them all; else the layered sky alone (Chorus's)
    bool combined = _ChorusStarsB.w > 0.5;
    half3 under = 0;
    if (combined) under = BaseNebula(d);   // (a branch, not a select: the layers under only where asked)
    half3 c = ChorusSky(d, under, combined);
    if (combined) c += BaseStars(d);
    #else
    half3 c = BaseNebula(d) + BaseStars(d);
    #endif
    if (_SkyBlend > 0.001)
    {
        float3 e = mul((float3x3)_BlendRotation, world);
        half3 other = Layer(TEXTURECUBE_ARGS(_BlendStars, sampler_BlendStars), TEXTURE2D_ARGS(_BlendStarsMap, sampler_BlendStarsMap), _BlendStarsTexel, e, (int)_BlendStarsMode) * _BlendStarsGain
                    + Layer(TEXTURECUBE_ARGS(_BlendNebula, sampler_BlendNebula), TEXTURE2D_ARGS(_BlendNebulaMap, sampler_BlendNebulaMap), _BlendNebulaTexel, e, (int)_BlendNebulaMode) * _BlendNebulaGain;
        if (_BlendStarGrid.x > 0) other += StarPoints(TEXTURE2D_ARGS(_BlendStarIndex, sampler_BlendStarIndex), TEXTURE2D_ARGS(_BlendStarData, sampler_BlendStarData), _BlendStarGrid, e, _BlendStarsGain, _BlendNebulaGain);
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
