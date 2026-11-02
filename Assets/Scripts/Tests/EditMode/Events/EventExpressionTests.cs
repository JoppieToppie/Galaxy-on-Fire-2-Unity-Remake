// EventExpressionTests.cs
// Feature: CLAUDE.md "Multiplayer" > Events (expressions) and "Mods" > Conditions: the events' expression language,
// evaluated outside any event by EventRunner.Condition (the quests' "Starts when", the mods' "available"): numbers,
// arithmetic, comparisons, and / or / not (and && || !), random(a, b), min, max, floor, count(<players>), the game-state
// words (campaign, credits, rank, station, system, ship, kills, players, enemies, time, toppoints, missionstation,
// systemsvisited) and cargo(), has(), visited(), quest(), option(), won(), medals() and bought(). Empty = true; nonzero =
// true; a bad expression is false with an error.
// Expected values: hand calculation; the won() thresholds from CLAUDE.md (the main game is won at step 45, the Valkyrie
// add-on at 84, the Supernova add-on at its last step 162; or the multiplayer finished world); the station / system
// numbers of the game's tables.

using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Events;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    public class EventExpressionTests : SandboxTest
    {
        static bool True(string e)
        {
            bool v = EventRunner.Condition(e, out string error);
            Assert.IsNull(error, e + ": " + error);
            return v;
        }

        static void Holds(string e) => Assert.IsTrue(True(e), e);
        static void Fails(string e) => Assert.IsFalse(True(e), e);

        static void Bad(string e)
        {
            Assert.IsFalse(EventRunner.Condition(e, out string error), e);
            Assert.IsNotNull(error, e + ": an error");
        }

        [Test]
        public void EmptyIsTrue()
        {
            Assert.IsTrue(EventRunner.Condition("", out string error));
            Assert.IsNull(error);
            Assert.IsTrue(EventRunner.Condition("   ", out _));
            Assert.IsTrue(EventRunner.Condition(null, out _));
        }

        [Test]
        public void NonzeroIsTrue()
        {
            Holds("1");
            Holds("2.5");
            Holds("-1");
            Fails("0");
            Fails("0.0");
        }

        [Test]
        public void Arithmetic()
        {
            Holds("2 + 3 * 4 == 14");
            Holds("(2 + 3) * 4 == 20");
            Holds("10 - 4 - 3 == 3");      // left to right
            Holds("100 / 10 / 2 == 5");
            Holds("7 % 4 == 3");
            Holds("-3 + 5 == 2");
            Holds("--3 == 3");
            Holds("+4 == 4");
            Holds("2 * -3 == -6");
            Holds("0.5 + 0.25 == 0.75");
        }

        [Test]
        public void DivisionByZeroIsZero()
        {
            Holds("5 / 0 == 0");
            Holds("5 % 0 == 0");
        }

        [Test]
        public void Comparisons()
        {
            Holds("2 == 2");
            Holds("2 = 2");
            Holds("2 != 3");
            Holds("2 < 3");
            Holds("3 <= 3");
            Holds("4 > 3");
            Holds("3 >= 3");
            Fails("3 < 3");
            Fails("2 > 3");
            Holds("1 + 1 == 2 * 1");   // comparisons below the sums
        }

        [Test]
        public void Logic()
        {
            Holds("1 and 1");
            Fails("1 and 0");
            Holds("0 or 1");
            Fails("0 or 0");
            Holds("not 0");
            Fails("not 5");
            Holds("!0");
            Holds("1 && 2");
            Holds("0 || 3");
            Holds("1 != 2 and 3 > 2");
            Holds("NOT 0 AND 1");             // words in any case
            Holds("0 and 1 or 1");            // and before or
            Fails("0 and (1 or 1)");
            Holds("not 0 and 0 or 1");
        }

        [Test]
        public void Functions()
        {
            Holds("min(3, 9) == 3");
            Holds("max(3, 9) == 9");
            Holds("max(-1, -5) == -1");
            Holds("floor(2.7) == 2");
            Holds("floor(-2.5) == -3");
            Holds("random(4, 4) == 4");
            for (int i = 0; i < 50; i++) Holds("random(1, 6) >= 1 and random(1, 6) <= 6");
            Holds("random(6, 1) >= 1 and random(6, 1) <= 6");   // either order
            Holds("MAX(1, 2) == 2");
        }

        [Test]
        public void RandomCoversItsRangeInclusive()
        {
            bool low = false, high = false;
            for (int i = 0; i < 400 && !(low && high); i++)
            {
                if (True("random(1, 3) == 1")) low = true;
                if (True("random(1, 3) == 3")) high = true;
            }
            Assert.IsTrue(low && high, "both ends of random(1, 3) come up");
        }

        [Test]
        public void BadExpressions()
        {
            Bad("1 +");
            Bad("(1 + 2");
            Bad("2 3");
            Bad("nosuchvariable == 1");
            Bad("min(1)");
            Bad("floor(1, 2)");
            Bad("sqrt(4)");
            Bad("won(somethingelse)");
            Bad("visited(Nowhere At All)");
            Bad("cargo(no such item at all)");
            Bad("@");
        }

        [Test]
        public void TheGameStateWords()
        {
            Session.Credits = 12345;
            Session.Kills = 7;
            Session.StationIndex = 78;
            Session.ShipIndex = 10;
            Holds("credits == 12345");
            Holds("CREDITS > 12000");
            Holds("kills == 7");
            Holds("station == 78");
            Holds("ship == 10");
            Holds("rank >= 0");
            int system = Db.Stations.First(s => s.index == 78).system;
            Holds("system == " + system);
        }

        [Test]
        public void CampaignIsMinusOneInFreePlay()
        {
            Session.FreePlay = false;
            Session.CampaignMission = 30;
            Holds("campaign == 30");
            Session.FreePlay = true;
            Holds("campaign == -1");
        }

        [Test]
        public void TheRunsOwnWords()
        {
            // Outside an event: the one local pilot, no spawned ships, no points, no mission station.
            Holds("players == 1");
            Holds("enemies == 0");
            Holds("ships == 0");
            Holds("toppoints == 0");
            Holds("missionstation == -1");
            Holds("time >= 0");
        }

        [Test]
        public void CountTakesASelector()
        {
            Session.StationIndex = 78;
            Holds("count(@a) == 1");
            Holds("count(@team) == 1");
            Holds("count(@a[orbit=78]) == 1");
            Holds("count(@a[orbit=void]) == 0");
            Holds("count(@survivors) == 0");
            Bad("count(@a[orbit=99999])");
            Bad("count(@nobody)");
        }

        [Test]
        public void CargoAndHas()
        {
            Session.Cargo.Clear();
            Session.Equipment.Clear();
            Session.Cargo.Add(new ItemStack(85, 2));
            Session.Cargo.Add(new ItemStack(85, 3));
            Holds("cargo(85) == 5");
            Holds("has(85) == 5");
            Session.Equipment.Add(new ItemStack(85, 0));   // a mounted item counts at least 1
            Holds("cargo(85) == 5");
            Holds("has(85) == 6");
            Holds("cargo(84) == 0");
            Holds("cargo({80 + 5}) == 5");                 // the argument may hold {expressions}
        }

        [Test]
        public void Visited()
        {
            Session.VisitedStations = new System.Collections.Generic.HashSet<int> { 78, 70 };
            Holds("visited(78)");
            Holds("visited(70)");
            Fails("visited(void)");
            Holds("visited(78) + visited(70) == 2");
        }

        [Test]
        public void VisitedByStationName()
        {
            EventsTestUtil.RequireEnglish();
            Session.VisitedStations = new System.Collections.Generic.HashSet<int> { 78 };
            Holds("visited(Var Hastra)");
        }

        [Test]
        public void SystemsVisitedCountsDistinctSystems()
        {
            var first = Db.Stations.First(s => s.index >= 0 && Db.Stations.Any(o => o.system == s.system && o.index != s.index));
            var sameSystem = Db.Stations.First(s => s.system == first.system && s.index != first.index);
            var other = Db.Stations.First(s => s.system != first.system);
            Session.VisitedStations = new System.Collections.Generic.HashSet<int> { first.index, sameSystem.index, other.index, 99999 };
            Holds("systemsvisited == 2");
            Session.VisitedStations = new System.Collections.Generic.HashSet<int>();
            Holds("systemsvisited == 0");
        }

        [Test]
        public void Quests()
        {
            Session.GraphQuestsDone.Add("finished");
            Session.GraphQuests.Add(new GraphQuestState { name = "underway" });
            Holds("quest(finished) == 2");
            Holds("quest(underway) == 1");
            Holds("quest(unknown) == 0");
        }

        [Test]
        public void MedalsByGrade()
        {
            Session.Medals = new int[Achievements.Count];
            Holds("medals(any) == 0");
            Fails("medals(allgold)");
            Session.Medals[0] = 1; Session.Medals[1] = 2; Session.Medals[2] = 3; Session.Medals[3] = 1;
            Holds("medals(gold) == 2");
            Holds("medals(silver) == 1");
            Holds("medals(bronze) == 1");
            Holds("medals(any) == 4");
            Fails("medals(allgold)");
            for (int i = 0; i < Achievements.BaseCount; i++) Session.Medals[i] = 1;
            Holds("medals(allgold)");
            Holds("medals(gold) == " + Achievements.BaseCount);
            Fails("medals(allsupernova)");
            for (int i = Achievements.BaseCount; i < Achievements.Count; i++) Session.Medals[i] = 1;
            Holds("medals(allsupernova)");
            Bad("medals(platinum)");
        }

        [Test]
        public void BoughtShips()
        {
            EventsTestUtil.RequireEnglish();
            Session.ShipsBought.Clear();
            Fails("bought(9)");
            Session.ShipsBought.Add(Session.ShipRef(9));
            Holds("bought(9)");
            Holds("bought(H'Soc)");
            Fails("bought(0)");
            Bad("bought(no such ship at all)");
        }

        [Test]
        public void OptionOfAModThatIsntThere()
        {
            Holds("option(nomod:nooption) == 0");
        }

        [Test]
        public void WonByCampaign()
        {
            Session.FreePlay = false;
            Session.CompletedWorld = false;
            Session.CampaignMission = 44;
            Fails("won(main)");
            Session.CampaignMission = 45;
            Holds("won(main)");
            Holds("won(gof2)");
            Holds("won(MAIN)");
            Fails("won(valkyrie)");
            Session.CampaignMission = 84;
            Holds("won(valkyrie)");
            Fails("won(supernova)");
            Session.CampaignMission = 162;
            Holds("won(supernova)");
        }

        [Test]
        public void WonInFreePlayOnlyInTheFinishedWorld()
        {
            Session.FreePlay = true;
            Session.CompletedWorld = false;
            Session.CampaignMission = 162;
            Fails("won(main)");
            Session.CompletedWorld = true;
            Holds("won(main)");
            Holds("won(valkyrie)");
            Holds("won(supernova)");
        }

        [Test]
        public void TheWonThresholdsAreTheStorysOwn()
        {
            Assert.AreEqual(45, Story.GameWonIndex);
            Assert.AreEqual(84, Story.Dlc1WonIndex);
            Assert.AreEqual(162, Story.LastIndex);
        }
    }
}
