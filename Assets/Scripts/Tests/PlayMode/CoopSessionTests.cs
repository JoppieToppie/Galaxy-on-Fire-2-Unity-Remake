// CoopSessionTests.cs
// A co-op campaign session runs (docs/notes/multiplayer-coop.md): hosting a new campaign (NetGame.HostWantsCoop) waits in
// its lobby (NetGame.InLobby) with the crew's story published (NetState.CoopStory) in the story's world (not the finished
// one); the host's Start opens the prologue in flight, led by this game (its pirates the crew's ships); skipping the
// prologue and the rescue docks at Var Hastra; a launch into a story orbit runs the level as the leader (CampaignLevel, not
// a follower) with its ships marked as the crew's (NpcShip.StoryShip); the ending's credits play in the session (the main
// menu scene with Session.EndingPending, the session kept) and end docked at Thynome at step 44. The host's new game is
// saved into the auto-save slot as in single player (SaveGame.HostSaves), on the session's Normal / Android rules.
// Hosting a save slot instead: no lobby, the host docked in the save's game at its step (the crew's story), played on
// the session's rules and saved back under the save's own difficulty and economy.

using System.Collections;
using GoF2Remake.Data;
using GoF2Remake.Multiplayer;
using GoF2Remake.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace GoF2Remake.Tests
{
    [Category("Smoke")]
    public class CoopSessionTests : PlayModeTest
    {
        string savedName;
        bool savedCoop;
        int savedNew, savedSlot;

        [SetUp]
        public void CoopHostSettings()
        {
            savedName = PlayerPrefs.GetString("mp_name", "");
            PlayerPrefs.SetString("mp_name", "Leader");
            savedCoop = NetGame.HostWantsCoop;
            savedNew = NetGame.CoopNewCampaign;
            savedSlot = NetGame.CoopSlot;
        }

        [TearDown]
        public void RestoreSettings()
        {
            PlayerPrefs.SetString("mp_name", savedName);
            NetGame.HostWantsCoop = savedCoop;
            NetGame.CoopNewCampaign = savedNew;
            NetGame.CoopSlot = savedSlot;
        }

        [UnityTest]
        public IEnumerator HostLeadsTheCampaignIntoAStoryOrbit()
        {
            yield return Load("MainMenu");
            // After the menu (its Host card reads the World choice from PlayerPrefs).
            NetGame.HostWantsCoop = true;
            NetGame.CoopNewCampaign = (int)Campaign.GalaxyOnFire2;
            Assert.IsTrue(NetGame.CanHost(), "the port is free");
            Assert.IsTrue(NetGame.StartHost(), NetGame.Status);
            yield return WaitFor(() => NetGame.InLobby, 60f, "the new campaign's lobby");
            Assert.IsTrue(NetCoop.Active, "a co-op session");
            Assert.IsTrue(CoopCampaign.Leader, "the host leads");
            Assert.IsFalse(Session.FreePlay, "the story's world");
            Assert.AreEqual(0, Story.Index, "the prologue waits for the crew");
            yield return Run(1f);
            var published = CoopCampaign.FromJson(NetState.Instance.CoopStory);
            Assert.IsNotNull(published, "the crew's story is out");
            Assert.AreEqual(0, published.index);
            Assert.IsNull(Find<SpaceLevel>(), "nothing loads before the Start");

            NetCoop.StartCampaign();
            yield return WaitFor(() => Find<SpaceLevel>() != null && CampaignLevel.Current != null, 60f, "the prologue");
            yield return Run(2f);
            Assert.IsFalse(NetGame.InLobby);
            Assert.IsNotNull(CampaignLevel.Current.Intro, "the prologue's cutscenes");
            Assert.IsFalse(CampaignLevel.Current.Follower, "the leader runs it");
            foreach (var s in CampaignLevel.Current.Ships) if (s != null) Assert.IsTrue(s.StoryShip, "the pirates are the crew's");
            CampaignLevel.Current.SkipCutscene();   // -> the rescue
            yield return WaitFor(() => Story.Index == 1 && CampaignLevel.Current != null && CampaignLevel.Current.Intro != null, 60f, "the rescue");
            yield return Run(1f);
            CampaignLevel.Current.SkipCutscene();   // -> docked at Var Hastra
            yield return WaitFor(() => Find<StationLevel>() != null, 60f, "docked after the rescue");
            Assert.AreEqual(78, Session.StationIndex, "Var Hastra");
            yield return Run(2f);

            // Step 7, the pirate trap in Var Hastra's orbit: launching builds the story level, led by this game.
            var db = Database.Load();
            while (Story.Index < 7) Story.Advance(db);
            Find<StationLevel>().Launch();
            yield return WaitFor(() => Find<SpaceLevel>() != null && Find<SpaceLevel>().Player != null, 60f, "the story orbit");
            yield return Run(3f);
            var campaign = CampaignLevel.Current;
            Assert.IsNotNull(campaign, "a story orbit");
            Assert.IsFalse(campaign.Follower, "the leader runs it");
            Assert.Greater(campaign.Ships.Count, 0);
            foreach (var s in campaign.Ships) if (s != null) Assert.IsTrue(s.StoryShip, "the crew's ship");
            Assert.IsFalse(campaign.CoopHeld, "nobody reads a conversation");

            // Step 43 won by the crew while this player flies: the credits in the session, then Thynome and the epilogue.
            while (Story.Index < 43) Story.Advance(db);
            Story.Mission.won = true;
            Find<SpaceLevel>().PlayEnding();
            yield return WaitFor(() => Find<GoF2Remake.UI.EndingCredits>() != null, 60f, "the credits");
            yield return Run(2f);
            Assert.IsTrue(NetSession.Active, "the session runs on through the credits");
            var finish = typeof(GoF2Remake.UI.EndingCredits).GetMethod("Finish", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            finish.Invoke(Find<GoF2Remake.UI.EndingCredits>(), null);
            yield return WaitFor(() => Find<StationLevel>() != null, 60f, "docked after the credits");
            Assert.AreEqual(44, Story.Index, "Keith's epilogue");
            Assert.AreEqual(10, Session.StationIndex, "Thynome");
            Assert.IsFalse(Session.EndingPending);
            Assert.IsTrue(NetSession.Active, "still in the session");
            yield return Run(2f);

            NetGame.Shutdown();
            yield return Run(1f);
            NetGame.ShutdownNow();
            yield return Run(1f);
            Assert.IsFalse(NetSession.Active, "the session ended");
            Assert.IsFalse(SaveGame.HostSaves, "no more saves after the session");
            var saved = SaveGame.Preview(SaveGame.AutoSaveSlot);
            Assert.IsNotNull(saved, "the host's game in the auto-save");
            Assert.AreEqual(44, saved.campaignMission, "saved docked at Thynome when the host left");
            Assert.AreEqual((int)Campaign.GalaxyOnFire2, saved.campaign);
            Assert.AreEqual(Session.DifficultyNormal, saved.difficulty, "a new co-op game: the session's rules");
            Assert.AreEqual((int)Economy.Android, saved.economy);
        }

        [UnityTest]
        public IEnumerator HostContinuesASaveSlot()
        {
            yield return Load("MainMenu");
            // A single-player game at step 7 on Extreme with the Default economy, in slot 3.
            var db = Database.Load();
            Session.ResetNewGame();
            Story.StartNewGame(0);
            while (Story.Index < 7) Story.Advance(db);
            Session.StationIndex = 78;
            Session.Difficulty = Session.DifficultyExtreme;
            Session.Economy = Economy.Default;
            Session.Credits = 123456;
            Session.PlaySeconds = 600f;
            Assert.IsTrue(SaveGame.Save(3));
            Session.ResetNewGame();

            NetGame.HostWantsCoop = true;
            NetGame.CoopNewCampaign = -1;
            NetGame.CoopSlot = 3;
            Assert.IsTrue(NetGame.CanHost(), "the port is free");
            Assert.IsTrue(NetGame.StartHost(), NetGame.Status);
            yield return WaitFor(() => Find<StationLevel>() != null, 60f, "docked where the save was (no lobby for a save)");
            yield return Run(2f);
            Assert.IsTrue(CoopCampaign.Leader, "the host leads");
            Assert.AreEqual(7, Story.Index, "the save's step");
            Assert.AreEqual(123456, Session.Credits, "the save's credits");
            Assert.AreEqual(Session.DifficultyNormal, Session.Difficulty, "the session's rules");
            Assert.IsTrue(SaveGame.HostSaves);
            Assert.AreEqual(7, CoopCampaign.FromJson(NetState.Instance.CoopStory).index, "the crew's story is the save's");

            NetGame.Shutdown();
            yield return Run(1f);
            NetGame.ShutdownNow();
            yield return Run(1f);
            var saved = SaveGame.Preview(SaveGame.AutoSaveSlot);
            Assert.IsNotNull(saved, "the host's game in the auto-save");
            Assert.AreEqual(7, saved.campaignMission);
            Assert.AreEqual(Session.DifficultyExtreme, saved.difficulty, "saved back under its own difficulty");
            Assert.AreEqual((int)Economy.Default, saved.economy, "and its own economy");
            Assert.AreEqual(Session.DifficultyExtreme, SaveGame.Preview(3).difficulty, "the slot it continued is left as it was");
        }
    }
}
