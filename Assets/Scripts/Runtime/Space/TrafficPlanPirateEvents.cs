// TrafficPlanPirateEvents.cs
// TrafficPlan: remake: pirate events (GitHub #6) (part of the partial TrafficPlan; its fields are in TrafficPlan.cs).

using System.Collections.Generic;
using GoF2Remake.Data;
using UnityEngine;

namespace GoF2Remake.Flight
{
    public static partial class TrafficPlan
    {
        /// <summary>The chance of an event per orbit entry by the system's security level (0 Dangerous, 1 Risky; Average and
        /// Secure systems never have one). With the Pirate activity board (PirateReports) most events are reported ones, so
        /// the unannounced roll drops to a third.</summary>
        static int[] EventChance => PirateEventRules.EventChance;
        static int[] UnreportedChance => PirateEventRules.UnreportedChance;
        /// <summary>A tier 4 / 5 outpost's equipment: one piece (tier 5 two different ones) of DangerousLootHigh (tier 5 or rank
        /// 10+) else DangerousLootLow. Remake mods: at tier 5 each piece is one time in TopTechOdds a mod's gear above the
        /// original's top tech level (Shop.AboveTopTech, weighted by occurrence) while any may be found; without such gear
        /// the original's tables alone, with the same random draws.</summary>
        public static List<int> OutpostGear(Database db, int tier, int rank)
        {
            var table = tier == 5 || rank >= 10 ? DangerousLootHigh : DangerousLootLow;
            var gear = new List<int> { table[Random.Range(0, table.Length)] };
            if (tier != 5) return gear;
            int second = table[Random.Range(0, table.Length)];
            if (second == gear[0]) second = table[(System.Array.IndexOf(table, second) + 1) % table.Length];
            gear.Add(second);
            var top = Shop.AboveTopTech(db);
            for (int k = 0; k < gear.Count && top.Count > 0; k++)
            {
                if (Random.Range(0, TopTechOdds) != 0) continue;
                var it = Shop.PickByOccurrence(top);
                gear[k] = it.index;
                top.Remove(it);
            }
            return gear;
        }
        static bool InStoryline(int station) => PirateEventRules.InStoryline(station);
        /// <param name="hasRaiders">The orbit already has a raider group: no unreported event on top of it (a reported
        /// sighting still comes; players' report 2026-10-16: on Extreme the two together overwhelmed a pilot on arrival).</param>
        static void AddPirateEvent(List<SpawnSpec> list, Database db, int station, SystemData system, int secEff, bool story, int cm, bool baseSystem, bool hasRaiders = false)
        {
            // A reported sighting here (the Missions window's Pirate activity): its outpost or named boss, whatever this pilot's
            // rank (the rank only decides which reports are made: a session's board is the server's, its pilots start at 0).
            // Not in an Informer orbit (a player's report): the original's is the spy and the police alone, and any other death there spoils
            // the mission (Traffic.OnShipDied); a report waits for a later visit.
            if (InformerOrbit(station)) return;
            var sighting = PirateReports.At(station);
            if (sighting != null && PirateEventAllowed(db, station, system, true)) { AddPirateEventShips(list, !sighting.boss, sighting.tier, sighting); return; }
            if (hasRaiders || !PirateEventAllowed(db, station, system)) return;
            int sec = system.securityLevel;
            var chance = PirateReports.Enabled ? UnreportedChance : EventChance;
            if (Random.Range(0, 100) >= chance[sec]) return;
            AddPirateEventShips(list, Random.value < 0.5f, PirateReports.RollTier(sec, Session.Rank));
        }
        /// <summary>PirateEventRules.Allowed (in GoF2.Game, shared with the Pirate activity board).</summary>
        public static bool PirateEventAllowed(Database db, int station, SystemData system, bool anyRank = false) => PirateEventRules.Allowed(db, station, system, anyRank);
        /// <summary>The event's ships by its threat tier (1 Minor .. 5 Extremely dangerous, PirateReports.TierName): the outpost
        /// with its guards (more by tier), its turrets (2 from tier 3, 4 at tier 4, all 6 at tier 5), its hull (x0.4 .. x2) and
        /// its crate (equipment from tier 4); or the boss (hull x0.35 .. x2.4, its gun x1 .. x3, 0 .. 7 escorts, the dearer
        /// fighters from tier 4, more and costlier goods). Public for testing (spawned with Traffic.SpawnShip).</summary>
        public static void AddPirateEventShips(List<SpawnSpec> list, bool outpostEvent, int tier, PirateSighting sighting = null)
        {
            tier = Mathf.Clamp(tier, 1, PirateReports.MaxTier);
            int t = tier - 1;
            int sightingId = sighting != null ? sighting.id : -1;
            int rank = Mathf.Min(Session.Rank, 20);
            var db = Database.Shared;
            int Fighter() => tier >= 4 ? NpcTables.StrongFighter(db, Standing.Pirate) : NpcTables.RandomFighter(Standing.Pirate);
            if (outpostEvent)
            {
                // The outpost, 110-160 km out in a random direction (flattened; the biggest stations reach 70 km), guarded like
                // a pirate base.
                var dir = Random.onUnitSphere;
                dir.y *= 0.3f;
                var at = dir.normalized * Random.Range(110000f, 160000f);
                var assets = CombatAssets.Load();
                // The crate by tier: 1 half the pirate base's goods, 2 those, 3 and 2-4 t of rare goods, 4 a piece of equipment
                // (by rank) and 3-6 t of the costliest goods, 5 two pieces of the best equipment and 8-12 t of them.
                var loot = PirateBases.Loot[Random.Range(0, PirateBases.Loot.Length)];
                var extra = new List<ItemStack>();
                if (tier == 1) loot.amount = Mathf.Max(1, loot.amount / 2);
                if (tier >= 4)
                {
                    var gear = OutpostGear(db, tier, rank);
                    loot = (gear[0], 1);
                    for (int g = 1; g < gear.Count; g++) extra.Add(new ItemStack(gear[g], 1));
                    extra.Add(new ItemStack(DangerousBossLoot[Random.Range(0, DangerousBossLoot.Length)],
                                            tier == 5 ? Random.Range(8, 13) : Random.Range(3, 7)));
                }
                else if (tier == 3) extra.Add(new ItemStack(BossLoot[Random.Range(0, BossLoot.Length)], Random.Range(2, 5)));
                // Remake (a player's report 2026-10-16: an outpost gave 1 t of something, less than a freighter): the pirates' stash,
                // freighter cargo rolls by tier (NpcTables.RollLoot) after the tier's own stacks, each kind once (a kind the tier
                // already gives is left out).
                int tierStacks = extra.Count;
                for (int rolls = OutpostStashRolls[t], tries = 0; rolls > 0 && tries < 30; tries++)
                {
                    var hold = NpcTables.RollLoot(db, true);
                    if (hold.Count == 0) continue;
                    rolls--;
                    foreach (var s in hold)
                    {
                        if (s.item == loot.item || extra.FindIndex(x => x.item == s.item) is int found && found >= 0 && found < tierStacks) continue;
                        GoF2Remake.World.CapitalShips.Add(extra, s.item, s.amount);
                    }
                }
                list.Add(new SpawnSpec
                {
                    group = NpcGroup.Outpost, race = Standing.Pirate, ship = -1, position = at,
                    fixedObject = "station_pirates", collisionId = 1002, hitRadius = 7500f,
                    hitpoints = Mathf.Max(1, (int)(KaamoClub.OutpostHull() * OutpostHullFactor[t])),
                    wreckPrefab = assets != null ? assets.outpostWreck : null, explosionScale = 8f, stationary = true, asleep = true,
                    nameText = 441, lootItem = loot.item, lootAmount = loot.amount, extraLoot = extra, alwaysEnemy = true,
                    pirateEvent = SpawnSpec.EventOutpost, eventTier = tier, sightingId = sightingId,
                });
                int baseGuards = (int)((Session.Difficulty - 0.5f) * 5f + 5f);
                int guards = Mathf.Max(1, baseGuards + OutpostGuardsAdd[t]);
                for (int g = 0; g < guards; g++)
                {
                    float S() => Random.value < 0.5f ? -1f : 1f;
                    list.Add(new SpawnSpec
                    {
                        group = NpcGroup.Guard, race = Standing.Pirate, ship = Fighter(), asleep = true, guard = true, pirateEvent = SpawnSpec.EventGuard,
                        position = at + new Vector3(S() * Random.Range(0, 20000) + 10000, S() * Random.Range(0, 20000) + 10000, S() * Random.Range(0, 20000) + 10000),
                    });
                }
                for (int i = 0; i < OutpostTurretCount[t] && i < OutpostTurrets.Length; i++)
                {
                    var turret = Turret(0, at + OutpostTurrets[i].pos, OutpostTurrets[i].rot);
                    turret.race = Standing.Pirate;
                    turret.alwaysEnemy = true;
                    turret.pirateEvent = SpawnSpec.EventTurret;
                    list.Add(turret);
                }
                return;
            }
            // The boss and its escorts, in front of the station like a raider group but 30 km further out (no raider waves:
            // group Special).
            var spawn = RaiderSpawn() + new Vector3(0f, 0f, 30000f);
            int hull = Mathf.Max(1, (int)((15 * rank + 1500 + 4 * 45) * Session.DifficultyFactor * BossHullFactor[t]));
            int gun = rank < 5 ? 23 : rank < 10 ? 15 : rank < 15 ? 26 : 21;   // Micro Gun MKII, H'nookk, Scram Cannon, Tyrfing
            var bossLoot = tier >= 4 ? DangerousBossLoot : BossLoot;
            list.Add(new SpawnSpec
            {
                group = NpcGroup.Special, race = Standing.Pirate, ship = Modding.ModTraffic.PickBoss(BossShips[Random.Range(0, BossShips.Length)]), position = spawn,
                hitpoints = hull, nameText = 1606, speed = tier == 5 ? 4f : 3.5f, gunItem = gun, gunFactor = BossGunFactor[t], alwaysEnemy = true,
                lootItem = bossLoot[Random.Range(0, bossLoot.Length)], lootAmount = Random.Range(BossLootMin[t], BossLootMax[t] + 1),
                pirateEvent = SpawnSpec.EventBoss, eventTier = tier, sightingId = sightingId,
                name = sighting != null ? sighting.name : null,   // a reported boss goes by its name (the lock plate, its call)
            });
            int escorts = Random.Range(BossEscortsMin[t], BossEscortsMax[t] + 1);
            for (int i = 0; i < escorts; i++)
                list.Add(new SpawnSpec { group = NpcGroup.Special, race = Standing.Pirate, ship = Fighter(), position = spawn + Jitter(), alwaysEnemy = true, pirateEvent = SpawnSpec.EventGuard });
            // Remake mods: a mod's change to the pirate boss (npcships.json "override": "pirate_boss"): its hull, name, strength,
            // escorts, loot; a reported boss keeps its name.
            if (Modding.ModNpcShips.Changed(Modding.ModNpcShips.PirateBoss) is Modding.NpcShipType bt)
            {
                int bi = list.FindIndex(x => x.pirateEvent == SpawnSpec.EventBoss && x.sightingId == sightingId && x.position == spawn);
                if (bi >= 0)
                {
                    if (bt.ship >= 0) list[bi].ship = bt.ship;
                    ApplyNpcType(db, list, bi, bt, false);
                    if (sighting != null) list[bi].name = sighting.name;
                }
            }
        }
    }
}
