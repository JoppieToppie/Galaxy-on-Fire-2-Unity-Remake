// GoF2/ShieldBubble: the emergency system's bubble (mesh 14374 v_shield, material 27150; combat_equipment.md 2), a port of
// AbyssEngine::EnergyShield (Init 0x9519c). Material 27150 is type 0x22, which PaintCanvas::End3d draws after
// Engine::CopyFBO with GlEnable(0x1100018) (flag 0x200000 -> EnergyShield::ShaderIndex): the screen behind the sphere,
// pushed by its normal map and most at its rim, plus a blue rim of its own:
//   n = normalize(MVP * normal), e = normalize(MVP * (eye - position))   (both clip-space directions, w = 0)
//   nt = normal map * 2 - 1;  rim = smoothstep(0, 1, 1 - dot(e, n))
//   rimFx = smoothstep(0, 1, 1 - dot(e, n * nt)) * (0, 0.6, 1)
//   coord = fragCoord + (nt.xy * 0.4 + n.xy * rim) * 150 px;  colour = screen(coord) + rimFx
// The noise texture (s_texture[0]) is bound but unused on the phone, and so are the clocks UpdateMeshData sets; the PC
// version (EnergyShield.fx) draws two energy bands with them and pushes the screen further (250 / 200 px), as here.
// #251: the remake drew it with SimpleRefractionShader's formula instead (only the bent screen, no colour), so the shield was hard to see.
// The scene it shows is clamped to LDR, like the original's framebuffer: the remake's HDR sun core smeared over the bubble
// bloomed the screen white (#45). Remake (Discord report "Emergency system invisible"): drawn by CloakPass (LightMode
// GoF2Cloak) from its copy of the frame taken after the transparents, like the original's draw after Engine::CopyFBO, so the
// ship's additive lights, engine glow and exhaust show through it.
Shader "GoF2/ShieldBubble"
{
    Properties
    {
        _NormalMap ("Normal", 2D) = "bump" {}
        _NoiseMap ("Noise", 2D) = "black" {}
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
            TEXTURE2D(_NoiseMap); SAMPLER(sampler_NoiseMap);
            TEXTURE2D_X(_GoF2CloakScene); SAMPLER(sampler_GoF2CloakScene);
            float _GoF2RemasteredFx;   // Settings.RemasteredEffects (Bootstrap): 1 = the PC version's look below, 0 = the phone's
            CBUFFER_START(UnityPerMaterial)
                float4 _NormalMap_ST, _NoiseMap_ST;
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
                // Modified: the PC version's EnergyShield.fx (the user's files, 2026-11-17): the push 250 px across, 200 up
                // (the phone's 150 both ways), at the phone's 1024 x 768 reference.
                // Options > Graphics "Remastered shield and cloak" (off by default): the phone's 150 px both ways.
                float2 push = _GoF2RemasteredFx > 0.5 ? float2(250.0, 200.0) : float2(150.0, 150.0);
                float2 offset = (nt.xy * 0.4 + n.xy * rim) * push * _Strength / float2(1024.0, 768.0);
                half3 scene = SAMPLE_TEXTURE2D_X(_GoF2CloakScene, sampler_GoF2CloakScene, UnityStereoTransformScreenSpaceTex(saturate(suv + offset))).rgb;
                // Modified: the PC version's two energy bands, the noise texture's iso-lines at levels sweeping from 0.1 to 0.9
                // (one up, one down, half a cycle apart, a cycle 2.5 s), shown in a 9 s cycle (in over 0-3 s, out over 4-8 s).
                // The noise is read like the normal map, by the direction (the mesh's UVs break where they wrap).
                float noise1 = SAMPLE_TEXTURE2D(_NoiseMap, sampler_NoiseMap, d.yz * 0.25 + 0.5).r * w.x
                             + SAMPLE_TEXTURE2D(_NoiseMap, sampler_NoiseMap, d.xz * 0.25 + 0.5).r * w.y
                             + SAMPLE_TEXTURE2D(_NoiseMap, sampler_NoiseMap, d.xy * 0.25 + 0.5).r * w.z;
                float noise2 = SAMPLE_TEXTURE2D(_NoiseMap, sampler_NoiseMap, d.yz * 0.25 + 0.5 + float2(0.25, 0.5)).r * w.x
                             + SAMPLE_TEXTURE2D(_NoiseMap, sampler_NoiseMap, d.xz * 0.25 + 0.5 + float2(0.25, 0.5)).r * w.y
                             + SAMPLE_TEXTURE2D(_NoiseMap, sampler_NoiseMap, d.xy * 0.25 + 0.5 + float2(0.25, 0.5)).r * w.z;
                float t = _Anim;
                float level1 = smoothstep(0.0, 1.0, frac(t * 0.4)) * 0.8 + 0.1;
                float level2 = smoothstep(0.0, 1.0, 1.0 - frac(t * 0.4 + 0.5)) * 0.8 + 0.1;
                float cycle = fmod(t, 9.0);
                float shown = smoothstep(0.0, 3.0, cycle) * (1.0 - smoothstep(4.0, 8.0, cycle));
                float band1 = smoothstep(level1 - 0.04, level1, noise1) * (1.0 - smoothstep(level1, level1 + 0.04, noise1));
                float band2 = smoothstep(level2 - 0.06, level2, noise2) * (1.0 - smoothstep(level2, level2 + 0.06, noise2));
                half3 bands = shown * (band1 * half3(0.0, 0.4, 0.7) + band2 * half3(0.0, 0.3, 0.5)) * step(0.5, _GoF2RemasteredFx);
                return half4(saturate(min(scene, 1.0) + rimFx + bands), 1);
            }
            ENDHLSL
        }
    }
}
