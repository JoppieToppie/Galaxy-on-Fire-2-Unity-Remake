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
//     the system's sun (StarSystem::initLight); Terran service bot loops, the Midorian prop replays now and then.
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
    public class StationLevel : MonoBehaviour
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
        Transform hangarRoot, barRoot, playerShip;
        int shipIndex;
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
        CycleSound mainViewAtmo, loungeAtmo, hangarAtmo;
        bool hangarWindowOpen;

        class Visitor { public Transform body, glow; public Vector3 feet; public Agent agent; public float height; }

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
        public bool PlayerFlying => playerFlight != null || departed;
        /// <summary>The player's ship on the turntable (VR: grabbed to turn it).</summary>
        public Transform PlayerShip => playerShip;
        /// <summary>VR: a visitor picked with the laser (the lounge opens their chat, LoungePanel).</summary>
        public System.Action<int> VisitorPicked;
        /// <summary>The player's ship is taking off or gone (multiplayer: the others in this hangar see it leave).</summary>
        public bool PlayerDeparting => (playerFlight != null && !playerFlight.arriving) || departed;
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

        void Awake()
        {
            // Multiplayer: loaded after its session ended (a docking queued behind the menu): on to the menu (nothing is
            // saved, SaveGame.SessionGame).
            if (GoF2Remake.Multiplayer.NetGame.SessionLost) SceneManager.LoadScene("MainMenu");
            db = Database.Load();
            int station = stationOverride >= 0 ? stationOverride : Session.StationIndex;
            Stock = Shop.EnterStation(db, station);
            Story.OnDocked(db, station, Stock);   // ModStation::OnInitialize's story tweaks (index 1: Betty ...)
            // Remake debug (PlayerHull): a hull the player can't normally fly never sits in a hangar (an old save from the
            // battleship toggle): back in the player's own ship.
            if (!PlayerHull.PlayerShip) PlayerHull.ForceOwnShip();
            shipIndex = shipOverride >= 0 ? shipOverride : Session.ShipIndex;
            Station = db.Stations.Find(s => s.index == station);
            Layout = OrbitLayout.Build(db, station);
            HangarIndex = StationTables.HangarIndex(station, Layout.raceId);
            Session.VisitedStations.Add(station);   // Galaxy::setVisited: the star map's "Already visited"
            // Docking repairs the ship (the original launches with Status hull / shield / armor = -1, "full", StarMap::
            // depart; assumed for every launch) and autosaves (ModStation::autosave).
            // Survivor medal: the hull % this ship arrived with (before the repair).
            int maxHull = (db.Ship(Session.ShipIndex)?.armor ?? 100) + (Session.HasMod(0) ? 40 : 0);
            Session.LastArrivalHullPercent = Session.PlayerHull < 0 ? 100 : Mathf.RoundToInt(100f * Session.PlayerHull / Mathf.Max(1, maxHull));
            Session.HighestCredits = Mathf.Max(Session.HighestCredits, Session.Credits);
            Session.PlayerHull = Session.PlayerArmor = -1;
            Session.PlayerShield = -1f;
            if (Story.AutosaveAllowed(station)) Session.Autosave();
            BarRace = StationTables.BarRace(Layout.raceId);
            if (mainCamera == null) mainCamera = Camera.main;
            ApplyAntialiasing();
            Settings.Changed -= ApplyAntialiasing;
            Settings.Changed += ApplyAntialiasing;

            OrbitBuilder.SetupSky(Layout);   // the system's sky shows through the openings of both rooms
            OrbitBuilder.SpawnBackdrop(Layout, mainCamera);
            BuildHangar();
            BuildBar();
            // Remake: ships now and then flying past outside the bar's windows.
            gameObject.AddComponent<BarFlybys>().Setup(this, barRoot, barPosB, barRotB, BarRace == 1, RandomParkedShip,
                                                       ship => SpawnShip(ship, Vector3.zero, Quaternion.identity, barRoot, "Flyby"));
            Debug.Log($"StationLevel: station {station} {Station?.name} ({Station?.systemName}), hangar {HangarIndex}, " +
                      $"bar {BarRace}, ship {shipIndex}, {VisitorCount} visitors");

            if (musicSource != null)
            {
                var clip = music != null && music.Length > 0 ? music[(int)StationTables.MusicFor(station, Layout.raceId)] : null;
                // Globals::playMusicAndFadeOutCurrent(0): station 10 at campaign 0x9f plays 144 OutroSong.
                if (station == 10 && !Session.FreePlay && Session.CampaignMission == 0x9f && StoryAssets.Load()?.outroSong != null) clip = StoryAssets.Load().outroSong;
                if (clip == null && music != null && music.Length > 0) clip = music[0];
                musicSource.clip = clip;
                musicSource.loop = true;
                musicSource.volume = Settings.MusicVolume * Multiplayer.NetScreen.SceneMusic;
                if (clip != null) musicSource.Play();
            }
            SetView(StationView.Hangar, true);

            // Remake: docked from space = fly in through the forcefield (not after loading a save, a new game, a reload).
            bool flyIn = Session.DockedFromSpace && Settings.HangarFlights;
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

        StationTables.HangarLane Lane => HangarIndex >= 0 && HangarIndex < StationTables.HangarLanes.Length ? StationTables.HangarLanes[HangarIndex] : null;

        void BuildHangar()
        {
            hangarRoot = new GameObject("Hangar").transform;
            string room = StationTables.HangarRoom[HangarIndex];
            // Level::createScene: PlayerStatic + setRotation(0, pi, 0) = Unity identity.
            var roomGo = Spawn(room, Vector3.zero, OrbitLayout.RotationToUnity(new Vector3(0f, Mathf.PI, 0f)), hangarRoot, "Room");
            // CutScene::process updates every geometry of the scene each frame, so the room's animated layers all run,
            // not only the *_anim meshes (the Terran hangar_terran_add lights run along the side gutters; at frame 0 they
            // sat on the player's pad). The loops skip their one-off first key (every part at the origin for 33 / 50 ms,
            // a jump on every wrap), the rotations swing back and forth (the Vossk ring lights' 57 deg sweep, clear of the
            // portal, instead of snapping back), and the Vossk portal's light fades out as it moves (its `extra` channel).
            if (roomGo != null)
                foreach (var a in roomGo.GetComponentsInChildren<PartAnimation>(true))
                {
                    a.play = true;
                    a.loopStartMs = a.OneOffStartMs;
                    a.pingPongRotation = true;
                    if (a.gameObject.name.Contains("_anim")) a.applyMaterialChannels = true;
                }

            float y = StationTables.PadPivotY(HangarIndex, -1, shipIndex, Quaternion.identity) / M;   // clear of the pad (remake)
            shipPivot = OrbitLayout.ToUnity(new Vector3(0f, y, 0f));
            var ship = SpawnShip(shipIndex, new Vector3(0f, y, 0f), 0f, hangarRoot, "Player ship");
            playerShip = ship != null ? ship.transform : null;
            if (playerShip != null) PlayerHull.FitHangar(playerShip);   // remake debug: a freighter / capital ship shrunk
            RefreshTurret(true);
            shipYaw = StationTables.StartYaw(HangarIndex);
            ApplyShipYaw();
            // Multiplayer: another player docked here runs the hangar's NPC ships: theirs come with its snapshot (NetHangar).
            bool hangarFollower = GoF2Remake.Multiplayer.NetGame.Active && GoF2Remake.Multiplayer.NetHangar.OtherDockedHere(Layout.stationIndex);
            if (!hangarFollower) SpawnParkedShips();
            // The others come and go (remake), except the club's stored hulls. Multiplayer: the other players docked here park
            // on the slots too (NetHangar), with or without the NPC traffic and the flights.
            bool slots = StationTables.ParkedSlots[HangarIndex] != null && StationTables.ParkedMax[HangarIndex] > 0;
            bool npcTraffic = Settings.HangarFlights && Lane != null && slots && !KaamoClub.StorageAt(Layout.stationIndex);
            if (npcTraffic || (slots && GoF2Remake.Multiplayer.NetGame.Active))
                traffic = new HangarTraffic(Lane, StationTables.ParkedSlots[HangarIndex].Length, StationTables.ParkedMax[HangarIndex],
                                            parkedShips, db, NewParkedShip, ParkedPosition,
                                            (ship, pos, rot) => SpawnShip(ship, pos, rot, hangarRoot, "Visiting ship"),
                                            npcTraffic, Settings.HangarFlights);
            if (traffic != null && GoF2Remake.Multiplayer.NetGame.Active)
            {
                traffic.KeepOneFree = true;          // a pad for another player docking
                traffic.RunsNpcs = !hangarFollower;
            }
            if (GoF2Remake.Multiplayer.NetGame.Active) gameObject.AddComponent<GoF2Remake.Multiplayer.NetHangar>().Setup(this);

            // Camera: ModStation::OnInitialize state 0x14 (phone table), rotation order 2 with roll -0.03.
            hangarCamBase = StationTables.HangarCameraPos[HangarIndex];
            for (int i = 0; i < 3; i++) { driftSign[i] = Random.Range(0, 20) < 10; NextDriftLeg(i, Base(i)); }
        }

        /// <summary>CutScene::replacePlayerShip 0xa53b4: after buying a ship, the new one on the turntable at its own height
        /// (the camera keeps the pivot of the ship the hangar was entered with).</summary>
        public void ReplacePlayerShip(int index)
        {
            if (playerShip != null) Destroy(playerShip.gameObject);
            shipIndex = index;
            var ship = SpawnShip(index, new Vector3(0f, StationTables.PadPivotY(HangarIndex, -1, index, Quaternion.identity) / M, 0f), 0f, hangarRoot, "Player ship");
            playerShip = ship != null ? ship.transform : null;
            if (playerShip != null) PlayerHull.FitHangar(playerShip);   // remake debug: a freighter / capital ship shrunk
            RefreshTurret(true);
            ApplyShipYaw();
        }

        GameObject turret;
        int turretItem = -1;

        /// <summary>CutScene::checkForTurret 0xa4594: the mounted turret on the turntable ship, rebuilt whenever the turret
        /// item changes (the original re-runs it after every equipment change).</summary>
        void RefreshTurret(bool force)
        {
            int item = GoF2Remake.Flight.PlayerTurret.TurretItem(db, Session.Equipment);
            if (!force && item == turretItem) return;
            turretItem = item;
            if (turret != null) Destroy(turret);
            turret = playerShip != null ? GoF2Remake.Flight.PlayerTurret.BuildStatic(db, shipIndex, Session.Equipment, playerShip) : null;
        }

        readonly List<HangarTraffic.Parked> parkedShips = new List<HangarTraffic.Parked>();

        /// <summary>The club's parked hulls changed (Use / Sell in the storage): park them again.</summary>
        public void RefreshParkedShips()
        {
            if (!KaamoClub.StorageAt(Layout.stationIndex)) return;
            foreach (var p in parkedShips) if (p.go != null) Destroy(p.go);
            parkedShips.Clear();
            SpawnParkedShips();
        }

        /// <summary>Level::createScene 0x17: count = nextInt(max + 1), 70 % a fighter of the hangar's race, 30 % a random race
        /// (of those 30 % pirates); random free slot, yaw nextInt(300) / 100 rad. The owned Kaamo Club (kaamo_club.md 6.6)
        /// parks the first min(stored, max) hulls of its storage instead, in list order.</summary>
        void SpawnParkedShips()
        {
            var slots = StationTables.ParkedSlots[HangarIndex];
            int max = StationTables.ParkedMax[HangarIndex];
            if (slots == null || max <= 0) return;
            bool club = KaamoClub.StorageAt(Layout.stationIndex);
            int count = Mathf.Min(club ? Mathf.Min(Session.KaamoShips.Count, max) : Random.Range(0, max + 1), slots.Length);
            if (GoF2Remake.Multiplayer.NetGame.Active && !club) count = Mathf.Min(count, slots.Length - 1);   // a pad for another player
            var taken = new bool[slots.Length];
            for (int n = 0; n < count; n++)
            {
                int ship = club ? Session.KaamoShips[n].ship : NewParkedShip();
                int slot = Random.Range(0, slots.Length), tries = 0;
                while (taken[slot] && ++tries < 100) slot = Random.Range(0, slots.Length);
                if (taken[slot]) break;
                taken[slot] = true;
                float yaw = Random.Range(0, 300) / 100f;
                var pos = new Vector3(slots[slot].x,
                    StationTables.PadPivotY(HangarIndex, slot, ship, OrbitLayout.RotationToUnity(new Vector3(0f, yaw, 0f))) / M, slots[slot].z);
                var parked = SpawnShip(ship, pos, yaw, hangarRoot, $"Parked ship {n}");
                if (parked != null) parkedShips.Add(new HangarTraffic.Parked { go = parked, slot = slot, ship = ship, yaw = yaw });
            }
        }

        int NewParkedShip() => Layout.stationIndex == 100 ? StationTables.DeepScienceShips[Random.Range(0, 3)] : RandomParkedShip();

        /// <summary>The Unity pivot of 'ship' parked on 'slot' at 'rotation' (the slot plus the ship's height, lifted where the
        /// hull would cut into the pad: StationTables.PadPivotY).</summary>
        Vector3 ParkedPosition(int slot, int ship, Quaternion rotation) =>
            new Vector3(0f, StationTables.PadPivotY(HangarIndex, slot, ship, rotation), 0f)
            + Vector3.Scale(OrbitLayout.ToUnity(StationTables.ParkedSlots[HangarIndex][slot]), new Vector3(1f, 0f, 1f));

        int RandomParkedShip()
        {
            int race = HangarIndex;
            if (Random.value >= 0.7f) race = Random.value < 0.3f ? 8 : Random.Range(0, 4);
            var list = race == 8 ? StationTables.PirateFighters : StationTables.RaceFighters[Mathf.Clamp(race, 0, 3)];
            return list[Random.Range(0, list.Length)];
        }

        void BuildBar()
        {
            barRoot = new GameObject("Space Lounge").transform;
            // createScene branch 4: rooms with no rotation (game identity = Unity yaw 180).
            var room = Spawn(StationTables.BarRoom[BarRace], Vector3.zero, OrbitLayout.RotationToUnity(Vector3.zero), barRoot, "Room");
            // As in the hangar: the loops (and the Midorian prop's replays) skip their one-off first key, where every part
            // sits at the origin for 33 / 50 ms; played, it flashed for a frame on every wrap (the Nivelian bar's 6.5 s
            // bar_nivelian_anim_add, the Midorian prop each time it replayed).
            if (room != null)
                foreach (var a in room.GetComponentsInChildren<PartAnimation>(true)) a.loopStartMs = a.OneOffStartMs;
            if (room != null && BarRace == 3)
            {
                // CutScene::initialize (mode 4): bar_midorian_alpha_anim is a one-shot, restarted with 30 % every 2 s.
                foreach (var a in room.GetComponentsInChildren<PartAnimation>(true))
                    if (a.gameObject.name.Contains("alpha_anim")) { midorianProp = a; a.loop = false; a.play = false; }
            }

            // One visitor per agent (Generator::createAgents, kept with the stock): slot nextInt(7) re-rolled until free,
            // the mesh by the agent's race (Level::createScene 0xc2b3e: a Midorian with a Nivelian face uses the Nivelian
            // mesh, female Terrans their own).
            var agents = Stock != null ? Stock.agents : new List<Agent>();
            var slots = StationTables.VisitorSlots[BarRace];
            slotTaken = new bool[slots.Length];
            VisitorCount = Mathf.Min(agents.Count, slots.Length);
            for (int i = 0; i < VisitorCount; i++) SpawnVisitor(agents[i]);
            // Remake multiplayer: the event graphs' bar missions offered here (NetEventMissions) join as they arrive.
            Multiplayer.NetEventMissions.RequestOffers(Station != null ? Station.index : Session.StationIndex, AddVisitor);

            barPosA = OrbitLayout.ToUnity(StationTables.BarCameraStart[BarRace]);
            barPosB = OrbitLayout.ToUnity(StationTables.BarCameraRest[BarRace]);
            barRotA = CameraRotation(0f, StationTables.BarCameraStartYaw[BarRace], 0f);
            barRotB = CameraRotation(0f, StationTables.BarCameraRestYaw[BarRace], 0f);
            swayYaw.Start(0f, 5f);
        }

        bool[] slotTaken;

        /// <summary>Remake: one more visitor on a free slot after the bar was built (false: every slot taken). The lounge's
        /// plates and list follow (LoungePanel rebuilds when the count changes).</summary>
        public bool AddVisitor(Agent agent)
        {
            if (agent == null || slotTaken == null || barRoot == null || System.Array.IndexOf(slotTaken, false) < 0) return false;
            if (visitors.Exists(v => v.agent == agent)) return true;
            return SpawnVisitor(agent);
        }

        /// <summary>A visitor billboard for 'agent' on a random free slot (Generator::createAgents' slot nextInt(7) re-rolled
        /// until free) with its glow and floor shadow.</summary>
        bool SpawnVisitor(Agent agent)
        {
            var slots = StationTables.VisitorSlots[BarRace];
            if (System.Array.IndexOf(slotTaken, false) < 0) return false;
            int slot;
            do slot = Random.Range(0, slots.Length); while (slotTaken[slot]);
            slotTaken[slot] = true;
            int i = visitors.Count;
            {
                int race = agent.race == 3 && agent.portrait != null && agent.portrait[0] == 2 ? 2 : agent.race;
                var prefab = VisitorPrefab(StationTables.VisitorFor(race, !agent.male));
                if (prefab == null) return false;
                var feet = OrbitLayout.ToUnity(slots[slot]);
                var v = new Visitor { feet = feet, agent = agent };
                v.body = Instantiate(prefab, feet, Quaternion.identity, barRoot).transform;
                v.body.name = $"Visitor {i} ({prefab.name})";
                var bounds = new Bounds(feet, Vector3.zero);
                foreach (var r in v.body.GetComponentsInChildren<Renderer>()) bounds.Encapsulate(r.bounds);
                v.height = Mathf.Max((bounds.max.y - feet.y) * 0.55f, 60f * M);   // the billboard quad is about twice the figure
                if (visitorGlow != null)
                {
                    v.glow = Instantiate(visitorGlow, feet, Quaternion.identity, barRoot).transform;
                    v.glow.name = $"Visitor {i} glow";
                    var mat = glowMaterials != null && BarRace < glowMaterials.Length ? glowMaterials[BarRace] : null;
                    if (mat != null) foreach (var r in v.glow.GetComponentsInChildren<Renderer>()) r.sharedMaterial = mat;
                }
                if (visitorShadow != null)
                    Instantiate(visitorShadow, feet + new Vector3(0f, 20f * M, 0f), OrbitLayout.RotationToUnity(Vector3.zero), barRoot).name = $"Visitor {i} shadow";
                visitors.Add(v);
            }
            return true;
        }

        GameObject VisitorPrefab(StationTables.Visitor v) => v switch
        {
            StationTables.Visitor.TerranFemale => visitorTerranFemale != null ? visitorTerranFemale : visitorTerranMale,
            StationTables.Visitor.Vossk => visitorVossk,
            StationTables.Visitor.Nivelian => visitorNivelian,
            StationTables.Visitor.Multipod => visitorMultipod,
            StationTables.Visitor.Bobolan => visitorBobolan,
            StationTables.Visitor.Grey => visitorGrey,
            _ => visitorTerranMale,
        };

        GameObject Spawn(string assembly, Vector3 unityPos, Quaternion rot, Transform parent, string label)
        {
            var prefab = AssembledObject.LoadPrefab(db.AssemblyByName(assembly));
            if (prefab == null) { Debug.LogWarning($"StationLevel: no assembled prefab '{assembly}'"); return null; }
            var go = Instantiate(prefab, unityPos, rot, parent);
            go.name = label;
            foreach (var lod in go.GetComponentsInChildren<LODGroup>()) lod.ForceLOD(0);   // close-up room: full detail
            return go;
        }

        /// <summary>createShip(race, 0, idx, null, false): NPC mesh group, setExhaustVisible(false), asleep.</summary>
        GameObject SpawnShip(int index, Vector3 gamePos, float gameYaw, Transform parent, string label)
        {
            var entry = PlayerHull.Assembly(db, index);   // the player's own ship: remake debug, the Ships tab's pick
            if (entry == null) return null;
            var go = Spawn(entry.name, OrbitLayout.ToUnity(gamePos), OrbitLayout.RotationToUnity(new Vector3(0f, gameYaw, 0f)), parent, label);
            var asm = go != null ? go.GetComponent<AssembledObject>() : null;
            if (asm != null)
            {
                asm.SetPlayerVariant(false);
                if (asm.npcVariantParts != null) foreach (var p in asm.npcVariantParts) if (p != null) p.SetActive(false);   // exhaust off
            }
            return go;
        }

        GameObject SpawnShip(int index, Vector3 unityPos, Quaternion unityRot, Transform parent, string label)
        {
            var go = SpawnShip(index, Vector3.zero, 0f, parent, label);
            if (go != null) go.transform.SetPositionAndRotation(unityPos, unityRot);
            return go;
        }

        /// <summary>Remake: the rooms' thin, glossy parts (the Nivelian bar stools, the Midorian window frames) shimmered as the
        /// camera swayed: SMAA works within one frame. The station camera takes URP's temporal AA instead (its slow camera
        /// and still rooms are where TAA has nothing to smear), unless a temporal upscaler (DLSS, FSR 2+, STP) already
        /// anti-aliases or MSAA is on (URP's TAA needs it off); then SMAA as before. Again when the options change.</summary>
        void OnDestroy() => Settings.Changed -= ApplyAntialiasing;

        void ApplyAntialiasing()
        {
            if (mainCamera == null) return;
            var data = UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(mainCamera);
            if (data == null) return;
            var urp = GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
            bool msaa = urp != null && urp.msaaSampleCount > 1;
            data.antialiasing = Bootstrap.IsTemporal(Bootstrap.ActiveUpscaler) || msaa
                ? UnityEngine.Rendering.Universal.AntialiasingMode.SubpixelMorphologicalAntiAliasing
                : UnityEngine.Rendering.Universal.AntialiasingMode.TemporalAntiAliasing;
            data.antialiasingQuality = UnityEngine.Rendering.Universal.AntialiasingQuality.High;
        }

        /// <summary>Game camera rotation, order 2 (Ry * Rx * Rz, looking down local -Z) -> Unity (looking down +Z).</summary>
        static Quaternion CameraRotation(float x, float y, float z) =>
            Quaternion.Euler(-x * Mathf.Rad2Deg, -y * Mathf.Rad2Deg, z * Mathf.Rad2Deg);

        // ---- view switching ----------------------------------------------------------------------------------

        /// <summary>Instant switch, like ModStation (no fades). The lounge plays its camera intro on the first visit only.</summary>
        public void SetView(StationView view, bool force = false)
        {
            if (view == View && !force) return;
            View = view;
            hangarRoot.gameObject.SetActive(view == StationView.Hangar);
            barRoot.gameObject.SetActive(view == StationView.Lounge);
            if (view == StationView.Lounge)
            {
                introT = loungeVisited ? 1.25f : 0.75f;   // SpaceLounge::init starts at B
                introSkip = false;
                loungeVisited = true;
                ApplyLoungeLighting();
            }
            else
            {
                flingVelocity = 0f;
                ApplyHangarLighting();
                traffic?.ResumeAudio();
            }
            PlayAmbience();
            UpdateCamera(0f);
            ViewChanged?.Invoke();
        }

        void ApplyHangarLighting()
        {
            if (keyLight != null)
            {
                keyLight.transform.rotation = Quaternion.LookRotation(-OrbitLayout.DirToUnity(StationTables.HangarTowardLight).normalized);
                keyLight.color = Color.white;
                keyLight.intensity = hangarLightIntensity;
            }
            var amb = (StationTables.HangarAmbient(Layout.raceId) + new Color(0.2f, 0.2f, 0.2f)) * 0.7f * hangarAmbientScale;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(amb.r, amb.g, amb.b);
            SetFog(HangarIndex == 1, StationTables.HangarFogEnd);
        }

        void ApplyLoungeLighting()
        {
            OrbitBuilder.SetupLights(Layout, keyLight, null, sunIntensityAt2);
            RenderSettings.ambientMode = AmbientMode.Skybox;
            DynamicGI.UpdateEnvironment();
            SetFog(BarRace == 1, StationTables.BarFogEnd);
        }

        static void SetFog(bool on, float end)
        {
            Bootstrap.SetSceneFog(on);   // off below Quality High
            if (!on) return;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 0f;
            RenderSettings.fogEndDistance = end * M;
            RenderSettings.fogColor = StationTables.VosskFog;
        }

        // ---- turntable (ModStation::OnTouchMove 0xea340 / OnTouchEnd / OnUpdate) ----------------------------

        /// <summary>Turns the player's ship by 'gameRadians' (the original: +0xe0 += dx px, yaw = +0xe0 / 120).</summary>
        public void RotateShip(float gameRadians)
        {
            if (PlayerFlying) return;
            flingVelocity = 0f;
            shipYaw += gameRadians;
            ApplyShipYaw();
        }

        /// <summary>Lets the ship keep turning after a drag; slows by x0.9 per 20 ms frame (normalised).</summary>
        public void FlingShip(float gameRadiansPerSecond) => flingVelocity = PlayerFlying ? 0f : gameRadiansPerSecond;

        void ApplyShipYaw()
        {
            if (playerShip != null && !PlayerFlying) playerShip.rotation = OrbitLayout.RotationToUnity(new Vector3(0f, shipYaw, 0f));
        }

        /// <summary>SpaceLounge::OnTouchEnd case 0 jumps to B; remake: the rest of the ease runs at a 0.7 s pace instead, so the
        /// camera doesn't snap.</summary>
        public void SkipIntro()
        {
            if (IntroPlaying) introSkip = true;
        }

        bool introSkip;

        /// <summary>ModStation::leaveStation 0xec1ec after the "Depart the station?" confirmation: into space (after the
        /// remake's take-off).</summary>
        public void Launch() => Depart(LaunchNow);

        /// <summary>Remake: the player's ship takes off and flies out through the forcefield, then 'then' runs (loads the
        /// next scene); straight away with the flights off. It lifts off at once and holds over its pad while another
        /// ship flies (HangarTraffic: one flight at a time).</summary>
        public void Depart(Action then)
        {
            if (PlayerFlying) return;
            var lane = Lane;
            if (!Settings.HangarFlights || lane == null || playerShip == null) { then?.Invoke(); return; }
            SetView(StationView.Hangar);
            flingVelocity = 0f;
            afterDeparture = then;
            var engine = HangarFlight.AddEngine(playerShip.gameObject, true, db, shipIndex, out float volume);
            playerFlight = HangarFlight.Departure(playerShip, lane, engine, volume);
            playerFlightStart = Time.unscaledTime;
            playerFlight.Hold = traffic != null && traffic.Busy;
        }

        /// <summary>A tap / key during the player's flight: landed at once, or gone. Not in its first 0.4 s (the key that
        /// confirmed the launch).</summary>
        public void SkipPlayerFlight()
        {
            if (playerFlight != null && Time.unscaledTime - playerFlightStart > 0.4f) playerFlight.Skip();
        }

        float playerFlightStart;

        void LaunchNow()
        {
            Session.LastDepartureTime = Time.realtimeSinceStartup;   // Status+0x70, for computerTradeGoods
            Session.ArrivedByTravel = false;
            Session.LaunchedFromStation = true;
            int storyOrbit = Story.LaunchStation;
            if (storyOrbit >= 0)
            {
                // Index 48: departStation(58) + initStreamOutPosition, arriving in B'akrram's orbit (Taret Orskk flies).
                Session.PreviousStationIndex = Session.StationIndex;
                Session.StationIndex = storyOrbit;
                Session.ArrivedByTravel = true;
                Session.LaunchedFromStation = false;
            }
            if (Application.CanStreamedLevelBeLoaded(spaceScene)) SceneManager.LoadScene(spaceScene);
        }

        // ---- per frame ---------------------------------------------------------------------------------------

        void Update()
        {
            float dtMs = Time.deltaTime * 1000f;
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
                flingVelocity *= Mathf.Pow(0.9f, dtMs / 20f);
                // Stops below 1 px per frame (1/120 rad per 20 ms).
                if (Mathf.Abs(flingVelocity) < 1f / StationTables.TurntablePixelsPerRadian / 0.02f) flingVelocity = 0f;
                ApplyShipYaw();
            }

            if (View == StationView.Lounge && midorianProp != null && (midorianTimer += dtMs) > 2000f)
            {
                midorianTimer = 0f;
                if (Random.value < 0.3f) midorianProp.Restart();
            }

            if (View == StationView.Hangar) RefreshTurret(false);
            if (musicSource != null) musicSource.volume = Settings.MusicVolume * Multiplayer.NetScreen.SceneMusic;
            float atmoMs = Time.unscaledDeltaTime * 1000f;
            mainViewAtmo?.Update(atmoMs); loungeAtmo?.Update(atmoMs); hangarAtmo?.Update(atmoMs);
        }

        void LateUpdate()
        {
            UpdateCamera(Time.deltaTime * 1000f);
            if (View == StationView.Lounge) UpdateBillboards();
        }

        void UpdateCamera(float dtMs)
        {
            if (mainCamera == null) return;
            var cam = mainCamera.transform;
            if (Vr.VrMode.Enabled) { VrCamera(cam); return; }
            if (View == StationView.Hangar)
            {
                // ModStation::OnUpdate: each axis eases to a new target on the other side of the base when within 5 units.
                var p = Vector3.zero;
                for (int i = 0; i < 3; i++)
                {
                    drift[i].Increase(dtMs);
                    if (Mathf.Abs(drift[i].Value - drift[i].Target) < 5f) NextDriftLeg(i, drift[i].Value);
                    p[i] = drift[i].Value;
                }
                var r = StationTables.HangarCameraRot[HangarIndex];
                cam.SetPositionAndRotation(shipPivot + OrbitLayout.ToUnity(p), CameraRotation(r.x, r.y, StationTables.HangarCameraRoll));
                SetLens(StationTables.HangarFov, StationTables.HangarNear, StationTables.HangarFar);
            }
            else
            {
                Vector3 pos;
                Quaternion rot;
                if (introT < 1.25f)
                {
                    // EaseInOutMatrix(A, B, 3000): t 0.75 -> 1.25, blend sin(2 pi t) * 0.5 + 0.5; Increase(min(dt, 50)).
                    float introMs = introSkip ? 700f : StationTables.BarIntroMs;
                    introT = Mathf.Min(1.25f, introT + Mathf.Min(dtMs, 50f) * 0.5f / introMs);
                    if (introT >= 1.25f) introSkip = false;
                    float b = Mathf.Sin(TwoPi * introT) * 0.5f + 0.5f;
                    pos = Vector3.Lerp(barPosA, barPosB, b);
                    rot = Quaternion.Slerp(barRotA, barRotB, b);
                }
                else
                {
                    // Sway: [RotY(v / 35), translation (0, bob, 0)] * B, v easing between -4..5 at speed 1..4.
                    swayYaw.Increase(dtMs * swaySpeed);
                    if (Mathf.Abs(swayYaw.Value - swayYaw.Target) < 0.25f)
                    {
                        swayYaw.Start(swayYaw.Value, 5 - Random.Range(0, 10));
                        swaySpeed = 1 + Random.Range(0, 4);
                    }
                    // Phase step 0.05..0.12 per 20 ms frame in the original (normalised); bob at most 3.5 units.
                    bobPhase += Mathf.Clamp(dtMs * 0.0025f, 0f, 0.12f);
                    var yaw = Quaternion.Euler(0f, -swayYaw.Value / 35f * Mathf.Rad2Deg, 0f);
                    pos = yaw * barPosB + new Vector3(0f, Mathf.Sin(bobPhase) * 3.5f * M, 0f);
                    rot = yaw * barRotB;
                }
                cam.SetPositionAndRotation(pos, rot);
                SetLens(StationTables.BarFov, StationTables.BarNear, StationTables.BarFar);
            }
        }

        /// <summary>VR: standing in the room, still (no drift, sway or intro). The hangar: on the floor beside the pad, between
        /// the original camera's spot and the ship, facing it, the eyes 1.7 m above the hull's bottom (the pad); the bar: the
        /// original's view point (B), level.</summary>
        void VrCamera(Transform cam)
        {
            if (View == StationView.Hangar)
            {
                var ship = playerShip;
                var bounds = new Bounds(shipPivot, Vector3.one);
                if (ship != null && !PlayerFlying)
                {
                    bool any = false;
                    foreach (var r in ship.GetComponentsInChildren<MeshRenderer>())
                    {
                        if (!any) { bounds = r.bounds; any = true; } else bounds.Encapsulate(r.bounds);
                    }
                    vrShipBounds = bounds;
                }
                else if (vrShipBounds.size != Vector3.zero) bounds = vrShipBounds;
                var original = shipPivot + OrbitLayout.ToUnity(hangarCamBase);
                var away = original - bounds.center;
                away.y = 0f;
                if (away.sqrMagnitude < 0.01f) away = Vector3.back;
                away.Normalize();
                float radius = Mathf.Max(bounds.extents.x, bounds.extents.z);
                var pos = bounds.center + away * (radius + 5f);
                pos.y = bounds.min.y + 1.7f;
                cam.SetPositionAndRotation(pos, Quaternion.LookRotation(-away, Vector3.up));
                SetLens(StationTables.HangarFov, StationTables.HangarNear, StationTables.HangarFar);
            }
            else
            {
                introT = 1.25f;   // no intro ease
                cam.SetPositionAndRotation(barPosB, Quaternion.Euler(0f, barRotB.eulerAngles.y, 0f));
                SetLens(StationTables.BarFov, StationTables.BarNear, StationTables.BarFar);
            }
        }

        Bounds vrShipBounds;

        float Base(int axis) => hangarCamBase[axis];

        void NextDriftLeg(int axis, float from)
        {
            driftSign[axis] = !driftSign[axis];
            int min = axis == 0 ? 18 : axis == 1 ? 30 : 50, range = axis == 0 ? 131 : axis == 1 ? 120 : 100;
            float offset = (min + Random.Range(0, range)) * (driftSign[axis] ? 1f : -1f);
            drift[axis].Start(from, Base(axis) + offset);
        }

        void SetLens(float fovRad, float near, float far)
        {
            mainCamera.fieldOfView = Aspect.VerticalFovKeepWidth(fovRad * Mathf.Rad2Deg, mainCamera.aspect);   // the rooms end outside 16:9
            mainCamera.nearClipPlane = near * M;
            mainCamera.farClipPlane = far * M;
        }

        /// <summary>SpaceLounge::updateScreenPositions: visitors and glows face the camera (MatrixGetLookAt, local +Z toward
        /// the camera, with the camera's up); the glow sits 100 units behind; Terran bars turn visitors a further 180 deg.</summary>
        void UpdateBillboards()
        {
            // VR: they face the head (the headset's camera), upright.
            var eye = Vr.VrRig.Current != null ? Vr.VrRig.Current.Eye : null;
            var cam = eye != null ? eye.transform : mainCamera.transform;
            foreach (var v in visitors)
            {
                var toCam = cam.position - v.feet;
                if (toCam.sqrMagnitude < 1e-6f) continue;
                var look = Quaternion.LookRotation(toCam.normalized, eye != null ? Vector3.up : cam.up);
                if (v.body != null) v.body.rotation = BarRace == 0 ? look * Quaternion.Euler(0f, 180f, 0f) : look;
                if (v.glow != null)
                {
                    var z = look * Vector3.forward;
                    v.glow.SetPositionAndRotation(v.feet - new Vector3(z.x, 0f, z.z) * (100f * M), look);
                }
            }
        }

        // ---- audio -------------------------------------------------------------------------------------------

        /// <summary>The screen's ambience event (the FEV's LGCY data, CycleSound): 122 Station_Atmo_Mainview on the main view,
        /// 95 Station_Atmo_Hangar while the hangar window is open (ModStation::OnKeyPress stops 122 and plays 95), 108
        /// Station_Atmo_Lounge in the lounge.</summary>
        void PlayAmbience()
        {
            if (mainViewAtmo == null)
            {
                mainViewAtmo = CycleSound.MainView(gameObject, mainViewAmbience, mainViewAdds);
                hangarAtmo = CycleSound.Hangar(gameObject, hangarAmbience, hangarAdds);
                // The two lounge add definitions: every Add_* wave but Add_1 (Add_1) / but Add_11 (Add_2).
                var adds = loungeAdds ?? new AudioClip[0];
                loungeAtmo = CycleSound.Lounge(gameObject, loungeAmbience,
                                               System.Array.FindAll(adds, c => c != null && c.name != "Station_Atmo_Lounge_Add_1"),
                                               System.Array.FindAll(adds, c => c != null && c.name != "Station_Atmo_Lounge_Add_11"));
            }
            var want = View == StationView.Lounge ? loungeAtmo : hangarWindowOpen ? hangarAtmo : mainViewAtmo;
            foreach (var a in new[] { mainViewAtmo, loungeAtmo, hangarAtmo }) if (a != want && a.IsPlaying) a.Stop();
            if (!want.IsPlaying) want.Play();
        }

        /// <summary>The hangar window opened / closed: its own ambience (95) instead of the main view's (122).</summary>
        public void SetHangarWindowOpen(bool open)
        {
            if (hangarWindowOpen == open) return;
            hangarWindowOpen = open;
            PlayAmbience();
        }
    }
}
