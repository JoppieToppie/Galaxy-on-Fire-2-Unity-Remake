// GoF2/SpaceSky: the flight-level skybox. Level::renderBG draws the stars sphere (opaque) and adds the nebula sphere
// on top, both rotated by the per-station R_sky. Here both layers are baked cubemaps (GoF2 > Bake Space Skies),
// added together and sampled through _SkyRotation (world direction -> baked cube direction), which the built-in
// Skybox/Cubemap can't do (Y rotation only). Set as RenderSettings.skybox, so it also drives ambient + reflections.
// Hand-written because Shader Graph has no skybox target.
// Remake mods (systems.json "skybox", OrbitBuilder.SetupSky): either layer can instead be a mod's image, sampled as it is
// (no baking): a 2:1 panorama (equirectangular, its middle straight ahead: +Z of the sky) or a 6:1 strip of cube faces in
// the order right (+X), left (-X), up (+Y), down (-Y), front (+Z), back (-Z), each as seen from inside facing it (the up
// face with the front at its bottom edge, the down face with the front at its top edge). Mip level 0 (no seam at the
// panorama's wrap, no bleeding between the strip's faces: the face coordinates stay half a texel inside).
// SRP Batcher compatible (2026-10-22): every per-material value in the UnityPerMaterial buffer.
// Remake: the warp (Visuals.WarpSky, globals, so every system's and mod's sky has it): the sky itself is stretched round the
// flight, as EVERSPACE 2's travel mode bends it. The sky is read in polar coordinates about _WarpAxis with the angle squeezed,
// so a little of it runs out in long radial streaks, in two phases half a cycle apart that crossfade so the flow never ends;
// the noise breaks it into bands, the sparks add the thin white lines, the centre stays calm. At _WarpLevel 0 it is exactly
// the plain sky, so the warp starts and ends without a cut, and everything else draws over it as ever. It takes after the
// Khador Drive's animation between the systems only (_WarpKhador): that fx's blue vein web runs round the centre, and the sky
// leans to its ice blue. A cruise in an orbit is the plain stretch.
// _SkyBlend: another system's sky over this one (World.InterstellarField, approaching it), its layers set as globals with
// runtime modes in place of the keywords, so an arrival finds the sky it was already flying in.
Shader "GoF2/SpaceSky"
{
    Properties
    {
        [NoScaleOffset] _Stars ("Stars", Cube) = "black" {}
        [NoScaleOffset] _Nebula ("Nebula (added)", Cube) = "black" {}
        [NoScaleOffset] _StarsMap ("Stars (mod image)", 2D) = "black" {}
        [NoScaleOffset] _NebulaMap ("Nebula (mod image)", 2D) = "black" {}
        _StarsGain ("Stars brightness", Float) = 1
        _NebulaGain ("Nebula brightness", Float) = 1
        _Exposure ("Exposure", Float) = 1
    }
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" }
        Cull Off ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_local _ _NEBULA_PANORAMA _NEBULA_STRIP
            #pragma multi_compile_local _ _STARS_PANORAMA _STARS_STRIP
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURECUBE(_Stars);  SAMPLER(sampler_Stars);
            TEXTURECUBE(_Nebula); SAMPLER(sampler_Nebula);
            TEXTURE2D(_StarsMap);  SAMPLER(sampler_StarsMap);
            TEXTURE2D(_NebulaMap); SAMPLER(sampler_NebulaMap);
            TEXTURE2D(_WarpNoise);  SAMPLER(sampler_WarpNoise);
            TEXTURE2D(_WarpDetail); SAMPLER(sampler_WarpDetail);
            TEXTURE2D(_WarpSparks); SAMPLER(sampler_WarpSparks);
            TEXTURE2D(_WarpArcs);   SAMPLER(sampler_WarpArcs);
            TEXTURECUBE(_BlendStars);  SAMPLER(sampler_BlendStars);
            TEXTURECUBE(_BlendNebula); SAMPLER(sampler_BlendNebula);
            TEXTURE2D(_BlendStarsMap);  SAMPLER(sampler_BlendStarsMap);
            TEXTURE2D(_BlendNebulaMap); SAMPLER(sampler_BlendNebulaMap);
            float _WarpLevel, _WarpTime, _WarpKhador;
            float3 _WarpAxis;
            half3 _WarpTint;
            float _SkyBlend, _BlendStarsGain, _BlendNebulaGain, _BlendExposure, _BlendStarsMode, _BlendNebulaMode;
            float4x4 _BlendRotation;
            float4 _BlendStarsTexel, _BlendNebulaTexel;

            CBUFFER_START(UnityPerMaterial)
                float4x4 _SkyRotation;   // world direction -> baked cube direction, set by OrbitBuilder.SetupSky
                float4 _StarsMap_TexelSize;
                float4 _NebulaMap_TexelSize;
                half _Exposure, _StarsGain, _NebulaGain;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 world : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings vert(Attributes i)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.world = i.positionOS.xyz;
                return o;
            }

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

            // The sky toward the world direction 'world'.
            half3 Sky(float3 world)
            {
                float3 d = mul((float3x3)_SkyRotation, world);
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
                if (_SkyBlend > 0.001)
                {
                    float3 e = mul((float3x3)_BlendRotation, world);
                    half3 other = Layer(TEXTURECUBE_ARGS(_BlendStars, sampler_BlendStars), TEXTURE2D_ARGS(_BlendStarsMap, sampler_BlendStarsMap), _BlendStarsTexel, e, (int)_BlendStarsMode) * _BlendStarsGain
                                + Layer(TEXTURECUBE_ARGS(_BlendNebula, sampler_BlendNebula), TEXTURE2D_ARGS(_BlendNebulaMap, sampler_BlendNebulaMap), _BlendNebulaTexel, e, (int)_BlendNebulaMode) * _BlendNebulaGain;
                    c = lerp(c, other * _BlendExposure / max(_Exposure, 0.001), _SkyBlend);
                }
                return c;
            }

            // The vein web of the Khador Drive's animation, from its own sheet (khador_jump.png), mirrored round the ring,
            // turning and running out.
            half3 Web(float around, float ring)
            {
                float u = abs(frac(around * 3.0 + _WarpTime * 0.06) * 2.0 - 1.0);
                float v = abs(frac(ring * 2.2 - _WarpTime * 0.3) * 2.0 - 1.0);
                half3 web = SAMPLE_TEXTURE2D_LOD(_WarpArcs, sampler_WarpArcs, float2(0.47 + u * 0.19, 0.62 + v * 0.18), 0).rgb;
                return web * smoothstep(0.03, 0.14, ring) * (1.0 - smoothstep(0.32, 0.62, ring));
            }

            half3 Warp(float3 view)
            {
                float3 axis = normalize(_WarpAxis);
                float3 x = normalize(cross(axis, abs(axis.y) < 0.9 ? float3(0, 1, 0) : float3(1, 0, 0)));
                float3 y = cross(axis, x);
                float ring = acos(clamp(dot(view, axis), -1.0, 1.0)) / PI;   // 0 ahead, 1 behind
                float turn = atan2(dot(view, y), dot(view, x));
                float around = turn / (2.0 * PI) + 0.5;
                float3 out_ = x * cos(turn) + y * sin(turn);
                float amount = smoothstep(0.02, 0.4, ring) * _WarpLevel;     // the calm centre
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
                sky *= lerp(1.0, 0.35 + 2.4 * smoothstep(0.08, 0.5, bands), amount);
                sky *= 1.0 - _WarpLevel * 0.4 * (1.0 - smoothstep(0.0, 0.2, ring));
                sky = lerp(sky, dot(sky, half3(0.3, 0.6, 0.1)) * half3(0.62, 0.8, 1.25), amount * 0.4 * _WarpKhador);   // the Khador fx's ice blue
                half lines = SAMPLE_TEXTURE2D_LOD(_WarpSparks, sampler_WarpSparks, float2(around * 3.0, ring * 0.7 - _WarpTime * 1.4), 0).r;
                return sky + _WarpTint * lines * lines * lines * amount * 0.8
                     + Web(around, ring) * amount * 1.4 * _WarpKhador;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                half3 c = _WarpLevel > 0.001 ? Warp(normalize(i.world)) : Sky(i.world);
                return half4(c * _Exposure, 1);
            }
            ENDHLSL
        }
    }
}
