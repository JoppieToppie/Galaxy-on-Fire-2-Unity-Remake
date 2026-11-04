// MeshSmoother.cs
// Modified: a rounder copy of a low-poly effect mesh (the gamma shield blaze's cone, PlayerHealth.SoftenBlaze; a player's
// request: its polygons showed along the silhouette). The normals are averaged over every vertex at the same place (the
// UV seams' copies too), then each triangle is split in four 'levels' times with Phong tessellation: an edge's midpoint is
// pulled toward the tangent planes of its two ends, so the surface bulges between the original vertices as a smooth
// surface would, while the original vertices stay where they were. UVs, colours and normals are interpolated; a midpoint
// is shared by the triangles on both sides of its edge, so no cracks open. The second UV channel's x is 0 on the open
// edges and 1 inside (OpenEdges), for a shader to fade a hole's rim.
// A mesh lying on a sphere (within 3 %: the blaze glow's cap) is put onto it exactly, its normals radial.

using System.Collections.Generic;
using UnityEngine;

namespace GoF2Remake.Visuals
{
    public static class MeshSmoother
    {
        /// <summary>How far an edge's midpoint moves to the smooth surface (Phong tessellation's alpha, 0..1).</summary>
        const float Shape = 0.75f;
        /// <summary>A vertex whose faces' normals sum to less than this share of their lengths is a sharp point.</summary>
        const float SharpRatio = 0.75f;

        public static Mesh Smooth(Mesh src, int levels) => Smooth(src, levels, out _);

