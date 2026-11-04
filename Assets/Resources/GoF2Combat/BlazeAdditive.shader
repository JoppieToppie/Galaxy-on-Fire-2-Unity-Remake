// GoF2/BlazeAdditive: the gamma shield's blaze flames (PlayerHealth.SetupBlaze) as GoF2/Additive draws them (texture x the
// vertex colour where _UseVertexColor x _Color x _Glow, added), but drawn by CloakPass (LightMode GoF2Cloak) after GoF2/
// BlazeHaze (Queue Transparent+30): so CloakPass's copy of the frame, which the haze bends, doesn't hold the blaze, and the
// flames stay unbent over the wavering ship (a player's request: the mirage on what is behind, not on the flames).
Shader "GoF2/BlazeAdditive"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Color", Color) = (1, 1, 1, 1)
        _Glow ("Glow", Float) = 1
        _UseVertexColor ("Use vertex colour", Float) = 0
    }
    SubShader
    {
        Tags { "Queue" = "Transparent+30" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off
        Cull Off
        Blend SrcAlpha One

        Pass
        {
            Tags { "LightMode" = "GoF2Cloak" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _UVOffset;
                float4 _Color;
                float _Glow, _UseVertexColor;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.uv = i.uv * _MainTex_ST.xy + _MainTex_ST.zw - _UVOffset.xy;
                o.color = lerp(float4(1, 1, 1, 1), i.color, _UseVertexColor);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half4 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv) * i.color * _Color;
                return half4(c.rgb * _Glow, c.a);
            }
            ENDHLSL
        }
    }
}
