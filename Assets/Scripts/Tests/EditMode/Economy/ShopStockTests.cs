// ShopStockTests.cs
// Feature: CLAUDE.md "Shop" > Stock (Generator::getItemBuyList 0xa05c4) and dealer ships (getShipBuyList 0xa0eb8), "Dealer
// extras", the 3-station stock memory (Status::departStation / addStationToStack) and the remake's free-play extras.
// Expected rules: Reference/research/shop.md 4.2-4.4 (the original's filters, amounts and special stations) and CLAUDE.md
// "Dealer extras". The stock is time-seeded (UnityEngine.Random), so each rule is checked as an invariant over many rolls
// (the generator is seeded with fixed seeds here so a failure repeats).

using System.Collections.Generic;
using System.Linq;
using GoF2Remake.Data;
using NUnit.Framework;
using UnityEngine;

namespace GoF2Remake.Tests
{
    public class ShopStockTests : EconomyTest
    {
        const int Rolls = 40;
        Random.State savedRandom;

        [SetUp]
        public void SeedRandom()
        {
            savedRandom = Random.state;
            Random.InitState(12345);
        }

        [TearDown]
        public void RestoreRandom() => Random.state = savedRandom;

        List<ItemStack> Items(int station) => Shop.GenerateItems(Db, station);
        List<int> Ships(int station) => Shop.GenerateShips(Db, station);

        bool Exclusive(ItemData it, int station) =>
            it.Attr(61, -1) == station && !((it.index == 196 || (it.index >= 198 && it.index <= 200)) && Session.CampaignMission < 142);

        // ---- items ---------------------------------------------------------------------------------------------

        [Test]
        public void TutorialStockAtVarHastra()
        {
            // shop.md 4.3: station 78 before mission 7 -> exactly items 0, 22, 55, one each
            Session.CampaignMission = 3;
            for (int i = 0; i < 5; i++)
            {
                var list = Items(78);
                Assert.AreEqual(new[] { 0, 22, 55 }, list.Select(s => s.item).ToArray());
                Assert.IsTrue(list.All(s => s.amount == 1));
            }
        }

        [Test]
        public void KaamoClubAndTheSupernovaSystemHaveNoStock()
        {
            Session.CampaignMission = 100;
            for (int i = 0; i < 5; i++)
            {
                Assert.IsEmpty(Items(108));
                Assert.IsEmpty(Items(109), "Ginoya before step 158 (Status::inSupernovaSystem)");
                Assert.IsEmpty(Ships(108));
                Assert.IsEmpty(Ships(109));
            }
        }

        [Test]
        public void StockFollowsTheTechAndProductRules()
        {
            Session.CampaignMission = 100;
            foreach (int station in new[] { 0, 15, 41, 52, 70, 86, 100, 120, 126, 134 })
            {
                var st = Db.Stations.Find(s => s.index == station);
                int system = st.system, race = Shop.RaceOfSystem(Db, system);
                for (int r = 0; r < Rolls; r++)
                {
                    var list = Items(station);
                    for (int n = 0; n < list.Count; n++)
                    {
                        var it = Db.Item(list[n].item);
                        Assert.IsNotNull(it);
                        if (n > 0) Assert.Less(list[n - 1].item, list[n].item, "item index order, no duplicates");
                        if (Exclusive(it, station) || (station == 126 && it.index == 209)) continue;
                        bool specialty = it.index >= 132 && it.index <= 153;
                        Assert.IsEmpty(it.blueprint, $"blueprint product {it.index} at {station}");
                        CollectionAssert.DoesNotContain(new[] { 217, 218, 164, 175 }, it.index);
                        Assert.Greater(it.maxPrice, 0);
                        Assert.IsTrue(it.techLevel <= st.techLevel || specialty, $"item {it.index} tech {it.techLevel} at tech {st.techLevel}");
                        if (it.Attr(60) == 1) Assert.AreEqual(1, race, $"Vossk-only {it.index} outside a Vossk system");
                        if (specialty) Assert.AreEqual(132 + system, it.index, "one specialty per system");
                    }
                }
            }
        }

