// StationCollisionTests.cs
// Station safety volumes must account for the player's clearance sphere, not only its centre. This closes the gaps
// between coarse station boxes for ships of every size while preserving the detailed mesh hull as the primary surface.

using System.Collections.Generic;
using GoF2Remake.Flight;
using NUnit.Framework;
using UnityEngine;

namespace GoF2Remake.Tests
{
    public class StationCollisionTests
    {
        GameObject root;
        Obstacle station;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("station");
            station = root.AddComponent<Obstacle>();
            station.isStation = station.landmark = true;
            station.volumes = new List<CollisionVolume> { CollisionVolume.Box(Vector3.zero, new Vector3(10f, 10f, 10f)) };
        }

        [TearDown]
        public void TearDown()
        {
            if (root != null) Object.DestroyImmediate(root);
        }

        [TestCase(3f, 12.5f)]   // small fighter: its centre is outside, but its 3 m clearance sphere overlaps
        [TestCase(20f, 25f)]    // large ship: same rule at a larger clearance
        public void StationVolumeDetectsTheShipsClearanceSphere(float radius, float centreZ)
        {
            Assert.IsTrue(station.TouchesSphere(new Vector3(0f, 0f, centreZ), radius, out int index));
            Assert.AreEqual(0, index);
        }

        [Test]
        public void StationVolumeDoesNotTouchWhenTheClearanceSphereIsClear()
        {
            Assert.IsFalse(station.TouchesSphere(new Vector3(0f, 0f, 13.1f), 3f, out _));
        }

        [TestCase(3f, 9f, 13f)]
        [TestCase(20f, 5f, 30f)]
        public void StationPushesTheWholeShipClearOfTheVolume(float radius, float centreZ, float expectedZ)
        {
            var pushed = station.PushOutSphere(new Vector3(0f, 0f, centreZ), radius);
            Assert.AreEqual(expectedZ, pushed.z, 0.001f);
            Assert.IsFalse(station.TouchesSphere(pushed, radius, out _));
        }

        [Test]
        public void VarHastraCollisionVolumesCatchASmallShipInsideTheStationStructure()
        {
            // Var Hastra is station 78. Its legacy safety volumes must remain active alongside the detailed mesh hull;
            // otherwise the mesh's open spaces can let Betty pass through the station's central structure.
            station.volumes = CollisionVolume.ForStation(78, false);
            Assert.AreEqual(7, station.volumes.Count);
            Assert.IsTrue(station.TouchesSphere(Vector3.zero, 3f, out _),
                          "a small ship at the station's central structure must be treated as intersecting it");
        }

        [Test]
        public void StationSphereVolumesExpandByTheShipRadius()
        {
            station.volumes = new List<CollisionVolume> { CollisionVolume.Sphere(Vector3.zero, 10f) };
            Assert.IsTrue(station.TouchesSphere(new Vector3(0f, 0f, 12f), 3f, out _));
            var pushed = station.PushOutSphere(new Vector3(0f, 0f, 12f), 3f);
            Assert.AreEqual(13f, pushed.z, 0.001f);
            Assert.IsFalse(station.TouchesSphere(pushed, 3f, out _));
        }
    }
}
