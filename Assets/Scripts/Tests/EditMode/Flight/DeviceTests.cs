// DeviceTests.cs
// Feature: docs/notes/combat-equipment.md "Devices", Modding/README.md "Devices": an active device's rules (Flight.Device):
// ready -> running for its duration -> cooling down -> ready; an instant device goes straight to its cooldown; the strike
// charge fires only once charged by damage dealt (nothing while it runs); the EMP wave's front, the overcharger's cone and
// sums; the slow an EMP device leaves on a ship (Flight.Slow).

using GoF2Remake.Flight;
using NUnit.Framework;
using UnityEngine;

namespace GoF2Remake.Tests
{
    public class DeviceTests
    {
        [Test]
        public void ARunThenTheCooldownThenReady()
        {
            var d = new Device(300, Device.Type.AreaEmp, 8000, 60000);
            Assert.IsTrue(d.Available);
            Assert.IsTrue(d.TryFire());
            Assert.IsTrue(d.Active);
            Assert.IsFalse(d.Available);
            Assert.IsFalse(d.TryFire(), "not again while it runs");
            Assert.AreEqual(Device.Event.None, d.Update(4000f));
            Assert.AreEqual(0.5f, d.ActiveLeft, 1e-4f);
            Assert.AreEqual(Device.Event.Ended, d.Update(4000f));
            Assert.AreEqual(Device.Phase.Cooldown, d.State);
            Assert.AreEqual(0f, d.RechargeRate, 1e-4f);
            d.Update(30000f);
            Assert.AreEqual(0.5f, d.RechargeRate, 1e-4f);
            Assert.AreEqual(Device.Event.Ready, d.Update(30000f));
            Assert.IsTrue(d.Available);
            Assert.AreEqual(1f, d.RechargeRate);
        }

        [Test]
        public void AnInstantDeviceCoolsDownAtOnce()
        {
            var d = new Device(301, Device.Type.OverchargeArea, 0, 60000);
            Assert.IsTrue(d.TryFire());
            Assert.AreEqual(Device.Phase.Cooldown, d.State);
            Assert.IsFalse(d.Active);
            Assert.AreEqual(0f, d.ActiveLeft);
        }

        [Test]
        public void PausedFramesChangeNothing()
        {
            var d = new Device(300, Device.Type.Deflector, 8000, 60000);
            d.TryFire();
            Assert.AreEqual(Device.Event.None, d.Update(0f));
            Assert.AreEqual(Device.Event.None, d.Update(-50f));
            Assert.AreEqual(0f, d.Timer);
        }

        [Test]
        public void TheStrikeChargeFiresOnlyWhenCharged()
        {
            var d = new Device(302, Device.Type.StrikeCharge, 5000, 60000, 875);
            Assert.IsFalse(d.Available);
            Assert.IsFalse(d.TryFire());
            d.AddCharge(500f);
            Assert.AreEqual(500f / 875f, d.RechargeRate, 1e-4f);
            d.AddCharge(-100f);
            Assert.AreEqual(500f, d.Charge);
            d.AddCharge(1000f);
            Assert.AreEqual(875f, d.Charge, "full, no more");
            Assert.IsTrue(d.TryFire());
            Assert.AreEqual(0f, d.Charge, "firing empties it");
            d.AddCharge(300f);
            Assert.AreEqual(0f, d.Charge, "nothing while it runs");
            d.Update(5000f);
            d.AddCharge(300f);
            Assert.AreEqual(300f, d.Charge, "it fills during the cooldown");
            d.Update(60000f);
            Assert.AreEqual(Device.Phase.Ready, d.State);
            Assert.IsFalse(d.Available, "ready but not charged");
        }

        [Test]
        public void TheDebugOptionSkipsTheCooldownAndTheCharge()
        {
            var d = new Device(302, Device.Type.StrikeCharge, 5000, 60000, 875);
            d.Update(16f, true);
            Assert.IsTrue(d.Available, "charged at once");
            d.TryFire();
            d.Update(16f, true);
            Assert.IsTrue(d.Active, "a run still runs its time");
            d.Update(5000f, true);
            Assert.AreEqual(Device.Event.Ready, d.Update(16f, true));
            Assert.IsTrue(d.Available);
        }

