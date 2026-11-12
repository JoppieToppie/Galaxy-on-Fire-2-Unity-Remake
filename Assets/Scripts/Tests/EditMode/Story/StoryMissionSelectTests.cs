// StoryMissionSelectTests.cs
// Feature: CLAUDE.md "UI and platforms", Debug panel: the mission select (Story.StartAtMission / MissionSelectStart): a new
// game that starts at any story step, the step's campaign started as usual and every step up to it replayed.
// Expected values: CLAUDE.md (the steps only reached inside another: 46 -> 47 (42 on its own since 2026-11-09), 53 -> 54, 107 -> 108, 129 -> 130,
// 149 / 150 -> 151; the campaign by index: Valkyrie from 45, Supernova from 84; the step's planet-jump requirements given:
// 532 scanner + tractor beam from 24, cabins 91-94, Gamma Shield II from 105, the drill at 135, a Vossk ship + signature at
// 139, the plasma kit from 142), campaign_flow.md §1.4 (where the campaigns start), §3.1 (the moves after the conversations:
// 15 docked at Alioth, 90 docked at Thynome, 162 docked at Maissa; 89 the auto-launch into Naneroh's orbit) and §3.2
// (the chapter radio flag at 93 / 111 / 143). Where only the replay defines the place, the tests check invariants (a docked
// start is at a station that takes ships, the scene matches the entry).

