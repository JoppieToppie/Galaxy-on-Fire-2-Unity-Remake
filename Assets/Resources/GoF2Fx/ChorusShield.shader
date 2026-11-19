// GoF2/ChorusShield: a mod material's "shield" block (ModMaterials.Shield; Chorus's M_Shield, from its compiled translucent
// pass on MI_Shield_PlayerCapital_Auxiliary: the player capital ship's Shield Overcharge, a mesh particle of the hull's
// shield shell; docs/notes/mods-models.md "Chorus shield shell"). Unlit, both faces, added. Flight.ShieldShell drives
// _ShieldLife (the particle's relative time 0..1) and _ShieldTint (its colour x the colour over life, a its alpha) per
// renderer. In the model's own space, d the distance from its origin (the particle's pivot); t the time:
//   the mask m = ModulationMask(uv0 x NoiseScale + t Pan1).r x ModulationMask(uv0 x NoiseScale + t Pan2).r
//   the time T = (1 - life)^TimeExponent (Reverse Time; full health: the strained values unused)
//   the noise n = Noise(uv1 + t NoiseTimeScalar NoisePanSpeed, turned by t NoiseTimeScalar 0.025 rad).r x 2 - 1
//   e = (n EMA + T + EMA) / (1 + EMA), past = e >= T; Tm = (n TMA + T + TMA) / (1 + TMA)
//   colour = (F + (2m - 1) MCA + MCA) / (1 + MCA) x m (1 + past EdgeModulateIntensity) x the tint, F the fresnel
//     (1 - N.V)^FresnelExponent x 0.96 + 0.04 (N facing the eye)
//   the glow (1 - d / lerp(RadiusStart, RadiusEnd, Tm))^RadiusExponent x RadiusIntensity, the ring
//   (1 - |d - lerp(RingRadiusStart, End, Tm)| / lerp(RingWidthStart, End, Tm))^RingExponent x RingIntensity
//   opacity = lerp(OpacityStart, OpacityEnd, life) x (ring + glow) / 2 x (front: OpacityScaleFacing, back: Backface)
//     x (1 + OpacityNoise (4m - 1)) + EdgeModulateIntensity x saturate(1 - |T - e| / EdgeModulateWidth) x past,
//     x the tint's alpha, the camera fade (eye depth - offset) / length, the depth fade (scene - eye) / Depth Fade Distance
//   out: colour x opacity x _ShieldFade.w (the exposure: Chorus's HDR to the remake's), added.
// The flicker (below StartFrizzHealthPercentage) and CompAmount (the auto exposure's compensation) are left out.
Shader "GoF2/ChorusShield"
{
    Properties
    {
        _Mask ("Modulation mask (R)", 2D) = "white" {}
        _Noise ("Noise (R, over UV1)", 2D) = "gray" {}
        _ShieldPan ("Pan 1 (u, v), pan 2 (u, v)", Vector) = (-0.4244, 0, 0, -0.3735)
        _ShieldNoise ("Noise scale, noise time scalar, noise pan speed, time exponent", Vector) = (12, 0.02, 0.01, 0.37)
        _ShieldEdge ("Edge modulate amount, intensity, width, fresnel exponent", Vector) = (0.5, 5, 0.01, 1)
        _ShieldMod ("Modulate colour amount, time modulation amount, opacity noise", Vector) = (0.3, 0.2, 0.75, 0)
        _ShieldRadius ("Radius start, end, exponent, intensity", Vector) = (0, 600, 3, 1)
        _ShieldRing ("Ring radius start, end, width start, end", Vector) = (0, 50, 0, 0.2)
        _ShieldRing2 ("Ring exponent, intensity, opacity start, end", Vector) = (3, 10, 1, 1)
        _ShieldFade ("Opacity facing, backface, camera fade offset (m), exposure", Vector) = (1, 0.01, 0.036, 0.01)
        _ShieldDepth ("Camera fade length (m), depth fade (m)", Vector) = (0.072, 0.072, 0, 0)
        _ShieldLife ("Life (0..1)", Float) = 0
        _ShieldTint ("Tint (rgb), alpha", Vector) = (0.7, 10, 3.025, 25)
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            TEXTURE2D(_Mask); SAMPLER(sampler_Mask);
            TEXTURE2D(_Noise); SAMPLER(sampler_Noise);
            CBUFFER_START(UnityPerMaterial)
                float4 _ShieldPan, _ShieldNoise, _ShieldEdge, _ShieldMod, _ShieldRadius, _ShieldRing, _ShieldRing2, _ShieldFade, _ShieldDepth;
                float _ShieldLife;
                float4 _ShieldTint;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv0 : TEXCOORD0; float2 uv1 : TEXCOORD1; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION; float4 uv : TEXCOORD0; float3 positionWS : TEXCOORD1; float3 normalWS : TEXCOORD2;
                float3 positionOS : TEXCOORD3;
            };

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.uv = float4(i.uv0, i.uv1);
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.positionOS = i.positionOS.xyz;
                return o;
            }

            float PowZ(float x, float e) { return x > 0.0 ? pow(x, e) : 0.0; }
            float2 UE(float2 uv) { return float2(uv.x, 1.0 - uv.y); }   // (Unreal's UVs on the loader's)

            half4 frag(Varyings i, bool front : SV_IsFrontFace) : SV_Target
            {
                float t = _Time.y, life = saturate(_ShieldLife);
                float2 uv0 = UE(i.uv.xy), uv1 = UE(i.uv.zw);
                float scale = _ShieldNoise.x;
                float m = SAMPLE_TEXTURE2D(_Mask, sampler_Mask, UE(uv0 * scale + t * _ShieldPan.xy)).r
                        * SAMPLE_TEXTURE2D(_Mask, sampler_Mask, UE(uv0 * scale + t * _ShieldPan.zw)).r;
                float T = PowZ(1.0 - life, _ShieldNoise.w);
                float a = t * _ShieldNoise.y * 0.025, s, c;
                sincos(a, s, c);
                float2 q = t * _ShieldNoise.y * _ShieldNoise.z + uv1;
                float2 r = float2(c * q.x - s * q.y, s * q.x + c * q.y);
                float n = SAMPLE_TEXTURE2D_LOD(_Noise, sampler_Noise, UE(r), 0).r * 2.0 - 1.0;
                float ema = _ShieldEdge.x, tma = _ShieldMod.y;
                float e = (n * ema + T + ema) / (1.0 + ema);
                float past = e >= T ? 1.0 : 0.0;
                float w = m * (1.0 + past * _ShieldEdge.y);
                float3 N = normalize(i.normalWS) * (front ? 1.0 : -1.0);
                float3 V = normalize(GetCameraPositionWS() - i.positionWS);
                float f = 1.0 - max(dot(N, V), 0.0);
                float F = f != 0.0 ? pow(abs(f), _ShieldEdge.w) * 0.96 + 0.04 : 0.04;
                float mca = _ShieldMod.x;
                float k = (F + (m * 2.0 - 1.0) * mca + mca) / (1.0 + mca) * w;
                float3 col = k * _ShieldTint.rgb;

                float Tm = (n * tma + T + tma) / (1.0 + tma);
                float d = length(i.positionOS);
                float R = lerp(_ShieldRadius.x, _ShieldRadius.y, Tm);
                float glow = PowZ(max(1.0 - d / max(R, 1e-5), 0.0), _ShieldRadius.z) * _ShieldRadius.w;
                float rr = lerp(_ShieldRing.x, _ShieldRing.y, Tm), rw = lerp(_ShieldRing.z, _ShieldRing.w, Tm);
                float ring = PowZ(max(1.0 - abs(d - rr) / max(rw, 1e-5), 0.0), _ShieldRing2.x) * _ShieldRing2.y;
                float op = lerp(_ShieldRing2.z, _ShieldRing2.w, life) * (ring + glow) * 0.5;
                op *= front ? _ShieldFade.x : _ShieldFade.y;
                op *= 1.0 + _ShieldMod.z * (4.0 * m - 1.0);
                op += _ShieldEdge.y * saturate(1.0 - abs(T - e) / max(_ShieldEdge.z, 1e-5)) * past;
                op *= _ShieldTint.a;
                float eye = -TransformWorldToView(i.positionWS).z;
                op *= saturate((eye - _ShieldFade.z) / max(_ShieldDepth.x, 1e-5));
                if (_ShieldDepth.y > 0.0)
                {
                    float raw = SampleSceneDepth(i.positionCS.xy / _ScaledScreenParams.xy);
                    float behind = saturate((LinearEyeDepth(raw, _ZBufferParams) - eye) / _ShieldDepth.y);
                    #if !UNITY_REVERSED_Z
                    if (raw <= 0.0) behind = 1.0;   // (no depth texture: nothing to fade against)
                    #endif
                    op *= behind;
                }
                return half4(max(col, 0.0) * saturate(op) * _ShieldFade.w, 0.0);
            }
            ENDHLSL
        }
    }
}
