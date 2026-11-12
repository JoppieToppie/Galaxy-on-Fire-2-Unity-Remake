// CruiseEncounterSpawner.cs
// Remake: what a cruise drive's encounter puts into the orbit (Flight.CruiseEncounters, NavigationCruiseDrive): spawned
// through the level's traffic like the debug spawner's ships, at the game-space point the navigation chose.
//   pirates   2 + rank / 6 raiders (2..5), hostile whatever the standing (the hostiles rule then drops the drive)
//   trader    a freighter of the system's race (NpcTables.RandomFreighter) with two locals of its race beside it
//   derelict  2-4 drifting junk crates with a freighter's loot roll (NpcTables.RollLoot; an empty roll: energy cells)
//   distress  a local fighter of the system's race under attack by 1 + rank / 8 raiders (1..3)
//   flyby     three locals of a random race passing through (the drive keeps running)
// "ships" in an entry sets the count (pirates, the trader's escorts, the derelict's crates, the distress call's raiders,
// the flyby's wing). A "quest" entry is the navigation's (GameHooks.StartQuest), nothing is spawned here.
// Remake (2026-11-03, for mods): EVERSPACE 2's encounter types (decompiled BP_Encounter_*: FactionBattle, Trap, HiddenStash,
// ResourceDepot, HighRiskArea, and BP_EncounterBase's reward), each a kind of its own:
//   battle    a wing of the system's race (3) fighting raiders (3 + rank / 8); once the raiders are gone, BattlePay credits
//             (ES2's FactionBattle: "Battle Support", GivesCreditsReward, UMissionLib::CalculateRewardCredits)
//   trap      passes for an unknown signal: raiders (1.25 x the pirates' count, ES2's SpawnFactorNPCs) around the player on
//             arrival, a second group ahead one time in five (ChanceForTwoGroups 0.2)
//   stash     one crate with three loot rolls (ES2's hidden container, its spawn group 75)
//   depot     three crates of ore (ES2's SpawnFactorResources 3, Containers 2) guarded by 1.5 x the pirates' count / 2 raiders
//   highrisk  three waves of raiders, each on the last one's end (ES2's HighRiskArea: waves, respawns, boss loot); after the
//             third a crate with 8-14 loot rolls (ES2's HighRiskAreaBoss loot) and BattlePay (ES2 pays every encounter by
//             the one formula: none overrides RewardCreditsFactor); leaving ends it
//   lootdrone a loot drone (Flight.NpcRoles' lootDrone: it runs, shedding its loot, and jumps away): one of the mods' (their
//             ships.json "traffic": { "role": "lootDrone" }, ModTraffic.PickLootDrone), else a pirate fighter flying as one;
//             neutral, as a pirate (ES2's BP_LootDrone, placed at its locations: a Freelancer that never turns hostile, raises
//             no wanted level and the other ships ignore; here no friendly-fire rules, and its kill is a pirate's), in the
//             group the orbit never relaunches (Special)
//   rift      a rift run (World.RiftEncounter, Flight.RiftRun: EVERSPACE 2's Ancient Rifts), "ships" its stages (default 4)
//   minesweeper (2026-11-06, ES2's BP_Encounter_Minesweeper, StuckFreighterVariant) a freighter of the system's race stuck
//             (stationary) in a minefield: one BP_Minefield_BiggerRadius (Radius 30 000 cm x the ES2 ships' 3.6, as the mines'
//             trigger and blast: MinesweeperRadius units, so the mines lie further apart than they reach) of
//             "ships" kinetic mines (default 40, ES2's MaxMineAmount), marked; all shot (or set off), BattlePay
//             (World.MinesweeperWatch); the freighter lost, nothing
//   challenge (2026-11-06, ES2's BP_Encounter_ShootingChallenge) a Challenger of the system's race (a local fighter, its gun
//             x ChallengerDealDamageFactor 3) and NumGroups 4-5 groups of raiders (1 + rank / 12 each, or "ships" in all);
//             destroy more of them than it does (World.ChallengeWatch): half a battle's pay
//   needy     (2026-11-06, ES2's BP_Encounter_ItemsNeeded, "Ship In Need") a damaged freighter or fighter of the system's race,
//             still, asking "ships" (default ShipInNeed.Need 3) units of a repair good; handed over, BattlePay (World.ShipInNeed)
// The counts are the remake's picks (ES2 places its groups by spawn_groups.CSV at a location's level). The pay is ES2's
// (BattlePay); its location level the orbit's "level" (stations.json: an ES2 location's own), else the station's tech level
// x 3 (Es2Level: GoF2's 1-10 over ES2's 1-30).

