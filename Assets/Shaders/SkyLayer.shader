// GoF2/SkyLayer: the extra sky layers of Level::renderBG (Reference/research/space_backdrop.md, skybox layers): the planet
// ring sky (alpha), the storms and the supernova flares (additive) and the asteroid belt (alpha + LIGHT0, blend 8). They
// are camera-centred meshes drawn with the depth test off in the original; here their depth is pushed onto the far plane
// like GoF2/Backdrop, so painter's order comes from the render queue and every object stays in front.
// _Fade is the part animation's `extra` channel (0..1: RGB for additive layers, alpha otherwise), _UVOffset its `v5_0`
// UV scroll. _Lit (the belt) lights the texture by _LightDir / _LightColor plus _Ambient.
Shader "GoF2/SkyLayer"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        [HDR] _Color ("Color", Color) = (1, 1, 1, 1)
        _Fade ("Fade", Float) = 1
        _UVOffset ("UV offset", Vector) = (0, 0, 0, 0)
        _UseVertexColor ("Multiply vertex colour", Float) = 0
        _Lit ("Lit", Float) = 0
        _LightDir ("Light direction (toward the light)", Vector) = (0, 1, 0, 0)
        [HDR] _LightColor ("Light colour", Color) = (1, 1, 1, 1)
        _Ambient ("Ambient", Color) = (0.2, 0.2, 0.2, 1)
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
                float4 _MainTex_ST;
                half4 _Color;
                float _Fade;
                float4 _UVOffset;
                float _UseVertexColor;
                float _Lit;
                float4 _LightDir;
                half4 _LightColor;
                half4 _Ambient;
                float _SrcBlend, _DstBlend;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 normalWS : TEXCOORD1; half4 color : COLOR; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                #if UNITY_REVERSED_Z
                    o.positionCS.z = o.positionCS.w * 1e-6;
                #else
                    o.positionCS.z = o.positionCS.w * (1 - 1e-6);
                #endif
                // The texture moves toward +u as v5_0 grows (the supernova flares stream out of the supernova; added, they
                // streamed into it).
                o.uv = i.uv - _UVOffset.xy;
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                // The supernova flares fade out toward their edges through their vertex colours (0..0.5): the original
                // draws a mesh's colour array whenever it has one.
                o.color = _UseVertexColor > 0.5 ? i.color : half4(1, 1, 1, 1);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half4 t = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv) * _Color * i.color;
                if (_Lit > 0.5)
                {
                    // Two-sided: the camera sits inside the belt.
                    half ndl = abs(dot(normalize(i.normalWS), normalize(_LightDir.xyz)));
                    // The original's fixed-function lighting saturates at 1 (LDR): no HDR values, so no bloom haze.
                    t.rgb = saturate(t.rgb * (_LightColor.rgb * ndl + _Ambient.rgb));
                }
                if (abs(_DstBlend - 1) < 0.5) t.rgb *= _Fade;   // additive (ONE, ONE): the fade darkens
                else t.a *= _Fade;
                return t;
            }
            ENDHLSL
        }
    }
}
