using System;
using System.IO;
using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using NUnit.Framework;
using UnityEngine;
using Random = UnityEngine.Random;

namespace GoF2Remake.Tests
{
    public class ScavengerModeTests : SandboxTest
    {
        void Start(bool kaamo = false) => ScavengerMode.Start(kaamo);

        [Test]
        public void StartHasOnlyTheRequestedLoadoutAndAnOpenWorld()
        {
            Start();
            Assert.AreEqual(GameMode.Scavenger, Session.Mode);
            Assert.AreEqual(Economy.Default, Session.Economy);
            Assert.AreEqual(70, Session.StationIndex);
            Assert.AreEqual(0, Session.ShipIndex);
            CollectionAssert.AreEquivalent(new[] { 16, 41, 68, 82 }, Session.Equipment.Select(x => x.item));
            Assert.AreEqual(10, Session.Equipment.Single(x => x.item == 41).amount);
            Assert.IsEmpty(Session.Cargo);
            Assert.IsTrue(Session.FreePlay && Session.CompletedWorld);
            Assert.IsTrue(GalaxyMap.Visibility(Db).All(x => x));
            CollectionAssert.AreEquivalent(new[] { 70 }, Session.VisitedStations); // Revealed is not visited.
            Assert.AreEqual(2, Session.KaamoState);
            Session.ResetNewGame();
            Start(true);
            Assert.AreEqual(3, Session.KaamoState);
        }

        [Test]
        public void RecipesHaveTheAgreedCollectionWithAllCoresLastAndNoAlcohol()
        {
            Start();
            CollectionAssert.AreEquivalent(new[] { 210, 232 }, Blueprints.Products(Db).Select(x => x.index));
            var recipe = Db.Item(232).blueprint;
            int[] gear = { 228, 229, 183, 231, 230, 193, 178, 224, 182, 225, 54, 59, 67, 194,
                           195, 188, 80, 84, 85, 90, 96, 184, 220, 200, 206, 208, 223, 227 };
            CollectionAssert.AreEqual(gear, recipe.Take(28).Select(x => x.item));
            Assert.IsTrue(recipe.Take(30).All(x => x.amount == 1));
            CollectionAssert.AreEqual(new[] { 210, 209 }, recipe.Skip(28).Take(2).Select(x => x.item));
            CollectionAssert.AreEqual(new[] { 165, 166, 167, 168, 169, 170, 171, 172, 173, 174, 175, 218 },
                recipe.Skip(30).Select(x => x.item));
            Assert.IsTrue(recipe.Skip(30).All(x => x.amount == 5));
            Assert.IsFalse(recipe.Any(x => Session.IsBooze(x.item)));
            Assert.IsTrue(Db.Item(210).blueprint.All(x => x.amount == 917));
            Assert.AreEqual(10, Blueprints.BaseQuantity(Db.Item(232)));
            Assert.IsFalse(Blueprints.CanAutocomplete(210));
            Assert.IsFalse(Blueprints.CanAutocomplete(232));
            Blueprints.Unlock(85);
            Assert.IsFalse(Blueprints.IsUnlocked(85));
        }

        [Test]
        public void ShopsRespectTechFiveAndKeepSecondaryBlueprintWeaponsAndMutagen()
        {
            Start();
            var state = Random.state;
            try
            {
                Random.InitState(194);
                for (int pass = 0; pass < 4; pass++)
                    foreach (var station in Db.Stations)
                        foreach (var stack in Shop.GenerateItems(Db, station.index))
                        {
                            var item = Db.Item(stack.item);
                            Assert.AreNotEqual(232, item.index);
                            Assert.IsFalse(item.categoryId == 23 || item.categoryId == 24, item.name);
                            if (item.TypeId == 1)
                                Assert.IsTrue(Shop.StationStocks(station.techLevel, item.techLevel) || item.Attr(61, -1) == station.index, item.name);
                            if (item.TypeId == 0 || item.TypeId == 2 || item.TypeId == 3)
                                Assert.LessOrEqual(item.techLevel, 5, item.name);
                        }
                var weapons = Enumerable.Range(0, 100).SelectMany(_ => Shop.GenerateItems(Db, 105)).Select(x => x.item).ToArray();
                foreach (int item in new[] { 33, 37, 46, 179, 221, 226 }) CollectionAssert.Contains(weapons, item);
                Assert.IsTrue(Shop.GenerateItems(Db, 126).Any(x => x.item == 209));
                CollectionAssert.Contains(Shop.GenerateShips(Db, 10), 8);
                CollectionAssert.Contains(Shop.GenerateShips(Db, 120), 44);
            }
            finally { Random.state = state; }
        }

