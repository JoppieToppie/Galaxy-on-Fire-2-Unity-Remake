// SpaceLevel.cs
// Builds the flight level for the current station orbit at startup, like Level::init 0xbb49c (two passes) and
// MGame::reset 0x1a793c: one level = one station orbit; the other stations of the system only appear as planets.
// The world itself (sky, lights, station, jumpgate, asteroids, dust, sun/planets) comes from OrbitBuilder,
// which the main menu background uses too. This adds the flight parts:
//   createPlayer:  player ship at (10, 10, 10000) facing away from the station (+-8.8 deg), speed 2 u/ms
//   MGame::reset:  camera fov 1.22 rad, near 20, far 300000 units; chase offsets (0, 600, -1338) / (0, 600, -650)
//   MGame::dockEvent 0x1afebc: docking switches straight to the station module (no animation). The original needs the
//                  autopilot aimed at the station plus a collision or |pos| < 16000; here the HUD offers "Dock" inside
//                  16000 units once the player has flown out of that range after spawning (no autopilot yet).
//   LevelScript 0x15e650 / process 0x160d50: after launching from the station a fixed camera 9000 units ahead of the
//                  ship (+-500..2499 sideways and up) watches it fly past for 7 s, then the chase camera takes over.
//   Level::init arrival by travel (planet jump, Navigation): 4x the previous station's planet billboard (about 80000
//                  units out) or the hidden jumpgate in the gate orbit, facing the station, with the travel launch camera.
//   Navigation: station / jumpgate / planet locks, autopilot (docks at the station), planet jump, fast-forward.
//   SystemJump: the jumpgate (star map, jump scene) and the Khador Drive. Arrival from another system: the hidden gate
//                  in the gate orbit, else (0, 0, 100000); the arrival camera then shows the orbit information.
//   LevelScript::process 0x160d50: at the end of the launch / arrival camera the autopilot continues to a programmed
//                  station (Navigation.ContinueToProgrammedStation), unless the Khador Drive is about to charge.
//   Level::createMission etc.: the NPC traffic (Traffic) and ship combat: the player's pools and death
//                  (PlayerHealth), ship / salvage locks (CombatRadar); invulnerable during the launch / arrival
//                  camera and the jump scenes.
//   PlayerEgo::calcCollision: the ship slides along the station, the visible jumpgate and freighters (Obstacle,
//                  PlayerCollision), touching an asteroid destroys it; off during the launch / arrival camera and the
//                  jump scenes. MGame::dockEvent: the autopilot to the station also docks on touching the station.
//   Story (StorySpace, CampaignLevel): an orbit built around a campaign mission (Story.IsLevelMission) gets
//                  the campaign level instead of normal traffic; briefings, success / failure, the add-on entry calls. On a
//                  story mission the station refuses docking and the planet jumps / Khador Drive are blocked (525).
//   Wormhole (landmark 3): exists until the game is won, visible when coming out of the Void, at the station the Void
//                  attack or in the alien orbit (Session.VoidOrbit, the Void's home: its station, sky 010, Void asteroids);
//                  arriving from the Void it closes behind the player (LevelScript::LevelScript: player - dir * 10000,
//                  reset(true)). MGame::OnUpdate, the player inside it: an active campaign mission advances first (index
//                  < 41, not 29 / 40; 40 only once Errkt's freighter went through, carrying its hull into 41), entering too
//                  early at 29 / 40 / 41 kills the player, 42 in the alien orbit is the level script's; then the ride:
//                  into the alien orbit (remembering this station, Status+0x84) or back out to that station.

using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.Multiplayer;
using GoF2Remake.Visuals;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.SceneManagement;
using GoF2Remake.Events;

namespace GoF2Remake.World
{
    [DefaultExecutionOrder(-100)]
    public class SpaceLevel : MonoBehaviour
    {
        const float M = OrbitLayout.MetersPerUnit;

        [Header("Scene")]
        public Camera mainCamera;
        public Light sunLight;
        public Light planetLight;

        [Header("Orbit (-1 = Session)")]
        public int stationOverride = -1;
        public string stationScene = "Station";

        [Header("Tuning (not recovered constants)")]
        [Tooltip("URP intensity for the original's LIGHT0 diffuse of 2.0 (clamp(15 * sunColour, 0, 2)).")]
        public float sunIntensityAt2 = 1.6f;
        public float planetLightIntensity = 1f;
        public float ambientIntensity = 1f;

        /// <summary>The orbit's own sky (Level::switchSkyboxForIntro: the prologue's time jump, nebula 9 = Mido's).</summary>
        public void RestoreOrbitSky() => OrbitBuilder.SetupSky(Layout, ambientIntensity);

        public OrbitLayout Layout { get; private set; }
        public ShipController Player { get; private set; }
        public WeaponSystem Weapons { get; private set; }
        public Mining Mining { get; private set; }
        public Navigation Navigation { get; private set; }
        public SystemJump SystemJump { get; private set; }
        /// <summary>Docking at a story object (ObjectDocking).</summary>
        public ObjectDocking Docking { get; private set; }
        /// <summary>The orbit's plasma clouds (null without a spectral filter).</summary>
        public GasCloudField GasClouds { get; private set; }
        public PlayerHealth Health { get; private set; }
        public Traffic Traffic { get; private set; }
        public CombatRadar Radar { get; private set; }
        public PlayerCollision Collision { get; private set; }
        public StorySpace StorySpace { get; private set; }
        /// <summary>The one-time tutorial windows (MGame::OnUpdate's Globals::hints).</summary>
        public FlightHints Hints { get; private set; }
        /// <summary>The campaign level of a story orbit, null in a normal orbit.</summary>
        public CampaignLevel Campaign { get; private set; }
        /// <summary>The freelance mission's orbit (FreelanceOrbit), null = none.</summary>
        public FreelanceOrbit FreelanceOrbit { get; private set; }
        /// <summary>The Kaamo Club's pirate siege (station 108 before it's freed), null elsewhere.</summary>
        public KaamoSiege Siege { get; private set; }
        /// <summary>The ship's turrets, in mount order (remake: more than one on a custom ship with several turret mounts).</summary>
        public System.Collections.Generic.IReadOnlyList<PlayerTurret> Turrets => turrets;
        readonly System.Collections.Generic.List<PlayerTurret> turrets = new System.Collections.Generic.List<PlayerTurret>();
        /// <summary>The turret the HUD and the level scripts talk to: the one in the turret view, else the first (null: none).</summary>
        public PlayerTurret Turret
        {
            get
            {
                PlayerTurret first = null;
                foreach (var t in turrets)
                {
                    if (t == null) continue;
                    if (t.InTurretView) return t;
                    if (first == null) first = t;
                }
                return first;
            }
        }
        public FreeLookCamera FreeLook { get; private set; }
        public PlayerCloak Cloak { get; private set; }
        public TimeExtender Extender { get; private set; }
        /// <summary>MGame::dockEvent: 525 while a mission holds the player here (the story's blocks, the Kaamo siege).</summary>
        public bool DockingBlocked => Story.BlocksDocking(Layout.stationIndex, IsStoryOrbit) || (Siege != null && Siege.Active) || FreelanceBlocks
                                      || Events.EventRules.NoDocking;   // an event's Restrict Travel
        bool freelanceMissionOrbit;
        /// <summary>The orbit was built around the freelance mission (Status::departStation put it in Status+400) and it holds
        /// the player here until it is won or failed (Freelance.BlocksTravel).</summary>
        bool FreelanceBlocks => freelanceMissionOrbit && Layout != null && Freelance.BlocksTravel(Layout.stationIndex);
        /// <summary>The orbit was built as the story's (Status::departStation put the campaign mission in Status+400).</summary>
        public bool IsStoryOrbit { get; private set; }
        /// <summary>A story conversation is open (the game is paused).</summary>
        public bool Dialogue => StorySpace != null && StorySpace.DialogueOpen;
        /// <summary>LevelScript startSequenceOver: the launch / arrival camera has ended (in the prologue / rescue the
        /// cutscene script decides).</summary>
        public bool StartSequenceOver => launchCameraMs <= 0f && (Campaign == null || Campaign.StartSequenceOver);
        public bool LaunchCameraOver => launchCameraMs <= 0f;
        /// <summary>A LevelScript cutscene owns the camera (MGame+0x5f): no HUD, no player control. Also an event graph's
        /// cutscene (Events.EventCutscene, "cutscene start").</summary>
        public bool Cutscene => (Campaign != null && Campaign.Cutscene) || Events.EventCutscene.Cinematic;
        public Transform Asteroids { get; private set; }
        public Backdrop Backdrop { get; private set; }
        public GameObject Station { get; private set; }
        public GameObject Jumpgate { get; private set; }
        /// <summary>Landmark 3 (PlayerWormHole), null once the game is won.</summary>
        public Wormhole Wormhole { get; private set; }
        /// <summary>Hud::drawOrbitInformation: during the arrival camera after a gate / Khador jump.</summary>
        public bool OrbitInfoVisible => orbitInfo && launchCameraMs > 0f;
        /// <summary>This orbit's station (name, tech level) and its system's jumpgate station (-1 = none).</summary>
        public StationData StationInfo { get; private set; }
        public int SystemJumpgateStation { get; private set; } = -1;

