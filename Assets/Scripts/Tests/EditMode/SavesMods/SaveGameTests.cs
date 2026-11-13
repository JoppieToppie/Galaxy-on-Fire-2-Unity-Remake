// SaveGameTests.cs
// Feature: CLAUDE.md "UI and platforms" > Save games, Deleting saves, Hardcore, and "Multiplayer" (sessions are never
// saved); SaveGame.cs. Expected behaviour from those texts: one JSON file per slot in <data>/Saves (slot_NN.json), slot 0
// the auto-save on docking (ModStation::autosave skips a game without playing time), 1..11 manual; Delete moves the file to
// Saves/Deleted keeping the newest 20; hardcore saves only to slot 0 and DeleteRun removes every slot of the run; the
// Mod Builder's game and a multiplayer session's game never save; loading drops item numbers the item table lacks
// (DropUnknownItems) and repairs the original's checkpoints 25 -> 24, 29 -> 28, 41 -> 39, 79 -> 78 (Story.RepairCheckpoint,
// campaign_flow.md). The round trip values are arbitrary test data (save then load must give them back).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Modding;
using NUnit.Framework;
using UnityEngine;

namespace GoF2Remake.Tests
{
    public class SaveGameTests : SavesModsTest
    {
        string SlotFile(int slot) => Path.Combine(Sandbox, "Saves", $"slot_{slot:00}.json");

        void WriteSlot(int slot, string json)
        {
            Directory.CreateDirectory(Path.Combine(Sandbox, "Saves"));
            File.WriteAllText(SlotFile(slot), Q(json));
        }

        // ---- slots and files -----------------------------------------------------------------------------------------

        [Test]
        public void TwelveSlotsWithTheAutoSaveFirst()
        {
            Assert.AreEqual(12, SaveGame.SlotCount);
            Assert.AreEqual(0, SaveGame.AutoSaveSlot);
        }

        [Test]
        public void SlotFilesLiveInTheDataFoldersSavesFolder()
        {
            Assert.AreEqual(Path.Combine(Sandbox, "Saves"), SaveGame.Folder);
            for (int i = 0; i < SaveGame.SlotCount; i++) Assert.AreEqual(SlotFile(i), SaveGame.SlotPath(i));
        }

        [Test]
        public void SaveWritesTheSlotsJsonAndNoTemporaryFile()
        {
            Session.Credits = 4711;
            Assert.IsTrue(SaveGame.Save(4));
            Assert.IsTrue(File.Exists(SlotFile(4)));
            Assert.IsFalse(File.Exists(SlotFile(4) + ".tmp"));
            Assert.IsTrue(SaveGame.Exists(4));
            Assert.IsFalse(SaveGame.Exists(5));
            var p = SaveGame.Preview(4);
            Assert.IsNotNull(p);
            Assert.AreEqual(SaveData.CurrentVersion, p.version);
            Assert.AreEqual(4711, p.credits);
            Assert.AreEqual(78, p.station);
            Assert.AreEqual(10, p.ship);
            Assert.IsFalse(string.IsNullOrEmpty(p.savedAtUtc));
        }

        [Test]
        public void EverySlotCanBeWrittenAndReadBack()
        {
            for (int i = 0; i < SaveGame.SlotCount; i++)
            {
                Session.Credits = 1000 + i;
                Assert.IsTrue(SaveGame.Save(i), "slot " + i);
            }
            for (int i = 0; i < SaveGame.SlotCount; i++) Assert.AreEqual(1000 + i, SaveGame.Preview(i).credits, "slot " + i);
        }

        [Test]
        public void SavingAgainReplacesTheSlot()
        {
            Session.Credits = 1;
            SaveGame.Save(3);
            Session.Credits = 2;
            SaveGame.Save(3);
            Assert.AreEqual(2, SaveGame.Preview(3).credits);
            Assert.AreEqual(1, Directory.GetFiles(Path.Combine(Sandbox, "Saves"), "slot_03*").Length);
        }

        [Test]
        public void AnEmptySlotHasNoPreviewAndDoesntLoad()
        {
            Assert.IsNull(SaveGame.Preview(7));
            Assert.IsFalse(SaveGame.Load(7));
        }

        [Test]
        public void AnUnreadableSlotHasNoPreviewAndDoesntLoad()
        {
            WriteSlot(2, "this is not JSON");
            Session.Credits = 55;
            Assert.IsNull(SaveGame.Preview(2));
            Assert.IsFalse(SaveGame.Load(2));
            Assert.AreEqual(55, Session.Credits, "a failed load leaves the game as it was");
        }

        [Test]
        public void MostRecentSlotIsTheNewestFile()
        {
            Assert.AreEqual(-1, SaveGame.MostRecentSlot());
            SaveGame.Save(2);
            SaveGame.Save(7);
            SaveGame.Save(5);
            var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(SlotFile(2), t.AddHours(1));
            File.SetLastWriteTimeUtc(SlotFile(7), t.AddHours(3));
            File.SetLastWriteTimeUtc(SlotFile(5), t.AddHours(2));
            Assert.AreEqual(7, SaveGame.MostRecentSlot());
        }

        // ---- auto-save, hardcore, sessions, the Mod Builder --------------------------------------------------------------

