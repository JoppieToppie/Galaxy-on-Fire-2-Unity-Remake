// WingmanDeviceTests.cs
// Feature: Modding/README.md "Wingmen", docs/notes/bar-and-freelance.md "Wingmen": a hired wingman's device by its hull
// (Flight.WingmanDevice, after GoF3's DeviceConfigurationWingman*): Hull Repair under 40 % mends 25 % over 3 s, Ammo Supply
// at one round or none, Charged Shots x1.75 for 5 s while it attacks; 55 s of cooldown after each use.

using GoF2Remake.Flight;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    public class WingmanDeviceTests
    {
        [Test]
        public void TheWordsNameTheDevices()
        {
            Assert.AreEqual(WingmanDevice.Kind.Repair, WingmanDevice.Parse("Repair"));
            Assert.AreEqual(WingmanDevice.Kind.Ammo, WingmanDevice.Parse(" ammo "));
            Assert.AreEqual(WingmanDevice.Kind.Charged, WingmanDevice.Parse("charged"));
            Assert.AreEqual(WingmanDevice.Kind.None, WingmanDevice.Parse("cloak"));
            Assert.AreEqual(WingmanDevice.Kind.None, WingmanDevice.Parse(null));
            Assert.IsFalse(WingmanDevice.Valid(""));
        }

        [Test]
        public void EachWantsItsOwnMoment()
        {
            var repair = new WingmanDevice(WingmanDevice.Kind.Repair);
            Assert.IsTrue(repair.Wanted(0.39f, -1, false));
            Assert.IsFalse(repair.Wanted(0.4f, -1, false), "40 % or more: not yet");
            Assert.IsFalse(repair.Wanted(0f, -1, false), "a dead leader: no");
            var ammo = new WingmanDevice(WingmanDevice.Kind.Ammo);
            Assert.IsTrue(ammo.Wanted(1f, 1, false));
            Assert.IsTrue(ammo.Wanted(1f, 0, false));
            Assert.IsFalse(ammo.Wanted(1f, 2, false));
            Assert.IsFalse(ammo.Wanted(1f, -1, false), "no secondary selected");
            var charged = new WingmanDevice(WingmanDevice.Kind.Charged);
            Assert.IsTrue(charged.Wanted(1f, -1, true));
            Assert.IsFalse(charged.Wanted(1f, -1, false));
        }

        [Test]
        public void HullRepairMendsAQuarterOverThreeSecondsThenCoolsDown()
        {
            var d = new WingmanDevice(WingmanDevice.Kind.Repair);
            Assert.IsTrue(d.Step(16f, true, out float share));
            Assert.AreEqual(0f, share, "it starts; the mending comes in the frames after");
            float total = 0f;
            for (int i = 0; i < 400 && d.Active; i++) { Assert.IsFalse(d.Step(10f, true, out share)); total += share; }
            Assert.AreEqual(WingmanDevice.RepairShare, total, 1e-4f);
            Assert.IsFalse(d.Ready, "cooling down");
            Assert.IsFalse(d.Step(WingmanDevice.CooldownMs - 1f, true, out _));
            Assert.IsFalse(d.Step(2f, true, out _), "(the last of the cooldown)");
            Assert.IsTrue(d.Ready);
            Assert.IsTrue(d.Step(1f, true, out _), "again after 55 s");
        }

        [Test]
        public void ChargedShotsBoostOnlyWhileTheyRun()
        {
            var d = new WingmanDevice(WingmanDevice.Kind.Charged);
            Assert.AreEqual(1f, d.DamageFactor);
            Assert.IsFalse(d.Step(16f, false, out _), "not attacking: nothing");
            Assert.IsTrue(d.Step(16f, true, out _));
            Assert.AreEqual(1.75f, d.DamageFactor);
            d.Step(WingmanDevice.ChargedMs, true, out _);
            Assert.AreEqual(1f, d.DamageFactor, "5 s later");
            Assert.AreEqual(1f, new WingmanDevice(WingmanDevice.Kind.Repair).DamageFactor);
        }

        [Test]
        public void NoneNeverStarts()
        {
            var d = new WingmanDevice(WingmanDevice.Kind.None);
            Assert.IsFalse(d.Wanted(0.1f, 0, true));
            Assert.IsFalse(d.Step(16f, true, out _));
        }
    }
}
