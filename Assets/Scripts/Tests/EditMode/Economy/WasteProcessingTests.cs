// WasteProcessingTests.cs
// Feature: remake-only, the Mido Mining Plant sorts Space Waste for a fee (WasteProcessing), a gamble.
// Expected: every ton is sorted on its own, costs the fee and is junk or one ton of something (gear: one piece); in both
// economies a ton returns about 93 % of what it costs in the long run, bad-luck caps included (never a profit: the waste is
// bought for 5-6$); never more than DryCap empty tons in a row and a rare find at the latest every RarePity tons; the finds
// are real, priced items of the original tables, each in the tier of its price band, no blueprint gear, gear up to tech
// level 5, the plasmas only after the plasma kit's step and never the Toad Mutagen; the rolls follow the game's RunId and
// the saved counters, so a reloaded save finds the same; the menu and the answer show the fees beside what is found.

using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.UI;
using NUnit.Framework;
using UnityEngine;
using Tier = GoF2Remake.Data.WasteProcessing.FindTier;

namespace GoF2Remake.Tests
{
    public class WasteProcessingTests : EconomyTest
    {
        static readonly Economy[] Economies = { Economy.Android, Economy.Default };

        [Test]
        public void ATonNeverPaysInTheLongRunInEitherEconomy([ValueSource(nameof(Economies))] Economy economy)
        {
            Session.Economy = economy;
            var db = Db;
            float ratio = WasteProcessing.ExpectedValue(db) / WasteProcessing.TonCost(db);
            Assert.That(ratio, Is.InRange(0.9f, WasteProcessing.Payback + 0.001f), $"fee {WasteProcessing.Fee(db)}");
            Session.Difficulty = Session.DifficultyExtreme;
            Assert.Less(WasteProcessing.ExpectedValue(db) / WasteProcessing.TonCost(db), ratio - 0.2f, "Extreme: 1.5x the fee");
        }

        [Test]
        public void TheFeeFollowsTheEconomysPrices()
        {
            int android = WasteProcessing.Fee(Db);
            Session.Economy = Economy.Default;
            int dflt = WasteProcessing.Fee(Db);
            Assert.Greater(android, 10);
            Assert.Greater(dflt, 10);
            Assert.AreNotEqual(android, dflt, "worked out from each economy's prices");
        }

        [Test]
        public void TheFindsAndTheCapsHoldOverManyTons([ValueSource(nameof(Economies))] Economy economy)
        {
            Session.Economy = economy;
            var db = Db;
            Session.RunId = "waste-test";
            const int n = 200000;
            double sum = 0, common = 0;
            int dry = 0, sinceRare = 0, hits = 0, rares = 0, emptyRun = 0, longestEmpty = 0, sinceLastRare = 0, longestRareGap = 0;
            for (int ton = 0; ton < n; ton++)
            {
                var p = WasteProcessing.Roll(db, ton, ref dry, ref sinceRare, out var tier);
                sinceLastRare++;
                if (tier >= Tier.Rare) { rares++; longestRareGap = System.Math.Max(longestRareGap, sinceLastRare); sinceLastRare = 0; }
                if (p == null)
                {
                    Assert.AreEqual(Tier.None, tier);
                    longestEmpty = System.Math.Max(longestEmpty, ++emptyRun);
                    continue;
                }
                emptyRun = 0;
                hits++;
                Assert.AreNotEqual(Tier.None, tier);
                Assert.AreEqual(1, p.amount, "a ton of waste finds one ton (or one piece of gear)");
                if (ton >= 3000) { sum += WasteProcessing.Value(db, p.item); if (tier <= Tier.Valuable) common += WasteProcessing.Value(db, p.item); }
                if (ton >= 3000) continue;   // the item checks on the first ones (they are slow)
                var it = db.Item(p.item);
                Assert.IsNotNull(it, $"ton {ton}: item {p.item}");
                Assert.IsFalse(it.modded);
                Assert.Greater(it.minPrice, 0, it.name);
                Assert.AreNotEqual(209, p.item, "never the Toad Mutagen");
                Assert.IsFalse(p.item >= 201 && p.item <= 204, "no plasma before the plasma kit");
                CollectionAssert.Contains(WasteProcessing.Finds(db, tier), p.item, $"{it.name} in its tier's pool");
                if (it.categoryId < 22 || it.categoryId > 24)
                {
                    Assert.AreEqual(Tier.Rare, tier, it.name);
                    Assert.LessOrEqual(it.techLevel, WasteProcessing.GearMaxTech, it.name);
                    Assert.IsTrue(it.blueprint == null || it.blueprint.Count == 0, $"{it.name} is built from a blueprint");
                }
            }
            var odds = WasteProcessing.LongRun(db);
            Assert.LessOrEqual(longestEmpty, WasteProcessing.DryCap, "never more than DryCap empty tons in a row");
            Assert.LessOrEqual(longestRareGap, WasteProcessing.RarePity, "a rare find at the latest every RarePity tons");
            Assert.That(hits / (double)n, Is.EqualTo(odds.hitRate).Within(0.01), "the share of tons that find something");
            Assert.That(rares / (double)n, Is.EqualTo(odds.rareRate).Within(0.001), "the share of rare finds");
            Assert.That(odds.hitRate, Is.InRange(0.3f, 0.5f), "a good part of the tons find something");
            Assert.That(sum / (n - 3000), Is.EqualTo(odds.value).Within(odds.value * 0.05), "the long-run value the fee is set from");
            Assert.Greater(common / sum, 0.6, "most of the return in the common tiers: a hold rarely comes back poor");
        }

