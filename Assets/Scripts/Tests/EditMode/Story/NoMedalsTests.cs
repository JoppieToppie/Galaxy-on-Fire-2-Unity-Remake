// NoMedalsTests.cs
// Feature: CLAUDE.md "Bar and freelance" > "A game without medals" (remake, a player's request 2026-11-04;
// Session.NoMedals, the new game's Medals toggle): no medal is awarded, counted toward or carried in, and the content the
// medals unlock comes from the story instead: Thynome's VoidX once the main story is won (Story.GameWonIndex), the
// fireworks blueprint and Katashán's Specter once the Supernova story is finished (Story.LastIndex). Saved from version
// 18; older saves have medals. Expected values: the gates in Shop.GenerateShips / Story.OnDocked (the VoidX is ship 8,
// the Specter 44, Katashán station 120, Thynome 10), Achievements.Check / Elite / ProgressText, the add-on starts' medals
// (Story.StartCampaign: 23 bronze, 30 gold). The station's docking texts (StationMenu.CheckMedalHints) are a MonoBehaviour's:
// not covered here.

using System.IO;
using System.Text.RegularExpressions;
using GoF2Remake.Data;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public class NoMedalsTests : StoryAreaTestBase
    {
        const int VoidX = 8, Specter = 44, Thynome = 10, Katashan = 120;

        static void AllGold()
        {
            for (int i = 0; i < Achievements.Count; i++) Session.Medals[i] = 1;
        }

        [Test]
        public void ANewGameHasMedals()
        {
            Assert.IsFalse(Session.NoMedals);
            Assert.IsTrue(Achievements.Enabled);
            Session.NoMedals = true;
            Session.ResetNewGame();
            Assert.IsFalse(Session.NoMedals, "chosen for each new game");
        }

        [Test]
        public void NoMedalIsAwardedOrCountedInFlight()
        {
            Session.NoMedals = true;
            Session.Kills = 1000;
            var improved = Achievements.Check(Db);
            Achievements.Elite(38, 50);
            Assert.AreEqual(0, improved.Count);
            Assert.AreEqual(0, Session.Medals[0], "not even the preset Veteran");
            Assert.AreEqual(0, Session.Medals[4]);
            Assert.IsFalse(Session.EliteFlags.Contains(38));
            Assert.IsNull(Achievements.ProgressText(40, 10), "no medal progress on the HUD");
            Assert.IsFalse(Achievements.Enabled);
        }

        [Test]
        public void TheVoidXComesWithTheMainStoryWon()
        {
            Session.NoMedals = true;
            Session.CampaignMission = Story.GameWonIndex - 1;
            Assert.IsFalse(Achievements.VoidXUnlocked);
            Session.CampaignMission = Story.GameWonIndex;
            Assert.IsTrue(Achievements.VoidXUnlocked);
            Assert.IsFalse(Achievements.SupernovaUnlocked);
            Session.CampaignMission = Story.LastIndex;
            Assert.IsTrue(Achievements.SupernovaUnlocked);
        }

        [Test]
        public void WithMedalsTheGatesStayTheMedals()
        {
            Session.CampaignMission = Story.LastIndex;
            Assert.IsFalse(Achievements.VoidXUnlocked, "the story alone doesn't open them in a game with medals");
            Assert.IsFalse(Achievements.SupernovaUnlocked);
            for (int i = 0; i < Achievements.BaseCount; i++) Session.Medals[i] = 1;
            Assert.IsTrue(Achievements.VoidXUnlocked);
            Assert.IsFalse(Achievements.SupernovaUnlocked);
            AllGold();
            Assert.IsTrue(Achievements.SupernovaUnlocked);
        }

        [Test]
        public void FreePlayOpensNoGate()
        {
            Session.NoMedals = true;
            Session.FreePlay = true;
            Session.CampaignMission = Story.LastIndex;
            Assert.IsFalse(Achievements.VoidXUnlocked);
            Assert.IsFalse(Achievements.SupernovaUnlocked);
        }

        [Test]
        public void ThynomeSellsTheVoidXAfterTheMainStory()
        {
            Session.NoMedals = true;
            Session.CampaignMission = Story.GameWonIndex - 1;
            for (int r = 0; r < 20; r++) CollectionAssert.DoesNotContain(Shop.GenerateShips(Db, Thynome), VoidX);
            Session.CampaignMission = Story.GameWonIndex;
            for (int r = 0; r < 20; r++) Assert.AreEqual(VoidX, Shop.GenerateShips(Db, Thynome)[0]);
            // ... and a stock rolled before the story was won gets it at the next docking.
            var stock = new StationStock { station = Thynome };
            Story.OnDocked(Db, Thynome, stock);
            Assert.AreEqual(VoidX, stock.ships[0]);
        }

        [Test]
        public void KatashanSellsTheSpecterAfterTheSupernovaStory()
        {
            Session.NoMedals = true;
            Session.CampaignMission = Story.LastIndex - 1;
            for (int r = 0; r < 20; r++) CollectionAssert.DoesNotContain(Shop.GenerateShips(Db, Katashan), Specter);
            Session.CampaignMission = Story.LastIndex;
            int lists = 0;
            for (int r = 0; r < 40; r++)
            {
                var ships = Shop.GenerateShips(Db, Katashan);
                if (ships.Count == 0) continue;   // an empty dealer: "no rolls, no extras either"
                lists++;
                CollectionAssert.Contains(ships, Specter);
            }
            Assert.Greater(lists, 0);
        }

        [Test]
        public void TheAddOnStartsGiveNoMedals()
        {
            Session.NoMedals = true;
            Story.StartCampaign(Db, Campaign.Valkyrie);
            Assert.AreEqual(0, Session.Medals[23]);
            Assert.AreEqual(0, Session.Medals[30]);
            Assert.IsTrue(Achievements.VoidXUnlocked, "a Valkyrie start counts the main story as won");

            Session.ResetNewGame();
            Story.StartCampaign(Db, Campaign.Valkyrie);
            Assert.AreEqual(3, Session.Medals[23], "with medals: 23 bronze");
            Assert.AreEqual(1, Session.Medals[30], "and 30 gold");
        }

        [Test]
        public void NewGamePlusCarriesNoMedalsIntoAGameWithout()
        {
            var old = new SaveData { medals = new int[Achievements.Count] };
            for (int i = 0; i < old.medals.Length; i++) old.medals[i] = 1;
            Session.NoMedals = true;
            NewGamePlus.Apply(old);
            Assert.IsFalse(Achievements.GotAllGoldMedals);
            Assert.AreEqual(0, Session.Medals[4]);
        }

        [Test]
        public void TheChoiceIsSavedAndOlderSavesHaveMedals()
        {
            Assert.AreEqual(18, SaveData.CurrentVersion);
            Session.NoMedals = true;
            Assert.IsTrue(SaveGame.Save(1));
            Session.ResetNewGame();
            Assert.IsTrue(SaveGame.Load(1));
            Assert.IsTrue(Session.NoMedals);
            Assert.IsTrue(SaveGame.Preview(1).noMedals);

            // A version 17 save has no such field: medals on.
            string json = File.ReadAllText(SaveGame.SlotPath(1));
            json = Regex.Replace(json, @"""noMedals""\s*:\s*true\s*,", "");
            json = Regex.Replace(json, @"""version""\s*:\s*18", "\"version\": 17");
            StringAssert.Contains("\"version\": 17", json);
            StringAssert.DoesNotContain("noMedals", json);
            File.WriteAllText(SaveGame.SlotPath(1), json);
            Session.NoMedals = true;
            Assert.IsTrue(SaveGame.Load(1));
            Assert.IsFalse(Session.NoMedals);
        }
    }
}
