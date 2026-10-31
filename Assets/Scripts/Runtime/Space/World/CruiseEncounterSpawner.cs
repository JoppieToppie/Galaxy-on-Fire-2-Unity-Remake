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
                default:
                    return;
            }
            traffic.ConnectPlayers();   // the new ships and the others see each other (targets, hit lists)
        }
    }
}
