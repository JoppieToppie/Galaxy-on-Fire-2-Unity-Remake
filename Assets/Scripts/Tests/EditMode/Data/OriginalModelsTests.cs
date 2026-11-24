// OriginalModelsTests.cs
// Feature: docs/notes/player-assets.md "The content layer": a model built at run time from the player's converted copy
// (OriginalModels) is the model the Editor's import made of the same original (the FBX of Assets/Models after
// ModelOrientationPostprocessor): the same parts, the same vertices (bounds), the triangles facing the same way (signed
// volume). Needs both: a converted copy (see PlayerCopyTests) and the imported originals; else ignored.

using System;
using System.IO;
using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Visuals;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GoF2Remake.Tests
{
    public class OriginalModelsTests
    {
        static string Cache => Environment.GetEnvironmentVariable("GOF2_ORIGINAL_CACHE")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "GOF2UnityRemakeMods", "aeitest", "cache_android");

        [SetUp]
        public void UseTheConvertedCopy()
        {
            if (!File.Exists(Path.Combine(Cache, "_manifest.tsv"))) Assert.Ignore("no converted copy of the game at " + Cache);
            GameContent.Root = Cache;
        }

        [TearDown]
        public void Back() => GameContent.Root = null;

        [TestCase("Models/main/ships/battleship_terran.fbx")]
        [TestCase("Models/main/ships/ship_001_terran.fbx")]
        [TestCase("Models/main/stations/station_kaamo_club_fx_anim_add.fbx")]
        public void AModelIsWhatTheImportMadeOfIt(string model)
        {
            var imported = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/" + model);
            if (imported == null) Assert.Ignore("the imported original isn't in this project: " + model);
            var built = OriginalModels.Build(model);
            Assert.IsNotNull(built, "built from the converted copy");
            try
            {
                var a = imported.GetComponentsInChildren<MeshFilter>(true).Where(f => f.sharedMesh != null).ToDictionary(f => f.name);
                var b = built.GetComponentsInChildren<MeshFilter>(true).Where(f => f.sharedMesh != null).ToDictionary(f => f.name);
                CollectionAssert.AreEquivalent(a.Keys, b.Keys, "the same parts");
                foreach (var name in a.Keys)
                {
                    Mesh ma = a[name].sharedMesh, mb = b[name].sharedMesh;
                    Vector3 ca = ma.bounds.center, cb = mb.bounds.center, sa = ma.bounds.size, sb = mb.bounds.size;
                    float tol = Mathf.Max(1e-3f, sa.magnitude * 1e-4f);
                    Assert.Less((ca - cb).magnitude, tol, $"{name}: bounds centre {ca} vs {cb}");
                    Assert.Less((sa - sb).magnitude, tol, $"{name}: bounds size {sa} vs {sb}");
                    double va = SignedVolume(ma), vb = SignedVolume(mb);
                    Assert.AreEqual(Math.Sign(va), Math.Sign(vb), $"{name}: the triangles face the same way ({va} vs {vb})");
                    Assert.AreEqual(va, vb, Math.Abs(va) * 0.02 + 1e-6, $"{name}: signed volume");
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(built); }
        }

        static double SignedVolume(Mesh m)
        {
            using var data = Mesh.AcquireReadOnlyMeshData(m);
            var d = data[0];
            var v = new Vector3[d.vertexCount];
            using (var na = new Unity.Collections.NativeArray<Vector3>(d.vertexCount, Unity.Collections.Allocator.Temp))
            { d.GetVertices(na); na.CopyTo(v); }
            double sum = 0;
            for (int s = 0; s < d.subMeshCount; s++)
            {
                var sm = d.GetSubMesh(s);
                using var idx = new Unity.Collections.NativeArray<int>(sm.indexCount, Unity.Collections.Allocator.Temp);
                d.GetIndices(idx, s);
                for (int i = 0; i + 2 < idx.Length; i += 3)
                    sum += Vector3.Dot(v[idx[i]], Vector3.Cross(v[idx[i + 1]], v[idx[i + 2]])) / 6.0;
            }
            return sum;
        }
    }
}
