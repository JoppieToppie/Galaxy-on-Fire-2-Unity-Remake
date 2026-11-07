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
//             (ES2's FactionBattle: "Battle Support", GivesCreditsReward)
//   trap      passes for an unknown signal: raiders (1.25 x the pirates' count, ES2's SpawnFactorNPCs) around the player on
//             arrival, a second group ahead one time in five (ChanceForTwoGroups 0.2)
//   stash     one crate with three loot rolls (ES2's hidden container, its spawn group 75)
//   depot     three crates of ore (ES2's SpawnFactorResources 3, Containers 2) guarded by 1.5 x the pirates' count / 2 raiders
//   highrisk  three waves of raiders, each on the last one's end (ES2's HighRiskArea: waves, respawns, boss loot); after the
//             third a crate with three loot rolls and twice BattlePay; leaving ends it
//   lootdrone a loot drone (Flight.NpcRoles' lootDrone: it runs, shedding its loot, and jumps away): one of the mods' (their
//             ships.json "traffic": { "role": "lootDrone" }, ModTraffic.PickLootDrone), else a pirate fighter flying as one;
//             neutral, as a pirate (ES2's BP_LootDrone, placed at its locations: a Freelancer that never turns hostile, raises
//             no wanted level and the other ships ignore; here no friendly-fire rules, and its kill is a pirate's), in the
//             group the orbit never relaunches (Special)
// The counts and pay are the remake's picks: ES2 keeps them in its spawn groups and reward tables.

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
                    EncounterWatch.Start(level, raiders, BattlePay(rank), false);
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
                    EncounterWatch.Start(level, wave, BattlePay(rank) * 2, true, waves: 3, waveSize: n, centre: at);
                    break;
                }
                case CruiseEncounters.LootDrone:
                {
                    int ship = Modding.ModTraffic.PickLootDrone();
                    traffic.SpawnShip(new SpawnSpec { group = NpcGroup.Special, race = Standing.Pirate, ship = ship >= 0 ? ship : NpcTables.RandomFighter(Standing.Pirate),
                                                      role = ship >= 0 ? null : "lootDrone", alwaysNeutral = true, position = at });
                    break;
                }
                default:
                    return;
            }
            traffic.ConnectPlayers();   // the new ships and the others see each other (targets, hit lists)
        }

        /// <summary>The ores a resource depot holds (items 154-164: the asteroids' ores).</summary>
        static readonly int[] Ores = { 154, 155, 156, 157, 158, 159, 160, 161, 162, 163, 164 };

        static Vector3 ToGame(Vector3 unity) => new Vector3(unity.x, unity.y, -unity.z) / M;

        /// <summary>A battle's pay: 1500 + 300 a rank (remake pick for ES2's CalculateRewardCredits).</summary>
        public static int BattlePay(int rank) => 1500 + 300 * Mathf.Max(0, rank);

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
