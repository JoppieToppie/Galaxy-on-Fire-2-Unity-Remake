// HangarTradeTests.cs
// Feature: CLAUDE.md "Shop" > buying / selling one unit (Item::transaction 0xf4074), Sell all / Buy all limits, the merged
// shop list (Item::mixItems), unknown item numbers left out of the list, unsaleable story items (323), Status::changeCredits,
// the owned Kaamo Club's free transfers.
// Expected: Reference/research/shop.md 3.1-3.3 (buy needs stock and credits, no cargo check; sell pays the same price; a
// sold unit joins the station's stock in index order; credits clamp at 0) and kaamo_club.md 6.1 (storage: no prices).
// Prices: Reference/tools/shop/prices.py at Dis (70): stock [0, 55, 122] -> 421 / 2373 / 1454.

using System.Collections.Generic;
using GoF2Remake.Data;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    public class HangarTradeTests : EconomyTest
    {
        Hangar Dis(params int[] stock) => new Hangar(Db, Stock(70, stock));

        [Test]
        public void BuyOneUnit()
        {
            Session.Credits = 10000;
            var h = Dis(0, 5, 55, 5, 122, 5);
            Assert.AreEqual(Hangar.Result.Ok, h.Buy(122, out int need));
            Assert.AreEqual(0, need);
            Assert.AreEqual(10000 - 1454, Session.Credits);
            Assert.AreEqual(4, h.StockOf(122));
            Assert.AreEqual(1, h.CargoOf(122));
            CollectionAssert.Contains(Session.SeenItems, 122);
        }

        /// <summary>Step 67's mines (Item::makeItem(5, 0), StationStock.freeUnits): the gifted units cost nothing, then the
        /// usual price; a sold unit doesn't become free (#131).</summary>
        [Test]
        public void GiftedUnitsAreFreeThenFullPrice()
        {
            Session.Credits = 10000;
            var h = Dis(122, 5);
            h.Stock.freeUnits.Add(new ItemStack(122, 2));
            int price = h.PriceOf(122);   // a price depends on the item's place in the station's lists
            Assert.Greater(price, 0);
            Assert.AreEqual(0, h.BuyPriceOf(122));
            h.Buy(122, out _);
            h.Buy(122, out _);
            Assert.AreEqual(10000, Session.Credits, "the two gifted units");
            Assert.AreEqual(price, h.BuyPriceOf(122));
            h.Sell(122);
            Assert.AreEqual(10000 + price, Session.Credits, "selling pays the price");
            Assert.AreEqual(0, h.Stock.FreeOf(122), "a sold unit isn't free");
            h.Buy(122, out _);
            Assert.AreEqual(10000, Session.Credits);
        }

        [Test]
        public void BuyingTheLastUnitRemovesTheRow()
        {
            Session.Credits = 10000;
            var h = Dis(55, 1);
            h.Buy(55, out _);
            Assert.IsEmpty(h.Stock.items);
            Assert.AreEqual(Hangar.Result.NoStock, h.Buy(55, out _));
        }

        [Test]
        public void BuyWithoutStockOrCreditsChangesNothing()
        {
            Session.Credits = 1000;
            var h = Dis(0, 5, 55, 5, 122, 5);
            Assert.AreEqual(Hangar.Result.NoStock, h.Buy(109, out _));
            Assert.AreEqual(Hangar.Result.NoCredits, h.Buy(55, out int need));
            Assert.AreEqual(2373 - 1000, need, "203: the missing credits");
            Assert.AreEqual(1000, Session.Credits);
            Assert.AreEqual(5, h.StockOf(55));
            Assert.IsEmpty(Session.Cargo);
        }

        [Test]
        public void ExactCreditsAreEnough()
        {
            var h = Dis(55, 5);
            Session.Credits = h.PriceOf(55);   // a price depends on the item's place in the station's lists
            Assert.AreEqual(Hangar.Result.Ok, h.Buy(55, out _));
            Assert.AreEqual(0, Session.Credits);
        }

        [Test]
        public void BuyingNeverChecksTheHold()
        {
            // shop.md 3.1: no cargo-space check; the launch is refused later (204)
            Session.Credits = 1000000;
            var h = Dis(122, 200);
            for (int i = 0; i < 60; i++) Assert.AreEqual(Hangar.Result.Ok, h.Buy(122, out _));
            Assert.AreEqual(60, h.Load);
            Assert.AreEqual(52, h.MaxLoad);
            Assert.IsTrue(h.Overloaded);
        }

        [Test]
        public void SellPaysTheBuyingPrice()
        {
            Session.Credits = 0;
            Session.Cargo.Add(new ItemStack(122, 3));
            var h = Dis(0, 5, 55, 5, 122, 5);
            Assert.AreEqual(Hangar.Result.Ok, h.Sell(122));
            Assert.AreEqual(1454, Session.Credits);
            Assert.AreEqual(6, h.StockOf(122));
            Assert.AreEqual(2, h.CargoOf(122));
            Session.Credits = 5000;
            h.Buy(122, out _);
            Assert.AreEqual(5000 - 1454, Session.Credits, "buy and sell at one price: no profit from a round trip");
        }

        [Test]
        public void SoldItemsJoinTheStockInIndexOrder()
        {
            Session.Cargo.Add(new ItemStack(109, 2));
            Session.Cargo.Add(new ItemStack(200, 1));
            Session.Cargo.Add(new ItemStack(1, 1));
            var h = Dis(0, 5, 55, 5, 122, 5);
            h.Sell(109);
            h.Sell(200);
            h.Sell(1);
            CollectionAssert.AreEqual(new[] { 0, 1, 55, 109, 122, 200 }, h.Stock.items.ConvertAll(r => r.item));
            Assert.AreEqual(1, h.StockOf(109));
        }

        [Test]
        public void SellingTheLastUnitEmptiesTheStack()
        {
            Session.Cargo.Add(new ItemStack(122, 1));
            var h = Dis(122, 5);
            h.Sell(122);
            Assert.IsEmpty(Session.Cargo);
            Assert.AreEqual(Hangar.Result.NothingToSell, h.Sell(122));
        }

        [Test]
        public void StoryItemsCantBeSold()
        {
            // shop.md 3.1: unsaleable items -> 323
            Session.Cargo.Add(new ItemStack(90, 1));
            Session.Unsaleable.Add(90);
            var h = Dis(122, 5);
            Assert.IsFalse(Hangar.IsSaleable(90));
            Assert.AreEqual(Hangar.Result.NotSaleable, h.Sell(90));
            Assert.AreEqual(1, h.CargoOf(90));
            Assert.AreEqual(0, Session.Credits);
            Assert.AreEqual(Hangar.Result.NothingToSell, h.Sell(91), "nothing of it in the hold comes first");
        }

        [Test]
        public void TutorialGearTradesForNothing()
        {
            // Var Hastra before step 7: the tutorial gear costs 0 and sells for 0
            Session.CampaignMission = 5;
            Session.Credits = 0;
            var h = new Hangar(Db, Stock(78, 0, 1, 22, 1, 55, 1));
            Assert.AreEqual(Hangar.Result.Ok, h.Buy(0, out _), "free tutorial gear");
            Assert.AreEqual(0, Session.Credits);
            Assert.AreEqual(Hangar.Result.Ok, h.Sell(0));
            Assert.AreEqual(0, Session.Credits);
        }

        [Test]
        public void SellAllAndBuyAllRespectHoldAndCredits()
        {
            // the remake's Buy all: units one by one while the hold has room and the credits reach (HangarWindow.TradeAll);
            // the hangar side only refuses on stock / credits, so the limit is on the caller: here the credits run out first
            var h = Dis(122, 50);
            int price = h.PriceOf(122);   // a price depends on the item's place in the station's lists
            Session.Credits = price * 3 + 100;
            int bought = 0;
            while (h.Buy(122, out _) == Hangar.Result.Ok) bought++;
            Assert.AreEqual(3, bought);
            Assert.AreEqual(100, Session.Credits);
            int sold = 0;
            h.BeginBatch();
            while (h.Sell(122) == Hangar.Result.Ok) sold++;
            h.EndBatch();
            Assert.AreEqual(3, sold);
            Assert.AreEqual(100 + 3 * price, Session.Credits);
            Assert.AreEqual(50, h.StockOf(122));
        }

        [Test]
        public void ShopListMergesHoldAndStockByIndex()
        {
            Session.Cargo.Add(new ItemStack(200, 1));
            Session.Cargo.Add(new ItemStack(55, 2));
            Session.Cargo.Add(new ItemStack(109, 0));
            var h = Dis(0, 5, 55, 5, 122, 0, 164, 3);
            CollectionAssert.AreEqual(new[] { 0, 55, 164, 200 }, h.ShopItems(), "empty rows left out, duplicates merged, index order");
        }

        [Test]
        public void ShopListLeavesOutUnknownItemNumbers()
        {
            var stock = Stock(70, 0, 5, 55, 5);
            stock.items.Add(new ItemStack(99999, 4));
            Session.Cargo.Add(new ItemStack(88888, 1));
            var h = new Hangar(Db, stock);
            CollectionAssert.AreEqual(new[] { 0, 55 }, h.ShopItems());
        }

        [Test]
        public void SpecialtyTradesCountForTheBarkeeperMedal()
        {
            Session.Credits = 100000;
            var h = Dis(140, 5);
            h.Buy(140, out _);
            CollectionAssert.Contains(Session.BoozeTypes, 140);
        }

        [Test]
        public void KaamoStorageTransfersAreFree()
        {
            // kaamo_club.md 6.1: at the owned club nothing costs money and nothing pays
            Session.KaamoState = 3;
            Session.Credits = 500;
            Session.Cargo.Add(new ItemStack(122, 4));
            var stock = new StationStock { station = KaamoClub.Station, items = Session.KaamoItems };
            Session.KaamoItems.Add(new ItemStack(54, 1));
            var h = new Hangar(Db, stock);
            Assert.IsTrue(h.Storage);
            Assert.AreEqual(Hangar.Result.Ok, h.Buy(54, out _));
            Assert.AreEqual(Hangar.Result.Ok, h.Sell(122));
            Assert.AreEqual(500, Session.Credits);
            Assert.AreEqual(1, Shop.CargoOf(54));
            Assert.AreEqual(1, Session.KaamoItems.Find(r => r.item == 122).amount);
            Assert.IsNull(Session.KaamoItems.Find(r => r.item == 54));
        }

        // A blueprint at the owned club takes its ingredients from the hold, then from the storage; elsewhere the hold only.
        [Test]
        public void ABlueprintAtTheOwnedClubDrawsOnTheStorageToo()
        {
            Session.KaamoState = 3;
            Session.Cargo.Add(new ItemStack(122, 1));
            Session.KaamoItems.Add(new ItemStack(122, 2));
            var h = new Hangar(Db, new StationStock { station = KaamoClub.Station, items = Session.KaamoItems });
            Assert.AreEqual(3, h.BlueprintSupply(122));
            Assert.IsTrue(h.TakeForBlueprint(122));
            Assert.AreEqual(0, Shop.CargoOf(122), "the hold first");
            Assert.AreEqual(2, h.StockOf(122));
            Assert.IsTrue(h.TakeForBlueprint(122));
            Assert.IsTrue(h.TakeForBlueprint(122));
            Assert.IsNull(Session.KaamoItems.Find(r => r.item == 122), "an emptied storage row goes");
            Assert.IsFalse(h.TakeForBlueprint(122));

            // Handed back: what the hold has room for, the rest into the storage.
            int room = h.MaxLoad - h.Load;
            h.ReturnFromBlueprint(122, room + 5);
            Assert.AreEqual(room, Shop.CargoOf(122));
            Assert.AreEqual(5, h.StockOf(122));
            Assert.AreEqual(h.MaxLoad, h.Load);

            // A station's shop is not the player's: its stock is never taken.
            var shop = Dis(122, 9);
            Assert.AreEqual(room, shop.BlueprintSupply(122));
            Session.Cargo.Clear();
            Assert.IsFalse(shop.TakeForBlueprint(122));
            Assert.AreEqual(9, shop.StockOf(122));
            shop.ReturnFromBlueprint(122, 2);
            Assert.AreEqual(2, Shop.CargoOf(122));
        }

        [Test]
        public void KaamoClubIsAShopUntilOwned()
        {
            Session.KaamoState = 2;
            var h = new Hangar(Db, Stock(KaamoClub.Station, 122, 5));
            Assert.IsFalse(h.Storage);
            Assert.AreEqual(1426, h.PriceOf(122), "prices.py price_list(108, [122])");
        }

        [Test]
        public void FreeShoppingCheatBuysForNothing()
        {
            using (new SettingsScope<bool>(() => Cheats.FreeShopping, v => Cheats.FreeShopping = v))
            {
                Cheats.FreeShopping = true;
                Session.Credits = 0;
                var h = Dis(54, 1);
                Assert.AreEqual(Hangar.Result.Ok, h.Buy(54, out _));
                Assert.AreEqual(0, Session.Credits);
            }
        }
    }
}
