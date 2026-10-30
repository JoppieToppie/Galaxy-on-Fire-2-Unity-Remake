// NpcTables.cs
// NPC rules as plain C# (Reference/research/npc_traffic_ai.md 2-3, ship_combat.md 3-5; reference implementation
// Reference/tools/npc/npc_tables.py):
//   Globals::getRandomEnemyFighter 0xf9034   ship pool per race (races 4-8 and >= 11 use the pirates')
//   Level::createShip 0xcf83c               hull (4 * campaign + 14 * rank + 20, freighters x5, battleship x25,
//                                           Extreme x2), hit cube half-size 1000 (650 Extreme), EMP 5 * rank + 40
//   Level::assignGuns 0xcb638               one gun: 4 bullets, 16 u/ms, 3000 ms, reload 600 - 2 * campaign ms, damage
//                                           clamp(int(0.9 * (rank - 2)), 0, 20) (x2 Extreme, cap 22) + 2, 3 minimum;
//                                           the look (projectile, impact) and shot sound of one item per race
//   Level::createMission 0xbda70            raider chance per security level 90 / 65 / 35 / 10 %
//   Generator::getLootList 0xa1b78          what an NPC carries (1/3 nothing; 1-2 entries)

using System.Collections.Generic;
using GoF2Remake.Data;
using UnityEngine;

