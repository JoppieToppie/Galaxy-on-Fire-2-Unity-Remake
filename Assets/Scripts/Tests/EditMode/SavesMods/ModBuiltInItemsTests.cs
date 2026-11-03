// ModBuiltInItemsTests.cs
// Feature: CLAUDE.md "Mods" > Built-in items: ships.json "integrated": "items" builds any equipment into a ship
// (Database.IntegratedItems). Hangar.SyncBuiltIn puts the flown ship's as built-in stacks first in Session.Equipment: they
// work like mounted items but take no slot (MountedOfType, FitToSlots) and can't be demounted; a fitted item of a built-in
// one's one-per-ship kind takes its place, no slot, the built-in one stepping aside until it is demounted (like the Specter's
// cloak bay); they never go to the hold, the club or another ship (SwitchTo); a secondary starts with its "amount" and keeps
// what is left; they are saved with their flag.

using System.Collections.Generic;
using System.Linq;
using GoF2Remake.Data;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    public class ModBuiltInItemsTests : SavesModsTest
    {
        const string ModId = "t_builtin";
        const int Phantom = 10, Laser = 2, Missiles = 36, Booster = 73, Shield = 54, Commodity = 164;
        int savedShip, savedSecondary;
        long savedCredits;
        List<ItemStack> savedEquipment, savedCargo;
        List<int> savedMods;

        [SetUp]
        public void Prepare()
        {
            savedShip = Session.ShipIndex;
            savedCredits = Session.Credits;
            savedSecondary = Session.SelectedSecondary;
            savedEquipment = Session.Equipment;
            savedCargo = Session.Cargo;
            savedMods = Session.ShipMods;
            Session.ShipIndex = Phantom;
            Session.Equipment = new List<ItemStack>();
            Session.Cargo = new List<ItemStack>();
            Session.ShipMods = new List<int>();
        }

        [TearDown]
        public void Restore()
        {
            Session.ShipIndex = savedShip;
            Session.Credits = savedCredits;
            Session.SelectedSecondary = savedSecondary;
            Session.Equipment = savedEquipment;
            Session.Cargo = savedCargo;
            Session.ShipMods = savedMods;
        }

        Database ModOn()
        {
            WriteMod("builtin", ModId, "1.0", "", ("ships.json", $@"[ {{ 'override': {Phantom}, 'integrated': {{ 'items':
                [ {Laser}, {{ 'item': {Missiles}, 'amount': 5 }}, {Booster}, {Commodity} ] }} }} ]"));
            ScanAndEnable(ModId);
            Assert.IsFalse(Installed(ModId).Broken, AllMessages(Installed(ModId)));
            return Database.Load();
        }

        Hangar At(Database db, int station, params int[] ships)
        {
            var stock = new StationStock { station = station };
            stock.ships.AddRange(ships);
            return new Hangar(db, stock);
        }

        [Test]
        public void TheShipsItemsAreReadAndACommodityIsRefused()
        {
            var db = ModOn();
            var list = db.BuiltInItems(Phantom);
            CollectionAssert.AreEqual(new[] { Laser, Missiles, Booster }, list.Select(e => e.item).ToArray());
            Assert.AreEqual(5, list[1].amount, "the missiles' starting ammo");
            Assert.AreEqual(1, list[2].amount);
            Assert.IsTrue(Installed(ModId).Warnings.Any(w => w.Contains("no equipment")), AllMessages(Installed(ModId)));
            Assert.IsEmpty(db.BuiltInItems(9), "other ships have none");
        }

        [Test]
        public void TheyAreMountedButTakeNoSlotAndStay()
        {
            var db = ModOn();
            Session.Equipment.Add(new ItemStack(Shield, 1));
            Assert.IsTrue(Hangar.SyncBuiltIn(db));
            CollectionAssert.AreEqual(new[] { Laser, Missiles, Booster, Shield }, Session.Equipment.Select(e => e.item).ToArray(), "first, then the mounted ones");
            Assert.IsFalse(Hangar.SyncBuiltIn(db), "a second sync changes nothing");

            var h = At(db, 70);
            Assert.AreEqual(0, h.MountedOfType(0).Count, "the built-in laser takes no primary slot");
            Assert.AreEqual(1, h.MountedOfType(3).Count, "only the shield counts among the equipment");
            Assert.AreEqual(3, h.BuiltInStacks().Count);

            h.Demount(0);
            Assert.AreEqual(Laser, Session.Equipment[0].item, "can't be demounted");
            Assert.AreEqual(0, Shop.CargoOf(Laser));

            Assert.IsFalse(h.Swap(2, Laser), "a built-in one isn't swapped out");

            Session.Equipment[1].amount = 2;   // two missiles fired
            Hangar.SyncBuiltIn(db);
            Assert.AreEqual(2, Session.Equipment[1].amount, "what is left stays (no refill by syncing)");

            Assert.AreEqual(0, Hangar.FitToSlots(db), "FitToSlots keeps them");
            Assert.IsTrue(Session.Equipment.Take(3).All(e => e.builtIn));
        }

        [Test]
        public void AFittedOneTakesTheBuiltInOnesPlace()
        {
            var db = ModOn();
            Hangar.SyncBuiltIn(db);
            var h = At(db, 70);
            const int OtherBooster = 72;
            Session.Cargo.Add(new ItemStack(OtherBooster, 1));
            Assert.AreEqual(Hangar.Result.Ok, h.CanMount(OtherBooster, out _), "into the built-in one's place");
            Assert.IsTrue(h.Mount(OtherBooster));
            Assert.IsFalse(Session.Equipment.Any(e => e.item == Booster), "the built-in booster steps aside");
            int fitted = Session.Equipment.FindIndex(e => e.item == OtherBooster);
            Assert.IsTrue(Hangar.InBuiltInPlace(db, Session.Equipment[fitted]));
            Assert.AreEqual(0, h.MountedOfType(3).Count, "no slot taken");
            Assert.IsFalse(Hangar.SyncBuiltIn(db), "it stays aside at the next sync");

            Session.Cargo.Add(new ItemStack(Booster, 1));
            Assert.AreEqual(Hangar.Result.Swap, h.CanMount(Booster, out int swapWith), "a third: swapped with the fitted one");
            Assert.AreEqual(fitted, swapWith);

            h.Demount(fitted);
            Assert.AreEqual(1, Shop.CargoOf(OtherBooster), "demounted to the hold");
            Assert.IsTrue(Session.Equipment.Any(e => e.builtIn && e.item == Booster), "the built-in booster is back");
        }

        [Test]
        public void TheyStayWithTheShipItsTypeWhenAnotherIsBought()
        {
            var db = ModOn();
            Hangar.SyncBuiltIn(db);
            Session.Equipment.Add(new ItemStack(Shield, 1));
            Session.Credits = 10000000;
            var h = At(db, 70, 9);
            Assert.IsTrue(h.BuyShip(9));
            Assert.AreEqual(9, Session.ShipIndex);
            Assert.IsFalse(Session.Equipment.Any(e => e.builtIn), "the Phantom's built-ins stay with it");
            CollectionAssert.Contains(Session.Equipment.Select(e => e.item).ToArray(), Shield, "the player's own gear moves over");
            Assert.AreEqual(0, Shop.CargoOf(Laser) + Shop.CargoOf(Missiles) + Shop.CargoOf(Booster), "none of them in the hold");

            Session.ShipIndex = Phantom;   // back in a Phantom: its own again
            Hangar.SyncBuiltIn(db);
            Assert.AreEqual(3, Session.Equipment.Count(e => e.builtIn));
        }

        [Test]
        public void TheyAreSavedAsBuiltIn()
        {
            var db = ModOn();
            Hangar.SyncBuiltIn(db);
            Assert.IsTrue(SaveGame.Save(1));
            Session.Equipment = new List<ItemStack>();
            Assert.IsTrue(SaveGame.Load(1));
            Assert.AreEqual(3, Session.Equipment.Count(e => e.builtIn));
            Assert.IsFalse(Hangar.SyncBuiltIn(db), "the loaded game has them already");
        }
    }
}
