// Traffic.cs
// The orbit's NPC ships (Reference/research/npc_traffic_ai.md 2, 4, 7, 8), created by SpaceLevel:
//   Level::createMission / createShip   spawns TrafficPlan's ships (NpcShip)
//   Level::connectPlayers 0xcc330       each NPC's enemy list: the player first, then every ship of another race (same-race
//                                       ships never target or hit each other)
//   Level::updateOrbit 0xd50b0          every 10 s one gone jumper relaunches from the station; every 45 s gone local
//                                       fighters relaunch from the station and a destroyed raider group comes back 60-120 km
//                                       from the player (at most 2 waves; security 0, or 3 ships in security 1)
//   Level::friendTurnedEnemy / alarmAllFriends   radio 0 "Hold your fire!" (426-428) once, radio 1 (429-431) once and
//                                       every ship of that race turns hostile; the station remembers it (next visit: at
//                                       least 7 local fighters, all hostile at once, radio 2 "He's back!" 445-447)
//   Level::enemyDied / Standing::applyKill   a player kill: kills +1 (hostile ships), pirate kills, standing -5 with the
//                                       race (a pirate: +1 toward the system race); not in the black-market system
//   Radar::draw hostile counter         hostile living ships (only with a scanner): blocks fast-forward and picks the music:
//                                       0 the system's space track, 1-2 / 3-4 / 5+ Space_Battle_Low / Medium / Full
// Radio messages are shown as HUD messages (the original's radio window with portraits isn't built yet).

using System;
using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.Visuals;
using UnityEngine;

namespace GoF2Remake.World
{
    public partial class Traffic : MonoBehaviour
    {
        const float M = 0.05f;

        public readonly List<NpcShip> Ships = new List<NpcShip>();
        public Target Player { get; private set; }
        public int SystemRace { get; private set; }
        public int AttackRace { get; private set; }
        public int Security { get; private set; }
        public int StationIndex { get; private set; }
        public Vector3 StationPosition { get; private set; }
        /// <summary>Radar+0x1b8: hostile living ships (0 without a scanner).</summary>
        public int HostileCount { get; private set; }
        public event Action<string> Message;

        Database db;
        bool hasScanner, radioTurned, radioAlarm, blackMarket;
        float respawnMs, jumperMs;
        int raiderWaves, raidersRespawned;
        AudioSource music;
        int musicCategory = -1, pendingCategory = -1;
        float fade = 1f;
        CombatAssets assets;

        /// <summary>'storyOrbit': the orbit is built around a campaign mission (Level::init calls createCampaignMission
        /// instead of createMission), so no normal traffic; the campaign level spawns its ships with SpawnShip. 'oneAFrame':
        /// remake, the ships come in over the next frames (Update) instead of in this one, the enemies only once the warp has
        /// ended (ReleaseEnemies: an arrival at speed, SystemApproach).</summary>
        public void Setup(Database database, OrbitLayout layout, Target player, GameObject station, bool storyOrbit = false, Wormhole wormhole = null, bool oneAFrame = false)
        {
            db = database;
            Wormhole = wormhole;
            // Level::update: updateAlienAttackers in the alien orbit and at the station the Void attack; their music.
            VoidAttack = layout.alienOrbit || (!Session.FreePlay && Session.CampaignMission < 45 && layout.stationIndex == Session.VoidInvasionStation);
            Player = player;
            assets = CombatAssets.Load();
            StationIndex = layout.stationIndex;
            systemIndex = layout.systemIndex;
            this.storyOrbit = storyOrbit;
            var sys = db.Systems.Find(s => s.index == layout.systemIndex);
            // (a mod race's system keeps its mod race: its locals, kills and alarms; the tables use its look, Standing.Look)
            SystemRace = Modding.ModRaces.Is(sys?.raceId ?? 0) ? sys.raceId : Mathf.Clamp(sys?.raceId ?? 0, 0, 3);
            AttackRace = Standing.EnemyRaceOf(SystemRace);
            Security = sys?.securityLevel ?? 0;
            blackMarket = layout.systemIndex == 25;
            hasScanner = Shop.FirstMounted(db, 17) != null;
            // MGame::OnInitialize: the elite medal counters that only count within one level.
            if (hasScanner) Session.BlindKills = 0;
            Session.EmpDisabledNow = Session.RocketAsteroids = 0;
            if (station != null) StationPosition = station.transform.position;

            fxRoot = new GameObject("NPC weapon fx").transform;
            fxRoot.SetParent(transform, false);
            IsStoryOrbit = storyOrbit;
            var flownPast = SystemApproach.TakeShips();
            bool wormholeHere = wormhole != null && wormhole.Visible;
            // The approach planned without the orbit's wormhole, where a Void raid comes from (TrafficPlan): planned again here.
            if (!storyOrbit && flownPast != null && oneAFrame && !wormholeHere)
            {
                // The plan the approach made: the ones already out there first, in their place.
                foreach (var spec in flownPast.FindAll(s => s.standIn != null)) waiting.Enqueue(spec);
                foreach (var spec in flownPast.FindAll(s => s.standIn == null)) waiting.Enqueue(spec);
            }
            else if (!storyOrbit)
            {
                var plan = TrafficPlan.Build(db, StationIndex, sys, player != null ? new Vector3(player.transform.position.x, player.transform.position.y, -player.transform.position.z) / 0.05f : Vector3.zero,
                                             wormholeHere ? wormhole.GamePosition : (Vector3?)null);
                foreach (var spec in plan) waiting.Enqueue(spec);
            }
            if (!oneAFrame || waiting.Count == 0) SystemApproach.DropShips();
            music = gameObject.AddComponent<AudioSource>();
            music.loop = true;
            music.playOnAwake = false;
            music.spatialBlend = 0f;
            orbit = layout;
            if (oneAFrame && waiting.Count > 0) { holdEnemies = true; return; }
            while (waiting.Count > 0) Create(waiting.Dequeue());
            ShipsPlaced();
        }

