// SaveParseTests.cs
// Feature: CLAUDE.md "UI and platforms" > Save export / import: SaveGame.TryParse checks a save from outside before it can
// reach the game: version 1..current, campaign, economy, finite times, difficulty 0-2, story index 0-162, and every
// station / ship / item / system index it names (cargo, equipment, Kaamo storage, the parked ship, shop memory and bar
// visitors, blueprints, production, the Most Wanted board) against the game's tables (items 0-232, ships 0-63, systems
// 0-33, stations 0-134 in Resources/GoF2Data); a modded number the save itself names (modKeys) is fine. Each case below
// breaks one thing of an otherwise valid save.

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using GoF2Remake.Data;
using GoF2Remake.Modding;
using NUnit.Framework;
using UnityEngine;

namespace GoF2Remake.Tests
{
    public class SaveParseTests : SavesModsTest
    {
        static SaveData Valid() => new SaveData
        {
            station = 78, previousStation = -1, ship = 10, credits = 100, campaignMission = 0, difficulty = 0.5f,
            economy = (int)Economy.Android, playSeconds = 10f,
            equipment = new List<ItemStack> { new ItemStack(2, 1), new ItemStack(36, 6) },
            cargo = new List<ItemStack> { new ItemStack(155, 3) },
        };

        static bool Parse(string json, out SaveData save, out string problem) => SaveGame.TryParse(json, Database.Load(), out save, out problem);

        static string Refusal(Action<SaveData> change)
        {
            var s = Valid();
            change(s);
            bool ok = Parse(JsonUtility.ToJson(s), out var parsed, out string problem);
            Assert.IsFalse(ok, "accepted");
            Assert.IsNull(parsed);
            Assert.IsFalse(string.IsNullOrEmpty(problem));
            return problem;
        }

        [Test]
        public void AValidSaveIsAccepted()
        {
            Assert.IsTrue(Parse(JsonUtility.ToJson(Valid()), out var s, out string problem), problem);
            Assert.IsNull(problem);
            Assert.AreEqual(78, s.station);
            Assert.AreEqual(100, s.credits);
        }

        [Test]
        public void AFileTheGameWroteIsAccepted()
        {
            Session.Credits = 5;
            Session.Cargo = Stacks((155, 2));
            SaveGame.Save(2);
            Assert.IsTrue(Parse(System.IO.File.ReadAllText(SaveGame.SlotPath(2)), out _, out string problem), problem);
        }

        [Test]
        public void TheEdgesOfEveryRangeAreAccepted()
        {
            foreach (var change in new Action<SaveData>[]
            {
                s => s.version = 1, s => s.version = SaveData.CurrentVersion,
                s => s.campaign = (int)Campaign.Supernova, s => s.economy = (int)Economy.Default,
                s => s.difficulty = 0f, s => s.difficulty = 2f, s => s.campaignMission = 162,
                s => s.station = 0, s => s.station = 134, s => s.station = Session.VoidOrbit, s => s.previousStation = 134,
                s => s.ship = 63, s => s.credits = int.MaxValue, s => s.cargo.Add(new ItemStack(232, 0)),
                s => s.voidInvasionSystem = 33, s => s.voidInvasionStation = 134,
                // A finished game: nothing attacked any more (Story.OnDepart writes -10; #117 refused it).
                s => { s.voidInvasionSystem = -10; s.voidInvasionStation = -10; },
                s => s.version = 9, // an unknown economy number before version 10 isn't checked (the economy was always Android)
            })
            {
                var s = Valid();
                change(s);
                if (s.version == 9) s.economy = 7;
                Assert.IsTrue(Parse(JsonUtility.ToJson(s), out _, out string problem), problem);
            }
        }

        [TestCase("", "not a save")]
        [TestCase("   ", "not a save")]
        [TestCase("[1, 2]", "not a save")]
        [TestCase("null", "not a save")]
        [TestCase("{}", "fields missing")]
        [TestCase("{ \"version\": 17 }", "fields missing")]
        [TestCase("{ \"version\": 17, \"ship\": 10, \"station\": ", "unreadable")]
        public void TextThatIsNoSaveIsRefused(string json, string expected)
        {
            Assert.IsFalse(Parse(json, out var s, out string problem));
            Assert.IsNull(s);
            StringAssert.Contains(expected, problem);
        }

