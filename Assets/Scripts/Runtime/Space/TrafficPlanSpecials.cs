// TrafficPlanSpecials.cs
// TrafficPlan: the Most Wanted criminal, step 59's convoy, capital ships, fleet battles, pirate bases (part of the partial TrafficPlan; its fields are in TrafficPlan.cs).

using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Modding;
using UnityEngine;

namespace GoF2Remake.Flight
{
    public static partial class TrafficPlan
    {
        // ---- the Most Wanted criminal (Level::createMission 0xbda70, wingmen_wanted.md 2.6) ---------------------------
        /// <summary>[0] the criminal at the police's point (its race, pirates for races 4+), hull 15 * min(rank, 20) +
        /// its hitpoints + 4 * 45 (x2 Extreme), speed 4.5, its weapon x4, its loot and name; then its escort of the same race
        /// at half that hull.</summary>
        static void AddWanted(List<SpawnSpec> list, WantedData w, Vector3 wpLocal)
        {
            int race = w.race < 4 ? w.race : Standing.Pirate;
            int h = 15 * Mathf.Min(Session.Rank, 20) + w.hitpoints + 4 * 45;
            int hull = (int)(h * Session.DifficultyFactor);
            list.Add(new SpawnSpec
            {
                group = NpcGroup.Local, race = race, ship = w.ship, position = wpLocal + Jitter(), hitpoints = hull, name = w.name,
                lootItem = w.loot, lootAmount = w.lootAmount, wantedIndex = w.index, speed = 4.5f, gunItem = w.weapon, gunFactor = 4f,
                // Level::assignGuns: the wanted's flying ships 45-48 add a rocket gun (item 31 G'liissk, 4 x damage).
                secondaryItem = w.ship >= 45 && w.ship <= 48 ? 31 : -1, secondaryFactor = 4f,
            });
            for (int i = 0; i < w.numWingmen; i++)
                list.Add(new SpawnSpec { group = NpcGroup.Escort, race = race, ship = NpcTables.RandomFighter(race), position = wpLocal + Jitter(),
                                         hitpoints = hull / 2, wantedEscortOf = w.index });
        }
        /// <summary>[0] the freighter "Arms delivery" (1664) parked at C = (+-(80000..109999), -6000..-3001, 120000..169999),
        /// its crate one of items 0 / 1 / 2 / 36 / 22 / 23; [1-5] escorts looping on C; [6-7] battlestation turrets (1000 HP,
        /// scale 0.3); then the local fighters at the usual point in front of the station. The player's route is C
        /// (Traffic.ConvoyRoute).</summary>
        static void AddConvoy(List<SpawnSpec> list, int race, int local)
        {
            var wpLocal = new Vector3(Random.Range(0, 20000) - 10000, Random.Range(0, 20000) - 10000, Random.Range(0, 30000) + 20000);
            float sign = Random.Range(0, 2) == 0 ? 1f : -1f;
            var c = new Vector3(sign * (Random.Range(0, 30000) + 80000), Random.Range(0, 3000) - 6000, Random.Range(0, 50000) + 120000);
            int[] loot = { 0, 1, 2, 0x24, 0x16, 0x17 };
            list.Add(new SpawnSpec
            {
                group = NpcGroup.Special, race = race, ship = race == 1 ? 13 : 15, freighter = true, stationary = true, position = c,
                nameText = 1664, lootItem = loot[Random.Range(0, loot.Length)], lootAmount = 1, convoyRole = SpawnSpec.ConvoyFreighter,
            });
            var loop = new Route(true);
            loop.points.Add(c);
            for (int i = 0; i < 5; i++)
                list.Add(new SpawnSpec { group = NpcGroup.Escort, race = race, ship = NpcTables.RandomFighter(race), position = c + Jitter(),
                                         route = loop, convoyRole = SpawnSpec.ConvoyEscort });
            foreach (var t in ConvoyTurrets[race])
                list.Add(new SpawnSpec
                {
                    group = NpcGroup.Turret, race = race, ship = -1, position = c + t.pos, rotation = t.rot, scale = 0.3f, hitpoints = 1000,
                    turretAssembly = "v_station_battlestation_turret", noLoot = true, nameText = 1666, stationary = true,
                    convoyRole = SpawnSpec.ConvoyTurret,
                });
            for (int i = 0; i < local; i++)
                list.Add(new SpawnSpec { group = NpcGroup.Local, race = race, ship = NpcTables.RandomFighter(race), position = wpLocal + Jitter() });
        }
        static void AddCapitalShip(Database db, List<SpawnSpec> list, bool terran, bool carrier, int station)
        {
            int kind = !terran ? SpawnSpec.CapitalVossk : carrier ? SpawnSpec.CapitalCarrier : SpawnSpec.CapitalBattleship;
            // The original's boxes in createMission: the battleship's, the carrier's and the Vossk battleship's.
            System.Func<Vector3> roll = kind == SpawnSpec.CapitalBattleship
                ? () => new Vector3(Random.Range(0, 80000) - 40000, Random.Range(0, 10000) - 5000, Random.Range(0, 80000) + 40000)
                : kind == SpawnSpec.CapitalCarrier
                ? () => new Vector3(Random.Range(0, 80000) - 40000, Random.Range(0, 40000) - 20000, Random.Range(0, 80000) + 100000)
                : () => new Vector3(Random.Range(0, 80000) - 40000, Random.Range(0, 40000) - 20000, Random.Range(0, 80000) + 60000);
            // Remake option: escorts, stronger turrets, the killable carrier / Vossk battleship (World.CapitalShips).
            AddCapitalShipAt(db, list, kind, ClearOfStation(station, CapitalVolumes(kind), roll), Settings.CapitalShips);
        }
        /// <summary>Remake mods: a mod's capital ship (Modding.ModCapitalShips) parked in the Terran battleship's box, clear of
        /// the station.</summary>
        static void AddModCapital(List<SpawnSpec> list, int ship, int race, int station)
        {
            var at = ClearOfStation(station, CollisionVolume.ForFreighter(ship, race),
                () => new Vector3(Random.Range(0, 80000) - 40000, Random.Range(0, 10000) - 5000, Random.Range(0, 80000) + 80000));
            list.Add(Modding.ModCapitalShips.Spawn(ship, race, at));
        }
        static List<CollisionVolume> CapitalVolumes(int kind) => kind == SpawnSpec.CapitalBattleship ? CollisionVolume.ForFreighter(14, 0)
            : CollisionVolume.ForStaticObject(kind == SpawnSpec.CapitalCarrier ? 2005 : 2006);
        /// <summary>A capital ship of 'kind' (SpawnSpec.Capital*) at 'host' (game units) with its turrets, enhanced (escorts,
        /// killable: World.CapitalShips) or as the original's; returns the host's index in the list. Remake mods: a mod's
        /// change to it (npcships.json "override": "battleship" / "carrier" / "vossk_battleship", ApplyNpcType): its hull
        /// ("ship": a mod hull in its place, its turrets then its own), hardpoints, escorts, loot, dock, launches.</summary>
        public static int AddCapitalShipAt(Database db, List<SpawnSpec> list, int kind, Vector3 host, bool enhanced)
        {
            var type = ModNpcShips.Changed(NpcKeyOf(kind));
            int first = list.Count;
            if (type != null && type.ship >= 0)
            {
                var s = NpcHost(db, type, kind == SpawnSpec.CapitalVossk ? 1 : 0, host);
                s.capital = kind;
                list.Add(s);
            }
            else AddCapitalHull(list, kind, host, type?.hardpoints == null);
            if (enhanced) GoF2Remake.World.CapitalShips.Enhance(db, list, first);
            if (type != null)
            {
                ApplyNpcType(db, list, first, type, false);
                // A hull of the mod's own has none of the game hull's pads: only its own (docking.pads).
                if (type.ship >= 0 && list[first].spacePoints >= 0 && list[first].spacePoints < SpacePoints.NpcPadBase)
                { list[first].dockingType = 0; list[first].spacePoints = -1; }
            }
            return first;
        }
        /// <summary>The game's capital ship hull of 'kind' at 'host' and (with 'turrets') its turrets, as the original builds them.</summary>
        static void AddCapitalHull(List<SpawnSpec> list, int kind, Vector3 host, bool turrets)
        {
            int first = list.Count;
            if (kind == SpawnSpec.CapitalBattleship)
            {
                list.Add(new SpawnSpec { group = NpcGroup.Special, race = 0, ship = 14, freighter = true, stationary = true, position = host,
                                         capital = SpawnSpec.CapitalBattleship });
                if (turrets) foreach (var t in BattleshipTurrets) list.Add(Turret(0, host + t.pos, t.rot));
            }
            else if (kind == SpawnSpec.CapitalCarrier)
            {
                list.Add(new SpawnSpec { group = NpcGroup.Special, race = 0, ship = -1, position = host, fixedObject = "sn_carrier_terran_1",
                                         collisionId = 2005, hitRadius = 0f, hitpoints = 9999999, noLoot = true, stationary = true,
                                         capital = SpawnSpec.CapitalCarrier });
                if (turrets) foreach (var t in CarrierTurrets) list.Add(Turret(0, host + t.pos, t.rot));
            }
            else
            {
                list.Add(new SpawnSpec { group = NpcGroup.Special, race = 1, ship = -1, position = host, fixedObject = "sn_battleship_vossk",
                                         collisionId = 2006, hitRadius = 0f, hitpoints = 9999999, noLoot = true, stationary = true, nameText = 1667,
                                         capital = SpawnSpec.CapitalVossk });
                if (turrets) foreach (var t in VosskTurrets) list.Add(Turret(1, host + t, Vector3.zero));
            }
            for (int i = first + 1; i < list.Count; i++) { list[i].capitalPart = true; list[i].capitalHost = list[first]; }
        }
        static bool FleetBattleHere(int station, SystemData system, int sysRace, bool baseSystem, bool story)
        {
            if (!Settings.CapitalShips || (sysRace != 0 && sysRace != 1) || Session.CampaignMission <= 0x67) return false;
            if (system.securityLevel >= SecureSystem) return false;
            if (Session.FreePlay && !Session.CompletedWorld) return false;
            if (baseSystem || KaamoClub.SiegeAt(station) || (story && InStoryline(station))) return false;
            if (station == 100 || station == 101 || station == 108 || station == 10 || (station >= 102 && station <= 104)) return false;
            if (system.index == 25 || system.index == 27 || system.index == 32 || system.index == 33) return false;
            return Random.Range(0, 100) < GoF2Remake.World.CapitalShips.FleetBattleChance;
        }
        /// <summary>A fleet battle (World.CapitalShips): a Terran carrier or battleship and the Vossk battleship side by side,
        /// both facing game +Z, BattleSeparationUnits apart across 'centre' (rolled 110 000-150 000 units in front of the
        /// station, clear of it), each with its turrets and escorts and a wing that loops round the enemy capital ship.</summary>
        public static void AddFleetBattle(Database db, List<SpawnSpec> list, int station, Vector3? centre = null)
        {
            int terranKind = Random.value < 0.5f ? SpawnSpec.CapitalCarrier : SpawnSpec.CapitalBattleship;
            float side = Random.value < 0.5f ? -1f : 1f;
            var half = new Vector3(GoF2Remake.World.CapitalShips.BattleSeparationUnits / 2f, 0f, 0f) * side;
            Vector3 c = centre ?? Vector3.zero;
            if (centre == null)
            {
                var hull = CollisionVolume.ForStation(GoF2Remake.World.OrbitBuilder.StationLook(station), false);
                for (int tries = 0; tries < 40; tries++)
                {
                    c = new Vector3(Random.Range(0, 60000) - 30000, Random.Range(0, 16000) - 8000, Random.Range(0, 40000) + 110000);
                    if (!Touches(hull, CapitalVolumes(terranKind), c - half) && !Touches(hull, CapitalVolumes(SpawnSpec.CapitalVossk), c + half)
                        && !AtMenuCamera(CapitalVolumes(terranKind), c - half) && !AtMenuCamera(CapitalVolumes(SpawnSpec.CapitalVossk), c + half)) break;
                }
            }
            var terran = list[AddCapitalShipAt(db, list, terranKind, c - half, true)];
            var vossk = list[AddCapitalShipAt(db, list, SpawnSpec.CapitalVossk, c + half, true)];
            terran.fleetBattle = vossk.fleetBattle = true;
            terran.battleFoe = vossk;
            vossk.battleFoe = terran;
            AddBattleWing(list, terran, vossk);
            AddBattleWing(list, vossk, terran);
        }
        /// <summary>4 + rank / 5 fighters of the capital ship's race beside it, looping round the enemy capital ship (they meet
        /// the enemy fleet there).</summary>
        static void AddBattleWing(List<SpawnSpec> list, SpawnSpec own, SpawnSpec foe)
        {
            var loop = new Route(true);
            loop.points.Add(foe.position + new Vector3(0, 8000, -45000));
            loop.points.Add(foe.position + new Vector3(30000, 4000, 0));
            loop.points.Add(foe.position + new Vector3(0, 8000, 45000));
            loop.points.Add(foe.position + new Vector3(-30000, 4000, 0));
            int n = 4 + Mathf.Min(Session.Rank, 20) / 5;
            for (int i = 0; i < n; i++)
                list.Add(new SpawnSpec
                {
                    group = NpcGroup.Escort, race = own.race, ship = NpcTables.RandomFighter(own.race), route = loop,
                    position = own.position + new Vector3(Random.Range(-15000, 15000), Random.Range(-6000, 6000), Random.Range(-20000, 20000)),
                    capitalPart = true, capitalHost = own, capitalEnhanced = true, routeAround = foe,
                });
        }
        /// <summary>Remake (#45): the capital ship's box starts 2 km in front of the station, inside the bigger stations (Valadon's
        /// 3 km hull had the battleship through it; the original rolls it anywhere in the box): the point is rolled again
        /// while the ship's volumes (game units relative to it) would touch the station's, at most 40 times.</summary>
        static Vector3 ClearOfStation(int station, List<CollisionVolume> ship, System.Func<Vector3> roll)
        {
            var hull = CollisionVolume.ForStation(GoF2Remake.World.OrbitBuilder.StationLook(station), false);
            Vector3 p = roll();
            for (int i = 0; i < 40 && (Touches(hull, ship, p) || AtMenuCamera(ship, p)); i++) p = roll();
            return p;
        }
        /// <summary>The ship's volumes at 'gamePos' reach within MenuCameraMargin of the menu camera.</summary>
        static bool AtMenuCamera(List<CollisionVolume> ship, Vector3 gamePos)
        {
            if (MenuCamera == null) return false;
            var cam = MenuCamera.Value;
            var offset = new Vector3(gamePos.x - cam.x, gamePos.y - cam.y, -(gamePos.z - cam.z)) * 0.05f;   // the ship from the camera, Unity metres
            foreach (var b in ship)
            {
                var bh = (b.sphere ? Vector3.one * b.radius : b.half) + Vector3.one * MenuCameraMargin;
                var d = b.centre + offset;
                if (Mathf.Abs(d.x) < bh.x && Mathf.Abs(d.y) < bh.y && Mathf.Abs(d.z) < bh.z) return true;
            }
            return false;
        }
        static bool Touches(List<CollisionVolume> station, List<CollisionVolume> ship, Vector3 gamePos)
        {
            var offset = new Vector3(gamePos.x, gamePos.y, -gamePos.z) * 0.05f;   // Unity metres, the station at the origin
            const float Margin = 150f;
            foreach (var a in station)
            {
                var ah = a.sphere ? Vector3.one * a.radius : a.half;
                foreach (var b in ship)
                {
                    var bh = (b.sphere ? Vector3.one * b.radius : b.half) + Vector3.one * Margin;
                    var d = b.centre + offset - a.centre;
                    if (Mathf.Abs(d.x) < ah.x + bh.x && Mathf.Abs(d.y) < ah.y + bh.y && Mathf.Abs(d.z) < ah.z + bh.z) return true;
                }
            }
            return false;
        }
        /// <summary>Level::createStaticObject 0x1a74 / 0x1a76: 1000 HP, scale 6, name 1666 "Turret", no loot.</summary>
        static SpawnSpec Turret(int race, Vector3 pos, Vector3 rot) => new SpawnSpec
        {
            group = NpcGroup.Turret, race = race, ship = -1, position = pos, rotation = rot, scale = 6f, hitpoints = 1000,
            turretAssembly = race == 1 ? "turret_003_static" : "turret_002_static", noLoot = true, nameText = 1666, stationary = true,
        };
        // ---- pirate bases (npc_combat_specials.md 3) --------------------------------------------------------
        static void AddPirateBase(List<SpawnSpec> list, int station, bool hardcore)
        {
            int i = PirateBases.IndexOf(station);
            var table = PirateBases.OutpostPositions[i];
            var assets = CombatAssets.Load();
            var loot = PirateBases.Loot[i];
            list.Add(new SpawnSpec
            {
                group = NpcGroup.Outpost, race = Standing.Pirate, ship = -1, position = table + Jitter() / 2f,   // +-10000
                fixedObject = "station_pirates", collisionId = 1002, hitRadius = 7500f, hitpoints = KaamoClub.OutpostHull(),
                wreckPrefab = assets != null ? assets.outpostWreck : null, explosionScale = 8f, stationary = true, asleep = true,
                nameText = 441, lootItem = loot.item, lootAmount = loot.amount, alwaysEnemy = true,
            });
            int guards = (int)((Session.Difficulty - 0.5f) * 5f + 5f);   // 2 / 5 / 7 / 10
            for (int g = 0; g < guards; g++)
            {
                float S() => Random.value < 0.5f ? -1f : 1f;
                list.Add(new SpawnSpec
                {
                    group = NpcGroup.Guard, race = Standing.Pirate, ship = NpcTables.RandomFighter(Standing.Pirate), asleep = true, guard = true,
                    position = table + new Vector3(S() * Random.Range(0, 20000) + 10000, S() * Random.Range(0, 20000) + 10000, S() * Random.Range(0, 20000) + 10000),
                });
            }
        }
        static Vector3 RaiderSpawn() => new Vector3(Random.Range(0, 100000) - 50000, 0f, Random.Range(0, 50000) + 50000);
        static void AddRaiders(List<SpawnSpec> list, int n, int race, Vector3 spawn)
        {
            int ship = NpcTables.RandomFighter(race);   // one model for the whole group
            for (int i = 0; i < n; i++)
                list.Add(new SpawnSpec { group = NpcGroup.Raider, race = race, ship = ship, position = spawn + Jitter() });
        }
    }
}
