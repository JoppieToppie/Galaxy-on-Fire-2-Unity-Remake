// SkyStars.cs
// Remake (2026-11-14, the user's request: GoF2's skies rebuilt "the same way Chorus does"): the Enhanced skies' stars
// (Settings.EnhancedSkies). Reference/tools/sky/enhance_skies.py lifts every point star of the stars layers and every speck of
// the nebulae out of the baked skies into star lists (Resources/GoF2SkyHd/<layer>.bytes: a direction, a size in radians, an
// amplitude); GoF2/SpaceSky draws them as sharp points at any resolution (SpaceSkyCore.hlsl StarPoints) over what stays of the
// layers (GoF2SkyHd's cubemaps: the big stars' halos, the clouds upscaled).
//
// The shader finds the stars near a direction through a grid on the cube's faces (its StripUV layout, v up): GridSize x
// GridSize cells a face, two star slots a cell, each the star's number + 1 in 16 bits (an RGBA32 texel: hi, lo, hi, lo; 0 =
// empty), the stars themselves two RGBAFloat texels each in a data texture DataWidth wide (direction, size; amplitude, layer).
// A pixel reads the 2 x 2 cells round it, so a star is seen within half a cell; one within half a cell of a face's edge is put
// in the edge cell of the face beyond too (that side's pixels clamp to it). The brightest stars take the slots first: a third
// star in a cell is left out (Grid's 'dropped').
using System.Collections.Generic;
using UnityEngine;

