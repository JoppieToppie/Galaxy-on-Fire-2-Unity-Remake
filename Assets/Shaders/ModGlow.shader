// GoF2/ModGlow: a mod model's additive glow layer (ships.json / stations.json / shipkits.json "materials" with
// "additive": true): light added to what is behind it, unlit, like the game's own _add layers (GoF2/Additive), its
// texture (_EmissionMap, rgb) x _EmissionColor (HDR: above 1 it blooms) x the mesh's vertex colours (_UseVertexColor),
// the texture scrolling by _Scroll (UV units a second). Galaxy on Fire 3's gate glows, rings and electricity (its
// BlendMode 2 materials: DiffuseColor x Opacity x the vertex colours, UVAnim_Speed x UVAnim_SpeedMultiplier).
// GoF3's effect terms (ModMaterials.GlowTerms, all off by default), from its effect vertex shaders: the angle fade (_FxAngle:
// on, min, max, absolute; |N.E| with E the eye's direction, faded in between min and max: AngleBlend, spotlight cones and
// glows that vanish edge-on), the pulse (_FxPulse: frequency, step threshold, min, max; _FxPulseMode: on, step, smooth,
// vertex offset: |sin((t [+ vertex red]) x frequency)|, [step(threshold)], then smoothstep into min..max or clamped to
// them, x the whole colour: Pulsate), the billboard (_Billboard: each vertex at its sprite's centre, UV1 its corner's offset
// in view space x the object's scale, length(MV (1, 1, 1) / sqrt 3): GoF3's sprites, which face the camera).
Shader "GoF2/ModGlow"
{
    Properties
    {
        [HDR] _EmissionColor ("Colour", Color) = (1, 1, 1, 1)
        _EmissionMap ("Texture", 2D) = "white" {}
        _Scroll ("Scroll (UV / s)", Vector) = (0, 0, 0, 0)
        [Toggle] _UseVertexColor ("Vertex colours", Float) = 1
        _FxAngle ("Angle fade (on, min, max, absolute)", Vector) = (0, 0, 1, 1)
        _FxPulse ("Pulse (frequency, threshold, min, max)", Vector) = (0, 0, 0, 1)
        _FxPulseMode ("Pulse mode (on, step, smooth, vertex offset)", Vector) = (0, 0, 0, 0)
        [Toggle] _Billboard ("Billboard sprites (UV1 = offset)", Float) = 0
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
                float4 _FxAngle;
                float4 _FxPulse;
                float4 _FxPulseMode;
                float _Billboard;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; float2 uv1 : TEXCOORD1; half4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; half fx : TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings vert(Attributes i)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 positionWS = TransformObjectToWorld(i.positionOS.xyz);
                if (_Billboard > 0.5)
                {
                    // GoF3: vs_pos.xy += length(localToView x (1, 1, 1) / sqrt 3) x a_pos2dOffset
                    float3 positionVS = TransformWorldToView(positionWS);
                    float scale = length(TransformObjectToWorldDir(float3(0.57735, 0.57735, 0.57735), false));
                    positionVS.xy += scale * i.uv1;
                    o.positionCS = TransformWViewToHClip(positionVS);
                }
                else o.positionCS = TransformWorldToHClip(positionWS);
                o.uv = TRANSFORM_TEX(i.uv, _EmissionMap) + _Scroll.xy * _Time.y;
                o.color = _UseVertexColor > 0.5 ? i.color : half4(1, 1, 1, 1);
                half fx = 1;
                if (_FxAngle.x > 0.5)
                {
                    float nde = dot(TransformObjectToWorldNormal(i.normalOS), normalize(positionWS - _WorldSpaceCameraPos));
                    if (_FxAngle.w > 0.5) nde = abs(nde);
                    fx *= saturate((nde - _FxAngle.y) / (_FxAngle.z - _FxAngle.y));
                }
                if (_FxPulseMode.x > 0.5)
                {
                    float p = abs(sin((_Time.y + (_FxPulseMode.w > 0.5 ? i.color.r : 0)) * _FxPulse.x));
                    if (_FxPulseMode.y > 0.5) p = step(_FxPulse.y, p);
                    p = _FxPulseMode.z > 0.5 ? smoothstep(0, 1, p) * (_FxPulse.w - _FxPulse.z) + _FxPulse.z : clamp(p, _FxPulse.z, _FxPulse.w);
                    fx *= p;
                }
                o.fx = fx;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                half4 t = SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, i.uv);
                return half4(t.rgb * t.a * _EmissionColor.rgb * i.color.rgb * i.color.a * i.fx, 0);
            }
            ENDHLSL
        }
    }
}
