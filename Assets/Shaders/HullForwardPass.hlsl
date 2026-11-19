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

#include "HullWreck.hlsl"   // a wreck piece's burn and disintegration

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
//   env   = the environment (the material's GoF3 cube, its four blurs picked by gloss, tinted by and plus the system's sky
//           cube at that blur; else the place's brightness) x spec x (light + fresnel), clamped,
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
// A 6:1 strip of cube faces (right, left, up, down, front, back, each as seen from inside): SpaceSkyCore.hlsl's StripUV,
// which this file can't include (its own UnityPerMaterial), so the GoF3 cubes lie as the GoF3 Systems mod's skies do.
float2 GoF3StripUV(float3 d, float texelHeight)
{
    float3 a = abs(d);
    float face; float2 f;
    if (a.x >= a.y && a.x >= a.z) { if (d.x > 0) { face = 0; f = float2(-d.z, d.y) / a.x; } else { face = 1; f = float2(d.z, d.y) / a.x; } }
    else if (a.y >= a.z) { if (d.y > 0) { face = 2; f = float2(d.x, -d.z) / a.y; } else { face = 3; f = float2(d.x, d.z) / a.y; } }
    else { if (d.z > 0) { face = 4; f = float2(d.x, d.y) / a.z; } else { face = 5; f = float2(-d.x, d.y) / a.z; } }
    float inset = 0.5 * texelHeight;
    float2 uv = clamp(f * 0.5 + 0.5, inset, 1.0 - inset);
    return float2((face + uv.x) / 6.0, uv.y);
}

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
        half3 envC;
        if (_GoF3Env.x > 0.5)
        {
            // The material's own GoF3 cube (_GoF3Env; the importer's hulls: GoF3's cubemap_generic_000): GoF3's ShipShader reads
            // it as it is, a monochrome cube with four blurs in its channels, A the blurriest to R the sharpest, the gloss
            // choosing between them (its "Interpolate between R->G->B->A by glossiness", the first step unclamped as GoF3's);
            // the strip in the sky's layout (SpaceSkyCore.hlsl StripUV). In a GoF2 hangar dimmed with the indirect light.
            half4 c = SAMPLE_TEXTURE2D_LOD(_GoF3EnvMap, sampler_LinearClamp, GoF3StripUV(reflect(-v, nrm), _GoF3Env.y), 0);
            half envG = c.a;
            envG = lerp(envG, c.b, gloss / 0.33h);
            envG = lerp(envG, c.g, saturate((gloss - 0.33h) / 0.33h));
            envG = lerp(envG, c.r, saturate((gloss - 0.66h) / 0.33h));
            // Remake (2026-11-12, the user's choice): the place's own sky too. GoF3's cube shapes the reflection; the system's
            // reflection cubemap (_GoF2ReflectionCube, SkyReflection: each system's sky, a hangar's own), at the gloss's blur,
            // colours it, by its hue as much as the sky is bright there (dark sky: GoF3's own grey), and adds its light (a
            // bright sun or nebula shows on the hull).
            half3 sky = LinearToSRGB(SAMPLE_TEXTURECUBE_LOD(_GoF2ReflectionCube, sampler_GoF2ReflectionCube, reflect(-v, nrm),
                (1.0h - gloss) * 6.0h).rgb) * _GoF2HullReflection;
            half skyLum = dot(sky, half3(0.2126h, 0.7152h, 0.0722h));
            half3 tint = lerp(half3(1.0h, 1.0h, 1.0h), min(sky / max(skyLum, 1e-3h), 2.0h), saturate(skyLum * 4.0h));
            envC = (envG * tint + sky) * surfaceData.occlusion;
        }
        else
        {
            // Without one: the place's environment as its brightness, at the gloss's blur
            half3 envColor = GlossyEnvironmentReflection(reflect(-v, nrm), inputData.positionWS, 1.0h - gloss, surfaceData.occlusion,
                inputData.normalizedScreenSpaceUV, UseEnvironmentReflections());
            envC = LinearToSRGB(dot(envColor, half3(0.2126h, 0.7152h, 0.0722h)).xxx);
        }
        env = saturate(envC * spec * (light + fresnel)) * environment;
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

// Chorus's trim sheets and platings (Deep Silver FISHLABS' Chorus, Unreal 4: M_TrimLayersMaster / M_NPCShipTrimMaster /
// M_PlatingMaster, from their compiled base-pass pixel and vertex shaders; the Chorus importer's tools/npcs.py TRIM_SHADER).
// A hull's colour is a palette swatch per face: LayerColors and LayerData read at UV3 (the swatch's colour; LayerData: R / G
// the roughness range, B the metal flag). Trim: x the tiling layer (Tiling Diffuse, its RMO: R where in that roughness range,
// G added metal, B occlusion; its normal) over UV1 x Base UV Tile Amount; the trim sheet over UV0: Trim AAO (R the accent
// mask: Accent 1 Color, its metal and roughness bias; B occlusion x Trim AO Intensity), Trim Normal (combined with the tiling
// normal, reoriented), Trim Emissive (its channels in their tints x Trim Emissive Amount, only on faces shifted out of the
// palette when the master masks by the UV shift). Plating: x Albedo, ORM (R occlusion, G where in the roughness range, B
// added metal) and Normal over UV0 x Tiling. Both: dirt by the vertex colour (Dirt BaseColor x Dirt Tint over UV1, its mask
// and roughness), which also takes the metal off. The far kicker rim and emission boost come after the lighting inputs
// (ChorusFarEmission). Chorus's selection highlight and its dissolve / burn effects are left out.
static half3 gChorusEmissive;

