// HullForwardPass.hlsl: GoF2/Hull's ForwardLit fragment. URP Lit's own fragment (LitPassFragment, unchanged lighting:
// UniversalFragmentPBR with its lights, shadows and environment), then the original's BumpRimCubeShader terms on top
// (the fragment source in libgof2hdaa.so, 0x215575):
//   reflectTex   = spec^2                                   (spec = the specular map, the normal map's alpha in the original;
//                                                            here _MetallicGlossMap's alpha, the same values)
//   rim          = smoothstep(0.8, 1, 1 - N.V)
//   reflectivity = clamp(smoothstep(0.6, 1, 1 - N.V), reflectTex, 2 spec + rim)
//   out += cubemap(reflect(V, N)) * reflectivity + rim * reflectTex * rimColor
// The cubemap is _GoF2ReflectionCube (the system's cubemap, or a hangar's own: SkyReflection), sampled sharp like the
// original's textureCube (the GPU's mip choice). Only materials with a specular map (_METALLICSPECGLOSSMAP) get the terms, scaled down by the
// material's metallic (a mod's metal parts already reflect through URP). Globals (SRP batcher safe): _GoF2HullReflection
// (strength, 0 = URP Lit as it is), _GoF2HullRimColor (3 x the system's rim table, StarSystem::initLight).
// _GOF2_PBR (the mods' material templates, ModAssetsBuilder): no GoF2 terms. A mod material's _MetallicGlossMap alpha is
// PBR smoothness, high over the whole hull, not the original's sparse specular mask, so the terms laid the cubemap over
// every panel (spec^2 everywhere, up to 2 spec at grazing angles) on top of URP's own environment reflection: mod ships
// came out milky white in the hangars, whose cubemaps are bright rooms. URP's Fresnel reflection alone lights them, and
// in a GoF2 hangar their indirect light is dimmed by _GoF2PbrIndirectDim (SkyReflection.SetHangar).

#ifndef GOF2_HULL_FORWARD_PASS_INCLUDED
#define GOF2_HULL_FORWARD_PASS_INCLUDED

half _GoF2HullReflection;
half3 _GoF2HullRimColor;
// Modified: the original lights in gamma space (StarSystem::initLight: LIGHT0 diffuse 2.0, fixed-function), so a face
// toward the sun is the texture x 2 clamped: nearly white. Per channel, the original's LIGHT0 (gamma) per unit of the main
// light's linear colour, so a tinted sun keeps the original's tint (0 = URP's linear diffuse as it is).
// OrbitBuilder.SetSunGamma.
half3 _GoF2SunGamma;
// Modified: the sunlit side's glow: the part of texture x LIGHT0 above _GoF2SunGlowFrom (gamma) goes on x _GoF2SunGlow as
// HDR light, so the bloom picks it up like the original's bright pass (about half brightness) did; 0 = none.
// OrbitBuilder.SetSunGamma.
half _GoF2SunGlow;
half _GoF2SunGlowFrom;
// Modified: the original's scene ambient (LightSetGlobalSceneColorAmbient: 0.15 x the sun colour, 0.7 x in the supernova
// system), added to LIGHT0's term in gamma space: the supernova's orange tint over the whole hull. OrbitBuilder.SetupLights.
half3 _GoF2SunAmbient;
// Modified: LIGHT1, the orbit planet's light (1.5 x its colour table, initLight), in gamma space like LIGHT0: xyz toward
// the planet (world), _GoF2PlanetColor its gamma colour, _GoF2PlanetLinear what URP's own planet light adds (its linear
// colour x intensity), taken out again. Linearised, the dimmer planets' light (0.1-0.3) all but vanished.
float4 _GoF2PlanetLight;
half3 _GoF2PlanetColor;
half3 _GoF2PlanetLinear;
// _GOF2_PBR: how much of the indirect light (ambient and environment reflection) a mod material loses, through URP's
// occlusion (0 = all of it kept; SkyReflection: 0 in space and the lounge, more in a GoF2 hangar).
half _GoF2PbrIndirectDim;
// _GOF2_PBR: in a GoF2 hangar the main light swapped for one from above (xyz toward it, world; w = how much of the swap,
// 0 = off), its colour a factor on the main light's (SkyReflection.SetHangar).
float4 _GoF2PbrLight;
half3 _GoF2PbrLightColor;
// Remake: the system's reflection cubemap as a global of its own (SkyReflection.SetSystem): unity_SpecCube0 stayed empty in
// the Android build (Forward on the Mobile renderer).
TEXTURECUBE(_GoF2ReflectionCube);
SAMPLER(sampler_GoF2ReflectionCube);
// A kit's livery (ModShips.ApplyLivery; its values in HullInput.hlsl's UnityPerMaterial).
TEXTURE2D(_LiveryMap);
SAMPLER(sampler_LiveryMap);

