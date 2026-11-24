// PackModelExporter.cs
// Feature: docs/notes/player-assets.md "Packs": the remake's remodelled models (the hangars' remodels, the Deep Science ship,
// ...: what no edition of the player's copy has) as .aem files the content layer reads (OriginalModels builds a model from
// the converted copy's .aem and its .gof2mesh.json sidecar), for the common pack. Written from Unity's import of the
// project's FBX, the geometry the game has drawn: each part's vertices in the model's root space mapped back to the
// original's (OriginalModels.Map inverted: (-x, y, z) / 0.05, the triangles' order reversed), normals, UVs, colours; the
// parts and their parents in the project sidecar's order (its keyframes ship as that sidecar). V4 with float data,
// indexed where 16-bit indices hold, else a triangle list. Each file is read back with the runtime's own AemMesh and
// OriginalModels.BuildMesh and compared with the FBX's meshes before it is kept.
//   Unity -batchmode -projectPath <checkout with those FBX> -executeMethod GoF2Remake.EditorTools.PackModelExporter.ExportListed
//     $GOF2_PACK_MODELS: a file of "Models/<rel>.gof2mesh.json" lines; $GOF2_PACK_OUT: where Models/<rel>.aem go

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using GoF2Remake.Data;
using GoF2Remake.Visuals;
using UnityEditor;
using UnityEngine;

namespace GoF2Remake.EditorTools
{
    public static class PackModelExporter
    {
        [Serializable] class SidecarPart { public string name, parent; }
        [Serializable] class Sidecar { public int flags; public SidecarPart[] parts; }

        static string Norm(string n) => n == null ? null : Regex.Replace(n, @"\.\d{3}$", "");

        public static void ExportListed()
        {
            string list = Environment.GetEnvironmentVariable("GOF2_PACK_MODELS"), outDir = Environment.GetEnvironmentVariable("GOF2_PACK_OUT");
            int bad = 0, done = 0;
            foreach (var rel in File.ReadAllLines(list).Select(l => l.Trim()).Where(l => l.Length > 0))
            {
                string stem = rel.Substring(0, rel.Length - ".gof2mesh.json".Length);
                try
                {
                    var bytes = Export("Assets/" + stem + ".fbx", "Assets/" + rel, out string check);
                    string to = Path.Combine(outDir, stem + ".aem");
                    Directory.CreateDirectory(Path.GetDirectoryName(to));
                    File.WriteAllBytes(to, bytes);
                    Debug.Log($"GoF2 pack model: {stem}: {check}");
                    done++;
                }
                catch (Exception e) { bad++; Debug.LogError($"GoF2 pack model: {stem}: {e.Message}"); }
            }
            Debug.Log($"GoF2 pack models: {done} written, {bad} failed");
            if (Application.isBatchMode) EditorApplication.Exit(bad == 0 ? 0 : 1);
        }