        OrbitLayout orbit;
        readonly Queue<SpawnSpec> waiting = new Queue<SpawnSpec>();
        readonly List<SpawnSpec> heldEnemies = new List<SpawnSpec>();
        readonly List<NpcShip> offRadar = new List<NpcShip>();
        readonly ArrivalScale arrivalScale = new ArrivalScale();

        /// <summary>No ship fires while the warp holds (SpaceLevel.WarpHolds).</summary>
        public bool HoldFire { get; set; }

        /// <summary>The ships here growing with the arrival, as its station does (Visuals.WarpSky).</summary>
        public void SetArrivalSize(float size) => arrivalScale.Apply(size);
        bool holdEnemies;

        /// <summary>The warp of an arrival has ended: the enemies held back come in, and the ships already here show on the
        /// radar (a whole orbit's blips at the load gave it away).</summary>
        public void ReleaseEnemies()
        {
            if (!holdEnemies) return;
            holdEnemies = false;
            foreach (var ship in offRadar) if (ship != null) ship.RadarHidden = false;
            offRadar.Clear();
            arrivalScale.Restore();
            foreach (var spec in heldEnemies) waiting.Enqueue(spec);
            heldEnemies.Clear();
            if (waiting.Count == 0) ShipsPlaced();
        }

        /// <summary>What the approach builds ahead of the arrival (SystemApproach), by what a ship is and never by where it came
        /// from, so a mod's ships are in it whatever they are: anything friendly in the orbit from the start. Not what isn't there
        /// yet (a jumper, one asleep or held by a script), what the arrival alone can decide (a Most Wanted criminal, an event), a
        /// turret or a part on a hull, a fleet battle's. What this leaves out, or whose model isn't ready, grows in instead
        /// (ArrivalScale.Add), and enemies wait for the warp's end.</summary>
        public static bool Carried(SpawnSpec spec) => !Enemy(spec) && !spec.startsDead && !spec.inactive && !spec.asleep && spec.wantedIndex < 0 && spec.pirateEvent == 0
            && spec.turretAssembly == null && spec.capitalHost == null && spec.battleFoe == null;

        /// <summary>A ship that will be hostile as it comes in (NpcShip's targeting, by the race and flags it starts with).</summary>
        public static bool Enemy(SpawnSpec spec) => !spec.alwaysFriend && !spec.alwaysNeutral && (spec.alwaysEnemy || spec.race == Standing.Pirate
            || spec.race == Standing.Void || spec.race == Standing.Specter || Standing.IsEnemy(spec.race));

