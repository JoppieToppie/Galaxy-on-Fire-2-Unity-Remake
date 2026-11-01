// CollisionVolumeTests.cs
// The original's bounding volumes (CLAUDE.md "NPCs and combat", Collisions; Reference/research/ship_combat.md 2.9).
// Expected values: the research's rules applied by this test to the raw GoF2Data JSON (read from disk with its own
// parser): station records "1 a b c d e f" = box, centre (-a, c, b), half (|d|, |f|, |e|) (x0.9 in the alien orbit),
// "0 a b c d" = sphere r |d| x 0.5 (x0.4 alien); static objects centre (a, c, -b), half x1.2, r x0.6; wrecks centre
// (-a, c, b), half x1.1, r x0.6, id 0 (the battleship) everything x2; game -> Unity (x, y, -z) x 0.05. Box push = the
// nearest of the 6 faces, sphere push = onto the surface, two passes over the list (worked by hand). The freighter box
// counts are the labels of Reference/tools/npc/freighter_boxes.py (battleship 11, Vossk 5, Nivelian 3, Midorian 2,
// Terran 3).

using System;
using System.Collections.Generic;
using System.IO;
using GoF2Remake.Flight;
using NUnit.Framework;
using UnityEngine;

namespace GoF2Remake.Tests
{
    public class CollisionVolumeTests
    {
        // ---- the geometry -------------------------------------------------------------------------------------------

        [Test]
        public void BoxContainsStrictly()
        {
            var b = CollisionVolume.Box(new Vector3(10f, 0f, 0f), new Vector3(5f, 5f, 5f));
            Assert.IsTrue(b.Contains(new Vector3(12f, 1f, -1f)));
            Assert.IsTrue(b.Contains(new Vector3(5.5f, 4.9f, 4.9f)));
            Assert.IsFalse(b.Contains(new Vector3(15f, 0f, 0f)), "on the face is outside");
            Assert.IsFalse(b.Contains(new Vector3(0f, 0f, 0f)));
            Assert.IsFalse(b.Contains(new Vector3(10f, 0f, 6f)));
        }

        [Test]
        public void SphereContainsStrictly()
        {
            var s = CollisionVolume.Sphere(new Vector3(0f, 10f, 0f), 5f);
            Assert.IsTrue(s.Contains(new Vector3(0f, 13f, 3f)));
            Assert.IsFalse(s.Contains(new Vector3(0f, 15f, 0f)));
            Assert.IsFalse(s.Contains(new Vector3(4f, 13f, 3f)));
        }

        [Test]
        public void BoxProjectsOntoTheNearestFace()
        {
            var b = CollisionVolume.Box(new Vector3(10f, 0f, 0f), new Vector3(5f, 5f, 5f));
            AssertNear(new Vector3(15f, 0f, 0f), b.Project(new Vector3(14f, 0f, 0f)));    // x+ 1 away
            AssertNear(new Vector3(5f, 1f, 0f), b.Project(new Vector3(6f, 1f, 0f)));      // x- 1 away
            AssertNear(new Vector3(11f, 5f, 0f), b.Project(new Vector3(11f, 4f, 0f)));    // y+ 1 away, x 4
            AssertNear(new Vector3(10f, 0f, -5f), b.Project(new Vector3(10f, 0f, -4.5f)));
        }

        [Test]
        public void SphereProjectsOntoItsSurface()
        {
            var s = CollisionVolume.Sphere(Vector3.zero, 10f);
            AssertNear(new Vector3(6f, 8f, 0f), s.Project(new Vector3(3f, 4f, 0f)));
            AssertNear(new Vector3(30f, 0f, 0f), s.Project(new Vector3(30f, 0f, 0f)), "outside: unchanged");
            AssertNear(Vector3.zero, s.Project(Vector3.zero), "the centre has no direction");
            var off = CollisionVolume.Sphere(new Vector3(100f, 0f, 0f), 10f);
            AssertNear(new Vector3(100f, 0f, 10f), off.Project(new Vector3(100f, 0f, 2f)));
        }

