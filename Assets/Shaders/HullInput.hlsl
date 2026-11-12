// HullInput.hlsl: GoF2/Hull's LitInput.hlsl, a copy of URP 17.7's (Packages/com.unity.render-pipelines.universal/Shaders/
// LitInput.hlsl) with the remake's per-material values added to UnityPerMaterial, so the SRP batcher still takes the shader
// (every pass includes this one instead of URP's; the include guard is URP's own, so nothing includes both). When URP is
// updated, copy its LitInput.hlsl again and put back the block marked "GoF2".
#ifndef UNIVERSAL_LIT_INPUT_INCLUDED
#define UNIVERSAL_LIT_INPUT_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceData.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/DebugMipmapStreamingMacros.hlsl"
#include "Packages/com.unity.render-pipelines.universal/Shaders/Utils/NormalMap.hlsl"
#include "Packages/com.unity.render-pipelines.universal/Shaders/Utils/DetailMap.hlsl"
#include "Packages/com.unity.render-pipelines.universal/Shaders/Utils/ParallaxMap.hlsl"
#include "Packages/com.unity.render-pipelines.universal/Shaders/Utils/MetallicSpecGloss.hlsl"
#include "Packages/com.unity.render-pipelines.universal/Shaders/Utils/AlphaBlend.hlsl"
#include "Packages/com.unity.render-pipelines.universal/Shaders/Utils/OcclusionMap.hlsl"
#include "Packages/com.unity.render-pipelines.universal/Shaders/Utils/ClearCoat.hlsl"
#include "Packages/com.unity.render-pipelines.universal/Shaders/Utils/Emission.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/ParallaxMapping.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DBuffer.hlsl"
#include "Packages/com.unity.render-pipelines.universal/Shaders/Utils/SurfaceType.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Macros.hlsl"
#include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.deprecated.hlsl"

// NOTE: Do not ifdef the properties here as SRP batcher can not handle different layouts.
CBUFFER_START(UnityPerMaterial)
float4 _BaseMap_ST;
float4 _BaseMap_TexelSize;
float4 _DetailAlbedoMap_ST;
half4 _BaseColor;
half4 _SpecColor;
half4 _EmissionColor;
half _Cutoff;
half _Smoothness;
half _Metallic;
half _BumpScale;
half _Parallax;
half _OcclusionStrength;
half _ClearCoatMask;
half _ClearCoatSmoothness;
half _DetailAlbedoMapScale;
half _DetailNormalMapScale;
half _Blend;
// GoF2: a kit's livery (ModShips.ApplyLivery, HullForwardPass.hlsl), all zero = none.
float4 _LiveryParams;      // x 1 / (2 x the ship's largest half-extent) in object units, y opacity (0 = no livery), z / w mirror x / y
float4 _LiveryTransform;   // xy scale, zw offset
float4 _LiveryParams2;     // x tiling, y the clip (1 - clipAngle / 90), z the mask's exponent, w the livery's smoothness
half4 _LiveryColor;        // rgb the livery's colour, a its metallic
float4 _LiveryForward;     // the parts' forward, right and up in object space
float4 _LiveryRight;
float4 _LiveryUp;
// GoF2: a kit's decal (ModShips.ApplyDecals): x 1 = the base map is a decal mask (R x colour 1 + G x colour 2 + B x border),
// y 1 = no decal (cut away).
float4 _DecalParams;
half4 _DecalColor1;
half4 _DecalColor2;
half4 _DecalColor3;
// GoF2: a mod specular material's GoF3 terms (ModMaterials, HullForwardPass.hlsl GoF3ShipShader): x environment (GoF3's
// Environment_Amount), y rim light, z GoF3's Specular_Multiplier, w 1 = the map carries that multiplier (GoF3's ship bodies:
// the whole specular x it) / 0 = the map doesn't (glass, capital-ship parts: the highlight x it).
half4 _GoF3Params;
// GoF2: EVERSPACE 2's structure layers (M_Structures_Parent's compiled base pass; ModMaterials, HullForwardPass.hlsl
// Es2StructureLayers), on when _Es2Wear2.z > 0. _Es2Wear: x DamageDetailMaskTiling, y DamageDetailMaskExponent, z DamageCap,
// w DamageMaskThresholdWidth; _Es2Wear2: x DamageBrightness, y DamageMaxMetalness, z 1 = on, w BaseRoughness (< 0: none);
// _Es2Grunge: x GrungeAmount, y GrungeTiling, z the rim's strength (ES2's 5).
float4 _Es2Wear;
float4 _Es2Wear2;
float4 _Es2Grunge;
UNITY_TEXTURE_STREAMING_DEBUG_VARS;
CBUFFER_END

