// GoF2/RebuiltPlanet: an orbit planet rebuilt as a lit sphere from its png (World.RebuiltPlanets, Options > Graphics "3D planets"),
// the star map's and the space between the systems' planets (UI.StarMap, World.InterstellarField): in the scene's own depth.
// The sums in RebuiltPlanet.hlsl. In Resources so a build keeps it.
Shader "GoF2/RebuiltPlanet"
{
    Properties
    {
        _Albedo ("Surface (equirect, sqrt(x / 4))", 2D) = "black" {}
        _Face ("Face (the png de-lit, sqrt(x / 4))", 2D) = "black" {}
        _Corr ("Face to surface colour (log2 / 8 + 0.5)", 2D) = "gray" {}
        _Emission ("Emission (equirect, sqrt(x / 2))", 2D) = "black" {}
        _Night ("Night layer (equirect, x * 2 + 0.5)", 2D) = "gray" {}
        _Glow ("Glow (equirect, sqrt(x / 2))", 2D) = "black" {}
        _Curves ("Light, glow and grade curves", 2D) = "black" {}
        _Halo ("Halo table", 2D) = "black" {}
        _Cover ("Edge cover table", 2D) = "white" {}
        _Ring ("Ring as the png shows it (angle x radius, premultiplied)", 2D) = "black" {}
        _RingProfile ("3D ring band profile (premultiplied)", 2D) = "black" {}
        _RingProfileR ("3D ring profile radii (body radii)", Vector) = (1, 2, 0, 0)
        _LightDir ("Toward the light (world; w 2: the star map's sun)", Vector) = (0, 0, -1, 0)
        _FaceLight ("The png's light (png frame)", Vector) = (0, 0, 1, 0)
        _Curve ("Curve bands (first, step, count)", Vector) = (-0.975, 0.05, 40, 0)
        _Knobs ("Ambient, gain, halo from, peak", Vector) = (1, 1, 0.95, 1)
        _RimColor ("Rim colour, strength", Vector) = (0, 0, 0, 0)
        _RimShape ("Rim power", Vector) = (1, 0, 0, 0)
        _Grade ("Grade top, on", Vector) = (1, 0, 0, 0)
        _GradeGain ("Grade gain", Vector) = (1, 1, 1, 1)
        _HaloR ("Halo radii", Vector) = (0.88, 1.15, 0, 0)
        _Layers ("Emission, night, glow there", Vector) = (0, 0, 0, 0)
        _RingPole ("Ring pole (png frame), w ring there", Vector) = (0, 1, 0, 0)
        _RingR ("Ring radii (plane), quad reach, body radii per plane unit", Vector) = (0.4, 1.12, 1.16, 1)
        _RingE1 ("Ring plane x (png frame), near half's y sign", Vector) = (1, 0, 0, 1)
        _RingE2 ("Ring plane y (png frame)", Vector) = (0, 0, 1, 0)
        _RingM0 ("Png to ring plane row 0", Vector) = (1, 0, 0, 0)
        _RingM1 ("Png to ring plane row 1", Vector) = (0, 1, 0, 0)
        _RingM2 ("Png to ring plane row 2", Vector) = (0, 0, 1, 0)
        _FaceHalf ("Face square half size", Float) = 1.05
        _Mirror ("Mirror", Float) = 0
        _Debug ("Debug (1 surface .. 7 halo)", Float) = 0
        _Color ("Fade (a)", Color) = (1, 1, 1, 1)
        _Tint ("Added tint", Color) = (0, 0, 0, 0)
    }
    SubShader
    {
        Tags { "Queue" = "Transparent-10" "RenderType" = "Transparent" "IgnoreProjector" = "True" "RenderPipeline" = "UniversalPipeline" }
        Blend One OneMinusSrcAlpha
        ZWrite On
        ZTest LEqual
        Cull Off

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex RebuiltVert
            #pragma fragment RebuiltFrag
            #pragma target 3.5
            
            #include "RebuiltPlanet.hlsl"
            ENDHLSL
        }
    }
}
