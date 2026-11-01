// GoF2/WarpWeb: the warp's vein web between the systems (Visuals.WarpSky): the blue web of the Khador Drive's animation, from
// its own sheet (khador_jump.png, _WarpArcs), mirrored round the flight, turning and running out. A layer of its own and no
// part of GoF2/SpaceSky, as the sky draws under the planets: this draws after them (GoF2/Backdrop, queue 2900 and up), added
// so it needs no order against the suns, at the far plane so all that has depth (ships, stations, asteroids) stays in front.
// A direction per vertex of a sphere about the camera, as a skybox, so VR's two eyes need nothing more.
Shader "GoF2/WarpWeb"
{
    SubShader
    {
        Tags { "Queue" = "Transparent-50" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend One One
        ZWrite Off
        ZTest LEqual
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            #include "Assets/Shaders/WarpWebCore.hlsl"

            float _WarpLevel, _WarpTime, _WarpKhador;
            float3 _WarpAxis;

            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 view : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings vert(Attributes i)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.view = i.positionOS.xyz;
                o.positionCS = TransformWorldToHClip(_WorldSpaceCameraPos + o.view);
                o.positionCS.z = UNITY_RAW_FAR_CLIP_VALUE * o.positionCS.w;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                return half4(WarpWeb(normalize(i.view), normalize(_WarpAxis), _WarpLevel, _WarpKhador, _WarpTime), 1);
            }
            ENDHLSL
        }
    }
}
