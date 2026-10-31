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
using GoF2Remake.Visuals;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.SceneManagement;

namespace GoF2Remake.World
{
    [DefaultExecutionOrder(-100)]
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public partial class SpaceLevel : MonoBehaviour
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
        /// <summary>The Supernova Challenge's orbit (SupernovaChallenge), else null.</summary>
        public ChallengeOrbit Challenge { get; private set; }
        /// <summary>The ship's turrets, in mount order (remake: more than one on a custom ship with several turret mounts).</summary>
        public System.Collections.Generic.IReadOnlyList<PlayerTurret> Turrets => turrets;
        readonly System.Collections.Generic.List<PlayerTurret> turrets = new System.Collections.Generic.List<PlayerTurret>();
        GameObject hardpoints;   // a kit ship's weapon models (Modding.ModHardpoints)
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
                                      || GoF2Remake.World.SpaceHooks.NoDocking;   // an event's Restrict Travel
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
        public bool Cutscene => (Campaign != null && Campaign.Cutscene) || GoF2Remake.World.SpaceHooks.Cinematic || Es2Busy;
        /// <summary>Remake mods: the station's dock when it docks EVERSPACE 2's way (null: GoF2's docking).</summary>
        public Es2Dock Es2 { get; private set; }
        /// <summary>The dock being docked at, docked at or taken off from (the orbit's station's, or a mobile station's).</summary>
        public Es2Dock ActiveEs2 { get; private set; }
        bool Es2Busy => ActiveEs2 != null && ActiveEs2.Running;
        /// <summary>Remake mods: the mobile stations here that dock EVERSPACE 2's way, and the points their landmarks follow.</summary>
        readonly System.Collections.Generic.Dictionary<MobileStation, Es2Dock> mobileDocks = new System.Collections.Generic.Dictionary<MobileStation, Es2Dock>();
        readonly System.Collections.Generic.Dictionary<Transform, MobileStation> mobileDockPoints = new System.Collections.Generic.Dictionary<Transform, MobileStation>();
        Es2Dock MobileDock(MobileStation m) => m != null && mobileDocks.TryGetValue(m, out var d) ? d : null;
        /// <summary>Remake mods: docked on the station's pad inside this orbit (EVERSPACE 2's way): the Station scene runs on
        /// top (its menu, the hangar view through this level's camera), the orbit lives on around it.</summary>
        public bool DockedInOrbit { get; private set; }
        /// <summary>The level's camera (the station's menu drives it while docked inside the orbit).</summary>
        public Camera MainCamera => mainCamera;
        readonly System.Collections.Generic.List<AudioSource> dockPausedAudio = new System.Collections.Generic.List<AudioSource>();
        public Transform Asteroids { get; private set; }
        public Backdrop Backdrop { get; private set; }
        public GameObject Station { get; private set; }
        /// <summary>Remake mods: the mobile stations cruising through this orbit now (Modding.MobileStations).</summary>
        public readonly System.Collections.Generic.List<MobileStation> Mobiles = new System.Collections.Generic.List<MobileStation>();
        /// <summary>The mobile station this level was launched from (its orbit is where it is now), -1 = none.</summary>
        int launchFromMobile = -1;
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
        public bool CanDock => ((Layout.hasStation && StationShown && leftDockRange && InDockRange) || MobileInRange != null)
                               && Player != null && launchCameraMs <= 0f && (Health == null || !Health.Dead)
                               && !DockingBlocked && PlayerHull.PlayerShip   // remake debug: no hangar for a freighter / capital ship
                               && (Mining == null || Mining.State == Mining.Phase.Idle);
        /// <summary>The station is there (a level script may hide it: 78's Valkyrie jumping away); nothing docks at a hidden one.</summary>
        bool StationShown => Station == null || Station.activeInHierarchy;
        bool InDockRange => Es2 != null ? Es2.InZone(Player.transform.position)   // EVERSPACE 2: the dock's Interact box
                                         : Player.transform.position.sqrMagnitude < StationDockRange * M * StationDockRange * M;
        /// <summary>Remake mods: a mobile station whose dock range the player is in (having left it once since the level began).</summary>
        MobileStation MobileInRange
        {
            get
            {
                if (Player == null) return null;
                foreach (var m in Mobiles)
                    if (m != null && m.LeftRange && (MobileDock(m) is Es2Dock d ? d.InZone(Player.transform.position) : m.InRange(Player.transform.position))) return m;
                return null;
            }
        }

        Database db;
        public Database Database => db;
        ChaseCamera chase;
        float launchCameraMs;
        /// <summary>This level is an arrival out of interstellar space (Session.WarpArrival): no fly-in camera, the flight goes on.</summary>
        bool warpArrival;
        /// <summary>... flown in, the orbit shown on the way (SystemApproach).</summary>
        bool flownIn;
        bool leftDockRange, orbitInfo;

        void OnEnable()
        {
            Settings.Changed += ApplyOptions;
            Modding.ModShips.ModelsChanged += OnModShipModels;
        }