        [Test]
        public void ProjectedPointsAreNotInside()
        {
            var b = CollisionVolume.Box(new Vector3(-3f, 2f, 7f), new Vector3(4f, 6f, 2f));
            var s = CollisionVolume.Sphere(new Vector3(1f, -2f, 3f), 8f);
            var rng = new System.Random(7);
            for (int i = 0; i < 200; i++)
            {
                var p = new Vector3((float)rng.NextDouble() * 20f - 10f, (float)rng.NextDouble() * 20f - 10f, (float)rng.NextDouble() * 20f - 5f);
                if (b.Contains(p))
                {
                    // on the box's surface: one axis at its half extent, the others within
                    var d = b.Project(p) - b.centre;
                    float worst = Mathf.Max(Mathf.Abs(d.x) / b.half.x, Mathf.Abs(d.y) / b.half.y, Mathf.Abs(d.z) / b.half.z);
                    Assert.AreEqual(1f, worst, 1e-4f);
                }
                if (s.Contains(p) && (p - s.centre).sqrMagnitude > 1e-4f)
                    Assert.AreEqual(8f, (s.Project(p) - s.centre).magnitude, 1e-3f);
            }
        }

        [Test]
        public void ProjectOutMakesTwoPasses()
        {
            // A spans x -10..10, B x 7..17. From (9, 0, 0): A -> 10, B -> 7, A -> 10, B -> 7: two passes end inside A.
            var list = new List<CollisionVolume>
            {
                CollisionVolume.Box(Vector3.zero, new Vector3(10f, 10f, 10f)),
                CollisionVolume.Box(new Vector3(12f, 0f, 0f), new Vector3(5f, 10f, 10f)),
            };
            AssertNear(new Vector3(7f, 0f, 0f), CollisionVolume.ProjectOut(new Vector3(9f, 0f, 0f), list));
        }

        [Test]
        public void ProjectOutLeavesOutsidePointsAlone()
        {
            var list = new List<CollisionVolume> { CollisionVolume.Sphere(Vector3.zero, 5f), CollisionVolume.Box(new Vector3(20f, 0f, 0f), Vector3.one) };
            var p = new Vector3(10f, 3f, -2f);
            Assert.AreEqual(p, CollisionVolume.ProjectOut(p, list));
        }

        [Test]
        public void ProjectOutOfTwoSeparateVolumes()
        {
            var list = new List<CollisionVolume> { CollisionVolume.Sphere(Vector3.zero, 5f), CollisionVolume.Box(new Vector3(20f, 0f, 0f), Vector3.one * 2f) };
            AssertNear(new Vector3(0f, 5f, 0f), CollisionVolume.ProjectOut(new Vector3(0f, 1f, 0f), list));
            AssertNear(new Vector3(22f, 0f, 0f), CollisionVolume.ProjectOut(new Vector3(21.5f, 0f, 0f), list));
        }

        // ---- the data -------------------------------------------------------------------------------------------------

        [Serializable] class RawEntry { public int id; public int[] values; }
        [Serializable] class RawList { public RawEntry[] items; }

        static Dictionary<int, int[]> Raw(string name)
        {
            string path = Path.Combine(Application.dataPath, "Resources", "GoF2Data", name + ".json");
            var list = JsonUtility.FromJson<RawList>("{\"items\":" + File.ReadAllText(path) + "}");
            var map = new Dictionary<int, int[]>();
            foreach (var e in list.items) map[e.id] = e.values;
            return map;
        }

        /// <summary>The research's rule, written out here: station / wreck records (centre (-a, c, b)) or static ones
        /// (centre (a, c, -b)), then game -> Unity.</summary>
        static List<CollisionVolume> Expected(int[] v, bool staticRule, float boxK, float sphereK, float doubled)
        {
            var list = new List<CollisionVolume>();
            int i = 1;
            for (int n = 0; n < v[0]; n++)
            {
                bool box = v[i] == 1;
                float a = v[i + 1], b = v[i + 2], c = v[i + 3], d = v[i + 4];
                var game = staticRule ? new Vector3(a, c, -b) : new Vector3(-a, c, b);
                var centre = new Vector3(game.x, game.y, -game.z) * 0.05f * doubled;
                if (box)
                {
                    float e = v[i + 5], f = v[i + 6];
                    list.Add(CollisionVolume.Box(centre, new Vector3(Mathf.Abs(d), Mathf.Abs(f), Mathf.Abs(e)) * 0.05f * boxK * doubled));
                    i += 7;
                }
                else
                {
                    Assert.AreEqual(0, v[i], "unknown volume type in the data");
                    list.Add(CollisionVolume.Sphere(centre, Mathf.Abs(d) * 0.05f * sphereK * doubled));
                    i += 5;
                }
            }
            return list;
        }

