// CoopLevelMirrorTests.cs
// Feature: docs/notes/multiplayer-coop.md "Story orbits": what a follower shows of the leader's story level besides its
// ships: the wormhole (Wormhole.Mirror, from CoopLevelState) and the fires a level lights on its ships (NpcShip.StoryBurns,
// carried in NetProxy's role flags). A Discord report 2026-11-17: the second pilot saw no wormhole at step 40, no burning
// freighter and no blast at 42.

using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.Multiplayer;
using GoF2Remake.World;
using NUnit.Framework;
using UnityEngine;

namespace GoF2Remake.Tests
{
    public class CoopLevelMirrorTests
    {
        [Test]
        public void TheFollowersWormholeIsTheLeaders()
        {
            var hole = Wormhole.Spawn(null, new Vector3(1000f, 0f, 50000f), false);
            try
            {
                var at = new Vector3(25000f, 20000f, -55000f);   // 42's, at the mother ship
                hole.Mirror(true, at, -2500);   // opening: growing
                Assert.IsTrue(hole.Mirrored);
                Assert.IsTrue(hole.Visible);
                Assert.AreEqual(at.x, hole.GamePosition.x, 1f);
                Assert.AreEqual(at.y, hole.GamePosition.y, 1f);
                Assert.AreEqual(at.z, hole.GamePosition.z, 1f);
                Assert.AreEqual(-2500, hole.Timer);
                hole.Mirror(true, at, -2300);   // a few frames on: its own timer kept (no jolt in the growth)
                Assert.AreEqual(-2500, hole.Timer);
                hole.Mirror(true, at, 59000);   // reset to close (reset(true)): taken at once
                Assert.AreEqual(59000, hole.Timer);
                hole.Mirror(false, at, 0);
                Assert.IsFalse(hole.Visible, "hidden as the leader's (42 hides the arrival's)");
                hole.StopMirroring();
                Assert.IsFalse(hole.Mirrored, "the leader gone: its own rules again");
            }
            finally { Object.DestroyImmediate(hole.gameObject); }
        }

        [Test]
        public void AStoryShipsFiresGoWithItsCopy()
        {
            var go = new GameObject("story ship");
            try
            {
                var ship = go.AddComponent<NpcShip>();
                var fire = ship.StoryBurn(WreckBurn.VosskCargo);
                ship.StoryBurn(WreckBurn.VosskFreighterSmoke);
                Assert.AreSame(fire, ship.StoryBurn(WreckBurn.VosskCargo), "lit again: the same fire");
                Assert.AreEqual(3, ship.StoryBurns);
                ship.StopStoryBurn(WreckBurn.VosskCargo);
                Assert.AreEqual(2, ship.StoryBurns);
                // In the role flags above StoryRole, clear of it and of the sign bit.
                Assert.AreEqual(NpcShip.BurnRecords.Length, 5);
                Assert.AreEqual(0, (31 << NetProxy.StoryBurnShift) & NetProxy.StoryRole);
                Assert.Greater(31 << NetProxy.StoryBurnShift, 0);
                int flags = NetProxy.StoryRole | (ship.StoryBurns & 31) << NetProxy.StoryBurnShift;
                Assert.AreEqual(2, (flags >> NetProxy.StoryBurnShift) & 31);
            }
            finally { Object.DestroyImmediate(go); }
        }

        /// <summary>A Discord report 2026-11-17: only the pilot who entered the orbit first could dock at its wrecks. A
        /// blueprint wreck is open to each player until they have its blueprint, whatever its owner did.</summary>
        [Test]
        public void ABlueprintWreckIsOpenToEachPlayerUntilTheirsIsFound()
        {
            int found = Session.HiddenBlueprintsFound;
            var known = new System.Collections.Generic.List<int>(Session.UnlockedBlueprints);
            try
            {
                Session.HiddenBlueprintsFound = 0;
                var hidden = new SpawnSpec { hiddenBlueprint = 1, dockingType = 0 };   // the owner had found it: closed there
                Assert.IsTrue(TrafficPlan.BlueprintLeft(hidden));
                Session.HiddenBlueprintsFound = 1 << 1;
                Assert.IsFalse(TrafficPlan.BlueprintLeft(hidden), "found here: no stand-in");
                int product = TrafficPlan.HiddenBlueprints[0].blueprint;
                Session.UnlockedBlueprints.Remove(product);
                var mod = new SpawnSpec { modBlueprint = product };
                Assert.IsTrue(TrafficPlan.BlueprintLeft(mod));
                Session.UnlockedBlueprints.Add(product);
                Assert.IsFalse(TrafficPlan.BlueprintLeft(mod));
                Assert.IsFalse(TrafficPlan.BlueprintLeft(new SpawnSpec { derelict = true }), "a derelict: the shared rule (OpenDerelict)");
            }
            finally
            {
                Session.HiddenBlueprintsFound = found;
                Session.UnlockedBlueprints.Clear();
                foreach (int b in known) Session.UnlockedBlueprints.Add(b);
            }
        }
    }
}