// Remake mods: a livery, EVERSPACE 2's projection decal (M_ShipModules_Hull_Parent's compiled pixel shader, its
// ProjectionDecal parameters): a mask projected from above in the ship's own space. p = (forward, right) / (2 x the ship's
// largest half-extent) + 0.5 (the nose at the image's right); on an axis with mirror, the half at or below 0.5 sign -1
// (offset and scale flipped there, so the image mirrors at the middle); p = (p + sign x offset - 0.5) / (sign x scale) + 0.5;
// outside 0..1 nothing unless tiling; mask = tex^(1 + 1.2 luminance(colour)) x opacity, none on faces whose object-space
// normal's up part is at most the clip (1 - clipAngle / 90). The engine's image rows run down from v 0, Unity's up: v flipped.
half GoF2LiveryMask(float3 positionWS, half3 normalWS)
{
    float3 p3 = TransformWorldToObject(positionWS);
    float2 p = float2(dot(p3, _LiveryForward.xyz), dot(p3, _LiveryRight.xyz)) * _LiveryParams.x + 0.5;
    float2 sgn = float2(p.x <= 0.5 && _LiveryParams.z > 0.5 ? -1.0 : 1.0, p.y <= 0.5 && _LiveryParams.w > 0.5 ? -1.0 : 1.0);
    p += sgn * _LiveryTransform.zw;
    p = (p - 0.5) / (sgn * _LiveryTransform.xy) + 0.5;
    half m = SAMPLE_TEXTURE2D(_LiveryMap, sampler_LiveryMap, float2(p.x, 1.0 - p.y)).r;
    half inside = (p.x > 0.0 && p.x <= 1.0 && p.y > 0.0 && p.y <= 1.0) ? 1.0h : (half)_LiveryParams2.x;
    m = m <= 2.98e-8h ? 0.0h : pow(m, (half)_LiveryParams2.z) * inside;
    half3 nOS = normalize(TransformWorldToObjectNormal(normalWS));
    m *= abs(dot(nOS, (half3)_LiveryUp.xyz)) <= _LiveryParams2.y ? 0.0h : 1.0h;
    return m * (half)_LiveryParams.y;
}

// Remake: the distance fog by the real distance to the camera, as the original (computeLinearFogFactor(eyeDist), the
// eye-to-vertex distance). URP's fog goes by the view depth (clip z), which is shorter for the same distance toward the
// screen's edges: a fogged station in the middle of the screen came out clear and close-looking at the edge, though the
// ship hadn't moved. unity_FogParams: x = density / sqrt(ln 2), y = density / ln 2, z = -1 / (end - start), w = end / (end - start).
half3 GoF2RadialFog(half3 color, float d)
{
#if defined(FOG_LINEAR) || defined(FOG_EXP) || defined(FOG_EXP2)
    #if defined(FOG_LINEAR)
        half f = saturate(d * unity_FogParams.z + unity_FogParams.w);
    #elif defined(FOG_EXP)
        half f = saturate(exp2(-unity_FogParams.y * d));
    #else
        float x = unity_FogParams.x * d;
        half f = saturate(exp2(-x * x));
    #endif
    color = lerp(unity_FogColor.rgb, color, f);
#endif
    return color;
}

