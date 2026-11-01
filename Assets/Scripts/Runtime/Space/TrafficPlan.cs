// TrafficPlan.cs
// Which NPC ships an orbit gets (Level::createMission 0xbda70, empty-mission branch; Reference/research/
// npc_traffic_ai.md 2.2-2.4), as plain C# spawn specs in game units. Random per visit (the original reseeds its
// java.util.Random with time(NULL)), so UnityEngine.Random.
//   raiders    chance 90 / 65 / 35 / 10 % by security (one lower on Extreme); count rnd(4) (Extreme rnd(6) + 2, x2)
//              + rank / 4; 75 % pirates else the system's enemy race; one ship model for the group; around
//              (rnd +-50000, 0, 50000..100000) +-20000
//   jumpers    rnd(2) system-race fighters, created dead; the level relaunches them from the station (Traffic)
//   freighters rnd(5) at (+-(20000..80000), +-20000, +-80000), flying game +Z; none at Var Hastra (78)
//   local      security + rnd(2) + freighters / 4 system-race fighters around (+-10000, +-10000, 20000..50000) +-20000;
//              at least 7 at a station whose race the player attacked; 4 when nothing else spawned
//   special    stations 100, 101, 108, 10: nothing; 102-104: rnd(5) + 3 pirates; Loma (black market): rnd(4) + 6
//              pirates; systems 32 / 33 (pirate loot orbits): rnd(4) + 10 pirates
// Not yet: Wanted targets and their wingmen, the Terran battleship / carrier and Vossk battleship specials with turrets,
// pirate outposts, late-campaign Specters, freelance missions, the Void / alien orbit.

using System.Collections.Generic;
using GoF2Remake.Data;
using UnityEngine;

namespace GoF2Remake.Flight
{
    public enum NpcGroup { Local, Jumper, Freighter, Raider, Wingman, Escort, Special, Turret, Guard, Outpost }

