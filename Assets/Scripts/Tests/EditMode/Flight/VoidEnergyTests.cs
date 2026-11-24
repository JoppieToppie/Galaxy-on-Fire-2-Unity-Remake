// VoidEnergyTests.cs
// Feature: docs/notes/combat-equipment.md "Rites", Modding/README.md "Devices": Chorus's Void energy (Flight.VoidEnergy):
// 3 pips, one back every 1 / 0.22 s on a tonne of Void Crystals (none aboard: none grow), the bonus pips paid first and
// running out, a cost above the total refused; Rite of the
// Hunt's numbers (Flight.Device 23: Blink's range, landing and cost), where it lands, its timelines and the camera's glide;
// the Rite of the Storm's numbers (Flight.Device 24: Pulse Zap's) and its jam (Target.Jammed); the Rite of the Star's
// (Flight.Device 25: the Drill's), its reach and its drive's turn cap (ShipController.CapTurn); the Rite of Control's
// (Flight.Device 26: Telekinesis's); the Rite of the Senses' (Flight.Device 27: Scan's) and its sight (RiteSenses); the
// passive Rite (28) and the upgrades' numbers (the bonus pip's life, the fury, the Star's side spheres).

using GoF2Remake.Flight;
using NUnit.Framework;
using UnityEngine;

namespace GoF2Remake.Tests
{
    public class VoidEnergyTests
    {
        [Test]
        public void ThePoolStartsFullAndGrowsBackAPipEvery4Point5Seconds()
        {
            var r = new VoidEnergy();
            Assert.AreEqual(3, r.Total);
            Assert.IsTrue(r.Full);
            Assert.IsTrue(r.TryPay(2));
            Assert.AreEqual(1, r.Regular);
            r.Step(4.5f);
            Assert.AreEqual(1, r.Regular, "0.99 of a pip");
            Assert.AreEqual(0.99f, r.Progress, 1e-4f);
            r.Step(0.05f);
            Assert.AreEqual(2, r.Regular, "1 / 0.22 = 4.545 s a pip");
            r.Step(10f);
            Assert.AreEqual(3, r.Regular);
            Assert.AreEqual(0f, r.Progress, "nothing grows while full");
        }

        [Test]
        public void ThePipsGrowBackOnVoidCrystals()
        {
            int crystals = 2;
            var r = new VoidEnergy { HasFuel = () => crystals >= VoidEnergy.CrystalsPerPip, Burn = () => crystals -= VoidEnergy.CrystalsPerPip };
            Assert.AreEqual(164, VoidEnergy.VoidCrystalItem);
            r.TryPay(3);
            r.Step(4.6f);
            Assert.AreEqual(1, r.Regular);
            Assert.AreEqual(1, crystals, "a tonne a pip");
            r.Step(4.6f);
            Assert.AreEqual(2, r.Regular);
            Assert.AreEqual(0, crystals);
            Assert.IsTrue(r.Starved, "the last one went");
            r.Step(30f);
            Assert.AreEqual(2, r.Regular, "none aboard: none grow");
            Assert.AreEqual(0f, r.Progress);
            crystals = 5;
            r.Step(4.6f);
            Assert.AreEqual(3, r.Regular, "more aboard: it grows again");
            Assert.IsFalse(r.Starved);
            Assert.AreEqual(4, crystals);
            r.Step(10f);
            Assert.AreEqual(4, crystals, "nothing burnt while full");
        }

        [Test]
        public void ACostAboveTheTotalIsRefusedAndNothingPaid()
        {
            var r = new VoidEnergy();
            Assert.IsTrue(r.TryPay(2));
            Assert.IsFalse(r.CanPay(2));
            Assert.IsFalse(r.TryPay(2));
            Assert.AreEqual(1, r.Regular, "untouched");
            Assert.IsTrue(r.TryPay(0), "a free Rite");
            Assert.AreEqual(0.5f, r.ChargeToward(2), 1e-4f, "the HUD's sweep: 1 of 2");
            r.Step(1f);
            Assert.AreEqual(0.61f, r.ChargeToward(2), 1e-4f, "with the pip growing back");
        }

        [Test]
        public void BonusPipsArePaidFirstAndRunOut()
        {
            var r = new VoidEnergy();
            r.AddBonus(2, 5f);
            Assert.AreEqual(5, r.Total, "past the maximum (Blink's upgrade A)");
            Assert.IsTrue(r.TryPay(3));
            Assert.AreEqual(0, r.Bonus);
            Assert.AreEqual(2, r.Regular, "the bonus ones first");
            r.AddBonus(1, 2f);
            r.Step(2.5f);
            Assert.AreEqual(0, r.Bonus, "timed out");
            r.AddRegular(5);
            Assert.AreEqual(3, r.Regular, "capped");
        }