        /// <summary>'onSphere': the mesh lay on a sphere and was rounded onto it.</summary>
        public static Mesh Smooth(Mesh src, int levels, out bool onSphere)
        {
            onSphere = false;
            if (src == null || !src.isReadable) return null;
            var pos = new List<Vector3>(src.vertices);
            var uv = new List<Vector2>(src.uv);
            var col = new List<Color>(src.colors);
            bool hasUv = uv.Count == pos.Count, hasCol = col.Count == pos.Count;
            var nrm = SmoothNormals(pos, src.triangles, src.normals, out var sharp);
            var edge = OpenEdges(pos, src.triangles);
            // A part of a sphere (the blaze's cones are caps of one, radius 0.5): every point onto it and its normals radial,
            // instead of Phong's guess, which bulged where the file's triangles are uneven (a belly near the glow's centre, a
            // player's report).
            bool sphere = FitSphere(pos, out var centre, out float radius);
            onSphere = sphere;
            if (sphere)
                for (int i = 0; i < pos.Count; i++) (pos[i], nrm[i]) = OnSphere(pos[i], nrm[i], centre, radius);
            var tris = new List<int>(src.triangles);

            for (int l = 0; l < levels; l++)
            {
                var mid = new Dictionary<long, int>();
                var next = new List<int>(tris.Count * 4);
                int Mid(int a, int b)
                {
                    long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                    if (mid.TryGetValue(key, out int m)) return m;
                    Vector3 pa = pos[a], pb = pos[b], na = nrm[a], nb = nrm[b];
                    var c = (pa + pb) * 0.5f;
                    // A sharp point (the cone's tip, its faces turned every way round it) keeps its edges straight: its
                    // averaged normal's tangent plane would flatten them into petals (a player's report: spikes at the tip).
                    var qa = sharp[a] ? c : c - Vector3.Dot(c - pa, na) * na;
                    var qb = sharp[b] ? c : c - Vector3.Dot(c - pb, nb) * nb;
                    var n = na + nb;
                    n = n.sqrMagnitude > 1e-12f ? n.normalized : na;
                    if (sphere) { var (sp, sn) = OnSphere(c, n, centre, radius); pos.Add(sp); nrm.Add(sn); }
                    else { pos.Add(Vector3.Lerp(c, (qa + qb) * 0.5f, Shape)); nrm.Add(n); }
                    sharp.Add(false);
                    edge.Add((edge[a] + edge[b]) * 0.5f);
                    if (hasUv) uv.Add((uv[a] + uv[b]) * 0.5f);
                    if (hasCol) col.Add((col[a] + col[b]) * 0.5f);
                    m = pos.Count - 1;
                    mid[key] = m;
                    return m;
                }
                for (int i = 0; i + 2 < tris.Count; i += 3)
                {
                    int a = tris[i], b = tris[i + 1], c = tris[i + 2];
                    int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                    next.Add(a); next.Add(ab); next.Add(ca);
                    next.Add(ab); next.Add(b); next.Add(bc);
                    next.Add(ca); next.Add(bc); next.Add(c);
                    next.Add(ab); next.Add(bc); next.Add(ca);
                }
                tris = next;
            }

            var mesh = new Mesh { name = src.name + " (smooth)" };
            if (pos.Count > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(pos);
            mesh.SetNormals(nrm);
            if (hasUv) mesh.SetUVs(0, uv);
            if (hasCol) mesh.SetColors(col);
            var edgeUv = new List<Vector2>(edge.Count);
            foreach (var e in edge) edgeUv.Add(new Vector2(e, 0f));
            mesh.SetUVs(1, edgeUv);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        static (Vector3, Vector3) OnSphere(Vector3 p, Vector3 n, Vector3 centre, float radius)
        {
            var d = p - centre;
            if (d.sqrMagnitude < 1e-12f) return (p, n);
            d.Normalize();
            return (centre + d * radius, Vector3.Dot(d, n) < 0f ? -d : d);
        }

        /// <summary>The sphere through the points (least squares: x^2 + y^2 + z^2 = 2 c.x x + 2 c.y y + 2 c.z z + k); true when
        /// every point lies within 3 % of its radius of it.</summary>
        static bool FitSphere(List<Vector3> pos, out Vector3 centre, out float radius)
        {
            centre = Vector3.zero; radius = 0f;
            if (pos.Count < 8) return false;
            var a = new double[4, 5];
            foreach (var p in pos)
            {
                double[] row = { 2 * p.x, 2 * p.y, 2 * p.z, 1 };
                double rhs = (double)p.x * p.x + (double)p.y * p.y + (double)p.z * p.z;
                for (int i = 0; i < 4; i++)
                {
                    for (int j = 0; j < 4; j++) a[i, j] += row[i] * row[j];
                    a[i, 4] += row[i] * rhs;
                }
            }
            for (int c = 0; c < 4; c++)   // Gauss-Jordan with partial pivoting
            {
                int piv = c;
                for (int r = c + 1; r < 4; r++) if (System.Math.Abs(a[r, c]) > System.Math.Abs(a[piv, c])) piv = r;
                if (System.Math.Abs(a[piv, c]) < 1e-12) return false;
                for (int k = 0; k < 5; k++) { var t = a[c, k]; a[c, k] = a[piv, k]; a[piv, k] = t; }
                for (int r = 0; r < 4; r++)
                {
                    if (r == c) continue;
                    double f = a[r, c] / a[c, c];
                    for (int k = c; k < 5; k++) a[r, k] -= f * a[c, k];
                }
            }
            centre = new Vector3((float)(a[0, 4] / a[0, 0]), (float)(a[1, 4] / a[1, 1]), (float)(a[2, 4] / a[2, 2]));
            double r2 = a[3, 4] / a[3, 3] + centre.sqrMagnitude;
            if (r2 <= 0) return false;
            radius = (float)System.Math.Sqrt(r2);
            foreach (var p in pos) if (Mathf.Abs((p - centre).magnitude - radius) > 0.03f * radius) return false;
            return true;
        }

        /// <summary>Per vertex 0 on the mesh's open edges (an edge only one face has, by position: the UV seams' copies don't
        /// count), else 1, for the second UV channel: interpolated over the faces it fades the surface out toward a hole's
        /// rim (the blaze cone's open centre, whose cut showed as a ring round the core, a player's report).</summary>
        static List<float> OpenEdges(List<Vector3> pos, int[] tris)
        {
            Vector3Int Key(Vector3 p) => new Vector3Int(Mathf.RoundToInt(p.x * 1e4f), Mathf.RoundToInt(p.y * 1e4f), Mathf.RoundToInt(p.z * 1e4f));
            var count = new Dictionary<(Vector3Int, Vector3Int), int>();
            (Vector3Int, Vector3Int) EdgeKey(Vector3Int a, Vector3Int b) =>
                a.x < b.x || (a.x == b.x && (a.y < b.y || (a.y == b.y && a.z <= b.z))) ? (a, b) : (b, a);
            for (int i = 0; i + 2 < tris.Length; i += 3)
                for (int k = 0; k < 3; k++)
                {
                    var e = EdgeKey(Key(pos[tris[i + k]]), Key(pos[tris[i + (k + 1) % 3]]));
                    count[e] = count.TryGetValue(e, out int c) ? c + 1 : 1;
                }
            var open = new HashSet<Vector3Int>();
            foreach (var kv in count) if (kv.Value == 1) { open.Add(kv.Key.Item1); open.Add(kv.Key.Item2); }
            var result = new List<float>(pos.Count);
            for (int i = 0; i < pos.Count; i++) result.Add(open.Contains(Key(pos[i])) ? 0f : 1f);
            return result;
        }

        /// <summary>One normal per place: the faces' normals (area weighted) touching any vertex at the vertex's position, each
        /// turned to the vertex's own normal from the file (a double-sided effect has an inner and an outer copy of a place,
        /// whose faces would cancel out), summed.</summary>
        static List<Vector3> SmoothNormals(List<Vector3> pos, int[] tris, Vector3[] given, out List<bool> sharp)
        {
            sharp = new List<bool>(pos.Count);
            var faces = new Dictionary<Vector3Int, List<Vector3>>();
            Vector3Int Key(Vector3 p) => new Vector3Int(Mathf.RoundToInt(p.x * 1e4f), Mathf.RoundToInt(p.y * 1e4f), Mathf.RoundToInt(p.z * 1e4f));
            for (int i = 0; i + 2 < tris.Length; i += 3)
            {
                var n = Vector3.Cross(pos[tris[i + 1]] - pos[tris[i]], pos[tris[i + 2]] - pos[tris[i]]);
                for (int k = 0; k < 3; k++)
                {
                    var key = Key(pos[tris[i + k]]);
                    if (!faces.TryGetValue(key, out var list)) faces[key] = list = new List<Vector3>();
                    list.Add(n);
                }
            }
            bool hasGiven = given != null && given.Length == pos.Count;
            var result = new List<Vector3>(pos.Count);
            for (int i = 0; i < pos.Count; i++)
            {
                var own = hasGiven ? given[i] : Vector3.zero;
                var s = Vector3.zero;
                float total = 0f;
                if (faces.TryGetValue(Key(pos[i]), out var list))
                    foreach (var n in list) { s += hasGiven && Vector3.Dot(n, own) < 0f ? -n : n; total += n.magnitude; }
                // Its faces pointing many ways (the summed normal much shorter than their lengths): a sharp point.
                sharp.Add(total > 0f && s.magnitude < SharpRatio * total);
                result.Add(s.sqrMagnitude > 1e-12f ? s.normalized : (hasGiven ? own : Vector3.up));
            }
            return result;
        }
    }
}