        /// <summary>PlayerEgo::collidesWithStation / calcCollision 0xab550: |pos| &lt; 16000 units.</summary>
        public const float DockRange = 16000f;

        /// <summary>This orbit's dock range in game units: 16000, or for a mod's own station model (ModStations) its bounding
        /// radius + 6000, so the range reaches past its collision (a bigger model kept the player out of the 16000).</summary>
        public float StationDockRange { get; private set; } = DockRange;

        /// <summary>Where a launch starts (Level::init 0xbb7a8: (10, 10, 10000) game units; a mod's station model: its bounding
        /// radius + 3000 out along game +z, at least 10000).</summary>
        public Vector3 UndockPoint { get; private set; } = OrbitLayout.UndockPosition;
        const float LaunchCameraMs = 7000f;

        /// <summary>The HUD's "Dock" prompt: an orbit with a station, player inside the dock range, not during the launch
        /// camera, and only after having left the range once (the undock spawn at 10000 units is inside it).</summary>
        public bool CanDock => Layout.hasStation && StationShown && Player != null && launchCameraMs <= 0f && leftDockRange && InDockRange && (Health == null || !Health.Dead)
                               && !DockingBlocked && PlayerHull.PlayerShip   // remake debug: no hangar for a freighter / capital ship
                               && (Mining == null || Mining.State == Mining.Phase.Idle);
        /// <summary>The station is there (a level script may hide it: 78's Valkyrie jumping away); nothing docks at a hidden one.</summary>
        bool StationShown => Station == null || Station.activeInHierarchy;
        bool InDockRange => Player.transform.position.sqrMagnitude < StationDockRange * M * StationDockRange * M;

        Database db;
        public Database Database => db;
        ChaseCamera chase;
        float launchCameraMs;
        bool leftDockRange, orbitInfo;

        void OnEnable() => Settings.Changed += ApplyOptions;
        void OnDisable()
        {
            Settings.Changed -= ApplyOptions;
            if (savedMaxDelta > 0f) Time.maximumDeltaTime = savedMaxDelta;
        }

        /// <summary>Options changed in flight (the pause menu) reach the ship and the chase camera at once.</summary>
        void ApplyOptions()
        {
            if (Player != null) { Player.sensitivity = Settings.Sensitivity; Player.invertPitch = Settings.InvertPitch; Player.invertYaw = Settings.InvertYaw; }
            if (chase != null) chase.baseFov = Settings.FieldOfView;
        }

        float savedMaxDelta = -1f;

