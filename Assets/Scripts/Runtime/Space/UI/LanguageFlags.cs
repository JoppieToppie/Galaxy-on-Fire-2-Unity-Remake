// LanguageFlags.cs
// Remake (the user's request, 2026-10-22, the Mod Builder's voice line languages): a small flag for each of the game's text
// languages (LanguageTables.Offered; Japanese too), and a globe for "every language" ("*"), painted in code (the game has no
// flag images): each flag a function of the point (x right, y down, 0..1) evaluated at 3 x 3 points per texel. Simplified
// where a detail wouldn't read at this size: Spain without its arms, the Union Jack's red diagonals centred, the Chinese stars
// upright, the Ashoka Chakra's 24 spokes drawn plainly. Hindi shows India's flag, Portuguese (Brazil) Brazil's, Korean South
// Korea's (the taegeuk and the four trigrams to the official proportions). Arabic has none (no one country's). 3:2 textures,
// made once per run; null for a language without a flag.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace GoF2Remake.UI
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class LanguageFlags
    {
        const int Width = 180, Height = 120, Samples = 3;

        static readonly Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => cache.Clear();

        /// <summary>The flag of a language code ("*": a globe, every language); null when there is none.</summary>
        public static Texture2D Of(string code)
        {
            if (string.IsNullOrEmpty(code)) return null;
            if (cache.TryGetValue(code, out var t) && t != null) return t;
            var paint = Painter(code);
            if (paint == null) return null;
            t = Paint(paint);
            t.name = "flag_" + code;
            cache[code] = t;
            return t;
        }

        static Texture2D Paint(Func<float, float, Color> paint)
        {
            var px = new Color[Width * Height];
            float step = 1f / Samples;
            for (int row = 0; row < Height; row++)
                for (int col = 0; col < Width; col++)
                {
                    Color sum = Color.clear;
                    for (int sy = 0; sy < Samples; sy++)
                        for (int sx = 0; sx < Samples; sx++)
                        {
                            float x = (col + (sx + 0.5f) * step) / Width, y = (row + (sy + 0.5f) * step) / Height;
                            var c = paint(x, y);
                            sum += new Color(c.r * c.a, c.g * c.a, c.b * c.a, c.a);   // averaged premultiplied
                        }
                    sum /= Samples * Samples;
                    px[(Height - 1 - row) * Width + col] = sum.a > 0f ? new Color(sum.r / sum.a, sum.g / sum.a, sum.b / sum.a, sum.a) : Color.clear;
                }
            var t = new Texture2D(Width, Height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            t.SetPixels(px);
            t.Apply(false, true);
            return t;
        }

        static Color Hex(int rgb) => new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, 1f);

        static Func<float, float, Color> Horizontal(params int[] bands) => (x, y) => Hex(bands[Mathf.Min(bands.Length - 1, (int)(y * bands.Length))]);
        static Func<float, float, Color> Vertical(params int[] bands) => (x, y) => Hex(bands[Mathf.Min(bands.Length - 1, (int)(x * bands.Length))]);

        static Func<float, float, Color> Painter(string code)
        {
            switch (code)
            {
                case "de": return Horizontal(0x000000, 0xDD0000, 0xFFCE00);
                case "fr": return Vertical(0x0055A4, 0xFFFFFF, 0xEF4135);
                case "it": return Vertical(0x009246, 0xFFFFFF, 0xCE2B37);
                case "nl": return Horizontal(0xAE1C28, 0xFFFFFF, 0x21468B);
                case "pl": return Horizontal(0xFFFFFF, 0xDC143C);
                case "ru": return Horizontal(0xFFFFFF, 0x0039A6, 0xD52B1E);
                case "es": return (x, y) => Hex(y < 0.25f || y >= 0.75f ? 0xAA151B : 0xF1BF00);
                case "ja": return (x, y) => Disc(x, y, 0.5f, 0.5f, 0.3f) ? Hex(0xBC002D) : Hex(0xFFFFFF);
                case "en": return UnionJack;
                case "pt": return Brazil;
                case "zh": return China;
                case "hi": return India;
                case "ko": return Korea;
                case "*": return Globe;
                default: return null;
            }
        }

        /// <summary>A disc in flag units (the flag 1.5 wide, 1 high), so it stays round on the 3:2 flag.</summary>
        static bool Disc(float x, float y, float cx, float cy, float r)
        {
            float dx = (x - cx) * 1.5f, dy = y - cy;
            return dx * dx + dy * dy <= r * r;
        }

        static Color UnionJack(float x, float y)
        {
            // In the flag's own 60 x 30 units.
            float X = x * 60f, Y = y * 30f;
            float d1 = Mathf.Abs(Y * 2f - X) / Mathf.Sqrt(5f), d2 = Mathf.Abs((30f - Y) * 2f - X) / Mathf.Sqrt(5f);
            float diagonal = Mathf.Min(d1, d2);
            float cross = Mathf.Min(Mathf.Abs(X - 30f), Mathf.Abs(Y - 15f));
            if (cross <= 3f) return Hex(0xC8102E);
            if (cross <= 5f) return Hex(0xFFFFFF);
            if (diagonal <= 1f) return Hex(0xC8102E);
            if (diagonal <= 3f) return Hex(0xFFFFFF);
            return Hex(0x012169);
        }

        static Color Brazil(float x, float y)
        {
            // 20 x 14 units: the rhombus 1.7 from the edges, the globe 3.5 across the centre.
            float X = x * 20f, Y = y * 14f;
            if ((X - 10f) * (X - 10f) + (Y - 7f) * (Y - 7f) <= 3.5f * 3.5f) return Hex(0x002776);
            if (Mathf.Abs(X - 10f) / 8.3f + Mathf.Abs(Y - 7f) / 5.3f <= 1f) return Hex(0xFFDF00);
            return Hex(0x009C3B);
        }

        static Color China(float x, float y)
        {
            // 30 x 20 units: the big star at (5, 5), the small ones round it.
            float X = x * 30f, Y = y * 20f;
            if (Star(X, Y, 5f, 5f, 3f)) return Hex(0xFFDE00);
            foreach (var (cx, cy) in new[] { (10f, 2f), (12f, 4f), (12f, 7f), (10f, 9f) })
                if (Star(X, Y, cx, cy, 1f)) return Hex(0xFFDE00);
            return Hex(0xEE1C25);
        }

        static Color India(float x, float y)
        {
            int band = Mathf.Min(2, (int)(y * 3f));
            if (band == 1)
            {
                // The Ashoka Chakra: a ring, a hub and 24 spokes, in flag units.
                float dx = (x - 0.5f) * 1.5f, dy = y - 0.5f, r = Mathf.Sqrt(dx * dx + dy * dy);
                const float outer = 0.15f;
                if (r <= outer && r >= outer - 0.022f) return Hex(0x000080);
                if (r <= 0.028f) return Hex(0x000080);
                if (r < outer)
                {
                    float a = Mathf.Atan2(dy, dx) / (Mathf.PI * 2f / 24f);
                    if (Mathf.Abs(a - Mathf.Round(a)) * (Mathf.PI * 2f / 24f) * r <= 0.006f) return Hex(0x000080);
                }
                return Hex(0xFFFFFF);
            }
            return Hex(band == 0 ? 0xFF9933 : 0x138808);
        }

        static Color Korea(float x, float y)
        {
            // In flag units (1.5 wide, 1 high, y down), from the centre. The taegeuk is half the flag's height across, its
            // axis along the upper-left / lower-right diagonal: red above, blue below, the red head toward the hoist.
            float dx = (x - 0.5f) * 1.5f, dy = y - 0.5f;
            const float R = 0.25f;
            float ax = 0.75f / 0.9014f, ay = 0.5f / 0.9014f;   // the diagonal, normalised (|(0.75, 0.5)| = 0.9014)
            float u = dx * ax + dy * ay, v = dx * ay - dy * ax;   // along the diagonal, and across it (> 0 above)
            if (dx * dx + dy * dy <= R * R)
            {
                float hu = R * 0.5f;
                if ((u + hu) * (u + hu) + v * v <= hu * hu) return Hex(0xCD2E3A);
                if ((u - hu) * (u - hu) + v * v <= hu * hu) return Hex(0x0047A0);
                return Hex(v > 0f ? 0xCD2E3A : 0x0047A0);
            }
            // The trigrams on the diagonals, a quarter of the circle's width out from it: three bars 1/24 high with 1/48
            // between them, 1/4 long, a broken bar split by 1/48. Upper left geon (solid), lower right gon (broken), upper
            // right gam (broken, solid, broken), lower left ri (solid, broken, solid).
            // Each point belongs to the diagonal it is nearer: this one, or (0.75, -0.5) toward the upper right.
            float bu = dx * ax - dy * ay, bv = dx * ay + dy * ax;
            float along, across;
            bool[] broken;
            if (Mathf.Abs(v) <= Mathf.Abs(bv))
            {
                along = Mathf.Abs(u); across = v;
                broken = u < 0f ? new[] { false, false, false } : new[] { true, true, true };
            }
            else
            {
                along = Mathf.Abs(bu); across = bv;
                broken = bu > 0f ? new[] { true, false, true } : new[] { false, true, false };
            }
            const float bar = 1f / 24f, gap = 1f / 48f, start = R * 1.5f;
            if (Mathf.Abs(across) <= 0.125f && along >= start && along <= start + 3f * bar + 2f * gap)
            {
                float t = along - start;
                int i = (int)(t / (bar + gap));
                if (i < 3 && t - i * (bar + gap) <= bar && !(broken[i] && Mathf.Abs(across) < gap * 0.5f))
                    return Hex(0x000000);
            }
            return Hex(0xFFFFFF);
        }

        static Color Globe(float x, float y)
        {
            // A globe in the middle of the 3:2 frame: its outline, the equator, two parallels and two meridians.
            float dx = (x - 0.5f) * 1.5f, dy = y - 0.5f, r = Mathf.Sqrt(dx * dx + dy * dy);
            const float R = 0.42f, w = 0.022f;
            var line = new Color(0.85f, 0.93f, 0.97f, 1f);
            if (Mathf.Abs(r - R) <= w) return line;
            if (r > R) return Color.clear;
            if (Mathf.Abs(dy) <= w * 0.8f || Mathf.Abs(Mathf.Abs(dy) - R * 0.5f) <= w * 0.8f) return line;
            if (Mathf.Abs(dx) <= w * 0.8f) return line;
            // The meridian ellipse, half as wide as the globe.
            float e = Mathf.Sqrt((dx * dx) / (R * R * 0.25f) + (dy * dy) / (R * R));
            if (Mathf.Abs(e - 1f) * R * 0.5f <= w * 0.8f) return line;
            return new Color(0.13f, 0.32f, 0.45f, 0.55f);
        }

        /// <summary>Inside an upright five-pointed star of outer radius 'r' at (cx, cy).</summary>
        static bool Star(float x, float y, float cx, float cy, float r)
        {
            float dx = x - cx, dy = y - cy;
            if (dx * dx + dy * dy > r * r) return false;
            bool inside = false;
            const int n = 10;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                Vector2 a = Vertex(i, r), b = Vertex(j, r);
                if ((a.y > dy) != (b.y > dy) && dx < (b.x - a.x) * (dy - a.y) / (b.y - a.y) + a.x) inside = !inside;
            }
            return inside;
        }

        static Vector2 Vertex(int i, float r)
        {
            float radius = i % 2 == 0 ? r : r * 0.382f;
            float angle = -Mathf.PI / 2f + i * Mathf.PI / 5f;
            return new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius);
        }
    }
}
