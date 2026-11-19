// GoF2/ChorusPortal: a mod material's "portal" block (ModMaterials.Portal; Chorus's FTL portal, from its compiled shaders:
// M_FTL_Portal's base and distortion passes, M_FTL_PortalGlow's; docs/notes/mods-models.md "Chorus portals"). Unlit,
// drawn on both faces of a card.
//   _Kind 0, the portal: its UVs pushed by the height map along the view (a bump offset, Height Ratio), turned to polar
//     coordinates about the card's middle (the angle round it, the distance out) x (1, Tiling V), scrolling out by
//     Panning V a second; the noise there ^ Structure Power x Structure Mult picks the colour from its curve row (the
//     colours' image, CA_BaseVFX's row baked by the importer), x Fade, added. Before it, the refraction (its distortion
//     pass): the scene behind (URP's opaque texture, World.PortalRefraction switches it on) seen through the pulsing
//     spherical normal map (zoomed about the middle by frac(t x Pulse Speed) x Pulse B - 2) plus the ring normal map,
//     bent by (IOR - 1) x the height map's mask squared x _Refraction (the remake's screen scale for Chorus's distortion
//     offsets), blended in by that mask.
//   _Kind 1, the glow: the vertical gradient at -UV: lerp(Color x g, the curve's colour at g, Lerp Color) x saturate(g),
//     added.
Shader "GoF2/ChorusPortal"
{
    Properties
    {
        _Kind ("Kind (0 portal, 1 glow)", Float) = 0
        _Noise ("Noise (T_main)", 2D) = "black" {}
        _Height ("Height / mask (spherical gradient)", 2D) = "black" {}
        _Curve ("Colour curve row", 2D) = "black" {}
        _SphereNormal ("Spherical normal (distortion)", 2D) = "bump" {}
        _RingNormal ("Ring normal (distortion)", 2D) = "bump" {}
        _Gradient ("Glow: vertical gradient", 2D) = "black" {}
        _Polar ("Tiling V, panning V, structure power, structure mult", Vector) = (0.15, 0.33, 1.5, 1)
        _Bump ("Height ratio, fade, IOR, refraction scale", Vector) = (-1, 1, 1.5, 0.05)
        _Pulse ("Pulse speed, pulse B", Vector) = (-0.4, 3, 0, 0)
        _GlowColor ("Glow: colour (rgb), lerp to the curve (a)", Vector) = (1, 0, 0, 1)
        _Color ("Fade (a)", Color) = (1, 1, 1, 1)
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        ZWrite Off
        Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
        TEXTURE2D(_Noise); SAMPLER(sampler_Noise);
        TEXTURE2D(_Height); SAMPLER(sampler_Height);
        TEXTURE2D(_Curve); SAMPLER(sampler_Curve);
        TEXTURE2D(_SphereNormal); SAMPLER(sampler_SphereNormal);
        TEXTURE2D(_RingNormal);
        TEXTURE2D(_Gradient); SAMPLER(sampler_Gradient);
        CBUFFER_START(UnityPerMaterial)
            float _Kind;
            float4 _Polar, _Bump, _Pulse, _GlowColor;
            half4 _Color;
        CBUFFER_END

        struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 tangentOS : TANGENT; float2 uv : TEXCOORD0; };
        struct Varyings
        {
            float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 positionWS : TEXCOORD1;
            float3 normalWS : TEXCOORD2; float4 tangentWS : TEXCOORD3; float4 screen : TEXCOORD4;
        };

        Varyings vert(Attributes i)
        {
            Varyings o;
            o.positionWS = TransformObjectToWorld(i.positionOS.xyz);
            o.positionCS = TransformWorldToHClip(o.positionWS);
            o.uv = i.uv;
            o.normalWS = TransformObjectToWorldNormal(i.normalOS);
            o.tangentWS = float4(TransformObjectToWorldDir(i.tangentOS.xyz), i.tangentOS.w);
            o.screen = ComputeScreenPos(o.positionCS);
            return o;
        }

        // Unreal's UVs (v down) on the loader's (v up): the card's maps as made
        float2 UE(float2 uv) { return float2(uv.x, 1.0 - uv.y); }
        float PowZ(float x, float e) { return x > 0.0 ? pow(x, e) : 0.0; }
        float3 UnpackXY(float4 c) { float2 n = c.xy * 2.0 - 1.0; return float3(n, sqrt(max(1.0 - dot(n, n), 0.0))); }
        ENDHLSL

        // the refraction (M_FTL_Portal's distortion pass), first: its tag is drawn before UniversalForward
        Pass
        {
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            half4 frag(Varyings i) : SV_Target
            {
                if (_Kind > 0.5) discard;
                float2 uv = UE(i.uv);
                float t = _Time.y;
                float e = frac(t * _Pulse.x) * _Pulse.y - 2.0;
                float2 c = uv - 0.5;
                float m = 1.0 - max(1.0 - 2.0 * length(c), 0.0);
                float2 uvS = m > 0.0 ? c * pow(m, e) + 0.5 : float2(0.5, 0.5);
                float3 n = UnpackXY(SAMPLE_TEXTURE2D(_SphereNormal, sampler_SphereNormal, UE(uvS))) + UnpackXY(SAMPLE_TEXTURE2D(_RingNormal, sampler_SphereNormal, i.uv));
                float3 b = cross(i.normalWS, i.tangentWS.xyz) * i.tangentWS.w;
                float3 w = normalize(i.tangentWS.xyz * n.x + b * n.y + i.normalWS * n.z);
                float2 v = mul((float3x3)UNITY_MATRIX_V, w).xy;
                v = v / max(length(v), 1e-5);
                float mask = SAMPLE_TEXTURE2D(_Height, sampler_Height, i.uv).r;
                float k = (_Bump.z - 1.0) * mask * mask;
                float2 suv = i.screen.xy / i.screen.w;
                half3 behind = SampleSceneColor(suv + v * float2(1, -1) * k * _Bump.w);
                return half4(behind, saturate(mask) * _Color.a);
            }
            ENDHLSL
        }

        // the veins (M_FTL_Portal's base pass) or the glow (M_FTL_PortalGlow's), added
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            half4 frag(Varyings i) : SV_Target
            {
                float2 uv = UE(i.uv);
                half3 col;
                if (_Kind > 0.5)
                {
                    float g = SAMPLE_TEXTURE2D(_Gradient, sampler_Gradient, UE(-uv)).r;
                    half3 curve = SAMPLE_TEXTURE2D_LOD(_Curve, sampler_Curve, float2(g, 0.5), 0).rgb;
                    col = lerp(_GlowColor.rgb * g, curve, _GlowColor.a) * saturate(g);
                }
                else
                {
                    // the bump offset: the view in the card's tangent frame x height ratio x the height
                    float3 view = normalize(GetCameraPositionWS() - i.positionWS);
                    float3 b = cross(i.normalWS, i.tangentWS.xyz) * i.tangentWS.w;
                    float h = SAMPLE_TEXTURE2D(_Height, sampler_Height, i.uv).r;
                    float2 off = float2(dot(i.tangentWS.xyz, view), dot(b, view)) * (_Bump.x * h * 0.5);
                    float2 p = (uv + off) * 2.0 - 1.0;
                    float2 polar = float2(frac(atan2(p.x, p.y) * (0.5 / PI)), length(p)) * float2(1.0, _Polar.x) + float2(0.0, _Time.y * _Polar.y);
                    float2 gx = ddx(uv), gy = ddy(uv);   // (the uv's own gradients: the angle's seam stays clean)
                    float noise = SAMPLE_TEXTURE2D_GRAD(_Noise, sampler_Noise, UE(polar), gx, gy).r;
                    float s = PowZ(noise, _Polar.z) * _Polar.w;
                    col = SAMPLE_TEXTURE2D_LOD(_Curve, sampler_Curve, float2(s, 0.5), 0).rgb * _Bump.y;
                }
                return half4(max(col, 0.0) * _Color.a, 0.0);
            }
            ENDHLSL
        }
    }
}
