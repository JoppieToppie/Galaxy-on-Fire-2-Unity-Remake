// AchievementsTests.cs
// Feature: CLAUDE.md "Bar and freelance", Medals (Achievements: 45 medals, grade 0 none / 1 gold / 2 silver / 3 bronze,
// checkForNewMedal's thresholds, the strict "more than" medals 5-8, 10, 16, 18, 20, 21, 26, 27, 29, 31-34, 36, Survivor's
// "at most", Veteran preset, Champion for all other base medals, the elite medals' flags, none in multiplayer).
// Expected values: Reference/research/lounge_ui.md §6.3 (the threshold table [gold, silver, bronze] per medal and the
// counters), Reference/tools/wingmen/wingmen_wanted.py MEDALS (the same table, DAT_00259860), wingmen_wanted.md §1.3
// (gotAllMedals / gotAllGoldMedals / gotAllSupernovaMedals). The medal rewards (5000 / 2500 / 1000, none on Extreme) are paid by
// StationMenu.ShowNextMedal (a MonoBehaviour): not covered here.

using System.Collections.Generic;
using GoF2Remake.Data;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public class AchievementsTests : StoryAreaTestBase
    {
        /// <summary>wingmen_wanted.py MEDALS = DAT_00259860: gold, silver, bronze (-1 unused).</summary>
        static readonly int[,] Table =
        {
            { 0, -1, -1 }, { 5, 15, 30 }, { 11, 8, 5 }, { 11, 8, 5 }, { 250, 100, 50 }, { 200, 100, 25 }, { 1000, 500, 100 },
            { 25, 10, 3 }, { 1000, 100, 25 }, { 22, 16, 5 }, { 150, 100, 30 }, { 100, 50, 25 }, { 22, 10, 5 }, { 13, 6, 3 },
            { 13, 6, 3 }, { 20, 10, 5 }, { 50, 25, 5 }, { 100, 50, 10 }, { 50, 20, 5 }, { 5, 3, 2 }, { 50, 20, 5 },
            { 25, 10, 5 }, { 0, -1, -1 }, { 4, 3, 2 }, { 500, 200, 50 }, { 1000000, 500000, 125000 }, { 100, 50, 20 },
            { 20, 10, 3 }, { 1, -1, -1 }, { 250, 150, 50 }, { 0, -1, -1 }, { 500, 250, 100 }, { 50, -1, -1 }, { 10, -1, -1 },
            { 12, -1, -1 }, { 0, -1, -1 }, { 3000, -1, -1 }, { 50, -1, -1 }, { 10, -1, -1 }, { 20, -1, -1 }, { 100, -1, -1 },
            { 3, -1, -1 }, { 15, -1, -1 }, { 5, -1, -1 }, { 8, -1, -1 },
        };

        int Award(int medal)
        {
            Achievements.Check(Db);
            return Session.Medals[medal];
        }

        [Test]
        public void ThresholdTable()
        {
            Assert.AreEqual(45, Achievements.Count);
            Assert.AreEqual(36, Achievements.BaseCount);
            for (int m = 0; m < 45; m++)
                for (int g = 1; g <= 3; g++) Assert.AreEqual(Table[m, g - 1], Achievements.Threshold(m, g), "medal " + m + " grade " + g);
            Assert.AreEqual(0, Achievements.Threshold(4, 0));
            Assert.AreEqual(0, Achievements.Threshold(4, 4));
        }

        [Test]
        public void VeteranIsPresetGold()
        {
            Assert.AreEqual(0, Achievements.Grade(0));
            var improved = Achievements.Check(Db);
            Assert.AreEqual(1, Achievements.Grade(0));
            Assert.IsFalse(improved.Contains(0), "a preset, not a new medal");
        }

        static void SetCounter(int medal, int value)
        {
            switch (medal)
            {
                case 4: Session.Kills = value; break;
                case 5: Session.ContainersDelivered = value; break;
                case 6: Session.OreMined = value; break;
                case 7: Session.CoresMined = value; break;
                case 8: Session.BoozeBought = value; break;
                case 10: Session.JunkDestroyed = value; break;
                case 16: Session.FreelanceCompleted = value; break;
                case 17: Session.JumpgatesUsed = value; break;
                case 18: Session.PassengersDelivered = value; break;
                case 20: Session.BombsDetonated = value; break;
                case 21: Session.AlienRemainsCollected = value; break;
                case 24: Session.CratesSalvaged = value; break;
                case 25: Session.HighestCredits = value; break;
                case 26: Session.AgentsTalkedTo = value; break;
                case 27: Session.WingmenHired = value; break;
                case 29: Session.AsteroidsDestroyed = value; break;
                case 32: Session.OffersDeclined = value; break;
                case 33: Session.AcceptedBlindRisk = value; break;
                case 34: Session.AcceptedBlindMap = value; break;
                case 19: Session.CloakMs = value * 60000L; break;
                case 2: Session.OreTypesMined = Range(value); break;
                case 3: Session.CoreTypesMined = Range(value); break;
                case 9: Session.BoozeTypes = Range(value); break;
                case 13: Session.UnlockedBlueprints = Range(value); break;
                case 37: Session.KaamoShips = new List<StoredShip>(); for (int i = 0; i < value; i++) Session.KaamoShips.Add(new StoredShip(i, 0, null)); break;
                default: Assert.Fail("no counter for " + medal); break;
            }
        }

        static HashSet<int> Range(int n) { var h = new HashSet<int>(); for (int i = 0; i < n; i++) h.Add(1000 + i); return h; }

        [TestCase(5)] [TestCase(6)] [TestCase(7)] [TestCase(8)] [TestCase(10)] [TestCase(16)] [TestCase(18)] [TestCase(20)]
        [TestCase(21)] [TestCase(26)] [TestCase(27)] [TestCase(29)]
        public void StrictMedalsNeedMoreThanTheThreshold(int medal)
        {
            for (int g = 3; g >= 1; g--)
            {
                int t = Table[medal, g - 1];
                Session.Medals = new int[45];
                SetCounter(medal, t);
                int below = Award(medal);
                Assert.AreEqual(g == 3 ? 0 : g + 1, below, "medal " + medal + " at exactly " + t);
                Session.Medals = new int[45];
                SetCounter(medal, t + 1);
                Assert.AreEqual(g, Award(medal), "medal " + medal + " at " + (t + 1));
            }
        }

        [TestCase(32)] [TestCase(33)] [TestCase(34)]
        public void StrictGoldOnlyMedals(int medal)
        {
            int t = Table[medal, 0];
            SetCounter(medal, t);
            Assert.AreEqual(0, Award(medal));
            SetCounter(medal, t + 1);
            Assert.AreEqual(1, Award(medal));
        }

        [TestCase(2)] [TestCase(3)] [TestCase(4)] [TestCase(9)] [TestCase(13)] [TestCase(17)] [TestCase(19)] [TestCase(24)]
        [TestCase(25)]
        public void OtherMedalsAtTheThreshold(int medal)
        {
            for (int g = 3; g >= 1; g--)
            {
                int t = Table[medal, g - 1];
                Session.Medals = new int[45];
                SetCounter(medal, t - 1);
                Assert.AreEqual(g == 3 ? 0 : g + 1, Award(medal), "medal " + medal + " at " + (t - 1));
                Session.Medals = new int[45];
                SetCounter(medal, t);
                Assert.AreEqual(g, Award(medal), "medal " + medal + " at exactly " + t);
            }
        }

        [Test]
        public void ShipCollectorCountsTheStoredHulls()
        {
            SetCounter(37, 49);
            Assert.AreEqual(0, Award(37));
            SetCounter(37, 50);
            Assert.AreEqual(1, Award(37));
        }

        [TestCase(100, 0)]
        [TestCase(31, 0)]
        [TestCase(30, 3)]
        [TestCase(16, 3)]
        [TestCase(15, 2)]
        [TestCase(6, 2)]
        [TestCase(5, 1)]
        [TestCase(0, 1)]
        public void SurvivorIsTheHullOnArrivalAtMost(int percent, int grade)
        {
            Session.LastArrivalHullPercent = percent;
            Assert.AreEqual(grade, Award(1));
        }

        [Test]
        public void WeaponFanaticCountsThePrimariesMounted()
        {
            Session.Equipment = new List<ItemStack> { new ItemStack(0, 1) };
            Assert.AreEqual(0, Award(23));
            Session.Equipment.Add(new ItemStack(0, 1));
            Assert.AreEqual(3, Award(23));
            Session.Equipment.Add(new ItemStack(1, 1));
            Assert.AreEqual(2, Award(23));
            Session.Equipment.Add(new ItemStack(2, 1));
            Assert.AreEqual(1, Award(23));
        }

        [Test]
        public void ExplorerCountsTheSystemsVisited()
        {
            Session.VisitedStations = new HashSet<int> { 78, 55, 56, 15, 20 };   // Mido, Union x2, V'ikka, Eanya
            Assert.AreEqual(0, Award(12));
            Session.VisitedStations.Add(25);   // S'kolptorr
            Assert.AreEqual(3, Award(12), "5 systems");
        }

        [Test]
        public void EngineerCountsTheBlueprintsProduced()
        {
            for (int i = 0; i < 3; i++) Session.Blueprints.Add(new BlueprintState { item = 85 + i, timesProduced = i == 2 ? 0 : 4 });
            Assert.AreEqual(0, Award(14));
            Session.Blueprints[2].timesProduced = 1;
            Assert.AreEqual(3, Award(14));
        }

        [Test]
        public void AddictCountsPlayingHours()
        {
            Session.PlaySeconds = 5 * 3600f - 60f;
            Assert.AreEqual(0, Award(15));
            Session.PlaySeconds = 5 * 3600f + 1f;
            Assert.AreEqual(3, Award(15));
        }

        [Test]
        public void RenegadeWhenAnyFactionIsHostile()
        {
            Assert.AreEqual(0, Award(28));
            Session.Standing = new[] { 0, -75 };   // the Nivelians hate you
            Assert.AreEqual(1, Award(28));
        }

        [Test]
        public void VoidTerrorForWinningTheGame()
        {
            Session.CampaignMission = 44;
            Assert.AreEqual(0, Award(30));
            Session.CampaignMission = 45;
            Assert.AreEqual(1, Award(30));
            Session.Medals = new int[45];
            Session.FreePlay = true;
            Assert.AreEqual(0, Award(30), "not in free play");
        }

        [Test]
        public void HarumScarumForArrivingWithoutGear()
        {
            Assert.AreEqual(0, Award(22));
            Session.ArrivedWithoutGear = true;
            Assert.AreEqual(1, Award(22));
        }

        [Test]
        public void SpaceSaverNeedsMoreThan100FreeTonnes()
        {
            int free = Shop.FreeCargo(Db);
            int expected = free > 500 ? 1 : free > 250 ? 2 : free > 100 ? 3 : 0;
            Assert.AreEqual(expected, Award(31));
        }

        [Test]
        public void TheBestGradeIsKept()
        {
            SetCounter(4, 120);
            Assert.AreEqual(2, Award(4));
            SetCounter(4, 60);
            var improved = Achievements.Check(Db);
            Assert.AreEqual(2, Session.Medals[4], "never downgraded");
            Assert.IsFalse(improved.Contains(4));
            SetCounter(4, 300);
            improved = Achievements.Check(Db);
            Assert.AreEqual(1, Session.Medals[4]);
            Assert.IsTrue(improved.Contains(4), "an improvement counts as new");
        }

        [Test]
        public void ChampionForEveryOtherBaseMedal()
        {
            for (int i = 0; i < 35; i++) Session.Medals[i] = 3;
            Session.Medals[17] = 0;
            Achievements.Check(Db);
            Assert.AreEqual(0, Session.Medals[35]);
            Session.Medals[17] = 3;
            var improved = Achievements.Check(Db);
            Assert.AreEqual(1, Session.Medals[35]);
            Assert.IsTrue(improved.Contains(35));
            Assert.IsTrue(Achievements.GotAllMedals);
            Assert.IsFalse(Achievements.GotAllGoldMedals);
        }

        [Test]
        public void AllGoldAndAllSupernovaMedals()
        {
            for (int i = 0; i < 36; i++) Session.Medals[i] = 1;
            Assert.IsTrue(Achievements.GotAllGoldMedals);
            Assert.IsFalse(Achievements.GotAllSupernovaMedals);
            for (int i = 36; i < 45; i++) Session.Medals[i] = 1;
            Assert.IsTrue(Achievements.GotAllSupernovaMedals);
            Session.Medals[20] = 2;
            Assert.IsFalse(Achievements.GotAllGoldMedals);
            Assert.IsFalse(Achievements.GotAllSupernovaMedals);
            Assert.IsTrue(Achievements.GotAllMedals, "any grade");
        }

        [TestCase(38)] [TestCase(40)] [TestCase(41)] [TestCase(42)] [TestCase(44)]
        public void EliteMedalsByTheirFlightCounters(int medal)
        {
            int t = Table[medal, 0];
            Achievements.Elite(medal, t - 1);
            Assert.IsFalse(Session.EliteFlags.Contains(medal));
            Assert.AreEqual(0, Award(medal));
            Achievements.Elite(medal, t);
            Assert.IsTrue(Session.EliteFlags.Contains(medal), "the flag at the gold threshold");
            Assert.AreEqual(1, Award(medal), "awarded on docking");
            Assert.IsTrue(Achievements.Has(medal));
        }

        [Test]
        public void NoEliteFlagOnceEarned()
        {
            Session.Medals[41] = 1;
            Achievements.Elite(41, 99);
            Assert.IsFalse(Session.EliteFlags.Contains(41));
        }

        [Test]
        public void NoMedalsInMultiplayer()
        {
            NetSession.ActiveProvider = () => true;
            SetCounter(4, 300);
            var improved = Achievements.Check(Db);
            Achievements.Elite(38, 50);
            NetSession.ActiveProvider = null;
            Assert.AreEqual(0, improved.Count);
            Assert.AreEqual(0, Session.Medals[4]);
            Assert.IsFalse(Session.EliteFlags.Contains(38));
        }

        [Test]
        public void SupernovaMedalProgressShowsInFlightAsTheOriginalDoes()
        {
            // Hud::hudEventMedal "name: n%" at the points its callers pick (MiningGame::update, PlayerFixedObject::update,
            // Level::enemyDied, Gun::calcCharacterCollision / ignite, Player::damageEmp).
            string Name(int m) => Localization.Get(1507 + m);
            Assert.IsNull(Achievements.ProgressText(38, 2), "Ore Athlete: not below 30 %");
            Assert.AreEqual(Name(38) + ": 30%", Achievements.ProgressText(38, 3));
            Assert.AreEqual(Name(38) + ": 100%", Achievements.ProgressText(38, 10));
            Assert.AreEqual(Name(39) + ": 5%", Achievements.ProgressText(39, 1), "Destroyer Destroyer: every kill");
            Assert.AreEqual(Name(39) + ": 100%", Achievements.ProgressText(39, 39), "kept at 100 % past the threshold");
            Assert.IsNull(Achievements.ProgressText(39, 40));
            Assert.IsNull(Achievements.ProgressText(40, 15), "Blindfolded Killer: at each 10 %");
            Assert.AreEqual(Name(40) + ": 20%", Achievements.ProgressText(40, 20));
            for (int m = 41; m <= 44; m++)
            {
                int t = Achievements.Threshold(m, 1);
                Assert.IsNull(Achievements.ProgressText(m, t - 1), "medal " + m + ": only once reached");
                Assert.AreEqual(Name(m) + ": 100%", Achievements.ProgressText(m, t));
            }
            Assert.IsNull(Achievements.ProgressText(36, 3000), "Space Saver Pro is counted on docking");
            Assert.IsNull(Achievements.ProgressText(4, 250), "a base medal");

            var shown = new List<string>();
            var hook = GameHooks.FlightMessage;
            GameHooks.FlightMessage = shown.Add;
            try
            {
                Achievements.Elite(44, 8);
                Assert.AreEqual(new[] { Name(44) + ": 100%" }, shown.ToArray(), "Hot Shot's line on the HUD");
                Session.Medals[44] = 1;
                shown.Clear();
                Achievements.Progress(44, 9);
                Assert.AreEqual(0, shown.Count, "nothing once the medal is earned");
                NetSession.ActiveProvider = () => true;
                Achievements.Progress(43, 5);
                Assert.AreEqual(0, shown.Count, "nor in multiplayer");
            }
            finally
            {
                NetSession.ActiveProvider = null;
                GameHooks.FlightMessage = hook;
            }
        }

        [Test]
        public void HarumScarumCheck()
        {
            Session.CampaignMission = 7;
            Session.Equipment.Clear();
            Assert.IsFalse(Achievements.NoWeaponOrEquipment(Db), "from campaign 8");
            Session.CampaignMission = 8;
            Assert.IsTrue(Achievements.NoWeaponOrEquipment(Db));
            Session.Equipment.Add(new ItemStack(0, 1));
            Assert.IsTrue(Achievements.NoWeaponOrEquipment(Db), "a weapon but no equipment");
            Session.Equipment.Add(new ItemStack(55, 1));
            Assert.IsFalse(Achievements.NoWeaponOrEquipment(Db));
            Session.Equipment.RemoveAll(e => e.item == 0);
            Assert.IsTrue(Achievements.NoWeaponOrEquipment(Db), "equipment but no weapon");
            Session.Equipment.Add(new ItemStack(181, 1));   // a turret counts as a weapon
            Assert.IsFalse(Achievements.NoWeaponOrEquipment(Db));
        }

        [Test]
        public void MedalsSurviveASaveAndLoad()
        {
            Session.Medals[4] = 2;
            Session.EliteFlags.Add(40);
            Session.StationIndex = 78;
            Assert.IsTrue(SaveGame.Save(7));
            Session.ResetNewGame();
            Assert.IsTrue(SaveGame.Load(7));
            Assert.AreEqual(2, Session.Medals[4]);
            Assert.IsTrue(Session.EliteFlags.Contains(40));
        }

        // ---- Remake: the Status window's progress toward the next grade (Achievements.NextGrade) ----

        [Test]
        public void ProgressClimbsFromBronzeToGold()
        {
            Session.Medals = new int[45];
            Session.Kills = 30;
            Assert.IsTrue(Achievements.NextGrade(Db, 4, out int grade, out int count, out int target, out float fraction));
            Assert.AreEqual((3, 30, 50), (grade, count, target), "none yet: bronze next");
            Assert.AreEqual(0.6f, fraction, 1e-4f);
            Session.Medals[4] = 3;
            Achievements.NextGrade(Db, 4, out grade, out _, out target, out _);
            Assert.AreEqual((2, 100), (grade, target), "bronze: silver next");
            Session.Medals[4] = 2;
            Achievements.NextGrade(Db, 4, out grade, out _, out target, out _);
            Assert.AreEqual((1, 250), (grade, target), "silver: gold next");
            Session.Medals[4] = 1;
            Assert.IsFalse(Achievements.NextGrade(Db, 4, out _, out _, out _, out _), "gold: nothing left");
        }

        [Test]
        public void AStrictMedalStaysShortOfFullAtTheThreshold()
        {
            Session.Medals = new int[45];
            Session.ContainersDelivered = 25;
            Achievements.NextGrade(Db, 5, out _, out _, out int target, out float fraction);
            Assert.AreEqual(25, target);
            Assert.Less(fraction, 1f, "\"more than 25\": 25 isn't there yet");
            Session.ContainersDelivered = 26;
            Achievements.NextGrade(Db, 5, out _, out _, out _, out fraction);
            Assert.AreEqual(1f, fraction);
        }

        [TestCase(0)] [TestCase(1)] [TestCase(22)] [TestCase(28)] [TestCase(30)] [TestCase(38)] [TestCase(40)] [TestCase(41)]
        [TestCase(42)] [TestCase(44)]
        public void MedalsWithoutACountUpHaveNoProgress(int medal)
        {
            Session.Medals = new int[45];
            Assert.IsFalse(Achievements.NextGrade(Db, medal, out _, out _, out _, out _));
        }

        [Test]
        public void GoldOnlyMedalsAimAtGold()
        {
            Session.Medals = new int[45];
            Session.AcceptedBlindRisk = 4;
            Assert.IsTrue(Achievements.NextGrade(Db, 33, out int grade, out int count, out int target, out _));
            Assert.AreEqual((1, 4, 10), (grade, count, target));
            Session.BattleshipsDestroyed = 3;
            Assert.IsTrue(Achievements.NextGrade(Db, 39, out grade, out count, out target, out _), "Destroyer Destroyer counts up");
            Assert.AreEqual((1, 3, 20), (grade, count, target));
        }

        [Test]
        public void ChampionCountsTheOtherBaseMedals()
        {
            Session.Medals = new int[45];
            for (int i = 0; i < 10; i++) Session.Medals[i] = 3;
            Session.Medals[40] = 1;   // an elite medal doesn't count
            Assert.IsTrue(Achievements.NextGrade(Db, 35, out _, out int count, out int target, out _));
            Assert.AreEqual((10, 35), (count, target));
            Assert.AreEqual((11, 1), Achievements.Earned());
        }

        [Test]
        public void AMissingMedalsHintGivesItsNextGrade()
        {
            Session.Medals = new int[45];
            StringAssert.Contains("50", Achievements.Hint(4));
            StringAssert.DoesNotContain("250", Achievements.Hint(4), "bronze's 50 kills, not gold's 250");
            Session.Medals[4] = 2;
            StringAssert.Contains("100", Achievements.Hint(4), "an earned medal keeps its own grade's threshold");
        }
    }
}