void HullPassFragment(
    Varyings input
    , out half4 outColor : SV_Target0
#ifdef _WRITE_RENDERING_LAYERS
    , out uint outRenderingLayers : SV_Target1
#endif
)
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

#if defined(REQUIRES_TANGENT_SPACE_VIEW_DIR_INTERPOLATOR)
    half3 viewDirTS = input.viewDirTS;
    ApplyPerPixelDisplacement(viewDirTS, input.uv);
#endif

    SurfaceData surfaceData;
    InitializeStandardLitSurfaceData(input.uv, surfaceData);

    LODFadeCrossFade(input.positionCS);

    InputData inputData;
    InitializeInputData(input, surfaceData.normalTS, inputData);
    SETUP_DEBUG_TEXTURE_DATA(inputData, UNDO_TRANSFORM_TEX(input.uv, _BaseMap));

#if defined(_DBUFFER)
    if (!IsSurfaceTypeTransparent())
        ApplyDecalToSurfaceData(input.positionCS, surfaceData, inputData, IsSpecularSetup());
#endif

    InitializeBakedGIData(input, inputData);

    if (_DecalParams.x > 0.0)
    {
        // A kit's decal (EVERSPACE 2's M_ShipModules_Decals_Parent, its compiled pixel shader): the mask's R x colour 1 +
        // G x colour 2 + B x the border colour, saturated; its A is the base alpha (cut by the material's alpha clip).
        if (_DecalParams.y > 0.0) clip(-1.0);
        half4 dm = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
        surfaceData.albedo = saturate(dm.r * _DecalColor1.rgb + dm.g * _DecalColor2.rgb + dm.b * _DecalColor3.rgb);
    }

    if (_LiveryParams.y > 0.0)
    {
        // The livery's colour over the base colour, keeping the base map's shading (its AO and plating).
        half m = GoF2LiveryMask(inputData.positionWS, input.normalWS.xyz);
        half3 shade = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb;
        surfaceData.albedo = lerp(surfaceData.albedo, shade * _LiveryColor.rgb, m);
        surfaceData.metallic = lerp(surfaceData.metallic, _LiveryColor.a, m);
        surfaceData.smoothness = lerp(surfaceData.smoothness, (half)_LiveryParams2.w, m);
    }

#if defined(_GOF2_PBR)
    // A mod's PBR hull has no occlusion map (the GoF3 and ES2 hulls), and in a hangar the flat room ambient and the
    // bright room cubemap lit every panel alike: flat and hazy next to GoF3's own render. The original's own textures
    // carry their shading painted in, so the game's hulls keep all of it.
    surfaceData.occlusion *= 1.0h - _GoF2PbrIndirectDim;
#endif

    URP_LIGHT_ACCUM4 color = UniversalFragmentPBR(inputData, surfaceData,
        IsSpecularSetup(), UseSpecularHighlights(), UseAlphaPremultiply(),
        UseClearCoat() || UseClearCoatMap(), ReceiveShadows(), IsSurfaceTypeTransparent(), UseEnvironmentReflections(), input.normalWS.xyz);

#if defined(_METALLICSPECGLOSSMAP) && !defined(_GOF2_PBR)
    {
        half spec = SAMPLE_TEXTURE2D(_MetallicGlossMap, sampler_MetallicGlossMap, input.uv).a;
        half reflectTex = spec * spec;
        half3 n = inputData.normalWS;
        half3 v = inputData.viewDirectionWS;
        half facing = 1.0h - saturate(dot(v, n));
        half rim = smoothstep(0.8h, 1.0h, facing);
        half reflectivity = clamp(smoothstep(0.6h, 1.0h, facing), reflectTex, spec * 2.0h + rim);
        half3 r = reflect(-v, n);
        // The GPU's own mip choice (the original's textureCube): a fixed mip 0 sparkled on far and grazing panels.
        half3 env = SAMPLE_TEXTURECUBE(_GoF2ReflectionCube, sampler_GoF2ReflectionCube, r).rgb;   // an LDR cubemap: nothing to decode
        half k = _GoF2HullReflection * (1.0h - surfaceData.metallic);
        color.rgb += (env * reflectivity + rim * reflectTex * _GoF2HullRimColor) * k * surfaceData.occlusion;
    }
