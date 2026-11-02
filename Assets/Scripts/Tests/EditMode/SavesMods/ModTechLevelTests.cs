// ModTechLevelTests.cs
// Feature: CLAUDE.md "Mods (remake-only)" > Tech levels above 10 (Shop.StationStocks / AboveTopTech, ModContent): the
// original's items and stations both end at tech level 10 (getItemBuyList stocks an item where techI <= techS), so a mod's
// tech level 11+ item (up to Shop.MaxItemTech, clamped with a warning past it) is stocked by the tech level 10 stations
// (Anesa 2, Posého 4, ...), never below; a tier-5 pirate outpost's equipment is one time in TrafficPlan.TopTechOdds such an
// item while one is available (its "available" condition), and an enhanced capital ship's gear may be one; ordinary NPC
// cargo keeps the original's rule (no weapon or equipment from tech level 8). The station tech levels come from
// Resources/GoF2Data/stations.json (Anesa 2 = 10, Dis 70 = 7, Var Hastra 78 = 1).

using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Events;
using GoF2Remake.Flight;
using GoF2Remake.Modding;
using NUnit.Framework;
using UnityEngine;

namespace GoF2Remake.Tests
{
    public class ModTechLevelTests : SavesModsTest
    {
        const string ModId = "t_tech";
        const int TopStation = 2, MidStation = 70;
        GameHooks.ConditionCheck savedCondition;
        Random.State savedRandom;
        int savedMission;
        float savedLootReady, savedPlay;

        [SetUp]
        public void Prepare()
        {
            savedCondition = GameHooks.Condition;
            GameHooks.Condition = EventRunner.Condition;
            savedRandom = Random.state;
            Random.InitState(4711);
            savedMission = Session.CampaignMission;
            Session.CampaignMission = 100;
            savedLootReady = Session.CapitalLootReadyAt;
            savedPlay = Session.PlaySeconds;
        }

        [TearDown]
        public void Restore()
        {
            GameHooks.Condition = savedCondition;
            Random.state = savedRandom;
            Session.CampaignMission = savedMission;
            Session.CapitalLootReadyAt = savedLootReady;
            Session.PlaySeconds = savedPlay;
        }