        void OnDisable()
        {
            Settings.Changed -= ApplyOptions;
            Modding.ModShips.ModelsChanged -= OnModShipModels;
            if (savedMaxDelta > 0f) Time.maximumDeltaTime = savedMaxDelta;
        }

        /// <summary>Remake mods: a customizable ship's parts arrived (loaded on demand, ModShips.EnsureBuild): the player's model
        /// becomes the build if it showed the default build meanwhile.</summary>
        void OnModShipModels()
        {
            if (this == null || Player == null || Modding.ModShipKits.CustomizationOf(Session.ShipIndex) == null) return;
            string want = PlayerHull.Assembly(db, Session.ShipIndex)?.name;
            string shown = Player.visualModel != null ? Player.visualModel.name.Replace("(Clone)", "") : null;
            // ... or its exhaust style's flame model arrived (ModExhausts: loaded on demand).
            bool exhaust = Player.visualModel != null && Modding.ModExhausts.Find(Modding.ModShipKits.PlayerBuild(Session.ShipIndex)?.exhaust, Session.ShipIndex) != null
                           && PlayerHull.OwnEngines(db) && !Modding.ModExhausts.HasHolder(Player.visualModel);
            if ((want != null && want != shown) || exhaust) SwapPlayerShip(Session.ShipIndex);
        }

        /// <summary>Options changed in flight (the pause menu) reach the ship and the chase camera at once.</summary>
        void ApplyOptions()
        {
            if (Player != null) { Player.sensitivity = Settings.Sensitivity; Player.invertPitch = Settings.InvertPitch; Player.invertYaw = Settings.InvertYaw; }
            if (chase != null) chase.baseFov = Settings.FieldOfView;
        }

        float savedMaxDelta = -1f;

        // ---- the seamless swap (into and out of interstellar space) ------------------------------------------------

        static Scene replaced;
        static Database prepared;
        static int preparedField;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetSwap() { replaced = default; prepared = null; preparedField = 0; }

        /// <summary>The next arrival's tables, read while the ship flies in (SystemApproach), not in the frame of the swap, and
        /// the seed of the asteroids it has been shown.</summary>
        public static void PrepareArrival(int asteroidSeed)
        {
            prepared = Database.Load();
            preparedField = asteroidSeed;
        }

        /// <summary>Remake: this level for another with no stall to hide. A plain scene change also sweeps every unused asset,
        /// which with a player's mods loaded held the frame for most of a second (measured 2026-10-31: ~600 ms of ~870, the level
        /// itself ~170); an additive load has no sweep. The old level goes as the new one's Awake starts (ClearReplaced), so the
        /// two never run together; a docking's plain scene change still sweeps.</summary>
        public static void SwapScene(string scene)
        {
            replaced = SceneManager.GetActiveScene();
            SceneManager.sceneLoaded += OnSwapped;
            SceneManager.LoadScene(scene, LoadSceneMode.Additive);
        }

        static void ClearReplaced()
        {
            if (!replaced.IsValid() || !replaced.isLoaded) return;
            foreach (var root in replaced.GetRootGameObjects()) DestroyImmediate(root);
        }

        /// <summary>The new scene takes over: RenderSettings are the active scene's, so its sky and lights are set again.</summary>
        static void OnSwapped(Scene scene, LoadSceneMode mode)
        {
            SceneManager.sceneLoaded -= OnSwapped;
            var old = replaced;
            replaced = default;
            SceneManager.SetActiveScene(scene);
            if (old.IsValid() && old.isLoaded) SceneManager.UnloadSceneAsync(old);
            FindAnyObjectByType<SpaceLevel>()?.ApplySkyAndLights();
        }

        void ApplySkyAndLights()
        {
            OrbitBuilder.SetupSky(Layout, ambientIntensity);
            LightFor(Layout);
        }

        /// <summary>The scene's lights as this orbit has them.</summary>
        public void LightFor(OrbitLayout orbit) => OrbitBuilder.SetupLights(orbit, sunLight, planetLight, sunIntensityAt2, planetLightIntensity);