    public class SpawnSpec
    {
        public NpcGroup group;
        public int race, ship;
        public bool freighter;
        public Vector3 position;   // game units
        public Route route;    // null = the default patrol
        public bool startsDead;    // jumpers
        // Campaign levels (Level::createCampaignMission, campaign_levels_a.md 1.2):
        public bool asleep;        // setToSleep: waits until the player comes within its detect range (NpcShip.UpdateSleep)
        public bool inactive;      // setInitActive(false): waits until the level script wakes it
        public bool alwaysEnemy, alwaysFriend;
        public bool alwaysNeutral; // remake (/spawn, the debug spawner): neither hostile nor friendly, whatever the standing
        public int eventTag;       // remake multiplayer: the event batch that spawned it (EventRunner counts them), 0 = none
        public int hitpoints = -1; // Player::setHitpoints / setMaxHitpoints override (-1 = the createShip formula)
        public bool noLoot;        // KIPlayer+0x4c / +0x48 = 0: no cargo, no crate
        public int nameText = -1;  // KIPlayer+0x18: the name the lock plate shows (text id)
        public string name;        // KIPlayer+0x18 as a literal (freelance: the agent's / the Wanted target's name)
        // Freelance levels (Level::createMission 0xbda70, freelance_missions.md 4.1):
        public bool stationary;    // KIPlayer+0x48 = 0: parked (Protection's mining ships), still a target
        public float speed = -1f;  // setSpeed (u/ms, -1 = the normal 2.0 with boosts): 3.0 for the Challenge rival / Wanted
        public int missionCrate = -1; // PlayerFighter::setMissionCrate: carries only this item; EMP-disabling it drops the crate
        // Static objects (Level::createStaticObject 0xcda54, PlayerFixedObject): the Kaamo siege's Pirate Outposts.
        public string fixedObject;    // assembly name: a target that never moves, has no gun, no engine and no loot
        public int collisionId = -1;  // Level::getBoundingVolume id (collision.json below 2000, else static_collisions.json)
        public float hitRadius = -1f; // Player+0x40, the bullet hit cube's half size (units)
        public GameObject wreckPrefab; // setWreckedMeshId: the wreck animation played on death, then the explosion
        // PlayerFixedObject::setDeadButSelectable 0x180248 (the Supernova wrecks with a hidden blueprint): a fixedObject
        // freighter ('ship' 13 / 15 of 'race') shown as its wreck held at the animation's end, invulnerable, still lockable.
        public bool deadButSelectable;
        public bool eventWreck;            // remake: an event's / quest's hackable wreck ("spawn ... wreck"): hacked once
        public float explosionScale = 1f;
        // Capital-ship turrets (PlayerTurret, npc_combat_specials.md 1): a static turret object.
        public string turretAssembly;  // turret_002_static (Terran) / turret_003_static (Vossk)
        public Vector3 rotation;       // game Euler (radians, Rx*Ry*Rz) of the root
        public float scale = 1f;       // setScaling (turrets 6)
        public AssemblyData assembly;  // remake: its look, once picked (Traffic.AssemblyOf: a mod's kit ship picks at random)
        public GameObject standIn;     // remake: its model, already out there on the approach (World.SystemApproach), taken over by NpcShip.Setup
        public List<Behaviour> standInAsleep;   // what SystemApproach.Sleep turned off on it, for Wake
        // Remake (generic turrets, Flight.TurretCore): any turret item on an NPC.
        public int turretItem = -1;    // a turret object built from this turret item: its model, aim and gun (-1 = turretAssembly's)
        public List<int> turretItems;  // turret items on the ship's turret mounts (null = its type's, Database.NpcTurrets; empty = none)
        public bool guard;             // PlayerFighter+0x12b: waking calls Level::pirateStationAction(true)
        public int lootItem = -1, lootAmount;   // a fixed crate (the pirate outposts, DAT_002543e0)
        public List<ItemStack> extraLoot;       // remake: more stacks in that crate (a pirate outpost by tier)
        // Campaign step 59's arms convoy (Level::createMission 0xbe742): its freighter, escorts and turrets (Traffic scripts it).
        public int convoyRole;         // SpawnSpec.ConvoyFreighter / ConvoyEscort / ConvoyTurret, 0 = none
        public const int ConvoyFreighter = 1, ConvoyEscort = 2, ConvoyTurret = 3;
        // Supernova story objects (PlayerFixedObject::setDockingType, KIPlayer::setSpacePoints, KIPlayer+0x70):
        public int dockingType;        // 0 none, 1 drop-off, 2 pick-up, 3 hackable (ObjectDocking)
        public int spacePoints = -1;   // the SpacePoints set (docks_hd.json)
        public bool radarHidden;       // KIPlayer+0x70: no marker, no lock
        // The Most Wanted criminal (KIPlayer+0x3e / +0x44) and its escort (wingmen_wanted.md 2.6).
        public int wantedIndex = -1;
        public int wantedEscortOf = -1;
        public int gunItem = -1;       // Level::assignGuns: the wanted's own weapon ...
        public float gunFactor = 1f;   // ... at x4
        public int secondaryItem = -1;     // a second gun slot (the wanted flying ships 45-48: G'liissk rockets) ...
        public float secondaryFactor = 1f; // ... at x4
        public int hiddenBlueprint = -1;   // a Supernova wreck's hidden blueprint (TrafficPlan.HiddenBlueprints slot)
        public int modBlueprint = -1;      // remake mods: a derelict's blueprint (the product item; blueprints.json "derelict")
        public int pirateEvent;            // remake: EventOutpost / EventBoss / EventTurret (TrafficPlan.AddPirateEvent), 0 = none
        public int eventTier = 3;          // ... its threat tier 1-5 (PirateReports.TierName): its strength and bounty
        public int sightingId = -1;        // ... a Pirate activity report's (PirateReports): its bounty, its name
        public const int EventOutpost = 1, EventBoss = 2, EventTurret = 3, EventGuard = 4;   // EventGuard: an outpost's guard, a boss's escort
        // Remake (Settings.CapitalShips, World.CapitalShips): the capital ship enhancements.
        public int capital;                // the host: CapitalBattleship / CapitalCarrier / CapitalVossk, 0 = none
        public bool capitalPart;           // one of its turrets or escorts (its turrets go with it: Traffic.DestroyCapitalTurrets)
        public SpawnSpec capitalHost;      // ... and whose
        public bool capitalEnhanced;       // built with the option on (hit boxes, CapitalShip on the host)
        public bool fleetBattle;           // a fleet battle's capital ship (TrafficPlan.AddFleetBattle) ...
        public SpawnSpec battleFoe;        // ... and the enemy capital ship it fights
        public bool turretHunter;          // an enhanced turret that goes for the enemy capital ship's turrets first
        public SpawnSpec routeAround;      // the capital ship its loop circles (moved along with it in a fleet battle)
        public float deathMs = -1f;        // a fixed object's dying time before the big explosion (-1 = its wreck animation or 20 s)
        public int empPoints = -1;         // the EMP pool (-1 = NpcTables.Emp)
        public bool modCapital;            // remake mods: a mod's capital ship or freighter (Modding.ModCapitalShips): its own
                                           // model and boxes, its turret items armed even parked, no wreck
        public const int CapitalBattleship = 1, CapitalCarrier = 2, CapitalVossk = 3;
    }

    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static partial class TrafficPlan
    {
        /// <summary>Station::stationHasHiddenBlueprint 0xb3ec8 (blueprints_mods.md 1.4 3): per slot the station
        /// (DAT_0025273c), the blueprint (DAT_002521f0), the wreck's race (DAT_00253754), position (DAT_00253768) and docking
        /// point set (DAT_002537a4: loadSpacePoints 11-14, one per race's freighter wreck).</summary>
        public static readonly (int station, int blueprint, int race, Vector3 position, int points)[] HiddenBlueprints =
        {
            (132, 226, 1, new Vector3(-20000, 30000, 80000), 14), (133, 221, 3, new Vector3(40000, -30000, 100000), 11),
            (134, 223, 2, new Vector3(-80000, 80000, -90000), 12), (129, 225, 0, new Vector3(40000, 20000, 140000), 13),
            (123, 227, 2, new Vector3(40000, 20000, 140000), 12),
        };

        static Vector3 Jitter() => new Vector3(Random.Range(0, 40000) - 20000, Random.Range(0, 40000) - 20000, Random.Range(0, 40000) - 20000);

        /// <summary>Remake: a hackable freighter wreck like the hidden blueprints' (Level::createMission's block below): race 0-3
        /// (-1 any: rolled), its race's freighter (the Vossk 13, else 15) held at its wreck pose, docking type 3 with that wreck's
        /// docking point set (11-14), at 'gamePosition'. The mods' derelicts and the events' "spawn ... wreck".</summary>
        public static SpawnSpec HackableWreck(int race, Vector3 gamePosition)
        {
            if (race < 0 || race > 3) race = Random.Range(0, 4);
            return new SpawnSpec
            {
                group = NpcGroup.Special, race = race, ship = race == 1 ? 13 : 15, position = gamePosition,
                fixedObject = NpcTables.FreighterAssembly(race), deadButSelectable = true, stationary = true, alwaysFriend = true,
                hitpoints = 9999999, noLoot = true, nameText = 3211, dockingType = 3, spacePoints = race == 1 ? 14 : race == 3 ? 11 : race == 2 ? 12 : 13,
                hitRadius = 4000f,
            };
        }

        /// <summary>'playerGame': the player's start (game units), where freelance escorts gather.</summary>
        /// <param name="wormholeGame">The orbit's visible wormhole (game units), where a Void raid comes from; null = none.</param>
        /// <summary>Level::createMission's Informer layout: an Informer mission at its target station while it isn't spoiled
        /// (status[0xf1] == 0; a spoiled one gets the normal traffic).</summary>
        public static bool InformerOrbit(int station)
        {
            var fm = Session.FreelanceMission;
            return fm != null && fm.type == MissionType.Informer && fm.target == station && !Session.InformerFailed;
        }

        public static List<SpawnSpec> Build(Database db, int station, SystemData system, Vector3 playerGame = default, Vector3? wormholeGame = null)
        {
            var list = new List<SpawnSpec>();
            // The alien orbit (npc_traffic_ai.md 2.1): 2 Void fighters, more with the player's level (2 at index 0x21 / 0x44),
            // anywhere in the orbit, always enemy; nothing else.
            if (station == Session.VoidOrbit)
            {
                int n = 2, more = Session.Rank / 2 - 1 + Random.Range(0, 2);
                if (more >= 2 && Session.CampaignMission != 0x21 && Session.CampaignMission != 0x44) n = more;
                for (int i = 0; i < n; i++)
                    list.Add(new SpawnSpec { group = NpcGroup.Raider, race = Standing.Void, ship = NpcTables.RandomFighter(Standing.Void), alwaysEnemy = true,
                                             position = new Vector3(Random.Range(0, 120000) - 60000, Random.Range(0, 80000) - 40000, Random.Range(0, 120000) - 60000) });
                // Remake mods: now and then one of the mods' Void warships (Modding.ModCapitalShips).
                int voidCapital = Modding.ModCapitalShips.PickHostile(Standing.Void, 0.35f);
                if (voidCapital >= 0)
                    list.Add(Modding.ModCapitalShips.SpawnHostile(voidCapital, Standing.Void,
                        new Vector3(Random.Range(0, 120000) - 60000, Random.Range(0, 40000) - 20000, Random.Range(0, 60000) + 120000)));
                return list;
            }
            if (system == null) return list;
            // Remake mods: a mod race's system keeps its mod race (ModRaces); "traffic": "mod" flies mods' ships only.
            int sysRace = Modding.ModRaces.Is(system.raceId) ? system.raceId : Mathf.Clamp(system.raceId, 0, 3);
            bool modOnly = Modding.ModWorld.ModTrafficOnly(system.index);
            var localRaces = Modding.ModWorld.Locals(system.index);
            var raiderRaces = Modding.ModWorld.Raiders(system.index);
            bool hardcore = Session.IsExtreme;
            int rank = Session.Rank;
            int cm = Session.CampaignMission;
            bool story = !Session.FreePlay;

            // Mido (system 15) before campaign 0x10: no raiders (except on Extreme), and rnd(2) instead of the security level
            // in the local fighter count (the tutorial's home system stays quiet).
            bool mido = story && system.index == 15 && cm < 0x10;
            int r100 = Random.Range(0, 100);
            int sec = system.securityLevel;
            int secEff = sec >= 1 && hardcore ? sec - 1 : sec;
            bool raidersOn = !(mido && !hardcore) && r100 < NpcTables.RaiderChance(secEff);
            var raiderSpawn = RaiderSpawn();
            int raiderRace = raiderRaces != null ? raiderRaces[Random.Range(0, raiderRaces.Count)]   // remake mods: the system's "raiders"
                : modOnly || Modding.ModRaces.Is(sysRace) ? Standing.EnemyRaceOf(sysRace)   // a mod race's: its hostile enemies
                : Random.Range(0, 100) < 75 ? Standing.Pirate : Standing.EnemyRaceOf(sysRace);
            // (the game's pirates where a mod system has mods' ships only: its raiders instead)
            int pirateRace = modOnly && raiderRace != Standing.Void ? raiderRace : Standing.Pirate;
            int raiders = raidersOn ? Random.Range(0, 4) : 0;
            if (raiders > 0)
            {
                if (hardcore) raiders = Random.Range(0, 6) + 2;
                raiders = (int)(raiders * Session.DifficultyFactor);
                raiders += rank / 4;
            }
            if (secEff == 3 && raidersOn && Session.Difficulty < 1f) raiders = Random.Range(0, 2) + 1;   // Easy / Normal

            int jumpers = 0, freighters = 0, x = 0;
            if (station != 78) { jumpers = Random.Range(0, 2); freighters = Random.Range(0, 5); x = Random.Range(0, 2); }
            int local = (mido ? Random.Range(0, 2) : secEff) + x + freighters / 4;
            // The "big battle" (npc_traffic_ai.md 2.2): raiders on, campaign > 0x1f, 8 %: 9 raiders against 9 locals.
            if (raidersOn && cm > 0x1f && Random.Range(0, 100) < 8) raiders = local = 9;
            // Freelance cargo attracts pirates: int(d / 10 * 5) escorts for Courier, Passenger and Ore Mining missions (types 0,
            // 0xb, 0xf; Ore Mining is only rolled in multiplayer). The station 100 / 101 / 108 / 10 rule below leaves them too.
            var fm = Session.FreelanceMission;
            int escorts = fm != null && (fm.type == MissionType.Courier || fm.type == MissionType.Passenger || fm.type == MissionType.OreMining)
                ? (int)(fm.difficulty / 10f * 5f) : 0;
            // A pirate-base system (npc_combat_specials.md 3.2): no raider group; instead 2 (Extreme 4-6) pirates near the player.
            bool baseSystem = PirateBases.SystemHasBase(db, system.index);
            int baseEscorts = 0;
            if (baseSystem) { raidersOn = false; raiders = 0; escorts = 0; baseEscorts = hardcore ? Random.Range(0, 3) + 4 : 2; }
            // Coming out of the Void, or at the station the Void attack (not at 42): 2-5 Void raiders from the wormhole and at
            // least 2 freighters for them to hunt (Level::createMission, npc_traffic_ai.md 2.2).
            bool voidRaid = story && cm != 42 && cm < 45 && (Session.ComingFromVoid || station == Session.VoidInvasionStation);
            if (voidRaid)
            {
                raidersOn = true;
                raiders = Random.Range(0, 4) + 2;
                raiderRace = Standing.Void;
                freighters = Mathf.Max(freighters, 2);
                if (wormholeGame.HasValue) raiderSpawn = wormholeGame.Value;
            }
            // Status::getWantedInCurrentOrbit: a Most Wanted criminal here caps the police at 2 (it and its escort come first).
            var wanted = WantedBoard.InOrbit(db, station);
            if (wanted != null) local = Mathf.Min(local, 2);
            // An Informer mission at its target station: only 7 local fighters (6 once the informer is dead), the first
            // named "Informer" (1663); no jumpers, freighters or raiders. Otherwise an attacked station has at least 7, and an
            // orbit with nothing at all gets 4 (before the special orbits below empty theirs again).
            bool informer = InformerOrbit(station);
            if (informer) { local = Session.InformerKilled ? 6 : 7; jumpers = freighters = raiders = escorts = baseEscorts = 0; raidersOn = false; }
            else
            {
                if (Session.AttackedStations.Contains(station)) local = Mathf.Max(local, 7);
                if (jumpers + local + freighters + raiders + escorts + baseEscorts == 0) local = 4;
            }
            // The special orbits: 102-104 only 3-7 pirates (escorts stay); 100 / 101 / 108 and Thynome (10) empty.
            if (station >= 102 && station <= 104) { local = jumpers = freighters = 0; raiders = Random.Range(0, 5) + 3; raidersOn = true; raiderRace = Standing.Pirate; }
            else if (station == 100 || station == 101 || station == 108 || station == 10) { local = jumpers = freighters = raiders = 0; raidersOn = false; }
            // Campaign 0x24 / 0x25 in S'kolptorr: no local fighters, no raiders; 0x2a / 0x2b: no raiders, no pirate escorts.
            if (story && (cm == 0x24 || cm == 0x25) && system.index == 5) local = raiders = 0;
            if (story && cm > 0x29 && cm < 0x2c) raiders = escorts = baseEscorts = 0;
            // Loma's black market (system 25): 6-9 pirates (Extreme rnd(3) + 2n), nothing else. The pirate loot orbits
            // (systems 32 / 33): 10-13 pirates (the same on Extreme) with double hull, speed 3.5, around the player.
            bool lootOrbit = system.index == 32 || system.index == 33;
            if (system.index == 25 || lootOrbit)
            {
                escorts = baseEscorts = local = jumpers = freighters = 0;
                raiders = Random.Range(0, 4) + (lootOrbit ? 10 : 6);
                if (hardcore) raiders = Random.Range(0, 3) + raiders * 2;
                raidersOn = true;
                raiderRace = Standing.Pirate;
            }
            // Late-campaign Specter raids (100 < campaign < 0x91): (campaign / 144 * 15 + 5) % (x2 Extreme), 2-4 (x2).
            int specters = 0;
            if (story && cm > 100 && cm < 0x91)
            {
                int p = (int)(cm / 144f * 15f + 5f) << (hardcore ? 1 : 0);
                if (Random.Range(0, 100) < p) specters = (Random.Range(0, 3) + 2) << (hardcore ? 1 : 0);
            }
            // The capital-ship specials (npc_combat_specials.md 2.1): the first freighter becomes the battleship / carrier
            // (Terran) or the Vossk battleship.
            bool terran = sysRace == 0 && freighters > 0 && Random.Range(0, 100) < 30;
            bool vossk = sysRace == 1 && freighters > 0 && Random.Range(0, 100) < 30 && cm > 0x8c;
            bool carrier = terran && Random.Range(0, 100) < 30 && cm > 0x67;
            // The supernova system (27) before campaign 0x9e (Status::inSupernovaSystem): nothing but the story's.
            if (story && system.index == 27 && cm < 0x9e) { local = jumpers = freighters = raiders = 0; raidersOn = terran = vossk = false; }
            // Remake (CapitalShips): now and then a fleet battle (a Terran and a Vossk capital ship with their wings) instead of
            // the single capital ship special.
            bool fleetBattle = FleetBattleHere(station, system, sysRace, baseSystem, story);
            if (fleetBattle) terran = vossk = false;
            // Remake mods (Modding.ModCapitalShips): a mod's capital ship of the system's race in the special's place (Terran /
            // Vossk at the "Mod ships in traffic" chance), or in a Nivelian / Midorian system, which has none, at the same 30 %.
            int modCapital = -1;
            if (!fleetBattle)
            {
                if (terran || vossk)
                {
                    modCapital = Modding.ModCapitalShips.PickCapital(sysRace, true);
                    if (modCapital >= 0) terran = vossk = carrier = false;
                }
                else if ((sysRace == 2 || sysRace == 3 || Modding.ModRaces.Is(sysRace)) && freighters > 0
                         && Modding.ModCapitalShips.Has(sysRace, Modding.ModCapitalShips.Battleship) && Random.Range(0, 100) < 30)
                    modCapital = Modding.ModCapitalShips.PickCapital(sysRace, false);
            }
            if (terran || vossk || modCapital >= 0) freighters--;
            // A pirate raid on a traveller (initStreamOutPosition): rnd(100) < rank + 20 (40 Extreme) moves the pirates'
            // point toward the arriving player (Route::setNewCoords(player position / 3), the divisor read in the iOS build's
            // Level::createMission; the remake takes a quarter of the way: half the way put up to 19 Extreme pirates on a player just arrived within
            // a minute, players' report 2026-10-16).
            if (raidersOn && raiderRace == Standing.Pirate && Session.ArrivedByTravel && !lootOrbit && Random.Range(0, 100) < rank + (hardcore ? 40 : 20))
                raiderSpawn = playerGame * AmbushShare;

            // Campaign step 59 (type 0xa3) at one of its target stations still to do (Status+0x90): the rival arms convoy
            // and the local fighters instead of the rest of the traffic.
            if (!Session.FreePlay && Session.StoryMission != null && Session.StoryMission.type == StoryType.TargetList && Session.StoryTargets.Contains(station))
            {
                AddConvoy(list, sysRace, local);
                return list;
            }

            // 1 local fighters around one point in front of the station
            var wpLocal = new Vector3(Random.Range(0, 20000) - 10000, Random.Range(0, 20000) - 10000, Random.Range(0, 30000) + 20000);
            if (wanted != null) AddWanted(list, wanted, wpLocal);
            for (int i = 0; i < local; i++)
            {
                int localRace = localRaces != null ? localRaces[Random.Range(0, localRaces.Count)] : sysRace;   // remake mods: "locals"
                list.Add(new SpawnSpec { group = NpcGroup.Local, race = localRace, ship = NpcTables.RandomFighter(localRace), position = wpLocal + Jitter(),
                                             nameText = informer && i == 0 && !Session.InformerKilled ? 1663 : -1 });
            }
            // 5 pirate escorts around the player
            for (int i = 0; i < escorts; i++)
                list.Add(new SpawnSpec { group = NpcGroup.Raider, race = pirateRace, ship = NpcTables.RandomFighter(pirateRace),
                                             position = playerGame + new Vector3(Random.Range(0, 160000) - 80000, Random.Range(0, 100000) - 50000, Random.Range(0, 160000) - 80000) });
            // 2 jumpers (dead until relaunched from the station), each with a far one-point route
            for (int i = 0; i < jumpers; i++)
            {
                var route = new Route(false);
                route.points.Add(new Vector3(Random.Range(0, 400000) - 200000, Random.Range(0, 200000) - 100000, Random.Range(0, 100000) + 50000));
                list.Add(new SpawnSpec { group = NpcGroup.Jumper, race = sysRace, ship = NpcTables.RandomFighter(sysRace), route = route, startsDead = true });
            }
            // 3 freighters
            for (int i = 0; i < freighters; i++)
            {
                var (ship, race) = NpcTables.RandomFreighter(sysRace);
                float s = Random.value < 0.5f ? -1f : 1f;
                var at = new Vector3(s * (Random.Range(0, 60000) - 80000), Random.Range(0, 40000) - 20000, Random.Range(0, 160000) - 80000);
                int modShip = Modding.ModCapitalShips.PickFreighter(race, -1);   // remake mods: now and then a mod's freighter
                if (modShip < 0 && modOnly) continue;   // (mods' ships only: no game freighter)
                list.Add(modShip >= 0 ? Modding.ModCapitalShips.Spawn(modShip, race, at)
                       : new SpawnSpec { group = NpcGroup.Freighter, race = race, ship = ship, freighter = true, position = at });
            }
            if (terran || vossk) AddCapitalShip(db, list, terran, carrier, station);
            if (modCapital >= 0) AddModCapital(list, modCapital, sysRace, station);
            if (fleetBattle) AddFleetBattle(db, list, station);
            // 4 raiders (the loot orbits': Player::setHitpoints(2 x max), speed 3.5, each at the player + (20000 +- rnd 50000,
            // 10000 +- rnd 50000, 20000 +- rnd 50000))
            int raidersFrom = list.Count;
            AddRaiders(list, raiders, raiderRace, raiderSpawn);
            // Remake mods: now and then a pirate / Void warship of the mods comes with the raiders, parked beyond their point.
            if (raiders > 0 && !lootOrbit && (raiderRace == Standing.Pirate || raiderRace == Standing.Void || Modding.ModRaces.Is(raiderRace)))
            {
                int hostileCapital = Modding.ModCapitalShips.PickHostile(raiderRace, 0.35f);
                if (hostileCapital >= 0)
                {
                    var away = raiderSpawn - playerGame;
                    away = away.sqrMagnitude > 1f ? away.normalized : Vector3.forward;
                    list.Add(Modding.ModCapitalShips.SpawnHostile(hostileCapital, raiderRace, raiderSpawn + away * 60000f));
                }
            }
            if (lootOrbit)
                for (int i = raidersFrom; i < list.Count; i++)
                {
                    float S() => Random.Range(0, 2) == 0 ? 1f : -1f;
                    list[i].hitpoints = 2 * NpcTables.Hull(0, list[i].ship);
                    list[i].speed = 3.5f;
                    list[i].position = playerGame + new Vector3(S() * Random.Range(0, 50000) + 20000, S() * Random.Range(0, 50000) + 10000, S() * Random.Range(0, 50000) + 20000);
                }
            // Pirate-base system: the escorts near the player; the base station's orbit: the outpost and its guards.
            for (int i = 0; i < baseEscorts; i++)
                list.Add(new SpawnSpec { group = NpcGroup.Escort, race = pirateRace, ship = NpcTables.RandomFighter(pirateRace),
                                             position = playerGame + new Vector3(Random.Range(0, 160000) - 80000, Random.Range(0, 100000) - 50000, Random.Range(0, 160000) - 80000) });
            if (PirateBases.StationHasBase(station)) AddPirateBase(list, station, hardcore);
            // 8 a Supernova wreck with a hidden blueprint: dockable and hackable (docking type 3) until it is found.
            // Level::createMission (hidden-blueprint block): createShip(race DAT_00253754[k], 1, k == 0 ? 0xd : 0xf), the
            // race's freighter (the Vossk one for slot 0), setDockingType(3), PlayerFixedObject::setDeadButSelectable (its
            // wreck mesh at the animation's end, HP 1, invulnerable), placed at DAT_00253768[k], loadSpacePoints(
            // DAT_002537a4[k]). A fixed object here (never moves, no engine or gun) showing the wreck (deadButSelectable).
            for (int k = 0; k < HiddenBlueprints.Length; k++)
            {
                if (HiddenBlueprints[k].station != station) continue;
                bool found = (Session.HiddenBlueprintsFound & (1 << k)) != 0;
                int race = HiddenBlueprints[k].race;
                list.Add(new SpawnSpec
                {
                    group = NpcGroup.Special, race = race, ship = k == 0 ? 13 : 15, position = HiddenBlueprints[k].position,
                    fixedObject = NpcTables.FreighterAssembly(race), deadButSelectable = true, stationary = true, alwaysFriend = true,
                    hitpoints = 9999999, noLoot = true, nameText = 3211, dockingType = found ? 0 : 3, spacePoints = HiddenBlueprints[k].points,
                    hiddenBlueprint = k, hitRadius = 4000f,
                });
            }
            // Remake mods: a mod blueprint's derelict (blueprints.json "derelict"), at most one per visit: a hackable freighter
            // wreck like the hidden blueprints' (its race's: the Vossk freighter 13, else 15, docking point sets 11-14),
            // somewhere out in front of the station, or in the orbits / at the spot its "stations" / "position" name; the hack
            // leaves the blueprint in a data crate (Traffic.OnHackWon).
            var derelict = Modding.ModBlueprints.RollDerelict(station);
            if (derelict != null)
            {
                var at = derelict.derelictPosition ?? new Vector3(Random.Range(-80000f, 80000f), Random.Range(-40000f, 40000f), Random.Range(60000f, 140000f));
                var wreck = HackableWreck(derelict.derelictRace, at);
                wreck.modBlueprint = derelict.product;
                list.Add(wreck);
            }
            // 7 Specters, always enemy: one point near the player, each at it + createShip's +-20000 jitter.
            if (specters > 0)
            {
                float S() => Random.value < 0.5f ? -1f : 1f;
                var sp = playerGame + new Vector3(S() * (Random.Range(0, 50000) + 20000), S() * (Random.Range(0, 50000) + 10000), S() * (Random.Range(0, 50000) + 20000));
                for (int i = 0; i < specters; i++)
                    list.Add(new SpawnSpec { group = NpcGroup.Special, race = Standing.Specter, ship = 44, alwaysEnemy = true, position = sp + Jitter() });
            }
            AddPirateEvent(list, db, station, system, secEff, story, cm, baseSystem, raiders > 0);
            return list;
        }

        // ---- remake: pirate events (GitHub #6) ----------------------------------------------------------------------

        static readonly int[] BossShips = { 60, 29, 32, 25 };       // Darkzov, Mantis, Wasp, Tyrion
        static readonly int[] BossLoot = { 113, 108, 107, 102, 101 };   // Implants, Vossk Organs, Organs, Rare Plants / Animals
        static readonly int[] DangerousBossLoot = { 113, 108 };          // Implants, Vossk Organs
        /// <summary>A Dangerous outpost's crate: one piece of equipment by the player's rank (below 10: Berger Converge IV,
        /// Tyrfing Blaster, MaxHeat o20, Mass Driver MD 10, Beamshield II; from 10: M6 A3 "Wolverine", D'iol, T'yol, L'ksaar,
        /// Fluxed Matter Shield, Rhoda Blackhole).</summary>
        static readonly int[] DangerousLootLow = { 8, 21, 30, 27, 53 };
        static readonly int[] DangerousLootHigh = { 11, 58, 59, 49, 54, 67 };
        /// <summary>Remake mods: the odds (1 in N) that a tier-5 outpost's piece of equipment is a mod's tech level 11+ gear.</summary>
        public const int TopTechOdds = 3;

        /// <summary>A Dangerous outpost's turrets (game units from its centre, the outpost unrotated): the four corners of the
        /// raised hub on top and two under the deck, turned over (measured on the station_pirates mesh).</summary>
        static readonly (Vector3 pos, Vector3 rot)[] OutpostTurrets =
        {
            (new Vector3(4000, 917, 4000), Vector3.zero), (new Vector3(-4000, 917, 4000), Vector3.zero),
            (new Vector3(4000, 917, -4000), Vector3.zero), (new Vector3(-4000, 917, -4000), Vector3.zero),
            (new Vector3(4000, -8548, 4000), new Vector3(0, 0, Mathf.PI)), (new Vector3(-4000, -8548, -4000), new Vector3(0, 0, Mathf.PI)),
        };

        /// <summary>Remake (option "Pirate outposts and bosses", on by default; players' suggestions): now and then (15 % in
        /// Dangerous systems, 7 % in Risky ones, never in Average or Secure ones) a free-flight orbit holds a sleeping pirate
        /// outpost with its guards far out (the pirate bases' outpost: radio 435-437 when a guard wakes, 438-440 when it falls)
        /// or a pirate boss (a Wanted-like hull and gun by the player's rank, speed 3.5, a crate of rare goods) with 2-4 escorts
        /// in front of the station, further out than the raiders; either pays a bounty (Traffic.PirateEventDone). Remake (players'
        /// suggestion, 2026-10): every event has a threat tier 1-5 (PirateReports.RollTier; AddPirateEventShips) instead of the
        /// Dangerous / not split below. In a
        /// Dangerous system the outpost has six turrets, three more guards and a piece of equipment in its crate, the boss two
        /// more escorts and more of the costlier goods, and the bounty is half as much again. Not below rank 2, at a station
        /// any campaign step sends the player to, in Mido before Keith leaves it (campaign 0x10), the special orbits (Loma, the
        /// empty ones, the supernova system, the pirates' own systems), a pirate base's system or the Kaamo siege.</summary>
        /// <summary>The arrival ambush's raider point: this share of the way from the station to the arriving player.</summary>
        public const float AmbushShare = 0.25f;

        // The tiers' numbers (index = tier - 1: Minor, Low, Moderate, High, Extremely dangerous).
        static readonly float[] OutpostHullFactor = { 0.4f, 0.7f, 1f, 1.4f, 2f };
        static readonly int[] OutpostGuardsAdd = { -3, -1, 0, 3, 6 };
        static readonly int[] OutpostTurretCount = { 0, 0, 2, 4, 6 };
        public static readonly int[] OutpostStashRolls = { 1, 2, 2, 3, 4 };
        static readonly float[] BossHullFactor = { 0.35f, 0.6f, 1f, 1.5f, 2.4f };
        static readonly float[] BossGunFactor = { 1f, 1.5f, 2f, 2.4f, 3f };
        static readonly int[] BossEscortsMin = { 0, 1, 2, 4, 6 }, BossEscortsMax = { 0, 1, 3, 5, 7 };
        static readonly int[] BossLootMin = { 1, 2, 3, 6, 10 }, BossLootMax = { 2, 4, 8, 12, 16 };

        // ---- step 59's arms convoy (Level::createMission 0xbe742, npc_combat_specials.md 4) --------------------------

        /// <summary>Table 0x253464 (per system race): the two battlestation turrets' offsets from the freighter and rotations.</summary>
        static readonly (Vector3 pos, Vector3 rot)[][] ConvoyTurrets =
        {
            new[] { (new Vector3(0, -1097.14f, -4178.23f), new Vector3(0, 0, Mathf.PI)), (new Vector3(0, 1158.09f, 1180.59f), Vector3.zero) },
            new[] { (new Vector3(0, -1096.98f, -2691.52f), new Vector3(0, 0, Mathf.PI)), (new Vector3(0, 1893.48f, 1068.07f), Vector3.zero) },
            new[] { (new Vector3(0, -484.719f, 1741.22f), new Vector3(0, 0, Mathf.PI)), (new Vector3(0, 1458.84f, -2304.28f), Vector3.zero) },
            new[] { (new Vector3(0, -516.08f, -3744.45f), new Vector3(0, 0, Mathf.PI)), (new Vector3(0, 515.766f, -3744.45f), Vector3.zero) },
        };

        // ---- capital ships (npc_combat_specials.md 2.2 - 2.4) ---------------------------------------------------

        /// <summary>Table 0x253604 / 0x253544 / 0x2536ac: the turrets' offsets from their host (game units) and rotations.</summary>
        public static readonly (Vector3 pos, Vector3 rot)[] BattleshipTurrets =
        {
            (new Vector3(5322, 1893, -15571), new Vector3(0, 0, -Mathf.PI / 2)), (new Vector3(2356, 1115, 7324), new Vector3(0, 0, -Mathf.PI / 2)),
            (new Vector3(0, -4261, 4876), new Vector3(0, 0, Mathf.PI)), (new Vector3(-5322, 1893, -15571), new Vector3(0, 0, Mathf.PI / 2)),
            (new Vector3(0, 5669, -13872), Vector3.zero), (new Vector3(-2356, 1115, 7324), new Vector3(0, 0, Mathf.PI / 2)),
            (new Vector3(0, 2855, 4161), Vector3.zero),
        };
        public static readonly (Vector3 pos, Vector3 rot)[] CarrierTurrets =
        {
            (new Vector3(-6726, 2458, -66), new Vector3(0, Mathf.PI / 2, 0)), (new Vector3(5824, 2458, 10243), new Vector3(0, -Mathf.PI / 2, 0)),
            (new Vector3(-6726, 2458, 2100), new Vector3(0, Mathf.PI / 2, 0)), (new Vector3(5824, 2458, 8437), new Vector3(0, -Mathf.PI / 2, 0)),
            (new Vector3(-5549, -2743, 11954), new Vector3(-Mathf.PI, Mathf.PI / 2, 0)), (new Vector3(-5549, -2743, -3711), new Vector3(-Mathf.PI, Mathf.PI / 2, 0)),
            (new Vector3(4981, -2743, 14659), new Vector3(Mathf.PI, -2.1817f, 0)), (new Vector3(0, 2458, -32811), Vector3.zero),
        };
        public static readonly Vector3[] VosskTurrets =
        {
            new Vector3(10283, -1123, 26039), new Vector3(-10171, -1083, 25907), new Vector3(-15624, -787, -7569),
            new Vector3(15624, -787, -7569), new Vector3(0, 4901, 3084),
        };

        // ---- remake: fleet battles (World.CapitalShips) -------------------------------------------------------------

        /// <summary>A fleet battle in this orbit: the option on, a Terran or Vossk system (the two at war), past step 103 (the
        /// carrier's era; the finished game's world too), 3 % per visit; not in the special orbits, a pirate base system, the
        /// Kaamo siege or a storyline's orbit while a story runs; not in a Secure system (a player's report 2026-10-16: a war
        /// broke out in the safe orbits).</summary>
        /// <summary>SystemData.securityLevel of a Secure system (text 405).</summary>
        public const int SecureSystem = 3;

        /// <summary>Remake: the main menu's backdrop camera (game units; MenuBackground sets it while its traffic is planned,
        /// null otherwise). A capital ship placed with the camera inside its hull filled the menu with clipped plating.</summary>
        public static Vector3? MenuCamera;
        /// <summary>How close a capital ship's boxes may come to the menu camera (Unity metres): close is fine, inside isn't.</summary>
        const float MenuCameraMargin = 60f;

    }
}
