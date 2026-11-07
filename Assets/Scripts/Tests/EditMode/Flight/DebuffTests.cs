// DebuffTests.cs
// Feature: Modding/README.md "Weapons" (dotDamage / dotMs, vulnerablePercent / vulnerableMs), docs/notes/combat-equipment.md
// "Weapon debuffs": Flight.Debuffs, after EVERSPACE 2's Corrosion (a 0.95 s timer dealing its intensity) and Destabilized
// (+0.2 incoming damage per token, stacking) conditions. Expected values: those timers and sums by hand.

using GoF2Remake.Flight;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    public class DebuffTests
    {
        [Test]
        public void DamageOverTimeTicksEveryNinetyFiveHundredthsForItsTime()
        {
            var d = new Debuffs();
            d.AddDot(5f, 3000f);
            Assert.AreEqual(0f, d.Step(900f), "not yet a tick");
            Assert.AreEqual(5f, d.Step(100f), 1e-4f, "the first at 0.95 s");
            Assert.AreEqual(10f, d.Step(1900f), 1e-4f, "two more by 2.9 s");
            Assert.AreEqual(5f, d.Step(1000f), 1e-4f, "a fourth tick, then its time is up");
            Assert.AreEqual(0f, d.Step(5000f));
            Assert.IsFalse(d.Any);
        }

        [Test]
        public void StackedHitsDealTheirSum()
        {
            var d = new Debuffs();
            d.AddDot(5f, 3000f);
            d.AddDot(3f, 3000f);
            Assert.AreEqual(8f, d.Step(Debuffs.TickMs), 1e-4f);
        }

        [Test]
        public void VulnerabilityStacksUpToTheCapAndRunsOut()
        {
            var d = new Debuffs();
            Assert.AreEqual(1f, d.DamageFactor);
            d.AddVulnerability(20f, 4000f);
            Assert.AreEqual(1.2f, d.DamageFactor, 1e-4f, "ES2's +0.2 a token");
            d.AddVulnerability(20f, 1000f);
            Assert.AreEqual(1.4f, d.DamageFactor, 1e-4f);
            d.Step(1500f);
            Assert.AreEqual(1.2f, d.DamageFactor, 1e-4f, "the shorter stack ran out");
            for (int i = 0; i < 10; i++) d.AddVulnerability(20f, 4000f);
            Assert.AreEqual(1f + Debuffs.MaxVulnerablePercent / 100f, d.DamageFactor, 1e-4f, "capped");
            d.Step(5000f);
            Assert.AreEqual(1f, d.DamageFactor);
        }

        [Test]
        public void NothingForAZeroAmountOrTime()
        {
            var d = new Debuffs();
            d.AddDot(0f, 3000f);
            d.AddVulnerability(20f, 0f);
            Assert.IsFalse(d.Any);
        }
    }
}