        void Awake()
        {
            ClearReplaced();
            Story.ReopenUnshownSuccess();   // #88
            HullCollision.Suppressed = false;   // the main menu's backdrop had them off (whichever scene's code runs first)
            Session.ArrivedByLoad = false;   // a load's no-fine docking is only the station it opened
            // Multiplayer: loaded after its session ended (a docking queued behind the menu): on to the menu (nothing is
            // saved, SaveGame.SessionGame).
            if (GoF2Remake.Data.NetSession.SessionLost) SceneManager.LoadScene("MainMenu");
            var ready = Session.WarpArrival ? prepared : null;
            prepared = null;
            db = ready ?? Database.Load();
            // MGame::OnUpdate: a frame's dt is capped at 150 ms (a hitch never jumps the game far ahead).
            savedMaxDelta = Time.maximumDeltaTime;
            if (Time.maximumDeltaTime > 0.15f) Time.maximumDeltaTime = 0.15f;
            int station = stationOverride >= 0 ? stationOverride : Session.StationIndex;
            // Multiplayer arena match (NetArenaClient): the template orbit's sky, sun and planets, its own orbit id.
            var arena = GoF2Remake.World.SpaceHooks.Arena;
            if (arena != null) { station = arena.template; Session.ArrivedByTravel = Session.ArrivedBySystemJump = false; }
            // Remake: the space between the systems (InterstellarSpace): the orbit left, emptied; none of it is arrived in.
            bool interstellar = Session.EnterInterstellar && arena == null;
            Session.EnterInterstellar = false;
            bool plainOrbit = arena == null && !interstellar;
            warpArrival = Session.WarpArrival && plainOrbit;
            flownIn = warpArrival && ready != null;
            Session.WarpArrival = false;
            // Remake mods: launched from a mobile station (Modding.MobileStations): into the orbit it cruises through now.
            if (plainOrbit && stationOverride < 0 && Modding.ModWorld.IsMobile(station))
            {
                launchFromMobile = station;
                int host = Modding.MobileStations.HostNow(station);
                station = Session.StationIndex = host >= 0 ? host : Modding.ModWorld.RefugeStation;
            }
            ComingFromVoid = Session.ComingFromVoid;
            // Status::departStation: the Void-invasion re-roll counter (index 32-44).
            if (!interstellar) Story.OnDepart(db, station);
            // Status::departStation's station stack: an attacked station's locals calm down 3 new orbits later (a player's report).
            if (plainOrbit) Session.ArriveInOrbit(station);
            // ... and the shops' stack: a station new to it gets its stock here, not only when docking (a player's report).
            if (plainOrbit) Shop.ArriveInOrbit(db, station);
            // Modified: MGame::OnInitialize 0x1a76a8 visits the orbit's station in space for the orbits with no docking: Naneroh
            // (109), Valpatro (110), 132-134, the dropship orbits 102-104 and Luur (111) from step 0x5e (Station::visit); the
            // remake only marked a station visited on docking, so those systems never got the star map's tick (a player's report).
            if (plainOrbit && VisitedInSpace(station)) Session.VisitedStations.Add(station);
            // Status::moveWanted: a real orbit change (not a launch from the docked station, not to / from the Void) moves the
            // Most Wanted criminals one system along their routes.
            if ((Session.ArrivedByTravel || Session.ArrivedBySystemJump) && station != Session.VoidOrbit && Session.PreviousStationIndex != Session.VoidOrbit)
                WantedBoard.Move(db, station, Session.ProgrammedStation);
            Layout = OrbitLayout.Build(db, station);
            if (interstellar) InterstellarSpace.Clear(Layout);
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
            bool prologue = plainOrbit && station == 78 && !Session.FreePlay && Story.Index <= 1;
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
            if (!interstellar) { SpawnWormhole(); AddObstacles(); }
            // Remake mods: the mobile stations in this orbit now (not in an arena or the prologue).
            if (plainOrbit && !prologue) Mobiles.AddRange(MobileStation.SpawnAll(db, Layout));
            // Multiplayer (NetGame): the orbit's field from the world seed, the same for every player here (NetOrbit).
            if (!GoF2Remake.Data.NetSession.Active) Asteroids = ready != null && warpArrival ? OrbitBuilder.SpawnAsteroids(db, Layout, preparedField, null) : OrbitBuilder.SpawnAsteroids(db, Layout);
            else SpawnNetworkAsteroids(GoF2Remake.Data.NetSession.OrbitSeed(NetOrbitId));
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
            if (!interstellar) PlaceArrivalWormhole();
            OrbitBuilder.SpawnDust(Layout);
            var backdrop = OrbitBuilder.SpawnBackdrop(Layout, mainCamera);
            if (!interstellar) SkyLayers.Spawn(Layout, mainCamera);   // ring sky, storms, supernova flares, asteroid belt
            Backdrop = backdrop;

            // Locks on the station, the jumpgate and the other stations' planets; autopilot, planet jump, fast-forward.
            Navigation = Player.gameObject.AddComponent<Navigation>();
            Navigation.Setup(db, Layout, backdrop, Player, Mining, chase, Weapons);
            Navigation.StationObject = Station;
            // the flight goes on across the load
            if (warpArrival || interstellar) Navigation.Drive?.CarrySpeed(warpArrival ? WarpSky.ArrivalCarryMs : WarpSky.EntryCarryMs);
            foreach (var m in Mobiles) Navigation.AddMobileStation(m.transform, m.Station, m.Label, m.TechLevel);
            // Remake mods: a station docked EVERSPACE 2's way: its landmark and the autopilot are its dock; a launch from it
            // takes off from the pad (Es2Dock).
            if (plainOrbit && !prologue && Layout.hasStation) Es2 = Es2Dock.Attach(Station, Layout.stationIndex, Player, chase);
            if (Es2 != null && Navigation.Targets.Find(x => x.kind == Navigation.Kind.Station && x.transform == null) is Navigation.Target dockTarget)
                dockTarget.fixedPosition = Es2.DockPoint;
            foreach (var m in Mobiles)
            {
                if (m == null || Es2Dock.Attach(m.gameObject, m.Station, Player, chase) is not Es2Dock md) continue;
                mobileDocks[m] = md;
                var point = md.MakeDockPoint();
                mobileDockPoints[point] = m;
                if (Navigation.Targets.Find(x => x.transform == m.transform) is Navigation.Target mt) mt.transform = point;
            }
            void OnTookOff()
            {
                // the take-off's end, like the launch camera's: on to a programmed station (the map's destination)
                if (Session.ProgrammedStation >= 0 && !Session.InstantJump) Navigation?.ContinueToProgrammedStation();
            }
            if (Es2 != null) Es2.TookOff += OnTookOff;
            foreach (var md in mobileDocks.Values) md.TookOff += OnTookOff;
            // a launch from a station docked EVERSPACE 2's way (the orbit's, or the mobile station's it was launched from)
            var launchDock = launchFromMobile >= 0 ? MobileDock(Mobiles.Find(m => m != null && m.Station == launchFromMobile)) : Es2;
            if (Session.Es2TakeOff)
            {
                Session.Es2TakeOff = false;
                ActiveEs2 = launchDock;
                launchDock?.BeginTakeOff();
            }
            if (Session.Es2StartDocked)
            {
                // the station scene was loaded on its own (a save, a story move): docked on the pad, the station on top
                Session.Es2StartDocked = false;
                if (launchDock != null) { ActiveEs2 = launchDock; launchDock.HoldDocked(); DockInOrbit(); }
                else if (Application.CanStreamedLevelBeLoaded(stationScene)) { Leaving = true; Session.Es2NoRedirect = true; SceneManager.LoadScene(stationScene); }
            }
            Mining.navigation = Navigation;
            SystemJump = Player.gameObject.AddComponent<SystemJump>();
            SystemJump.Setup(db, Navigation, Player, Weapons, chase, Jumpgate);

            // Ship combat: the player's Player object, the orbit's NPC traffic, the ship / salvage locks.
            Health = Player.gameObject.AddComponent<PlayerHealth>();
            Health.Setup(db, Player, chase, Weapons);
            if (Navigation != null) Health.Hit += Navigation.InterruptDrive;   // remake: a hit drops a cruise drive
            if (Player.visualModel != null)   // NPC shots hit its real shape (with Original ship collision its cube)
                HullCollision.Attach(Player.visualModel.gameObject, Health.Target, null, HullCollision.FlyableShip(Session.ShipIndex));
            Collision = Player.gameObject.AddComponent<PlayerCollision>();
            Collision.Setup(Health, chase, Mining);
            Collision.wormhole = Wormhole;
            Collision.wormholeHeld = () => WormholeHeld;
            Collision.wormholeNoPull = () => WormholeNoPull;
            VolatileCargo.Attach(Player.gameObject, db, Player);   // PlayerEgo+0x398: volatile goods, sound 35
            bool storyOrbit = !interstellar && !Session.FreePlay && Story.IsLevelMission(station);
            IsStoryOrbit = storyOrbit;
            // Status::departStation: the freelance mission's target orbit is built around it (not over a story orbit).
            // Multiplayer: not when a squadmate here already runs this mission (their ships are shown here, NetMissions).
            bool missionHere = !storyOrbit && plainOrbit && Freelance.IsMissionOrbit(station);
            freelanceMissionOrbit = missionHere;
            bool freelanceOrbit = missionHere && GoF2Remake.World.SpaceHooks.RunsMission(station);
            bool missionFollower = missionHere && !freelanceOrbit;   // a squadmate here runs it: its briefing, route, timer, score
            // Level::createMission: the Kaamo Club under siege (kaamo_club.md 3).
            bool siege = !storyOrbit && !missionHere && plainOrbit && KaamoClub.SiegeAt(station);
            // Remake: an event graph quest's orbit (questorbit): no normal traffic, like a story orbit (single player).
            bool questOrbit = !storyOrbit && !missionHere && !siege && plainOrbit && !GoF2Remake.Data.NetSession.Active && GoF2Remake.World.SpaceHooks.IsQuietOrbit(station);
            // Remake: a Mod Builder test flight (Modding.ModBuilder): a quiet orbit with its own pirates (ModBuilderRange).
            // The builder's own work in space (Stations, Weapon fx: ModBuilder.InSpace) too, with nothing flying at all.
            bool builderTest = Modding.ModBuilder.TestFlying || Modding.ModBuilder.InSpace;
            // The Supernova Challenge (mission type 0xb7, SupernovaChallenge): its own Specters and scenery, no traffic.
            bool challenge = SupernovaChallenge.Active && !storyOrbit && plainOrbit && !builderTest && !GoF2Remake.Data.NetSession.Active;
            // Level::assignGuns reads the level mission (Status+400): a campaign level or a freelance mission's type.
            NpcTables.InCampaignLevel = storyOrbit;
            NpcTables.LevelFreelanceType = freelanceOrbit ? Freelance.Mission.type : -1;
            Traffic = new GameObject("Traffic").AddComponent<Traffic>();
            // Multiplayer: only the first player in an empty orbit builds its traffic and runs it; the others show that player's
            // ships (NetOrbit) and take them over if it leaves, never building new ones.
            // An arena: no NPCs, or with the Void fighters the alien orbit's own (run by the first player in the match's orbit).
            NetAuthority = GoF2Remake.Data.NetSession.Active && !interstellar && (arena == null || arena.voids) && GoF2Remake.World.SpaceHooks.IsOrbitEmpty(NetOrbitId);
            ownPassive = storyOrbit || freelanceOrbit || siege || questOrbit || builderTest || challenge || interstellar || (arena != null && !arena.voids);
            Traffic.LevelMissionActive = () => (storyOrbit && Story.IsLevelMission(station)) || (missionHere && Freelance.IsMissionOrbit(station));
            Traffic.Setup(db, Layout, Health.Target, Station, ownPassive || (GoF2Remake.Data.NetSession.Active && !NetAuthority), Wormhole,
                          flownIn && !missionHere && !GoF2Remake.Data.NetSession.Active);
            // Remake: a cruise drive's encounters (NavigationCruiseDrive, CruiseEncounterSpawner): only where the traffic is this
            // player's own and nothing scripted runs (a multiplayer orbit another player runs rolls none).
            Navigation.EncountersAllowed = !ownPassive && !(GoF2Remake.Data.NetSession.Active && !NetAuthority) && !prologue
                                           && CruiseEncounterRules.Allowed(db, station, Layout.systemIndex, Layout.alienOrbit);
            Navigation.SpawnEncounter = (entry, at, forward) => CruiseEncounterSpawner.Spawn(this, entry, at, forward);
            Traffic.LaunchCameraRunning = () => !LaunchCameraOver;
            Traffic.RadarHidden = () => Cutscene;
            // No locks or lock sounds (the scanner off). Remake: also in the planet jump, the jumpgate / Khador Drive scenes and
            // while a level script holds the controls (its cutscene camera moves before or after Cutscene says so).
            Navigation.RadarHidden = () => Cutscene || !LaunchCameraOver || Navigation.Jumping
                || (SystemJump != null && SystemJump.Cinematic) || (Player != null && Player.inputLocked);
            PlayerHull.AttachTurrets(this);   // remake debug: a capital ship's turrets on the player's hull (PlayerHull)
            // Docking at the story's objects (PlayerEgo::dockToDockingPoint): locked through Navigation.
            Docking = Player.gameObject.AddComponent<ObjectDocking>();
            Docking.Setup(db, Player, chase, Weapons);
            if (FreeLook != null)
                FreeLook.Blocked = () => Cutscene || !LaunchCameraOver || (Mining != null && Mining.State != Mining.Phase.Idle)
                                         // the approach is an autopilot leg; docked (not hacking) free look is the view (#132)
                                         || (Docking != null && Docking.Busy && Docking.State != ObjectDocking.Phase.Approach && !DockedLook)
                                         || (Navigation != null && Navigation.Jumping)
                                         || (SystemJump != null && SystemJump.Cinematic) || (Health != null && Health.Dead);
            // MGame::switchCamera / PlayerEgo::setTurretMode refuse the turret view only while docking to an asteroid, mining,
            // for an auto turret and while the Liberator flies: through an object docking's ease-in and docked too (#91/92:
            // the remake refused it from the approach on).
            if (FreeLook != null) FreeLook.TurretAllowed = () => Docking != null && (Docking.State == ObjectDocking.Phase.Entering || Docking.IsDocked)
                                                                 && Docking.Hacking == null && !Cutscene;
            if (FreeLook != null)
            {
                // PlayerEgo::approachDockingPoint / MGame::OnUpdate: docked, the camera goes to free look by itself (not while
                // hacking: setDockingState(2) suppresses it); the undock's end goes back to the standard view (Blocked while
                // leaving).
                FreeLook.DockedView = () => DockedLook;
                Docking.Docked += () => { if (DockedLook) FreeLook.EnterDockedLook(); };
            }
            Navigation.Docking = Docking;
            Navigation.Ships = Traffic.Ships;
            Collision.docking = Docking;
            Docking.HackWon += ship =>
            {
                Traffic.OnHackWon(ship);   // a Supernova wreck's hidden blueprint, a mod's derelict
                SpaceHooks.WreckHacked?.Invoke(Session.StationIndex, ship != null ? ship.Target.displayName ?? "" : "");   // remake: "on hacked"
            };
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
                if (GoF2Remake.Data.NetSession.Active && KaamoSiege.OtherRunsHere(station)) Siege.SetupFollower(this, Traffic);   // one siege per orbit
                else Siege.Setup(this, Traffic);
            }
            else if (challenge)
            {
                Challenge = new GameObject("Supernova Challenge").AddComponent<ChallengeOrbit>();
                Challenge.Setup(this, Traffic);
            }
            // Multiplayer: an Informer mission's spy is this player's when another player built the orbit's traffic (theirs
            // has it only for their own mission) and no squadmate here has one already.
            if (GoF2Remake.Data.NetSession.Active && !NetAuthority && !storyOrbit && plainOrbit && Freelance.Active && Freelance.Mission.type == MissionType.Informer
                && Freelance.Mission.target == station && !Session.InformerKilled && !Session.InformerFailed && !GoF2Remake.World.SpaceHooks.TeamHere(station))
                SpawnInformerSpy();
            // Step 59's arms convoy: its point is the player's route until the freighter is gone.
            if (Traffic.ConvoyRoute != null)
            {
                Navigation.SetRoute(Traffic.ConvoyRoute);
                Traffic.ConvoyDone += () => { Navigation.SetAutopilot(null); Navigation.SetRoute(null); };
            }
            // Level::createWingmen: after the mission's ships (Challenge: unarmed).
            if (plainOrbit && !builderTest && !challenge) Traffic.SpawnWingmen(Player.transform, FreelanceOrbit != null && FreelanceOrbit.Type == MissionType.Challenge);
            Navigation.HasWingmen = () => Traffic != null && Traffic.LivingWingmen.Count > 0;
            StorySpace = gameObject.AddComponent<StorySpace>();
            StorySpace.Setup(this, Campaign);
            Hints = gameObject.AddComponent<FlightHints>();
            Hints.Setup(this);
            Navigation.JumpsBlocked = () => GoF2Remake.World.SpaceHooks.InArena || builderTest || challenge || !Story.PlanetJumpsAllowed || Story.BlocksJumps(Layout.stationIndex, IsStoryOrbit) || (Siege != null && Siege.Active)
                                            || FreelanceBlocks   // a freelance mission's orbit (#32)
                                            || GoF2Remake.World.SpaceHooks.NoJumps   // an event's Restrict Travel
                                            || (!Session.FreePlay && Story.Index == 65 && Layout.stationIndex == 100)   // escorting Khador (MGame::UseKhadorDrive)
                                            || Story.EscapesByWormhole(Layout.stationIndex);   // 42: the wormhole (#84)
            Navigation.JumpRefusalText = () => Story.EscapesByWormhole(Layout.stationIndex) ? WormholeEscapeHint : null;
            // MGame::UseKhadorDrive 0x1a9480 has no Void rule of its own: the mission gate above (Story.BlocksJumps, 525) is the
            // only refusal, and in the alien orbit the drive returns to Status+0x84 (#26: the remake used to refuse it there
            // through the main story).
            Navigation.SetWormhole(Wormhole);
            Navigation.PlanetJumpRefused = st => StorySpace != null && StorySpace.RefusePlanetJump(st);
            // MGame::dockEvent refuses the gate on the same mission check as docking: the story's level missions too.
            SystemJump.GateBlocked = () => builderTest || challenge || Story.BlocksJumps(Layout.stationIndex, IsStoryOrbit) || FreelanceBlocks
                                           || (Siege != null && Siege.Active) || GoF2Remake.World.SpaceHooks.NoJumps;
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
            SpaceHooks.LevelBuilt?.Invoke(this);   // multiplayer: the shared orbit (NetOrbit)
            if (arena != null) GoF2Remake.World.SpaceHooks.ArenaLevelReady?.Invoke();
            if (interstellar) gameObject.AddComponent<InterstellarField>().Setup(this);
            if (interstellar || warpArrival || Navigation.Drive != null) gameObject.AddComponent<WarpSky>().Setup(this, warpArrival, !flownIn, SystemJump);
            if (Modding.ModBuilder.TestFlying) gameObject.AddComponent<ModBuilderRange>().Setup(this);
            if (Modding.ModBuilder.InSpace) HoldForBuilder();
        }

        /// <summary>Remake (the Mod Builder in space, ModBuilder.InSpace): the player's ship parked where it came in, held
        /// still, unharmed and unarmed; the builder's screen holds the camera (UI.ModBuilderSpaceView).</summary>
        void HoldForBuilder()
        {
            Player.externalControl = true;
            Player.inputLocked = true;
            if (Health != null && Health.Target != null) Health.Target.invulnerable = true;
            if (Weapons != null) Weapons.Blocked = true;
            if (chase != null) chase.enabled = false;
        }

        /// <summary>Multiplayer: the orbit as the other players see it: the station's index, or an arena match's own id
        /// (NetArena.OrbitBase + the match), so a match is a private copy of its template orbit.</summary>
        public int NetOrbitId => GoF2Remake.World.SpaceHooks.Arena != null ? GoF2Remake.World.SpaceHooks.Arena.orbitId
                               : Layout != null ? (Layout.interstellar ? InterstellarSpace.OrbitId : Layout.stationIndex) : -1;

        /// <summary>Multiplayer: this player runs the orbit's NPC traffic (the first one here), see NetOrbit.</summary>
        public bool NetAuthority { get; private set; }

        /// <summary>Multiplayer: the server made this player run the orbit (NetOrbit.FollowRunner): its traffic is this
        /// player's now, its relaunches and waves run, unless the level keeps it passive itself (a story, mission or siege
        /// orbit); the old runner's ships come with the server's hand-over (NetOrbit.AdoptHandedOver).</summary>
        public void TakeOverNetAuthority()
        {
            NetAuthority = true;
            Traffic?.SetPassive(ownPassive);
        }

        /// <summary>Multiplayer: another player built this orbit's traffic at the same moment and the server picked them
        /// (NetOrbit.FollowRunner): this player's goes (not its mission ships or wingmen) and theirs is shown instead.</summary>
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
                group = NpcGroup.Local, race = race, ship = NpcTables.RandomFighter(race), position = GoF2Remake.World.SpaceHooks.OutOfSightOf(at), nameText = 1663,
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

        /// <summary>Modified: MGame::OnInitialize's Station::visit in space (not in the alien orbit): stations 109, 110,
        /// 132-134 and 102-104 always, 111 from step 0x5e (Session.WorldIndex, so the finished game's world too).</summary>
        public static bool VisitedInSpace(int station) =>
            station == 109 || station == 110 || (station >= 132 && station <= 134) || (station >= 102 && station <= 104)
            || (station == 111 && Session.WorldIndex >= 0x5e);

        /// <summary>Docked at an object and not hacking (free look allowed, FreeLookCamera.DockedView).</summary>
        bool DockedLook => Docking != null && Docking.IsDocked && Docking.Hacking == null && !Cutscene;

        /// <summary>A cinematic, a jump, the launch camera or the player's death stops the time extender (MGame::OnUpdate 0x1af162).</summary>
        bool ExtenderBlocked => launchCameraMs > 0f || Navigation.Jumping || (SystemJump != null && SystemJump.Cinematic) || Cutscene
                                || Health == null || Health.Dead;

        /// <summary>Modified: what ends a running time extender: a cinematic, the launch camera or the player's death. Not the
        /// jumps any more (the jumpgate / Khador drive scenes, the planet jump): a running slow-down carries on through them
        /// (it can't be started there, ExtenderBlocked) until the next place loads, which ends it (TimeExtender.OnDestroy).</summary>
        bool ExtenderCancelled => launchCameraMs > 0f || Cutscene || Health == null || Health.Dead;

        void Update()
        {
            // MGame::OnUpdate: the wingmen's contract runs down while flying (fast-forward included, not while paused).
            if (Session.Wingmen.Count > 0 && Session.WingmanContractMs > 0f) Session.WingmanContractMs = Mathf.Max(0f, Session.WingmanContractMs - Time.deltaTime * 1000f);
            if (Health == null) return;
            Health.invulnerable = launchCameraMs > 0f || Navigation.Jumping || (SystemJump != null && SystemJump.Cinematic) || Es2Busy || DockedInOrbit
                                  || (Campaign != null && Campaign.PlayerInvulnerable) || (StorySpace != null && StorySpace.SuccessPending)
                                  || GoF2Remake.World.SpaceHooks.Invulnerable;
            Collision.off = launchCameraMs > 0f || Navigation.Jumping || (SystemJump != null && SystemJump.Cinematic) || Es2Busy || DockedInOrbit
                            || (Campaign != null && Campaign.CollisionOff);   // PlayerEgo+0x144
            Collision.ignoreGate = Navigation.GoingToGate;
            // Hostiles, or a radio line on screen, block fast-forward (MGame::OnUpdate).
            Navigation.HostilesPresent = (Traffic != null && (Traffic.HostileCount > 0 || Traffic.ChatterVisible != null)) || (Campaign != null && Campaign.Radio != null && Campaign.Radio.Busy);
            if (Extender != null && ExtenderCancelled) Extender.Cancel();
            if (DockedInOrbit) return;   // docked inside the orbit: no wormhole, no hidden-system leave
            UpdateWormholeRide();
            UpdateHiddenSystem();
        }

        // ---- remake mods: a system that disappears (GalaxyMap.ForcedHidden) ----------------------------------------------

        const float HiddenCheckMs = 500f, HiddenLeaveMs = 4000f;
        float hiddenCheckMs, hiddenMs;

        /// <summary>A system an event / quest hid ("system ... hide") or whose mod's "available" condition stopped holding,
        /// with the player in it: a HUD message, and 4 s of game time later a stream-out arrival at GalaxyMap.FallbackStation
        /// (the nearest system on the map along the gate routes). Not during a cutscene, a jump, the launch camera or death
        /// (the clock waits), nor in the Void, an arena, a story orbit or the Mod Builder's own work.</summary>
        void UpdateHiddenSystem()
        {
            if (Leaving || Layout.alienOrbit || Layout.stationIndex < 0 || IsStoryOrbit || GoF2Remake.World.SpaceHooks.InArena
                || (Modding.ModBuilder.Active && !Modding.ModBuilder.PlayTesting)) { hiddenMs = 0f; return; }
            if (Cutscene || Navigation.Jumping || launchCameraMs > 0f || (SystemJump != null && SystemJump.Cinematic)
                || (Health != null && Health.Dead) || Time.deltaTime <= 0f) return;
            if ((hiddenCheckMs -= Time.unscaledDeltaTime * 1000f) <= 0f)
            {
                hiddenCheckMs = HiddenCheckMs;
                bool hidden = GalaxyMap.ForcedHidden(Layout.systemIndex);
                if (!hidden) { hiddenMs = 0f; hiddenNowhere = false; return; }
                if (hiddenNowhere) return;   // no shown system to go to: the player stays
                if (hiddenMs <= 0f)
                {
                    hiddenMs = 0.001f;
                    Radar?.Say(Localization.Extra("systemClosing", "This system is off the map now: leaving it."), 1);
                }
            }
            if (hiddenMs <= 0f) return;
            hiddenMs += Time.deltaTime * 1000f;
            if (hiddenMs < HiddenLeaveMs) return;
            hiddenMs = 0f;
            int to = GalaxyMap.FallbackStation(db, Layout.systemIndex);
            if (to < 0 || to == Layout.stationIndex) { hiddenNowhere = true; return; }   // nowhere shown: stay (it looped into hidden Mido)
            TravelTo(to);
        }

        /// <summary>The hidden system has no shown system to leave for (GalaxyMap.FallbackStation -1): the player stays.</summary>
        bool hiddenNowhere;

        // ---- the wormhole (landmark 3) -----------------------------------------------------------------------

        bool riding, rode;
        float rideHoldMs;
        /// <summary>The level is being left (wormhole ride, docking): the story checks stop.</summary>
        public bool Leaving { get; private set; }

        // ---- remake events: an event's / mission's wormhole (EventWormhole, /wormhole) ----------------------------

        /// <summary>The event's wormhole in this orbit (null: none).</summary>
        public Wormhole EventWormhole { get; private set; }
        int eventWormholeTo;

        float farClip = 300000f * M;   // the level's far plane (m), see SetupCamera

        bool launchTravel;
        // Modified: the Supernova Challenge's fly-in: the camera starts 150 m in front of the ship, 40 m to its left and 30 m
        // above (game units, ship-local -> Unity (-x, y, z)), watches the ship come at it and glides to the chase position
        // behind it, all in ChallengeFlyInMs (UpdateChallengeFlyIn), then the chase camera goes on from exactly there.
        bool challengeFlyIn;
        static readonly Vector3 ChallengeFlyInLocal = new Vector3(-800f, 600f, 3000f);
        const float ChallengeFlyInMs = 2000f;
        Vector3 challengeFlyStart, challengeFlyShipLast, challengeFlyVelocity;
        float challengeFlyMs;

        void LateUpdate()
        {
            if (Player == null || (Health != null && Health.Dead)) return;
            if (!InDockRange) leftDockRange = true;
            foreach (var m in Mobiles) if (m != null && !m.LeftRange && !m.InRange(Player.transform.position)) m.LeftRange = true;
            // Remake mods: the autopilot to a mobile station docks within its range.
            if (DockedInOrbit || Es2Busy) return;   // EVERSPACE 2's docking / take-off flies the ship
            if (Navigation != null && Navigation.GoingToMobile is Navigation.Target toMobile && launchCameraMs <= 0f
                && (mobileDockPoints.TryGetValue(toMobile.transform, out var viaDock) ? viaDock : Mobiles.Find(m => m != null && m.transform == toMobile.transform)) is MobileStation mobile
                && (MobileDock(mobile) is Es2Dock zone ? zone.InZone(Player.transform.position) : mobile.InRange(Player.transform.position)))
            {
                DockMobile(mobile);
                return;
            }
            // MGame::dockEvent: the autopilot to the station docks within 16000 units or on touching the station (collision
            // is off during the launch).
            if (Navigation != null && Navigation.GoingToStation && (InDockRange || (Es2 == null && Collision.TouchingStation)) && launchCameraMs <= 0f && Layout.hasStation && StationShown)
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
            if (challengeFlyIn) { UpdateChallengeFlyIn(); return; }
            if (launchCameraMs <= 0f) EndLaunchCamera(false);
        }

        GameObject spaceEventSystem;

        void OnDestroy()
        {
            if (DockedInOrbit) SpaceHooks.DockedInOrbit = false;   // (a scene loaded over both: a save, the menu)
        }

    }
}