half4 ChorusSwatch(float2 uv3, bool data)
{
    float w, h;
    _ChorusPalette.GetDimensions(w, h);
    float half_ = floor(w * 0.5);
    int2 p = int2(min(floor(frac(uv3) * float2(half_, h)), float2(half_, h) - 1.0));   // (UE wraps the palette's sampler)
    if (data) p.x += (int)half_;
    return LOAD_TEXTURE2D(_ChorusPalette, p);
}

// The tiling normal (its own UVs' frame, from the derivatives as Chorus builds it) in UV0's tangent frame.
half3 ChorusTilingNormal(float2 uv, float3 positionWS, half3 n, half3 t0, half3 b0)
{
    half3 ts = UnpackNormal(SAMPLE_TEXTURE2D(_DetailNormalMap, sampler_LinearRepeat, uv));
    float3 dp1 = ddx(positionWS), dp2 = ddy(positionWS);
    float2 du1 = ddx(uv), du2 = ddy(uv);
    float3 p2 = cross(dp2, n), p1 = cross(n, dp1);
    float3 t = p2 * du1.x + p1 * du2.x, b = p2 * du1.y + p1 * du2.y;
    float k = rsqrt(max(max(dot(t, t), dot(b, b)), 1e-20));
    float3 ws = t * k * ts.x + b * k * ts.y + n * ts.z;
    return normalize(half3(dot(ws, t0), dot(ws, b0), dot(ws, n)));
}

void ChorusLayers(Varyings input, inout SurfaceData s)
{
    bool plating = _ChorusParams.x > 1.5;
    float2 uv0 = input.uv, uv1 = input.chorusUV.xy, uv3 = input.chorusUV.zw;
    half4 swatch = ChorusSwatch(uv3, false), data = ChorusSwatch(uv3, true);
    float2 tuv = (plating ? uv0 : uv1) * _ChorusParams.y;
    half3 diffuse = SAMPLE_TEXTURE2D(_BaseMap, sampler_LinearRepeat, tuv).rgb;
    half4 rmo = SAMPLE_TEXTURE2D(_MetallicGlossMap, sampler_LinearRepeat, tuv);
    half3 n = normalize(input.normalWS.xyz);

    half accent = 0.0h, ao, metal, rough, occl;
    half3 albedo = swatch.rgb * diffuse;
    if (plating)
    {
        metal = rmo.b; rough = rmo.g; ao = rmo.r;
        s.normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_LinearRepeat, tuv), _BumpScale);
    }
    else
    {
        half4 aao = SAMPLE_TEXTURE2D(_OcclusionMap, sampler_LinearRepeat, uv0);
        accent = aao.r * _ChorusAccent.a;
        metal = rmo.g; rough = rmo.r;
        ao = lerp(rmo.b, rmo.b * aao.b, (half)_ChorusParams.z);
        half3 trim = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_LinearRepeat, uv0), _BumpScale);
    #if defined(REQUIRES_WORLD_SPACE_TANGENT_INTERPOLATOR)
        half3 t0 = normalize(input.tangentWS.xyz);
        half3 b0 = input.tangentWS.w * cross(n, t0);
        half3 tiling = ChorusTilingNormal(tuv, input.positionWS, n, t0, b0);
    #else
        half3 tiling = half3(0.0h, 0.0h, 1.0h);
    #endif
        // under the accent the tiling normal at Accent 1 Tiling Base Normal Intensity
        tiling = lerp(tiling, normalize(lerp(half3(0.0h, 0.0h, 1.0h), tiling, (half)_ChorusAccent2.z)), accent);
        // reoriented normal mapping: the trim sheet's detail on the tiling layer
        half3 tb = tiling + half3(0.0h, 0.0h, 1.0h), td = trim * half3(-1.0h, -1.0h, 1.0h);
        s.normalTS = normalize(tb * dot(tb, td) / tb.z - td);
    }
    metal = saturate((data.b - 0.5h) * 2.0h + metal);
    rough = lerp(data.r, data.g, rough);
    if (accent > 0.0h)
    {
        albedo = lerp(albedo, _ChorusAccent.rgb, accent);
        metal = saturate(lerp(metal, (half)_ChorusAccent2.x, accent));
        rough = saturate(rough + accent * (half)_ChorusAccent2.y);
    }
    half dirtD = 0.0h;
    if (_ChorusDirt.a > 0.0)
    {
        half vc = input.color.r;
        half2 dr = SAMPLE_TEXTURE2D(_ChorusDirtRough, sampler_LinearRepeat, uv1 * _ChorusDirt2.x).rb;
        half mask = SAMPLE_TEXTURE2D(_DetailMask, sampler_LinearRepeat, uv1 * _ChorusDirt2.y).r;
        half d = saturate(saturate((1.0h - ao) * (half)_ChorusDirt2.z + (vc - 0.5h) * 2.0h) * 2.0h + mask - 1.0h);
        half c = (half)_ChorusDirt2.w;
        // (M_NPCShipTrimMaster's dirt, the Disruptor's: x the vertex alpha alone, _ChorusDirt3.y)
        d = saturate(lerp(-c, 1.0h + c, d)) * (_ChorusDirt3.y > 0.5 ? input.color.a : vc) * dr.y;
        if (_ChorusDirt3.x > 0.5) d *= input.color.a;
        dirtD = d;
        half3 dirt = _ChorusDirt.rgb * SAMPLE_TEXTURE2D(_DetailAlbedoMap, sampler_LinearRepeat, uv1 * _ChorusDirt2.x).rgb;
        albedo = saturate(lerp(albedo, dirt, d));
        metal = saturate(metal * (1.0h - d * d));
        rough = saturate(rough + 2.0h * (dr.x - 0.5h) * d * d);
    }
    s.albedo = saturate(albedo);
    s.metallic = metal;
    s.smoothness = 1.0h - saturate(rough);
    s.occlusion = saturate(ao);

    half3 e = 0.0h;
    if (!plating && _ChorusEmitR.a > 0.0)
    {
        half3 m = SAMPLE_TEXTURE2D(_EmissionMap, sampler_LinearRepeat, uv0).rgb;
        e = m.r * _ChorusEmitR.rgb + m.g * _ChorusEmitG.rgb + m.b * _ChorusEmitB.rgb;
        // (the masters' "UV shift" mask: Chorus lights only the faces whose palette UV is shifted below 0, past 1 here)
        half on = _ChorusEmitG.a > 0.5 ? (uv3.y > 1.0 ? 1.0h : 0.0h) : 1.0h;
        e *= on * (half)_ChorusEmitR.a;
        if (_ChorusDirt3.z > 0.5) e *= 1.0h - dirtD;   // (M_NPCShipTrimMaster's: the dirt covers the emissive too)
    }
    gChorusEmissive = e;
    s.emission = e * (half)(_ChorusKicker2.z * _ChorusParams.w);
}

