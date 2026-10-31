// SupernovaChallengeTests.cs
// Feature: CLAUDE.md "Supernova add-on" > Supernova Challenge (Data.SupernovaChallenge, Reference/research/
// supernova_challenge.md): MenuTouchWindow::startSupernovaChallenge's start (the Blue Fyre and its loadout at Luur, campaign
// index 0x98, rank 9), the round of 181 000 ms, Hud::drawChallengeModeScore's combo bonus / blinking / pop, the remake's
// scoring (the iOS build's: 1000 + 2000 x the 7500 ms combo timer's remaining share a kill, the combo raised by a kill while
// its timer runs, paid when it runs out or the round ends), the
// highscore kept, the restart's Patala x10, no saving while it runs, and a new game or load ending it.

using GoF2Remake.Data;
using NUnit.Framework;
using UnityEngine;

namespace GoF2Remake.Tests
{
    public class SupernovaChallengeTests : StoryAreaTestBase
    {
        string savedKey;

        [SetUp]
        public void UseTestHighscore()
        {
            savedKey = SupernovaChallenge.PrefsKey;
            SupernovaChallenge.PrefsKey = "test_challenge_highscore";
            PlayerPrefs.DeleteKey(SupernovaChallenge.PrefsKey);
            PlayerPrefs.DeleteKey(SupernovaChallenge.PrefsKey + "_kills");
        }

        [TearDown]
        public void PutHighscoreBack()
        {
            SupernovaChallenge.End();
            PlayerPrefs.DeleteKey(SupernovaChallenge.PrefsKey);
            PlayerPrefs.DeleteKey(SupernovaChallenge.PrefsKey + "_kills");
            SupernovaChallenge.PrefsKey = savedKey;
        }

        [Test]
        public void TheStartIsTheBlueFyreAtLuurAtRankNine()
        {
            Assert.AreEqual("Space", SupernovaChallenge.Start(false));
            Assert.IsTrue(SupernovaChallenge.Active);
            Assert.AreEqual(1, SupernovaChallenge.Round);
            Assert.AreEqual(46, Session.ShipIndex);
            Assert.AreEqual(111, Session.StationIndex);
            Assert.AreEqual(0x98, Session.CampaignMission);
            Assert.IsFalse(Session.FreePlay);
            Assert.IsTrue(Story.Mission.IsEmpty, "no story step's side effects: resetGame's empty mission");
            Assert.AreEqual(9, Session.Rank, "220 kills + the index 152 (+ Var Hastra visited)");
            Assert.AreEqual(Session.DifficultyNormal, Session.Difficulty);
            CollectionAssert.AreEqual(new[] { 231, 230, 231, 73, 54, 59, 83, 206, 75, 76, 216 }, Session.Equipment.ConvertAll(e => e.item));
            Assert.AreEqual(5, Session.Equipment.Find(e => e.item == SupernovaChallenge.Missile).amount);
            foreach (var e in Session.Equipment) Assert.IsNotNull(Db.Item(e.item), "item " + e.item);

            SupernovaChallenge.Start(true);
            Assert.AreEqual(2, SupernovaChallenge.Round);
            Assert.AreEqual(10, Session.Equipment.Find(e => e.item == SupernovaChallenge.Missile).amount, "the restart's Patala x10");
        }

