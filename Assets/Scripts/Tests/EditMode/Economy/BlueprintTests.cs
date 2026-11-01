// BlueprintTests.cs
// Feature: CLAUDE.md "Bar and freelance" > Blueprints (25 products, per-ingredient progress, the production station, 200 $
// per unit shipping, volatile goods, 210 / 223 need gate routes, Autocomplete int(qty x maxPrice x 1.25) / 210: 2 000 000 +
// the rest's value, finished runs to the hold or waiting, the story unlocks) and "Shop" > Blueprint states (Blueprints.Fit).
// Expected: Reference/tools/blueprints/blueprint_table.py (the table, base quantities, autocomplete prices, the story's
// pre-invested ingredients) and Reference/research/blueprints_mods.md 1 / shop.md 7; single price = (min + max) / 2
// (Item::init, the Python tool's mid_price). Android tables.

using System.Collections.Generic;
using System.Linq;
using GoF2Remake.Data;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public class BlueprintTests : EconomyTest
    {
        // python -I Reference/tools/blueprints/blueprint_table.py json: item, base quantity, autocomplete price, ingredients
        static readonly int[] ProductIds = { 11, 15, 25, 27, 33, 37, 46, 54, 59, 67, 85, 90, 96, 178, 179, 182, 183, 206, 210, 221, 223, 225, 226, 227, 232 };
        static readonly int[] TenPerRun = { 33, 37, 46, 179, 221, 226, 232 };
        static readonly int[][] Autocomplete =
        {
            new[] { 11, 134973 }, new[] { 15, 65501 }, new[] { 25, 33358 }, new[] { 27, 150578 }, new[] { 33, 1262 }, new[] { 37, 3412 },
            new[] { 46, 62100 }, new[] { 54, 349946 }, new[] { 59, 229222 }, new[] { 67, 138375 }, new[] { 85, 828653 }, new[] { 90, 295142 },
            new[] { 96, 361176 }, new[] { 178, 548746 }, new[] { 179, 1066412 }, new[] { 182, 665578 }, new[] { 183, 233192 },
            new[] { 206, 733227 }, new[] { 221, 109512 }, new[] { 223, 681371 }, new[] { 225, 976043 }, new[] { 226, 98812 },
            new[] { 227, 1125757 },
        };

        [Test]
        public void TwentyFiveProducts()
        {
            CollectionAssert.AreEqual(ProductIds, Blueprints.Products(Db).Where(i => !i.modded).Select(i => i.index).ToArray());
        }

        [Test]
        public void SecondariesProduceTenPerRun()
        {
            foreach (int p in ProductIds)
                Assert.AreEqual(System.Array.IndexOf(TenPerRun, p) >= 0 ? 10 : 1, Blueprints.BaseQuantity(Db.Item(p)), "product " + p);
            Assert.AreEqual(1, Blueprints.BaseQuantity(null));
        }

        [Test]
        public void RecipesMatchTheTable()
        {
            // a few rows of blueprint_table.py (items.bin's ingredients)
            CollectionAssert.AreEqual(new[] { 9, 155, 120, 126, 122, 129 }, Db.Item(11).blueprint.Select(b => b.item).ToArray());
            CollectionAssert.AreEqual(new[] { 1, 50, 75, 20, 20, 20 }, Db.Item(11).blueprint.Select(b => b.amount).ToArray());
            CollectionAssert.AreEqual(new[] { 50, 200, 200, 30, 100, 150, 150, 120, 125 }, Db.Item(85).blueprint.Select(b => b.amount).ToArray());
            CollectionAssert.AreEqual(new[] { 930, 930, 930, 930 }, Db.Item(210).blueprint.Select(b => b.amount).ToArray());
            Assert.AreEqual(1, Blueprints.Total(Db, 11, 0));
            Assert.AreEqual(20, Blueprints.Total(Db, 11, 5));
            Assert.AreEqual(0, Blueprints.Total(Db, 11, 6), "past the recipe");
            Assert.AreEqual(0, Blueprints.Total(Db, 0, 0), "not a product");
        }

        [Test]
        public void AutocompletePriceIsMaxPriceTimesQuantityTimesOneAndAQuarter()
        {
            foreach (var row in Autocomplete) Assert.AreEqual(row[1], Blueprints.AutoCompletePrice(Db, row[0]), "product " + row[0]);
            Assert.AreEqual(0, Blueprints.AutoCompletePrice(Db, 99999));
        }

        [Test]
        public void FireworksAutocompleteWithinFloatRounding()
        {
            // 10 x 1 573 250 x 1.25 = 19 665 625 (the Python tool, in double); the original converts to float first and the
            // product lies above 2^24, where a float steps by 2: the port's float result may differ by one
            Assert.AreEqual(19665625, Blueprints.AutoCompletePrice(Db, 232), 2);
        }

        [Test]
        public void ChromoPlasmaAutocompleteIsTwoMillionPlusTheRest()
        {
            // 2 000 000 + 930 x (37 + 42 + 52 + 84) mid prices of the four plasmas = 2 199 950 (blueprint_table.py)
            Assert.AreEqual(2199950, Blueprints.AutoCompletePrice(Db, 210));
            // the story's step 141 invests 847 / 834 / 861 / 892: 83 x 37 + 96 x 42 + 69 x 52 + 38 x 84 = 13 883 left
            Blueprints.UnlockFromStory(Db, 141);
            Assert.AreEqual(2013883, Blueprints.AutoCompletePrice(Db, 210));
        }

        [Test]
        public void ANewStateNeedsTheWholeRecipe()
        {
            var s = Blueprints.State(Db, 11);
            CollectionAssert.AreEqual(new[] { 1, 50, 75, 20, 20, 20 }, s.remaining);
            Assert.AreEqual(-1, s.station);
            Assert.IsTrue(Blueprints.IsEmpty(s));
            Assert.IsFalse(Blueprints.IsCompleted(s));
            Assert.AreSame(s, Blueprints.State(Db, 11), "one state per product");
            Assert.AreEqual(1, Session.Blueprints.Count);
        }

        [Test]
        public void InvestingFixesTheProductionStationAndCountsTheMoney()
        {
            Blueprints.Invest(Db, 11, 155, 20, 70);
            var s = Blueprints.State(Db, 11);
            Assert.AreEqual(30, s.remaining[1]);
            Assert.AreEqual(20, Blueprints.Invested(Db, s, 1));
            Assert.AreEqual(20 * ((150 + 165) / 2), s.moneySpent, "Titanium's single price 157");
            Assert.AreEqual(70, s.station);
            Blueprints.Invest(Db, 11, 120, 5, 15);
            Assert.AreEqual(70, s.station, "the first investment's station stays");
            Assert.IsFalse(Blueprints.IsEmpty(s));
        }

        [Test]
        public void InvestIgnoresOtherItemsAndNothing()
        {
            Blueprints.Invest(Db, 11, 122, 0, 70);
            Blueprints.Invest(Db, 11, 109, 5, 70);   // Buskat isn't in the recipe
            var s = Blueprints.State(Db, 11);
            CollectionAssert.AreEqual(new[] { 1, 50, 75, 20, 20, 20 }, s.remaining);
            Assert.AreEqual(0, s.moneySpent);
            Assert.AreEqual(-1, s.station);
        }

        [Test]
        public void CompletionIsTheMeanOfTheIngredients()
        {
            // BluePrint::getCompletionRate: each ingredient weighs the same
            var s = Blueprints.State(Db, 11);
            Assert.AreEqual(0f, Blueprints.CompletionRate(Db, s));
            Blueprints.Invest(Db, 11, 9, 1, 70);      // 1 / 1
            Blueprints.Invest(Db, 11, 155, 25, 70);   // 25 / 50
            Assert.AreEqual((1f + 0.5f) / 6f, Blueprints.CompletionRate(Db, s), 1e-5f);
            Blueprints.Invest(Db, 11, 120, 75, 70);
            Blueprints.Invest(Db, 11, 126, 20, 70);
            Blueprints.Invest(Db, 11, 122, 20, 70);
            Blueprints.Invest(Db, 11, 129, 20, 70);
            Blueprints.Invest(Db, 11, 155, 25, 70);
            Assert.AreEqual(1f, Blueprints.CompletionRate(Db, s), 1e-5f);
            Assert.IsTrue(Blueprints.IsCompleted(s));
        }

        [Test]
        public void ProducedAtTheProductionStationGoesToTheHold()
        {
            Blueprints.Invest(Db, 11, 155, 50, 70);
            Assert.IsTrue(Blueprints.Produce(Db, 11, 70));
            Assert.AreEqual(1, Shop.CargoOf(11));
            var s = Blueprints.State(Db, 11);
            CollectionAssert.AreEqual(new[] { 1, 50, 75, 20, 20, 20 }, s.remaining, "BluePrint::reset");
            Assert.AreEqual(0, s.moneySpent);
            Assert.AreEqual(-1, s.station);
            Assert.AreEqual(1, s.timesProduced);
            Assert.AreEqual(1, Session.GoodsProduced);
        }

        [Test]
        public void ProducedElsewhereWaitsAtTheProductionStation()
        {
            Blueprints.Invest(Db, 37, 128, 2, 70);
            Assert.IsFalse(Blueprints.Produce(Db, 37, 15));
            Assert.AreEqual(0, Shop.CargoOf(37));
            Assert.AreEqual(1, Session.PendingProducts.Count);
            Assert.AreEqual((37, 10, 70), (Session.PendingProducts[0].item, Session.PendingProducts[0].quantity, Session.PendingProducts[0].station));
            Blueprints.Invest(Db, 37, 128, 2, 70);
            Blueprints.Produce(Db, 37, 15);
            Assert.AreEqual(1, Session.PendingProducts.Count, "one row per product and station");
            Assert.AreEqual(20, Session.PendingProducts[0].quantity);
            Assert.AreEqual(2, Session.GoodsProduced);
        }

        [Test]
        public void ProducingAnUnstartedBlueprintMakesItHere()
        {
            Assert.IsTrue(Blueprints.Produce(Db, 67, 15));
            Assert.AreEqual(1, Shop.CargoOf(67));
        }

        [Test]
        public void CollectedProductsGoToTheHoldOrTheMountedStack()
        {
            Session.PendingProducts.Add(new PendingProduct { item = 67, quantity = 1, station = 70 });
            Session.PendingProducts.Add(new PendingProduct { item = 36, quantity = 10, station = 70 });   // Edo is mounted
            Session.PendingProducts.Add(new PendingProduct { item = 37, quantity = 10, station = 15 });
            var moved = Blueprints.CollectPending(Db, 70);
            Assert.AreEqual(2, moved.Count);
            Assert.AreEqual(1, Shop.CargoOf(67));
            Assert.AreEqual(16, AmountMounted(36), "secondaries join a mounted stack");
            Assert.AreEqual(0, Shop.CargoOf(36));
            Assert.AreEqual(1, Session.PendingProducts.Count);
            Assert.AreEqual(15, Session.PendingProducts[0].station);
        }

        [Test]
        public void FitStretchesOrTrimsAStateToTheRecipe()
        {
            var shortState = new BlueprintState { item = 11, remaining = new List<int> { 0 } };
            Blueprints.Fit(Db, shortState);
            CollectionAssert.AreEqual(new[] { 0, 50, 75, 20, 20, 20 }, shortState.remaining, "new ingredients need their whole amount");
            var longState = new BlueprintState { item = 11, remaining = new List<int> { 1, 2, 3, 4, 5, 6, 7, 8 } };
            Blueprints.Fit(Db, longState);
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5, 6 }, longState.remaining);
            var empty = new BlueprintState { item = 85 };
            Session.Blueprints.Add(empty);
            Assert.AreEqual(0f, Blueprints.CompletionRate(Db, empty));
            Assert.AreEqual(9, empty.remaining.Count, "the reader fits it before reading (the 2026-10-11 crash)");
            Assert.AreEqual(0, Blueprints.Invested(Db, empty, 8));
        }

        [Test]
        public void ResetAtStationForgetsWhatWasInvestedThere()
        {
            // index 77 -> 78: what was invested at the Valkyrie (101) is lost
            Blueprints.Invest(Db, 11, 155, 10, 101);
            Blueprints.Invest(Db, 67, 118, 10, 70);
            Blueprints.ResetAtStation(Db, 101);
            var s = Blueprints.State(Db, 11);
            Assert.AreEqual(50, s.remaining[1]);
            Assert.AreEqual(-1, s.station);
            Assert.IsTrue(Blueprints.IsEmpty(s));
            Assert.AreEqual(40, Blueprints.State(Db, 67).remaining[0], "another station's untouched");
        }

        [Test]
        public void CargoHelpsWhenANeededIngredientIsInTheHold()
        {
            var s = Blueprints.State(Db, 11);
            Assert.IsFalse(Blueprints.CargoHelps(Db, s));
            Shop.AddToCargo(155, 1);
            Assert.IsTrue(Blueprints.CargoHelps(Db, s));
            Blueprints.Invest(Db, 11, 155, 50, 70);
            Assert.IsFalse(Blueprints.CargoHelps(Db, s), "Titanium is no longer needed");
        }

        [Test]
        public void VolatileGoodsAndGateRoutes()
        {
            // blueprint_table.py: VOLATILE (204, 209) can't be shipped; NEEDS_ROUTES (210, 223)
            Assert.IsTrue(Blueprints.IsVolatile(204));
            Assert.IsTrue(Blueprints.IsVolatile(209));
            Assert.IsFalse(Blueprints.IsVolatile(122));
            Assert.AreEqual(200, Blueprints.ShippingPerUnit);
            Assert.IsFalse(Blueprints.CanStartIn(Db, 210, 78), "Mido has no gate routes");
            Assert.IsFalse(Blueprints.CanStartIn(Db, 223, 50), "Y'mirr has none either");
            Assert.IsTrue(Blueprints.CanStartIn(Db, 210, 70));
            Assert.IsTrue(Blueprints.CanStartIn(Db, 11, 78), "the others anywhere");
        }

        [Test]
        public void StoryUnlocksWithPreInvestedIngredients()
        {
            // blueprint_table.py CAMPAIGN: step 34 -> 85 with 50 Void Crystals at Thynome (10)
            Blueprints.UnlockFromStory(Db, 34);
            Assert.IsTrue(Blueprints.IsUnlocked(85));
            var k = Blueprints.State(Db, 85);
            Assert.AreEqual(0, k.remaining[0]);
            Assert.AreEqual(10, k.station);
            Assert.AreEqual(50 * 42, k.moneySpent);
            Assert.AreEqual(1f / 9f, Blueprints.CompletionRate(Db, k), 1e-5f);
            // 58 -> Liberator with 5 Microchips at the Valkyrie (101)
            Blueprints.UnlockFromStory(Db, 58);
            var l = Blueprints.State(Db, 179);
            Assert.IsTrue(Blueprints.IsUnlocked(179));
            Assert.AreEqual(145, l.remaining[3]);
            Assert.AreEqual(101, l.station);
            // 72 -> Disruptor Laser + 1 Void Essence into the hold
            Blueprints.UnlockFromStory(Db, 72);
            Assert.IsTrue(Blueprints.IsUnlocked(183));
            Assert.AreEqual(1, Shop.CargoOf(175));
            Assert.IsTrue(Blueprints.IsEmpty(Blueprints.State(Db, 183)));
            // 104 -> Gamma Shield II with 10 Hypanium at Thynome
            Blueprints.UnlockFromStory(Db, 104);
            Assert.AreEqual(90, Blueprints.State(Db, 206).remaining[3]);
            Assert.AreEqual(10, Blueprints.State(Db, 206).station);
            // 141 -> Chromo Plasma, remaining 83 / 96 / 69 / 38 at Var Lupra (112)
            Blueprints.UnlockFromStory(Db, 141);
            CollectionAssert.AreEqual(new[] { 83, 96, 69, 38 }, Blueprints.State(Db, 210).remaining);
            Assert.AreEqual(112, Blueprints.State(Db, 210).station);
            CollectionAssert.AreEquivalent(new[] { 85, 179, 183, 206, 210 }, Session.UnlockedBlueprints);
        }

        [Test]
        public void OtherStepsUnlockNothing()
        {
            foreach (int step in new[] { 0, 33, 35, 57, 100, 162 }) Blueprints.UnlockFromStory(Db, step);
            Assert.IsEmpty(Session.UnlockedBlueprints);
            Assert.IsEmpty(Session.Blueprints);
        }
    }
}