        [Test]
        public void TheTiersFollowWhatTheGoodsAreWorth()
        {
            // By price band in the active table: the Default Economy's Void crystals (400$) are Valuable, the Android
            // tables' (42$) Scrap; every tier has its choice in both economies; junk (towels, basic food) never turns up.
            CollectionAssert.Contains(WasteProcessing.Finds(Db, Tier.Scrap), 164);
            CollectionAssert.Contains(WasteProcessing.Finds(Db, Tier.Exceptional), 175, "Void essence");
            CollectionAssert.DoesNotContain(WasteProcessing.Finds(Db, Tier.Scrap), 112, "towels (6$) are junk");
            Session.Economy = Economy.Default;
            CollectionAssert.Contains(WasteProcessing.Finds(Db, Tier.Valuable), 164);
            foreach (var economy in Economies)
            {
                Session.Economy = economy;
                Assert.GreaterOrEqual(WasteProcessing.Finds(Db, Tier.Scrap).Count, 10, economy + " Scrap");
                Assert.GreaterOrEqual(WasteProcessing.Finds(Db, Tier.Salvage).Count, 20, economy + " Salvage");
                Assert.GreaterOrEqual(WasteProcessing.Finds(Db, Tier.Valuable).Count, 15, economy + " Valuable");
                Assert.GreaterOrEqual(WasteProcessing.Finds(Db, Tier.Rare).Count, 30, economy + " Rare (gear and rare goods)");
                Assert.GreaterOrEqual(WasteProcessing.Finds(Db, Tier.Exceptional).Count, 4, economy + " Exceptional");
                foreach (int item in WasteProcessing.Finds(Db, Tier.Exceptional))
                    Assert.GreaterOrEqual(WasteProcessing.Value(Db, item), WasteProcessing.ExceptionalFrom, Db.Item(item).name);
                CollectionAssert.DoesNotContain(WasteProcessing.Finds(Db, Tier.Exceptional), 209, "never the Toad Mutagen");
            }
        }

        [Test]
        public void ThePlasmasTurnUpOnlyAfterThePlasmaKit()
        {
            // 142's tutorial counts any plasma aboard; afterwards the four plasmas join the pools, Red Plasma volatile.
            List<int> All()
            {
                var all = new List<int>();
                for (var t = Tier.Scrap; t <= Tier.Exceptional; t++) all.AddRange(WasteProcessing.Finds(Db, t));
                return all;
            }
            CollectionAssert.DoesNotContain(All(), WasteProcessing.RedPlasma, "step 30");
            Session.CampaignMission = 142;
            CollectionAssert.DoesNotContain(All(), WasteProcessing.RedPlasma, "during the plasma kit");
            Session.CampaignMission = 143;
            for (int plasma = 201; plasma <= 204; plasma++) CollectionAssert.Contains(All(), plasma, "after the plasma kit");
            CollectionAssert.DoesNotContain(All(), 209, "never the Toad Mutagen");
        }

