// HullOutlineCollisionTests.cs
// The player's ship against a station's hull on its own outline (PlayerCollision.EnsureHullBoxes; docs/notes/npcs-and-combat.md,
// the probe sphere paragraph): the ship put nose first against the hull, its centre's sphere clear of it but its nose in it,
// is pushed back until no slice box is inside the hull any more.

using System.Collections;
using System.Collections.Generic;
using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace GoF2Remake.Tests
{
    [Category("Smoke")]
    public class HullOutlineCollisionTests : PlayModeTest
    {
        int collisionBefore;

        [SetUp]
        public void KeepCollisionSetting() { collisionBefore = Settings.ShipCollision; }

        [TearDown]
        public void CollisionSettingBack() { Settings.ShipCollision = collisionBefore; }

        static float Deepest(List<BoxCollider> boxes, Collider hull)
        {
            float deepest = 0f;
            foreach (var b in boxes)
                if (Physics.ComputePenetration(b, b.transform.position, b.transform.rotation, hull, hull.transform.position, hull.transform.rotation, out _, out float d))
                    deepest = Mathf.Max(deepest, d);
            return deepest;
        }

        [UnityTest]
        public IEnumerator TheShipsNoseIsPushedOutOfAStationHull()
        {
            Settings.ShipCollision = SessionRules.CollisionMixed;
            FreePlayAt(Db.Stations.First(s => s.system == 19).index);
            Session.LaunchedFromStation = true;
            yield return Load("Space");
            yield return WaitFor(() => Find<SpaceLevel>() != null && Find<SpaceLevel>().LaunchCameraOver, 60f, "the orbit");
            yield return Run(1f);

            var space = Find<SpaceLevel>();
            var collision = space.Player.GetComponent<PlayerCollision>();
            var boxes = Get<List<BoxCollider>>(collision, "hullBoxes");
            Assert.Greater(boxes.Count, 1, "the ship measured into slices");
            var station = Object.FindObjectsByType<Obstacle>().First(o => o.isStation && o.UsesHull);

            // The station's top surface, straight above its centre.
            Physics.SyncTransforms();
            var from = station.transform.position + Vector3.up * 5000f;
            Assert.IsTrue(Physics.Raycast(from, Vector3.down, out var hit, 10000f, HullCollision.HullMask), "the station's hull");

            // The ship's centre just outside the probe sphere's reach, its nose down into the hull.
            float probe = Get<SphereCollider>(collision, "probe").radius;
            var ship = space.Player.transform;
            ship.SetPositionAndRotation(hit.point + hit.normal * (probe + 0.25f), Quaternion.LookRotation(-hit.normal));
            Physics.SyncTransforms();
            Assert.Greater(Deepest(boxes, hit.collider), 0.5f, "the nose starts inside the hull");

            yield return Run(0.5f);
            Physics.SyncTransforms();
            Assert.Less(Deepest(boxes, hit.collider), 0.5f, "no slice of the ship left inside the hull");
        }
    }
}
