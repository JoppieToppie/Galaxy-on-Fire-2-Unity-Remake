// NpcHullGunTests.cs
// Feature: CLAUDE.md "NPCs and combat" > NPC stats / NPC guns, and the difficulty factor (UI and platforms > Difficulty):
// NpcTables.Hull / Emp / HitRadiusUnits / GunDamage / GunReloadMs / GunItem / ShotSound / RaiderChance, Session's
// DifficultyFactor.
// Expected numbers: Reference/research/npc_traffic_ai.md 2.4 (the HP table at campaign 20, Level::createShip),
// Reference/tools/npc/npc_tables.py (npc_hp / npc_gun / raider_chance, run by hand: e.g. npc_gun(20, 0, hardcore=True) = 24),
// the decompiled Level::createShip quoted in the code comments (hp truncated to int after the 1.7 / 17 / 2.25 ship factor),
// and CLAUDE.md "NPC guns" for the campaign special cases (hand-calculated from the quoted formula).

using GoF2Remake.Data;
using GoF2Remake.Flight;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    public class NpcHullGunTests : NpcTestBase
    {
        // ---- the difficulty factor -------------------------------------------------------------------------------

        [TestCase(Session.DifficultyEasy, 0.5f, false)]
        [TestCase(Session.DifficultyNormal, 1f, false)]
        [TestCase(Session.DifficultyHard, 1.5f, false)]
        [TestCase(Session.DifficultyExtreme, 2f, true)]
        public void DifficultyFactorAndExtreme(float difficulty, float factor, bool extreme)
        {
            // "x + x * (difficulty - 0.5)": x0.5 / 1 / 1.5 / 2; Status::hardCoreMode = difficulty == 1.5.
            Session.Difficulty = difficulty;
            Assert.AreEqual(factor, Session.DifficultyFactor, 1e-6f);
            Assert.AreEqual(extreme, Session.IsExtreme);
        }

        [TestCase(0, 0.5f, 0f)]
        [TestCase(2, 1f, 0f)]
        [TestCase(3, 1f, 1f / 14f)]
        [TestCase(9, 1f, 0.5f)]
        [TestCase(9, 2f, 0.7f)]
        [TestCase(16, 1f, 1f)]
        [TestCase(20, 0.5f, 1f)]
        public void FighterShieldPresenceScales(int rank, float difficulty, float expected)
        {
            Assert.AreEqual(expected, NpcTables.FighterShieldChance(rank, difficulty), 1e-5f);
        }

        [TestCase(0, 100, 12)]
        [TestCase(10, 200, 52)]
        [TestCase(20, 400, 160)]
        [TestCase(20, 800, 320)]
        [TestCase(20, 0, 0)]
        public void FighterShieldCapacityScalesFromHull(int rank, int hull, int expected)
        {
            Assert.AreEqual(expected, NpcTables.FighterShieldCapacity(rank, hull));
        }

        // ---- hull ----------------------------------------------------------------------------------------------

        [TestCase(0, 100, 500, 2500)]
        [TestCase(4, 156, 780, 3900)]
        [TestCase(8, 212, 1060, 5300)]
        [TestCase(12, 268, 1340, 6700)]
        [TestCase(16, 324, 1620, 8100)]
        [TestCase(20, 380, 1900, 9500)]
        public void HullTableAtCampaign20(int rank, int fighter, int freighter, int battleship)
        {
            // npc_traffic_ai.md 2.4's table (campaign mission 20, normal difficulty).
            InFreePlay(rank);
            Assert.AreEqual(fighter, NpcTables.Hull(0, 1));
            Assert.AreEqual(freighter, NpcTables.Hull(1, 15));
            Assert.AreEqual(freighter, NpcTables.Hull(1, 13));
            Assert.AreEqual(battleship, NpcTables.Hull(1, 14));
        }

        [TestCase(Session.DifficultyEasy, 120)]
        [TestCase(Session.DifficultyNormal, 240)]
        [TestCase(Session.DifficultyHard, 360)]
        [TestCase(Session.DifficultyExtreme, 480)]
        public void HullScalesWithDifficulty(float difficulty, int hull)
        {
            // rank 10: 80 + 140 + 20 = 240, x0.5 / 1 / 1.5 / 2 (npc_hp(10) = 240, hardcore 480).
            InFreePlay(10);
            Session.Difficulty = difficulty;
            Assert.AreEqual(hull, NpcTables.Hull(0, 1));
        }

        [Test]
        public void ExtremeFreighterAtRank20()
        {
            InFreePlay(20);
            Session.Difficulty = Session.DifficultyExtreme;
            Assert.AreEqual(3800, NpcTables.Hull(1, 15));   // npc_hp(20, kind=1, ship=15, hardcore=True)
            Assert.AreEqual(19000, NpcTables.Hull(1, 14));
        }

        [Test]
        public void HullUsesTheWonGamesCampaignCap()
        {
            // Level::createShip: "gameWon ? 180 : 4 * campaign". Story index 100 (won): xp 100 + 1 visited = rank 4.
            InStory(100);
            Assert.AreEqual(4, Session.Rank);
            Assert.AreEqual(180 + 14 * 4 + 20, NpcTables.Hull(0, 1));   // npc_hp(4, game_won=True) = 256
            InCompletedWorld(10);
            Assert.AreEqual(180 + 140 + 20, NpcTables.Hull(0, 1));
        }

        [Test]
        public void HullInStoryBeforeTheWinUsesFourTimesTheIndex()
        {
            InStory(30, 5);   // xp 31 + kills
            Assert.AreEqual(4 * 30 + 14 * 5 + 20, NpcTables.Hull(0, 1));
        }

        [Test]
        public void SpecialShipHullFactors()
        {
            // ship 51 x1.7, 49 x17, 44 (Specter) x2.25 (npc_hp(0, ship=...)).
            InFreePlay(0);
            Assert.AreEqual(170, NpcTables.Hull(0, 51));
            Assert.AreEqual(1700, NpcTables.Hull(0, 49));
            Assert.AreEqual(225, NpcTables.Hull(0, 44));
        }

        [Test]
        public void SpecialShipHullTruncatesBeforeTheDifficulty()
        {
            // Level::createShip 0xcf83c: "iVar6 = (int)(hp * 2.25)" (LAB_000cf930) before the difficulty term
            // "(int)(f + f * (difficulty - 0.5))"; npc_hp truncates the same way. Rank 1 at campaign 20: hp = 114.
            InFreePlay(1);
            Session.Difficulty = Session.DifficultyExtreme;
            Assert.AreEqual(512, NpcTables.Hull(0, 44), "int(114 * 2.25) = 256, x2");      // not 513
            Session.Difficulty = Session.DifficultyHard;
            Assert.AreEqual(289, NpcTables.Hull(0, 51), "int(114 * 1.7) = 193, int(193 * 1.5)");   // not 290
        }

        [TestCase(49, true)]
        [TestCase(50, true)]
        [TestCase(51, true)]
        [TestCase(52, true)]
        [TestCase(53, false)]
        [TestCase(54, false)]
        [TestCase(55, false)]
        [TestCase(56, true)]
        [TestCase(48, false)]
        [TestCase(57, false)]
        public void Hull270Missions(int campaign, bool is270)
        {
            // mask 0x8f from 0x31: 49, 50, 51, 52 and 56 (npc_traffic_ai.md 2.4).
            InStory(campaign);
            Assert.AreEqual(is270, NpcTables.IsHull270Mission(campaign));
            if (is270)
            {
                Assert.AreEqual(270, NpcTables.Hull(0, 9));
                Assert.AreEqual(270 * 5, NpcTables.Hull(1, 13));
            }
        }

        [Test]
        public void Hull270OnlyInTheStory()
        {
            InFreePlay(0);
            Assert.IsFalse(NpcTables.IsHull270Mission(50));
        }

        [TestCase(0, 40)]
        [TestCase(10, 90)]
        [TestCase(20, 140)]
        public void EmpPoints(int rank, int emp)
        {
            // emp = 5 * L + 40, fixed objects x3; recovery 15 s / 45 s.
            InFreePlay(rank);
            Assert.AreEqual(emp, NpcTables.Emp(0));
            Assert.AreEqual(emp * 3, NpcTables.Emp(1));
            Assert.AreEqual(15000f, NpcTables.EmpRecoveryMs(0));
            Assert.AreEqual(45000f, NpcTables.EmpRecoveryMs(1));
        }

        [Test]
        public void HitCubeIs650OnExtreme()
        {
            Assert.AreEqual(1000f, NpcTables.HitRadiusUnits);
            Session.Difficulty = Session.DifficultyHard;
            Assert.AreEqual(1000f, NpcTables.HitRadiusUnits);
            Session.Difficulty = Session.DifficultyExtreme;
            Assert.AreEqual(650f, NpcTables.HitRadiusUnits);
        }

        // ---- guns ----------------------------------------------------------------------------------------------

        [TestCase(0, 3)]
        [TestCase(2, 3)]
        [TestCase(3, 3)]
        [TestCase(5, 4)]
        [TestCase(10, 9)]
        [TestCase(14, 12)]
        [TestCase(20, 18)]
        public void GunDamageByRank(int rank, int damage)
        {
            // npc_gun(level, 0): clamp(int(0.9 (L - 2)), 0, 20) + 2, 3 for 0.
            InFreePlay(rank);
            Assert.AreEqual(damage, NpcTables.GunDamage(0));
            Assert.AreEqual(damage, NpcTables.GunDamage(8));
        }

        [TestCase(Session.DifficultyEasy, 10, 5)]
        [TestCase(Session.DifficultyHard, 10, 12)]
        [TestCase(Session.DifficultyExtreme, 0, 3)]
        [TestCase(Session.DifficultyExtreme, 5, 6)]
        [TestCase(Session.DifficultyExtreme, 10, 16)]
        [TestCase(Session.DifficultyExtreme, 20, 24)]
        [TestCase(Session.DifficultyHard, 20, 24)]
        public void GunDamageByDifficulty(float difficulty, int rank, int damage)
        {
            // d = int(b * (1 + difficulty - 0.5)), 22 above 21, then + 2 (npc_gun(.., hardcore=True); Easy / Hard by hand).
            InFreePlay(rank);
            Session.Difficulty = difficulty;
            Assert.AreEqual(damage, NpcTables.GunDamage(0));
        }

        [Test]
        public void GunDamageIsOneAtCampaign4()
        {
            InStory(4);
            Assert.AreEqual(1, NpcTables.GunDamage(8));
            InFreePlay(0);
            Session.CampaignMission = 4;
            Assert.AreEqual(3, NpcTables.GunDamage(8), "free play has no campaign 4 rule");
        }

        [Test]
        public void VoidGunsDoubleAt0x10ElseTimes08()
        {
            InStory(0x10);   // xp 17: rank 1, base 3
            Assert.AreEqual(1, Session.Rank);
            Assert.AreEqual(6, NpcTables.GunDamage(Standing.Void));
            InFreePlay(10);
            Assert.AreEqual(7, NpcTables.GunDamage(Standing.Void), "int(9 * 0.8)");
            Assert.AreEqual(9, NpcTables.GunDamage(new SpawnSpec { race = Standing.Void }, true, false, out _), "wingmen: no Void factor");
            Assert.AreEqual(9, NpcTables.GunDamage(new SpawnSpec { race = Standing.Void, alwaysFriend = true }, false, false, out _), "a friend: none");
        }

        [Test]
        public void SpecterGunsTimes07()
        {
            InFreePlay(10);
            Assert.AreEqual(6, NpcTables.GunDamage(Standing.Specter), "int(9 * 0.7)");
        }

        [Test]
        public void PiratesAtCampaign7Halved()
        {
            InStory(7, 10);
            Assert.AreEqual(4, NpcTables.GunDamage(Standing.Pirate), "int(9 * 0.5)");
            Assert.AreEqual(9, NpcTables.GunDamage(0));
        }

        [Test]
        public void Hull270MissionsFireFive()
        {
            InStory(50, 10);
            Assert.AreEqual(5, NpcTables.GunDamage(0));
            Assert.AreEqual(5, NpcTables.GunDamage(Standing.Void));
            InStory(53, 10);
            Assert.AreEqual(9, NpcTables.GunDamage(0));
        }

        [Test]
        public void Campaign0x50TurretsTimes17()
        {
            InStory(0x50, 10);
            Assert.AreEqual(15, NpcTables.GunDamage(new SpawnSpec { race = 0 }, false, true, out _), "int(9 * 1.7)");
            Assert.AreEqual(9, NpcTables.GunDamage(new SpawnSpec { race = 0 }, false, false, out _));
        }

        [Test]
        public void Campaign0x46DisruptorTimes25NotWingmen()
        {
            InStory(0x46, 10);
            Assert.AreEqual(22, NpcTables.GunDamage(new SpawnSpec { race = 1 }, false, false, out _), "int(9 * 2.5)");
            Assert.AreEqual(9, NpcTables.GunDamage(new SpawnSpec { race = 1 }, true, false, out _));
            Assert.AreEqual(183, NpcTables.GunItemFor(1, false));
            Assert.AreEqual(NpcTables.GunItem(1), NpcTables.GunItemFor(1, true));
            InStory(0x47, 10);
            Assert.AreEqual(NpcTables.GunItem(1), NpcTables.GunItemFor(1, false));
        }

        [Test]
        public void WantedAndChallengeRivalFireRankPlusBaseAt28()
        {
            InFreePlay(10);
            NpcTables.LevelFreelanceType = MissionType.Wanted;
            Assert.AreEqual(19, NpcTables.GunDamage(new SpawnSpec { race = 8 }, false, false, out float speed));
            Assert.AreEqual(28f, speed);
            Assert.AreEqual(9, NpcTables.GunDamage(new SpawnSpec { race = 8, alwaysFriend = true }, false, false, out speed), "a friend in a Wanted orbit");
            Assert.AreEqual(NpcTables.GunSpeed, speed);
            NpcTables.LevelFreelanceType = MissionType.Challenge;
            Assert.AreEqual(19, NpcTables.GunDamage(new SpawnSpec { race = 0, alwaysFriend = true }, false, false, out speed), "the rival");
            Assert.AreEqual(28f, speed);
            Assert.AreEqual(9, NpcTables.GunDamage(new SpawnSpec { race = 8 }, false, false, out speed), "the Challenge's pirates");
            NpcTables.LevelFreelanceType = -1;
            Assert.AreEqual(9, NpcTables.GunDamage(new SpawnSpec { race = 8 }, false, false, out speed));
            Assert.AreEqual(16f, speed);
        }

        [Test]
        public void ThreeFailuresInARowSoftenHostileGuns()
        {
            InStory(30, 10);
            NpcTables.InCampaignLevel = true;
            Session.LastFailedMission = 30;
            Session.FailCount = 3;
            Assert.AreEqual(6, NpcTables.GunDamage(new SpawnSpec { race = 8 }, false, false, out _), "int(9 * 0.7)");
            Assert.AreEqual(9, NpcTables.GunDamage(new SpawnSpec { race = 8, alwaysFriend = true }, false, false, out _));
            Assert.AreEqual(9, NpcTables.GunDamage(new SpawnSpec { race = 8 }, true, false, out _), "wingmen");
            Session.FailCount = 2;
            Assert.AreEqual(9, NpcTables.GunDamage(new SpawnSpec { race = 8 }, false, false, out _));
            Session.FailCount = 3;
            Session.LastFailedMission = 29;
            Assert.AreEqual(9, NpcTables.GunDamage(new SpawnSpec { race = 8 }, false, false, out _), "another step failed");
            Session.LastFailedMission = 30;
            NpcTables.InCampaignLevel = false;
            Assert.AreEqual(9, NpcTables.GunDamage(new SpawnSpec { race = 8 }, false, false, out _), "not in the campaign level");
        }

        [Test]
        public void GunDamageNeverBelowOne()
        {
            InStory(7);   // xp 8: rank 1, base 3, pirates x0.5 = 1
            Assert.GreaterOrEqual(NpcTables.GunDamage(Standing.Pirate), 1);
            for (int race = 0; race <= 11; race++) Assert.GreaterOrEqual(NpcTables.GunDamage(race), 1);
        }

        [Test]
        public void GunReloadByCampaign()
        {
            // 600 - 2 * campaign, campaign capped at 45 once the main story is won (npc_gun's reload_ms).
            InFreePlay(0);
            Assert.AreEqual(560f, NpcTables.GunReloadMs);
            InStory(30);
            Assert.AreEqual(540f, NpcTables.GunReloadMs);
            InStory(100);
            Assert.AreEqual(510f, NpcTables.GunReloadMs);
            InCompletedWorld();
            Assert.AreEqual(510f, NpcTables.GunReloadMs);
        }

        [TestCase(0, 0)]
        [TestCase(1, 3)]
        [TestCase(2, 7)]
        [TestCase(3, 25)]
        [TestCase(8, 19)]
        [TestCase(5, 19)]
        [TestCase(9, 5)]
        [TestCase(10, 229)]
        [TestCase(12, 19)]
        public void GunLookItemPerRace(int race, int item)
        {
            // npc_gun's item_for_visuals table.
            Assert.AreEqual(item, NpcTables.GunItem(race));
        }

        [Test]
        public void ShotSoundPerRace()
        {
            // npc_tables.py NPC_SHOT_SOUND[race] (>= 11: 61); ShotSound returns the index into
            // CombatAssets.shots: 0 Nirai EX1 (52), 1 Shkoom (55), 2 Nirai Charged (54), 3 Nirai EX2 (53), 4 Laser_Enemy (61),
            // 5 Laser_Void (62), 6 DarkMatterLaser (2276).
            int[] eventOfIndex = { 52, 55, 54, 53, 61, 62, 2276 };
            int[] npcShotSound = { 52, 55, 54, 53, 61, 61, 61, 61, 61, 62, 2276 };
            for (int race = 0; race <= 12; race++)
                Assert.AreEqual(race < 11 ? npcShotSound[race] : 61, eventOfIndex[NpcTables.ShotSound(race)], "race " + race);
        }

        [TestCase(-1, 90)]
        [TestCase(0, 90)]
        [TestCase(1, 65)]
        [TestCase(2, 35)]
        [TestCase(3, 10)]
        [TestCase(4, 10)]
        public void RaiderChanceBySecurity(int security, int chance)
        {
            Assert.AreEqual(chance, NpcTables.RaiderChance(security));
        }

        [Test]
        public void GunAndAiConstants()
        {
            // npc_traffic_ai.md 3 / CLAUDE.md "NPC stats" / "AI": 4 bullets, 16 u/ms, 3000 ms; base speed 2, freighters 1;
            // attack inside +-50000, fire inside +-35000, circle away inside +-8000.
            Assert.AreEqual(4, NpcTables.GunPool);
            Assert.AreEqual(16f, NpcTables.GunSpeed);
            Assert.AreEqual(3000f, NpcTables.GunLifetimeMs);
            Assert.AreEqual(2f, NpcTables.BaseSpeed);
            Assert.AreEqual(1f, NpcTables.FreighterSpeed);
            Assert.AreEqual(50000f, NpcTables.DetectRange);
            Assert.AreEqual(35000f, NpcTables.FireRange);
            Assert.AreEqual(8000f, NpcTables.BreakOffRange);
            Assert.AreEqual(0.0076f, NpcTables.FireCone, 0.0001f);
        }

        [Test]
        public void LevelFreelanceTypeDefaultsToNone()
        {
            // NpcTestBase puts it to -1 like a fresh run; the field must read "no freelance orbit" there.
            Assert.AreEqual(-1, NpcTables.LevelFreelanceType);
            Assert.AreEqual(MissionType.Empty, NpcTables.LevelFreelanceType);
        }
    }
}
