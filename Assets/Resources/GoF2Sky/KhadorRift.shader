// GoF2/KhadorRift: the hole the Khador Slipstream tears in the orbit (World.KhadorFx). The original's lightning ring (khador_jump
// part 18) is its rim, and inside it is the space between the systems already: GoF2/SpaceSky's warp and the warp's vein web
// from the same code (SpaceSkyCore / WarpWebCore), turned by _RiftTurn to the heading the next level opens on. So when the
// hole fills the view the frame is the next level's first, and the swap has nothing to hide.
// It draws first, before every opaque, with the depth of a plane just past the ship (_RiftDepth, metres along the view): the
// orbit behind that plane fails the depth test and is gone, and the ship, its flames and anything nearer draw over the hole
// as ever, also once the ship has flown through it. The sky and the Backdrop's planets draw at the far plane behind it. It is a
// tear, not a circle: the same rim as the ring's lightning (KhadorRim).
Shader "GoF2/KhadorRift"
{
    Properties
    {
        [NoScaleOffset] _Stars ("Stars", Cube) = "black" {}
        [NoScaleOffset] _Nebula ("Nebula (added)", Cube) = "black" {}
        [NoScaleOffset] _StarsMap ("Stars (mod image)", 2D) = "black" {}
        [NoScaleOffset] _NebulaMap ("Nebula (mod image)", 2D) = "black" {}
        _StarsGain ("Stars brightness", Float) = 1
        _NebulaGain ("Nebula brightness", Float) = 1
        _Exposure ("Exposure", Float) = 1
    }
    SubShader
    {
        Tags { "Queue" = "Geometry-100" "RenderType" = "Opaque" "IgnoreProjector" = "True" }
        Cull Off

        HLSLINCLUDE
        #pragma multi_compile_instancing
        #pragma multi_compile_local _ _NEBULA_PANORAMA _NEBULA_STRIP
        #pragma multi_compile_local _ _STARS_PANORAMA _STARS_STRIP
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Assets/Shaders/SpaceSkyCore.hlsl"
        #include "Assets/Shaders/WarpWebCore.hlsl"
        #include "Assets/Shaders/KhadorNoise.hlsl"

        float4x4 _RiftTurn;     // this level's directions -> the next level's
        float3 _RiftAxis;       // the ship's heading there
        float _RiftDepth;       // metres along the view
        float _RiftRadius;      // of the mesh's own unit disc
        float _RiftSeconds;

        struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 world : TEXCOORD0;
            float2 disc : TEXCOORD1;
            UNITY_VERTEX_OUTPUT_STEREO
        };

        Varyings vert(Attributes i)
        {
            Varyings o;
            UNITY_SETUP_INSTANCE_ID(i);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
            o.world = TransformObjectToWorld(i.positionOS.xyz);
            o.disc = i.positionOS.xy;
            o.positionCS = TransformWorldToHClip(o.world);
            float4 plane = TransformWViewToHClip(float3(0, 0, -_RiftDepth));
            o.positionCS.z = plane.z / plane.w * o.positionCS.w;
            return o;
        }

        void Rim(Varyings i)
        {
            float r = length(i.disc);
            float turn = atan2(i.disc.y, i.disc.x) / (2.0 * PI);
            clip(_RiftRadius * KhadorRim(turn, _RiftSeconds) - r);
        }
        ENDHLSL

        Pass
        {
            Name "Rift"
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                Rim(i);
                float3 view = normalize(mul((float3x3)_RiftTurn, i.world - _WorldSpaceCameraPos));
                float3 axis = normalize(_RiftAxis);
                half3 c = Warp(view, axis, 1.0, 1.0) * _Exposure + WarpWeb(view, axis, 1.0, 1.0, _WarpTime);
                return half4(c, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            half4 frag(Varyings i) : SV_Target
            {
                Rim(i);
                return 0;
            }
            ENDHLSL
        }
    }
}
