// DebugKillAroundTests.cs
// Feature: CLAUDE.md "Debug panel": the pause menu's Debug page destroys what is around the player (Actions tab, "Destroy
// around you", World.DebugSpawner.KillAround): within a range of 10 m - 30 km, hostile only / all ships / everything; never
// the player, another player or what is already dead.
// Expected values: the range steps of the Debug page, the distances placed here.

using System.Collections.Generic;
using GoF2Remake.Flight;
using GoF2Remake.World;
using NUnit.Framework;
using UnityEngine;

namespace GoF2Remake.Tests
{
    public class DebugKillAroundTests
    {
        readonly List<GameObject> made = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in made) Object.DestroyImmediate(go);
            made.Clear();
        }

        Target At(float metres, bool ship = true, bool hostile = false)
        {
            var go = new GameObject("target");
            made.Add(go);
            go.transform.position = new Vector3(0f, 0f, metres);
            var t = go.AddComponent<Target>();
            t.isShip = ship;
            t.hostileToPlayer = hostile;
            return t;
        }

        [Test]
        public void TheRangesRunFromTenMetresToThirtyKilometres()
        {
            var r = DebugSpawner.KillRanges;
            Assert.AreEqual(10f, r[0]);
            Assert.AreEqual(30000f, r[r.Length - 1]);
            for (int i = 1; i < r.Length; i++) Assert.Greater(r[i], r[i - 1]);
            Assert.AreEqual("10 m", DebugSpawner.RangeText(10f));
            Assert.AreEqual("2.5 km", DebugSpawner.RangeText(2500f).Replace(',', '.'));
            Assert.AreEqual("30 km", DebugSpawner.RangeText(30000f));
        }

        [Test]
        public void OnlyWhatIsInRangeIsTaken()
        {
            Assert.IsTrue(DebugSpawner.KillsIt(At(900f), Vector3.zero, 1000f, DebugSpawner.KillFilter.Ships));
            Assert.IsFalse(DebugSpawner.KillsIt(At(1100f), Vector3.zero, 1000f, DebugSpawner.KillFilter.Ships));
            Assert.IsTrue(DebugSpawner.KillsIt(At(29000f), Vector3.zero, 30000f, DebugSpawner.KillFilter.Ships));
        }

        [Test]
        public void TheFilterPicksHostilesShipsOrEverything()
        {
            var friend = At(100f);
            var enemy = At(100f, hostile: true);
            var rock = At(100f, ship: false);
            Assert.IsFalse(DebugSpawner.KillsIt(friend, Vector3.zero, 1000f, DebugSpawner.KillFilter.Hostile));
            Assert.IsTrue(DebugSpawner.KillsIt(enemy, Vector3.zero, 1000f, DebugSpawner.KillFilter.Hostile));
            Assert.IsTrue(DebugSpawner.KillsIt(friend, Vector3.zero, 1000f, DebugSpawner.KillFilter.Ships));
            Assert.IsFalse(DebugSpawner.KillsIt(rock, Vector3.zero, 1000f, DebugSpawner.KillFilter.Ships));
            Assert.IsTrue(DebugSpawner.KillsIt(rock, Vector3.zero, 1000f, DebugSpawner.KillFilter.Everything));
        }

        [Test]
        public void ThePlayersAndTheDeadAreLeftAlone()
        {
            var self = At(5f);
            self.isPlayer = true;
            var other = At(5f, hostile: true);
            other.netPilot = true;
            var dead = At(5f);
            dead.hp = 0f;
            foreach (var t in new[] { self, other, dead })
                Assert.IsFalse(DebugSpawner.KillsIt(t, Vector3.zero, 30000f, DebugSpawner.KillFilter.Everything));
        }
    }
}
