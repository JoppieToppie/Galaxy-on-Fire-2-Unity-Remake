// RebuiltPlanet.hlsl: the body of GoF2/RebuiltPlanet and GoF2/RebuiltPlanetBackdrop (World.RebuiltPlanets): an orbit planet rebuilt
// as a sphere from its png (the planet_rebuild tools' export: tune5.py's model, its sums as tune5's view() makes them), drawn on a
// quad facing the camera round the sphere: each pixel's ray meets the sphere (its normal), passes it (the rim's normal, for the
// png's soft edge and its halo) or meets the ring's plane. The frame: the object's axes, the png's x = object x (mirrored by
// _Mirror), y = object y, toward the png's viewer = object -z; the sphere's radius 1 in object units, centred on the object.
// The sums run in the png's own (gamma) values, as the png and tune5 keep them; the result goes out linear, premultiplied.
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

TEXTURE2D(_Albedo); SAMPLER(sampler_Albedo);
TEXTURE2D(_Face); SAMPLER(sampler_Face);
TEXTURE2D(_Corr); SAMPLER(sampler_Corr);
TEXTURE2D(_Emission); TEXTURE2D(_Night); TEXTURE2D(_Glow);
TEXTURE2D(_Curves); SAMPLER(sampler_Curves);   // rows: the light curve, the glow curve (40 bands from _Curve.x by _Curve.y), the grade (256 over 0.._Grade.x)
TEXTURE2D(_Halo); SAMPLER(sampler_Halo);       // u the angle from the light's (-pi..pi), v the radius (_HaloR.x.._HaloR.y)
TEXTURE2D(_Cover); SAMPLER(sampler_Cover);     // u the radius 0.9..1.1, v the angle from the light's: the png's edge as it fades
TEXTURE2D(_RingProfile); SAMPLER(sampler_RingProfile);   // the 3D ring: its band profile out from _RingProfileR.x to .y body radii, round it (premultiplied)
TEXTURE2D(_Ring); SAMPLER(sampler_Ring);       // the ring as its png shows it: u its angle (-pi..pi), v its radius _RingR.x.._RingR.y (plane units), premultiplied
float4 _StarMapSunPos;

CBUFFER_START(UnityPerMaterial)
    float4 _LightDir;      // toward the light (world); w 1: the png's own (_FaceLight, on the planet's frame), w 2: from the star map's sun (_StarMapSunPos)
    float4 _FaceLight;     // the png's own light, in the png's frame (the face's light taken out under it)
    float4 _Curve;         // x the first band's n.L, y the step, z the bands
    float4 _Knobs;         // x ambient, y gain, z halo from (radius), w the knee's peak
    float4 _RimColor;      // rgb; a the rim's strength
    float4 _RimShape;      // x its power
    float4 _Grade;         // x the grade's top level, y 1 graded; gain in _GradeGain
    float4 _GradeGain;
    float4 _HaloR;         // x, y the halo table's radii
    float4 _Layers;        // x emission, y night, z glow (1: there)
    float4 _RingPole;      // the ring's pole in the png's frame; w 1: a ring
    float4 _RingR;         // x, y the texture's radii (plane units: the outer edge 1); z the quad's reach (body radii); w body radii per plane unit
    float4 _RingE1;        // w the png's plane's y sign of its near half (the backdrop's: the far half behind the body)
    float4 _RingE2;
    float4 _RingProfileR;  // the 3D ring's profile: x, y its radii (body radii)
    float4 _RingM0, _RingM1, _RingM2;   // the png's frame (body radii) -> the plane, as the png's camera has it (homogeneous)
    float _FaceHalf;
    float _Mirror;
    float _Debug;          // 0 off; 1 surface, 2 face, 3 the light curve at s, 4 s, 5 the face's share, 6 the edge's cover, 7 the halo x 4
    half4 _Color;          // fade (a)
    half4 _Tint;           // added (the orbit's fog), as GoF2/Backdrop
CBUFFER_END

struct Attributes { float4 positionOS : POSITION; };
struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; };

float3 CentreWS() { return TransformObjectToWorld(float3(0, 0, 0)); }
float RadiusWS() { return length(float3(UNITY_MATRIX_M._m00, UNITY_MATRIX_M._m10, UNITY_MATRIX_M._m20)); }