        /// <summary>What goes by the orbit's ships, once they are all there.</summary>
        void ShipsPlaced()
        {
            AddCampaignStatics(orbit);
            ConnectPlayers();
            wantedShip = Ships.Find(s => s.Spec.wantedIndex >= 0);
            convoyFreighter = Ships.Find(s => s.Spec.convoyRole == SpawnSpec.ConvoyFreighter);
            if (convoyFreighter != null)
            {
                ConvoyRoute = new Route(false);
                ConvoyRoute.points.Add(convoyFreighter.Spec.position);
            }
            Debug.Log($"Traffic: {Ships.Count} ships ({CountGroup(NpcGroup.Local)} local, {CountGroup(NpcGroup.Jumper)} jumpers, " +
                      $"{CountGroup(NpcGroup.Freighter)} freighters, {CountGroup(NpcGroup.Raider)} raiders)");

            // Level::initParticleSystems: the red static fog around a pirate base's outpost.
            foreach (var s in Ships) if (s.Spec.group == NpcGroup.Outpost) SpawnRedFog(s.transform.position);

            SetupLoma();
            SetupVoidChatter();
            // Level+0x18a: back at a station whose race the player attacked. Not in an Informer orbit: its branch of
            // Level::createMission skips the hasAttackedFriends block (the locals, the spy among them, start calm).
            if (Session.AttackedStations.Contains(StationIndex) && !TrafficPlan.InformerOrbit(StationIndex))
            {
                AlarmAllFriends(SystemRace, false);
                Radio(445, 447, SystemRace);
            }
        }

        int CountGroup(NpcGroup g) => Ships.FindAll(s => s.Spec.group == g).Count;

        Transform fxRoot;
        public bool IsStoryOrbit { get; private set; }
        /// <summary>The orbit's wormhole (landmark 3), null = none.</summary>
        public Wormhole Wormhole { get; private set; }
        /// <summary>The alien orbit or a station under Void attack: dead Void ships come back, Void music.</summary>
        public bool VoidAttack { get; private set; }
        float alienMs;

        /// <summary>Multiplayer (NetOrbit): a ship another player ran in this orbit, taken over where it is (spec.position)
        /// with its heading and hull.</summary>
        public NpcShip Adopt(SpawnSpec spec, Quaternion rotation, float hullFraction)
        {
            var ship = Create(spec);
            ship.transform.rotation = rotation;
            var hp = ship.Hp;
            if (hp != null && hp.maxHull > 0)
            {
                hp.hull = Mathf.Clamp(Mathf.RoundToInt(hp.maxHull * hullFraction), 1, hp.maxHull);
                ship.Target.hp = hp.hull;
            }
            return ship;
        }

        /// <summary>A spec's look, picked once: the approach's stand-in and the ship that takes it over are the same one.</summary>
        public static AssemblyData AssemblyOf(Database db, SpawnSpec spec)
        {
            if (spec.assembly != null) return spec.assembly;
            return spec.assembly = spec.turretAssembly != null ? db.AssemblyByName(spec.turretAssembly)
                      : spec.ship == 14 ? db.AssemblyByName("battleship_terran")
                      : spec.fixedObject != null ? db.AssemblyByName(spec.fixedObject)
                      // Ship 13 is the Vossk freighter whatever the race (Globals::getShipGroup; its boxes too): step 40's
                      // Errkt flies it as race 0 until it appears (createShip(0, 1, 0xd)) and showed the Terran model.
                      : spec.freighter && !spec.modCapital ? db.AssemblyByName(NpcTables.FreighterAssembly(spec.ship == 13 ? 1 : spec.race))
                                       : Modding.ModTraffic.Assembly(spec.ship) ?? ShipAssembly(db, spec.ship, spec.race);   // remake mods: a kit ship's random build
        }

