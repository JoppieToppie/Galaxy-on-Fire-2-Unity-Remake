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
        [NoScaleOffset] _ChorusStarsMap ("Chorus star mask", 2D) = "black" {}
        [NoScaleOffset] _ChorusLayer1 ("Chorus layer 1", 2D) = "black" {}
        [NoScaleOffset] _ChorusLayer2 ("Chorus layer 2", 2D) = "black" {}
        [NoScaleOffset] _ChorusLayer3 ("Chorus layer 3", 2D) = "black" {}
        [NoScaleOffset] _ChorusLayer4 ("Chorus layer 4", 2D) = "black" {}
        [NoScaleOffset] _ChorusLayer5 ("Chorus layer 5", 2D) = "black" {}
        [NoScaleOffset] _ChorusLayer6 ("Chorus layer 6", 2D) = "black" {}
        [HideInInspector] _ChorusGradUp ("_ChorusGradUp", Vector) = (0, 0, 0, 0)
        [HideInInspector] _ChorusGradA ("_ChorusGradA", Vector) = (0, 0, 0, 0)
        [HideInInspector] _ChorusGradB ("_ChorusGradB", Vector) = (0, 0, 0, 0)
        [HideInInspector] _ChorusStars ("_ChorusStars", Vector) = (0, 0, 0, 0)
        [HideInInspector] _ChorusCurve0 ("_ChorusCurve0", Vector) = (0, 0, 0, 0)
        [HideInInspector] _ChorusCurve1 ("_ChorusCurve1", Vector) = (0, 0, 0, 0)
        [HideInInspector] _ChorusMisc ("_ChorusMisc", Vector) = (0, 0, 0, 0)
        [HideInInspector] _ChorusStarsB ("_ChorusStarsB", Vector) = (0, 0, 0, 0)
        [HideInInspector] _ChorusStarsW ("_ChorusStarsW", Vector) = (1, 0, 0, 0)
        [HideInInspector] _ChorusSun ("_ChorusSun", Vector) = (0, 0, 1, 0)
        [HideInInspector] _ChorusSunColor ("_ChorusSunColor", Vector) = (1, 1, 1, 0)
        [HideInInspector] _ChorusL1A ("_ChorusL1A", Vector) = (0, 0, 0, 0)
        [HideInInspector] _ChorusL1B ("_ChorusL1B", Vector) = (0, 0, 0, 0)
        [HideInInspector] _ChorusL1C ("_ChorusL1C", Vector) = (0, 0, 0, 0)
        [HideInInspector] _ChorusL1D ("_ChorusL1D", Vector) = (0, 0, 0, 0)
        [HideInInspector] _ChorusL1E ("_ChorusL1E", Vector) = (0, 0, 0, 0)
        [HideInInspector] _ChorusL2A ("_ChorusL2A", Vector) = (0, 0, 0, 0)
        [HideInInspector] _ChorusL2B ("_ChorusL2B", Vector) = (0, 0, 0, 0)
        [HideInInspector] _ChorusL2C ("_ChorusL2C", Vector) = (0, 0, 0, 0)
        [HideInInspector] _ChorusL2D ("_ChorusL2D", Vector) = (0, 0, 0, 0)
        [HideInInspector] _ChorusL2E ("_ChorusL2E", Vector) = (0, 0, 0, 0)
        [HideInInspector] _ChorusL3A ("_ChorusL3A", Vector) = (0, 0, 0, 0)
        [HideInInspector] _ChorusL3B ("_ChorusL3B", Vector) = (0, 0, 0, 0)
        [HideInInspector] _ChorusL3C ("_ChorusL3C", Vector) = (0, 0, 0, 0)
        [HideInInspector] _ChorusL3D ("_ChorusL3D", Vector) = (0, 0, 0, 0)
        [HideInInspector] _ChorusL3E ("_ChorusL3E", Vector) = (0, 0, 0, 0)
        [HideInInspector] _ChorusL4A ("_ChorusL4A", Vector) = (0, 0, 0, 0)
        [HideInInspector] _ChorusL4B ("_ChorusL4B", Vector) = (0, 0, 0, 0)
        [HideInInspector] _ChorusL4C ("_ChorusL4C", Vector) = (0, 0, 0, 0)
        [HideInInspector] _ChorusL4D ("_ChorusL4D", Vector) = (0, 0, 0, 0)
        [HideInInspector] _ChorusL4E ("_ChorusL4E", Vector) = (0, 0, 0, 0)
        [HideInInspector] _ChorusL5A ("_ChorusL5A", Vector) = (0, 0, 0, 0)
        [HideInInspector] _ChorusL5B ("_ChorusL5B", Vector) = (0, 0, 0, 0)
        [HideInInspector] _ChorusL5C ("_ChorusL5C", Vector) = (0, 0, 0, 0)
        [HideInInspector] _ChorusL5D ("_ChorusL5D", Vector) = (0, 0, 0, 0)
        [HideInInspector] _ChorusL5E ("_ChorusL5E", Vector) = (0, 0, 0, 0)
        [HideInInspector] _ChorusL6A ("_ChorusL6A", Vector) = (0, 0, 0, 0)
        [HideInInspector] _ChorusL6B ("_ChorusL6B", Vector) = (0, 0, 0, 0)
        [HideInInspector] _ChorusL6C ("_ChorusL6C", Vector) = (0, 0, 0, 0)
        [HideInInspector] _ChorusL6D ("_ChorusL6D", Vector) = (0, 0, 0, 0)
        [HideInInspector] _ChorusL6E ("_ChorusL6E", Vector) = (0, 0, 0, 0)
        [NoScaleOffset] _StarIndex ("Enhanced stars: grid", 2D) = "black" {}
        [NoScaleOffset] _StarData ("Enhanced stars: data", 2D) = "black" {}
        [HideInInspector] _StarGrid ("_StarGrid", Vector) = (0, 0, 0, 0)
    }
    SubShader
    {
        Tags { "Queue" = "Geometry-100" "RenderType" = "Opaque" "IgnoreProjector" = "True" }
        Cull Off

        HLSLINCLUDE
        #pragma multi_compile_instancing
        #pragma multi_compile_local _ _NEBULA_PANORAMA _NEBULA_STRIP
        #pragma multi_compile_local _ _STARS_PANORAMA _STARS_STRIP
        #pragma multi_compile_local _ _CHORUS_SKY
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