namespace GoF2Remake.Flight
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class NpcTables
    {
        public const float BaseSpeed = 2f, BoostSpeed = 5.5f, FreighterSpeed = 1f;   // u/ms
        public const int GunPool = 4;
        public const float GunLifetimeMs = 3000f, GunSpeed = 16f;
        public const float DetectRange = 50000f, FireRange = 35000f, BreakOffRange = 8000f;
        public const float FireCone = 0.0076294f;   // PlayerFighter+0x1a4, 0x3bf9fff6

        /// <summary>Globals::getRandomEnemyFighter (FighterPool, in GoF2.Game for the shop and the ticker).</summary>
        public static int RandomFighter(int race) => Modding.ModTraffic.Pick(race, FighterPool.Random(race));   // remake mods: now and then a mod's (ModTraffic)

        /// <summary>One of the dearer half of the race's fighters (FighterPool.Strong); remake mods: now and then a mod's
        /// (ModTraffic).</summary>
        public static int StrongFighter(Database db, int race) => Modding.ModTraffic.Pick(race, FighterPool.Strong(db, race), strong: true);

        /// <summary>Freighter ship index and its race (30 % Nivelian in Terran systems and the other way round).</summary>
        public static (int ship, int race) RandomFreighter(int systemRace)
        {
            // remake mods: a mod race's system: its own freighters (the look race's model when it has no mod freighter)
            if (Modding.ModRaces.Is(systemRace)) return (Modding.ModRaces.Look(systemRace) == 1 ? 13 : 15, systemRace);
            int race = systemRace;
            if (Random.Range(0, 100) < 30) race = systemRace == 0 ? 2 : systemRace == 2 ? 0 : systemRace;
            if (race < 0 || race > 3) race = 0;
            return (race == 1 ? 13 : 15, race);
        }

        public static string FreighterAssembly(int race) => Modding.ModRaces.Look(race) switch
        {
            1 => "cargo_004_vossk",
            2 => "cargo_002_nivelian",
            3 => "cargo_001_midorian",
            _ => "cargo_003_terran",
        };

        static float Difficulty => Session.DifficultyFactor;   // x + x * (options[0x2c] - 0.5)

        /// <summary>The campaign index the NPC formulas use: capped at 45 once the main story is won (Level::createShip's
        /// "gameWon ? 180 : 4 * campaign", assignGuns' 600 - 2 * 45).</summary>
        static int StatCampaign => (!Session.FreePlay || Session.CompletedWorld) && Session.CampaignMission >= Story.GameWonIndex ? Story.GameWonIndex : Session.CampaignMission;

        /// <summary>Level::createShip hull. kind: 0 fighter, 1 fixed object (freighter / battleship 14).</summary>
        public static int Hull(int kind, int ship)
        {
            int rank = Mathf.Min(Session.Rank, 20);
            float hp = 4 * StatCampaign + 14 * rank + 20;
            // Level::createShip 0xcf930 truncates the special ships' hull to an int before the difficulty factor
            if (ship == 51) hp = (int)(hp * 1.7f); else if (ship == 49) hp = (int)(hp * 17f); else if (ship == 44) hp = (int)(hp * 2.25f);
            if (IsHull270Mission(Session.CampaignMission)) hp = 270;
            if (kind == 1) hp *= ship == 14 ? 25 : 5;
            return (int)(hp * Difficulty);
        }

        public static int Emp(int kind) => (5 * Mathf.Min(Session.Rank, 20) + 40) * (kind == 1 ? 3 : 1);
        public static float EmpRecoveryMs(int kind) => kind == 1 ? 45000f : 15000f;
        public static float HitRadiusUnits => Session.IsExtreme ? 650f : 1000f;

        /// <summary>Level::assignGuns damage for a plain ship of this race (see the SpawnSpec overload).</summary>
        public static int GunDamage(int race) => GunDamage(new SpawnSpec { race = race }, false, false, out _);

        /// <summary>The freelance mission whose orbit this is (Status::getMission's type while FreelanceOrbit runs), -1 = none.</summary>
        public static int LevelFreelanceType = -1;
        /// <summary>A campaign level runs in this orbit (Mission::isCampaignMission on the level mission).</summary>
        public static bool InCampaignLevel;

        /// <summary>Level::assignGuns 0xcb638, one NPC gun: b = clamp(int(0.9 (rank - 2)), 0, 20), d = int(b x (1 + difficulty
        /// - 0.5)) (22 above 21), base = d + 2 (3 for 0); campaign 4 = 1. The Wanted target (freelance type 6, not a friend) and the
        /// Challenge rival (type 0xc, the always-friend) fire rank + base at 28 u/ms. Void (not a friend): x2 at 0x10, else
        /// x0.8; campaign 0x31-0x34 / 0x38 = 5 (not the 0x10 Void); 0x50 turrets x1.7; 0x46 x2.5 (not wingmen); race 10 x0.7;
        /// 7 pirates x0.5; a campaign level failed 3+ times in a row: hostile ships x0.7.</summary>
        public static int GunDamage(SpawnSpec spec, bool wingman, bool turret, out float speed)
        {
            int campaign = Session.CampaignMission;
            float f = 0.9f * (Session.Rank - 2);
            int b = f >= 20 ? 20 : f < 0 ? 0 : (int)f;
            int d = (int)(b * Difficulty);
            if (d > 21) d = 22;
            int baseDmg = d == 0 ? 3 : d + 2;
            int dmg = !Session.FreePlay && campaign == 4 ? 1 : baseDmg;
            speed = GunSpeed;
            if ((LevelFreelanceType == MissionType.Wanted && !spec.alwaysFriend) || (LevelFreelanceType == MissionType.Challenge && spec.alwaysFriend))
            {
                dmg = Session.Rank + baseDmg;
                speed = 28f;
            }
            bool story = !Session.FreePlay;
            if (!wingman && !spec.alwaysFriend && spec.race == Standing.Void)
            {
                if (story && campaign == 0x10) dmg *= 2;
                else { dmg = (int)(dmg * 0.8f); if (story && IsHull270Mission(campaign)) dmg = 5; }
            }
            else if (story && IsHull270Mission(campaign)) dmg = 5;
            if (story && campaign == 0x50 && turret) dmg = (int)(dmg * 1.7f);
            if (story && campaign == 0x46 && !wingman) dmg = (int)(dmg * 2.5f);
            if (spec.race == Standing.Specter) dmg = (int)(dmg * 0.7f);
            if (story && campaign == 7 && spec.race == Standing.Pirate) dmg = (int)(dmg * 0.5f);
            // The same campaign mission failed 3+ times in a row -> the hostile ships' guns x0.7.
            if (story && InCampaignLevel && Session.FailCount >= 3 && Session.LastFailedMission == campaign && !spec.alwaysFriend && !wingman) dmg = (int)(dmg * 0.7f);
            return Mathf.Max(1, dmg);
        }

        /// <summary>Level::createShip / assignGuns' mask 0x8f from 0x31: campaign 0x31-0x34 and 0x38 (the Valkyrie K'Suukk
        /// escape): every ship's hull is 270 and every gun does 5.</summary>
        public static bool IsHull270Mission(int campaign) => !Session.FreePlay && campaign >= 0x31 && campaign <= 0x38 && ((0x8f >> (campaign - 0x31)) & 1) != 0;

        /// <summary>Level::assignGuns: at campaign 0x46 every ship but the wingmen fires the Disruptor Laser (0xb7).</summary>
        public static int GunItemFor(int race, bool wingman) => !Session.FreePlay && Session.CampaignMission == 0x46 && !wingman ? 183 : GunItem(race);

        public static float GunReloadMs => 600f - 2f * StatCampaign;

        /// <summary>The item whose projectile / impact an NPC gun of this race shows.</summary>
        public static int GunItem(int race) => Modding.ModRaces.Get(race) is var f && f != null && f.gunItem >= 0 ? f.gunItem   // remake mods: a mod race's "gun"
            : Modding.ModRaces.Look(race) switch { 0 => 0, 1 => 3, 2 => 7, 3 => 25, 9 => 5, 10 => 229, _ => 19 };

        /// <summary>DAT_002526c0 shot sound per race, as an index into CombatAssets.shots: 0 Nirai EX1 (52),
        /// 1 Shkoom (55), 2 Nirai Charged (54), 3 Nirai EX2 (53), 4 Laser_Enemy (61), 5 the Void's Laser_Void (62,
        /// Laser_Vossk_V01), 6 the Specters' DarkMatterLaser (2276).</summary>
        public static int ShotSound(int race) => Modding.ModRaces.Look(race) switch { 0 => 0, 1 => 1, 2 => 2, 3 => 3, 9 => 5, 10 => 6, _ => 4 };

        /// <summary>Level::createMission: raider group chance for the (effective) security level.</summary>
        public static int RaiderChance(int security) => security <= 0 ? 90 : security == 1 ? 65 : security == 2 ? 35 : 10;

        static readonly int[] LootTypeChance = { 10, 40, 2, 10, 100, 10 };   // DAT_00251e5c by item type

        /// <summary>Generator::getLootList(-1, -1): the cargo an NPC drops (empty 1/3 of the time).</summary>
        public static List<ItemStack> RollLoot(Database db, bool freighter)
        {
            var list = new List<ItemStack>();
            int n = Random.Range(0, 3);
            // Android 2.0.16: empty 1 in 3; iOS 1.1.19 (the Default economy): the second roll only when the first is 0, 1 in 9.
            if (Session.Economy == Economy.Default ? n == 0 && Random.Range(0, 3) == 0 : Random.Range(0, 3) == 0) return list;
            for (int e = 0; e < Mathf.Max(1, n); e++)
            {
                ItemStack entry = null;
                for (int tries = 0; tries < 100 && entry == null; tries++)
                {
                    var it = db.Items[Random.Range(0, db.Items.Count)];
                    int type = it.TypeId;
                    if (it.blueprint.Count > 0 || it.maxPrice <= 0) continue;
                    if (it.index == 164 || it.index == 175 || it.index == 217 || it.index == 218) continue;
                    if (Random.Range(0, 100) >= (type >= 0 && type < LootTypeChance.Length ? LootTypeChance[type] : 10)) continue;
                    if (Random.Range(0, 100) >= it.occurrence) continue;
                    if (!Modding.ModUnlocks.ItemAvailable(it.index)) continue;   // remake mods: its "available" condition
                    if (Shop.NotInSession(db, it.index)) continue;   // remake: no time extender in multiplayer
                    if (type == 4) entry = new ItemStack(it.index, Random.Range(0, 9) + 1);
                    else if (it.techLevel < 8) entry = new ItemStack(it.index, Random.Range(0, 3) + 1);
                }
                entry ??= new ItemStack(154 + Random.Range(0, 10), Random.Range(0, 9) + 1);
                if (freighter) entry.amount = Mathf.Max(entry.amount * (Random.Range(0, 4) + 2), Random.Range(0, 5) + 8);
                list.Add(entry);
            }
            if (GalaxyMap.HasJumpDrive(db) && GalaxyMap.CellsInCargo() == 0 && Random.Range(0, 100) < 10)
                list[0] = new ItemStack(GalaxyMap.EnergyCellItem, Session.Economy == Economy.Default ? list[0].amount : Random.Range(0, 9) + 1);   // iOS keeps the amount
            return list;
        }
    }
}
