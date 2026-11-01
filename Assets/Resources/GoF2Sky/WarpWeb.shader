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

            TEXTURE2D(_WarpArcs); SAMPLER(sampler_WarpArcs);
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
                float3 view = normalize(i.view), axis = normalize(_WarpAxis);
                float3 x = normalize(cross(axis, abs(axis.y) < 0.9 ? float3(0, 1, 0) : float3(1, 0, 0)));
                float3 y = cross(axis, x);
                float ring = acos(clamp(dot(view, axis), -1.0, 1.0)) / PI;   // 0 ahead, 1 behind
                float around = atan2(dot(view, y), dot(view, x)) / (2.0 * PI) + 0.5;
                float u = abs(frac(around * 3.0 + _WarpTime * 0.06) * 2.0 - 1.0);
                float v = abs(frac(ring * 2.2 - _WarpTime * 0.3) * 2.0 - 1.0);
                half3 web = SAMPLE_TEXTURE2D_LOD(_WarpArcs, sampler_WarpArcs, float2(0.47 + u * 0.19, 0.62 + v * 0.18), 0).rgb;
                web *= smoothstep(0.03, 0.14, ring) * (1.0 - smoothstep(0.32, 0.62, ring));
                return half4(web * smoothstep(0.02, 0.4, ring) * _WarpLevel * 1.4 * _WarpKhador, 1);
            }
            ENDHLSL
        }
    }
}