// Chorus's M_Guilt_Shell (_ChorusParams.x 3; Guilt's two shells), after its compiled base and masked depth passes: a dither
// and the flowing noise dissolve it (HullInput.hlsl ChorusShellClip, the shadow and depth passes too); its colour, metal and
// roughness flat; its normal the noise map's red and green at the flowed UVs as a tangent normal (Unreal's green flipped),
// blended as the noise; its glow the glow map (_EmissionMap) flowing the same way, less Emissive Exponent, x Emissive Color
// x Intensity x saturate(1 - the distance from the model's origin / Center Emissive Radius)^2.
half3 ChorusShellNormal(half2 rg)
{
    half2 xy = rg * 2.0h - 1.0h;
    return half3(xy.x, -xy.y, sqrt(max(1.0h - dot(xy, xy), 0.0h)));
}

void ChorusShell(Varyings input, inout SurfaceData s)
{
    float2 uv0 = input.uv, a, b; float k;
    ChorusShellClip(uv0, input.positionCS.xy);
    ChorusShellFlow(uv0, a, b, k);
    float2 dx = ddx(uv0 * _ChorusParams.y), dy = ddy(uv0 * _ChorusParams.y);   // (the flowed UVs jump; their gradients don't)
    half3 na = ChorusShellNormal(SAMPLE_TEXTURE2D_GRAD(_OcclusionMap, sampler_LinearRepeat, a, dx, dy).rg);
    half3 nb = ChorusShellNormal(SAMPLE_TEXTURE2D_GRAD(_OcclusionMap, sampler_LinearRepeat, b, dx, dy).rg);
    s.albedo = (half)_ChorusEmitG.z;
    s.metallic = (half)_ChorusEmitG.w;
    s.smoothness = 1.0h - (half)_ChorusEmitB.x;
    s.occlusion = 1.0h;
    s.normalTS = normalize(lerp(nb, na, (half)k));
    s.alpha = 1.0h;
    half3 at = lerp(SAMPLE_TEXTURE2D_GRAD(_EmissionMap, sampler_LinearRepeat, b, dx, dy).rgb, SAMPLE_TEXTURE2D_GRAD(_EmissionMap, sampler_LinearRepeat, a, dx, dy).rgb, (half)k);
    float d = length(TransformWorldToObject(input.positionWS));
    half fall = saturate(1.0h - (half)(d / _ChorusAccent2.y)); fall *= fall;
    s.emission = max(at - (half)_ChorusAccent2.x, 0.0h) * _ChorusAccent.rgb * (half)(_ChorusAccent.a * _ChorusParams.w) * fall;
    gChorusEmissive = 0.0h;
}

// GoF2: Chorus's M_Phoenix glow lines (its compiled base pass): the Magnetic Plates Pattern (R its strength, G and B its
// phase) over UV0 x Magnetic Pattern Tiling; frac(G - (B + time)) a triangle wave 1 - 2 |phase - 0.5| running along it;
// lerp(Emissive02 Min, Max, wave) x wave x R x the emissive masks' green x Emissive02_Multiplier, added.
half3 GlowWave(float2 uv)
{
    half3 p = SAMPLE_TEXTURE2D(_WavePattern, sampler_LinearRepeat, uv * _WaveParams.y).rgb;
    half4 m = SAMPLE_TEXTURE2D(_WaveMask, sampler_LinearRepeat, uv);
    half mask = _WaveParams.w < 0.5 ? m.r : (_WaveParams.w < 1.5 ? m.g : m.b);
    half phase = frac(p.g - p.b - (half)(_Time.y * _WaveParams.z));
    half t = 1.0h - 2.0h * abs(phase - 0.5h);
    return lerp((half3)_WaveMin.rgb, (half3)_WaveMax.rgb, t) * (t * p.r * mask * (half)_WaveParams.x);
}