        [Test]
        public void LootContainsEveryPermittedItemWithoutCategoryOrOccurrenceWeights()
        {
            Start();
            int[] forbidden = { 115, 116, 117, 131, 154, 164, 165, 175, 201, 202, 203, 204, 209, 210, 217, 218, 232 };
            foreach (int id in forbidden) Assert.IsFalse(ScavengerMode.LootAllowed(Db.Item(id)), id.ToString());
            var counts = Db.Items.Where(ScavengerMode.LootAllowed).ToDictionary(x => x.index, _ => 0);
            var state = Random.state;
            try
            {
                Random.InitState(82714);
                for (int i = 0; i < 40000; i++)
                    foreach (var stack in NpcTables.RollLoot(Db, i % 2 == 0))
                    {
                        Assert.IsTrue(counts.ContainsKey(stack.item), stack.item.ToString());
                        counts[stack.item]++;
                    }
                double mean = counts.Values.Average();
                // Seeded integration sample: detects the old turret/commodity weighting and rarity exclusions.
                foreach (var pair in counts) Assert.That(pair.Value, Is.InRange(mean * 0.6, mean * 1.4), pair.Key.ToString());
            }
            finally { Random.state = state; }
        }

        [Test]
        public void OnlyManufacturingAwardsChampionAndItIsAwardedOnce()
        {
            Start();
            Session.Kills = 100000;
            Session.Credits = 100000000;
            Shop.AddToCargo(232, 10);
            Assert.IsEmpty(Achievements.Check(Db));
            Assert.IsFalse(Blueprints.Produce(Db, 232, 70));
            Assert.AreEqual(10, Shop.CargoOf(232));
            Assert.IsTrue(Achievements.NextGrade(Db, 35, out _, out int crafts, out int needed, out _));
            Assert.AreEqual(0, crafts);
            Assert.AreEqual(1, needed);
            foreach (var part in Db.Item(232).blueprint)
                Blueprints.Invest(Db, 232, part.item, part.amount, 70, true);
            Assert.IsTrue(Blueprints.Produce(Db, 232, 70));
            Assert.AreEqual(20, Shop.CargoOf(232));
            Assert.IsTrue(Achievements.NextGrade(Db, 35, out _, out crafts, out needed, out _));
            Assert.AreEqual(1, crafts);
            Assert.AreEqual(1, needed);
            CollectionAssert.AreEqual(new[] { 35 }, Achievements.Check(Db));
            Assert.IsEmpty(Achievements.Check(Db));
            Assert.AreEqual(1, Session.Medals.Count(x => x != 0));
        }

        [Test]
        public void SavesAreSeparateAndRestoreProductionAndCompletedWorld()
        {
            Assert.IsTrue(SaveGame.Save(0));
            string standard = File.ReadAllText(SaveGame.SlotPath(0));
            Start(true);
            Blueprints.Invest(Db, 232, 228, 1, 70, true);
            Session.Kills = 123;
            Assert.IsTrue(SaveGame.Save(0));
            string path = SaveGame.SlotPath(0);
            Session.ResetNewGame();
            Assert.AreEqual(standard, File.ReadAllText(SaveGame.SlotPath(0)));
            SaveGame.SelectedMode = GameMode.Scavenger;
            Assert.IsTrue(SaveGame.Load(0));
            Assert.IsTrue(Session.CompletedWorld && Session.FreePlay);
            Assert.AreEqual(Economy.Default, Session.Economy);
            Assert.AreEqual(123, Session.Kills);
            Assert.AreEqual(3, Session.KaamoState);
            Assert.AreEqual(1, Blueprints.Invested(Db, Blueprints.State(Db, 232), 0));
            Assert.IsTrue(SaveGame.TryParse(File.ReadAllText(path), Db, out _, out string problem), problem);
        }