        [Test]
        public void ARegenDelayHoldsTheGrowthAfterAPayment()
        {
            var r = new VoidEnergy(3, 0.5f, 1f);
            r.TryPay(1);
            r.Step(0.8f);
            Assert.AreEqual(0f, r.Progress);
            r.Step(0.4f);
            Assert.AreEqual(0.1f, r.Progress, 1e-4f, "the rest of the step past the delay");
            r.Refill();
            Assert.AreEqual(3, r.Total);
        }

        [Test]
        public void TheHuntsNumbersAreBlinks()
        {
            Assert.AreEqual(Device.Type.RiteHunt, (Device.Type)23);
            Assert.AreEqual(1000f, Device.HuntRangeTops * 140f, 1e-3f, "Range 100000 cm at Chorus's 140 m/s");
            Assert.AreEqual(180f, Device.HuntBehindTops * 140f, 1e-3f, "BlinkLocationOffset -18000 cm");
            Assert.AreEqual(2, Device.HuntCost);
            Assert.AreEqual(500, Device.HuntCooldownMs);
            var spot = Device.HuntSpot(new Vector3(10f, 0f, 0f), new Vector3(0f, 0f, 2f), 50f);
            Assert.Less(Vector3.Distance(new Vector3(10f, 0f, -50f), spot), 1e-4f, "on its tail, whatever the forward's length");
        }

        [Test]
        public void TheHuntsTimelinesAndTheCamerasGlide()
        {
            Assert.AreEqual(1.5f, Device.HuntPlayRate(0f), 1e-5f, "BlinkPlayRateCurve: 1.5x for a leap of nothing");
            Assert.AreEqual(1f, Device.HuntPlayRate(1f), 1e-5f, "1x at the full reach");
            Assert.AreEqual(1f, Device.HuntPlayRate(3f), 1e-5f);
            Assert.AreEqual(0f, Device.HuntVanish(0f));
            Assert.AreEqual(1f, Device.HuntVanish(1f));
            Assert.AreEqual(0.39f, Device.HuntVanish(0.733f), 0.03f, "Blink_Disappear's 0.41 at 73 %");
            Assert.AreEqual(1f, Device.HuntAppear(0f), "gone as it lands");
            Assert.AreEqual(0f, Device.HuntAppear(1f), "whole again after 0.33 s");
            Assert.AreEqual(66.667f, Device.HuntVanishMs, 1e-3f);
            Assert.AreEqual(0f, ChaseCamera.GlideAlpha(0f));
            Assert.AreEqual(0.875f, ChaseCamera.GlideAlpha(0.5f), 1e-5f, "fast at first");
            Assert.AreEqual(1f, ChaseCamera.GlideAlpha(1f));
        }

        [Test]
        public void TheStormsNumbersArePulseZaps()
        {
            Assert.AreEqual(Device.Type.RiteStorm, (Device.Type)24);
            Assert.AreEqual(100f, Device.StormChainTops * 140f, 1e-3f, "ChainRange 10000 cm");
            Assert.AreEqual(2, Device.StormCost);
            Assert.AreEqual(4000, Device.StormJamMs, "BP_Buff_Pulse's Duration 4");
            Assert.AreEqual(250, Device.StormChainMs, "ChainInterval 0.25");
            Assert.AreEqual(500, Device.StormCooldownMs);
            var go = new GameObject("jammed");
            try
            {
                var t = go.AddComponent<Target>();
                Assert.IsFalse(t.Jammed);
                t.jammedUntil = Time.time + 4f;
                Assert.IsTrue(t.Jammed, "holds its fire");
            }
            finally { Object.DestroyImmediate(go); }
        }
    
        [Test]
        public void TheStarsNumbersAreTheDrills()
        {
            Assert.AreEqual(Device.Type.RiteStar, (Device.Type)25);
            Assert.AreEqual(3f, Device.StarSpeedTops, 1e-5f, "DrillForwardVelocity 42000 cm/s: 3x Chorus's top speed");
            Assert.AreEqual(72f, Device.StarTurnDegS);
            Assert.AreEqual(3, Device.StarCost);
            Assert.AreEqual(500, Device.StarMinMs);
            Assert.AreEqual(3000, Device.StarMaxMs);
            Assert.AreEqual(200, Device.StarTickMs, "the blueprint's CheckForCollisionsInterval");
            Assert.AreEqual(300, Device.StarTailMs);
            Assert.AreEqual(100f, Device.StarRadiusTops * 140f, 1e-3f, "SphereTraceRadius 10000 cm");
            Assert.AreEqual(25f, Device.StarAheadTops * 140f, 1e-3f, "2500 cm ahead");
            Assert.IsTrue(Device.InStar(Vector3.zero, Vector3.forward, new Vector3(0f, 0f, 120f), 25f, 100f));
            Assert.IsFalse(Device.InStar(Vector3.zero, Vector3.forward, new Vector3(0f, 0f, -80f), 25f, 100f), "behind: out of reach");
            float p = 3f, y = 4f;
            ShipController.CapTurn(ref p, ref y, 1f);
            Assert.AreEqual(0.6f, p, 1e-5f);
            Assert.AreEqual(0.8f, y, 1e-5f, "held to the cap, its direction kept");
            p = 0.1f; y = 0.2f;
            ShipController.CapTurn(ref p, ref y, 1f);
            Assert.AreEqual(0.1f, p, "under it: untouched");
        }

