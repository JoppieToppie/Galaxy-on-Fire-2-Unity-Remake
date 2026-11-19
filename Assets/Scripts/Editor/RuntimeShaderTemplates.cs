// RuntimeShaderTemplates.cs
// Feature: docs/notes/player-assets.md "Builds without the originals": GoF2 > Build > Runtime Shader Templates writes
// Resources/GoF2Content/ShaderTemplates, one material per shader and keyword set the content layer makes from the player's
// converted copy (OriginalContent.EveryMaterial), its textures left out (the remake's own assets: a shader, keywords and
// numbers). A build without the originals has no material using those variants, so the player build would strip them
// (URP/Lit with a normal map, transparent, emission...) and the game's models would draw wrong; these keep them. Needs a
// converted copy (the test bed: $GOF2_ORIGINAL_CACHE). Run it again when the content layer's materials change
// (PlayerCopyTests checks every runtime material has its template).

using System.Collections.Generic;
using System.IO;
using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Visuals;
using UnityEditor;
using UnityEngine;

namespace GoF2Remake.EditorTools
{
    public static class RuntimeShaderTemplates
    {
        public const string Dir = "Assets/Resources/" + OriginalContent.TemplateFolder;

        [MenuItem("GoF2/Build/Runtime Shader Templates", priority = 231)]
        public static void Build()
        {
            if (!GameContent.Installed) { Debug.LogError("GoF2: Runtime Shader Templates needs a converted copy (GOF2_ORIGINAL_CACHE, or the data folder's Original)"); return; }
            var byKey = new SortedDictionary<string, Material>(System.StringComparer.Ordinal);
            foreach (var m in OriginalContent.EveryMaterial())
            {
                string key = OriginalContent.VariantKey(m);
                if (key != null && !byKey.ContainsKey(key)) byKey[key] = m;
            }
            Directory.CreateDirectory(Dir);
            var keep = new HashSet<string>();
            foreach (var kv in byKey)
            {
                // no original's image: a slot that had one gets a 4 x 4 placeholder (a flat normal for the normal maps), so the
                // keywords that need a texture stay (URP's material check drops _NORMALMAP / _METALLICSPECGLOSSMAP without)
                var t = new Material(kv.Value);
                foreach (var name in t.GetTexturePropertyNames())
                    if (t.GetTexture(name) != null) t.SetTexture(name, name.Contains("Bump") || name.Contains("Normal") ? FlatNormal : White);
                string file = $"{Dir}/{Safe(kv.Value.shader.name)}_{Hash(kv.Key)}.mat";
                t.name = Path.GetFileNameWithoutExtension(file);
                keep.Add(file);
                var old = AssetDatabase.LoadAssetAtPath<Material>(file);
                if (old != null) { EditorUtility.CopySerialized(t, old); EditorUtility.SetDirty(old); Object.DestroyImmediate(t); }
                else AssetDatabase.CreateAsset(t, file);
            }
            foreach (var f in Directory.GetFiles(Dir, "*.mat"))
                if (!keep.Contains(f.Replace('\\', '/'))) AssetDatabase.DeleteAsset(f.Replace('\\', '/'));
            AssetDatabase.SaveAssets();
            Debug.Log($"GoF2: {byKey.Count} runtime shader templates in {Dir}");
        }

        static Texture2D White => Placeholder("placeholder_white", new Color32(255, 255, 255, 255));
        static Texture2D FlatNormal => Placeholder("placeholder_normal", new Color32(128, 128, 255, 255));

        /// <summary>A 4 x 4 texture of one colour, kept beside the templates (the remake's own, nothing of the original's).</summary>
        static Texture2D Placeholder(string name, Color32 c)
        {
            string file = $"{Dir}/{name}.asset";
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>(file);
            if (t != null) return t;
            t = new Texture2D(4, 4, TextureFormat.RGBA32, false, name.Contains("normal")) { name = name };
            t.SetPixels32(Enumerable.Repeat(c, 16).ToArray());
            t.Apply();
            AssetDatabase.CreateAsset(t, file);
            return t;
        }

        static string Safe(string s) => new string(s.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray()).Trim('_');

        static string Hash(string s)
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            var h = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(s));
            return string.Concat(h.Take(4).Select(b => b.ToString("x2")));
        }

        /// <summary>Batch mode: -executeMethod GoF2Remake.EditorTools.RuntimeShaderTemplates.BuildAndQuit</summary>
        public static void BuildAndQuit()
        {
            Build();
            EditorApplication.Exit(0);
        }
    }
}
