// StationLevel.cs
// The docked-station scene (ModStation, module 5): builds the hangar (the main station view) and the bar
// (Space Lounge) of the current station and switches between them instantly, like the original.
// Research and all constants: Reference/research/station_interior.md, tables in StationTables.
//   Hangar (CutScene 0x17 / Level::createScene 0xc2910): room per hangar index at identity, the player's ship on a
//     turntable at (0, Y[ship], 0) (NPC engine meshes, exhaust off), 0..N parked ships on fixed slots; camera from the
//     phone table relative to the ship pivot with a slow random position drift (ModStation::OnUpdate 0xed2a8);
//     one fixed light from the camera side plus a per-race ambient (ModStation::resetLight 0xe9f1c); Vossk fog.
//   Bar (SpaceLounge 0x197890): room per race (no rotation = Unity yaw 180), one visitor per bar agent (Stock.agents,
//     AgentGenerator) on random slots as
//     camera-facing billboards with a glow behind and a floor shadow (updateScreenPositions 0x19ec30); camera eases
//     from A to B in 3 s on the first visit, then sways around the room origin (SpaceLounge::update 0x19ef20); lit by
//     the system's sun (StarSystem::initLight); Terran service bot loops, the Midorian prop replays now and then; remake:
//     the Vossk bar's two bartender arms the original never shows replay every 20 / 22 s (VosskBarBots).
//   Modified: the Terran hangar's five service bots (a to d and the remake's e) fly one at a time, in random order (TerranHangarBots); so do the
//     Nivelian hangar's three, which the original never shows.
// Both show the current system's sky behind the room (Level::createSpace builds the StarSystem for these levels).
// Music per station/race, ambience per screen (Station_Atmo_Mainview / _Lounge).
// Arrival also rolls or refreshes the station's shop stock (Shop.EnterStation); the shop itself is HangarWindow.
// Remake-only hangar flights (Settings.HangarFlights): docking from space flies the player's ship in through the
// forcefield onto the turntable (the station menu, its conversations and windows wait until it has landed),
// launching lifts it off and flies it out before the flight level loads (HangarFlight); the other ships come and go
// on the parked slots meanwhile (HangarTraffic). The camera keeps the original framing (the rooms are only modelled
// where it looks).
// Not yet: turret on the player ship (CutScene::checkForTurret),
// home-base stored ships.

using System;
using System.Collections.Generic;
using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Visuals;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

namespace GoF2Remake.World
{
    public enum StationView { Hangar, Lounge }

    [DefaultExecutionOrder(-100)]
    public partial class StationLevel : MonoBehaviour
    {
        const float M = OrbitLayout.MetersPerUnit;
        const float TwoPi = Mathf.PI * 2f;

        [Header("Scene")]
        public Camera mainCamera;
        [Tooltip("LIGHT0: the fixed hangar light, the system's sun in the bar.")]
        public Light keyLight;
        public string spaceScene = "Space";

        [Header("Station (-1 = Session)")]
        public int stationOverride = -1;
        public int shipOverride = -1;

        [Header("Bar visitors (single-mesh prefabs, set by the scene builder)")]
        public GameObject visitorTerranMale;
        public GameObject visitorTerranFemale;
        public GameObject visitorVossk;
        public GameObject visitorNivelian;
        public GameObject visitorMultipod;
        public GameObject visitorBobolan;
        public GameObject visitorGrey;
        public GameObject visitorGlow;
        public GameObject visitorShadow;
        [Tooltip("Glow material per bar race (materials 34800..34803).")]
        public Material[] glowMaterials = new Material[4];

        [Header("Audio")]
        public AudioSource musicSource;

        /// <summary>Remake: the Mod Builder plays a sound, voice line or track to choose it; the station's music fades out
        /// meanwhile (0.3 s) and stays out 3 s after the last one (the user's request: the next one is often played right
        /// after), then fades back in over a second. The station's own: gone with the scene.</summary>
        public bool MusicHeld { get; set; }
        float musicHold = 1f, musicHeldUntil;
        const float MusicHoldAfter = 3f;
        [Tooltip("Indexed by StationTables.Music.")]
        public AudioClip[] music = new AudioClip[7];
        public AudioClip mainViewAmbience;
        public AudioClip[] mainViewAdds;
        public AudioClip loungeAmbience;
        public AudioClip[] loungeAdds;
        [Tooltip("95 Station_Atmo_Hangar, while the hangar window is open (ModStation::OnKeyPress).")]
        public AudioClip hangarAmbience;
        public AudioClip[] hangarAdds;