        void Awake()
        {
            // Multiplayer: loaded after its session ended (a docking queued behind the menu): on to the menu (nothing is
            // saved, SaveGame.SessionGame).
            if (GoF2Remake.Multiplayer.NetGame.SessionLost) SceneManager.LoadScene("MainMenu");
            db = Database.Load();
            // MGame::OnUpdate: a frame's dt is capped at 150 ms (a hitch never jumps the game far ahead).
            savedMaxDelta = Time.maximumDeltaTime;
            if (Time.maximumDeltaTime > 0.15f) Time.maximumDeltaTime = 0.15f;
            int station = stationOverride >= 0 ? stationOverride : Session.StationIndex;
            // Multiplayer arena match (NetArenaClient): the template orbit's sky, sun and planets, its own orbit id.
            var arena = NetArenaClient.Current;
            if (arena != null) { station = arena.template; Session.ArrivedByTravel = Session.ArrivedBySystemJump = false; }
            ComingFromVoid = Session.ComingFromVoid;
            // Status::departStation: the Void-invasion re-roll counter (index 32-44).
            Story.OnDepart(db, station);
            // Status::moveWanted: a real orbit change (not a launch from the docked station, not to / from the Void) moves the
            // Most Wanted criminals one system along their routes.
            if ((Session.ArrivedByTravel || Session.ArrivedBySystemJump) && station != Session.VoidOrbit && Session.PreviousStationIndex != Session.VoidOrbit)
                WantedBoard.Move(db, station, Session.ProgrammedStation);
            Layout = OrbitLayout.Build(db, station);
            var st = db.Stations.Find(s => s.index == station);
            StationInfo = st;
            SystemJumpgateStation = db.Systems.Find(s => s.index == Layout.systemIndex)?.jumpgateStation ?? -1;
            Debug.Log($"SpaceLevel: station {station} {st?.name} (system {Layout.systemIndex} {st?.systemName}), " +
                      $"gate {Layout.hasJumpgate}, {Layout.asteroidCount} asteroids");

            OrbitBuilder.SetupSky(Layout, ambientIntensity);
            OrbitBuilder.SetupLights(Layout, sunLight, planetLight, sunIntensityAt2, planetLightIntensity);
            SetupCamera();
            // Status::inEmptyOrbit: Var Hastra (78) has no station while the index is 0 or 1 (the prologue and the rescue);
            // Level::init: the prologue's asteroid belt is centred on the origin, under its own sky (Level::createSpace).
            bool prologue = station == 78 && !Session.FreePlay && Story.Index <= 1;
            if (prologue) Layout.hasStation = false;
            if (prologue && Story.Index == 0) Layout.asteroidCentre = Vector3.zero;
            // An arena (the Void's home orbit): no station or gate (no docking, no jumps); the asteroids around the centre.
            if (arena != null)
            {
                Layout.hasStation = Layout.hasJumpgate = false;
                Layout.asteroidCentre = Vector3.zero;
            }
            var snCentre = SupernovaLevels.AsteroidCentre(Story.Index, station, station == Session.VoidOrbit);
            if (snCentre.HasValue && Story.IsLevelMission(station)) Layout.asteroidCentre = snCentre.Value;
            Station = OrbitBuilder.SpawnStation(db, Layout);
            Jumpgate = OrbitBuilder.SpawnJumpgate(db, Layout);
            SpawnWormhole();
            AddObstacles();
            // Multiplayer (NetGame): the orbit's field from the world seed, the same for every player here (NetOrbit).
            if (!NetGame.Active) Asteroids = OrbitBuilder.SpawnAsteroids(db, Layout);
            else SpawnNetworkAsteroids(NetGame.OrbitSeed(NetOrbitId));
            if (prologue && Story.Index == 0)
            {
                // Level::createSpace: the belt's sky is nebula 3 (skybox_003) under the orbit's stars and sky rotation;
                // StarSystem::StarSystem (Level type 3, mission 0): the orbit planet is planet_001_big until the time jump.
                OrbitBuilder.SetupSky(Layout, ambientIntensity, 3);
                var own = Layout.planets.Find(p => p.isOrbitPlanet);
                if (own != null) own.texture = "planet_001_big";
            }
            orbitInfo = Session.ArrivedBySystemJump;
            Session.ArrivedBySystemJump = false;
            SpawnPlayer();
            PlaceArrivalWormhole();
            OrbitBuilder.SpawnDust(Layout);
            var backdrop = OrbitBuilder.SpawnBackdrop(Layout, mainCamera);
            SkyLayers.Spawn(Layout, mainCamera);   // ring sky, storms, supernova flares, asteroid belt
            Backdrop = backdrop;

            // Locks on the station, the jumpgate and the other stations' planets; autopilot, planet jump, fast-forward.
            Navigation = Player.gameObject.AddComponent<Navigation>();
            Navigation.Setup(db, Layout, backdrop, Player, Mining, chase, Weapons);
            Navigation.StationObject = Station;
            Mining.navigation = Navigation;
            SystemJump = Player.gameObject.AddComponent<SystemJump>();
            SystemJump.Setup(db, Navigation, Player, Weapons, chase, Jumpgate);

            // Ship combat: the player's Player object, the orbit's NPC traffic, the ship / salvage locks.
            Health = Player.gameObject.AddComponent<PlayerHealth>();
            Health.Setup(db, Player, chase, Weapons);
            Collision = Player.gameObject.AddComponent<PlayerCollision>();
            Collision.Setup(Health, chase, Mining);
            Collision.wormhole = Wormhole;
            Collision.wormholeHeld = () => WormholeHeld;
            Collision.wormholeNoPull = () => WormholeNoPull;
            VolatileCargo.Attach(Player.gameObject, db, Player);   // PlayerEgo+0x398: volatile goods, sound 35
            bool storyOrbit = !Session.FreePlay && Story.IsLevelMission(station);
            IsStoryOrbit = storyOrbit;
            // Status::departStation: the freelance mission's target orbit is built around it (not over a story orbit).
            // Multiplayer: not when a squadmate here already runs this mission (their ships are shown here, NetMissions).
            bool missionHere = !storyOrbit && arena == null && Freelance.IsMissionOrbit(station);
            freelanceMissionOrbit = missionHere;
            bool freelanceOrbit = missionHere && NetMissions.ShouldRun(station);
            bool missionFollower = missionHere && !freelanceOrbit;   // a squadmate here runs it: its briefing, route, timer, score
            // Level::createMission: the Kaamo Club under siege (kaamo_club.md 3).
            bool siege = !storyOrbit && !missionHere && arena == null && KaamoClub.SiegeAt(station);
            // Remake: an event graph quest's orbit (questorbit): no normal traffic, like a story orbit (single player).
            bool questOrbit = !storyOrbit && !missionHere && !siege && arena == null && !NetGame.Active && EventRunner.QuietOrbit(station);
            // Level::assignGuns reads the level mission (Status+400): a campaign level or a freelance mission's type.
            NpcTables.InCampaignLevel = storyOrbit;
            NpcTables.LevelFreelanceType = freelanceOrbit ? Freelance.Mission.type : -1;
            Traffic = new GameObject("Traffic").AddComponent<Traffic>();
            // Multiplayer: only the first player in an empty orbit builds its traffic and runs it; the others show that player's
            // ships (NetOrbit) and take them over if it leaves, never building new ones.
            // An arena: no NPCs, or with the Void fighters the alien orbit's own (run by the first player in the match's orbit).
            NetAuthority = NetGame.Active && (arena == null || arena.voids) && NetState.OrbitEmpty(NetOrbitId);
            ownPassive = storyOrbit || freelanceOrbit || siege || questOrbit || (arena != null && !arena.voids);
            Traffic.LevelMissionActive = () => (storyOrbit && Story.IsLevelMission(station)) || (missionHere && Freelance.IsMissionOrbit(station));
            Traffic.Setup(db, Layout, Health.Target, Station, ownPassive || (NetGame.Active && !NetAuthority), Wormhole);
            Traffic.LaunchCameraRunning = () => !LaunchCameraOver;
            Traffic.RadarHidden = () => Cutscene;
            PlayerHull.AttachTurrets(this);   // remake debug: a capital ship's turrets on the player's hull (PlayerHull)
            // Docking at the story's objects (PlayerEgo::dockToDockingPoint): locked through Navigation.
            Docking = Player.gameObject.AddComponent<ObjectDocking>();
            Docking.Setup(db, Player, chase, Weapons);
            if (FreeLook != null)
                FreeLook.Blocked = () => Cutscene || !LaunchCameraOver || (Mining != null && Mining.State != Mining.Phase.Idle)
                                         || (Docking != null && Docking.Busy) || (Navigation != null && Navigation.Jumping)
                                         || (SystemJump != null && SystemJump.Cinematic) || (Health != null && Health.Dead);
            if (FreeLook != null) FreeLook.TurretAllowed = () => Docking != null && Docking.IsDocked && Docking.Hacking == null && !Cutscene;
            Navigation.Docking = Docking;
            Navigation.Ships = Traffic.Ships;
            Collision.docking = Docking;
            Docking.HackWon += ship => Traffic.OnHackWon(ship);   // a Supernova wreck's hidden blueprint
            if (storyOrbit)
            {
                Campaign = new GameObject("Campaign").AddComponent<CampaignLevel>();
                Campaign.Setup(this, Traffic);
                Navigation.SetRoute(Campaign.PlayerRoute);
            }
            else if (freelanceOrbit)
            {
                FreelanceOrbit = new GameObject("FreelanceOrbit mission").AddComponent<FreelanceOrbit>();
                FreelanceOrbit.Setup(this, Traffic);
                Navigation.SetRoute(FreelanceOrbit.PlayerRoute, true);
            }
            else if (missionFollower)
            {
                FreelanceOrbit = new GameObject("FreelanceOrbit mission (squad)").AddComponent<FreelanceOrbit>();
                FreelanceOrbit.SetupFollower(this, Traffic);
            }
            else if (siege)
            {
                Siege = new GameObject("Kaamo siege").AddComponent<KaamoSiege>();
                if (NetGame.Active && KaamoSiege.OtherRunsHere(station)) Siege.SetupFollower(this, Traffic);   // one siege per orbit
                else Siege.Setup(this, Traffic);
            }
            // Multiplayer: an Informer mission's spy is this player's when another player built the orbit's traffic (theirs
            // has it only for their own mission) and no squadmate here has one already.
            if (NetGame.Active && !NetAuthority && !storyOrbit && arena == null && Freelance.Active && Freelance.Mission.type == MissionType.Informer
                && Freelance.Mission.target == station && !Session.InformerKilled && !Session.InformerFailed && !NetMissions.TeamHere(station))
                SpawnInformerSpy();
            // Step 59's arms convoy: its point is the player's route until the freighter is gone.
            if (Traffic.ConvoyRoute != null)
            {
                Navigation.SetRoute(Traffic.ConvoyRoute);
                Traffic.ConvoyDone += () => { Navigation.SetAutopilot(null); Navigation.SetRoute(null); };
            }
            // Level::createWingmen: after the mission's ships (Challenge: unarmed).
            if (arena == null) Traffic.SpawnWingmen(Player.transform, FreelanceOrbit != null && FreelanceOrbit.Type == MissionType.Challenge);
            Navigation.HasWingmen = () => Traffic != null && Traffic.LivingWingmen.Count > 0;
            StorySpace = gameObject.AddComponent<StorySpace>();
            StorySpace.Setup(this, Campaign);
            Hints = gameObject.AddComponent<FlightHints>();
            Hints.Setup(this);
            Navigation.JumpsBlocked = () => NetArenaClient.InMatch || !Story.PlanetJumpsAllowed || Story.BlocksJumps(Layout.stationIndex, IsStoryOrbit) || (Siege != null && Siege.Active)
                                            || FreelanceBlocks   // a freelance mission's orbit (#32)
                                            || Events.EventRules.NoJumps   // an event's Restrict Travel
                                            || (!Session.FreePlay && Story.Index == 65 && Layout.stationIndex == 100)   // escorting Khador (MGame::UseKhadorDrive)
                                            // Remake (#84): at 42 the way out of the alien orbit is the wormhole, which shows the
                                            // mother ship's explosion; its 0xa0 mission isn't a level mission there (its target is
                                            // that orbit), so the original's Khador Drive skipped the ending.
                                            || (!Session.FreePlay && Story.Index == 42 && Layout.stationIndex == Session.VoidOrbit);
            // MGame::UseKhadorDrive 0x1a9480 has no Void rule of its own: the mission gate above (Story.BlocksJumps, 525) is the
            // only refusal, and in the alien orbit the drive returns to Status+0x84 (#26: the remake used to refuse it there
            // through the main story).
            Navigation.SetWormhole(Wormhole);
            Navigation.PlanetJumpRefused = st => StorySpace != null && StorySpace.RefusePlanetJump(st);
            // MGame::dockEvent refuses the gate on the same mission check as docking: the story's level missions too.
            SystemJump.GateBlocked = () => Story.BlocksJumps(Layout.stationIndex, IsStoryOrbit) || FreelanceBlocks
                                           || (Siege != null && Siege.Active) || Events.EventRules.NoJumps;
            Radar = Player.gameObject.AddComponent<CombatRadar>();
            Radar.Setup(db, Player, Navigation, Mining, Weapons, Health, Traffic);
            Radar.Clouds = GasClouds;
            Traffic.LockedTarget = () => Radar != null ? Radar.Locked : null;   // locking a Most Wanted criminal uncovers it
            // Equipment (combat_equipment.md): the cloak (autopilot menu entry), the time extender, the repair / transfusion beams.
            Cloak = PlayerCloak.Attach(Player.gameObject, db, Session.ShipIndex, Health.Target, Player.visualModel);
            Navigation.Cloak = Cloak;
            Collision.cloak = Cloak;
            Navigation.Extender = Extender;
            if (Extender != null) Extender.Blocked = () => ExtenderBlocked;
            RepairBeam.AttachAll(Player.gameObject, db, Health.Target, Traffic);
            Session.ComingFromVoid = false;   // Level::init / LevelScript have used it (the Void raid, the closing wormhole)
            if (NetGame.Active) gameObject.AddComponent<NetOrbit>().Setup(this);   // multiplayer: the shared orbit
            if (arena != null) NetArenaClient.OnLevelReady();
        }

