// GoF2/ModGlow: a mod model's additive glow layer (ships.json / stations.json / shipkits.json "materials" with
// "additive": true): light added to what is behind it, unlit, like the game's own _add layers (GoF2/Additive), its
// texture (_EmissionMap, rgb) x _EmissionColor (HDR: above 1 it blooms) x the mesh's vertex colours (_UseVertexColor),
// the texture scrolling by _Scroll (UV units a second). Galaxy on Fire 3's gate glows, rings and electricity (its
// BlendMode 2 materials: DiffuseColor x Opacity x the vertex colours, UVAnim_Speed x UVAnim_SpeedMultiplier).
Shader "GoF2/ModGlow"
{
    Properties
    {
        [HDR] _EmissionColor ("Colour", Color) = (1, 1, 1, 1)
        _EmissionMap ("Texture", 2D) = "white" {}
        _Scroll ("Scroll (UV / s)", Vector) = (0, 0, 0, 0)
        [Toggle] _UseVertexColor ("Vertex colours", Float) = 1
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" "RenderPipeline" = "UniversalPipeline" }
        Blend One One
        ZWrite Off
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_EmissionMap); SAMPLER(sampler_EmissionMap);
            CBUFFER_START(UnityPerMaterial)
                half4 _EmissionColor;
                float4 _EmissionMap_ST;
                float4 _Scroll;
                float _UseVertexColor;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings vert(Attributes i)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.uv = TRANSFORM_TEX(i.uv, _EmissionMap) + _Scroll.xy * _Time.y;
                o.color = _UseVertexColor > 0.5 ? i.color : half4(1, 1, 1, 1);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                half4 t = SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, i.uv);
                return half4(t.rgb * t.a * _EmissionColor.rgb * i.color.rgb * i.color.a, 0);
            }
            ENDHLSL
        }
    }
}
