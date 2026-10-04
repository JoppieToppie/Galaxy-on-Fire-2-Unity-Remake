// Navigation.cs
// Target locks on the station, the jumpgate and the other stations' planets, the autopilot, the planet jump and
// fast-forward (Reference/research/autopilot_travel.md):
//   Radar::draw 0x1554fc          landmarks (station, jumpgate): on screen, within +-w/6 of the centre and +-w/16 of the
//                                 crosshair; planets (not the current station's own): +-w/32 of the crosshair, also during
//                                 the autopilot. Lock after the scanner's lock time (attr 29, 8000 ms without), sound 26.
//                                 Landmarks beat planets, planets beat asteroids (Mining asks BlocksAsteroidLock): a lock
//                                 of one kind stops the others, a candidate doesn't.
//   MGame::OnTouchBegin 0x1a838c  action: landmark locked -> autopilot ("Target: Var Hastra Station", sound 28);
//                                 planet locked -> planet jump, no confirmation
//   PlayerEgo::update / setAutoPilot  steering by moveToPosition (ShipController.autopilotTarget), throttle reset to
//                                 100 % once, stick ignored, throttle / boost / guns still work; the autopilot button (here
//                                 the action prompt) turns it off ("Autopilot Off", sound 29)
//   MGame::dockEvent 0x1afebc     autopilot to the station docks within 16000 units (SpaceLevel)
//   PlayerEgo::dockToPlanet       sound 5, the camera freezes and looks at the ship, straight on at 8 u/ms, after 3000 ms
//                                 the level reloads in the target station's orbit (arrival: SpaceLevel)
//   MGame+0x160 fast-forward      held button: the whole game runs 5x (Time.timeScale) while the autopilot or an asteroid
//                                 approach runs and the target is >= 20000 units away; releasing, arriving or the
//                                 autopilot ending stops it, and hostile ships around block it (Radar+0x54)
//   Hud::initHudMenu(3) 0x18e080  the autopilot menu (autopilot button while the autopilot is off; the game pauses):
//                                 549 "Asteroid field" (not in the alien orbit; flies to the field centre and, like the
//                                 original, keeps going until switched off), "<name> Station" (not in empty orbits),
//                                 547 "Jumpgate" (gate orbit only); each "Target: X" + sound 28 (MGame::OnTouchEnd);
//                                 "574 Destination: X" with a programmed station (setAutoPilotToProgrammedStation)
//   LevelScript::setAutoPilotToProgrammedStation 0x160b50  the star map's destination: the current station clears it; in
//                                 this system its planet (the lock then jumps by itself); else in the gate orbit the
//                                 jumpgate, otherwise the planet of the system's gate station. Run at the end of the
//                                 launch / arrival camera ("Autopilot On" + 28) and from the menu.
// Reaching the jumpgate (Level::collideStream) is handled by SystemJump (ReachedGate).
// Docking targets (Level::getDockingTarget, MGame::OnTouchBegin 0x1a838c): the story's dockable objects are locked like a
// landmark; the action docks (ObjectDocking), and the autopilot menu lists them (Hud::initHudMenu(3)).
// The menu's order (Hud::initHudMenu(3)): outside the alien orbit 549 Asteroid field, "<name> Station" (not in empty orbits),
// 547 Jumpgate (gate orbit), 573 Waypoint (a player route whose last waypoint isn't reached: the autopilot follows the
// route, "Target: Waypoint"), "574 Destination: X"; then every named docking target. Remake-only: then "Khador Drive" (1359)
// with a drive (the original has it in the HUD's main menu; refused on missions, 525, and with volatile goods, 612), the
// wingmen and the cloak. The original's only restriction is no menu at all in mission type 0xb7 (the unreachable
// Supernova challenge).

