// GoF2/NebulaCloud: the local nebula clouds (World.NebulaClouds: Options > Graphics "Nebula clouds", and the "Skies" styles
// EVERSPACE 2 style / Combined). Remake, 2026-11-14, the user: EVERSPACE 2's sky methods in the base game, "a closer
// approximation" from its files. As ES2's local nebulae draw their pieces (M_Nebula_Local_Parent, its compiled shader and its
// parameters' defaults from its CachedExpressionData): each piece a flat sprite at a random turn; the mask's channel at uv x 0.3
// x its scale x (0.25 + 0.75 x random.x), offset by random.zw, and at twice that, the smaller of the two; its colour the
// cloud's own where dense, the system colour at its thin edges (1 - radial x mask through Schlick's bias ColorBias 0.1 and the
// contrast curve ColorContrast 4); its opacity the mask through Bias 0.05, ^Exponent 2, Contrast 1, x Density, faded edge-on
// (FresnelFadeMin 0.3) and near the camera; toward the sun its colour turns to the sun's (^32). ES2 lights them by Brightness
// 10000 against its stars' 10 under its auto exposure; here each cloud's light (its vertex colour) is set so its mean over
// its disc is its target (World.NebulaClouds.PieceMeanOpacity): ES2's exposure, per cloud.
// Added: the radial falloff squared (linear, a piece's round edge showed once its light saturated); a piece fades within its
// own size of the camera (a piece km wide filled the view as a disc); a piece past the level's
// far plane (15 km) drawn at 95 % of it in its own direction (the distant clouds keep their size on the sky); premultiplied
// (light added, some of what is behind covered), the pieces unsorted.
// Vertex data: uv the sprite's corner, uv1 its random values, uv2.x its size, uv3 its thin edges' colour (a: 1, set), normal
// its facing, colour its tint times its light (a: its mask channel / 2).
Shader "GoF2/NebulaCloud"
{
    Properties
    {
        [NoScaleOffset] _Masks ("Cloud masks (r g b)", 2D) = "black" {}
        _Intensity ("Intensity", Float) = 1
        _Absorb ("Covers what is behind", Float) = 1
        _Tiling ("Mask tiling", Float) = 2.5
        _Exponent ("Exponent", Float) = 2
        _Bias ("Bias", Float) = 0.05
        _Contrast ("Contrast", Float) = 1
        _ColorBias ("Colour bias", Float) = 0.1
        _ColorContrast ("Colour contrast", Float) = 4
        _Density ("Density", Float) = 1
        _SysColor ("System colour (thin edges)", Color) = (1, 1, 1, 1)
        _FresnelMin ("Edge-on fade", Float) = 0.3
        _NearFade ("Near fade (m)", Float) = 400
        _SunScatter ("Toward the sun", Float) = 1.5
        _SunDir ("Toward the sun (world)", Vector) = (0, 0, 1, 0)
        _SunColor ("Sun colour", Color) = (1, 1, 1, 1)
    }
    SubShader
    {
        Tags { "Queue" = "Transparent-50" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend One OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_Masks); SAMPLER(sampler_Masks);
            CBUFFER_START(UnityPerMaterial)
                float _Intensity, _Absorb, _Tiling, _Exponent, _Bias, _Contrast, _ColorBias, _ColorContrast, _Density, _FresnelMin, _NearFade, _SunScatter;
                float4 _SunDir, _SunColor, _SysColor;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; float4 rnd : TEXCOORD1; float2 size : TEXCOORD2; float4 edge : TEXCOORD3; float4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float4 rnd : TEXCOORD1; float3 normalWS : TEXCOORD2;
                float4 view : TEXCOORD3; float size : TEXCOORD4; float4 edge : TEXCOORD5; float4 color : COLOR; UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes i)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 w = TransformObjectToWorld(i.positionOS.xyz);
                float3 v = w - _WorldSpaceCameraPos;
                float dist = length(v);
                float lim = _ProjectionParams.z * 0.95;
                if (dist > lim) w = _WorldSpaceCameraPos + v * (lim / dist);   // past the far plane: drawn just inside it
                o.positionCS = TransformWorldToHClip(w);
                o.uv = i.uv;
                o.rnd = i.rnd;
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.view = float4(v / max(dist, 1e-3), dist);
                o.color = i.color;
                o.size = i.size.x;
                o.edge = i.edge;
                return o;
            }

            half Channel(half3 t, half c) { return c < 0.25 ? t.r : c < 0.75 ? t.g : t.b; }

            // M_Nebula_Local_Parent's curves: Schlick's bias (its preshader passes 1 / bias - 2) and its two-sided contrast curve
            float Bias(float x, float b) { return x / ((1.0 - x) * (1.0 / max(b, 1e-4) - 2.0) + 1.0); }
            float Contrast(float x, float c)
            {
                float lo = x <= 0 ? 0 : pow(saturate(2.0 * x), c) * 0.5;
                float up = 1.0 - pow(max(1.0 - saturate(2.0 * (x - 0.5)), 1e-6), c);
                return lerp(lo, up * 0.5 + 0.5, saturate(up * 500.0));
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float radial = max(1.0 - 2.0 * length(i.uv - 0.5), 0.0);
                if (radial <= 0) return 0;
                // ES2: the mask's channel at uv x 0.3 x its scale x (0.25 + 0.75 x random.x), offset by random.zw, and twice that, the smaller
                float2 t = i.uv * 0.3 * _Tiling * (0.25 + 0.75 * i.rnd.x) + i.rnd.zw;
                half ch = i.color.a;
                half m = min(Channel(SAMPLE_TEXTURE2D(_Masks, sampler_Masks, t).rgb, ch), Channel(SAMPLE_TEXTURE2D(_Masks, sampler_Masks, t * 2.0 + i.rnd.y).rgb, ch));
                // its colour: the cloud's own where it is dense, the system's at its thin edges (ColorBias, ColorContrast)
                float tone = Contrast(Bias(1.0 - radial * m, _ColorBias), _ColorContrast);
                // (the vertex colour carries the cloud's light, set per cloud: World.NebulaClouds; the system colour scaled to it)
                float light = max(max(i.color.r, i.color.g), max(i.color.b, 1e-6));
                // the thin edges' colour: the cloud's own second hue (World.NebulaClouds), else the sky's main colour
                float3 edgeHue = i.edge.a > 0.5 ? i.edge.rgb : _SysColor.rgb;
                float3 col = lerp(i.color.rgb, edgeHue * light, tone);
                // its opacity: the mask through Bias, ^Exponent, Contrast, x Density, faded round its edge
                // (the radial falloff squared: linear, a piece's round edge showed once its light saturated)
                float dens = Contrast(pow(max(Bias(m, _Bias), 0.0), _Exponent), _Contrast) * radial * radial * _Density;
                float facing = abs(dot(normalize(i.normalWS), i.view.xyz));
                dens *= saturate((facing - _FresnelMin) / max(1.0 - _FresnelMin, 1e-3));   // edge-on: gone (FresnelFadeMin)
                // near the camera: gone within its own size (a piece km wide filled the view as a disc), and within _NearFade
                dens *= saturate((i.view.w - 0.6 * i.size) / max(0.8 * i.size, 1.0)) * saturate((i.view.w - _NearFade) / max(_NearFade, 1.0));
                // forward scattering toward the sun (^32), toward its colour
                float toSun = saturate((dot(i.view.xyz, _SunDir.xyz) + 1.0) * 0.5);
                col = lerp(col, _SunColor.rgb * max(max(col.r, col.g), col.b), pow(toSun, 32.0) * _SunScatter * 0.5);
                return half4(col * dens * _Intensity, saturate(dens * _Absorb));   // (premultiplied: it covers some of what is behind)
            }
            ENDHLSL
        }
    }
}
