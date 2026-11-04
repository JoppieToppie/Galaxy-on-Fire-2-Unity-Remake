// GoF2/BlazeHaze: the gamma shield's blaze (sn_ship_blaze_flames / _glow, PlayerHealth.SetupBlaze) bends the light of what
// is behind it, a mirage over hot air (the iOS version's blaze does; a player's comparison with a video of it): the ship and
// the scene behind the blaze waver, the blaze's own flames don't. A second material on the blaze's meshes, drawn by CloakPass
// (LightMode GoF2Cloak) from its copy of the frame after the transparents, which has every light behind the blaze but not
// the blaze itself: its flames are drawn by CloakPass too, after this (GoF2/BlazeAdditive, Queue Transparent+30), unbent.
// The bend is a rising ripple of procedural noise in the part's UVs;
// where it shows comes from the blaze texture's brightness (scrolled as the part's own through the renderer's property
// block: _MainTex_ST, _UVOffset), fading out with it, so it has no edge of its own.
Shader "GoF2/BlazeHaze"
{
    Properties
    {
        _MainTex ("Blaze (where it bends)", 2D) = "black" {}
        _Strength ("Strength (screen fraction)", Float) = 0.006
        _Scale ("Ripple scale", Float) = 6
        _Speed ("Rise speed", Float) = 0.35
        _NormalMap ("Bend map (sn_ship_blaze_flames_normal)", 2D) = "bump" {}
        _UseNormalMap ("Bend by the map (1) or by noise (0)", Float) = 0
    }
    SubShader
    {
        Tags { "Queue" = "Transparent+20" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off
        Cull Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Tags { "LightMode" = "GoF2Cloak" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D(_NormalMap); SAMPLER(sampler_NormalMap);
            TEXTURE2D_X(_GoF2CloakScene); SAMPLER(sampler_GoF2CloakScene);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _UVOffset;
                float _Strength, _Scale, _Speed, _UseNormalMap;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float2 rawUv : TEXCOORD1; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.uv = i.uv * _MainTex_ST.xy + _MainTex_ST.zw - _UVOffset.xy;
                o.rawUv = i.uv;
                return o;
            }

            float Hash(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }

            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), u.x), lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), u.x), u.y);
            }

            half4 frag(Varyings i) : SV_Target
            {
                half3 blaze = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).rgb;
                float m = smoothstep(0.02, 0.35, dot(blaze, half3(0.3, 0.59, 0.11)));
                if (m <= 0.001) discard;
                // Two layers of noise drifting up the plane at different rates: the shimmer of hot air.
                float t = _Time.y * _Speed;
                float2 p = i.rawUv * _Scale;
                float2 bend = float2(Noise(p + float2(0.0, -t * 2.0)) + 0.5 * Noise(p * 2.1 + float2(5.2, -t * 3.1)),
                                     Noise(p + float2(3.7, -t * 2.3)) + 0.5 * Noise(p * 1.9 + float2(1.3, -t * 2.7)));
                bend = (bend / 1.5 - 0.5) * 2.0;
                // Modified: or the blaze's own normal map, scrolled with the flames (the iOS build's heat haze, it seems).
                float2 mapped = UnpackNormal(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, i.uv)).xy;
                bend = lerp(bend, mapped, _UseNormalMap);
                float2 suv = GetNormalizedScreenSpaceUV(i.positionCS);
                half3 scene = SAMPLE_TEXTURE2D_X(_GoF2CloakScene, sampler_GoF2CloakScene, UnityStereoTransformScreenSpaceTex(saturate(suv + bend * _Strength * m))).rgb;
                return half4(scene, m);
            }
            ENDHLSL
        }
    }
}