        /// <summary>Multiplayer: the orbit as the other players see it: the station's index, or an arena match's own id
        /// (NetArena.OrbitBase + the match), so a match is a private copy of its template orbit.</summary>
        public int NetOrbitId => NetArenaClient.Current != null ? NetArenaClient.Current.orbitId : (Layout != null ? Layout.stationIndex : -1);

        /// <summary>Multiplayer: this player runs the orbit's NPC traffic (the first one here), see NetOrbit.</summary>
        public bool NetAuthority { get; private set; }

        /// <summary>Multiplayer: the orbit's authority left, this player takes its ships over (NetOrbit).</summary>
        /// <summary>Multiplayer: the orbit's traffic is this player's now (taken over): its relaunches and waves run, unless the
        /// level keeps it passive itself (a story, mission or siege orbit).</summary>
        public void TakeOverNetAuthority()
        {
            NetAuthority = true;
            Traffic?.SetPassive(ownPassive);
        }

        /// <summary>Multiplayer: another player built this orbit's traffic at the same moment (NetOrbit): this player's goes
        /// (not its mission ships or wingmen) and theirs is shown instead.</summary>
        public void DropNetAuthority()
        {
            NetAuthority = false;
            if (Traffic == null) return;
            foreach (var s in Traffic.Ships)
                if (s != null && !s.Gone && !s.MissionShip && !s.IsWingman && (Siege == null || !Siege.Owns(s))) s.Vanish();
            Traffic.SetPassive(true);
        }

        bool ownPassive;

        /// <summary>TrafficPlan's Informer: a local fighter named 1663 in front of the station (out of the others' view).</summary>
        void SpawnInformerSpy()
        {
            int race = Traffic.SystemRace;
            var at = new Vector3(Random.Range(0, 20000) - 10000, Random.Range(0, 20000) - 10000, Random.Range(0, 30000) + 20000);
            var spy = Traffic.SpawnShip(new SpawnSpec
            {
                group = NpcGroup.Local, race = race, ship = NpcTables.RandomFighter(race), position = NetOrbit.OutOfSight(at), nameText = 1663,
            });
            spy.MissionShip = true;   // this player's mission: never taken over by the orbit's authority
            Traffic.ConnectPlayers();
        }

        /// <summary>Multiplayer (NetGame): the asteroid field from the session's seed, the same on every player's level.</summary>
        public void SpawnNetworkAsteroids(int seed)
        {
            if (Asteroids != null) return;
            var saved = Random.state;
            Random.InitState(seed);
            Asteroids = OrbitBuilder.SpawnAsteroids(db, Layout);
            Random.state = saved;
        }

        /// <summary>Level::comingFromAlienWorld for this level (the session flag is cleared once the level is built).</summary>
        public bool ComingFromVoid { get; private set; }

        /// <summary>A cinematic, a jump, the launch camera or the player's death stops the time extender (MGame::OnUpdate 0x1af162).</summary>
        bool ExtenderBlocked => launchCameraMs > 0f || Navigation.Jumping || (SystemJump != null && SystemJump.Cinematic) || Cutscene
                                || Health == null || Health.Dead;

