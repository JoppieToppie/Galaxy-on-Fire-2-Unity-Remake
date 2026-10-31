// DefaultEconomyRulesTests.cs
// Feature: CLAUDE.md "Shop" > the Default economy's rules from the iOS 1.1.19 build (PR #213): the stock factor
// (step + 10) / 45 (Android (step + 25) / 45), the 40 % AB-1 Retractor in every shop during steps 17..24, dealers of 2..5
// ships with duplicates rolled again (Android 0..5, a duplicate dropped), blueprint auto-completion at 1.4 x the larger of
// the remaining ingredients' value and quantity x maxPrice (Android 1.25 x quantity x maxPrice), and the news ticker's
// numbers (NewsTicker::replaceTokens, whose int arguments the Android decompile lost). Loma's x 1.05, the loot's empty roll,
// the Thynome seller and the ship table are in ShopPriceTests, FighterPoolTests, AgentGeneratorTests and DatabaseTests.
// The rolls use UnityEngine.Random, seeded here so a failure repeats.

using System.Collections.Generic;
using System.Linq;
using GoF2Remake.Data;
using NUnit.Framework;
using UnityEngine;

namespace GoF2Remake.Tests
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public class DefaultEconomyRulesTests : EconomyTest
    {
        const int Rolls = 300;
        Random.State savedRandom;
        float savedPlaySeconds;

        [SetUp]
        public void SeedRandom()
        {
            savedRandom = Random.state;
            savedPlaySeconds = Session.PlaySeconds;
            Random.InitState(4213);
        }

        [TearDown]
        public void RestoreRandom()
        {
            Random.state = savedRandom;
            Session.PlaySeconds = savedPlaySeconds;
        }

        // ---- stock ---------------------------------------------------------------------------------------------

        [Test]
        public void TheStockFactorStartsFifteenStepsLaterOnTheIosBuild()
        {
            Session.Economy = Economy.Android;
            Assert.AreEqual(25f / 45f, Shop.StockFactor(0), 1e-6f);
            Assert.AreEqual(1f, Shop.StockFactor(20), 1e-6f);
            Assert.AreEqual(1.5f, Shop.StockFactor(43), 1e-6f, "capped at 1.5");
            Session.Economy = Economy.Default;
            Assert.AreEqual(10f / 45f, Shop.StockFactor(0), 1e-6f);
            Assert.AreEqual(30f / 45f, Shop.StockFactor(20), 1e-6f);
            Assert.AreEqual(1f, Shop.StockFactor(35), 1e-6f);
            Assert.AreEqual(1.5f, Shop.StockFactor(58), 1e-6f, "capped at 1.5");
            Assert.AreEqual(1.5f, Shop.StockFactor(162), 1e-6f);
        }

        [Test]
        public void EveryShopMayHaveTheRetractorDuringSteps17To24()
        {
            // Sao Perula (106) sells only commodities: an AB-1 Retractor (68) there comes from this rule alone.
            int Count(int step, Economy economy, bool freePlay = false)
            {
                Session.Economy = economy;
                Session.FreePlay = freePlay;
                Session.CampaignMission = step;
                int n = 0;
                for (int r = 0; r < Rolls; r++)
                {
                    var list = Shop.GenerateItems(Db, 106);
                    int at = list.FindIndex(s => s.item == 68);
                    if (at < 0) continue;
                    Assert.AreEqual(0, at, "put first");
                    Assert.AreEqual(1, list[at].amount, "one");
                    n++;
                }
                return n;
            }
            foreach (int step in new[] { 17, 20, 24 })
                Assert.That(Count(step, Economy.Default) / (float)Rolls, Is.InRange(0.32f, 0.48f), $"40 % at step {step}");
            Assert.AreEqual(0, Count(16, Economy.Default), "not before step 17");
            Assert.AreEqual(0, Count(25, Economy.Default), "not after step 24");
            Assert.AreEqual(0, Count(20, Economy.Android), "the Android build has the Thynome seller instead");
            Assert.AreEqual(0, Count(20, Economy.Default, freePlay: true), "free play has no story");
        }

        // ---- dealers -------------------------------------------------------------------------------------------

        /// <summary>A Midorian dealer (race 3: no race extras), outside system 17's extras and the special stations.</summary>
        int PlainDealer()
        {
            var special = new HashSet<int> { 10, 41, 78, 100, 101, 107, 108 };
            var st = Db.Stations.FirstOrDefault(s => s.system >= 0 && s.system != 17 && !special.Contains(s.index)
                                                     && !Shop.InSupernovaSystem(s.system, s.index) && Shop.RaceOfSystem(Db, s.system) == 3);
            Assert.IsNotNull(st, "a Midorian station");
            return st.index;
        }

        [Test]
        public void IosDealersHaveTwoToFiveShipsAndNoDuplicates()
        {
            int station = PlainDealer();
            Session.CampaignMission = 30;
            Session.Economy = Economy.Default;
            var sizes = new HashSet<int>();
            for (int r = 0; r < Rolls; r++)
            {
                var ships = Shop.GenerateShips(Db, station);
                Assert.That(ships.Count, Is.InRange(2, 5), $"station {station}: never empty, a duplicate rolled again");
                Assert.AreEqual(ships.Count, ships.Distinct().Count());
                sizes.Add(ships.Count);
            }
            CollectionAssert.AreEquivalent(new[] { 2, 3, 4, 5 }, sizes);

            // the Android build: rnd 6, so an empty dealer one time in six
            Session.Economy = Economy.Android;
            int empty = 0;
            for (int r = 0; r < Rolls; r++) if (Shop.GenerateShips(Db, station).Count == 0) empty++;
            Assert.That(empty / (float)Rolls, Is.InRange(0.10f, 0.24f));
        }

        [Test]
        public void IosDealerAt41StillStartsWithThePhantom()
        {
            Session.CampaignMission = 30;
            Session.Economy = Economy.Default;
            for (int r = 0; r < 50; r++)
            {
                var ships = Shop.GenerateShips(Db, 41);
                Assert.AreEqual(10, ships[0]);
                Assert.GreaterOrEqual(ships.Count, 3, "rnd 4 + 2 + 1");
            }
        }

        // ---- blueprints ----------------------------------------------------------------------------------------

        [Test]
        public void IosAutocompleteIsOnePointFourOfTheLargerOfValueAndPrice()
        {
            Session.Economy = Economy.Default;
            foreach (var it in Blueprints.Products(Db))
            {
                int value = RemainingValue(it);
                if (it.index == 210)   // the Chromo Plasma: 2 000 000 + the rest's value in both builds
                {
                    Assert.AreEqual(2000000 + value, Blueprints.AutoCompletePrice(Db, 210));
                    continue;
                }
                int byPrice = Blueprints.BaseQuantity(it) * it.maxPrice;
                Assert.AreEqual((int)(Mathf.Max(value, byPrice) * 1.4f), Blueprints.AutoCompletePrice(Db, it.index), 2, "product " + it.index);
            }
        }

        static int RemainingValue(ItemData it)
        {
            var s = Blueprints.State(Db, it.index);
            int value = 0;
            for (int k = 0; k < it.blueprint.Count; k++)
                value += Mathf.Max(0, s.remaining[k]) * AgentGenerator.SinglePrice(Db.Item(it.blueprint[k].item));
            return value;
        }

        [Test]
        public void IosAutocompleteFollowsTheIngredientsStillMissing()
        {
            // An ingredient delivered lowers the value side; once the value is below quantity x maxPrice that side decides.
            Session.Economy = Economy.Default;
            // the product whose fresh recipe is worth the most over its price: there the value side decides
            var it = Blueprints.Products(Db).Where(p => p.index != 210)
                .OrderByDescending(p => RemainingValue(p) / (double)(Blueprints.BaseQuantity(p) * p.maxPrice)).First();
            int byPrice = (int)(Blueprints.BaseQuantity(it) * it.maxPrice * 1.4f);
            int fresh = Blueprints.AutoCompletePrice(Db, it.index);
            Assert.Greater(RemainingValue(it), Blueprints.BaseQuantity(it) * it.maxPrice, $"product {it.index}: a recipe worth more than its price");
            Assert.Greater(fresh, byPrice + 2, "the value side");
            var s = Blueprints.State(Db, it.index);
            for (int k = 0; k < s.remaining.Count; k++) s.remaining[k] = 0;
            Assert.AreEqual(byPrice, Blueprints.AutoCompletePrice(Db, it.index), 2, "all delivered: quantity x maxPrice x 1.4");
        }

        // ---- news ticker ---------------------------------------------------------------------------------------

        static string Tokens(string s) => (string)DataUiTestKit.CallStatic(typeof(NewsTicker), "ReplaceTokens", Db, s, 0);

        static int Number(string s) => int.Parse(new string(s.Where(char.IsDigit).ToArray()));

        [Test]
        public void TickerNumbersAreTheIosBuilds()
        {
            for (int r = 0; r < Rolls; r++)
            {
                var parts = Tokens("#VICTIMS|#DEMONSTRATORS|#POLL_PERCENTAGE|#CASUALTIES").Split('|');
                int victims = Number(parts[0]), demonstrators = Number(parts[1]);
                Assert.That(victims, Is.InRange(100000, 198000));
                Assert.AreEqual(0, victims % 1000, "to the thousand");
                Assert.That(demonstrators, Is.InRange(1000000, 1989000));
                Assert.AreEqual(0, demonstrators % 1000, "to the thousand");
                Assert.That(Number(parts[2]), Is.InRange(10, 89));
                Assert.That(Number(parts[3]), Is.InRange(0, 47));
            }
        }

        [Test]
        public void VosskRevenueGrowsWithThePlayingTime()
        {
            // the playing time in ms / 1 000 000 + 12
            Session.PlaySeconds = 50000f;
            StringAssert.StartsWith("62 ", Tokens("#VOSSK_SHIP_OR_ITEM|#VOSSK_REVENUE").Split('|')[1]);
            Session.PlaySeconds = 0f;
            StringAssert.StartsWith("12 ", Tokens("#VOSSK_SHIP_OR_ITEM|#VOSSK_REVENUE").Split('|')[1]);
        }
    }
}