// NOTE: Do not ifdef the properties for dots instancing, but ifdef the actual usage.
// Otherwise you might break CPU-side as property constant-buffer offsets change per variant.
// NOTE: Dots instancing is orthogonal to the constant buffer above.
#ifdef UNITY_DOTS_INSTANCING_ENABLED

UNITY_DOTS_INSTANCING_START(MaterialPropertyMetadata)
    UNITY_DOTS_INSTANCED_PROP(float4, _BaseColor)
    UNITY_DOTS_INSTANCED_PROP(float4, _SpecColor)
    UNITY_DOTS_INSTANCED_PROP(float4, _EmissionColor)
    UNITY_DOTS_INSTANCED_PROP(float , _Cutoff)
    UNITY_DOTS_INSTANCED_PROP(float , _Smoothness)
    UNITY_DOTS_INSTANCED_PROP(float , _Metallic)
    UNITY_DOTS_INSTANCED_PROP(float , _BumpScale)
    UNITY_DOTS_INSTANCED_PROP(float , _Parallax)
    UNITY_DOTS_INSTANCED_PROP(float , _OcclusionStrength)
    UNITY_DOTS_INSTANCED_PROP(float , _ClearCoatMask)
    UNITY_DOTS_INSTANCED_PROP(float , _ClearCoatSmoothness)
    UNITY_DOTS_INSTANCED_PROP(float , _DetailAlbedoMapScale)
    UNITY_DOTS_INSTANCED_PROP(float , _DetailNormalMapScale)
    UNITY_DOTS_INSTANCED_PROP(float , _Blend)
UNITY_DOTS_INSTANCING_END(MaterialPropertyMetadata)

// Here, we want to avoid overriding a property like e.g. _BaseColor with something like this:
// #define _BaseColor UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _BaseColor0)
//
// It would be simpler, but it can cause the compiler to regenerate the property loading code for each use of _BaseColor.
//
// To avoid this, the property loads are cached in some static values at the beginning of the shader.
// The properties such as _BaseColor are then overridden so that it expand directly to the static value like this:
// #define _BaseColor unity_DOTS_Sampled_BaseColor
//
// This simple fix happened to improve GPU performances by ~10% on Meta Quest 2 with URP on some scenes.
static float4 unity_DOTS_Sampled_BaseColor;
static float4 unity_DOTS_Sampled_SpecColor;
static float4 unity_DOTS_Sampled_EmissionColor;
static float  unity_DOTS_Sampled_Cutoff;
static float  unity_DOTS_Sampled_Smoothness;
static float  unity_DOTS_Sampled_Metallic;
static float  unity_DOTS_Sampled_BumpScale;
static float  unity_DOTS_Sampled_Parallax;
static float  unity_DOTS_Sampled_OcclusionStrength;
static float  unity_DOTS_Sampled_ClearCoatMask;
static float  unity_DOTS_Sampled_ClearCoatSmoothness;
static float  unity_DOTS_Sampled_DetailAlbedoMapScale;
static float  unity_DOTS_Sampled_DetailNormalMapScale;
static float  unity_DOTS_Sampled_Blend;

void SetupDOTSLitMaterialPropertyCaches()
{
    unity_DOTS_Sampled_BaseColor            = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _BaseColor);
    unity_DOTS_Sampled_SpecColor            = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _SpecColor);
    unity_DOTS_Sampled_EmissionColor        = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _EmissionColor);
    unity_DOTS_Sampled_Cutoff               = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _Cutoff);
    unity_DOTS_Sampled_Smoothness           = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _Smoothness);
    unity_DOTS_Sampled_Metallic             = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _Metallic);
    unity_DOTS_Sampled_BumpScale            = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _BumpScale);
    unity_DOTS_Sampled_Parallax             = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _Parallax);
    unity_DOTS_Sampled_OcclusionStrength    = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _OcclusionStrength);
    unity_DOTS_Sampled_ClearCoatMask        = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _ClearCoatMask);
    unity_DOTS_Sampled_ClearCoatSmoothness  = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _ClearCoatSmoothness);
    unity_DOTS_Sampled_DetailAlbedoMapScale = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _DetailAlbedoMapScale);
    unity_DOTS_Sampled_DetailNormalMapScale = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _DetailNormalMapScale);
    unity_DOTS_Sampled_Blend                = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _Blend);
}

