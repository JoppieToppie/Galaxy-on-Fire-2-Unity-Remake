// StoryRulesTests.cs
// Feature: CLAUDE.md "Story" (state and rules: IsComplete per mission type, departStation's level mission, BlocksDocking /
// BlocksJumps, the menu unlocks, checkpoint repairs on load, the story icon) and "Valkyrie add-on" (LaunchStation, the Khador
// Drive's story cases), "Supernova add-on" (RequirementRefusal), plus the docked-station tweaks (Story.OnDocked).
// Expected values: Reference/research/campaign_flow.md §1.5 (GameRecord::load repairs), §2.2 (Status::missionCompleted's
// rule per type), §3.1 (ModStation::OnInitialize: Betty at index 1, Kappa's EMP bombs, the Alien Remains, Khador's ships,
// Thynome's Void Crystals, the energy cells; the menu buttons in §6), §3.2 (departStation's excluded types), §7 (525 "Not
// possible on a mission" for types other than 0, 0xb, 0xd, 0xab, 0xac; 49-54 except Kanado; the autopilot / planet-jump
// gates; the requirement boxes 3213-3218), §7 markers; CLAUDE.md for the remake's #46 (no Hangar in any loaner) and #49
// (79 -> 78) repairs.

using System.Collections.Generic;
using GoF2Remake.Data;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public class StoryRulesTests : StoryAreaTestBase
    {
        static void Mission(int type, int station = 10, int value = 0, int goods = -1, int amount = 0, int index = 30)
        {
            Session.CampaignMission = index;
            Session.StoryMission = new StoryMission { type = type, station = station, value = value, goodsItem = goods, goodsAmount = amount };
        }

        static bool Done(bool docked, int station, float levelMs = 0f, bool lounge = false) =>
            Story.IsComplete(Db, new StoryContext { docked = docked, station = station, levelMs = levelMs, inLounge = lounge });

        // ---- IsComplete (Status::missionCompleted, campaign_flow.md 2.2) ---------------------------------------

        [TestCase(StoryType.Dock)]
        [TestCase(StoryType.DockedAny)]
        public void DockTypesCompleteDockedAtTheTarget(int type)
        {
            Mission(type, 79);
            Assert.IsTrue(Done(true, 79));
            Assert.IsFalse(Done(true, 78), "another station");
            Assert.IsFalse(Done(false, 79, 60000f), "in its orbit");
        }

        [Test]
        public void PurchaseNeedsTheGoodsDockedAtTheTarget()
        {
            Mission(StoryType.Purchase, 10, goods: 164, amount: 50);
            Shop.AddToCargo(164, 49);
            Assert.IsFalse(Done(true, 10));
            Shop.AddToCargo(164, 1);
            Assert.IsTrue(Done(true, 10));
            Assert.IsFalse(Done(true, 98), "elsewhere");
            Assert.IsFalse(Done(false, 10, 20000f), "in space");
        }

        [Test]
        public void Step143AlsoCountsTheChromoPlasmaWaitingAsAProduct()
        {
            Mission(StoryType.Purchase, 112, goods: 210, amount: 1, index: 143);
            Assert.IsFalse(Done(false, 112, 20000f));
            Session.PendingProducts.Add(new PendingProduct { item = 210, quantity = 1, station = 112 });
            Assert.IsTrue(Done(false, 112, 20000f), "in the target's orbit");
            Assert.IsTrue(Done(true, 112));
            Assert.IsFalse(Done(true, 10), "not at another station");
        }

        [Test]
        public void FreelanceCountAndCargoLoad()
        {
            Mission(StoryType.FreelanceCount, -1, value: 3);
            Session.FreelanceCompleted = 2;
            Assert.IsFalse(Done(true, 78));
            Session.FreelanceCompleted = 3;
            Assert.IsTrue(Done(false, 78));

            Mission(StoryType.CargoLoad, 78, value: 10);
            Shop.AddToCargo(122, 4); Shop.AddToCargo(164, 5);
            Assert.IsFalse(Done(false, 78, 5000f), "9 t");
            Shop.AddToCargo(155, 1);
            Assert.IsTrue(Done(false, 78, 5000f), "10 t of anything");
        }

        [Test]
        public void ReachOrbitTakesTenSecondsInTheTargetOrbit()
        {
            Mission(StoryType.ReachOrbit, 56);
            Assert.IsFalse(Done(false, 56, 9999f));
            Assert.IsTrue(Done(false, 56, 10000f));
            Assert.IsFalse(Done(false, 55, 60000f), "another orbit");
            Assert.IsFalse(Done(true, 56, 60000f), "docked");
        }

        [Test]
        public void WeaponAndArmorMounted()
        {
            Mission(StoryType.WeaponAndArmor, 78);
            Session.Equipment = new List<ItemStack> { new ItemStack(2, 1) };   // a primary only
            Assert.IsFalse(Done(true, 78));
            Session.Equipment = new List<ItemStack> { new ItemStack(55, 1) };  // armor (sort 10) only
            Assert.IsFalse(Done(true, 78));
            Session.Equipment = new List<ItemStack> { new ItemStack(2, 1), new ItemStack(55, 1) };
            Assert.IsTrue(Done(true, 78));
        }

        [Test]
        public void DelayedCallDockedAnywhereOrTenSecondsElsewhere()
        {
            Mission(StoryType.DelayedCall, 27);
            Assert.IsTrue(Done(true, 78), "docked anywhere");
            Assert.IsFalse(Done(false, 27, 60000f), "not in the target orbit");
            Assert.IsFalse(Done(false, 30, 9999f));
            Assert.IsTrue(Done(false, 30, 10000f));
            Mission(StoryType.DelayedCall, Session.VoidOrbit);
            Assert.IsFalse(Done(false, Session.VoidOrbit, 60000f), "42: in the Void it waits");
            Assert.IsTrue(Done(false, 10, 10000f), "out of the Void");
        }

        [Test]
        public void CallAfterLaunchIsMoreThanTenSecondsInSpace()
        {
            Mission(StoryType.CallAfterLaunch, 101);
            Assert.IsFalse(Done(false, 5, 10000f));
            Assert.IsTrue(Done(false, 5, 10001f));
            Assert.IsFalse(Done(true, 101), "docked");
        }

        [Test]
        public void InOrbitAndCounterAndScriptFlag()
        {
            Mission(StoryType.InOrbit, Session.VoidOrbit);
            Assert.IsTrue(Done(false, Session.VoidOrbit));
            Assert.IsFalse(Done(false, 10));

            Mission(StoryType.Counter, 131, value: 10);
            Session.StoryCounter = 9;
            Assert.IsFalse(Done(false, 131));
            Session.StoryCounter = 10;
            Assert.IsTrue(Done(false, 131));

            Mission(StoryType.ScriptFlag, 10, value: 0);
            Assert.IsFalse(Done(false, 10));
            Session.StoryMission.value = 1;
            Assert.IsTrue(Done(false, 10));
        }

        [Test]
        public void DeliverOrMountTakesTheGoodsInTheHoldOrMounted()
        {
            Mission(StoryType.DeliverOrMount, 10, goods: 206, amount: 1);
            Assert.IsFalse(Done(true, 10));
            Session.Equipment.Add(new ItemStack(206, 1));
            Assert.IsTrue(Done(true, 10), "mounted");
            Assert.IsFalse(Done(true, 98), "at the target only");
            Session.Equipment.RemoveAll(e => e.item == 206);
            Shop.AddToCargo(206, 1);
            Assert.IsTrue(Done(true, 10), "in the hold");
        }

        [Test]
        public void TargetListNeedsEveryTargetDone()
        {
            Mission(StoryType.TargetList, 101);
            Session.StoryTargets = new List<int>();
            Assert.IsFalse(Done(false, 5), "no list");
            Session.StoryTargets = new List<int> { -56, 45, -22 };
            Assert.IsFalse(Done(false, 5));
            Session.StoryTargets[1] = -45;
            Assert.IsTrue(Done(false, 5), "wherever the player is");
        }

        [Test]
        public void LoungeTypesNeedTheLoungeIntroFinished()
        {
            Mission(StoryType.Lounge, 38);
            Assert.IsFalse(Done(true, 38));
            Assert.IsTrue(Done(true, 38, lounge: true));
            Assert.IsFalse(Done(true, 82, lounge: true));

            Mission(StoryType.LoungeWithGoods, 38, goods: 146, amount: 1);
            Assert.IsFalse(Done(true, 38, lounge: true));
            Shop.AddToCargo(146, 1);
            Assert.IsTrue(Done(true, 38, lounge: true));
            Assert.IsFalse(Done(true, 38));
        }

        [Test]
        public void AmountReachedAndPassengers()
        {
            Mission(StoryType.AmountReached, 103, value: 139, goods: 155, amount: 140);
            Assert.IsFalse(Done(false, 103));
            Session.StoryMission.value = 140;
            Assert.IsTrue(Done(false, 103));

            Mission(StoryType.Passengers, 110, value: 1, index: 91);
            Assert.IsFalse(Done(false, 110));
            Session.StoryMission.value = 0;
            Assert.IsTrue(Done(false, 110));
            Mission(StoryType.Passengers, 113, value: 0, index: 92);
            Assert.IsFalse(Done(false, 113), "92: the level script ends it");
        }

        [Test]
        public void EquipCategoryDockedAnywhere()
        {
            Mission(StoryType.EquipCategory, 55, value: 6, index: 20);
            Assert.IsFalse(Done(true, 55));
            Session.Equipment.Add(new ItemStack(41, 10));   // EMP GL I, sort 6
            Assert.IsTrue(Done(true, 55));
            Assert.IsTrue(Done(true, 78), "any station");
            Assert.IsFalse(Done(false, 55, 60000f), "docked only");
        }

        [TestCase(StoryType.Level)]
        [TestCase(StoryType.Defense)]
        [TestCase(StoryType.Wanted)]
        [TestCase(StoryType.Intercept)]
        [TestCase(StoryType.Challenge)]
        [TestCase(StoryType.VoidInvasion)]
        public void LevelDrivenTypesNeverCompleteHere(int type)
        {
            Mission(type, 78);
            Assert.IsFalse(Done(true, 78));
            Assert.IsFalse(Done(false, 78, 1e6f));
        }

        [Test]
        public void EmptyOrWonMissionsAreNotReported()
        {
            Session.StoryMission = new StoryMission();
            Assert.IsFalse(Done(true, 78));
            Mission(StoryType.Dock, 78);
            Session.StoryMission.won = true;
            Assert.IsFalse(Done(true, 78), "already won: the dialogue is up");
            Session.StoryMission = null;
            Assert.IsFalse(Done(true, 78));
        }

        // ---- level mission (Status::departStation) and the restrictions (campaign_flow.md 3.2 / 7) ------------

        [TestCase(0x96)] [TestCase(0x97)] [TestCase(0x99)] [TestCase(0x9b)] [TestCase(0xa2)] [TestCase(0xa3)] [TestCase(0xa4)]
        [TestCase(0xa6)] [TestCase(0xa7)] [TestCase(0xa9)] [TestCase(0xad)] [TestCase(0x08)] [TestCase(0x0e)]
        public void ExcludedTypesNeverBuildTheOrbit(int type)
        {
            Mission(type, 10);
            Assert.IsFalse(Story.IsLevelMission(10));
        }

        [TestCase(0x04)] [TestCase(0x0b)] [TestCase(0x9c)] [TestCase(0xab)] [TestCase(0xb8)]
        public void OtherTypesBuildTheirTargetsOrbit(int type)
        {
            Mission(type, 10);
            Assert.IsTrue(Story.IsLevelMission(10));
            Assert.IsFalse(Story.IsLevelMission(98));
        }

        [Test]
        public void DelayedCallIsALevelMissionAwayFromItsTarget()
        {
            Mission(StoryType.DelayedCall, 27);
            Assert.IsFalse(Story.IsLevelMission(27));
            Assert.IsTrue(Story.IsLevelMission(30));
        }

        [Test]
        public void VoidInvasionIsTheAttackedOrbitBeforeTheGameIsWon()
        {
            Mission(StoryType.VoidInvasion, -1, index: 40);
            Session.VoidInvasionStation = 91;
            Assert.IsTrue(Story.IsLevelMission(91));
            Assert.IsFalse(Story.IsLevelMission(22));
            Assert.AreEqual(91, Story.TargetStation, "the attacked station is the target");
            Session.VoidInvasionStation = Session.VoidOrbit;
            Assert.IsFalse(Story.IsLevelMission(Session.VoidOrbit), "never the alien orbit");
            Mission(StoryType.VoidInvasion, -1, index: 50);
            Session.VoidInvasionStation = 91;
            Assert.IsFalse(Story.IsLevelMission(91), "index < 45 only");
        }

        [Test]
        public void BlockingLevelMissionRefusesDockingAndJumpsInItsStoryOrbit()
        {
            AtStep(7);   // 0x04 at Var Hastra: the pirate hideout
            Assert.IsTrue(Story.BlocksJumps(78, true));
            Assert.IsTrue(Story.BlocksDocking(78, true));
            Assert.IsFalse(Story.BlocksJumps(78, false), "an orbit not built as the story's (step 13 -> 14 mid-flight)");
            Assert.IsFalse(Story.BlocksJumps(79, true), "another orbit");
        }

        [TestCase(10)]   // 0x0b
        [TestCase(20)]   // 0xbd
        [TestCase(110)]  // 0xab
        [TestCase(112)]  // 0xac
        [TestCase(33)]   // 0x08: not a level mission at all
        [TestCase(58)]   // 0xa6
        public void DockingTypesDontBlock(int index)
        {
            AtStep(index);
            int target = Story.Mission.station;
            Assert.IsFalse(Story.BlocksJumps(target, true));
            Assert.IsFalse(Story.BlocksDocking(target, true));
        }

        [Test]
        public void DelayedCallBlocksAwayFromItsTarget()
        {
            AtStep(37);
            Assert.IsFalse(Story.BlocksJumps(27, true));
            Assert.IsTrue(Story.BlocksJumps(30, true));
        }

        [Test]
        public void TheKSuukkDocksOnlyAtKanado()
        {
            for (int i = 49; i <= 54; i++)
            {
                if (i == 53) continue;
                AtStep(i);
                Assert.IsTrue(Story.BlocksDocking(78, false), "index " + i);
                Assert.IsFalse(Story.BlocksDocking(74, false), "Kanado at " + i);
            }
            AtStep(55);
            Assert.IsFalse(Story.BlocksDocking(78, false));
        }

        [Test]
        public void NoDockingAtTheVoidStation()
        {
            Session.FreePlay = true;
            Assert.IsTrue(Story.BlocksDocking(Session.VoidOrbit, false));
        }

        // ---- menus and gates (ModStation::OnInitialize / OnKeyPress, campaign_flow.md 6 / 7) -----------------

        [Test]
        public void HangarFromStep5ExceptInALoaner()
        {
            AtStep(4); Assert.IsFalse(Story.HangarUnlocked);
            AtStep(5); Assert.IsTrue(Story.HangarUnlocked);
            foreach (int i in new[] { 48, 49, 56 }) { AtStep(i); Assert.IsFalse(Story.HangarUnlocked, "index " + i); }
            AtStep(57); Assert.IsTrue(Story.HangarUnlocked);
            Session.ParkedShip = new ParkedShip { ship = 10 };
            Assert.IsFalse(Story.HangarUnlocked, "#46: any loaner flown");
            Session.FreePlay = true;
            Assert.IsTrue(Story.HangarUnlocked);
            Session.ParkedShip.lentBy = "trial";
            Assert.IsFalse(Story.HangarUnlocked, "an event's loaner, free play too");
        }

        [Test]
        public void MapFromStep9NotAt15_48_49()
        {
            AtStep(8); Assert.IsFalse(Story.MapUnlocked);
            AtStep(9); Assert.IsTrue(Story.MapUnlocked);
            foreach (int i in new[] { 15, 48, 49 }) { AtStep(i); Assert.IsFalse(Story.MapUnlocked, "index " + i); }
            AtStep(16); Assert.IsTrue(Story.MapUnlocked);
            AtStep(50); Assert.IsTrue(Story.MapUnlocked);
        }

        [Test]
        public void LoungeFromStep13NeverAtKotharOrValkyrie()
        {
            AtStep(12); Assert.IsFalse(Story.LoungeUnlocked(79), "ModStation::OnKeyPress refuses it before 13");
            AtStep(13); Assert.IsTrue(Story.LoungeUnlocked(79));
            AtStep(15); Assert.IsFalse(Story.LoungeUnlocked(98));
            AtStep(49); Assert.IsFalse(Story.LoungeUnlocked(74));
            AtStep(60);
            Assert.IsFalse(Story.LoungeUnlocked(100));
            Assert.IsFalse(Story.LoungeUnlocked(101));
            Session.FreePlay = true;
            Assert.IsTrue(Story.LoungeUnlocked(78));
            Assert.IsFalse(Story.LoungeUnlocked(100), "stations 100 / 101 never");
        }

        [Test]
        public void Step77MapOnlyInTheCronusAndNoAutosaveImprisoned()
        {
            AtStep(77);
            Session.ShipIndex = 10;
            Assert.IsNotNull(Story.MapRefusal, "326");
            Session.ShipIndex = 37;
            Assert.IsNull(Story.MapRefusal);
            Assert.IsFalse(Story.AutosaveAllowed(101));
            Assert.IsTrue(Story.AutosaveAllowed(100));
            AtStep(76);
            Session.ShipIndex = 10;
            Assert.IsNull(Story.MapRefusal);
            Assert.IsTrue(Story.AutosaveAllowed(101));
        }

        [Test]
        public void PlanetJumpsAndAutopilotGates()
        {
            AtStep(9); Assert.IsFalse(Story.PlanetJumpsAllowed);
            AtStep(10); Assert.IsTrue(Story.PlanetJumpsAllowed);
            AtStep(48); Assert.IsFalse(Story.PlanetJumpsAllowed); Assert.IsFalse(Story.AutopilotAllowed);
            AtStep(1); Assert.IsFalse(Story.AutopilotAllowed);
            AtStep(2); Assert.IsTrue(Story.AutopilotAllowed);
            AtStep(0); Session.FreePlay = true;
            Assert.IsTrue(Story.PlanetJumpsAllowed); Assert.IsTrue(Story.AutopilotAllowed);
        }

        [Test]
        public void LaunchingAt48GoesToBakrram()
        {
            AtStep(48); Assert.AreEqual(58, Story.LaunchStation);
            AtStep(47); Assert.AreEqual(-1, Story.LaunchStation);
            AtStep(48); Session.FreePlay = true; Assert.AreEqual(-1, Story.LaunchStation);
        }

        [Test]
        public void KhadorDriveStoryCases()
        {
            AtStep(78);
            Assert.AreEqual(Session.VoidOrbit, Story.ForcedKhadorTarget(101), "78: the misjump into the Void");
            Assert.IsNull(Story.ForcedKhadorTarget(Session.VoidOrbit));
            AtStep(80);
            Assert.AreEqual(100, Story.ForcedKhadorTarget(Session.VoidOrbit), "80: out of the Void to Kothar");
            Assert.IsNull(Story.ForcedKhadorTarget(100));
            AtStep(79);
            Assert.IsNull(Story.ForcedKhadorTarget(Session.VoidOrbit));
            AtStep(78); Session.FreePlay = true;
            Assert.IsNull(Story.ForcedKhadorTarget(101));
        }

        [Test]
        public void ShipSwappedAfterTheLoanerConversations()
        {
            foreach (int n in new[] { 48, 49, 55, 56, 58 }) Assert.IsTrue(Story.ShipSwapped(n), "n " + n);
            foreach (int n in new[] { 47, 50, 54, 57, 77, 84 }) Assert.IsFalse(Story.ShipSwapped(n), "n " + n);
        }

        [Test]
        public void FreeTutorialGearAndKappasEmpBombs()
        {
            Session.CampaignMission = 6;
            Assert.AreEqual(0, Story.AdjustPrice(78, 0, 1500), "Var Hastra before step 7");
            Assert.AreEqual(1500, Story.AdjustPrice(79, 0, 1500));
            Session.CampaignMission = 7;
            Assert.AreEqual(1500, Story.AdjustPrice(78, 0, 1500));
            Session.CampaignMission = 20;
            Assert.AreEqual(0, Story.AdjustPrice(55, 41, 900), "the EMP GL I at Kappa in step 20");
            Assert.AreEqual(900, Story.AdjustPrice(55, 40, 900));
            Assert.AreEqual(900, Story.AdjustPrice(56, 41, 900));
            Session.CampaignMission = 21;
            Assert.AreEqual(900, Story.AdjustPrice(55, 41, 900));
        }

        // ---- requirement boxes (StarMap::OnTouchEnd, campaign_flow.md 7) -------------------------------------

        [TestCase(91)]
        [TestCase(94)]
        public void PassengerCabinsNeeded(int index)
        {
            AtStep(index);
            int target = Story.Mission.station;
            Assert.AreEqual(3214, Story.RequirementRefusal(Db, target));
            Assert.AreEqual(-1, Story.RequirementRefusal(Db, 78), "only the campaign target");
            Session.Equipment.Add(new ItemStack(93, 1));   // Large Cabin
            Assert.AreEqual(-1, Story.RequirementRefusal(Db, target));
        }

        [Test]
        public void TheValpatroRescueNeedsTenSeats()
        {
            AtStep(91);
            int target = Story.Mission.station;
            Session.Equipment.Add(new ItemStack(92, 1));   // Medium Cabin: 5 seats
            Assert.AreEqual(3214, Story.RequirementRefusal(Db, target), "5 seats of 10");
            Session.Equipment.Add(new ItemStack(92, 1));   // a second one: 10
            Assert.AreEqual(-1, Story.RequirementRefusal(Db, target));
        }

        [Test]
        public void GammaShieldIINeededAt105()
        {
            AtStep(105);
            Assert.AreEqual(3217, Story.RequirementRefusal(Db, 109));
            Session.Equipment.Add(new ItemStack(205, 1));
            Assert.AreEqual(3217, Story.RequirementRefusal(Db, 109), "Gamma Shield I isn't enough");
            Session.Equipment.Add(new ItemStack(206, 1));
            Assert.AreEqual(-1, Story.RequirementRefusal(Db, 109));
        }

        [Test]
        public void DrillNeededAt135()
        {
            AtStep(135);
            Session.Equipment.RemoveAll(e => Db.Item(e.item)?.categoryId == 19);
            Assert.AreEqual(3213, Story.RequirementRefusal(Db, 103));
            Session.Equipment.Add(new ItemStack(86, 1));
            Assert.AreEqual(-1, Story.RequirementRefusal(Db, 103));
        }

        [Test]
        public void VosskShipAndSignatureNeededAt139()
        {
            AtStep(139);
            Session.ShipIndex = 10;
            Session.Equipment.Add(new ItemStack(190, 1));
            Assert.AreEqual(3215, Story.RequirementRefusal(Db, 131), "a Terran ship");
            Session.ShipIndex = 42;
            Assert.AreEqual(-1, Story.RequirementRefusal(Db, 131), "the Vol Noor");
            Session.ShipIndex = 9;
            Assert.AreEqual(-1, Story.RequirementRefusal(Db, 131), "a Vossk ship (H'Soc)");
            Session.Equipment.RemoveAll(e => e.item == 190);
            Assert.AreEqual(3215, Story.RequirementRefusal(Db, 131), "no signature");
        }

        [Test]
        public void PlasmaKitNeededAt142()
        {
            AtStep(142);
            Session.Cargo.Clear();
            Assert.AreEqual(3216, Story.RequirementRefusal(Db, 79));
            Session.Equipment.Add(new ItemStack(196, 1));
            Session.Equipment.Add(new ItemStack(198, 1));
            Session.Equipment.Add(new ItemStack(197, 14));
            Assert.AreEqual(3216, Story.RequirementRefusal(Db, 79), "14 ionizing missiles");
            Session.Equipment.Find(e => e.item == 197).amount = 15;
            Assert.AreEqual(-1, Story.RequirementRefusal(Db, 79));
            Shop.AddToCargo(122, Shop.MaxLoad(Db));
            Assert.AreEqual(3218, Story.RequirementRefusal(Db, 79), "1 t free cargo");
        }

        [Test]
        public void NoRequirementsInFreePlay()
        {
            AtStep(91);
            Session.FreePlay = true;
            Assert.AreEqual(-1, Story.RequirementRefusal(Db, 110));
        }

        // ---- checkpoint repairs (GameRecord::load, campaign_flow.md 1.5) -------------------------------------

        [Test]
        public void RepairCheckpoint25To24()
        {
            AtStep(25);
            Story.RepairCheckpoint();
            Assert.AreEqual(24, Story.Index);
            Assert.AreEqual(0x04, Story.Mission.type); Assert.AreEqual(48, Story.Mission.station);
            Assert.AreEqual(9, Session.VoidInvasionSystem); Assert.AreEqual(48, Session.VoidInvasionStation);
        }

        [Test]
        public void RepairCheckpoint29To28()
        {
            AtStep(29);
            Story.RepairCheckpoint();
            Assert.AreEqual(28, Story.Index);
            Assert.AreEqual(0x04, Story.Mission.type); Assert.AreEqual(91, Story.Mission.station);
            Assert.AreEqual(18, Session.VoidInvasionSystem); Assert.AreEqual(91, Session.VoidInvasionStation);
        }

        [Test]
        public void RepairCheckpoint41To39()
        {
            AtStep(41);
            Story.RepairCheckpoint();
            Assert.AreEqual(39, Story.Index);
            Assert.AreEqual(0x0b, Story.Mission.type); Assert.AreEqual(30, Story.Mission.station);
        }

        [Test]
        public void RepairCheckpoint35WithAnotherTarget()
        {
            AtStep(35);
            Session.StoryMission.station = 30;
            Story.RepairCheckpoint();
            Assert.AreEqual(35, Story.Index);
            Assert.AreEqual(0x0b, Story.Mission.type); Assert.AreEqual(29, Story.Mission.station, "back to Ga'kkrr");
        }

        [Test]
        public void RepairCheckpoint79To78()
        {
            AtStep(79);
            Story.RepairCheckpoint();
            Assert.AreEqual(78, Story.Index, "#49");
            Assert.AreEqual(0x04, Story.Mission.type); Assert.AreEqual(101, Story.Mission.station);
            AtStep(79); Session.FreePlay = true;
            Story.RepairCheckpoint();
            Assert.AreEqual(79, Story.Index, "not in free play");
        }

        [TestCase(24)]
        [TestCase(30)]
        [TestCase(40)]
        [TestCase(78)]
        [TestCase(100)]
        public void OtherStepsStayOnLoad(int index)
        {
            AtStep(index);
            var m = Story.Mission;
            Story.RepairCheckpoint();
            Assert.AreEqual(index, Story.Index);
            Assert.AreSame(m, Story.Mission);
        }

        [Test]
        public void AStepWonWithoutItsConversationCompletesAgain()
        {
            // #88: step 72's call (0xa4, 10 s in space) completed, the scene changed before its conversation, the save kept
            // the step won: never reported again. A new scene reopens it (Story.ReopenUnshownSuccess).
            AtStep(72);
            var ctx = new StoryContext { docked = false, station = 66, levelMs = 11000f };
            Assert.IsTrue(Story.IsComplete(Db, ctx));
            Story.Mission.won = true;
            Assert.IsFalse(Story.IsComplete(Db, ctx), "a won step isn't reported again within the scene");
            Story.ReopenUnshownSuccess();
            Assert.IsTrue(Story.IsComplete(Db, ctx), "the next scene reports it again");
            Assert.AreEqual(72, Story.Index);
        }

        [Test]
        public void TheKhadorDriveIsOnlyUnsaleableAt77()
        {
            AtStep(77);
            Session.Unsaleable.Add(GalaxyMap.KhadorDriveItem);
            Story.RepairCheckpoint();
            Assert.IsTrue(Session.Unsaleable.Contains(GalaxyMap.KhadorDriveItem));
            AtStep(90);
            Story.RepairCheckpoint();
            Assert.IsFalse(Session.Unsaleable.Contains(GalaxyMap.KhadorDriveItem), "a later Khador Drive stays saleable");
        }

        [Test]
        public void LoadingASaveRepairsTheCheckpoint()
        {
            AtStep(29);
            Session.StationIndex = 98;
            Assert.IsTrue(SaveGame.Save(1));
            Session.ResetNewGame();
            Assert.IsTrue(SaveGame.Load(1));
            Assert.AreEqual(28, Story.Index, "29 -> 28 on load");
            Assert.AreEqual(91, Story.Mission.station);
        }

        // ---- the story icon (StarMap::drawOnScreenInfo / Radar::draw, campaign_flow.md 7) ---------------------

        [Test]
        public void MarksTheTargetStation()
        {
            AtStep(30);   // 0x9c at Dima
            Assert.IsTrue(Story.MapMarks(91));
            Assert.IsFalse(Story.MapMarks(10));
            Assert.IsFalse(Story.MapMarks(-5));
            Assert.AreEqual(91, Story.MapTarget);
        }

        [Test]
        public void Step116MarksTheBarsNotSearchedYet()
        {
            AtStep(116);
            Session.StoryMission.value = 1;   // 90 searched (bit 0)
            Assert.IsFalse(Story.MapMarks(90));
            Assert.IsTrue(Story.MapMarks(91));
            Assert.IsTrue(Story.MapMarks(94), "slot 3");
            Assert.IsTrue(Story.MapMarks(93), "Maissa, the target");
            Assert.IsFalse(Story.MapMarks(95));
        }

        [Test]
        public void Step120MarksOnlyMaissa()
        {
            AtStep(120);
            Assert.IsTrue(Story.MapMarks(93));
            Assert.IsFalse(Story.MapMarks(40), "the level's own orbit isn't marked");
            Assert.AreEqual(93, Story.MapTarget);
        }

        [Test]
        public void Step125MarksTheFreighterStationsNotScanned()
        {
            AtStep(125);
            Session.StoryMission.value = 1;   // 15 scanned
            Assert.IsFalse(Story.MapMarks(15));
            Assert.IsTrue(Story.MapMarks(30));
            Assert.IsTrue(Story.MapMarks(95));
            Assert.IsTrue(Story.MapMarks(55), "the target");
        }

        [Test]
        public void Step59MarksTheConvoysLeft()
        {
            AtStep(59);
            Session.StoryTargets = new List<int> { -56, 45, 22 };
            Assert.IsFalse(Story.MapMarks(56));
            Assert.IsTrue(Story.MapMarks(45));
            Assert.IsTrue(Story.MapMarks(22));
            Assert.IsFalse(Story.MapMarks(101), "not the mission's own station");
            Assert.AreEqual(45, Story.MapTarget);
        }

        [TestCase(52)]
        [TestCase(128)]
        [TestCase(130)]
        [TestCase(148)]
        [TestCase(151)]
        public void NoMarkerDuringHunts(int index)
        {
            AtStep(index);
            int target = Story.Mission.station;
            Assert.IsFalse(Story.MapMarks(target));
        }

        // ---- docking (ModStation::OnInitialize, campaign_flow.md 3.1) -----------------------------------------

        [Test]
        public void DockingAtIndex1GivesBetty()
        {
            AtStep(1);
            Session.ShipIndex = 10;
            Session.OreStreak = 4; Session.BlindKills = 3;
            Story.OnDocked(Db, 78, null);
            Assert.AreEqual(0, Session.ShipIndex, "Betty");
            CollectionAssert.AreEquivalent(new[] { 90, 81 }, Session.Equipment.ConvertAll(e => e.item));
            Assert.IsTrue(Session.Unsaleable.Contains(90) && Session.Unsaleable.Contains(81));
            Assert.AreEqual(0, Session.OreStreak, "the medal streaks end with the flight");
            Assert.AreEqual(0, Session.BlindKills);
        }

        [Test]
        public void KappaStocksTenEmpBombsAtStep20OnlyFromSpace()
        {
            AtStep(20);
            var stock = new StationStock { station = 55 };
            Session.DockedFromSpace = false;
            Story.OnDocked(Db, 55, stock);
            Assert.IsNull(stock.items.Find(s => s.item == 41), "a load adds nothing");
            Session.DockedFromSpace = true;
            Story.OnDocked(Db, 55, stock);
            Assert.AreEqual(10, stock.items.Find(s => s.item == 41).amount);
        }

        [Test]
        public void TheAlienRemainsAreHandedOverAt27()
        {
            AtStep(27);
            Shop.AddToCargo(131, 3);
            Session.Unsaleable.Add(131);
            Story.OnDocked(Db, 98, null);
            Assert.AreEqual(3, Shop.CargoOf(131), "not at another station");
            Story.OnDocked(Db, 10, null);
            Assert.AreEqual(0, Shop.CargoOf(131));
            Assert.IsFalse(Session.Unsaleable.Contains(131));
        }

        [Test]
        public void KhadorsShipsAtKothar()
        {
            var stock = new StationStock { station = 100 };
            Session.RecentStations.Add(stock);
            AtStep(77);
            Story.OnDocked(Db, 100, stock);
            CollectionAssert.AreEquivalent(new[] { 37 }, stock.ships, "the Cronus");
            AtStep(81);
            Story.OnDocked(Db, 100, stock);
            CollectionAssert.AreEquivalent(new[] { 37, 38, 40 }, stock.ships, "Cronus, Typhon, Nemesis; once each");
        }

        [Test]
        public void ThynomeStocksVoidCrystalsAfterTheGameIsWon()
        {
            var stock = new StationStock { station = 10 };
            AtStep(44);
            Story.OnDocked(Db, 10, stock);
            Assert.IsNull(stock.items.Find(s => s.item == 164));
            AtStep(50);
            Story.OnDocked(Db, 10, stock);
            Assert.AreEqual(50, stock.items.Find(s => s.item == 164).amount);

            var stock2 = new StationStock { station = 10 };
            Session.Equipment.Add(new ItemStack(85, 1));
            Story.OnDocked(Db, 10, stock2);
            Assert.IsNull(stock2.items.Find(s => s.item == 164), "a player with a Khador Drive gets none");
        }

        [TestCase(10)]
        [TestCase(100)]
        [TestCase(101)]
        public void EnergyCellsForANearlyEmptyHold(int station)
        {
            AtStep(30);
            var stock = new StationStock { station = station };
            Shop.AddToCargo(GalaxyMap.EnergyCellItem, 5);
            Story.OnDocked(Db, station, stock);
            Assert.AreEqual(10, stock.items.Find(s => s.item == GalaxyMap.EnergyCellItem).amount);
            var stock2 = new StationStock { station = station };
            Shop.AddToCargo(GalaxyMap.EnergyCellItem, 1);   // 6
            Story.OnDocked(Db, station, stock2);
            Assert.IsNull(stock2.items.Find(s => s.item == GalaxyMap.EnergyCellItem));
        }

        [Test]
        public void NoFreeEnergyCellsElsewhere()
        {
            AtStep(30);
            var stock = new StationStock { station = 98 };
            Story.OnDocked(Db, 98, stock);
            Assert.IsNull(stock.items.Find(s => s.item == GalaxyMap.EnergyCellItem));
        }

        [Test]
        public void ThynomeSellsTheVoidXForAllGoldMedals()
        {
            AtStep(50);
            for (int i = 0; i < Achievements.BaseCount; i++) Session.Medals[i] = 1;
            var stock = new StationStock { station = 10, ships = new List<int> { 3, 4 } };
            Story.OnDocked(Db, 10, stock);
            // The original's list is the VoidX alone; the remake keeps the dealer's other ships beside it.
            CollectionAssert.AreEqual(new[] { 8, 3, 4 }, stock.ships);
            Story.OnDocked(Db, 10, stock);
            CollectionAssert.AreEqual(new[] { 8, 3, 4 }, stock.ships, "added once");
        }

        [Test]
        public void KhadorsGiftsAfterTheDockedConversations()
        {
            var stock = new StationStock { station = 100 };
            Session.RecentStations.Add(stock);
            Story.AfterDockedAdvance(77, 100);
            CollectionAssert.AreEquivalent(new[] { 37 }, stock.ships);
            Story.AfterDockedAdvance(84, 100);
            CollectionAssert.AreEquivalent(new[] { 37, 38, 40 }, stock.ships);
            Assert.AreEqual(1, Shop.CargoOf(137), "a bottle of S'kolptorr Rum");
            Story.AfterDockedAdvance(84, 10);
            Assert.AreEqual(1, Shop.CargoOf(137), "only at Kothar");
        }

        // ---- the Void invasion's re-rolls (Status::departStation) --------------------------------------------

        [Test]
        public void EveryTenthDepartureMovesTheVoidAttack()
        {
            AtStep(35);   // target 29
            Session.VoidInvasionSystem = 18; Session.VoidInvasionStation = 91;
            for (int i = 0; i < 9; i++) Story.OnDepart(Db, 78);
            Assert.AreEqual(9, Session.InvasionDepartures);
            Assert.AreEqual(91, Session.VoidInvasionStation);
            Story.OnDepart(Db, 29); Story.OnDepart(Db, 91);
            Assert.AreEqual(9, Session.InvasionDepartures, "the target and the attacked station don't count");
            Story.OnDepart(Db, 78);
            Assert.AreEqual(0, Session.InvasionDepartures);
            int sys = Session.VoidInvasionSystem;
            Assert.AreNotEqual(10, sys); Assert.AreNotEqual(15, sys);
            Assert.IsTrue(Visible(sys), "a visible system");
            var sd = Db.Systems.Find(s => s.index == sys);
            CollectionAssert.Contains(sd.stations, Session.VoidInvasionStation);
        }

        [Test]
        public void NoInvasionRerollsOutside32To44()
        {
            AtStep(31);
            Session.VoidInvasionStation = 91;
            for (int i = 0; i < 12; i++) Story.OnDepart(Db, 78);
            Assert.AreEqual(0, Session.InvasionDepartures);
            AtStep(45);
            Story.OnDepart(Db, 78);
            Assert.AreEqual(-10, Session.VoidInvasionStation, "won: never again");
            Assert.AreEqual(-10, Session.VoidInvasionSystem);
        }

        [Test]
        public void ObjectiveTextsNameTheTarget()
        {
            for (int i = 0; i <= Story.LastIndex; i++)
            {
                var step = StoryTable.Step(i);
                if (step == null || step.objectiveText < 0 || step.type < 0) continue;
                AtStep(i);
                string text = Story.ObjectiveText(Db);
                Assert.IsFalse(text.Contains("#"), "step " + i + ": " + text);
                var target = Db.Stations.Find(s => s.index == step.station);
                if (target != null && Localization.Get(step.objectiveText).Contains("#"))
                    StringAssert.Contains(target.name, text, "step " + i);
            }
        }
    }
}