        NpcShip Create(SpawnSpec spec)
        {
            var entry = AssemblyOf(db, spec);
            var prefab = AssembledObject.LoadPrefab(entry);
            // Remake: a turret object built from a turret item shows the item's turret (a mod's own model too).
            var itemTurret = spec.turretAssembly != null && spec.turretItem >= 0 ? WeaponFx.Load(spec.turretItem)?.turretMounted : null;
            if (itemTurret != null) prefab = itemTurret;
            var go = new GameObject($"NPC {spec.group} {spec.race}/{spec.ship}");
            go.transform.SetParent(transform, false);
            var ship = go.AddComponent<NpcShip>();
            Ships.Add(ship);
            // Multiplayer: the clients' proxies load the same prefab (an item's turret by its item, NetProxy).
            ship.ModelPath = itemTurret != null ? NpcShip.TurretModelPrefix + spec.turretItem : AssembledObject.PrefabPath(entry);
            ship.Setup(this, db, spec, prefab, fxRoot);
            // Remake (generic turrets): turret items on its turret mount (SpawnSpec.turretItems, a mod's "npcTurrets") take the
            // fighter turret's place, which sits on that mount.
            if (spec.turretAssembly == null && spec.fixedObject == null && !spec.freighter && FighterTurretAssembly(spec.ship) != null
                && ship.MountedTurrets == null)
                AttachFighterTurret(ship);
            if (spec.capital != 0 && spec.capitalEnhanced) CapitalShip.Attach(ship, this, db);   // remake option (CapitalShips)
            // Remake mods: a mod capital ship's subsystems (turrets, shield generators, weak spots, hangars) on its hull; an NPC
            // ship type's "hardpoints" (npcships.json) instead of its hull's.
            var subsystems = spec.npcType?.hardpoints;
            if (subsystems == null && spec.modCapital) db.ShipSubsystems.TryGetValue(spec.ship, out subsystems);
            if (subsystems != null && subsystems.Count > 0) CapitalSubsystems.Attach(ship, this, db, subsystems);
            // ... one that jumps away instead of dying (ships.json "retreat": EVERSPACE 2's Dreadnought; npcships.json too)
            if ((spec.modCapital && CustomShips.Get(spec.ship)?.retreat == true) || spec.npcType?.retreat == true) CapitalRetreat.Attach(ship, this);
            // ... a freighter's escort follows it, fighting what it meets on the way (TrafficPlan.AddNpcEscorts).
            if (spec.escortLeader != null && Ships.Find(x => x != null && x.Spec == spec.escortLeader) is NpcShip leader)
                ship.SetOrder(new NpcOrder { kind = NpcOrder.Kind.Follow, leader = leader.transform, offset = spec.escortOffset, fight = true });
            return ship;
        }

        /// <summary>The fighter turret's assembly on ships 45 / 51 (Level::createFighterTurrets), null for the others.</summary>
        public static string FighterTurretAssembly(int ship) => ship == 45 || ship == 51 ? "turret_002_static" : null;

        /// <summary>Where the fighter turret of ship 45 / 51 sits in the ship's frame (Unity metres): the game's ship-relative
        /// (0, 172.25, -460.7) / (0, 470, -83) as (-x, y, z) x 0.05.</summary>
        public static Vector3 FighterTurretLocal(int ship)
        {
            var off = ship == 45 ? new Vector3(0f, 172.25f, -460.7f) : new Vector3(0f, 470f, -83f);
            return new Vector3(-off.x, off.y, off.z) * M;
        }

        /// <summary>Level::createFighterTurrets 0xcb200: ship 45 (Qyrr Myfft's) and 51 (Hans') carry an invulnerable turret
        /// object 0x1a74 with the host's max hull at the ship-relative (0, 172.25, -460.7) / (0, 470, -83), race 8 / 0
        /// (PlayerTurret::setHost); Level::assignGuns gives it item 22 at x0.5. It follows its host (NpcShip.SyncTurret).</summary>
        void AttachFighterTurret(NpcShip host)
        {
            bool myfft = host.Spec.ship == 45;
            var spec = new SpawnSpec
            {
                group = NpcGroup.Turret, race = myfft ? Standing.Pirate : 0, ship = -1, turretAssembly = "turret_002_static",
                hitpoints = Mathf.Max(1, host.Hp.maxHull), noLoot = true, nameText = 1666, stationary = true,
                alwaysEnemy = host.Spec.alwaysEnemy, alwaysFriend = host.Spec.alwaysFriend, position = host.Spec.position,
            };
            var t = Create(spec);
            t.transform.SetParent(host.transform, false);
            t.transform.localPosition = FighterTurretLocal(host.Spec.ship);
            t.transform.localRotation = Quaternion.identity;
            t.Target.invulnerable = true;
            // Remake: no hit cube of its own (the +-1000 turret cube would soak up the shots at its host) and no lock.
            t.Target.radius = 0f;
            t.Target.untargetable = true;
            HullCollision.Detach(t.Target);   // nor a mesh hull (it stopped the shots at the Rhino under it)
            t.SetGun(22, 0.5f);
            host.AttachedTurret = t;
        }