        void Update()
        {
            // MGame::OnUpdate: the wingmen's contract runs down while flying (fast-forward included, not while paused).
            if (Session.Wingmen.Count > 0 && Session.WingmanContractMs > 0f) Session.WingmanContractMs = Mathf.Max(0f, Session.WingmanContractMs - Time.deltaTime * 1000f);
            if (Health == null) return;
            Health.invulnerable = launchCameraMs > 0f || Navigation.Jumping || (SystemJump != null && SystemJump.Cinematic)
                                  || (Campaign != null && Campaign.PlayerInvulnerable) || (StorySpace != null && StorySpace.SuccessPending)
                                  || Events.EventCutscene.Invulnerable;
            Collision.off = launchCameraMs > 0f || Navigation.Jumping || (SystemJump != null && SystemJump.Cinematic)
                            || (Campaign != null && Campaign.CollisionOff);   // PlayerEgo+0x144
            Collision.ignoreGate = Navigation.GoingToGate;
            // Hostiles, or a radio line on screen, block fast-forward (MGame::OnUpdate).
            Navigation.HostilesPresent = (Traffic != null && (Traffic.HostileCount > 0 || Traffic.ChatterVisible != null)) || (Campaign != null && Campaign.Radio != null && Campaign.Radio.Busy);
            if (Extender != null && ExtenderBlocked) Extender.Cancel();
            UpdateWormholeRide();
        }

        // ---- the wormhole (landmark 3) -----------------------------------------------------------------------

        /// <summary>Level::createSpace: landmark 3 while the game isn't won, at a random spot (time-seeded).</summary>
        void SpawnWormhole()
        {
            if (Session.FreePlay || Story.GameWon) return;
            bool attacked = Layout.stationIndex == Session.VoidInvasionStation;
            bool visible = (ComingFromVoid || attacked || Layout.alienOrbit) && Story.Index < 43;
            var pos = new Vector3(Random.Range(0, 80000) - 40000, Random.Range(0, 40000) - 20000, Random.Range(0, 40000) + 40000);
            Wormhole = Wormhole.Spawn(StoryAssets.Load(), pos, visible);
            Wormhole.alienOrbit = Layout.alienOrbit;
            Wormhole.attackedStation = attacked;
        }

        /// <summary>LevelScript::LevelScript 0x16056c: coming out of the Void (or in the alien orbit) the wormhole sits
        /// 10000 units behind the player and closes after a second (reset(true)).</summary>
        void PlaceArrivalWormhole()
        {
            if (Wormhole == null) return;
            Wormhole.player = Player.transform;
            if (!(ComingFromVoid || Layout.alienOrbit) || Story.Index >= 43) return;
            var p = Player.transform.position - Player.transform.forward * 10000f * M;
            Wormhole.ResetTimer(true);
            Wormhole.transform.position = p;
            Wormhole.SetVisible(true);
        }

        bool riding, rode;
        float rideHoldMs;
        /// <summary>The level is being left (wormhole ride, docking): the story checks stop.</summary>
        public bool Leaving { get; private set; }

        /// <summary>Entering the wormhole now kills the player (MGame::OnUpdate: 29 / 41, and 40 until Errkt's freighter has
        /// gone through).</summary>
        bool WormholeKills
        {
            get
            {
                int index = Story.Index;
                return !Session.FreePlay && Campaign != null && Story.IsLevelMission(Layout.stationIndex)
                       && (index == 29 || index == 41 || (index == 40 && Campaign.Event <= 3));
            }
        }

        /// <summary>Remake: during a level's cutscene, while entering would kill, the wormhole neither pulls nor takes the
        /// player, who has no control then (step 40: Errkt's call comes 40 s in wherever the player is, and the cutscene
        /// following his freighter drew a player already near the wormhole into it, dead before the scene ended). And at
        /// step 40 until his freighter has gone through it doesn't pull at all: its pull near the centre is faster than a
        /// ship, so a player near it when he called was dragged in the moment the scene gave the controls back; flying into
        /// it still kills (the original's rule).</summary>
        bool WormholeHeld => Campaign != null && Campaign.Cutscene && WormholeKills;
        bool WormholeNoPull => WormholeKills && Story.Index == 40;

        /// <summary>MGame::OnUpdate, PlayerEgo::isInWormhole (see the header).</summary>
        void UpdateWormholeRide()
        {
            if (riding || Collision == null || !Collision.InWormhole || Health == null || Health.Dead) return;
            int index = Story.Index;
            bool active = !Session.FreePlay && Campaign != null && Story.IsLevelMission(Layout.stationIndex);
            if (index == 42)
            {
                if (Layout.alienOrbit) return;   // the mother ship's explosion: the level script rides out itself
                RideWormhole();
                return;
            }
            // Step 24: Carla's "What is this thing? KEITH!" (radio line 4, text 1921) starts as the wormhole opens and shows
            // 2 s later; the ride waits for it (8 s at most), or the scene load swallows the line.
            if (active && index == 24 && Campaign.Radio != null && Campaign.Radio.Triggered(4) && !Campaign.Radio.Over(4)
                && (rideHoldMs += Time.deltaTime * 1000f) < 8000f) return;
            if (active)
            {
                if (WormholeKills) { if (!WormholeHeld) { Health.Kill(); riding = Health.Dead; } return; }   // the original sets HP 0 every frame: an emergency system or god mode tries again
                if (index == 40) Session.LastFreighterHull = Campaign.FreighterHull;
                if (index < 41) Story.Advance(db);
            }
            RideWormhole();
        }

        /// <summary>The ride: into the alien orbit (remembering this station as Status+0x84), or out of it back there; the
        /// hull etc. are kept (Status), a stream-out arrival with the wormhole behind the player.</summary>
        public void RideWormhole()
        {
            if (rode) return;
            rode = riding = true;
            Leaving = true;
            Weapons?.StoreAmmo();
            int from = Layout.stationIndex;
            if (Layout.alienOrbit) Session.StationIndex = Session.VoidReturnStation;
            else { Session.VoidReturnStation = from; Session.StationIndex = Session.VoidOrbit; }
            Session.PreviousStationIndex = from;
            Session.ArrivedByTravel = true;
            Session.LaunchedFromStation = false;
            Session.ComingFromVoid = true;
            Session.ProgrammedStation = -1;
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }

        /// <summary>A level script's jump to another orbit (MGame::OnTouchEnd after index 64's / 80's success conversation:
        /// switch_to_target_setting, departStation, initStreamOutPosition): hull, shield and armor kept, a stream-out arrival.</summary>
        public void TravelTo(int station)
        {
            if (Leaving) return;
            Leaving = true;
            Weapons?.StoreAmmo();
            if (station == Session.VoidOrbit) Session.VoidReturnStation = Layout.stationIndex;
            Session.PreviousStationIndex = Layout.stationIndex;
            Session.StationIndex = station;
            Session.ArrivedByTravel = true;
            Session.LaunchedFromStation = false;
            Session.ProgrammedStation = -1;
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }

        /// <summary>Remake multiplayer (NetTeleport): an admin's teleport out of this orbit, into another orbit (the pose
        /// from NetTeleport.TakePose, no launch camera) or into a station's hangar ('dock'). False while the ship can't go
        /// (already leaving, destroyed).</summary>
        public bool TeleportOut(int station, bool dock)
        {
            if (Leaving || (Health != null && Health.Dead)) return false;
            if (dock)
            {
                Session.StationIndex = station;
                Dock(false);
                return true;
            }
            Leaving = true;
            Weapons?.StoreAmmo();
            if (station == Session.VoidOrbit && !Layout.alienOrbit) Session.VoidReturnStation = Layout.stationIndex;
            Session.PreviousStationIndex = Layout.stationIndex;
            Session.StationIndex = station;
            Session.ArrivedByTravel = false;
            Session.LaunchedFromStation = false;
            Session.ProgrammedStation = -1;
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
            return true;
        }