// GoF2: Chorus's flowing lava (M_BrokenPlanet_Core's compiled base pass: Nimika Prime's broken planet's core), added to the
// emission. Over UV0, with r the part mask (_LavaMasks.b) and t the time: the first flow, Core - Flow Emissive Diffuse at
// uv x Magma Tiling pushed along Core - Flowmap (at uv x Flowmap Tiling, its mip FlowMipLevel; rg x 2 - 1 x FlowIntensity
// x r, x mirrored) by two phases half a cycle apart, t x FlowSpeed - TimeOffset (R at uv x its tiling, x its intensity),
// the first's UVs wrapped and offset by Flowmap UV Texture Offset, blended by a triangle wave; the second the same with
// its own maps (no part mask, no intensity on its offset, its first UVs + 0.5), x 2nd magma multiply ^ 2nd magma Exp;
// mixed by the first's (x 2nd core mask multiply)^exp, x r; the temperature Temp Min + (Max - Min) x that x r; the glow
// Chorus's blackbody (HullWreck.hlsl) x the core mask (_LavaMasks.a) x _LavaE.w. (The flowing normals are left baked.) The
// flow's offsets are Unreal's (v down): on the remake's UVs (v up) their v turns over, as the texture offset's.
float LavaTri(float f) { return f < 0.5 ? 2.0 * f : 2.0 - 2.0 * f; }

half3 LavaFlow(float2 uv)
{
    float t = _Time.y;
    half4 m = SAMPLE_TEXTURE2D(_LavaMasks, sampler_LinearRepeat, uv);
    float r = m.b;
    // the first flow
    float2 uvM = uv * _LavaA.x;
    float ph = t * _LavaA.y - SAMPLE_TEXTURE2D(_LavaMasks, sampler_LinearRepeat, uv * _LavaB.x).r * _LavaB.y;
    float2 fv = (SAMPLE_TEXTURE2D_LOD(_LavaFlowMaps, sampler_LinearRepeat, uv * _LavaA.z, _LavaF.x).rg * 2.0 - 1.0) * _LavaA.w * r * float2(-1.0, -1.0);
    float2 dx = ddx(uvM), dy = ddy(uvM);
    float a1 = SAMPLE_TEXTURE2D_GRAD(_LavaEmit, sampler_LinearRepeat, frac(uvM + fv * frac(ph - 0.5)) + float2(_LavaB.z, -_LavaB.w), dx, dy).r;
    float b1 = SAMPLE_TEXTURE2D_GRAD(_LavaEmit, sampler_LinearRepeat, uvM + fv * frac(ph), dx, dy).r;
    float lava1 = lerp(a1, b1, LavaTri(frac(ph)));
    float k = lava1 * _LavaD.y > 0.0 ? pow(lava1 * _LavaD.y, _LavaD.z) : 0.0;
    // the second
    float2 uv2 = uv * _LavaC.x;
    float ph2 = t * _LavaC.y - SAMPLE_TEXTURE2D(_LavaMasks, sampler_LinearRepeat, uv * _LavaD.x).g;
    float2 fv2 = (SAMPLE_TEXTURE2D_LOD(_LavaFlowMaps, sampler_LinearRepeat, uv * _LavaC.z, _LavaF.y).ba * 2.0 - 1.0) * _LavaC.w * float2(-1.0, -1.0);
    float2 dx2 = ddx(uv2), dy2 = ddy(uv2);
    float a2 = SAMPLE_TEXTURE2D_GRAD(_LavaEmit, sampler_LinearRepeat, frac(uv2 + fv2 * frac(ph2 - 0.5)) + 0.5, dx2, dy2).g;
    float b2 = SAMPLE_TEXTURE2D_GRAD(_LavaEmit, sampler_LinearRepeat, uv2 + fv2 * frac(ph2), dx2, dy2).g;
    float l2 = lerp(a2, b2, LavaTri(frac(ph2))) * _LavaD.w;
    float lava2 = l2 > 0.0 ? pow(l2, _LavaE.x) : 0.0;
    float lava = lerp(lava1, lava2, k) * r;
    float T = _LavaE.y + (_LavaE.z - _LavaE.y) * lava * r;
    return (half3)(max(HullWreckBlackbody(T), 0.0) * m.a * _LavaE.w);
}

// GoF2: Chorus's hit flash (M_Phoenix's compiled base pass, its MF_HitEffect): u = the flash's age 0..1; a ring
// max(1 - |max(1 - d / (u R), 0) - 0.07| / 0.07, 0) growing from the hit to R, plus (1 - N.V)^7 x saturate(1 - d / R);
// x (1 - u) x HitColor x HitEmissiveStrength, x min(((P(2 uv) + P(5 uv - 0.5 turned by 0.1 t, + 0.5)) / 2)^4, 1) with P
// T_Lu_Noise_29's red (here _WavePattern.a; none: 1).
half3 HitFlash(float2 uv, float3 positionWS, float3 normalWS)
{
    float u = _HitFx.x, R = max(_HitFx.y, 1e-5);
    float d = distance(positionWS, _HitPos.xyz);
    float3 v = normalize(GetCameraPositionWS() - positionWS);
    float rim = 1.0 - max(dot(normalize(normalWS), v), 0.0);
    float glow = rim > 0.0 ? pow(rim, 7.0) * saturate(1.0 - d / R) : 0.0;
    float ring = max(1.0 - abs(max(1.0 - d / max(u * R, 1e-5), 0.0) - 0.07) * 14.285714, 0.0);
    float mask = 1.0;
    if (_HitFx.w < 1.5)
    {
        float s, c;
        sincos(_Time.y * 0.1, s, c);
        float2 q = uv * 5.0 - 0.5;
        float2 turned = float2(c * q.x - s * q.y, s * q.x + c * q.y) + 0.5;
        float p = (SAMPLE_TEXTURE2D(_WavePattern, sampler_LinearRepeat, uv * 2.0).a + SAMPLE_TEXTURE2D(_WavePattern, sampler_LinearRepeat, turned).a) * 0.5;
        mask = min(p * p * p * p, 1.0);
    }
    return (half3)(_HitColor.rgb * ((ring + glow) * (1.0 - u) * mask));
}

