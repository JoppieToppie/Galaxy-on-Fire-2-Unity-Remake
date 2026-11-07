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
// _GOF2_PBR with _SPECULAR_SETUP (a mod's specular map: GoF3's hulls, stations and capital ships) is lit by GoF3's own ship
// shader instead (GoF3ShipShader below).

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

#if defined(_GOF2_PBR) && defined(_SPECULAR_SETUP)
// Remake mods: a mod's specular map ("specularGlossiness" / "specularColor", glTF's KHR_materials_pbrSpecularGlossiness: the
// GoF3 importer's hulls, glass, guns, stations and capital ships) lit the way Galaxy on Fire 3 lights them. GoF3's shaders
// (its .ae4shader variants, ae4_shaders.text in gof3_ships_importer: the retro Groza's body and glass, the Terran structures'
// platings, glass and largeship parts, the generic guns) share one lighting, in gamma space as GoF3 draws:
//   spec  = the map (x Specular_Color) + fresnel / 2 x (1 - it), gloss likewise, fresnel = (1 - N.V)^2
//   light = lightColour x N.L + ambient
//   specular = (Phong pow(R.V, exp2(9 gloss + 1)) x lightColour [x Specular_Multiplier] + ambient x fresnel) x spec
//   out   = diffuse x light x (1 - specular.r) + specular [x Specular_Multiplier]
//   env   = the environment (GoF3's generic cubes are grey, its blur picked by gloss) x spec x (light + fresnel), clamped,
//           x Environment_Amount: out = out x (1 - env) + env
//   rim   = 0.1 x (1 - N.V) x lightColour x light;  glass: alpha raised by specular.r, env.r and the fresnel
// and differ per kind in what _GoF3Params says (ModMaterials, from the mod's material): x the environment (ship bodies and
// glass 1, structures, capital-ship parts and guns 0), y the rim (ship bodies, station glass and capital-ship parts 1,
// platings, ship glass and guns 0), z Specular_Multiplier, w where it goes: 1 = the map already carries it (the importer's
// ship bodies: taken out for spec, the whole specular x it, as GoF3's ShipShader), 0 = the highlight x it (glass, capital
// parts; 1 for the structures, whose maps carry it as GoF3's own do).
// URP's PBR specular setup took the map as physical reflectance (the diffuse cut to albedo x (1 - max(spec)), its Fresnel
// toward white at grazing angles, the room reflected at that strength): a grey-white veil over the paint (the Betty II's
// rust and orange gone grey next to GoF3's own copper, 2026-11-02); URP's Blinn-Phong had the highlight four times too
// wide. The gloss comes as smoothness 1 - (2 / (n + 2))^0.25 (the importer's conversion, inverted here). One light, as
// GoF3: the main light (in a GoF2 hangar the key light from above, _GoF2PbrLight, as the other mod materials get it); the
// ambient URP's own; both and the environment dimmed by the occlusion (_GoF2PbrIndirectDim in a GoF2 hangar).
half4 GoF3ShipShader(InputData inputData, SurfaceData surfaceData)
{
    half3 nrm = inputData.normalWS, v = inputData.viewDirectionWS;
    half ndv = saturate(dot(nrm, v));
    half fresnel = (1.0h - ndv) * (1.0h - ndv);
    half environment = _GoF3Params.x, rimAmount = _GoF3Params.y, multiplier = max(_GoF3Params.z, 1e-3h);
    bool inMap = _GoF3Params.w > 0.5h;

    // the texels in GoF3's gamma space
    half3 diffuse = LinearToSRGB(surfaceData.albedo);
    half3 spec = LinearToSRGB(surfaceData.specular) / (inMap ? multiplier : 1.0h);
    half r = 1.0h - surfaceData.smoothness, r2 = r * r;
    half n = clamp(2.0h / max(r2 * r2, 1e-3h) - 2.0h, 2.0h, 1024.0h);
    half gloss = saturate((log2(n) - 1.0h) / 9.0h);
    spec += fresnel * 0.5h * (1.0h - spec);
    gloss += fresnel * 0.5h * (1.0h - gloss);

    half4 shadowMask = CalculateShadowMask(inputData);
    Light key = GetMainLight(inputData.shadowCoord, inputData.positionWS, shadowMask, ReceiveShadows(), IsSurfaceTypeTransparent());
    half3 lightDir = key.direction;
    half3 lightColor = key.color * key.distanceAttenuation;
    if (_GoF2PbrLight.w > 0.0)
    {
        lightDir = normalize(lerp(lightDir, half3(normalize(_GoF2PbrLight.xyz)), (half)_GoF2PbrLight.w));
        lightColor *= lerp(half3(1.0h, 1.0h, 1.0h), _GoF2PbrLightColor, (half)_GoF2PbrLight.w);
    }
    half3 lightG = LinearToSRGB(lightColor);
    half3 ambientG = LinearToSRGB(inputData.bakedGI * surfaceData.occlusion);
    half shadow = key.shadowAttenuation;

    half lambert = saturate(dot(lightDir, nrm)) * shadow;
    half3 light = lightG * lambert + ambientG;
    half specDot = saturate(dot(reflect(-lightDir, nrm), v));
    half highlight = PositivePow(specDot, exp2(9.0h * gloss + 1.0h)) * shadow;
    half3 specular = (highlight * (inMap ? 1.0h : multiplier) * lightG + ambientG * fresnel) * spec;
    half3 outG = diffuse * light * (1.0h - saturate(specular.r)) + specular * (inMap ? multiplier : 1.0h);

    half3 env = 0.0h;
    if (environment > 0.0h)
    {
        // GoF3's generic cubes are grey: the place's environment as its brightness, at the gloss's blur
        half3 envColor = GlossyEnvironmentReflection(reflect(-v, nrm), inputData.positionWS, 1.0h - gloss, surfaceData.occlusion,
            inputData.normalizedScreenSpaceUV, UseEnvironmentReflections());
        half envG = LinearToSRGB(dot(envColor, half3(0.2126h, 0.7152h, 0.0722h)).xxx).r;
        env = saturate(envG * spec * (light + fresnel)) * environment;
        outG = outG * (1.0h - env) + env;
    }
    outG += rimAmount * 0.1h * (1.0h - ndv) * lightG * light;   // (RimLight_LightStrength 1, RimLight_FallOff 1)

    half alpha = surfaceData.alpha;
    if (IsSurfaceTypeTransparent())
    {
        // GoF3's glass: its highlights, reflections and grazing angles less see-through
        alpha = lerp(alpha, 1.0h, saturate(specular.r * (inMap ? multiplier : 1.0h)));
        alpha = lerp(alpha, 1.0h, env.r);
        alpha = lerp(alpha, 1.0h, fresnel);
    }
    half3 rgb = SRGBToLinear(saturate(outG));
    if (UseAlphaPremultiply()) rgb *= alpha;   // (the glass templates blend premultiplied)
    return half4(rgb + surfaceData.emission, alpha);
}
#endif