        static void AssertSame(List<CollisionVolume> expected, List<CollisionVolume> actual, int count = -1)
        {
            if (count < 0) Assert.AreEqual(expected.Count, actual.Count);
            for (int i = 0; i < expected.Count; i++)
            {
                Assert.AreEqual(expected[i].sphere, actual[i].sphere, $"volume {i}");
                AssertNear(expected[i].centre, actual[i].centre, $"volume {i} centre", 0.01f);
                if (expected[i].sphere) Assert.AreEqual(expected[i].radius, actual[i].radius, 0.01f, $"volume {i} radius");
                else AssertNear(expected[i].half, actual[i].half, $"volume {i} half", 0.01f);
            }
        }

        [Test]
        public void StationVolumesFollowTheResearchRule()
        {
            var raw = Raw("collision");
            int checkedStations = 0;
            for (int s = 0; s < 109; s++)
            {
                if (!raw.TryGetValue(s, out var v)) continue;
                AssertSame(Expected(v, false, 1f, 0.5f, 1f), CollisionVolume.ForStation(s, false));
                checkedStations++;
            }
            Assert.Greater(checkedStations, 0);
        }

        [Test]
        public void StationWithoutEntryIsVossk1000()
        {
            var raw = Raw("collision");
            int s = -1;
            for (int i = 0; i < 109 && s < 0; i++) if (!raw.ContainsKey(i)) s = i;
            Assume.That(s, Is.GreaterThanOrEqualTo(0), "every station has its own entry");
            AssertSame(Expected(raw[1000], false, 1f, 0.5f, 1f), CollisionVolume.ForStation(s, false));
        }

        [Test]
        public void BattlestationInTheAlienOrbit()
        {
            // after the Valkyrie add-on: 1003, boxes x0.9, spheres x0.4, no Void station extras
            var raw = Raw("collision");
            AssertSame(Expected(raw[1003], false, 0.9f, 0.4f, 1f), CollisionVolume.ForStation(0, true, battlestation: true));
        }

        [Test]
        public void VoidStationGetsItsExtras()
        {
            var raw = Raw("collision");
            var expected = Expected(raw[1001], false, 0.9f, 0.4f, 1f);
            var actual = CollisionVolume.ForStation(0, true);
            Assert.AreEqual(expected.Count + CollisionVolume.VoidStationExtras().Count, actual.Count);
            AssertSame(expected, actual, expected.Count);
            Assert.Greater(CollisionVolume.VoidStationExtras().Count, 0, "#37: the blades and spire have boxes");
        }

        [Test]
        public void WreckedSupernovaStationsUseStatic2002()
        {
            var raw = Raw("static_collisions");
            var expected = Expected(raw[2002], false, 1f, 0.5f, 1f);
            AssertSame(expected, CollisionVolume.ForStation(109, false));
            AssertSame(expected, CollisionVolume.ForStation(110, false));
        }

        [Test]
        public void PirateOutpostIsAStaticObject()
        {
            var raw = Raw("collision");
            AssertSame(Expected(raw[1002], true, 1.2f, 0.6f, 1f), CollisionVolume.ForStaticObject(1002));
        }

        [Test]
        public void StaticObjectsFromStaticCollisions()
        {
            var raw = Raw("static_collisions");
            foreach (int id in new[] { 2000, 2003, 2005, 2006 })
                if (raw.TryGetValue(id, out var v)) AssertSame(Expected(v, true, 1.2f, 0.6f, 1f), CollisionVolume.ForStaticObject(id));
        }

        [Test]
        public void MissingStaticObjectHasNoVolumes()
        {
            Assert.AreEqual(0, CollisionVolume.ForStaticObject(2999).Count);
        }

