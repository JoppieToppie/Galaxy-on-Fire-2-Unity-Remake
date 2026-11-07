// GoF2/ModEs2Particle: EVERSPACE 2's engine particles on a mod ship (Modding.Es2EngineVfx, Modding.LateralThrusters), each
// its material's own formula, decompiled (2026-10-24: the materials' compiled shaders read out of the game's IoStore and
// disassembled; the ES2 importer's research/engine_exhaust.md). _Kind picks one:
//   0 a manoeuvring jet's card (P_Vehicle_LateralThruster, M_Vehicle_LateralThruster): a rectangle along the jet
//     (EPAL_ROTATE_X: turned about it to face the camera), 300 x 80 cm centred on the socket; its shape from the card's
//     centre, d = 2 |uv - 0.5|, s = sat(max(1 - d, 0) x 1.05), s / (1 + 18 (1 - s)), ^ (2 + sqrt(noise(time x 100))), only
//     the half along the jet (sat((u - 0.5) x 10)), x the particle's alpha (Opacity x (1 + 3 Boost)), the colour x 20;
//   1 that jet's "Front" disc, 100 x 100 cm facing along the jet (EPAL_X), 10 cm toward the camera; the same material, so the
//     same half mask in EVERSPACE 2 (a half disc on the socket's own side axis, which the import doesn't keep): drawn whole
//     here (remake pick);
//   2 the engine glow of NPC ships (NS_Vehicle_EngineTrail's NE_Glow, M_Vehicle_EngineTrail_Glow): a camera-facing sprite
//     10 000 cm x the socket's scale, r = max(1 - 2 |uv - 0.5|, 0), (r / (1 + 98 (1 - r)))^2 (Bias 0.01, Power 2): a tight
//     hot core in a faint halo;
//   3 NPC ships' engine trail (its Trail ribbon, M_ShipTrail_001): across a = max(1 - |2v - 1|, 0), opacity
//     (a^4 + sqrt(max((a - 0.9) x 10, 0))) x 0.5 x alpha, the colour x 0.5 (BaseIntensity) x (1 + 19 h^3) with
//     h = max((sat(1 - u) - 0.9) x 10, 0), u the distance from the ribbon's head over 50 m (its 5000 cm tiling): its first 5 m
//     up to 20 x as bright (SourceIntensity);
//   4 the player's veil trail (NS_Vehicle_EngineTrail_Player_Default, M_Vehicle_EngineTrail_Player_Veil; alpha-blended):
//     across a / (1 + kA (1 - a)), kA = 1 / 0.8 - 2, squared; along b = 1 - u, b / (1 + 18 (1 - b)) ^ 1.2 x min(20 u, 1)^2
//     (the dynamic material parameters 0.8, 2, 0.05, 1.2), x the particle's alpha; nothing within 24 cm of the camera,
//     fading in over 5 m (in EVERSPACE 2's metres: _Es2Scale).
//   5 a wing-tip contrail (P_Vehicle_Contrail, M_Vehicle_Contrail; alpha-blended; Modding.Es2Contrails, 2026-11-02): along
//     b = sat(1 - u), b / (1 + 3 (1 - b)); across a = max(1 - |2v - 1|, 0), a / (1 + 8 (1 - a)), squared; x min(20 (1 - b), 1)
//     at the head; x the particle's alpha; nothing within 24 cm of the camera, fading in over 20 m (its pixel depth x 0.0005
//     per cm); x its barcode (T_Pattern_Barcode_002 across the ribbon, a fifth of the texture over its length): stripes
//     the remake can't ship, so the flame noise thresholded into bars along the length stands in. EVERSPACE 2 lights it
//     (volumetric per-vertex): a white fog the scene lights; here the colour the CPU gives it (Es2Contrails.Colour);
//   6 a headlight's lens flare (BP_PawnHeadlight's P_LensFlare_001, a screen-sized flare: InfiniteScreenRadius 0.02, white x 10,
//     opacity its Intensity; Flight.Headlights): a sprite of a fixed share of the screen height (TEXCOORD1.x: the radius),
//     a tight core in a halo with a thin horizontal streak, x the alpha.
// Geometry (built by the CPU each frame): POSITION the anchor (a socket, a ribbon point), NORMAL the jet's way / the ribbon's
// tangent (object space), TEXCOORD0 the uv, TEXCOORD1 the half sizes (jets: along, across; the disc: half size, camera offset;
// the glow: half size; ribbons: half width, side -1 / 1), COLOR the colour (HDR) and alpha. The colours are EVERSPACE 2's
// emissive x _Gain (its exposure against the remake's: ModMaterials.Es2FlameGain / 1000).
Shader "GoF2/ModEs2Particle"
{
    Properties
    {
        _Kind ("Kind (0 jet card, 1 jet disc, 2 glow, 3 NPC trail, 4 veil, 5 contrail, 6 flare)", Float) = 0
        _Gain ("EVERSPACE 2's emissive to the remake's", Float) = 0.3
        _NoiseMap ("Noise (the jets' flicker)", 2D) = "grey" {}
        _BoostColor ("The jets' colour (a kit colour slot's boost target sets it)", Color) = (0.05, 0.405164, 1, 1)
        _Es2Scale ("Unity metres per EVERSPACE 2 metre", Float) = 3.6
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst blend", Float) = 1
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" "RenderPipeline" = "UniversalPipeline" }
        Blend [_SrcBlend] [_DstBlend]
        ZWrite Off
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_NoiseMap); SAMPLER(sampler_NoiseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _NoiseMap_ST;
                float4 _BoostColor;
                float _Kind, _Gain, _Es2Scale, _SrcBlend, _DstBlend;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; float2 size : TEXCOORD1; float4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; float3 positionWS : TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings vert(Attributes i)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                int kind = (int)round(_Kind);
                float3 p = TransformObjectToWorld(i.positionOS.xyz);
                float scale = length(float3(UNITY_MATRIX_M[0].x, UNITY_MATRIX_M[1].x, UNITY_MATRIX_M[2].x));
                float3 axis = TransformObjectToWorldDir(i.normalOS);
                float3 view = normalize(_WorldSpaceCameraPos - p);
                float2 c = i.uv * 2 - 1;   // the corner, -1..1
                if (kind == 0)
                {
                    // along the jet, turned about it to face the camera
                    float3 side = cross(axis, view);
                    side = length(side) > 1e-5 ? normalize(side) : normalize(cross(axis, float3(0, 1, 0.01)));
                    p += (axis * c.x * i.size.x + side * c.y * i.size.y) * scale;
                }
                else if (kind == 1)
                {
                    // facing along the jet, a little toward the camera
                    float3 right = cross(axis, abs(axis.y) < 0.99 ? float3(0, 1, 0) : float3(1, 0, 0));
                    right = normalize(right);
                    float3 up = cross(right, axis);
                    p += (right * c.x + up * c.y) * i.size.x * scale + view * i.size.y * scale;
                }
                else if (kind == 2)
                {
                    // facing the camera
                    float3 right = UNITY_MATRIX_V[0].xyz, up = UNITY_MATRIX_V[1].xyz;
                    p += (right * c.x + up * c.y) * i.size.x * scale;
                }
                else if (kind == 6)
                {
                    // a screen-sized flare: the corners pushed out in clip space by the radius (a share of the screen height)
                    o.positionCS = TransformWorldToHClip(p);
                    float aspect = _ScreenParams.y / max(_ScreenParams.x, 1);
                    o.positionCS.xy += c * float2(aspect, 1) * i.size.x * 2 * o.positionCS.w;
                    o.positionWS = p;
                    o.uv = i.uv;
                    o.color = i.color;
                    return o;
                }
                else
                {
                    // a ribbon point: across its tangent, facing the camera
                    float3 side = cross(axis, view);
                    side = length(side) > 1e-5 ? normalize(side) : float3(0, 0, 0);
                    p += side * i.size.x * i.size.y * scale;
                }
                o.positionCS = TransformWorldToHClip(p);
                o.positionWS = p;
                o.uv = i.uv;
                o.color = i.color;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                int kind = (int)round(_Kind);
                float2 uv = i.uv;
                if (kind <= 1)
                {
                    float d = length(uv - 0.5) * 2;
                    float s = saturate(max(1 - d, 0) * 1.05);
                    s = s / (1 + 18 * (1 - s));
                    float n = SAMPLE_TEXTURE2D(_NoiseMap, sampler_NoiseMap, float2(frac(_Time.y * 100), 0.5)).r;
                    s = s > 2.98e-8 ? pow(s, n > 2.98e-8 ? sqrt(n) + 2 : 2) : 0;
                    if (kind == 0) s *= saturate((uv.x - 0.5) * 10);
                    s = saturate(s * i.color.a);
                    return half4(i.color.rgb * s * _Gain, 0);
                }
                if (kind == 2)
                {
                    float r = max(1 - length(uv - 0.5) * 2, 0);
                    r = r / (1 + 98 * (1 - r));
                    return half4(i.color.rgb * r * r * i.color.a * _Gain, 0);
                }
                if (kind == 6)
                {
                    float2 d6 = uv * 2 - 1;
                    float r = saturate(1 - length(d6));
                    float core = pow(r, 6);
                    float halo = r * r * 0.25;
                    float streak = pow(saturate(1 - abs(d6.y) * 8), 2) * pow(saturate(1 - abs(d6.x)), 1.5) * 0.5;
                    float f = saturate(core + halo + streak) * i.color.a;
                    return half4(i.color.rgb * f * _Gain, f);
                }
                float a = max(1 - abs(uv.y * 2 - 1), 0);
                if (kind == 3)
                {
                    float h = max((saturate(1 - uv.x) - 0.9) * 10, 0);
                    float op = (pow(a, 4) + sqrt(max((a - 0.9) * 10, 0))) * 0.5 * i.color.a;
                    return half4(i.color.rgb * 0.5 * (1 + 19 * h * h * h) * op * _Gain, 0);
                }
                if (kind == 5)
                {
                    // the contrail
                    float A5 = a / (1 + 8 * (1 - a));
                    A5 = A5 * A5;
                    float b5 = saturate(1 - uv.x);
                    float B5 = b5 / (1 + 3 * (1 - b5));
                    float head5 = min((1 - b5) * 20, 1);
                    float n5 = SAMPLE_TEXTURE2D(_NoiseMap, sampler_NoiseMap, float2(uv.x * 0.2 + 0.5, 0.5)).r;
                    float bars = saturate((n5 - 0.35) * 4);
                    float op5 = A5 * B5 * head5 * bars * i.color.a;
                    float dist5 = length(i.positionWS - _WorldSpaceCameraPos) / max(_Es2Scale, 1e-4);
                    op5 = saturate(op5 * saturate((dist5 - 0.24) * 0.05));
                    return half4(i.color.rgb, op5);
                }
                // the veil
                float kA = 1 / 0.8 - 2;
                float A = a / (1 + kA * (1 - a));
                A = A * A;
                float b = saturate(1 - uv.x);
                float B = b / (1 + 18 * (1 - b));
                B = B > 2.98e-8 ? pow(B, 1.2) : 0;
                float head = min((1 - b) * 20, 1);
                float op = A * B * head * head * i.color.a;
                float dist = length(i.positionWS - _WorldSpaceCameraPos) / max(_Es2Scale, 1e-4);
                op = saturate(op * saturate((dist - 0.24) * 0.2));
                return half4(i.color.rgb * _Gain, op);
            }
            ENDHLSL
        }
    }
}