        [Header("Tuning (not recovered constants)")]
        [Tooltip("URP intensity of the hangar light (original: diffuse 1.0, half the space sun's 2.0).")]
        public float hangarLightIntensity = 1.1f;
        [Tooltip("Scales the hangar ambient ((race ambient + GL global 0.2) * material ambient 0.7).")]
        public float hangarAmbientScale = 1f;
        public float sunIntensityAt2 = 1.6f;

        public StationView View { get; private set; } = StationView.Hangar;
        public event Action ViewChanged;
        public StationData Station { get; private set; }
        public OrbitLayout Layout { get; private set; }
        public int HangarIndex { get; private set; }
        public int BarRace { get; private set; }
        public int VisitorCount { get; private set; }
        public Database Database => db;
        /// <summary>This station's shop stock and ships for sale (kept while it is among the last 3 visited).</summary>
        public StationStock Stock { get; private set; }
        public bool IntroPlaying => introT < 1.25f && View == StationView.Lounge;

        Database db;
        int dbRevision;                       // ModManager.Revision when db was loaded
        /// <summary>The station's game database (the Customize panel's stats).</summary>
        public Database Db => db;
        Transform hangarRoot, barRoot, playerShip;
        int shipIndex;
        /// <summary>The ship on the turntable (ReplacePlayerShip).</summary>
        public int ShownShip => shipIndex;
        Vector3 shipPivot;                    // Unity, the camera parent (0, Y[ship at entry], 0)

        // Turntable (ModStation +0xe0 / 120 px per rad), game yaw in radians.
        float shipYaw, flingVelocity;         // rad/s

        // Hangar camera drift: three EaseInOuts around the table position (game units).
        readonly EaseInOut[] drift = { new EaseInOut(), new EaseInOut(), new EaseInOut() };
        readonly bool[] driftSign = new bool[3];
        Vector3 hangarCamBase;

        // Bar camera: intro A -> B (EaseInOutMatrix, t 0.75 -> 1.25), then sway.
        Vector3 barPosA, barPosB;
        Quaternion barRotA, barRotB;
        float introT = 0.75f;
        bool loungeVisited;
        readonly EaseInOut swayYaw = new EaseInOut();
        float swaySpeed = 2f, bobPhase;

        readonly List<Visitor> visitors = new List<Visitor>();
        PartAnimation midorianProp;
        float midorianTimer;
        // Remake: the Vossk bar's bartender arms (VosskBarBots): bot_a / bot_c the end arm's two animations, bot_b the second arm.
        VosskBarBots vosskBots;
        PartAnimation vosskBotA, vosskBotB, vosskBotC;
        bool vosskBotsPosed;
        // Modified: the Terran hangar's service bots (TerranHangarBots): each bot's _anim and _anim_add layers, a to d;
        // the Nivelian hangar's a to c the same way.
        TerranHangarBots terranBots;
        readonly List<List<PartAnimation>> terranBotParts = new List<List<PartAnimation>>();
        CycleSound mainViewAtmo, loungeAtmo, hangarAtmo;
        bool hangarWindowOpen;

        class Visitor
        {
            public Transform body, glow, shadow;
            public Vector3 feet; public Agent agent; public float height;
            // Modified: the figure's and the glow's drawn centres in their own space (the quads aren't centred on the pivot),
            // so the glow can sit right behind the figure and the shadow right under it (UpdateBillboards).
            public Vector3 bodyCentre, glowCentre;
        }

        /// <summary>The bar's agents with their visitor billboards (in Stock.agents order).</summary>
        public int VisitorAgentCount => visitors.Count;
        public Agent VisitorAgent(int i) => visitors[i].agent;
        /// <summary>World position just above the visitor's head (name labels, taps).</summary>
        public Vector3 VisitorHead(int i) => visitors[i].feet + Vector3.up * visitors[i].height;
        public Vector3 VisitorFeet(int i) => visitors[i].feet;
        public Camera MainCamera => mainCamera;

