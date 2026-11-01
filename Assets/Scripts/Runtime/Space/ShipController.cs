// ShipController.cs
// Drives a ship transform with FlightModel. Put it on the ship root; put the visible mesh in a
// child object and assign it to 'visualModel' to get the original's cosmetic banking.
//
// Input: Input System package only (no legacy Input Manager). The four actions below are serialized
// on the component, so bindings can be edited/rebound in the Inspector. Defaults:
//   Steer         WASD / arrow keys / gamepad left stick
//   Throttle      E = up, Q = down / gamepad right bumper = up, left bumper = down (the triggers fire,
//                 WeaponSystem)
//   Boost         Space / gamepad south button (A / Cross)
//   AlignHorizon  R / gamepad north button (Y / Triangle)
// Touch or your own UI: call SetSteer()/SetThrottle()/Boost(); the stronger of the external steer and the
// built-in actions wins, so both can be used at the same time (FlightHud's touch stick does this).
// Remake: free flight (Settings.FlightStyle, after EVERSPACE 2; FlightModel.StepFree) with the keyboard / mouse or a
// controller: GameControls' Thrust / Strafe / Hover / Roll held, the stick or the mouse's virtual joystick aims, the boost
// held, Dampeners toggles the inertial dampeners. Touch, tilt and VR keep the original style; so do the autopilot, the
// launch / arrival camera, the turret view, the dodge and computer control, which hand the ship back where they left it.