#endif

#if !defined(_GOF2_PBR)
    if (max(_GoF2SunGamma.r, max(_GoF2SunGamma.g, _GoF2SunGamma.b)) > 0.0)
    {
        // Modified: the main light's diffuse as the original's gamma-space sum: texture x (LIGHT0 . N), clamped, in place
        // of URP's linear one, which left the lit side at the texture x 1.24 (1.6 ^ (1 / 2.2)) instead of x 2.
        BRDFData brdf = InitializeBRDFData(surfaceData, IsSpecularSetup(), UseAlphaPremultiply());
        Light key = GetMainLight();
        half3 radiance = key.color * (key.distanceAttenuation * key.shadowAttenuation * saturate(dot(inputData.normalWS, key.direction)));
        half3 texG = LinearToSRGB(brdf.diffuse);
        half3 lit = texG * radiance * _GoF2SunGamma;
        half ndp = saturate(dot(inputData.normalWS, (half3)_GoF2PlanetLight.xyz));
        half3 planet = texG * _GoF2PlanetColor * ndp;
        // Modified: the glow keeps the lit colour's hue, scaled by how far its brightness is past _GoF2SunGlowFrom (the
        // original's bright pass goes by luminance): each channel's own excess over-weighted the strongest ones, and a
        // warm sun on Dis' beige panels (system 5's LIGHT0 2 / 1.95 / 1.65) glowed a saturated yellow.
        half lum = dot(lit, half3(0.299h, 0.587h, 0.114h));
        half3 gammaLit = SRGBToLinear(saturate(lit + planet + texG * _GoF2SunAmbient)) + SRGBToLinear(saturate(lit)) * (max(lum - _GoF2SunGlowFrom, 0.0h) * _GoF2SunGlow);
        color.rgb += max(gammaLit - brdf.diffuse * (radiance + _GoF2PlanetLinear * ndp), 0.0h);
    }
#endif

#if defined(_GOF2_PBR)
    if (_GoF2PbrLight.w > 0.0)
    {
        // The hangar's key light comes from the camera's side (ModStation::resetLight), which suits the original's hulls:
        // their textures carry their shading painted in. A mod's PBR hull lit from the front is evenly lit all over, flat
        // next to GoF3's own render; lit from above it has lit tops and dark undersides. The main light's term is taken
        // out and the one from above put in (the room and the game's ships keep the main light).
        BRDFData brdf = InitializeBRDFData(surfaceData, IsSpecularSetup(), UseAlphaPremultiply());
        Light key = GetMainLight();
        Light top = key;
        top.direction = half3(normalize(_GoF2PbrLight.xyz));
        top.color = key.color * _GoF2PbrLightColor;
        bool highlights = UseSpecularHighlights();
        color.rgb += (LightingPhysicallyBased(brdf, top, inputData.normalWS, inputData.viewDirectionWS, highlights, false)
                    - LightingPhysicallyBased(brdf, key, inputData.normalWS, inputData.viewDirectionWS, highlights, false)) * (half)_GoF2PbrLight.w;
    }
#endif

    // The distance along the pixel's ray: its view depth over the ray's cosine to the camera's forward axis.
    float fogDist = LinearEyeDepth(input.positionCS.z, _ZBufferParams) / max(dot(-inputData.viewDirectionWS, -UNITY_MATRIX_V[2].xyz), 1e-4);
    color.rgb = ClampExposed(inputData.preExposureMultiplier * GoF2RadialFog(color.rgb, fogDist));
#if defined(_TRANSPARENT_RECEIVE_FOG)
    if (IsSurfaceTypeTransparent())
        color.rgb = MixVolumetricFog(color.rgb, color.a, _Blend, UseAlphaPremultiply(), input.positionCS);
#endif
    color.a = OutputAlpha(color.a, IsSurfaceTypeTransparent());

    outColor = color;

#ifdef _WRITE_RENDERING_LAYERS
    outRenderingLayers = EncodeMeshRenderingLayer();
#endif
}

#endif
