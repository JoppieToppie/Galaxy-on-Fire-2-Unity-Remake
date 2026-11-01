// EngineGlowShapeTests.cs
// Feature: CLAUDE.md "Mods" > a mod ship's engine glow discs: a mount's "glowShape" gives the flame another outline in its
// glowSize box (EngineGlowShape: round, rounded, square, hexagon, diamond, triangle), drawn by ModShipBuilder.BuildEngineGlow
// (a square's corners reach the box's corners), kept by the Mod Builder (ProjectShip.MountJson writes it unless round) and
// read from ships.json (JsonUtility); "round" builds the same twelve-segment ellipse as before.
// Expected values: the format in Modding/README.md (ships.json "mounts").

using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Modding;
using NUnit.Framework;
using UnityEngine;

namespace GoF2Remake.Tests
{
    public class EngineGlowShapeTests
    {
        [Test]
        public void UnknownOrMissingShapesAreRound()
        {
            Assert.AreEqual("round", EngineGlowShape.Normalize(null));
            Assert.AreEqual("round", EngineGlowShape.Normalize("blob"));
            Assert.AreEqual("square", EngineGlowShape.Normalize(" Rectangle "));
            Assert.AreEqual("hexagon", EngineGlowShape.Normalize("HEXAGON"));
        }

        [Test]
        public void RoundIsTheOldTwelveSegmentEllipse()
        {
            var o = EngineGlowShape.Outline(null);
            Assert.AreEqual(12, o.Length);
            for (int i = 0; i < 12; i++)
            {
                float a = i * Mathf.PI * 2f / 12;
                Assert.AreEqual(Mathf.Sin(a), o[i].x, 1e-5f);
                Assert.AreEqual(Mathf.Cos(a), o[i].y, 1e-5f);
            }
            // The exhaust styles' profile: the ellipse's own radius.
            Assert.AreEqual(30f, EngineGlowShape.Radius("round", 30f, 10f, 0f), 1e-3f);
            Assert.AreEqual(10f, EngineGlowShape.Radius("round", 30f, 10f, Mathf.PI / 2f), 1e-3f);
        }

        [Test]
        public void EveryOutlineFitsTheBoxAndRunsClockwise()
        {
            foreach (var name in EngineGlowShape.Names)
            {
                var o = EngineGlowShape.Outline(name);
                Assert.IsTrue(o.All(p => Mathf.Abs(p.x) <= 1.0001f && Mathf.Abs(p.y) <= 1.0001f), name);
                float area = 0f;
                for (int i = 0; i < o.Length; i++) area += o[i].x * o[(i + 1) % o.Length].y - o[(i + 1) % o.Length].x * o[i].y;
                Assert.Less(area, 0f, name + " (the round glow's winding, so the disc's triangles face the same way)");
            }
        }

        [Test]
        public void ARectangleReachesItsCornersAndSides()
        {
            Assert.AreEqual(40f, EngineGlowShape.Radius("square", 40f, 10f, 0f), 1e-3f);
            Assert.AreEqual(10f, EngineGlowShape.Radius("square", 40f, 10f, Mathf.PI / 2f), 1e-3f);
            float corner = Mathf.Atan2(10f, 40f);
            Assert.AreEqual(Mathf.Sqrt(40f * 40f + 10f * 10f), EngineGlowShape.Radius("square", 40f, 10f, corner), 1e-2f);
            // A diamond's sides are halfway in.
            Assert.AreEqual(Mathf.Sqrt(0.5f) * 20f, EngineGlowShape.Radius("diamond", 20f, 20f, Mathf.PI / 4f), 1e-3f);
        }

        [Test]
        public void TheGlowMeshTakesTheShape()
        {
            Assume.That(ModAssets.Get()?.engineGlow, Is.Not.Null, "Mod Assets not built");
            var mount = new WeaponMount { slotType = 3, position_engine = new int[3], glowSize = new[] { 40f, 10f, 30f }, glowShape = "square" };
            var ship = new CustomShipData { assembly = "test", mounts = new System.Collections.Generic.List<WeaponMount> { mount } };
            var go = ModShipBuilder.BuildEngineGlow(ship);
            try
            {
                var v = go.GetComponent<MeshFilter>().sharedMesh.vertices;
                Assert.AreEqual(1 + 12 + 12, v.Length);
                const float M = 0.05f;
                Assert.IsTrue(v.Any(p => Mathf.Abs(Mathf.Abs(p.x) - 40f * M) < 1e-4f && Mathf.Abs(Mathf.Abs(p.y) - 10f * M) < 1e-4f && Mathf.Abs(p.z) < 1e-4f),
                    "a disc vertex in the box's corner");
                Assert.IsTrue(v.All(p => p.z >= -30f * M - 1e-4f), "the flared ring at the flame's length");
            }
            finally
            {
                Object.DestroyImmediate(go.GetComponent<MeshFilter>().sharedMesh);
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void TheBuilderKeepsTheShapeAndShipsJsonReadsIt()
        {
            var m = new WeaponMount { slotType = 3, position_engine = new[] { 0, 0, -100 }, glowSize = new[] { 40f, 10f, 30f }, glowShape = "hexagon" };
            Assert.AreEqual("hexagon", (string)ProjectShip.MountJson(m)["glowShape"]);
            Assert.AreEqual("hexagon", ProjectShip.Copy(m).glowShape);
            m.glowShape = "round";
            Assert.IsNull(ProjectShip.MountJson(m)["glowShape"], "round is the default, not written");
            var read = JsonUtility.FromJson<WeaponMount>("{ \"slotType\": 3, \"glowSize\": [40, 10, 30], \"glowShape\": \"triangle\" }");
            Assert.AreEqual("triangle", read.glowShape);
        }
    }
}