using System;
using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.World;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GoF2Remake.Flight
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public class Navigation : MonoBehaviour
    {
        public enum Kind { Station, Jumpgate, Planet, AsteroidField, Destination, KhadorDrive, Waypoint, Wingmen, Cloak, Wormhole, DockingTarget, Secondary, TimeExtender,
            /// <summary>Remake multiplayer: a named object (/spawn ... named, DebugSpawner.SpawnObject): its name and distance
            /// near the crosshair like a landmark's, never locked or flown to.</summary>
            Marker }

        public class Target
        {
            public Kind kind;
            public Transform transform;      // null for fixed positions
            public Vector3 fixedPosition;
            public int station = -1;         // planets: the station it leads to
            public bool disabled;            // drawn half-transparent, ignores taps (the cloak while not ready)
            public bool hidden;              // not drawn and not lockable now (the wormhole while invisible)
            public string name;
            public NpcShip dockingShip;     // docking targets: the object
            public bool freelance;          // waypoints: a freelance mission's route (the white freelance icon, not the gold story one)
            public Vector3 Position => transform != null ? transform.position : fixedPosition;
        }

        const float M = 0.05f;
        const float CrosshairDistanceMeters = 22000f * M;
        public const float AboutToReachUnits = 20000f;   // PlayerEgo+0x330
        const float FastForwardScale = 5f;
        const float PlanetJumpMs = 3000f, PlanetJumpSpeed = 8f;   // dockToPlanet: 8 u/ms for 3 s

        public string spaceScene = "Space";

        public readonly List<Target> Targets = new List<Target>();
        /// <summary>Level::getAsteroidWaypoint: the field centre (menu only, never locked), null in the alien orbit.</summary>
        public Target AsteroidField { get; private set; }
        /// <summary>The autopilot menu is open: the game is paused.</summary>
        public bool MenuOpen { get; private set; }
        /// <summary>Paused by the star map / the jumpgate prompt (SystemJump).</summary>
        public bool Paused { get => paused; set { paused = value; ApplyTimeScale(); } }
        /// <summary>The flight HUD's pause menu is open (MenuTouchWindow mode 1): the game is paused.</summary>
        public bool PauseMenuOpen { get => pauseMenuOpen; set { pauseMenuOpen = value; ApplyTimeScale(); } }
        bool pauseMenuOpen;
        /// <summary>The autopilot to the jumpgate is inside its sphere (Level::collideStream, radius 7500 / Vossk 11250).</summary>
        public bool ReachedGate => AutopilotTarget?.kind == Kind.Jumpgate && (AutopilotTarget.Position - ship.transform.position).magnitude < gateRadiusUnits * M;
        /// <summary>Wingmen fly with the player: the menu offers 306 "Wingmen" (the HUD's action-menu entry in the original).</summary>
        public Func<bool> HasWingmen;
        /// <summary>The player route (campaign / freelance waypoints), null = none.</summary>
        public Route PlayerRoute => playerRoute;

        /// <summary>The "Khador Drive" menu entry was picked (SystemJump opens the star map).</summary>
        public event Action KhadorRequested;
        /// <summary>The player's cloak and time extender (null = not mounted), set by SpaceLevel.</summary>
        public PlayerCloak Cloak;
        public TimeExtender Extender;
        public Target Candidate { get; private set; }
        public Target Locked { get; private set; }
        public float LockTimer { get; private set; }
        public int LockTimeMs { get; private set; } = 8000;
        public Target AutopilotTarget { get; private set; }
        public bool Autopilot => AutopilotTarget != null;
        public bool GoingToStation => AutopilotTarget?.kind == Kind.Station;
        public bool GoingToGate => AutopilotTarget?.kind == Kind.Jumpgate;
        /// <summary>A campaign level's player route (Level+0x108, PlayerEgo::setRoute): its current waypoint is a landmark
        /// target named "Waypoint" (548): marked on the HUD, lockable, and the autopilot flies to it; "Waypoint reached."
        /// (543) / "Last waypoint reached." (544) as the route advances.</summary>
        public void SetRoute(Route route, bool freelance = false)
        {
            playerRoute = route;
            routeTarget = null;
            Targets.RemoveAll(t => t.kind == Kind.Waypoint);
            if (route == null) return;
            routeTarget = new Target { kind = Kind.Waypoint, name = Localization.Get(548), freelance = freelance };
            routeIndex = route.index;
            UpdateRoute();
        }

        Route playerRoute;
        Target routeTarget;
        int routeIndex;

        void UpdateRoute()
        {
            if (playerRoute == null) return;
            if (playerRoute.index != routeIndex)
            {
                routeIndex = playerRoute.index;
                Say(Localization.Get(playerRoute.Waypoint == null ? 544 : 543));
                if (AutopilotTarget == routeTarget) SetAutopilot(playerRoute.Waypoint == null ? null : routeTarget);
            }
            var wp = playerRoute.Waypoint;
            if (wp == null) { Targets.Remove(routeTarget); if (Locked == routeTarget || Candidate == routeTarget) Locked = Candidate = null; return; }
            routeTarget.fixedPosition = new Vector3(wp.Value.x, wp.Value.y, -wp.Value.z) * M;
            if (!Targets.Contains(routeTarget)) Targets.Add(routeTarget);
        }

        /// <summary>Landmark 3: named 545 "Wormhole" near the centre, icon 0x450 elsewhere; never a lock candidate (Radar::draw).</summary>
        public void SetWormhole(GoF2Remake.World.Wormhole wormhole)
        {
            this.wormhole = wormhole;
            Targets.RemoveAll(t => t.kind == Kind.Wormhole);
            if (wormhole == null) return;
            wormholeTarget = new Target { kind = Kind.Wormhole, transform = wormhole.transform, name = Localization.Get(545), hidden = !wormhole.Visible };
            Targets.Add(wormholeTarget);
        }
        GoF2Remake.World.Wormhole wormhole;
        Target wormholeTarget;

        /// <summary>ObjectDocking on the player (set by the level): locked docking targets dock through it.</summary>
        [NonSerialized] public ObjectDocking Docking;
        /// <summary>The level's ships (Traffic.Ships): those with a docking type are docking targets.</summary>
        [NonSerialized] public List<NpcShip> Ships;

        /// <summary>Level::getDockingTarget: the dockable objects, lockable while visible and not radar-hidden.</summary>
        void UpdateDockingTargets()
        {
            if (Ships == null) return;
            foreach (var s in Ships)
            {
                if (s == null || s.DockingType <= 0) continue;
                if (!Targets.Exists(t => t.dockingShip == s))
                {
                    s.DockIndex = Targets.FindAll(t => t.kind == Kind.DockingTarget).Count;   // Level::getDockingTarget's index
                    Targets.Add(new Target { kind = Kind.DockingTarget, transform = s.transform, dockingShip = s });
                }
            }
            foreach (var t in Targets)
            {
                if (t.kind != Kind.DockingTarget) continue;
                var s = t.dockingShip;
                t.name = s != null ? s.Target.displayName ?? "" : "";
                t.hidden = s == null || s.Gone || !s.Target.Alive || s.DockingType <= 0 || s.RadarHidden || s.Hidden || s.Inactive;
                if (t.hidden && (Locked == t || Candidate == t)) { Locked = Candidate = null; LockTimer = 0f; }
            }
        }

        /// <summary>Set by the level: planet jumps and the Khador Drive are refused (story, campaign_flow.md 7).</summary>
        public Func<bool> JumpsBlocked;
        /// <summary>Set by the level: a story rule refuses the jump to this station itself (index 24: Sahi needs a scanner and a
        /// tractor beam, 532) and shows why; true = refused.</summary>
        public Func<int, bool> PlanetJumpRefused;

        /// <summary>HUD event 0x15: "Not possible on a mission." (525); the autopilot stops.</summary>
        public void Refuse() => Refuse(Localization.Get(525));

        /// <summary>The autopilot off with 'text' as the HUD message.</summary>
        public void Refuse(string text)
        {
            Say(text);
            SetAutopilot(null);
        }
        public bool Jumping { get; private set; }
        public bool FastForward { get; private set; }
        /// <summary>Lock ring frame 0..23 (no 500 ms delay for landmarks and planets), -1 = none.</summary>
        public int LockFrame => Candidate == null ? -1 : Locked != null ? 23 : Mathf.Min(23, (int)(23f * LockTimer / Mathf.Max(1, LockTimeMs)));
        /// <summary>Radar::draw 0x1574c0 skips the asteroid loop on a landmark (+0x24) or planet (+0x14) lock, a ship / crate
        /// candidate (+8, and local_f8 in the candidate test), a crate on the tractor beam (+0x1c) or the autopilot; a
        /// landmark or planet that is only a candidate (+0x28 / +0x18) doesn't stop it (both rings fill).</summary>
        public bool BlocksAsteroidLock => Locked != null || Autopilot || Jumping || ShipLockActive;
        /// <summary>A landmark (station, jumpgate, docking target, waypoint) is locked: Radar+0x24, which stops the ship lock's
        /// timer (Radar::draw 0x1558ae); a planet lock (+0x14) doesn't.</summary>
        public bool LandmarkLocked => Locked != null && Locked.kind != Kind.Planet;
        /// <summary>CombatRadar has a ship / crate candidate this frame or a crate on the tractor beam.</summary>
        [NonSerialized] public bool ShipLockActive;
        /// <summary>Radar+0x54: hostile ships around (Traffic): no fast-forward.</summary>
        [NonSerialized] public bool HostilesPresent;
        public bool AboutToReach { get; private set; }
        public event Action<string> Message;

        /// <summary>The action prompt text (null = nothing to do here).</summary>
        public string PromptText =>
            Jumping ? null
            : Autopilot ? Localization.Extra("hudAutopilotOff", "AUTOPILOT OFF")
            : Locked == null ? null
            : Locked.kind == Kind.Planet ? Localization.Extra("hudJump", "JUMP")
            : Locked.kind == Kind.DockingTarget ? Localization.Extra("hudDock", "DOCK")
            : layout != null && layout.alienOrbit ? null   // MGame::OnTouchBegin: no autopilot to the Void station
            : Localization.Extra("hudAutopilot", "AUTOPILOT");

        ShipController ship;
        Database db;
        OrbitLayout layout;
        int systemGateStation = -1;
        bool paused;
        Mining mining;
        ChaseCamera chase;
        WeaponSystem weapons;
        CombatAudio sounds;
        AudioSource sfx;
        bool wasLocked, fastForwardHeld;
        float jumpMs, gateRadiusUnits = OrbitLayout.GateRadius;
        Target jumpTarget;

        public void Setup(Database database, OrbitLayout layout, Backdrop backdrop, ShipController controller,
                          Mining miningSystem, ChaseCamera chaseCamera, WeaponSystem weaponSystem)
        {
            ship = controller;
            db = database;
            this.layout = layout;
            systemGateStation = db.Systems.Find(s => s.index == layout.systemIndex)?.jumpgateStation ?? -1;
            mining = miningSystem;
            chase = chaseCamera;
            weapons = weaponSystem;
            sounds = CombatAudio.Load();
            sfx = gameObject.AddComponent<AudioSource>();
            sfx.playOnAwake = false;
            var scanner = Shop.FirstMounted(database, 17);
            LockTimeMs = Cheats.LockMs(scanner != null && scanner.HasAttr(29) ? scanner.Attr(29) : 8000);
            gateRadiusUnits = layout.JumpgateRadius;

            // Level::getLandmarks: [0] station (none in empty orbits), [1] the visible jumpgate (gate orbit only).
            var st = db.Stations.Find(s => s.index == layout.stationIndex);
            if (layout.hasStation && st != null)
                Targets.Add(new Target { kind = Kind.Station, fixedPosition = Vector3.zero, station = st.index,
                                         name = st.index == 101 ? st.name : $"{st.name} {Localization.Get(136)}" });
            else if (layout.hasStation && layout.alienOrbit)   // the Void station: distance only, no autopilot
            {
                // Radar::Radar: 415 "Void" + " Station" (the Void pseudo-station's index is -1, not 101); after the Valkyrie
                // add-on the battlestation is there and landmark[0] carries station 101's name (Level::createSpace).
                string name = Story.Dlc1Won ? db.Stations.Find(s => s.index == 101)?.name ?? Localization.Get(77)
                                            : $"{Localization.Get(415)} {Localization.Get(136)}";
                Targets.Add(new Target { kind = Kind.Station, fixedPosition = Vector3.zero, station = Session.VoidOrbit, name = name });
            }
            if (layout.hasJumpgate)
                Targets.Add(new Target { kind = Kind.Jumpgate, fixedPosition = OrbitLayout.ToUnity(layout.jumpgate), name = Localization.Get(547) });
            if (layout.systemIndex >= 0)
                AsteroidField = new Target { kind = Kind.AsteroidField, fixedPosition = OrbitLayout.ToUnity(layout.asteroidCentre), name = Localization.Get(549) };
            // StarSystem::getPlanetTargets: the other stations' planets (the orbit planet is the current station's own).
            if (backdrop != null)
                foreach (var (station, t, orbit) in backdrop.PlanetTargets)
                    if (!orbit && station != layout.stationIndex)
                        Targets.Add(new Target { kind = Kind.Planet, transform = t, station = station,
                                                 name = db.Stations.Find(s => s.index == station)?.name ?? "" });
        }

        void OnDisable()
        {
            MenuOpen = false;
            paused = false;
            SetFastForward(false);
            ApplyTimeScale();
        }

        // ---- autopilot menu (Hud::initHudMenu(3)) ------------------------------------------------------------

        /// <summary>The autopilot menu: Hud::initHudMenu(3)'s entries in its order. 'actions' = the quick menu instead,
        /// Hud::initHudMenu(0): secondary weapons, the cloak, the Khador Drive and the wingmen.</summary>
        public List<Target> MenuEntries(bool actions = false)
        {
            var list = new List<Target>();
            if (actions) return ActionEntries(list);
            if (!layout.alienOrbit)
            {
                if (AsteroidField != null) list.Add(AsteroidField);
                var station = Targets.Find(t => t.kind == Kind.Station);
                if (station != null) list.Add(station);
                var gate = Targets.Find(t => t.kind == Kind.Jumpgate);
                if (gate != null) list.Add(gate);
                // 573 "Waypoint": the player route's last waypoint isn't reached (Route::getLastWaypoint +300).
                if (playerRoute != null && playerRoute.Waypoint != null && routeTarget != null)
                    list.Add(new Target { kind = Kind.Waypoint, name = Localization.Get(573) });
                int prog = Session.ProgrammedStation;
                if (prog >= 0 && prog != layout.stationIndex)
                    list.Add(new Target { kind = Kind.Destination, station = prog,
                                          name = $"{Localization.Get(574)}: {db.Stations.Find(s => s.index == prog)?.name}" });
            }
            // Level::getDockingTarget: every one with a name (PlayerFixedObject::getName), in the alien orbit too.
            foreach (var t in Targets) if (t.kind == Kind.DockingTarget && !t.hidden && !string.IsNullOrEmpty(t.name)) list.Add(t);
            return list;
        }

        List<Target> ActionEntries(List<Target> list)
        {
            // Hud::initHudMenu(0) in its order: 266 "Secondary weapons" with any secondary mounted (its list is
            // Hud::initHudMenu(1)), 306 Wingmen, the item entries (0x18e734: the cloak by the item's name, unusable while
            // cloaked / charging / recharging), 1359 Khador Drive.
            if (weapons != null && weapons.SecondaryItems().Count > 0) list.Add(new Target { kind = Kind.Secondary, name = Localization.Get(266) });
            if (HasWingmen != null && HasWingmen()) list.Add(new Target { kind = Kind.Wingmen, name = Localization.Get(306) });
            if (Cloak != null) list.Add(new Target { kind = Kind.Cloak, name = Cloak.ItemName, disabled = !Cloak.Rules.Available });
            if (GalaxyMap.HasJumpDrive(db)) list.Add(new Target { kind = Kind.KhadorDrive, name = Localization.Get(1359) });
            // Remake: the time extender too (the original has it only on its own touch slot), so one menu holds every
            // device: secondaries, wingmen, cloak, Khador Drive and the time extender (half-transparent while recharging).
            if (Extender != null)
            {
                var te = Shop.FirstMounted(db, 26);
                list.Add(new Target { kind = Kind.TimeExtender, name = te != null ? Localization.Get(1274 + te.index) : "Time extender",
                                      disabled = !Extender.Ready && !Extender.Running });
            }
            return list;
        }

        /// <summary>MGame::OnTouchEnd, autopilot button: only while nothing else flies the ship; pauses the game.</summary>
        public bool CanOpenMenu => !Autopilot && !Jumping && !paused && (mining == null || mining.State == Mining.Phase.Idle) && (Docking == null || !Docking.Busy);
        /// <summary>MGame::OnTouchEnd, quick menu button (key 4): refused only while mining (PlayerEgo::isMining), so it opens
        /// on the autopilot too (remake: not while jumping or docking at an object either).</summary>
        public bool CanOpenActions => !Jumping && !paused && (mining == null || mining.State == Mining.Phase.Idle) && (Docking == null || !Docking.Busy);

        /// <summary>The open menu is the quick (action) menu, not the autopilot's.</summary>
        public bool MenuIsActions { get; private set; }

        public void OpenMenu(bool actions = false)
        {
            if (actions ? !CanOpenActions : !CanOpenMenu) return;
            MenuIsActions = actions;
            MenuOpen = true;
            if (weapons != null) weapons.Blocked = true;
            ApplyTimeScale();
        }

        public void CloseMenu()
        {
            if (!MenuOpen) return;
            MenuOpen = false;
            if (weapons != null) weapons.Blocked = false;
            ApplyTimeScale();
        }

        /// <summary>A menu entry: "Target: X" + sound 28, autopilot on, the menu closes and the game resumes.</summary>
        public void ChooseMenuEntry(Target target)
        {
            CloseMenu();
            if (target == null || target.disabled) return;
            if (target.kind == Kind.Destination) { ContinueToProgrammedStation(); return; }
            if (target.kind == Kind.Cloak) { Cloak?.Use(); return; }
            if (target.kind == Kind.TimeExtender) { Extender?.Toggle(); return; }   // the game runs again: CloseMenu above
            if (target.kind == Kind.DockingTarget) { Docking?.Dock(target.dockingShip); return; }
            if (target.kind == Kind.Waypoint)
            {
                // MGame::OnTouchEnd key 0x2000000: HUD event 0xd "Target: Waypoint" + 28; the autopilot follows the route.
                if (routeTarget == null || playerRoute == null || playerRoute.Waypoint == null) return;
                Say($"{Localization.Get(546)}: {Localization.Get(573)}");
                Play(sounds?.autopilotOn);
                SetAutopilot(routeTarget);
                return;
            }
            if (target.kind == Kind.KhadorDrive)
            {
                if (JumpsBlocked != null && JumpsBlocked() && Story.ForcedKhadorTarget(Session.StationIndex) == null) { Say(Localization.Get(525)); return; }
                if (GalaxyMap.HasVolatileGoods) { Say(Localization.Get(612)); return; }   // MGame::UseKhadorDrive: ChoiceWindow 612
                KhadorRequested?.Invoke();
                return;
            }
            Say($"{Localization.Get(546)}: {target.name}");
            Play(sounds?.autopilotOn);
            SetAutopilot(target);
        }

        /// <summary>Touch button / key held for fast-forward (checked every frame).</summary>
        public void SetFastForwardHeld(bool held) => fastForwardHeld = held;

        /// <summary>MGame::OnTouchBegin key 0x100: allowed while the autopilot or an asteroid approach runs, not within
        /// 20000 units of the target (and no hostile ships, none exist yet).</summary>
        public bool CanFastForward
        {
            get
            {
                if (Jumping || HostilesPresent || TimeExtender.Active) return false;
                if (Autopilot) return !AboutToReach;
                if (mining != null && mining.Target != null && mining.State == Mining.Phase.Approaching)
                    return (mining.Target.transform.position - ship.transform.position).magnitude / M >= AboutToReachUnits;
                return false;
            }
        }

        // ---- per frame -----------------------------------------------------------------------------------------

        void Update()
        {
            if (ship == null) return;
            float dtMs = Time.deltaTime * 1000f;
            // PlayerEgo::update: whatever set the player route (a campaign level, a freelance mission's waypoints, a
            // Challenge's course, step 59's convoy point), Route::update advances it from the ship's position every frame;
            // UpdateRoute says 543 / 544 (HUD events 0x17 / 0x18).
            if (playerRoute != null)
            {
                var p = ship.transform.position;
                playerRoute.Update(new Vector3(p.x, p.y, -p.z) / M);
            }
            if (Jumping) { UpdateJump(dtMs); return; }
            if (paused || pauseMenuOpen) return;
            if (Autopilot) AboutToReach = (AutopilotTarget.Position - ship.transform.position).magnitude / M < AboutToReachUnits;
            if (MenuOpen) return;   // paused
            UpdateLock(dtMs);
            SetFastForward(fastForwardHeld && CanFastForward);
        }

        void LateUpdate()
        {
            // dockToPlanet: TargetFollowCamera look-at mode, the camera stays put and keeps looking at the ship.
            if (!Jumping) return;
            var cam = Camera.main;
            if (cam != null) cam.transform.rotation = Quaternion.LookRotation(ship.transform.position - cam.transform.position, ship.transform.up);
        }

        /// <summary>Radar::draw, landmark and planet blocks.</summary>
        void UpdateLock(float dtMs)
        {
            UpdateRoute();
            UpdateDockingTargets();
            if (Docking != null && Docking.Busy) { Candidate = Locked = null; LockTimer = 0f; wasLocked = false; return; }
            if (wormholeTarget != null) wormholeTarget.hidden = !wormhole.Visible;
            // Radar::draw's planet block runs only above campaign mission 1 and outside the alien orbit: no planet locks,
            // names or icons in the prologue and the rescue (autopilot_travel.md 1.4).
            bool planets = Session.CampaignMission > 1 && !(layout != null && layout.alienOrbit);
            foreach (var t in Targets)
                if (t.kind == Kind.Planet)
                {
                    t.hidden = !planets;
                    if (t.hidden && (Locked == t || Candidate == t)) { Locked = Candidate = null; LockTimer = 0f; }
                }
            var cam = Camera.main;
            Target best = null;
            bool miningBusy = mining != null && (mining.State != Mining.Phase.Idle || mining.Locked != null);
            if (cam != null && !miningBusy)
            {
                var c = cam.WorldToScreenPoint(ship.transform.position + ship.transform.forward * CrosshairDistanceMeters);
                float w = Screen.width, h = Screen.height;
                float box = w / 16f, centre = w / 6f, planetBox = w / 32f;
                if (c.z > 0f)
                {
                    // Landmarks first (not during the autopilot), then planets (also during the autopilot).
                    foreach (var t in Targets)
                    {
                        if (t.kind == Kind.Planet || t.kind == Kind.Wormhole || t.kind == Kind.Marker || t.hidden || Autopilot) continue;
                        var p = cam.WorldToScreenPoint(t.Position);
                        if (p.z <= 0f || p.x < 0f || p.y < 0f || p.x > w || p.y > h) continue;
                        if (Mathf.Abs(p.x - w / 2f) >= centre || Mathf.Abs(p.y - h / 2f) >= centre) continue;
                        if (Mathf.Abs(p.x - c.x) < box && Mathf.Abs(p.y - c.y) < box) { best = t; break; }
                    }
                    if (best == null)
                        foreach (var t in Targets)
                        {
                            if (t.kind != Kind.Planet || t.hidden) continue;
                            var p = cam.WorldToScreenPoint(t.Position);
                            if (p.z <= 0f || p.x < 0f || p.y < 0f || p.x > w || p.y > h) continue;
                            if (Mathf.Abs(p.x - c.x) < planetBox && Mathf.Abs(p.y - c.y) < planetBox) { best = t; break; }
                        }
                }
            }
            if (best != Candidate) { Candidate = best; LockTimer = 0f; }
            if (Candidate == null) { Locked = null; wasLocked = false; return; }
            LockTimer += dtMs;
            Locked = LockTimer > LockTimeMs ? Candidate : null;   // strict >, no -200 ms here
            if (Locked != null && !wasLocked) Play(sounds?.targetLock);
            wasLocked = Locked != null;
            // The autopilot flying to a programmed station's planet jumps as soon as that planet is locked.
            if (Locked != null && Locked == AutopilotTarget && Locked.kind == Kind.Planet)
            {
                if (PlanetJumpRefused != null && PlanetJumpRefused(Locked.station)) { SetAutopilot(null); return; }
                StartJump(Locked);
            }
        }

        /// <summary>LevelScript::setAutoPilotToProgrammedStation: autopilot toward the programmed station (see the header);
        /// false when there is nothing to fly to.</summary>
        public bool ContinueToProgrammedStation()
        {
            int prog = Session.ProgrammedStation;
            if (prog < 0 || Jumping) return false;
            if (prog == layout.stationIndex) { Session.ProgrammedStation = -1; return false; }
            int system = db.Stations.Find(s => s.index == prog)?.system ?? -1;
            Target target;
            if (system == layout.systemIndex) target = Targets.Find(t => t.kind == Kind.Planet && t.station == prog);
            else if (layout.hasJumpgate) target = Targets.Find(t => t.kind == Kind.Jumpgate);
            else target = Targets.Find(t => t.kind == Kind.Planet && t.station == systemGateStation);
            if (target == null) return false;
            if (mining != null && mining.State != Mining.Phase.Idle) mining.Interact();
            Say(Localization.Get(571) + " " + Localization.Get(38));   // Autopilot On
            Play(sounds?.autopilotOn);
            SetAutopilot(target);
            return true;
        }

        /// <summary>The action prompt / Enter / controller X.</summary>
        public void Interact()
        {
            if (Jumping) return;
            if (Autopilot)
            {
                Say(Localization.Get(571) + " " + Localization.Get(39));   // Autopilot Off
                Play(sounds?.autopilotOff);
                SetAutopilot(null);
                return;
            }
            if (Locked == null) return;
            if (Locked.kind != Kind.Planet && layout.alienOrbit) return;
            if (Locked.kind == Kind.Planet && JumpsBlocked != null && JumpsBlocked()) { Say(Localization.Get(525)); return; }
            if (Locked.kind == Kind.Planet && PlanetJumpRefused != null && PlanetJumpRefused(Locked.station)) return;
            if (Locked.kind == Kind.Planet) StartJump(Locked);
            else if (Locked.kind == Kind.DockingTarget) { var s = Locked.dockingShip; SetAutopilot(null); Docking?.Dock(s); }
            else
            {
                Say($"{Localization.Get(546)}: {Locked.name}");                 // Target: Var Hastra Station
                Play(sounds?.autopilotOn);
                SetAutopilot(Locked);
            }
        }

        /// <summary>PlayerEgo::setAutoPilot: throttle to 100 % when turning on; turning off clears the locks.</summary>
        public void SetAutopilot(Target target)
        {
            AutopilotTarget = target;
            ship.autopilotTarget = target != null ? () => target.Position : null;
            if (target != null) ship.SetThrottle(1f);
            Locked = Candidate = null;
            LockTimer = 0f;
            AboutToReach = false;
        }

        // ---- planet jump (PlayerEgo::dockToPlanet 0xadd20) ----------------------------------------------------

        void StartJump(Target planet)
        {
            SetAutopilot(null);
            jumpTarget = planet;
            Jumping = true;
            weapons?.ResetGunDelay();   // PlayerEgo::dockToPlanet
            jumpMs = 0f;
            ship.externalControl = true;
            if (weapons != null) weapons.Blocked = true;
            if (chase != null) chase.enabled = false;
            Play(sounds?.jumpToPlanet);
            Haptics.Play(Haptics.Jump);   // remake
        }

        void UpdateJump(float dtMs)
        {
            // Straight on along the current heading, no steering, 8 u/ms regardless of the throttle.
            float step = dtMs * PlanetJumpSpeed * M;
            ship.transform.position += ship.transform.forward * step;
            ship.ExternalSpeedMetersPerSecond = Time.deltaTime > 0f ? step / Time.deltaTime : 0f;
            jumpMs += dtMs;
            if (jumpMs <= PlanetJumpMs) return;
            // MGame::OnUpdate: departStation(planet's station), stream-out arrival, reload the level in that orbit.
            weapons?.StoreAmmo();
            Session.PreviousStationIndex = Session.StationIndex;
            Session.StationIndex = jumpTarget.station;
            Session.ArrivedByTravel = true;
            Session.LaunchedFromStation = false;
            SetFastForward(false);
            enabled = false;
            if (Application.CanStreamedLevelBeLoaded(spaceScene)) SceneManager.LoadScene(spaceScene);
        }

        // ---- fast-forward ------------------------------------------------------------------------------------

        void SetFastForward(bool on)
        {
            FastForward = on;
            ApplyTimeScale();
        }

        /// <summary>A menu, conversation or map holds the game (single player: Time.timeScale 0). Multiplayer keeps the time
        /// running, so the player's controls ask this instead (InputHalted).</summary>
        static bool halted;

        /// <summary>The player's flight controls (steering, dodge, guns, mining) are off: the game is paused, or in multiplayer
        /// a menu, conversation or map is open (the world goes on there).</summary>
        public static bool InputHalted => Time.timeScale <= 0f || (halted && GoF2Remake.Multiplayer.NetGame.Active)
                                          || GoF2Remake.Multiplayer.NetScreen.QuestionOpen;   // an event's question (multiplayer)

        void OnDestroy() { halted = false; MusicPaused = false; if (!GoF2Remake.Multiplayer.NetGame.Active) AudioListener.pause = false; }

        /// <summary>MGame::pauseSounds 0x1a8304: a conversation, hint or map pauses only the sound effects and the engines
        /// (FModSound::pauseAllPlayingSoundFXEvents: the events of the effects category); only the pause menu stops
        /// everything (MGame::OnTouchEnd: FModSound::pauseAllPlaying). The music sources ignore the listener pause and
        /// follow this instead (SyncMusic).</summary>
        public static bool MusicPaused { get; private set; }

        /// <summary>A music source keeps playing through the listener pause and pauses with the pause menu only.</summary>
        public static void SyncMusic(AudioSource music, ref bool held)
        {
            if (music == null) return;
            music.ignoreListenerPause = true;
            if (MusicPaused == held) return;
            held = MusicPaused;
            if (held) music.Pause();
            else music.UnPause();
        }

        void ApplyTimeScale()
        {
            halted = MenuOpen || paused || pauseMenuOpen;
            float scale = MenuOpen || paused || pauseMenuOpen ? 0f : FastForward ? FastForwardScale : TimeExtender.Active ? TimeExtender.WorldScale : 1f;
            if (GoF2Remake.Multiplayer.NetGame.Active) scale = 1f;   // multiplayer: one player's pause doesn't stop the shared world
            if (Time.timeScale != scale) Time.timeScale = scale;
            // A halted clock also halts the sound, as PauseMenu does: the engine loops, a boost fired just before and every
            // other source played on through a conversation. The voice, UI and star-map sources ignore the listener pause.
            if (!GoF2Remake.Multiplayer.NetGame.Active) AudioListener.pause = scale == 0f;
            MusicPaused = pauseMenuOpen && !GoF2Remake.Multiplayer.NetGame.Active;
        }

        void Say(string text) => Message?.Invoke(text);

        void Play(AudioClip clip)
        {
            if (clip != null) sfx.PlayOneShot(clip, Settings.SfxVolume);
        }

        /// <summary>Radar::calcDistance 0x15827c: M = 8 * floor(d / 128); "624m" below 1000, else "6.2km".</summary>
        public static string FormatDistance(float units)
        {
            int m = 8 * (int)(units / 128f);
            if (m < 1000) return m + "m";
            int frac = m % 1000;
            return $"{m / 1000}.{(frac >= 100 ? frac.ToString()[0] : '0')}km";
        }
    }
}