        /// <summary>Level::createWingmen 0xcb338: the hired wingmen next to the player (after every other ship). The model
        /// comes from a generator seeded with 5 x the name's length, so each wingman flies the same ship everywhere; 600
        /// hull at least; unarmed in a Challenge. Not at campaign 158.</summary>
        public void SpawnWingmen(Transform player, bool unarmed)
        {
            var names = Session.Wingmen;
            if (names == null || names.Count == 0 || player == null || Session.CampaignMission == 158) return;
            // Level::createWingmen: not in the supernova system (27) either, before campaign 0x9e.
            if (!Session.FreePlay && Session.CampaignMission < 0x9e && (db.Stations.Find(s => s.index == StationIndex)?.system ?? -1) == 27) return;
            int race = Session.WingmanRace;
            float[] right = { -1000f, 2000f, 0f };
            for (int i = 0; i < Mathf.Min(3, names.Count); i++)
            {
                var state = UnityEngine.Random.state;
                UnityEngine.Random.InitState(5 * names[i].Length);
                int ship = NpcTables.RandomFighter(race <= 3 || Modding.ModRaces.Is(race) ? race : Standing.Pirate);   // races 4-7: the pirate pool
                UnityEngine.Random.state = state;
                var pos = player.position + (player.right * right[i] - player.forward * 2000f + Vector3.up * (i == 2 ? 1000f : 0f)) * 0.05f;
                var spec = new SpawnSpec
                {
                    group = NpcGroup.Wingman, race = race, ship = ship, alwaysFriend = true, noLoot = true, name = names[i],
                    position = new Vector3(pos.x, pos.y, -pos.z) / 0.05f, hitpoints = Mathf.Max(600, NpcTables.Hull(0, ship)),
                };
                var s = Create(spec);
                s.transform.rotation = player.rotation;
                s.MakeWingman(i, !unarmed);
            }
            ConnectPlayers();
        }

        public List<NpcShip> LivingWingmen => Ships.FindAll(s => s.IsWingman && !s.Gone && s.Current == NpcShip.State.Fly);

        /// <summary>The wingman menu's command for every living wingman (MGame::OnTouchEnd, menu 2).</summary>
        public void CommandWingmen(int command, Target locked, Route playerRoute)
        {
            if (command == 0) Session.WingmanShowEmp = !Session.WingmanShowEmp;   // Status+0xf8
            foreach (var w in LivingWingmen) w.WingmanCommand(command, locked, playerRoute);
        }

        /// <summary>Level::createShip for a campaign level: one ship; call ConnectPlayers once all are spawned.</summary>
        public NpcShip SpawnShip(SpawnSpec spec) => Create(spec);

        /// <summary>Multiplayer: this player now runs the orbit (took it over): its relaunches and raider waves run
        /// (Traffic.Setup had it passive), unless the level itself keeps it passive (a story, mission or siege orbit).</summary>
        public void SetPassive(bool passive) => IsStoryOrbit = passive;

        /// <summary>Level::connectPlayers: each ship's enemy list is the player first, then every ship of another race.
        /// 'playerExempt': ships of these races leave the player out (campaign 16 / 24 / 28: the Void attack the others).</summary>
        public void ConnectPlayers(int playerExemptRace = -99)
        {
            // Level::connectPlayers 0xcc330: in free flight (Mission::isEmpty) a Specter's only enemy is the player (setEnemy).
            bool freeFlight = !NpcTables.InCampaignLevel && NpcTables.LevelFreelanceType < 0;
            foreach (var s in Ships)
            {
                s.enemies.Clear();
                if (Player != null && s.Race != playerExemptRace) s.enemies.Add(Player);
                if (freeFlight && s.Race == Standing.Specter) continue;
                // Remake (a player's report): a wreck held at its end (deadButSelectable: the Supernova's hidden-blueprint freighters, mods'
                // derelicts) is nobody's target; the Terran one at Wah'norr drew the orbit's Vossk onto it (invulnerable decor).
                // Level::connectPlayers 0xcc330: race != shooter's race, or the shooter is a wingman (every other ship is in a
                // wingman's list, so its shots hit same-race freighters and the battleship; Discord report 2026-10-19: escorts'
                // EMP shots flew through big ships). Not the other wingmen (the original's stray hits on them: left out).
                foreach (var o in Ships)
                    if (o != s && (o.Race != s.Race || (s.IsWingman && !o.IsWingman)) && !o.Spec.deadButSelectable && !o.Spec.stationPort) s.enemies.Add(o.Target);
            }
        }

