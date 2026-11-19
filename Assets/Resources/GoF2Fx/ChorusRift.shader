// GoF2/ChorusRift: a mod material's "rift" block (ModMaterials.Rift; Chorus's rift vortex, M_PortalRift_2, from its compiled
// base pass; docs/notes/mods-models.md "Chorus rifts"). Unlit, added, both faces. In Unreal's UVs (v down; g = v, 1 at the
// vortex's middle on SM_Rift_Vortex, 0 at its rim):
//   a = Tiled at (u, g^mainPow) x main tiling + t x main pan; d = Tiled at (uv + detail offset) x detail tiling + t x its
//   pan, ^ detailPow x detailMul; the red centre r = a d g^9.
//   The bolts: two 1D pulses from Strip (t x bolt speed x pulse rate 1 / 2) multiplied; an N x N flipbook (frame t x sequence
//   time x N^2) of Lightning over UV0 and its CloudMask over UV1, x the pulses x brightness x lightning.
//   The clouds: Cloud (R) at (u, g^parallaxPow) x parallax tiling + t x its pan, pushed along the view in the tangent frame by
//   heightRatio x (Cloud (G) at uv + t x heightPan - 0.5) (a bump offset): n.
//   s = a d + n + r x redCenter + g^9; the colour lerp(outer, inner, saturate(s^blendExp x blendMult)) x s, + the core
//   (min(g^9^corePow, 1) x core x coreTint), + r x the bolts' colour, + r x (1 - n)^90 x brightness x the pulses x the mask x
//   lightningCloud, + (1 - saturate s) x Rock(uv + 0.3)^2 x Rock(uv) (both x the rock tiling + t x its pan) x background,
//   + n^cloudPow x clouds x g^2. Its opacity: D = min(g^gradation, 1); y = (D - (1 - saturate s) x rock + 1) x D;
//   saturate(y (1 + saturate(9 s x the colour's red))). Added: colour x opacity x exposure (Chorus's HDR to the remake's).
// The vertex shader's world position offset (2026-11-11, its VS: the vertex along its normal by T_Noise_Tiled04 (R) at uv x
// (ucoord_WP, Vcoord_WP) + t x (Upannel_WP, Vpannel_WP), x saturate(5 g (1 - g)) x WP_Strenth): _Wpo / _WpoUV / _WpoMap.
// _Mode 1, M_PortalRift_BOTTOM (the Rift seen from inside, LocRFA's vortex; its compiled base pass): s x Emmisive_Color for
// the inner / outer blend; the bolts and the cloud lightning added whole (not x r); the rock x Base_Color; the clouds
// n^cloudPow x the emissive colour x cloudMul (no g^2); the opacity's red term s x the emissive red; translucent (alpha
// blended: _SrcBlend / _DstBlend), its alpha the opacity x its depth fade (Dapthfade: what is behind within it) x its
// camera fade (Fade Offset, Fade Length). Its "cb2[0].x" factor on D (a parameter collection's) is taken as 1.
Shader "GoF2/ChorusRift"
{
    Properties
    {
        _Tiled ("Tiled (T_HeroRift_Tileable)", 2D) = "black" {}
        _Strip ("Pulse strip (T_NoiseStrip_B)", 2D) = "black" {}
        _Lightning ("Lightning flipbook", 2D) = "black" {}
        _CloudMask ("Cloud mask flipbook", 2D) = "black" {}
        _Cloud ("Cloud (R noise, G height)", 2D) = "black" {}
        _Rock ("Background rock", 2D) = "black" {}
        _MainUV ("Main: tiling u, v, pan u, v", Vector) = (1.5, 1, 0.01, 0.03)
        _DetailUV ("Detail: tiling u, v, pan u, v", Vector) = (1, 1.5, 0, 0.02)
        _ParUV ("Clouds: tiling u, v, pan u, v", Vector) = (2, 1, 0.018, 0.02)
        _RockUV ("Rock: tiling u, v, pan u, v", Vector) = (3, 1, 0.009, 0.018)
        _Shape ("Main pow, detail pow, detail mul, red centre", Vector) = (0.7, 7, 1, 1794)
        _Detail2 ("Detail offset u, v, clouds pow, height ratio", Vector) = (0.4, 0.4, 2, 1.5)
        _HeightPan ("Height pan u, v, cloud pow, cloud mul", Vector) = (0.018, 0.02, 15, 100)
        _Bolts ("Bolt speed, pulse rate 1, 2, sequence time", Vector) = (0.1, 0.05, 0.02, 0.1)
        _Bolts2 ("Frames, lightning, lightning cloud, gradation", Vector) = (4, 100000, 200, 4)
        _Blend ("Blend exp, blend mult, core pow, core tint", Vector) = (1, 1, 35, 0.05)
        _Inner ("Inner colour", Vector) = (0.1, 0, 0.003, 0)
        _Outer ("Outer colour", Vector) = (0.1, 0, 0.0333, 0)
        _Core ("Core colour", Vector) = (0, 0, 0, 0)
        _Brightness ("Bolt brightness", Vector) = (200, 0.8, 5, 0)
        _Background ("Background colour", Vector) = (0.01, 0.013, 0.02, 0)
        _Clouds ("Clouds colour", Vector) = (0.0064, 0.0061, 0.02, 0)
        _Exposure ("Exposure", Float) = 0.4
        _Color ("Fade (a)", Color) = (1, 1, 1, 1)
        _Mode ("0 M_PortalRift_2, 1 M_PortalRift_BOTTOM", Float) = 0
        _Emissive ("Bottom: emissive colour", Vector) = (0.05, 0.0075, 0.0088, 0)
        _BaseCol ("Bottom: base (rock) colour", Vector) = (0.05, 0.0097, 0.0092, 0)
        _Fade ("Bottom: depth fade, fade offset, fade length (m)", Vector) = (0, 0, 0, 0)
        _WpoMap ("Offset noise (T_Noise_Tiled04)", 2D) = "black" {}
        _Wpo ("Offset strength (model units)", Vector) = (0, 0, 0, 0)
        _WpoUV ("Offset: tiling u, v, pan u, v", Vector) = (1, 2, 0.01, 0.04)
        [HideInInspector] _SrcBlend ("Src blend", Float) = 1
        [HideInInspector] _DstBlend ("Dst blend", Float) = 1
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        ZWrite Off
        Cull Off
        Blend [_SrcBlend] [_DstBlend]

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            TEXTURE2D(_WpoMap); SAMPLER(sampler_WpoMap);
            TEXTURE2D(_Tiled); SAMPLER(sampler_Tiled);
            TEXTURE2D(_Strip); SAMPLER(sampler_Strip);
            TEXTURE2D(_Lightning); SAMPLER(sampler_Lightning);
            TEXTURE2D(_CloudMask); SAMPLER(sampler_CloudMask);
            TEXTURE2D(_Cloud); SAMPLER(sampler_Cloud);
            TEXTURE2D(_Rock); SAMPLER(sampler_Rock);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainUV, _DetailUV, _ParUV, _RockUV, _Shape, _Detail2, _HeightPan, _Bolts, _Bolts2, _Blend;
                float4 _Inner, _Outer, _Core, _Brightness, _Background, _Clouds;
                float _Exposure;
                half4 _Color;
                float _Mode;
                float4 _Emissive, _BaseCol, _Fade, _Wpo, _WpoUV;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 tangentOS : TANGENT; float2 uv : TEXCOORD0; float2 uv1 : TEXCOORD1; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION; float4 uv : TEXCOORD0; float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2; float4 tangentWS : TEXCOORD3;
            };

            Varyings vert(Attributes i)
            {
                Varyings o;
                if (_Wpo.x != 0.0)
                {
                    // the world position offset: along the normal, none at the vortex's rim and middle
                    float g = 1.0 - i.uv.y;   // (Unreal's v)
                    float2 at = float2(i.uv.x, g) * _WpoUV.xy + _Time.y * _WpoUV.zw;
                    float n = SAMPLE_TEXTURE2D_LOD(_WpoMap, sampler_WpoMap, float2(at.x, 1.0 - at.y), 0).r;
                    i.positionOS.xyz += i.normalOS * (n * saturate(5.0 * g * (1.0 - g)) * _Wpo.x);
                }
                o.positionWS = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.uv = float4(i.uv, i.uv1);
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.tangentWS = float4(TransformObjectToWorldDir(i.tangentOS.xyz), i.tangentOS.w);
                return o;
            }

            // Unreal's UVs (v down) on the loader's (v up), and the maps sampled as made
            float2 UE(float2 uv) { return float2(uv.x, 1.0 - uv.y); }
            float PowZ(float x, float e) { return x > 0.0 ? pow(x, e) : 0.0; }
            float Tex(TEXTURE2D_PARAM(t, s), float2 ueUv) { return SAMPLE_TEXTURE2D(t, s, UE(ueUv)).r; }

            half4 frag(Varyings i) : SV_Target
            {
                float2 uv = UE(i.uv.xy), uv1 = UE(i.uv.zw);
                float g = uv.y, t = _Time.y;
                float a = Tex(TEXTURE2D_ARGS(_Tiled, sampler_Tiled), float2(uv.x, PowZ(g, _Shape.x)) * _MainUV.xy + t * _MainUV.zw);
                float d = PowZ(Tex(TEXTURE2D_ARGS(_Tiled, sampler_Tiled), (uv + _Detail2.xy) * _DetailUV.xy + t * _DetailUV.zw), _Shape.y) * _Shape.z;
                float g9 = PowZ(g, 9.0);
                float r = a * d * g9;

                // the bolts
                float q = t * _Bolts.x;
                float p = Tex(TEXTURE2D_ARGS(_Strip, sampler_Strip), (q * _Bolts.y).xx) * Tex(TEXTURE2D_ARGS(_Strip, sampler_Strip), (q * _Bolts.z).xx);
                float N = max(_Bolts2.x, 1.0);
                float f = frac(floor(t * _Bolts.w * N * N) / (N * N));
                float2 frame = floor(f * float2(N * N, N)) / N;
                float bolt = Tex(TEXTURE2D_ARGS(_Lightning, sampler_Lightning), uv / N + frame + 1.0);
                float mask = Tex(TEXTURE2D_ARGS(_CloudMask, sampler_CloudMask), uv1 / N + frame);
                float3 boltCol = bolt * mask * p * _Brightness.rgb * _Bolts2.y;

                // the clouds, pushed along the view (a bump offset in the tangent frame)
                float3 view = normalize(GetCameraPositionWS() - i.positionWS);
                float3 b = cross(i.normalWS, i.tangentWS.xyz) * i.tangentWS.w;
                float h = SAMPLE_TEXTURE2D(_Cloud, sampler_Cloud, UE(uv + t * _HeightPan.xy)).g;
                float2 off = float2(dot(i.tangentWS.xyz, view), dot(b, view)) * (_Detail2.w * (h - 0.5));
                float n = Tex(TEXTURE2D_ARGS(_Cloud, sampler_Cloud), float2(uv.x, PowZ(g, _Detail2.z)) * _ParUV.xy + t * _ParUV.zw + off);

                float s = a * d + n + r * _Shape.w + g9;
                if (_Mode > 0.5)
                {
                    // M_PortalRift_BOTTOM
                    float3 c = s * _Emissive.rgb;
                    c += g9 > 0.0 ? min(pow(g9, _Blend.z), 1.0) * _Core.rgb * _Blend.w : 0.0;
                    c += boltCol;
                    c += PowZ(1.0 - n, 90.0) * _Brightness.rgb * p * mask * _Bolts2.z;
                    float out1 = 1.0 - saturate(s);
                    float k1 = Tex(TEXTURE2D_ARGS(_Rock, sampler_Rock), (uv + 0.3) * _RockUV.xy + t * _RockUV.zw);
                    float k2 = Tex(TEXTURE2D_ARGS(_Rock, sampler_Rock), uv * _RockUV.xy + t * _RockUV.zw);
                    float rk = k1 * k1 * k2;
                    c += out1 * rk * _BaseCol.rgb;
                    c += PowZ(n, _HeightPan.z) * _Emissive.rgb * _HeightPan.w;
                    float D1 = g > 0.0 ? min(pow(g, _Bolts2.w), 1.0) : 0.0;
                    float y1 = (D1 - out1 * rk + 1.0) * D1;
                    float alpha = saturate(saturate(s * _Emissive.r * 9.0) * y1 + y1);
                    // its fades: what is behind it within the depth fade, the camera nearer than offset + length
                    float eye = -TransformWorldToView(i.positionWS).z;
                    if (_Fade.x > 0.0)
                    {
                        float raw = SampleSceneDepth(i.positionCS.xy / _ScaledScreenParams.xy);
                        float behind = saturate((LinearEyeDepth(raw, _ZBufferParams) - eye) / _Fade.x);
                        #if !UNITY_REVERSED_Z
                        if (raw <= 0.0) behind = 1.0;   // (no depth texture: nothing to fade against)
                        #endif
                        alpha *= behind;
                    }
                    if (_Fade.z > 0.0) alpha *= saturate((eye - _Fade.y) / _Fade.z);
                    return half4(max(c, 0.0) * _Exposure, saturate(alpha * _Color.a));
                }
                float blend = s > 0.0 ? saturate(pow(s, _Blend.x) * _Blend.y) : 0.0;
                float3 base = lerp(_Outer.rgb, _Inner.rgb, blend);
                float3 col = s * base;
                col += g9 > 0.0 ? min(pow(g9, _Blend.z), 1.0) * _Core.rgb * _Blend.w : 0.0;
                col += r * boltCol;
                col += r * PowZ(1.0 - n, 90.0) * _Brightness.rgb * p * mask * _Bolts2.z;
                float outside = 1.0 - saturate(s);
                float o1 = Tex(TEXTURE2D_ARGS(_Rock, sampler_Rock), (uv + 0.3) * _RockUV.xy + t * _RockUV.zw);
                float o2 = Tex(TEXTURE2D_ARGS(_Rock, sampler_Rock), uv * _RockUV.xy + t * _RockUV.zw);
                float rock = o1 * o1 * o2;
                col += outside * rock * _Background.rgb;
                col += PowZ(n, _HeightPan.z) * _Clouds.rgb * _HeightPan.w * (g > 0.0 ? g * g : 0.0);

                float D = g > 0.0 ? min(pow(g, _Bolts2.w), 1.0) : 0.0;
                float y = (D - outside * rock + 1.0) * D;
                float opacity = saturate(saturate(s * base.r * 9.0) * y + y);
                return half4(max(col, 0.0) * opacity * _Exposure * _Color.a, 0.0);
            }
            ENDHLSL
        }
    }
}
