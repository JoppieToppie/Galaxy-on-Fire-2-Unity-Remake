// TargetLockRulesTests.cs
// The remake's ship lock (CLAUDE.md "NPCs and combat", Radar; TargetLockRules, CombatRadar): the Smart lock's priority
// (hostile first, then the nearest to the crosshair), its hold on an enemy (Settings.KeepEnemyLock, its own option after a
// player's suggestion), the ship let go by Unlock target, which stays out until it leaves the crosshair box, and the
// cargo steal from another game's EMP-disabled ship in a session, and the Smart lock's asteroid pick.

using System.Linq;
using GoF2Remake.Flight;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    public class TargetLockRulesTests
    {
        struct Ship { public string name; public float dx, dy; public bool hostile; }

        /// <summary>CombatRadar's pick with the Smart lock: the eligible ship with the lowest score, or null.</summary>
        static string Pick(Ship[] ships, bool keepEnemyLock, bool lockedHostileAlive, string released = null)
        {
            bool holds = TargetLockRules.HoldsEnemy(true, keepEnemyLock, lockedHostileAlive);
            return ships.Where(s => TargetLockRules.Eligible(holds, s.hostile, s.name == released))
                        .OrderBy(s => TargetLockRules.Score(s.dx, s.dy, s.hostile))
                        .Select(s => s.name).FirstOrDefault();
        }

        static readonly Ship Enemy = new Ship { name = "enemy", dx = 60, dy = 40, hostile = true };
        static readonly Ship Trader = new Ship { name = "trader", dx = 2, dy = 1, hostile = false };
        static readonly Ship Raider = new Ship { name = "raider", dx = 90, dy = 0, hostile = true };

        [Test]
        public void HostileShipsComeFirstThenTheNearest()
        {
            Assert.AreEqual("enemy", Pick(new[] { Trader, Raider, Enemy }, true, false), "hostile first, the nearer of the two");
            Assert.AreEqual("trader", Pick(new[] { Trader }, true, false), "no hostile ship in the box: the neutral one");
            Assert.Less(TargetLockRules.Score(1000, 1000, true), TargetLockRules.Score(0, 0, false), "any hostile beats any neutral");
        }

        [Test]
        public void TheHoldKeepsNeutralShipsOffWhileAnEnemyIsLocked()
        {
            Assert.IsNull(Pick(new[] { Trader }, true, true), "an enemy locked: the trader can't take it");
            Assert.AreEqual("raider", Pick(new[] { Trader, Raider }, true, true), "another hostile ship still can");
            Assert.AreEqual("trader", Pick(new[] { Trader }, true, false), "the locked enemy dead or neutral: no hold");
        }

        [Test]
        public void WithoutTheHoldANeutralShipCanBeLockedDuringABattle()
        {
            Assert.AreEqual("trader", Pick(new[] { Trader }, false, true), "an enemy locked, the hold off: the trader locks");
            Assert.AreEqual("enemy", Pick(new[] { Trader, Enemy }, false, true), "the priority stays: a hostile ship in the box first");
        }

        [Test]
        public void TheHoldIsOnlyPartOfTheSmartLock()
        {
            Assert.IsFalse(TargetLockRules.HoldsEnemy(false, true, true), "the Original lock never holds");
            Assert.IsTrue(TargetLockRules.HoldsEnemy(true, true, true));
            Assert.IsFalse(TargetLockRules.HoldsEnemy(true, false, true));
        }

        [Test]
        public void AnUnlockedShipStaysOutUntilItLeavesTheBox()
        {
            // Unlocked: the enemy still in the box isn't picked straight back, the trader beside it is (no lock, no hold).
            Assert.AreEqual("trader", Pick(new[] { Trader, Enemy }, true, false, released: "enemy"));
            Assert.AreEqual("raider", Pick(new[] { Trader, Enemy, Raider }, true, false, released: "enemy"), "another enemy still first");
            Assert.IsFalse(TargetLockRules.Eligible(false, true, true), "released: never a candidate");
            Assert.IsTrue(TargetLockRules.StillReleased(true, true), "alive and in the box: still released");
            Assert.IsFalse(TargetLockRules.StillReleased(true, false), "out of the box: lockable again");
            Assert.IsFalse(TargetLockRules.StillReleased(false, true), "dead: forgotten");
        }

        // Discord report 2026-10-24: "Smart targeting does not do well at all trying to scan asteroids" - the original's
        // nearest asteroid in the box locked a rock beside the crosshair instead of the one under it.
        [Test]
        public void TheSmartLockTakesTheAsteroidUnderTheCrosshair()
        {
            var eye = UnityEngine.Vector3.zero;
            var aim = UnityEngine.Vector3.forward;
            float Score(float x, float z, float surface, bool candidate = false) =>
                TargetLockRules.AsteroidScore(eye, aim, new UnityEngine.Vector3(x, 0f, z), surface, candidate);

            float farUnder = Score(0f, 2000f, 50f), nearBeside = Score(60f, 300f, 20f);
            Assert.IsTrue(TargetLockRules.IsUnderCrosshair(farUnder), "the crosshair's line meets the far rock");
            Assert.IsFalse(TargetLockRules.IsUnderCrosshair(nearBeside), "it misses the near one beside it");
            Assert.Less(farUnder, nearBeside, "the rock under the crosshair beats a nearer one beside it");
            Assert.IsTrue(TargetLockRules.IsUnderCrosshair(Score(120f, 1000f, 150f)), "a big rock over the crosshair, its centre off it");
            Assert.Less(Score(0f, 500f, 50f), Score(0f, 2000f, 50f), "two under it: the front one, the one seen");
            Assert.Less(Score(10f, 500f, 50f), Score(-5f, 3000f, 50f), "a rock in front partly covering one behind wins");
            Assert.AreEqual(TargetLockRules.UnderCrosshair, Score(0f, 10f, 50f), "inside the rock");

            Assert.Less(Score(40f, 1000f, 20f), Score(80f, 1000f, 20f), "none under it: the edge nearest the crosshair");
            Assert.Less(Score(120f, 1000f, 90f), Score(60f, 1000f, 5f), "a big rock's edge nearer than a small rock's centre");
            Assert.Less(Score(60f, 1000f, 20f, candidate: true), Score(58f, 1000f, 20f), "the rock already locking keeps a small lead");
            Assert.Greater(Score(200f, 1000f, 20f, candidate: true), Score(60f, 1000f, 20f), "but not against one clearly nearer");
        }

        // Discord report 2026-10-21: "the tractor beam can't take loot from EMP-disabled ships". The radar only looked for a
        // steal among its own traffic, so in a session the players whose game didn't run the orbit's NPCs saw an EMP'd NPC
        // (another game's NetProxy) as a plain ship lock and could never steal from it.
        [Test]
        public void AnotherGamesDisabledShipWithCargoCanBeStolenFrom()
        {
            Assert.IsTrue(TargetLockRules.RemoteStealable(true, true, true, 6, 12), "disabled, flying, 12 t aboard: a steal");
            Assert.IsFalse(TargetLockRules.RemoteStealable(false, true, true, 6, 12), "not disabled: a ship lock");
            Assert.IsFalse(TargetLockRules.RemoteStealable(true, false, true, 6, 12), "dying or dead: its own crate, no steal");
            Assert.IsFalse(TargetLockRules.RemoteStealable(true, true, true, -1, 0), "no cargo: a ship lock");
            Assert.IsFalse(TargetLockRules.RemoteStealable(true, true, false, 6, 12), "a mission ship's cargo: the team's only");

            var go = new UnityEngine.GameObject("proxy");
            try
            {
                var t = go.AddComponent<Target>();
                t.netStealItem = 6;
                t.netStealAmount = 12;
                Assert.IsFalse(t.NetStealable, "only a copy of another game's ship (RemoteSteal) is stolen from this way");
                int stolen = 0;
                t.RemoteSteal = (item, amount) => stolen += amount;
                Assert.IsTrue(t.NetStealable, "the radar takes it as a steal candidate");
                t.RemoteSteal(6, 4);
                Assert.AreEqual(4, stolen, "the capture's units go to the owner's ship");
                t.netStealAmount = 0;
                Assert.IsFalse(t.NetStealable, "emptied: a ship lock again");

                var crate = go.AddComponent<Crate>();
                Assert.IsFalse(crate.Stolen);
                crate.stolenFromRemote = t;
                Assert.IsTrue(crate.Stolen, "a remote steal's crate is the stealer's: never shared (NetOrbit) or auto-salvaged");
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }
    }
}