using GoF2Remake.UI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GoF2Remake.Flight
{
    [DisallowMultipleComponent]
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public class ShipController : MonoBehaviour
    {
        /// <summary>Free flight's inertial dampeners (the Dampeners key toggles them; on at every start, like EVERSPACE 2's
        /// default; kept between levels).</summary>
        public static bool Dampeners { get; private set; } = true;
        /// <summary>The dampeners were toggled (on / off): the HUD says so.</summary>
        public static event System.Action<bool> DampenersChanged;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Dampeners = true; DampenersChanged = null; }

        /// <summary>Free flight is in use for this ship now: the style (the option, or a mod's / an event's), the player's own input (keyboard / mouse or a
        /// controller; touch, tilt and VR keep the original style).</summary>
        public bool FreeFlightActive => Data.FlightStyles.Current == Data.FlightStyles.Free && useBuiltInInput && !tiltMode
                                        && UI.InputMode.Current != UI.InputKind.Touch && !Data.VrFlags.Enabled;

        [Header("Ship")]
        public FlightStats stats = new FlightStats();

        [Tooltip("Meters per GoF unit. Base speed is 2000 GoF units/s, weapon ranges are ~2000-4000 units.")]
        public float metersPerUnit = 0.05f;

        [Range(0f, 2.2f)] public float sensitivity = 1.0f;

        [Header("Input")]
        [UnityEngine.Serialization.FormerlySerializedAs("useLegacyInput")]
        public bool useBuiltInInput = true;
        public bool invertPitch = false;
        public bool invertYaw = false;
        public float throttleChangePerSecond = 0.8f;

        // The controls are GameControls' (rebindable): Steer, Throttle, Brake, Boost, LevelOut, Roll (remake: manual roll, the
        // PC version's "Turn left / right"; the phone original only auto-levels), DodgeLeft / DodgeRight.
        [Tooltip("Manual roll at full input (remake, not a recovered constant).")]
        public float rollDegreesPerSecond = 90f;
        float touchRoll;

        /// <summary>The touch HUD's roll (the Level button slid sideways): -1 left .. 1 right.</summary>
        public void SetRoll(float roll) => touchRoll = Mathf.Clamp(roll, -1f, 1f);

        [Header("Visual banking (cosmetic only)")]
        public Transform visualModel;
        [Tooltip("Free flight only: degrees of model roll per unit of H while yawing (tuned by eye). The original style banks from the turn rates (UpdateVisualBank).")]
        public float bankDegreesPerH = 1.2f;
        public float pitchTiltDegreesPerH = 0.4f;
        public float bankSmoothing = 6f;

        [Header("Weight (remake debug: the hulls the player can't normally fly, PlayerHull.ApplyMass)")]
        [Tooltip("0 = an ordinary ship (the original's handling) .. 1 = the largest capital ships: FlightModel.Mass, plus a slower " +
                 "roll, dodge, autopilot turn and banking here, a heavier chase camera and a deeper engine.")]
        [Range(0f, 1f)] public float mass;
        [Tooltip("The normal chase distance / this hull's fitted one (PlayerHull.ApplyMass; 1 for an ordinary ship): the engine " +
                 "sound's distance is scaled by it, so a capital ship's engines, km from its camera, sound as near as a fighter's.")]
        public float engineEarScale = 1f;

        public FlightModel Model { get; private set; } = new FlightModel();
        /// <summary>The dodge (PlayerEgo::updateManeuver).</summary>
        public Maneuver Maneuver { get; } = new Maneuver();
        /// <summary>Every dodge request (PlayerEgo::initManeuver), also one ignored while a dodge runs: the volatile goods' +0.17.</summary>
        public event System.Action DodgeRequested;
        /// <summary>The dodge's sideways move this frame (world, metres; zero otherwise): the chase camera takes back 90 %.</summary>
        public Vector3 ManeuverSlide { get; private set; }
        /// <summary>This frame's strafe move (world, metres): the chase camera moves with it (TargetFollowCamera::translateNoUpdate).</summary>
        public Vector3 StrafeSlide { get; private set; }

        /// <summary>An autopilot moves the ship (asteroid docking, PlayerEgo+0x145): no input, no flight model step and
        /// no cosmetic banking; it reports its speed through ExternalSpeedMetersPerSecond.</summary>
        [System.NonSerialized] public bool externalControl;
        /// <summary>Remake: under externalControl the stick's left / right (and the roll keys) still roll the ship about its
        /// nose, nothing else: the Khador Drive's jump scene (SystemJump), where the original let the player bank the ship, and
        /// the camera with it, while the scene moves it along its path, facing the jump.</summary>
        [System.NonSerialized] public bool turnOnly;
        /// <summary>The death tumble owns the model's rotation (PlayerHealth): no levelling of the bank meanwhile.</summary>
        [System.NonSerialized] public bool modelTumbling;
        /// <summary>The turret view / the Liberator: the ship flies straight on its throttle (no steering, auto-level on);
        /// the stick goes to <see cref="SteerInput"/> instead.</summary>
        [System.NonSerialized] public bool steeringLocked;
        /// <summary>The stick this frame (keys, stick, touch), also while steering is locked.</summary>
        public Vector2 SteerInput { get; private set; }
        [System.NonSerialized] public float ExternalSpeedMetersPerSecond;
        /// <summary>A docking script turns the model itself (the asteroid landing's pitch-up): the levelling leaves it alone.</summary>
        [System.NonSerialized] public bool modelHeld;

        /// <summary>Autopilot (PlayerEgo::setAutoPilot): the world position to fly to, re-read every frame; null = off.
        /// The stick is ignored; throttle, boost and the flight model's speed still apply (autopilot_travel.md 3.3).</summary>
        [System.NonSerialized] public System.Func<Vector3> autopilotTarget;

        /// <summary>Speed this frame in m/s (after world scaling).</summary>
        public float SpeedMetersPerSecond { get; private set; }

        Vector2 externalSteer;
        float bankAngle, tiltAngle;
        bool autopilotBanking;

        void Awake() => ApplyStats();

        /// <summary>Call after changing 'stats' at runtime (new ship, new equipment, cargo change).</summary>
        public void ApplyStats()
        {
            Model.Configure(stats);
            Model.Sensitivity = sensitivity;
        }

        /// <summary>LevelScript's start sequence (the launch / arrival camera): the player's input is ignored.</summary>
        [System.NonSerialized] public bool inputLocked;

        public void SetSteer(Vector2 steer) => externalSteer = Vector2.ClampMagnitude(steer, 1f);

        /// <summary>MGame::maneuverTouchEnd -> PlayerEgo::initManeuver: 1 = dodge left, 2 = right. Refused while the ship
        /// isn't the player's to fly (launch camera, autopilot docking, turret view, cinematics).</summary>
        public bool RequestDodge(int type)
        {
            if (inputLocked || externalControl || steeringLocked || Navigation.InputHalted) return false;
            DodgeRequested?.Invoke();
            return Maneuver.Start(type);
        }
        public void SetThrottle(float t) => Model.SetThrottle(t);
        public void Boost() => Model.Boost();
        public void AlignToHorizon() => Model.AlignToHorizon();

        /// <summary>The accelerometer steers (FlightHud): the tilt sensitivity and pitch factors apply.</summary>
        [System.NonSerialized] public bool tiltMode;
        FreeLookCamera freeLook;

        /// <summary>PlayerEgo::update with Globals::mouseCursorActivated (FlightHud sets it: the option, the keyboard and mouse
        /// in use, nothing on screen): the mouse moves the crosshair, clamped to +-0.7 of half the screen, and the offset /
        /// that limit steers like a stick (left / right / up / down); the ramp divisor is a fixed 12 (the sensitivity option
        /// doesn't apply). Remake: the mouse's movement at Settings.MouseSensitivity.</summary>
        [System.NonSerialized] public bool mouseSteering;
        /// <summary>The crosshair's offset from the screen centre (screen pixels, x right, y up).</summary>
        public Vector2 MouseOffset { get; private set; }

        /// <summary>Mouse steering's virtual stick back to the centre (the Liberator starts and ends straight).</summary>
        public void CenterMouse() => MouseOffset = Vector2.zero;
        /// <summary>Remake: the mouse offset is inside the steering dead zone (Settings.MouseDeadzone): no turning.</summary>
        public bool MouseInDeadzone { get; private set; } = true;

        /// <summary>Free flight: the mouse's virtual joystick radius, of the screen height (full deflection at its edge):
        /// EVERSPACE 2's measured ~0.51 (the turn rate grows linearly with the reticle's offset, 0.229 deg/s per px at 1080p,
        /// 125 deg/s at ~546 px).</summary>
        public const float StickRadius = 0.5f;

        Vector2 ReadMouseSteer()
        {
            var mouse = Mouse.current;
            if (!mouseSteering || mouse == null) { MouseOffset = Vector2.zero; MouseInDeadzone = true; return Vector2.zero; }
            if (Model.FreeFlight) return ReadVirtualStick(mouse);
            // Centred while the autopilot steers, as free flight's stick (#132: the offset kept adding up on the autopilot, the
            // HUD drew a steering dot that steered nothing, and the ship lurched when it handed back). Not while steering is
            // locked: the mouse aims the turret view or the Liberator then (SteerInput), the autopilot flying on (a player's
            // report: a Liberator fired on the autopilot couldn't be steered with the mouse).
            if (autopilotTarget != null && !steeringLocked) { MouseOffset = Vector2.zero; MouseInDeadzone = true; return Vector2.zero; }
            var lim = new Vector2(Screen.width * 0.5f * 0.7f, Screen.height * 0.5f * 0.7f);
            var o = MouseOffset + Data.Settings.MouseMove(mouse.delta.ReadValue());
            MouseOffset = new Vector2(Mathf.Clamp(o.x, -lim.x, lim.x), Mathf.Clamp(o.y, -lim.y, lim.y));
            var steer = new Vector2(MouseOffset.x / Mathf.Max(1f, lim.x), MouseOffset.y / Mathf.Max(1f, lim.y));
            // Remake: a round dead zone (in the same units as the screen's height, so it is a circle on screen), the rest
            // rescaled so the steering still starts at 0 at its edge and reaches full at the limit.
            float dz = Data.Settings.MouseDeadzone, m = new Vector2(MouseOffset.x / Mathf.Max(1f, lim.y), MouseOffset.y / Mathf.Max(1f, lim.y)).magnitude;
            MouseInDeadzone = m <= dz;
            if (MouseInDeadzone) return Vector2.zero;
            var scaled = steer * ((m - dz) / (1f - dz) / m);
            return new Vector2(Mathf.Clamp(scaled.x, -1f, 1f), Mathf.Clamp(scaled.y, -1f, 1f));
        }

        /// <summary>Free flight's mouse, EVERSPACE 2's virtual joystick (measured: the reticle stays where the mouse leaves
        /// it, the ship keeps turning at a rate by its offset): the mouse moves the reticle inside a circle round the centre,
        /// its offset beyond the dead zone (Settings.MouseDeadzone) is the stick's deflection. It holds still while free look
        /// turns the camera and is centred while the autopilot steers; while steering is locked it aims the Liberator or the
        /// turret view instead (SteerInput; it was centred then too, so the mouse never steered a Liberator in free flight).</summary>
        Vector2 ReadVirtualStick(Mouse mouse)
        {
            if (freeLook == null) freeLook = GetComponent<FreeLookCamera>();
            if (autopilotTarget != null && !steeringLocked) MouseOffset = Vector2.zero;
            else if (freeLook == null || !freeLook.FreeLookActive)
                MouseOffset = Vector2.ClampMagnitude(MouseOffset + Data.Settings.MouseMove(mouse.delta.ReadValue()), Screen.height * StickRadius);
            float m = MouseOffset.magnitude / Mathf.Max(1f, Screen.height * StickRadius), dz = Data.Settings.MouseDeadzone;
            MouseInDeadzone = m <= dz;
            if (MouseInDeadzone) return Vector2.zero;
            // A hard dead zone, not rescaled: beyond it the deflection is the whole offset over the radius (measured in
            // EVERSPACE 2: 0.229 deg/s per px of offset, through the centre, from the dead zone's edge on).
            return MouseOffset / Mathf.Max(1f, Screen.height * StickRadius);
        }

        Target selfTarget;
        bool freeSettle;   // free flight brakes the fly-in's speed away (StepFree)
        Transform enginePartsModel;
        GameObject[] engineParts;
        bool enginePartsOff;

        /// <summary>Free flight with no forward thrust (FlightModel.EnginesOff): the model's player engine parts (the original
        /// ships' engine glow, a mod ship's glow discs) aren't drawn. Renderer.enabled, so the parts' active state (mining,
        /// cutscenes: AssembledObject.SetExhaust) and the cloak's forceRenderingOff (CloakGlow) stay theirs.</summary>
        void UpdateEngineParts()
        {
            if (visualModel != enginePartsModel)
            {
                enginePartsModel = visualModel;
                var assembled = visualModel != null ? visualModel.GetComponentInChildren<Visuals.AssembledObject>(true) : null;
                engineParts = assembled != null ? assembled.playerVariantParts : null;
                enginePartsOff = false;   // a new model's renderers start drawn
            }
            bool off = Model.EnginesOff;
            if (off == enginePartsOff || engineParts == null) return;
            enginePartsOff = off;
            foreach (var g in engineParts)
                if (g != null)
                    foreach (var r in g.GetComponentsInChildren<Renderer>(true))
                        if (!IsManoeuvringJet(r.transform)) r.enabled = !off;   // the jets fire without the main engines
        }

        static bool IsManoeuvringJet(Transform t)
        {
            for (; t != null; t = t.parent) if (t.name.StartsWith("engine_glow_thruster")) return true;
            return false;
        }

        void Update()
        {
            UpdateEngineParts();
            // Remake: a boost shakes off the homing missiles locked on this ship (Gun, Target.boosting).
            if (selfTarget == null) selfTarget = GetComponent<Target>();
            if (selfTarget != null) selfTarget.boosting = Model.IsBoosting;
            // A mod or an event can switch the flight style in flight (Modding.ModFlight): its key layout follows.
            if (useBuiltInInput) GameControls.ApplyStyle();
            Model.TiltMode = tiltMode;
            Model.LevelPitch = Data.Settings.LevelPitch;
            Model.Mass = mass;
            // PlayerEgo::up / right with the mouse cursor: the ramp divisor is 12, i.e. (3.3 - sens) x 20 with sens 2.7.
            Model.Sensitivity = tiltMode ? Data.Settings.TiltSensitivity : mouseSteering ? 2.7f : sensitivity;
            // PlayerEgo::left / right / up / down on Extreme (+0x235): the live cargo load against Ship::getMaxLoad.
            if (stats != null && stats.cargoAffectsHandling) stats.cargoLoad = Data.Shop.CargoLoad();
            float dtMs = Time.deltaTime * 1000f * TimeExtender.PlayerFactor;   // MGame+0x44: the player's dt
            Model.UpdateCruise(Navigation.Cruising, dtMs);   // remake, multiplayer: cruise looks like a boost
            if (externalControl)
            {
                // The turret view while docked at an object (PlayerTurret aims by SteerInput): the stick still reads.
                if (steeringLocked && useBuiltInInput && !inputLocked)
                {
                    var turretSteer = ReadInput();
                    if (externalSteer.sqrMagnitude > turretSteer.sqrMagnitude) turretSteer = externalSteer;
                    SteerInput = turretSteer;
                }
                Model.FreeFlight = FreeFlightActive;
                Model.BoostHeld = Model.FreeFlight && useBuiltInInput && !inputLocked && !Navigation.InputHalted && GameControls.Boost.IsPressed();
                Model.TickBoost(dtMs);   // PlayerEgo::update: the boost and its recharge run on (the mining approach boosts)
                SpeedMetersPerSecond = ExternalSpeedMetersPerSecond;
                Model.SyncFree(transform.forward, ExternalSpeedMetersPerSecond / 1000f / metersPerUnit);
                Maneuver.Cancel();
                if (turnOnly && useBuiltInInput && !inputLocked && !Navigation.InputHalted)
                {
                    // Remake (turnOnly): the ship only rolls about its nose, which stays on the scene's path: the stick's
                    // left / right and the roll keys (the scene moves the ship; the camera's drift follows the roll).
                    var s = ReadInput();
                    if (externalSteer.sqrMagnitude > s.sqrMagnitude) s = externalSteer;
                    var ms = ReadMouseSteer();
                    if (ms.sqrMagnitude > s.sqrMagnitude) s = ms;
                    SteerInput = s;
                    float roll = Mathf.Clamp((invertYaw ? -s.x : s.x) + GameControls.Roll.ReadValue<float>() + touchRoll, -1f, 1f);
                    if (Mathf.Abs(roll) > 0.01f)
                        transform.Rotate(0f, 0f, -roll * rollDegreesPerSecond * Model.RollScale * dtMs / 1000f, Space.Self);
                    if (!modelTumbling && !modelHeld) UpdateVisualBank(0f, 0f);
                    return;
                }
                if (!modelTumbling && !modelHeld) UpdateVisualBank(0f, 0f);   // computer controlled: no stick, the model's bank and tilt level out
                return;
            }
            Model.FreeFlight = FreeFlightActive;
            if (useBuiltInInput && !inputLocked) ReadDodgeInput();
            if (Maneuver.Active && (inputLocked || steeringLocked)) Maneuver.Cancel();
            ManeuverSlide = Vector3.zero;
            StrafeSlide = Vector3.zero;
            if (Maneuver.Active)
            {
                // PlayerEgo::updateManeuver instead of handleShip (also over the autopilot's steering).
                Maneuver.Step(dtMs, Model.Handling, out float slide, out float heading, out float yawRate);
                // Remake debug: a heavy hull only lurches a little to the side (FlightModel.SideScale).
                float side = Model.SideScale;
                slide *= side; heading *= side; yawRate *= side;
                var mr = Model.StepManeuver(dtMs, yawRate);
                SteerInput = Vector2.zero;
                transform.Rotate(0f, heading * Mathf.Rad2Deg, 0f, Space.Self);
                ManeuverSlide = -transform.right * (slide * metersPerUnit);
                transform.position += transform.forward * (mr.forwardUnits * metersPerUnit) + ManeuverSlide;
                SpeedMetersPerSecond = dtMs > 0f ? mr.forwardUnits * metersPerUnit / (dtMs / 1000f) : 0f;
                if (Model.FreeFlight && dtMs > 0f) Model.SyncFree(transform.forward, mr.forwardUnits / dtMs);
                UpdateVisualBank();
                return;
            }

            // The launch / arrival camera: no steering, throttle, boost or levelling (the ship flies on). Nor while a menu,
            // conversation or map holds the controls in multiplayer (Navigation.InputHalted), where the clock runs on: the ship
            // flies on as it was (a player's report: the pause menu still steered it).
            bool halted = Navigation.InputHalted;
            bool live = useBuiltInInput && !inputLocked && !halted;
            if (!live) Model.Braking = false;
            if (inputLocked) freeSettle = true;   // free flight takes over at rest once the fly-in is over (StepFree)
            Vector2 steer = live ? ReadInput() : Vector2.zero;
            if (!inputLocked && !halted && externalSteer.sqrMagnitude > steer.sqrMagnitude) steer = externalSteer;
            var mouseSteer = !inputLocked && !halted ? ReadMouseSteer() : Vector2.zero;
            if (mouseSteer.sqrMagnitude > steer.sqrMagnitude) steer = mouseSteer;
            // Remake: a motion controller's gyro (ControllerGyro); Level out re-centres it.
            if (live && !Navigation.PressesBlocked && GameControls.LevelOut.WasPressedThisFrame()) ControllerGyro.Recenter();
            var gyroSteer = live && autopilotTarget == null ? ControllerGyro.Steer(Time.timeScale > 0f ? Time.unscaledDeltaTime : 0f) : Vector2.zero;
            // Added to the other steering (it was the stronger of the two: a tilted controller held the stick off).
            if (gyroSteer != Vector2.zero) steer = new Vector2(Mathf.Clamp(steer.x + gyroSteer.x, -1f, 1f), Mathf.Clamp(steer.y + gyroSteer.y, -1f, 1f));
            SteerInput = steer;
            if (autopilotTarget != null || steeringLocked) steer = Vector2.zero;

            // Model convention: +x = yaw left, +y = pitch down. Map "stick right = turn right".
            var model = new Vector2(invertYaw ? steer.x : -steer.x, invertPitch ? steer.y : -steer.y);

            Model.BoostHeld = Model.FreeFlight && live && GameControls.Boost.IsPressed();
            if (Model.FreeFlight && autopilotTarget == null && !inputLocked && !steeringLocked)
            {
                // In free look the stick (the right one in this layout) and the mouse turn the camera: the ship flies on.
                if (freeLook == null) freeLook = GetComponent<FreeLookCamera>();
                if (freeLook != null && freeLook.FreeLookActive) model = Vector2.zero;
                StepFree(model, dtMs);
                return;
            }

            var r = Model.Step(model, dtMs, transform.up, transform.right);
            r.forwardUnits *= Navigation.CruiseFactor;   // remake, multiplayer: the held fast-forward is a cruise mode

            if (autopilotTarget != null)
            {
                // PlayerEgo::moveToPosition 0xa8720: turn = min(handling + 2.7, 4), dir += (to - dir) * (int)(dt * turn) / 4096,
                // world up (the ship levels out, no roll).
                float turn = Mathf.Min(stats.handling / 100f + 0.2f * stats.handlingUpgrades + 2.7f, 4f) * Model.TurnScale;   // remake debug: a heavy hull turns slower
                var to = (autopilotTarget() - transform.position).normalized;
                // Remake: without the original's (int): at 30 fps it rounds off under 1 %, at 261 fps up to 10 % a frame, a
                // wobble on the autopilot (Discord report, 2026-10-18).
                var dir = (transform.forward + (to - transform.forward) * (dtMs * turn / 4096f)).normalized;
                // The model banks into the turn (#45: it flew the turns flat).
                float turnAngle = Mathf.Acos(Mathf.Clamp(Vector3.Dot(transform.forward, dir), -1f, 1f));
                if (Vector3.Dot(transform.right, dir) > 0f) turnAngle = -turnAngle;
                if (dir.sqrMagnitude > 0f) transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
                Model.AutopilotBank(turnAngle, dtMs, Mathf.Min(Time.unscaledDeltaTime, Time.maximumDeltaTime) * 1000f);
                autopilotBanking = true;
            }
            else if (autopilotBanking) { Model.ResetAutopilotBank(); autopilotBanking = false; }
            if (autopilotTarget == null)
            {
                // Original: yaw positive = left. Unity yaw positive = right, so negate.
                transform.Rotate(r.pitchDeg, -r.yawDeg, r.rollDeg, Space.Self);
                // Remake: manual roll (keys 1 / 3, the touch Level button slid sideways); rolling ends an auto-level.
                float roll = inputLocked || steeringLocked || halted ? 0f : Mathf.Clamp((useBuiltInInput ? GameControls.Roll.ReadValue<float>() : 0f) + touchRoll, -1f, 1f);
                if (Mathf.Abs(roll) > 0.01f)
                {
                    Model.StopLeveling();
                    transform.Rotate(0f, 0f, -roll * rollDegreesPerSecond * Model.RollScale * dtMs / 1000f, Space.Self);
                }
            }
            transform.position += transform.forward * (r.forwardUnits * metersPerUnit)
                                + transform.right * (r.sidePushUnits * metersPerUnit);
            // The PC version's held strafe (bindings 3350 / 3351 "Strafe left / right"): not while the controls are locked.
            // Remake: on the autopilot too (it slides the ship sideways; the autopilot keeps correcting the heading).
            if (live && !steeringLocked)
            {
                int dir = (GameControls.StrafeRight.IsPressed() ? 1 : 0) - (GameControls.StrafeLeft.IsPressed() ? 1 : 0);
                if (dir != 0) Model.Strafe(dir, dtMs);
            }
            float strafe = Model.StepStrafe(dtMs);
            if (strafe != 0f)
            {
                StrafeSlide = transform.right * (strafe * metersPerUnit);
                transform.position += StrafeSlide;
            }

            SpeedMetersPerSecond = dtMs > 0f ? r.forwardUnits * metersPerUnit / (dtMs / 1000f) : 0f;
            if (Model.FreeFlight && dtMs > 0f) Model.SyncFree(transform.forward, r.forwardUnits / dtMs);   // free flight picks up from here

            UpdateVisualBank();
        }

        /// <summary>Free flight (FlightModel.StepFree): the held thrust / strafe / up-down / roll, the aim, the dampeners.</summary>
        void StepFree(Vector2 aim, float dtMs)
        {
            bool live = useBuiltInInput && !Navigation.InputHalted;
            if (live && GameControls.Dampeners.WasPressedThisFrame())
            {
                Dampeners = !Dampeners;
                DampenersChanged?.Invoke(Dampeners);
            }
            var input = new FlightModel.FreeInput
            {
                thrust = live ? GameControls.Thrust.ReadValue<float>() : 0f,
                strafe = live ? GameControls.StrafeAxis.ReadValue<float>() : 0f,
                hover = live ? GameControls.Hover.ReadValue<float>() : 0f,
                roll = live ? Mathf.Clamp(GameControls.Roll.ReadValue<float>() + touchRoll, -1f, 1f) : 0f,
                aim = aim,
                dampeners = Dampeners,
            };
            // After the launch / arrival fly-in (the original flight at full throttle) the ship comes to rest with its engines
            // off, the dampeners braking it even when they are switched off, until the player thrusts or it has stopped.
            if (freeSettle)
            {
                if (input.thrust != 0f || input.strafe != 0f || input.hover != 0f || Model.BoostHeld || Model.FreeVelocity.sqrMagnitude < 1e-6f) freeSettle = false;
                else input.dampeners = true;
            }
            var r = Model.StepFree(input, dtMs, transform.rotation, transform.up, transform.right);
            transform.Rotate(r.pitchDeg, -r.yawDeg, r.rollDeg, Space.Self);
            var move = r.move * metersPerUnit + transform.right * (r.sidePushUnits * metersPerUnit);
            transform.position += move;
            // The chase camera keeps the sideways part of the move (TargetFollowCamera::translateNoUpdate, as for the
            // strafe): it trails the ship's forward motion and stays square behind it while it slides.
            StrafeSlide = move - transform.forward * Vector3.Dot(move, transform.forward);
            SpeedMetersPerSecond = dtMs > 0f ? r.move.magnitude * metersPerUnit / (dtMs / 1000f) : 0f;
            UpdateVisualBank();
        }

        // The dodge bindings (the original: a touch swipe, FlightHud): GameControls' DodgeLeft / DodgeRight (no keyboard default:
        // A / D are the PC version's held strafe; the controller's right stick pushed left / right). A stick binding is left
        // alone in free look, where the right stick turns the camera.
        void ReadDodgeInput()
        {
            if (Navigation.PressesBlocked) return;   // a menu's press (#85)
            if (GameControls.DodgeLeft.WasPressedThisFrame() && !StickInFreeLook(GameControls.DodgeLeft)) RequestDodge(1);
            if (GameControls.DodgeRight.WasPressedThisFrame() && !StickInFreeLook(GameControls.DodgeRight)) RequestDodge(2);
        }

        bool StickInFreeLook(UnityEngine.InputSystem.InputAction a)
        {
            if (freeLook == null) freeLook = GetComponent<FreeLookCamera>();
            if (freeLook == null || !freeLook.FreeLookActive) return false;
            // A quick flick can press and release inside one input update: WasPressedThisFrame holds but activeControl is
            // already gone, so the binding's paths decide (Discord report, 2026-10-17: the right stick dodged in free look).
            var c = a.activeControl;
            if (c != null) return c.path.Contains("Stick", System.StringComparison.OrdinalIgnoreCase);
            foreach (var b in a.bindings)
                if (b.effectivePath != null && b.effectivePath.Contains("Stick", System.StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        bool brakeOverridden;

        Vector2 ReadInput()
        {
            if (Model.FreeFlight)
            {
                // Free flight: thrust is held (StepFree), the boost too (BoostHeld); the level-out still works.
                Model.Braking = false;
                if (GameControls.LevelOut.WasPressedThisFrame()) Model.AlignToHorizon();
                return Vector2.ClampMagnitude(GameControls.Steer.ReadValue<Vector2>(), 1f);
            }
            float throttle = GameControls.Throttle.ReadValue<float>();
            if (freeLook == null) freeLook = GetComponent<FreeLookCamera>();
            // In free look the throttle keys zoom the camera instead (FreeLookCamera; a controller has no wheel or pinch).
            if (Mathf.Abs(throttle) > 0.01f && (freeLook == null || !freeLook.FreeLookActive))
                Model.ChangeThrottle(throttle * throttleChangePerSecond * Time.deltaTime);
            // Brake (S): the engines stop while it is held (FlightModel.Braking); a boost overrides it until it is pressed again.
            bool brake = GameControls.Brake.IsPressed();
            // Presses wait while a menu holds the controls and on the frame it closes (#85: A in the pause, autopilot and quick
            // menus boosted the ship).
            bool presses = !Navigation.PressesBlocked;
            if (!brake) brakeOverridden = false;
            if (presses && brake && GameControls.Boost.WasPressedThisFrame()) brakeOverridden = true;
            Model.Braking = brake && !brakeOverridden;
            // The wheel: +- thrust, 10 % a notch (not while it zooms the free-look camera; MouseWheel: a Samsung tablet's
            // mouse scrolled as it moved).
            if (freeLook == null) freeLook = GetComponent<FreeLookCamera>();
            if (freeLook == null || !freeLook.FreeLookActive)
            {
                float wheel = MouseWheel.Steps(Mouse.current);
                if (Mathf.Abs(wheel) > 0.01f) Model.ChangeThrottle(Mathf.Sign(wheel) * 0.1f);
            }
            if (presses && GameControls.Boost.WasPressedThisFrame()) Model.Boost();
            if (presses && GameControls.LevelOut.WasPressedThisFrame()) Model.AlignToHorizon();
            return Vector2.ClampMagnitude(GameControls.Steer.ReadValue<Vector2>(), 1f);
        }

        /// <summary>The turn rates' units (1/4096 of a turn, PlayerEgo +0x278 / +0x27c) in degrees.</summary>
        public const float RateToDegrees = 360f / 4096f;

        // PlayerEgo::update 0xaa5f4: the model node rolls by -yawRate x 2 pi / 4096 and tilts by pitchRate x 0.25 x 2 pi / 4096,
        // straight from the turn rates, which ramp in (dt H / ((3.3 - sens) 20) a ms) and decay (dt H / 126): no easing of
        // its own. #114: the remake eased toward a fixed angle per stick (+0x25c / +0x258, the frame's input) at 6 / s, so the
        // ship snapped into a bank in ~0.2 s and out of it as fast, where the original's takes ~0.5 s in and ~1.5 s out.
        // Free flight keeps the remake's stick-driven bank; a heavy hull banks less and settles slowly (remake debug, mass).
        void UpdateVisualBank()
        {
            float heavy = Mathf.Lerp(1f, 0.35f, mass);
            if (Model.FreeFlight)
                UpdateVisualBank(Model.VisualYawBank * bankDegreesPerH * heavy, Model.VisualPitchBank * pitchTiltDegreesPerH * heavy);
            else
                UpdateVisualBank(Model.YawRate * RateToDegrees * heavy, Model.PitchRate * 0.25f * RateToDegrees * heavy, mass <= 0f);
        }

        void UpdateVisualBank(float targetBank, float targetTilt, bool direct = false)
        {
            if (visualModel == null) return;
            float k = direct ? 1f : 1f - Mathf.Exp(-bankSmoothing * Mathf.Lerp(1f, 0.2f, mass) * Time.deltaTime);
            bankAngle = Mathf.Lerp(bankAngle, targetBank, k);
            tiltAngle = Mathf.Lerp(tiltAngle, targetTilt, k);
            visualModel.localRotation = Quaternion.Euler(tiltAngle, 0f, bankAngle);
        }

        /// <summary>Push the ship sideways after a collision (decays to 70% per frame like the original).</summary>
        public void OnHitPush(float amount) => Model.AddCollisionPush(amount);

#if UNITY_EDITOR
        void OnValidate()
        {
            if (Application.isPlaying) ApplyStats();
        }
#endif
    }
}