        [Test]
        public void TheSameGameFindsTheSameAndAnotherGameOtherThings()
        {
            List<string> Rolls(string run)
            {
                Session.RunId = run;
                var l = new List<string>();
                int dry = 0, sinceRare = 0;
                for (int i = 0; i < 200; i++)
                {
                    var s = WasteProcessing.Roll(Db, i, ref dry, ref sinceRare, out var tier);
                    l.Add(s == null ? "-" : $"{s.item} {tier}");
                }
                return l;
            }
            CollectionAssert.AreEqual(Rolls("a"), Rolls("a"));
            CollectionAssert.AreNotEqual(Rolls("a"), Rolls("b"));
        }

        [Test]
        public void EveryTonIsSortedAndCostsTheFee()
        {
            int fee = WasteProcessing.Fee(Db);
            Session.Credits = 100000;
            Assert.IsFalse(WasteProcessing.Offered(), "no waste");
            Shop.AddToCargo(WasteProcessing.Waste, 34);
            Assert.IsTrue(WasteProcessing.Offered());
            Assert.AreEqual(34, WasteProcessing.Tons(Db), "all of it, no lots: nothing stays");
            StringAssert.Contains(GameNames.Credits(34 * fee), WasteProcessing.MenuEntry(Db), "the menu entry names the price");
            StringAssert.Contains("34", WasteProcessing.MenuEntry(Db), "and the tons");
            var s = WasteProcessing.Process(Db);
            Assert.AreEqual(34, s.Tons);
            Assert.AreEqual(34 * fee, s.fees);
            Assert.AreEqual(0, Shop.CargoOf(WasteProcessing.Waste));
            Assert.AreEqual(100000 - 34 * fee, Session.Credits);
            Assert.AreEqual(34, Session.WasteTons);
            int foundTons = 0, litSquares = 0;
            foreach (var f in s.found) foundTons += f.amount;
            foreach (var t in s.tons) if (t != Tier.None) litSquares++;
            Assert.AreEqual(foundTons, litSquares, "a lit square for every ton found");
            for (int i = 1; i < s.found.Count; i++) Assert.GreaterOrEqual(s.found[i - 1].tier, s.found[i].tier, "best first");
            foreach (var f in s.found)
            {
                Assert.GreaterOrEqual(Shop.CargoOf(f.item), f.amount, GameNames.Item(f.item));
                Assert.AreEqual(f.tier, s.tons[f.firstTon], "the ton it first turned up in");
            }
            string result = WasteProcessing.Result(Db, s);
            StringAssert.Contains(GameNames.Credits(34 * fee), result, "the fees beside the finds");
            StringAssert.Contains(GameNames.Credits(WasteProcessing.Worth(Db, s.found)), result);
        }

        [Test]
        public void TheCountersCarryOverBetweenSortings()
        {
            Session.Credits = 10000000;
            Shop.AddToCargo(WasteProcessing.Waste, WasteProcessing.RarePity);
            var all = WasteProcessing.Process(Db);
            Assert.AreEqual(WasteProcessing.RarePity, all.Tons);
            Assert.IsTrue(all.tons.Exists(t => t >= Tier.Rare), "RarePity tons hold a rare find");
            int empty = 0;
            foreach (var t in all.tons)
            {
                empty = t == Tier.None ? empty + 1 : 0;
                Assert.LessOrEqual(empty, WasteProcessing.DryCap);
            }
            Assert.AreEqual(WasteProcessing.RarePity, Session.WasteTons);
            Assert.That(Session.WasteDry, Is.InRange(0, WasteProcessing.DryCap));
            Assert.That(Session.WasteSinceRare, Is.InRange(0, WasteProcessing.RarePity - 1));
        }

        [Test]
        public void ABigLoadIsSortedTonByTon()
        {
            // A big hold's waste at once: 1,205 t, every ton; the hold only gets lighter (a ton of waste finds a ton at
            // most); the fees and the finds add up.
            int fee = WasteProcessing.Fee(Db);
            Session.Credits = 10000000;
            Shop.AddToCargo(WasteProcessing.Waste, 1205);
            int before = Shop.CargoLoad();
            var s = WasteProcessing.Process(Db);
            Assert.AreEqual(1205, s.Tons);
            Assert.AreEqual(0, Shop.CargoOf(WasteProcessing.Waste));
            Assert.AreEqual(1205 * fee, s.fees);
            Assert.AreEqual(10000000 - 1205 * fee, Session.Credits);
            int tons = 0;
            foreach (var f in s.found) tons += f.amount;
            Assert.AreEqual(before - 1205 + tons, Shop.CargoLoad());
            Assert.Less(Shop.CargoLoad(), before);
            Assert.GreaterOrEqual(s.tons.FindAll(t => t >= Tier.Rare).Count, 2, "a rare find at least every RarePity tons");
            Assert.AreEqual(s.found.Count, new HashSet<int>(s.found.ConvertAll(f => f.item)).Count, "one row per item");
        }

