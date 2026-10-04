// Hidden/GoF2/BarDistance: the distance from the camera to the nearest surface (metres, in a float target). BarFlybys
// renders the Space Lounge's room with it once and keeps its flybys behind the room's walls (never inside the room).
Shader "Hidden/GoF2/BarDistance"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite On
        ZTest LEqual
        Cull Off

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                return o;
            }

            float4 frag(Varyings i) : SV_Target
            {
                return float4(distance(i.positionWS, GetCameraPositionWS()), 0, 0, 1);
            }
            ENDHLSL
        }
    }
}
