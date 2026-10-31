// FighterPoolTests.cs
// Feature: CLAUDE.md "NPCs and combat" (the fighters each race flies, freighters, loot) and "Shop > Dealer extras" (after
// Valkyrie the Vossk fly 60 / 25 / 15 % 9 / 41 / 39): FighterPool / NpcTables.RandomFighter / StrongFighter /
// RandomFreighter / FreighterAssembly / RollLoot.
// Expected values: Reference/research/npc_traffic_ai.md 2.5 (Globals::getRandomEnemyFighter pools, freighter races and
// cargo prefabs) and 2.6 (Generator::getLootList), Reference/tools/npc/npc_tables.py fighter_pool (same pools, run by
// hand: the Vossk 0.60 / 0.25 / 0.15 split). Draw frequencies are checked statistically with a fixed
// UnityEngine.Random seed and wide tolerances.

using System.Collections.Generic;
using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using NUnit.Framework;
using UnityEngine;

namespace GoF2Remake.Tests
{
    public class FighterPoolTests : NpcTestBase
    {
        static Dictionary<int, int> Draw(int race, int n, int seed)
        {
            Random.InitState(seed);
            var counts = new Dictionary<int, int>();
            for (int i = 0; i < n; i++)
            {
                int s = NpcTables.RandomFighter(race);
                counts[s] = counts.TryGetValue(s, out int c) ? c + 1 : 1;
            }
            return counts;
        }

        [TestCase(0)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(8)]
        [TestCase(9)]
        [TestCase(10)]
        public void EachRaceFliesItsWholePool(int race)
        {
            InFreePlay();
            var pool = Pool(race);
            var counts = Draw(race, 300 * pool.Length, 11 + race);
            CollectionAssert.AreEquivalent(pool, counts.Keys.ToArray(), "only and every ship of the pool");
            foreach (var kv in counts)   // uniform: each about n / pool
                Assert.AreEqual(300, kv.Value, 75, "ship " + kv.Key);
        }

        [TestCase(4)]
        [TestCase(5)]
        [TestCase(6)]
        [TestCase(7)]
        [TestCase(11)]
        [TestCase(15)]
        public void MinorRacesFlyThePiratesShips(int race)
        {
            InFreePlay();
            var counts = Draw(race, 700, 3 + race);
            CollectionAssert.IsSubsetOf(counts.Keys.ToArray(), Pool(8));
        }

        [Test]
        public void VosskFlyOnlyTheHSocBeforeValkyrieIsWon()
        {
            InStory(83);
            Assert.IsFalse(GoF2Remake.Data.Story.Dlc1Won);
            CollectionAssert.AreEquivalent(new[] { 9 }, Draw(1, 500, 5).Keys.ToArray());
        }

        [Test]
        public void VosskSplitAfterValkyrie()
        {
            // fighter_pool(1, dlc1_won=True) = [(9, .60), (41, .25), (39, .15)].
            InStory(84);
            Assert.IsTrue(GoF2Remake.Data.Story.Dlc1Won);
            const int n = 6000;
            var c = Draw(1, n, 77);
            CollectionAssert.AreEquivalent(new[] { 9, 41, 39 }, c.Keys.ToArray());
            Assert.AreEqual(0.60, c[9] / (double)n, 0.03);
            Assert.AreEqual(0.25, c[41] / (double)n, 0.03);
            Assert.AreEqual(0.15, c[39] / (double)n, 0.03);
        }

        [Test]
        public void VosskSplitInTheFinishedGamesWorld()
        {
            InCompletedWorld();
            CollectionAssert.AreEquivalent(new[] { 9, 41, 39 }, Draw(1, 2000, 9).Keys.ToArray());
        }

        [TestCase(0)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(8)]
        public void StrongFightersAreTheDearerHalf(int race)
        {
            // FighterPool.Strong: the dearer half (rounded up) of the race's pool by ships.json price.
            InFreePlay();
            var pool = Pool(race);
            var byPrice = pool.OrderByDescending(s => Db.Ship(s).price).ToArray();
            var dearer = byPrice.Take((pool.Length + 1) / 2).ToArray();
            Random.InitState(42 + race);
            var seen = new HashSet<int>();
            for (int i = 0; i < 100 * dearer.Length; i++) seen.Add(NpcTables.StrongFighter(Db, race));
            CollectionAssert.AreEquivalent(dearer, seen.ToArray());
        }

        [Test]
        public void StrongVosskFighters()
        {
            InFreePlay();
            Random.InitState(3);
            for (int i = 0; i < 50; i++) Assert.AreEqual(9, NpcTables.StrongFighter(Db, 1), "only the H'Soc before Valkyrie");
            InStory(90);
            var pool = new[] { 9, 41, 39 };
            var dearer = pool.OrderByDescending(s => Db.Ship(s).price).Take(2).ToArray();
            var seen = new HashSet<int>();
            for (int i = 0; i < 200; i++) seen.Add(NpcTables.StrongFighter(Db, 1));
            CollectionAssert.AreEquivalent(dearer, seen.ToArray());
        }

        // ---- freighters ----------------------------------------------------------------------------------------

        [TestCase(0, 2)]
        [TestCase(2, 0)]
        public void FreightersSwapTerranAndNivelian30Percent(int systemRace, int other)
        {
            // npc_traffic_ai.md 2.5: r < 30 -> Terran systems Nivelian freighters and the other way round; ship 15.
            Random.InitState(100 + systemRace);
            int swapped = 0;
            const int n = 4000;
            for (int i = 0; i < n; i++)
            {
                var (ship, race) = NpcTables.RandomFreighter(systemRace);
                Assert.AreEqual(15, ship);
                Assert.That(race == systemRace || race == other);
                if (race == other) swapped++;
            }
            Assert.AreEqual(0.30, swapped / (double)n, 0.03);
        }