// GoF2: Chorus's crystal (M_JTU_Blend's compiled base pass: the Omen crystal, the Prophet's crown), _ChorusParams.x 4. Two
// surfaces A and B, each mapped along the mesh's own tangent frame at a world scale (the object-space position in cm on
// its tangent and bitangent / A or B - Surface Textures World Scale), blended by the vertex colour's red: sharpened by a
// 3-octave turbulence (its world scale, contrast, level offset) and the breath, then against A's emissive mask (red) as a
// height with VC Transition Softness. A: its colour tinted to the Macro Color by A's mask at x Macro Tiling Factor; both
// overlaid with the inner variation. Normals x their intensities, metal and roughness (+ biases; A's toward Macro Roughness
// by the macro mask). The inner glow: the vein heightmap (red) at the position / Inner Variation World Scale pushes the
// inner variation's lookup (green) along the view by Inner HeightRatio (h - 1) and along the normal's tilt by Inner
// Refraction from Normal; x Inner Glow Color x the breath (a CA_BaseVFX row at frac(time / Breath Interval - the distance
// from the object's origin / Pulse Wave Period): red the glow, green added to the blend x Breath Amount) x A's mask x A -
// Emissive Intensity, or B's mask x B - Emissive Pulse Intensity x B - Emissive Intensity, blended. Slots: _BaseMap /
// _BumpMap / _MetallicGlossMap A's colour / normal / MRO, _DetailAlbedoMap / _DetailNormalMap / _OcclusionMap B's,
// _EmissionMap A's emissive mask, _ChorusPalette B's, _Es2WearMap the vein heightmap, _Es2WearNoiseMap the inner
// variation, _ChorusDirtRough the breath row. _ChorusParams: y A's world scale, z B's, w the emission's scale; _ChorusAccent
// rgb Macro Color, a Macro Color Amt; _ChorusAccent2 x Macro Tiling Factor, y Macro Roughness, z Macro Roughness Amt, w
// Specular; _ChorusEmitR rgb Inner Glow Color, a A - Emissive Intensity; _ChorusEmitG x B - Emissive Pulse Intensity, y B -
// Emissive Intensity, z Inner Refraction from Normal, w Inner HeightRatio; _ChorusEmitB x Inner Variation World Scale, y
// Noise World Scale, z Noise Contrast, w NoiseLeveOffset; _ChorusKicker x Breath Interval, y Pulse Wave Period UU, z Breath
// Amount, w VC Transition Softness; _ChorusKicker2 x A - NormalIntensity, y B - NormalIntensity; _ChorusDirt x A - Roughness
// Bias, y B - Roughness Bias.
float CrystalHash(float3 p) { p = frac(p * 0.3183099 + 0.1); p *= 17.0; return frac(p.x * p.y * p.z * (p.x + p.y + p.z)); }
float CrystalNoise(float3 x)   // value noise in -1..1 (Unreal's gradient noise texture stands in)
{
    float3 i = floor(x), f = frac(x);
    f = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);
    float n = lerp(lerp(lerp(CrystalHash(i), CrystalHash(i + float3(1, 0, 0)), f.x), lerp(CrystalHash(i + float3(0, 1, 0)), CrystalHash(i + float3(1, 1, 0)), f.x), f.y),
                   lerp(lerp(CrystalHash(i + float3(0, 0, 1)), CrystalHash(i + float3(1, 0, 1)), f.x), lerp(CrystalHash(i + float3(0, 1, 1)), CrystalHash(i + float3(1, 1, 1)), f.x), f.y), f.z);
    return n * 2.0 - 1.0;
}
half3 CrystalOverlay(half3 a, half b) { return lerp(2.0h * a * b, 1.0h - 2.0h * (1.0h - a) * (1.0h - b), step(0.5h, a)); }

