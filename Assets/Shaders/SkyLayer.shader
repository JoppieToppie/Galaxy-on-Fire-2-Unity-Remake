// GoF2/SkyLayer: the extra sky layers of Level::renderBG (Reference/research/space_backdrop.md, skybox layers): the planet
// ring sky (alpha), the storms and the supernova flares (additive) and the asteroid belt (alpha + LIGHT0, blend 8). They
// are camera-centred meshes drawn with the depth test off in the original; here their depth is pushed onto the far plane
// like GoF2/Backdrop, so painter's order comes from the render queue and every object stays in front.
// _Fade is the part animation's `extra` channel (0..1: RGB for additive layers, alpha otherwise), _UVOffset its `v5_0`
// UV scroll. _Lit (the belt) lights the texture by _LightDir / _LightColor plus _Ambient.
// Remake: the belt's mesh normals point both ways (face by face), and the light took abs(N.L): the whole belt was lit, as
// if the sun sat in the middle of it. The belt is a band around the camera, so the face we see is the one toward the
// camera: lit where that face turns to the sun (the far side of the belt, opposite the sun), in shadow on the sun's side
// (its asteroids are seen against the light), the ambient (the sun's colour) on top.
// Remake: and its normal map and specular, like the original's bump shader 28 (material 27160: skybox_asteroid_belt_diffuse +
// _normal_specular): each asteroid lit on its sun side and shaded on the other, with a highlight where the specular
// map (_SpecMap: the _metallic_smoothness alpha) is set. The tangent frame comes from the screen derivatives around the
// face toward the camera (the mesh's own normals point both ways).
// Remake: _Cull (default Off): the planet ring sky culls its back faces (SkyLayers).
// Remake: _Polar (the planet ring sky, SkyLayers.PolarRing): its mesh is 64 long segments over a quarter-circle texture
// of concentric bands; interpolated straight across each triangle the bands came out as chords (the rings looked
// faceted up close). Here a band's radius comes from the pixel's distance to the ring's centre (_PolarCenter, object
// space; _PolarRadii = inner and outer radius, then their radii in the texture) and only the angle from the mesh's UV
// (around _PolarUV, the texture's centre corner): every band is a true circle.
Shader "GoF2/SkyLayer"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        [HDR] _Color ("Color", Color) = (1, 1, 1, 1)
        _Fade ("Fade", Float) = 1
        _UVOffset ("UV offset", Vector) = (0, 0, 0, 0)
        _UseVertexColor ("Multiply vertex colour", Float) = 0
        _Lit ("Lit", Float) = 0
        _LightDir ("Light direction (toward the light)", Vector) = (0, 1, 0, 0)
        [HDR] _LightColor ("Light colour", Color) = (1, 1, 1, 1)
        _Ambient ("Ambient", Color) = (0.2, 0.2, 0.2, 1)
        [Normal] _BumpMap ("Normal map (lit layers)", 2D) = "bump" {}
        _SpecMap ("Specular in alpha (lit layers)", 2D) = "black" {}
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 10
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 0
        _Polar ("Polar ring mapping", Float) = 0
        _PolarCenter ("Ring centre (object space)", Vector) = (0, 0, 0, 0)
        _PolarRadii ("Ring radii: inner, outer, texture inner, texture outer", Vector) = (0, 1, 0, 1)
        _PolarUV ("Texture centre (UV)", Vector) = (1, 0, 0, 0)
    }
    SubShader
    {
        Tags { "Queue" = "Transparent-100" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend [_SrcBlend] [_DstBlend]
        ZWrite Off
        ZTest LEqual
        Cull [_Cull]

        Pass
        {
            // Drawn by BackdropPass after the skybox, before the opaque texture copy (the cloak refracts it).
            Tags { "LightMode" = "GoF2Backdrop" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/Shaders/GoF2Deband.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
            TEXTURE2D(_SpecMap); SAMPLER(sampler_SpecMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Color;
                float _Fade;
                float4 _UVOffset;
                float _UseVertexColor;
                float _Lit;
                float4 _LightDir;
                half4 _LightColor;
                half4 _Ambient;
                float _Polar;
                float4 _PolarCenter, _PolarRadii, _PolarUV;
                float _SrcBlend, _DstBlend;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; half4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 normalWS : TEXCOORD1; half4 color : COLOR; float3 positionOS : TEXCOORD2; float3 positionWS : TEXCOORD3; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings vert(Attributes i)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                #if UNITY_REVERSED_Z
                    o.positionCS.z = o.positionCS.w * 1e-6;
                #else
                    o.positionCS.z = o.positionCS.w * (1 - 1e-6);
                #endif
                // The texture moves toward +u as v5_0 grows (the supernova flares stream out of the supernova; added, they
                // streamed into it).
                o.uv = i.uv - _UVOffset.xy;
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.positionOS = i.positionOS.xyz;
                o.positionWS = TransformObjectToWorld(i.positionOS.xyz);
                // The supernova flares fade out toward their edges through their vertex colours (0..0.5): the original
                // draws a mesh's colour array whenever it has one.
                o.color = _UseVertexColor > 0.5 ? i.color : half4(1, 1, 1, 1);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float2 uv = i.uv;
                if (_Polar > 0.5)
                {
                    float rho = saturate((length(i.positionOS - _PolarCenter.xyz) - _PolarRadii.x) / max(_PolarRadii.y - _PolarRadii.x, 1e-5));
                    float2 d = uv - _PolarUV.xy;
                    float phi = atan2(d.y, d.x);
                    uv = _PolarUV.xy + lerp(_PolarRadii.z, _PolarRadii.w, rho) * float2(cos(phi), sin(phi));
                }
                half4 t = SAMPLE_TEXTURE2D_GRAD(_MainTex, sampler_MainTex, uv, ddx(i.uv), ddy(i.uv));
                t.rgb = GoF2Deband(t.rgb, i.positionCS.xy);   // (HDR: the RGB565 steps dithered)
                t *= _Color * i.color;
                if (_Lit > 0.5)
                {
                    // The face toward the camera (the camera sits inside the belt).
                    float3 v = normalize(_WorldSpaceCameraPos - i.positionWS);
                    // Its tangent frame from the screen derivatives (a cotangent frame), the normal map on it.
                    float3 dp1 = ddx(i.positionWS), dp2 = ddy(i.positionWS);
                    float2 duv1 = ddx(i.uv), duv2 = ddy(i.uv);
                    float3 dp2perp = cross(dp2, v), dp1perp = cross(v, dp1);
                    float3 T = dp2perp * duv1.x + dp1perp * duv2.x;
                    float3 B = dp2perp * duv1.y + dp1perp * duv2.y;
                    float scale = rsqrt(max(max(dot(T, T), dot(B, B)), 1e-20));
                    half3 nTS = UnpackNormal(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, i.uv));
                    // The relief's tilt comes out mirrored on this mesh (its UVs run against the cotangent frame): both
                    // tangent axes reversed, so each asteroid's lit side faces the sun.
                    half3 n = normalize(-T * scale * nTS.x - B * scale * nTS.y + v * nTS.z);
                    half3 l = normalize(_LightDir.xyz);
                    half ndl = saturate(dot(n, l));
                    half spec = SAMPLE_TEXTURE2D(_SpecMap, sampler_SpecMap, i.uv).a;
                    half3 h = normalize(l + v);
                    half highlight = pow(saturate(dot(n, h)), exp2(10 * spec + 1)) * spec * ndl;
                    // The original's fixed-function lighting saturates at 1 (LDR): no HDR values, so no bloom haze.
                    t.rgb = saturate(t.rgb * (_LightColor.rgb * ndl + _Ambient.rgb) + _LightColor.rgb * highlight);
                }
                if (abs(_DstBlend - 1) < 0.5) t.rgb *= _Fade;   // additive (ONE, ONE): the fade darkens
                else t.a *= _Fade;
                return t;
            }
            ENDHLSL
        }
    }
}
