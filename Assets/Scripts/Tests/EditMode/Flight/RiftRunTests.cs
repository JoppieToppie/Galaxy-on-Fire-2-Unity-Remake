// RiftRunTests.cs
// Feature: docs/notes/navigation.md "Rifts", Modding/README.md encounters' "rift": Flight.RiftRun, after EVERSPACE 2's
// BP_Encounter_Rift (decompiled). Expected values: its GetTargetKillPointCount table, its kill points and resolve gains by
// EPawnType, its resolve drain, the mutator pick from the rift's and the boss's sets, the loot factor worked by hand.

using GoF2Remake.Flight;
using System.Linq;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    public class RiftRunTests
    {
        [Test]
        public void AStagesTargetIsEs2s()
        {
            Assert.AreEqual(15f, RiftRun.StageTarget(0, 0f));
            Assert.AreEqual(20f, RiftRun.StageTarget(0, 1f), "+ 0..5");
            Assert.AreEqual(45f, RiftRun.StageTarget(20, 0f), "the table's last past its end");
            var r = new RiftRun(4);
            r.BeginStage(0f, 0f);
            Assert.AreEqual(6, r.MinAlive, "trunc(15 x 0.4)");
        }

        [Test]
        public void KillsAndResolve()
        {
            var r = new RiftRun(3);
            r.BeginStage(0f, 0f);
            r.Tick(RiftRun.ResolveDelayS);
            Assert.AreEqual(1f, r.Resolve, 1e-5f, "no drain in the first 10 s");
            r.Tick(10f);
            Assert.AreEqual(1f - 0.16f, r.Resolve, 1e-4f, "1.6 % a second");
            Assert.IsFalse(r.Kill(RiftRun.Kind.Fighter));
            Assert.AreEqual(1f, r.Points);
            Assert.AreEqual(0.84f + 0.12f, r.Resolve, 1e-4f, "a fighter's resolve back");
            for (int i = 0; i < 6; i++) r.Kill(RiftRun.Kind.Capital);
            Assert.AreEqual(1f, r.Resolve, 1e-5f, "at most full");
            Assert.IsFalse(r.Kill(RiftRun.Kind.Drone), "1 + 12 + 0.5 of 15");
            Assert.IsTrue(r.Kill(RiftRun.Kind.Capital), "15.5: the stage is won");
        }

        [Test]
        public void AStageRunDryIsNotMastered()
        {
            var r = new RiftRun(3);
            r.BeginStage(0f, 0f);
            r.Tick(200f);
            Assert.AreEqual(0f, r.Resolve);
            r.EndStage();
            Assert.IsFalse(r.mastered[0]);
            r.BeginStage(0f, 0f);
            r.EndStage();
            Assert.IsTrue(r.mastered[1]);
            Assert.AreEqual(0.75f, r.LootFactor, 1e-5f, "0.5 + 0.5 x 1 of 2");
            Assert.IsTrue(r.IsBossStage);
        }

        [Test]
        public void MutatorsFromTheRiftsAndTheBosssSets()
        {
            var r = new RiftRun(3);
            r.BeginStage(0f, 0f);
            Assert.AreEqual(0, r.active.Count, "none on the first stage");
            r.EndStage();
            r.BeginStage(0f, 0f);
            Assert.AreEqual(RiftRun.Mutators[0], r.active[0]);
            r.EndStage();
            r.BeginStage(0f, 0.99f);
            Assert.AreEqual(RiftRun.BossMutators[RiftRun.BossMutators.Length - 1], r.active[1], "the boss's stage: a boss mutator");
            CollectionAssert.DoesNotContain(RiftRun.Mutators, "mutator_two_bosses", "not a rift's in ES2's mutators.csv");
        }

        [Test]
        public void TheMutatorsAreEsTwosRiftOnesAndBossOnes()
        {
            // mutators.csv: bAllowedInRifts 16, bAllowedOnlyForRiftBosses 6
            Assert.AreEqual(16, RiftRun.Mutators.Length);
            Assert.AreEqual(6, RiftRun.BossMutators.Length);
            CollectionAssert.IsSubsetOf(new[] { RiftRun.MineDrop, RiftRun.Overheat, RiftRun.CorrosionField, RiftRun.BurstBulbs }, RiftRun.Mutators);
            CollectionAssert.IsSubsetOf(new[] { RiftRun.BossEnergyShield, RiftRun.BossWebFields, RiftRun.BossApocalypse }, RiftRun.BossMutators);
            CollectionAssert.IsEmpty(RiftRun.Mutators.Intersect(RiftRun.BossMutators));
        }

        [Test]
        public void OverheatHalvesOneFreeSlotForItsDuration()
        {
            Overheat.Clear();
            try
            {
                var s = Overheat.HeatRandom(slot => slot != Overheat.Slot.Booster, 0.99f);
                Assert.AreEqual(Overheat.Slot.Shield, s, "the last free slot the ship has (no booster)");
                Assert.AreEqual(0.5f, Overheat.Factor(Overheat.Slot.Shield));
                Assert.AreEqual(1f, Overheat.Factor(Overheat.Slot.Primary));
                Assert.AreNotEqual(Overheat.Slot.Shield, Overheat.HeatRandom(null, 0.99f), "a hot slot isn't picked again");
                Overheat.Tick(Overheat.DurationS - 0.01f);
                Assert.IsTrue(Overheat.Hot(Overheat.Slot.Shield));
                Overheat.Tick(0.02f);
                Assert.IsFalse(Overheat.Hot(Overheat.Slot.Shield), "cool after DurationS");
                Assert.IsNull(Overheat.HeatRandom(_ => false, 0.5f), "nothing to overheat");
                Overheat.HeatRandom(null, 0f);
                Assert.IsTrue(Overheat.AnyHot);
                Assert.AreEqual(Overheat.Slot.Primary, Overheat.CoolRandom(0f), "a Nano Repair Kit cools one");
                Assert.IsFalse(Overheat.AnyHot);
                Assert.IsNull(Overheat.CoolRandom(0f));
            }
            finally { Overheat.Clear(); }
        }
    }
}
