// ModAssets.cs
// What the game needs at run time to build a mod's ship (Resources/GoF2Mods/ModAssets, made by "GoF2 > Build > Mod
// Assets", ModAssetsBuilder): the player engine glow material every ship's exhaust uses (mat_34813, GoF2/Additive: also
// the template of the throttle glows and their trails) and engineGlowTint, the same glow on a white copy of its texture
// for exhausts with their own colour (ships.json glowColor / engineGlowColor, tinted by vertex colour), and the URP Lit
// template materials a mod's materials are copied from. The templates fix the shader keywords (normal map, metallic /
// smoothness map, emission always on; opaque / alpha clip / transparent, each with and without URP's detail maps), so
// those variants are in every build (a material made at run time from Shader.Find would need variants the build
// stripped). metallicRoughness converts a glTF metallic-roughness texture (B metal, G roughness) into URP's metallic (R) /
// smoothness (A) map. fxAdditive / fxAlpha: the GoF2 Shader Graph materials a mod weapon's sprites and textured models
// are copied from (ModWeapons). engineFlame: GoF2/ModEngineFlame, the template of the "flame" materials (EngineFlame); hologram: GoF2/ModHologram, the
// template of the "hologram" materials; nozzleShimmer: GoF2/ModNozzleShimmer, of the "shimmer" materials; glow:
// GoF2/ModGlow, of the "additive" materials.

using UnityEngine;

namespace GoF2Remake.Modding
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public class ModAssets : ScriptableObject
    {
        public const string ResourcePath = "GoF2Mods/ModAssets";

        [Tooltip("The player ships' engine glow (mat_34813_ship_engine_glow, GoF2/Additive).")]
        public Material engineGlow;
        [Tooltip("engineGlow on a white copy of its texture (ModEngineGlowWhite.png): mod ships' coloured exhausts, tinted by vertex colour.")]
        public Material engineGlowTint;
        [Tooltip("URP Lit, opaque / alpha clip / transparent (premultiplied), each without and with detail maps.")]
        public Material litOpaque, litOpaqueDetail, litCutout, litCutoutDetail, litTransparent, litTransparentDetail;
        [Tooltip("The same in URP's specular setup (_SPECULAR_SETUP, _SpecGlossMap): materials with \"specularGlossiness\" / \"specularColor\" and glTF's KHR_materials_pbrSpecularGlossiness.")]
        public Material specOpaque, specOpaqueDetail, specCutout, specCutoutDetail, specTransparent, specTransparentDetail;
        [Tooltip("The weapon fx templates of mod items (items.json \"fx\", ModWeapons): sprite_fire (GoF2/Additive) and sprite_smoke (GoF2/AlphaBlend).")]
        public Material fxAdditive, fxAlpha;
        [Tooltip("Hidden/GoF2/MetallicRoughnessToGloss: a glTF metallic-roughness texture to URP's metallic / smoothness map.")]
        public Material metallicRoughness;
        [Tooltip("GoF2/ModEngineFlame: the template of mod ships' engine flames (materials with \"flame\", EngineFlame).")]
        public Material engineFlame;
        [Tooltip("GoF2/ModHologram: the template of mod ships' hologram projectors (materials with \"hologram\").")]
        public Material hologram;
        [Tooltip("GoF2/ModNozzleShimmer: the template of mod ships' nozzle shimmer (materials with \"shimmer\").")]
        public Material nozzleShimmer;
        [Tooltip("GoF2/ModGlow: the template of mod models' additive glow layers (materials with \"additive\").")]
        public Material glow;

        static ModAssets loaded;

        public static ModAssets Get()
        {
            if (loaded == null) loaded = Resources.Load<ModAssets>(ResourcePath);
            if (loaded == null) Debug.LogWarning("Mods: Resources/" + ResourcePath + " missing (GoF2 > Build > Mod Assets): mod ships can't be built");
            return loaded;
        }

        public Material Lit(bool cutout, bool transparent, bool detail) =>
            transparent ? (detail ? litTransparentDetail : litTransparent)
            : cutout ? (detail ? litCutoutDetail : litCutout)
            : (detail ? litOpaqueDetail : litOpaque);

        /// <summary>Lit's template in the specular setup; Lit's own when the assets were built before it had them.</summary>
        public Material Lit(bool cutout, bool transparent, bool detail, bool specular)
        {
            if (!specular) return Lit(cutout, transparent, detail);
            var m = transparent ? (detail ? specTransparentDetail : specTransparent)
                  : cutout ? (detail ? specCutoutDetail : specCutout)
                  : (detail ? specOpaqueDetail : specOpaque);
            return m != null ? m : Lit(cutout, transparent, detail);
        }

        /// <summary>Every Lit template, metallic and specular.</summary>
        public Material[] LitTemplates => new[] { litOpaque, litOpaqueDetail, litCutout, litCutoutDetail, litTransparent, litTransparentDetail,
                                                  specOpaque, specOpaqueDetail, specCutout, specCutoutDetail, specTransparent, specTransparentDetail };
    }
}
