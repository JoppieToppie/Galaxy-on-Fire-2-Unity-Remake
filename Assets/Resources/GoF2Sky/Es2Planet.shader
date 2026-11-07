// GoF2/Es2Planet: an orbit's planet as a lit sphere (World.Backdrop, stations.json "planetSphere"), after EVERSPACE 2's planet
// materials, read from their compiled base-pass pixel shaders (tools/shaders' ShaderDump in the es2 importer). Drawn in the
// backdrop pass like GoF2/Backdrop's quads: behind every opaque object, its depth on the far plane (a sphere: its back faces
// culled, so it needs no depth of its own). Lit here by the orbit's sun (_SunDir, _SunColor): ES2 lights it with the level's
// sun; the night side keeps a little ambient. In Resources so a build keeps it.
//
// _Look 0, M_Planet_Desert (its space form: the MPC_Environment's PlanetSurface 0):
//   UVs: u round the planet (x2), v a latitude stretch (0.5 (2v)^0.2 below the equator, 0.5 + 0.5 (1 - (2 - 2v)^0.2) above),
//   x5: the base layer; the detail layer x6 of that; the pole layer a projection from above (x5).
//   Surface: T_Planet_Rock_002_H_C_R (H height, C colour, R roughness) and _N, detail and base mixed by distance (here
//   _DetailMix); the normal flattened 70 % toward the surface.
//   Colour: overlay(C^2.2, tone x (1, 0.65, 0.4)), tone = 0.5 + 0.5 contrast20(T_Noise_016_M at (base + 0.25) x 2); x the rim
//   (1 + 3 (1 - N.V)^3), saturated.
//   Poles: where the pole projection's distance from the pole is under half T_Noise_016_M^1.5 (x 2), x10, saturated: the
//   colour (1, 1 - 0.7 (1 - s), 0.8 s), s = (T_Rocks_004_C^2 - 0.5) 0.5 + 0.5; its normal T_Rocks_004_N flattened 95 %; its
//   roughness 0.4 + 0.4 smoothstep((T_Rocks_004_C - 0.2) x 20). Metalness 0, specular 0.5.
// Left out: the parallax-occlusion relief of the detail layer (its height steps), the city lights (off on the desert), the
// distance fades of ES2's own planet scaling.
//
// _Look 1, M_Planet_GasSmooth (2026-11-06, es2_re planetmat): the desert's UVs; the base layer T_Planet_Gas_002_C_H (C, H)
//   overlaid with its detail layer (x DetailTiling 2) pulled toward 0.5 by distance (_DetailMix); colour lerp(Color, Color2,
//   H) (_Color1 / _Color2, linear), overlaid with C, its hue turned about the grey axis by Variation x 2 pi (_Variation), then
//   overlaid with (H - 0.5) x HeightOverlay 0.5 + 0.5; the poles as the desert's, overlaid with T_Rocks_004_C. Normals
//   T_Planet_Gas_002_N, base and detail, flattened FlattenNormal 0.9 toward the surface. Roughness 1, metalness 0.
// _Look 2, M_Planet_Gas: the base layer T_Planet_Gas_001_C_H's C, lerped with its detail (x DetailTiling 4) by distance,
//   squared; the bands contrast4(sqrt(T_Noise_001_M at (u, 4 v))) between SecondaryColor and BaseColor (_Color1 / _Color2),
//   its hue turned by Variation, overlaid with that C^2. Normals T_Planet_Gas_001_N, the detail flattened Param_1 0.6. No
//   poles. Roughness 1, metalness 0.
// _Look 3, M_Planet_Cratered: grey. The base UVs x 2 (4 u, 10 v'); a mask contrast8(T_Noise_016_M at ((2 u, 5 v') + 0.25)
//   x 2) between the craters (T_Planet_Craters_001_H_C_R: C^2.2 overlaid with 0.2) and the rock (T_Planet_Rock_002_H_C_R:
//   C^2.2 overlaid with 0.05), each x the rim (1 + 3 (1 - N.V)^3) saturated; their R the roughness, their normal maps
//   (_N, _SurfaceNormal2) mixed by the same mask; the poles the desert's, their grey 0.4 ((T_Rocks_004_C^2 - 0.5) 0.5 + 0.5).
// _Look 4, M_Planet_Volcanic: its lava from its compiled emissive pass (the dump has no colour pass for it): the base UVs x
//   Tiling 4; LavaColor (_Color2) turned by Variation, x ((1 - N.V)^LavaFresnelExponent 5 x LavaFresnelIntensity 0.3 + the
//   lava x _LavaGlow), the lava = (T_Planet_Volcanic_001_M's G + the rivers: 1 - smoothstep(10 (T_Quixel_Rock_024_C_R_H's R
//   - (LavaRiverThreshold 0.42 x 1.2 - 0.15)))) x contrast7((1 - that R at x LavaMaskTiling 4)^LavaMaskExponent 3) x
//   T_Noise_008_M (x 0.5); emitted, so it glows on the night side too. The rock approximated from the master's colours:
//   BaseColor (_Color1) overlaid with contrast3(the map's B), roughness RoughnessMin 1 to RoughnessMax 0.5 by it, the poles
//   PoleColor. Normals T_Planet_Volcanic_001_N flattened NormalFlatten 0.8.
// Clouds (looks 5 and 6): T_Planet_Clouds_001_C_AO at the base UVs x 8 drifting with time x CloudsSpeed 0.002, its x 1/4 layer
//   by distance (_DetailMix); coverage C^(1 / CloudsDensity) (the preshader divides), their colour (C x AO)^Param 0.5, their
//   normals T_Planet_Clouds_001_N flattened CloudsNormalFlatten 0.8; over the surface by the coverage, roughness 1.
// _Look 5, M_Planet_Ice: T_Planet_Rock_002_H_C_R overlaid with its detail (x DetailTiling 4) toward 0.5 by distance; colour
//   BaseColor (_Color1) x the curvature (saturate(C x CurvatureMultiplier 1.3) through the contrast curve CurvatureContrast 4) x
//   the darkening (1 + (smoothstep(DarkenThreshold -+ DarkenWidth, H) - 1) Darken 0.05); the poles overlay(BaseColor, the
//   desert's tone); roughness RoughnessMin 0.2 to RoughnessMax 0.6 by 1 - R; normals T_Planet_Rock_002_N flattened 0.8;
//   clouds CloudsDensity 2.
// _Look 6, M_Planet_Water: water Color (_Color1) x T_Noise_009_M; ice floes where T_Noise_018_M (x IceTiling 2) overlaid with
//   its detail (x IceDetailTiling 16, sqrt, (x - 0.5) DetailOverlay 1 + 0.5) passes smoothstep(IceThreshold 0.6 -+
//   IceThresholdWidth 0.2), IceColor (_Color2) x that mask, their normals T_Noise_018_N (base and detail, flattened 0.95);
//   roughness Roughness 0.2, 0.7 on the ice; clouds CloudsDensity 0.3. No poles.
// _Look 7, M_Planet_Earthlike: at the base UVs x 2, a height h = T_Noise_016_M; the land two layers by contrast16(h^MaskBias
//   1.1): the mountains (T_Planet_Mountains_001_H_C_R: C^2.2 overlaid with (0.2, 0.19, 0.16)) and the rock
//   (T_Planet_Rock_001_H_C_R: C^2.2), each x the rim; their normals (_N, flattened 0.8 / 0.7) and roughness the same way.
//   The water where h is under the level WaterLevel 0.4586 + (T_Noise_015_M at x ShoreFuzzinessTiling 16, x 2 - 1) x
//   ShoreFuzziness 0.02 (1 - smoothstep(1000 (h - (L x 1.002 - 0.0015)))): (0.2, 0.4, 0.5) x (0.2 + 0.8 (1 - N.V)^2 + half
//   the shore band smoothstep(66.7 (h - (L x 1.03 - 0.0225)))), flat, roughness 0.4; clouds CloudsDensity 0.7; the poles icy
//   (0.656, 0.838, 1) x the desert's tone. Its rivers (RiverTiling / RiverDepth) left out.
Shader "GoF2/Es2Planet"
{
    Properties
    {
        _Look ("Look (0 desert, 1 gas smooth, 2 gas, 3 cratered, 4 volcanic, 5 ice, 6 water, 7 earthlike)", Float) = 0
        _Noise2 ("Volcanic: the lava noise; water: the water's noise", 2D) = "gray" {}
        _Clouds ("Clouds C_AO", 2D) = "black" {}
        _CloudsNormal ("Clouds normal (ES2's, DirectX)", 2D) = "bump" {}
        _LavaGlow ("Volcanic: the lava's brightness (ES2's LavaIntensity in its exposure)", Float) = 3
        _Color1 ("Gas: Color / SecondaryColor (linear)", Vector) = (0.02, 0.071433, 0.1, 0)
        _Color2 ("Gas: Color2 / BaseColor (linear)", Vector) = (0.166665, 0.487763, 0.5, 0)
        _Variation ("Gas: Variation (0..1, the hue x 2 pi)", Float) = 0
        _SurfaceMap ("Surface H_C_R", 2D) = "gray" {}
        _SurfaceNormal ("Surface normal (ES2's, DirectX)", 2D) = "bump" {}
        _SurfaceMap2 ("Cratered: the rock's H_C_R", 2D) = "gray" {}
        _SurfaceNormal2 ("Cratered: the rock's normal", 2D) = "bump" {}
        _CapNormal ("Pole normal (ES2's, DirectX)", 2D) = "bump" {}
        _CapColour ("Pole colour mask", 2D) = "gray" {}
        _Noise ("Noise mask", 2D) = "gray" {}
        _DetailMix ("Base layer share (1 = base only)", Range(0, 1)) = 0.6
        _SunDir ("Toward the sun (world)", Vector) = (0, 0, 1, 0)
        _SunColor ("Sun light", Color) = (1, 1, 1, 1)
        _Ambient ("Ambient", Float) = 0.03
        _Atmosphere ("Atmosphere rim colour (a: strength)", Color) = (0, 0, 0, 0)
        _Color ("Fade (a)", Color) = (1, 1, 1, 1)
        _Debug ("Debug: 1 albedo, 2 surface map, 3 sun colour", Float) = 0
    }
    SubShader
    {
        Tags { "Queue" = "Transparent-100" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        ZTest LEqual
        Cull Back

        Pass
        {
            Tags { "LightMode" = "GoF2Backdrop" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_SurfaceMap); SAMPLER(sampler_SurfaceMap);
            TEXTURE2D(_SurfaceNormal);
            TEXTURE2D(_SurfaceMap2);
            TEXTURE2D(_SurfaceNormal2);
            TEXTURE2D(_Noise2);
            TEXTURE2D(_Clouds);
            TEXTURE2D(_CloudsNormal);
            TEXTURE2D(_CapNormal);
            TEXTURE2D(_CapColour);
            TEXTURE2D(_Noise);
            CBUFFER_START(UnityPerMaterial)
                float _Look;
                float4 _Color1, _Color2;
                float _Variation;
                half _LavaGlow;
                half _DetailMix;
                float4 _SunDir;
                half4 _SunColor;
                half _Ambient;
                half4 _Atmosphere;
                half4 _Color;
                float _Debug;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dirOS : TEXCOORD0; float3 positionWS : TEXCOORD1; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                #if UNITY_REVERSED_Z
                    o.positionCS.z = o.positionCS.w * 1e-6;
                #else
                    o.positionCS.z = o.positionCS.w * (1 - 1e-6);
                #endif
                o.dirOS = i.positionOS.xyz;
                return o;
            }

            // ES2's contrast curve (its compiled shader's pow pair): 0.5 (2x)^p below the middle, 1 - 0.5 (2 - 2x)^p above
            half Contrast(half x, half p) { return x < 0.5 ? 0.5 * pow(saturate(2.0 * x), p) : 1.0 - 0.5 * pow(saturate(2.0 - 2.0 * x), p); }
            half Overlay(half b, half t) { return b >= 0.5 ? 1.0 - (1.0 - b) * 2.0 * (1.0 - t) : 2.0 * b * t; }
            // the hue turned 'angle' radians about the grey axis (ES2's HueShift: Rodrigues about (1, 1, 1) / sqrt 3)
            half3 HueRotate(half3 c, half angle)
            {
                const half3 k = half3(0.57735026, 0.57735026, 0.57735026);
                half dk = dot(k, c);
                half3 p = c - k * dk;
                half s, co;
                sincos(angle, s, co);
                return p * co + cross(k, p) * s + k * dk;
            }
            half3 Overlay3(half3 b, half3 t) { return half3(Overlay(b.r, t.r), Overlay(b.g, t.g), Overlay(b.b, t.b)); }
            half3 Unpack(half4 c, half flatten)
            {
                half2 xy = c.xy * 2.0 - 1.0;
                half3 n = half3(xy, sqrt(saturate(1.0 - dot(xy, xy))));
                return normalize(lerp(n, half3(0, 0, 1), flatten));
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 d = normalize(i.dirOS);
                // UVs (v 0 at the north pole)
                float u = atan2(d.x, d.z) / (2.0 * PI) + 0.5;
                // (the mip level from a u that doesn't wrap here: the wrap at u 0 / 1 would pick the smallest mip, a seam)
                float uAlt = frac(u + 0.5) - 0.5;
                float2 dU = float2(ddx(u), ddy(u)), dUAlt = float2(ddx(uAlt), ddy(uAlt));
                if (dot(dUAlt, dUAlt) < dot(dU, dU)) dU = dUAlt;
                float v = acos(clamp(d.y, -1.0, 1.0)) / PI;
                float g = v < 0.5 ? 0.5 * pow(saturate(2.0 * v), 0.2) : 0.5 + 0.5 * (1.0 - pow(saturate(2.0 - 2.0 * v), 0.2));
                float2 baseUv = float2(2.0 * u, 5.0 * g), detailUv = baseUv * 6.0;
                float2 gx = float2(2.0 * dU.x, 5.0 * ddx(g)), gy = float2(2.0 * dU.y, 5.0 * ddy(g));
                float2 poleUv = d.xz * 0.5 + 0.5;
                // the sphere's frame: east, north, out (object space)
                float3 east = normalize(cross(float3(0, 1, 0), d) + float3(1e-5, 0, 0));
                float3 north = cross(d, east);

                // the poles (every look but the banded gas)
                half pn = SAMPLE_TEXTURE2D(_Noise, sampler_SurfaceMap, poleUv * 2.0).r;
                half reach = max(0.5 * pow(max(pn, 1e-4), 1.5), 1e-4);
                half cap = abs(_Look - 2.0) < 0.5 || abs(_Look - 6.0) < 0.5 ? 0.0 : saturate(10.0 * max(1.0 - length(poleUv - 0.5) / reach, 0.0));
                half3 ncap = Unpack(SAMPLE_TEXTURE2D(_CapNormal, sampler_SurfaceMap, poleUv * 5.0), 0.95);
                half cc = SAMPLE_TEXTURE2D(_CapColour, sampler_SurfaceMap, poleUv * 5.0).r;
                half s = cc <= 2.98e-8 ? 0.25 : (cc * cc - 0.5) * 0.5 + 0.5;
                float3 view = normalize(GetCameraPositionWS() - i.positionWS);
                half3 albedo, nts, glow = 0.0;
                half rough, f;
                if (_Look < 0.5)
                {
                    half4 sd = SAMPLE_TEXTURE2D_GRAD(_SurfaceMap, sampler_SurfaceMap, detailUv, gx * 6.0, gy * 6.0);
                    half4 sb = SAMPLE_TEXTURE2D_GRAD(_SurfaceMap, sampler_SurfaceMap, baseUv, gx, gy);
                    half2 cr = lerp(sd.yz, sb.yz, _DetailMix);   // (colour, roughness)
                    half3 nd = Unpack(SAMPLE_TEXTURE2D_GRAD(_SurfaceNormal, sampler_SurfaceMap, detailUv, gx * 6.0, gy * 6.0), 0.0);
                    half3 nb = Unpack(SAMPLE_TEXTURE2D_GRAD(_SurfaceNormal, sampler_SurfaceMap, baseUv, gx, gy), 0.0);
                    nts = normalize(lerp(normalize(lerp(lerp(nd, nb, _DetailMix), half3(0, 0, 1), 0.7)), ncap, cap));
                    float3 n0 = normalize(TransformObjectToWorldDir(normalize(east * nts.x + north * nts.y + d * nts.z)));
                    half c = pow(max(cr.x, 1e-4), 2.2);
                    half tone = 0.5 + 0.5 * Contrast(SAMPLE_TEXTURE2D_GRAD(_Noise, sampler_SurfaceMap, (baseUv + 0.25) * 2.0, gx * 2.0, gy * 2.0).r, 20.0);
                    albedo = Overlay3(half3(c, c, c), tone * half3(1.0, 0.65, 0.4));
                    f = max(1.0 - saturate(dot(n0, view)), 1e-4);
                    albedo = saturate(albedo * (1.0 + 3.0 * f * f * f));
                    albedo = saturate(lerp(albedo, half3(1.0, 1.0 - 0.7 * (1.0 - s), 0.8 * s), cap));
                    half capRough = 0.4 + 0.4 * smoothstep(0.0, 1.0, saturate((cc - 0.2) * 20.0));
                    rough = saturate(lerp(cr.y, capRough, cap));
                }
                else if (_Look < 1.5)
                {
                    // the smooth gas giant (DetailTiling 2)
                    float2 dUv = baseUv * 2.0;
                    half2 b = SAMPLE_TEXTURE2D_GRAD(_SurfaceMap, sampler_SurfaceMap, baseUv, gx, gy).xy;
                    half2 dt = SAMPLE_TEXTURE2D_GRAD(_SurfaceMap, sampler_SurfaceMap, dUv, gx * 2.0, gy * 2.0).xy;
                    dt = dt + _DetailMix * (0.5 - dt);
                    half2 ch = half2(Overlay(b.x, dt.x), Overlay(b.y, dt.y));   // (C, H)
                    half3 col = lerp(_Color1.rgb, _Color2.rgb, ch.y);
                    col = Overlay3(col, half3(ch.x, ch.x, ch.x));
                    col = HueRotate(col, _Variation * 2.0 * PI);
                    half ho = (ch.y - 0.5) * 0.5 + 0.5;
                    half3 body = Overlay3(col, half3(ho, ho, ho));
                    half3 capCol = Overlay3(col, half3(s, s, s));
                    albedo = saturate(lerp(body, capCol, cap));
                    half3 nb = Unpack(SAMPLE_TEXTURE2D_GRAD(_SurfaceNormal, sampler_SurfaceMap, baseUv, gx, gy), 0.0);
                    half3 nd = Unpack(SAMPLE_TEXTURE2D_GRAD(_SurfaceNormal, sampler_SurfaceMap, dUv, gx * 2.0, gy * 2.0), _DetailMix);
                    half3 t1 = nb + half3(0, 0, 1), u1 = nd * half3(-1, -1, 1);
                    half3 rn = normalize(t1 * dot(t1, u1) / t1.z - u1);   // (the detail over the base: ES2's RNM blend)
                    nts = normalize(lerp(normalize(lerp(rn, half3(0, 0, 1), 0.9)), ncap, cap));
                    rough = 1.0;
                }
                else if (_Look > 4.5)
                {
                    float2 cUv = baseUv * 8.0 + float2(_Time.y * 0.002, 0.0), cgx = gx * 8.0, cgy = gy * 8.0;
                    half2 cf = SAMPLE_TEXTURE2D_GRAD(_Clouds, sampler_SurfaceMap, cUv, cgx, cgy).xy;
                    half2 cc4 = SAMPLE_TEXTURE2D_GRAD(_Clouds, sampler_SurfaceMap, cUv * 0.25, cgx * 0.25, cgy * 0.25).xy;
                    half2 cl = lerp(cf, cc4, _DetailMix);   // (C, AO)
                    half3 ncl = normalize(lerp(Unpack(SAMPLE_TEXTURE2D_GRAD(_CloudsNormal, sampler_SurfaceMap, cUv, cgx, cgy), 0.8),
                                               Unpack(SAMPLE_TEXTURE2D_GRAD(_CloudsNormal, sampler_SurfaceMap, cUv * 0.25, cgx * 0.25, cgy * 0.25), 0.8), _DetailMix));
                    half cover, cloudGrey = pow(max(cl.x * cl.y, 1e-4), 0.5);
                    half3 sn;
                    if (_Look < 5.5)
                    {
                        // the ice planet
                        cover = pow(max(cl.x, 1e-4), 1.0 / 2.0);
                        float2 dUv = baseUv * 4.0;
                        half3 b = SAMPLE_TEXTURE2D_GRAD(_SurfaceMap, sampler_SurfaceMap, baseUv, gx, gy).rgb;   // (H, C, R)
                        half3 dt = SAMPLE_TEXTURE2D_GRAD(_SurfaceMap, sampler_SurfaceMap, dUv, gx * 4.0, gy * 4.0).rgb;
                        dt = dt + _DetailMix * (0.5 - dt);
                        half3 hcr = Overlay3(b, dt);
                        half dark = 1.0 + (smoothstep(0.3, 0.7, hcr.x) - 1.0) * 0.05;
                        half curv = Contrast(saturate(hcr.y * 1.3), 4.0);
                        half3 body = _Color1.rgb * dark * curv;
                        half3 capCol = Overlay3(_Color1.rgb, half3(s, s, s));
                        albedo = saturate(lerp(body, capCol, cap));
                        rough = lerp(0.2 + (0.6 - 0.2) * (1.0 - hcr.z), 0.4 + 0.4 * smoothstep(0.0, 1.0, saturate((cc - 0.2) * 20.0)), cap);
                        half3 nb = Unpack(SAMPLE_TEXTURE2D_GRAD(_SurfaceNormal, sampler_SurfaceMap, baseUv, gx, gy), 0.0);
                        half3 nd = Unpack(SAMPLE_TEXTURE2D_GRAD(_SurfaceNormal, sampler_SurfaceMap, dUv, gx * 4.0, gy * 4.0), _DetailMix);
                        half3 t1 = nb + half3(0, 0, 1), u1 = nd * half3(-1, -1, 1);
                        sn = normalize(lerp(normalize(lerp(normalize(t1 * dot(t1, u1) / t1.z - u1), half3(0, 0, 1), 0.8)), ncap, cap));
                    }
                    else if (_Look > 6.5)
                    {
                        // the earthlike planet: land, seas, clouds
                        cover = pow(max(cl.x, 1e-4), 1.0 / 0.7);
                        float2 lUv = baseUv * 2.0, lgx = gx * 2.0, lgy = gy * 2.0;
                        half h = SAMPLE_TEXTURE2D_GRAD(_Noise, sampler_SurfaceMap, lUv, lgx, lgy).r;
                        half m = Contrast(pow(max(h, 1e-4), 1.1), 16.0);
                        half3 a = SAMPLE_TEXTURE2D_GRAD(_SurfaceMap, sampler_SurfaceMap, lUv, lgx, lgy).rgb;    // (H, C, R)
                        half3 b = SAMPLE_TEXTURE2D_GRAD(_SurfaceMap2, sampler_SurfaceMap, lUv, lgx, lgy).rgb;
                        half3 na = Unpack(SAMPLE_TEXTURE2D_GRAD(_SurfaceNormal, sampler_SurfaceMap, lUv, lgx, lgy), 0.8);
                        half3 nr = Unpack(SAMPLE_TEXTURE2D_GRAD(_SurfaceNormal2, sampler_SurfaceMap, lUv, lgx, lgy), 0.7);
                        half sh = SAMPLE_TEXTURE2D_GRAD(_Noise2, sampler_SurfaceMap, baseUv * 16.0, gx * 16.0, gy * 16.0).r;
                        half level = (sh * 2.0 - 1.0) * 0.02 + 0.458572;
                        half water = 1.0 - smoothstep(0.0, 1.0, saturate((h - (level * 1.002 - 0.0015)) * 1000.0));
                        half shore = smoothstep(0.0, 1.0, saturate((h - (level * 1.03 - 0.0225)) * 66.6667));
                        float3 n0 = normalize(TransformObjectToWorldDir(d));
                        half fv = max(1.0 - saturate(dot(n0, view)), 1e-4);
                        half rim = 1.0 + 3.0 * fv * fv * fv;
                        half ca = pow(max(a.y, 1e-4), 2.2), cb = pow(max(b.y, 1e-4), 2.2);
                        half3 landA = saturate(Overlay3(half3(ca, ca, ca), half3(0.2, 0.190813, 0.16)) * rim);
                        half3 landB = saturate(half3(cb, cb, cb) * rim);
                        half3 land = lerp(landA, landB, m);
                        half3 sea = (shore * 0.5 + 0.2 + 0.8 * fv * fv) * half3(0.2, 0.4, 0.5);
                        half3 capCol = half3(0.65625 * s, 0.838338 * s, s);
                        albedo = saturate(lerp(lerp(land, sea, water), capCol, cap));
                        rough = lerp(lerp(lerp(a.z, b.z, m), 0.4, water), 0.4 + 0.4 * smoothstep(0.0, 1.0, saturate((cc - 0.2) * 20.0)), cap);
                        sn = normalize(lerp(normalize(lerp(normalize(lerp(na, nr, m)), half3(0, 0, 1), water)), ncap, cap));
                    }
                    else
                    {
                        // the water planet
                        cover = pow(max(cl.x, 1e-4), 1.0 / 0.3);
                        float2 iUv = baseUv * 2.0, idUv = baseUv * 16.0;
                        half im = SAMPLE_TEXTURE2D_GRAD(_SurfaceMap, sampler_SurfaceMap, iUv, gx * 2.0, gy * 2.0).r;
                        half imd = sqrt(max(SAMPLE_TEXTURE2D_GRAD(_SurfaceMap, sampler_SurfaceMap, idUv, gx * 16.0, gy * 16.0).r, 0.0));
                        half iceV = Overlay(im, (imd - 0.5) * 1.0 + 0.5);
                        half ice = smoothstep(0.6 - 0.2, 0.6 + 0.2, iceV);
                        half wn = SAMPLE_TEXTURE2D_GRAD(_Noise2, sampler_SurfaceMap, baseUv, gx, gy).r;
                        albedo = saturate(lerp(_Color1.rgb * wn, _Color2.rgb * iceV, ice));
                        rough = lerp(0.2, 0.7, ice);
                        half3 nb = Unpack(SAMPLE_TEXTURE2D_GRAD(_SurfaceNormal, sampler_SurfaceMap, iUv, gx * 2.0, gy * 2.0), 0.0);
                        half3 nd = Unpack(SAMPLE_TEXTURE2D_GRAD(_SurfaceNormal, sampler_SurfaceMap, idUv, gx * 16.0, gy * 16.0), 0.0);
                        half3 t1 = nb + half3(0, 0, 1), u1 = nd * half3(-1, -1, 1);
                        half3 iceN = normalize(lerp(normalize(t1 * dot(t1, u1) / t1.z - u1), half3(0, 0, 1), 0.95));
                        sn = normalize(lerp(half3(0, 0, 1), iceN, ice));
                    }
                    // the clouds over it
                    albedo = saturate(lerp(albedo, half3(cloudGrey, cloudGrey, cloudGrey), cover));
                    rough = lerp(rough, 1.0, cover);
                    nts = normalize(lerp(sn, ncl, cover));
                }
                else if (_Look > 3.5)
                {
                    // the volcanic planet: dark rock, lava rivers glowing
                    float2 vUv = baseUv * 4.0, vgx = gx * 4.0, vgy = gy * 4.0;
                    half4 vm = SAMPLE_TEXTURE2D_GRAD(_SurfaceMap, sampler_SurfaceMap, vUv, vgx, vgy);
                    half qr = SAMPLE_TEXTURE2D_GRAD(_SurfaceMap2, sampler_SurfaceMap, vUv, vgx, vgy).r;
                    half qm = SAMPLE_TEXTURE2D_GRAD(_SurfaceMap2, sampler_SurfaceMap, vUv * 4.0, vgx * 4.0, vgy * 4.0).r;
                    half nz = SAMPLE_TEXTURE2D_GRAD(_Noise2, sampler_SurfaceMap, vUv * 0.5, vgx * 0.5, vgy * 0.5).r;
                    half river = 1.0 - smoothstep(0.0, 1.0, saturate((qr - (0.42 * 1.2 - 0.15)) * 10.0));
                    half mask = Contrast(pow(max(1.0 - qm, 1e-4), 3.0), 7.0);
                    half lava = (vm.g + river) * mask * nz;
                    half3 nv = Unpack(SAMPLE_TEXTURE2D_GRAD(_SurfaceNormal, sampler_SurfaceMap, vUv, vgx, vgy), 0.8);
                    nts = normalize(lerp(nv, ncap, cap));
                    float3 n0 = normalize(TransformObjectToWorldDir(normalize(east * nts.x + north * nts.y + d * nts.z)));
                    f = max(1.0 - saturate(dot(n0, view)), 1e-4);
                    half3 lavaCol = HueRotate(_Color2.rgb, _Variation * 2.0 * PI);
                    glow = lavaCol * (pow(f, 5.0) * 0.3 + lava * _LavaGlow) * (1.0 - cap);
                    half k = Contrast(vm.b, 3.0);
                    albedo = saturate(lerp(Overlay3(_Color1.rgb, half3(k, k, k)), half3(0.114583, 0.108537, 0.097873), cap));
                    rough = lerp(1.0, 0.5, k);
                }
                else if (_Look > 2.5)
                {
                    // the cratered moon: craters or rock by a sharpened noise, grey
                    float2 cUv = baseUv * 2.0, cgx = gx * 2.0, cgy = gy * 2.0;
                    half m = Contrast(SAMPLE_TEXTURE2D_GRAD(_Noise, sampler_SurfaceMap, (baseUv + 0.25) * 2.0, gx * 2.0, gy * 2.0).r, 8.0);
                    half3 na = Unpack(SAMPLE_TEXTURE2D_GRAD(_SurfaceNormal, sampler_SurfaceMap, cUv, cgx, cgy), 0.0);
                    half3 nr = Unpack(SAMPLE_TEXTURE2D_GRAD(_SurfaceNormal2, sampler_SurfaceMap, cUv, cgx, cgy), 0.0);
                    nts = normalize(lerp(normalize(lerp(na, nr, m)), ncap, cap));
                    float3 n0 = normalize(TransformObjectToWorldDir(normalize(east * nts.x + north * nts.y + d * nts.z)));
                    f = max(1.0 - saturate(dot(n0, view)), 1e-4);
                    half rim = 1.0 + 3.0 * f * f * f;
                    half3 a = SAMPLE_TEXTURE2D_GRAD(_SurfaceMap, sampler_SurfaceMap, cUv, cgx, cgy).rgb;    // (H, C, R)
                    half3 b = SAMPLE_TEXTURE2D_GRAD(_SurfaceMap2, sampler_SurfaceMap, cUv, cgx, cgy).rgb;
                    half ga = saturate(Overlay(pow(max(a.y, 1e-4), 2.2), 0.2) * rim);
                    half gb = saturate(Overlay(pow(max(b.y, 1e-4), 2.2), 0.05) * rim);
                    half grey = lerp(ga, gb, m);
                    half capGrey = cc <= 2.98e-8 ? 0.1 : s * 0.4;
                    grey = saturate(lerp(grey, capGrey, cap));
                    albedo = half3(grey, grey, grey);
                    half capRough = 0.4 + 0.4 * smoothstep(0.0, 1.0, saturate((cc - 0.2) * 20.0));
                    rough = saturate(lerp(lerp(a.z, b.z, m), capRough, cap));
                }
                else
                {
                    // the banded gas giant (DetailTiling 4)
                    float2 dUv = baseUv * 4.0;
                    half cb = SAMPLE_TEXTURE2D_GRAD(_SurfaceMap, sampler_SurfaceMap, baseUv, gx, gy).x;
                    half cd = SAMPLE_TEXTURE2D_GRAD(_SurfaceMap, sampler_SurfaceMap, dUv, gx * 4.0, gy * 4.0).x;
                    half c = lerp(cd, cb, _DetailMix);
                    c *= c;
                    half bandN = sqrt(max(SAMPLE_TEXTURE2D_GRAD(_Noise, sampler_SurfaceMap, float2(u, 4.0 * v), float2(dU.x, 4.0 * ddx(v)), float2(dU.y, 4.0 * ddy(v))).r, 0.0));
                    half t = Contrast(bandN, 4.0);
                    half3 col = HueRotate(lerp(_Color1.rgb, _Color2.rgb, t), _Variation * 2.0 * PI);
                    albedo = saturate(Overlay3(col, half3(c, c, c)));
                    half3 nb = Unpack(SAMPLE_TEXTURE2D_GRAD(_SurfaceNormal, sampler_SurfaceMap, baseUv, gx, gy), 0.0);
                    half3 nd = Unpack(SAMPLE_TEXTURE2D_GRAD(_SurfaceNormal, sampler_SurfaceMap, dUv, gx * 4.0, gy * 4.0), 0.6);
                    nts = normalize(lerp(nd, nb, _DetailMix));
                    rough = 1.0;
                }
                float3 nOS = normalize(east * nts.x + north * nts.y + d * nts.z);
                float3 n = normalize(TransformObjectToWorldDir(nOS));
                f = max(1.0 - saturate(dot(n, view)), 1e-4);

                // the sun: diffuse and a GGX highlight (specular 0.5: F0 0.04)
                float3 l = normalize(_SunDir.xyz);
                half nl = saturate(dot(n, l));
                float3 h = normalize(l + view);
                half a = max(rough * rough, 0.002), a2 = a * a;
                half nh = saturate(dot(n, h));
                half dd = nh * nh * (a2 - 1.0) + 1.0;
                half spec = a2 / (PI * dd * dd) * 0.04 * nl * 0.25;
                half3 lit = albedo * (_SunColor.rgb * nl + _Ambient) + _SunColor.rgb * spec;
                if (_Debug > 2.5) return half4(_SunColor.rgb, 1);
                if (_Debug > 1.5) return half4(SAMPLE_TEXTURE2D(_SurfaceMap, sampler_SurfaceMap, baseUv).rgb, 1);
                if (_Debug > 0.5) return half4(albedo, 1);
                return half4(lit + glow, _Color.a);   // (the atmosphere is its own shell: GoF2/Es2Atmosphere)
            }
            ENDHLSL
        }
    }
}
