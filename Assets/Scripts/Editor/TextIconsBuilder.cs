// TextIconsBuilder.cs
// GoF2 > Build > Text Icons: the inline icons of the animated dialogue (TextReveal, rich text <sprite>). One atlas of 64 px
// cells and a TextCore SpriteAsset in Resources/Sprite Assets/, where the runtime panel text settings look sprite assets
// up by name (defaultSpriteAssetPath "Sprite Assets/"), so no PanelTextSettings asset is needed.
//   race_0 / 1 / 2 / 3 / 8 / 9   the HUD's race emblems (GoF2Hud, Build HUD Images): Terran, Vossk, Nivelian, Midorian,
//                                pirates, Void
//   gate_icon, wormhole_icon     the HUD's jumpgate and wormhole icons
//   map_products, core, crate_off, autopilot   blueprint (the map's production icon), ore core, container, autopilot
//   item_NNN, ship_NNN           the shop icons (GoF2Icons, Build Item Icons), the centre square of their 180x88 plate
//   coin                         remake-made (the original writes amounts with a "$" only): a gold coin drawn here
//   heart                        remake-made: a red heart for the main menu's credit line (Inter has no emoji)
// All in 64 px cells, 32 to a row.

using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore;
using UnityEngine.TextCore.Text;

namespace GoF2Remake.EditorTools
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class TextIconsBuilder
    {
        public const string AssetName = "gof2_text_icons";
        const string Folder = "Assets/Resources/Sprite Assets";
        const string HudFolder = "Assets/Resources/GoF2Hud";
        const string IconFolder = "Assets/Resources/GoF2Icons";
        const int Cell = 64, Inset = 4, Columns = 32;

        static readonly (string name, string source)[] Icons =
        {
            ("coin", null), ("terran", "race_0"), ("vossk", "race_1"), ("nivelian", "race_2"), ("midorian", "race_3"),
            ("pirate", "race_8"), ("void", "race_9"), ("gate", "gate_icon"), ("wormhole", "wormhole_icon"),
            ("blueprint", "map_products"), ("core", "core"), ("container", "crate_off"), ("autopilot", "autopilot"),
        };

        /// <summary>The HUD icons, then every shop icon (items 0-232, ships 0-63): (sprite name, source path, plate crop).</summary>
        static System.Collections.Generic.List<(string name, string path, bool plate)> AllIcons()
        {
            var list = new System.Collections.Generic.List<(string, string, bool)>();
            foreach (var (name, source) in Icons) list.Add((name, source == null ? null : $"{HudFolder}/{source}.png", false));
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
                var (name, path, plate) = icons[i];
                int x0 = i % Columns * Cell, y0 = atlas.height - Cell - i / Columns * Cell;   // row 0 at the top
                if (name == "heart") DrawHeart(atlas, x0, y0);
                else if (path == null) DrawCoin(atlas, x0, y0);
                else Blit(atlas, x0, y0, LoadPng(path), name, plate);
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

        /// <summary>'src' scaled to fit the cell (keeping its aspect), centred; a shop plate ('plate') by its centre square.</summary>
        static void Blit(Texture2D atlas, int x0, int y0, Texture2D src, string name, bool plate)
        {
            if (src == null) { Debug.LogWarning($"GoF2: text icon {name}: source missing, run GoF2 > Build > HUD Images / Build Item Icons"); return; }
            // The source rect in pixels: a plate's centre square, else the visible part (the HUD emblems have a wide glow
            // margin that made them tiny next to the text).
            RectInt rect = new RectInt(0, 0, src.width, src.height);
            if (plate && src.width > src.height) rect = new RectInt((src.width - src.height) / 2, 0, src.height, src.height);
            else if (!plate) rect = VisibleRect(src);
            int box = Cell - Inset * 2;
            float s = Mathf.Min((float)box / rect.width, (float)box / rect.height);
            int w = Mathf.Max(1, Mathf.RoundToInt(rect.width * s)), h = Mathf.Max(1, Mathf.RoundToInt(rect.height * s));
            int ox = x0 + (Cell - w) / 2, oy = y0 + (Cell - h) / 2;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    atlas.SetPixel(ox + x, oy + y, src.GetPixelBilinear((rect.x + (x + 0.5f) / w * rect.width) / src.width,
                                                                        (rect.y + (y + 0.5f) / h * rect.height) / src.height));
            Object.DestroyImmediate(src);
        }

        /// <summary>The bounds of the pixels above 60 % alpha (the glow around the emblems is dropped), square.</summary>
        static RectInt VisibleRect(Texture2D src)
        {
            int minX = src.width, minY = src.height, maxX = -1, maxY = -1;
            var px = src.GetPixels32();
            for (int y = 0; y < src.height; y++)
                for (int x = 0; x < src.width; x++)
                    if (px[y * src.width + x].a > 150) { minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x); minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y); }
            if (maxX < 0) return new RectInt(0, 0, src.width, src.height);
            int size = Mathf.Max(maxX - minX + 1, maxY - minY + 1);
            int cx = (minX + maxX + 1) / 2, cy = (minY + maxY + 1) / 2;
            return new RectInt(cx - size / 2, cy - size / 2, size, size);
        }

        /// <summary>A red heart (the implicit curve (x² + y² − 1)³ − x²y³ ≤ 0), shaded from the top left, with a highlight,
        /// antialiased by 4x4 supersampling.</summary>
        static void DrawHeart(Texture2D atlas, int x0, int y0)
        {
            var light = new Color(1f, 0.38f, 0.42f);
            var dark = new Color(0.78f, 0.06f, 0.16f);
            const float size = 21f, c = Cell / 2f;   // curve units -> pixels (the heart spans about x ±1.14, y -1..1.2)
            for (int y = 0; y < Cell; y++)
                for (int x = 0; x < Cell; x++)
                {
                    int inside = 0;
                    for (int sy = 0; sy < 4; sy++)
                        for (int sx = 0; sx < 4; sx++)
                        {
                            float hx = (x + (sx + 0.5f) / 4f - c) / size, hy = (y + (sy + 0.5f) / 4f - c + 2f) / size;
                            float a = hx * hx + hy * hy - 1f;
                            if (a * a * a - hx * hx * hy * hy * hy <= 0f) inside++;
                        }
                    if (inside == 0) continue;
                    float ux = (x + 0.5f - c) / size, uy = (y + 0.5f - c + 2f) / size;
                    var col = Color.Lerp(dark, light, Mathf.Clamp01(0.45f + (uy - ux) * 0.35f));
                    float gx = ux + 0.5f, gy = uy - 0.55f;
                    col = Color.Lerp(col, Color.white, Mathf.Clamp01(1f - Mathf.Sqrt(gx * gx + gy * gy) / 0.28f) * 0.6f);
                    col.a = inside / 16f;
                    atlas.SetPixel(x0 + x, y0 + y, col);
                }
        }

        /// <summary>A gold coin: a shaded disc, a dark rim, an inner ring and a highlight, antialiased.</summary>
        static void DrawCoin(Texture2D atlas, int x0, int y0)
        {
            var light = new Color(1f, 0.86f, 0.45f);
            var dark = new Color(0.86f, 0.56f, 0.14f);
            var rim = new Color(0.45f, 0.27f, 0.04f);
            const float r = 24f, c = Cell / 2f;
            for (int y = 0; y < Cell; y++)
                for (int x = 0; x < Cell; x++)
                {
                    float dx = x + 0.5f - c, dy = y + 0.5f - c, d = Mathf.Sqrt(dx * dx + dy * dy);
                    float alpha = Mathf.Clamp01(r + 0.5f - d);
                    if (alpha <= 0f) continue;
                    // light from the top left
                    float shade = Mathf.Clamp01(0.5f + (dy - dx) / (2.6f * r));
                    var col = Color.Lerp(dark, light, shade);
                    float ring = Mathf.Abs(d - r * 0.7f);
                    if (ring < 1.6f) col = Color.Lerp(col, Color.Lerp(dark, rim, 0.4f), 1f - ring / 1.6f);
                    if (d > r - 3f) col = Color.Lerp(col, rim, Mathf.Clamp01(d - (r - 3f)) );
                    float hx = dx + r * 0.35f, hy = dy - r * 0.35f;
                    float glint = Mathf.Clamp01(1f - Mathf.Sqrt(hx * hx + hy * hy) / (r * 0.35f));
                    col = Color.Lerp(col, Color.white, glint * 0.55f);
                    col.a = alpha;
                    atlas.SetPixel(x0 + x, y0 + y, col);
                }
        }
    }
}