        static AssemblyData ShipAssembly(Database db, int ship, int race)
        {
            string raceName = race switch { 0 => "terran", 1 => "vossk", 2 => "nivelian", 3 => "midorian", 9 => "void", _ => "pirate" };
            return db.ShipAssembly(ship, raceName);
        }

        // ---- generic radio chatter (Level::createRadioMessage(kind, race) 0xd5568; combat_equipment.md 6) ------------

        /// <summary>One generic radio line: a random face of the race (ImageFactory::createChar) or the Pirate Boss (speaker 9).</summary>
        public class Chatter
        {
            public string text, speaker;
            public int[] portrait;      // a generic face, or null with speakerId
            public int speakerId = -1;  // a story speaker (9 Pirate Boss)
            public string character;    // a mod's character ("mod_id:id", Modding.ModCharacters): its portrait
            public string voice;        // Globals::getDialogueSoundId (the story's radio calls)
        }

        /// <summary>Status::getMission (Status+400) is not empty: the orbit is a story level's or the freelance mission's
        /// target (set by Status::departStation, cleared when the mission ends). Level::createRadioMessage returns at once
        /// then, so no generic line plays in a mission orbit (the level's own radio is separate). Null = never.</summary>
        public Func<bool> LevelMissionActive;

        /// <summary>A story cutscene runs (SpaceLevel.Cutscene): the original draws no radar then, so no music switch.</summary>
        public Func<bool> RadarHidden;

        readonly Queue<Chatter> chatterQueue = new Queue<Chatter>();
        Chatter chatter;
        float chatterMs, chatterDurationMs;

        bool baseWakeRadio, baseDestroyedRadio;
        int emergencyKills;

        // ---- remake: pirate events (TrafficPlan.AddPirateEvent, GitHub #6) ----------------------------------------------

        bool bossCalled, warningChecked, warningSent, storyOrbit;
        float eventMs;
        int systemIndex = -1;

        // ---- Alice in the Void (MGame::OnInitialize -> Level::createRadioMessage(8), tables 0x2541c8 / 0x2543a0) ---------

        /// <summary>Per conversation the number of lines (DAT_002543a0) and the (speaker image, text) pairs (DAT_002541c8):
        /// 0x1a Alice, 0 Keith, 0x1f Cornelius Tenner.</summary>
        static readonly int[] VoidTalkCounts = { 2, 2, 2, 4, 3, 3, 2, 3 };
        static readonly (int image, int text)[] VoidTalk =
        {
            (0x1a, 2453), (0, 2454), (0x1a, 2455), (0, 2456), (0x1a, 2457), (0, 2458), (0x1a, 2459), (0, 2460), (0x1a, 2461),
            (0, 2462), (0x1a, 2463), (0x1f, 2464), (0x1a, 2465), (0, 2466), (0x1a, 2467), (0, 2468), (0x1a, 2469), (0, 2470),
            (0x1f, 2471), (0x1a, 2472), (0, 2473),
        };

        // ---- Loma's pirate toll (MGame::OnInitialize / OnUpdate / OnTouchEnd, Player::damage; Status+0x110 / +0x111) --------

        /// <summary>A ChoiceWindow in flight: (text, yes, no, on yes, on no); the game pauses meanwhile.</summary>
        public event Action<string, string, string, Action, Action> ChoiceRequested;

        public string ChoiceTitle { get; private set; }
        /// <summary>The launch / arrival camera runs (SpaceLevel): the wingmen drift meanwhile.</summary>
        public Func<bool> LaunchCameraRunning;
        bool lomaAskPending;
        int lomaToll, lomaPercent;