        [Test]
        public void AutoSaveWritesSlotZero()
        {
            Session.PlaySeconds = 30f;
            Session.Credits = 321;
            SaveGame.AutoSave();
            Assert.IsTrue(SaveGame.Exists(0));
            Assert.IsTrue(Session.HasAutosave);
            Assert.AreEqual(321, SaveGame.Preview(0).credits);
            for (int i = 1; i < SaveGame.SlotCount; i++) Assert.IsFalse(SaveGame.Exists(i));
        }

        [Test]
        public void AutoSaveSkipsAGameWithoutPlayingTime()
        {
            // ModStation::autosave skips a game with no playing time.
            Session.PlaySeconds = -100f;
            SaveGame.AutoSave();
            Assert.IsFalse(SaveGame.Exists(0));
        }

        [Test]
        public void HardcoreSavesOnlyToTheAutoSave()
        {
            Session.Hardcore = true;
            for (int i = 1; i < SaveGame.SlotCount; i++) Assert.IsFalse(SaveGame.Save(i), "slot " + i);
            Assert.IsTrue(SaveGame.Save(0));
            Assert.IsTrue(SaveGame.Preview(0).hardcore);
            Assert.AreEqual(Session.RunId, SaveGame.Preview(0).runId);
        }

        [Test]
        public void DeleteRunRemovesOnlyThatRunsSlots()
        {
            Session.RunId = "runA";
            SaveGame.Save(0);
            SaveGame.Save(3);
            Session.RunId = "runB";
            SaveGame.Save(5);
            Assert.AreEqual(2, SaveGame.DeleteRun("runA"));
            Assert.IsFalse(SaveGame.Exists(0));
            Assert.IsFalse(SaveGame.Exists(3));
            Assert.IsTrue(SaveGame.Exists(5));
            Assert.AreEqual(0, SaveGame.DeleteRun(""));
            Assert.AreEqual(0, SaveGame.DeleteRun(null));
            Assert.AreEqual(0, SaveGame.DeleteRun("runC"));
            Assert.IsTrue(SaveGame.Exists(5));
        }

        [Test]
        public void ANewGameGetsANewRunId()
        {
            string a = Session.RunId;
            Session.ResetNewGame();
            Assert.IsFalse(string.IsNullOrEmpty(Session.RunId));
            Assert.AreNotEqual(a, Session.RunId);
        }

        [Test]
        public void AMultiplayerSessionsGameIsNeverSaved()
        {
            var oldProvider = NetSession.SessionGameProvider;
            var oldUpload = GameHooks.ProfileUpload;
            int uploads = 0;
            try
            {
                NetSession.SessionGameProvider = () => true;
                GameHooks.ProfileUpload = () => uploads++;
                Session.PlaySeconds = 60f;
                for (int i = 0; i < SaveGame.SlotCount; i++) Assert.IsFalse(SaveGame.Save(i), "slot " + i);
                SaveGame.AutoSave();
                for (int i = 0; i < SaveGame.SlotCount; i++) Assert.IsFalse(SaveGame.Exists(i));
                Assert.AreEqual(1, uploads, "the docking's auto-save goes to the server profile instead");
            }
            finally
            {
                NetSession.SessionGameProvider = oldProvider;
                GameHooks.ProfileUpload = oldUpload;
            }
        }

        [Test]
        public void NoSaveIsLoadedIntoARunningSession()
        {
            Session.Credits = 900;
            SaveGame.Save(1);
            Session.Credits = 5;
            var old = NetSession.ActiveProvider;
            try
            {
                NetSession.ActiveProvider = () => true;
                Assert.IsFalse(SaveGame.Load(1));
                Assert.AreEqual(5, Session.Credits);
            }
            finally { NetSession.ActiveProvider = old; }
        }

        [Test]
        public void TheModBuildersGameIsNeverSaved()
        {
            bool old = ModManager.BuilderOpen;
            try
            {
                ModManager.BuilderOpen = true;
                Session.PlaySeconds = 60f;
                Assert.IsFalse(SaveGame.Save(1));
                SaveGame.AutoSave();
                Assert.IsFalse(SaveGame.Exists(0));
                Assert.IsFalse(SaveGame.Exists(1));
            }
            finally { ModManager.BuilderOpen = old; }
        }

        // ---- deleting -----------------------------------------------------------------------------------------------

        [Test]
        public void DeleteMovesTheSlotToSavesDeleted()
        {
            Session.Credits = 77;
            SaveGame.Save(3);
            Assert.IsTrue(SaveGame.Delete(3));
            Assert.IsFalse(SaveGame.Exists(3));
            var bin = Path.Combine(Sandbox, "Saves", "Deleted");
            var files = Directory.GetFiles(bin, "slot_03_*.json");
            Assert.AreEqual(1, files.Length);
            Assert.AreEqual(77, JsonUtility.FromJson<SaveData>(File.ReadAllText(files[0])).credits, "the deleted save is kept whole");
        }

        [Test]
        public void DeletingAnEmptySlotFails()
        {
            Assert.IsFalse(SaveGame.Delete(6));
        }

