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
// The programmed station is cleared on arrival: no autopilot leg follows a system jump.
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
        bool activated, hidden;
        int targetStation;
        /// <summary>A Khador jump without the map (the Void in or out, Story.ForcedKhadorTarget): charging, then the jump, to
        /// this station (-1 = the Void), for jumpCells energy cells; a story-forced one is never refused for missing cells.</summary>
        int? storyTarget;
        int jumpCells;
        bool storyForced;

        /// <summary>startChargingJumpDrive 0x1a9710: 1 cell out of the Void, 2 into it; x2 on Extreme. Only checked: the jump
        /// into the Void takes one share (Ship::removeCargo with iVar6), not the two it asks for.</summary>
        static int VoidCells(bool intoVoid) => (Session.IsExtreme ? 2 : 1) * (intoVoid ? 2 : 1);
        static int VoidCellsTaken => Session.IsExtreme ? 2 : 1;
        GameObject fx;

        /// <summary>The HUD is hidden: docked to the gate or a jump scene running.</summary>
        public bool Cinematic => state == State.AtGate || state == State.GateScene || state == State.KhadorScene;
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
            weapons = weaponSystem;
            chase = chaseCamera;
            gate = jumpgate;
            assets = StarMapAssets.Load();
            sfx = gameObject.AddComponent<AudioSource>();
            sfx.playOnAwake = false;
            nav.KhadorRequested += OpenKhadorMap;
        }

        /// <summary>Set by the level: the gate refuses the jump (the Kaamo siege).</summary>
        public System.Func<bool> GateBlocked;

        void OnDestroy()
        {
            if (nav != null) nav.KhadorRequested -= OpenKhadorMap;
        }

        void Update()
        {
            if (ship == null) return;
            float dtMs = Time.deltaTime * 1000f;
            switch (state)
            {
                case State.None:
                    if (nav.ReachedGate)
                    {
                        if (GateBlocked != null && GateBlocked()) nav.Refuse();   // MGame::dockEvent: 525 on a mission
                        else DockToStream();
                    }
                    else if ((Session.InstantJump || storyTarget.HasValue) && Time.timeSinceLevelLoad * 1000f > 5000f && !nav.Jumping && !nav.Paused && !StarMap.IsOpen)
                        StartCharging();
                    break;
                case State.AtGate:
                    // dockToStream: input locked, the ship keeps moving at its speed (the game is paused while asking).
                    ship.transform.position += ship.transform.forward * speed * dtMs * M;
                    break;
                case State.Charging:
                    chargeMs += dtMs;
                    Haptics.Rumble(0.1f + 0.35f * ChargeRate);   // remake: the drive winding up
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
            if (cam != null) cam.transform.rotation = Quaternion.LookRotation(ship.transform.position - cam.transform.position, Vector3.up);
        }

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
            var map = StarMap.Open(db, StarMapMode.Gate, false, OnGateMapClosed, prog >= 0 && prog != Session.StationIndex ? prog : -1);
            if (map == null) OnGateMapClosed(new StarMapResult { station = -1 });
        }

        void OnGateMapClosed(StarMapResult result)
        {
            nav.Paused = false;
            if (result.station >= 0)
            {
                Session.ProgrammedStation = result.station;
                StartGateScene(result.station);
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
            Haptics.Play(Haptics.Jump);   // remake
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
                // the Void, startChargingJumpDrive, nextCampaignMission (-> 79). No map.
                if (Story.Index == 78 && !inVoid) Story.Advance(db);
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
            bool askVoid = GalaxyMap.HasIntegratedDrive(Session.ShipIndex) || Session.Equipment.Exists(e => e.item == GalaxyMap.KhadorDriveItem);
            var map = StarMap.Open(db, StarMapMode.Khador, true, r =>
            {
                nav.Paused = false;
                if (weapons != null) weapons.Blocked = false;
                if (r.toVoid) { storyTarget = Session.VoidOrbit; jumpCells = VoidCells(true); storyForced = false; return; }
                if (r.station < 0) return;
                Session.ProgrammedStation = r.station;
                if (r.instantJump) { Session.InstantJump = true; Session.EnergyCellsForNextJump = r.cells; }
                else nav.ContinueToProgrammedStation();
            }, -1, -1, askVoid);
            if (map == null) { nav.Paused = false; if (weapons != null) weapons.Blocked = false; }
        }

        void StartCharging()
        {
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
            int station = storyTarget ?? Session.ProgrammedStation;
            if (station < 0 && !storyTarget.HasValue) { state = State.None; return; }
            BeginScene(station);
            state = State.KhadorScene;
            var fxPos = ship.transform.position + ship.transform.forward * 3000f * M;
            fx = SpawnKhadorFx(assets, fxPos, ship.transform.rotation, out animLength);
            JumpFxStarted?.Invoke(false, fxPos, ship.transform.rotation);
            activated = true;
            animMs = 0f;
            var cam = Camera.main;
            if (cam != null) cam.transform.position = fxPos + ship.transform.rotation * (new Vector3(2000f, 300f, -2000f) * M);   // game (-2000, 300, -2000)
            Play(assets != null ? assets.khadorDrive : null);
            Haptics.Play(Haptics.Jump);   // remake
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
            if (chase != null) chase.enabled = false;
            var cam = Camera.main;
            if (cam != null) cam.fieldOfView = Aspect.VerticalFov(1.22f * Mathf.Rad2Deg, cam.aspect);
        }

        void UpdateScene(float dtMs)
        {
            bool gateScene = state == State.GateScene;
            var cam = Camera.main;
            if (!hidden && cam != null)   // ship-space game (5, 2, -3 / -5) u/ms -> Unity local (-5, 2, -3 / -5)
                cam.transform.position += ship.transform.rotation * (new Vector3(-5f, 2f, gateScene ? -3f : -5f) * dtMs * M);
            float step = speed * dtMs * M;
            ship.transform.position += ship.transform.forward * step;
            ship.ExternalSpeedMetersPerSecond = speed * M * 1000f;

            if (gateScene && !activated && gate != null && cam != null
                && -cam.transform.position.z / M < -gate.transform.position.z / M - 10000f)   // camera z (game) < gate z - 10000
                ActivateGate();
            if (!activated) return;
            animMs += dtMs;
            if (!hidden && animMs > (gateScene ? 1000f : 1700f))
            {
                hidden = true;
                speed = HiddenSpeed;
                if (ship.visualModel != null) ship.visualModel.gameObject.SetActive(false);
            }
            if (animMs >= animLength) Arrive(gateScene);
        }

        /// <summary>Status::departStation(programmedStation), Level::setInitStreamOut, reload in the target orbit.</summary>
        void Arrive(bool viaGate)
        {
            weapons?.StoreAmmo();
            if (targetStation == Session.VoidOrbit) Session.VoidReturnStation = Session.StationIndex;
            Session.PreviousStationIndex = Session.StationIndex;
            Session.StationIndex = targetStation;
            Session.ArrivedByTravel = true;
            Session.ArrivedBySystemJump = true;
            Session.LaunchedFromStation = false;
            if (viaGate) Session.JumpgatesUsed++;
            Session.ProgrammedStation = -1;
            Session.InstantJump = false;
            state = State.None;
            enabled = false;
            if (Application.CanStreamedLevelBeLoaded(spaceScene)) SceneManager.LoadScene(spaceScene);
        }

        void Play(AudioClip clip)
        {
            if (clip != null) sfx.PlayOneShot(clip, Settings.SfxVolume);
        }
    }
}
