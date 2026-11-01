// CoopCampaignTests.cs
// Feature: docs/notes/multiplayer-coop.md (CoopCampaign: the co-op campaign's story rules). The leader's State and a guest
// catching up to it with every step's own side effects, the wait for a guest's own success conversation, a guest's progress
// on the step's shared counters and the leader's merge (bits at 116 / 125 / 148, counts elsewhere, convoy targets), the
// leader following a guest's own advance, step 13's crew missions, a won step reported by another crew member, a failed
// level's checkpoint, and the story levels' hull factor. The hooks stand in for the session (no network).

using System;
using System.Collections.Generic;
using GoF2Remake.Data;
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
            CoopCampaign.SetSettings(100, true);
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
            CoopCampaign.SetSettings(50, true);
            Assert.AreEqual(1000, CoopCampaign.Share(3000), "the session's crew of three");
            GameHooks.CoopActive = () => false;
            Assert.AreEqual(3000, CoopCampaign.Share(3000), "single player keeps the whole reward");
        }

        [Test]
        public void GuestsTakeTheLeadersSettings()
        {
            leader = true;
            CoopCampaign.SetSettings(100, false);
            string json = LeaderAt(20);
            CoopCampaign.SetSettings(CoopCampaign.DefaultStrength, true);
            GuestAt(20);
            CoopCampaign.ApplyLeader(json, db);
            Assert.AreEqual(100, CoopCampaign.Strength);
            Assert.IsFalse(CoopCampaign.SplitRewards);
        }

        [Test]
        public void TheGameWonBonusIsShared()
        {
            CoopCampaign.SetSettings(50, true);
            leader = true;
            Session.ResetNewGame();
            Story.StartNewGame(0);
            while (Story.Index < Story.GameWonIndex - 1) Story.Advance(db);
            int before = Session.Credits;
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
            CoopCampaign.NewCampaign(db, Campaign.GalaxyOnFire2);
            Assert.AreEqual(0, Story.Index, "the prologue, in flight (the crew gathered in the lobby first)");
            Assert.AreEqual(78, Session.StationIndex);
            Assert.IsFalse(Session.LaunchedFromStation);
            Assert.IsFalse(Session.FreePlay);
            Session.ResetNewGame();
            CoopCampaign.NewCampaign(db, Campaign.Valkyrie);
            Assert.AreEqual(Story.GameWonIndex, Story.Index, "the Valkyrie add-on's start");
        }
    }
}
