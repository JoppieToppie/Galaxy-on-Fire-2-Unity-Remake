// EngineGlowShape.cs
// The outline of a mod ship's engine flame (remake): a mount's "glowShape" ("round", the default, "rounded", "square",
// "hexagon", "diamond" or "triangle"), stretched to its glowSize's half width / half height. ModShipBuilder.BuildEngineGlow
// draws the disc and its flared ring on Outline; ModExhausts.ShapesOf takes Radius as an exhaust style's nozzle shape.

using UnityEngine;

namespace GoF2Remake.Modding
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class EngineGlowShape
    {
        /// <summary>The shapes a mount's glowShape can name, the builder's buttons in this order.</summary>
        public static readonly string[] Names = { "round", "rounded", "square", "hexagon", "diamond", "triangle" };

        /// <summary>A glowShape as one of Names (lower case); anything else (none, a typo) is "round".</summary>
        public static string Normalize(string shape)
        {
            if (string.IsNullOrEmpty(shape)) return "round";
            string s = shape.Trim().ToLowerInvariant();
            if (s == "rectangle" || s == "rect") return "square";
            foreach (var n in Names) if (n == s) return n;
            return "round";
        }

        /// <summary>The outline in the unit box (x across, y up, both -1..1), clockwise from the
        /// top as the round glow's twelve segments always ran, so a "round" mount builds the same mesh as before.</summary>
        public static Vector2[] Outline(string shape)
        {
            switch (Normalize(shape))
            {
                case "square": return Polygon(3, new Vector2(-1f, 1f), new Vector2(1f, 1f), new Vector2(1f, -1f), new Vector2(-1f, -1f));
                case "hexagon":
                    return Polygon(2, new Vector2(-0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(1f, 0f), new Vector2(0.5f, -1f),
                        new Vector2(-0.5f, -1f), new Vector2(-1f, 0f));
                case "diamond": return Polygon(3, new Vector2(0f, 1f), new Vector2(1f, 0f), new Vector2(0f, -1f), new Vector2(-1f, 0f));
                case "triangle": return Polygon(4, new Vector2(0f, 1f), new Vector2(1f, -1f), new Vector2(-1f, -1f));
                case "rounded":
                {
                    // A rectangle with corners of 0.4 of its half size, four points per corner.
                    const float R = 0.4f;
                    var p = new Vector2[16];
                    var centres = new[] { new Vector2(1f - R, 1f - R), new Vector2(1f - R, -1f + R), new Vector2(-1f + R, -1f + R), new Vector2(-1f + R, 1f - R) };
                    for (int c = 0; c < 4; c++)
                        for (int k = 0; k < 4; k++)
                        {
                            float a = (c + k / 3f) * Mathf.PI * 0.5f;
                            p[c * 4 + k] = centres[c] + new Vector2(Mathf.Sin(a), Mathf.Cos(a)) * R;
                        }
                    return p;
                }
                default:
                {
                    var p = new Vector2[12];
                    for (int i = 0; i < p.Length; i++)
                    {
                        float a = i * Mathf.PI * 2f / p.Length;
                        p[i] = new Vector2(Mathf.Sin(a), Mathf.Cos(a));
                    }
                    return p;
                }
            }
        }

        /// <summary>The distance from the centre to the outline stretched to 'halfWidth' x 'halfHeight', along (cos a, sin a).</summary>
        public static float Radius(string shape, float halfWidth, float halfHeight, float angle)
        {
            string s = Normalize(shape);
            var d = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            if (s == "round")
            {
                float x = d.x / halfWidth, y = d.y / halfHeight;
                return 1f / Mathf.Sqrt(x * x + y * y);
            }
            // The ray against each edge of the stretched outline (all the shapes are star-shaped about the centre).
            var o = Outline(s);
            float best = 0f;
            for (int i = 0; i < o.Length; i++)
            {
                Vector2 a = Vector2.Scale(o[i], new Vector2(halfWidth, halfHeight)), b = Vector2.Scale(o[(i + 1) % o.Length], new Vector2(halfWidth, halfHeight));
                var e = b - a;
                float den = d.x * e.y - d.y * e.x;
                if (Mathf.Abs(den) < 1e-9f) continue;
                float t = (a.x * e.y - a.y * e.x) / den, u = (a.x * d.y - a.y * d.x) / den;
                if (t > 0f && u >= -1e-5f && u <= 1f + 1e-5f) best = Mathf.Max(best, t);
            }
            return best;
        }

        /// <summary>The corners, each edge cut into 'steps' segments.</summary>
        static Vector2[] Polygon(int steps, params Vector2[] corners)
        {
            var p = new Vector2[corners.Length * steps];
            for (int c = 0; c < corners.Length; c++)
                for (int k = 0; k < steps; k++)
                    p[c * steps + k] = Vector2.Lerp(corners[c], corners[(c + 1) % corners.Length], k / (float)steps);
            return p;
        }
    }
}