        HangarFlight playerFlight;
        HangarTraffic traffic;
        Action afterDeparture;
        bool departed;
        /// <summary>The player's ship is flying in or out (the station menu hides and waits).</summary>
        public bool PlayerFlying => playerFlight != null || departed || pendingDeparture != null;
        /// <summary>The player's ship on the turntable (VR: grabbed to turn it).</summary>
        public Transform PlayerShip => playerShip;
        /// <summary>VR: a visitor picked with the laser (the lounge opens their chat, LoungePanel).</summary>
        public System.Action<int> VisitorPicked;
        /// <summary>The player's ship is taking off or gone (multiplayer: the others in this hangar see it leave).</summary>
        public bool PlayerDeparting => (playerFlight != null && !playerFlight.arriving) || departed || pendingDeparture != null;
        /// <summary>The hangar's ship traffic (multiplayer: the other players' ships too, NetHangar), null = none.</summary>
        public HangarTraffic Traffic => traffic;
        /// <summary>This docking flew in from the orbit (multiplayer: the others see it land; else the ship just appears).</summary>
        public bool ArrivedFlying { get; private set; }

        /// <summary>AEEngine EaseInOut (0x7aa34): a + (b - a) * (sin(phi) * 0.5 + 0.5), phi 3pi/2 -> 5pi/2, Increase(d) adds
        /// d / 65536 * 2pi, so a whole leg takes 32768 units of d.</summary>
        class EaseInOut
        {
            float from, to, phi = 2.5f * Mathf.PI;
            public float Target => to;
            public float Value => from + (to - from) * (Mathf.Sin(phi) * 0.5f + 0.5f);
            public void Start(float a, float b) { from = a; to = b; phi = 1.5f * Mathf.PI; }
            public void Increase(float d) => phi = Mathf.Min(phi + d / 65536f * TwoPi, 2.5f * Mathf.PI);
        }

        // ---- build -------------------------------------------------------------------------------------------

        /// <summary>Remake mods: docked inside the orbit (EVERSPACE 2's dock, Es2Dock): this scene runs on top of the space level,
        /// whose station model is the hangar (hangarRoot rides on its hull) and whose camera shows it.</summary>
        SpaceLevel orbit;
        bool orbitDocked;
        public bool DockedInOrbit => orbitDocked;