        [Test]
        public void ControlsNumbersAreTelekinesiss()
        {
            Assert.AreEqual(Device.Type.RiteControl, (Device.Type)26);
            Assert.AreEqual(700f, Device.ControlGrabTops * 140f, 1e-3f, "GrabRange 70000 cm");
            Assert.AreEqual(800f, Device.ControlTossTops * 140f, 1e-3f, "TossRange 80000 cm");
            Assert.AreEqual(4f, Device.ControlSpeedTops, 1e-5f, "TossSpeed 56000 cm/s: 4x Chorus's top speed");
            Assert.Less(Vector3.Distance(new Vector3(25f, 25f, 50f), Device.ControlHoldTops * 140f), 1e-3f, "OffsetLocations[0]: ahead, right, up");
            Assert.AreEqual(3, Device.ControlCost, "the blueprint's EnergyCost 3");
            Assert.AreEqual(1928.57f, Device.ControlFlightMs, 0.01f, "the toss's reach at its speed and half a second");
        }

        [Test]
        public void TheSensesNumbersAreScans()
        {
            Assert.AreEqual(Device.Type.RiteSenses, (Device.Type)27);
            Assert.AreEqual(6000f, Device.SensesTops * 140f, 1e-2f, "ShortRangeScanDistance 600000 cm");
            Assert.AreEqual(3000, Device.SensesWaveMs, "ShortRangeScanDuration 3 s");
            Assert.AreEqual(1, Device.SensesCost);
            Assert.AreEqual(200000f, Device.SensesLongTops * 140f, 1e-1f, "LongRangeScanDistance 20000000 cm");
            Assert.AreEqual(1000, Device.SensesChargeMs, "LongRangeChargeDuration 1 s");
            Assert.AreEqual(3000, Device.SensesLongWaveMs, "LongRangeScanDuration 3 s");
            float was = RiteSenses.Until;
            try
            {
                RiteSenses.Until = Time.time - 1f;
                Assert.IsFalse(RiteSenses.Active);
                RiteSenses.Until = Time.time + 20f;
                Assert.IsTrue(RiteSenses.Active, "the ship sees as the Ultrascan does");
            }
            finally { RiteSenses.Until = was; }
        }

        [Test]
        public void ThePassiveAndTheUpgrades()
        {
            Assert.AreEqual(Device.Type.RitePassive, (Device.Type)28);
            Assert.AreEqual(28, Device.TypeCount);
            var r = new VoidEnergy(VoidEnergy.ChorusMax + 2);
            Assert.AreEqual(5, r.Total, "the Elemental Shield's BonusEnergy 2: two pips more");
            r.AddBonus(Device.HuntBonusPips);
            Assert.AreEqual(6, r.Total, "Blink A's bonus pip");
            r.Step(9.9f);
            Assert.AreEqual(1, r.Bonus);
            r.Step(0.2f);
            Assert.AreEqual(0, r.Bonus, "BonusEnergyLifetime 10 s");
            Assert.AreEqual(1.5f, Device.FuryFactor);
            Assert.AreEqual(5000, Device.FuryMs);
            Assert.AreEqual(500f, Device.StormChainTopsA * 140f, 1e-3f, "Pulse A's ChainRange 50000 cm");
            Assert.AreEqual(0.25f, Device.StealShare);
            var side = Device.StarSide(0, 0f) * 140f;
            Assert.AreEqual(100f, side.z, 1e-3f, "Drill A's spheres 10000 cm ahead");
            Assert.AreEqual(60f, side.x, 1e-3f);
            Assert.AreEqual(60f, side.y, 1e-3f, "at (+6000, +6000) cm");
            var later = Device.StarSide(0, 900f) * 140f;
            Assert.AreEqual(-60f, later.x, 1e-3f, "turned 90 deg in 0.9 s at 100 deg/s");
            Assert.AreEqual(60f, later.y, 1e-3f);
        }
    }
}