        [Test]
        public void StockAmountsByType()
        {
            // shop.md 4.3 step 7: r = 5..19; secondaries r, weapons / turrets / equipment r / 5 (1..3), Buskat max(1, r / 2)
            Session.CampaignMission = 100;
            foreach (int station in new[] { 15, 41, 70, 86, 134 })
                for (int r = 0; r < Rolls; r++)
                    foreach (var row in Items(station))
                    {
                        var it = Db.Item(row.item);
                        Assert.Greater(row.amount, 0);
                        if (row.item == 109) Assert.That(row.amount, Is.InRange(2, 9));
                        else if (it.TypeId == 1) Assert.That(row.amount, Is.InRange(5, 19), "secondary " + row.item);
                        else if (it.TypeId <= 3) Assert.That(row.amount, Is.InRange(1, 3), "item " + row.item);
                        else if (row.item == 110) Assert.That(row.amount, Is.InRange(5, 19));
                        else Assert.GreaterOrEqual(row.amount, 5, "commodity " + row.item);
                    }
        }

        [Test]
        public void HardcoreStocksNoOreOrCores()
        {
            Session.Difficulty = Session.DifficultyExtreme;
            Session.CampaignMission = 100;
            foreach (int station in new[] { 0, 15, 70, 86, 100 })
                for (int r = 0; r < Rolls; r++)
                    foreach (var row in Items(station))
                        CollectionAssert.DoesNotContain(new[] { 23, 24 }, Db.Item(row.item).categoryId, $"item {row.item} at {station}");
        }

        [Test]
        public void SpecialStationsStockTheirKindOnly()
        {
            // shop.md 4.3 step 5: Quineros (107) equipment only, Var Destro (105) weapons or weapon mods (sort 28),
            // Valkyrie (101) weapons only, Sao Perula (106) commodities only
            Session.CampaignMission = 100;
            for (int r = 0; r < Rolls; r++)
            {
                foreach (var row in Items(107)) Assert.AreEqual(3, Db.Item(row.item).TypeId, "107: " + row.item);
                foreach (var row in Items(105)) { var it = Db.Item(row.item); Assert.IsTrue(it.TypeId <= 2 || it.categoryId == 28, "105: " + row.item); }
                foreach (var row in Items(101)) Assert.LessOrEqual(Db.Item(row.item).TypeId, 2, "101: " + row.item);
                foreach (var row in Items(106)) Assert.AreEqual(4, Db.Item(row.item).TypeId, "106: " + row.item);
            }
        }

        [Test]
        public void AModItemsAlwaysSoldAtStationStocksItWhateverItsKind()
        {
            // Remake mods: "alwaysSoldAt" beats the special stations' one-kind filters (a mod's shields and cloaks at Var
            // Destro were never sold); the original's own attr-61 items keep them.
            Session.CampaignMission = 100;
            var db = Database.Load();   // a copy to change: Db is the shared read-only one
            var shield = db.Item(50);   // Targe Shield: equipment, not sold at Var Destro (105) or Valkyrie (101)
            var goods = db.Item(22);    // a commodity: not sold at Quineros (107)
            foreach (var (it, station) in new[] { (shield, 105), (shield, 101), (goods, 107), (shield, 106) })
            {
                it.modded = true;
                int at = System.Array.IndexOf(it.attrKeys, 61);
                if (at >= 0) it.attrValues[at] = station;
                else { it.attrKeys = it.attrKeys.Append(61).ToArray(); it.attrValues = it.attrValues.Append(station).ToArray(); }
                for (int r = 0; r < 5; r++)
                    Assert.IsTrue(Shop.GenerateItems(db, station).Exists(s => s.item == it.index), $"item {it.index} at {station}");
                it.modded = false;
                for (int r = 0; r < 5; r++)
                    Assert.IsFalse(Shop.GenerateItems(db, station).Exists(s => s.item == it.index), $"original item {it.index} at {station}");
            }
        }