        void Awake()
        {
            // Remake mods: a station docked EVERSPACE 2's way is docked at inside its orbit: loaded on its own (a save, a story
            // move, a station reload), the flight level starts docked on its pad and loads this scene on top.
            orbit = FindAnyObjectByType<SpaceLevel>();
            orbitDocked = orbit != null && orbit.DockedInOrbit && orbit.ActiveEs2 != null;
            bool noRedirect = Session.Es2NoRedirect;
            Session.Es2NoRedirect = false;
            if (!orbitDocked && !noRedirect && stationOverride < 0 && !Modding.ModBuilder.Active && !GoF2Remake.Data.NetSession.SessionLost
                && Modding.ModInteriors.Es2DockOf(Session.StationIndex) != null && Application.CanStreamedLevelBeLoaded(spaceScene))
            {
                enabled = false;
                foreach (var go in gameObject.scene.GetRootGameObjects()) if (go != gameObject) go.SetActive(false);
                Session.Es2StartDocked = true;
                Session.LaunchedFromStation = Session.ArrivedByTravel = false;
                SceneManager.LoadScene(spaceScene);
                return;
            }
            Story.ReopenUnshownSuccess();   // #88
            Modding.ModShips.ModelsChanged += OnModShipModels;
            Modding.ModTraffic.Warm();   // remake mods: more kit parts for the NPCs' random builds, loaded while docked
            // Multiplayer: loaded after its session ended (a docking queued behind the menu): on to the menu (nothing is
            // saved, SaveGame.SessionGame).
            if (GoF2Remake.Data.NetSession.SessionLost) SceneManager.LoadScene("MainMenu");
            db = Database.Load();
            dbRevision = Modding.ModManager.Revision;
            int station = stationOverride >= 0 ? stationOverride : Session.StationIndex;
            Hangar.NewDocking();
            if (stationOverride < 0) Session.LastDockedStation = station;   // where a multiplayer pilot starts and respawns
            Stock = Shop.EnterStation(db, station);
            PirateReports.Refresh(db);   // remake: the Pirate activity board's old reports off, new ones in
            Story.OnDocked(db, station, Stock);   // ModStation::OnInitialize's story tweaks (index 1: Betty ...)
            // Remake debug (PlayerHull): a hull the player can't normally fly never sits in a hangar (an old save from the
            // battleship toggle): back in the player's own ship.
            if (!PlayerHull.PlayerShip) PlayerHull.ForceOwnShip();
            Hangar.SyncBuiltIn(db);  // remake mods: the flown ship's built-in items (ships.json "integrated": "items")
            Hangar.FitToSlots(db);   // remake: no more mounted than the ship has slots (a mod's lower slot count, a debug swap)
            shipIndex = shipOverride >= 0 ? shipOverride : Session.ShipIndex;
            Station = db.Stations.Find(s => s.index == station);
            Layout = OrbitLayout.Build(db, station);
            // Remake mods: a station may name the race of its hangar and bar ("interior", ModWorld).
            int interior = Modding.ModWorld.InteriorRace(station);
            HangarIndex = StationTables.HangarIndex(station, interior >= 0 ? interior : Layout.raceId);
            // Remake mods: a custom hangar / bar room (interiors.json, ModInteriors); the race's tables stay for the rest.
            customHangar = Modding.ModInteriors.HangarOf(station);
            customBar = Modding.ModInteriors.BarOf(station);
            // The Mod Builder previews one of its project's rooms in place of the station's (ModBuilder.PreviewInterior).
            if (Modding.ModBuilder.Active && Modding.ModInteriors.Built(Modding.ModBuilder.PreviewInterior) is Modding.ModInteriors.Room preview)
            {
                if (preview.def.hangar) customHangar = preview; else customBar = preview;
            }
            Session.VisitedStations.Add(station);   // Galaxy::setVisited: the star map's "Already visited"
            // Docking repairs the ship (the original launches with Status hull / shield / armor = -1, "full", StarMap::
            // depart; assumed for every launch) and autosaves (ModStation::autosave).
            // Survivor medal: the hull % this ship arrived with (before the repair).
            int maxHull = Modding.ModShipKits.Armor(db.Ship(Session.ShipIndex), 100) + 40 * Session.ModLevel(0);
            Session.LastArrivalHullPercent = Session.PlayerHull < 0 ? 100 : Mathf.RoundToInt(100f * Session.PlayerHull / Mathf.Max(1, maxHull));
            Session.ArrivedWithoutGear = Achievements.NoWeaponOrEquipment(db);   // Harum-Scarum: the loadout docked with
            Session.HighestCredits = Mathf.Max(Session.HighestCredits, Session.Credits);
            Session.PlayerHull = Session.PlayerArmor = -1;
            Session.PlayerShield = -1f;
            // The gamma pool too: launching sets Status +0x68 to -1 (ModStation::OnTouchEnd, StarMap::depart), so a docked
            // supernova station (Var Lupra 112 ...) sends you out with a full pool; kept, a nearly empty one killed you again.
            Session.PlayerGamma = -1f;
            // Modified: not when the station opens from a loaded save (Session.ArrivedByLoad): only a docking autosaves; loading
            // a manual slot wrote it straight over the autosave.
            if (!Session.ArrivedByLoad && Story.AutosaveAllowed(station)) Session.Autosave();
            BarRace = StationTables.BarRace(interior >= 0 ? interior : Layout.raceId);
            if (mainCamera == null) mainCamera = Camera.main;
            if (orbitDocked)
            {
                // the orbit's own camera, sky, backdrop, lights and post-processing show the dock; this scene's step aside
                if (mainCamera != null) mainCamera.gameObject.SetActive(false);
                foreach (var v in FindObjectsByType<Volume>(FindObjectsInactive.Include)) if (v.gameObject.scene == gameObject.scene) v.enabled = false;
                if (keyLight != null) keyLight.enabled = false;
                mainCamera = orbit.MainCamera;
            }
            else
            {
                ApplyAntialiasing();
                SetupDepthOfField();
                Settings.Changed -= ApplyAntialiasing;
                Settings.Changed += ApplyAntialiasing;

                OrbitBuilder.SetupSky(Layout);   // the system's sky shows through the openings of both rooms
                // Modified: no swell or streak in the station: the hangar camera facing the sun through an opening blew it up
                // like a sun at the crosshair in space (the user's request; Backdrop.NoSwell).
                OrbitBuilder.SpawnBackdrop(Layout, mainCamera).NoSwell = true;
                // Remake: the orbit's sky layers outside too, as in space: the planet ring sky (stations 120, 126, 130, 132), the
                // supernova flares (the stronger "nasty" ones from mission 106) and the storms in the supernova system, the asteroid
                // belt of systems 24-26 (Skavac, Loma, Shima) lit by the system's sun.
                SkyLayers.Spawn(Layout, mainCamera);
            }
            BuildHangar();
            BuildBar();
            // Remake: ships now and then flying past outside the bar's windows (not at an evacuated station).
            if (!Evacuated && !orbitDocked) gameObject.AddComponent<BarFlybys>().Setup(this, barRoot, barPosB, barRotB, BarRace == 1, RandomParkedShip,
                                                       ship => SpawnShip(ship, Vector3.zero, Quaternion.identity, barRoot, "Flyby"));
            Debug.Log($"StationLevel: station {station} {Station?.name} ({Station?.systemName}), hangar {HangarIndex}, " +
                      $"bar {BarRace}, ship {shipIndex}, {VisitorCount} visitors");

            if (musicSource != null)
            {
                var clip = music != null && music.Length > 0 ? music[(int)StationTables.MusicFor(station, Layout.raceId)] : null;
                // Globals::playMusicAndFadeOutCurrent(0): station 10 at campaign 0x9f plays 144 OutroSong.
                if (station == 10 && !Session.FreePlay && Session.CampaignMission == 0x9f && StoryAssets.Load()?.outroSong != null) clip = StoryAssets.Load().outroSong;
                if (clip == null && music != null && music.Length > 0) clip = music[0];
                clip = Modding.ModWorld.StationMusic(station) ?? Modding.ModMusic.Replace(clip);   // remake mods: a station's / system's own, a replaced track
                musicSource.clip = clip;
                musicSource.loop = true;
                musicSource.volume = Settings.MusicVolume * GoF2Remake.World.SpaceHooks.SceneMusic;
                if (clip != null) musicSource.Play();
            }
            SetView(StationView.Hangar, true);

            // Remake: docked from space = fly in through the forcefield (not after loading a save, a new game, a reload).
            // (a station docked EVERSPACE 2's way has its ship on the pad already: World.Es2Dock landed it in its orbit)
            bool flyIn = Session.DockedFromSpace && Settings.HangarFlights && !Es2Docked;
            Session.DockedFromSpace = false;
            ArrivedFlying = flyIn;
            var lane = Lane;
            if (flyIn && lane != null && playerShip != null)
            {
                var engine = HangarFlight.AddEngine(playerShip.gameObject, true, db, shipIndex, out float volume);
                playerFlight = HangarFlight.Arrival(playerShip, lane, playerShip.position, playerShip.rotation, engine, volume);
                playerFlightStart = Time.unscaledTime;
            }
        }

