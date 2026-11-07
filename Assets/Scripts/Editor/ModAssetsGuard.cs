// ModAssetsGuard.cs  (Editor only)
// Keeps the mod material templates (Resources/GoF2Mods/ModAssets, ModAssetsBuilder) holding the _EMISSION keyword before
// every player build (and _GOF2_PBR: GoF2/Hull's variant without the GoF2 terms, see HullForwardPass.hlsl; the ModSpec_
// templates _SPECULAR_SETUP). URP Lit's emission is a shader_feature: a build keeps its variants only while a material in the
// build uses the keyword, and the templates are the only Lit materials that carry it (mods' copies come at runtime). On
// 2026-10-05 the templates came out of a Windows build without the keyword (saved with the version stamp's restore), so the
// builds after it could have shipped without the emission variants (mod ships' glowing parts dark). Logs when it fixes one.

using GoF2Remake.Modding;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace GoF2Remake.EditorTools
{
    public class ModAssetsGuard : IPreprocessBuildWithReport
    {
        public int callbackOrder => -900;

        public void OnPreprocessBuild(BuildReport report) => Enforce();

        public static void Enforce()
        {
            var a = AssetDatabase.LoadAssetAtPath<ModAssets>("Assets/Resources/" + ModAssets.ResourcePath + ".asset");
            if (a == null) return;
            foreach (var m in a.LitTemplates)
            {
                if (m != null && !m.IsKeywordEnabled("_GOF2_PBR"))
                {
                    // Without it a build strips GoF2/Hull's PBR variant and the mods' hulls take the GoF2 terms again.
                    m.EnableKeyword("_GOF2_PBR");
                    EditorUtility.SetDirty(m);
                    AssetDatabase.SaveAssetIfDirty(m);
                    Debug.LogWarning($"GoF2: the mod template {m.name} had lost _GOF2_PBR; put back before the build");
                }
                bool spec = m != null && m.name.StartsWith("ModSpec_");
                if (m != null && spec != m.IsKeywordEnabled("_SPECULAR_SETUP"))
                {
                    // The specular templates are the only ones a build keeps GoF2/Hull's specular-setup variants for.
                    if (spec) m.EnableKeyword("_SPECULAR_SETUP"); else m.DisableKeyword("_SPECULAR_SETUP");
                    EditorUtility.SetDirty(m);
                    AssetDatabase.SaveAssetIfDirty(m);
                    Debug.LogWarning($"GoF2: the mod template {m.name} had {(spec ? "lost" : "gained")} _SPECULAR_SETUP; put right before the build");
                }
                if (m == null || m.IsKeywordEnabled("_EMISSION")) continue;
                m.EnableKeyword("_EMISSION");
                if (m.GetColor("_EmissionColor").maxColorComponent <= 0f) m.SetColor("_EmissionColor", new Color(1f / 255f, 1f / 255f, 1f / 255f, 1f));
                EditorUtility.SetDirty(m);
                AssetDatabase.SaveAssetIfDirty(m);
                Debug.LogWarning($"GoF2: the mod template {m.name} had lost _EMISSION; put back before the build");
            }
        }
    }
}