// the quad round the sphere, toward the camera from its front: wide enough that every ray within _RingR.z radii of the centre crosses it
Varyings RebuiltVert(Attributes i)
{
    Varyings o;
    float3 c = CentreWS(), eye = GetCameraPositionWS();
    float R = RadiusWS(), E = max(_RingR.z, 1.16);
    float3 toC = c - eye; float D = max(length(toC), 1e-4); float3 f = toC / D;
    float3 r1 = normalize(UNITY_MATRIX_V[0].xyz - f * dot(UNITY_MATRIX_V[0].xyz, f) + float3(1e-6, 0, 0));
    float3 u1 = cross(f, r1);
    float near = min(R, D * 0.5);   // (the plane through the sphere's front, or halfway when the camera is close)
    float dq = D - near;
    float er = E * R;
    float rho = er < D * 0.999 ? er * D / sqrt(D * D - er * er) : 1e3 * R;   // (the reach, at the centre's distance)
    float3 p = c - f * near + (r1 * i.positionOS.x + u1 * i.positionOS.y) * rho * dq / D;
    o.positionWS = p;
    o.positionCS = TransformWorldToHClip(p);
    #ifdef REBUILT_BACKDROP   // (the backdrop pass: behind every opaque object, on the far plane, as GoF2/Backdrop's quads)
        #if UNITY_REVERSED_Z
            o.positionCS.z = o.positionCS.w * 1e-6;
        #else
            o.positionCS.z = o.positionCS.w * (1 - 1e-6);
        #endif
    #endif
    return o;
}

float3 PngOf(float3 w)   // a world direction in the png's frame
{
    float3 ax = normalize(float3(UNITY_MATRIX_M._m00, UNITY_MATRIX_M._m10, UNITY_MATRIX_M._m20));
    float3 ay = normalize(float3(UNITY_MATRIX_M._m01, UNITY_MATRIX_M._m11, UNITY_MATRIX_M._m21));
    float3 az = normalize(float3(UNITY_MATRIX_M._m02, UNITY_MATRIX_M._m12, UNITY_MATRIX_M._m22));
    return float3(dot(w, ax) * (_Mirror > 0.5 ? -1.0 : 1.0), dot(w, ay), -dot(w, az));
}
float3 WorldOfPng(float3 p)
{
    float3 ax = normalize(float3(UNITY_MATRIX_M._m00, UNITY_MATRIX_M._m10, UNITY_MATRIX_M._m20));
    float3 ay = normalize(float3(UNITY_MATRIX_M._m01, UNITY_MATRIX_M._m11, UNITY_MATRIX_M._m21));
    float3 az = normalize(float3(UNITY_MATRIX_M._m02, UNITY_MATRIX_M._m12, UNITY_MATRIX_M._m22));
    return ax * p.x * (_Mirror > 0.5 ? -1.0 : 1.0) + ay * p.y - az * p.z;
}

float3 Curve(float s, float row)   // the light / glow curve: linear between the bands' middles, flat past the ends
{
    float t = clamp((s - _Curve.x) / _Curve.y, 0.0, _Curve.z - 1.0);
    return SAMPLE_TEXTURE2D_LOD(_Curves, sampler_Curves, float2((t + 0.5) / 256.0, (row + 0.5) / 3.0), 0).rgb;
}
float3 Lit(float s) { return _Knobs.y * Curve(s, 0) * (1.0 + (_Knobs.x - 1.0) * saturate(1.0 - (s + 0.1) / 0.3)); }
float Night(float s) { float t = saturate((0.1 - s) / 0.25); return t * t * (3.0 - 2.0 * t); }
float Smooth(float t) { return t * t * (3.0 - 2.0 * t); }
float Mean(float3 c) { return (c.r + c.g + c.b) / 3.0; }

// an equirect map in the png's frame at direction m (u round atan2(x, z), v up), its mip from a u that doesn't wrap here
float4 Equirect(TEXTURE2D_PARAM(tex, smp), float3 m, float g)
{
    float u = atan2(m.x, m.z) / (2.0 * PI) + 0.5;
    float v = asin(clamp(m.y, -1.0, 1.0)) / PI + 0.5;
    float uAlt = frac(u + 0.5) - 0.5;
    float2 dU = float2(ddx(u), ddy(u)), dUAlt = float2(ddx(uAlt), ddy(uAlt));
    if (dot(dUAlt, dUAlt) < dot(dU, dU)) dU = dUAlt;
    return SAMPLE_TEXTURE2D_GRAD(tex, smp, float2(u, v), float2(dU.x, ddx(v)) * g, float2(dU.y, ddy(v)) * g);
}