using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using UnityEngine;

namespace GoF2Remake.World
{
    public static class CruiseEncounterSpawner
    {
        const float M = 0.05f;
        static Vector3 ToUnity(Vector3 game) => new Vector3(game.x, game.y, -game.z) * M;
        static Vector3 Jitter(float units) => new Vector3(Random.Range(-units, units), Random.Range(-units, units) * 0.5f, Random.Range(-units, units));

        /// <summary>'at' and 'forward' in game units / game space.</summary>
        public static void Spawn(SpaceLevel level, CruiseEncounters.Entry e, Vector3 at, Vector3 forward)
        {
            if (level == null || level.Traffic == null || e == null) return;
            var traffic = level.Traffic;
            var db = level.Database ?? Database.Shared;
            int rank = Session.Rank, systemRace = traffic.SystemRace;
            if (e.music != null) EncounterMusic.Begin(level, e.music, at);   // (its own fight music, encounters.json "music")
            switch (e.kind)
            {
                case CruiseEncounters.Pirates:
                {
                    int n = e.ships > 0 ? e.ships : Mathf.Clamp(2 + rank / 6, 2, 5);
                    for (int i = 0; i < n; i++)
                        traffic.SpawnShip(new SpawnSpec { group = NpcGroup.Raider, race = Standing.Pirate, ship = NpcTables.RandomFighter(Standing.Pirate),
                                                          alwaysEnemy = true, position = at + Jitter(6000f) });
                    break;
                }
                case CruiseEncounters.Trader:
                {
                    var (ship, race) = NpcTables.RandomFreighter(systemRace);
                    traffic.SpawnShip(new SpawnSpec { group = NpcGroup.Freighter, race = race, ship = ship, freighter = true, position = at });
                    int n = e.ships >= 0 ? e.ships : 2;
                    for (int i = 0; i < n; i++)
                        traffic.SpawnShip(new SpawnSpec { group = NpcGroup.Local, race = race, ship = NpcTables.RandomFighter(race), position = at + Jitter(5000f) });
                    break;
                }
                case CruiseEncounters.Derelict:
                {
                    int n = e.ships > 0 ? e.ships : Random.Range(2, 5);
                    for (int i = 0; i < n; i++)
                    {
                        var cargo = NpcTables.RollLoot(db, true);
                        if (cargo.Count == 0) cargo = new List<ItemStack> { new ItemStack(GalaxyMap.EnergyCellItem, Random.Range(1, 5)) };
                        Crate.Spawn(ToUnity(at + Jitter(4000f)), cargo, systemRace, Crate.LookJunk);
                    }
                    break;
                }
                case CruiseEncounters.Distress:
                {
                    traffic.SpawnShip(new SpawnSpec { group = NpcGroup.Local, race = systemRace, ship = NpcTables.RandomFighter(systemRace), position = at });
                    int n = e.ships > 0 ? e.ships : Mathf.Clamp(1 + rank / 8, 1, 3);
                    for (int i = 0; i < n; i++)
                        traffic.SpawnShip(new SpawnSpec { group = NpcGroup.Raider, race = Standing.Pirate, ship = NpcTables.RandomFighter(Standing.Pirate),
                                                          alwaysEnemy = true, position = at + forward * 8000f + Jitter(4000f) });
                    break;
                }
                case CruiseEncounters.Flyby:
                {
                    int n = e.ships > 0 ? e.ships : 3, race = Random.Range(0, 4);
                    for (int i = 0; i < n; i++)
                        traffic.SpawnShip(new SpawnSpec { group = NpcGroup.Local, race = race, ship = NpcTables.RandomFighter(race), position = at + Jitter(8000f) });
                    break;
                }
                case CruiseEncounters.Battle:
                {
                    var raiders = new List<NpcShip>();
                    for (int i = 0; i < 3; i++)
                        traffic.SpawnShip(new SpawnSpec { group = NpcGroup.Local, race = systemRace, ship = NpcTables.RandomFighter(systemRace), position = at + Jitter(4000f) });
                    int n = e.ships > 0 ? e.ships : Mathf.Clamp(3 + rank / 8, 3, 5);
                    for (int i = 0; i < n; i++)
                        raiders.Add(Raider(traffic, at + forward * 6000f + Jitter(4000f)));
                    EncounterWatch.Start(level, raiders, BattlePay(Es2Level(level)), false);
                    break;
                }
                case CruiseEncounters.Trap:
                {
                    int n = e.ships > 0 ? e.ships : Mathf.CeilToInt(Mathf.Clamp(2 + rank / 6, 2, 5) * 1.25f);
                    var player = ToGame(level.Player != null ? level.Player.transform.position : ToUnity(at));
                    for (int i = 0; i < n; i++) Raider(traffic, player + Jitter(7000f));
                    if (Random.value < 0.2f)
                        for (int i = 0; i < n; i++) Raider(traffic, player + forward * 20000f + Jitter(6000f));
                    break;
                }
                case CruiseEncounters.Stash:
                {
                    var cargo = new List<ItemStack>();
                    for (int k = 0; k < 3; k++) Merge(cargo, NpcTables.RollLoot(db, true));
                    if (cargo.Count == 0) cargo.Add(new ItemStack(GalaxyMap.EnergyCellItem, Random.Range(2, 6)));
                    Crate.Spawn(ToUnity(at), cargo, systemRace, Crate.LookJunk).keep = true;   // (found, not timed)
                    break;
                }
                case CruiseEncounters.Depot:
                {
                    int crates = e.ships > 0 ? e.ships : 3;
                    for (int i = 0; i < crates; i++)
                        Crate.Spawn(ToUnity(at + Jitter(3000f)), new List<ItemStack> { new ItemStack(Ores[Random.Range(0, Ores.Length)], Random.Range(4, 13)) }, systemRace, Crate.LookJunk).keep = true;
                    int guards = Mathf.Max(1, Mathf.RoundToInt(Mathf.Clamp(2 + rank / 6, 2, 5) * 1.5f / 2f));
                    for (int i = 0; i < guards; i++) Raider(traffic, at + Jitter(6000f));
                    break;
                }
                case CruiseEncounters.HighRisk:
                {
                    int n = e.ships > 0 ? e.ships : Mathf.Clamp(2 + rank / 8, 2, 4);
                    var wave = new List<NpcShip>();
                    for (int i = 0; i < n; i++) wave.Add(Raider(traffic, at + Jitter(6000f)));
                    EncounterWatch.Start(level, wave, BattlePay(Es2Level(level)), true, waves: 3, waveSize: n, centre: at);
                    break;
                }
                case CruiseEncounters.LootDrone:
                {
                    int ship = Modding.ModTraffic.PickLootDrone();
                    traffic.SpawnShip(new SpawnSpec { group = NpcGroup.Special, race = Standing.Pirate, ship = ship >= 0 ? ship : NpcTables.RandomFighter(Standing.Pirate),
                                                      role = ship >= 0 ? null : "lootDrone", alwaysNeutral = true, position = at });
                    break;
                }
                case CruiseEncounters.Rift:
                    RiftEncounter.Start(level, at, e.ships, e.bosses);
                    return;   // (it spawns its own and connects them)
                case CruiseEncounters.Challenge:
                {
                    traffic.SpawnShip(new SpawnSpec { group = NpcGroup.Local, race = systemRace, ship = NpcTables.RandomFighter(systemRace), position = at,
                                                      name = Localization.Extra("challenger", "Challenger"), gunItem = NpcTables.GunItemFor(systemRace, false),
                                                      gunFactor = ChallengerDamageFactor });
                    var raiders = new List<NpcShip>();
                    int groups = Random.Range(4, 6), each = Mathf.Clamp(1 + rank / 12, 1, 2);
                    int total = e.ships > 0 ? e.ships : groups * each;
                    for (int i = 0; i < total; i++)
                    {
                        var group = at + forward * 6000f + new Vector3(Mathf.Cos(i % groups * 1.3f), 0.2f, Mathf.Sin(i % groups * 1.3f)) * 9000f;
                        raiders.Add(Raider(traffic, group + Jitter(2000f)));
                    }
                    ChallengeWatch.Start(level, raiders, BattlePay(Es2Level(level)) / 2, at);
                    break;
                }
                case CruiseEncounters.Needy:
                {
                    bool big = Random.value < 0.5f;   // (ES2's freelancer or freelancer freighter)
                    NpcShip needy;
                    if (big)
                    {
                        var (ship, race) = NpcTables.RandomFreighter(systemRace);
                        needy = traffic.SpawnShip(new SpawnSpec { group = NpcGroup.Special, race = race, ship = ship, freighter = true, stationary = true, position = at });
                    }
                    else needy = traffic.SpawnShip(new SpawnSpec { group = NpcGroup.Special, race = systemRace, ship = NpcTables.RandomFighter(systemRace), stationary = true, position = at });
                    ShipInNeed.Start(level, needy, e.ships > 0 ? e.ships : ShipInNeed.Need, BattlePay(Es2Level(level)));
                    break;
                }
                case CruiseEncounters.Minesweeper:
                {
                    var (ship, race) = NpcTables.RandomFreighter(systemRace);
                    var freighter = traffic.SpawnShip(new SpawnSpec { group = NpcGroup.Special, race = race, ship = ship, freighter = true, stationary = true, position = at });
                    int count = Mathf.Clamp(e.ships > 0 ? e.ships : 40, 1, 40);
                    float radiusM = MinesweeperRadius * M;
                    var root = new GameObject("Minesweeper field");
                    root.transform.SetParent(level.transform, false);
                    var mines = new List<OrbitMine>();
                    float damage = OrbitMinefield.RaiderSeconds(new Modding.ModWorld.MinefieldSpec()) * NpcTables.RaiderGunDps;
                    var places = OrbitMinefield.Places(ToUnity(at), radiusM, count, radiusM * 0.48f * Mathf.Pow(40f / count, 1f / 3f), new System.Random(Random.Range(int.MinValue, int.MaxValue)));
                    foreach (var p in places)
                    {
                        var off = p - ToUnity(at);   // (clear of the freighter's hull)
                        var place = off.magnitude < MinesweeperClearM ? ToUnity(at) + (off.sqrMagnitude > 1e-4f ? off.normalized : Random.onUnitSphere) * MinesweeperClearM : p;
                        mines.Add(OrbitMine.Lay(level, root.transform, place, Modding.ModWorld.MinefieldSpec.Kind.Kinetic, damage, true));
                    }
                    if (freighter != null) MinesweeperWatch.Start(level, freighter, mines, BattlePay(Es2Level(level)), ToUnity(at));
                    break;
                }
                default:
                    return;
            }
            traffic.ConnectPlayers();   // the new ships and the others see each other (targets, hit lists)
        }