        /// <summary>Remake multiplayer (NetTeleport): a teleport within this orbit moves the ship in place, the autopilot off.
        /// False while something else flies it (mining, object docking, a planet jump, a script's camera, dead): the orbit
        /// is loaded again instead.</summary>
        public bool MoveForTeleport(Vector3 position, Quaternion rotation)
        {
            if (Leaving || Player == null || (Health != null && Health.Dead) || (Mining != null && Mining.State != Flight.Mining.Phase.Idle)
                || (Docking != null && Docking.Busy) || (Navigation != null && Navigation.Jumping) || chase == null || chase.scriptCamera)
                return false;
            Navigation?.SetAutopilot(null);
            MovePlayer(position, rotation);
            return true;
        }

        /// <summary>The station's volumes (collision.json) and the visible jumpgate's sphere (see Obstacle).</summary>
        void AddObstacles()
        {
            OrbitBuilder.AddObstacles(Layout, Station, Jumpgate);
            // Remake mods: a station of its own model sizes the dock range and the launch point.
            var own = Modding.ModWorld.ModelOf(Layout.stationIndex);
            var obstacle = Station != null ? Station.GetComponent<Obstacle>() : null;
            if (own != null && Modding.ModStations.Built(own.station) && obstacle != null)
            {
                float radius = obstacle.cubeHalf / M - 5000f;   // AddObstacles: the bounding radius + 5000 units
                StationDockRange = Mathf.Max(DockRange, radius + 6000f);
                UndockPoint = new Vector3(OrbitLayout.UndockPosition.x, OrbitLayout.UndockPosition.y, Mathf.Max(OrbitLayout.UndockPosition.z, radius + 3000f));
            }
        }

        float farClip = 300000f * M;   // the level's far plane (m), see SetupCamera

        void SetupCamera()
        {
            if (mainCamera == null) mainCamera = Camera.main;
            mainCamera.nearClipPlane = 20f * M;
            // StarSystem::render: 300000, 450000 in the alien orbit before mission 0x50 (the Void's fighters sit up to
            // 100000 out and the wormhole reopens 60000-100000 out while the player arrives 170000-220000 out).
            farClip = (Layout != null && Layout.alienOrbit && Story.Index < 0x50 ? 450000f : 300000f) * M;
            mainCamera.farClipPlane = farClip;
            mainCamera.clearFlags = CameraClearFlags.Skybox;
        }

        // Level::createPlayer + the undock branch of Level::init.
        /// <summary>Remake debug (the Debug page's Ships tab, PlayerHull): the player's hull replaced in flight where it is:
        /// the new model, its flight stats, the chase camera's distance and the model-bound parts (exhaust particles, cloak,
        /// mounted turret) rebuilt; the old hull's turrets go, the new one's come (the capital ships, ships 45 / 51).
        /// Session.ShipIndex and PlayerHull's pick are set first (the model is PlayerHull.Assembly).</summary>
        public void SwapPlayerShip(int shipIndex)
        {
            var ctrl = Player;
            if (ctrl == null) return;
            int before = Session.ShipIndex;
            Session.ShipIndex = shipIndex;
            var prefab = AssembledObject.LoadPrefab(PlayerHull.Assembly(db, shipIndex));
            if (prefab == null) { Session.ShipIndex = before; return; }
            var root = ctrl.gameObject;
            PlayerHull.RemoveTurrets(this);   // before the old model goes (they ride on it)
            if (ctrl.visualModel != null) Destroy(ctrl.visualModel.gameObject);
            var model = Instantiate(prefab, root.transform, false);
            model.GetComponent<AssembledObject>()?.SetPlayerVariant(true);
            foreach (var lg in model.GetComponentsInChildren<LODGroup>(true)) lg.ForceLOD(0);
            PlayerHull.PrepareModel(db, model);   // the Void ship at its full size
            ctrl.visualModel = model.transform;

            var ship = db.Ship(shipIndex);
            var equipment = new System.Collections.Generic.List<ItemData>();
            foreach (var e in Session.Equipment) { var it = db.Item(e.item); if (it != null) equipment.Add(it); }
            if (ship != null) ctrl.stats = Database.BuildFlightStats(ship, equipment, Session.ModLevel(3));
            ctrl.stats.cargoAffectsHandling = Session.IsExtreme;
            ctrl.stats.cargoCapacity = Mathf.Max(1, Shop.MaxLoad(db));
            ctrl.stats.cargoLoad = Shop.CargoLoad();
            ctrl.ApplyStats();
            Health?.RefreshLoadout(db, true);   // the new hull's hull points (its armor value, mod 0) and the equipment's pools

            PlayerHull.FitCamera(root.transform, model.transform, chase, farClip);
            PlayerHull.ApplyMass(ctrl, model.transform, chase);   // a capital ship flies like one
            Weapons?.Rebuild(db, shipIndex, Session.Equipment);   // the new hull's mounts (none for 13 / 14 / 15 / capital ships)

            foreach (var ex in root.GetComponents<ShipExhaust>()) Destroy(ex);
            if (PlayerHull.OwnEngines(db)) ShipExhaust.Attach(root, db, ctrl, shipIndex);   // a freighter / capital ship: none
            if (Cloak != null) Destroy(Cloak);
            Cloak = PlayerCloak.Attach(root, db, shipIndex, Health.Target, model.transform);
            if (Navigation != null) Navigation.Cloak = Cloak;
            if (Collision != null) Collision.cloak = Cloak;
            PlayerTurret.RemoveAll(turrets);
            turrets.Clear();
            turrets.AddRange(PlayerTurret.AttachAll(root, db, shipIndex, Session.Equipment, chase));

            PlayerHull.AttachTurrets(this);
        }