// EVERSPACE 2's structure layers over the base layer the importer bakes (M_Structures_Parent's compiled base-pass pixel
// shader, its space parts: the ice, snow and algae of its planet levels left out). On the raw UVs (the base maps' Tiling
// undone): the wear (T_Structures_Damage_C_R_MT_H as DamageDetailMask, at 0.5 x DamageDetailMaskTiling; its noise mask
// T_Noise_018_M at 8 x that) where the vertex alpha is low, thresholded by DamageCap over DamageMaskThresholdWidth with a
// smoothstep, none on glowing panels; it takes the colour (the wear map's R through DamageBrightness's overlay), roughness (G),
// metalness (B x DamageMaxMetalness) and normal. The grunge (R at GrungeTiling) darkens by GrungeAmount and roughens; the
// vertex alpha is the occlusion except where worn. BaseRoughness's overlay on the masks' roughness.
half Es2Overlay(half v, half b) { return b < 0.5 ? v * 2.0 * b : 1.0 - (1.0 - v) * 2.0 * (1.0 - b); }

void Es2StructureLayers(Varyings input, inout SurfaceData s)
{
    float2 raw = (input.uv - _BaseMap_ST.zw) / max(_BaseMap_ST.xy, 1e-4);
    float2 wuv = raw * 0.5 * _Es2Wear.x;
    half4 wear = SAMPLE_TEXTURE2D(_Es2WearMap, sampler_BaseMap, wuv);
    half noise = SAMPLE_TEXTURE2D(_Es2WearNoiseMap, sampler_BaseMap, wuv * 8.0).r;
    half m = (noise <= 2.98e-8 ? 0.0 : pow(noise, (half)_Es2Wear.y)) + wear.a;
    half w = max((half)_Es2Wear.w, 1e-4);
    half t = (1.0 - input.color.a) * (half)_Es2Wear.z * (1.0 + 2.0 * w) - w;
    half k = saturate((m - (t - 0.5 * w)) / w);
    half damage = 1.0 - k * k * (3.0 - 2.0 * k);
    if (dot(s.emission, half3(0.2126, 0.7152, 0.0722)) >= 0.05) damage = 0.0;   // (no wear on the glowing panels)
    half g = SAMPLE_TEXTURE2D(_Es2GrungeMap, sampler_BaseMap, raw * _Es2Grunge.y).r;
    half grunge = (1.0 - g) * (half)_Es2Grunge.x;

    s.albedo = lerp(s.albedo, Es2Overlay(wear.r, (half)_Es2Wear2.x).xxx, damage) * (1.0 - grunge);
    s.metallic = lerp(s.metallic, wear.b * (half)_Es2Wear2.y, damage) * (1.0 - 0.1 * grunge);
    half rough = 1.0 - s.smoothness;
    if (_Es2Wear2.w >= 0.0) rough = Es2Overlay(rough, (half)_Es2Wear2.w);
    s.smoothness = 1.0 - saturate(lerp(rough, wear.g, damage) + grunge);
    half3 wn = UnpackNormal(SAMPLE_TEXTURE2D(_Es2WearNormalMap, sampler_BaseMap, wuv));
    s.normalTS = normalize(lerp(s.normalTS, wn, damage));
    s.occlusion *= lerp(input.color.a, 1.0, damage);
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
    if (_Es2Wear2.z > 0.0) Es2StructureLayers(input, surfaceData);   // GoF2: EVERSPACE 2's structure layers

    LODFadeCrossFade(input.positionCS);

    InputData inputData;
    InitializeInputData(input, surfaceData.normalTS, inputData);
    if (_Es2Wear2.z > 0.0)
    {
        // EVERSPACE 2's rim: the colour x (1 + 5 (1 - N.V)^3), saturated
        half f = max(1.0 - saturate(dot(inputData.normalWS, inputData.viewDirectionWS)), 1e-4);
        surfaceData.albedo = saturate(surfaceData.albedo * (1.0 + (half)_Es2Grunge.z * f * f * f));
    }
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

#if defined(_GOF2_PBR) && defined(_SPECULAR_SETUP)
    URP_LIGHT_ACCUM4 color = GoF3ShipShader(inputData, surfaceData);
#else
    URP_LIGHT_ACCUM4 color = UniversalFragmentPBR(inputData, surfaceData,
        IsSpecularSetup(), UseSpecularHighlights(), UseAlphaPremultiply(),
        UseClearCoat() || UseClearCoatMap(), ReceiveShadows(), IsSurfaceTypeTransparent(), UseEnvironmentReflections(), input.normalWS.xyz);
#endif

#if defined(_METALLICSPECGLOSSMAP) && !defined(_GOF2_PBR)
    {
        half spec = SampleMetallicGlossMap(input.uv).a;   // Modified: bicubic like the surface data (_GOF2_BICUBIC)
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

#if defined(_GOF2_PBR) && !defined(_SPECULAR_SETUP)   // (GoF3ShipShader takes the hangar's key light itself)
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