        [Test]
        public void SaveImportRefusesToMixTheTwoGroups()
        {
            Start();
            Assert.IsTrue(SaveGame.Save(1));
            string saved = File.ReadAllText(SaveGame.SlotPath(1));
            // Build JSON with the existing serializer instead of manually escaping the nested save.
            var export = new TestBundle { slots = new[] { new TestSlot { slot = 1, json = saved } } };
            string file = Path.Combine(Sandbox, "scavenger.gof2saves");
            File.WriteAllText(file, JsonUtility.ToJson(export));
            SaveGame.SelectedMode = GameMode.Standard;
            Assert.AreEqual(SaveTransfer.Outcome.Invalid, SaveTransfer.Import(file).outcome);
            SaveGame.SelectedMode = GameMode.Scavenger;
            Assert.AreEqual(SaveTransfer.Outcome.Done, SaveTransfer.Import(file).outcome);
        }

        [Serializable] class TestBundle
        {
            public string format = "gof2remake-saves";
            public int formatVersion = 1, gameMode = 1;
            public TestSlot[] slots;
        }
        [Serializable] class TestSlot { public int slot; public string json; }

        [Test]
        public void PlayerCanSellAndBuyBackHighTechLoot()
        {
            Start();
            var stock = Shop.EnterStation(Db, 70);
            Shop.AddToCargo(225, 1);
            var hangar = new Hangar(Db, stock);
            Assert.AreEqual(Hangar.Result.Ok, hangar.Sell(225));
            Assert.Greater(hangar.StockOf(225), 0);
            Assert.AreEqual(Hangar.Result.Ok, hangar.Buy(225, out _));
            Assert.AreEqual(1, Shop.CargoOf(225));
        }

        [Test]
        public void LoungesKeepMissionsAndWingmenButNoBlueprintOrKaamoItemSeller()
        {
            Start(true);
            var previous = AgentGenerator.Rng;
            try
            {
                AgentGenerator.Rng = new System.Random(4717);
                bool missions = false, wingmen = false;
                for (int i = 0; i < 100; i++)
                    foreach (var agent in AgentGenerator.CreateAgents(Db, 70))
                    {
                        missions |= agent.offer == AgentOffer.Mission;
                        wingmen |= agent.offer == AgentOffer.Wingmen;
                        Assert.AreNotEqual(AgentOffer.SellBlueprint, agent.offer);
                        if (agent.offer == AgentOffer.SellItem)
                            Assert.IsTrue(ScavengerMode.ShopAllowed(Db.Item(agent.sellItem)));
                    }
                Assert.IsTrue(missions && wingmen);
                var club = AgentGenerator.CreateAgents(Db, 108);
                Assert.IsFalse(club.Any(a => a.offer == AgentOffer.KaamoSpecial));
                Assert.IsTrue(club.Any(a => a.offer == AgentOffer.ShipDealer));
                Assert.IsTrue(club.Any(a => a.offer == AgentOffer.SellMod));
            }
            finally { AgentGenerator.Rng = previous; }
        }

        [Test]
        public void ReturningToStandardRestoresItsDatabaseAndMedals()
        {
            int standardProducts = Blueprints.Products(Db).Count;
            Start();
            Assert.AreEqual(2, Blueprints.Products(Db).Count);
            Session.ResetNewGame();
            Assert.AreEqual(standardProducts, Blueprints.Products(Db).Count);
            Assert.IsTrue(Blueprints.CanAutocomplete(232));
            Achievements.Check(Db);
            Assert.AreEqual(1, Session.Medals[0]);
            Assert.AreEqual(GameMode.Standard, SaveGame.SelectedMode);
        }
    }
}