        void SpawnPlayer()
        {
            var ship = db.Ships.Find(s => s.index == Session.ShipIndex);
            var root = new GameObject($"Player ({ship?.name})");
            if (Session.ArrivedByTravel)
            {
                var arrival = ArrivalPosition();
                var facing = Quaternion.LookRotation(-arrival.normalized, Vector3.up);
                // Level::init: arriving in Loma's black market (system 25) puts the player 40 000 further along game +z.
                if (Layout.systemIndex == 25) arrival += OrbitLayout.ToUnity(new Vector3(0f, 0f, 40000f));
                root.transform.SetPositionAndRotation(arrival, facing);
            }
            else
                root.transform.SetPositionAndRotation(
                    OrbitLayout.ToUnity(UndockPoint),
                    OrbitLayout.RotationToUnity(new Vector3(0f, (Random.value < 0.5f ? 1 : -1) * OrbitLayout.UndockYaw / 65536f * 2f * Mathf.PI, 0f)));
            // Multiplayer: an arena's spawn ring. (Players launching together were set 80 m apart by client id: a server's
            // client ids are never reused, so after a few dozen joins players launched 2-3 km to the side of the station.)
            if (NetArenaClient.InMatch) { var spawn = NetArenaClient.SpawnPose(); root.transform.SetPositionAndRotation(spawn.position, spawn.rotation); }
            // Multiplayer: answering a squadmate's distress call, next to them (NetDistress).
            if (NetGame.Active && Session.ArrivedByTravel && NetDistress.ArrivalNear(Layout.stationIndex, out var near, out var nearFacing))
                root.transform.SetPositionAndRotation(near, nearFacing);
            // LevelScript::LevelScript 0x160380: at Coromesk (103) from campaign 0x55 on (or at 0x87), outside the Void, every
            // start is at (70000, 0, 100000) facing the station (the mining plant stands at the origin from then on).
            int cm = Session.CampaignMission;
            if ((cm > 0x54 || cm == 0x87) && Layout.stationIndex == 103 && Layout.systemIndex >= 0)
            {
                var start = OrbitLayout.ToUnity(new Vector3(70000f, 0f, 100000f));
                root.transform.SetPositionAndRotation(start, Quaternion.LookRotation(-start.normalized, Vector3.up));
            }
            // Remake multiplayer: an admin's teleport into this orbit (NetTeleport) brings its own pose.
            if (NetTeleport.TakePose(out var tpPos, out var tpRot)) root.transform.SetPositionAndRotation(tpPos, tpRot);
            var ctrl = root.AddComponent<ShipController>();
            var equipment = new System.Collections.Generic.List<ItemData>();
            foreach (var e in Session.Equipment) { var it = db.Item(e.item); if (it != null) equipment.Add(it); }
            if (ship != null) ctrl.stats = Database.BuildFlightStats(ship, equipment, Session.ModLevel(3));   // mod 3: handling +0.2 per level
            // PlayerEgo ctor: +0x235 = Status::hardCoreMode(), the cargo load then weighs on the handling.
            ctrl.stats.cargoAffectsHandling = Session.IsExtreme;
            ctrl.stats.cargoCapacity = Mathf.Max(1, Shop.MaxLoad(db));
            ctrl.stats.cargoLoad = Shop.CargoLoad();
            ctrl.sensitivity = Settings.Sensitivity;
            ctrl.invertPitch = Settings.InvertPitch;
            ctrl.invertYaw = Settings.InvertYaw;
            ctrl.ApplyStats();

            var entry = PlayerHull.Assembly(db, Session.ShipIndex);   // remake debug: the Ships tab's pick (else the ship's own)
            var prefab = AssembledObject.LoadPrefab(entry);
            if (prefab != null)
            {
                var model = Instantiate(prefab, root.transform, false);
                model.GetComponent<AssembledObject>()?.SetPlayerVariant(true);
                // AEGeometry::updateLod always picks LOD 0 in this binary: the player's ship keeps full detail, or its
                // lights (LOD 0 only) popped in as a cutscene camera closed in.
                foreach (var lg in model.GetComponentsInChildren<LODGroup>(true)) lg.ForceLOD(0);
                PlayerHull.PrepareModel(db, model);   // remake debug: the Void ship at its full size
                ctrl.visualModel = model.transform;
            }
            Player = ctrl;

            // Level::createPlayer: one gun per equipped weapon on the ship's mounts (weapons_hd.json).
            Weapons = root.AddComponent<WeaponSystem>();
            Weapons.Setup(db, Session.ShipIndex, Session.Equipment);

            chase = mainCamera.GetComponent<ChaseCamera>();
            if (chase == null) chase = mainCamera.gameObject.AddComponent<ChaseCamera>();
            chase.target = ctrl;
            // TargetFollowCamera offsets (game local) -> Unity local (-x, y, z) * 0.05.
            chase.offset = new Vector3(0f, 600f, -1338f) * M;
            chase.lookOffset = new Vector3(0f, 600f, -650f) * M;
            // Remake debug: a freighter's or capital ship's hull is far bigger than any ship the camera was made for.
            if (PlayerHull.Big) PlayerHull.FitCamera(root.transform, ctrl.visualModel, chase, farClip);
            PlayerHull.ApplyMass(ctrl, ctrl.visualModel, chase);   // remake debug: a capital ship flies like one (0 for every ordinary ship)
            // CameraSetPerspective(1.22 rad) is the vertical FOV: with the level look offset the ship then sits in the
            // lower middle of the screen like in the original. Used as the 16:9 value (Hor+ on wider screens). Remake: the
            // field of view option (Settings.OriginalFov by default).
            chase.baseFov = Settings.FieldOfView;
            chase.boostFovAdd = 0.35f * Mathf.Rad2Deg;   // MGame::OnUpdate: + 0.35 rad x the boost percentage
            chase.positionCoefficient = 0.006f;         // TargetFollowCamera::resetShipHandling: position / look-at
            chase.rotationCoefficient = 0.005f;
            chase.Snap();
            // PlayerEgo::checkForTurret: the turret-slot item on the ship's turret mount (remake: each on its own mount).
            turrets.Clear();
            turrets.AddRange(PlayerTurret.AttachAll(root, db, Session.ShipIndex, Session.Equipment, chase));
            // MGame::switchCamera: the camera button's modes (standard / turret / free look).
            FreeLook = FreeLookCamera.Attach(root, chase, Turret);
            // Level::createGasClouds: the Supernova plasma clouds (a spectral filter mounted).
            GasClouds = GasCloudField.Spawn(db, Layout, ctrl, Turret);
            if (Radar != null) Radar.Clouds = GasClouds;   // the spectral filters' markers and lock
            Extender = TimeExtender.Attach(root, db);

            // Asteroid mining (lock, autopilot approach, minigame): needs a drill (category 19) to lock.
            Mining = root.AddComponent<Mining>();
            Mining.Setup(db, ctrl, Weapons, chase);
            // MGame::OnInitialize: the engine loop (PlayerEgo+0x1c) and the boost sound (+0xd4).
            PlayerEngine.Attach(root, db, ctrl);
            // Level::initParticleSystems: the exhaust particles (remake debug: none on a freighter / capital ship, PlayerHull).
            if (PlayerHull.OwnEngines(db)) ShipExhaust.Attach(root, db, ctrl, Session.ShipIndex);

            if (Session.LaunchedFromStation || Session.ArrivedByTravel) StartLaunchCamera();
        }

        /// <summary>Level::init with initStreamOutPosition (space_level_setup.md 4): into the gate orbit at the hidden gate
        /// (landmark 2); else 4 x the planet billboard (-20000 * dir) of the station the player came from, or (0, 0, 100000)
        /// when that station isn't in this system.</summary>
        Vector3 ArrivalPosition()
        {
            if (Layout.hasJumpgate || Layout.alienOrbit) return OrbitLayout.ToUnity(Layout.hiddenJumpgate);
            var from = Layout.planets.Find(p => p.station == Session.PreviousStationIndex);
            if (from == null) return OrbitLayout.ToUnity(new Vector3(0f, 0f, 100000f));
            return OrbitLayout.ToUnity(-4f * OrbitLayout.BackdropDistance * OrbitLayout.Direction(from.pitch, from.yaw));
        }

        /// <summary>Level::createCampaignMission / LevelScript: the level puts the player somewhere else (Unity pose); the launch /
        /// arrival camera is placed again around the new pose.</summary>
        public void MovePlayer(Vector3 position, Quaternion rotation)
        {
            Player.transform.SetPositionAndRotation(position, rotation);
            chase.Snap();
            if (launchCameraMs <= 0f) return;
            bool travel = launchTravel;
            float Side() => (Random.value < 0.5f ? -1f : 1f) * (travel ? Random.Range(500, 1000) : Random.Range(500, 2500));
            var local = new Vector3(-Side(), Side(), travel ? 7000f : 9000f) * M;
            mainCamera.transform.position = Player.transform.TransformPoint(local);
            mainCamera.transform.rotation = Quaternion.LookRotation(Player.transform.position - mainCamera.transform.position, Player.transform.up);
            PlaceArrivalWormhole();
        }
        bool launchTravel;
        /// <summary>Level::initStreamOutPosition: this level began with an arrival by travel (planet jump, gate, wormhole).</summary>
        public bool StreamOutArrival => launchTravel;