        /// <summary>Level::enemyDied / friendDied bookkeeping and Standing::applyKill.</summary>
        /// <summary>Level::enemyDied / friendDied: a ship died, killed by the player or not (the campaign's kill counters).</summary>
        public event Action<NpcShip, bool> ShipDied;
        /// <summary>Level::stealFriendCargo (Level+0x13c): cargo was stolen from a friendly ship (Objective 0x13).</summary>
        public bool FriendCargoStolen { get; set; }

        bool musicHeld;

        void Update()
        {
            if (waiting.Count > 0)
            {
                var spec = waiting.Dequeue();
                if (holdEnemies && Enemy(spec)) heldEnemies.Add(spec);
                else
                {
                    bool builtAhead = spec.standIn != null;
                    var ship = Create(spec);
                    if (holdEnemies && !spec.radarHidden)
                    {
                        ship.RadarHidden = true;
                        offRadar.Add(ship);
                        arrivalScale.Add(ship.Model, !builtAhead);
                    }
                }
                if (waiting.Count == 0)
                {
                    SystemApproach.DropShips();
                    if (!holdEnemies) ShipsPlaced();
                }
            }
            float dtMs = Time.deltaTime * 1000f;
            Navigation.SyncMusic(music, ref musicHeld);   // on through conversations, paused by the pause menu
            UpdateOrbit(dtMs);
            UpdateAlienAttackers(dtMs);
            UpdateChatter(dtMs);
            UpdateLomaToll();
            UpdateConvoy();
            UpdateWanted();
            UpdatePirateEvent(dtMs);
            int hostiles = 0;
            if (hasScanner)
                foreach (var s in Ships)
                    // Radar::draw 0x156xxx: only active ships (Player::isActive) count, so a ship held back by its level
                    // script doesn't start the battle music or block fast-forward before it shows up. A sleeping ship is
                    // inactive too (KIPlayer::setToSleep -> Player::setActive(false)): index 7's ambush, sleeping guards.
                    // Level::getEnemies holds the freighters (PlayerFixedObject) too: a hostile one counts (Intercept's convoy).
                    if (!s.Gone && !s.Inactive && !s.Asleep && s.Current != NpcShip.State.Dying && s.Current != NpcShip.State.Dead && s.Target.Alive && s.Target.hostileToPlayer)
                        hostiles++;
            // Multiplayer: the other players' ships here (NetProxy targets, shown only in this orbit) count too.
            if (hasScanner && GoF2Remake.Data.NetSession.Active)
                foreach (var t in Target.NetShips)
                    if (t != null && t.enabled && !t.untargetable && !t.isPlayer && t.Alive && t.hostileToPlayer) hostiles++;
            HostileCount = hostiles;
            if (!MenuBackdrop) UpdateMusic(Time.unscaledDeltaTime);   // the menu plays its own theme
        }

        /// <summary>The main menu's backdrop (CutScene(2)): the orbit's traffic without the level's music.</summary>
        [NonSerialized] public bool MenuBackdrop;

        // ---- the Most Wanted criminal (wingmen_wanted.md 2.6-2.7) -----------------------------------------------------

        NpcShip wantedShip;
        bool wantedUncovered, wantedAttacked, wantedSurrendered;
        int damageAfterSurrender;
        /// <summary>The player's radar ship lock (CombatRadar.Locked), set by the level: locking the criminal uncovers it.</summary>
        public Func<Target> LockedTarget;
        /// <summary>A bounty was paid (the reward message: 3206 + credits, sound 36).</summary>
        public event Action<int> BountyCollected;

        // ---- step 59's arms convoy (LevelScript::process, LevelScript+0xa9) ------------------------------------------

        NpcShip convoyFreighter;
        int convoyStep;
        /// <summary>The convoy point as the player's route (Level+0x108), null = none / done.</summary>
        public Route ConvoyRoute { get; private set; }
        /// <summary>The convoy freighter died: the player's route and autopilot are cleared.</summary>
        public event Action ConvoyDone;

        /// <summary>A cutscene plays its own music (the prologue / rescue): the traffic music stays silent.</summary>
        public bool MusicMuted { get; set; }

    }
}
