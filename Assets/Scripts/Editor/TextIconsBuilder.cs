// TextIconsBuilder.cs
// GoF2 > Build > Text Icons: the inline icons of the animated dialogue (TextReveal, rich text <sprite>). One atlas of 64 px
// cells and a TextCore SpriteAsset in Resources/Sprite Assets/, where the runtime panel text settings look sprite assets
// up by name (defaultSpriteAssetPath "Sprite Assets/"), so no PanelTextSettings asset is needed. What each cell shows and
// how it is drawn: TextIcons (shared with UiImageSlots, which draws the atlas from the player's copy in a build without
// the originals).
// All in 64 px cells, 32 to a row.

using System.IO;
using GoF2Remake.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore;
using UnityEngine.TextCore.Text;

namespace GoF2Remake.EditorTools
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class TextIconsBuilder
    {
        public const string AssetName = TextIcons.AssetName;
        const string Folder = "Assets/Resources/Sprite Assets";
        const string IconFolder = "Assets/Resources/GoF2Icons";
        const int Cell = TextIcons.Cell, Columns = TextIcons.Columns;

        /// <summary>The HUD icons, then every shop icon (items 0-232, ships 0-63): (sprite name, source path, plate crop).</summary>
        static System.Collections.Generic.List<(string name, string path, bool plate)> AllIcons()
        {
            var list = new System.Collections.Generic.List<(string, string, bool)>();
            foreach (var (name, _) in TextIcons.Hud) list.Add((name, null, false));
            foreach (var prefix in new[] { "item", "ship" })
                for (int i = 0; i < 1000; i++)
                {
                    string path = $"{IconFolder}/{prefix}_{i:000}.png";
                    if (File.Exists(path)) list.Add(($"{prefix}_{i:000}", path, true));
                }
            list.Add(("heart", null, false));   // last, so the other icons keep their places
            return list;
        }

        [MenuItem("GoF2/Build/Text Icons", priority = 222)]
        public static void Build()
        {
            Directory.CreateDirectory(Folder);
            var icons = AllIcons();
            int rows = (icons.Count + Columns - 1) / Columns;
            var atlas = new Texture2D(Cell * Columns, Cell * rows, TextureFormat.RGBA32, false);
            atlas.SetPixels32(new Color32[atlas.width * atlas.height]);
            for (int i = 0; i < icons.Count; i++)
            {
                int x0 = i % Columns * Cell, y0 = atlas.height - Cell - i / Columns * Cell;   // row 0 at the top
                TextIcons.Draw(atlas, x0, y0, icons[i].name, rel => LoadPng($"Assets/Resources/{rel}.png"));
            }
            atlas.Apply();
            string texPath = $"{Folder}/{AssetName}.png";
            File.WriteAllBytes(texPath, atlas.EncodeToPNG());
            Object.DestroyImmediate(atlas);
            AssetDatabase.ImportAsset(texPath, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(texPath);
            importer.textureType = TextureImporterType.Default;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);

            string assetPath = $"{Folder}/{AssetName}.asset";
            var asset = AssetDatabase.LoadAssetAtPath<SpriteAsset>(assetPath);
            bool created = asset == null;
            if (created) asset = ScriptableObject.CreateInstance<SpriteAsset>();
            asset.name = AssetName;
            SetInternal(asset, "spriteSheet", tex);   // internal setters in this TextCore version
            // Metrics in the atlas' pixels: a 64 px sprite on a 64 point face, its baseline 13 px from the bottom, so it
            // sits on the text's baseline and reaches about the cap height.
            var face = new FaceInfo { pointSize = Cell, scale = 1f, lineHeight = Cell, ascentLine = Cell - 13, descentLine = -13, baseline = 0 };
            SetInternal(asset, "faceInfo", face);
            asset.spriteGlyphTable.Clear();
            asset.spriteCharacterTable.Clear();
            for (int i = 0; i < icons.Count; i++)
            {
                int x0 = i % Columns * Cell, y0 = tex.height - Cell - i / Columns * Cell;
                var metrics = new GlyphMetrics(Cell, Cell, 2f, Cell - 13, Cell + 4);
                var glyph = new SpriteGlyph((uint)i, metrics, new GlyphRect(x0, y0, Cell, Cell), 1f, 0);
                asset.spriteGlyphTable.Add(glyph);
                var character = new SpriteCharacter((uint)(0xE000 + i), asset, glyph) { name = icons[i].name };
                asset.spriteCharacterTable.Add(character);
            }
            if (created) AssetDatabase.CreateAsset(asset, assetPath);
            var mat = asset.material;
            if (mat == null)
            {
                mat = new Material(Shader.Find("Hidden/TextCore/Sprite")) { name = AssetName + " Material" };
                AssetDatabase.AddObjectToAsset(mat, asset);
                asset.material = mat;
            }
            mat.SetTexture("_MainTex", tex);
            asset.UpdateLookupTables();
            EditorUtility.SetDirty(mat);
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            Debug.Log($"GoF2: text icons -> {assetPath} ({icons.Count} icons)");
        }

        static void SetInternal(SpriteAsset asset, string property, object value) =>
            typeof(SpriteAsset).GetProperty(property).GetSetMethod(true).Invoke(asset, new[] { value });

        static Texture2D LoadPng(string path)
        {
            if (!File.Exists(path)) return null;
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            t.LoadImage(File.ReadAllBytes(path));
            return t;
        }
    }
}