using GoF2Remake.Data;
using GoF2Remake.World;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public class StoryMissionSelectTests : StoryAreaTestBase
    {
        [TestCase(42, 42)]
        [TestCase(46, 47)]
        [TestCase(53, 54)]
        [TestCase(107, 108)]
        [TestCase(129, 130)]
        [TestCase(149, 151)]
        [TestCase(150, 151)]
        [TestCase(0, 0)]
        [TestCase(41, 41)]
        [TestCase(108, 108)]
        [TestCase(162, 162)]
        public void MissionSelectStartMapsTransientSteps(int index, int start)
        {
            Assert.AreEqual(start, Story.MissionSelectStart(index));
        }

        static readonly int[] Spread =
        {
            0, 1, 2, 5, 9, 13, 15, 20, 24, 25, 29, 35, 40, 41, 42, 44, 45, 46, 47, 48, 49, 53, 56, 59, 60, 77, 78, 79, 80,
            84, 85, 89, 90, 91, 95, 105, 107, 116, 120, 128, 129, 135, 139, 142, 143, 149, 150, 154, 157, 160, 162,
        };

        [TestCaseSource(nameof(Spread))]
        public void StartsAtTheRequestedStep(int target)
        {
            string scene = Story.StartAtMission(Db, target);
            int start = Story.MissionSelectStart(target);
            Assert.AreEqual(start, Story.Index, "index");
            var expected = target >= Story.Dlc1WonIndex ? Campaign.Supernova : target >= Story.GameWonIndex ? Campaign.Valkyrie : Campaign.GalaxyOnFire2;
            Assert.AreEqual(expected, Session.Campaign);
            Assert.IsNotNull(Db.Ship(Session.ShipIndex), "a real ship");
            Assert.That(scene, Is.EqualTo("Station").Or.EqualTo("Space"));
            if (scene == "Station")
            {
                Assert.IsFalse(Session.LaunchedFromStation || Session.ArrivedByTravel, "docked");
                Assert.That(Session.StationIndex, Is.GreaterThanOrEqualTo(0));
                Assert.IsFalse(OrbitLayout.IsEmptyOrbit(Session.StationIndex, start), "a station that takes ships");
                if (start >= 49 && start <= 54) Assert.AreEqual(74, Session.StationIndex, "only Kanado takes the K'Suukk");
            }
            else Assert.IsFalse(Session.LaunchedFromStation && Session.ArrivedByTravel, "one entry");
            Assert.AreEqual(start == 93 || start == 111 || start == 143, Session.StoryRadioPending, "the chapter call");
            Assert.AreEqual(-1, Session.PlayerHull, "repaired");
        }

        [Test]
        public void ThePrologueStartsInFlightAtVarHastra()
        {
            Assert.AreEqual("Space", Story.StartAtMission(Db, 0));
            Assert.AreEqual(78, Session.StationIndex);
            Assert.AreEqual(10, Session.ShipIndex);
        }

        [Test]
        public void AddOnStartsAreTheirCampaignStarts()
        {
            Assert.AreEqual("Station", Story.StartAtMission(Db, 45));
            Assert.AreEqual(91, Session.StationIndex, "Dima");
            Assert.AreEqual(5, Session.ShipIndex);
            Session.ResetNewGame();
            Assert.AreEqual("Station", Story.StartAtMission(Db, 84));
            Assert.AreEqual(70, Session.StationIndex, "Dis");
            Assert.AreEqual(30, Session.ShipIndex);
        }

        [TestCase(15, 98)]
        [TestCase(90, 10)]
        [TestCase(162, 93)]
        public void DockedAfterTheOriginalsMoves(int target, int station)
        {
            Assert.AreEqual("Station", Story.StartAtMission(Db, target));
            Assert.AreEqual(station, Session.StationIndex);
        }

        [Test]
        public void Step89IsTheAutoLaunchIntoNanerohsOrbit()
        {
            Assert.AreEqual("Space", Story.StartAtMission(Db, 89));
            Assert.AreEqual(109, Session.StationIndex);
            Assert.IsTrue(Session.ArrivedByTravel, "the stream-out arrival");
        }

        [Test]
        public void PastStep1TheShipIsBetty()
        {
            Story.StartAtMission(Db, 5);
            Assert.AreEqual(0, Session.ShipIndex);
        }

        [Test]
        public void MainCampaignFromStep24HasScannerAndTractorBeam()
        {
            Story.StartAtMission(Db, 30);
            Assert.IsNotNull(Shop.FirstMounted(Db, 17), "a scanner");
            Assert.IsNotNull(Shop.FirstMounted(Db, 13), "a tractor beam");
        }

        [Test]
        public void SupernovaRequirementsAreGiven()
        {
            Story.StartAtMission(Db, 91);
            Assert.AreEqual(-1, Story.RequirementRefusal(Db, Story.Mission.station), "cabins");
            Session.ResetNewGame();
            Story.StartAtMission(Db, 105);
            Assert.AreEqual(-1, Story.RequirementRefusal(Db, 109), "Gamma Shield II");
            Session.ResetNewGame();
            Story.StartAtMission(Db, 135);
            Assert.AreEqual(-1, Story.RequirementRefusal(Db, 103), "a drill");
            Session.ResetNewGame();
            Story.StartAtMission(Db, 139);
            Assert.AreEqual(-1, Story.RequirementRefusal(Db, 131), "a Vossk ship and the signature");
            Session.ResetNewGame();
            Story.StartAtMission(Db, 142);
            Assert.AreEqual(-1, Story.RequirementRefusal(Db, 79), "the plasma kit");
        }

        [Test]
        public void TheReplayCreditsTheStepRewards()
        {
            // §8: 23 -> 20 000, 31 -> 30 000 (the success dialogues before 32), plus nothing else before 44.
            Story.StartAtMission(Db, 32);
            Assert.That(Session.Credits, Is.GreaterThanOrEqualTo(50000));
        }

        [Test]
        public void Step48FliesTheHSocWithTheOwnShipParked()
        {
            Story.StartAtMission(Db, 48);
            Assert.AreEqual(9, Session.ShipIndex);
            Assert.IsNotNull(Session.ParkedShip);
        }

        [Test]
        public void StartAtMissionClampsItsTarget()
        {
            Story.StartAtMission(Db, 500);
            Assert.AreEqual(Story.LastIndex, Story.Index);
            Session.ResetNewGame();
            Story.StartAtMission(Db, -3);
            Assert.AreEqual(0, Story.Index);
        }
    }
}