        [Test]
        public void NullTextIsRefused()
        {
            Assert.IsFalse(Parse(null, out _, out string problem));
            StringAssert.Contains("not a save", problem);
        }

        static IEnumerable<TestCaseData> Broken()
        {
            TestCaseData C(string name, Action<SaveData> change, string expected) => new TestCaseData(change, expected).SetName("Refuses_" + name);
            yield return C("Version0", s => s.version = 0, "no version");
            yield return C("NewerVersion", s => s.version = SaveData.CurrentVersion + 1, "newer version");
            yield return C("NegativeCampaign", s => s.campaign = -1, "unknown campaign");
            yield return C("Campaign3", s => s.campaign = 3, "unknown campaign");
            yield return C("Economy2", s => s.economy = 2, "unknown economy");
            yield return C("NegativePlayingTime", s => s.playSeconds = -1f, "playing time");
            yield return C("NegativeDifficulty", s => s.difficulty = -0.5f, "difficulty");
            yield return C("Difficulty2_5", s => s.difficulty = 2.5f, "difficulty");
            yield return C("NegativeStoryIndex", s => s.campaignMission = -1, "story mission");
            yield return C("StoryIndex163", s => s.campaignMission = 163, "story mission");
            yield return C("Station135", s => s.station = 135, "unknown station");
            yield return C("StationMinus2", s => s.station = -2, "unknown station");
            yield return C("PreviousStation", s => s.previousStation = 500, "previous station");
            yield return C("Ship64", s => s.ship = 64, "unknown ship");
            yield return C("NegativeShip", s => s.ship = -1, "unknown ship");
            yield return C("NegativeCredits", s => s.credits = -1, "negative credits");
            yield return C("EquipmentItem", s => s.equipment.Add(new ItemStack(99999, 1)), "equipment");
            yield return C("EquipmentAmount", s => s.equipment[0].amount = -1, "equipment");
            yield return C("CargoItem", s => s.cargo.Add(new ItemStack(-1, 1)), "cargo");
            yield return C("KaamoItem", s => s.kaamoItems = new List<ItemStack> { new ItemStack(999, 1) }, "Kaamo Club storage");
            yield return C("ShopStation", s => s.recentStations = new List<StationStock> { new StationStock { station = 200 } }, "shop memory");
            yield return C("ShopItem", s => s.recentStations = new List<StationStock> { new StationStock { station = 78, items = new List<ItemStack> { new ItemStack(500, 1) } } }, "shop memory");
            yield return C("ShopShip", s => s.recentStations = new List<StationStock> { new StationStock { station = 78, ships = new List<int> { 99 } } }, "ship in the shop memory");
            yield return C("ShopModdedShip", s => s.recentStations = new List<StationStock> { new StationStock { station = 78, shipMods = new List<StoredShip> { new StoredShip(99, 0, null) } } }, "modded ship");
            yield return C("VisitorStation", s => s.recentStations = new List<StationStock> { new StationStock { station = 78, agents = new List<Agent> { new Agent { station = 300 } } } }, "bar visitor");
            yield return C("VisitorItem", s => s.recentStations = new List<StationStock> { new StationStock { station = 78, agents = new List<Agent> { new Agent { sellItem = 400 } } } }, "bar visitor");
            yield return C("VisitorShip", s => s.recentStations = new List<StationStock> { new StationStock { station = 78, agents = new List<Agent> { new Agent { sellShip = 64 } } } }, "bar visitor");
            yield return C("VisitorSystem", s => s.recentStations = new List<StationStock> { new StationStock { station = 78, agents = new List<Agent> { new Agent { sellSystem = 34 } } } }, "bar visitor");
            yield return C("KaamoShip", s => s.kaamoShips = new List<StoredShip> { new StoredShip(64, 0, null) }, "Kaamo Club");
            yield return C("KaamoShipEquipment", s => s.kaamoShips = new List<StoredShip> { new StoredShip(5, 0, null, new List<ItemStack> { new ItemStack(300, 1) }) }, "Kaamo Club ships");
            yield return C("ParkedShip", s => { s.hasParkedShip = true; s.parkedShip = new ParkedShip { ship = 70 }; }, "parked ship");
            yield return C("ParkedEquipment", s => { s.hasParkedShip = true; s.parkedShip = new ParkedShip { ship = 10, equipment = new List<ItemStack> { new ItemStack(-3, 1) } }; }, "parked ship");
            yield return C("ParkedCargo", s => { s.hasParkedShip = true; s.parkedShip = new ParkedShip { ship = 10, cargo = new List<ItemStack> { new ItemStack(240, 1) } }; }, "parked ship");
            yield return C("StoryMissionStation", s => s.storyMission = new StoryMission { station = 135 }, "story mission");
            yield return C("BarMissionStation", s => s.freelanceMission = new FreelanceMission { clientStation = 999 }, "bar mission");
            yield return C("WantedStation", s => s.wanted = new List<WantedState> { new WantedState { current = 777 } }, "Most Wanted");
            yield return C("WantedTravel", s => s.wanted = new List<WantedState> { new WantedState { travelsTo = 135 } }, "Most Wanted");
            yield return C("WantedLastSeen", s => s.wanted = new List<WantedState> { new WantedState { lastSeen = -5 } }, "Most Wanted");
            yield return C("InvadedStation", s => s.voidInvasionStation = 135, "invaded station");
            yield return C("InvadedSystem", s => s.voidInvasionSystem = 34, "invaded system");
            yield return C("InvadedSystemNegative", s => s.voidInvasionSystem = -2, "invaded system");
            yield return C("Blueprint", s => s.blueprints = new List<BlueprintState> { new BlueprintState { item = 99999 } }, "blueprint");
            yield return C("ProductionItem", s => s.pendingProducts = new List<PendingProduct> { new PendingProduct { item = 99999, station = 78 } }, "production queue");
            yield return C("ProductionStation", s => s.pendingProducts = new List<PendingProduct> { new PendingProduct { item = 178, station = 135 } }, "production queue");
            yield return C("ProductionQuantity", s => s.pendingProducts = new List<PendingProduct> { new PendingProduct { item = 178, station = 78, quantity = -1 } }, "production queue");
        }