        [Test]
        public void TheMenuShowsTheWasteAndWhatSortingCosts()
        {
            // The plant menu's waste section: the fee a ton, then the waste with what sorting all of it costs (ready: the
            // menu offers sorting), part of it when the credits don't pay for all, a line without any waste.
            int fee = WasteProcessing.Fee(Db);
            Session.Credits = 100000;
            var none = WasteProcessing.Section(Db);
            Assert.AreEqual(0, none.rows.Count);
            StringAssert.Contains(GameNames.Credits(fee), none.text);
            Shop.AddToCargo(WasteProcessing.Waste, 50);
            var row = WasteProcessing.Section(Db).rows[0];
            Assert.IsTrue(row.ready);
            Assert.AreEqual("50 t", row.amount);
            StringAssert.Contains(GameNames.Credits(50 * fee), row.note, "all of it and its price");
            StringAssert.Contains(GameNames.Credits(50 * fee), WasteProcessing.MenuEntry(Db));
            Session.Credits = 20 * fee + 3;
            var part = WasteProcessing.Section(Db).rows[0];
            Assert.IsTrue(part.ready);
            StringAssert.Contains("20", part.note, "as much as the credits pay for");
            StringAssert.Contains(GameNames.Credits(20 * fee), part.note);
        }

        [Test]
        public void TheCreditsLimitTheTons()
        {
            int fee = WasteProcessing.Fee(Db);
            Shop.AddToCargo(WasteProcessing.Waste, 50);
            Session.Credits = fee + 5;
            Assert.AreEqual(1, WasteProcessing.Tons(Db));
            Session.Credits = fee - 1;
            Assert.AreEqual(0, WasteProcessing.Tons(Db));
            var section = WasteProcessing.Section(Db);
            Assert.AreEqual(1, section.rows.Count);
            Assert.IsFalse(section.rows[0].ready, "nothing to sort");
            StringAssert.Contains(Localization.Extra("plantWasteNoCredits", "not enough credits"), section.rows[0].note);
            var s = WasteProcessing.Process(Db);
            Assert.AreEqual(0, s.Tons);
            Assert.AreEqual(50, Shop.CargoOf(WasteProcessing.Waste));
            Assert.AreEqual(fee - 1, Session.Credits);
        }

        [Test]
        public void TheFindTonesAreShortSoftAndClickFree()
        {
            // The result page's tone for a ton with a find (LotTones): higher for a better tier, under 0.3 s, never
            // clipping, silent at both ends (no click), louder only a little with the tier.
            float lastNote = 0f;
            for (var t = Tier.None; t <= Tier.Exceptional; t++)
            {
                var (notes, ms, volume) = LotTones.Shape(t);
                var s = LotTones.Samples(t);
                Assert.AreEqual(LotTones.SampleRate * ms / 1000, s.Length, t.ToString());
                Assert.LessOrEqual(ms, 300, t.ToString());
                Assert.That(volume, Is.InRange(0.1f, 0.7f), t.ToString());
                Assert.Greater(notes[0], lastNote, $"{t} is higher than the tier below");
                lastNote = notes[0];
                float peak = 0f;
                foreach (float v in s) peak = Mathf.Max(peak, Mathf.Abs(v));
                Assert.That(peak, Is.InRange(0.2f, 1f), $"{t}: audible, never clipping");
                Assert.Less(Mathf.Abs(s[0]), 0.01f, $"{t} starts silent");
                Assert.Less(Mathf.Abs(s[s.Length - 1]), 0.01f, $"{t} ends silent");
            }
        }

        [Test]
        public void TheItemDetailsTellWhereOnceThePlantIsBuilt()
        {
            var waste = Db.Item(WasteProcessing.Waste);
            StringAssert.DoesNotContain(WasteProcessing.Hint, ItemInfo.ItemText(Db, waste, 0), "step 30: no plant yet");
            Session.CampaignMission = 0x55;
            StringAssert.Contains(WasteProcessing.Hint, ItemInfo.ItemText(Db, waste, 0));
            StringAssert.DoesNotContain(WasteProcessing.Hint, ItemInfo.ItemText(Db, Db.Item(98), 0), "only Space Waste");
        }
    }
}
