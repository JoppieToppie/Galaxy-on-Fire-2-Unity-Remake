// ShopPriceTests.cs
// Feature: CLAUDE.md "Shop" > Prices (Status::calcCargoPrices 0xb9d70), the Loma rule, Ship::adjustPrice, the remake's
// pinned prices per docking (Hangar.PinPrices), the seen-price records (HangarWindow::initialize).
// Expected numbers: Reference/tools/shop/prices.py (an independent Python port of the price rule with float32 rounding and
// its own java.util.Random), run on the Android items.json; Loma = maxPrice with the Android economy, x 1.05 with the
// Default economy (the iOS build's Status::calcCargoPrices, shop.md "Prices"); ship prices from
// ships.json x 0.99 by hand (shop.md 5); the Phantom 966667 x 0.99f = 957000.

using System.Collections.Generic;
using GoF2Remake.Data;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public class ShopPriceTests : EconomyTest
    {
        public struct PriceCase
        {
            public int station;
            public int[] items, prices;
            public PriceCase(int station, int[] items, int[] prices) { this.station = station; this.items = items; this.prices = prices; }
            public override string ToString() => $"station {station}, {items.Length} items";
        }

        // python -I Reference/tools/shop/prices.py <station> <items...>
        static readonly PriceCase[] Cases =
        {
            new PriceCase(78, new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23 }, new[] { 435, 6433, 10530, 8932, 13702, 21249, 30537, 35434, 85258, 36365, 43767, 109231, 11700, 15578, 23795, 52934, 5278, 11645, 37411, 39992, 63727, 92251, 469, 5153 }),
            new PriceCase(0, new[] { 122, 109, 110, 1, 5, 54, 85, 132, 150, 200 }, new[] { 1404, 9717, 755, 6262, 19535, 273271, 637730, 138, 887, 1148428 }),
            new PriceCase(10, new[] { 100, 101, 102, 103, 104, 105, 106, 107, 108, 109, 110, 111, 112, 113, 114, 115, 116, 117, 118, 119, 120, 121, 122, 123, 124, 125, 126, 127, 128, 129, 130, 131 }, new[] { 1241, 2756, 3919, 32, 1165, 35, 3, 4551, 24290, 9696, 667, 6, 4, 6476, 377, 1319, 0, 0, 405, 137, 154, 947, 1511, 6, 259, 472, 380, 170, 80, 413, 296, 45 }),
            new PriceCase(50, new[] { 2, 36, 54, 59, 73, 82, 86, 122, 164 }, new[] { 10110, 92, 273554, 173882, 20948, 7994, 515, 1517, 43 }),
            new PriceCase(70, new[] { 200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 213, 214, 215, 216, 217, 218, 219, 220, 221, 222, 223, 224, 225, 226, 227, 228, 229, 230, 231, 232 }, new[] { 1129442, 40, 42, 55, 84, 27010, 596291, 6744, 357176, 525747, 792162, 695, 1316, 5401, 346, 750, 1435, 410, 5016, 468343, 911718, 8743, 8728, 546164, 480807, 749113, 7648, 871690, 532242, 511920, 404698, 353081, 1573725 }),
            new PriceCase(100, new[] { 7, 3, 160, 161, 162, 163, 164, 165, 166 }, new[] { 35492, 8913, 36, 46, 40, 72, 45, 441, 825 }),
            new PriceCase(134, new[] { 122, 122, 122, 5, 5 }, new[] { 1502, 1472, 1471, 20515, 20329 }),
            new PriceCase(41, new[] { 24, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 44, 45, 46, 47, 48, 49, 50, 51, 52, 53, 54, 55, 56, 57, 58, 59 }, new[] { 12887, 26129, 44497, 116081, 8158, 34621, 81854, 22, 31, 100, 51, 65, 93, 265, 178, 95, 265, 67, 189, 347, 836, 1265, 4848, 20438, 69476, 140536, 3909, 12047, 31625, 91472, 271827, 2387, 10179, 43770, 114533, 176518 }),
            new PriceCase(108, new[] { 109, 122, 140 }, new[] { 9605, 1428, 280 }),
            new PriceCase(86, new[] { 60, 61, 62, 63, 64, 65, 66, 67, 68, 69, 70 }, new[] { 211, 367, 1530, 2213, 4963, 13854, 26521, 110566, 150071, 459229, 1121324 }),
        };

        [TestCaseSource(nameof(Cases))]
        public void PriceListMatchesReference(PriceCase c)
        {
            Assert.AreEqual(c.prices, Shop.PriceList(Db, c.station, c.items));
        }

        [Test]
        public void LomaChargesTheMaximumAndTheIosBuildFivePercentMore()
        {
            // Loma's stations 105-107 (system 25): Android economy = maxPrice, Default economy = (int)(maxPrice x 1.05), no jitter.
            var items = new List<int> { 0, 5, 54, 109, 122, 164, 200 };
            var before = Session.Economy;
            try
            {
                foreach (int st in new[] { 105, 106, 107 })
                {
                    // (Db follows Session.Economy: each economy's maxima from its own tables)
                    Session.Economy = Economy.Android;
                    var dbAndroid = Db;
                    var android = Shop.PriceList(dbAndroid, st, items);
                    Session.Economy = Economy.Default;
                    var dbDefault = Db;
                    var ios = Shop.PriceList(dbDefault, st, items);
                    for (int n = 0; n < items.Count; n++)
                    {
                        Assert.AreEqual(dbAndroid.Item(items[n]).maxPrice, android[n], $"item {items[n]} at station {st}, Android");
                        Assert.AreEqual((int)(dbDefault.Item(items[n]).maxPrice * 1.05f), ios[n], $"item {items[n]} at station {st}, Default");
                    }
                }
            }
            finally { Session.Economy = before; }
        }

        [Test]
        public void PriceListIsDeterministic()
        {
            var items = new List<int> { 0, 5, 54, 109, 122, 164, 200 };
            for (int st = 0; st < 135; st += 7)
                Assert.AreEqual(Shop.PriceList(Db, st, items), Shop.PriceList(Db, st, items), "station " + st);
        }

        [Test]
        public void PricesStayWithinTwoPercentOfTheRange()
        {
            // base in [min, max], jitter +-max(1, 2 % of base): a price never leaves [min - 2 %, max + 2 %] (+-1 for tiny prices)
            var items = new List<int>();
            foreach (var it in Db.Items) if (it.maxPrice > 0) items.Add(it.index);
            for (int st = 0; st < 135; st++)
            {
                if (Shop.SystemOf(Db, st) == 25) continue;
                var p = Shop.PriceList(Db, st, items);
                for (int n = 0; n < items.Count; n++)
                {
                    var it = Db.Item(items[n]);
                    int lo = it.minPrice - System.Math.Max(1, (int)(it.minPrice * 0.02f)) ;
                    int hi = it.maxPrice + System.Math.Max(1, (int)(it.maxPrice * 0.02f));
                    Assert.That(p[n], Is.InRange(lo, hi), $"item {it.index} at station {st}");
                }
            }
        }

        [Test]
        public void ThePositionInTheListChangesThePrice()
        {
            // Each list is priced with a fresh Random(station), in order: the same item gets a different jitter further down.
            Assert.AreEqual(new[] { 1502 }, Shop.PriceList(Db, 134, new[] { 122 }));
            Assert.AreEqual(new[] { 19761, 1472 }, Shop.PriceList(Db, 134, new[] { 5, 122 }));
            Assert.AreEqual(new[] { 416, 20395, 1471 }, Shop.PriceList(Db, 134, new[] { 0, 5, 122 }));
        }

        [Test]
        public void UnknownItemGetsNoPriceAndConsumesNoRandomNumber()
        {
            // An unknown number is skipped (price 0) without drawing: the next item's price is as if it weren't there.
            var p = Shop.PriceList(Db, 134, new[] { 99999, 122 });
            Assert.AreEqual(0, p[0]);
            Assert.AreEqual(1502, p[1]);
        }

        [TestCase(105)]
        [TestCase(106)]
        [TestCase(107)]
        public void LomaStationsAlwaysChargeTheMaximum(int station)
        {
            // python prices.py max_price_mode: items 0 / 5 / 122 / 200 -> their maxPrice
            Assert.AreEqual(new[] { 440, 21010, 1540, 1142310 }, Shop.PriceList(Db, station, new[] { 0, 5, 122, 200 }));
        }

        [Test]
        public void ShipPriceIsOnePercentLessInTheShipsRaceSystems()
        {
            // H'Soc (9, Vossk, 120000): V'ikka (system 3, Vossk) station 15 vs Dis (70, Terran Magnetar)
            Assert.AreEqual(118800, Shop.ShipPrice(Db, 9, 15));
            Assert.AreEqual(120000, Shop.ShipPrice(Db, 9, 70));
            // Phantom (10, Terran, 966667): (int)(966667 x 0.99f) = 957000 at Dis, full price in V'ikka
            Assert.AreEqual(957000, Shop.ShipPrice(Db, 10, 70));
            Assert.AreEqual(966667, Shop.ShipPrice(Db, 10, 15));
            // Betty (0, Midorian table race, 16200) at Var Hastra (Mido, Midorian)
            Assert.AreEqual(16038, Shop.ShipPrice(Db, 0, 78));
            Assert.AreEqual(0, Shop.ShipPrice(Db, 9999, 70), "unknown ship");
        }

        [Test]
        public void ShipRaceTableIsTheOriginals()
        {
            // shop.md 4.4: DAT_00251d5c = DAT_00254990, 64 ints
            int[] expected = { 3, 0, 8, 3, 2, 0, 3, 0, 9, 1, 0, 8, 2, 0, 0, 0, 2, 0, 2, 3, 3, 2, 0, 8, 8, 8, 0, 0, 0, 8, 3, 2,
                               8, 0, 0, 2, 0, 0, 0, 1, 0, 1, 1, 2, 1, 3, 3, 3, 3, 1, 1, 0, 8, 1, 1, 0, 3, 2, 0, 0, 8, 1, 3, 1 };
            Assert.AreEqual(expected, Shop.ShipRace);
            for (int s = 0; s < 64; s++) Assert.AreEqual(expected[s], Shop.RaceOfShip(s));
            Assert.AreEqual(-1, Shop.RaceOfShip(5000), "no such ship");
        }

        [Test]
        public void ShipMakerRaceExceptions()
        {
            // Shop.ShipMakerRace's documented exceptions to the price table
            Assert.AreEqual(7, Shop.ShipMakerRace(42));
            Assert.AreEqual(2, Shop.ShipMakerRace(44));
            Assert.AreEqual(2, Shop.ShipMakerRace(49));
            Assert.AreEqual(2, Shop.ShipMakerRace(61));
            Assert.AreEqual(0, Shop.ShipMakerRace(52));
            Assert.AreEqual(0, Shop.ShipMakerRace(62));
            Assert.AreEqual(-1, Shop.ShipMakerRace(45));
            Assert.AreEqual(-1, Shop.ShipMakerRace(46));
            Assert.AreEqual(8, Shop.ShipMakerRace(48));
            Assert.AreEqual(1, Shop.ShipMakerRace(9));
        }

        [Test]
        public void GalaxyDistanceIsTheTruncatedMapDistance()
        {
            // Galaxy::distancePercent: Suteo (22, 55) - Pan (22, 81) = 26; Suteo - V'ikka (52, 61): sqrt(900 + 36) = 30.59 -> 30
            Assert.AreEqual(26, Shop.Distance(Db, 0, 1));
            Assert.AreEqual(30, Shop.Distance(Db, 0, 3));
            Assert.AreEqual(0, Shop.Distance(Db, 5, 5));
            Assert.AreEqual(Shop.Distance(Db, 3, 17), Shop.Distance(Db, 17, 3));
            Assert.AreEqual(0, Shop.Distance(Db, 0, 999), "unknown system");
        }

        // ---- the hangar's prices -------------------------------------------------------------------------------

        [Test]
        public void HangarPricesTheStockListAndTheStationPriceWins()
        {
            // Dis (70): stock [0, 55, 122] -> 421 / 2373 / 1454 (prices.py); 122 alone in the hold would be 1443, but the
            // station's list wins in the merged list (Item::mixItems).
            Session.Cargo.Add(new ItemStack(122, 3));
            var h = new Hangar(Db, Stock(70, 0, 5, 55, 5, 122, 5));
            Assert.AreEqual(421, h.PriceOf(0));
            Assert.AreEqual(2373, h.PriceOf(55));
            Assert.AreEqual(1454, h.PriceOf(122));
            // the mounted list: the start equipment [2, 2, 36, 54, 59, 82, 73] at Dis -> 54 = 264044 (prices.py, position 3)
            Assert.AreEqual(264044, h.PriceOf(54));
        }

        [Test]
        public void PricesArePinnedForTheDocking()
        {
            // Energy Cells at 134: 1502 first in a list, 1472 second (prices.py). The first price this docking gave stays.
            var first = new Hangar(Db, Stock(134, 122, 10));
            Assert.AreEqual(1502, first.PriceOf(122));
            var again = new Hangar(Db, Stock(134, 5, 1, 122, 10));
            Assert.AreEqual(1502, again.PriceOf(122), "pinned");
            Hangar.NewDocking();
            var next = new Hangar(Db, Stock(134, 5, 1, 122, 10));
            Assert.AreEqual(1472, next.PriceOf(122), "a new docking prices afresh");
        }

        [Test]
        public void PinnedPricesBelongToOneStation()
        {
            new Hangar(Db, Stock(134, 122, 10));
            var other = new Hangar(Db, Stock(70, 0, 5, 55, 5, 122, 5));
            Assert.AreEqual(1454, other.PriceOf(122), "another station starts afresh");
        }

        [Test]
        public void ItemJoiningAfterOpeningGetsItsPriceOnDemand()
        {
            var h = new Hangar(Db, Stock(134, 122, 10));
            // 5 wasn't in any list: priced alone, first in its own list (prices.py price_list(134, [5]) = 19761)
            Assert.AreEqual(19761, h.PriceOf(5));
        }

        [Test]
        public void LomaHangarSellsAtMaximumAndRecordsNoSeenPrices()
        {
            var h = new Hangar(Db, Stock(106, 109, 5, 122, 5));
            Assert.AreEqual(9900, h.PriceOf(109));
            Assert.AreEqual(1540, h.PriceOf(122));
            Assert.AreEqual(0, Session.LowestKnownPrice.Count, "HangarWindow::initialize skips the records in the black market");
            Assert.AreEqual(0, Session.HighestKnownPrice.Count);
        }

        [Test]
        public void HangarRecordsLowestAndHighestSeenPrices()
        {
            new Hangar(Db, Stock(70, 0, 5, 55, 5, 122, 5));
            Assert.AreEqual((1454, 14), Session.LowestKnownPrice[122]);
            Assert.AreEqual((1454, 14), Session.HighestKnownPrice[122]);
            Hangar.NewDocking();
            new Hangar(Db, Stock(134, 122, 10));   // 1502 in Alda (system 33)
            Assert.AreEqual((1454, 14), Session.LowestKnownPrice[122]);
            Assert.AreEqual((1502, 33), Session.HighestKnownPrice[122]);
        }

        [Test]
        public void TutorialGearIsFreeAtVarHastraBeforeStepSeven()
        {
            // getItemBuyList: items 0 / 22 / 55 at price 0 at Var Hastra before campaign step 7 (shop.md 4.3)
            Session.CampaignMission = 5;
            var h = new Hangar(Db, Stock(78, 0, 1, 22, 1, 55, 1));
            Assert.AreEqual(0, h.PriceOf(0));
            Assert.AreEqual(0, h.PriceOf(55));
            Session.CampaignMission = 7;
            Hangar.NewDocking();
            h = new Hangar(Db, Stock(78, 0, 1, 22, 1, 55, 1));
            Assert.AreEqual(435, h.PriceOf(0), "prices.py price_list(78, [0, 22, 55])");
            Assert.AreEqual(468, h.PriceOf(22));
            Assert.AreEqual(2485, h.PriceOf(55));
        }
    }
}
