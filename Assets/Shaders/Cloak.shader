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
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            float _AnimValue, _CloakRate;
        CBUFFER_END

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

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 tangentOS : TANGENT; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 tangentWS : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes i)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
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
                half3 lit = albedo * (main.color * saturate(dot(nWS, main.direction)) + SampleSH(nWS));

                float2 suv = GetNormalizedScreenSpaceUV(i.positionCS);
                // Modified: the screen behind is bent by the hull's normal map where it is and stays bent; only the glow
                // seen through the hull (the ship's own lights, engine glow, shots, glare: the bright part of the copy) moves
                // with the original's wobble (n.x * 100 * sin(u_CloakRate), n.y * 75 * cos(u_CloakRate) at 1024x768). The
                // original swept the whole refracted picture, so the stars and space behind the ship slid too (the user's
                // comparison: there only the lights move). The bright part of the still picture is taken out and the
                // bright part of the moving one put in.
                float2 bend = float2(nTS.x * 100.0, nTS.y * 75.0) / float2(1024.0, 768.0);
                float2 sweep = float2(nTS.x * 100.0 * sin(_CloakRate), nTS.y * 75.0 * cos(_CloakRate)) / float2(1024.0, 768.0);
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

        // Modified: the hull's depth, drawn by CloakPass before the transparents (CloakPass.DepthPass): the cloaked hull's
        // colour comes after them, so without it the ship's lights and engine glow on its far side (the wing tips' light
        // strips seen through the wings) drew through the hull and showed through the cloak, which an uncloaked hull
        // hides. The whole hull, at every stage of the cloak (2026-11-17; the #185 review had it only where the hull was
        // still mostly there, so a fully cloaked ship showed its far-side lights again).
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
                // Modified: the whole hull, cloaked or not (the user's request, 2026-11-17): a fully cloaked ship wrote no
                // depth (the #185 review), so its own lights and engine glow on the far side showed through the
                // invisible hull. The shots, explosions and smoke behind it are hidden by it too, as an uncloaked hull does.
                return 0;
            }
            ENDHLSL
        }
    }
}