void ChorusCrystal(Varyings input, inout SurfaceData s)
{
    half3 n = normalize(input.normalWS.xyz);
#if defined(REQUIRES_WORLD_SPACE_TANGENT_INTERPOLATOR)
    half3 t = normalize(input.tangentWS.xyz);
    half3 b = input.tangentWS.w * cross(n, t);
#else
    half3 t = normalize(cross(abs(n.y) < 0.99h ? half3(0.0h, 1.0h, 0.0h) : half3(1.0h, 0.0h, 0.0h), n));
    half3 b = cross(n, t);
#endif
#if defined(REQUIRES_WORLD_SPACE_POS_INTERPOLATOR)
    float3 posWS = input.positionWS;
#else
    float3 posWS = float3(0, 0, 0);
#endif
    // the object-space position in Chorus's centimetres (the model's units are its metres) on the tangent frame
    float3 p = mul(GetWorldToObjectMatrix(), float4(posWS, 1.0)).xyz * 100.0;
    float3 to = normalize(mul((float3x3)GetWorldToObjectMatrix(), t)), bo = normalize(mul((float3x3)GetWorldToObjectMatrix(), b));
    float2 proj = float2(dot(p, to), dot(p, bo));
    float2 uvA = proj / max((float)_ChorusParams.y, 1.0), uvB = proj / max((float)_ChorusParams.z, 1.0);

    half maskA = SAMPLE_TEXTURE2D(_EmissionMap, sampler_LinearRepeat, uvA).r;
    // the breath: a wave out from the object's origin
    // (a Breath Interval of 0: no breathing, the curve's start; the Prophet's crown)
    float phase = _ChorusKicker.x > 0.0 ? frac(_Time.y / _ChorusKicker.x - length(p) / max((float)_ChorusKicker.y, 1.0)) : 0.0;
    half2 breath = SAMPLE_TEXTURE2D_LOD(_ChorusDirtRough, sampler_LinearClamp, float2(phase, 0.5), 0).rg;
    // the blend: the vertex colour sharpened by the turbulence and the breath, against A's mask
    float3 q = p / max((float)_ChorusEmitB.y, 1.0);
    float turb = 0.0, amp = 1.0;
    [unroll] for (int o = 0; o < 3; o++) { turb += abs(CrystalNoise(q)) * amp; q *= 2.0; amp *= 0.5; }
    half c = (half)_ChorusEmitB.z;
    half nz = saturate((half)(turb + _ChorusEmitB.w) * (2.0h * c + 1.0h) - c) - 0.5h;
    half v = input.color.r - 0.5h;
    half x = nz + (2.0h * v) * (2.0h * v) * (2.0h * v - nz);
    x += breath.g * (half)_ChorusKicker.z;
    half th = (1.0h - saturate((x + 1.0h) * 0.5h)) * ((half)_ChorusKicker.w + 1.0h) - (half)_ChorusKicker.w;
    half blend = saturate(saturate((maskA - th) / max((half)_ChorusKicker.w, 1e-5h)) * 5.0h - 2.0h);

    // the normals, blended
    half3 nA = UnpackNormal(SAMPLE_TEXTURE2D(_BumpMap, sampler_LinearRepeat, uvA)) * half3((half)_ChorusKicker2.x, (half)_ChorusKicker2.x, 1.0h);
    half3 nB = UnpackNormal(SAMPLE_TEXTURE2D(_DetailNormalMap, sampler_LinearRepeat, uvB)) * half3((half)_ChorusKicker2.y, (half)_ChorusKicker2.y, 1.0h);
    half3 nT = normalize(lerp(nA, nB, blend));
    s.normalTS = nT;
    half3 nW = normalize(t * nT.x + b * nT.y + n * nT.z);

    // the inner glow's variation, through the veins' parallax and the normal's refraction
    float3 view = normalize(GetCameraPositionWS() - posWS);
    float2 uvI = proj / max((float)_ChorusEmitB.x, 1.0);
    half h = SAMPLE_TEXTURE2D(_Es2WearMap, sampler_LinearRepeat, uvI).r;
    half hr = (half)_ChorusEmitG.w;
    uvI += float2(dot(t, view), dot(b, view)) * (hr * h - hr) + float2(dot(t, nW), dot(b, nW)) * _ChorusEmitG.z;
    half variation = SAMPLE_TEXTURE2D(_Es2WearNoiseMap, sampler_LinearRepeat, uvI).g;

    // the colours: A tinted to the macro colour, both overlaid with the variation
    half macro = SAMPLE_TEXTURE2D(_EmissionMap, sampler_LinearRepeat, uvA * _ChorusAccent2.x).r;
    half3 colA = lerp(SAMPLE_TEXTURE2D(_BaseMap, sampler_LinearRepeat, uvA).rgb, (half3)_ChorusAccent.rgb, macro * (half)_ChorusAccent.a);
    half3 colB = SAMPLE_TEXTURE2D(_DetailAlbedoMap, sampler_LinearRepeat, uvB).rgb;
    s.albedo = saturate(lerp(CrystalOverlay(colA, variation), CrystalOverlay(colB, variation), blend));
    half2 mroA = SAMPLE_TEXTURE2D(_MetallicGlossMap, sampler_LinearRepeat, uvA).rg;
    half2 mroB = SAMPLE_TEXTURE2D(_OcclusionMap, sampler_LinearRepeat, uvB).rg;
    half roughA = mroA.y + (half)_ChorusDirt.x;
    roughA += (half)_ChorusAccent2.z * macro * ((half)_ChorusAccent2.y - roughA);
    half roughB = mroB.y + (half)_ChorusDirt.y;
    s.metallic = saturate(lerp(mroA.x, mroB.x, blend));
    s.smoothness = 1.0h - saturate(lerp(roughA, roughB, blend));
    s.occlusion = 1.0h;

    // the inner glow
    half3 glow = variation * (half3)_ChorusEmitR.rgb * breath.r;
    half3 eA = glow * maskA * (half)_ChorusEmitR.a;
    half3 eB = glow * SAMPLE_TEXTURE2D(_ChorusPalette, sampler_LinearRepeat, uvB).r * (half)(_ChorusEmitG.x * _ChorusEmitG.y);
    gChorusEmissive = lerp(eA, eB, blend);
    s.emission = gChorusEmissive * (half)_ChorusParams.w;
}