        /// <summary>BP_Job_ShootingChallenge_Task_01's ChallengerDealDamageFactor (its primary weapon's damage).</summary>
        public const float ChallengerDamageFactor = 3f;

        /// <summary>BP_Minefield_BiggerRadius's Radius 30 000 cm in game units (x 0.2, x 3.6 as the mines' numbers); the mines keep this
        /// far (m) from the freighter.</summary>
        public const float MinesweeperRadius = 6000f * 3.6f, MinesweeperClearM = 60f;

        /// <summary>The ores a resource depot holds (items 154-164: the asteroids' ores).</summary>
        static readonly int[] Ores = { 154, 155, 156, 157, 158, 159, 160, 161, 162, 163, 164 };

        static Vector3 ToGame(Vector3 unity) => new Vector3(unity.x, unity.y, -unity.z) / M;

        /// <summary>An encounter's pay at ES2 location level 'es2Level' (decompiled UMissionLib::CalculateRewardCredits:
        /// (800 + 4200 x level / MaxPlayerLevel 30, clamped 0..1) x RewardCreditsFactor 1, down to a multiple of 50).</summary>
        public static int BattlePay(int es2Level) => (int)(800f + 4200f * Mathf.Clamp01(es2Level / (float)MaxPlayerLevel)) / 50 * 50;