        StationTables.HangarLane Lane => customHangar != null ? CustomLane
            : HangarIndex >= 0 && HangarIndex < StationTables.HangarLanes.Length ? StationTables.HangarLanes[HangarIndex] : null;

        // ---- remake mods: a custom hangar / bar (ModInteriors) ----

        Modding.ModInteriors.Room customHangar, customBar;
        StationTables.HangarLane customLane;

        int captureSavedIndex = -1, captureSavedRace;
        Modding.ModInteriors.Room captureSavedCustom;

        bool enginePreview;
        float enginePreviewDrive = 1f;

        GameObject turret, hardpoints;
        string turretItems = "";

        readonly List<HangarTraffic.Parked> parkedShips = new List<HangarTraffic.Parked>();

        bool[] slotTaken;

        void OnDestroy()
        {
            if (orbitDocked && hangarRoot != null) Destroy(hangarRoot.gameObject);   // it rode on the station in the orbit's scene
            Modding.ModShips.ModelsChanged -= OnModShipModels;
            Settings.Changed -= ApplyAntialiasing;
            if (dofProfile != null) Destroy(dofProfile);
            var copies = new HashSet<Material>();
            foreach (var (_, _, matte) in roomHighlights) foreach (var m in matte) copies.Add(m);
            foreach (var (_, lit, _) in roomHighlights) foreach (var m in lit) copies.Remove(m);
            foreach (var m in copies) if (m != null) Destroy(m);
        }