        [Test]
        public void TheDeletedFolderKeepsTheNewestTwenty()
        {
            var bin = Path.Combine(Sandbox, "Saves", "Deleted");
            Directory.CreateDirectory(bin);
            var old = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            for (int i = 0; i < 25; i++)
            {
                string f = Path.Combine(bin, $"slot_09_2020010{i % 9 + 1}-0000{i:00}.json");
                File.WriteAllText(f, "{}");
                File.SetLastWriteTimeUtc(f, old.AddMinutes(i));
            }
            SaveGame.Save(4);
            Assert.IsTrue(SaveGame.Delete(4));
            var left = Directory.GetFiles(bin, "slot_*.json");
            Assert.AreEqual(20, left.Length);
            Assert.AreEqual(1, left.Count(f => Path.GetFileName(f).StartsWith("slot_04_")), "the new deletion is kept");
            // the oldest six of the 25 went (the newest 19 old ones stayed beside the new one)
            Assert.AreEqual(19, left.Count(f => Path.GetFileName(f).StartsWith("slot_09_")));
            Assert.IsTrue(left.All(f => File.GetLastWriteTimeUtc(f) >= old.AddMinutes(6)));
        }

        // ---- the round trip -----------------------------------------------------------------------------------------

        [Test]
        public void EverySavedFieldComesBackAfterLoading()
        {
            Session.PlaySeconds = 4321f;
            Session.Campaign = Campaign.Valkyrie;
            Session.Difficulty = Session.DifficultyHard;
            Session.Economy = Economy.Default;
            Session.CampaignMission = 12;
            Session.StationIndex = 70;
            Session.PreviousStationIndex = 78;
            Session.ShipIndex = 22;
            Session.Credits = 123456;
            Session.Equipment = Stacks((2, 1), (36, 6), (50, 1));
            Session.Cargo = Stacks((155, 12), (204, 3));
            Session.RecentStations = new List<StationStock>
            {
                new StationStock
                {
                    station = 70, items = Stacks((155, 40)), ships = new List<int> { 5, 22 },
                    agents = new List<Agent> { new Agent { name = "Zed", station = 70, offer = AgentOffer.SellItem, sellItem = 12, sellQuantity = 2, sellPrice = 999 } },
                },
            };
            Session.SeenItems = new HashSet<int> { 2, 12 };
            Session.VisitedStations = new HashSet<int> { 78, 70 };
            Session.AttackedStations = new HashSet<int> { 40 };
            Session.LowestKnownPrice[155] = (140, 15);
            Session.HighestKnownPrice[155] = (170, 4);
            var visible = new bool[34];
            visible[3] = true;
            Session.SystemVisible = visible;
            Session.JumpgatesUsed = 4;
            Session.Kills = 17;
            Session.PirateKills = 9;
            Session.Standing = new[] { -40, 55 };
            Session.FreePlay = false;
            Session.StoryMission = new StoryMission { type = 4, reward = 500, station = 70, value = 3, goodsItem = 155, goodsAmount = 10, visible = false };
            Session.StoryStepStart = 100f;
            Session.StoryRadioPending = true;
            Session.FreelanceCompleted = 6;
            Session.StoryCounter = 2;
            Session.VoidInvasionSystem = 9;
            Session.VoidInvasionStation = 48;
            Session.InvasionDepartures = 3;
            Session.Unsaleable = new HashSet<int> { 90 };
            Session.FreelanceMission = new FreelanceMission { type = MissionType.Courier, clientName = "Bob", clientStation = 70, target = 75, good = 155, amount = 7 };
            Session.Passengers = 2;
            Session.UsedMissionTypes = new bool[15];
            Session.UsedMissionTypes[3] = true;
            Session.InformerKilled = true;
            Session.InformerFailed = true;
            Session.Wingmen = new List<string> { "Ann", "Ben" };
            Session.WingmanRace = 2;
            Session.WingmanContractMs = 60000f;
            Session.WingmanPortrait = new[] { 1, 2, 3, 4, 5 };
            Session.WingmenHired = 3;
            Session.XpBonus = -12;
            Session.UnlockedBlueprints = new HashSet<int> { 178 };
            Session.ShipMods = new List<int> { 0, 1, 1 };
            Session.Blueprints = new List<BlueprintState> { new BlueprintState { item = 178, remaining = new List<int> { 5, 6 }, moneySpent = 10, timesProduced = 1, station = 70 } };
            Session.PendingProducts = new List<PendingProduct> { new PendingProduct { item = 178, quantity = 2, station = 70 } };
            Session.GoodsProduced = 3;
            Session.Medals = new int[45];
            Session.Medals[4] = 2;
            Session.AsteroidsDestroyed = 11; Session.OreMined = 500; Session.CoresMined = 2; Session.CratesSalvaged = 5;
            Session.JunkDestroyed = 1; Session.BattleshipsDestroyed = 1; Session.HighestCredits = 200000; Session.LastArrivalHullPercent = 64;
            Session.CapitalLootReadyAt = 999f;
            Session.OreTypesMined = new HashSet<int> { 155 };
            Session.CoreTypesMined = new HashSet<int> { 156 };
            Session.BoozeBought = 7; Session.AlienRemainsCollected = 3;
            Session.BoozeTypes = new HashSet<int> { 132 };
            Session.AgentsTalkedTo = 4; Session.OffersDeclined = 5; Session.OffersRepeated = 6; Session.AcceptedBlindRisk = 7;
            Session.AcceptedBlindMap = 8; Session.ContainersDelivered = 9; Session.PassengersDelivered = 10;
            Session.KaamoState = 3;
            Session.KaamoItems = Stacks((54, 1));
            Session.KaamoShips = new List<StoredShip> { new StoredShip(5, 0, new List<int> { 1 }, Stacks((2, 1))) };
            Session.SelectedSecondary = 36;
            Session.BombsDetonated = 2;
            Session.PirateBaseDestroyed = new[] { true, false, true, false };
            Session.Hints = new HashSet<int> { 590, 0x23 };
            Session.GraveRiserKills = 4;
            Session.CloakMs = 123456789L;
            Session.ParkedShip = new ParkedShip { ship = 10, equipment = Stacks((2, 1)), cargo = Stacks((155, 1)), mods = new List<int> { 2 }, hull = 50, armor = 10, shield = 0.5f };
            Session.StoryTargets = new List<int> { 40, 45 };
            Session.Wanted = new List<WantedState> { new WantedState { active = true, current = 70, travelsTo = 75, lastSeen = 40 } };
            Session.CollectedBounties = new[] { 1, 2, 3, 4 };
            Session.WantedHints = 5;
            Session.HiddenBlueprintsFound = 3;
            Session.EliteFlags = new HashSet<int> { 38 };
            Session.OreStreak = 2;
            Session.BlindKills = 3;
            Session.LomaTollPaid = true;
            Session.LomaTollRefused = false;
            Session.StoryAgentsAccepted = new HashSet<int> { 5 };
            Session.GraphQuests = new List<GraphQuestState>
            {
                new GraphQuestState { name = "q1", title = "Quest", checkpoint = "cp", varNames = new List<string> { "x" }, varValues = new List<double> { 2.5 }, objective = "Go", target = 70 },
            };
            Session.GraphQuestsDone = new HashSet<string> { "done1" };
            Session.ModCampaign = "some_campaign";
            Session.RunId = "run-123";
            Session.ModGameOptions = new HashSet<string> { "m:o" };
            Session.PirateSightings = new List<PirateSighting>
            {
                new PirateSighting { id = 4, boss = true, tier = 5, station = 40, system = 7, name = "Kral", bounty = 9000, reportedAt = 10f, expiresAt = 99f },
            };
            Session.NextPirateSightingId = 5;

            Assert.IsTrue(SaveGame.Save(6));
            Session.ResetNewGame();
            Session.Campaign = Campaign.GalaxyOnFire2;
            Session.Difficulty = Session.DifficultyNormal;
            Session.Economy = Economy.Android;
            Assert.IsTrue(SaveGame.Load(6));

            Assert.That(Session.PlaySeconds, Is.InRange(4321f, 4381f));
            Assert.AreEqual(Campaign.Valkyrie, Session.Campaign);
            Assert.AreEqual(Session.DifficultyHard, Session.Difficulty);
            Assert.AreEqual(Economy.Default, Session.Economy);
            Assert.AreEqual(12, Session.CampaignMission);
            Assert.AreEqual(70, Session.StationIndex);
            Assert.AreEqual(78, Session.PreviousStationIndex);
            Assert.AreEqual(22, Session.ShipIndex);
            Assert.AreEqual(123456, Session.Credits);
            Assert.AreEqual("2x1 36x6 50x1", Describe(Session.Equipment));
            Assert.AreEqual("155x12 204x3", Describe(Session.Cargo));
            Assert.AreEqual(1, Session.RecentStations.Count);
            var rs = Session.RecentStations[0];
            Assert.AreEqual(70, rs.station);
            Assert.AreEqual("155x40", Describe(rs.items));
            CollectionAssert.AreEqual(new[] { 5, 22 }, rs.ships);
            Assert.AreEqual("Zed", rs.agents[0].name);
            Assert.AreEqual(12, rs.agents[0].sellItem);
            Assert.AreEqual(999, rs.agents[0].sellPrice);
            CollectionAssert.AreEquivalent(new[] { 2, 12 }, Session.SeenItems);
            CollectionAssert.AreEquivalent(new[] { 78, 70 }, Session.VisitedStations);
            CollectionAssert.AreEquivalent(new[] { 40 }, Session.AttackedStations);
            Assert.AreEqual((140, 15), Session.LowestKnownPrice[155]);
            Assert.AreEqual((170, 4), Session.HighestKnownPrice[155]);
            Assert.AreEqual(34, Session.SystemVisible.Length);
            Assert.IsTrue(Session.SystemVisible[3]);
            Assert.IsFalse(Session.SystemVisible[4]);
            Assert.AreEqual(4, Session.JumpgatesUsed);
            Assert.AreEqual(17, Session.Kills);
            Assert.AreEqual(9, Session.PirateKills);
            CollectionAssert.AreEqual(new[] { -40, 55 }, Session.Standing);
            Assert.IsFalse(Session.FreePlay);
            Assert.AreEqual(4, Session.StoryMission.type);
            Assert.AreEqual(500, Session.StoryMission.reward);
            Assert.AreEqual(70, Session.StoryMission.station);
            Assert.AreEqual(3, Session.StoryMission.value);
            Assert.AreEqual(155, Session.StoryMission.goodsItem);
            Assert.AreEqual(10, Session.StoryMission.goodsAmount);
            Assert.IsFalse(Session.StoryMission.visible);
            Assert.AreEqual(100f, Session.StoryStepStart);
            Assert.IsTrue(Session.StoryRadioPending);
            Assert.AreEqual(6, Session.FreelanceCompleted);
            Assert.AreEqual(2, Session.StoryCounter);
            Assert.AreEqual(9, Session.VoidInvasionSystem);
            Assert.AreEqual(48, Session.VoidInvasionStation);
            Assert.AreEqual(3, Session.InvasionDepartures);
            CollectionAssert.AreEquivalent(new[] { 90 }, Session.Unsaleable);
            Assert.AreEqual(MissionType.Courier, Session.FreelanceMission.type);
            Assert.AreEqual("Bob", Session.FreelanceMission.clientName);
            Assert.AreEqual(70, Session.FreelanceMission.clientStation);
            Assert.AreEqual(75, Session.FreelanceMission.target);
            Assert.AreEqual(155, Session.FreelanceMission.good);
            Assert.AreEqual(7, Session.FreelanceMission.amount);
            Assert.AreEqual(2, Session.Passengers);
            Assert.AreEqual(15, Session.UsedMissionTypes.Length);
            Assert.IsTrue(Session.UsedMissionTypes[3]);
            Assert.IsFalse(Session.UsedMissionTypes[2]);
            Assert.IsTrue(Session.InformerKilled);
            Assert.IsTrue(Session.InformerFailed);
            CollectionAssert.AreEqual(new[] { "Ann", "Ben" }, Session.Wingmen);
            Assert.AreEqual(2, Session.WingmanRace);
            Assert.AreEqual(60000f, Session.WingmanContractMs);
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5 }, Session.WingmanPortrait);
            Assert.AreEqual(3, Session.WingmenHired);
            Assert.AreEqual(-12, Session.XpBonus, "the Debug pages' level offset");
            CollectionAssert.AreEquivalent(new[] { 178 }, Session.UnlockedBlueprints);
            CollectionAssert.AreEqual(new[] { 0, 1, 1 }, Session.ShipMods);
            Assert.AreEqual(1, Session.Blueprints.Count);
            Assert.AreEqual(178, Session.Blueprints[0].item);
            CollectionAssert.AreEqual(new[] { 5, 6 }, Session.Blueprints[0].remaining);
            Assert.AreEqual(10, Session.Blueprints[0].moneySpent);
            Assert.AreEqual(1, Session.Blueprints[0].timesProduced);
            Assert.AreEqual(70, Session.Blueprints[0].station);
            Assert.AreEqual(178, Session.PendingProducts[0].item);
            Assert.AreEqual(2, Session.PendingProducts[0].quantity);
            Assert.AreEqual(70, Session.PendingProducts[0].station);
            Assert.AreEqual(3, Session.GoodsProduced);
            Assert.AreEqual(45, Session.Medals.Length);
            Assert.AreEqual(2, Session.Medals[4]);
            Assert.AreEqual(11, Session.AsteroidsDestroyed);
            Assert.AreEqual(500, Session.OreMined);
            Assert.AreEqual(2, Session.CoresMined);
            Assert.AreEqual(5, Session.CratesSalvaged);
            Assert.AreEqual(1, Session.JunkDestroyed);
            Assert.AreEqual(1, Session.BattleshipsDestroyed);
            Assert.AreEqual(200000, Session.HighestCredits);
            Assert.AreEqual(64, Session.LastArrivalHullPercent);
            Assert.AreEqual(999f, Session.CapitalLootReadyAt);
            CollectionAssert.AreEquivalent(new[] { 155 }, Session.OreTypesMined);
            CollectionAssert.AreEquivalent(new[] { 156 }, Session.CoreTypesMined);
            Assert.AreEqual(7, Session.BoozeBought);
            Assert.AreEqual(3, Session.AlienRemainsCollected);
            CollectionAssert.AreEquivalent(new[] { 132 }, Session.BoozeTypes);
            Assert.AreEqual(new[] { 4, 5, 6, 7, 8, 9, 10 }, new[] { Session.AgentsTalkedTo, Session.OffersDeclined, Session.OffersRepeated,
                Session.AcceptedBlindRisk, Session.AcceptedBlindMap, Session.ContainersDelivered, Session.PassengersDelivered });
            Assert.AreEqual(3, Session.KaamoState);
            Assert.AreEqual("54x1", Describe(Session.KaamoItems));
            Assert.AreEqual(1, Session.KaamoShips.Count);
            Assert.AreEqual(5, Session.KaamoShips[0].ship);
            CollectionAssert.AreEqual(new[] { 1 }, Session.KaamoShips[0].mods);
            Assert.AreEqual("2x1", Describe(Session.KaamoShips[0].equipment));
            Assert.AreEqual(36, Session.SelectedSecondary);
            Assert.AreEqual(2, Session.BombsDetonated);
            CollectionAssert.AreEqual(new[] { true, false, true, false }, Session.PirateBaseDestroyed);
            CollectionAssert.AreEquivalent(new[] { 590, 0x23 }, Session.Hints);
            Assert.AreEqual(4, Session.GraveRiserKills);
            Assert.AreEqual(123456789L, Session.CloakMs);
            Assert.IsNotNull(Session.ParkedShip);
            Assert.AreEqual(10, Session.ParkedShip.ship);
            Assert.AreEqual("2x1", Describe(Session.ParkedShip.equipment));
            Assert.AreEqual("155x1", Describe(Session.ParkedShip.cargo));
            CollectionAssert.AreEqual(new[] { 2 }, Session.ParkedShip.mods);
            Assert.AreEqual(50, Session.ParkedShip.hull);
            Assert.AreEqual(10, Session.ParkedShip.armor);
            Assert.AreEqual(0.5f, Session.ParkedShip.shield);
            CollectionAssert.AreEqual(new[] { 40, 45 }, Session.StoryTargets);
            Assert.AreEqual(1, Session.Wanted.Count);
            Assert.IsTrue(Session.Wanted[0].active);
            Assert.AreEqual(70, Session.Wanted[0].current);
            Assert.AreEqual(75, Session.Wanted[0].travelsTo);
            Assert.AreEqual(40, Session.Wanted[0].lastSeen);
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4 }, Session.CollectedBounties);
            Assert.AreEqual(5, Session.WantedHints);
            Assert.AreEqual(3, Session.HiddenBlueprintsFound);
            CollectionAssert.AreEquivalent(new[] { 38 }, Session.EliteFlags);
            Assert.AreEqual(2, Session.OreStreak);
            Assert.AreEqual(3, Session.BlindKills);
            Assert.IsTrue(Session.LomaTollPaid);
            Assert.IsFalse(Session.LomaTollRefused);
            CollectionAssert.AreEquivalent(new[] { 5 }, Session.StoryAgentsAccepted);
            Assert.AreEqual(1, Session.GraphQuests.Count);
            var q = Session.GraphQuests[0];
            Assert.AreEqual("q1", q.name);
            Assert.AreEqual("Quest", q.title);
            Assert.AreEqual("cp", q.checkpoint);
            CollectionAssert.AreEqual(new[] { "x" }, q.varNames);
            CollectionAssert.AreEqual(new[] { 2.5 }, q.varValues);
            Assert.AreEqual("Go", q.objective);
            Assert.AreEqual(70, q.target);
            Assert.IsTrue(GraphQuestState.RestorePending, "quests under way run again from their checkpoints");
            CollectionAssert.AreEquivalent(new[] { "done1" }, Session.GraphQuestsDone);
            Assert.AreEqual("some_campaign", Session.ModCampaign);
            Assert.AreEqual("run-123", Session.RunId);
            Assert.IsFalse(Session.Hardcore);
            CollectionAssert.AreEquivalent(new[] { "m:o" }, Session.ModGameOptions);
            Assert.AreEqual(1, Session.PirateSightings.Count);
            var ps = Session.PirateSightings[0];
            Assert.AreEqual(4, ps.id);
            Assert.IsTrue(ps.boss);
            Assert.AreEqual(5, ps.tier);
            Assert.AreEqual(40, ps.station);
            Assert.AreEqual(7, ps.system);
            Assert.AreEqual("Kral", ps.name);
            Assert.AreEqual(9000, ps.bounty);
            Assert.AreEqual(10f, ps.reportedAt);
            Assert.AreEqual(99f, ps.expiresAt);
            Assert.AreEqual(5, Session.NextPirateSightingId);
        }

        [Test]
        public void TheHardcoreFlagAndRunComeBack()
        {
            Session.Hardcore = true;
            Session.RunId = "hc-run";
            Assert.IsTrue(SaveGame.Save(0));
            Session.ResetNewGame();
            Assert.IsTrue(SaveGame.Load(0));
            Assert.IsTrue(Session.Hardcore);
            Assert.AreEqual("hc-run", Session.RunId);
        }

        [Test]
        public void ANoParkedShipStaysNone()
        {
            // JsonUtility writes an empty object for a null ParkedShip: hasParkedShip decides.
            Session.ParkedShip = null;
            SaveGame.Save(1);
            Session.ParkedShip = new ParkedShip { ship = 5 };
            SaveGame.Load(1);
            Assert.IsNull(Session.ParkedShip);
        }

        [Test]
        public void LoadingLeavesACompletedWorld()
        {
            SaveGame.Save(1);
            Session.UseCompletedWorld();
            SaveGame.Load(1);
            Assert.IsFalse(Session.CompletedWorld, "a save is its own world");
        }

        [Test]
        public void LomaAndShimaAreVisibleAfterLoading()
        {
            // GameRecord::load: Loma (25) and Shima (26) are always visible after loading.
            Session.SystemVisible = new bool[34];
            SaveGame.Save(1);
            SaveGame.Load(1);
            Assert.IsTrue(Session.SystemVisible[GalaxyMap.LomaSystem]);
            Assert.IsTrue(Session.SystemVisible[GalaxyMap.ShimaSystem]);
            Assert.AreEqual(25, GalaxyMap.LomaSystem);
            Assert.AreEqual(26, GalaxyMap.ShimaSystem);
            Assert.AreEqual(2, Session.SystemVisible.Count(v => v));
        }

        // ---- loading repairs ------------------------------------------------------------------------------------------

        [Test]
        public void UnknownItemNumbersAreDroppedOnLoading()
        {
            // DropUnknownItems: numbers the item table doesn't have go from every list (a negative one isn't a mod's number,
            // so ModSaves.Fix leaves it to this), and the selected secondary with an unknown equipment item.
            WriteSlot(1, @"{ 'version': 17, 'station': 78, 'ship': 10, 'credits': 10, 'campaignMission': 20, 'freePlay': true,
                'selectedSecondary': 36,
                'equipment': [ { 'item': 2, 'amount': 1 }, { 'item': -5, 'amount': 1 }, { 'item': 36, 'amount': 4 } ],
                'cargo': [ { 'item': 155, 'amount': 3 }, { 'item': -7, 'amount': 9 } ],
                'kaamoItems': [ { 'item': -8, 'amount': 1 }, { 'item': 54, 'amount': 1 } ],
                'kaamoShips': [ { 'ship': 5, 'race': 0, 'mods': [], 'equipment': [ { 'item': -9, 'amount': 1 }, { 'item': 2, 'amount': 1 } ] } ],
                'hasParkedShip': true,
                'parkedShip': { 'ship': 10, 'equipment': [ { 'item': -1, 'amount': 1 } ], 'cargo': [ { 'item': -2, 'amount': 1 }, { 'item': 155, 'amount': 2 } ] },
                'recentStations': [ { 'station': 78, 'items': [ { 'item': -3, 'amount': 5 }, { 'item': 155, 'amount': 5 } ], 'ships': [],
                    'agents': [ { 'name': 'A', 'sellItem': -4 }, { 'name': 'B', 'sellItem': 12 }, { 'name': 'C', 'sellItem': -1 } ] } ],
                'blueprints': [ { 'item': -6 }, { 'item': 178 } ],
                'pendingProducts': [ { 'item': -10, 'quantity': 1, 'station': 78 }, { 'item': 178, 'quantity': 1, 'station': 78 } ] }");
            Assert.IsTrue(SaveGame.Load(1));
            Assert.AreEqual("2x1 36x4", Describe(Session.Equipment));
            Assert.AreEqual(-1, Session.SelectedSecondary, "an unknown equipment item resets the secondary selection");
            Assert.AreEqual("155x3", Describe(Session.Cargo));
            Assert.AreEqual("54x1", Describe(Session.KaamoItems));
            Assert.AreEqual("2x1", Describe(Session.KaamoShips[0].equipment));
            Assert.AreEqual("", Describe(Session.ParkedShip.equipment));
            Assert.AreEqual("155x2", Describe(Session.ParkedShip.cargo));
            Assert.AreEqual("155x5", Describe(Session.RecentStations[0].items));
            CollectionAssert.AreEqual(new[] { "B", "C" }, Session.RecentStations[0].agents.Select(a => a.name).ToArray(), "-1 = sells no item");
            CollectionAssert.AreEqual(new[] { 178 }, Session.Blueprints.Select(b => b.item).ToArray());
            CollectionAssert.AreEqual(new[] { 178 }, Session.PendingProducts.Select(p => p.item).ToArray());
        }

        [Test]
        public void ModdedNumbersWithoutTheirModAreDroppedOnLoading()
        {
            // Item numbers past the original table that no mod accounts for (ModSaves.Fix, then DropUnknownItems).
            WriteSlot(1, @"{ 'version': 17, 'station': 78, 'ship': 10, 'campaignMission': 20, 'freePlay': true,
                'cargo': [ { 'item': 155, 'amount': 3 }, { 'item': 260, 'amount': 2 }, { 'item': 9999, 'amount': 1 } ] }");
            Assert.IsTrue(SaveGame.Load(1));
            Assert.AreEqual("155x3", Describe(Session.Cargo));
        }

        [TestCase(25, 24, 48)]
        [TestCase(29, 28, 91)]
        public void TheVoidCheckpointsAreRepairedOnLoading(int saved, int loaded, int station)
        {
            // campaign_flow.md / CLAUDE.md "Story": checkpoint repairs on load 25 -> 24, 29 -> 28 (the Void orbit's steps).
            WriteSlot(1, "{ 'version': 17, 'station': 78, 'ship': 10, 'campaignMission': " + saved + " }");
            Assert.IsTrue(SaveGame.Load(1));
            Assert.AreEqual(loaded, Session.CampaignMission);
            Assert.AreEqual(station, Session.StoryMission.station);
            Assert.AreEqual(station, Session.VoidInvasionStation);
        }

        [Test]
        public void Step41IsRepairedTo39()
        {
            WriteSlot(1, "{ 'version': 17, 'station': 78, 'ship': 10, 'campaignMission': 41 }");
            SaveGame.Load(1);
            Assert.AreEqual(39, Session.CampaignMission);
            Assert.AreEqual(30, Session.StoryMission.station);
        }

        [Test]
        public void Step79IsRepairedTo78OutsideFreePlay()
        {
            // #49: a save the old Khador advance stranded at 79 goes back to 78 (docked at the Valkyrie, 101).
            WriteSlot(1, "{ 'version': 17, 'station': 78, 'ship': 10, 'campaignMission': 79, 'freePlay': false }");
            SaveGame.Load(1);
            Assert.AreEqual(78, Session.CampaignMission);
            Assert.AreEqual(101, Session.StoryMission.station);
            WriteSlot(2, "{ 'version': 17, 'station': 78, 'ship': 10, 'campaignMission': 79, 'freePlay': true }");
            SaveGame.Load(2);
            Assert.AreEqual(79, Session.CampaignMission);
        }

        [Test]
        public void OtherStepsAreLoadedAsSaved()
        {
            foreach (int step in new[] { 0, 24, 28, 39, 40, 78, 80, 162 })
            {
                WriteSlot(1, "{ 'version': 17, 'station': 78, 'ship': 10, 'campaignMission': " + step + " }");
                SaveGame.Load(1);
                Assert.AreEqual(step, Session.CampaignMission, "step " + step);
            }
        }

        [Test]
        public void TheLiberatorBlueprintIsKeptPastStep58()
        {
            // Story.ApplyStepEffects: the original's BluePrint::lock at 59 writes the unlocked flag, so the Liberator (179)
            // stays unlocked for every game past 58.
            WriteSlot(1, "{ 'version': 17, 'station': 78, 'ship': 10, 'campaignMission': 59, 'unlockedBlueprints': [] }");
            SaveGame.Load(1);
            Assert.IsTrue(Session.UnlockedBlueprints.Contains(179));
            WriteSlot(2, "{ 'version': 17, 'station': 78, 'ship': 10, 'campaignMission': 58, 'unlockedBlueprints': [] }");
            SaveGame.Load(2);
            Assert.IsFalse(Session.UnlockedBlueprints.Contains(179));
        }

        [Test]
        public void OldLoungeShipSellersBecomeOffer12()
        {
            // Before the event missions took offer 11 a lounge ship seller was 11 (AgentOffer.SellShip is 12 now).
            Assert.AreEqual(12, AgentOffer.SellShip);
            WriteSlot(1, @"{ 'version': 17, 'station': 78, 'ship': 10, 'campaignMission': 20,
                'recentStations': [ { 'station': 78, 'agents': [ { 'name': 'S', 'offer': 11, 'sellShip': 5 }, { 'name': 'E', 'offer': 11, 'sellShip': -1 } ] } ] }");
            SaveGame.Load(1);
            Assert.AreEqual(AgentOffer.SellShip, Session.RecentStations[0].agents[0].offer);
            Assert.AreEqual(11, Session.RecentStations[0].agents[1].offer, "an event mission's agent (no ship) stays");
        }

        [Test]
        public void Step59sFinishedConvoysSurviveLoading()
        {
            // Session.StoryTargets: step 59's (type 0xa3) target stations, -1 = that convoy done (Traffic marks it); the step is
            // complete when the list is non-empty and every entry is negative (Story.IsComplete's TargetList case). Loading
            // runs ModSaves.Fix, whose FixWorld drops every station number below 0 from storyTargets, so the done markers go:
            // [56, -1, -1] loads as [56], and a save with all three done loads as [] (never complete again).
            Session.CampaignMission = 59;
            Session.StoryMission = new StoryMission { type = StoryType.TargetList, station = 101 };
            Session.StoryTargets = new List<int> { 56, -1, -1 };
            SaveGame.Save(1);
            SaveGame.Load(1);
            CollectionAssert.AreEqual(new[] { 56, -1, -1 }, Session.StoryTargets);
            Session.StoryTargets = new List<int> { -1, -1, -1 };
            SaveGame.Save(2);
            SaveGame.Load(2);
            CollectionAssert.AreEqual(new[] { -1, -1, -1 }, Session.StoryTargets);
        }

        [Test]
        public void MissingVisitedStationsMeanTheSavesStation()
        {
            WriteSlot(1, "{ 'version': 17, 'station': 70, 'ship': 10, 'campaignMission': 20 }");
            SaveGame.Load(1);
            CollectionAssert.AreEquivalent(new[] { 70 }, Session.VisitedStations);
        }

        [Test]
        public void ArraysOfTheWrongLengthKeepTheNewGamesValues()
        {
            WriteSlot(1, @"{ 'version': 17, 'station': 78, 'ship': 10, 'campaignMission': 20, 'standing': [1, 2, 3],
                'usedMissionTypes': [true], 'wingmanPortrait': [1], 'medals': [1, 2], 'pirateBaseDestroyed': [true], 'collectedBounties': [9] }");
            SaveGame.Load(1);
            CollectionAssert.AreEqual(new[] { 30, 0 }, Session.Standing, "a new game's 30 / 0");
            Assert.AreEqual(15, Session.UsedMissionTypes.Length);
            Assert.AreEqual(5, Session.WingmanPortrait.Length);
            Assert.AreEqual(45, Session.Medals.Length);
            Assert.AreEqual(4, Session.PirateBaseDestroyed.Length);
            CollectionAssert.AreEqual(new int[4], Session.CollectedBounties);
        }
    }
}
