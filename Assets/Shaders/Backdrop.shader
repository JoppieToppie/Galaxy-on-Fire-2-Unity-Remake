// GoF2/Backdrop: sun and planet quads (StarSystem::render). The original draws them in the background pass with the
// depth test off, after the sky and before the scene. Here they are regular quads around the camera whose depth is
// pushed onto the far plane: BackdropPass draws them right after the skybox (before URP's opaque texture copy, which the
// cloak refracts), behind every opaque object; the scene's transparent effects draw over them. Painter's order via the
// render queue.
// _Mirror flips u (StarSystem's rotate(0, pi, 0), so a planet's lit rim faces the sun); _Tint is added to the
// texture colour (blend mode 21, fogged planets). _CoreGlow lifts only the near-white core into HDR (the sun under the
// remake's bloom), so the rest of the texture keeps the original's brightness. Hand-written for the far-plane depth trick.
Shader "GoF2/Backdrop"
{
    Properties
    {
        [NoScaleOffset] _MainTex ("Texture", 2D) = "white" {}
        [HDR] _Color ("Color", Color) = (1, 1, 1, 1)
        _Tint ("Added tint", Color) = (0, 0, 0, 0)
        _Mirror ("Mirror U", Float) = 0
        _CoreGlow ("Core glow (HDR on the near-white texels)", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 10
    }
    SubShader
    {
        Tags { "Queue" = "Transparent-100" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend [_SrcBlend] [_DstBlend]
        ZWrite Off
        ZTest LEqual
        Cull Off

        Pass
        {
            // Drawn by BackdropPass after the skybox, before the opaque texture copy (the cloak refracts it).
            Tags { "LightMode" = "GoF2Backdrop" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _Tint;
                float _Mirror;
                float _CoreGlow;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                #if UNITY_REVERSED_Z
                    o.positionCS.z = o.positionCS.w * 1e-6;
                #else
                    o.positionCS.z = o.positionCS.w * (1 - 1e-6);
                #endif
                o.uv = float2(_Mirror > 0.5 ? 1 - i.uv.x : i.uv.x, i.uv.y);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half4 t = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                half core = smoothstep(0.7, 1.0, max(t.r, max(t.g, t.b)));
                return half4((t.rgb + _Tint.rgb) * _Color.rgb * lerp(1.0, _CoreGlow, core), t.a * _Color.a);
            }
            ENDHLSL
        }
    }
}
