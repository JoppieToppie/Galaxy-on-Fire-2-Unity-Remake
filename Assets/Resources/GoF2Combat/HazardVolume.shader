// GoF2/HazardVolume: the view from inside an orbit's hazard volume (World.HazardLooks; stations.json "orbit.hazards"),
// EVERSPACE 2's fields' post-process materials as their compiled pixel shaders compute them (es2_re fxmat, ShaderDump +
// disasm), added over the frame. Drawn by the volume's sphere (Unity's primitive, radius 0.5) from inside only (Cull Front,
// ZTest Always: over everything while the camera is within it; nothing from outside):
//   _Mode 1  M_Condition_Frozen_PostProcess: the vignette (length(uv - 0.5))^1.5 through the contrast curve 3, times the
//            frost texture revealed by Opacity (saturate((f - (1 - Opacity)) / Opacity): its brightest crystals first),
//            times f^2; T_Grunge_Frost_001_C's R is procedural here (ice cells and a fine grain); _Amount = Opacity
//   _Mode 2  M_Condition_Radiated_PostProcess: the edges' glow (the lower of two T_Noise_010_M layers at uv / 2 scrolling
//            apart at time x 2, raised to 1 / the distance from the centre) x RadiationColor x 5 x Opacity^1.5; a grain
//            (a hash per 1/500 of the screen) x 0.3 x distance^3 x Opacity; and sparse blobs (three octaves of gradient
//            noise at uv x 2 and the time, saturate(n x 4 - 3)^3) x RadiationColor x Opacity^2; _Amount = Opacity
Shader "GoF2/HazardVolume"
{
    Properties
    {
        _Color ("Color", Color) = (1, 1, 0.39, 1)
        _Mode ("Mode (1 frost, 2 radiation)", Float) = 1
        _Amount ("Amount (ES2's Opacity)", Float) = 0
    }
    SubShader
    {
        Tags { "Queue" = "Transparent+50" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        ZWrite Off
        ZTest Always
        Cull Front
        Blend One One

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _Mode, _Amount;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                return o;
            }

            float Hash(float2 p) { p = frac(p * float2(0.3183099, 0.3678794)) * 17.0; return frac(p.x * p.y * (p.x + p.y)); }

            float Noise(float2 x)
            {
                float2 i = floor(x), f = frac(x);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), f.x), lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), f.x), f.y);
            }

            float3 Hash3(float3 p)
            {
                p = float3(dot(p, float3(127.1, 311.7, 74.7)), dot(p, float3(269.5, 183.3, 246.1)), dot(p, float3(113.5, 271.9, 124.6)));
                return frac(sin(p) * 43758.5453) * 2.0 - 1.0;
            }

            float Gradient(float3 p)
            {
                float3 i = floor(p), f = frac(p);
                float3 u = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);
                return lerp(lerp(lerp(dot(Hash3(i), f), dot(Hash3(i + float3(1, 0, 0)), f - float3(1, 0, 0)), u.x),
                                 lerp(dot(Hash3(i + float3(0, 1, 0)), f - float3(0, 1, 0)), dot(Hash3(i + float3(1, 1, 0)), f - float3(1, 1, 0)), u.x), u.y),
                            lerp(lerp(dot(Hash3(i + float3(0, 0, 1)), f - float3(0, 0, 1)), dot(Hash3(i + float3(1, 0, 1)), f - float3(1, 0, 1)), u.x),
                                 lerp(dot(Hash3(i + float3(0, 1, 1)), f - float3(0, 1, 1)), dot(Hash3(i + float3(1, 1, 1)), f - float3(1, 1, 1)), u.x), u.y), u.z);
            }

            // the distance to the nearest of a jittered grid's points: ice cells
            float Cells(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float d = 1.0;
                for (int y = -1; y <= 1; y++)
                    for (int x = -1; x <= 1; x++)
                    {
                        float2 g = float2(x, y);
                        float2 o = float2(Hash(i + g), Hash(i + g + 17.0));
                        d = min(d, length(g + o - f));
                    }
                return d;
            }

            // the masters' contrast curve: 0.5 (2x)^k below a half, 1 - 0.5 (2 - 2x)^k above
            float Contrast(float x, float k)
            {
                float hi = 1.0 - pow(saturate(1.0 - 2.0 * (x - 0.5)), k);
                return hi > 0.0 ? 0.5 + 0.5 * hi : 0.5 * pow(saturate(2.0 * x), k);
            }

            // 1 with the camera inside the sphere (object radius 0.5), else 0
            float CameraInside() { return step(length(TransformWorldToObject(_WorldSpaceCameraPos)), 0.5); }

            half4 frag(Varyings i) : SV_Target
            {
                float inside = CameraInside();
                float2 uv = i.positionCS.xy / _ScreenParams.xy;
                float aspect = _ScreenParams.x / _ScreenParams.y;
                float2 tuv = uv * float2(aspect, 1.0);
                float d = length(uv - 0.5);
                float opacity = max(_Amount, 1e-4);
                float3 c;
                if (_Mode < 1.5)
                {
                    float vignette = Contrast(pow(d, 1.5), 3.0);
                    float f = saturate(1.15 - Cells(tuv * 7.0) * 1.1) * 0.75 + Noise(tuv * 160.0) * 0.25;   // (the frost texture's R)
                    float reveal = saturate((f - (1.0 - opacity)) / opacity);
                    c = _Color.rgb * vignette * reveal * f * f * step(1e-3, _Amount);
                }
                else
                {
                    float t = _Time.y;
                    float dd = min(d, 1.0);
                    float n = min(Noise((uv * 0.5 + float2(t * 2.0, 0.0)) * 8.0), Noise((uv * 0.5 + 0.5 + float2(-t * 2.0, t * 0.3)) * 8.0));
                    float3 glow = pow(max(n, 1e-4), 1.0 / max(dd, 1e-3)) * _Color.rgb * 5.0;
                    float grain = (Hash(floor(float2(tuv.x, uv.y) * 500.0) + floor(t * 60.0)) * 2.0 - 1.0) * 0.3 * dd * dd * dd * opacity;
                    float3 p = float3(tuv * 2.0, t);
                    float blobs = Gradient(p) + Gradient(p * 2.0) * 0.5 + Gradient(p * 4.0) * 0.25;
                    blobs = pow(saturate((blobs * 0.5 + 0.5) * 4.0 - 3.0), 3.0);
                    c = glow * pow(opacity, 1.5) + grain + blobs * _Color.rgb * opacity * opacity;
                }
                return half4(max(c, 0.0) * inside, 1);
            }
            ENDHLSL
        }
    }
}
