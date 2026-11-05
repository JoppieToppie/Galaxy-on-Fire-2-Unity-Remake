// SaveVersionTests.cs
// Feature: CLAUDE.md "UI and platforms" > Save games (the versioned remake format) and the version notes in
// SaveData (SaveGame.cs): what a save of an older version loads as. Expected values from those notes: version 1 = before
// the story (free play at index 20); the bar (3), the Kaamo Club (4), combat (5), the Valkyrie story (6) and the Most Wanted
// boards (7) are only read from saves that have them; before version 10 every game played the Android economy; before
// version 11 a story agent whose blueprint the player owns was bought from (agents.json: agent 0 Geryan Zhamoum sells
// blueprint 11).

using GoF2Remake.Data;
using NUnit.Framework;
using System.IO;
using UnityEngine;

namespace GoF2Remake.Tests
{
    public class SaveVersionTests : SavesModsTest
    {
        void Load(string json)
        {
            Directory.CreateDirectory(Path.Combine(Sandbox, "Saves"));
            File.WriteAllText(SaveGame.SlotPath(1), Q(json));
            Assert.IsTrue(SaveGame.Load(1));
        }

        [Test]
        public void CurrentVersionIs18()
        {
            Assert.AreEqual(18, SaveData.CurrentVersion);
            Assert.AreEqual(18, new SaveData().version);
        }

        [Test]
        public void Version1IsFreePlayAtIndex20()
        {
            Load("{ 'version': 1, 'station': 70, 'ship': 5, 'credits': 777, 'campaignMission': 33, 'freePlay': false, 'unsaleable': [90], 'kills': 4 }");
            Assert.IsTrue(Session.FreePlay);
            Assert.AreEqual(Session.FreePlayMission, Session.CampaignMission);
            Assert.AreEqual(20, Session.FreePlayMission);
            Assert.AreEqual(70, Session.StationIndex);
            Assert.AreEqual(5, Session.ShipIndex);
            Assert.AreEqual(777, Session.Credits);
            Assert.AreEqual(4, Session.Kills);
            Assert.AreEqual(0, Session.Unsaleable.Count, "the story's fields aren't read from a version 1 save");
        }

        [Test]
        public void TheBarIsReadFromVersion3()
        {
            Load("{ 'version': 2, 'station': 78, 'ship': 10, 'campaignMission': 20, 'passengers': 5, 'freelanceCompleted': 3 }");
            Assert.AreEqual(0, Session.Passengers);
            Assert.AreEqual(3, Session.FreelanceCompleted, "the story's counters are version 2");
            Load("{ 'version': 3, 'station': 78, 'ship': 10, 'campaignMission': 20, 'passengers': 5 }");
            Assert.AreEqual(5, Session.Passengers);
        }

        [Test]
        public void TheKaamoClubIsReadFromVersion4()
        {
            Load("{ 'version': 3, 'station': 78, 'ship': 10, 'campaignMission': 20, 'kaamoState': 3, 'kaamoItems': [ { 'item': 2, 'amount': 1 } ] }");
            Assert.AreEqual(0, Session.KaamoState);
            Assert.AreEqual(0, Session.KaamoItems.Count);
            Load("{ 'version': 4, 'station': 78, 'ship': 10, 'campaignMission': 20, 'kaamoState': 3, 'kaamoItems': [ { 'item': 2, 'amount': 1 } ] }");
            Assert.AreEqual(3, Session.KaamoState);
            Assert.AreEqual(1, Session.KaamoItems.Count);
        }

        [Test]
        public void CombatIsReadFromVersion5()
        {
            Load("{ 'version': 4, 'station': 78, 'ship': 10, 'campaignMission': 20, 'selectedSecondary': 36, 'bombsDetonated': 2, 'cloakMs': 5000 }");
            Assert.AreEqual(-1, Session.SelectedSecondary);
            Assert.AreEqual(0, Session.BombsDetonated);
            Assert.AreEqual(0, Session.CloakMs);
            Load("{ 'version': 5, 'station': 78, 'ship': 10, 'campaignMission': 20, 'selectedSecondary': 36, 'bombsDetonated': 2, 'cloakMs': 5000 }");
            Assert.AreEqual(36, Session.SelectedSecondary);
            Assert.AreEqual(2, Session.BombsDetonated);
            Assert.AreEqual(5000, Session.CloakMs);
        }

        [Test]
        public void TheParkedShipIsReadFromVersion6()
        {
            const string rest = ", 'station': 78, 'ship': 10, 'campaignMission': 20, 'hasParkedShip': true, 'parkedShip': { 'ship': 22 }, 'storyTargets': [56] }";
            Load("{ 'version': 5" + rest);
            Assert.IsNull(Session.ParkedShip);
            Assert.AreEqual(0, Session.StoryTargets.Count);
            Load("{ 'version': 6" + rest);
            Assert.IsNotNull(Session.ParkedShip);
            Assert.AreEqual(22, Session.ParkedShip.ship);
            CollectionAssert.AreEqual(new[] { 56 }, Session.StoryTargets);
        }

