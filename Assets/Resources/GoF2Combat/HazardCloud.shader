// GoF2/HazardCloud: the clouds of an orbit's hazard volumes (World.HazardLooks), EVERSPACE 2's field cloud materials as their
// compiled pixel shaders compute them (es2_re fxmat, ShaderDump + disasm), on billboard particles: additive, the particle's
// colour (Unity's vertex colour) x the pattern x a soft disc; its centre (the Center vertex stream) seeds the pattern. The
// noise textures ES2 samples (T_Noise_009_M, T_Pattern_Dots_002, T_Noise_013_M) are procedural here.
//   _Mode 0  M_CorrosionField_Cloud: two octaves of |gradient noise| at (uv x 3, 3 x (time x 0.2 + seed + the noise
//            texture's half)), to the power 1.5 then squared; the disc max(1 - 2 r, 0)
//   _Mode 1  M_RadiationField_Cloud: one octave of simplex noise at (uv x 20, time x 8), sharpened (saturate x 10), over the
//            dots pattern (uv x 0.5), in a disc wobbled by a noise (r / (0.5 x n^0.2)); + the disc x 0.003
//   _Mode 2  M_WebField_Plasma: a noise flowed by two scrolling noises (uv x 2, uv x 8, time x 0.2), squared, raised to
//            1 / disc^Exp (3): wisps sharp at the centre, gone at the rim
Shader "GoF2/HazardCloud"
{
    Properties
    {
        _Mode ("Mode (0 turbulence, 1 dots, 2 wisps)", Float) = 0
        _Glow ("Glow", Float) = 1
        _DotCount ("Dots across (mode 1)", Float) = 16
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" "PreviewType" = "Plane" }
        ZWrite Off
        Cull Off
        Blend One One

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Mode, _Glow, _DotCount;
            CBUFFER_END

            // streams (HazardLooks): Position, Color, UV, Center -> TEXCOORD0 = uv.xy + center.xy, TEXCOORD1.x = center.z
            struct Attributes { float4 positionOS : POSITION; float4 color : COLOR; float4 uvCentre : TEXCOORD0; float centreZ : TEXCOORD1; };
            struct Varyings { float4 positionCS : SV_POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; float seed : TEXCOORD1; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.color = i.color;
                o.uv = i.uvCentre.xy;
                float3 c = float3(i.uvCentre.zw, i.centreZ);
                o.seed = frac(sin(dot(c, float3(12.9898, 78.233, 37.719))) * 43758.5453);
                return o;
            }

            float3 Hash3(float3 p)
            {
                p = float3(dot(p, float3(127.1, 311.7, 74.7)), dot(p, float3(269.5, 183.3, 246.1)), dot(p, float3(113.5, 271.9, 124.6)));
                return frac(sin(p) * 43758.5453) * 2.0 - 1.0;
            }

            // gradient noise in -1..1 (UE's fast gradient noise stands in)
            float Gradient(float3 p)
            {
                float3 i = floor(p), f = frac(p);
                float3 u = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);
                return lerp(lerp(lerp(dot(Hash3(i), f), dot(Hash3(i + float3(1, 0, 0)), f - float3(1, 0, 0)), u.x),
                                 lerp(dot(Hash3(i + float3(0, 1, 0)), f - float3(0, 1, 0)), dot(Hash3(i + float3(1, 1, 0)), f - float3(1, 1, 0)), u.x), u.y),
                            lerp(lerp(dot(Hash3(i + float3(0, 0, 1)), f - float3(0, 0, 1)), dot(Hash3(i + float3(1, 0, 1)), f - float3(1, 0, 1)), u.x),
                                 lerp(dot(Hash3(i + float3(0, 1, 1)), f - float3(0, 1, 1)), dot(Hash3(i + float3(1, 1, 1)), f - float3(1, 1, 1)), u.x), u.y), u.z);
            }

            float Value(float2 p) { return Gradient(float3(p, 0.37)) * 0.5 + 0.5; }

            half4 frag(Varyings i) : SV_Target
            {
                float t = _Time.y;
                float r = length(i.uv - 0.5);
                float disc = max(1.0 - 2.0 * r, 0.0);
                float v;
                if (_Mode < 0.5)
                {
                    // corrosion: turbulence^1.5 squared, in the disc
                    float3 p = float3(i.uv, t * 0.2 + i.seed * 10.0 + (1.0 - Value(i.uv * 0.5)) * 0.5) * 3.0;
                    float n = abs(Gradient(p)) + abs(Gradient(p * 2.0)) * 0.5;
                    n = pow(max(n, 1e-4), 1.5);
                    v = n * n * disc;
                }
                else if (_Mode < 1.5)
                {
                    // radiation: dots where the sharp noise peaks, in a wobbly disc
                    float s = Gradient(float3(i.uv * 20.0 + i.seed * 31.0, t * 8.0));
                    float blobs = saturate(s * 10.0);
                    float2 cell = frac(i.uv * _DotCount) - 0.5;
                    float dots = smoothstep(0.32, 0.18, length(cell));
                    float wobble = max(0.5 * pow(max(Value(i.uv + 1.0 + i.seed), 1e-4), 0.2), 1e-4);
                    float d = max(1.0 - r / wobble, 0.0);
                    v = dots * blobs * d + d * 0.003;
                }
                else
                {
                    // frost: flowed noise wisps, sharp at the centre
                    float2 flow = float2(Gradient(float3(i.uv * 2.0 + float2(t * 0.2, 0), i.seed)), Gradient(float3(i.uv * 8.0 + float2(t * 0.2, 0), i.seed + 5.0))) * 0.2;
                    float n = Value((i.uv + float2(-t * 0.2, 0) + 1.0 + flow) * 3.0 + i.seed * 7.0);
                    n *= n;
                    float e = 1.0 / max(pow(disc, 3.0), 1e-3);
                    v = disc > 0.0 ? pow(max(n, 1e-4), e) : 0.0;
                }
                float3 c = i.color.rgb * i.color.a * v * _Glow;
                return half4(c, 1);
            }
            ENDHLSL
        }
    }
}