#undef UNITY_SETUP_DOTS_MATERIAL_PROPERTY_CACHES
#define UNITY_SETUP_DOTS_MATERIAL_PROPERTY_CACHES() SetupDOTSLitMaterialPropertyCaches()

#define _BaseColor              unity_DOTS_Sampled_BaseColor
#define _SpecColor              unity_DOTS_Sampled_SpecColor
#define _EmissionColor          unity_DOTS_Sampled_EmissionColor
#define _Cutoff                 unity_DOTS_Sampled_Cutoff
#define _Smoothness             unity_DOTS_Sampled_Smoothness
#define _Metallic               unity_DOTS_Sampled_Metallic
#define _BumpScale              unity_DOTS_Sampled_BumpScale
#define _Parallax               unity_DOTS_Sampled_Parallax
#define _OcclusionStrength      unity_DOTS_Sampled_OcclusionStrength
#define _ClearCoatMask          unity_DOTS_Sampled_ClearCoatMask
#define _ClearCoatSmoothness    unity_DOTS_Sampled_ClearCoatSmoothness
#define _DetailAlbedoMapScale   unity_DOTS_Sampled_DetailAlbedoMapScale
#define _DetailNormalMapScale   unity_DOTS_Sampled_DetailNormalMapScale
#define _Blend                  unity_DOTS_Sampled_Blend

#endif

TEXTURE2D(_BaseMap);            SAMPLER(sampler_BaseMap);
TEXTURE2D(_BumpMap);            SAMPLER(sampler_BumpMap);
TEXTURE2D(_EmissionMap);        SAMPLER(sampler_EmissionMap);
TEXTURE2D(_ParallaxMap);        SAMPLER(sampler_ParallaxMap);
TEXTURE2D(_OcclusionMap);       SAMPLER(sampler_OcclusionMap);
TEXTURE2D(_DetailMask);         SAMPLER(sampler_DetailMask);
TEXTURE2D(_DetailAlbedoMap);    SAMPLER(sampler_DetailAlbedoMap);
TEXTURE2D(_DetailNormalMap);    SAMPLER(sampler_DetailNormalMap);
TEXTURE2D(_MetallicGlossMap);   SAMPLER(sampler_MetallicGlossMap);
TEXTURE2D(_SpecGlossMap);       SAMPLER(sampler_SpecGlossMap);
TEXTURE2D(_ClearCoatMap);       SAMPLER(sampler_ClearCoatMap);
// GoF2: EVERSPACE 2's structure layers (sampled with _BaseMap's sampler: no samplers of their own)
TEXTURE2D(_Es2WearMap);
TEXTURE2D(_Es2WearNormalMap);
TEXTURE2D(_Es2WearNoiseMap);
TEXTURE2D(_Es2GrungeMap);
UNITY_TEXTURE_STREAMING_DEBUG_VARS_FOR_TEX(_BaseMap);

// Simple getters for the material properties above. All raw material input (constant buffer
// values, texture samples) is accessed through these; the feature helpers build on them.
half4 GetBaseColor()            { return _BaseColor; }
half4 GetSpecColor()            { return _SpecColor; }
half4 GetEmissionColor()        { return _EmissionColor; }
half GetCutoff()                { return _Cutoff; }
half GetSmoothness()            { return _Smoothness; }
half GetMetallic()              { return _Metallic; }
half GetBumpScale()             { return _BumpScale; }
half GetParallax()              { return _Parallax; }
half GetOcclusionStrength()     { return _OcclusionStrength; }
half GetClearCoatMask()         { return _ClearCoatMask; }
half GetClearCoatSmoothness()   { return _ClearCoatSmoothness; }
half GetDetailAlbedoMapScale()  { return _DetailAlbedoMapScale; }
half GetDetailNormalMapScale()  { return _DetailNormalMapScale; }
float4 GetBaseMapST()           { return _BaseMap_ST; }
float4 GetBaseMapTexelSize()    { return _BaseMap_TexelSize; }
float4 GetDetailAlbedoMapST()   { return _DetailAlbedoMap_ST; }

