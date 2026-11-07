// Gof3DogfightTests.cs
// Feature: docs/notes/npcs-and-combat.md "GoF3 dogfighting", Options > Gameplay "NPC dogfighting" (Flight.Gof3Dogfight, after
// GoF3's AIJob_Fight / Tease / ChasePrey and NPCBankingSystem): attack runs end in a veer-off (or a short hold-back) inside
// the break-off range or when their time is up; a fighter behind its target chases it at 1.6x; one in front of its nose
// teases now and then, holding fire; hits start a 1.8 s roll, invulnerable for 1.5 s, by the hull's base chance capped by
// the difficulty; on Easy fighters slow down in the player's sights.

using GoF2Remake.Flight;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    public class Gof3DogfightTests
    {
        static Gof3Dogfight Make(float chance, params float[] rolls)
        {
            int i = 0;   // the rolls in turn, the last one again and again (the constructor takes two: the tease side, the first run)
            return new Gof3Dogfight(chance, () => rolls[System.Math.Min(i++, rolls.Length - 1)]);
        }

        static Gof3Dogfight.View Far => new Gof3Dogfight.View { distance = 30000f, targetFacing = 0f };

        [Test]
        public void AnApproachEndsInAVeerOffInsideTheBreakOffRange()
        {
            var d = Make(0f, 0.9f);   // (0.9: no hold-back, no tease)
            Assert.AreEqual(Gof3Dogfight.Mode.Approach, d.Current);
            Assert.IsTrue(d.Fires);
            d.Step(16f, Far);
            Assert.AreEqual(Gof3Dogfight.Mode.Approach, d.Current, "still far: on");
            d.Step(16f, new Gof3Dogfight.View { distance = 5000f, insideBreakOff = true });
            Assert.AreEqual(Gof3Dogfight.Mode.VeerOff, d.Current);
            Assert.IsFalse(d.Fires, "no shots veering off");
            d.Step(Gof3Dogfight.VeerOffMaxMs, Far);
            Assert.AreEqual(Gof3Dogfight.Mode.Approach, d.Current, "then the next run");
            Assert.AreEqual(1f, d.AimError, 1e-4f, "a new run: the aim has to settle again");
            d.Tick(Gof3Dogfight.AimSettleMs);
            Assert.AreEqual(0f, d.AimError);
        }

        [Test]
        public void SomeRunsEndHoldingBackAndEveryRunHasItsTime()
        {
            var d = Make(0f, 0.1f);   // (0.1 < the 0.3 hold-back chance)
            d.Step(Gof3Dogfight.ApproachMaxMs + 1f, Far);
            Assert.AreEqual(Gof3Dogfight.Mode.Idle, d.Current, "its time up: a run ends too");
            d.Step(Gof3Dogfight.IdleMs + 1f, Far);
            Assert.AreEqual(Gof3Dogfight.Mode.Approach, d.Current);
        }

        [Test]
        public void BehindItsTargetItChasesThenVeersOffAndWaits()
        {
            var d = Make(0f, 0.9f);
            var behind = new Gof3Dogfight.View { distance = 5000f, targetFacing = -0.9f };
            d.Step(16f, behind);
            Assert.AreEqual(Gof3Dogfight.Mode.Chase, d.Current);
            Assert.AreEqual(Gof3Dogfight.ChaseSpeed, d.SpeedFactor);
            Assert.IsTrue(d.Fires);
            d.Step(Gof3Dogfight.ChaseMs + 1f, behind);
            Assert.AreEqual(Gof3Dogfight.Mode.VeerOff, d.Current);
            d.Step(Gof3Dogfight.VeerOffMaxMs, Far);
            d.Step(16f, behind);
            Assert.AreEqual(Gof3Dogfight.Mode.Approach, d.Current, "the chase's cooldown");
        }

        [Test]
        public void InFrontOfItsNoseItTeasesNowAndThen()
        {
            var d = Make(0f, 0.6f, 0.9f, 0.35f);   // (its side; a 12.5 s run; then below the tease chance)
            var ahead = new Gof3Dogfight.View { distance = 6000f, targetFacing = 0.9f };
            d.Step(Gof3Dogfight.TeaseEveryMs - 100f, ahead);
            Assert.AreEqual(Gof3Dogfight.Mode.Approach, d.Current, "not before its interval");
            d.Step(200f, ahead);
            Assert.AreEqual(Gof3Dogfight.Mode.Tease, d.Current);
            Assert.IsFalse(d.Fires, "bait: no shots");
            Assert.IsTrue(d.teaseSide == 1 || d.teaseSide == -1);
        }

        [Test]
        public void AHitStartsARollThatShieldsItForPartOfIt()
        {
            var d = Make(0.25f, 0.1f);
            Assert.IsTrue(d.Hit());
            Assert.IsTrue(d.Rolling);
            Assert.IsTrue(d.Invulnerable);
            Assert.IsFalse(d.Hit(), "not twice at once");
            d.Tick(Gof3Dogfight.RollInvulnerableMs + 1f);
            Assert.IsFalse(d.Invulnerable, "1.5 s of the 1.8 s");
            Assert.IsTrue(d.Rolling);
            d.Tick(Gof3Dogfight.RollMs);
            Assert.IsFalse(d.Rolling);
            Assert.IsFalse(d.Hit(), "locked for a while after");
            d.Tick(Gof3Dogfight.RollLockMs);
            Assert.IsTrue(d.Hit());
            Assert.IsFalse(Make(0.25f, 0.3f).Hit(), "its chance missed");
            Assert.IsFalse(Make(0f, 0f).Hit(), "Easy: never");
        }

        [Test]
        public void TheChanceGoesByTheDifficulty()
        {
            Assert.AreEqual(0f, Gof3Dogfight.EvadeChance(0f, false), "Easy");
            Assert.AreEqual(0.15f, Gof3Dogfight.EvadeChance(0.5f, false), "Normal: GoF3's Medium cap");
            Assert.AreEqual(0.25f, Gof3Dogfight.EvadeChance(1f, false), "Hard");
            Assert.AreEqual(0.33f, Gof3Dogfight.EvadeChance(1.5f, false), "Extreme (GoF3's boss column, 0.66 cap): a fighter's whole 0.33");
            Assert.AreEqual(0.66f, Gof3Dogfight.EvadeChance(1.5f, false, 1f), "Extreme: a T3+ gunship's 1.0 to the cap");
            Assert.AreEqual(0.25f, Gof3Dogfight.EvadeChance(1f, false, 1f), "Hard: capped");
            Assert.AreEqual(0f, Gof3Dogfight.EvadeChance(1.5f, false, 0f), "a scout or a T1-T2 hull never rolls");
            Assert.AreEqual(0.1f, Gof3Dogfight.EvadeChance(0.5f, false, 0.1f), 1e-6f, "GoF3's bosses: 0.1");
            Assert.AreEqual(0.33f, Gof3Dogfight.EvadeChance(0.5f, true), "a boss on the boss column: its base");
            Assert.AreEqual(0f, Gof3Dogfight.EvadeChance(0f, true));
        }

        [Test]
        public void OnEasyFightersSlowDownInThePlayersSights()
        {
            Assert.AreEqual(1f, Gof3Dogfight.EasySlowdown(Gof3Dogfight.EasySlowRange + 1f, 1f, 0f), "too far");
            Assert.AreEqual(1f, Gof3Dogfight.EasySlowdown(1000f, 0.2f, 0f), "outside its sights");
            Assert.AreEqual(Gof3Dogfight.EasySlowMin, Gof3Dogfight.EasySlowdown(1000f, 0.9f, 0f), "crossing: a third");
            Assert.AreEqual(0.8f, Gof3Dogfight.EasySlowdown(1000f, 0.9f, -0.8f), 1e-6f, "head-on: |heading . heading|");
        }

        [Test]
        public void TheVFillsBothWingsInTurnAndTheSpeedClosesOnTheSlot()
        {
            Assert.AreEqual((1, 1), Gof3Dogfight.VSlot(0));
            Assert.AreEqual((1, -1), Gof3Dogfight.VSlot(1));
            Assert.AreEqual((2, 1), Gof3Dogfight.VSlot(2));
            Assert.AreEqual((3, -1), Gof3Dogfight.VSlot(5));
            Assert.AreEqual(1f, Gof3Dogfight.FormationSpeed(0f), "on its slot");
            Assert.AreEqual(1.25f, Gof3Dogfight.FormationSpeed(1f), 1e-5f, "a spacing behind: a quarter faster");
            Assert.AreEqual(1.5f, Gof3Dogfight.FormationSpeed(10f), "far behind: at most 1.5 x");
            Assert.AreEqual(0.6f, Gof3Dogfight.FormationSpeed(-10f), "far ahead: at least 0.6 x");
        }
    }
}