        /// <summary>The FBX's model as an .aem file, checked against the FBX; 'check' says how close.</summary>
        public static byte[] Export(string fbx, string sidecarPath, out string check)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbx) ?? throw new Exception("no model at " + fbx);
            var side = JsonUtility.FromJson<Sidecar>(File.ReadAllText(sidecarPath));
            string baseName = Path.GetFileNameWithoutExtension(fbx);
            var byName = new Dictionary<string, Transform>();
            foreach (var t in model.GetComponentsInChildren<Transform>(true)) byName[Norm(t.name)] = t;
            var root = model.transform.worldToLocalMatrix;
            var parts = side.parts;
            var childrenOf = new Dictionary<string, List<int>>();
            for (int i = 0; i < parts.Length; i++)
            {
                string parent = Norm(parts[i].parent);
                if (parent == null || parent == baseName || !parts.Any(p => Norm(p.name) == parent)) parent = "";
                if (!childrenOf.TryGetValue(parent, out var l)) childrenOf[parent] = l = new List<int>();
                l.Add(i);
            }
            bool colours = false;
            foreach (var p in parts)
                if (byName.TryGetValue(Norm(p.name), out var t) && t.GetComponent<MeshFilter>()?.sharedMesh is Mesh m && m.colors.Length > 0) colours = true;
            int flags = 2 | 4 | (colours ? 8 : 0);
            // indexed when every part's indices fit 16 bits (the flag is per file)
            bool indexed = parts.All(p => !byName.TryGetValue(Norm(p.name), out var t) || !(t.GetComponent<MeshFilter>()?.sharedMesh is Mesh m)
                                            || (m.vertexCount <= 65535 && m.triangles.Length <= 65535));
            if (indexed) flags |= 0x10;
            var w = new BinaryWriter(new MemoryStream());
            w.Write(Encoding.ASCII.GetBytes("V4AEMesh\0"));
            w.Write((byte)flags);
            var top = childrenOf.TryGetValue("", out var tops) ? tops : new List<int>();
            w.Write((ushort)top.Count);
            var meshes = new Dictionary<int, (Vector3[] v, int[] tris)>();
            void Part(int i)
            {
                byName.TryGetValue(Norm(parts[i].name), out var t);
                var mesh = t != null ? t.GetComponent<MeshFilter>()?.sharedMesh : null;
                var m4 = t != null ? root * t.localToWorldMatrix : Matrix4x4.identity;
                Vector3[] v = mesh != null ? mesh.vertices.Select(x => m4.MultiplyPoint3x4(x)).ToArray() : new Vector3[0];
                Vector3[] n = mesh != null && mesh.normals.Length == v.Length ? mesh.normals.Select(x => m4.MultiplyVector(x).normalized).ToArray() : new Vector3[v.Length];
                Vector2[] uv = mesh != null && mesh.uv.Length == v.Length ? mesh.uv : new Vector2[v.Length];
                Color[] col = mesh != null && mesh.colors.Length == v.Length ? mesh.colors : Enumerable.Repeat(Color.white, v.Length).ToArray();
                int[] tris = mesh != null ? mesh.triangles : new int[0];
                // a triangle list when the indices won't fit: every corner its own vertex
                // (in the file each triangle's corners in the original's order, a c b: the runtime turns them back)
                if (!indexed)
                {
                    var corner = new List<int>(tris.Length);
                    for (int k = 0; k < tris.Length; k += 3) { corner.Add(tris[k]); corner.Add(tris[k + 2]); corner.Add(tris[k + 1]); }
                    v = corner.Select(k => v[k]).ToArray(); n = corner.Select(k => n[k]).ToArray(); uv = corner.Select(k => uv[k]).ToArray();
                    col = corner.Select(k => col[k]).ToArray();
                    tris = new int[corner.Count];
                    for (int k = 0; k < tris.Length; k += 3) { tris[k] = k; tris[k + 1] = k + 2; tris[k + 2] = k + 1; }
                }
                if (v.Length > 65535) throw new Exception($"{parts[i].name}: {v.Length} vertices, more than an .aem part holds");
                meshes[i] = (v, tris);
                w.Write(0f); w.Write(0f); w.Write(0f);   // the pivot (the runtime builds from the vertices as they are)
                if (indexed)
                {
                    w.Write((ushort)tris.Length);
                    for (int k = 0; k < tris.Length; k += 3) { w.Write((ushort)tris[k]); w.Write((ushort)tris[k + 2]); w.Write((ushort)tris[k + 1]); }
                }
                w.Write((ushort)v.Length);
                foreach (var x in v) { w.Write(-x.x / OriginalModels.MetersPerUnit); w.Write(x.y / OriginalModels.MetersPerUnit); w.Write(x.z / OriginalModels.MetersPerUnit); }
                foreach (var x in uv) { w.Write(x.x); w.Write(x.y); }
                foreach (var x in n) { w.Write(-x.x); w.Write(x.y); w.Write(x.z); }
                if (colours) foreach (var x in col) { w.Write(x.r); w.Write(x.g); w.Write(x.b); w.Write(x.a); }
                // keyframes: none here (the sidecar has them): a bounding sphere, no position / rotation / scale, no extra
                for (int k = 0; k < 4; k++) w.Write(0f);
                w.Write((short)-1); w.Write((short)-1); w.Write((short)-1); w.Write((short)-1);
                var kids = childrenOf.TryGetValue(Norm(parts[i].name), out var ks) ? ks : new List<int>();
                w.Write((ushort)kids.Count);
                foreach (int c in kids) Part(c);
            }
            foreach (int i in top) Part(i);
            var bytes = ((MemoryStream)w.BaseStream).ToArray();

            // read back as the game reads it, against the FBX
            var aem = AemMesh.Read(bytes);
            var built = aem.AllParts().Select(p => p.part).ToList();
            var order = new List<int>();
            void Walk(int i) { order.Add(i); if (childrenOf.TryGetValue(Norm(parts[i].name), out var ks)) foreach (int c in ks) Walk(c); }
            foreach (int i in top) Walk(i);
            if (built.Count != order.Count) throw new Exception($"read back {built.Count} parts of {order.Count}");
            float worst = 0f; int tri = 0;
            for (int k = 0; k < order.Count; k++)
            {
                var (v, tris) = meshes[order[k]];
                var m = OriginalModels.BuildMesh(built[k], "check");
                var mv = m.vertices; var mt = m.triangles;
                if (mv.Length != v.Length || mt.Length != tris.Length) throw new Exception($"part {k}: {mv.Length} / {mt.Length} against {v.Length} / {tris.Length}");
                for (int q = 0; q < v.Length; q++) worst = Mathf.Max(worst, (mv[q] - v[q]).magnitude);
                for (int q = 0; q < tris.Length; q++) if (mt[q] != tris[q]) throw new Exception($"part {k}: triangle {q / 3} differs");
                tri += tris.Length / 3;
                UnityEngine.Object.DestroyImmediate(m);
            }
            check = $"{order.Count} parts, {tri} triangles, {(indexed ? "indexed" : "triangle list")}, vertices within {worst:0.######} m of the FBX";
            return bytes;
        }
    }
}
