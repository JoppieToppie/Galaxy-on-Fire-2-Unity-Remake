// ModStockTests.cs
// Feature: CLAUDE.md "Mods (remake-only)" > items.json "stock" (Shop.ModStock, attributes 125 / 126): how many units of the
// item a station's shop holds, in place of getItemBuyList's amount by item type (goods and secondaries 5..19, a good
// multiplied by the distance to its cheapest system, weapons / turrets / equipment 1..3). A number is an exact amount,
// [least, most] a random one between the two (1 to Shop.MaxStock, clamped with a warning); "occurrence" still decides
// whether the shop stocks it at all, and a row with "stock" is left out of computerTradeGoods' re-docking nibble.
// The mod's items are sold at Var Hastra (78) through "alwaysSoldAt" (getItemBuyList's exclusive branch), so every roll
// holds them and the amount alone is under test.

using System.Collections.Generic;
using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Modding;
using NUnit.Framework;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace GoF2Remake.Tests
{
    public class ModStockTests : SavesModsTest
    {
        const string ModId = "t_stock";
        const int Station = 78, Blaster = 12, EnergyCells = 122;   // Energy Cells, a game commodity
        Random.State savedRandom;
        int savedMission;

        [SetUp]
        public void Prepare()
        {
            savedRandom = Random.state;
            Random.InitState(2468);
            savedMission = Session.CampaignMission;
            Session.CampaignMission = 100;   // past the tutorial stock at 78
        }

        [TearDown]
        public void Restore()
        {
            Random.state = savedRandom;
            Session.CampaignMission = savedMission;
        }

        Database ModOn()
        {
            WriteMod("stock", ModId, "1.0", "", ("items.json", $@"[
                    {{ 'id': 'fifty', 'base': {Blaster}, 'name': 'Fifty', 'alwaysSoldAt': {Station}, 'stock': 50 }},
                    {{ 'id': 'range', 'base': {Blaster}, 'name': 'Range', 'alwaysSoldAt': {Station}, 'stock': [10, 25] }},
                    {{ 'id': 'plain', 'base': {Blaster}, 'name': 'Plain', 'alwaysSoldAt': {Station} }},
                    {{ 'override': {EnergyCells}, 'alwaysSoldAt': {Station}, 'stock': 7 }}
                ]"));
            ScanAndEnable(ModId);
            Assert.IsFalse(Installed(ModId).Broken, AllMessages(Installed(ModId)));
            return Database.Load();
        }

        int Item(string id) => ModContent.ItemIndexOf(ModId + ":" + id);

        static int AmountOf(List<ItemStack> list, int item)
        {
            var row = list.Find(s => s.item == item);
            Assert.IsNotNull(row, $"item {item} isn't in the stock: {Describe(list)}");
            return row.amount;
        }

        [Test]
        public void AdvancedEconomyRemainsOptInAndValidationDoesNotPublish()
        {
            Database.Load();
            Assert.IsFalse(ModAdvancedEconomy.Enabled);
            Assert.IsFalse(ModAdvancedEconomy.PersistentInventory);
            WriteMod("economy_no_config", "economy_no_config");
            WriteMod("economy_disabled", "economy_disabled", "1.0", "",
                (ModAdvancedEconomy.File, "{'enabled':false,'persistentInventory':true,'saveInventory':true,'dynamicPricing':true,'physicalFreight':true}"));
            ScanAndEnable("economy_no_config", "economy_disabled");
            var db = Database.Load();
            Assert.IsFalse(ModAdvancedEconomy.Enabled);
            Assert.IsFalse(ModAdvancedEconomy.PersistentInventory);
            Assert.IsFalse(ModAdvancedEconomy.DynamicPricing);
            Assert.IsFalse(ModAdvancedEconomy.PhysicalFreight);
            var mod = Installed("economy_disabled");
            ModAdvancedEconomy.ValidateConfiguration(db, mod, JObject.Parse("{'enabled':true,'persistentInventory':true,'saveInventory':true,'dynamicPricing':true}"));
            Assert.IsFalse(ModAdvancedEconomy.Enabled, "validation must not publish draft settings");
            Assert.Throws<ModJsonException>(() => ModAdvancedEconomy.ValidateConfiguration(db, mod,
                JObject.Parse("{'enabled':true,'stockRules':[{'item':1}]}")));
            Assert.IsFalse(ModAdvancedEconomy.PersistentInventory);
        }

        [Test]
        public void StockWritesTheLeastAndMostAttributes()
        {
            var db = ModOn();
            Assert.IsTrue(Shop.ModStock(db.Item(Item("fifty")), out int min, out int max));
            Assert.AreEqual(50, min);
            Assert.AreEqual(50, max);
            Assert.IsTrue(Shop.ModStock(db.Item(Item("range")), out min, out max));
            Assert.AreEqual(10, min);
            Assert.AreEqual(25, max);
            Assert.AreEqual(10, db.Item(Item("range")).Attr(Shop.StockMinAttr));
            Assert.AreEqual(25, db.Item(Item("range")).Attr(Shop.StockMaxAttr));
            Assert.IsFalse(Shop.ModStock(db.Item(Item("plain")), out _, out _), "no stock field: the amount by item type");
            Assert.IsFalse(Shop.ModStock(db.Item(Blaster), out _, out _), "an untouched game item");
            Assert.IsFalse(Shop.ModStock(null, out _, out _));
        }

        [Test]
        public void AnExactStockIsTheUnitsTheShopHolds()
        {
            var db = ModOn();
            int fifty = Item("fifty");
            for (int r = 0; r < 20; r++)
            {
                var list = Shop.GenerateItems(db, Station);
                Assert.AreEqual(50, AmountOf(list, fifty));
                Assert.AreEqual(7, AmountOf(list, EnergyCells), "an override sets a game item's stock too");
            }
        }

        [Test]
        public void ARangeRollsBetweenTheTwoAndTheRestKeepsTheOriginalRule()
        {
            var db = ModOn();
            int range = Item("range"), plain = Item("plain");
            var seen = new HashSet<int>();
            for (int r = 0; r < 60; r++)
            {
                var list = Shop.GenerateItems(db, Station);
                int n = AmountOf(list, range);
                Assert.That(n, Is.InRange(10, 25));
                seen.Add(n);
                // getItemBuyList: a weapon is (rnd(15) + 5) / 5, so 1 to 3
                Assert.That(AmountOf(list, plain), Is.InRange(1, 3), "without a stock field: the amount by item type");
            }
            Assert.Greater(seen.Count, 1, "a range is rolled per visit");
        }

        [Test]
        public void AStockOutsideTheRangeIsClampedWithAWarning()
        {
            WriteMod("odd", ModId, "1.0", "", ("items.json", $@"[
                    {{ 'id': 'none', 'base': {Blaster}, 'name': 'None', 'alwaysSoldAt': {Station}, 'stock': 0 }},
                    {{ 'id': 'huge', 'base': {Blaster}, 'name': 'Huge', 'alwaysSoldAt': {Station}, 'stock': 999999 }},
                    {{ 'id': 'swapped', 'base': {Blaster}, 'name': 'Swapped', 'alwaysSoldAt': {Station}, 'stock': [25, 10] }}
                ]"));
            ScanAndEnable(ModId);
            var mod = Installed(ModId);
            Assert.IsFalse(mod.Broken, AllMessages(mod));
            Assert.AreEqual(3, mod.Warnings.Count(w => w.Contains("\"stock\"")), AllMessages(mod));
            var db = Database.Load();
            var list = Shop.GenerateItems(db, Station);
            Assert.AreEqual(1, AmountOf(list, Item("none")), "0 units: at least one");
            Assert.AreEqual(Shop.MaxStock, AmountOf(list, Item("huge")));
            Assert.AreEqual(25, AmountOf(list, Item("swapped")), "least first: the most is raised to it");
        }

        [Test]
        public void StockMustBeANumberOrTwo()
        {
            WriteMod("bad", ModId, "1.0", "", ("items.json", $@"[
                    {{ 'id': 'three', 'base': {Blaster}, 'name': 'Three', 'stock': [1, 2, 3] }}
                ]"));
            ScanAndEnable(ModId);
            var mod = Installed(ModId);
            Assert.IsTrue(mod.Broken, "a stock field of three numbers is an error");
            Assert.IsTrue(mod.Errors.Any(e => e.Contains("\"stock\"")), AllMessages(mod));
        }

        [Test]
        public void AnExactStockIsNotNibbledOnReDocking()
        {
            // Generator::computerTradeGoods takes 0..2 units off each row after 30 s away; a mod's own amount stays as it is.
            if (Time.realtimeSinceStartup < 32f) Assert.Ignore("needs a departure time of at least 0 more than 30 s ago");
            var db = ModOn();
            int fifty = Item("fifty");
            var stock = Shop.EnterStation(db, Station);
            stock.items = new List<ItemStack> { new ItemStack(fifty, 50), new ItemStack(Blaster, 10) };
            Session.LastDepartureTime = Time.realtimeSinceStartup - 31f;
            for (int r = 0; r < 20; r++) Assert.AreSame(stock, Shop.EnterStation(db, Station));
            Assert.AreEqual(50, stock.items[0].amount, "the mod's own stock amount");
            Assert.Less(stock.items[1].amount, 10, "20 visits nibbled the ordinary row");
        }
    }
}
