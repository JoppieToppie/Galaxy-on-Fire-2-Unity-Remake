// ModAssetsBuilder.cs  (Editor only)
// "GoF2 > Build > Mod Assets": Resources/GoF2Mods/ModAssets (Modding.ModAssets) and its URP Lit template materials
// (Resources/GoF2Mods/ModLit_*.mat), the engine flame template (ModEngineFlame.mat) and the tintable engine glow
// (ModEngineGlowWhite.png + ModEngineGlowTint.mat): what
// the game builds mods' ships from at run time.

using System.IO;
using GoF2Remake.Modding;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace GoF2Remake.EditorTools
{
    public static class ModAssetsBuilder
    {
        const string Dir = ImportSettings.Root + "/Resources/GoF2Mods";
        const string EngineGlow = ImportSettings.Root + "/Materials/mat_34813_ship_engine_glow.mat";
        const string FxAdditive = ImportSettings.Root + "/Materials/mat_27250_sprite_fire.mat";
        const string FxAlpha = ImportSettings.Root + "/Materials/mat_20101_sprite_smoke.mat";

        [MenuItem("GoF2/Build/Mod Assets", priority = 209)]
        public static void Build()
        {
            Directory.CreateDirectory(Dir);
            string path = Dir + "/ModAssets.asset";
            var a = AssetDatabase.LoadAssetAtPath<ModAssets>(path);
            if (a == null) { a = ScriptableObject.CreateInstance<ModAssets>(); AssetDatabase.CreateAsset(a, path); }
            a.engineGlow = AssetDatabase.LoadAssetAtPath<Material>(EngineGlow);
            if (a.engineGlow == null) Debug.LogWarning($"GoF2: {EngineGlow} missing (Build Materials And Prefabs)");
            a.engineGlowTint = a.engineGlow != null ? EngineGlowTint(a.engineGlow) : null;
            a.fxAdditive = AssetDatabase.LoadAssetAtPath<Material>(FxAdditive);
            a.fxAlpha = AssetDatabase.LoadAssetAtPath<Material>(FxAlpha);
            if (a.fxAdditive == null || a.fxAlpha == null) Debug.LogWarning($"GoF2: {FxAdditive} / {FxAlpha} missing (Build Materials And Prefabs)");
            a.litOpaque = Lit("ModLit_Opaque", false, false, false);
            a.litOpaqueDetail = Lit("ModLit_OpaqueDetail", false, false, true);
            a.litCutout = Lit("ModLit_Cutout", true, false, false);
            a.litCutoutDetail = Lit("ModLit_CutoutDetail", true, false, true);
            a.litTransparent = Lit("ModLit_Transparent", false, true, false);
            a.litTransparentDetail = Lit("ModLit_TransparentDetail", false, true, true);
            a.specOpaque = Lit("ModSpec_Opaque", false, false, false, true);
            a.specOpaqueDetail = Lit("ModSpec_OpaqueDetail", false, false, true, true);
            a.specCutout = Lit("ModSpec_Cutout", true, false, false, true);
            a.specCutoutDetail = Lit("ModSpec_CutoutDetail", true, false, true, true);
            a.specTransparent = Lit("ModSpec_Transparent", false, true, false, true);
            a.specTransparentDetail = Lit("ModSpec_TransparentDetail", false, true, true, true);
            var shader = Shader.Find("Hidden/GoF2/MetallicRoughnessToGloss");
            string mrPath = Dir + "/ModMetallicRoughness.mat";
            var mr = AssetDatabase.LoadAssetAtPath<Material>(mrPath);
            if (mr == null && shader != null) { mr = new Material(shader); AssetDatabase.CreateAsset(mr, mrPath); }
            a.metallicRoughness = mr;
            var flameShader = Shader.Find("GoF2/ModEngineFlame");
            string flamePath = Dir + "/ModEngineFlame.mat";
            var flame = AssetDatabase.LoadAssetAtPath<Material>(flamePath);
            if (flame == null && flameShader != null) { flame = new Material(flameShader); AssetDatabase.CreateAsset(flame, flamePath); }
            else if (flame != null && flameShader != null)
            {
                // The template carries the shader's defaults (a material keeps the values it was saved with).
                var fresh = new Material(flameShader);
                flame.shader = flameShader;
                flame.CopyPropertiesFromMaterial(fresh);
                Object.DestroyImmediate(fresh);
                EditorUtility.SetDirty(flame);
            }
            if (flameShader == null) Debug.LogWarning("GoF2: shader GoF2/ModEngineFlame missing");
            a.engineFlame = flame;
            a.hologram = Template("GoF2/ModHologram", Dir + "/ModHologram.mat");
            a.nozzleShimmer = Template("GoF2/ModNozzleShimmer", Dir + "/ModNozzleShimmer.mat");
            a.glow = Template("GoF2/ModGlow", Dir + "/ModGlow.mat");
            EditorUtility.SetDirty(a);
            AssetDatabase.SaveAssets();
            Debug.Log("GoF2: Resources/GoF2Mods/ModAssets built.");
        }

        /// <summary>A shader's template material, reset to the shader's defaults (made when missing).</summary>
        static Material Template(string shaderName, string path)
        {
            var shader = Shader.Find(shaderName);
            if (shader == null) { Debug.LogWarning("GoF2: shader " + shaderName + " missing"); return AssetDatabase.LoadAssetAtPath<Material>(path); }
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); return m; }
            var fresh = new Material(shader);
            m.shader = shader;
            m.CopyPropertiesFromMaterial(fresh);
            Object.DestroyImmediate(fresh);
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>The engine glow on a white copy of its texture (each texel's brightest channel, alpha kept; the glow sprite
        /// itself made round and soft, SmoothGlow), so a mod ship's
        /// exhaust can take any colour through the vertex colours (GoF2/Additive with _USEVERTEXCOLOR_ON multiplies them).
        /// The copy takes the original texture's import settings.</summary>
        static Material EngineGlowTint(Material glow)
        {
            var src = (glow.HasProperty("_MainTex") ? glow.GetTexture("_MainTex") : glow.mainTexture) as Texture2D;
            string srcPath = src != null ? AssetDatabase.GetAssetPath(src) : null;
            if (string.IsNullOrEmpty(srcPath) || !File.Exists(srcPath)) { Debug.LogWarning("GoF2: the engine glow's texture is missing"); return null; }
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            t.LoadImage(File.ReadAllBytes(srcPath));
            var px = t.GetPixels32();
            for (int i = 0; i < px.Length; i++)
            {
                byte v = (byte)Mathf.Max(px[i].r, Mathf.Max(px[i].g, px[i].b));
                px[i] = new Color32(v, v, v, px[i].a);
            }
            SmoothGlow(px, t.width, t.height);
            t.SetPixels32(px);
            string texPath = Dir + "/ModEngineGlowWhite.png";
            File.WriteAllBytes(texPath, t.EncodeToPNG());
            Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(texPath);
            var srcImp = (TextureImporter)AssetImporter.GetAtPath(srcPath);
            var imp = (TextureImporter)AssetImporter.GetAtPath(texPath);
            if (srcImp != null && imp != null)
            {
                var settings = new TextureImporterSettings();
                srcImp.ReadTextureSettings(settings);
                imp.SetTextureSettings(settings);
                imp.textureCompression = srcImp.textureCompression;
                imp.maxTextureSize = srcImp.maxTextureSize;
                imp.SaveAndReimport();
            }
            string matPath = Dir + "/ModEngineGlowTint.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (m == null) { m = new Material(glow) { name = "ModEngineGlowTint" }; AssetDatabase.CreateAsset(m, matPath); }
            else m.CopyPropertiesFromMaterial(glow);
            var white = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            foreach (var p in m.GetTexturePropertyNames())
                if (m.GetTexture(p) == src) m.SetTexture(p, white);
            m.EnableKeyword("_USEVERTEXCOLOR_ON");
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>The glow sprite (centre uv (0.46, 0.947), out to the flared ring's uv radius 0.048, ModShipBuilder) made round
        /// and soft: each ring of texels takes the mean of its brightness and alpha, the profile never rises going out and is
        /// blurred (the sprite's ring of dashes between radius 0.012 and 0.026 showed through a tinted exhaust as a "marker").</summary>
        static void SmoothGlow(Color32[] px, int w, int h)
        {
            float cx = 0.46f * w, cy = 0.947f * h, radius = 0.05f * w;
            int bins = Mathf.CeilToInt(radius * 2f) + 1;
            var sumV = new double[bins]; var sumA = new double[bins]; var n = new int[bins];
            int x0 = Mathf.Max(0, (int)(cx - radius)), x1 = Mathf.Min(w - 1, (int)(cx + radius));
            int y0 = Mathf.Max(0, (int)(cy - radius)), y1 = Mathf.Min(h - 1, (int)(cy + radius));
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float r = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                    if (r >= radius) continue;
                    int b = (int)(r * 2f); var p = px[y * w + x];
                    sumV[b] += p.r; sumA[b] += p.a; n[b]++;
                }
            var v = new float[bins]; var a = new float[bins];
            float maxV = 0f, maxA = 0f;
            for (int b = bins - 1; b >= 0; b--)   // from the rim in: never darker than further out
            {
                if (n[b] > 0) { maxV = Mathf.Max(maxV, (float)(sumV[b] / n[b])); maxA = Mathf.Max(maxA, (float)(sumA[b] / n[b])); }
                v[b] = maxV; a[b] = maxA;
            }
            // then a soft fall-off instead of the old ring's edge (a box blur of +-6 bins, 3 texels, twice)
            for (int pass = 0; pass < 2; pass++)
            {
                var sv = new float[bins]; var sa = new float[bins];
                for (int b = 0; b < bins; b++)
                {
                    float tv = 0f, ta = 0f; int c = 0;
                    for (int k = -6; k <= 6; k++) { int j = Mathf.Clamp(b + k, 0, bins - 1); tv += v[j]; ta += a[j]; c++; }
                    sv[b] = tv / c; sa[b] = ta / c;
                }
                v = sv; a = sa;
            }
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float r = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                    if (r >= radius) continue;
                    int b = (int)(r * 2f);
                    byte vv = (byte)Mathf.Clamp(Mathf.RoundToInt(v[b]), 0, 255);
                    px[y * w + x] = new Color32(vv, vv, vv, (byte)Mathf.Clamp(Mathf.RoundToInt(a[b]), 0, 255));
                }
        }

        /// <summary>A URP Lit template with the keywords every mod material uses (normal map, metallic map, emission) and the
        /// surface type, so the build keeps those variants. URP sets Lit's keywords from what the material holds when it is
        /// saved, so the templates hold placeholder textures (a flat normal, white, grey details) and a faint emission;
        /// ModMaterials replaces them. 'specular': URP's specular setup (_SPECULAR_SETUP, the map in _SpecGlossMap: RGB the
        /// specular colour, A smoothness), for the mods' "specularGlossiness" (Galaxy on Fire 3's coloured specular).</summary>
        static Material Lit(string name, bool cutout, bool transparent, bool detail, bool specular = false)
        {
            string path = $"{Dir}/{name}.mat";
            // GoF2/Hull with _GOF2_PBR: URP Lit as it is for a mod's PBR maps (the hull's fog and the shared shader; the
            // original's reflection and rim are for its own specular masks); ModMaterials.PlainLit puts a mod's hangar or
            // bar on URP Lit itself.
            var shader = Shader.Find("GoF2/Hull") ?? Shader.Find("Universal Render Pipeline/Lit");
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool create = m == null;
            if (create) m = new Material(shader) { name = name };
            else m.shader = shader;
            Texture2D Tex(string n) => AssetDatabase.LoadAssetAtPath<Texture2D>($"{Dir}/{n}.png");
            m.SetTexture("_BumpMap", Tex("ModFlatNormal"));
            m.SetTexture("_MetallicGlossMap", specular ? null : Tex("ModWhite"));
            m.SetTexture("_SpecGlossMap", specular ? Tex("ModWhite") : null);
            m.SetFloat("_WorkflowMode", specular ? 0f : 1f);
            m.SetColor("_SpecColor", Color.white);
            if (specular) m.EnableKeyword("_SPECULAR_SETUP"); else m.DisableKeyword("_SPECULAR_SETUP");
            m.SetTexture("_EmissionMap", Tex("ModWhite"));
            // As URP's LitShader editor leaves the material (see AssetImport): on for GoF2/Hull, off for URP Lit itself.
            m.SetShaderPassEnabled("MotionVectors", m.shader.name == "GoF2/Hull");
            m.EnableKeyword("_NORMALMAP");
            m.EnableKeyword("_METALLICSPECGLOSSMAP");
            m.EnableKeyword("_EMISSION");
            // A mod's metallic / smoothness is PBR (alpha = smoothness over the whole hull), not the original's specular
            // mask: GoF2/Hull leaves its GoF2 reflection and rim out for it (HullForwardPass.hlsl), URP's own reflection
            // stays. With them the mod ships were milky white in the hangars.
            m.EnableKeyword("_GOF2_PBR");
            m.SetColor("_EmissionColor", new Color(1f / 255f, 1f / 255f, 1f / 255f, 1f));
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            m.SetFloat("_SmoothnessTextureChannel", 0f);
            m.SetFloat("_AlphaClip", cutout ? 1f : 0f);
            m.SetFloat("_Cutoff", 0.5f);
            if (cutout) m.EnableKeyword("_ALPHATEST_ON"); else m.DisableKeyword("_ALPHATEST_ON");
            m.SetFloat("_Surface", transparent ? 1f : 0f);
            m.SetFloat("_Blend", transparent ? 1f : 0f);   // premultiplied: highlights stay bright on glass
            m.SetFloat("_SrcBlend", (float)BlendMode.One);
            m.SetFloat("_DstBlend", transparent ? (float)BlendMode.OneMinusSrcAlpha : (float)BlendMode.Zero);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_DstBlendAlpha", transparent ? (float)BlendMode.OneMinusSrcAlpha : (float)BlendMode.Zero);
            m.SetFloat("_ZWrite", transparent ? 0f : 1f);
            if (transparent)
            {
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.EnableKeyword("_ALPHAPREMULTIPLY_ON");
                m.SetOverrideTag("RenderType", "Transparent");
                m.renderQueue = (int)RenderQueue.Transparent;
                m.SetShaderPassEnabled("DepthOnly", false);
                m.SetShaderPassEnabled("ShadowCaster", false);
            }
            else
            {
                m.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                m.SetOverrideTag("RenderType", cutout ? "TransparentCutout" : "Opaque");
                m.renderQueue = cutout ? (int)RenderQueue.AlphaTest : -1;
                m.SetShaderPassEnabled("DepthOnly", true);
                m.SetShaderPassEnabled("ShadowCaster", true);
            }
            m.SetTexture("_DetailAlbedoMap", detail ? Tex("ModGrey") : null);
            m.SetTexture("_DetailNormalMap", detail ? Tex("ModFlatNormal") : null);
            // _DETAIL_SCALED (not MULX2): the same at scale 1, and it honours _DetailAlbedoMapScale (DamageEmbers' charring
            // fades the detail layer in by it); a build keeps only the template's variant.
            m.DisableKeyword("_DETAIL_MULX2");
            if (detail) m.EnableKeyword("_DETAIL_SCALED"); else m.DisableKeyword("_DETAIL_SCALED");
            if (create) AssetDatabase.CreateAsset(m, path); else EditorUtility.SetDirty(m);
            return m;
        }
    }
}