        [TestCase(1, 13)]
        [TestCase(3, 15)]
        public void VosskAndMidorianFreightersAlwaysTheirOwn(int systemRace, int ship)
        {
            Random.InitState(7);
            for (int i = 0; i < 500; i++)
                Assert.AreEqual((ship, systemRace), NpcTables.RandomFreighter(systemRace));
        }

        [Test]
        public void FreighterPrefabs()
        {
            Assert.AreEqual("cargo_003_terran", NpcTables.FreighterAssembly(0));
            Assert.AreEqual("cargo_004_vossk", NpcTables.FreighterAssembly(1));
            Assert.AreEqual("cargo_002_nivelian", NpcTables.FreighterAssembly(2));
            Assert.AreEqual("cargo_001_midorian", NpcTables.FreighterAssembly(3));
            Assert.AreEqual("cargo_003_terran", NpcTables.FreighterAssembly(8));
        }

        // ---- loot (Generator::getLootList) ---------------------------------------------------------------------

        static List<List<ItemStack>> Loot(int n, int seed, bool freighter, Economy economy = Economy.Android)
        {
            var before = Session.Economy;
            Session.Economy = economy;   // the empty roll differs between the builds (Android 1 in 3, iOS / Default 1 in 9)
            try
            {
                Random.InitState(seed);
                var all = new List<List<ItemStack>>();
                for (int i = 0; i < n; i++) all.Add(NpcTables.RollLoot(Db, freighter));
                return all;
            }
            finally { Session.Economy = before; }
        }

        [Test]
        public void LootIsEmptyAThirdOfTheTimeElseOneOrTwoEntries()
        {
            // n = nextInt(3); nextInt(3) == 0 -> nothing; entries = max(1, n): 1 with 2/3, 2 with 1/3 of the rest.
            const int n = 3000;
            var all = Loot(n, 21, false);
            int empty = all.Count(l => l.Count == 0), two = all.Count(l => l.Count == 2);
            Assert.AreEqual(1.0 / 3, empty / (double)n, 0.04);
            Assert.That(all.All(l => l.Count <= 2));
            Assert.AreEqual(1.0 / 3, two / (double)(n - empty), 0.05);
        }

        [Test]
        public void LootIsEmptyOneTimeInNineOnTheIosBuild()
        {
            // iOS 1.1.19 (the Default economy): the second nextInt(3) only when the first gave 0 entries.
            const int n = 3000;
            var all = Loot(n, 23, false, Economy.Default);
            int empty = all.Count(l => l.Count == 0);
            Assert.AreEqual(1.0 / 9, empty / (double)n, 0.03);
            Assert.That(all.All(l => l.Count <= 2));
        }

        [Test]
        public void LootEntriesFollowTheRules()
        {
            // accept: no blueprint, price > 0, not 164 / 175 / 217 / 218; commodities 1-9 t, equipment 1-3; the fallback ore
            // 154 + nextInt(10) at 1-9.
            foreach (var list in Loot(2000, 22, false))
                foreach (var e in list)
                {
                    var it = Db.Item(e.item);
                    Assert.IsNotNull(it);
                    CollectionAssert.DoesNotContain(new[] { 164, 175, 217, 218 }, e.item);
                    Assert.AreEqual(0, it.blueprint.Count, it.name);
                    Assert.Greater(it.maxPrice, 0, it.name);
                    if (it.TypeId == 4 || (e.item >= 154 && e.item <= 163)) Assert.That(e.amount >= 1 && e.amount <= 9, it.name + " " + e.amount);
                    else Assert.That(e.amount >= 1 && e.amount <= 3, it.name + " " + e.amount);
                }
        }

        [Test]
        public void FreighterLootAtLeastEightOfEach()
        {
            // freighters: amount x (nextInt(4) + 2), at least nextInt(5) + 8.
            foreach (var list in Loot(1500, 23, true))
                foreach (var e in list) Assert.GreaterOrEqual(e.amount, 8);
        }

        [Test]
        public void JumpDriveWithoutCellsBringsEnergyCells()
        {
            // 10 %: the first entry becomes Energy Cells (122) when the player has a jump drive and none in the hold.
            int Cells(List<List<ItemStack>> all) => all.Count(l => l.Count > 0 && l[0].item == GalaxyMap.EnergyCellItem);
            var without = Loot(3000, 24, false);
            int nonEmpty = without.Count(l => l.Count > 0);
            Assert.Less(Cells(without) / (double)nonEmpty, 0.04, "without a drive cells are only rolled by chance");
            Session.Equipment.Add(new ItemStack(GalaxyMap.KhadorDriveItem, 1));
            Assert.IsTrue(GalaxyMap.HasJumpDrive(Db));
            var with = Loot(3000, 24, false);
            nonEmpty = with.Count(l => l.Count > 0);
            Assert.AreEqual(0.10, Cells(with) / (double)nonEmpty, 0.04);
            Session.Cargo.Add(new ItemStack(GalaxyMap.EnergyCellItem, 3));
            var stocked = Loot(3000, 24, false);
            Assert.Less(Cells(stocked) / (double)stocked.Count(l => l.Count > 0), 0.04, "cells in the hold: no extra cells");
        }
    }
}
