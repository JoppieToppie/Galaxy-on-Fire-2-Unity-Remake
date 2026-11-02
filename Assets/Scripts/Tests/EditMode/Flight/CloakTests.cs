// CloakTests.cs
// The player's cloak (CLAUDE.md "Combat equipment", Cloak; Reference/research/combat_equipment.md 1.2-1.4). Expected
// numbers from the research tables: U'tool (95) 10000 ms cloaked, 2000 ms charge, 1 energy cell; Shadow Ninja (96)
// 40000 / 3500 / 5; cooldown by difficulty <= 0 5000, <= 0.6 7000, <= 1.1 9000, else 12000; getCloakingPercentage
// t * 100 / 2000 over the first 2000 ms, (1 - (t - (dur - 2000)) / 2000) * 100 over the last, 100 between; the charge
// ends when the timer passes the charge time (phaseTimer > chargeTime), the cloak when it passes the duration. The EMP
// break is the remake's option (Settings.EmpBreaksCloak): tested from its description.

using GoF2Remake.Data;
using GoF2Remake.Flight;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    public class CloakTests
    {
        static Cloak UTool(float difficulty = Session.DifficultyNormal) => new Cloak(95, 10000, 2000, 1, difficulty);

        [TestCase(Session.DifficultyEasy, 5000f)]
        [TestCase(Session.DifficultyNormal, 7000f)]
        [TestCase(Session.DifficultyHard, 9000f)]
        [TestCase(Session.DifficultyExtreme, 12000f)]
        public void CooldownByDifficulty(float difficulty, float expected)
        {
            Assert.AreEqual(expected, UTool(difficulty).cooldownMs);
        }

        [Test]
        public void StartsReady()
        {
            var c = UTool();
            Assert.AreEqual(Cloak.Phase.Ready, c.State);
            Assert.IsTrue(c.Available);
            Assert.IsFalse(c.Cloaked);
            Assert.AreEqual(0f, c.Percentage);
            Assert.AreEqual(1f, c.RechargeRate);
        }

        [Test]
        public void NeedsItsEnergyCells()
        {
            var ninja = new Cloak(96, 40000, 3500, 5, 0.5f);
            Assert.IsFalse(ninja.TryStart(4));
            Assert.AreEqual(Cloak.Phase.Ready, ninja.State);
            Assert.IsTrue(ninja.TryStart(5));
            Assert.AreEqual(Cloak.Phase.Charging, ninja.State);
            Assert.IsFalse(ninja.TryStart(100), "not again while charging");
        }

        [Test]
        public void ChargeThenEngage()
        {
            var c = UTool();
            c.TryStart(1);
            Assert.AreEqual(Cloak.Event.None, c.Update(1000f));
            Assert.AreEqual(0.5f, c.ChargeRate, 1e-5f);
            Assert.IsFalse(c.Available);
            Assert.AreEqual(Cloak.Event.None, c.Update(1000f), "the charge ends past 2000 ms");
            Assert.AreEqual(Cloak.Event.Engaged, c.Update(1f));
            Assert.IsTrue(c.Cloaked);
            Assert.AreEqual(0f, c.ChargeRate);
        }

        [Test]
        public void FadesInAndOutOverTwoSeconds()
        {
            var c = UTool();
            c.TryStart(1);
            c.Update(2001f);
            Assert.AreEqual(0f, c.Percentage);
            c.Update(1000f);
            Assert.AreEqual(50f, c.Percentage, 1e-3f);
            c.Update(4000f);    // 5000 ms
            Assert.AreEqual(100f, c.Percentage, 1e-3f);
            c.Update(4000f);    // 9000 ms: half way out
            Assert.AreEqual(50f, c.Percentage, 1e-3f);
            c.Update(500f);
            Assert.AreEqual(25f, c.Percentage, 1e-3f);
        }

        [Test]
        public void EndsAfterItsDurationThenCoolsDown()
        {
            var c = UTool();
            c.TryStart(1);
            c.Update(2001f);
            Assert.AreEqual(Cloak.Event.None, c.Update(10000f));
            Assert.IsTrue(c.Cloaked);
            Assert.AreEqual(Cloak.Event.Ended, c.Update(1f));
            Assert.AreEqual(Cloak.Phase.Cooldown, c.State);
            Assert.AreEqual(7000f, c.CooldownLeft);
            Assert.AreEqual(0f, c.RechargeRate, 1e-6f);
            Assert.IsFalse(c.TryStart(10), "no cloak while recharging");
            c.Update(3500f);
            Assert.AreEqual(0.5f, c.RechargeRate, 1e-5f);
            Assert.AreEqual(Cloak.Event.Ready, c.Update(3500f));
            Assert.IsTrue(c.Available);
            Assert.AreEqual(0f, c.CooldownLeft);
        }

        [Test]
        public void NoEquipmentCooldownEndsTheCooldownAtOnce()
        {
            // Remake debug (Cheats.NoEquipmentCooldown, PlayerCloak): the cooldown is over in the next update, "Cloak ready";
            // the charge and the cloak itself keep their times.
            var c = UTool();
            c.TryStart(1);
            Assert.AreEqual(Cloak.Event.None, c.Update(1000f, true), "the charge still takes its time");
            c.Update(1001f, true);
            Assert.AreEqual(Cloak.Event.None, c.Update(5000f, true), "and so does the cloak");
            c.Update(5001f, true);
            Assert.AreEqual(Cloak.Phase.Cooldown, c.State);
            Assert.AreEqual(Cloak.Event.Ready, c.Update(1f, true));
            Assert.IsTrue(c.Available);
            Assert.IsTrue(c.TryStart(1), "cloaks again straight away");
        }

        [Test]
        public void TimelineLengthOfTheShadowNinja()
        {
            // 3500 ms charge, 40000 ms cloaked, 12000 ms cooldown on Extreme
            var c = new Cloak(96, 40000, 3500, 5, Session.DifficultyExtreme);
            c.TryStart(5);
            int ms = 0;
            Cloak.Event e;
            var seen = new System.Collections.Generic.List<(Cloak.Event, int)>();
            do
            {
                e = c.Update(100f);
                ms += 100;
                if (e != Cloak.Event.None) seen.Add((e, ms));
            } while (e != Cloak.Event.Ready && ms < 100000);
            Assert.AreEqual(3, seen.Count);
            Assert.AreEqual((Cloak.Event.Engaged, 3600), seen[0]);
            Assert.AreEqual((Cloak.Event.Ended, 3600 + 40100), seen[1]);
            Assert.AreEqual((Cloak.Event.Ready, 3600 + 40100 + 12000), seen[2]);
        }

        [Test]
        public void DurationNeverShorterThanBothFades()
        {
            Assert.AreEqual(4000, new Cloak(95, 1000, 2000, 1, 0.5f).durationMs);
            Assert.AreEqual(10000, UTool().durationMs);
        }

        [Test]
        public void EmpBreaksAChargeOrTheCloak()
        {
            var charging = UTool();
            charging.TryStart(1);
            Assert.IsTrue(charging.Break());
            Assert.AreEqual(Cloak.Phase.Cooldown, charging.State);
            Assert.AreEqual(7000f, charging.CooldownLeft);

            var cloaked = UTool();
            cloaked.TryStart(1);
            cloaked.Update(2001f);
            cloaked.Update(3000f);
            Assert.IsTrue(cloaked.Break());
            Assert.IsFalse(cloaked.Cloaked);
            Assert.AreEqual(0f, cloaked.Percentage);
        }

        [Test]
        public void NothingToBreakWhenReadyOrRecharging()
        {
            var c = UTool();
            Assert.IsFalse(c.Break());
            Assert.AreEqual(Cloak.Phase.Ready, c.State);
            c.TryStart(1);
            c.Break();
            c.Update(1000f);
            Assert.IsFalse(c.Break());
            Assert.AreEqual(6000f, c.CooldownLeft, 1e-3f, "a second EMP doesn't restart the cooldown");
        }
    }
}