        [Test]
        public void KillsInARowMakeACombo()
        {
            SupernovaChallenge.Start(false);
            SupernovaChallenge.OnKill();
            Assert.AreEqual(1000, SupernovaChallenge.Score);
            Assert.AreEqual(1, SupernovaChallenge.Combo);
            Assert.IsFalse(SupernovaChallenge.ComboShown, "a combo of 1 isn't shown");
            SupernovaChallenge.Tick(5000f);
            SupernovaChallenge.OnKill();
            SupernovaChallenge.OnKill();
            Assert.AreEqual(3, SupernovaChallenge.Combo);
            Assert.AreEqual(2, SupernovaChallenge.TakeSound(), "SV_Mode_02 for the third in a row");
            Assert.AreEqual(0, SupernovaChallenge.TakeSound());
            Assert.Greater(SupernovaChallenge.ComboScale, 1f, "a fresh combo pops");
            Assert.IsTrue(SupernovaChallenge.ComboShown);
            Assert.IsFalse(SupernovaChallenge.BonusShown);
            SupernovaChallenge.Tick(4500f);
            Assert.IsTrue(SupernovaChallenge.BonusShown || !SupernovaChallenge.ComboShown, "its last 3000 ms: the bonus beside it, blinking");
            SupernovaChallenge.Tick(3000f);
            Assert.AreEqual(1, SupernovaChallenge.Combo, "run out");
            // 1000 (no timer) + 1666 (2500 of 7500 ms left: 2500 / 7500 x 2000 + 1000) + 3000 (a full timer, the same frame)
            Assert.AreEqual(5666 + SupernovaChallenge.ComboBonus(3), SupernovaChallenge.Score);
            Assert.AreEqual(10, SupernovaChallenge.TakeSound(), "SV_Mode_10 for the bonus");
            SupernovaChallenge.OnKill();
            Assert.AreEqual(1, SupernovaChallenge.Combo, "a kill after the timer ran out starts again");
        }

        [Test]
        public void TheBonusFormulaIsTheHuds()
        {
            Assert.AreEqual(0, SupernovaChallenge.ComboBonus(1));
            Assert.AreEqual(2200, SupernovaChallenge.ComboBonus(2));
            Assert.AreEqual(6250, SupernovaChallenge.ComboBonus(5));
            Assert.AreEqual(15000, SupernovaChallenge.ComboBonus(10));
            Assert.AreEqual("0012345", SupernovaChallenge.Digits(12345));
            Assert.AreEqual("9999999", SupernovaChallenge.Digits(123456789));
        }

        [Test]
        public void TheRoundEndsAfter181SecondsAndKeepsTheHighscore()
        {
            SupernovaChallenge.Start(false);
            Assert.AreEqual(181000f, SupernovaChallenge.TimeLeftMs);
            SupernovaChallenge.OnKill();
            SupernovaChallenge.OnKill();
            SupernovaChallenge.Tick(180999f);
            Assert.IsFalse(SupernovaChallenge.TimeUp);
            SupernovaChallenge.Tick(2f);
            Assert.IsTrue(SupernovaChallenge.TimeUp);
            Assert.IsTrue(SupernovaChallenge.Finish());
            Assert.IsTrue(SupernovaChallenge.Over);
            // 1000 (no timer) + 3000 (the second kill on a full timer) + the combo bonus 2200
            Assert.AreEqual(4000 + 2200, SupernovaChallenge.Score, "the combo still running at the end pays out");
            Assert.AreEqual(6200, SupernovaChallenge.Highscore);
            Assert.AreEqual(2, SupernovaChallenge.BestKills);
            SupernovaChallenge.OnKill();
            Assert.AreEqual(6200, SupernovaChallenge.Score, "nothing counts after the end");

            SupernovaChallenge.Start(true);
            SupernovaChallenge.OnKill();
            Assert.IsFalse(SupernovaChallenge.Finish(), "1000 is no new highscore");
            Assert.AreEqual(6200, SupernovaChallenge.Highscore);
        }

        [Test]
        public void ItNeverSavesAndANewGameEndsIt()
        {
            SupernovaChallenge.Start(false);
            Session.PlaySeconds = 100f;
            Assert.IsFalse(SaveGame.Save(1));
            SaveGame.AutoSave();
            Assert.IsFalse(SaveGame.Exists(SaveGame.AutoSaveSlot));
            Session.ResetNewGame();
            Assert.IsFalse(SupernovaChallenge.Active);
        }
    }
}
