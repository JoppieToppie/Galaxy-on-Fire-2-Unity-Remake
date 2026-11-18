// TextIcons.cs
// The inline icons of the animated dialogue (TextReveal, rich text <sprite>): what each cell of the gof2_text_icons sprite
// asset shows and how it is drawn. The editor's GoF2 > Build > Text Icons (TextIconsBuilder) makes the asset and its atlas
// from the project's images; a build without the originals has the asset (the remake's own table of names and cells) but a
// placeholder atlas, which UiImageSlots fills with Compose from the player's converted copy (docs/notes/player-assets.md
// "Text icons"). 64 px cells:
//   terran, vossk, nivelian, midorian, pirate, void   the HUD's race emblems (GoF2Hud race_0 / 1 / 2 / 3 / 8 / 9)
//   gate, wormhole                                    the HUD's jumpgate and wormhole icons
//   blueprint, core, container, autopilot             the map's production icon, ore core, container, autopilot
//   item_NNN, ship_NNN                                the shop icons (GoF2Icons), the centre square of their 180x88 plate
//   coin                                              remake-made (the original writes amounts with a "$" only)
//   heart                                             remake-made: the main menu's credit line (Inter has no emoji)

using System;
using UnityEngine;
using UnityEngine.TextCore.Text;

namespace GoF2Remake.UI
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]   // (only constant tables: nothing to reset)
    public static class TextIcons
    {
        public const string AssetName = "gof2_text_icons";
        public const int Cell = 64, Inset = 4, Columns = 32;

        /// <summary>The HUD icons: sprite name, GoF2Hud image (null: drawn here, the coin).</summary>
        public static readonly (string name, string source)[] Hud =
        {
            ("coin", null), ("terran", "race_0"), ("vossk", "race_1"), ("nivelian", "race_2"), ("midorian", "race_3"),
            ("pirate", "race_8"), ("void", "race_9"), ("gate", "gate_icon"), ("wormhole", "wormhole_icon"),
            ("blueprint", "map_products"), ("core", "core"), ("container", "crate_off"), ("autopilot", "autopilot"),
        };

        /// <summary>A sprite's source image by its Resources path ("GoF2Hud/race_0", "GoF2Icons/item_005"); null for the drawn
        /// ones (coin, heart). 'plate': a shop plate, shown by its centre square.</summary>
        public static string SourceOf(string name, out bool plate)
        {
            plate = name.StartsWith("item_") || name.StartsWith("ship_");
            if (plate) return "GoF2Icons/" + name;
            foreach (var (n, source) in Hud) if (n == name) return source == null ? null : "GoF2Hud/" + source;
            return null;
        }

        /// <summary>The atlas of a sprite asset's cells, each drawn from 'load' (a readable image by its Resources path; null:
        /// the cell stays clear). Its size is the cells' extent, the asset's own atlas size.</summary>
        public static Texture2D Compose(SpriteAsset asset, Func<string, Texture2D> load)
        {
            int w = 0, h = 0;
            foreach (var c in asset.spriteCharacterTable)
            {
                var r = c.glyph.glyphRect;
                w = Mathf.Max(w, r.x + r.width);
                h = Mathf.Max(h, r.y + r.height);
            }
            if (w == 0 || h == 0) return null;
            var atlas = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = AssetName };
            atlas.SetPixels32(new Color32[w * h]);
            foreach (var c in asset.spriteCharacterTable)
            {
                var r = c.glyph.glyphRect;
                Draw(atlas, r.x, r.y, c.name, load);
            }
            atlas.Apply();
            return atlas;
        }

        /// <summary>One cell (bottom left at x0, y0): the coin and the heart drawn, the others from their image.</summary>
        public static void Draw(Texture2D atlas, int x0, int y0, string name, Func<string, Texture2D> load)
        {
            if (name == "heart") { DrawHeart(atlas, x0, y0); return; }
            if (name == "coin") { DrawCoin(atlas, x0, y0); return; }
            string path = SourceOf(name, out bool plate);
            var src = path != null ? load(path) : null;
            if (src == null) { Debug.LogWarning($"TextIcons: {name}: no {path ?? "source"} image"); return; }
            Blit(atlas, x0, y0, src, plate);
            if (Application.isPlaying) UnityEngine.Object.Destroy(src); else UnityEngine.Object.DestroyImmediate(src);
        }

        /// <summary>'src' scaled to fit the cell (keeping its aspect), centred; a shop plate ('plate') by its centre square.</summary>
        static void Blit(Texture2D atlas, int x0, int y0, Texture2D src, bool plate)
        {
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
