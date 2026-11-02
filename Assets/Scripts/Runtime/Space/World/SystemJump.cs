// SystemJump.cs
// Travel to another system (Reference/research/starmap_travel.md 6, 8, 9), on the player ship next to Navigation:
//   MGame::dockEvent 0x1afebc        the autopilot to the jumpgate enters its sphere (7500, Vossk 11250): dockToStream (input
//                                    locked, HUD hidden, game paused); with a programmed station "Destination: X / Travel to
//                                    this station?" (574 + 421, Yes preselected), No or no destination: the star map in gate
//                                    mode. Picking a station starts the jump; backing out puts the ship at gate + (0, 0, 8000)
//                                    with the gate's direction and gives the controls back.
//   MGame::startJumpScene 0x1abf10 / updateJumpScene 0x1afad4   gate: the ship at gate - (0, 0, 10000) facing game +Z at
//                                    2 u/ms, a fixed look-at camera at gate + (-2000, 300, -6000) drifting (5, 2, -3) u/ms in
//                                    ship space; when the camera passes gate z - 10000 (~1.33 s) the gate swaps its idle
//                                    _anim_add for the one-shot _jump_anim_add (5000 ms, PlayerJumpgate::activate) and sound
//                                    31 plays; 1000 ms into it the ship vanishes (speed 90); at its end the level reloads in
//                                    the target station's orbit (departStation, setInitStreamOut, jumpgateUsed).
//   MGame::UseKhadorDrive 0x1a9480   the star map in jump mode (from the autopilot menu entry here)
//   MGame::OnUpdate 0x1ac778 / startChargingJumpDrive 0x1a9710 / PlayerEgo::startJumpDrive   doInstantJump and level time
//                                    > 5000 ms: the energy cells are removed ("-N t Energy Cells", 579 if short), sound 33, a
//                                    5000 ms charge while flying normally; then the khador_jump fx (15026, scale 2) 3000
//                                    units ahead, camera at fx + R(-2000, 300, -2000) drifting (5, 2, -5), sound 32, the ship
//                                    vanishes at 1700 ms, the level reloads at the fx end (~4000 ms).
// The programmed station is cleared on arrival: no autopilot leg follows a system jump. Remake (jumpgate route planning): a
// destination past the gate neighbours is reached gate by gate (GalaxyMap.NextHop): a hop keeps it, the autopilot flies on
// to the next gate, and that gate jumps on without asking (Session.RouteDestination).
// Mission blocks: 525 at the gate (GateBlocked) and on the Khador menu entry (Navigation); volatile goods: 612 on the menu
// entry and on the map (StarMap).

