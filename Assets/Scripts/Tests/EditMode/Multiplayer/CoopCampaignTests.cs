// CoopCampaignTests.cs
// Feature: docs/notes/multiplayer-coop.md (CoopCampaign: the co-op campaign's story rules). The leader's State and a guest
// catching up to it with every step's own side effects, the wait for a guest's own success conversation, a guest's progress
// on the step's shared counters and the leader's merge (bits at 116 / 125 / 148, counts elsewhere, convoy targets), the
// leader following a guest's own advance, step 13's crew missions, a won step reported by another crew member, a failed
// level's checkpoint, the story levels' hull factor, and the economy (the hosted save's or the new game's pick, the byte
// NetState carries, a guest's tables matching the host's, a profile applied under the session's economy). The hooks stand
// in for the session (no network).

using System;
using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Multiplayer;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public class CoopCampaignTests : StoryAreaTestBase
    {
        Func<bool> savedActive, savedLeader;
        Func<int> savedCrew;
        Action<int> savedWon, savedAdvanced;
        Action<string> savedProgress, savedPublish;
        Action<long> savedFreelance;
        bool leader;
        readonly List<int> won = new List<int>(), advanced = new List<int>();
        readonly List<string> progress = new List<string>(), published = new List<string>();
        Database db;

        [SetUp]
        public void SetUpCoop()
        {
            savedActive = GameHooks.CoopActive; savedLeader = GameHooks.CoopLeader; savedCrew = GameHooks.CoopCrewSize;
            savedWon = GameHooks.CoopStepWon; savedAdvanced = GameHooks.CoopAdvanced; savedProgress = GameHooks.CoopProgress;
            savedPublish = GameHooks.CoopPublish; savedFreelance = GameHooks.CoopFreelanceDone;
            won.Clear(); advanced.Clear(); progress.Clear(); published.Clear();
            GameHooks.CoopActive = () => true;
            GameHooks.CoopLeader = () => leader;
            GameHooks.CoopCrewSize = () => 3;
            GameHooks.CoopStepWon = won.Add;
            GameHooks.CoopAdvanced = advanced.Add;
            GameHooks.CoopProgress = progress.Add;
            GameHooks.CoopPublish = published.Add;
            GameHooks.CoopFreelanceDone = _ => { };
            CoopCampaign.Reset();
            db = Database.Load();
            Session.FreePlay = false;
            Session.CompletedWorld = false;
        }

        [TearDown]
        public void TearDownCoop()
        {
            CoopCampaign.Reset();
            GameHooks.CoopActive = savedActive; GameHooks.CoopLeader = savedLeader; GameHooks.CoopCrewSize = savedCrew;
            GameHooks.CoopStepWon = savedWon; GameHooks.CoopAdvanced = savedAdvanced; GameHooks.CoopProgress = savedProgress;
            GameHooks.CoopPublish = savedPublish; GameHooks.CoopFreelanceDone = savedFreelance;
        }

        /// <summary>The leader's State at step 'index' (reached by Advance from a new game), as JSON.</summary>
        string LeaderAt(int index, Action<CoopCampaign.State> change = null)
        {
            leader = true;
            Session.ResetNewGame();
            Story.StartNewGame(0);
            while (Story.Index < index) Story.Advance(db);
            var s = CoopCampaign.Capture();
            change?.Invoke(s);
            leader = false;
            return CoopCampaign.ToJson(s);
        }

        void GuestAt(int index)
        {
            leader = false;
            Session.ResetNewGame();
            Story.StartNewGame(0);
            using (new SettingsScope<Func<bool>>(() => GameHooks.CoopActive, v => GameHooks.CoopActive = v))
            {
                GameHooks.CoopActive = () => false;   // the guest's own steps, before the session (no reports)
                while (Story.Index < index) Story.Advance(db);
            }
        }

        [Test]
        public void HullFactorGrowsWithTheCrew()
        {
            Assert.AreEqual(1f, CoopCampaign.HullFactor(1), 1e-4f);
            Assert.AreEqual(1.5f, CoopCampaign.HullFactor(2), 1e-4f);
            Assert.AreEqual(2.5f, CoopCampaign.HullFactor(4), 1e-4f);
            Assert.AreEqual(5f, CoopCampaign.HullFactor(9), 1e-4f, "a bigger crew than the default four");
            Assert.AreEqual(1f, CoopCampaign.HullFactor(0), 1e-4f);
        }

        [Test]
        public void StrengthScalesHullAndHalfAsMuchDamage()
        {
            Assert.AreEqual(1f, CoopCampaign.HullFactor(4, 0), 1e-4f, "Off");
            Assert.AreEqual(1.75f, CoopCampaign.HullFactor(4, 25), 1e-4f);
            Assert.AreEqual(4f, CoopCampaign.HullFactor(4, 100), 1e-4f);
            Assert.AreEqual(1.25f, CoopCampaign.DamageFactor(2, 50), 1e-4f);
            Assert.AreEqual(2.5f, CoopCampaign.DamageFactor(4, 100), 1e-4f);
            Assert.AreEqual(5.5f, CoopCampaign.HullFactor(4, 150), 1e-4f);
            Assert.AreEqual(7f, CoopCampaign.HullFactor(4, 200), 1e-4f);
            Assert.AreEqual(4f, CoopCampaign.DamageFactor(4, 200), 1e-4f);
            CoopCampaign.SetSettings(200, 0, true);
            Assert.AreEqual(200, CoopCampaign.Strength, "the highest choice is kept");
            CoopCampaign.SetSettings(100, 0, true);
            Assert.AreEqual(2f, CoopCampaign.NpcDamageFactor, 1e-4f, "three players (the test's crew) at +100 %: half of it on the guns");
            GameHooks.CoopActive = () => false;
            Assert.AreEqual(1f, CoopCampaign.NpcDamageFactor, 1e-4f, "single player");
        }

        [Test]
        public void GuestsTakeTheLeadersCrewLimit()
        {
            leader = true;
            CoopCampaign.SetCrewLimit(8);
            string json = LeaderAt(5);
            CoopCampaign.SetCrewLimit(CoopCampaign.DefaultCrew);
            GuestAt(5);
            CoopCampaign.ApplyLeader(json, db);
            Assert.AreEqual(8, CoopCampaign.CrewLimit, "the lobby's crew N / 8");
            CoopCampaign.SetCrewLimit(500);
            Assert.AreEqual(CoopCampaign.MaxCrew, CoopCampaign.CrewLimit, "a session's own limit at most");
        }

        [Test]
        public void StoryRewardsAreSharedOrPaidInFull()
        {
            Assert.AreEqual(3334, CoopCampaign.Share(10000, 3, true), "shared out, rounded up");
            Assert.AreEqual(10000, CoopCampaign.Share(10000, 3, false), "each in full");
            Assert.AreEqual(10000, CoopCampaign.Share(10000, 1, true), "alone");
            Assert.AreEqual(0, CoopCampaign.Share(0, 4, true));
            CoopCampaign.SetSettings(50, 0, true);
            Assert.AreEqual(1000, CoopCampaign.Share(3000), "the session's crew of three");
            GameHooks.CoopActive = () => false;
            Assert.AreEqual(3000, CoopCampaign.Share(3000), "single player keeps the whole reward");
        }

        [Test]
        public void GuestsTakeTheLeadersSettings()
        {
            leader = true;
            CoopCampaign.SetSettings(100, 150, false);
            string json = LeaderAt(20);
            CoopCampaign.SetSettings(CoopCampaign.DefaultStrength, CoopCampaign.DefaultSpawns, true);
            GuestAt(20);
            CoopCampaign.ApplyLeader(json, db);
            Assert.AreEqual(100, CoopCampaign.Strength);
            Assert.AreEqual(150, CoopCampaign.Spawns, "the spawning is its own setting");
            Assert.IsFalse(CoopCampaign.SplitRewards);
        }

        [Test]
        public void AStateFromBeforeTheSpawningSettingSpawnsNoExtras()
        {
            var s = CoopCampaign.FromJson("{\"index\":20,\"strength\":100,\"splitRewards\":true}");
            Assert.AreEqual(100, s.strength);
            Assert.AreEqual(CoopCampaign.DefaultSpawns, s.spawns);
            Assert.AreEqual(0, CoopCampaign.DefaultSpawns, "Off: the story levels' own ships");
        }

        [Test]
        public void SpawnsAddShipsPerExtraPlayer()
        {
            Assert.AreEqual(1f, CoopCampaign.SpawnFactor(4, 0), 1e-4f, "Off");
            Assert.AreEqual(1f, CoopCampaign.SpawnFactor(1, 200), 1e-4f, "alone");
            Assert.AreEqual(2.5f, CoopCampaign.SpawnFactor(4, 50), 1e-4f);
            Assert.AreEqual(7f, CoopCampaign.SpawnFactor(4, 200), 1e-4f);

            float carry = 0f;
            Assert.AreEqual(0, CoopCampaign.ExtraShips(ref carry, 4, 0), "Off adds none");
            Assert.AreEqual(1, CoopCampaign.ExtraShips(ref carry, 2, 100), "+100 % with two players: one copy each");
            carry = 0f;
            int total = 0;
            for (int i = 0; i < 6; i++) total += CoopCampaign.ExtraShips(ref carry, 2, 50);
            Assert.AreEqual(3, total, "+50 % with two players: one for every second ship");
            carry = 0f;
            total = 0;
            for (int i = 0; i < 4; i++) total += CoopCampaign.ExtraShips(ref carry, 4, 50);
            Assert.AreEqual(6, total, "four ships for four players at +50 %: ten in all");
            carry = 0f;
            total = 0;
            for (int i = 0; i < 4; i++) total += CoopCampaign.ExtraShips(ref carry, 3, 25);
            Assert.AreEqual(2, total, "a fraction carried over, never lost");
        }

        [Test]
        public void StrengthAndSpawnsAreSeparate()
        {
            CoopCampaign.SetSettings(0, 200, true);
            Assert.AreEqual(0, CoopCampaign.Strength);
            Assert.AreEqual(200, CoopCampaign.Spawns);
            Assert.AreEqual(1f, CoopCampaign.NpcDamageFactor, 1e-4f, "more ships, no stronger guns");
            CoopCampaign.SetSettings(100, 500, true);
            Assert.AreEqual(200, CoopCampaign.Spawns, "+200 % at most");
        }

        [Test]
        public void TheGameWonBonusIsShared()
        {
            CoopCampaign.SetSettings(50, 0, true);
            leader = true;
            Session.ResetNewGame();
            Story.StartNewGame(0);
            while (Story.Index < Story.GameWonIndex - 1) Story.Advance(db);
            long before = Session.Credits;
            Story.Advance(db);
            Assert.AreEqual(before + 13334, Session.Credits, "40 000 for a crew of three");
        }

        [Test]
        public void GuestCatchesUpWithEveryStepsEffects()
        {
            string json = LeaderAt(49);
            GuestAt(0);
            CoopCampaign.ApplyLeader(json, db);
            Assert.AreEqual(49, Story.Index);
            Assert.IsNotNull(Session.ParkedShip, "step 48's loaner parks the guest's own ship");
            Assert.AreEqual(41, Session.ShipIndex, "step 49's stolen K'Suukk");
            Assert.Greater(CoopCampaign.CatchUpSerial, 0);
            Assert.IsEmpty(advanced, "a catch-up reports nothing to the leader");
        }

        [Test]
        public void GuestWaitsForItsOwnSuccessConversation()
        {
            string json = LeaderAt(10);
            GuestAt(9);
            Story.Mission.won = true;   // its success conversation is about to open
            CoopCampaign.ApplyLeader(json, db);
            Assert.AreEqual(9, Story.Index, "the conversation advances it");
            CoopCampaign.CatchUpTo(10, db);
            Assert.AreEqual(10, Story.Index, "the leader's level moved the crew on: at once");
        }

        [Test]
        public void GuestTakesTheLeadersSharedFieldsAndKeepsItsWonFlag()
        {
            string json = LeaderAt(116, s => { s.mission.value = 5; s.counter = 7; s.invasionStation = 33; });
            GuestAt(116);
            CoopCampaign.ApplyLeader(json, db);
            Assert.AreEqual(5, Story.Mission.value);
            Assert.AreEqual(7, Session.StoryCounter);
            Assert.AreEqual(33, Session.VoidInvasionStation);
            Assert.IsFalse(Story.Mission.won);
            Assert.IsFalse(CoopCampaign.RemoteWon);
        }

        [Test]
        public void LeaderWonStepReachesAGuestJoiningLater()
        {
            string json = LeaderAt(30, s => s.mission.won = true);
            GuestAt(30);
            CoopCampaign.ApplyLeader(json, db);
            Assert.IsTrue(CoopCampaign.RemoteWon, "its success conversation opens here too");
        }

        [Test]
        public void OnStepWonOnlyForTheCurrentStep()
        {
            GuestAt(30);
            CoopCampaign.OnStepWon(29);
            Assert.IsFalse(CoopCampaign.RemoteWon);
            CoopCampaign.OnStepWon(30);
            Assert.IsTrue(CoopCampaign.RemoteWon);
            Story.Mission.won = true;
            Assert.IsFalse(CoopCampaign.RemoteWon, "shown");
        }

        [Test]
        public void GuestOneStepBehindKeepsTheCrewsWin()
        {
            // The host mounts its weapons (6) while the guest still reads step 5's conversation (a player's report).
            string json = LeaderAt(7);
            GuestAt(5);
            CoopCampaign.OnStepWon(6);
            Assert.IsFalse(CoopCampaign.RemoteWon, "not this game's step yet");
            Story.Mission.won = true;   // step 5's own conversation closes
            Story.Advance(db);
            Assert.AreEqual(6, Story.Index);
            Assert.IsTrue(CoopCampaign.RemoteWon, "step 6's conversation opens here too");
            CoopCampaign.ApplyLeader(json, db);
            Assert.AreEqual(6, Story.Index, "the catch-up waits for it");
        }

        [Test]
        public void CatchUpStopsAtEachStepTheCrewWon()
        {
            string json = LeaderAt(8);
            GuestAt(5);
            CoopCampaign.OnStepWon(6);
            CoopCampaign.OnStepWon(7);
            CoopCampaign.ApplyLeader(json, db);
            Assert.AreEqual(6, Story.Index, "the silent catch-up stops at the first step the crew won");
            Assert.IsTrue(CoopCampaign.RemoteWon);
            Story.Mission.won = true;
            Story.Advance(db);   // its conversation closes
            Assert.AreEqual(7, Story.Index);
            Assert.IsTrue(CoopCampaign.RemoteWon, "then the next one");
            CoopCampaign.GuestTick(db);
            Assert.AreEqual(7, Story.Index);
            CoopCampaign.CatchUpTo(8, db);
            Assert.AreEqual(8, Story.Index, "the leader's level moving the crew on doesn't wait");
            Assert.IsFalse(CoopCampaign.RemoteWon);
        }

        [Test]
        public void AnOpenSuccessConversationHoldsTheCatchUpAndAdvancesOnce()
        {
            string json = LeaderAt(11);
            GuestAt(10);
            Story.Mission.won = true;
            CoopCampaign.SuccessOpened(10);
            CoopCampaign.ApplyLeader(json, db);
            Assert.AreEqual(10, Story.Index);
            Assert.IsTrue(CoopCampaign.SuccessClosed(10), "closed on its own step: it advances");
            CoopCampaign.SuccessOpened(10);
            CoopCampaign.CatchUpTo(11, db);
            Assert.AreEqual(11, Story.Index, "the leader's level took the crew along");
            Assert.IsFalse(CoopCampaign.SuccessClosed(10), "its close doesn't advance a second time");
            Assert.IsEmpty(advanced, "nothing for the leader to follow");
        }

        [Test]
        public void GuestAheadOfTheLeaderFindsNothingComplete()
        {
            string json = LeaderAt(11);
            GuestAt(12);
            CoopCampaign.ApplyLeader(json, db);
            Assert.IsTrue(CoopCampaign.AheadOfLeader, "docked at Kernstal it doesn't win step 12 before the leader has shown 11");
            GuestAt(11);
            CoopCampaign.ApplyLeader(json, db);
            Assert.IsFalse(CoopCampaign.AheadOfLeader);
            leader = true;
            Assert.IsFalse(CoopCampaign.AheadOfLeader, "never the leader");
        }

        [Test]
        public void AFailedChainForgetsTheCrewsWins()
        {
            string before = LeaderAt(23), failed = LeaderAt(24, s => s.failSerial = 1);
            GuestAt(23);
            CoopCampaign.ApplyLeader(before, db);
            CoopCampaign.OnStepWon(24);
            Story.Mission.won = true;   // the guest reads its own step 23 conversation
            CoopCampaign.ApplyLeader(failed, db);   // the leader's 25 failed: back to 24, to be won again
            Assert.AreEqual(23, Story.Index);
            Story.Advance(db);
            Assert.AreEqual(24, Story.Index);
            Assert.IsFalse(CoopCampaign.RemoteWon);
        }

        [Test]
        public void GuestProgressIsSentAndMerged()
        {
            string json = LeaderAt(116, s => s.mission.value = 1);
            GuestAt(116);
            CoopCampaign.ApplyLeader(json, db);
            Story.Mission.value |= 4;   // the bar at Pescal Inartu 92 searched
            CoopCampaign.GuestTick(db);
            Assert.AreEqual(1, progress.Count);
            var p = UnityEngine.JsonUtility.FromJson<CoopCampaign.Progress>(progress[0]);
            Assert.AreEqual(4, p.valueOr);
            CoopCampaign.GuestTick(db);
            Assert.AreEqual(1, progress.Count, "sent once");

            // The leader (bits merged with OR; its own bit 2 stays).
            LeaderAt(116, null);
            leader = true;
            Story.Mission.value = 2;
            CoopCampaign.Merge(progress[0]);
            Assert.AreEqual(6, Story.Mission.value);
        }

        [Test]
        public void CountsAreAddedAndConvoyTargetsMarked()
        {
            leader = true;
            Story.StartNewGame(0);
            Session.CampaignMission = 91;
            Story.Mission.value = 3;
            Session.StoryCounter = 2;
            Session.StoryTargets.Clear();
            Session.StoryTargets.AddRange(new[] { 101, 22, 40 });
            CoopCampaign.Merge(new CoopCampaign.Progress { index = 91, valueAdd = -2, counterAdd = 5, targetsDone = new List<int> { 22 } });
            Assert.AreEqual(1, Story.Mission.value);
            Assert.AreEqual(7, Session.StoryCounter);
            CollectionAssert.AreEqual(new[] { 101, -1, 40 }, Session.StoryTargets);
            CoopCampaign.Merge(new CoopCampaign.Progress { index = 90, valueAdd = 100 });
            Assert.AreEqual(1, Story.Mission.value, "another step's progress is dropped");
        }

        [Test]
        public void DiffFindsTheGuestsChanges()
        {
            var before = new CoopCampaign.State { index = 59, mission = new StoryMission { value = 2 }, counter = 1, targets = new List<int> { 101, 22 } };
            Session.CampaignMission = 59;
            Session.StoryMission = new StoryMission { value = 3 };
            Session.StoryCounter = 4;
            Session.StoryTargets.Clear();
            Session.StoryTargets.AddRange(new[] { 101, -1 });
            var p = CoopCampaign.Diff(before);
            Assert.AreEqual(1, p.valueAdd);
            Assert.AreEqual(3, p.counterAdd);
            CollectionAssert.AreEqual(new[] { 22 }, p.targetsDone);
        }

        [Test]
        public void GuestsOwnAdvanceIsReportedAndFollowed()
        {
            GuestAt(78);
            Story.Advance(db);   // the Khador misjump (SystemJump at 78)
            CollectionAssert.AreEqual(new[] { 78 }, advanced);

            advanced.Clear();
            Story.Mission.won = true;
            Story.Advance(db);   // a won step: its own conversation, nothing to report
            Assert.IsEmpty(advanced);

            LeaderAt(30);
            leader = true;
            Assert.IsTrue(CoopCampaign.FollowGuestAdvance(30, db));
            Assert.AreEqual(31, Story.Index);
            Assert.IsFalse(CoopCampaign.FollowGuestAdvance(30, db), "another step");
            Story.Mission.won = true;
            Assert.IsFalse(CoopCampaign.FollowGuestAdvance(31, db), "a won step advances with its conversation");
            LeaderAt(148);
            leader = true;
            Assert.IsTrue(CoopCampaign.FollowGuestAdvance(148, db));
            Assert.AreEqual(151, Story.Index, "the broker at Kalun Amir");
        }

        [Test]
        public void StepThirteenCountsTheCrewsMissionsOnce()
        {
            LeaderAt(13);
            leader = true;
            Assert.AreEqual(3, Story.Mission.value, "three missions, whatever this player did before");
            var ctx = new StoryContext { docked = true, station = 79 };
            CoopCampaign.LeaderTick();
            CoopCampaign.CountCrewMission(77);
            CoopCampaign.CountCrewMission(77);   // a squad mission's second member
            CoopCampaign.CountCrewMission(0);
            Assert.AreEqual(2, CoopCampaign.CrewMissions);
            Assert.IsFalse(Story.IsComplete(db, ctx));
            CoopCampaign.CountCrewMission(0);
            Assert.IsTrue(Story.IsComplete(db, ctx));
        }

        [Test]
        public void FailedLevelGoesBackToItsCheckpointForTheCrew()
        {
            string first = LeaderAt(25);
            leader = true;
            CoopCampaign.LeaderFailed();
            Assert.AreEqual(24, Story.Index, "the Void orbit's chain starts again at 24");
            var failed = CoopCampaign.Capture();
            Assert.AreEqual(1, failed.failSerial);

            GuestAt(25);
            CoopCampaign.ApplyLeader(first, db);   // the guest's first State: its serial noted
            CoopCampaign.ApplyLeader(CoopCampaign.ToJson(failed), db);
            Assert.AreEqual(24, Story.Index, "the guest follows the checkpoint");
            Assert.IsFalse(Story.Mission.won);
        }

        [Test]
        public void LeaderPublishesOnlyChanges()
        {
            LeaderAt(20);
            leader = true;
            CoopCampaign.LeaderTick();
            CoopCampaign.LeaderTick();
            Assert.AreEqual(1, published.Count);
            Story.Mission.value++;
            CoopCampaign.LeaderTick();
            Assert.AreEqual(2, published.Count);
        }

        [Test]
        public void NewGuestStartsWithTheLeadersKitWhereItDocked()
        {
            string json = LeaderAt(20, s => { s.kitShip = 5; s.kit = new List<ItemStack> { new ItemStack(2, 1) }; s.station = 55; });
            leader = false;
            Session.ResetNewGame();
            CoopCampaign.PrepareGuest(CoopCampaign.FromJson(json), db);
            Assert.AreEqual(20, Story.Index);
            Assert.AreEqual(5, Session.ShipIndex);
            Assert.AreEqual(55, Session.StationIndex);
            Assert.IsTrue(Session.Equipment.Exists(e => e.item == 2));
            Assert.IsFalse(Session.FreePlay);
        }

        [Test]
        public void NewCampaignStartsWithThePrologue()
        {
            Session.ResetNewGame();
            Session.ShipIndex = 0;          // NetGame.PrepareSession's multiplayer pilot: Betty and 10000 credits
            Session.Credits = 10000;
            CoopCampaign.NewCampaign(db, Campaign.GalaxyOnFire2);
            Assert.AreEqual(0, Story.Index, "the prologue, in flight (the crew gathered in the lobby first)");
            Assert.AreEqual(10, Session.ShipIndex, "the Phantom, not the multiplayer Betty (#229)");
            Assert.AreEqual(0, Session.Credits, "resetGame's credits");
            Assert.AreEqual(78, Session.StationIndex);
            Assert.IsFalse(Session.LaunchedFromStation);
            Assert.IsFalse(Session.FreePlay);
            Session.ResetNewGame();
            CoopCampaign.NewCampaign(db, Campaign.Valkyrie);
            Assert.AreEqual(Story.GameWonIndex, Story.Index, "the Valkyrie add-on's start");
        }

        [Test]
        public void TheHostContinuesOnlyTheThreeCampaignsWithoutHardcore()
        {
            Assert.IsFalse(CoopCampaign.CanHost(null), "an empty slot");
            Assert.IsTrue(CoopCampaign.CanHost(new SaveData { campaign = (int)Campaign.Supernova }));
            Assert.IsFalse(CoopCampaign.CanHost(new SaveData { hardcore = true }), "a death in a session ends nothing");
            Assert.IsFalse(CoopCampaign.CanHost(new SaveData { modCampaign = "some_mod:story" }), "a mod's campaign");
            Assert.IsFalse(CoopCampaign.CanHost(new SaveData { campaign = 7 }));
        }

        [Test]
        public void TheHostsGameSavesIntoTheSlotsUnderItsOwnDifficultyAndEconomy()
        {
            Func<bool> oldActive = NetSession.ActiveProvider, oldGame = NetSession.SessionGameProvider;
            try
            {
                NetSession.ActiveProvider = () => true;
                NetSession.SessionGameProvider = () => true;
                Assert.IsFalse(SaveGame.Save(3), "a session's game is never saved");
                SaveGame.BeginHostSaves(new SaveData { difficulty = Session.DifficultyHard, economy = (int)Economy.Default, version = SaveData.CurrentVersion });
                Assert.IsTrue(SaveGame.HostSaves);
                Session.Difficulty = Session.DifficultyNormal;   // the session's rules
                Session.Economy = Economy.Android;
                Session.PlaySeconds = 60f;
                Assert.IsTrue(SaveGame.Save(3), "the co-op host's game");
                SaveGame.AutoSave();
                foreach (int slot in new[] { 3, SaveGame.AutoSaveSlot })
                {
                    var saved = SaveGame.Preview(slot);
                    Assert.IsNotNull(saved, "slot " + slot);
                    Assert.AreEqual(Session.DifficultyHard, saved.difficulty, "the game's own difficulty");
                    Assert.AreEqual((int)Economy.Default, saved.economy, "the game's own economy");
                }
                SaveGame.BeginHostSaves(null);   // a new game: the session's own rules
                Assert.IsTrue(SaveGame.Save(4));
                Assert.AreEqual(Session.DifficultyNormal, SaveGame.Preview(4).difficulty);
                Assert.AreEqual((int)Economy.Android, SaveGame.Preview(4).economy);
                SaveGame.EndHostSaves();
                Assert.IsFalse(SaveGame.Save(5), "the session's end: no more");
                SaveGame.BeginHostSaves(null, Economy.Default);   // a new game the host picked the PC economy for
                Session.Economy = Economy.Default;
                Assert.IsTrue(SaveGame.Save(6));
                Assert.AreEqual((int)Economy.Default, SaveGame.Preview(6).economy, "the new game's pick");
                SaveGame.BeginHostSaves(null);
                NetSession.ActiveProvider = () => false;   // the connection went, the game still loaded
                Assert.IsFalse(SaveGame.Save(5));
            }
            finally
            {
                SaveGame.EndHostSaves();
                NetSession.ActiveProvider = oldActive;
                NetSession.SessionGameProvider = oldGame;
            }
        }

        // ---- the economy (NetSession.Economy: the host's, carried to the guests by NetState) ----

        [Test]
        public void TheCampaignPlaysTheSavesEconomyOrTheNewGamesPick()
        {
            var pc = new SaveData { version = SaveData.CurrentVersion, economy = (int)Economy.Default };
            var android = new SaveData { version = SaveData.CurrentVersion, economy = (int)Economy.Android };
            Assert.AreEqual(Economy.Default, CoopCampaign.HostEconomy(pc, Economy.Android), "a save keeps its own");
            Assert.AreEqual(Economy.Android, CoopCampaign.HostEconomy(android, Economy.Default));
            Assert.AreEqual(Economy.Android, CoopCampaign.HostEconomy(new SaveData { version = 9, economy = (int)Economy.Default }, Economy.Default),
                "a save from before version 10 played the Android tables");
            Assert.AreEqual(Economy.Default, CoopCampaign.HostEconomy(null, Economy.Default), "a new game: the host's pick");
            Assert.AreEqual(Economy.Android, CoopCampaign.HostEconomy(null, Economy.Android));
        }

        [Test]
        public void TheEconomyCrossesTheHandshakeIntact()
        {
            foreach (Economy e in Enum.GetValues(typeof(Economy)))
                Assert.AreEqual(e, NetSession.EconomyFromCode(NetSession.EconomyCode(e)), e.ToString());
            Assert.AreEqual(Economy.Android, NetSession.EconomyFromCode(0), "NetState's default: every free-roam session");
            Assert.AreEqual(Economy.Android, NetSession.EconomyFromCode(7), "a value this build doesn't know");
            Assert.AreEqual(0, NetSession.EconomyCode(Economy.Android), "Android is the variable's default, as before the economy was sent");
        }

        [Test]
        public void AGuestLoadsTheHostsTables()
        {
            var savedNet = NetSession.Economy;
            try
            {
                Session.Economy = Economy.Default;   // the host
                var host = Database.Load();
                Session.Economy = NetSession.EconomyFromCode(NetSession.EconomyCode(Economy.Default));   // a guest, from NetState
                var guest = Database.Load();
                Session.Economy = Economy.Android;
                var android = Database.Load();
                Assert.AreEqual(Economy.Default, guest.Economy);
                Assert.AreEqual(host.Items.Count, guest.Items.Count);
                int differ = 0;
                for (int i = 0; i < host.Items.Count; i++)
                {
                    Assert.AreEqual(host.Items[i].minPrice, guest.Items[i].minPrice, "item " + i);
                    Assert.AreEqual(host.Items[i].maxPrice, guest.Items[i].maxPrice, "item " + i);
                    if (host.Items[i].maxPrice != android.Items[i].maxPrice) differ++;
                }
                for (int i = 0; i < host.Ships.Count; i++) Assert.AreEqual(host.Ships[i].price, guest.Ships[i].price, "ship " + i);
                Assert.Greater(differ, 50, "the Default tables aren't the Android ones (the handshake matters)");
                // NetGame.Db, the session's look-ups, follows the economy set after its first use.
                NetGame.ResetDb();
                Assert.AreEqual(Economy.Android, NetGame.Db.Economy);
                Session.Economy = Economy.Default;
                Assert.AreEqual(Economy.Default, NetGame.Db.Economy, "read again for the co-op campaign's economy");
            }
            finally
            {
                NetSession.Economy = savedNet;
                NetGame.ResetDb();
            }
        }

        [Test]
        public void AProfileAppliesUnderTheSessionsEconomy()
        {
            var savedNet = NetSession.Economy;
            try
            {
                Session.Economy = Economy.Android;
                Session.Credits = 12345;
                string json = SaveGame.ProfileJson();   // a guest's profile from an Android campaign
                Session.Economy = NetSession.Economy = Economy.Default;   // this campaign plays the PC economy
                var db = Database.Load();
                Assert.IsTrue(SaveGame.TryParse(json, db, out var profile, out string problem), "a persistent profile keeps working: " + problem);
                Assert.AreEqual((int)Economy.Android, profile.economy);
                SaveGame.ApplyProfile(profile);
                Assert.AreEqual(Economy.Default, Session.Economy, "the session's economy, not the profile's");
                Assert.AreEqual(12345, Session.Credits, "the profile's own game");
                NetSession.Economy = Economy.Android;   // and back in an Android session
                SaveGame.ApplyProfile(profile);
                Assert.AreEqual(Economy.Android, Session.Economy);
            }
            finally { NetSession.Economy = savedNet; }
        }
    }
}
