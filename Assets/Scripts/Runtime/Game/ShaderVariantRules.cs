// ShaderVariantRules.cs
// Remake: the shader variants a build leaves out (EditorTools.ShaderVariantStripper; a rule here so the tests reach it, the
// Editor scripts have no assembly of their own). URP's Lit, and GoF2/Hull with it, multiplies out every feature URP may turn on;
// a full Linux build spent 640 of its 940 s on 14 612 variants (2026-10-26), GoF2/Hull 4232 of them at 1.5 s each, of which
// only 8 combinations are real material differences. Each rule is a feature this game can't turn on. Anyone adding such a
// feature must drop its rule: a stripped variant fails quietly, the nearest one left being drawn instead.
//   FOG_EXP, FOG_EXP2: fog is always linear (OrbitBuilder.SetupSky, StationLevelViews) and off in the scenes' own settings.
//   LOD_FADE_CROSSFADE: no LODGroup fades (ShaderVariantTests checks the prefabs, AutoLods makes none).
//   _LIGHT_COOKIES: no light has a cookie.
//   Hidden/TerrainEngine: the project has no terrain, yet a build compiles these.
//   STEREO_INSTANCING_ON (Linux, Android, iOS): VR can't start there. OpenXR ships no Linux library, Android has no XR loader in
//   the project and iOS no XR settings at all. Windows keeps it, and so does macOS: OpenXR ships a loader for its players.

namespace GoF2Remake.Data
{
    public static class ShaderVariantRules
    {
        public static bool UnusedKeyword(string keyword, bool vr) => keyword switch
        {
            "FOG_EXP" or "FOG_EXP2" or "LOD_FADE_CROSSFADE" or "_LIGHT_COOKIES" => true,
            "STEREO_INSTANCING_ON" => !vr,
            _ => false,
        };

        public static bool UnusedShader(string shader) => shader.StartsWith("Hidden/TerrainEngine/");
    }
}
