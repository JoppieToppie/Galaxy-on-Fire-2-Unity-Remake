// GoF2/BlazeHaze: the gamma shield's blaze (sn_ship_blaze_flames / _glow, PlayerHealth.SetupBlaze) bending what is behind
// it, the ship among it, like a heat haze (a player's comparison with the original). A second material on the blaze's
// meshes, drawn by CloakPass (LightMode GoF2Cloak) from its copy of the frame taken after the transparents: the scene there
// is shown pushed along the brightness slope of the blaze texture, scrolled as the part's own (its UV scroll reaches this
// material through the renderer's property block: _MainTex_ST, _UVOffset), with a slow wobble; only where the blaze is
// bright enough, fading out with it, so the bend has no edge of its own.
Shader "GoF2/BlazeHaze"
{
    Properties
    {
        _MainTex ("Blaze", 2D) = "black" {}
        _Strength ("Strength (screen fraction)", Float) = 0.02
        _Wobble ("Wobble speed", Float) = 1.5
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

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D_X(_GoF2CloakScene); SAMPLER(sampler_GoF2CloakScene);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _UVOffset;
                float _Strength, _Wobble;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.uv = i.uv * _MainTex_ST.xy + _MainTex_ST.zw - _UVOffset.xy;
                return o;
            }

            float Lum(float2 uv) { return dot(SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).rgb, float3(0.3, 0.59, 0.11)); }

            half4 frag(Varyings i) : SV_Target
            {
                const float e = 0.01;
                float m = Lum(i.uv);
                float2 slope = float2(Lum(i.uv + float2(e, 0)) - Lum(i.uv - float2(e, 0)),
                                      Lum(i.uv + float2(0, e)) - Lum(i.uv - float2(0, e)));
                float t = _Time.y * _Wobble;
                float2 wobble = float2(sin(t + i.uv.y * 23.0), cos(t * 1.3 + i.uv.x * 19.0));
                float2 offset = (slope * 4.0 + wobble * m * 0.5) * _Strength;
                float2 suv = GetNormalizedScreenSpaceUV(i.positionCS);
                half3 scene = SAMPLE_TEXTURE2D_X(_GoF2CloakScene, sampler_GoF2CloakScene, UnityStereoTransformScreenSpaceTex(saturate(suv + offset))).rgb;
                float a = smoothstep(0.02, 0.25, m);
                return half4(scene, a);
            }
            ENDHLSL
        }
    }
}