        [TestCaseSource(nameof(Broken))]
        public void BrokenSavesAreRefused(Action<SaveData> change, string expected)
        {
            StringAssert.Contains(expected, Refusal(change));
        }

        [Test]
        public void AnInfinitePlayingTimeIsRefused()
        {
            // "finite times": whatever the parser makes of it, a non-number never passes.
            string json = Regex.Replace(JsonUtility.ToJson(Valid()), "\"playSeconds\":[^,}]*", "\"playSeconds\":Infinity");
            Assert.IsFalse(Parse(json, out _, out _));
            json = Regex.Replace(JsonUtility.ToJson(Valid()), "\"storyStepStart\":[^,}]*", "\"storyStepStart\":NaN");
            Assert.IsFalse(Parse(json, out _, out _));
        }

        [Test]
        public void TheParkedShipIsOnlyCheckedWhenThereIsOne()
        {
            var s = Valid();
            s.hasParkedShip = false;
            s.parkedShip = new ParkedShip { ship = 999 };
            Assert.IsTrue(Parse(JsonUtility.ToJson(s), out _, out string problem), problem);
        }

        [Test]
        public void ModdedNumbersTheSaveNamesAreAccepted()
        {
            // A save from a modded game: the numbers its modKeys name are put right on loading (ModSaves.Fix).
            var s = Valid();
            s.cargo.Add(new ItemStack(240, 2));
            s.ship = 70;
            s.station = 140;
            s.modKeys = new List<SaveModKey>
            {
                new SaveModKey { kind = "item", key = "m:item", index = 240, price = 10 },
                new SaveModKey { kind = "ship", key = "m:ship", index = 70 },
                new SaveModKey { kind = "station", key = "m:station", index = 140 },
            };
            Assert.IsTrue(Parse(JsonUtility.ToJson(s), out _, out string problem), problem);
            Assert.IsTrue(ModSaves.Names(s, 240));
            Assert.IsFalse(ModSaves.Names(s, 241));
            Assert.IsTrue(ModSaves.NamesShip(s, 70));
            Assert.IsFalse(ModSaves.NamesShip(s, 240));
            Assert.IsTrue(ModSaves.NamesStation(s, 140));
            Assert.IsFalse(ModSaves.NamesStation(s, 70));
            // a key of another kind doesn't vouch for the number
            s.modKeys[0].kind = "ship";
            Assert.IsFalse(Parse(JsonUtility.ToJson(s), out _, out _));
        }
    }
}