        [Test]
        public void BattleshipWreckIsDoubled()
        {
            var raw = Raw("wreck_collisions");
            AssertSame(Expected(raw[0], false, 1.1f, 0.6f, 2f), CollisionVolume.ForWreckId(0));
        }

        [Test]
        public void FreighterWrecks()
        {
            var raw = Raw("wreck_collisions");
            for (int id = 1; id <= 4; id++)
                AssertSame(Expected(raw[id], false, 1.1f, 0.6f, 1f), CollisionVolume.ForWreckId(id));
        }

        [Test]
        public void WreckIdByShipAndRace()
        {
            // battleship 0, Midorian 1, Nivelian 2, Terran 3, Vossk freighter 4
            AssertSame(CollisionVolume.ForWreckId(0), CollisionVolume.ForWreck(14, 0));
            AssertSame(CollisionVolume.ForWreckId(4), CollisionVolume.ForWreck(13, 1));
            AssertSame(CollisionVolume.ForWreckId(1), CollisionVolume.ForWreck(15, 3));
            AssertSame(CollisionVolume.ForWreckId(2), CollisionVolume.ForWreck(15, 2));
            AssertSame(CollisionVolume.ForWreckId(3), CollisionVolume.ForWreck(15, 0));
        }

        [TestCase(14, 0, 11)]   // Terran battleship
        [TestCase(13, 1, 5)]    // Vossk freighter
        [TestCase(15, 1, 5)]
        [TestCase(15, 2, 3)]    // Nivelian
        [TestCase(15, 3, 2)]    // Midorian
        [TestCase(15, 0, 3)]    // Terran
        [TestCase(15, 8, 3)]    // pirates fly the Terran one
        public void FreighterBoxCounts(int ship, int race, int count)
        {
            var boxes = CollisionVolume.ForFreighter(ship, race);
            Assert.AreEqual(count, boxes.Count);
            foreach (var b in boxes)
            {
                Assert.IsFalse(b.sphere);
                Assert.Greater(b.half.x, 0f); Assert.Greater(b.half.y, 0f); Assert.Greater(b.half.z, 0f);
                Assert.AreEqual(0f, b.centre.x, "the boxes sit on the ship's centre line");
            }
        }

        static void AssertNear(Vector3 expected, Vector3 actual, string message = null, float tolerance = 1e-4f)
        {
            Assert.AreEqual(expected.x, actual.x, tolerance, message);
            Assert.AreEqual(expected.y, actual.y, tolerance, message);
            Assert.AreEqual(expected.z, actual.z, tolerance, message);
        }

        [Test]
        public void RayEntryIntoBoxesAndSpheres()
        {
            var box = CollisionVolume.Box(new Vector3(0f, 0f, 100f), new Vector3(10f, 5f, 20f));
            Assert.AreEqual(80f, box.RayEntry(Vector3.zero, Vector3.forward), 0.001f, "the near face of the box");
            Assert.AreEqual(float.MaxValue, box.RayEntry(new Vector3(0f, 8f, 0f), Vector3.forward), "above its half height");
            Assert.AreEqual(0f, box.RayEntry(new Vector3(0f, 0f, 100f), Vector3.forward), "from inside");
            Assert.AreEqual(float.MaxValue, box.RayEntry(Vector3.zero, Vector3.back), "behind");
            var ball = CollisionVolume.Sphere(new Vector3(0f, 0f, 100f), 30f);
            Assert.AreEqual(70f, ball.RayEntry(Vector3.zero, Vector3.forward), 0.001f, "the near side of the sphere");
            Assert.AreEqual(100f - Mathf.Sqrt(30f * 30f - 20f * 20f), ball.RayEntry(new Vector3(20f, 0f, 0f), Vector3.forward), 0.001f, "a chord");
            Assert.AreEqual(float.MaxValue, ball.RayEntry(new Vector3(31f, 0f, 0f), Vector3.forward), "past its side");
            Assert.AreEqual(0f, ball.RayEntry(new Vector3(0f, 0f, 90f), Vector3.forward), "from inside");
            Assert.AreEqual(float.MaxValue, ball.RayEntry(Vector3.zero, Vector3.back), "behind");
        }
    }
}
