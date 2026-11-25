// BlueprintSaveTests.cs
// Feature: CLAUDE.md "Bar and freelance" > Blueprints (the player's own invested units, BlueprintState.playerUnits /
// playerSpent, which TAKE may give back) and "UI and platforms" > Save games: they are saved with the game (SaveGame's
// JSON of Session.Blueprints), and a save from before them loads with 0 for every slot, so nothing in it can be taken
// back. The amounts are arbitrary test data.

using System.IO;
using System.Text.RegularExpressions;
using GoF2Remake.Data;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    public class BlueprintSaveTests : SavesModsTest
    {
        [Test]
        public void NewGameRulesRoundTripAndStayIndependentOfPresets()
        {
            const SavedGameRules rules = SavedGameRules.CapitalShips |
                SavedGameRules.CloakBay | SavedGameRules.BlueprintAutocomplete | SavedGameRules.Tutorials;
            Session.GameRules = rules;
            Session.Difficulty = Session.DifficultyExtreme;
            Session.StackKaamoUpgrades = true;
            Session.KaamoUpgradeLimit = 3;
            Settings.KaamoVipMode = Settings.KaamoVipCard;
            Assert.IsTrue(SaveGame.Save(5));
            Session.ResetNewGame();
            using (new DataUiTestKit.PrefsSnapshot())
            {
                Settings.ApplyScopedExperience(false, 1);
                Assert.AreEqual(SavedGameRules.None, Session.GameRules);
                Assert.IsTrue(SaveGame.Load(5));
                Settings.ResetToDefaults();
            }
            Assert.AreEqual(rules, Session.GameRules);
            Assert.AreEqual(Session.DifficultyExtreme, Session.Difficulty);
            Assert.IsTrue(Session.StackKaamoUpgrades);
            Assert.AreEqual(3, Session.KaamoUpgradeLimit);
            Assert.AreEqual(Settings.KaamoVipCard, Settings.KaamoVipMode);
        }

        [Test]
        public void OldSlotKeepsLegacyKaamoPreferencesOnItsFirstSave()
        {
            using var prefs = new DataUiTestKit.PrefsSnapshot();
            UnityEngine.PlayerPrefs.SetFloat(DataUiTestKit.SettingsPrefix + "kaamoStacking", 1);
            UnityEngine.PlayerPrefs.SetFloat(DataUiTestKit.SettingsPrefix + "kaamoMaxLevel", 7);
            UnityEngine.PlayerPrefs.SetFloat(DataUiTestKit.SettingsPrefix + "kaamoVip", 0);
            DataUiTestKit.ClearSettingsCache();
            Assert.IsTrue(SaveGame.Save(5));
            var path = SaveGame.SlotPath(5);
            File.WriteAllText(path, Regex.Replace(File.ReadAllText(path),
                @"""(?:stackKaamoUpgrades|kaamoUpgradeLimit|kaamoVipRule)""\s*:\s*[^,]+,", ""));
            Assert.IsTrue(SaveGame.Load(5));
            Assert.IsTrue(Session.StackKaamoUpgrades);
            Assert.AreEqual(7, Session.KaamoUpgradeLimit);
            Assert.AreEqual(Settings.KaamoVipCard, Settings.KaamoVipMode);
            Assert.IsTrue(SaveGame.Save(5));
            UnityEngine.PlayerPrefs.SetFloat(DataUiTestKit.SettingsPrefix + "kaamoStacking", 0);
            UnityEngine.PlayerPrefs.SetFloat(DataUiTestKit.SettingsPrefix + "kaamoVip", 2);
            DataUiTestKit.ClearSettingsCache();
            Assert.IsTrue(SaveGame.Load(5));
            Assert.IsTrue(Session.StackKaamoUpgrades);
            Assert.AreEqual(Settings.KaamoVipCard, Settings.KaamoVipMode);
        }

        [Test]
        public void ThePlayersShareIsSavedWithTheGame()
        {
            Blueprints.Invest(Db, 11, 155, 20, 70, true);
            Blueprints.Invest(Db, 11, 120, 5, 70);   // not the player's (as the story invests)
            var before = Blueprints.State(Db, 11);
            int spent = before.playerSpent[1];
            Assert.IsTrue(SaveGame.Save(5));
            Session.ResetNewGame();
            Assert.IsEmpty(Session.Blueprints);
            Assert.IsTrue(SaveGame.Load(5));
            var s = Blueprints.State(Db, 11);
            CollectionAssert.AreEqual(new[] { 0, 20, 0, 0, 0, 0 }, s.playerUnits);
            Assert.AreEqual(spent, s.playerSpent[1]);
            Assert.AreEqual(20, Blueprints.Withdrawable(Db, s, 1));
            Assert.AreEqual(0, Blueprints.Withdrawable(Db, s, 2));
        }

        [Test]
        public void ASaveFromBeforeThePlayersShareCanTakeNothingBack()
        {
            Blueprints.Invest(Db, 11, 155, 20, 70, true);
            Assert.IsTrue(SaveGame.Save(5));
            string path = SaveGame.SlotPath(5);
            string json = File.ReadAllText(path);
            string old = Regex.Replace(json, @",\s*""player(Units|Spent)""\s*:\s*\[[^\]]*\]", "");
            Assert.AreNotEqual(json, old, "the save has the player's share");
            File.WriteAllText(path, old);
            Session.ResetNewGame();
            Assert.IsTrue(SaveGame.Load(5));
            var s = Blueprints.State(Db, 11);
            Assert.AreEqual(30, s.remaining[1], "the invested units load");
            Assert.AreEqual(70, s.station);
            Assert.AreEqual(0, Blueprints.Withdrawable(Db, s, 1));
            Assert.IsFalse(Blueprints.Withdraw(Db, 11, 1, 1, 70));
        }
    }
}
