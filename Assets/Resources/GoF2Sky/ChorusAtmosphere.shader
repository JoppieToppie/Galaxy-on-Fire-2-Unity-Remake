// GoF2/ChorusAtmosphere: a Chorus planet's atmosphere shell (stations.json planetSphere "chorusAtmosphere",
// World.Backdrop.AtmosphereShell), M_Planet03 with "Is Atmosphere Shell?" from its compiled translucent pass
// (MI_Planet_LocTRN_Atmosphere's, the same as MI_Planet03c_Atmosphere_LocAST's): the glow does not come from the shell's
// own surface but from how close the view ray passes the planet's centre: from the surface (PlanetRadiusUU) out to
// AtmosphereHeightUU above it, (1 - height / AtmosphereHeightUU)^AtmosphereExponent, nothing in front of the planet's disc.
// Its colour from the CA_Planets rows baked into _CACurves (0 the directional colour, 1 the fresnel colour) by the sun's
// angle on the shell, the fresnel one by (1 - N.V)^Fresnel Exp, both brighter on the lit side by the terminator
// ((N.L x Terminator Tightness + 1)^2.2); added. The shell's normals face in (Chorus's SM_Planet_Atmosphere): its far
// side is drawn (Cull Front), its normal the inward one.
Shader "GoF2/ChorusAtmosphere"
{
    Properties
    {
        _CACurves ("Curves (rows: directional, fresnel)", 2D) = "black" {}
        _CAShape ("Height / planet radius, exponent, shell scale, terminator tightness", Vector) = (0.03, 3, 1.04, 10)
        _CAColour ("Directional emissive, fresnel exp, fresnel base, fresnel emissive", Vector) = (1, 8, 0.05, 1)
        _CABright ("Fresnel lit, fresnel shade, brightness lit, brightness shadowed", Vector) = (1, 1, 1, 1)
        _CATint ("Overall emissive tint", Vector) = (1, 1, 1, 0)
        _SunDir ("Toward the sun (world)", Vector) = (0, 0, 1, 0)
        _Color ("Fade (a): Backdrop.Fade, as the planet's", Color) = (1, 1, 1, 1)
    }
    SubShader
    {
        Tags { "Queue" = "Transparent-98" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend One One
        ZWrite Off
        ZTest LEqual
        Cull Front

        Pass
        {
            Tags { "LightMode" = "GoF2Backdrop" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_CACurves); SAMPLER(sampler_CACurves);
            CBUFFER_START(UnityPerMaterial)
                float4 _CAShape, _CAColour, _CABright, _CATint;
                float4 _SunDir;
                half4 _Color;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; };

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
                return o;
            }

            float PowZ(float x, float e) { return x > 0.0 ? pow(x, e) : 0.0; }
            float3 Curve(float u, float row) { return SAMPLE_TEXTURE2D_LOD(_CACurves, sampler_CACurves, float2(u, (row + 0.5) * 0.5), 0).rgb; }

            half4 frag(Varyings i) : SV_Target
            {
                float3 centre = float3(UNITY_MATRIX_M._m03, UNITY_MATRIX_M._m13, UNITY_MATRIX_M._m23);
                float shellRadius = length(float3(UNITY_MATRIX_M._m00, UNITY_MATRIX_M._m10, UNITY_MATRIX_M._m20));
                float R = shellRadius / max(_CAShape.z, 1e-4), H = R * _CAShape.x;
                float3 cam = GetCameraPositionWS();
                float3 toCam = normalize(cam - i.positionWS);
                // the view ray's closest pass by the planet's centre
                float3 closest = cam + toCam * dot(toCam, centre - cam);
                float dist = length(closest - centre) - R;
                float density = PowZ(max((1.0 - saturate(dist / max(H, 1e-6))) - (dist <= 0.0 ? 1.0 : 0.0), 0.0), _CAShape.y);
                float3 outward = normalize(i.positionWS - centre);
                float3 l = normalize(_SunDir.xyz);
                float ndl = dot(outward, l);
                float uL = (ndl + 1.0) * 0.5;
                float x = 1.0 - max(dot(-outward, toCam), 0.0);
                float fres = PowZ(abs(x), _CAColour.y) * (1.0 - _CAColour.z) + _CAColour.z;
                float t = PowZ(saturate(ndl * _CAShape.w + 1.0), 2.2);
                float3 col = Curve(uL, 0.0) * _CAColour.x + Curve(uL, 1.0) * fres * lerp(_CABright.y, _CABright.x, t) * _CAColour.w;
                col *= density * lerp(_CABright.w, _CABright.z, t) * _CATint.rgb;
                return half4(max(col, 0.0) * _Color.a, 0.0);
            }
            ENDHLSL
        }
    }
}