        [Test]
        public void TheMostWantedBoardsAreReadFromVersion7()
        {
            const string rest = ", 'station': 78, 'ship': 10, 'campaignMission': 20, 'wanted': [ { 'active': true, 'current': 70 } ], 'collectedBounties': [1, 2, 3, 4], 'wantedHints': 3, 'hiddenBlueprintsFound': 2 }";
            Session.Wanted.Add(new WantedState());
            Load("{ 'version': 6" + rest);
            Assert.AreEqual(0, Session.Wanted.Count);
            CollectionAssert.AreEqual(new int[4], Session.CollectedBounties);
            Assert.AreEqual(0, Session.WantedHints);
            Assert.AreEqual(0, Session.HiddenBlueprintsFound);
            Load("{ 'version': 7" + rest);
            Assert.AreEqual(1, Session.Wanted.Count);
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4 }, Session.CollectedBounties);
            Assert.AreEqual(3, Session.WantedHints);
            Assert.AreEqual(2, Session.HiddenBlueprintsFound);
        }

        [Test]
        public void SavesBeforeVersion10PlayedTheAndroidEconomy()
        {
            Assert.AreEqual(Economy.Android, SaveGame.SaveEconomy(new SaveData { version = 9, economy = (int)Economy.Default }));
            Assert.AreEqual(Economy.Default, SaveGame.SaveEconomy(new SaveData { version = 10, economy = (int)Economy.Default }));
            Assert.AreEqual(Economy.Android, SaveGame.SaveEconomy(new SaveData { version = 10, economy = (int)Economy.Android }));
            Session.Economy = Economy.Default;
            Load("{ 'version': 9, 'station': 78, 'ship': 10, 'campaignMission': 20, 'economy': 1 }");
            Assert.AreEqual(Economy.Android, Session.Economy);
            Load("{ 'version': 10, 'station': 78, 'ship': 10, 'campaignMission': 20, 'economy': 1 }");
            Assert.AreEqual(Economy.Default, Session.Economy);
        }

        [Test]
        public void BeforeVersion11AnOwnedBlueprintMarksItsSellerAsBoughtFrom()
        {
            // agents.json: story agent 0 (Geryan Zhamoum) sells blueprint 11.
            Load("{ 'version': 10, 'station': 78, 'ship': 10, 'campaignMission': 20, 'unlockedBlueprints': [11] }");
            Assert.IsTrue(Session.StoryAgentsAccepted.Contains(0));
            Load("{ 'version': 11, 'station': 78, 'ship': 10, 'campaignMission': 20, 'unlockedBlueprints': [11] }");
            Assert.IsFalse(Session.StoryAgentsAccepted.Contains(0), "version 11 saves its own list");
            Load("{ 'version': 11, 'station': 78, 'ship': 10, 'campaignMission': 20, 'unlockedBlueprints': [11], 'storyAgentsAccepted': [0, 4] }");
            CollectionAssert.AreEquivalent(new[] { 0, 4 }, Session.StoryAgentsAccepted);
        }

        [Test]
        public void APaidCoordinatesSellerRevealsItsSystemOnLoad()
        {
            // agents.json: story agents 12 (Ylook Pykku) sells Suteo (0), 14 (Cyphron) Y'mirr (10). Discord "Bug with buying a
            // hidden systems": bought while the map was overridden, the reveal was lost and the seller had nothing left.
            string hidden = "[" + string.Join(",", System.Linq.Enumerable.Repeat("false", 34)) + "]";
            Load("{ 'version': 17, 'station': 78, 'ship': 10, 'campaignMission': 20, 'storyAgentsAccepted': [12, 14], 'systemVisible': " + hidden + " }");
            Assert.IsTrue(Session.SystemVisible[0] && Session.SystemVisible[10]);
            Assert.IsFalse(Session.SystemVisible[1], "Pan's seller wasn't paid");
        }

        [Test]
        public void TheLaterFieldsDefaultWhenAnOlderSaveLacksThem()
        {
            // Versions 13-17 add the quests, a mod campaign, hardcore, the mods' game options and the pirate board.
            Session.Hardcore = true;
            Session.ModCampaign = "x";
            Session.ModGameOptions.Add("a:b");
            Session.PirateSightings.Add(new PirateSighting());
            Load("{ 'version': 12, 'station': 78, 'ship': 10, 'campaignMission': 20 }");
            Assert.IsFalse(Session.Hardcore);
            Assert.AreEqual("", Session.ModCampaign);
            Assert.AreEqual(0, Session.ModGameOptions.Count);
            Assert.AreEqual(0, Session.PirateSightings.Count);
            Assert.AreEqual(0, Session.GraphQuests.Count);
            Assert.IsFalse(string.IsNullOrEmpty(Session.RunId), "an older save keeps the new run id the load made");
        }

        [Test]
        public void AnOlderSaveFileStaysAsItIsUntilSavedAgain()
        {
            Load("{ 'version': 3, 'station': 78, 'ship': 10, 'campaignMission': 20 }");
            Assert.AreEqual(3, SaveGame.Preview(1).version);
            SaveGame.Save(1);
            Assert.AreEqual(SaveData.CurrentVersion, SaveGame.Preview(1).version);
        }
    }
}