// GoF2: Chorus's Omen tissue (M_JTU_Faceless's compiled base pass: the Faceless ships and fortresses), _ChorusParams.x 5.
// A black base, no metal: all glow. In UV0, V the view in the tangent frame: the outer and inner variations (red) through a
// parallax of (Thickness.r - 0.5) x Depth; T_Dirt_02's blue at UV0 x NoiseTiling - 0.19 V; the ice mask (red) at UV0 x
// NoiseTiling + 0.1 t - 0.939 V, x the inverse fresnel 1 - (0.04 + 0.96 (1 - N.V)^0.1424); the breath (a CA_BaseVFX row's
// rgb at frac(time / Breath Interval - the distance from the object's origin / Pulse Wave Period)). The glow
// lerp(Outer Glow x (dirt + outer), Inner Glow x (ice x fresnel + inner), clamp((outer + inner) x saturate(1 / (Thickness.r x
// Strength)) + breath, 0.5, 1)); roughness lerp(RoughnessIntensity, 0.05, Thickness.g); occlusion the AO map. Slots:
// _BumpMap its normal, _OcclusionMap its AO, _MetallicGlossMap its thickness, _DetailAlbedoMap the outer variation,
// _Es2WearMap the inner variation, _Es2WearNoiseMap the ice mask, _ChorusPalette T_Dirt_02, _ChorusDirtRough the breath
// row. _ChorusEmitR / G rgb the outer / inner glow; _ChorusEmitB x NoiseTiling, y Depth, z Strength, w RoughnessIntensity;
// _ChorusKicker x Breath Interval (0: none), y Pulse Wave Period UU; _ChorusParams.w the emission's scale. The warp-in
// dissolve's edge glow (Driver, Edge Thickness) is left out (at rest it shows none). Enable Core (MI_JTU_OmenDroneCore's
// compiled base pass): the glow + m x (the glow + saturate(N.V)^Inner Contrast x Inner Core Color), m = saturate(1 - the
// distance from Sphere Offset / sphere radius (object space, Chorus's cm)) x Thickness.r x (1 - (0.04 + 0.96 (1 - N.V)^5));
// _ChorusAccent rgb Inner Core Color, a Inner Contrast (0: no core); _ChorusDirt xyz Sphere Offset, w sphere radius.
void ChorusTissue(Varyings input, inout SurfaceData s)
{
    half3 n = normalize(input.normalWS.xyz);
#if defined(REQUIRES_WORLD_SPACE_TANGENT_INTERPOLATOR)
    half3 t = normalize(input.tangentWS.xyz);
    half3 b = input.tangentWS.w * cross(n, t);
#else
    half3 t = normalize(cross(abs(n.y) < 0.99h ? half3(0.0h, 1.0h, 0.0h) : half3(1.0h, 0.0h, 0.0h), n));
    half3 b = cross(n, t);
#endif
#if defined(REQUIRES_WORLD_SPACE_POS_INTERPOLATOR)
    float3 posWS = input.positionWS;
#else
    float3 posWS = float3(0, 0, 0);
#endif
    float2 uv = input.uv;
    half3 view = normalize(GetCameraPositionWS() - posWS);
    float2 vt = float2(dot(t, view), dot(b, view));
    half3 nT = UnpackNormal(SAMPLE_TEXTURE2D(_BumpMap, sampler_LinearRepeat, uv));
    s.normalTS = nT;
    half3 nW = normalize(t * nT.x + b * nT.y + n * nT.z);

    half2 thick = SAMPLE_TEXTURE2D(_MetallicGlossMap, sampler_LinearRepeat, uv).rg;
    float tiling = _ChorusEmitB.x, depth = _ChorusEmitB.y;
    float2 uvP = uv + vt * (depth * (thick.r - 0.5));
    half outer = SAMPLE_TEXTURE2D(_DetailAlbedoMap, sampler_LinearRepeat, uvP).r;
    half inner = SAMPLE_TEXTURE2D(_Es2WearMap, sampler_LinearRepeat, uvP).r;
    half dirt = SAMPLE_TEXTURE2D(_ChorusPalette, sampler_LinearRepeat, uv * tiling - vt * 0.19).b;
    half ice = SAMPLE_TEXTURE2D(_Es2WearNoiseMap, sampler_LinearRepeat, uv * tiling + _Time.y * 0.1 - vt * 0.939183891).r;
    half f = 1.0h - max(dot(nW, view), 0.0h);
    half fres = f > 0.0h ? 1.0h - (pow(f, 0.142417h) * 0.96h + 0.04h) : 0.96h;
    half k = thick.r * (half)_ChorusEmitB.z;
    k = k > 0.0h ? saturate(1.0h / k) : 0.0h;
    float3 p = mul(GetWorldToObjectMatrix(), float4(posWS, 1.0)).xyz * 100.0;
    float phase = _ChorusKicker.x > 0.0 ? frac(_Time.y / _ChorusKicker.x - length(p) / max((float)_ChorusKicker.y, 1.0)) : 0.0;
    half3 breath = SAMPLE_TEXTURE2D_LOD(_ChorusDirtRough, sampler_LinearClamp, float2(phase, 0.5), 0).rgb;
    half3 w = clamp((outer + inner) * k + breath, 0.5h, 1.0h);
    half3 glow = lerp((half3)_ChorusEmitR.rgb * (dirt + outer), (half3)_ChorusEmitG.rgb * (ice * fres + inner), w);
    if (_ChorusAccent.a > 0.0)
    {
        half ndv = max(dot(nW, view), 0.0h);
        half x = 1.0h - ndv;
        half rim = x > 0.0h ? 1.0h - (x * x * x * x * x * 0.96h + 0.04h) : 0.96h;
        half m = saturate(1.0h - (half)(length(p - _ChorusDirt.xyz) / max(_ChorusDirt.w, 1e-5))) * thick.r * rim;
        half c = min(ndv, 1.0h);
        half3 core = c > 0.0h ? pow(c, (half)_ChorusAccent.a) * (half3)_ChorusAccent.rgb : 0.0h;
        glow += m * (glow + core);
    }

    s.albedo = 0.0h;
    s.metallic = 0.0h;
    half ri = (half)_ChorusEmitB.w;
    s.smoothness = 1.0h - saturate(lerp(ri, 0.05h, thick.g));
    s.occlusion = saturate(SAMPLE_TEXTURE2D(_OcclusionMap, sampler_LinearRepeat, uv).r);
    gChorusEmissive = glow;
    s.emission = glow * (half)_ChorusParams.w;
}

