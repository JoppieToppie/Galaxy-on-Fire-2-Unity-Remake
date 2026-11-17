// GoF2/ShieldBubble: the emergency system's bubble (mesh 14374 v_shield, material 27150; combat_equipment.md 2), a port of
// AbyssEngine::EnergyShield (Init 0x9519c). Material 27150 is type 0x22, which PaintCanvas::End3d draws after
// Engine::CopyFBO with GlEnable(0x1100018) (flag 0x200000 -> EnergyShield::ShaderIndex): the screen behind the sphere,
// pushed by its normal map and most at its rim, plus a blue rim of its own:
//   n = normalize(MVP * normal), e = normalize(MVP * (eye - position))   (both clip-space directions, w = 0)
//   nt = normal map * 2 - 1;  rim = smoothstep(0, 1, 1 - dot(e, n))
//   rimFx = smoothstep(0, 1, 1 - dot(e, n * nt)) * (0, 0.6, 1)
//   coord = fragCoord + (nt.xy * 0.4 + n.xy * rim) * 150 px;  colour = screen(coord) + rimFx
// The noise texture (s_texture[0]) is bound but unused, and so are the clocks UpdateMeshData sets. #251: the remake drew it
// with SimpleRefractionShader's formula instead (only the bent screen, no colour), so the shield was hard to see.
// The scene it shows is clamped to LDR, like the original's framebuffer: the remake's HDR sun core smeared over the bubble
// bloomed the screen white (#45). Remake (Discord report "Emergency system invisible"): drawn by CloakPass (LightMode
// GoF2Cloak) from its copy of the frame taken after the transparents, like the original's draw after Engine::CopyFBO, so the
// ship's additive lights, engine glow and exhaust show through it.
Shader "GoF2/ShieldBubble"
{
    Properties
    {
        _NormalMap ("Normal", 2D) = "bump" {}
        _Anim ("Anim", Float) = 0
        _Strength ("Strength", Float) = 1
    }
    SubShader
    {
        Tags { "Queue" = "Transparent+10" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off
        Cull Back

        Pass
        {
            Tags { "LightMode" = "GoF2Cloak" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_NormalMap); SAMPLER(sampler_NormalMap);
            TEXTURE2D_X(_GoF2CloakScene); SAMPLER(sampler_GoF2CloakScene);
            CBUFFER_START(UnityPerMaterial)
                float4 _NormalMap_ST;
                float _Anim, _Strength;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 nClip : TEXCOORD0; float3 eClip : TEXCOORD1; float3 dirOS : TEXCOORD2; UNITY_VERTEX_OUTPUT_STEREO };

            // A view-space direction as the original's MVP * (d, 0): the projection scales x / y (y up whatever the graphics
            // API flips) and turns z round (GL's z' ~ -z for a far plane far beyond the near one).
            float3 ClipDir(float3 dVS)
            {
                float2 p = float2(UNITY_MATRIX_P._m00, abs(UNITY_MATRIX_P._m11));
                return normalize(float3(dVS.xy * p, -dVS.z));
            }

            Varyings vert(Attributes i)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 ws = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(ws);
                o.dirOS = i.positionOS.xyz;
                o.nClip = ClipDir(TransformWorldToViewDir(TransformObjectToWorldNormal(i.normalOS), true));
                o.eClip = ClipDir(TransformWorldToViewDir(GetWorldSpaceViewDir(ws), true));
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                // Remake: the normal map is sampled around the sphere by three blended planar projections of its direction, not
                // by the mesh's UVs: the image doesn't tile, so where the UVs wrap (the side the chase camera looks at) the
                // refraction broke into a cut (the user's report, 2026-10-21). 0.25 a unit of direction is about the UVs' own
                // density (a turn of the sphere is one texture width).
                float3 d = normalize(i.dirOS);
                float3 w = pow(abs(d), 4.0);
                w /= max(w.x + w.y + w.z, 1e-5);
                float3 nt = UnpackNormal(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, d.yz * 0.25 + 0.5)) * w.x
                          + UnpackNormal(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, d.xz * 0.25 + 0.5)) * w.y
                          + UnpackNormal(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, d.xy * 0.25 + 0.5)) * w.z;
                float3 n = normalize(i.nClip), e = normalize(i.eClip);
                float rim = smoothstep(0.0, 1.0, 1.0 - dot(e, n));
                half3 rimFx = smoothstep(0.0, 1.0, 1.0 - dot(e, n * nt)) * half3(0.0, 0.6, 1.0);
                float2 suv = GetNormalizedScreenSpaceUV(i.positionCS);
                float2 offset = (nt.xy * 0.4 + n.xy * rim) * 150.0 * _Strength / float2(1024.0, 768.0);
                half3 scene = SAMPLE_TEXTURE2D_X(_GoF2CloakScene, sampler_GoF2CloakScene, UnityStereoTransformScreenSpaceTex(saturate(suv + offset))).rgb;
                return half4(saturate(min(scene, 1.0) + rimFx), 1);
            }
            ENDHLSL
        }
    }
}