namespace GoF2Remake.World
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class SkyStars
    {
        public const int GridSize = 512, DataWidth = 1024, Slots = 2, MaxStars = 65534;
        public const int LayerStars = 0, LayerNebula = 1;
        static readonly byte[] Magic = { (byte)'G', (byte)'S', (byte)'L', (byte)'1' };

        public struct Star
        {
            public Vector3 dir;    // unit, in the sky's (baked cube's) frame
            public float sigma;    // the source's size, radians
            public Vector3 amp;    // linear rgb at the centre
            public int layer;      // LayerStars (x _StarsGain) or LayerNebula (x _NebulaGain)
            public float Brightness => Mathf.Max(amp.x, Mathf.Max(amp.y, amp.z)) * sigma * sigma;
        }

        /// <summary>A star list (enhance_skies.py write_list); empty when it isn't one.</summary>
        public static Star[] Parse(byte[] b)
        {
            if (b == null || b.Length < 12 || b[0] != Magic[0] || b[1] != Magic[1] || b[2] != Magic[2] || b[3] != Magic[3]) return new Star[0];
            int n = System.BitConverter.ToInt32(b, 4), layer = System.BitConverter.ToInt32(b, 8);
            if (n < 0 || b.Length < 12 + n * 28) return new Star[0];
            var stars = new Star[n];
            for (int i = 0; i < n; i++)
            {
                int o = 12 + i * 28;
                float F(int k) => System.BitConverter.ToSingle(b, o + k * 4);
                stars[i] = new Star { dir = new Vector3(F(0), F(1), F(2)), sigma = F(3), amp = new Vector3(F(4), F(5), F(6)), layer = layer };
            }
            return stars;
        }

        /// <summary>The cube face of 'd' and where on it (0..1, v up), as GoF2/SpaceSky's StripUV (Unity's strip order
        /// +X -X +Y -Y +Z -Z, each face as seen from inside); 'onFace' >= 0 projects onto that face instead (past 0..1 off it).</summary>
        public static int Face(Vector3 d, out Vector2 uv, int onFace = -1)
        {
            float ax = Mathf.Abs(d.x), ay = Mathf.Abs(d.y), az = Mathf.Abs(d.z);
            int face = onFace >= 0 ? onFace
                : ax >= ay && ax >= az ? (d.x > 0 ? 0 : 1)
                : ay >= az ? (d.y > 0 ? 2 : 3)
                : d.z > 0 ? 4 : 5;
            Vector2 f;
            switch (face)
            {
                case 0: f = new Vector2(-d.z, d.y) / ax; break;
                case 1: f = new Vector2(d.z, d.y) / ax; break;
                case 2: f = new Vector2(d.x, -d.z) / ay; break;
                case 3: f = new Vector2(d.x, d.z) / ay; break;
                case 4: f = new Vector2(d.x, d.y) / az; break;
                default: f = new Vector2(-d.x, d.y) / az; break;
            }
            uv = f * 0.5f + new Vector2(0.5f, 0.5f);
            return face;
        }

        static readonly Vector3[] Axis = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };

        /// <summary>The grid's slots (6 g x g cells, row by row from the bottom, Slots each): the stars' numbers in 'order' + 1, 0 empty.
        /// 'order': the stars brightest first (Order).</summary>
        public static ushort[] Cells(IReadOnlyList<Star> stars, int[] order, int g, out int dropped)
        {
            var cells = new ushort[6 * g * g * Slots];
            dropped = 0;
            float margin = 0.5f / g;
            for (int k = 0; k < order.Length; k++)
            {
                var d = stars[order[k]].dir;
                bool placed = false;
                for (int face = 0; face < 6; face++)
                {
                    if (Vector3.Dot(d, Axis[face]) <= 0.5f) continue;   // (more than 60 deg off the face's axis: never near it)
                    Face(d, out var uv, face);
                    if (uv.x < -margin || uv.y < -margin || uv.x > 1 + margin || uv.y > 1 + margin) continue;
                    int cx = Mathf.Clamp((int)(uv.x * g), 0, g - 1), cy = Mathf.Clamp((int)(uv.y * g), 0, g - 1);
                    int at = ((cy * 6 * g) + face * g + cx) * Slots;
                    for (int s = 0; s < Slots; s++)
                        if (cells[at + s] == 0) { cells[at + s] = (ushort)(k + 1); placed = true; break; }
                }
                if (!placed) dropped++;
            }
            return cells;
        }

        /// <summary>The stars' numbers, brightest first, at most MaxStars.</summary>
        public static int[] Order(IReadOnlyList<Star> stars)
        {
            var order = new int[stars.Count];
            for (int i = 0; i < order.Length; i++) order[i] = i;
            System.Array.Sort(order, (a, b) => stars[b].Brightness.CompareTo(stars[a].Brightness));
            if (order.Length > MaxStars) System.Array.Resize(ref order, MaxStars);
            return order;
        }

        // ---- the textures --------------------------------------------------------------------------------------------

        static readonly Dictionary<string, Star[]> lists = new();
        static readonly Dictionary<(int, int), (Texture2D index, Texture2D data)> grids = new();
        static readonly List<(int, int)> recent = new();
        const int KeptGrids = 4;   // (one dropped from here is unloaded with the next scene's unused assets once no sky has it)

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { lists.Clear(); grids.Clear(); recent.Clear(); }

        /// <summary>The star list of a GoF2SkyHd layer ("stars_000", "nebula_016"); empty without it.</summary>
        public static Star[] List(string layer)
        {
            if (lists.TryGetValue(layer, out var l)) return l;
            var bytes = Data.GameContent.Bytes("GoF2SkyHd/" + layer);   // (the build's, else the HD pack's in the converted copy)
            l = bytes != null ? Parse(bytes) : new Star[0];
            return lists[layer] = l;
        }

        /// <summary>Whether this device can draw them (the data texture is 32-bit float: half precision is coarser than a star).</summary>
        public static bool Supported => SystemInfo.SupportsTextureFormat(TextureFormat.RGBAFloat);

        /// <summary>The grid of stars layer 'stars' and nebula 'nebula' together (-1: none of that layer); null without stars.</summary>
        public static (Texture2D index, Texture2D data)? Grid(int stars, int nebula)
        {
            var key = (stars, nebula);
            if (grids.TryGetValue(key, out var hit)) { recent.Remove(key); recent.Add(key); return hit; }
            var all = new List<Star>();
            if (stars >= 0) all.AddRange(List($"stars_{stars:000}"));
            if (nebula >= 0) all.AddRange(List($"nebula_{nebula:000}"));
            if (all.Count == 0 || !Supported) return null;
            var order = Order(all);
            var cells = Cells(all, order, GridSize, out int dropped);
            if (dropped > 0) Debug.Log($"SkyStars: stars {stars} + nebula {nebula}: {dropped} of {order.Length} stars left out (full cells)");

            var bytes = new byte[cells.Length * 2];
            for (int i = 0; i < cells.Length; i++) { bytes[i * 2] = (byte)(cells[i] >> 8); bytes[i * 2 + 1] = (byte)cells[i]; }
            var index = new Texture2D(6 * GridSize, GridSize, TextureFormat.RGBA32, false, true)
            { name = $"SkyStars index {stars} {nebula}", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            index.SetPixelData(bytes, 0);
            index.Apply(false, true);

            int rows = Mathf.Max(1, (order.Length * 2 + DataWidth - 1) / DataWidth);
            var texels = new float[DataWidth * rows * 4];
            for (int k = 0; k < order.Length; k++)
            {
                var s = all[order[k]];
                int o = k * 8;
                texels[o] = s.dir.x; texels[o + 1] = s.dir.y; texels[o + 2] = s.dir.z; texels[o + 3] = s.sigma;
                texels[o + 4] = s.amp.x; texels[o + 5] = s.amp.y; texels[o + 6] = s.amp.z; texels[o + 7] = s.layer;
            }
            var data = new Texture2D(DataWidth, rows, TextureFormat.RGBAFloat, false, true)
            { name = $"SkyStars data {stars} {nebula}", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            data.SetPixelData(texels, 0);
            data.Apply(false, true);

            var grid = (index, data);
            grids[key] = grid;
            recent.Add(key);
            if (recent.Count > KeptGrids) { grids.Remove(recent[0]); recent.RemoveAt(0); }
            return grid;
        }

        /// <summary>The grid on a GoF2/SpaceSky (or KhadorRift) material: _StarGrid (grid size, data width; 0: off).</summary>
        public static void Apply(Material sky, (Texture2D index, Texture2D data)? grid)
        {
            if (grid is not { } g) { sky.SetVector("_StarGrid", Vector4.zero); return; }
            sky.SetTexture("_StarIndex", g.index);
            sky.SetTexture("_StarData", g.data);
            sky.SetVector("_StarGrid", new Vector4(GridSize, DataWidth, 0f, 0f));
        }
    }
}
