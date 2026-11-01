// GoF2/SpaceSky: the flight-level skybox. Level::renderBG draws the stars sphere (opaque) and adds the nebula sphere
// on top, both rotated by the per-station R_sky. Here both layers are baked cubemaps (GoF2 > Bake Space Skies),
// added together and sampled through _SkyRotation (world direction -> baked cube direction), which the built-in
// Skybox/Cubemap can't do (Y rotation only). Set as RenderSettings.skybox, so it also drives ambient + reflections.
// Hand-written because Shader Graph has no skybox target.
// Remake mods (systems.json "skybox", OrbitBuilder.SetupSky): either layer can instead be a mod's image, sampled as it is
// (no baking): a 2:1 panorama (equirectangular, its middle straight ahead: +Z of the sky) or a 6:1 strip of cube faces in
// the order right (+X), left (-X), up (+Y), down (-Y), front (+Z), back (-Z), each as seen from inside facing it (the up
// face with the front at its bottom edge, the down face with the front at its top edge). Mip level 0 (no seam at the
// panorama's wrap, no bleeding between the strip's faces: the face coordinates stay half a texel inside).
// SRP Batcher compatible (2026-10-22): every per-material value in the UnityPerMaterial buffer.
// Remake: the warp (Visuals.WarpSky, globals, so every system's and mod's sky has it): the sky itself is stretched round the
// flight, as EVERSPACE 2's travel mode bends it. The sky is read in polar coordinates about _WarpAxis with the angle squeezed,
// so a little of it runs out in long radial streaks, in two phases half a cycle apart that crossfade so the flow never ends;
// the noise shades it into bands, the centre stays calm. Nothing is added to it: lines over it, spread by bloom, were white
// streaks (the user: only the stretching). At _WarpLevel 0 it is exactly the plain sky, so the warp starts and ends without a
// cut, and everything else draws over it as ever. It takes after the Khador Drive's animation between the systems only
// (_WarpKhador): the sky leans to that fx's ice blue, and that fx's vein web is GoF2/WarpWeb, over the planets. A cruise in an
// orbit is the plain stretch.
// _SkyBlend: another system's sky over this one (World.InterstellarField, approaching it), its layers set as globals with
// runtime modes in place of the keywords, so an arrival finds the sky it was already flying in.
Shader "GoF2/SpaceSky"
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
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" }
        Cull Off ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_local _ _NEBULA_PANORAMA _NEBULA_STRIP
            #pragma multi_compile_local _ _STARS_PANORAMA _STARS_STRIP
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            #include "Assets/Shaders/SpaceSkyCore.hlsl"

            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 world : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings vert(Attributes i)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.world = i.positionOS.xyz;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                half3 c = _WarpLevel > 0.001 ? Warp(normalize(i.world), normalize(_WarpAxis), _WarpLevel, _WarpKhador) : Sky(i.world);
                return half4(c * _Exposure, 1);
            }
            ENDHLSL
        }
    }
}
