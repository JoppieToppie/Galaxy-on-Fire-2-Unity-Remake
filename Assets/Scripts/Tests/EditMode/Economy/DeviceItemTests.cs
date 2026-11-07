// DeviceItemTests.cs
// Feature: docs/notes/combat-equipment.md "Devices", Modding/README.md "Devices": an equipment item with device (attribute
// 150) 1..7 is an active device (Shop.IsDevice) of its own one-per-ship kind per type (Shop.KindOf = Device.KindBase +
// type), whatever its base (a seatless cabin); the stat names (ItemStats); the item window's category and rows (ItemInfo);
// a cloak's own cooldown (cloakCooldownMs, attribute 160).

using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.Modding;
using GoF2Remake.UI;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public class DeviceItemTests : SandboxTest
    {
        static ItemData Item(int category, string type, params (int key, int value)[] attrs)
        {
            var it = new ItemData { index = 91, type = type, categoryId = category, attrKeys = new int[attrs.Length], attrValues = new int[attrs.Length] };
            for (int i = 0; i < attrs.Length; i++) { it.attrKeys[i] = attrs[i].key; it.attrValues[i] = attrs[i].value; }
            return it;
        }

        /// <summary>The importer's Berger Area EMP: a seatless cabin (91, category 20) with the device numbers.</summary>
        static ItemData AreaEmp() => Item(20, "equipment", (1, 3), (2, 20), (34, 0), (Device.TypeAttr, 1), (Device.CooldownAttr, 60000),
                                          (Device.DurationAttr, 8000), (Device.RadiusAttr, 9120), (Device.GrowAttr, 2500), (Device.SlowAttr, 25));

        [Test]
        public void TheStatNames()
        {
            Assert.AreEqual(Device.TypeAttr, ItemStats.IdOf("device"));
            Assert.AreEqual(Device.CooldownAttr, ItemStats.IdOf("deviceCooldownMs"));
            Assert.AreEqual(Device.DurationAttr, ItemStats.IdOf("deviceDurationMs"));
            Assert.AreEqual(Device.RadiusAttr, ItemStats.IdOf("deviceRadius"));
            Assert.AreEqual(Device.GrowAttr, ItemStats.IdOf("deviceGrowMs"));
            Assert.AreEqual(Device.SlowAttr, ItemStats.IdOf("deviceSlowPercent"));
            Assert.AreEqual(Device.ShieldCostAttr, ItemStats.IdOf("deviceShieldCost"));
            Assert.AreEqual(Device.DamagePercentAttr, ItemStats.IdOf("deviceDamagePercent"));
            Assert.AreEqual(Device.ChargeAttr, ItemStats.IdOf("deviceCharge"));
            Assert.AreEqual(Device.ConeAttr, ItemStats.IdOf("deviceConeDeg"));
            Assert.AreEqual(Cloak.CooldownAttr, ItemStats.IdOf("cloakCooldownMs"));
            Assert.AreEqual(Gun.ShieldBypassAttr, ItemStats.IdOf("shieldBypass"));
            Assert.AreEqual(Gun.SlowMsAttr, ItemStats.IdOf("slowMs"));
            Assert.AreEqual(Gun.SlowPercentAttr, ItemStats.IdOf("slowPercent"));
        }

        [Test]
        public void AnEquipmentItemWithADeviceTypeIsADevice()
        {
            Assert.IsTrue(Shop.IsDevice(AreaEmp()));
            Assert.AreEqual(Device.Type.AreaEmp, Shop.DeviceType(AreaEmp()));
            Assert.IsFalse(Shop.IsDevice(Item(20, "equipment", (1, 3), (34, 2))), "a cabin");
            Assert.IsFalse(Shop.IsDevice(Item(20, "equipment", (1, 3), (Device.TypeAttr, 99))), "an unknown type");
            Assert.IsFalse(Shop.IsDevice(Item(0, "primary", (1, 0), (Device.TypeAttr, 1))), "equipment only");
            Assert.IsFalse(Shop.IsDevice(null));
        }

        [Test]
        public void EachTypeIsItsOwnKindAndOnePerShip()
        {
            Assert.AreEqual(Device.KindBase + 1, Shop.KindOf(AreaEmp()));
            Assert.AreEqual(Device.KindBase + 7, Shop.KindOf(Item(20, "equipment", (1, 3), (Device.TypeAttr, 7))));
            Assert.AreEqual(20, Shop.KindOf(Item(20, "equipment", (1, 3), (34, 2))), "a cabin stays a cabin");
            Assert.IsFalse(Shop.CanInstallMultiple(Device.KindBase + 1));
            Assert.IsTrue(Shop.CanInstallMultiple(20), "cabins still can");
            Assert.Greater(Device.KindBase, CruiseDrive.Kind);
        }

        [Test]
        public void TheItemWindowNamesItAndShowsItsRows()
        {
            var it = AreaEmp();
            Assert.AreEqual("Device", ItemInfo.Category(it));
            var rows = ItemInfo.ItemStats(it);
            string all = string.Join("; ", rows);
            Assert.IsTrue(rows.Exists(r => r.label == "Cooldown" && r.value == "60 s"), all);
            Assert.IsTrue(rows.Exists(r => r.label == "Duration" && r.value == "8 s"), all);
            Assert.IsTrue(rows.Exists(r => r.value == "456 m"), all);
            Assert.IsTrue(rows.Exists(r => r.label == "Enemy speed" && r.value == "25 %"), all);
            Assert.IsFalse(rows.Exists(r => r.value == "0" ), "no seat count for the seatless cabin: " + all);
        }

        [Test]
        public void ACloakMayHaveItsOwnCooldown()
        {
            Assert.AreEqual(60000f, new Cloak(94, 10000, 2000, 1, 1f, 60000).cooldownMs);
            Assert.AreEqual(9000f, new Cloak(94, 10000, 2000, 1, 1f).cooldownMs, "the difficulty's without one");
            Assert.AreEqual(9000f, new Cloak(94, 10000, 2000, 1, 1f, 0).cooldownMs);
        }
    }
}
