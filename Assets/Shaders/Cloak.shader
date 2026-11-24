// GoF2/Cloak: the player's cloak (AbyssEngine::BumpShaderCloak, shader type 0xe; combat_equipment.md 1.6). The hull is
// lit like before, then dissolves by cloak_map.png into the screen behind it, refracted by the normal map (the original:
// x + n.x * 100 * sin(rate), y + n.y * 75 * cos(rate) at 1024x768; Modified: only the glow
// moves, see frag), with a light-blue (0.6, 0.8, 1.0) edge.
// _AnimValue is the fade (0..1 over 2 s): the hull dissolves where the map is below it, the band glowing along the front,
// blended over the lit hull up to 0.5; 0.5..0.75 blends into pure refraction (a glass silhouette).
// Drawn by CloakPass after URP's transparents (LightMode GoF2Cloak) from its copy of the frame, _GoF2CloakScene, which
// holds the ship's own lights and glare as the original's refraction FBO does.
Shader "GoF2/Cloak"
{
    Properties
    {
        _BaseMap ("Base", 2D) = "white" {}
        [Normal] _BumpMap ("Normal", 2D) = "bump" {}
        _CloakMap ("Cloak map", 2D) = "white" {}
        _MetallicGlossMap ("Specular (a)", 2D) = "black" {}
        _EmissionMap ("Emission", 2D) = "white" {}
        [HDR] _EmissionColor ("Emission colour", Color) = (0, 0, 0, 0)
        _AnimValue ("Anim value", Range(0, 1)) = 0
        _CloakRate ("Cloak rate", Float) = 0
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        ZWrite On
        Cull Back

        // Both passes share the textures and one UnityPerMaterial layout (the SRP Batcher needs it the same in every pass).
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
        TEXTURE2D(_CloakMap); SAMPLER(sampler_CloakMap);
        TEXTURE2D(_MetallicGlossMap); SAMPLER(sampler_MetallicGlossMap);
        TEXTURE2D(_EmissionMap); SAMPLER(sampler_EmissionMap);
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            float4 _EmissionColor;
            float _AnimValue, _CloakRate;
        CBUFFER_END
        // GoF2/Hull's globals (HullForwardPass.hlsl), for the hull's look while it is still there
        TEXTURECUBE(_GoF2ReflectionCube); SAMPLER(sampler_GoF2ReflectionCube);
        half _GoF2HullReflection;
        half3 _GoF2HullRimColor, _GoF2SunGamma, _GoF2SunAmbient;

        // How much of the lit hull is left in the colour pass's blend at this texel (d: the dissolve map): up to 0.5 the
        // hull where d - 0.025 >= _AnimValue stays, the dissolved rest fades from it over 0..0.25; from 0.5 to 0.75 what
        // was left goes too.
        half CloakHullLeft(float d)
        {
            bool solid = d - 0.025 >= _AnimValue;
            return _AnimValue < 0.5 ? (solid ? 1.0 : 1.0 - saturate(_AnimValue * 4.0))
                                    : (solid ? 1.0 - saturate((_AnimValue - 0.5) * 4.0) : 0.0);
        }
        ENDHLSL

        Pass
        {
            Tags { "LightMode" = "GoF2Cloak" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D_X(_GoF2CloakScene); SAMPLER(sampler_GoF2CloakScene);
            float _GoF2RemasteredFx;   // Settings.RemasteredEffects (Bootstrap): 1 = the remastered look

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 tangentOS : TANGENT; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 tangentWS : TEXCOORD2;
                float3 positionWS : TEXCOORD3;   // Modified: the view direction for the reflection
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes i)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.positionWS = TransformObjectToWorld(i.positionOS.xyz);
                o.uv = i.uv;
                VertexNormalInputs n = GetVertexNormalInputs(i.normalOS, i.tangentOS);
                o.normalWS = n.normalWS;
                o.tangentWS = float4(n.tangentWS, i.tangentOS.w * GetOddNegativeScale());
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                half3 nTS = UnpackNormal(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, i.uv));
                float3 bit = i.tangentWS.w * cross(i.normalWS, i.tangentWS.xyz);
                float3 nWS = normalize(mul(nTS, float3x3(i.tangentWS.xyz, bit, i.normalWS)));
                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).rgb;
                Light main = GetMainLight();
                half ndl = saturate(dot(nWS, main.direction));
                half3 lit = albedo * (main.color * ndl + SampleSH(nWS));
                // Modified (the user's report, 2026-11-17: as the cloak went on and off the hull lost its reflection and its
                // glow): the lit hull with GoF2/Hull's main terms. The original's gamma-space sun (texture x LIGHT0, clamped)
                // where the system sets it, the reflection cubemap and rim by the specular map, and the glowing panels'
                // emission (HDR, so they bloom).
                if (max(_GoF2SunGamma.r, max(_GoF2SunGamma.g, _GoF2SunGamma.b)) > 0.0)
                {
                    half3 texG = LinearToSRGB(albedo);
                    lit = SRGBToLinear(saturate(texG * main.color * ndl * _GoF2SunGamma + texG * _GoF2SunAmbient)) + albedo * SampleSH(nWS);
                }
                {
                    half spec = SAMPLE_TEXTURE2D(_MetallicGlossMap, sampler_MetallicGlossMap, i.uv).a;
                    half reflectTex = spec * spec;
                    float3 v = GetWorldSpaceNormalizeViewDir(i.positionWS);
                    half facing = 1.0h - saturate(dot(v, nWS));
                    half rim = smoothstep(0.8h, 1.0h, facing);
                    half reflectivity = clamp(smoothstep(0.6h, 1.0h, facing), reflectTex, spec * 2.0h + rim);
                    half3 env = SAMPLE_TEXTURECUBE(_GoF2ReflectionCube, sampler_GoF2ReflectionCube, reflect(-v, nWS)).rgb;
                    lit += (env * reflectivity + rim * reflectTex * _GoF2HullRimColor) * _GoF2HullReflection;
                }
                lit += SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, i.uv).rgb * _EmissionColor.rgb;

                float2 suv = GetNormalizedScreenSpaceUV(i.positionCS);
                // Modified: the screen behind is bent by the hull's normal map where it is and stays bent; only the glow
                // seen through the hull (the ship's own lights, engine glow, shots, glare: the bright part of the copy) moves
                // with the original's wobble (n.x * 100 * sin(u_CloakRate), n.y * 75 * cos(u_CloakRate) at 1024x768). The
                // original swept the whole refracted picture, so the stars and space behind the ship slid too (the user's
                // comparison: there only the lights move). The bright part of the still picture is taken out and the
                // bright part of the moving one put in.
                // Modified (#340, the user's comparison, 2026-11-17), Options > Graphics "Remastered shield and cloak"
                // (_GoF2RemasteredFx, off by default): half the original's 100 / 75 px: at the remake's resolutions the
                // normal map's fine detail smeared what shows through into a blur and bent objects too far.
                float bendScale = _GoF2RemasteredFx > 0.5 ? 0.5 : 1.0;
                float2 bend = float2(nTS.x * 100.0, nTS.y * 75.0) * bendScale / float2(1024.0, 768.0);
                float2 sweep = float2(nTS.x * 100.0 * sin(_CloakRate), nTS.y * 75.0 * cos(_CloakRate)) * bendScale / float2(1024.0, 768.0);
                half3 still = SAMPLE_TEXTURE2D_X(_GoF2CloakScene, sampler_GoF2CloakScene, UnityStereoTransformScreenSpaceTex(saturate(suv + bend))).rgb;
                half3 moving = SAMPLE_TEXTURE2D_X(_GoF2CloakScene, sampler_GoF2CloakScene, UnityStereoTransformScreenSpaceTex(saturate(suv + sweep))).rgb;
                // Modified (the user's Darkzov in an orange nebula, 2026-11-17): the glow taken out of the still picture and
                // the moving one's put in left black patches where the still one was bright and the moving one dark, and
                // doubled ones the other way; a bright nebula counted as glow (luma 0.3-0.75). Now the moving picture
                // replaces the still one where either is glow (luma 0.6-0.95), and both are clamped to the original's LDR.
                still = saturate(still);
                moving = saturate(moving);
                half glowStill = smoothstep(0.6, 0.95, dot(still, half3(0.3, 0.59, 0.11)));
                half glowMoving = smoothstep(0.6, 0.95, dot(moving, half3(0.3, 0.59, 0.11)));
                half3 refr = lerp(still, moving, max(glowStill, glowMoving));

                // BumpShaderCloak's fragment shader as it is (libgof2hdaa.so 0x218183): where the dissolve map is below
                // u_AnimValue (+0.025) the screen behind shows, plus a light-blue band along the dissolve front
                // (r >= u_AnimValue - 0.05); up to 0.5 that blends over the lit hull (x4), from 0.5 to 0.75 into pure
                // refraction. The map's pattern stays on the hull for most of the 2 s fade (#47: the remake dissolved by
                // u_AnimValue x 2, so it was gone within the first second).
                float d = SAMPLE_TEXTURE2D(_CloakMap, sampler_CloakMap, i.uv).r;
                half3 color2 = lit;
                if (d - 0.025 < _AnimValue) color2 = refr + half3(0.6, 0.8, 1.0) * step(_AnimValue - 0.05, d);
                half3 col = _AnimValue < 0.5 ? lerp(lit, color2, saturate(_AnimValue * 4.0))
                                             : lerp(color2, refr, saturate((_AnimValue - 0.5) * 4.0));
                return half4(col, 1);
            }
            ENDHLSL
        }

        // Modified: the hull's depth, drawn by CloakPass before the transparents (CloakPass.DepthPass), only where the hull
        // is still mostly there (CloakHullLeft >= 0.5): the cloaked hull's colour comes after them, so without it the
        // ship's lights and engine glow on its far side (the wing tips' light strips seen through the wings) drew through
        // the hull and showed through the cloak, which an uncloaked hull hides. Where the hull has dissolved, and on a
        // fully cloaked ship (_AnimValue >= 0.75, pure refraction), no depth: the shots, explosions, smoke and engine
        // flames behind it are drawn and refracted as the original's (no depth before its last-drawn hull); a depth
        // silhouette there cut a hole in them that gave the ship away.
        Pass
        {
            Tags { "LightMode" = "GoF2CloakDepth" }
            ZWrite On
            ColorMask 0
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            Varyings vert(Attributes i)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.uv = i.uv;
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                clip(CloakHullLeft(SAMPLE_TEXTURE2D(_CloakMap, sampler_CloakMap, i.uv).r) - 0.5);
                return 0;
            }
            ENDHLSL
        }
    
        // Modified (2026-11-17): the whole hull's depth, drawn by CloakPass after the transparents and before its copy of
        // the frame: the ship's own lights and glow (CloakGlow.Defer) are drawn against it then, so those on the hull's far
        // side stay hidden while the near ones go into the copy and are bent and wobbled by the refraction like everything
        // else in it. Everything else behind the ship is already drawn by then (no hole in it).
        Pass
        {
            Tags { "LightMode" = "GoF2CloakDepthAll" }
            ZWrite On
            ColorMask 0
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };
            Varyings vert(Attributes i)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                return o;
            }
            half4 frag(Varyings i) : SV_Target { UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i); return 0; }
            ENDHLSL
        }
    }
}