        [Test]
        public void SaoPerulaSellsOnlyItsGoodsAndTheSpecialties()
        {
            Session.CampaignMission = 100;
            var allowed = new HashSet<int> { 101, 102, 103, 107, 108, 109, 114, 124 };
            for (int i = 132; i <= 153; i++) allowed.Add(i);
            for (int r = 0; r < Rolls; r++)
                foreach (var row in Items(106)) Assert.IsTrue(allowed.Contains(row.item), "106: " + row.item);
        }

        [Test]
        public void ExclusiveItemsAreAlwaysStockedAtTheirHomeStation()
        {
            // shop.md 4.3: attr 61 = the item's home station; exclusive items skip every chance roll (Nirai lasers at 52)
            Session.CampaignMission = 100;
            var home = Db.Items.Where(it => it.Attr(61, -1) == 52 && it.TypeId != 4).Select(it => it.index).ToList();
            Assert.IsNotEmpty(home);
            for (int r = 0; r < 10; r++)
            {
                var list = Items(52).Select(s => s.item).ToList();
                foreach (int item in home) CollectionAssert.Contains(list, item, "exclusive " + item);
            }
        }

        [Test]
        public void FreePlayVarHastraAlwaysHasADrillAndEnergyCells()
        {
            // remake-only (CLAUDE.md "Mining" / "Star map"): free play's starting station always stocks a drill and cells
            Session.FreePlay = true;
            Session.CampaignMission = 20;
            for (int r = 0; r < Rolls; r++)
            {
                var list = Items(78);
                Assert.IsTrue(list.Any(s => Db.Item(s.item).categoryId == 19), "a drill");
                Assert.IsTrue(list.Any(s => s.item == GalaxyMap.EnergyCellItem && s.amount >= 5), "energy cells");
            }
        }

        [Test]
        public void CompletedWorldStartStationSellsTheKhadorDrive()
        {
            Session.CompletedWorld = true;
            Session.FreePlay = true;
            Session.CampaignMission = 162;
            for (int r = 0; r < Rolls; r++)
            {
                var row = Items(NetSession.StartStation).Find(s => s.item == 85);
                Assert.IsNotNull(row);
                Assert.That(row.amount, Is.InRange(2, 4));
            }
            Session.CompletedWorld = false;
            for (int r = 0; r < Rolls; r++) Assert.IsFalse(Items(NetSession.StartStation).Exists(s => s.item == 85), "the drive is a blueprint product");
        }

        [Test]
        public void StoryExtrasInTheStock()
        {
            // shop.md 4.3: mission 139 in system 25 -> item 190 prepended; station 126 -> item 209 x 1-10 (1 in mission 117)
            Session.CampaignMission = 139;
            for (int r = 0; r < 10; r++) Assert.AreEqual(190, Items(105)[0].item);
            Session.CampaignMission = 117;
            for (int r = 0; r < 10; r++) Assert.AreEqual(1, Items(126).Find(s => s.item == 209).amount);
            Session.CampaignMission = 120;
            for (int r = 0; r < Rolls; r++) Assert.That(Items(126).Find(s => s.item == 209).amount, Is.InRange(1, 10));
        }

        // ---- dealer ships ----------------------------------------------------------------------------------------

        [Test]
        public void NoDealerAtValkyrieKaamoOrMidoBeforeStep16()
        {
            Session.CampaignMission = 15;
            for (int r = 0; r < Rolls; r++)
            {
                Assert.IsEmpty(Ships(101));
                Assert.IsEmpty(Ships(108));
                Assert.IsEmpty(Ships(78), "Mido before mission 16");
            }
        }