using System;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.UI;
using GoF2Remake.Visuals;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GoF2Remake.World
{
    public class SystemJump : MonoBehaviour
    {
        const float M = 0.05f;
        const float BaseSpeed = 2f, HiddenSpeed = 90f;   // u/ms
        // Remake: a jump's rumble comes with its big flash, not as its animation starts: a gate's _jump_anim_add flash part
        // shoots out 63x its length at 1300 ms (the same keys on all four gates; the ship is gone at 1000), the khador_jump
        // fx's flash (part 21, ~38000 units, then part 20) shows at 1733 (the ship is gone at 1700).
        const float GateFlashMs = 1300f, KhadorFlashMs = 1733f;
        /// <summary>Remake: the Khador fx's whiteout, where the flight into interstellar space cuts (the rest of the fx would show
        /// the orbit left; the new level fades out of the same light, Visuals.WarpSky).</summary>
        public const float KhadorWhiteoutStartMs = 1500f, KhadorWhiteoutMs = 1860f;
        /// <summary>Remake: how long the camera glides in behind the ship into interstellar space; the longest the hole may take
        /// to fill the view, the fallback for a view it can't fill; a hull the animation fits without growing.</summary>
        const float GlideMs = 1250f, RiftLatestMs = 6000f, FxMeters = 70f;

        public string spaceScene = "Space";

        enum State { None, AtGate, Charging, GateScene, KhadorScene }

        Database db;
        Navigation nav;
        ShipController ship;
        WeaponSystem weapons;
        ChaseCamera chase;
        GameObject gate;
        StarMapAssets assets;
        AudioSource sfx;
        State state;
        float chargeMs, animMs, animLength, speed;
        bool activated, hidden, flashFelt;
        int targetStation;
        /// <summary>A Khador jump without the map (the Void in or out, Story.ForcedKhadorTarget): charging, then the jump, to
        /// this station (-1 = the Void), for jumpCells energy cells; a story-forced one is never refused for missing cells.</summary>
        int? storyTarget;
        bool advanceOnJump;   // index 78's nextCampaignMission, done as the Khador scene starts (#49)
        int jumpCells;
        bool storyForced;

        /// <summary>startChargingJumpDrive 0x1a9710: 1 cell out of the Void, 2 into it; x2 on Extreme. Only checked: the jump
        /// into the Void takes one share (Ship::removeCargo with iVar6), not the two it asks for.</summary>
        static int VoidCells(bool intoVoid) => (Session.IsExtreme ? 2 : 1) * (intoVoid ? 2 : 1);
        static int VoidCellsTaken => Session.IsExtreme ? 2 : 1;
        GameObject fx;
        KhadorFx rift;
        SpaceDust orbitFog;
        Vector3 heldView;
        Quaternion heldTurn = Quaternion.identity;
        float riftCrossMs;
        bool swapping;
        /// <summary>Remake: the Khador scene's frame, the ship's own rotation when the jump starts (its roll and pitch too): the
        /// fx's turn and the camera's place. The camera's drift and up follow the ship as it is turned (as the original's
        /// ship-space drift). The camera used the ship's frame for its place but the world's up to look, so a ship flying
        /// inverted (or rolled) was filmed from the wrong side.</summary>
        Quaternion sceneFrame = Quaternion.identity;
        /// <summary>Remake: the Khador scene's path (the ship's heading when it starts): the ship moves along it while the player
        /// turns it (ShipController.turnOnly).</summary>
        Vector3 sceneDir = Vector3.forward;

        /// <summary>The HUD is hidden: docked to the gate or a jump scene running.</summary>
        public bool Cinematic => state == State.AtGate || state == State.GateScene || state == State.KhadorScene || swapping;
        public bool Charging => state == State.Charging;
        /// <summary>Remake: the ship is on its way through a jumpgate or the Khador Drive (the jump scenes; ThrottleGlow).</summary>
        public bool Traveling => state == State.GateScene || state == State.KhadorScene;
        /// <summary>PlayerEgo::getDriveChargeRate: 0..1 while charging.</summary>
        public float ChargeRate => Mathf.Clamp01(chargeMs / GalaxyMap.ChargeMs);
        public event Action<string> Message;
        /// <summary>The jump's effect starts (multiplayer: NetPlayer shows it to the others): true = the gate's jump
        /// animation, false = the khador_jump fx at this position and rotation.</summary>
        public event Action<bool, Vector3, Quaternion> JumpFxStarted;
        /// <summary>The Khador Drive starts charging (multiplayer: the others hear it).</summary>
        public event Action ChargeStarted;

        public void Setup(Database database, Navigation navigation, ShipController controller, WeaponSystem weaponSystem,
                          ChaseCamera chaseCamera, GameObject jumpgate)
        {
            db = database;
            nav = navigation;
            ship = controller;
            health = controller != null ? controller.GetComponent<PlayerHealth>() : null;
            weapons = weaponSystem;
            chase = chaseCamera;
            gate = jumpgate;
            assets = StarMapAssets.Load();
            sfx = gameObject.AddComponent<AudioSource>();
            sfx.playOnAwake = false;
            nav.KhadorRequested += OpenKhadorMap;
            nav.InterstellarRequested += RequestInterstellar;
            nav.SystemReached += LeaveInterstellar;
            nav.TravelBusy = () => state != State.None || toInterstellar;
            levelStart = Time.time;
        }

        /// <summary>The game clock at this level's start. Not timeSinceLevelLoad: that still reads the last level's time while a
        /// level sets up (a Khador charge then waited out however long that level had run) and doesn't restart on a swap.</summary>
        float levelStart;
        PlayerHealth health;

        /// <summary>Set by the level: the gate refuses the jump (the Kaamo siege).</summary>
        public System.Func<bool> GateBlocked;

        void OnDestroy()
        {
            if (nav == null) return;
            nav.KhadorRequested -= OpenKhadorMap;
            nav.InterstellarRequested -= RequestInterstellar;
            nav.SystemReached -= LeaveInterstellar;
            nav.TravelBusy = null;
        }

        void Update()
        {
            if (ship == null) return;
            float dtMs = Time.deltaTime * 1000f;
            // Remake (#37): a destroyed ship neither takes the gate nor finishes a Khador charge (shot down on its way into
            // the gate it still asked for the destination and jumped, coming out repaired); the scenes themselves are
            // invulnerable.
            if (health == null) health = ship.GetComponent<PlayerHealth>();
            if (health != null && health.Dead)
            {
                if (state == State.Charging) { state = State.None; EndChargeShow(); }
                toInterstellar = false;
                if (state == State.None) return;
            }
            switch (state)
            {
                case State.None:
                    if (nav.ReachedGate)
                    {
                        if (GateBlocked != null && GateBlocked()) nav.Refuse();   // MGame::dockEvent: 525 on a mission
                        else DockToStream();
                    }
                    else if ((Session.InstantJump || storyTarget.HasValue || toInterstellar) && (Time.time - levelStart) * 1000f > 5000f && !nav.Jumping && !nav.Paused && !nav.Conversation && !GoF2Remake.World.SpaceHooks.StarMapOpen)
                        StartCharging();
                    break;
                case State.AtGate:
                    // dockToStream: input locked, the ship keeps moving at its speed (the game is paused while asking).
                    ship.transform.position += ship.transform.forward * speed * dtMs * M;
                    break;
                case State.Charging:
                    chargeMs += dtMs;
                    Haptics.Rumble(0.1f + 0.35f * ChargeRate);   // remake: the drive winding up
                    UpdateChargeShow();
                    if (chargeMs >= GalaxyMap.ChargeMs) StartKhadorScene();
                    break;
                case State.GateScene:
                case State.KhadorScene:
                    UpdateScene(dtMs);
                    break;
            }
        }

        void LateUpdate()
        {
            if (state != State.GateScene && state != State.KhadorScene) return;
            var cam = Camera.main;
            // Remake: the Khador scene's camera keeps the ship's up, so it rolls with the ship (turned by the player too).
            var up = state == State.KhadorScene ? ship.transform.up : Vector3.up;
            if (rift != null)
            {
                // The view the jump began with, held on the ship: the chase camera's lag let the ship slide back and forth on
                // screen as it eased off and was drawn in. So the ship stays put and the hole comes to it.
                if (cam != null) cam.transform.SetPositionAndRotation(ship.transform.TransformPoint(heldView), ship.transform.rotation * heldTurn);
                return;
            }
            if (cam != null) cam.transform.rotation = Quaternion.LookRotation(ship.transform.position - cam.transform.position, up);
            if (cam != null && toInterstellar && state == State.KhadorScene) GlideBehindShip(cam);
        }

        /// <summary>Remake: into interstellar space the Khador scene's camera glides from its side view to the chase camera's own
        /// place behind the ship, which stays in view, flying into the jump: the whiteout then hides a load that changes nothing
        /// about the view, as the new level opens on the same chase camera.</summary>
        void GlideBehindShip(Camera cam)
        {
            if (chase == null) return;
            float dtMs = Time.deltaTime * 1000f;
            sceneCam += ship.transform.rotation * (new Vector3(-5f, 2f, -5f) * dtMs * M);
            float t = Mathf.SmoothStep(0f, 1f, animMs / GlideMs);
            chase.SteadyPose(sceneDir * (speed * M), dtMs, out var position, out var rotation);
            cam.transform.SetPositionAndRotation(Vector3.Lerp(sceneCam, position, t), Quaternion.Slerp(cam.transform.rotation, rotation, t));
            cam.fieldOfView = Mathf.Lerp(sceneFov, Aspect.VerticalFov(chase.baseFov, cam.aspect), t);
        }

        Vector3 sceneCam;
        float sceneFov;

        // ---- the charge's show (remake; the original shows only the HUD's bar) --------------------------------------------

        EmpSparks hullArcs;
        KhadorFx.Gathering gathering;

        /// <summary>The drive winding up: lightning crawling over the hull, from the EMP's bolts on the fx's own sheet, and at the
        /// end the focus gathering where the beam will strike.</summary>
        void UpdateChargeShow()
        {
            if (!toInterstellar) return;   // a plain jump's charge shows only the HUD's bar, as the original's
            float rate = ChargeRate;
            if (KhadorTimeline.ChargeArcs(rate) > 0f)
            {
                hullArcs ??= new EmpSparks(ship.transform);
                hullArcs.SetEmitting(true);
            }
            if (KhadorTimeline.ChargeFocus(rate) > 0f)
            {
                if (gathering == null) gathering = KhadorFx.Gathering.Create(ship.transform, Vector3.forward * KhadorTimeline.RiftFocusUnits * M * FxScale(), Sheet());
                if (gathering != null) gathering.SetCharge(rate);
            }
        }

        void EndChargeShow()
        {
            hullArcs?.SetEmitting(false);
            if (gathering != null) Destroy(gathering.gameObject);
            gathering = null;
        }

        static Texture Sheet() => SheetOf(StarMapAssets.Load());

        static Texture SheetOf(StarMapAssets a)
        {
            var look = a != null && a.khadorJump != null ? a.khadorJump.GetComponentInChildren<Renderer>(true)?.sharedMaterial : null;
            return look != null && look.HasProperty("_MainTex") ? look.GetTexture("_MainTex") : null;
        }

        /// <summary>The ship's half span in game units, its model's largest half extent.</summary>
        float HalfSpanUnits()
        {
            var b = new Bounds(ship.transform.position, Vector3.zero);
            if (ship.visualModel != null)
                foreach (var r in ship.visualModel.GetComponentsInChildren<MeshRenderer>())
                    if (!r.name.Contains("_add")) b.Encapsulate(r.bounds);
            float half = Mathf.Max(b.extents.x, Mathf.Max(b.extents.y, b.extents.z)) / M;
            return half > 1f ? half : 700f;
        }

        /// <summary>The hole suits a ship up to FxMeters long; a bigger hull gets a bigger one, its focus clear of the bow.</summary>
        float FxScale() => Mathf.Max(1f, HalfSpanUnits() * 2f * M / FxMeters);

        // ---- the jumpgate ----------------------------------------------------------------------------------

        void DockToStream()
        {
            state = State.AtGate;
            speed = ship.SpeedMetersPerSecond / M / 1000f;
            nav.SetAutopilot(null);
            ship.externalControl = true;
            ship.ExternalSpeedMetersPerSecond = ship.SpeedMetersPerSecond;
            if (weapons != null) weapons.Blocked = true;
            nav.Paused = true;
            int prog = Session.ProgrammedStation;
            // Remake (jumpgate route planning): on a route under way (a hop kept the destination) the gate jumps on toward it
            // without asking: the route was confirmed on the map and at its first gate.
            if (prog >= 0 && prog == Session.RouteDestination && prog != Session.StationIndex)
            {
                OnGateMapClosed(new StarMapResult { station = prog });
                return;
            }
            bool opened = SpaceHooks.OpenStarMap != null && SpaceHooks.OpenStarMap(db, StarMapMode.Gate, false, OnGateMapClosed, prog >= 0 && prog != Session.StationIndex ? prog : -1, false);
            if (!opened) OnGateMapClosed(new StarMapResult { station = -1 });
        }

        void OnGateMapClosed(StarMapResult result)
        {
            nav.Paused = false;
            if (result.station >= 0)
            {
                Session.ProgrammedStation = result.station;
                int hop = GalaxyMap.NextHop(db, Session.StationIndex, result.station);
                hopping = hop != result.station;
                StartGateScene(hop);
                return;
            }
            // dockToStream(false): controls back, the ship past the gate with the gate's direction.
            state = State.None;
            ship.externalControl = false;
            if (weapons != null) weapons.Blocked = false;
            if (gate != null)
                ship.transform.SetPositionAndRotation(gate.transform.position + OrbitLayout.DirToUnity(new Vector3(0f, 0f, 8000f)) * M,
                                                      Quaternion.LookRotation(OrbitLayout.DirToUnity(Vector3.forward), Vector3.up));
        }

        /// <summary>Set while the gate takes the ship one system along the route to the programmed station (Arrive keeps it).
        /// Where the gate jumps: GalaxyMap.NextHop (the station, or the next system's gate orbit on the gate route).</summary>
        bool hopping;

        void StartGateScene(int station)
        {
            BeginScene(station);
            state = State.GateScene;
            var g = gate != null ? gate.transform.position : ship.transform.position;
            ship.transform.SetPositionAndRotation(g + OrbitLayout.DirToUnity(new Vector3(0f, 0f, -10000f)) * M,
                                                  Quaternion.LookRotation(OrbitLayout.DirToUnity(Vector3.forward), Vector3.up));
            var cam = Camera.main;
            if (cam != null) cam.transform.position = g + OrbitLayout.DirToUnity(new Vector3(-2000f, 300f, -6000f)) * M;
        }

        /// <summary>PlayerJumpgate::activate: the idle glow is replaced by the one-shot jump animation.</summary>
        void ActivateGate()
        {
            activated = true;
            animMs = 0f;
            float len = PlayGateJump(gate);
            animLength = len > 0f ? len : 5000f;
            if (assets != null && assets.jumpgate != null && assets.jumpgate.Length > 0)
                Play(assets.jumpgate[UnityEngine.Random.Range(0, assets.jumpgate.Length)]);
            JumpFxStarted?.Invoke(true, gate != null ? gate.transform.position : ship.transform.position, Quaternion.identity);
            flashFelt = false;   // the rumble waits for the flash (GateFlashMs)
        }

        /// <summary>PlayerJumpgate::activate on a gate: its _anim_add layers off, the _jump_anim_add played once (the flash's
        /// `extra` fades); returns the animation's length in ms (0 = none).</summary>
        public static float PlayGateJump(GameObject gate)
        {
            if (gate == null) return 0f;
            GameObject jump = null;
            foreach (var t in gate.GetComponentsInChildren<Transform>(true))
            {
                if (t.name.EndsWith("_jump_anim_add")) jump = t.gameObject;
                else if (t.name.EndsWith("_anim_add")) t.gameObject.SetActive(false);
            }
            if (jump == null) return 0f;
            // Remake (#167): the gate's made levels of detail (AutoLods) keep the idle layers but not the jump layer, so a
            // gate shown at level 1 (a phone's lower LOD bias: from 0.4 s into the flash at Medium detail) lost the flash
            // with the idle glow already off: the jump played unseen. Level 0 while it jumps; ResetGate lets it go.
            foreach (var lg in gate.GetComponentsInChildren<LODGroup>(true)) lg.ForceLOD(0);
            jump.SetActive(true);
            GunRig.EnableFades(jump);
            return PartAnimation.PlayOnce(jump);
        }

        /// <summary>Back to the gate's idle look after PlayGateJump (multiplayer: another player went through it).</summary>
        public static void ResetGate(GameObject gate)
        {
            if (gate == null) return;
            foreach (var t in gate.GetComponentsInChildren<Transform>(true))
            {
                if (t.name.EndsWith("_jump_anim_add")) t.gameObject.SetActive(false);
                else if (t.name.EndsWith("_anim_add")) t.gameObject.SetActive(true);
            }
            foreach (var lg in gate.GetComponentsInChildren<LODGroup>(true)) lg.ForceLOD(-1);
        }

        /// <summary>The khador_jump fx (15026, scale 2) at 'position', played once; 'lengthMs' = its length (4000 without).</summary>
        public static GameObject SpawnKhadorFx(StarMapAssets assets, Vector3 position, Quaternion rotation, out float lengthMs)
        {
            lengthMs = 4000f;
            if (assets == null || assets.khadorJump == null) return null;
            var go = Instantiate(assets.khadorJump, position, rotation);
            go.transform.localScale *= 2f;
            GunRig.EnableFades(go);   // the parts' `extra` fade-out
            float len = PartAnimation.PlayOnce(go);
            if (len > 0f) lengthMs = len;
            return go;
        }

        // ---- the Khador Drive ------------------------------------------------------------------------------

        /// <summary>MGame::UseKhadorDrive: the star map in jump mode (refused while charging or jumping).</summary>
        void OpenKhadorMap()
        {
            if (state != State.None || nav.Jumping || storyTarget.HasValue) return;
            var forced = Story.ForcedKhadorTarget(Session.StationIndex);
            bool inVoid = Session.StationIndex == Session.VoidOrbit;
            if (forced.HasValue || inVoid)
            {
                // MGame::UseKhadorDrive: in the Void straight back to Status+0x84 (100 at index 80); index 78: programmedStation =
                // the Void, startChargingJumpDrive, nextCampaignMission (-> 79). No map. Remake (#49): the step advances when
                // the jump itself starts (StartKhadorScene): the remake charges only once the level is 5 s old and nothing is
                // paused, and meanwhile 79 let the autopilot dock at the Valkyrie (no level mission there any more), stranding
                // the player at 79 outside the Void.
                advanceOnJump = Story.Index == 78 && !inVoid;
                storyTarget = forced ?? Session.VoidReturnStation;
                jumpCells = VoidCells(!inVoid);
                storyForced = forced.HasValue;
                return;
            }
            nav.Paused = true;
            if (weapons != null) weapons.Blocked = true;
            // askForJumpIntoAlienWorld outside the Void: the original asks whenever the Khador map opens (MGame::UseKhadorDrive
            // 0x1a9480 when !Status::inAlienOrbit; Status+0x78 is the Void's default station, index -1). The story gates the
            // drive itself (its blueprint unlocks at index 34, after the wormhole ride at 24), so the prompt needs no gate; a
            // drive from the Debug panel reaches the Void early. Free play and multiplayer sessions too (Void Crystals), but
            // only with a real drive: free play's gateless-system stand-in (GalaxyMap.HasJumpDrive) doesn't reach the Void.
            // Remake: the map opens at once and its Jump to the Void button asks 422 (StarMap.AskVoid).
            bool askVoid = GalaxyMap.HasIntegratedDrive(Session.ShipIndex) || Session.Equipment.Exists(e => e.item == GalaxyMap.KhadorDriveItem);
            bool opened = SpaceHooks.OpenStarMap != null && SpaceHooks.OpenStarMap(db, StarMapMode.Khador, true, r =>
            {
                nav.Paused = false;
                if (weapons != null) weapons.Blocked = false;
                if (r.toVoid) { storyTarget = Session.VoidOrbit; jumpCells = VoidCells(true); storyForced = false; return; }
                if (r.station < 0) return;
                Session.ProgrammedStation = r.station;
                if (r.instantJump) { Session.InstantJump = true; Session.EnergyCellsForNextJump = r.cells; }
                else nav.ContinueToProgrammedStation();
            }, -1, askVoid);
            if (!opened) { nav.Paused = false; if (weapons != null) weapons.Blocked = false; }
        }

        // ---- interstellar space (InterstellarSpace) ------------------------------------------------------------

        /// <summary>The Khador sequence goes to interstellar space, not to a station: the drive pays its legs there.</summary>
        bool toInterstellar;
        /// <summary>The Khador charge or scene into interstellar space runs (its whiteout, Visuals.WarpSky; the warp itself comes
        /// only after it: the orbit stays plain through the animation).</summary>
        public bool GoingInterstellar => toInterstellar && (state == State.Charging || state == State.KhadorScene);
        /// <summary>Ms into the Khador fx (0 before it).</summary>
        public float SceneMs => state == State.KhadorScene ? animMs : 0f;

        /// <summary>The mounted drive's quick menu entry: the Khador charge and scene, once at least a leg's cells are aboard.</summary>
        void RequestInterstellar()
        {
            if (state != State.None || nav.Jumping || storyTarget.HasValue) return;
            toInterstellar = true;
        }

        /// <summary>A system's edge reached: straight into its orbit (Arrive), no second Khador scene: the warp and the ship's speed
        /// carry over and ease out there (Session.WarpArrival). The programmed destination survives when it is in that system:
        /// the autopilot flies on to its planet, as after a gate hop; after a planet run (Session.WarpLocal) wherever it is. An
        /// orbit a script sets up on arrival (Navigation.ScriptedArrival) is loaded as after a jump instead, with its arrival
        /// camera: its opening is written for that.</summary>
        void LeaveInterstellar(int station)
        {
            if (state != State.None) return;
            int prog = Session.ProgrammedStation;
            // A planet run (Session.WarpLocal) keeps any other destination, as the planet jump does: the autopilot flies on.
            hopping = prog >= 0 && prog != station
                      && (Session.WarpLocal || db.Stations.Find(s => s.index == prog)?.system == db.Stations.Find(s => s.index == station)?.system);
            targetStation = station;
            Session.WarpArrival = !Navigation.ScriptedArrival(station);
            if (Session.WarpArrival) FindAnyObjectByType<GoF2Remake.Visuals.WarpSky>()?.CarrySound();
            Arrive(false);
        }

        /// <summary>The Khador scene is flying into its hole in space, no whiteout to cover it (Visuals.WarpSky).</summary>
        public bool RiftOpen => rift != null;
        /// <summary>The warp starting over this orbit as the ship is drawn into the hole, 0..1.</summary>
        public float RiftWarp => rift != null ? KhadorTimeline.RiftWarp(animMs, riftCrossMs) : 0f;

        void StartCharging()
        {
            if (toInterstellar)
            {
                int leg = nav.Drive != null ? nav.Drive.cellsPerLeg : 1;
                if (!Cheats.FreeJumps && GalaxyMap.CellsInCargo() < leg)
                {
                    Message?.Invoke(Localization.Get(579));
                    toInterstellar = false;
                    return;
                }
                Play(assets != null ? assets.jumpgateCharge : null);
                chargeMs = 0f;
                state = State.Charging;
                ChargeStarted?.Invoke();
                return;
            }
            Session.InstantJump = false;
            // Remake: a Khador jump straight into the campaign target's orbit takes the planet jump's equipment checks (24 a
            // scanner and a tractor beam, the Supernova's 3213-3218); the original only gates the planet jump
            // (MGame::OnTouchBegin), so the drive could arrive without them and the step couldn't be done.
            if (!storyTarget.HasValue && nav.PlanetJumpRefused != null && nav.PlanetJumpRefused(Session.ProgrammedStation))
            {
                Session.ProgrammedStation = -1;
                return;
            }
            int cells = storyTarget.HasValue ? jumpCells : Session.EnergyCellsForNextJump;
            // Into the Void it asks for two shares but takes one (startChargingJumpDrive 0x1a9710).
            int taken = storyTarget == Session.VoidOrbit ? VoidCellsTaken : cells;
            if (Cheats.FreeJumps) cells = taken = 0;   // remake: the Debug panel's free jumps
            if (GalaxyMap.CellsInCargo() < cells)
            {
                // Remake: the story's own jumps (78 into the Void, 80 out of it) take what there is instead of stranding the player.
                if (storyForced) cells = taken = Mathf.Min(taken, GalaxyMap.CellsInCargo());
                else
                {
                    // 0x243 / 0x244: the original shows 580 ("two cells, only one left") whenever it isn't Extreme and some
                    // cells are aboard; the remake only where that is what happened.
                    Message?.Invoke(Localization.Get(cells == 2 && GalaxyMap.CellsInCargo() == 1 ? 580 : 579));
                    storyTarget = null;
                    return;
                }
            }
            cells = taken;
            GalaxyMap.RemoveCells(cells);
            if (cells > 0) Message?.Invoke($"-{cells}t {Localization.Get(1396)}");
            Play(assets != null ? assets.jumpgateCharge : null);
            chargeMs = 0f;
            state = State.Charging;
            ChargeStarted?.Invoke();
        }

        void StartKhadorScene()
        {
            int station = toInterstellar ? Session.StationIndex : storyTarget ?? Session.ProgrammedStation;
            if (station < 0 && !storyTarget.HasValue && !toInterstellar) { state = State.None; return; }   // (out of the Void: -1)
            BeginScene(station);
            state = State.KhadorScene;
            if (advanceOnJump) { advanceOnJump = false; if (Story.Index == 78) Story.Advance(db); }
            StartKhadorFx();
        }

        /// <summary>Modified: the Khador Drive's jump scene alone, no charge, cells or arrival (the Supernova Challenge's end:
        /// the ship jumps out as the round's clock runs out); 'done' when the fx has played, the ship gone and the camera
        /// where the scene left it.</summary>
        public void PlayKhadorScene(Action done)
        {
            if (state != State.None) { done?.Invoke(); return; }
            sceneDone = done;
            BeginScene(-1);
            state = State.KhadorScene;
            StartKhadorFx();
        }

        Action sceneDone;

        void StartKhadorFx()
        {
            EndChargeShow();
            var fxPos = ship.transform.position + ship.transform.forward * KhadorTimeline.FocusUnits * M;
            // MGame::startJumpScene: the fx's setDirection(ship direction, (0, 1, 0)), upright in the world. Remake: in the
            // ship's own frame (sceneFrame), as the camera, so the scene is the same from a ship flying inverted or rolled.
            sceneFrame = ship.transform.rotation;
            sceneDir = ship.transform.forward;
            var fxRot = sceneFrame;
            rift = toInterstellar && !GoF2Remake.Data.VrFlags.Enabled ? OpenRift(ref fxPos, fxRot) : null;
            if (rift == null) fx = SpawnKhadorFx(assets, fxPos, fxRot, out animLength);
            JumpFxStarted?.Invoke(false, fxPos, fxRot);
            activated = true;
            animMs = 0f;
            ship.turnOnly = rift == null;   // remake: the player turns the ship (and the camera's drift with it), not its path
            var cam = Camera.main;
            // game (-2000, 300, -2000), in the ship's frame. Into the hole the camera stays where it is behind the ship.
            if (rift != null && cam != null)
            {
                if (chase != null && !chase.firstPerson)
                {
                    heldView = ship.transform.InverseTransformPoint(cam.transform.position);
                    heldTurn = Quaternion.Inverse(ship.transform.rotation) * cam.transform.rotation;
                }
                else if (chase != null)   // from the cockpit: the view the next level's chase camera opens on
                {
                    chase.SteadyPose(sceneDir * (speed * M), 16f, out var position, out var rotation);
                    heldView = ship.transform.InverseTransformPoint(position);
                    heldTurn = Quaternion.Inverse(ship.transform.rotation) * rotation;
                }
            }
            if (chase != null) chase.enabled = false;
            if (rift == null)
            {
                if (cam != null) cam.fieldOfView = Aspect.VerticalFov(1.22f * Mathf.Rad2Deg, cam.aspect);
            }
            if (cam != null && rift == null)
            {
                cam.transform.position = fxPos + sceneFrame * (new Vector3(2000f, 300f, -2000f) * M);
                sceneCam = cam.transform.position;
                sceneFov = cam.fieldOfView;
            }
            if (rift != null) PlayCarried(assets != null ? assets.khadorDrive : null);
            else Play(assets != null ? assets.khadorDrive : null);
            flashFelt = false;   // the rumble waits for the flash (KhadorFlashMs)
        }

        /// <summary>The Khador Slipstream's hole, further ahead than a jump's fx ('at' moved there); null should it be unable to
        /// open, and that jump goes the old way. Every other Khador jump keeps the original's fx (SpawnKhadorFx), the user's call.</summary>
        KhadorFx OpenRift(ref Vector3 at, Quaternion facing)
        {
            float fxScale = FxScale(), reach = KhadorTimeline.RiftFocusUnits * fxScale;
            var place = ship.transform.position + ship.transform.forward * reach * M;
            var drawn = KhadorFx.Create(place, facing, SheetOf(assets));
            if (drawn == null) return null;
            drawn.transform.localScale *= fxScale;
            riftCrossMs = KhadorTimeline.RiftCrossMs(reach);
            drawn.OpenRift(ship.transform, InterstellarSpace.StartRotation(db, FindAnyObjectByType<SpaceLevel>().Layout), riftCrossMs, HalfSpanUnits());
            if (!drawn.HasRift) { Destroy(drawn.gameObject); return null; }
            fx = drawn.gameObject;
            animLength = KhadorTimeline.LengthMs;
            at = place;
            return drawn;
        }

        // ---- the jump scene --------------------------------------------------------------------------------

        void BeginScene(int station)
        {
            targetStation = station;
            activated = hidden = false;
            speed = BaseSpeed;
            nav.SetAutopilot(null);
            ship.externalControl = true;
            if (weapons != null) weapons.Blocked = true;
            if (toInterstellar) return;   // StartKhadorFx settles the camera
            if (chase != null) chase.enabled = false;
            var cam = Camera.main;
            if (cam != null) cam.fieldOfView = Aspect.VerticalFov(1.22f * Mathf.Rad2Deg, cam.aspect);
        }

        void UpdateScene(float dtMs)
        {
            bool gateScene = state == State.GateScene;
            var cam = Camera.main;
            if (!hidden && cam != null && rift == null)   // ship-space game (5, 2, -3 / -5) u/ms -> Unity local (-5, 2, -3 / -5)
                cam.transform.position += ship.transform.rotation * (new Vector3(-5f, 2f, gateScene ? -3f : -5f) * dtMs * M);
            if (rift != null)
            {
                speed = KhadorTimeline.RiftSpeed(animMs);   // drawn into the hole
                // The orbit's fog lies in front of the hole, between it and the camera, and the next level has none.
                if (orbitFog == null) orbitFog = FindAnyObjectByType<SpaceLevel>()?.Fog;
                if (orbitFog != null) orbitFog.Fade(1f - Mathf.InverseLerp(KhadorTimeline.RingStartMs / KhadorTimeline.RiftRate, riftCrossMs, animMs));
            }
            float step = speed * dtMs * M;
            // Remake: the Khador scene keeps its path while the player turns the ship (turnOnly).
            ship.transform.position += (gateScene ? ship.transform.forward : sceneDir) * step;
            ship.ExternalSpeedMetersPerSecond = speed * M * 1000f;

            if (gateScene && !activated && gate != null && cam != null
                && -cam.transform.position.z / M < -gate.transform.position.z / M - 10000f)   // camera z (game) < gate z - 10000
                ActivateGate();
            if (!activated) return;
            animMs += dtMs;
            // (at the latest at the end: a shorter mod animation)
            if (!flashFelt && animMs >= (rift != null ? riftCrossMs : Mathf.Min(gateScene ? GateFlashMs : KhadorFlashMs, animLength))) { flashFelt = true; Haptics.Play(Haptics.Jump); }   // remake
            if (toInterstellar && (rift != null ? rift.RiftFilled(cam) || animMs >= RiftLatestMs : animMs >= KhadorWhiteoutMs)) { Arrive(false); return; }
            if (!hidden && !toInterstellar && animMs > (gateScene ? 1000f : 1700f))
            {
                hidden = true;
                speed = HiddenSpeed;
                ship.turnOnly = false;   // remake: the roll stops with the ship gone
                if (ship.visualModel != null) ship.visualModel.gameObject.SetActive(false);
            }
            if (animMs >= animLength && rift == null)   // into a hole the scene ends when the hole has filled the view
            {
                if (sceneDone != null)   // Modified: a scene without a jump (PlayKhadorScene)
                {
                    var d = sceneDone;
                    sceneDone = null;
                    state = State.None;
                    ship.turnOnly = false;
                    d();
                    return;
                }
                Arrive(gateScene);
            }
        }

        /// <summary>Status::departStation(programmedStation), Level::setInitStreamOut, reload in the target orbit.</summary>
        void Arrive(bool viaGate)
        {
            weapons?.StoreAmmo();
            if (toInterstellar)
            {
                // The station stays the one the ship left: a save or a load in there is this orbit.
                toInterstellar = false;
                Session.EnterInterstellar = true;
                Session.RiftEntry = swapping = rift != null;   // no HUD for the frame left of this level
                if (rift != null && chase != null && Camera.main != null)   // the view the hole showed
                {
                    Session.WarpView = ship.transform.InverseTransformPoint(Camera.main.transform.position);
                    Session.WarpViewTurn = Quaternion.Inverse(ship.transform.rotation) * Camera.main.transform.rotation;
                }
                Session.ArrivedByTravel = Session.ArrivedBySystemJump = Session.LaunchedFromStation = false;
                Session.InstantJump = false;
                state = State.None;
                ship.turnOnly = false;
                enabled = false;
                if (Application.CanStreamedLevelBeLoaded(spaceScene)) SpaceLevel.SwapScene(spaceScene);
                return;
            }
            // (from between the systems: the orbit left, unless that was the Void itself; the return stays the last one)
            if (targetStation == Session.VoidOrbit && Session.StationIndex != Session.VoidOrbit) Session.VoidReturnStation = Session.StationIndex;
            Session.PreviousStationIndex = Session.StationIndex;
            Session.StationIndex = targetStation;
            Session.ArrivedByTravel = true;
            Session.ArrivedBySystemJump = !Session.WarpLocal;
            Session.LaunchedFromStation = false;
            if (viaGate) Session.JumpgatesUsed++;
            if (!hopping) Session.ProgrammedStation = -1;   // remake: a hop keeps the destination, the autopilot flies on
            Session.RouteDestination = viaGate && hopping ? Session.ProgrammedStation : -1;   // and the next gate jumps on unasked
            hopping = false;
            Session.InstantJump = false;
            state = State.None;
            ship.turnOnly = false;
            enabled = false;
            if (Application.CanStreamedLevelBeLoaded(spaceScene))
            {
                if (Session.WarpArrival) SpaceLevel.SwapScene(spaceScene);
                else SceneManager.LoadScene(spaceScene);
            }
        }

        /// <summary>The jump's sound into the hole, played on over the level's end, as the swap comes before it has: at the
        /// scene's rate, deeper, its blast (KhadorFlashMs into the clip) as the ship reaches the hole.</summary>
        void PlayCarried(AudioClip clip)
        {
            if (clip == null) return;
            clip = GoF2Remake.Modding.ModSounds.Get(clip);
            var go = new GameObject("Khador sound");
            DontDestroyOnLoad(go);
            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.clip = clip;
            source.volume = Settings.SfxVolume;
            source.pitch = KhadorTimeline.RiftRate;
            float delayS = Mathf.Max(0f, riftCrossMs - KhadorFlashMs / source.pitch) / 1000f;
            source.PlayDelayed(delayS);
            Destroy(go, delayS + clip.length / source.pitch + 0.2f);
        }

        void Play(AudioClip clip)
        {
            if (clip != null) sfx.PlayOneShot(GoF2Remake.Modding.ModSounds.Get(clip), Settings.SfxVolume);
        }
    }
}