// Modified: bicubic (cubic B-spline, 4 bilinear taps) instead of bilinear while a texture is magnified (closer
// than one texel per pixel): the big hulls' diagonal texture edges (UV islands mapped at an angle) showed as stair-step
// stripes up close, made stronger by the hull's rim / reflection terms. Minified it is the plain sample (mipmaps).
// _GoF2HullBicubicOff (global) = 1 turns it off.
float _GoF2HullBicubicOff;
half4 GoF2SampleBicubic(TEXTURE2D_PARAM(tex, samp), float2 uv)
{
    float w, h;
    tex.GetDimensions(w, h);
    float2 size = float2(w, h);
    float2 dx = ddx(uv), dy = ddy(uv);
    float texelsPerPixel = max(length(dx * size), length(dy * size));
    if (_GoF2HullBicubicOff > 0.5 || texelsPerPixel > 1.0)
        return SAMPLE_TEXTURE2D_GRAD(tex, samp, uv, dx, dy);
    float2 st = uv * size - 0.5;
    float2 i = floor(st);
    float2 f = st - i;
    float2 f2 = f * f, f3 = f2 * f;
    float2 w0 = (1.0 - 3.0 * f + 3.0 * f2 - f3) / 6.0;
    float2 w1 = (4.0 - 6.0 * f2 + 3.0 * f3) / 6.0;
    float2 w2 = (1.0 + 3.0 * f + 3.0 * f2 - 3.0 * f3) / 6.0;
    float2 w3 = f3 / 6.0;
    float2 g0 = w0 + w1, g1 = w2 + w3;
    float2 p0 = (i - 0.5 + w1 / g0) / size;
    float2 p1 = (i + 1.5 + w3 / g1) / size;
    half4 a = SAMPLE_TEXTURE2D_GRAD(tex, samp, float2(p0.x, p0.y), dx, dy);
    half4 b = SAMPLE_TEXTURE2D_GRAD(tex, samp, float2(p1.x, p0.y), dx, dy);
    half4 c = SAMPLE_TEXTURE2D_GRAD(tex, samp, float2(p0.x, p1.y), dx, dy);
    half4 d = SAMPLE_TEXTURE2D_GRAD(tex, samp, float2(p1.x, p1.y), dx, dy);
    return g0.y * (g0.x * a + g1.x * b) + g1.y * (g0.x * c + g1.x * d);
}

// Modified: Catmull-Rom (5 bilinear taps, the corner taps dropped): sharp, for the colour map, where the B-spline's
// blur showed most. Same magnified-only rule as above.
half4 GoF2SampleCatmullRom(TEXTURE2D_PARAM(tex, samp), float2 uv)
{
    float w, h;
    tex.GetDimensions(w, h);
    float2 size = float2(w, h);
    float2 dx = ddx(uv), dy = ddy(uv);
    float texelsPerPixel = max(length(dx * size), length(dy * size));
    if (_GoF2HullBicubicOff > 0.5 || texelsPerPixel > 1.0)
        return SAMPLE_TEXTURE2D_GRAD(tex, samp, uv, dx, dy);
    float2 pos = uv * size;
    float2 t1 = floor(pos - 0.5) + 0.5;
    float2 f = pos - t1;
    float2 w0 = f * (-0.5 + f * (1.0 - 0.5 * f));
    float2 w1 = 1.0 + f * f * (-2.5 + 1.5 * f);
    float2 w2 = f * (0.5 + f * (2.0 - 1.5 * f));
    float2 w3 = f * f * (-0.5 + 0.5 * f);
    float2 w12 = w1 + w2;
    float2 t0 = (t1 - 1.0) / size;
    float2 t3 = (t1 + 2.0) / size;
    float2 t12 = (t1 + w2 / w12) / size;
    half4 r = SAMPLE_TEXTURE2D_GRAD(tex, samp, float2(t12.x, t0.y), dx, dy) * (w12.x * w0.y)
            + SAMPLE_TEXTURE2D_GRAD(tex, samp, float2(t0.x, t12.y), dx, dy) * (w0.x * w12.y)
            + SAMPLE_TEXTURE2D_GRAD(tex, samp, t12, dx, dy) * (w12.x * w12.y)
            + SAMPLE_TEXTURE2D_GRAD(tex, samp, float2(t3.x, t12.y), dx, dy) * (w3.x * w12.y)
            + SAMPLE_TEXTURE2D_GRAD(tex, samp, float2(t12.x, t3.y), dx, dy) * (w12.x * w3.y);
    float sum = w12.x * w0.y + w0.x * w12.y + w12.x * w12.y + w3.x * w12.y + w12.x * w3.y;
    return max(r / sum, 0.0);
}

