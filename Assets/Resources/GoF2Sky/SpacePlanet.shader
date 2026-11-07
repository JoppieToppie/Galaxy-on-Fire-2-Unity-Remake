// GoF2/SpacePlanet: a system's planets between the systems (World.InterstellarField), the star map's 3D planets lit as
// GoF2/MapPlanet lights them (ambient and a diffuse term toward the sun, no distance falloff), but toward their own sun's
// direction (_SunDir, per material: two systems' planets can be in view) and fading (_Fade 0..1, the hand-over to the
// arrival orbit's own planet as its approach builds, World.SystemApproach). In Resources so a build keeps it.
Shader "GoF2/SpacePlanet"
{
    Properties
    {
        _BaseMap ("Texture", 2D) = "white" {}
        _BaseColor ("Color", Color) = (1, 1, 1, 1)
        _Ambient ("Ambient", Float) = 0.2
        _Diffuse ("Diffuse", Float) = 1.6
        _SunDir ("Toward the sun (world)", Vector) = (0, 0, 1, 0)
        _Fade ("Fade", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-10" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite On
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _Ambient, _Diffuse;
                float4 _SunDir;
                half _Fade;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 normalWS : TEXCOORD1; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.uv = TRANSFORM_TEX(i.uv, _BaseMap);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half4 t = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv) * _BaseColor;
                half lit = _Ambient + _Diffuse * saturate(dot(normalize(i.normalWS), normalize(_SunDir.xyz)));
                return half4(t.rgb * lit, _Fade);
            }
            ENDHLSL
        }
    }
}