float3 ToLinear(float3 c)
{
    c = max(c, 0.0);
    return lerp(c / 12.92, pow((c + 0.055) / 1.055, 2.4), step(0.04045, c));
}

// (the scene's variant writes the depth of what it draws, the sphere's hit or the ring's: the quad's own depth isn't the planet's, and
//  without one a sun's glow drawn after it washed it out; past both, the far plane: its halo hides nothing)
#ifdef REBUILT_BACKDROP
half4 RebuiltFrag(Varyings i) : SV_Target
#else
half4 RebuiltFrag(Varyings i, out float depthOut : SV_Depth) : SV_Target
#endif
{
    float3 eye = GetCameraPositionWS(), c = CentreWS();
    float R = RadiusWS();
    float3 d = normalize(i.positionWS - eye);
    float3 f = normalize(c - eye);
    #ifdef REBUILT_BACKDROP
        // (the orbit's backdrop: the sphere seen as its png shows it, orthographic along the way to its centre (its disc as wide
        //  as the sphere's silhouette): the png is a flat picture, and a near-perspective view of the sphere's limb wasn't its)
        float Dc = length(c - eye);
        float3 lat = eye + d * (Dc / max(dot(d, f), 1e-4)) - c;
        lat *= sqrt(max(Dc * Dc - R * R, 1e-6)) / Dc;
        eye = c + lat - f * (2.0 * R * max(_RingR.z, 1.0));
        d = f;
    #endif
    float3 oc = c - eye; float tc = dot(oc, d);
    float3 q = eye + d * tc - c; float dist = length(q); float r = dist / R;
    float3 toView = -d;
    // the normal: the ray's hit, or past the edge the rim's (as tune5's views: clamped to 0.9995 of the radius)
    float3 nW; float tHit = 1e30;
    if (r < 0.9995) { tHit = tc - sqrt(max(R * R - dist * dist, 0.0)); nW = normalize(eye + d * tHit - c); }
    else { float3 qd = q / max(dist, 1e-6); nW = normalize(qd * 0.9995 + toView * sqrt(1.0 - 0.9995 * 0.9995)); }
    float nz = saturate(dot(nW, toView));
    float3 m0 = PngOf(nW);
    // (w 1: the png's own light, turning with the planet; w 2: the star map's, from its sun but turned toward the viewer, as the map's
    //  planets read (GoF2/MapPlanet's ambient 0.2 + diffuse 2): lit by the sun alone they showed the map's camera their night side)
    float3 Lw = _LightDir.w > 1.5 ? normalize(normalize(_StarMapSunPos.xyz - c) + 0.9 * normalize(eye - c)) : _LightDir.w > 0.5 ? normalize(WorldOfPng(_FaceLight.xyz)) : normalize(_LightDir.xyz);
    float s = dot(nW, Lw), s0 = dot(m0, _FaceLight.xyz);

    // the screen's frame round the centre: the angle from the light's as seen (the halo's and the edge's tables)
    float3 r1 = normalize(UNITY_MATRIX_V[0].xyz - f * dot(UNITY_MATRIX_V[0].xyz, f) + float3(1e-6, 0, 0)), u1 = cross(f, r1);
    float rel = (atan2(dot(q, u1), dot(q, r1)) - atan2(dot(Lw, u1), dot(Lw, r1))) * (_Mirror > 0.5 ? -1.0 : 1.0);
    float relU = frac(rel / (2.0 * PI) + 0.5);   // (the tables' u: -pi..pi from the light's, wrapping)

    // the surface (past the edge, where the rim's point barely moves along the radius, its maps from a smaller mip: sharp there
    // they streaked out from the silhouette)
    float g = r < 0.9995 ? 1.0 : 8.0;
    float4 a4 = Equirect(TEXTURE2D_ARGS(_Albedo, sampler_Albedo), m0, g);
    float3 col = 4.0 * a4.rgb * a4.rgb;
    float3 em = 0, nl = 0, gl = 0;
    if (_Layers.x > 0.5) { float3 e_ = Equirect(TEXTURE2D_ARGS(_Emission, sampler_Albedo), m0, g).rgb; em = 2.0 * e_ * e_; }
    if (_Layers.y > 0.5) nl = (Equirect(TEXTURE2D_ARGS(_Night, sampler_Albedo), m0, g).rgb - 0.5) * 0.5;
    if (_Layers.z > 0.5) { float3 g_ = Equirect(TEXTURE2D_ARGS(_Glow, sampler_Albedo), m0, g).rgb; gl = 2.0 * g_ * g_; }
    float2 fuv = 0.5 + m0.xy / (2.0 * _FaceHalf);
    float2 fdx = ddx(fuv) * g, fdy = ddy(fuv) * g;
    float3 fr = SAMPLE_TEXTURE2D_GRAD(_Face, sampler_Face, fuv, fdx, fdy).rgb; fr = 4.0 * fr * fr;
    float3 fc = exp2((SAMPLE_TEXTURE2D_GRAD(_Corr, sampler_Corr, fuv, fdx, fdy).rgb - 0.5) * 8.0);
    // the face where the png saw the spot head-on, or where the view hasn't turned from the png's
    // (as tune5's views, which are orthographic: the view's z the way to the planet's centre, not each pixel's ray; with the ray's
    //  a near planet's limb, seen from the side, handed the face over to the surface)
    float nzc = saturate(dot(nW, -f));
    float stretch = nzc / max(m0.z, 1e-3);
    float turnDeg = degrees(acos(clamp(dot(WorldOfPng(float3(0, 0, 1)), -f), -1.0, 1.0)));
    float fm = max(saturate(2.2 - stretch) * saturate((m0.z - 0.4) / 0.3) * saturate((nzc - 0.2) / 0.25),
                   saturate(1.0 - abs(nzc - m0.z) / 0.05) * saturate(1.0 - turnDeg / 2.0));
    fm = Smooth(fm) * (m0.z > 0.02 ? 1.0 : 0.0);
    fr *= pow(max(fc, 1e-3), saturate((1.0 - fm) * 2.0));
    float3 ls = Lit(s), l0 = Lit(s0);
    float3 lt = col * ls, lf = fr * ls;
    float L0m = Mean(l0), Lsm = Mean(ls), wl = saturate((L0m - 0.05) / 0.15);
    lf = wl * fr * l0 * clamp(Lsm / max(L0m, 1e-3), 0.0, 4.0) + (1.0 - wl) * lf;   // (the face relit by brightness: its own colour where the png saw it)
    lt += gl * (1.0 - Night(s));
    float pk = _Knobs.w, yl = max(lt.r, max(lt.g, lt.b)), kc = 0.7 * pk;   // (the tile's light kneed under the png's brightest)
    float yk = yl > kc ? kc + (pk - kc) * (1.0 - exp(-(yl - kc) / max(pk - kc, 1e-3))) : yl;
    lt *= yk / max(yl, 1e-6);
    float rim = _RimColor.a * pow(max(1.0 - nz, 0.0), _RimShape.x) * saturate(saturate(s) * 1.5 + 0.1);
    float tl = saturate((Lsm - 0.04) / 0.08);
    float3 rgb = lt * (1.0 - fm) + lf * fm + _Knobs.y * Curve(s, 1) + _RimColor.rgb * rim + em * Night(s) + nl * (1.0 - tl);
    if (_Grade.y > 0.5)   // (the tile's part graded to the png's levels)
    {
        float y = Mean(rgb);
        float gy = SAMPLE_TEXTURE2D_LOD(_Curves, sampler_Curves, float2((saturate(y / _Grade.x) * 255.0 + 0.5) / 256.0, 2.5 / 3.0), 0).r;
        float3 g = rgb * (gy / max(y, 1e-4)) * _GradeGain.rgb;
        rgb += (g - rgb) * (1.0 - fm);
    }
    // the png's edge as it fades (three taps over the pixel), its halo
    float w = max(fwidth(r), 1e-4);
    float cv = 0;
    [unroll] for (int k = -1; k <= 1; k++)
    {
        float rr = r + k * w / 3.0;
        cv += rr <= 0.9 ? 1.0 : SAMPLE_TEXTURE2D_LOD(_Cover, sampler_Cover, float2((clamp(rr, 0.9, 1.1) - 0.9) / 0.2, relU), 0).r;
    }
    cv = saturate(cv / 3.0);
    float hv = (r - _HaloR.x) / (_HaloR.y - _HaloR.x);
    float3 h = (hv > 0.0 && hv < 1.0) ? SAMPLE_TEXTURE2D_LOD(_Halo, sampler_Halo, float2(relU, hv), 0).rgb * saturate((r - _Knobs.z) / 0.02) : 0;
    float3 outc = max(rgb, 0.0) * cv + h;
    float alpha = cv;
    // the ring: over the body where it lies in front of the sphere's face, hidden behind it.
    float3 ringHit = 0; float ringA = 0;
    if (_RingPole.w > 0.5)
    {
        #ifdef REBUILT_BACKDROP
            // the orbit's backdrop: the png's ring through the png's own camera (its plane as its perspective lays it: where the png
            // has it, its light and the planet's shadow as the png has them; cut where the png's ring begins)
            float3 sp = PngOf(q) / R;
            float3 w3 = float3(dot(_RingM0.xyz, float3(sp.xy, 1.0)), dot(_RingM1.xyz, float3(sp.xy, 1.0)), dot(_RingM2.xyz, float3(sp.xy, 1.0)));
            float2 pp = abs(w3.z) > 1e-6 ? w3.xy / w3.z : float2(1e3, 1e3);
            bool hidden = r < 1.0 && sign(pp.y) != _RingE1.w;   // (its far half behind the body)
            float rv = (length(pp) - _RingR.x) / (_RingR.y - _RingR.x);
            if (!hidden && rv >= 0.0 && rv <= 1.0)
            {
                float2 ruv = float2(atan2(pp.y, pp.x) / (2.0 * PI) + 0.5, rv);
                float ruAlt = frac(ruv.x + 0.5) - 0.5;   // (the mip from an angle that doesn't wrap here)
                float2 rdx = ddx(ruv), rdy = ddy(ruv), adx = float2(ddx(ruAlt), ddy(ruAlt));
                if (dot(adx, adx) < rdx.x * rdx.x + rdy.x * rdy.x) { rdx.x = adx.x; rdy.x = adx.y; }
                float4 rg = SAMPLE_TEXTURE2D_GRAD(_Ring, sampler_Ring, ruv, rdx, rdy);
                outc = outc * (1.0 - rg.a) + rg.rgb;
                alpha = alpha * (1.0 - rg.a) + rg.a;
            }
        #else
            // the 3D ring, seen from any side: round, centred on the body, its band profile (from its inner edge, off the planet); the
            // planet's shadow on it from the light
            float3 pn = WorldOfPng(_RingPole.xyz);
            float dn = dot(d, pn);
            if (abs(dn) > 1e-5)
            {
                float tr = dot(c - eye, pn) / dn;
                float3 rp = eye + d * tr;
                float pu = (length(rp - c) / R - _RingProfileR.x) / (_RingProfileR.y - _RingProfileR.x);
                if (tr > 0.0 && !(r < 0.9995 && tr > tHit) && pu >= 0.0 && pu <= 1.0)
                {
                    float4 rg3 = SAMPLE_TEXTURE2D_LOD(_RingProfile, sampler_RingProfile, float2(pu, 0.5), 0);
                    float3 oc3 = rp - c; float b3 = dot(oc3, Lw), h3 = b3 * b3 - (dot(oc3, oc3) - R * R);
                    float sh = (b3 < 0.0 && h3 > 0.0) ? saturate(sqrt(h3) / (0.04 * R)) : 0.0;   // (in the planet's shadow, soft at its edge)
                    rg3.rgb *= 1.0 - 0.85 * sh;
                    outc = outc * (1.0 - rg3.a) + rg3.rgb;
                    alpha = alpha * (1.0 - rg3.a) + rg3.a;
                    ringA = rg3.a; ringHit = rp;
                }
            }
        #endif
    }
    #ifndef REBUILT_BACKDROP
        // (every pixel at its own depth: the sphere's hit, the ring's point, else the sphere's front for its halo; drawn after the
        //  scene's glows, a pixel told 'far' failed where a sun's glow quad had written its own depth: the ring vanished over it)
        float3 dp = r < 0.9995 ? eye + d * tHit : ringA > 0.002 ? ringHit : c - f * R;
        float4 hc = TransformWorldToHClip(dp);
        depthOut = hc.z / hc.w;
    #endif
    if (_Debug > 0.5)
    {
        float3 dbg = _Debug < 1.5 ? col * 0.5 : _Debug < 2.5 ? fr * 0.5 : _Debug < 3.5 ? ls : _Debug < 4.5 ? float3(saturate(s), saturate(-s), 0) : _Debug < 5.5 ? fm.xxx : _Debug < 6.5 ? cv.xxx : h * 4.0;
        return half4(ToLinear(dbg), 1);
    }
    // (the orbit's fog tint added as GoF2/Backdrop adds it: to the linear colour, where the png has its cover)
    float3 lin = ToLinear(outc) + _Tint.rgb * saturate(alpha);
    return half4(lin * _Color.a, saturate(alpha) * _Color.a);
}