// Modified: the sharper filtering only on the materials that turn on _GOF2_BICUBIC (the Valkyrie battlestation's hull,
// AssetImport.UsesBicubic): everything else keeps the plain bilinear sample and pays nothing.
#if defined(_GOF2_BICUBIC)
half4 SampleBaseMap(float2 uv)          { return GoF2SampleCatmullRom(TEXTURE2D_ARGS(_BaseMap, sampler_BaseMap), uv); }
half4 SampleBumpMap(float2 uv)          { return GoF2SampleBicubic(TEXTURE2D_ARGS(_BumpMap, sampler_BumpMap), uv); }
#else
half4 SampleBaseMap(float2 uv)          { return SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv); }
half4 SampleBumpMap(float2 uv)          { return SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uv); }
#endif
half4 SampleEmissionMap(float2 uv)      { return SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, uv); }
half4 SampleParallaxMap(float2 uv)      { return SAMPLE_TEXTURE2D(_ParallaxMap, sampler_ParallaxMap, uv); }
half4 SampleOcclusionMap(float2 uv)     { return SAMPLE_TEXTURE2D(_OcclusionMap, sampler_OcclusionMap, uv); }
half4 SampleDetailMask(float2 uv)       { return SAMPLE_TEXTURE2D(_DetailMask, sampler_DetailMask, uv); }
half4 SampleDetailAlbedoMap(float2 uv)  { return SAMPLE_TEXTURE2D(_DetailAlbedoMap, sampler_DetailAlbedoMap, uv); }
half4 SampleDetailNormalMap(float2 uv)  { return SAMPLE_TEXTURE2D(_DetailNormalMap, sampler_DetailNormalMap, uv); }
#if defined(_GOF2_BICUBIC)
half4 SampleMetallicGlossMap(float2 uv) { return GoF2SampleBicubic(TEXTURE2D_ARGS(_MetallicGlossMap, sampler_MetallicGlossMap), uv); }
#else
half4 SampleMetallicGlossMap(float2 uv) { return SAMPLE_TEXTURE2D(_MetallicGlossMap, sampler_MetallicGlossMap, uv); }
#endif
half4 SampleSpecGlossMap(float2 uv)     { return SAMPLE_TEXTURE2D(_SpecGlossMap, sampler_SpecGlossMap, uv); }
half4 SampleClearCoatMap(float2 uv)     { return SAMPLE_TEXTURE2D(_ClearCoatMap, sampler_ClearCoatMap, uv); }

inline void InitializeStandardLitSurfaceData(float2 uv, out SurfaceData outSurfaceData)
{
    half4 albedoAlpha = SampleBaseMap(uv);
    outSurfaceData.alpha = AlphaDiscard(UseSmoothnessTextureAlbedoChannelA() ? GetBaseColor().a : albedoAlpha.a * GetBaseColor().a, GetCutoff());

    half4 specGloss = SampleMetallicSpecGloss(uv, albedoAlpha.a);

    outSurfaceData.albedo = albedoAlpha.rgb * GetBaseColor().rgb;
    if (UseAlphaModulate())
        outSurfaceData.albedo = ApplyAlphaModulate(outSurfaceData.albedo, outSurfaceData.alpha);

    if (IsSpecularSetup())
    {
        outSurfaceData.metallic = half(1.0);
        outSurfaceData.specular = specGloss.rgb;
    }
    else
    {
        outSurfaceData.metallic = specGloss.r;
        outSurfaceData.specular = half3(0.0, 0.0, 0.0);
    }

    outSurfaceData.smoothness = specGloss.a;
    outSurfaceData.normalTS = SampleNormal(uv);
    outSurfaceData.occlusion = SampleOcclusion(uv);
    outSurfaceData.emission = SampleEmission(uv);

    half2 clearCoatMaskSmoothness = SampleClearCoat(uv);
    outSurfaceData.clearCoatMask       = clearCoatMaskSmoothness.r;
    outSurfaceData.clearCoatSmoothness = clearCoatMaskSmoothness.g;

    if (UseDetailMap())
    {
        half detailMask = SampleDetailMask(uv).a;
        float2 detailUv = uv * GetDetailAlbedoMapST().xy + GetDetailAlbedoMapST().zw;
        outSurfaceData.albedo = ApplyDetailAlbedo(detailUv, outSurfaceData.albedo, detailMask);
        outSurfaceData.normalTS = ApplyDetailNormal(detailUv, outSurfaceData.normalTS, detailMask);
    }
}

#endif // UNIVERSAL_INPUT_SURFACE_PBR_INCLUDED