        [Test]
        public void FirstDealerSlotIsTheStationsStarterShip()
        {
            // getShipBuyList: station 41's first slot = Phantom (n is at least 1 there), station 78's = Betty (when n > 0)
            Session.CampaignMission = 30;
            bool sawBetty = false;
            for (int r = 0; r < Rolls; r++)
            {
                var at41 = Ships(41);
                Assert.IsNotEmpty(at41);
                Assert.AreEqual(10, at41[0]);
                var at78 = Ships(78);
                if (at78.Count > 0) { Assert.AreEqual(0, at78[0]); sawBetty = true; }
            }
            Assert.IsTrue(sawBetty);
        }

        [Test]
        public void DealerListsHaveNoDuplicatesOrUnsellableHulls()
        {
            Session.CampaignMission = 100;
            foreach (int station in new[] { 0, 15, 41, 70, 86, 120, 126, 134 })
                for (int r = 0; r < Rolls; r++)
                {
                    var ships = Ships(station);
                    Assert.AreEqual(ships.Count, ships.Distinct().Count(), "station " + station);
                    foreach (int s in ships) CollectionAssert.DoesNotContain(new[] { 13, 14, 15 }, s);
                }
        }

        [Test]
        public void KotharSellsTheJumpDriveShipsAfterTheValkyrie()
        {
            Session.CampaignMission = 84;
            for (int r = 0; r < 5; r++) Assert.AreEqual(new[] { 37, 38, 40 }, Ships(100).ToArray());
        }

        [Test]
        public void QuinerosIsThePirateYard()
        {
            Session.CampaignMission = 100;
            for (int r = 0; r < 5; r++) Assert.AreEqual(new[] { 2, 11, 23, 24, 25, 29, 32 }, Ships(107).ToArray());
            // the terminated Most Wanted ships 45-48 (here: the finished game's world)
            Session.CompletedWorld = true;
            Assert.AreEqual(new[] { 2, 11, 23, 24, 25, 29, 32, 45, 46, 47, 48 }, Ships(107).ToArray());
        }

        [Test]
        public void QuinerosAddsACriminalsShipOnceItsEntryIsTerminated()
        {
            Session.CampaignMission = 100;
            Session.Wanted = new List<WantedState>();
            for (int i = 0; i < 25; i++) Session.Wanted.Add(new WantedState());
            Session.Wanted[12].terminated = true;
            var ships = Ships(107);
            CollectionAssert.Contains(ships, 46);
            CollectionAssert.DoesNotContain(ships, 45);
            CollectionAssert.DoesNotContain(ships, 47);
            CollectionAssert.DoesNotContain(ships, 48);
        }

        [Test]
        public void ThynomeSellsTheVoidXInTheFinishedWorld()
        {
            Session.CompletedWorld = true;
            Session.CampaignMission = 162;
            // The VoidX first, the dealer's usual roll after it (the original sells the VoidX alone there).
            bool others = false;
            for (int r = 0; r < Rolls; r++)
            {
                var ships = Ships(10);
                Assert.AreEqual(8, ships[0]);
                Assert.AreEqual(1, ships.Count(s => s == 8));
                others |= ships.Count > 1;
            }
            Assert.IsTrue(others, "Thynome's other ships stay");
        }

        [Test]
        public void KatashanSellsTheScimitarAndOnExtremeTheSpecter()
        {
            // (a roll of 0 ships gives none at all: "no rolls, no extras either")
            Session.CampaignMission = 159;
            int lists = 0;
            for (int r = 0; r < Rolls; r++)
            {
                var ships = Ships(120);
                if (ships.Count == 0) continue;
                lists++;
                CollectionAssert.Contains(ships, 49);
                CollectionAssert.DoesNotContain(ships, 44);
            }
            Session.Difficulty = Session.DifficultyExtreme;
            for (int r = 0; r < Rolls; r++)
            {
                var ships = Ships(120);
                if (ships.Count == 0) continue;
                lists++;
                CollectionAssert.Contains(ships, 49);
                CollectionAssert.Contains(ships, 44);
            }
            Assert.Greater(lists, Rolls);
            Session.CampaignMission = 150;
            for (int r = 0; r < Rolls; r++) CollectionAssert.DoesNotContain(Ships(120), 49, "only after step 158");
        }

