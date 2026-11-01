// GoF2/KhadorFx: the Khador Slipstream's way in (World.KhadorFx): each element of the original Khador fx from the original's
// own sheet (khador_jump.png), its beam band and lightning strip, vein web, ring colour and star, with what a 2010 phone
// couldn't: filaments that crawl, veins lit by a wave running out from the focus, a ring of live lightning, sparks with tails.
// All additive and above 1, for the bloom. One keyword per element:
// _BEAM   a ribbon along local -z, turned about its axis to face the camera; uv.x 0 at the focus, 1 at the tail
// _WEB    the unit disc facing the ship
// _RING   the ring, on the unit disc: _Radius and _Width of the disc's own radius
// _SPRITE a tile of the sheet (_Tile) on a camera-facing quad, stretched along _Stretch
// _SPARKS a mesh of quads flying out of the focus, all in the vertex shader (uv1 direction, uv2 start, life, size, seed)
Shader "GoF2/KhadorFx"
{
    Properties
    {
        [NoScaleOffset] _Sheet ("Sheet", 2D) = "black" {}
        _Level ("Level", Float) = 1
        _Seconds ("Seconds", Float) = 0
        _Radius ("Radius", Float) = 0.5
        _Width ("Width", Float) = 0.02
        _Grow ("Web grown", Float) = 1
        _Hole ("Web's hole", Float) = 0
        _Twist ("Web's twist", Float) = 0
        _Thick ("Beam's half width (m)", Float) = 10
        _NearFade ("Beam's fade near the camera (m)", Float) = 40
        _Size ("Sprite's half size (m)", Float) = 10
        _Tint ("Tint", Color) = (1, 1, 1, 1)
        _Tile ("Tile (u, v, width, height)", Vector) = (0, 0, 1, 1)
        _Stretch ("Stretch (screen right, x)", Float) = 1
        _Arcs ("Ring's arcs", Float) = 0
        _Fire ("Fire inside the ring", Float) = 1
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("Depth test", Float) = 4
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend One One
        ZWrite Off
        ZTest [_ZTest]
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_local _BEAM _WEB _RING _SPRITE _SPARKS
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/Shaders/KhadorNoise.hlsl"

            TEXTURE2D(_Sheet); SAMPLER(sampler_Sheet);

            CBUFFER_START(UnityPerMaterial)
                half4 _Tint;
                float4 _Tile;
                float _Level, _Seconds, _Radius, _Width, _Grow, _Hole, _Twist, _Thick, _NearFade, _Size, _Stretch, _Arcs, _Fire;
            CBUFFER_END

            // The sheet's own places (v up, as sampled): the beam's band, the thin lightning strip, the web's triangle.
            static const float2 BeamBand = float2(0.087, 0.075), BoltStrip = float2(0.206, 0.018);
            static const half3 Ice = half3(0.42, 0.68, 1.0), Violet = half3(0.27, 0.27, 1.0);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float3 uv1 : TEXCOORD1;
                float4 uv2 : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 extra : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes i)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.uv = i.uv;
                o.extra = 0;
                float3 world = TransformObjectToWorld(i.positionOS.xyz);
                #if defined(_BEAM)
                    float3 axis = normalize(TransformObjectToWorldDir(float3(0, 0, 1)));
                    float3 toCamera = _WorldSpaceCameraPos - world;
                    float3 side = normalize(cross(axis, toCamera));
                    world += side * i.uv.y * _Thick;
                    o.extra.x = length(_WorldSpaceCameraPos - world);
                #elif defined(_SPRITE)
                    float3 right = UNITY_MATRIX_I_V._m00_m10_m20, up = UNITY_MATRIX_I_V._m01_m11_m21;
                    world += (right * i.positionOS.x * _Stretch + up * i.positionOS.y) * _Size;
                #elif defined(_SPARKS)
                    float age = saturate((_Seconds - i.uv2.x) / i.uv2.y);
                    float seen = _Seconds > i.uv2.x && age < 1.0 ? 1.0 : 0.0;
                    float along = 1.0 - pow(1.0 - age, 2.2);
                    float3 centre = TransformObjectToWorld(i.uv1 * along);
                    float3 flight = TransformObjectToWorldDir(i.uv1);
                    float3 r = UNITY_MATRIX_I_V._m00_m10_m20, u = UNITY_MATRIX_I_V._m01_m11_m21;
                    float2 screen = float2(dot(flight, r), dot(flight, u));
                    screen = dot(screen, screen) > 1e-6 ? normalize(screen) : float2(1, 0);
                    float3 tail = r * screen.x + u * screen.y, wide = r * -screen.y + u * screen.x;
                    float size = i.uv2.z * (1.0 - age * 0.6) * seen;
                    float length_ = size * (1.0 + 7.0 * (1.0 - age));
                    world = centre + tail * i.positionOS.x * length_ + wide * i.positionOS.y * size;
                    o.extra = float4(saturate(age * 8.0) * (1.0 - age) * (1.0 - age), length_ / max(size, 0.001), i.uv2.w, 0);
                #endif
                o.positionCS = TransformWorldToHClip(world);
                return o;
            }

            half3 Sheet(float2 uv) { return SAMPLE_TEXTURE2D(_Sheet, sampler_Sheet, uv).rgb; }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                half3 c = 0;
                #if defined(_BEAM)
                    float s = i.uv.x, v = i.uv.y, t = _Seconds;
                    float settle = smoothstep(0.0, 0.03, s);
                    float wander = (KhadorFbm(float2(s * 26.0 - t * 5.0, 3.1)) - 0.5) * 0.5 * settle;
                    float x = v + wander;
                    float core = exp(-x * x * 90.0);
                    float halo = exp(-abs(x) * 3.6);
                    // the original's two strips: the wide band with its side sparks, and the thin bolt twisting along the beam
                    float2 band = float2(s * 7.0 - t * 2.6, BeamBand.x + clamp(x, -1.0, 1.0) * BeamBand.y);
                    half3 glow = Sheet(band);
                    float crawl = wander * 3.0 + (KhadorFbm(float2(s * 60.0 + t * 8.0, 8.4)) - 0.5) * 0.9;
                    half3 bolt = 0;
                    for (int k = 0; k < 2; k++)
                    {
                        float y = (v - crawl * (k == 0 ? 0.32 : -0.44)) * 2.2;
                        if (abs(y) < 1.0) bolt += Sheet(float2(s * (11.0 + 7.0 * k) + t * (1.4 - k * 2.3), BoltStrip.x + y * BoltStrip.y));
                    }
                    float flicker = 0.8 + 0.2 * KhadorNoise(float2(floor(t * 30.0) * 0.61, s * 3.0));
                    c = (glow * 1.5 + bolt * 1.3 + core * 3.2 + Ice * halo * 0.3) * flicker;
                    c *= (1.0 - smoothstep(0.55, 1.0, s)) * smoothstep(1.0, 0.72, abs(v)) * smoothstep(_NearFade * 0.3, _NearFade, i.extra.x);
                #elif defined(_WEB)
                    float r = length(i.uv), angle = atan2(i.uv.y, i.uv.x), t = _Seconds;
                    float front = _Grow + (KhadorFbm(float2(angle * 1.3, t)) - 0.5) * 0.22;
                    float grown = 1.0 - smoothstep(front - 0.1, front, r);
                    float crest = exp(-pow((r - front) / 0.07, 2.0));
                    float spun = angle + _Twist * (1.0 - r) * (1.0 - r);
                    half3 veins = 0;
                    for (int k = 0; k < 3; k++)
                    {
                        float sides = 6.0 - k;
                        float a = abs(frac(spun * sides / (2.0 * PI) * (k == 1 ? -1.0 : 1.0) + t * (0.02 + k * 0.015) + k * 0.31) * 2.0 - 1.0);
                        float b = abs(frac(r * (1.5 + k * 1.15) - t * (0.05 + k * 0.05) + k * 0.23) * 2.0 - 1.0);
                        veins += Sheet(float2(0.47 + a * 0.19, 0.62 + b * 0.18)) * (k == 0 ? 1.0 : 0.7);
                    }
                    float pulse = pow(saturate(1.0 - abs(frac(r * 2.6 - t * 1.7) - 0.5) * 3.4), 3.0);
                    float lit = 1.15 + crest * 6.0 + pulse * 2.0;
                    float edge = 1.0 - smoothstep(0.82, 1.0, r);
                    float cut = _Hole * KhadorRim(angle / (2.0 * PI), t);   // the tear's own edge
                    c = veins * lit * grown * edge * smoothstep(cut, cut + 0.03, r);
                    c += Ice * 0.05 * grown * edge * (1.0 - r);
                #elif defined(_RING)
                    float r = length(i.uv), turn = atan2(i.uv.y, i.uv.x) / (2.0 * PI), t = _Seconds;
                    float rim = _Radius * KhadorRim(turn, t);
                    float d = (r - rim) / _Width;
                    half3 lightning = 0;
                    for (int k = 0; k < 3; k++)
                    {
                        float y = d * (0.5 + k * 0.28) + (k - 1.0) * 0.55 * KhadorNoise(float2(turn * 40.0, t * 6.0 + k * 3.0));
                        if (abs(y) < 1.0) lightning += Sheet(float2(turn * (3.0 + k) + t * (0.4 - k * 0.5) + k * 0.37, BoltStrip.x + y * BoltStrip.y));
                    }
                    // the sheet's ring: thorns of lightning off a line, in its violet glow
                    float cell = (turn + 0.5) * 53.0, id = fmod(floor(cell), 53.0), f = frac(cell) - 0.5;
                    float beat = floor(t * 22.0 + KhadorHash(float2(id, 2.0)) * 7.0);
                    float lives = step(0.62, KhadorHash(float2(id, beat)));
                    float reach = 0.7 + 3.2 * KhadorHash(float2(id, beat + 5.0));
                    f += sin(d * 2.1 + id) * 0.14;   // bent, each its own way
                    float thorn = exp(-f * f * 90.0) * lives * saturate(1.0 - d / reach) * smoothstep(-0.5, 0.5, d);
                    float inner = exp(-max(-d, 0.0) * 2.4), outer = exp(-max(d, 0.0) * 0.9);   // tight inside: the hole beyond it is the next level's sky
                    c = lightning * 2.0 + half3(0.75, 0.82, 1.0) * thorn * 1.8 + Violet * inner * outer * 1.3 + half3(0.8, 0.9, 1.0) * exp(-d * d * 7.0) * 0.8;
                    // Inside the rim a band of the tear's blue fire, in clouds, gone as the ship reaches the hole (_Fire): none at the swap.
                    if (r < rim)
                    {
                        float band = saturate(1.0 - (rim - r) / (rim * 0.45));
                        float clouds = KhadorFbm(float2(turn * 11.0, r / _Radius * 3.5 - t * 0.9));
                        c += half3(0.1, 0.42, 1.0) * band * band * (0.25 + 1.5 * clouds * clouds) * 1.3 * _Fire;
                    }
                    // The arcs: branching lightning thrown out from the rim, more and further as the ship is drawn in.
                    for (int k = 0; k < 2 && _Arcs > 0.001; k++)
                    {
                        float place = (turn + 0.5) * 7.0 + k * 0.37;   // from the seam of atan2, a whole number of arcs round it
                        float arc = fmod(floor(place), 7.0), across = frac(place) - 0.5;
                        float now = floor(t * (8.0 + k * 5.0) + KhadorHash(float2(arc, k + 1.0)) * 5.0);
                        if (KhadorHash(float2(arc + k * 11.0, now)) < 1.0 - _Arcs * 0.72) continue;
                        float reach = _Radius * (0.5 + 1.7 * _Arcs * KhadorHash(float2(arc, now + 3.0)));
                        float along = (r - rim) / reach;
                        if (along < 0.0 || along > 1.0) continue;
                        float way = (KhadorNoise(float2(along * 6.0 + arc * 3.1, now * 0.73)) - 0.5) * 0.7 * sqrt(along);
                        float thin = 0.006 + 0.02 * (1.0 - along);
                        float main_ = exp(-pow((across - way) / thin, 2.0));
                        float side = KhadorHash(float2(arc, now + 7.0)) - 0.5;
                        float fork = clamp(way + max(along - 0.35, 0.0) * side * 1.3, -0.4, 0.4);
                        float branch = exp(-pow((across - fork) / (thin * 0.6), 2.0)) * smoothstep(0.35, 0.45, along);
                        float glowing = exp(-abs(across - way) * 18.0);
                        float inCell = 1.0 - smoothstep(0.38, 0.5, abs(across));   // nothing reaches an arc's edge: it would end in a straight line
                        c += (half3(0.9, 0.95, 1.0) * (main_ * 3.2 + branch * 1.6) + Ice * glowing * 0.55) * (1.0 - along * along) * inCell;
                    }
                #elif defined(_SPRITE)
                    c = Sheet(_Tile.xy + i.uv * _Tile.zw);
                #elif defined(_SPARKS)
                    float2 p = i.uv;
                    float along = p.x * i.extra.y;
                    float head = saturate(1.0 - length(float2(along, p.y)));
                    float tail = exp(-p.y * p.y * 30.0) * saturate(1.0 + p.x) * saturate(1.0 + p.x) * step(p.x, 0.0);
                    float twinkle = 0.75 + 0.25 * sin(_Seconds * 37.0 + i.extra.z * 40.0);
                    c = (half3(0.9, 0.95, 1.0) * pow(head, 3.0) * 5.0 + Ice * tail * 1.6) * i.extra.x * twinkle;
                #endif
                return half4(c * _Tint.rgb * _Level, 1);
            }
            ENDHLSL
        }
    }
}