        // ---- depth of field (remake option "Hangar depth of field", Settings.HangarDepthOfField) ---------------------------

        /// <summary>The volumes' layer only the station camera listens to (the star map's and the item window's cameras,
        /// which share the scene, keep their default mask): the turntable's focus distance would blur them.</summary>
        const int DofLayer = 26;
        Volume dofVolume;
        VolumeProfile dofProfile;
        UnityEngine.Rendering.Universal.DepthOfField dof;

        /// <summary>URP's depth of field focused on the player's ship in the hangar view: Bokeh (a 300 mm lens at f/2: at the
        /// ~130 m the hangar cameras keep from the turntable the ~60 m ship stays sharp, the room behind it goes soft),
        /// Gaussian on phones (cheaper; the background only). The focus follows the ship (the camera drifts, the hangar
        /// flights move it).</summary>
        bool bokehDof;   // the hangar view's mode (Bokeh, phones Gaussian); the orbit always Gaussian (UpdateDepthOfField)

        // The room's URP Lit materials with and without their specular highlights (CollectRoomHighlights).
        readonly List<(Renderer renderer, Material[] lit, Material[] matte)> roomHighlights = new List<(Renderer, Material[], Material[])>();
        bool roomMatte;

        // ---- turntable (ModStation::OnTouchMove 0xea340 / OnTouchEnd / OnUpdate) ----------------------------

        /// <summary>Modified: the VoidX (ship 8): its hull's middle sits ~18 m off its pivot along its length (the model's centre
        /// at -355 units, the other hulls' within ~85), so on the pad it stood off the turntable and swung around it.</summary>
        public const int OffCentreShip = 8;
        /// <summary>The pad pivot the player's ship was spawned at, and its hull's middle in its own frame (horizontal; zero for
        /// the hulls that stand on their pivot).</summary>
        Vector3 padPivot, padCentre;

        // ---- remake: Inspect ship (HangarInspect; UI.StationMenu reads the input) ----------------------------

        /// <summary>The hangar's orbit camera around the player's ship.</summary>
        public HangarInspect Inspect { get; } = new HangarInspect();

        Transform roomCollidersFor;
        readonly List<MeshCollider> roomColliders = new List<MeshCollider>();
        bool loggedUnreadableRoom;
        /// <summary>Not ModBuilderViewInteriors.RoomLayer (21): its colliders come and go with that view (and cover the glow
        /// layers too).</summary>
        const int RoomColliderLayer = 25;

        Transform shipCollidersFor;
        const int ShipColliderLayer = 20;
        const int FreeCameraMask = (1 << RoomColliderLayer) | (1 << ShipColliderLayer);

        /// <summary>Modified: the camera's sphere for the casts below (m). It was 1.5 m: a sphere cast skips a collider it starts
        /// in, and each room is one MeshCollider, so a sphere starting in contact (the free camera on its floor, the hull's
        /// lowest point + 1.5 m; the orbit's centre over a small ship's pad) skipped every wall. HangarInspect's near plane
        /// (WallNear) stays under it.</summary>
        const float CameraRadius = 0.4f;

        bool introSkip;

        float playerFlightStart;
        /// <summary>A take-off waiting for the camera's blend back from the orbit (Depart).</summary>
        Action pendingDeparture;

        // ---- per frame ---------------------------------------------------------------------------------------

