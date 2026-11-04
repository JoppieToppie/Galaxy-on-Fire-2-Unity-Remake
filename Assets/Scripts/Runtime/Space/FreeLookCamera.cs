// FreeLookCamera.cs
// The flight camera modes (MGame::switchCamera 0x1ac34c / nextCamId 0x1ac488, HUD element 0x80): the camera button cycles
// Standard (0) -> Turret (1, only with a manual turret or a plasma collector, sort 8 / 0x23) -> Free look (3) -> Standard;
// the cockpit view (2) can't be reached in this build. The new mode's name shows for 4000 ms (Hud::draw: 217 Standard,
// 218 Turret, 220 Free look). Free look (LevelScript::lookBehind + setRotationAroundTarget / setFreeLookMode): the camera
// orbits the ship (MGame::freeCamTouch*: -0.005 rad per px, the pitch offset clamped to +-200 px, a fling that decays x0.9
// per frame; zoom 1500..20000 units, wheel / key steps x -50) while the ship flies on under the player's controls; mining,
// object docking, cutscenes, the jump scenes and death go back to the standard view.
// Input (remake): V / controller D-pad up cycles; the orbit follows a touch drag off the controls (FlightHud forwards it
// instead of the dodge swipe), the mouse (moved with the mouse-steering cursor captured, else with the middle button
// held), or the right stick; the wheel or a two-finger
// pinch zooms (as in PhotoMode; the triggers keep firing).
// Remake: with several manual turrets (custom ships) the Turret mode is visited once per turret, in mount order
// (Standard -> Turret 1 -> Turret 2 -> Free look); the turrets are looked up on the player each time, so a hull swap's new
// turrets are found.
// Remake: the cockpit view (2, text 219 "Cockpit", the eye icon 0x4be / 0x4bf) is reachable, but not by the cycle
// (players' feedback, 2026-10-22: the button's first press went to the cockpit, and they wanted the orbit view at once):
// holding the camera button half a second (the key, the controller's D-pad up, the touch button) or the "Cockpit view"
// binding (F2) switches between the chase view and the cockpit, and the cycle runs from whichever of the two is the ship
// view: it -> Turret -> Free look -> it (the button then acts on release). Not in VR, which flies from the seat already.
// ChaseCamera places it (firstPerson). It stays the mode through what takes the camera over (launch / arrival,
// cutscenes, jumps, mining, object docking, death, Action Freeze: the chase camera is off meanwhile and comes back in it),
// and the ship view picked last is kept for the next flights (PlayerPrefs flight_camera_cockpit); the turret view or free
// look ending by itself goes back to it (Base).