        [Test]
        public void VosskDealersSellTheAddOnShipsOnlyAfterTheValkyrie()
        {
            // getShipBuyList: Vossk + DLC1 won: 1/2 each 39 / 41 (and the fighter rolls 60 / 25 / 15 % 9 / 41 / 39)
            Session.CampaignMission = 60;
            for (int r = 0; r < Rolls; r++)
            {
                var ships = Ships(15);
                CollectionAssert.DoesNotContain(ships, 39);
                CollectionAssert.DoesNotContain(ships, 41);
            }
            Session.CampaignMission = 100;
            bool saw = false;
            for (int r = 0; r < 200 && !saw; r++) { var ships = Ships(15); saw = ships.Contains(39) || ships.Contains(41); }
            Assert.IsTrue(saw);
        }

        [Test]
        public void VosskFighterRollsAfterTheValkyrie()
        {
            // Globals::getRandomEnemyFighter race 1: 9 before, then 60 % 9, 25 % 41, 15 % 39
            Session.CampaignMission = 60;
            for (int r = 0; r < 50; r++) Assert.AreEqual(9, Shop.RandomFighter(1));
            Session.CampaignMission = 100;
            var counts = new Dictionary<int, int> { [9] = 0, [41] = 0, [39] = 0 };
            for (int r = 0; r < 4000; r++) counts[Shop.RandomFighter(1)]++;
            Assert.That(counts[9] / 4000f, Is.InRange(0.55f, 0.65f));
            Assert.That(counts[41] / 4000f, Is.InRange(0.21f, 0.29f));
            Assert.That(counts[39] / 4000f, Is.InRange(0.12f, 0.18f));
        }

        [Test]
        public void RandomFighterPicksTheRacesShips()
        {
            // getRandomEnemyFighter: ships of DAT_00254990[ship] == race from 0..36 (not 0, 8, 9, 10, 13-15); Void 8, Specters 44
            Session.CampaignMission = 30;
            for (int r = 0; r < 300; r++)
            {
                foreach (int race in new[] { 0, 2, 3, 8 })
                {
                    int s = Shop.RandomFighter(race);
                    Assert.That(s, Is.InRange(1, 36));
                    CollectionAssert.DoesNotContain(new[] { 0, 8, 9, 10, 13, 14, 15 }, s);
                    Assert.AreEqual(race, Shop.ShipRace[s], "ship " + s);
                }
                Assert.AreEqual(8, Shop.RandomFighter(9));
                Assert.AreEqual(44, Shop.RandomFighter(10));
                Assert.AreEqual(8, Shop.ShipRace[Shop.RandomFighter(5)], "the minor races fly the pirates'");
            }
        }

        // ---- the 3-station memory --------------------------------------------------------------------------------

        [Test]
        public void StockIsKeptForTheLastThreeStations()
        {
            var a = Shop.EnterStation(Db, 70);
            Shop.EnterStation(Db, 15);
            Shop.EnterStation(Db, 41);
            Assert.AreEqual(new[] { 70, 15, 41 }, Session.RecentStations.Select(s => s.station).ToArray());
            Assert.AreSame(a, Shop.EnterStation(Db, 70), "still on the stack: the same stock");
            Shop.EnterStation(Db, 86);
            Assert.AreEqual(3, Session.RecentStations.Count);
            Assert.AreEqual(new[] { 15, 41, 86 }, Session.RecentStations.Select(s => s.station).ToArray());
            Assert.AreNotSame(a, Shop.EnterStation(Db, 70), "fell off the stack: generated afresh");
        }

