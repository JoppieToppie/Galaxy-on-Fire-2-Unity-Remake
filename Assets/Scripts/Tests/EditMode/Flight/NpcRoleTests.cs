// NpcRoleTests.cs
// Feature: Modding/README.md "traffic" role, docs/notes/npcs-and-combat.md "Drone roles": Flight.NpcRoles, after EVERSPACE 2's
// drone types (EDroneType; the drone blueprints). Expected values: the role names a mod writes, which roles fire, ES2's
// detonator trigger (20 m) and blast (100 m) at the ES2 ships' x3.6, and the repair and blast sums by hand; the loot drone's
// drop shares by BP_LootDrone's formula (i / n x 1 -+ 1 / n, clamped 0.05..1) worked by hand at the random ends.

using GoF2Remake.Flight;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    public class NpcRoleTests
    {
        [Test]
        public void TheRoleNamesAModWrites()
        {
            Assert.AreEqual(NpcRoles.Role.Sniper, NpcRoles.Parse("sniper"));
            Assert.AreEqual(NpcRoles.Role.Detonator, NpcRoles.Parse("Detonator"));
            Assert.AreEqual(NpcRoles.Role.Webber, NpcRoles.Parse("webber"));
            Assert.AreEqual(NpcRoles.Role.ShieldCharger, NpcRoles.Parse("shieldCharger"));
            Assert.AreEqual(NpcRoles.Role.ArmorLink, NpcRoles.Parse("armourLink"), "either spelling");
            Assert.AreEqual(NpcRoles.Role.AntiMissile, NpcRoles.Parse("antiMissile"));
            Assert.AreEqual(NpcRoles.Role.JumpSuppressor, NpcRoles.Parse(" jumpSuppressor "));
            Assert.AreEqual(NpcRoles.Role.LootDrone, NpcRoles.Parse("lootDrone"));
            Assert.AreEqual(NpcRoles.Role.LootDrone, NpcRoles.Parse("loot"));
            Assert.AreEqual(NpcRoles.Role.None, NpcRoles.Parse(null));
            Assert.AreEqual(NpcRoles.Role.None, NpcRoles.Parse("bomber"));
        }

        [Test]
        public void OnlyTheFightersOfTheRolesFire()
        {
            Assert.IsTrue(NpcRoles.Fires(NpcRoles.Role.None));
            Assert.IsTrue(NpcRoles.Fires(NpcRoles.Role.Sniper));
            Assert.IsTrue(NpcRoles.Fires(NpcRoles.Role.Webber));
            foreach (var r in new[] { NpcRoles.Role.Detonator, NpcRoles.Role.ShieldCharger, NpcRoles.Role.ArmorLink, NpcRoles.Role.AntiMissile, NpcRoles.Role.JumpSuppressor, NpcRoles.Role.LootDrone })
                Assert.IsFalse(NpcRoles.Fires(r), r.ToString());
        }

        [Test]
        public void TheDetonatorsReachAndBlast()
        {
            Assert.AreEqual(72f, NpcRoles.TriggerM, 1e-3f, "ES2's 20 m trigger x 3.6");
            Assert.AreEqual(360f, NpcRoles.BlastM, 1e-3f, "ES2's 100 m blast x 3.6");
            Assert.AreEqual(12f * 100f / 55f, NpcRoles.BlastDamage(12f), 1e-3f, "ES2's 50 + 50 against the raider's 31 + 24 DPS");
        }

        [Test]
        public void TheShieldChargerRepairsAShareOfTheHullASecond()
        {
            Assert.AreEqual(200f * 16f / 310f, NpcRoles.Repair(200, 1000f), 1e-3f, "ES2's 16 shield a second against the raider's 310");
            Assert.AreEqual(50f * 16f / 310f, NpcRoles.Repair(200, 250f), 1e-3f);
            Assert.AreEqual(310f / 410f, NpcRoles.LinkDamageFactor, 1e-5f, "the link's +100 armour against 310");
            Assert.AreEqual(5f / 310f, NpcRoles.LinkRepairShare, 1e-6f);
        }

        [Test]
        public void TheLootDroneDropsAPieceAtEachShareOfItsHull()
        {
            // The random factor at its low end (1 - 1/4 = 0.75): 0.25, 0.5, 0.75, 1 x 0.75.
            var low = NpcRoles.DropThresholds(4, () => 0f);
            CollectionAssert.AreEqual(new[] { 0.1875f, 0.375f, 0.5625f, 0.75f }, low);
            // At its high end (1.25) the last share passes the full hull: clamped to 1, the first hit drops it.
            var high = NpcRoles.DropThresholds(4, () => 1f);
            Assert.AreEqual(0.3125f, high[0], 1e-5f);
            Assert.AreEqual(1f, high[3], 1e-5f);
            // Small shares stay at least 0.05 (ES2's FClamp): 1 / 20 x 0.95.
            Assert.AreEqual(0.05f, NpcRoles.DropThresholds(20, () => 0f)[0], 1e-5f);
            Assert.AreEqual(0, NpcRoles.DropThresholds(0, () => 0.5f).Length);
            Assert.AreEqual(4, NpcRoles.LootMin);
            Assert.AreEqual(7, NpcRoles.LootMax);
            Assert.AreEqual(6000f, NpcRoles.JumpChargeMs, "ES2's OrbitJumpChargeDuration 6 s");
        }
    }
}