        /// <summary>ES2's MaxPlayerLevel (UESGameInstance's constructor).</summary>
        public const int MaxPlayerLevel = 30;

        /// <summary>The orbit as an ES2 location level: its station's tech level (1-10) x 3; 1 without a station.</summary>
        public static int Es2Level(SpaceLevel level)
        {
            var spec = level != null && level.Layout != null ? Modding.ModWorld.OrbitOf(level.Layout.stationIndex) : null;
            if (spec != null && spec.level > 0) return Mathf.Clamp(spec.level, 1, MaxPlayerLevel);
            return Mathf.Clamp((level != null && level.StationInfo != null ? level.StationInfo.techLevel : 0) * 3, 1, MaxPlayerLevel);
        }

        public static NpcShip Raider(Traffic traffic, Vector3 gameAt) =>
            traffic.SpawnShip(new SpawnSpec { group = NpcGroup.Raider, race = Standing.Pirate, ship = NpcTables.RandomFighter(Standing.Pirate),
                                              alwaysEnemy = true, position = gameAt });

        static void Merge(List<ItemStack> into, List<ItemStack> more)
        {
            foreach (var s in more)
            {
                var have = into.Find(x => x.item == s.item);
                if (have != null) have.amount += s.amount; else into.Add(new ItemStack(s.item, s.amount));
            }
        }
    }
}
