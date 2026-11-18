// AemMesh.cs
// Feature: docs/notes/player-assets.md "Importers so far": Galaxy on Fire 2's own mesh files (.aem, the ABYSS engine's
// "AEMesh") read from the player's copy of the game, as their parts' geometry and keyframes, and their animation sidecar
// (.gof2mesh.json, what PartAnimation reads). Plain C#, no Unity types. A port of Reference/tools/asset_conversion/aem.py
// (the parse, its truncation rules included) and convert_remodel.py (the sidecar).
//
// Layout: a 7-byte magic (AEMesh, V2AEMes .. V5AEMes; V2+ then 2 more bytes), a flags byte (2 UVs, 4 normals, 8 colours,
// 0x10 indices), V3+ a u16 part count, then each part:
//   V3+: pivot (3 f32); flags 0x10: u16 count + u16 indices; u16 vertex count; positions (AEMesh i16, V2 / V3 i32, V4 /
//   V5 f32, x3); UVs (old: i16 / 4096, new: f32); normals (old: i16, normalised here; new: f32); colours (old: u8 / 255,
//   new: f32, x4); V3+: the keyframes (bounding sphere, position / rotation / scale blocks, V4+ the "extra" track, V5 seven
//   more tracks) and a u16 child count, the children following with the same flags.
// A file can end inside its last part (the game's own are cut so): the part being read is dropped, or its keyframes when
// they are what's missing, as aem.py does.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace GoF2Remake.Data
{
    public sealed class AemMesh
    {
        public const int V2 = 1, V3 = 2, AE = 4, V4 = 8, V5 = 16;

        /// <summary>A keyframe track: (time, value) pairs.</summary>
        public sealed class Track { public readonly List<(float t, float v)> Keys = new List<(float t, float v)>(); }

        /// <summary>A position / rotation / scale block: mode 1 vector keys (time, xyz), mode 0 three channels, else none.</summary>
        public sealed class Block
        {
            public int Mode = -1;
            public readonly List<(float t, float x, float y, float z)> Vectors = new List<(float t, float x, float y, float z)>();
            public Track[] Axis = { new Track(), new Track(), new Track() };
        }

        public sealed class Part
        {
            public float[] Pivot;          // V3+ only
            public ushort[] Indices;       // null without the indices flag
            public int VertexCount;
            public float[] Positions;      // xyz per vertex, as stored (engine units)
            public float[] Uvs;            // uv per vertex
            public float[] Normals;        // xyz per vertex
            public float[] Colors;         // rgba per vertex, 0..1
            public bool HasAnimation;      // V3+, and the keyframes were all there
            public float[] BoundingSphere; // x y z r
            public Block Position, Rotation, Scale;
            public int ExtraMode = -1;
            public Track Extra;            // V4+ with extra mode 2
            public List<Track> V5Tracks;   // V5: seven tracks when present
            public readonly List<Part> Children = new List<Part>();
        }

        public int Version, Flags;
        public bool Truncated;
        public readonly List<Part> Parts = new List<Part>();

        public static int VersionOf(byte[] b)
        {
            if (b == null || b.Length < 7) throw new FormatException("not an AEMesh file");
            string h = Encoding.ASCII.GetString(b, 0, 7);
            switch (h)
            {
                case "V2AEMes": return V2;
                case "V3AEMes": return V3;
                case "V4AEMes": return V4;
                case "V5AEMes": return V5;
            }
            if (h.StartsWith("AEMesh")) return AE;
            throw new FormatException("not an AEMesh file");
        }

        public static AemMesh Read(byte[] b)
        {
            var m = new AemMesh { Version = VersionOf(b) };
            var r = new Reader(b) { Pos = 7 };
            if ((m.Version & (V2 | V3 | V4 | V5)) != 0) r.Pos += 2;
            m.Flags = r.U8();
            if ((m.Version & (V3 | V4 | V5)) != 0)
            {
                int n = r.U16();
                for (int i = 0; i < Math.Max(n, 1); i++)
                {
                    try { m.Parts.Add(ReadPart(r, m.Version, m.Flags)); }
                    catch (EndOfData) { m.Truncated = true; break; }
                }
            }
            else
            {
                try { m.Parts.Add(ReadPart(r, m.Version, m.Flags)); }
                catch (EndOfData) { throw new FormatException("the mesh ends early"); }
            }
            return m;
        }

        static Part ReadPart(Reader r, int ver, int flags)
        {
            var p = new Part();
            bool enh = (ver & (V3 | V4 | V5)) != 0, old = (ver & (AE | V2 | V3)) != 0;
            if (enh) p.Pivot = r.F32s(3);
            if ((flags & 0x10) != 0)
            {
                int ni = r.U16(); p.Indices = new ushort[ni];
                for (int i = 0; i < ni; i++) p.Indices[i] = (ushort)r.U16();
            }
            int nv = r.U16(); p.VertexCount = nv;
            p.Positions = new float[nv * 3];
            for (int i = 0; i < nv * 3; i++)
                p.Positions[i] = (ver & AE) != 0 ? r.I16() : (ver & (V2 | V3)) != 0 ? r.I32() : r.F32();
            if ((flags & 2) != 0)
            {
                p.Uvs = new float[nv * 2];
                for (int i = 0; i < nv * 2; i++) p.Uvs[i] = old ? r.I16() * (1f / 4096f) : r.F32();
            }
            if ((flags & 4) != 0)
            {
                p.Normals = new float[nv * 3];
                for (int i = 0; i < nv * 3; i++) p.Normals[i] = old ? r.I16() : r.F32();
                if (old)
                    for (int i = 0; i < nv; i++)
                    {
                        float x = p.Normals[i * 3], y = p.Normals[i * 3 + 1], z = p.Normals[i * 3 + 2];
                        float l = (float)Math.Sqrt(x * x + y * y + z * z); if (l == 0f) l = 1f;
                        p.Normals[i * 3] = x / l; p.Normals[i * 3 + 1] = y / l; p.Normals[i * 3 + 2] = z / l;
                    }
            }
            if ((flags & 8) != 0)
            {
                p.Colors = new float[nv * 4];
                for (int i = 0; i < nv * 4; i++) p.Colors[i] = old ? r.U8() / 255f : r.F32();
            }
            if (!enh) return p;
            int children;
            try
            {
                ReadKeyframes(r, ver, p);
                children = r.U16();
                p.HasAnimation = true;
            }
            catch (EndOfData)
            {
                // aem.py: keyframes cut short count as none and the part has no children; the file ends there (a further
                // part would run into its end too and be dropped)
                p.BoundingSphere = null; p.Position = p.Rotation = p.Scale = null; p.Extra = null; p.V5Tracks = null;
                p.ExtraMode = -1; p.HasAnimation = false; children = 0;
                r.Pos = r.Length;
            }
            for (int i = 0; i < children; i++) p.Children.Add(ReadPart(r, ver, flags));
            return p;
        }

        static void ReadKeyframes(Reader r, int ver, Part p)
        {
            p.BoundingSphere = r.F32s(4);
            p.Position = ReadBlock(r); p.Rotation = ReadBlock(r); p.Scale = ReadBlock(r);
            if ((ver & (V4 | V5)) != 0)
            {
                p.ExtraMode = r.I16();
                if (p.ExtraMode == 2) p.Extra = ReadTrack(r, r.U16());
            }
            if ((ver & V5) != 0)
            {
                int n = r.U16();
                p.V5Tracks = new List<Track>();
                if (n != 0)
                    for (int c = 0; c < 7; c++) { int k = r.I16(); p.V5Tracks.Add(ReadTrack(r, Math.Max(k, 0))); }
            }
        }

        static Block ReadBlock(Reader r)
        {
            var b = new Block { Mode = r.I16() };
            if (b.Mode == 1)
            {
                int n = r.U16();
                for (int i = 0; i < n; i++) b.Vectors.Add((r.F32(), r.F32(), r.F32(), r.F32()));
            }
            else if (b.Mode == 0)
                for (int c = 0; c < 3; c++) b.Axis[c] = ReadTrack(r, r.U16());
            return b;
        }

        static Track ReadTrack(Reader r, int n)
        {
            var t = new Track();
            for (int i = 0; i < n; i++) t.Keys.Add((r.F32(), r.F32()));
            return t;
        }

        /// <summary>Every part, depth first (the converted models' order: <name>_part0, _part1...).</summary>
        public IEnumerable<(Part part, Part parent)> AllParts()
        {
            var stack = new Stack<(Part, Part)>();
            for (int i = Parts.Count - 1; i >= 0; i--) stack.Push((Parts[i], null));
            while (stack.Count > 0)
            {
                var (p, parent) = stack.Pop();
                yield return (p, parent);
                for (int i = p.Children.Count - 1; i >= 0; i--) stack.Push((p.Children[i], p));
            }
        }

        // ---- the animation sidecar ---------------------------------------------------------------------------------

        /// <summary>The .gof2mesh.json PartAnimation reads (convert_remodel.py's format): per part its name
        /// (<name>_part<i>), parent, vertex count, pivot, which attributes it has, bounding sphere and 'channels' (targets
        /// posX/Y/Z, rotX/Y/Z, sclX/Y/Z, extra, v5_0..v5_6 with their keys).</summary>
        public string Sidecar(string name, string source)
        {
            var s = new StringBuilder();
            s.Append("{\"source\": ").Append(Str(source)).Append(", \"version\": ").Append(Version)
             .Append(", \"flags\": ").Append(Flags).Append(", \"truncated\": ").Append(Truncated ? "true" : "false")
             .Append(", \"parts\": [");
            var names = new Dictionary<Part, string>();
            int i = 0;
            foreach (var (p, parent) in AllParts())
            {
                string pn = $"{name}_part{i}"; names[p] = pn;
                if (i++ > 0) s.Append(", ");
                s.Append("{\"name\": ").Append(Str(pn)).Append(", \"parent\": ").Append(Str(parent == null ? name : names[parent]))
                 .Append(", \"vertices\": ").Append(p.VertexCount)
                 .Append(", \"pivot\": ").Append(p.Pivot == null ? "null" : Floats(p.Pivot))
                 .Append(", \"hasUV\": ").Append(p.Uvs != null ? "true" : "false")
                 .Append(", \"hasNormals\": ").Append(p.Normals != null ? "true" : "false")
                 .Append(", \"hasColors\": ").Append(p.Colors != null ? "true" : "false")
                 .Append(", \"bsphere\": ").Append(p.HasAnimation && p.BoundingSphere != null ? Floats(p.BoundingSphere) : "null")
                 .Append(", \"channels\": [");
                bool first = true;
                void Add(string target, IEnumerable<(float t, float v)> keys)
                {
                    var list = new List<(float t, float v)>(keys);
                    if (list.Count == 0) return;
                    if (!first) s.Append(", ");
                    first = false;
                    s.Append("{\"target\": ").Append(Str(target)).Append(", \"keys\": [");
                    for (int k = 0; k < list.Count; k++)
                        s.Append(k > 0 ? ", " : "").Append("{\"t\": ").Append(F(list[k].t)).Append(", \"v\": ").Append(F(list[k].v)).Append('}');
                    s.Append("]}");
                }
                if (p.HasAnimation)
                {
                    foreach (var (block, pre) in new[] { (p.Position, "pos"), (p.Rotation, "rot"), (p.Scale, "scl") })
                    {
                        if (block == null) continue;
                        if (block.Mode == 1 && block.Vectors.Count > 0)
                        {
                            Add(pre + "X", block.Vectors.ConvertAll(k => (k.t, k.x)));
                            Add(pre + "Y", block.Vectors.ConvertAll(k => (k.t, k.y)));
                            Add(pre + "Z", block.Vectors.ConvertAll(k => (k.t, k.z)));
                        }
                        else if (block.Mode == 0)
                        {
                            Add(pre + "X", block.Axis[0].Keys); Add(pre + "Y", block.Axis[1].Keys); Add(pre + "Z", block.Axis[2].Keys);
                        }
                    }
                    if (p.Extra != null) Add("extra", p.Extra.Keys);
                    if (p.V5Tracks != null) for (int c = 0; c < p.V5Tracks.Count; c++) Add("v5_" + c, p.V5Tracks[c].Keys);
                }
                s.Append("]}");
            }
            s.Append("]}");
            return s.ToString();
        }

        static string F(float v)
        {
            double d = v;
            if (double.IsNaN(d)) return "NaN";
            if (double.IsInfinity(d)) return d > 0 ? "Infinity" : "-Infinity";
            string t = d.ToString("R", CultureInfo.InvariantCulture);
            return t.Contains(".") || t.Contains("E") ? t : t + ".0";
        }

        static string Floats(float[] a)
        {
            var s = new StringBuilder("[");
            for (int i = 0; i < a.Length; i++) s.Append(i > 0 ? ", " : "").Append(F(a[i]));
            return s.Append(']').ToString();
        }

        static string Str(string v)
        {
            var s = new StringBuilder("\"");
            foreach (char c in v)
            {
                if (c == '"' || c == '\\') s.Append('\\').Append(c);
                else if (c < 0x20) s.Append("\\u").Append(((int)c).ToString("x4"));
                else s.Append(c);
            }
            return s.Append('"').ToString();
        }

        // ---- reading -----------------------------------------------------------------------------------------------

        sealed class EndOfData : Exception { }

        sealed class Reader
        {
            readonly byte[] b;
            public int Pos;
            public int Length => b.Length;
            public Reader(byte[] b) { this.b = b; }
            void Need(int n) { if (Pos + n > b.Length) throw new EndOfData(); }
            public int U8() { Need(1); return b[Pos++]; }
            public int U16() { Need(2); int v = b[Pos] | b[Pos + 1] << 8; Pos += 2; return v; }
            public int I16() { Need(2); int v = (short)(b[Pos] | b[Pos + 1] << 8); Pos += 2; return v; }
            public int I32() { Need(4); int v = BitConverter.ToInt32(b, Pos); Pos += 4; return v; }
            public float F32() { Need(4); float v = BitConverter.ToSingle(b, Pos); Pos += 4; return v; }
            public float[] F32s(int n) { var a = new float[n]; for (int i = 0; i < n; i++) a[i] = F32(); return a; }
        }
    }
}