// M_NPCShipTrimMaster's far emission: past Kicker Fade Offset (view depth), over Kicker Fade Length, the kicker rim
// ((1 - N.V)^Kicker Exp x 0.96 + 0.04 in Kicker Color) and the trim emissive x (Emissive Channel Distance Boost - 1) on top.
half3 ChorusFarEmission(InputData inputData)
{
    if (_ChorusKicker.a <= 0.0) return 0.0h;
    half ndv = 1.0h - saturate(dot(inputData.normalWS, inputData.viewDirectionWS));
    half f = (ndv <= 0.0h ? 0.0h : PositivePow(ndv, (half)_ChorusKicker.a)) * 0.96h + 0.04h;
    float depth = -TransformWorldToView(inputData.positionWS).z;
    half fade = saturate((depth - _ChorusKicker2.x) / max(_ChorusKicker2.y, 1e-3));
    return fade * (f * _ChorusKicker.rgb + ((half)_ChorusEmitB.a - 1.0h) * gChorusEmissive) * (half)_ChorusParams.w;
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
    // The mods' own surface features, compiled only into the mods' variant (_GOF2_PBR: every mod material is a copy of a
    // ModAssets template, all of which carry it; the game's hulls never do), so the game's ships, stations and asteroids
    // don't carry their code: a mobile GPU sizes a shader's registers for its heaviest path, branch taken or not (2026-11-14,
    // the user's "what can we do to improve performance for ios"). Wreck effects, decals, liveries and the sun's tint stay
    // in every variant: they reach the game's hulls too.
#if defined(_GOF2_PBR)
    if (_Es2Wear2.z > 0.0) Es2StructureLayers(input, surfaceData);   // GoF2: EVERSPACE 2's structure layers
    if (_ChorusParams.x > 4.5) ChorusTissue(input, surfaceData);     // GoF2: Chorus's Omen tissue (the Faceless)
    else if (_ChorusParams.x > 3.5) ChorusCrystal(input, surfaceData);    // GoF2: Chorus's crystal (the Prophet's crown)
    else if (_ChorusParams.x > 2.5) ChorusShell(input, surfaceData);      // GoF2: Chorus's shell (Guilt)
    else if (_ChorusParams.x > 0.0) ChorusLayers(input, surfaceData);   // GoF2: Chorus's trim sheets and platings
    if (_WaveParams.x > 0.0) surfaceData.emission += GlowWave(input.uv);   // GoF2: a glow along a pattern (Chorus's M_Phoenix)
    if (_LavaE.w > 0.0) surfaceData.emission += LavaFlow(input.uv);   // GoF2: Chorus's flowing lava (M_BrokenPlanet_Core)
    if (_HitFx.w > 0.0 && _HitFx.x < 1.0) surfaceData.emission += HitFlash(input.uv, input.positionWS, input.normalWS);   // GoF2: a hit (Chorus's M_Phoenix)
#endif
    if (_WreckFx.x > 0.0 || _WreckFx.y > 0.0)   // GoF2: a wreck piece's burn and disintegration (HullWreck.hlsl)
    {
        HullWreckClip(input.positionWS);
        if (_WreckBurn.z > 0.0)   // (Chorus's own burn, from its map)
        {
            half b = HullWreckBurn(input.uv, input.chorusUV.xy, input.color.r);
            surfaceData.emission += HullWreckBurnGlow(b) + HullWreckEmission(input.positionWS, false);
            surfaceData.smoothness *= 1.0h - b;
        }
        else surfaceData.emission += HullWreckEmission(input.positionWS);
    }

    LODFadeCrossFade(input.positionCS);

    InputData inputData;
    InitializeInputData(input, surfaceData.normalTS, inputData);
#if defined(_GOF2_PBR)   // (the mods' variant alone, as above)
    if (_ChorusParams.x > 0.0 && _ChorusParams.x < 3.5) surfaceData.emission += ChorusFarEmission(inputData);   // GoF2: Chorus's far kicker
    if (_Es2Wear2.z > 0.0)
    {
        // EVERSPACE 2's rim: the colour x (1 + 5 (1 - N.V)^3), saturated
        half f = max(1.0 - saturate(dot(inputData.normalWS, inputData.viewDirectionWS)), 1e-4);
        surfaceData.albedo = saturate(surfaceData.albedo * (1.0 + (half)_Es2Grunge.z * f * f * f));
    }
#endif
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
