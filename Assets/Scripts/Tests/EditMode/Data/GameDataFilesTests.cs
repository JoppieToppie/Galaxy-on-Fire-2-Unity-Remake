// GameDataFilesTests.cs
// CLAUDE.md "Data": every table in Resources/GoF2Data is valid JSON and Database.Load reads the original tables whole.
// Expected sizes: the original tables as CLAUDE.md "Mods" counts them (233 items, 64 ships, 34 systems, 135 stations; a
// mod's entries are numbered after them), item attribute 0 = the index and 1 = the type (CLAUDE.md "Data", Database's
// TypeId mapping from Reference/research/shop.md 2.4), the campaign's 163 steps (campaign_flow.md: steps 0-162) and the
// step summaries' gaps 53 / 129 (CLAUDE.md "Debug panel": "53 / 129 skipped"). The cross references (wanted criminals,
// bar agents, blueprint recipes) are checked against the tables themselves.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using GoF2Remake.Data;
using NUnit.Framework;
using UnityEngine;

namespace GoF2Remake.Tests
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public class GameDataFilesTests : SandboxTest
    {
        const string DataFolder = "Assets/Resources/GoF2Data";

        static IEnumerable<string> DataFiles() =>
            Directory.GetFiles(DataUiTestKit.FullPath(DataFolder), "*.json").Select(Path.GetFileName).OrderBy(f => f);

        [Test]
        public void TheDataFolderHasTheTablesTheGameReads()
        {
            var files = new HashSet<string>(DataFiles());
            foreach (var f in new[]
            {
                "items.json", "ships.json", "systems.json", "stations.json", "assemblies.json", "weapons_hd.json", "wanted.json",
                "item_attributes.json", "economy_default.json", "weapons_hd_default.json", "story.json", "collision.json", "static_collisions.json",
                "wreck_collisions.json", "hangar_heights.json", "voice_table.json", "step_summaries.json", "void_station_extra.json",
                "agents.json", "names.json", "ticker.json", "docks_hd.json", "resources.json", "weapon_fx.json",
            })
                Assert.That(files, Does.Contain(f), "Resources/GoF2Data/" + f);
        }

        [TestCaseSource(nameof(DataFiles))]
        public void EveryDataFileIsValidJson(string file)
        {
            var duplicates = new List<string>();
            object doc = null;
            Assert.DoesNotThrow(() => doc = DataUiTestKit.ParseJson(DataUiTestKit.ReadAsset(DataFolder + "/" + file), duplicates), file);
            Assert.IsNotNull(doc, file + " is empty");
            Assert.IsEmpty(duplicates, file + ": duplicate keys");
            // Resources loads it as a TextAsset (Database.Read).
            Assert.IsNotNull(Resources.Load<TextAsset>("GoF2Data/" + Path.GetFileNameWithoutExtension(file)), file);
        }

        [Test]
        public void DatabaseLoadsTheOriginalTableSizes()
        {
            var db = Database.Load();
            Assert.AreEqual(GameNames.OriginalItems, Modding.ModContent.OriginalItemCount, "items before any mod");
            Assert.AreEqual(GameNames.OriginalShips, Modding.ModContent.OriginalShipCount, "ships before any mod");
            Assert.AreEqual(233, GameNames.OriginalItems);
            Assert.AreEqual(64, GameNames.OriginalShips);
            Assert.GreaterOrEqual(db.Items.Count, 233);
            Assert.GreaterOrEqual(db.Ships.Count, 64);
            Assert.GreaterOrEqual(db.Systems.Count, 34);
            Assert.GreaterOrEqual(db.Stations.Count, 135);
            Assert.Greater(db.Assemblies.Count, 0);
            Assert.Greater(db.WeaponMounts.Count, 0);
            Assert.Greater(db.Wanted.Count, 0);
        }

        [Test]
        public void TableIndicesMatchTheirPlaces()
        {
            for (int i = 0; i < 233; i++) Assert.AreEqual(i, Db.Items[i].index, "item");
            for (int i = 0; i < 64; i++) Assert.AreEqual(i, Db.Ships[i].index, "ship");
            for (int i = 0; i < 34; i++) Assert.AreEqual(i, Db.Systems[i].index, "system");
            for (int i = 0; i < 135; i++) Assert.AreEqual(i, Db.Stations[i].index, "station");
            // Database.Item / Ship find them by number
            Assert.AreSame(Db.Items[122], Db.Item(122));
            Assert.AreSame(Db.Ships[10], Db.Ship(10));
            Assert.IsNull(Db.Item(-1));
            Assert.IsNull(Db.Ship(-1));
        }

        [Test]
        public void EveryItemHasItsOriginalAttributes()
        {
            // item_attributes.json: attribute 0 = Item+0x0 index, 1 = Item+0x4 type (0 primary .. 4 commodity)
            var types = new Dictionary<string, int> { { "primary", 0 }, { "secondary", 1 }, { "turret", 2 }, { "equipment", 3 }, { "commodity", 4 } };
            for (int i = 0; i < 233; i++)
            {
                var it = Db.Items[i];
                Assert.IsTrue(it.attrKeys.Length > 0 && it.attrKeys.Length == it.attrValues.Length, $"item {i} attributes");
                Assert.AreEqual(i, it.Attr(0, -1), $"item {i}: attribute 0");
                Assert.IsTrue(types.ContainsKey(it.type), $"item {i}: type '{it.type}'");
                Assert.AreEqual(types[it.type], it.Attr(1, -1), $"item {i}: attribute 1 vs '{it.type}'");
                Assert.AreEqual(types[it.type], it.TypeId, $"item {i}: TypeId");
            }
        }

        [Test]
        public void ItemPricesAndTechLevelsAreSane()
        {
            for (int i = 0; i < 233; i++)
            {
                var it = Db.Items[i];
                Assert.That(it.minPrice, Is.GreaterThanOrEqualTo(0), $"item {i} minPrice");
                Assert.That(it.maxPrice, Is.GreaterThanOrEqualTo(it.minPrice), $"item {i} maxPrice");
                Assert.That(it.techLevel, Is.InRange(0, 10), $"item {i} tech level");
            }
        }

        [Test]
        public void BlueprintRecipesNameExistingItems()
        {
            int recipes = 0;
            for (int i = 0; i < 233; i++)
                foreach (var part in Db.Items[i].blueprint)
                {
                    recipes++;
                    Assert.That(part.item, Is.InRange(0, 232), $"item {i}'s recipe");
                    Assert.Greater(part.amount, 0, $"item {i}'s recipe amount");
                    Assert.AreEqual(Db.Items[part.item].name, part.name, $"item {i}'s recipe names item {part.item}");
                }
            // Blueprints: 25 products (CLAUDE.md "Bar and freelance")
            Assert.AreEqual(25, Enumerable.Range(0, 233).Count(i => Db.Items[i].blueprint.Count > 0), "blueprint products");
            Assert.Greater(recipes, 25);
        }

        [Test]
        public void WantedCriminalsReferToRealShipsItemsAndPortraits()
        {
            Assert.AreEqual(25, Db.Wanted.Count, "wanted.json (Most Wanted board)");
            foreach (var w in Db.Wanted)
            {
                Assert.That(w.ship, Is.InRange(0, 63), w.name);
                Assert.IsNotNull(Db.ShipAssembly(w.ship), w.name + "'s ship model");
                Assert.That(w.loot, Is.InRange(0, 232), w.name + "'s loot");
                Assert.That(w.weapon, Is.InRange(0, 232), w.name + "'s weapon");
                Assert.Greater(w.hitpoints, 0, w.name);
                Assert.Greater(w.reward, 0, w.name);
                Assert.AreEqual(5, w.portraitParts?.Length ?? 0, w.name + "'s portrait");
            }
        }

        [System.Serializable] class AgentRow { public int index, station, system, race, sellItemSystem, sellBlueprint, sellMod, sellItemPrice; public string name; public int[] portraitParts; }
        [System.Serializable] class AgentList { public List<AgentRow> list; }

        [Test]
        public void StoryAgentsSitAtTheirSystemsStations()
        {
            var agents = JsonUtility.FromJson<AgentList>("{\"list\":" + Resources.Load<TextAsset>("GoF2Data/agents").text + "}").list;
            Assert.Greater(agents.Count, 0);
            foreach (var a in agents)
            {
                Assert.That(a.station, Is.InRange(0, 134), a.name);
                Assert.AreEqual(Db.Stations[a.station].system, a.system, a.name + ": the station's system");
                if (a.sellItemSystem >= 0) Assert.That(a.sellItemSystem, Is.InRange(0, 33), a.name + " sells a system's coordinates");
                if (a.sellBlueprint >= 0) Assert.Greater(Db.Items[a.sellBlueprint].blueprint.Count, 0, a.name + " sells a blueprint");
                if (a.sellMod >= 0) Assert.That(a.sellMod, Is.InRange(0, 3), a.name + " sells a Kaamo mod (hull, cargo, slot, handling)");
            }
            // Kaamo Club's own agents 21-26 at station 108 (CLAUDE.md "Kaamo Club": "agents 21-26")
            for (int i = 21; i <= 26; i++) Assert.AreEqual(108, agents[i].station, "agent " + i);
        }

        [Test]
        public void StepSummariesCoverEveryStep()
        {
            for (int i = 0; i <= 162; i++)
            {
                var e = StepSummaries.Get(i);
                Assert.IsNotNull(e, "step " + i);
                Assert.AreEqual(i, e.index);
                if (i == 53 || i == 129) continue;   // never stood on (52 -> 54, 128 -> 130)
                Assert.IsFalse(string.IsNullOrWhiteSpace(e.title), "step " + i + " has a title");
            }
        }
    }
}
