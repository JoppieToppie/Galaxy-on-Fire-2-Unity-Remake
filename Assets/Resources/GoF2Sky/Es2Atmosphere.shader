// GoF2/Es2Atmosphere: a planet sphere's atmosphere shell (World.Backdrop: a child of the GoF2/Es2Planet sphere, AtmosphereScale x
// its radius; stations.json "planetSphere" "atmosphere"), after EVERSPACE 2's M_Planet_Atmosphere_001 as its compiled translucent
// pixel shader computes it (es2_re planetmat): drawn in the backdrop pass after the planet, its depth on the far plane like it.
//   colour   AtmosphereColor (_AtmosColor, linear) with its hue turned by Variation x 2 pi, toward the opposite hue (turned by pi more)
//            near the terminator: by (1 - min(|N.L + Scattering 0.2|, 1))^8; lit by the sun x saturate(N.L + 0.2)
//   opacity  from f = (1 - |N.V|)^3.4: g = 1 - |2 f - 1|, a = sqrt(g) / (2 - sqrt(g)); MinOpacity 0.01 to 1 by a on the inner
//            half (f < 0.5), (g / ((1 - g) 8 + 1))^2 on the outer half: thickest round the limb, gone at the shell's own edge
// Left out: the selection highlight, ES2's planet distance scaling.
Shader "GoF2/Es2Atmosphere"
{
    Properties
    {
        _AtmosColor ("Atmosphere colour (linear)", Vector) = (0.1, 0.475, 1, 0)
        _Variation ("Variation (0..1, the hue x 2 pi)", Float) = 0
        _SunDir ("Toward the sun (world)", Vector) = (0, 0, 1, 0)
        _SunColor ("Sun light", Color) = (1, 1, 1, 1)
        _Density ("Density", Float) = 1
        _Color ("Fade (a): Backdrop.Fade, as the planet's", Color) = (1, 1, 1, 1)
    }
    SubShader
    {
        Tags { "Queue" = "Transparent-99" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
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

            CBUFFER_START(UnityPerMaterial)
                float4 _AtmosColor;
                float _Variation;
                float4 _SunDir;
                half4 _SunColor;
                half _Density;
                half4 _Color;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float3 positionWS : TEXCOORD1; };

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
                o.normalWS = TransformObjectToWorldDir(normalize(i.positionOS.xyz));
                return o;
            }

            // the hue turned 'angle' radians about the grey axis (Rodrigues about (1, 1, 1) / sqrt 3), as GoF2/Es2Planet's
            half3 HueRotate(half3 c, half angle)
            {
                const half3 k = half3(0.57735026, 0.57735026, 0.57735026);
                half dk = dot(k, c);
                half3 p = c - k * dk;
                half s, co;
                sincos(angle, s, co);
                return p * co + cross(k, p) * s + k * dk;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 view = normalize(GetCameraPositionWS() - i.positionWS);
                float3 l = normalize(_SunDir.xyz);
                half s = dot(n, l) + 0.2;
                half3 c1 = HueRotate(_AtmosColor.rgb, _Variation * 2.0 * PI);
                half3 c2 = HueRotate(c1, PI);
                half w = pow(1.0 - min(abs(s), 1.0), 8.0);
                half3 col = lerp(c1, c2, w) * saturate(s) * _SunColor.rgb;
                half f = pow(max(1.0 - abs(dot(n, view)), 1e-4), 3.4);
                half g = 1.0 - abs(2.0 * f - 1.0);
                half sg = sqrt(max(g, 0.0));
                half a = sg / (2.0 - sg);
                half inner = lerp(0.01, 1.0, a);
                half outer = g / ((1.0 - g) * 8.0 + 1.0);
                half opacity = (f >= 0.5 ? outer * outer : inner) * _Density;
                return half4(col, saturate(opacity) * _Color.a);
            }
            ENDHLSL
        }
    }
}