        [Test]
        public void CancelEndsARunAndStartsTheCooldown()
        {
            var d = new Device(300, Device.Type.AreaEmp, 8000, 60000);
            Assert.IsFalse(d.Cancel(), "nothing runs");
            d.TryFire();
            Assert.IsTrue(d.Cancel());
            Assert.AreEqual(Device.Phase.Cooldown, d.State);
            Assert.AreEqual(0f, d.Timer);
        }

        [Test]
        public void TheWaveGrowsToItsRadius()
        {
            Assert.AreEqual(0f, Device.WaveRadius(0f, 2500f, 9120f), 1e-3f);
            Assert.AreEqual(9120f * 0.75f, Device.WaveRadius(1250f, 2500f, 9120f), 1e-2f, "fast first, slower at the end");
            Assert.AreEqual(9120f, Device.WaveRadius(2500f, 2500f, 9120f), 1e-3f);
            Assert.AreEqual(9120f, Device.WaveRadius(9000f, 2500f, 9120f), 1e-3f);
            Assert.AreEqual(9120f, Device.WaveRadius(0f, 0f, 9120f), "no grow time: at once");
        }

        [Test]
        public void TheConeAndItsRange()
        {
            var fwd = Vector3.forward;
            Assert.IsTrue(Device.InCone(fwd, new Vector3(0f, 0f, 100f), 200f, 30f));
            Assert.IsFalse(Device.InCone(fwd, new Vector3(0f, 0f, 300f), 200f, 30f), "out of range");
            Assert.IsTrue(Device.InCone(fwd, Quaternion.Euler(0f, 29f, 0f) * Vector3.forward * 100f, 200f, 30f));
            Assert.IsFalse(Device.InCone(fwd, Quaternion.Euler(0f, 31f, 0f) * Vector3.forward * 100f, 200f, 30f));
            Assert.IsTrue(Device.InCone(fwd, Vector3.back * 100f, 200f, 180f), "180 = every direction");
        }

        [Test]
        public void TheOverchargerSpendsAShareOfTheShield()
        {
            Assert.AreEqual(198, Device.OverchargeCost(300, 66));
            Assert.AreEqual(0, Device.OverchargeCost(0, 66), "no shield, nothing to spend");
            Assert.AreEqual(79.2f, Device.OverchargeDamage(198, 40), 1e-3f);
            Assert.AreEqual(59.4f, Device.OverchargeDamage(198, 30), 1e-3f);
            Assert.AreEqual(0.25f, Device.SlowFactor(25), 1e-5f);
            Assert.AreEqual(1f, Device.SlowFactor(250));
        }

        [Test]
        public void ASlowRunsOutAndTheStrongerLongerOneWins()
        {
            var s = new Slow();
            Assert.IsFalse(s.Active);
            Assert.AreEqual(1f, s.SpeedFactor);
            Assert.IsTrue(s.CanTurn);
            s.Apply(8000f, 0.25f);
            Assert.AreEqual(0.25f, s.SpeedFactor);
            Assert.IsFalse(s.CanTurn);
            s.Tick(5000f);
            s.Apply(2000f, 0.5f);
            Assert.AreEqual(3000f, s.LeftMs, 1e-3f, "the longer time stays");
            Assert.AreEqual(0.25f, s.SpeedFactor, "the stronger slow stays");
            s.Tick(3000f);
            Assert.IsFalse(s.Active);
            Assert.AreEqual(1f, s.SpeedFactor);
            s.Apply(0f, 0.1f);
            Assert.IsFalse(s.Active, "no time, no slow");
        }

        [Test]
        public void AWeaponsSlowLetsTheShipTurn()
        {
            // GoF3's AbilitySlowdown (a mod weapon's slowMs / slowPercent): the speed only
            var s = new Slow();
            s.Apply(6000f, 0.5f, false);
            Assert.AreEqual(0.5f, s.SpeedFactor);
            Assert.IsTrue(s.CanTurn);
            s.Apply(2000f, 0.25f);
            Assert.IsFalse(s.CanTurn, "an EMP device's stops the turning");
            s.Tick(2000f);
            Assert.IsTrue(s.CanTurn, "for its own time");
            Assert.IsTrue(s.Active, "the longer slow goes on");
            Assert.AreEqual(0.25f, s.SpeedFactor);
        }
    }
}
