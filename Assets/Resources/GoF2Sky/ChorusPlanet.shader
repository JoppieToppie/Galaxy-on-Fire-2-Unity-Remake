// GoF2/ChorusPlanet: a stations.json planetSphere with "look": "chorus" (World.Backdrop.ChorusPlanetMaterial), Chorus's
// planet masters from their compiled shaders (the Chorus importer's tools/planets.py writes the specs; docs/notes/mods-world.md
// "Chorus planets"):
//   _CPKind 0 M_Planet03 (MI_Planet_LocTRN's base pass, MI_Planet03c's with water and flow-mapped clouds): tiled terrain and
//     macro normal maps blended by the macro height, an elevation colour from the CA_Planets curve atlas (its rows baked into
//     _CPCurves by the importer: 0 elevation, 1 the atmosphere's directional colour, 2 its fresnel colour) with grunge,
//     water under Water Level, clouds advected by a flow map in two phases; toward the rim the normal flattens and the
//     colour turns to the atmosphere's; an emissive atmosphere term; then lit as Unreal's deferred default lit (the sun,
//     GGX, the sky's ambient). Its switches as _CPFlags / _CPFlags2.
//   _CPKind 1 M_Planet_SurfaceCheap (unlit): diffuse and night maps and clouds blended by the sun (AreaCoverage /
//     AreaContrast on the normal-mapped surface), a rim and a "blue rim" (RimContrast, BlueRimContrast), InsideColor on the
//     lit side; "Use Simple" (_CPFlags2.w): no maps but the normal, the colours alone.
// Chorus's planet sphere (SM_Planet_Surface) maps its UVs by longitude / latitude about its pole (Unreal's z): u = 0.75 -
// atan2(y, x) / 2 pi, v = 0.5 - asin(z) / pi; worked out here from the direction (the backdrop sphere has no UVs), its
// tangent along u (west), bitangent along v (south), as its normal maps (Unreal's) expect. Its normal jitter (a 0.003 screen
// dither) is left out; Chorus's exposure too.
Shader "GoF2/ChorusPlanet"
{
    Properties
    {
        _CPKind ("Kind (0 M_Planet03, 1 M_Planet_SurfaceCheap)", Float) = 0
        _CPFlags ("Flags: water, clouds, macro normal map, macro as height threshold", Vector) = (0, 0, 1, 1)
        _CPFlags2 ("Flags: roughness constant, modify shaded specular, curve rows, cheap: use simple", Vector) = (0, 1, 3, 0)
        _CPTerrainNormal ("Terrain normal map / cheap: Normal", 2D) = "bump" {}
        _CPMacroNormal ("Macro normal map", 2D) = "bump" {}
        _CPMacroHeight ("Macro heightmap", 2D) = "gray" {}
        _CPTerrainHeight ("Terrain heightmap", 2D) = "gray" {}
        _CPGrunge ("Grunge", 2D) = "gray" {}
        _CPCurves ("Curves (rows: elevation, atmosphere directional, atmosphere fresnel)", 2D) = "black" {}
        _CPClouds ("Clouds 01 / cheap: CloudsTextures", 2D) = "black" {}
        _CPCloudsFlow ("Clouds flowmap", 2D) = "gray" {}
        _CPFlowNoise ("Flowmap time noise", 2D) = "gray" {}
        _CPDiffuse ("Cheap: DiffuseTexture", 2D) = "black" {}
        _CPNight ("Cheap: NightTexture", 2D) = "black" {}
        [HideInInspector] _CPTiling ("Terrain tiling, macro tile factor, grunge tiling, terrain pan speed", Vector) = (24, 0.2, 0.5, 0)
        [HideInInspector] _CPNormal ("Terrain normal, macro normal, macro threshold, threshold contrast", Vector) = (1, 1, 0.5, 1)
        [HideInInspector] _CPFresnel ("Fresnel exp, fresnel base, terminator tightness, atmosphere clouds emissive", Vector) = (8, 0.05, 10, 0)
        [HideInInspector] _CPAtmos ("Atmosphere directional emissive, fresnel lit, fresnel shade, fresnel emissive", Vector) = (1, 1, 1, 1)
        [HideInInspector] _CPBright ("Brightness lit, shadowed, specular shadowed, roughness grunge / height", Vector) = (1, 0.05, 0.5, 0.5)
        [HideInInspector] _CPColor ("Colour dimming, grunge intensity, atmosphere roughness, macro colour influence", Vector) = (1, 0, 0.85, 0)
        [HideInInspector] _CPWater ("Water level, density, underwater normal, macro height strength", Vector) = (0, 1, 0.1, 1)
        [HideInInspector] _CPWater2 ("Water metallic, specular, roughness, roughness constant", Vector) = (0, 0.5, 0.1, 0.7)
        [HideInInspector] _CPWaterDeep ("Water colour deep", Vector) = (0, 0, 0, 0)
        [HideInInspector] _CPWaterShallow ("Water colour shallow", Vector) = (0, 0, 0, 0)
        [HideInInspector] _CPCloudsA ("Clouds tiling, pan speed, coverage, contrast", Vector) = (11, 0.005, 0.5, 0.5)
        [HideInInspector] _CPCloudsB ("Flowmap tiling, pan speed, speed, offsets", Vector) = (5, 0, 0.01, 0.3)
        [HideInInspector] _CPCloudsC ("Flowmap noise tiling, noise, mip level, clouds brightness min", Vector) = (10, 0, 3, 0)
        [HideInInspector] _CPCloudsD ("Clouds emissive", Vector) = (0, 0, 0, 0)
        [HideInInspector] _CPCloudsTint ("Clouds tint", Vector) = (1, 1, 1, 0)
        [HideInInspector] _CPBaseTint ("Overall basecolor tint", Vector) = (1, 1, 1, 0)
        [HideInInspector] _CPEmissiveTint ("Overall emissive tint", Vector) = (1, 1, 1, 0)
        [HideInInspector] _CPCheap ("Cheap: planet speed, cloud speed, night intensity, normal intensity", Vector) = (0, 0, 0.2, 1)
        [HideInInspector] _CPCheap2 ("Cheap: area coverage, area contrast, rim contrast, blue rim contrast", Vector) = (0.5, 5, 4, 30)
        [HideInInspector] _CPInside ("Cheap: InsideColor", Vector) = (0, 0, 0, 0)
        [HideInInspector] _CPRim ("Cheap: RimColor", Vector) = (0, 0, 0, 0)
        [HideInInspector] _CPBlueRim ("Cheap: BlueRimColor", Vector) = (0, 0, 0, 0)
        _SunDir ("Toward the sun (world)", Vector) = (0, 0, 1, 0)
        _SunColor ("Sun light", Color) = (1, 1, 1, 1)
        _Color ("Fade (a)", Color) = (1, 1, 1, 1)
    }
    SubShader
    {
        Tags { "Queue" = "Transparent-100" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        ZTest LEqual
        Cull Back

        Pass
        {
            Tags { "LightMode" = "GoF2Backdrop" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_CPTerrainNormal); SAMPLER(sampler_CPTerrainNormal);
            TEXTURE2D(_CPMacroNormal);
            TEXTURE2D(_CPMacroHeight);
            TEXTURE2D(_CPTerrainHeight);
            TEXTURE2D(_CPGrunge);
            TEXTURE2D(_CPCurves); SAMPLER(sampler_CPCurves);   // (clamped, bilinear: the atlas's own sampling)
            TEXTURE2D(_CPClouds);
            TEXTURE2D(_CPCloudsFlow);
            TEXTURE2D(_CPFlowNoise);
            TEXTURE2D(_CPDiffuse);
            TEXTURE2D(_CPNight);
            CBUFFER_START(UnityPerMaterial)
                float _CPKind;
                float4 _CPFlags, _CPFlags2;
                float4 _CPTiling, _CPNormal, _CPFresnel, _CPAtmos, _CPBright, _CPColor, _CPWater, _CPWater2, _CPWaterDeep, _CPWaterShallow;
                float4 _CPCloudsA, _CPCloudsB, _CPCloudsC, _CPCloudsD, _CPCloudsTint, _CPBaseTint, _CPEmissiveTint;
                float4 _CPCheap, _CPCheap2, _CPInside, _CPRim, _CPBlueRim;
                float4 _SunDir;
                half4 _SunColor;
                half4 _Color;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dirOS : TEXCOORD0; float3 positionWS : TEXCOORD1; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                #if UNITY_REVERSED_Z
                    o.positionCS.z = o.positionCS.w * 1e-6;
                #else
                    o.positionCS.z = o.positionCS.w * (1 - 1e-6);
                #endif
                o.dirOS = i.positionOS.xyz;
                return o;
            }

            // the UV gradients of the sphere's map (its seam: from u unwrapped where that is smoother)
            static float2 gUx, gUy;
            float4 Tex(TEXTURE2D_PARAM(t, s), float2 uv, float2 scale)   // Unreal's UV (v down) at 'scale' x the sphere's
            {
                return SAMPLE_TEXTURE2D_GRAD(t, s, float2(uv.x, 1.0 - uv.y), gUx * scale * float2(1, -1), gUy * scale * float2(1, -1));
            }
            float3 UnpackXY(float4 c) { float2 n = c.xy * 2.0 - 1.0; return float3(n, sqrt(max(1.0 - dot(n, n), 0.0))); }
            float PowZ(float x, float e) { return x > 0.0 ? pow(x, e) : 0.0; }
            float3 Curve(float u, float row) { return SAMPLE_TEXTURE2D_LOD(_CPCurves, sampler_CPCurves, float2(u, (row + 0.5) / max(_CPFlags2.z, 1.0)), 0).rgb; }
            float4 Curve4(float u, float row) { return SAMPLE_TEXTURE2D_LOD(_CPCurves, sampler_CPCurves, float2(u, (row + 0.5) / max(_CPFlags2.z, 1.0)), 0); }

            half4 frag(Varyings i) : SV_Target
            {
                float3 d = normalize(i.dirOS);
                float lon = atan2(d.x, d.z), lat = asin(clamp(d.y, -1.0, 1.0));
                float ua = 0.75 - lon * (0.5 / PI), ub = 0.75 - atan2(-d.x, -d.z) * (0.5 / PI);   // (ub: the seam on the other side)
                float2 uv0 = float2(ua, 0.5 - lat / PI);
                float2 ga = float2(ddx(ua), ddy(ua)), gb = float2(ddx(ub), ddy(ub));
                float2 du = float2(abs(ga.x) <= abs(gb.x) ? ga.x : gb.x, abs(ga.y) <= abs(gb.y) ? ga.y : gb.y);
                gUx = float2(du.x, ddx(uv0.y)); gUy = float2(du.y, ddy(uv0.y));
                float3 east = float3(cos(lon), 0.0, -sin(lon));
                float3 north = float3(-sin(lat) * sin(lon), cos(lat), -sin(lat) * cos(lon));
                float3 T = -east, B = -north;   // along u, along v

                float3 view = normalize(GetCameraPositionWS() - i.positionWS);
                float3 nv = normalize(TransformObjectToWorldDir(d));   // the sphere's own normal
                float3 l = normalize(_SunDir.xyz);
                float time = _Time.y;

                if (_CPKind > 0.5)
                {
                    // M_Planet_SurfaceCheap
                    bool simple = _CPFlags2.w > 0.5;
                    float2 uvP = uv0 + float2(time * _CPCheap.x, 0.0);
                    float3 nt = UnpackXY(Tex(TEXTURE2D_ARGS(_CPTerrainNormal, sampler_CPTerrainNormal), uvP, 1.0));
                    nt.xy *= _CPCheap.w;
                    float3 n = normalize(TransformObjectToWorldDir(T * nt.x + B * nt.y + d * nt.z));
                    float s = saturate(dot(l, n) + _CPCheap2.x);
                    s = s <= 0.0 ? 0.0 : min(pow(s, _CPCheap2.y), 1.0);
                    float3 col = 0;
                    if (!simple)
                    {
                        float3 night = Tex(TEXTURE2D_ARGS(_CPNight, sampler_CPTerrainNormal), uvP, 1.0).rgb * _CPCheap.z;
                        float3 diff = Tex(TEXTURE2D_ARGS(_CPDiffuse, sampler_CPTerrainNormal), uvP, 1.0).rgb;
                        float clouds = Tex(TEXTURE2D_ARGS(_CPClouds, sampler_CPTerrainNormal), uv0 + float2(time * _CPCheap.y, 0.0), 1.0).r;
                        col = lerp(night * diff, diff + clouds, s);
                    }
                    float f = saturate(dot(nv, view));
                    float rim = 0.0;
                    if (f > 0.0 && 1.0 - f > 0.0) rim = min(pow(1.0 - f, _CPCheap2.z) * 0.35, 1.0) * min(f * f * 40.0, 1.0) * s;
                    col += rim * _CPRim.rgb + s * _CPInside.rgb + (rim > 0.0 ? pow(rim, _CPCheap2.w) * _CPBlueRim.rgb : 0.0);
                    return half4(max(col, 0.0), _Color.a);
                }

                // M_Planet03
                bool water = _CPFlags.x > 0.5, clouds = _CPFlags.y > 0.5, macroNormal = _CPFlags.z > 0.5, threshold = _CPFlags.w > 0.5;
                float2 uvT = uv0 * float2(2.0 * _CPTiling.x, _CPTiling.x) + float2(-time * _CPTiling.w, 0.0);
                float2 tT = float2(2.0 * _CPTiling.x, _CPTiling.x);
                float3 nT = UnpackXY(Tex(TEXTURE2D_ARGS(_CPTerrainNormal, sampler_CPTerrainNormal), uvT, tT));
                float3 n6 = float3(nT.xy * _CPNormal.x, nT.z);
                float hM = Tex(TEXTURE2D_ARGS(_CPMacroHeight, sampler_CPTerrainNormal), uvT * _CPTiling.y, tT * _CPTiling.y).r;
                float hT = Tex(TEXTURE2D_ARGS(_CPTerrainHeight, sampler_CPTerrainNormal), uvT, tT).r;
                float m = saturate((hM - _CPNormal.z) * _CPNormal.w);
                float3 n = n6;
                if (macroNormal)
                {
                    float3 nM = UnpackXY(Tex(TEXTURE2D_ARGS(_CPMacroNormal, sampler_CPTerrainNormal), uvT * _CPTiling.y, tT * _CPTiling.y));
                    n = lerp(n6, float3(nM.xy * _CPNormal.y, nM.z), m);
                }
                // the height: "Macro As Height Treshold" (the macro map above the threshold), else the two blended by strength
                float h = threshold ? lerp(hT * _CPNormal.z, hM * (1.0 - _CPNormal.z) + _CPNormal.z, m)
                                    : (hM * _CPWater.w + hT) / (_CPWater.w + 1.0);
                float hColour = threshold ? h : lerp(hT, h, _CPColor.w);
                float isWater = 0.0, w = 1.0;
                if (water)
                {
                    float depth = saturate(_CPWater.x - h);
                    w = min(exp(-depth * _CPWater.y), 1.0);
                    isWater = h >= _CPWater.x ? 0.0 : 1.0;
                    float3 nU = (n6 - float3(0, 0, 1)) * _CPWater.z * w + float3(0, 0, 1);
                    n = lerp(n, nU, isWater);
                }
                float c = 0.0, cc = 0.0;
                if (clouds)
                {
                    float2 tC = float2(2.0 * _CPCloudsA.x, _CPCloudsA.x);
                    float2 uvC = uv0 * tC + float2(-time * _CPCloudsA.y, 0.0);
                    float2 tN = float2(2.0 * _CPCloudsC.x, _CPCloudsC.x);
                    float phase = time * _CPCloudsB.z + Tex(TEXTURE2D_ARGS(_CPFlowNoise, sampler_CPTerrainNormal), uv0 * tN, tN).r * _CPCloudsC.y;
                    float2 tF = float2(2.0 * _CPCloudsB.x, _CPCloudsB.x);
                    float2 uvF = uv0 * tF + float2(-time * _CPCloudsB.y, 0.0);
                    float2 flow = (SAMPLE_TEXTURE2D_LOD(_CPCloudsFlow, sampler_CPTerrainNormal, float2(uvF.x, 1.0 - uvF.y), _CPCloudsC.z).xy - 0.5) * float2(-2.0, 2.0);
                    float p1 = frac(phase - 0.5), p2 = frac(phase);
                    float2 uvA = frac(uvC + flow * p1) + _CPCloudsB.w;
                    float2 uvB = uvC + flow * p2;
                    float cA = Tex(TEXTURE2D_ARGS(_CPClouds, sampler_CPTerrainNormal), uvA, tC).r;
                    float cB = Tex(TEXTURE2D_ARGS(_CPClouds, sampler_CPTerrainNormal), uvB, tC).r;
                    float wgt = 1.0 - abs(2.0 * frac(p2) - 1.0);
                    c = lerp(cA, cB, wgt);
                    cc = saturate(c + (_CPCloudsA.z - 0.5) * 2.0);
                    cc = saturate(cc * (2.0 * _CPCloudsA.w + 1.0) - _CPCloudsA.w);
                }
                float x = 1.0 - max(dot(nv, view), 0.0);
                float fres = PowZ(abs(x), _CPFresnel.x) * (1.0 - _CPFresnel.y) + _CPFresnel.y;
                float k = max(fres, cc);
                n = lerp(n, float3(0, 0, 1), k);
                float3 nw = normalize(TransformObjectToWorldDir(T * n.x + B * n.y + d * n.z));

                float ndlV = dot(nv, l);
                float t = PowZ(saturate(ndlV * _CPFresnel.z + 1.0), 2.2);
                float uL = (ndlV + 1.0) * 0.5;
                float3 colDir = Curve(uL, 1.0), colFres = Curve(uL, 2.0);
                float lit = lerp(_CPBright.y, _CPBright.x, t);
                float cloudsBright = c * c * (1.0 - _CPCloudsC.w) + _CPCloudsC.w;
                float3 cloudsE = clouds ? cc * (1.0 + cloudsBright * _CPCloudsD.x) * _CPCloudsTint.rgb * t : t;
                float3 em = colDir * _CPAtmos.x + fres * colFres * lerp(_CPAtmos.z, _CPAtmos.y, t) * _CPAtmos.w + cloudsE * _CPFresnel.w;
                em *= k * lit * _CPEmissiveTint.rgb;

                float4 elev = Curve4(hColour, 0.0);
                float3 g = Tex(TEXTURE2D_ARGS(_CPGrunge, sampler_CPTerrainNormal), uvT * _CPTiling.z, tT * _CPTiling.z).rgb;
                elev.rgb += (g - 0.5) * _CPColor.y * 2.0;
                float3 base0 = elev.rgb * _CPColor.x;
                if (water) base0 = lerp(base0, lerp(_CPWaterDeep.rgb, _CPWaterShallow.rgb, w), isWater);
                float3 rimCol = clouds ? cloudsBright * _CPCloudsTint.rgb * colFres : colFres;
                float3 baseColor = saturate(lerp(base0, rimCol, k) * lit * _CPBaseTint.rgb);
                float rough = _CPFlags2.x > 0.5 ? lerp(_CPWater2.w, _CPWater2.z, isWater) : lerp(g.r, elev.a, _CPBright.w);
                rough = saturate(lerp(rough, _CPColor.z, k));
                float spec = _CPFlags2.y > 0.5 ? saturate(lerp(_CPBright.z, 0.5, t))
                                               : saturate(lerp(0.5 + isWater * (_CPWater2.y - 0.5), 0.5, k));
                float metal = saturate(isWater * _CPWater2.x * (1.0 - k));

                // Unreal's default lit under the sun: Lambert, GGX with Schlick's fresnel and Smith's visibility; the sky's ambient
                float nl = saturate(dot(nw, l));
                float3 hv = normalize(l + view);
                float nh = saturate(dot(nw, hv)), nvv = saturate(dot(nw, view)) + 1e-4, vh = saturate(dot(view, hv));
                float a = max(rough * rough, 0.002), a2 = a * a;
                float dd = nh * nh * (a2 - 1.0) + 1.0;
                float D = a2 / (PI * dd * dd);
                float vis = 0.5 / (nl * (nvv * (1.0 - a) + a) + nvv * (nl * (1.0 - a) + a) + 1e-5);
                float3 f0 = lerp(0.08 * spec, baseColor, metal);
                float3 F = f0 + (1.0 - f0) * pow(1.0 - vh, 5.0);
                float3 diffuse = baseColor * (1.0 - metal);
                float3 col = (diffuse + D * vis * F) * _SunColor.rgb * nl   // (a light's diffuse as the remake's hulls take it: x N.L)
                           + diffuse * SampleSH(nw) + em;
                return half4(max(col, 0.0), _Color.a);
            }
            ENDHLSL
        }
    }
}