        Database ModOn(string gated = null)
        {
            string avail = gated == null ? "" : $", 'available': '{gated}'";
            WriteMod("tech", ModId, "1.0", "", ("items.json", $@"[
                    {{ 'id': 'aegis', 'base': 50, 'name': 'Aegis XI', 'techLevel': 11, 'occurrence': 100, 'price': [900000, 950000], 'vosskOnly': false{avail} }},
                    {{ 'id': 'apex', 'base': 50, 'name': 'Apex', 'techLevel': 25, 'occurrence': 100, 'price': 1000000, 'vosskOnly': false }}
                ]"));
            ScanAndEnable(ModId);
            Assert.IsFalse(Installed(ModId).Broken, AllMessages(Installed(ModId)));
            return Database.Load();
        }

        int Item(string id) => ModContent.ItemIndexOf(ModId + ":" + id);

        static int TechOf(Database db, int station) => db.Stations.Find(s => s.index == station).techLevel;

        [Test]
        public void TheStockRuleIsTheOriginalsUpToTenAndTheTopStationsAboveIt()
        {
            for (int st = 0; st <= Shop.TopTech; st++)
                for (int it = 0; it <= Shop.TopTech; it++)
                    Assert.AreEqual(it <= st, Shop.StationStocks(st, it), $"station {st}, item {it}: getItemBuyList's techI <= techS");
            Assert.IsTrue(Shop.StationStocks(10, 11));
            Assert.IsTrue(Shop.StationStocks(10, Shop.MaxItemTech));
            Assert.IsFalse(Shop.StationStocks(9, 11));
            var db = Database.Load();
            Assert.LessOrEqual(db.Items.Max(i => i.techLevel), Shop.TopTech, "no original item is above 10");
            Assert.LessOrEqual(db.Stations.Max(s => s.techLevel), Shop.TopTech, "no original station is above 10");
        }

        [Test]
        public void ATechLevelAboveTenIsKeptAndPastTheMaximumClamped()
        {
            var db = ModOn();
            Assert.AreEqual(11, db.Item(Item("aegis")).techLevel);
            Assert.AreEqual(11, db.Item(Item("aegis")).Attr(3));
            Assert.AreEqual(Shop.MaxItemTech, db.Item(Item("apex")).techLevel);
            Assert.IsTrue(Installed(ModId).Warnings.Any(w => w.Contains("\"techLevel\" 25")), AllMessages(Installed(ModId)));
        }

        [Test]
        public void ATopTechStationStocksATechLevel11ItemAndALowerOneNever()
        {
            var db = ModOn();
            Assert.AreEqual(10, TechOf(db, TopStation));
            Assert.Less(TechOf(db, MidStation), 10);
            int aegis = Item("aegis");
            int top = 0;
            for (int r = 0; r < 20; r++)
            {
                if (Shop.GenerateItems(db, TopStation).Any(s => s.item == aegis)) top++;
                Assert.IsFalse(Shop.GenerateItems(db, MidStation).Any(s => s.item == aegis), "a tech level 7 station");
            }
            Assert.AreEqual(20, top, "occurrence 100 at step 100 (k 1.5): always at a tech level 10 station");
        }

        [Test]
        public void AnUnavailableTechLevel11ItemIsNeitherStockedNorLooted()
        {
            var db = ModOn("credits >= 5000000");
            int aegis = Item("aegis");
            Session.Credits = 0;
            Assert.IsFalse(Shop.AboveTopTech(db).Any(i => i.index == aegis));
            for (int r = 0; r < 10; r++) Assert.IsFalse(Shop.GenerateItems(db, TopStation).Any(s => s.item == aegis));
            Session.Credits = 5000000;
            Assert.IsTrue(Shop.AboveTopTech(db).Any(i => i.index == aegis));
        }

        [Test]
        public void ATier5OutpostSometimesGivesTechLevel11Gear()
        {
            var db = ModOn();
            var mods = new[] { Item("aegis"), Item("apex") };
            int found = 0;
            for (int r = 0; r < 300; r++)
            {
                var gear = TrafficPlan.OutpostGear(db, 5, 12);
                Assert.AreEqual(2, gear.Count);
                Assert.AreNotEqual(gear[0], gear[1], "two different pieces");
                found += gear.Count(mods.Contains);
                Assert.IsFalse(TrafficPlan.OutpostGear(db, 4, 12).Any(mods.Contains), "tier 4 keeps the original's tables");
            }
            Assert.That(found, Is.InRange(140, 260), $"about one piece in {TrafficPlan.TopTechOdds} of 600");
        }

        [Test]
        public void WithoutModsTheOutpostGearIsTheOriginalTable()
        {
            var high = new[] { 11, 58, 59, 49, 54, 67, 90 };
            var db = Database.Load();
            Assert.IsEmpty(Shop.AboveTopTech(db));
            for (int r = 0; r < 100; r++)
                CollectionAssert.IsSubsetOf(TrafficPlan.OutpostGear(db, 5, 3), high);
        }

        [Test]
        public void ACapitalShipsGearMayBeTechLevel11ButOrdinaryCargoNever()
        {
            var db = ModOn();
            int aegis = Item("aegis");
            Session.PlaySeconds = 10f;
            Session.CapitalLootReadyAt = 0f;
            bool seen = false;
            for (int r = 0; r < 400 && !seen; r++)
                seen = World.CapitalShips.RollLoot(db, out _).Take(2).Any(s => s.item == aegis);
            Assert.IsTrue(seen, "the tech level 5+ gear pool holds it");
            for (int r = 0; r < 2000; r++)
                Assert.IsFalse(NpcTables.RollLoot(db, true).Any(s => s.item == aegis), "getLootList: no gear from tech level 8");
        }
    }
}