        void Update()
        {
            float dtMs = Time.deltaTime * 1000f;
            if (pendingDeparture != null && !Inspect.Shown)
            {
                var then = pendingDeparture;
                pendingDeparture = null;
                Depart(then);
            }
            if (playerFlight != null)
            {
                playerFlight.Hold = traffic != null && traffic.Busy;
                playerFlight.Update(Time.deltaTime);
                if (playerFlight.Done)
                {
                    bool arrived = playerFlight.arriving;
                    playerFlight = null;
                    if (arrived) ApplyShipYaw();
                    else
                    {
                        departed = true;   // keeps the menu hidden until the scene changes
                        var then = afterDeparture;
                        afterDeparture = null;
                        then?.Invoke();
                    }
                }
            }
            traffic?.Update(dtMs, playerFlight != null || departed);
            if (View == StationView.Hangar && flingVelocity != 0f)
            {
                shipYaw += flingVelocity * Time.deltaTime;
                // Modified: a fast flick keeps the ship turning and slows it gently (to 35 % a second; the original's x0.9 per
                // 20 ms frame stopped it within half a second), down to a crawl before it stops (it snapped still at 1 px a
                // frame, ~24 deg/s). How much of the speed is left after a second, and the speed it stops at (rad/s):
                const float FlingKeptPerSecond = 0.35f, FlingStopSpeed = 0.03f;
                flingVelocity *= Mathf.Pow(FlingKeptPerSecond, dtMs / 1000f);
                if (Mathf.Abs(flingVelocity) < FlingStopSpeed) flingVelocity = 0f;
                ApplyShipYaw();
            }

            if (View == StationView.Lounge && midorianProp != null && (midorianTimer += dtMs) > 2000f)
            {
                midorianTimer = 0f;
                if (Random.value < 0.3f) midorianProp.Restart();
            }
            if (View == StationView.Lounge && vosskBots != null) UpdateVosskBots(dtMs);
            if (View == StationView.Hangar && terranBots != null) UpdateTerranBots(dtMs);

            if (View == StationView.Hangar) RefreshTurret(false);
            if (MusicHeld) musicHeldUntil = Time.unscaledTime + MusicHoldAfter;
            bool held = Time.unscaledTime < musicHeldUntil;
            musicHold = Mathf.MoveTowards(musicHold, held ? 0f : 1f, Time.unscaledDeltaTime / (held ? 0.3f : 1f));
            if (musicSource != null) musicSource.volume = Settings.MusicVolume * GoF2Remake.World.SpaceHooks.SceneMusic * musicHold;
            float atmoMs = Time.unscaledDeltaTime * 1000f;
            mainViewAtmo?.Update(atmoMs); loungeAtmo?.Update(atmoMs); hangarAtmo?.Update(atmoMs);
        }

        void LateUpdate()
        {
            UpdateCamera(Time.deltaTime * 1000f);
            UpdateDepthOfField();
            if (View == StationView.Lounge) UpdateBillboards();
        }

        // ---- remake: the Mod Builder's free camera (its rooms' editor) ----

        /// <summary>The rooms as built (the hangar's and the Space Lounge's), for the Mod Builder.</summary>
        public Transform HangarRoom { get; private set; }
        public Transform BarRoom { get; private set; }

        /// <summary>The Mod Builder holds the camera (its room editor's orbit): this pose and lens instead of the room's.</summary>
        public bool FreeCamera { get; set; }
        public Pose FreeCameraPose { get; set; }
        public float FreeCameraFov { get; set; } = 50f;

        /// <summary>Remake: where the Inspect ship orbit puts the ship across the screen, from the left (0) to the right (1);
        /// 0.5 = the middle. The Customize screen sets it to the middle of the part its panel leaves free.</summary>
        public float InspectFrameX = 0.5f;
        float inspectFrame = 0.5f;
        const float InspectFrameMs = 450f;

        /// <summary>Remake: the bar camera's zoom toward the visitor talked to (the share of the field of view taken off), eased
        /// over ChatZoomMs.</summary>
        const float ChatZoomIn = 0.25f, ChatZoomMs = 800f;
        /// <summary>Where the visitor talked to stands across the screen, from the left (0) to the right (1): the middle of
        /// the room beside the chat window, left of it or (the window on the left) right of it (LoungePanel sets it from the
        /// window's place).</summary>
        public float ChatFrameX = 0.24f;
        int chatVisitor = -1;
        Vector3 chatTarget;
        float chatZoom;

        Bounds vrShipBounds;

    }
}
