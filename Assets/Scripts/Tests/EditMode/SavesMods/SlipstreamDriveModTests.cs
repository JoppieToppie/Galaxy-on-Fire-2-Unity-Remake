// SlipstreamDriveModTests.cs
// Feature: mod items as cruise drives (Modding/README.md "Cruise drives"; the ES2 importer's drives are these two, this
// mod's a copy of them): they load as cruise drives made from the Khador Drive (its look, its category 18, their own numbers after the originals), with the
// numbers of their stats, and a mounted one sits beside a Khador Drive (Shop.FirstMounted(18) stays the Khador Drive,
// Shop.MountedCruiseDrive finds the Slipstream; Hangar: no swap between the two kinds).

using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.Modding;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public class SlipstreamDriveModTests : SavesModsTest
    {
        const string Mod = "slipstream_drive";
        const string Items = @"[
            { 'id': 'slipstream', 'base': 85, 'name': 'Slipstream', 'techLevel': 7, 'occurrence': 25, 'price': [148000, 162800],
              'stats': { 'cruiseSpeed': 400, 'cruiseRange': 60000, 'cruiseChargeMs': 2000, 'energyCells': 1 } },
            { 'id': 'slipstream_ii', 'base': 85, 'name': 'Khador Slipstream', 'techLevel': 10, 'occurrence': 0, 'price': [1450000, 1595000],
              'stats': { 'cruiseSpeed': 800, 'cruiseRange': 90000, 'cruiseChargeMs': 1500, 'energyCells': 1, 'interstellar': 1 } } ]";
        List<ItemStack> savedEquipment;

        [SetUp]
        public void Prepare() { savedEquipment = Session.Equipment; Session.Equipment = new List<ItemStack>(); }

        [TearDown]
        public void Restore() { Session.Equipment = savedEquipment; }

        Database ModOn()
        {
            WriteMod(Mod, Mod, "1.0", "", ("items.json", Items));
            ScanAndEnable(Mod);
            Assert.IsFalse(Installed(Mod).Broken, AllMessages(Installed(Mod)));
            Assert.IsEmpty(Installed(Mod).Warnings, AllMessages(Installed(Mod)));
            return Database.Load();
        }

        [Test]
        public void TheExampleItemsAreCruiseDrives()
        {
            var db = ModOn();
            int i = ModContent.ItemIndexOf(Mod + ":slipstream"), ii = ModContent.ItemIndexOf(Mod + ":slipstream_ii");
            Assert.GreaterOrEqual(i, OriginalItems);
            Assert.GreaterOrEqual(ii, OriginalItems);
            var it = db.Item(i);
            Assert.IsNotNull(it);
            Assert.AreEqual("Slipstream", it.name);
            Assert.AreEqual("equipment", it.type);
            Assert.AreEqual(18, it.categoryId, "the Khador Drive's category");
            Assert.AreEqual(85, it.Look, "the Khador Drive's icon");
            Assert.IsTrue(Shop.IsCruiseDrive(it));
            Assert.AreEqual(CruiseDrive.Kind, Shop.KindOf(it));
            Assert.AreEqual(400, it.Attr(CruiseDrive.SpeedAttr));
            Assert.AreEqual(60000, it.Attr(CruiseDrive.RangeAttr));
            Assert.AreEqual(2000, it.Attr(CruiseDrive.ChargeAttr));
            Assert.AreEqual(1, it.Attr(CruiseDrive.CellsAttr));
            Assert.AreEqual(7, it.techLevel);
            Assert.AreEqual(25, it.occurrence);
            Assert.AreEqual(148000, it.minPrice);
            Assert.AreEqual(162800, it.maxPrice);
            var two = db.Item(ii);
            Assert.AreEqual(800, two.Attr(CruiseDrive.SpeedAttr));
            Assert.AreEqual(90000, two.Attr(CruiseDrive.RangeAttr));
            Assert.AreEqual(1500, two.Attr(CruiseDrive.ChargeAttr));
            Assert.AreEqual(1, two.Attr(CruiseDrive.InterstellarAttr), "flies between the systems");
            Assert.AreEqual(0, it.Attr(CruiseDrive.InterstellarAttr), "the first model stays in the orbits");
        }

        [Test]
        public void AMountedDriveSitsBesideTheKhadorDrive()
        {
            var db = ModOn();
            int i = ModContent.ItemIndexOf(Mod + ":slipstream");
            Session.Equipment = new List<ItemStack> { new ItemStack(GalaxyMap.KhadorDriveItem, 1), new ItemStack(i, 1) };
            Assert.AreEqual(GalaxyMap.KhadorDriveItem, Shop.FirstMounted(db, 18).index, "the jump drive's kind is the Khador Drive's");
            Assert.AreEqual(i, Shop.MountedCruiseDrive(db).index);
            Assert.IsTrue(GalaxyMap.HasJumpDrive(db));
            Session.Equipment = new List<ItemStack> { new ItemStack(i, 1) };
            Assert.IsNull(Shop.FirstMounted(db, 18), "a cruise drive is no jump drive");
            Assert.IsFalse(Session.Equipment.Exists(e => e.item == GalaxyMap.KhadorDriveItem), "Ship::hasJumpDrive asks for the item itself");
            Assert.IsFalse(Hangar.InBuiltInPlace(db, new ItemStack(i, 1)));
        }

        [Test]
        public void TheRulesComeFromTheItem()
        {
            var db = ModOn();
            var it = Shop.MountedCruiseDrive(db);
            Assert.IsNull(it, "nothing mounted");
            int i = ModContent.ItemIndexOf(Mod + ":slipstream_ii");
            Session.Equipment = new List<ItemStack> { new ItemStack(i, 1) };
            it = Shop.MountedCruiseDrive(db);
            var d = new CruiseDrive(it.index, it.Attr(CruiseDrive.SpeedAttr), it.Attr(CruiseDrive.RangeAttr, 60000),
                                    it.Attr(CruiseDrive.ChargeAttr, 2000), it.Attr(CruiseDrive.CellsAttr, 1), Session.IsExtreme);
            Assert.AreEqual(8f, d.speedFactor);
            Assert.AreEqual(90000, d.legUnits);
            Assert.AreEqual(1500, d.chargeMs);
            Assert.AreEqual(1, d.cellsPerLeg);
        }

        [Test]
        public void OnlyTheMountedSuccessorOpensInterstellarSpace()
        {
            var db = ModOn();
            Assert.IsNull(InterstellarSpace.Drive(db), "nothing mounted: no entry, and no option needed");
            Session.Equipment = new List<ItemStack> { new ItemStack(GalaxyMap.KhadorDriveItem, 1), new ItemStack(ModContent.ItemIndexOf(Mod + ":slipstream"), 1) };
            Assert.IsNull(InterstellarSpace.Drive(db), "neither the Khador Drive nor the first model does");
            int ii = ModContent.ItemIndexOf(Mod + ":slipstream_ii");
            Session.Equipment = new List<ItemStack> { new ItemStack(ii, 1) };
            Assert.AreEqual(ii, InterstellarSpace.Drive(db).index);
            Assert.IsFalse(GalaxyMap.HasJumpDrive(db), "it opens no shortcut around the Khador Drive's story checks");
            Session.Equipment = new List<ItemStack>();
            Assert.IsNull(InterstellarSpace.Drive(db), "in the hold it does nothing");
        }

        [Test]
        public void WarpJsonGivesTheLookTheLastModWins()
        {
            WriteMod("warp_a", "warp_a", "1.0", "", ("warp.json", "{ 'tunnel': 'warp/a.png', 'loop': 'warp/loop.ogg' }"));
            WriteMod("warp_b", "warp_b", "1.0", "", ("warp.json", "{ 'detail': 'warp/d.png' }"));
            Assert.IsNull(ModWarp.Current, "none on");
            ScanAndEnable("warp_a");
            var a = ModWarp.Current;
            Assert.IsNotNull(a);
            Assert.AreEqual("warp/a.png", a.tunnel);
            Assert.AreEqual("warp/loop.ogg", a.loop);
            ScanAndEnable("warp_a", "warp_b");
            var b = ModWarp.Current;
            Assert.AreEqual("warp/d.png", b.detail);
        }
    }
}