        // LevelScript::LevelScript: TargetFollowCamera in look-at mode at playerPos + playerRotation * (+-(500..2499),
        // +-(500..2499), 9000) (arrival by travel: +-(500..999), 7000); the chase camera takes over after 7000 ms.
        void StartLaunchCamera()
        {
            Session.LaunchedFromStation = false;
            bool travel = Session.ArrivedByTravel;
            launchTravel = travel;
            Session.ArrivedByTravel = false;
            float Side() => (Random.value < 0.5f ? -1f : 1f) * (travel ? Random.Range(500, 1000) : Random.Range(500, 2500));
            var local = new Vector3(-Side(), Side(), travel ? 7000f : 9000f) * M;   // ship-local game -> Unity (-x, y, z)
            mainCamera.transform.position = Player.transform.TransformPoint(local);
            mainCamera.transform.rotation = Quaternion.LookRotation(Player.transform.position - mainCamera.transform.position, Player.transform.up);
            mainCamera.fieldOfView = Aspect.VerticalFov(chase.baseFov, mainCamera.aspect);
            chase.enabled = false;
            launchCameraMs = LaunchCameraMs;
            Player.inputLocked = true;
            if (Weapons != null) Weapons.Blocked = true;
        }

        /// <summary>The end of the start sequence (LevelScript +0x24 > 7000): the chase camera and the controls come back, then
        /// the autopilot to a programmed station. 'skipped': the camera snaps behind the ship instead of easing there (the
        /// launch camera option off, a level opening on its own cutscene); a player's skip eases like the natural end.</summary>
        void EndLaunchCamera(bool skipped)
        {
            launchCameraMs = 0f;
            Player.inputLocked = false;
            if (Weapons != null) Weapons.Blocked = false;
            if (chase.scriptCamera) return;   // a level script's cutscene camera owns it (its Release brings the chase back)
            chase.enabled = true;   // eases from here to the chase position
            if (skipped) chase.Snap();
            if (Session.ProgrammedStation >= 0 && !Session.InstantJump) Navigation?.ContinueToProgrammedStation();
        }

        /// <summary>A level that opens on its own cutscene (LevelScript ctor, index 78 / 81): no launch / arrival camera.</summary>
        public void EndStartSequence()
        {
            if (launchCameraMs > 0f) EndLaunchCamera(true);
        }

        /// <summary>MGame::OnTouchEnd -> LevelScript::skipSequence: the player tried to fly (steer, throttle, boost, fire, a
        /// tap, any key or button except the pause and autopilot-menu ones) during the start sequence.</summary>
        /// <summary>Any key but Esc and the flight menus' (Q / E / V / K by default, GameControls), a click, a tap, a stick or a
        /// controller button but Menu and the menus' (View, D-pad left) this frame (also the station's skip for the hangar
        /// flights). The menu keys do nothing during the fly-in (FlightHud), so they don't skip it either.</summary>
        public static bool PlayerTriedToFly()
        {
            var kb = GoF2Remake.Multiplayer.NetChat.Keys;
            bool menuKey = GameControls.AutopilotMenu.WasPressedThisFrame() || GameControls.ActionsMenu.WasPressedThisFrame()
                           || GameControls.Wingmen.WasPressedThisFrame() || GameControls.KhadorDrive.WasPressedThisFrame();
            if (kb != null && kb.anyKey.wasPressedThisFrame && !kb.escapeKey.wasPressedThisFrame && !menuKey) return true;
            var mouse = Mouse.current;
            if (mouse != null && (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame)) return true;
            var touch = Touchscreen.current;
            if (touch != null && touch.primaryTouch.press.wasPressedThisFrame) return true;
            var pad = Gamepad.current;
            if (pad != null)
            {
                if (pad.leftStick.ReadValue().sqrMagnitude > 0.25f || pad.rightStick.ReadValue().sqrMagnitude > 0.25f) return true;
                if (pad.rightTrigger.wasPressedThisFrame || pad.leftTrigger.wasPressedThisFrame) return true;
                foreach (var c in pad.allControls)
                    if (c is ButtonControl b && b.wasPressedThisFrame && b != pad.startButton && !menuKey) return true;
            }
            return false;
        }

        void LateUpdate()
        {
            if (Player == null || (Health != null && Health.Dead)) return;
            if (!InDockRange) leftDockRange = true;
            // MGame::dockEvent: the autopilot to the station docks within 16000 units or on touching the station (collision
            // is off during the launch).
            if (Navigation != null && Navigation.GoingToStation && (InDockRange || Collision.TouchingStation) && launchCameraMs <= 0f && Layout.hasStation && StationShown)
            {
                if (DockingBlocked) { Navigation.Refuse(); return; }   // 525 "Not possible on a mission."
                if (!PlayerHull.PlayerShip)   // remake debug: no hangar takes a freighter / capital ship
                {
                    Navigation.Refuse(string.Format(Localization.Extra("hullNoDock", "The {0} doesn't fit in the hangar."), PlayerHull.Label));
                    return;
                }
                Dock(true);
                return;
            }
            if (launchCameraMs <= 0f) return;
            // A level script took the camera (the prologue / rescue scripts own it from the start): the fly-in is over.
            if (chase.scriptCamera) { EndLaunchCamera(true); return; }
            // Remake option: no launch / arrival camera = skipped at once (the camera never showed: it snaps behind the ship).
            if (!Settings.LaunchCamera) { EndLaunchCamera(true); return; }
            // LevelScript::skipSequence 0x16f57c only sets the fly-in clock to 7001: the next frame ends it like the natural
            // end (setLookAtCam(false), no resetCamera), so the chase camera eases in from where the camera is.
            if (Time.timeScale > 0f && PlayerTriedToFly()) { EndLaunchCamera(false); return; }
            launchCameraMs -= Time.deltaTime * 1000f;
            var cam = mainCamera.transform;
            cam.rotation = Quaternion.LookRotation(Player.transform.position - cam.position, Player.transform.up);
            if (launchCameraMs <= 0f) EndLaunchCamera(false);
        }

        /// <summary>The last save (auto-save slot) after a failed mission, or the main menu without one.</summary>
        public void LoadLastSave()
        {
            if (Session.LoadAutosave() && Application.CanStreamedLevelBeLoaded(stationScene)) SceneManager.LoadScene(stationScene);
            else SceneManager.LoadScene(0);
        }

        /// <summary>MGame::dockEvent: straight to the station module (SetCurrentApplicationModule(5)). 'flyIn': the player
        /// docked (the autopilot, touching the station, the Dock prompt), so the station opens with the remake's hangar
        /// fly-in; the story's own moves into a station (the rescue, arrests, "docked at ...") pass false.</summary>
        public void Dock(bool flyIn = false)
        {
            // Remake debug (PlayerHull): a hull the player can't normally fly never lands in a hangar. The player's own docking
            // is refused (CanDock and the autopilot already refuse it); the story's moves into a station put the player
            // back in their own ship first.
            if (!PlayerHull.PlayerShip)
            {
                if (flyIn)
                {
                    Navigation?.Refuse(string.Format(Localization.Extra("hullNoDock", "The {0} doesn't fit in the hangar."), PlayerHull.Label));
                    return;
                }
                PlayerHull.ForceOwnShip();
            }
            Leaving = true;
            Session.LaunchedFromStation = false;
            Session.DockedFromSpace = flyIn;
            Weapons?.StoreAmmo();   // MGame::dockEvent saves the ship state to Status
            if (Application.CanStreamedLevelBeLoaded(stationScene)) SceneManager.LoadScene(stationScene);
        }
    }
}