        [Test]
        public void ArrivingInOrbitsPushesTheStackLikeDocking()
        {
            // Status::departStation runs on every orbit arrival: flying through 3 other orbits without docking makes the
            // station's stock afresh (Katashán's empty dealer stayed until the player had docked at 3 other stations).
            var a = Shop.EnterStation(Db, 120);
            Shop.ArriveInOrbit(Db, 120);
            Assert.AreSame(a, Shop.EnterStation(Db, 120), "already on the stack: kept");
            Shop.ArriveInOrbit(Db, 15);
            Shop.ArriveInOrbit(Db, 41);
            Shop.ArriveInOrbit(Db, 15);   // a revisit doesn't reorder
            Assert.AreEqual(new[] { 120, 15, 41 }, Session.RecentStations.Select(s => s.station).ToArray());
            Shop.ArriveInOrbit(Db, 86);
            Assert.AreEqual(new[] { 15, 41, 86 }, Session.RecentStations.Select(s => s.station).ToArray());
            Assert.AreNotSame(a, Shop.EnterStation(Db, 120), "fell off the stack: generated afresh");
        }

        [Test]
        public void ReturningAfterThirtySecondsNibblesTheStock()
        {
            // Generator::computerTradeGoods: r = rnd(3); if r < amount, amount -= r (a row never empties)
            if (Time.realtimeSinceStartup < 32f) Assert.Ignore("needs a departure time of at least 0 more than 30 s ago");
            var stock = Shop.EnterStation(Db, 70);
            stock.items = new List<ItemStack> { new ItemStack(122, 10), new ItemStack(5, 1), new ItemStack(55, 2) };
            Session.LastDepartureTime = Time.realtimeSinceStartup - 31f;
            for (int r = 0; r < 20; r++)
            {
                var before = stock.items.Select(s => s.amount).ToArray();
                Assert.AreSame(stock, Shop.EnterStation(Db, 70));
                for (int n = 0; n < before.Length; n++)
                {
                    Assert.That(before[n] - stock.items[n].amount, Is.InRange(0, 2));
                    Assert.GreaterOrEqual(stock.items[n].amount, 1);
                }
            }
            Assert.Less(stock.items[0].amount, 10, "20 visits nibbled something");
        }

        [Test]
        public void ReturningWithinThirtySecondsKeepsTheStock()
        {
            var stock = Shop.EnterStation(Db, 70);
            stock.items = new List<ItemStack> { new ItemStack(122, 10) };
            Session.LastDepartureTime = Time.realtimeSinceStartup;
            Shop.EnterStation(Db, 70);
            Assert.AreEqual(10, stock.items[0].amount);
        }

        [Test]
        public void NoTimeExtenderForSaleInMultiplayer()
        {
            // Quineros (107) always stocks the Rhoda Vortex (184, attr 61); a multiplayer session doesn't (Shop.NotInSession:
            // TimeExtender.Attach does nothing there).
            Session.CampaignMission = 100;
            Assert.IsTrue(Items(107).Exists(s => s.item == 184), "single player: Quineros sells the time extender");
            var old = NetSession.ActiveProvider;
            try
            {
                NetSession.ActiveProvider = () => true;
                for (int i = 0; i < 5; i++) Assert.IsFalse(Items(107).Exists(s => s.item == 184), "a session: not for sale");
                Assert.IsTrue(Shop.NotInSession(Db, 184));
                Assert.IsFalse(Shop.NotInSession(Db, 185), "the emergency system is still sold");
            }
            finally { NetSession.ActiveProvider = old; }
            Assert.IsFalse(Shop.NotInSession(Db, 184));
        }

        [Test]
        public void OwnedKaamoClubStockIsTheStorage()
        {
            Session.KaamoState = 3;
            Session.KaamoItems.Add(new ItemStack(122, 7));
            var stock = Shop.EnterStation(Db, 108);
            Assert.AreSame(Session.KaamoItems, stock.items);
            Assert.IsEmpty(stock.ships);
        }
    }
}
