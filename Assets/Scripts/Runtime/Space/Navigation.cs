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
//                                 autopilot ending stops it, and hostile ships around block it (Radar+0x54); remake,
//                                 multiplayer: a cruise mode instead, only the player's ship 5x as fast (Cruise)
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
    public partial class Navigation : MonoBehaviour
    {
        public enum Kind { Station, Jumpgate, Planet, AsteroidField, Destination, KhadorDrive, Slipstream, System, Waypoint, Wingmen, Cloak, Wormhole, DockingTarget, Secondary, TimeExtender,
                           Distress, Assist,
            /// <summary>Remake multiplayer: a named object (/spawn ... named, DebugSpawner.SpawnObject): its name and distance
            /// near the crosshair like a landmark's, never locked or flown to.</summary>
            Marker,
            /// <summary>Remake multiplayer: follow a squadmate from orbit to orbit, or stop (NetFollow).</summary>
            Follow }

        public class Target
        {
            public Kind kind;
            public Transform transform;      // null for fixed positions
            public Vector3 fixedPosition;
            public int station = -1;         // planets: the station it leads to (interstellar systems: their arrival)
            public int system = -1;          // interstellar systems: the system
            public bool disabled;            // drawn half-transparent, ignores taps (the cloak while not ready)
            public bool hidden;              // not drawn and not lockable now (the wormhole while invisible)
            public string name;
            public NpcShip dockingShip;     // docking targets: the object
            public ulong client;             // multiplayer Assist / Follow: the squadmate calling for help / to follow
            public bool freelance;          // waypoints: a freelance mission's route (the white freelance icon, not the gold story one)
            public bool eventWaypoint;      // waypoints: the event's own one (SetEventWaypoint), beside the player route
            public int techLevel = -1;      // stations: the tech level its marker shows (-1 = the orbit's station's)
            public Vector3 Position => transform != null ? transform.position : fixedPosition;
        }

        const float M = 0.05f;
        const float CrosshairDistanceMeters = 22000f * M;
        public const float AboutToReachUnits = 20000f;   // PlayerEgo+0x330
        const float FastForwardScale = 5f;
        const float PlanetJumpMs = 3000f, PlanetJumpSpeed = 8f;   // dockToPlanet: 8 u/ms for 3 s
        // Remake: the planet jump's rumble comes as its sound (5 Jump_to_planets) swells, not as it starts: no flash to go by,
        // the sound climbs from 0.5 s, holds above 60 % from 1.25 s and peaks at 1.5 s.
        const float PlanetJumpRumbleMs = 1250f;

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
        /// <summary>A story or freelance conversation is open (StorySpace, FreelanceOrbit): paused as in the original, unless
        /// Options > Gameplay "Game runs in conversations" (Settings.PlayOnInConversations) keeps the world going (ConversationRuns).</summary>
        public bool Conversation { get => conversation; set { conversation = value; ApplyTimeScale(); } }
        bool conversation;
        /// <summary>Remake: a conversation is open and single player runs on (Settings.PlayOnInConversations): the controls stop
        /// as in multiplayer (InputHalted), the player can't be hurt, the NPCs hold fire and the story level holds
        /// (CampaignLevel.Held).</summary>
        public static bool ConversationRuns { get; private set; }
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
        public bool GoingToStation => AutopilotTarget?.kind == Kind.Station && AutopilotTarget.transform == null;
        /// <summary>Remake mods: the autopilot flies to a mobile station (World.MobileStation: a Station target that moves).</summary>
        public Target GoingToMobile => AutopilotTarget?.kind == Kind.Station && AutopilotTarget.transform != null ? AutopilotTarget : null;

        /// <summary>Remake mods: a mobile station in this orbit (World.MobileStation): a station landmark that follows it.</summary>
        public Target AddMobileStation(Transform station, int index, string name, int tech)
        {
            var t = new Target { kind = Kind.Station, transform = station, station = index, name = name, techLevel = tech };
            Targets.Add(t);
            return t;
        }
        public bool GoingToGate => AutopilotTarget?.kind == Kind.Jumpgate;
        /// <summary>A campaign level's player route (Level+0x108, PlayerEgo::setRoute): its current waypoint is a landmark
        /// target named "Waypoint" (548): marked on the HUD, lockable, and the autopilot flies to it; "Waypoint reached."
        /// (543) / "Last waypoint reached." (544) as the route advances.</summary>
        public void SetRoute(Route route, bool freelance = false) => SetRoute(ref playerRoute, ref routeTarget, ref routeIndex, route, freelance, false);

        /// <summary>Remake events: an event's waypoint (EventWaypoint: /waypoint, the graphs' Set Waypoint) in a slot of its own
        /// beside the player route, so an event or a multiplayer bar mission graph and a campaign / freelance route both show
        /// and clearing one leaves the other (a Discord report: the Pirate Hideout's waypoint replaced a lounge mission's, and
        /// its Clear Waypoint removed both). 'name' (null = 548 "Waypoint") is the lock plate's and the autopilot menu's.</summary>
        public void SetEventWaypoint(Route route, string name, bool freelance)
        {
            if (route != null && name != null) route.name = name;
            SetRoute(ref eventRoute, ref eventTarget, ref eventIndex, route, freelance, true);
        }

        void SetRoute(ref Route slot, ref Target target, ref int index, Route route, bool freelance, bool isEvent)
        {
            if (target != null)
            {
                Targets.Remove(target);
                if (Locked == target || Candidate == target) { Locked = Candidate = null; LockTimer = 0f; }
                if (isEvent && AutopilotTarget == target) SetAutopilot(null);   // the event's waypoint went: nothing to fly to
            }
            slot = route;
            target = null;
            if (route == null) return;
            target = new Target { kind = Kind.Waypoint, name = route.name ?? Localization.Get(548), freelance = freelance, eventWaypoint = isEvent };
            index = route.index;
            UpdateRoute(slot, target, ref index);
        }

        Route playerRoute, eventRoute;
        Target routeTarget, eventTarget;
        int routeIndex, eventIndex;

        void UpdateRoute()
        {
            UpdateRoute(playerRoute, routeTarget, ref routeIndex);
            UpdateRoute(eventRoute, eventTarget, ref eventIndex);
        }

        void UpdateRoute(Route route, Target target, ref int index)
        {
            if (route == null || target == null) return;
            if (route.index != index)
            {
                index = route.index;
                Say(Localization.Get(route.Waypoint == null ? 544 : 543));
                if (AutopilotTarget == target) SetAutopilot(route.Waypoint == null ? null : target);
            }
            var wp = route.Waypoint;
            if (wp == null) { Targets.Remove(target); if (Locked == target || Candidate == target) Locked = Candidate = null; return; }
            target.fixedPosition = new Vector3(wp.Value.x, wp.Value.y, -wp.Value.z) * M;
            if (!Targets.Contains(target)) Targets.Add(target);
        }

        /// <summary>Landmark 3: named 545 "Wormhole" near the centre, icon 0x450 elsewhere; never a lock candidate (Radar::draw).</summary>
        public void SetWormhole(GoF2Remake.World.Wormhole wormhole)
        {
            this.wormhole = wormhole;
            if (wormholeTarget != null) Targets.Remove(wormholeTarget);
            wormholeTarget = null;
            if (wormhole == null) return;
            wormholeTarget = new Target { kind = Kind.Wormhole, transform = wormhole.transform, name = Localization.Get(545), hidden = !wormhole.Visible };
            Targets.Add(wormholeTarget);
        }
        GoF2Remake.World.Wormhole wormhole, eventWormhole;
        Target wormholeTarget, eventWormholeTarget;

        /// <summary>Remake events: an event's wormhole, marked like landmark 3 (never a lock candidate).</summary>
        public void SetEventWormhole(GoF2Remake.World.Wormhole w)
        {
            eventWormhole = w;
            if (eventWormholeTarget != null) Targets.Remove(eventWormholeTarget);
            eventWormholeTarget = null;
            if (w == null) return;
            eventWormholeTarget = new Target { kind = Kind.Wormhole, transform = w.transform, name = Localization.Get(545), hidden = !w.Visible };
            Targets.Add(eventWormholeTarget);
        }

        /// <summary>ObjectDocking on the player (set by the level): locked docking targets dock through it.</summary>
        [NonSerialized] public ObjectDocking Docking;
        /// <summary>The level's ships (Traffic.Ships): those with a docking type are docking targets.</summary>
        [NonSerialized] public List<NpcShip> Ships;

        // A loop, not Targets.Exists(t => t.dockingShip == s): that closure was allocated per traffic ship per frame.
        bool HasDockingTarget(NpcShip s)
        {
            foreach (var t in Targets) if (t.dockingShip == s) return true;
            return false;
        }

        /// <summary>Level::getDockingTarget: the dockable objects, lockable while visible and not radar-hidden.</summary>
        void UpdateDockingTargets()
        {
            if (Ships == null) return;
            foreach (var s in Ships)
            {
                if (s == null || s.DockingType <= 0) continue;
                if (!HasDockingTarget(s))
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
                // a hostile or fighting carrier is a ship then (NpcShip.OffersDocking, CapitalShips.DeckOpen)
                t.hidden = s == null || s.Gone || !s.Target.Alive || !s.OffersDocking || s.RadarHidden || s.Hidden || s.Inactive;
                if (t.hidden && (Locked == t || Candidate == t)) { Locked = Candidate = null; LockTimer = 0f; }
            }
        }

        /// <summary>Set by the level: planet jumps and the Khador Drive are refused (story, campaign_flow.md 7).</summary>
        public Func<bool> JumpsBlocked;
        /// <summary>Set by the level: the HUD text for a jump JumpsBlocked refuses, null = 525 "Not possible on a mission."
        /// (42 in the Void: the way out is the wormhole, Story.EscapesByWormhole).</summary>
        public Func<string> JumpRefusalText;
        string JumpRefusal => JumpRefusalText?.Invoke() ?? Localization.Get(525);
        /// <summary>Set by the level: a level's cutscene or the launch / arrival fly-in runs. MGame::OnRender2D skips
        /// Radar::draw then, so no lock completes and no lock sound plays (a player's report: the lock beep in step 160's cutscene).</summary>
        public Func<bool> RadarHidden;
        public bool RadarOff => RadarHidden != null && RadarHidden();
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
        /// <summary>Remake, multiplayer: the shared world can't run 5x for one player, so the held fast-forward is a cruise
        /// mode there instead, under the same rules (CanFastForward): only the player's own ship flies 5x as fast (the
        /// autopilot, the asteroid approach, an object's approach); its turns, timers and the camera keep their pace.</summary>
        public bool Cruise { get; private set; }
        /// <summary>The player ship's forward speed factor (ShipController, Mining's approach): from 1 up to FastForwardScale
        /// while cruising, eased in over CruiseRampUpMs (smoothstep) and back over CruiseRampDownMs; or the mounted cruise
        /// drive's factor (CruiseDrive.Factor, its own ramp) while that runs (NavigationCruiseDrive).</summary>
        public static float CruiseFactor { get; private set; } = 1f;
        /// <summary>The cruise is held and allowed now, or the cruise drive runs (its boost look, FlightModel.CruiseVisual, follows this).</summary>
        public static bool Cruising { get; private set; }
        const float CruiseRampUpMs = 3000f, CruiseRampDownMs = 1200f;
        float cruiseLevel;
        /// <summary>Lock ring frame 0..23 (no 500 ms delay for landmarks and planets), -1 = none.</summary>
        public int LockFrame => Candidate == null ? -1 : Locked != null ? 23 : Mathf.Min(23, (int)(23f * LockTimer / Mathf.Max(1, LockTimeMs)));
        /// <summary>Radar::draw 0x1574c0 skips the asteroid loop on a landmark (+0x24) or planet (+0x14) lock, a ship / crate
        /// candidate (+8, and local_f8 in the candidate test), a crate on the tractor beam (+0x1c) or the autopilot; a
        /// landmark or planet that is only a candidate (+0x28 / +0x18) doesn't stop it (both rings fill).</summary>
        /// The remake's lockable route waypoint blocks nothing (the original can't lock it, #28).</summary>
        public bool BlocksAsteroidLock => (Locked != null && Locked.kind != Kind.Waypoint) || Autopilot || Jumping || ShipLockActive;
        /// <summary>A landmark (station, jumpgate, docking target) is locked: Radar+0x24, which stops the ship lock's timer
        /// (Radar::draw 0x1558ae); a planet lock (+0x14) doesn't, nor the remake's waypoint (the junk of a Junk removal lies
        /// around it: its lock wiped every junk candidate, #28).</summary>
        public bool LandmarkLocked => Locked != null && Locked.kind != Kind.Planet && Locked.kind != Kind.Waypoint;
        /// <summary>CombatRadar has a ship / crate candidate this frame or a crate on the tractor beam.</summary>
        [NonSerialized] public bool ShipLockActive;
        /// <summary>Radar+0x54: hostile ships around (Traffic): no fast-forward.</summary>
        [NonSerialized] public bool HostilesPresent;
        public bool AboutToReach { get; private set; }
        public event Action<string, HudMessageKind> Message;

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
            SetupDrive(database);   // remake: a mounted cruise drive (NavigationCruiseDrive)
            SetupInterstellar();   // remake: the suns of the space between the systems (NavigationInterstellar)
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
            if (layout.systemIndex >= 0 && layout.asteroidCount > 0)   // remake mods: an orbit without a field has no entry
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
            cruiseLevel = 0f; CruiseFactor = 1f; Cruising = false;
            ApplyTimeScale();
        }

        // ---- autopilot menu (Hud::initHudMenu(3)) ------------------------------------------------------------

        /// <summary>The autopilot menu: Hud::initHudMenu(3)'s entries in its order. 'actions' = the quick menu instead,
        /// Hud::initHudMenu(0): secondary weapons, the cloak, the Khador Drive and the wingmen.</summary>
        public List<Target> MenuEntries(bool actions = false)
        {
            var list = new List<Target>();
            if (actions) return ActionEntries(list);
            if (Interstellar) return SystemEntries(list);
            if (!layout.alienOrbit)
            {
                if (AsteroidField != null) list.Add(AsteroidField);
                foreach (var station in Targets)   // the orbit's station, then any mobile station here (remake mods)
                    if (station.kind == Kind.Station && !station.hidden) list.Add(station);
                var gate = Targets.Find(t => t.kind == Kind.Jumpgate);
                if (gate != null) list.Add(gate);
                // 573 "Waypoint": the player route's last waypoint isn't reached (Route::getLastWaypoint +300).
                if (playerRoute != null && playerRoute.Waypoint != null && routeTarget != null)
                    list.Add(new Target { kind = Kind.Waypoint, name = playerRoute.name ?? Localization.Get(573) });
                if (eventRoute != null && eventRoute.Waypoint != null && eventTarget != null)
                    list.Add(new Target { kind = Kind.Waypoint, name = eventRoute.name ?? Localization.Get(573), eventWaypoint = true });
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
            // Remake: not while docked at an object (it holds the ship: undock first).
            if (!Interstellar && GalaxyMap.HasJumpDrive(db) && (Docking == null || Docking.State == ObjectDocking.Phase.Idle || Docking.State == ObjectDocking.Phase.Approach)) list.Add(new Target { kind = Kind.KhadorDrive, name = Localization.Get(1359) });
            AddSlipstreamEntry(list);   // remake: the mounted drive that flies between the systems (NavigationInterstellar)
            // Remake: the time extender too (the original has it only on its own touch slot), so one menu holds every
            // device: secondaries, wingmen, cloak, Khador Drive and the time extender (half-transparent while recharging).
            if (Extender != null)
            {
                var te = Shop.FirstMounted(db, 26);
                list.Add(new Target { kind = Kind.TimeExtender, name = te != null ? GameNames.Item(te.index) : "Time extender",
                                      disabled = !Extender.Ready && !Extender.Running });
            }
            // Multiplayer (remake): Help for every squadmate calling for help (NetDistress). The pilot's own call isn't here
            // (a menu key picked it by accident): the flight HUD's Distress call button or its own binding.
            if (GoF2Remake.Data.NetSession.Active && GoF2Remake.World.SpaceHooks.LocalSquad != 0)
            {
                foreach (var m in GoF2Remake.World.SpaceHooks.Squad)
                    if (m != null && !m.IsOwner && m.Distress)
                        list.Add(new Target { kind = Kind.Assist, client = m.OwnerClientId,
                                              name = string.Format(Localization.Extra("mpHelpEntry", "Help {0}"), m.DisplayName) });
                // Follow each squadmate from orbit to orbit (NetFollow), or stop following the one followed.
                ulong followed = GoF2Remake.World.SpaceHooks.FollowedSquadmate?.Invoke() ?? ulong.MaxValue;
                foreach (var m in GoF2Remake.World.SpaceHooks.Squad)
                    if (m != null && !m.IsOwner)
                        list.Add(new Target { kind = Kind.Follow, client = m.OwnerClientId,
                                              name = string.Format(m.OwnerClientId == followed ? Localization.Extra("mpUnfollowEntry", "Stop following {0}")
                                                                                              : Localization.Extra("mpFollowEntry", "Follow {0}"), m.DisplayName) });
            }
            return list;
        }

        /// <summary>MGame::OnTouchEnd, autopilot button: only while nothing else flies the ship; pauses the game.</summary>
        /// <summary>MGame::OnTouchEnd's autopilot button: not in the Supernova Challenge (mission type 0xb7: Hud::initHudMenu(3)
        /// isn't opened and Hud::draw leaves the button out).</summary>
        public bool CanOpenMenu => !Autopilot && !Jumping && !paused && (mining == null || mining.State == Mining.Phase.Idle) && (Docking == null || !Docking.Busy)
                                   && !SupernovaChallenge.Active;
        /// <summary>MGame::OnTouchEnd, quick menu button (key 4): refused only while mining (PlayerEgo::isMining), so it opens
        /// on the autopilot and docked at an object too (remake: not while jumping, easing in or out of a docking or hacking;
        /// docked it was refused until a player's report at 135's mining plant).</summary>
        public bool CanOpenActions => !Jumping && !paused && (mining == null || mining.State == Mining.Phase.Idle)
                                      && (Docking == null || ((Docking.State == ObjectDocking.Phase.Idle || Docking.State == ObjectDocking.Phase.Approach
                                                               || Docking.State == ObjectDocking.Phase.Docked) && Docking.Hacking == null));

        /// <summary>The open menu is the quick (action) menu, not the autopilot's.</summary>
        public bool MenuIsActions { get; private set; }

        /// <summary>'force': whatever flies the ship (the carrier's resupply menu while docked at it).</summary>
        public void OpenMenu(bool actions = false, bool force = false)
        {
            if (!force && (actions ? !CanOpenActions : !CanOpenMenu)) return;
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
            if (target.kind == Kind.Distress) { Say(GoF2Remake.World.SpaceHooks.ToggleDistress?.Invoke()); return; }
            if (target.kind == Kind.Assist)
            {
                string msg = GoF2Remake.World.SpaceHooks.HelpSquadmate?.Invoke(target.client);
                if (!string.IsNullOrEmpty(msg)) Say(msg);
                return;
            }
            if (target.kind == Kind.Follow)
            {
                string msg = GoF2Remake.World.SpaceHooks.FollowSquadmate?.Invoke(target.client);
                if (!string.IsNullOrEmpty(msg)) Say(msg);
                return;
            }
            if (target.kind == Kind.TimeExtender) { Extender?.Toggle(); return; }   // the game runs again: CloseMenu above
            if (target.kind == Kind.DockingTarget) { Docking?.Dock(target.dockingShip); return; }
            if (target.kind == Kind.Waypoint)
            {
                // MGame::OnTouchEnd key 0x2000000: HUD event 0xd "Target: Waypoint" + 28; the autopilot follows the route.
                var route = target.eventWaypoint ? eventRoute : playerRoute;
                var to = target.eventWaypoint ? eventTarget : routeTarget;
                if (to == null || route == null || route.Waypoint == null) return;
                Say($"{Localization.Get(546)}: {route.name ?? Localization.Get(573)}", HudMessageKind.Target);
                Play(sounds?.autopilotOn);
                SetAutopilot(to);
                return;
            }
            if (target.kind == Kind.KhadorDrive)
            {
                if (JumpsBlocked != null && JumpsBlocked() && Story.ForcedKhadorTarget(Session.StationIndex) == null) { Say(JumpRefusal); return; }
                if (GalaxyMap.HasVolatileGoods) { Say(Localization.Get(612)); return; }   // MGame::UseKhadorDrive: ChoiceWindow 612
                KhadorRequested?.Invoke();
                return;
            }
            if (target.kind == Kind.Slipstream) { RequestInterstellar(); return; }
            if (target == VoidDive) StartVoidDive();
            Say($"{Localization.Get(546)}: {target.name}", HudMessageKind.Target);
            Play(sounds?.autopilotOn);
            SetAutopilot(target);
        }

        /// <summary>Touch button / key held for fast-forward (checked every frame).</summary>
        public void SetFastForwardHeld(bool held) => fastForwardHeld = held;

        /// <summary>MGame::OnTouchBegin key 0x100: allowed while the autopilot, an asteroid approach or the approach to an
        /// object's docking point runs (PlayerEgo::isDockingToDockingPoint; the remake left that one out, a player's report at
        /// 135's mining plant), not within 20000 units of the target, and no hostile ships.</summary>
        public bool CanFastForward
        {
            get
            {
                if (Interstellar || Jumping || HostilesPresent || TimeExtender.Active) return false;   // (the drive has the button there)
                if (Autopilot) return !AboutToReach;
                if (mining != null && mining.Target != null && mining.State == Mining.Phase.Approaching)
                    return (mining.Target.transform.position - ship.transform.position).magnitude / M >= AboutToReachUnits;
                if (Docking != null && Docking.ApproachDistanceUnits >= AboutToReachUnits) return true;
                return false;
            }
        }

        // ---- per frame -----------------------------------------------------------------------------------------

        void Update()
        {
            if (ship == null) return;
            float dtMs = Time.deltaTime * 1000f;
            UpdateDrive(dtMs);
            UpdateCruise(dtMs);
            // PlayerEgo::update: whatever set the player route (a campaign level, a freelance mission's waypoints, a
            // Challenge's course, step 59's convoy point), Route::update advances it from the ship's position every frame;
            // UpdateRoute says 543 / 544 (HUD events 0x17 / 0x18).
            if (playerRoute != null || eventRoute != null)
            {
                var p = ship.transform.position;
                var at = new Vector3(p.x, p.y, -p.z) / M;
                playerRoute?.Update(at);
                eventRoute?.Update(at);
            }
            if (Jumping) { UpdateJump(dtMs); return; }
            if (paused || pauseMenuOpen) return;
            if (Autopilot) AboutToReach = RunPlanet == null && (AutopilotTarget.Position - ship.transform.position).magnitude / M < AboutToReachUnits;
            if (MenuOpen) return;   // paused
            UpdateLock(dtMs);
            UpdateInterstellar();
            UpdatePlanetRun();
            SetFastForward(fastForwardHeld && CanFastForward);
        }

        void LateUpdate()
        {
            if (InputHalted) lastHaltedFrame = Time.frameCount;
            // dockToPlanet: TargetFollowCamera look-at mode, the camera stays put and keeps looking at the ship.
            if (!Jumping) return;
            var cam = Camera.main;
            if (cam != null) cam.transform.rotation = Quaternion.LookRotation(ship.transform.position - cam.transform.position, ship.transform.up);
        }

        /// <summary>The orbit's station object (SpaceLevel): a level script that hides it (78: the Valkyrie jumps away) takes
        /// its landmark with it.</summary>
        public GameObject StationObject;

        /// <summary>Set by a level whose station a docking target stands in for (remake, 102: Tadram's pads): the station's own
        /// landmark leaves the locks, markers and the autopilot menu, so the two don't sit on top of each other.</summary>
        public bool StationReplaced;

        /// <summary>Level::getLandmarks' station hidden with its object (#49: the autopilot menu still offered the Valkyrie
        /// after it had jumped away, and flew to an empty spot): no lock, marker, menu entry or autopilot to it.</summary>
        void UpdateStationShown()
        {
            if (StationObject == null) return;
            bool gone = !StationObject.activeInHierarchy || StationReplaced;
            foreach (var t in Targets)
            {
                if (t.kind != Kind.Station || t.transform != null || t.hidden == gone) continue;   // (not a mobile station)
                t.hidden = gone;
                if (!gone) continue;
                if (Locked == t || Candidate == t) { Locked = Candidate = null; LockTimer = 0f; }
                if (AutopilotTarget == t) SetAutopilot(null);
            }
        }

        /// <summary>Radar::draw, landmark and planet blocks.</summary>
        void UpdateLock(float dtMs)
        {
            UpdateRoute();
            UpdateDockingTargets();
            if (Docking != null && Docking.Busy) { Candidate = Locked = null; LockTimer = 0f; wasLocked = false; return; }
            if (wormholeTarget != null) wormholeTarget.hidden = !wormhole.Visible;
            if (eventWormholeTarget != null) eventWormholeTarget.hidden = eventWormhole == null || !eventWormhole.Visible;
            UpdateStationShown();
            // Radar::draw's planet block runs only above campaign mission 1 and outside the alien orbit: no planet locks,
            // names or icons in the prologue and the rescue (autopilot_travel.md 1.4).
            bool planets = Session.CampaignMission > 1 && !(layout != null && layout.alienOrbit);
            foreach (var t in Targets)
                if (t.kind == Kind.Planet)
                {
                    t.hidden = !planets;
                    if (t.hidden && (Locked == t || Candidate == t)) { Locked = Candidate = null; LockTimer = 0f; }
                }
            // Remake: no lock at all while the radar is off (a cutscene, the fly-in, the jump scenes: MGame::OnRender2D skips
            // Radar::draw); it only kept the lock sound quiet, so the scanner locked stations and planets behind the scenes.
            if (RadarOff) { Candidate = Locked = null; LockTimer = 0f; wasLocked = false; return; }
            var cam = Camera.main;
            Target best = null;
            bool miningBusy = mining != null && (mining.State != Mining.Phase.Idle || mining.Locked != null);
            if (cam != null && !miningBusy)
            {
                var c = cam.WorldToScreenPoint(ChaseCamera.AimOrigin(ship.transform) + ship.transform.forward * CrosshairDistanceMeters);
                float w = Screen.width, h = Screen.height;
                float box = w / 16f, centre = w / 6f, planetBox = w / 32f;
                if (c.z > 0f)
                {
                    // Landmarks first (not during the autopilot), then planets (also during the autopilot).
                    foreach (var t in Targets)
                    {
                        if (t.kind == Kind.Planet || t.kind == Kind.Wormhole || t.kind == Kind.Marker || t.hidden || Autopilot) continue;
                        if (t.kind == Kind.Waypoint && ShipLockActive) continue;   // remake: a ship / junk / crate in the box wins
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
            if (Locked != null && !wasLocked && !RadarOff) Play(sounds?.targetLock);
            wasLocked = Locked != null;
            // The autopilot flying to a programmed station's planet jumps as soon as that planet is locked.
            if (Locked != null && Locked == AutopilotTarget && Locked.kind == Kind.Planet && Locked != RunPlanet)
            {
                if (PlanetJumpRefused != null && PlanetJumpRefused(Locked.station)) { SetAutopilot(null); return; }
                if (JumpsBlocked != null && JumpsBlocked()) { Refuse(JumpRefusal); return; }   // Radar::draw: 525 instead of the jump
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

        /// <summary>MGame::OnTouchBegin 0x1a838c in the alien orbit: only the landmark block (autopilot to the Void station) is
        /// skipped; a planet still jumps and a locked docking target (Radar+4) still docks (PlayerEgo::dockToDockingPoint),
        /// as at 154's Valkyrie (a player's report: the DOCK prompt showed, and the action did nothing).</summary>
        public static bool ActsInAlienOrbit(Kind kind) => kind == Kind.Planet || kind == Kind.DockingTarget;

        /// <summary>The action prompt / Enter / controller X; false when nothing happened (a refused jump, nothing locked),
        /// so the touch fire button shoots instead.</summary>
        public bool Interact()
        {
            if (Jumping) return false;
            if (Autopilot)
            {
                Say(Localization.Get(571) + " " + Localization.Get(39));   // Autopilot Off
                Play(sounds?.autopilotOff);
                SetAutopilot(null);
                return true;
            }
            if (Locked == null) return false;
            if (layout.alienOrbit && !ActsInAlienOrbit(Locked.kind)) return false;
            if (Locked.kind == Kind.Planet && JumpsBlocked != null && JumpsBlocked()) { Say(JumpRefusal); return false; }
            if (Locked.kind == Kind.Planet && PlanetJumpRefused != null && PlanetJumpRefused(Locked.station)) return false;
            if (Locked.kind == Kind.Planet) StartJump(Locked);
            else if (Locked.kind == Kind.DockingTarget) { var s = Locked.dockingShip; SetAutopilot(null); Docking?.Dock(s); }
            else
            {
                Say($"{Localization.Get(546)}: {Locked.name}", HudMessageKind.Target);                 // Target: Var Hastra Station
                Play(sounds?.autopilotOn);
                SetAutopilot(Locked);
            }
            return true;
        }

        /// <summary>Remake: an arrival's warp is easing out: it runs its course, no drive and no autopilot till then.</summary>
        public bool ArrivalHeld { get; set; }

        /// <summary>PlayerEgo::setAutoPilot: throttle to 100 % when turning on; turning off clears the locks.</summary>
        public void SetAutopilot(Target target)
        {
            if (ArrivalHeld && target != null) return;
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
            if (!StartPlanetRun(planet)) JumpToPlanet(planet);
        }

        void JumpToPlanet(Target planet)
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
        }

        void UpdateJump(float dtMs)
        {
            // Straight on along the current heading, no steering, 8 u/ms regardless of the throttle.
            float step = dtMs * PlanetJumpSpeed * M;
            ship.transform.position += ship.transform.forward * step;
            ship.ExternalSpeedMetersPerSecond = Time.deltaTime > 0f ? step / Time.deltaTime : 0f;
            jumpMs += dtMs;
            if (jumpMs >= PlanetJumpRumbleMs && jumpMs - dtMs < PlanetJumpRumbleMs) Haptics.Play(Haptics.Jump);   // remake
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

        void UpdateCruise(float dtMs)
        {
            bool cruise = Cruise && !halted && !Jumping;
            Cruising = cruise || DriveCruising;
            cruiseLevel = Mathf.MoveTowards(cruiseLevel, cruise ? 1f : 0f, Mathf.Max(0f, dtMs) / (cruise ? CruiseRampUpMs : CruiseRampDownMs));
            float multiplayer = 1f + (FastForwardScale - 1f) * cruiseLevel * cruiseLevel * (3f - 2f * cruiseLevel);
            CruiseFactor = Mathf.Max(multiplayer, Drive != null ? Drive.Factor : 1f);   // remake: the cruise drive's own ramp
        }

        void SetFastForward(bool on)
        {
            bool session = GoF2Remake.Data.NetSession.Active;
            FastForward = on && !session;
            Cruise = on && session;
            ApplyTimeScale();
        }

        /// <summary>A menu, conversation or map holds the game (single player: Time.timeScale 0). Multiplayer keeps the time
        /// running, so the player's controls ask this instead (InputHalted).</summary>
        static bool halted;

        /// <summary>The player's flight controls (steering, dodge, guns, mining) are off: the game is paused, or in multiplayer
        /// a menu, conversation or map is open (the world goes on there).</summary>
        public static bool InputHalted => Time.timeScale <= 0f || ConversationRuns || (GoF2Remake.Data.NetSession.Active && (halted || GoF2Remake.World.SpaceHooks.MultiplayerWindowIsOpen))
                                          || GoF2Remake.World.SpaceHooks.QuestionOpen   // an event's question (multiplayer)
                                          || GoF2Remake.World.SpaceHooks.DockedInOrbit;  // remake mods: docked inside the orbit

        /// <summary>Remake (#85): the flight controls' presses wait while a menu, conversation or map halts them and for the frame
        /// after it closes: the press that closed it (A on Resume, a menu entry) went on to boost, as the ship's controls run
        /// later in that frame. Steering reads the stick's state and needs no guard.</summary>
        public static bool PressesBlocked => InputHalted || Time.frameCount - lastHaltedFrame <= 1;
        static int lastHaltedFrame = -10;

        void OnDestroy() { halted = ConversationRuns = false; CruiseFactor = 1f; Cruising = false; MusicPaused = false; if (!GoF2Remake.Data.NetSession.Active) AudioListener.pause = false; }

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
            halted = MenuOpen || paused || pauseMenuOpen || conversation;
            bool talkRuns = conversation && Settings.PlayOnInConversations;
            float scale = MenuOpen || paused || pauseMenuOpen || (conversation && !talkRuns) ? 0f : FastForward ? FastForwardScale : TimeExtender.Active ? TimeExtender.WorldScale : 1f;
            if (GoF2Remake.Data.NetSession.Active) scale = 1f;   // multiplayer: one player's pause doesn't stop the shared world
            ConversationRuns = talkRuns && !GoF2Remake.Data.NetSession.Active;   // multiplayer: the co-op hold instead
            if (Time.timeScale != scale) Time.timeScale = scale;
            // A halted clock also halts the sound, as PauseMenu does: the engine loops, a boost fired just before and every
            // other source played on through a conversation. The voice, UI and star-map sources ignore the listener pause.
            if (!GoF2Remake.Data.NetSession.Active) AudioListener.pause = scale == 0f;
            MusicPaused = pauseMenuOpen && !GoF2Remake.Data.NetSession.Active;
        }

        void Say(string text, HudMessageKind kind = HudMessageKind.Normal) => Message?.Invoke(text, kind);

        void Play(AudioClip clip)
        {
            if (clip != null) sfx.PlayOneShot(GoF2Remake.Modding.ModSounds.Get(clip), Settings.SfxVolume);
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
