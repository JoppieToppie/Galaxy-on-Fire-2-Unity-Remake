// GoF2/ModEngineMesh: one part of a Galaxy on Fire 3 engine effect drawn as GoF3 draws it (EngineModel, EngineFx). GoF3
// compiles a shader variant per material from one effect shader (romfs shaders/, e.g. shader_1899022019 for the capital
// engine's body); the options it compiles in are flags here, the formulas its GLSL's:
//   vertex: the vertex wobble (pos += strength N sin((t + y) freq) a; the vertex alpha then 1); a sprite's corners pushed
//     out in view space by its offset x the object's scale (|M (1,1,1)/sqrt 3|); the angle blend from N.E (E = from the camera to
//     the vertex; in world space, GoF3's in the object's): |N.E|, clamp(-N.E) or N.E, remapped Min..Max; pulsing |sin(t f)| clamped
//     Min..Max; UV scrolling fract(speed t) on the texture or on the flow map's coordinates (x the flow tiling);
//   fragment: the diffuse texture (pushed by the flow map's rg x2-1 x strength x 0.1) or the DiffuseColor; its alpha x its
//     luminance; its colour the DiffuseColor; x the DiffuseColor; x Opacity x the vertex colour; colourise 1 (x mix(C1, C2,
//     r) x M) or 2 (rgb *= rgb + mix(C1, C2, r) (1 - rgb) M, x its alpha), C1 the vertex colour in some; alpha x the angle
//     blend, x the pulse (colour too), x Alpha_Value (_Level: the engine's on / off fade and the "off" animation's alpha).
// GoF3 does this in gamma space into an 8-bit frame buffer, blending SrcAlpha / One (BlendMode 2) or alpha (1): the result
// is clamped to 1 and turned to linear here (the textures are read raw, as GoF3 reads them), x _Glow for the bloom.
// _Intensity: GoF3's plumes and flares come out brighter than its formula gives into an 8-bit frame (the Argus' exhaust
// peaks near white in its ship viewer, the formula at a third of that; its bloom, presumably, which none of its effect
// shaders holds): the fragment's addition x this before the clamp, per part (EnginePart.intensity, the importer's table
// matched against GoF3's ship viewer: the Argus' plume colour and length, the Moornta's flare, plume and helix, the
// Shamash's flare and plumes), 4 by default; the Fork particles keep 1 (their own gain below).
// _Tint (an exhaust style's colour, EngineFx: per renderer; alpha 0 = none): each pixel's hue replaced by it, its white
// kept (its smallest channel stays, the rest of its brightness takes the colour), before the intensity and the clamp.
// _SpriteBias: a sprite (the glare) drawn this far toward the camera (its own units, x the object's scale), so the rim of the
// nozzle it sits in, nearer the camera than its centre seen at an angle, doesn't cut a dark crescent out of it (the game's
// ships wearing a GoF3 exhaust style: the Betty's and the Phantom's housings); about a GoF3 nozzle's radius.
// _VertexGain: uv1.x scales a fragment's linear addition (EngineFx's Fork particles: N overlapping additions of x make about
// N x in GoF3's gamma frame but only N^0.45 x added in linear, so each takes N^1.2, N its overlap along the wake).
// _SteerBend / _BendOrigin / _BendAxis: the remake's (GoF3's engines stay rigid on the hull; the user's request, 2026-11-04,
// "bend them like ES2's"): EVERSPACE 2's steering bend (GoF2/ModEngineFlame), each vertex moved u^2 x _SteerBend x L in
// world space, u its share 0..1 of the way from the nozzle (_BendOrigin) along the exhaust (_BendAxis.xyz) to its tip
// (_BendAxis.w = L, metres); a sprite moved with its centre. Per exhaust (EngineFx); L 0 = none.
Shader "GoF2/ModEngineMesh"
{
    Properties
    {
        _MainTex ("Diffuse", 2D) = "white" {}
        _FlowMap ("Flow map", 2D) = "grey" {}
        _Diffuse ("DiffuseColor", Color) = (1, 1, 1, 1)
        _C1 ("Colorize_Color1", Color) = (1, 1, 1, 1)
        _C2 ("Colorize_Color2", Color) = (1, 1, 1, 1)
        _Opacity ("Opacity", Float) = 1
        _ColorizeMul ("Colorize_Multiplier", Float) = 1
        _AngleMin ("AngleBlend_MinAngle", Float) = 0
        _AngleMax ("AngleBlend_MaxAngle", Float) = 1
        _PulseMin ("Pulsate_Min", Float) = 0
        _PulseMax ("Pulsate_Max", Float) = 1
        _PulseFreq ("Pulsate_Frequency", Float) = 1
        _UVSpeed ("UVAnim_Speed x multiplier", Vector) = (0, 0, 0, 0)
        _FlowTiling ("UVAnim_FlowTiling", Float) = 1
        _FlowStrength ("UVAnim_FlowStrength", Float) = 0
        _VAFreq ("VertexAnimation_Frequenz", Float) = 0
        _VAStrength ("VertexAnimation_Strength", Float) = 0
        _Alpha ("Alpha_Value", Float) = 1
        _Glow ("Glow", Float) = 1
        _Level ("Level", Float) = 1
        _Sprite ("Sprite", Float) = 0
        _UseTex ("Texture", Float) = 1
        _LumAlpha ("Luminance alpha", Float) = 0
        _RgbFromDc ("DiffuseColor as colour", Float) = 0
        _MulDc ("x DiffuseColor", Float) = 0
        _Flow ("Flow map", Float) = 0
        _ScrollDirect ("Scroll the texture", Float) = 0
        _ScrollFlow ("Scroll the flow", Float) = 0
        _Colorize ("Colorize", Float) = 0
        _ColorizeVtx ("Colorize from the vertex colour", Float) = 0
        _Angle ("Angle blend", Float) = 0
        _Pulsate ("Pulsate", Float) = 0
        _VertexAnim ("Vertex animation", Float) = 0
        _OpaqueVtxAlpha ("Vertex alpha 1", Float) = 0
        _Additive ("Additive", Float) = 1
        _VertexGain ("Gain in uv1.x", Float) = 0
        _SpriteBias ("Sprite toward the camera", Float) = 1.6
        _Intensity ("Intensity", Float) = 4
        _Tint ("Recolour", Color) = (0, 0, 0, 0)
        _SteerBend ("Steering bend: the tip's offset, world space, x the length (EngineFx)", Vector) = (0, 0, 0, 0)
        _BendOrigin ("Steering bend: the nozzle, world space", Vector) = (0, 0, 0, 0)
        _BendAxis ("Steering bend: along the exhaust (xyz), its length (w)", Vector) = (0, 0, 1, 0)
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst blend", Float) = 1
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 0
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" "RenderPipeline" = "UniversalPipeline" }
        Blend [_SrcBlend] [_DstBlend]
        ZWrite Off
        ZTest LEqual
        Cull [_Cull]

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D(_FlowMap); SAMPLER(sampler_FlowMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST, _FlowMap_ST;
                float4 _Diffuse, _C1, _C2, _UVSpeed, _Tint, _SteerBend, _BendOrigin, _BendAxis;
                float _Opacity, _ColorizeMul, _AngleMin, _AngleMax, _PulseMin, _PulseMax, _PulseFreq, _FlowTiling, _FlowStrength;
                float _VAFreq, _VAStrength, _Alpha, _Glow, _Level;
                float _Sprite, _UseTex, _LumAlpha, _RgbFromDc, _MulDc, _Flow, _ScrollDirect, _ScrollFlow, _Colorize, _ColorizeVtx;
                float _Angle, _Pulsate, _VertexAnim, _OpaqueVtxAlpha, _Additive, _SrcBlend, _DstBlend, _Cull, _VertexGain, _Intensity, _SpriteBias;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                float2 offset : TEXCOORD1;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 uvFlow : TEXCOORD1;
                float4 color : COLOR;
                float angle : TEXCOORD2;
                float pulse : TEXCOORD3;
                float fog : TEXCOORD4;
                float gain : TEXCOORD5;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes i)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float t = _Time.y;
                float3 pos = i.positionOS.xyz;
                float4 col = i.color;
                if (_VertexAnim > 0.5) pos += _VAStrength * i.normalOS * (sin((t + pos.y) * _VAFreq) * i.color.a);
                if (_OpaqueVtxAlpha > 0.5) col.a = 1.0;
                o.color = col;
                float2 speed = _UVSpeed.xy;
                o.uv = i.uv + (_ScrollDirect > 0.5 ? frac(speed * t) : 0.0);
                o.uvFlow = i.uv * _FlowTiling + (_ScrollFlow > 0.5 ? frac(speed * t) : 0.0);
                float3 world = TransformObjectToWorld(pos);   // (a sprite's: its centre, the corners pushed out below)
                if (_BendAxis.w > 0)
                {
                    float u = saturate(dot(world - _BendOrigin.xyz, _BendAxis.xyz) / _BendAxis.w);
                    world += u * u * _SteerBend.xyz * _BendAxis.w;
                }
                if (_Sprite > 0.5)
                {
                    float3 vs = TransformWorldToView(world);
                    float scale = length(mul((float3x3)UNITY_MATRIX_M, float3(0.57735027, 0.57735027, 0.57735027)));
                    vs.xy += scale * i.offset;
                    vs.z += scale * _SpriteBias;   // (view space: +z toward the camera)
                    o.positionCS = TransformWViewToHClip(vs);
                }
                else o.positionCS = TransformWorldToHClip(world);
                // N.E in world space: the same as GoF3's object-space one under a uniform scale, and right under EngineFx's
                // stretch along the exhaust (ModExhausts.EngineLength), which skewed the object-space angle so far that the
                // flare and the body faded out seen from a little to the side.
                float3 nWS = normalize(TransformObjectToWorldNormal(i.normalOS));
                float nde = dot(nWS, normalize(world - GetCameraPositionWS()));
                float a = 1.0;
                if (_Angle > 0.5)
                {
                    a = _Angle < 1.5 ? abs(nde) : _Angle < 2.5 ? saturate(-nde) : nde;
                    a = saturate((a - _AngleMin) / max(1e-4, _AngleMax - _AngleMin));
                }
                o.angle = a;
                o.pulse = _Pulsate > 0.5 ? clamp(abs(sin(t * _PulseFreq)), _PulseMin, _PulseMax) : 1.0;
                o.fog = ComputeFogFactor(o.positionCS.z);
                o.gain = _VertexGain > 0.5 ? i.offset.x : 1.0;
                return o;
            }

            float3 ToLinear(float3 c) { return pow(max(c, 0.0), 2.2); }   // GoF3's gamma frame -> the remake's linear one

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float2 tc = i.uv;
                if (_Flow > 0.5) tc += (SAMPLE_TEXTURE2D(_FlowMap, sampler_FlowMap, i.uvFlow).rg * 2.0 - 1.0) * _FlowStrength * 0.1;
                float4 c;
                if (_UseTex > 0.5)
                {
                    c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, tc);
                    if (_LumAlpha > 0.5) c.a *= dot(c.rgb, float3(0.3, 0.59, 0.11));
                    if (_RgbFromDc > 0.5) c.rgb = _Diffuse.rgb;
                    if (_MulDc > 0.5) c *= _Diffuse;
                }
                else c = _Diffuse;
                c *= _Opacity;
                c *= i.color;
                if (_Colorize > 0.5)
                {
                    float4 cc = lerp(_ColorizeVtx > 0.5 ? i.color : _C1, _C2, c.r);
                    if (_Colorize < 1.5) c *= cc * _ColorizeMul;
                    else { c.rgb *= c.rgb + cc.rgb * (1.0 - c.rgb) * _ColorizeMul; c.rgb *= cc.a; }
                }
                c.a *= i.angle;
                c *= i.pulse;
                if (_Tint.a > 0.5)
                {
                    float hi = max(c.r, max(c.g, c.b)), lo = min(c.r, min(c.g, c.b));
                    float3 hue = _Tint.rgb / max(1e-4, max(_Tint.r, max(_Tint.g, _Tint.b)));
                    c.rgb = lo + (hi - lo) * hue;
                }
                c.a *= _Alpha * _Level;
                if (_Additive > 0.5)
                {
                    // GoF3: SrcAlpha / One into an 8-bit gamma frame buffer: the fragment's addition, clamped, in linear
                    float3 add = ToLinear(saturate(c.rgb * saturate(c.a) * _Intensity)) * _Glow * i.gain;
                    return half4(MixFogColor(add, float3(0, 0, 0), i.fog), 0);
                }
                float3 rgb = ToLinear(saturate(c.rgb)) * _Glow;
                return half4(MixFog(rgb, i.fog), saturate(c.a));
            }
            ENDHLSL
        }
    }
}
