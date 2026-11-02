// HangarShipTests.cs
// Feature: CLAUDE.md "Shop" > Ships (trade-in at full price, equipment to the new ship's slots and the rest to the hold, the
// dealer then sells your old ship, its Kaamo mods kept with the row), "Kaamo Club" > 327 Sell / Keep (328), stored hulls
// Use / Sell, "Stored ships keep their equipment" on / off (Settings.KaamoKeepsEquipment), the lounge sellers' ships.
// Expected: Reference/research/shop.md 5 (Ship::adjustPrice, credits += current - new, 304 / 336 / 329) and kaamo_club.md
// 6.3-6.5 (Use: the old hull takes the row, the stored hull's mods; Sell: the adjusted price, mods add nothing; Keep: the
// full price, the old hull stored bare, 328 when that type is stored). Prices by hand: at Dis (70, Terran) the Phantom
// (Terran, 966667) is (int)(966667 x 0.99f) = 957000, the Betty (Midorian table race, 16200) and the H'Soc (Vossk,
// 120000) cost their full price; at the Kaamo Club (108, Midorian Shima) the Betty is 16038.

using System.Collections.Generic;
using System.Linq;
using GoF2Remake.Data;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    public class HangarShipTests : EconomyTest
    {
        StationStock dealer;

        Hangar Dis(params int[] ships)
        {
            dealer = Stock(70, 122, 5);
            dealer.ships.AddRange(ships);
            return new Hangar(Db, dealer);
        }

        int[] Items(List<ItemStack> list) => list.Select(e => e.item).ToArray();

        [Test]
        public void TradeInCreditsTheDifference()
        {
            Session.Credits = 0;
            var h = Dis(9);
            Assert.AreEqual(957000, h.ShipPrice(10));
            Assert.AreEqual(Hangar.Result.Ok, h.CanBuyShip(9, out _));
            Assert.IsTrue(h.BuyShip(9));
            Assert.AreEqual(957000 - 120000, Session.Credits);
            Assert.AreEqual(9, Session.ShipIndex);
            Assert.AreEqual(new[] { 10 }, dealer.ships.ToArray(), "the dealer's row is now the old ship");
        }

        [Test]
        public void ABoughtShipCountsForTheModsConditions()
        {
            // Session.ShipsBought (the conditions' bought(ship)): bought from the dealer, by reference.
            Session.ShipsBought.Clear();
            var h = Dis(9);
            Assert.IsTrue(h.BuyShip(9));
            CollectionAssert.AreEquivalent(new[] { "9" }, Session.ShipsBought);
        }

        [Test]
        public void EquipmentMovesToTheNewSlotsAndTheRestToTheHold()
        {
            // a Betty (1 / 1 / 0 / 3): the second laser and the booster don't fit
            Session.Cargo.Add(new ItemStack(122, 4));
            var h = Dis(0);
            Assert.IsTrue(h.BuyShip(0));
            CollectionAssert.AreEqual(new[] { 2, 36, 54, 59, 82 }, Items(Session.Equipment));
            Assert.AreEqual(6, AmountMounted(36), "the missiles keep their ammo");
            Assert.AreEqual(1, Shop.CargoOf(2));
            Assert.AreEqual(1, Shop.CargoOf(73));
            Assert.AreEqual(4, Shop.CargoOf(122), "the cargo stays with the player");
            Assert.AreEqual(957000 - 16200, Session.Credits);
        }

        [Test]
        public void BuyingAShipNotListedAddsTheOldOne()
        {
            var h = Dis();
            Assert.IsTrue(h.BuyShip(9));
            Assert.AreEqual(new[] { 10 }, dealer.ships.ToArray());
        }

        [Test]
        public void ShipPurchaseRefusals()
        {
            var h = Dis(9, 44);
            Assert.AreEqual(Hangar.Result.SameShip, h.CanBuyShip(10, out _), "329");
            Session.Passengers = 1;
            Assert.AreEqual(Hangar.Result.Passengers, h.CanBuyShip(9, out _), "336");
            Assert.IsFalse(h.BuyShip(9));
            Session.Passengers = 0;
            // the Specter: 12 000 000 (Vossk in the price table: full price at Dis) - 957 000 trade-in
            Session.Credits = 1000;
            Assert.AreEqual(Hangar.Result.NoCredits, h.CanBuyShip(44, out int need));
            Assert.AreEqual(12000000 - 957000 - 1000, need, "203");
            Assert.IsFalse(h.BuyShip(44));
            Assert.AreEqual(10, Session.ShipIndex);
            Assert.AreEqual(1000, Session.Credits);
        }

        [Test]
        public void KaamoModsStayWithTheHull()
        {
            // HangarWindow::OnTouchEnd 0x176d94: the bought row's mods go to the new ship, the old ship's to its new row
            var h = Dis(9);
            dealer.PutMods(9, new List<int> { 1, 1 });
            Session.ShipMods = new List<int> { 2 };
            Assert.IsTrue(h.BuyShip(9));
            Assert.AreEqual(new[] { 1, 1 }, Session.ShipMods.ToArray());
            Assert.AreEqual(new[] { 2 }, dealer.ModsOf(10).ToArray());
            Assert.IsEmpty(dealer.ModsOf(9), "the bought row is gone");
            // and bought back: the Phantom's mod comes with it
            Hangar.NewDocking();
            Session.Credits = 10000000;
            var again = new Hangar(Db, dealer);
            Assert.IsTrue(again.BuyShip(10));
            Assert.AreEqual(new[] { 2 }, Session.ShipMods.ToArray());
            Assert.AreEqual(new[] { 1, 1 }, dealer.ModsOf(9).ToArray());
        }

        // ---- Kaamo Club: 327 Keep ------------------------------------------------------------------------------------

        [Test]
        public void KeepPaysTheFullPriceAndStoresTheOldHull()
        {
            using (new SettingsScope<bool>(() => Settings.KaamoKeepsEquipment, v => Settings.KaamoKeepsEquipment = v))
            {
                Settings.KaamoKeepsEquipment = false;
                Session.KaamoState = 3;
                Session.Credits = 20000;
                Session.ShipMods = new List<int> { 0 };
                var h = Dis(0);
                Assert.AreEqual(Hangar.Result.Ok, h.CanKeepAndBuyShip(0, out _));
                Assert.IsTrue(h.KeepAndBuyShip(0));
                Assert.AreEqual(20000 - 16200, Session.Credits, "the full price, no trade-in");
                Assert.IsEmpty(dealer.ships, "the bought row is removed");
                Assert.AreEqual(1, Session.KaamoShips.Count);
                Assert.AreEqual(10, Session.KaamoShips[0].ship);
                Assert.AreEqual(new[] { 0 }, Session.KaamoShips[0].mods.ToArray());
                Assert.IsEmpty(Session.KaamoShips[0].equipment, "the original: a bare hull");
                CollectionAssert.AreEqual(new[] { 2, 36, 54, 59, 82 }, Items(Session.Equipment), "the items moved over");
            }
        }

        [Test]
        public void KeepWithStoredEquipmentLeavesTheItemsOnTheOldHull()
        {
            using (new SettingsScope<bool>(() => Settings.KaamoKeepsEquipment, v => Settings.KaamoKeepsEquipment = v))
            {
                Settings.KaamoKeepsEquipment = true;
                Session.KaamoState = 3;
                Session.Credits = 20000;
                Session.Unsaleable.Add(82);   // a story item never stays behind
                var h = Dis(0);
                Assert.IsTrue(Hangar.KeepAsksAboutEquipment);
                Assert.IsTrue(h.KeepAndBuyShip(0));
                CollectionAssert.AreEqual(new[] { 2, 2, 36, 54, 59, 73 }, Items(Session.KaamoShips[0].equipment));
                Assert.AreEqual(6, Session.KaamoShips[0].equipment.Find(e => e.item == 36).amount);
                CollectionAssert.AreEqual(new[] { 82 }, Items(Session.Equipment));
            }
        }

        [Test]
        public void KeepCanMoveTheEquipmentAfterAll()
        {
            using (new SettingsScope<bool>(() => Settings.KaamoKeepsEquipment, v => Settings.KaamoKeepsEquipment = v))
            {
                Settings.KaamoKeepsEquipment = true;
                Session.KaamoState = 3;
                Session.Credits = 20000;
                var h = Dis(0);
                Assert.IsTrue(h.KeepAndBuyShip(0, moveEquipment: true));
                Assert.IsEmpty(Session.KaamoShips[0].equipment);
                CollectionAssert.AreEqual(new[] { 2, 36, 54, 59, 82 }, Items(Session.Equipment));
            }
        }

        [Test]
        public void KeepAsksOnlyWithMovableEquipmentAndTheOptionOn()
        {
            using (new SettingsScope<bool>(() => Settings.KaamoKeepsEquipment, v => Settings.KaamoKeepsEquipment = v))
            {
                Settings.KaamoKeepsEquipment = false;
                Assert.IsFalse(Hangar.KeepAsksAboutEquipment);
                Settings.KaamoKeepsEquipment = true;
                Assert.IsTrue(Hangar.KeepAsksAboutEquipment);
                foreach (var e in Session.Equipment) Session.Unsaleable.Add(e.item);
                Assert.IsFalse(Hangar.KeepAsksAboutEquipment, "only story items mounted");
            }
        }

        [Test]
        public void KeepRefusedWhenThatTypeIsStored()
        {
            // 328: the test is on the old ship's type
            Session.KaamoState = 3;
            Session.Credits = 20000;
            KaamoClub.Store(10, 0, null);
            var h = Dis(0);
            Assert.AreEqual(Hangar.Result.AlreadyStored, h.CanKeepAndBuyShip(0, out _));
            Assert.IsFalse(h.KeepAndBuyShip(0));
            Assert.AreEqual(20000, Session.Credits);
            Session.Credits = 100;
            Session.KaamoShips.Clear();
            Assert.AreEqual(Hangar.Result.NoCredits, h.CanKeepAndBuyShip(0, out int need));
            Assert.AreEqual(16100, need);
        }

        // ---- Kaamo Club: stored hulls ---------------------------------------------------------------------------------

        Hangar Club()
        {
            Session.KaamoState = 3;
            return new Hangar(Db, new StationStock { station = KaamoClub.Station, items = Session.KaamoItems });
        }

        [Test]
        public void UseStoredRefusals()
        {
            var h = Club();
            KaamoClub.Store(10, 0, null);
            KaamoClub.Store(0, 0, null);
            Assert.AreEqual(Hangar.Result.NoStock, h.CanUseStored(5));
            Assert.AreEqual(Hangar.Result.NoStock, h.CanUseStored(-1));
            Assert.AreEqual(Hangar.Result.SameShip, h.CanUseStored(0), "329");
            Session.Passengers = 2;
            Assert.AreEqual(Hangar.Result.Passengers, h.CanUseStored(1), "336");
            Session.Passengers = 0;
            Assert.AreEqual(Hangar.Result.Ok, h.CanUseStored(1));
        }

        [Test]
        public void UseStoredSwapsTheHullsInPlaceOriginalRule()
        {
            using (new SettingsScope<bool>(() => Settings.KaamoKeepsEquipment, v => Settings.KaamoKeepsEquipment = v))
            {
                Settings.KaamoKeepsEquipment = false;
                var h = Club();
                Session.ShipMods = new List<int> { 3 };
                Session.KaamoShips.Add(new StoredShip(9, 0, new List<int> { 1 }, new List<ItemStack> { new ItemStack(86, 1) }));
                Session.Credits = 77;
                Assert.IsTrue(h.UseStored(0));
                Assert.AreEqual(9, Session.ShipIndex);
                Assert.AreEqual(new[] { 1 }, Session.ShipMods.ToArray(), "the stored hull's own mods");
                Assert.AreEqual(10, Session.KaamoShips[0].ship, "the old hull takes the row");
                Assert.AreEqual(new[] { 3 }, Session.KaamoShips[0].mods.ToArray());
                Assert.IsEmpty(Session.KaamoShips[0].equipment);
                CollectionAssert.AreEqual(new[] { 2, 2, 36, 54, 59, 82, 73 }, Items(Session.Equipment), "the items moved over (H'Soc 2 / 2 / 0 / 7)");
                Assert.AreEqual(1, Session.KaamoItems.Find(r => r.item == 86).amount, "the stored hull's items go to the storage");
                Assert.AreEqual(77, Session.Credits, "no credit check or charge");
            }
        }

        [Test]
        public void UseStoredKeepsEachHullsEquipment()
        {
            using (new SettingsScope<bool>(() => Settings.KaamoKeepsEquipment, v => Settings.KaamoKeepsEquipment = v))
            {
                Settings.KaamoKeepsEquipment = true;
                var h = Club();
                Session.Unsaleable.Add(82);
                Session.KaamoShips.Add(new StoredShip(9, 0, null, new List<ItemStack> { new ItemStack(86, 1), new ItemStack(37, 12) }));
                Assert.IsTrue(h.UseStored(0));
                CollectionAssert.AreEqual(new[] { 82, 86, 37 }, Items(Session.Equipment), "the story item first, then the stored hull's");
                Assert.AreEqual(12, AmountMounted(37));
                CollectionAssert.AreEqual(new[] { 2, 2, 36, 54, 59, 73 }, Items(Session.KaamoShips[0].equipment));
                Assert.IsEmpty(Session.KaamoItems);
            }
        }

        [Test]
        public void SellStoredPaysTheAdjustedPriceWithoutMods()
        {
            var h = Club();
            Session.Credits = 0;
            Session.KaamoShips.Add(new StoredShip(0, 0, new List<int> { 0, 1, 2 }, new List<ItemStack> { new ItemStack(55, 1) }));
            Assert.AreEqual(16038, h.StoredPrice(0));
            Assert.AreEqual(0, h.StoredPrice(3));
            Assert.IsTrue(h.SellStored(0));
            Assert.AreEqual(16038, Session.Credits);
            Assert.IsEmpty(Session.KaamoShips);
            Assert.AreEqual(1, Session.KaamoItems.Find(r => r.item == 55).amount, "its items stay in the club");
            Assert.IsFalse(h.SellStored(0));
        }

        // ---- lounge sellers (remake, AgentOffer.SellShip) ---------------------------------------------------------------

        [Test]
        public void LoungeSellerTradeInAtTheSellersPrice()
        {
            var h = Dis();
            Session.Credits = 0;
            Assert.AreEqual(Hangar.Result.Ok, h.CanBuyShipFor(9, 500000, out _));
            Assert.IsTrue(h.BuyShipFor(9, 500000));
            Assert.AreEqual(957000 - 500000, Session.Credits);
            Assert.AreEqual(9, Session.ShipIndex);
            Assert.IsEmpty(dealer.ships, "the seller keeps the old hull, no dealer row");
            Assert.AreEqual(Hangar.Result.NoCredits, h.CanBuyShipFor(44, 5000000, out int need));
            Assert.AreEqual(5000000 - 120000 - 457000, need);
        }

        [Test]
        public void LoungeSellerKeepStoresTheOldHull()
        {
            Session.KaamoState = 3;
            Session.Credits = 600000;
            var h = Dis();
            Assert.AreEqual(Hangar.Result.Ok, h.CanKeepAndBuyShipFor(9, 500000, out _));
            Assert.IsTrue(h.KeepAndBuyShipFor(9, 500000, moveEquipment: true));
            Assert.AreEqual(100000, Session.Credits);
            Assert.IsTrue(KaamoClub.HasShip(10));
            Assert.AreEqual(Hangar.Result.SameShip, h.CanKeepAndBuyShipFor(9, 1, out _));
            KaamoClub.Store(9, 0, null);
            Assert.AreEqual(Hangar.Result.AlreadyStored, h.CanKeepAndBuyShipFor(0, 1, out _), "328: the flown type is stored");
        }
    }
}
