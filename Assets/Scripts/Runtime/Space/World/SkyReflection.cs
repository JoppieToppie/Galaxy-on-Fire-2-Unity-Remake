// SkyReflection.cs
// The ambient light of the current skybox (remake): DynamicGI.UpdateEnvironment after a level sets RenderSettings.skybox.
// The reflection the hulls show (URP Lit's environment reflection): the system's reflection cubemap, like the original
// (MGame::OnInitialize / ModMainMenu::OnInitialize: TextureCreate(0x2efe + the system's textureIndex), 0x2f08 in the alien
// orbit, then PaintCanvas::ChangeCubeTexture; BumpRimCubeShader adds it to the hulls weighted by their specular map). The
// cubemaps are Resources/GoF2Reflections (Textures/*/cubemaps' cubemap_skybox_000..018, glossy convolution), set as
// Unity's custom environment reflection. In a station the last one bound stays (the original never binds the hangar
// cubemaps 0x2ef4..0x2ef7): the station's system's. There used to be a realtime ReflectionProbe of the sky here, but both
// quality levels have Realtime Reflection Probes off, so it never rendered (rendered, it would reflect the black space sky).

using UnityEngine;
using UnityEngine.Rendering;

namespace GoF2Remake.World
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]   // black: a DontSave cubemap kept for the run, made again when gone
    public static class SkyReflection
    {
        /// <summary>The ambient light from RenderSettings.skybox (call after changing it).</summary>
        public static void Update()
        {
            DynamicGI.UpdateEnvironment();
        }

        /// <summary>The system's reflection cubemap as the environment reflection: 0x2efe + the system's texture index, the
        /// alien orbit 0x2f08 (index 10).</summary>
        public static void SetSystem(OrbitLayout layout)
        {
            if (layout == null) return;
            var cube = GoF2Remake.Visuals.OriginalSkies.Reflection(SystemCubeName(layout));
            Shader.SetGlobalFloat(ReflectionId, 1f);
            Shader.SetGlobalFloat(PbrDimId, 0f);   // space, the lounge, a mod's own hangar: the mods' hulls as URP lights them
            Shader.SetGlobalVector(PbrLightId, Vector4.zero);
            Shader.SetGlobalColor(RimId, RimColor(layout));
            if (cube == null) return;
            // Remake: also as a global of its own, which GoF2/Hull and GoF2/EmissiveCube sample. unity_SpecCube0 (the custom
            // environment reflection) came out empty in the Android build (Forward on the Mobile renderer, per-object probe
            // data), so the hulls had no reflection there; the PC's Forward+ fills it.
            Shader.SetGlobalTexture(CubeId, cube);
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            RenderSettings.customReflectionTexture = cube;
        }

        /// <summary>The system's reflection cubemap in Resources/GoF2Reflections: 0x2efe + the system's texture index, the
        /// alien orbit 0x2f08 (index 10). Modified: public for HangarCubemapCapture.</summary>
        public static string SystemCubeName(OrbitLayout layout)
        {
            int index = layout.alienOrbit ? 10 : layout.systemTexture;
            return index <= 10 ? $"cubemap_skybox_{index:000}" : index <= 14 ? $"v_cubemap_skybox_{index:000}" : $"sn_cubemap_skybox_{index:000}";
        }

        /// <summary>Remake: in a hangar its own race's cubemap (Resources/GoF2Reflections/cubemap_hangar_*, the original's
        /// unused 0x2ef4..0x2ef7) instead of the system's: Terran, Vossk, Nivelian, Midorian (StationTables.HangarIndex
        /// 0..3, also in the Supernova systems). The Valkyrie hangars (7, 8) have none yet: a black cubemap, no reflection
        /// (the rim light stays). Call after SetSystem, which the lounge and space go back to.</summary>
        public static void SetHangar(int hangarIndex)
        {
            // The mods' PBR hulls (GoF2/Hull with _GOF2_PBR) keep less of the room's indirect light: without an occlusion
            // map the flat ambient and the bright room cubemap made them flat and hazy (the game's hulls are unchanged).
            Shader.SetGlobalFloat(PbrDimId, HangarPbrIndirectDim);
            // ...and are lit from above instead of from the camera's side (their textures have no shading painted in).
            Shader.SetGlobalVector(PbrLightId, new Vector4(HangarPbrLightToward.x, HangarPbrLightToward.y, HangarPbrLightToward.z, 1f));
            Shader.SetGlobalColor(PbrLightColorId, new Color(HangarPbrLightStrength, HangarPbrLightStrength, HangarPbrLightStrength, 1f));
            Cubemap cube;
            if (hangarIndex >= 0 && hangarIndex < HangarCubes.Length)
            {
                cube = GoF2Remake.Visuals.OriginalSkies.Reflection(HangarCubes[hangarIndex]);
                if (cube == null) return;   // not found: the system's stays
            }
            else if (hangarIndex == 7 || hangarIndex == 8)
                // Modified: the Valkyrie hangars' own, once captured (HangarCubemapCapture), else none.
                cube = GoF2Remake.Visuals.OriginalSkies.Reflection(HangarCubeName(hangarIndex)) ?? Black;
            else return;
            Shader.SetGlobalTexture(CubeId, cube);
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            RenderSettings.customReflectionTexture = cube;
        }

        static readonly string[] HangarCubes = { "cubemap_hangar_terran", "cubemap_hangar_vossk", "cubemap_hangar_nivelian", "cubemap_hangar_midorian" };

        /// <summary>Modified: a hangar's reflection cubemap in Resources/GoF2Reflections (HangarCubemapCapture writes it):
        /// the four races' and the Valkyrie hangars' (7 deep science, 8 battlestation); null for the others.</summary>
        public static string HangarCubeName(int hangarIndex) =>
            hangarIndex >= 0 && hangarIndex < HangarCubes.Length ? HangarCubes[hangarIndex]
            : hangarIndex == 7 ? "cubemap_hangar_deep_science" : hangarIndex == 8 ? "cubemap_hangar_battlestation" : null;

        static Cubemap black;
        static Cubemap Black
        {
            get
            {
                if (black != null) return black;
                black = new Cubemap(1, TextureFormat.RGBA32, false) { name = "GoF2BlackReflection", hideFlags = HideFlags.DontSave };
                for (int f = 0; f < 6; f++) black.SetPixel((CubemapFace)f, 0, 0, Color.black);
                black.Apply(false, true);
                return black;
            }
        }

        static readonly int ReflectionId = Shader.PropertyToID("_GoF2HullReflection"), RimId = Shader.PropertyToID("_GoF2HullRimColor"),
            CubeId = Shader.PropertyToID("_GoF2ReflectionCube"), PbrDimId = Shader.PropertyToID("_GoF2PbrIndirectDim"),
            PbrLightId = Shader.PropertyToID("_GoF2PbrLight"), PbrLightColorId = Shader.PropertyToID("_GoF2PbrLightColor");

        /// <summary>Where a mod's PBR hull is lit from in a GoF2 hangar (toward the light, Unity world: above, the hangar
        /// camera's left and toward it, matched by eye to GoF3's own render of the Cronus) and how strongly against the
        /// hangar's key light (HullForwardPass.hlsl).</summary>
        public static readonly Vector3 HangarPbrLightToward = new Vector3(0.6f, 0.6f, 0.5f);
        public const float HangarPbrLightStrength = 1f;

        /// <summary>How much of the indirect light a mod's PBR hull loses in a GoF2 hangar (HullForwardPass.hlsl).</summary>
        public const float HangarPbrIndirectDim = 0.7f;

        /// <summary>StarSystem::initLight: LightSetRimColor(3 x DAT_00258d34[sysTex]): the sun colour table but for systems 11
        /// and 15 (GoF2/Hull's rim light).</summary>
        static Color RimColor(OrbitLayout layout)
        {
            var c = layout.systemTexture == 11 ? new Color(0.20f, 0.11f, 0.04f)
                  : layout.systemTexture == 15 ? new Color(0.50f, 0.26f, 0.12f)
                  : layout.sunColor;
            return new Color(c.r * 3f, c.g * 3f, c.b * 3f, 1f);
        }
    }
}
