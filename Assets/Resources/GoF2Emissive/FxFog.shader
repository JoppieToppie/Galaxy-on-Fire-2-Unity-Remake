// FxFog.shader
// Modified: the GoF2/Additive and GoF2/AlphaBlend Shader Graphs' look (_MainTex * lerp(1, vertex colour, _UseVertexColor)
// * _Color * _Glow, both faces, no depth write) plus the distance fog GoF2/Hull gives the station they sit on. URP 17's
// Shader Graph transparents take only volumetric fog, never the scene's linear / exp fog, so the burning Luur station's
// fire and smoke (sn_burning_station_fire_anim_add / _alpha) stood out clear against its fogged hull (a player's report).
// The fog as the hull's: by the real distance to the camera (GoF2RadialFog); an additive layer fades to nothing in the
// fog instead of taking its colour (_Additive). FxFog.Apply swaps the materials in (AssembledObject.Awake).

Shader "GoF2/FxFog"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _UseVertexColor ("Multiply Vertex Color", Float) = 1
        _Glow ("Glow Intensity", Float) = 1
        [HideInInspector] _SrcBlend ("Src Blend", Float) = 5    // SrcAlpha
        [HideInInspector] _DstBlend ("Dst Blend", Float) = 10   // OneMinusSrcAlpha (additive: One)
        [HideInInspector] _Additive ("Additive", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            Cull Off
            ZWrite Off
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Color;
                half _UseVertexColor;
                half _Glow;
                half _SrcBlend;
                half _DstBlend;
                half _Additive;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                float3 positionWS : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // As GoF2/Hull (HullForwardPass.hlsl): the fog factor by the eye-to-pixel distance. unity_FogParams: x = density /
            // sqrt(ln 2), y = density / ln 2, z = -1 / (end - start), w = end / (end - start). 1 = no fog.
            half GoF2FogFactor(float d)
            {
            #if defined(FOG_LINEAR)
                return saturate(d * unity_FogParams.z + unity_FogParams.w);
            #elif defined(FOG_EXP)
                return saturate(exp2(-unity_FogParams.y * d));
            #elif defined(FOG_EXP2)
                float x = unity_FogParams.x * d;
                return saturate(exp2(-x * x));
            #else
                return 1.0h;
            #endif
            }

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = lerp(half4(1, 1, 1, 1), v.color, _UseVertexColor);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                half4 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv) * i.color * _Color;
                c.rgb *= _Glow;
                half f = GoF2FogFactor(distance(i.positionWS, GetCameraPositionWS()));
                c.rgb = _Additive > 0.5h ? c.rgb * f : lerp(unity_FogColor.rgb, c.rgb, f);
                return half4(c.rgb, saturate(c.a));
            }
            ENDHLSL
        }
    }
}
