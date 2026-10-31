// CruiseDriveItemTests.cs
// Feature: docs/notes/navigation.md "Cruise drive", Modding/README.md "Cruise drives": an equipment item with cruiseSpeed
// (attribute 133) above 0 is a cruise drive (Shop.IsCruiseDrive), of its own one-per-ship kind (Shop.KindOf =
// CruiseDrive.Kind, past every category: beside a Khador Drive of the base's category 18, not in its place), the stat
// names cruiseSpeed / cruiseRange / cruiseChargeMs (ItemStats), the item window's category and stat rows (ItemInfo).

using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.Modding;
using GoF2Remake.UI;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public class CruiseDriveItemTests : SandboxTest   // (the sandbox: the player's own mods' blueprint deeds would claim a made-up number)
    {
        static ItemData Item(int category, string type, params (int key, int value)[] attrs)
        {
            var it = new ItemData { index = 85, type = type, categoryId = category, attrKeys = new int[attrs.Length], attrValues = new int[attrs.Length] };
            for (int i = 0; i < attrs.Length; i++) { it.attrKeys[i] = attrs[i].key; it.attrValues[i] = attrs[i].value; }
            return it;
        }

        static ItemData Slipstream() => Item(18, "equipment", (1, 3), (2, 18), (37, 2000), (38, 1), (CruiseDrive.SpeedAttr, 400), (CruiseDrive.RangeAttr, 60000));

        [Test]
        public void TheStatNames()
        {
            Assert.AreEqual(133, ItemStats.IdOf("cruiseSpeed"));
            Assert.AreEqual(134, ItemStats.IdOf("cruiseRange"));
            Assert.AreEqual(37, ItemStats.IdOf("cruiseChargeMs"));
            Assert.AreEqual(CruiseDrive.SpeedAttr, ItemStats.IdOf("cruiseSpeed"));
            Assert.AreEqual(CruiseDrive.RangeAttr, ItemStats.IdOf("cruiseRange"));
            Assert.AreEqual(CruiseDrive.ChargeAttr, ItemStats.IdOf("cruiseChargeMs"));
            Assert.AreEqual(CruiseDrive.CellsAttr, ItemStats.IdOf("energyCells"));
        }

        [Test]
        public void AnEquipmentItemWithCruiseSpeedIsACruiseDrive()
        {
            Assert.IsTrue(Shop.IsCruiseDrive(Slipstream()));
            Assert.IsFalse(Shop.IsCruiseDrive(Item(18, "equipment", (1, 3), (37, 5000), (38, 1))), "the Khador Drive itself");
            Assert.IsFalse(Shop.IsCruiseDrive(Item(0, "primary", (1, 0), (CruiseDrive.SpeedAttr, 400))), "equipment only");
            Assert.IsFalse(Shop.IsCruiseDrive(null));
        }

        [Test]
        public void ItsKindIsItsOwnAndOnePerShip()
        {
            Assert.AreEqual(CruiseDrive.Kind, Shop.KindOf(Slipstream()));
            Assert.AreEqual(18, Shop.KindOf(Item(18, "equipment", (1, 3))), "an original keeps its category");
            Assert.AreEqual(-1, Shop.KindOf(null));
            Assert.IsFalse(Shop.CanInstallMultiple(CruiseDrive.Kind));
            Assert.IsFalse(Shop.CanInstallMultiple(18));
            Assert.IsTrue(Hangar.Replaceable(Slipstream()));
            Assert.Greater(CruiseDrive.Kind, 221 + 43 + 1, "past every item category and its text id");
        }

        [Test]
        public void TheItemWindowNamesItAndShowsItsRows()
        {
            var it = Slipstream();
            Assert.AreEqual("Cruise drive", ItemInfo.Category(it));
            var rows = ItemInfo.ItemStats(it);
            Assert.IsTrue(rows.Exists(r => r.label == "Cruise speed" && r.value == "x 4"), string.Join("; ", rows));
            Assert.IsTrue(rows.Exists(r => r.label == "Range per energy cell leg" && r.value == "3000 m"), string.Join("; ", rows));
        }
    }
}
