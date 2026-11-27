// NebulaCloudsTests.cs
// Feature: docs/notes/space-scene.md "Nebula clouds": Options > Graphics "Nebula clouds" (Settings.NebulaClouds, off by default)
// puts clouds of gas round an orbit in its nebula sky's colours (World.NebulaClouds, GoF2/NebulaCloud), placed as EVERSPACE 2
// places its local nebulae' pieces, from the remake's own assets (Resources/GoF2SkyLayers, Reference/tools/sky/nebula_clouds.py).

using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.World;
using NUnit.Framework;
using UnityEngine;

namespace GoF2Remake.Tests
{
    public class NebulaCloudsTests : SavesModsTest
    {
        [Test]
        public void EveryGoF2SkyHasItsCloudLook()
        {
            for (int i = 0; i <= 18; i++)
            {
                var sky = NebulaClouds.Of(i);
                Assert.IsNotNull(sky, $"nebula {i}");
                Assert.IsNotEmpty(sky.colors, $"nebula {i}'s colours");
                Assert.AreEqual(1f, sky.colors.Sum(c => c.weight), 0.01f, $"nebula {i}: the colours' shares");
            }
            Assert.IsNull(NebulaClouds.Of(19));
            Assert.IsNotNull(Resources.Load<Texture2D>("GoF2SkyLayers/cloud_masks"), "the remake's own masks, no other game's");
        }

        [Test]
        public void AStartThatNeverReachedTheMenuPutsTheSkiesBack()
        {
            var keys = new[] { "skyStyle", "nebulaClouds", "startupPending" };
            try
            {
                Settings.SkyStyle = Settings.SkyEs2;
                Settings.NebulaClouds = true;
                Assert.IsFalse(Settings.GuardStartup(), "the first start: nothing failed before it");
                Settings.StartupReached();
                Assert.IsFalse(Settings.GuardStartup(), "the menu came up last time: the skies stay");
                Assert.AreEqual(Settings.SkyEs2, Settings.SkyStyle);
                Assert.IsTrue(Settings.GuardStartup(), "the last start hung before the menu (#371)");
                Assert.AreEqual(Settings.SkyOriginal, Settings.SkyStyle);
                Assert.IsFalse(Settings.NebulaClouds);
            }
            finally { Settings.Reset(keys); }
        }

        [Test]
        public void ThereAreMoreCloudsUnderFullerSkies()
        {
            Assert.AreEqual(0, NebulaClouds.CountFor(0.01f));
            Assert.AreEqual(1, NebulaClouds.CountFor(0.03f));
            Assert.AreEqual(2, NebulaClouds.CountFor(0.2f));
            Assert.AreEqual(3, NebulaClouds.CountFor(1f), "ES2's levels have one to five, most two");
        }

        [Test]
        public void ACloudIsAFlattenedDiscOfPieces()
        {
            const float radius = 10000f;
            var pieces = NebulaClouds.Pieces(new System.Random(5), radius);
            Assert.Greater(pieces.Count, 5);
            Assert.Less(pieces.Count, NebulaClouds.Candidates, "the points too far off its plane are left out");
            foreach (var p in pieces)
            {
                Assert.LessOrEqual(p.position.magnitude, radius * 1.0001f);
                Assert.Less(Mathf.Abs(p.position.z), NebulaClouds.HalfHeight * radius, "ES2: under HalfHeight x radius x r^2");
                Assert.That(p.size, Is.InRange(radius * NebulaClouds.PieceShare, 3f * radius * NebulaClouds.PieceShare), "ES2: 1-3 x its piece size");
                Assert.That(p.channel, Is.InRange(0, 2));
            }
            var again = NebulaClouds.Pieces(new System.Random(5), radius);
            Assert.AreEqual(pieces.Select(p => p.position), again.Select(p => p.position), "seeded: an orbit's clouds are the same each visit");
        }

        [Test]
        public void TheCloudsComeOnlyWithTheOption()
        {
            var db = Database.Load();
            var layout = OrbitLayout.Build(db, db.Stations.First(s => NebulaClouds.CountFor(NebulaClouds.Of(db.Systems.First(y => y.index == s.system).textureIndex)?.coverage ?? 0f) > 0).index);
            Settings.NebulaClouds = false;
            Assert.IsNull(NebulaClouds.Spawn(layout), "off by default: the original has none");
            Settings.NebulaClouds = true;
            NebulaClouds a = null, b = null;
            try
            {
                a = NebulaClouds.Spawn(layout);
                Assert.IsNotNull(a);
                var mesh = a.GetComponent<MeshFilter>().sharedMesh;
                Assert.Greater(mesh.vertexCount, 0);
                Assert.AreEqual(0, mesh.vertexCount % 4, "flat sprites");
                Assert.AreEqual("GoF2/NebulaCloud", a.GetComponent<MeshRenderer>().sharedMaterial.shader.name);
                b = NebulaClouds.Spawn(layout);
                CollectionAssert.AreEqual(mesh.vertices, b.GetComponent<MeshFilter>().sharedMesh.vertices, "the same orbit: the same clouds");
            }
            finally
            {
                Settings.NebulaClouds = false;
                if (a != null) Object.DestroyImmediate(a.gameObject);
                if (b != null) Object.DestroyImmediate(b.gameObject);
            }
        }

        [Test]
        public void Es2StyleDrawsTheSkysNebulaeAsDistantClouds()
        {
            var db = Database.Load();
            var st = db.Stations.First(s => db.Systems.First(y => y.index == s.system).textureIndex == 7);
            var layout = OrbitLayout.Build(db, st.index);
            int style = Settings.SkyStyle; bool live = Settings.LiveSkies;
            NebulaClouds plain = null, es2 = null;
            try
            {
                Settings.NebulaClouds = false; Settings.LiveSkies = true;
                Settings.SkyStyle = Settings.SkyOriginal;
                Assert.IsNull(NebulaClouds.Spawn(layout), "Original without the option: none");
                Settings.SkyStyle = Settings.SkyCombined;
                plain = NebulaClouds.Spawn(layout);
                Assert.IsNotNull(plain, "Combined: ES2's local nebulae round the orbit");
                Settings.SkyStyle = Settings.SkyEs2;
                es2 = NebulaClouds.Spawn(layout);
                // the same local nebulae first (the same seed), then ES2 style's distant ones
                Assert.Greater(es2.GetComponent<MeshFilter>().sharedMesh.vertexCount, plain.GetComponent<MeshFilter>().sharedMesh.vertexCount,
                    "ES2 style: the sky's bright regions as distant clouds too (Combined's painted sky has them)");
                Assert.Greater(es2.GetComponent<MeshFilter>().sharedMesh.vertices.Max(v => v.magnitude), NebulaClouds.RegionDistance * 0.8f);
            }
            finally
            {
                Settings.SkyStyle = style; Settings.LiveSkies = live;
                if (plain != null) Object.DestroyImmediate(plain.gameObject);
                if (es2 != null) Object.DestroyImmediate(es2.gameObject);
            }
        }

        [Test]
        public void TheCloudShaderCompiles()
        {
            var m = new Material(Resources.Load<Shader>("GoF2SkyLayers/NebulaCloud"));
            try
            {
                UnityEditor.ShaderUtil.CompilePass(m, 0, true);
                var errors = UnityEditor.ShaderUtil.GetShaderMessages(m.shader).Where(x => x.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error);
                CollectionAssert.IsEmpty(errors.Select(x => $"{x.file}:{x.line} {x.message}"));
            }
            finally { Object.DestroyImmediate(m); }
        }
    }
}