using System;
using GoF2Remake.Data;
using GoF2Remake.UI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GoF2Remake.Flight
{
    public class FreeLookCamera : MonoBehaviour
    {
        const float M = 0.05f, FrameMs = OriginalFrame.Ms, MinDistance = 1500f, MaxDistance = 20000f;
        public enum Mode { Standard = 0, Turret = 1, FirstPerson = 2, FreeLook = 3 }
        const string CockpitPref = "flight_camera_cockpit";

        static InputAction cycleAction => GameControls.Camera;   // rebindable (T / D-pad up)
        public Mode Current { get; private set; } = Mode.Standard;
        public bool FreeLookActive => Current == Mode.FreeLook;
        /// <summary>The view flying from the bow (Mode.FirstPerson).</summary>
        public bool FirstPersonActive => Current == Mode.FirstPerson;
        /// <summary>Standard or the cockpit view: the one the camera button reached last (kept between flights).</summary>
        public Mode Base => Cockpit ? Mode.FirstPerson : Mode.Standard;
        static bool Cockpit
        {
            get => !VrFlags.Enabled && PlayerPrefs.GetInt(CockpitPref, 0) == 1;
            set { PlayerPrefs.SetInt(CockpitPref, value ? 1 : 0); }
        }
        /// <summary>The ship view the player flies in: Standard or Cockpit (not the turret view or free look).</summary>
        public bool ShipView => Current == Mode.Standard || Current == Mode.FirstPerson;
        /// <summary>The mode's name as a HUD message (4000 ms).</summary>
        public event Action<string> Message;
        /// <summary>A mode change is refused while this holds (a cutscene, a jump, mining, docking at an object).</summary>
        public Func<bool> Blocked;
        /// <summary>While Blocked: the turret views may still be cycled (docked at an object, SpaceLevel), not free look.</summary>
        public Func<bool> TurretAllowed;
        /// <summary>Docked at an object (not hacking): MGame::switchCamera doesn't go back to the standard view there, the
        /// camera button cycles the manual turrets and free look (SpaceLevel).</summary>
        public Func<bool> DockedView;

        ChaseCamera chase;
        int turretIndex;   // which manual turret the Turret mode looks through
        Transform anchor;
        float px, py, distance = 3800f, wheel, pinch;
        Vector2 fling, touchDrag;
        bool touchHeld;
        /// <summary>FlightHud: the cursor is captured for mouse steering, so in free look plain mouse movement orbits the
        /// camera (remake: the PC version has no mouse free look; the middle button held does the same without it).</summary>
        [NonSerialized] public bool mouseLook;
        /// <summary>Modified: FlightHud: the pointer is over a HUD control or the hacking puzzle, where a left press is a click,
        /// not the docked orbit's drag.</summary>
        [NonSerialized] public Func<bool> pointerOverHud;
        bool leftDrag;   // Modified: docked at an object, the left button held from off the HUD orbits the camera

        /// <summary>'turret' is unused since the turrets are looked up on the player (PlayerTurret.On); kept for the callers.</summary>
        public static FreeLookCamera Attach(GameObject player, ChaseCamera chase, PlayerTurret turret = null)
        {
            var f = player.AddComponent<FreeLookCamera>();
            f.chase = chase;
            return f;
        }

        /// <summary>The ship's manual turrets (and plasma collectors), in mount order: the ones with a turret view. Looked up
        /// at most once a frame (the HUD asks for Next every frame).</summary>
        System.Collections.Generic.List<PlayerTurret> Manual()
        {
            if (manualFrame != Time.frameCount || manualCache == null)
            {
                manualCache = PlayerTurret.On(gameObject).FindAll(t => !t.IsAuto);
                manualFrame = Time.frameCount;
            }
            return manualCache;
        }
        System.Collections.Generic.List<PlayerTurret> manualCache;
        int manualFrame = -1;

        PlayerTurret CurrentTurret
        {
            get
            {
                var manual = Manual();
                return turretIndex >= 0 && turretIndex < manual.Count ? manual[turretIndex] : null;
            }
        }

        void Awake()
        {
            anchor = new GameObject("Free look anchor").transform;
            anchor.SetParent(transform, false);
        }

        void Start()
        {
            if (Base == Mode.FirstPerson) Set(Mode.FirstPerson, false);   // the view the last flight was in
        }

        bool TurretMode => Manual().Count > 0;

        /// <summary>MGame::nextCamId: the mode the camera button leads to (remake: Turret again while another manual turret
        /// follows).</summary>
        public Mode Next => ShipView ? AfterCockpit
                          : Current == Mode.Turret ? (turretIndex + 1 < Manual().Count ? Mode.Turret : Mode.FreeLook) : Base;
        Mode AfterCockpit => TurretMode ? Mode.Turret : Mode.FreeLook;

        /// <summary>The camera button (touch, V, D-pad up).</summary>
        public void Cycle()
        {
            // Docked at an object: each manual turret -> free look -> the turrets again (#132: the remake had no free look
            // there at all).
            if (DockedView != null && DockedView() && (Blocked == null || !Blocked()))
            {
                if (Current == Mode.Turret && turretIndex + 1 < Manual().Count) { EnterTurret(turretIndex + 1, true); return; }
                bool turrets = TurretMode && TurretAllowed != null && TurretAllowed();
                Set(Current != Mode.Turret && turrets ? Mode.Turret : Mode.FreeLook);
                if (Current == Mode.Standard) Set(Mode.FreeLook);   // a turret view refused
                return;
            }
            if (Blocked != null && Blocked())
            {
                // Docked at an object: standard -> each manual turret -> standard (PlayerEgo::setTurretMode allows it there).
                if (TurretAllowed == null || !TurretAllowed() || !TurretMode) { if (Current != Mode.Standard) Set(Mode.Standard); RememberBase(); return; }
                if (Current == Mode.Turret && turretIndex + 1 < Manual().Count) { EnterTurret(turretIndex + 1, true); return; }
                Set(ShipView ? Mode.Turret : Mode.Standard);
                RememberBase();
                return;
            }
            if (Current == Mode.Turret && turretIndex + 1 < Manual().Count) { EnterTurret(turretIndex + 1, true); return; }
            Set(Next);
            RememberBase();
        }

        /// <summary>Holding the camera button this long switches the ship view (ToggleCockpit) instead of cycling.</summary>
        const float CockpitHoldSeconds = 0.5f;
        float cameraDownAt = -1f;
        bool cameraHoldUsed;

        /// <summary>The camera button went down (the key, the controller, the touch button): a tap cycles on release, a hold
        /// switches to or from the cockpit view (Update).</summary>
        public void CameraDown()
        {
            cameraDownAt = Time.unscaledTime;
            cameraHoldUsed = false;
        }

        /// <summary>The camera button came up: a tap's cycle ('cycle' false: only let go, e.g. while mining).</summary>
        public void CameraUp(bool cycle = true)
        {
            bool tap = cameraDownAt >= 0f && !cameraHoldUsed;
            cameraDownAt = -1f;
            if (tap && cycle) Cycle();
        }

        /// <summary>The chase view <-> the cockpit view (the "Cockpit view" binding, a held camera button); from a turret view
        /// or free look straight into the other ship view.</summary>
        public void ToggleCockpit()
        {
            if (VrFlags.Enabled || Blocked != null && Blocked()) return;
            var to = Base == Mode.FirstPerson ? Mode.Standard : Mode.FirstPerson;
            if (heldLook) { heldFrom = to; Cockpit = to == Mode.FirstPerson; return; }   // free look by the held key: back into it
            Set(to);
            RememberBase();
        }

        /// <summary>The camera button reached Standard or the cockpit view: the one the next flights start in.</summary>
        void RememberBase()
        {
            if (ShipView && Cockpit != (Current == Mode.FirstPerson)) Cockpit = Current == Mode.FirstPerson;
        }

        bool heldLook;   // free look by the held key (FreeLookHold)
        Mode heldFrom;   // ... and the ship view it came from

        public void Set(Mode m, bool announce = true)
        {
            if (m == Current) return;
            if (Current == Mode.Turret) CurrentTurret?.SetTurretView(false);
            if (Current == Mode.FreeLook) ExitFreeLook();
            if (Current == Mode.FirstPerson && chase != null) chase.firstPerson = false;
            Current = m;
            if (m == Mode.Turret) { EnterTurret(0, announce); return; }
            if (m == Mode.FreeLook) EnterFreeLook();
            if (m == Mode.FirstPerson && chase != null) { chase.firstPerson = true; if (chase.enabled && chase.follow == null) chase.Snap(); }
            if (announce) Message?.Invoke(Localization.Get(m == Mode.Standard ? 217 : m == Mode.FirstPerson ? 219 : 220));
        }

        /// <summary>The turret view of the i-th manual turret (it leaves another turret's view by itself); refused -> Standard.
        /// The mode's name carries the turret's number when there are several.</summary>
        void EnterTurret(int index, bool announce)
        {
            turretIndex = index;
            Current = Mode.Turret;
            var t = CurrentTurret;
            t?.SetTurretView(true);
            if (t == null || !t.InTurretView) { Current = Mode.Standard; turretIndex = 0; Set(Base, false); return; }
            int count = Manual().Count;
            if (announce) Message?.Invoke(Localization.Get(218) + (count > 1 ? " " + (index + 1) : ""));
        }

        /// <summary>Docked at an object (ObjectDocking.Docked): MGame::OnUpdate's switchCamera(3) with its preset orbit
        /// (MGame+0x110 / +0x114 = -98 / 35 px, +0xb8 = 3800 units).</summary>
        public void EnterDockedLook()
        {
            dockedSaved = false;
            if (Current == Mode.FreeLook) { dockedView = true; return; }
            // Modified: the orbit starts where the docking's camera is and glides from there to the original's preset orbit
            // (-98 / 35 px at 3800 units), as the original's camera moves over to it (a player's comparison: the remake
            // jumped there; then, starting where the docking view was, it never went over). Dragging stops the glide.
            bool fromCamera = OrbitFromCamera(out float cx, out float cy, out float cd);
            Set(Mode.FreeLook);
            dockedView = true;
            px = DockPx; py = DockPy; distance = 3800f;
            if (fromCamera)
            {
                cx = px + Mathf.Repeat(cx - px + FullTurnPx * 0.5f, FullTurnPx) - FullTurnPx * 0.5f;   // the short way round
                px = cx; py = cy; distance = cd;
                dockGlide = true;
            }
            Place();
        }

        // Modified: a level's cutscene ending while docked (92's Specter entrance): LevelScript ev5's end keeps the camera where
        // the cutscene left it (setLookAtCam(false), no resetCamera) and, docked, goes back to free look (lookBehind +
        // setRotationAroundTarget + setFreeLookMode), so TargetFollowCamera eases from there back to the ship: the camera
        // flies back to the orbit it had before the cutscene instead of jumping to it (a player's comparison).
        float dockedPx, dockedPy, dockedDistance;
        bool dockGlide;   // EnterDockedLook: easing over to the preset orbit
        // Modified: the free look the docking started: the undock ends it where the camera is, no snap behind the ship (a
        // player's request); ObjectDocking's look-at keeps the ship in view, then the chase camera eases over.
        bool dockedView, easeOut;
        const float FullTurnPx = 2f * Mathf.PI / 0.005f, DockGlideMs = 450f;
        // Modified: the docked orbit's yaw: the preset -98 px from the bow, not the stern. LevelScript::lookBehind turns the
        // camera round before the free look (92 ev5), and the original's docked view looks at the ship from in front (a
        // player's screenshot); from behind it looked over the ship's tail.
        const float DockPx = -98f + FullTurnPx * 0.5f;
        // Modified: and above the ship: the remake's orbit pitch runs opposite the original's (Place, the free look's inverted
        // up / down), so its +35 px put the camera under the hull (a player's screenshot).
        const float DockPy = -35f;
        bool dockedSaved;

        /// <summary>CutsceneCamera.Release: docked (not hacking, nothing blocking free look), free look again on the orbit it
        /// had when the cutscene took the camera (the preset one without), eased in from where the camera is; false = not
        /// docked (the caller snaps the chase camera behind the ship).</summary>
        public bool ResumeDockedLook()
        {
            if (DockedView == null || !DockedView() || (Blocked != null && Blocked()) || !DockingHoldsCamera) return false;
            if (Current != Mode.FreeLook) Set(Mode.FreeLook, false);
            if (Current != Mode.FreeLook || chase == null || chase.follow != anchor) return false;
            dockedView = true;
            if (dockedSaved) { px = dockedPx; py = dockedPy; distance = dockedDistance; }
            else { px = DockPx; py = DockPy; distance = 3800f; }
            fling = Vector2.zero; wheel = 0f;
            chase.followRigid = false;
            Place();
            return true;
        }

        /// <summary>Modified: the orbit (px, py, distance) that puts the free look's camera where the camera is now, in the ship's
        /// frame (Place's inverse: the camera at anchor * (0, 0, -distance), anchor = Euler(-0.005 py, 0.005 px)); false when
        /// there is no camera or it sits on the ship.</summary>
        bool OrbitFromCamera(out float x, out float y, out float d)
        {
            x = y = d = 0f;
            if (chase == null) return false;
            var v = transform.InverseTransformDirection(chase.transform.position - anchor.position);
            float len = v.magnitude;
            if (len < 0.5f) return false;
            var u = -v / len;   // the anchor's forward
            float pitch = Mathf.Asin(Mathf.Clamp(-u.y, -1f, 1f)), yaw = Mathf.Atan2(u.x, u.z);
            x = yaw / 0.005f;
            y = Mathf.Clamp(-pitch / 0.005f, -200f, 200f);
            d = Mathf.Clamp(len / M, MinDistance, MaxDistance);
            return true;
        }

        void EnterFreeLook()
        {
            px = 0f; py = 0f; fling = Vector2.zero; wheel = 0f;
            distance = Mathf.Clamp(distance, MinDistance, MaxDistance);
            Place();
            if (chase == null) return;
            chase.follow = anchor;
            chase.followOffset = new Vector3(0f, 0f, -distance * M);
            chase.followLookOffset = Vector3.zero;
            chase.followRigid = false;
            chase.followUsesUp = true;
            chase.followClamp = ClampCamera;
            // Docked at an object the chase camera is off (ObjectDocking's look-at camera): free look moved only the anchor
            // the disabled camera never followed, so the docked free look (a Supernova freighter, a mining station) showed
            // the fixed look-at view and dragging did nothing (Discord report). On for the orbit, as PlayerTurret's view.
            if (DockingHoldsCamera && !chase.scriptCamera) chase.enabled = true;
        }

        void ExitFreeLook()
        {
            if (chase == null || chase.follow != anchor) return;
            if (DockingHoldsCamera) { dockedPx = px; dockedPy = py; dockedDistance = distance; dockedSaved = true; }   // ResumeDockedLook
            dockGlide = false;
            dockedView = false;
            bool ease = easeOut;
            easeOut = false;
            chase.follow = null;
            if (chase.followClamp == (Func<Vector3, Vector3, Vector3>)ClampCamera) chase.followClamp = null;
            // Modified: not when a level's cutscene took the camera (CutsceneCamera sets scriptCamera; its Cutscene ends free
            // look through Blocked): the snap put the camera behind the ship, over the cutscene's look-at position placed
            // the same frame, so the docked free look at 92 / 94 (Tadram's freighter, Luur's station) left the Specters'
            // entrance seen from behind the player, the fighters far off in the background (a player's report).
            if (chase.scriptCamera) return;
            if (!ease) chase.Snap();
            if (DockingHoldsCamera && !chase.scriptCamera) chase.enabled = false;   // back to ObjectDocking's look-at camera
        }

        // Modified: docked at an object (a freighter, a station, a carrier) the orbit could put the camera inside its hull
        // (the original's too). The camera is kept on this side of the first hull between the ship and it: the hull meshes
        // (HullCollision.CameraHit, a sphere of CameraRadius swept out from the ship, the player's own ship and fighters left
        // out), or without them (Options > Gameplay "Ship collision": Original) the objects' collision volumes it doesn't
        // start inside. Applied after the chase camera's easing, so it never shows the inside while catching up.
        const float CameraRadius = 2f, CameraMinMeters = 1f;

        Vector3 ClampCamera(Vector3 pivot, Vector3 position)
        {
            if (chase == null || chase.follow != anchor || !DockingHoldsCamera) return position;   // the free look's orbit only
            var d = position - pivot;
            float len = d.magnitude;
            if (len < 0.01f) return position;
            var dir = d / len;
            float hit = HullCollision.CameraHit(pivot, dir, len, CameraRadius, transform) - CameraRadius * 0.5f;
            var all = Obstacle.All;
            for (int i = 0; i < all.Count; i++)
            {
                var o = all[i];
                if (o == null || (o.hull != null && o.hull.Active)) continue;   // a hull: CameraHit
                float t = o.RayDistance(pivot, dir);
                if (t > 0f && t < float.MaxValue) hit = Mathf.Min(hit, t - CameraRadius);
            }
            if (hit >= len) return position;
            return pivot + dir * Mathf.Max(CameraMinMeters, hit);
        }

        ObjectDocking docking;
        /// <summary>An object docking's ease-in, docked or undocking: ObjectDocking has turned the chase camera off.</summary>
        bool DockingHoldsCamera
        {
            get
            {
                if (docking == null) docking = GetComponent<ObjectDocking>();
                return docking != null && (docking.State == ObjectDocking.Phase.Entering || docking.State == ObjectDocking.Phase.Docked
                                           || docking.State == ObjectDocking.Phase.Leaving);
            }
        }

        /// <summary>FlightHud: a touch drag off the controls (screen pixels, y up) goes to the camera in free look.</summary>
        public void TouchDrag(Vector2 deltaPixels, bool held)
        {
            touchDrag += deltaPixels;
            touchHeld = held;
        }

        /// <summary>FlightHud: two fingers off the controls change their span (screen pixels) to zoom in free look.</summary>
        public void TouchPinch(float spanPixels) => pinch += spanPixels;

        void Update()
        {
            bool halted = Time.timeScale <= 0f;
            // Not under a menu either (Navigation.PressesBlocked): in a multiplayer session the game doesn't pause, and the
            // pause / autopilot / secondary weapon menus' D-pad up cycled the camera (Discord report, 2026-10-17).
            // A tap cycles on release, a hold switches the cockpit view (CameraDown / CameraUp, also the touch button's).
            if (!halted && !Navigation.PressesBlocked && cycleAction.WasPressedThisFrame()) CameraDown();
            if (cycleAction.WasReleasedThisFrame() && cameraDownAt >= 0f) CameraUp(!halted && !Navigation.PressesBlocked);
            if (cameraDownAt >= 0f && !cameraHoldUsed && Time.unscaledTime - cameraDownAt >= CockpitHoldSeconds)
            {
                cameraHoldUsed = true;
                if (!halted && !Navigation.PressesBlocked) ToggleCockpit();
            }
            if (!halted && !Navigation.PressesBlocked && GameControls.CockpitView.WasPressedThisFrame()) ToggleCockpit();
            // Remake: free look while a key is held (GameControls.FreeLookHold, EVERSPACE 2's Alt in the free-flight layout):
            // from the standard view, back to it on release.
            if (!halted && !Navigation.InputHalted)
            {
                bool hold = GameControls.FreeLookHold.IsPressed();
                if (hold && ShipView && !heldLook && (Blocked == null || !Blocked())) { heldLook = true; heldFrom = Current; Set(Mode.FreeLook, false); }
                else if (!hold && heldLook) { heldLook = false; if (Current == Mode.FreeLook) Set(heldFrom, false); }
            }
            // The turret view ended on its own (mining, blocked guns): back to the standard mode.
            if (Current == Mode.Turret && (CurrentTurret == null || !CurrentTurret.InTurretView)) { Current = Mode.Standard; turretIndex = 0; Set(Base, false); }
            if (Current != Mode.FreeLook) return;
            // The undock: the orbit ends, the camera staying where it is (ObjectDocking's look-at turns it after the ship, and
            // its end lets the chase camera ease over).
            if (chase != null && !chase.scriptCamera && (DockingHoldsCamera ? docking.State == ObjectDocking.Phase.Leaving : dockedView)) { easeOut = true; Set(Base, false); return; }
            if (Blocked != null && Blocked()) { Set(Base, false); return; }
            if (chase != null && chase.follow != anchor && chase.follow != null) { Current = Mode.Standard; Set(Base, false); return; }   // taken over (the Liberator)
            if (halted || Navigation.InputHalted) return;   // a menu's sticks and keys aren't the orbit's

            float frames = Time.deltaTime * 1000f / FrameMs;
            float scale = 1080f / Mathf.Max(1, Screen.height);   // the original's pixels
            Vector2 delta = Vector2.zero;
            bool held = false;
            if (touchDrag != Vector2.zero || touchHeld)
            {
                delta = new Vector2(touchDrag.x, -touchDrag.y) * scale;
                held = touchHeld;
                touchDrag = Vector2.zero;
            }
            // The mouse's movement, not the cursor's position (that stops at the screen edge and never moves while captured):
            // with the middle button held, or always with the cursor captured for mouse steering (FlightHud, mouseLook).
            var mouse = Mouse.current;
            // Modified: docked at an object (Supernova 91's freighters, the hacking docks) the guns are off, so the left button
            // drags the orbit like the original's touch drag (the middle button still does); a press on a HUD button or the
            // hacking puzzle stays a click.
            if (mouse == null || !mouse.leftButton.isPressed || !ObjectDocking.PlayerDocked) leftDrag = false;
            else if (mouse.leftButton.wasPressedThisFrame) leftDrag = pointerOverHud == null || !pointerOverHud();
            if (mouse != null && (mouseLook || mouse.middleButton.isPressed || leftDrag))
            {
                var d = Settings.MouseMove(mouse.delta.ReadValue()) * scale;
                delta += new Vector2(d.x, -d.y);
                held = true;
            }
            if (held) fling = delta;
            else if (fling != Vector2.zero)
            {
                delta = fling * frames;
                fling *= Mathf.Pow(0.9f, frames);
                if (fling.magnitude <= 1f) fling = Vector2.zero;
            }
            var pad = Gamepad.current;
            if (pad != null)
            {
                var s = pad.rightStick.ReadValue();
                delta += new Vector2(-s.x, -s.y) * 8f * frames;   // right: the camera to the ship's right, up: up (like PhotoMode's arrows)
            }
            distance += pinch * scale * -50f;
            pinch = 0f;
            wheel += MouseWheel.Steps(mouse);
            if (Mathf.Abs(wheel) > 0.01f) { distance += wheel * -50f * frames; wheel *= Mathf.Pow(0.9f, frames); } else wheel = 0f;
            // Remake: the throttle binding (LB / RB, ] / /) zooms in free look; ShipController leaves the throttle alone meanwhile
            // (Discord report, 2026-10-17: no zoom on a controller).
            float zoom = GameControls.Throttle.ReadValue<float>();
            if (Mathf.Abs(zoom) > 0.01f) distance -= zoom * 150f * frames;
            // Modified: up and down turned the other way round in free look, for the mouse, a finger and the right stick alike
            // (the Invert orbit Y option still flips it back).
            delta.y = -delta.y;
            delta = GoF2Remake.Data.Settings.OrbitTurn(delta);   // #121: the camera views' own inversion
            if (dockGlide)
            {
                if (held || delta.sqrMagnitude > 0.01f || wheel != 0f || Mathf.Abs(zoom) > 0.01f || !DockingHoldsCamera) dockGlide = false;
                else
                {
                    float k = 1f - Mathf.Exp(-Time.deltaTime * 1000f / DockGlideMs);
                    px = Mathf.Lerp(px, DockPx, k); py = Mathf.Lerp(py, DockPy, k); distance = Mathf.Lerp(distance, 3800f, k);
                    if (Mathf.Abs(px - DockPx) < 0.5f && Mathf.Abs(py - DockPy) < 0.5f && Mathf.Abs(distance - 3800f) < 5f) dockGlide = false;
                }
            }
            px += delta.x;
            py = Mathf.Clamp(py + delta.y, -200f, 200f);
            distance = Mathf.Clamp(distance, MinDistance, MaxDistance);
            Place();
        }

        /// <summary>TargetFollowCamera::rotateAroundTarget in the ship's frame (-0.005 py, -0.005 px), mirrored into Unity's
        /// (-x, y, z) ship frame: pitch -0.005 py, yaw +0.005 px (see PhotoMode.Place; unmirrored, every axis was inverted).</summary>
        void Place()
        {
            anchor.localRotation = Quaternion.Euler(-0.005f * py * Mathf.Rad2Deg, 0.005f * px * Mathf.Rad2Deg, 0f);
            if (chase != null && chase.follow == anchor) chase.followOffset = new Vector3(0f, 0f, -distance * M);
        }
    }
}
