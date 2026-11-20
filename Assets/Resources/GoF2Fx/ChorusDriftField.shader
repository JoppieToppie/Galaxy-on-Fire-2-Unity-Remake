// GoF2/ChorusDriftField: a drift energy-field shell (ships.json "driftFx" fields, World.ModDriftFx; Chorus's
// M_Drift_MagnetField on SM_Drift_HalfSphere, the Forsaken's drift: from its compiled translucent base pass, the mesh-particle
// pixel shader; docs/notes/mods-models.md "Drift energy field"). Unlit, both faces, added. uv the mesh's UV0 as Unreal's (v
// down; the textures are sampled at (u, 1 - v)), t the time, C the particle's colour (_DriftTint: rgb, a its alpha):
//   n1 = Band(uv x Tiling + t (0, Pan)).r, n2 = Band(uv x Tiling - t (0, Pan)).r
//   m = Mask((uv.y + Upositon, uv.x + 1) + 0.2 n1).r; core = m > 0 ? m^pow_core x mul_core : 0
//   a = Distort(uv x Tiling_CoreLine + t (0, Panning_V_CoreLine)).r, b the same panned the other way
//   s = Line((uv.y + 5t, uv.x) + a b Distort_Ring).r; line = s > 0 ? s^2 mul_CoreLine : 0; edge = 1 - 4 v (1 - v)
//   l1 = Background(uv x Tiling_BG + (0, 0.1t)).r, l2 = Background(uv x Tiling_BG - (0, 0.06t)).r
//   colour = C.rgb (1 + core) + line (1 + 25 edge)
//   opacity = saturate(F x (C.a (q + p + m (n1 + n2) mul_core + (l1 + l2) edge) + line (1 + 25 edge))), with
//     q = (n1 n2)^Textures Exp x Textures Mult, p = (l1 l2)^pow_BG x Mul_BG (0 where the base is 0), F = (N.V)^Fresnel_pow
//   out: colour x opacity x _DriftE.z (the exposure: Chorus's HDR to the remake's), added.
// Left out: the auto exposure's Exposure Compensation (0.4 of the colour made exposure-independent), the editor's
// SelectionColor, the translucency's height fog.
Shader "GoF2/ChorusDriftField"
{
    Properties
    {
        _Band ("Band (R)", 2D) = "black" {}
        _Mask ("Mask (R)", 2D) = "black" {}
        _Distort ("Distortion noise (R)", 2D) = "black" {}
        _Line ("Line (R)", 2D) = "black" {}
        _Background ("Background noise (R)", 2D) = "black" {}
        _DriftTint ("Colour (rgb, HDR), alpha", Vector) = (0.4, 1, 0.9, 1)
        _DriftA ("Upositon, Panning V, Tiling U, Tiling V", Vector) = (0.5, 0.2, 1, 0.12)
        _DriftB ("pow_core, mul_core, Panning_V_CoreLine, Distort_Ring", Vector) = (1, 5, 2, 0.06)
        _DriftC ("Tiling U_CoreLine, Tiling_V_CoreLine, mul_CoreLine, Fresnel_pow", Vector) = (1, 1, 12, 1.6)
        _DriftD ("Tiling U_BG, Tiling V_BG, pow_BG, Mul_BG", Vector) = (1.5, 0.45, 1.5, 2)
        _DriftE ("Textures Exp, Textures Mult, exposure", Vector) = (1, 4, 1, 0)
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_Band); SAMPLER(sampler_Band);
            TEXTURE2D(_Mask); SAMPLER(sampler_Mask);
            TEXTURE2D(_Distort); SAMPLER(sampler_Distort);
            TEXTURE2D(_Line); SAMPLER(sampler_Line);
            TEXTURE2D(_Background); SAMPLER(sampler_Background);
            CBUFFER_START(UnityPerMaterial)
                float4 _DriftTint, _DriftA, _DriftB, _DriftC, _DriftD, _DriftE;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 positionWS : TEXCOORD1; float3 normalWS : TEXCOORD2; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.uv = i.uv;
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                return o;
            }

            float2 UE(float2 uv) { return float2(uv.x, 1.0 - uv.y); }   // (an Unreal texture coordinate on the PNG as loaded)
            float PowZ(float x, float e) { return x > 0.0 ? pow(x, e) : 0.0; }

            half4 frag(Varyings i) : SV_Target
            {
                float t = _Time.y;
                float2 uv = i.uv;
                float2 tiling = _DriftA.zw;
                float n1 = SAMPLE_TEXTURE2D(_Band, sampler_Band, UE(uv * tiling + t * float2(0.0, _DriftA.y))).r;
                float n2 = SAMPLE_TEXTURE2D(_Band, sampler_Band, UE(uv * tiling - t * float2(0.0, _DriftA.y))).r;
                float m = SAMPLE_TEXTURE2D(_Mask, sampler_Mask, UE(float2(uv.y + _DriftA.x, uv.x + 1.0) + n1 * 0.2)).r;
                float core = PowZ(m, _DriftB.x) * _DriftB.y;

                float2 lineUv = uv * _DriftC.xy;
                float a = SAMPLE_TEXTURE2D(_Distort, sampler_Distort, UE(lineUv + t * float2(0.0, _DriftB.z))).r;
                float b = SAMPLE_TEXTURE2D(_Distort, sampler_Distort, UE(lineUv - t * float2(0.0, _DriftB.z))).r;
                float s = SAMPLE_TEXTURE2D(_Line, sampler_Line, UE(float2(uv.y + 5.0 * t, uv.x) + a * b * _DriftB.w)).r;
                float lineGlow = s > 0.0 ? s * s * _DriftC.z : 0.0;
                float edge = 1.0 - 4.0 * uv.y * (1.0 - uv.y);
                float lineTerm = lineGlow * (1.0 + 25.0 * edge);

                float2 bgUv = uv * _DriftD.xy;
                float l1 = SAMPLE_TEXTURE2D(_Background, sampler_Background, UE(bgUv + float2(0.0, 0.1 * t))).r;
                float l2 = SAMPLE_TEXTURE2D(_Background, sampler_Background, UE(bgUv - float2(0.0, 0.06 * t))).r;

                float3 colour = _DriftTint.rgb * (1.0 + core) + lineTerm;
                float q = PowZ(n1 * n2, _DriftE.x) * _DriftE.y;
                float p = PowZ(l1 * l2, _DriftD.z) * _DriftD.w;
                float sum = q + p + m * (n1 + n2) * _DriftB.y + (l1 + l2) * edge;
                float3 V = normalize(GetCameraPositionWS() - i.positionWS);
                float d = abs(dot(normalize(i.normalWS), V));   // (both faces: Unreal turns a back face's normal to the eye)
                float F = PowZ(d, _DriftC.w);
                float opacity = saturate(F * (_DriftTint.a * sum + lineTerm));
                return half4(max(colour, 0.0) * opacity * _DriftE.z, 0.0);
            }
            ENDHLSL
        }
    }
}
