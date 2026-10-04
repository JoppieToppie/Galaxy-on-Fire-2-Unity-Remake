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

using UnityEngine;
using UnityEngine.InputSystem;

namespace GoF2Remake.Flight
{
    [DisallowMultipleComponent]
    public class ShipController : MonoBehaviour
    {
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
        [Tooltip("Degrees of model roll per unit of H while yawing. Tune by eye; not a recovered constant.")]
        public float bankDegreesPerH = 1.2f;
        public float pitchTiltDegreesPerH = 0.4f;
        public float bankSmoothing = 6f;

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
        /// doesn't apply).</summary>
        [System.NonSerialized] public bool mouseSteering;
        /// <summary>The crosshair's offset from the screen centre (screen pixels, x right, y up).</summary>
        public Vector2 MouseOffset { get; private set; }
        /// <summary>Remake: the mouse offset is inside the steering dead zone (Settings.MouseDeadzone): no turning.</summary>
        public bool MouseInDeadzone { get; private set; } = true;

        Vector2 ReadMouseSteer()
        {
            var mouse = Mouse.current;
            if (!mouseSteering || mouse == null) { MouseOffset = Vector2.zero; MouseInDeadzone = true; return Vector2.zero; }
            var lim = new Vector2(Screen.width * 0.5f * 0.7f, Screen.height * 0.5f * 0.7f);
            var o = MouseOffset + mouse.delta.ReadValue();
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

        void Update()
        {
            Model.TiltMode = tiltMode;
            // PlayerEgo::up / right with the mouse cursor: the ramp divisor is 12, i.e. (3.3 - sens) x 20 with sens 2.7.
            Model.Sensitivity = tiltMode ? Data.Settings.TiltSensitivity : mouseSteering ? 2.7f : sensitivity;
            // PlayerEgo::left / right / up / down on Extreme (+0x235): the live cargo load against Ship::getMaxLoad.
            if (stats != null && stats.cargoAffectsHandling) stats.cargoLoad = Data.Shop.CargoLoad();
            float dtMs = Time.deltaTime * 1000f * TimeExtender.PlayerFactor;   // MGame+0x44: the player's dt
            if (externalControl)
            {
                Model.TickBoost(dtMs);   // PlayerEgo::update: the boost and its recharge run on (the mining approach boosts)
                SpeedMetersPerSecond = ExternalSpeedMetersPerSecond;
                Maneuver.Cancel();
                if (!modelTumbling && !modelHeld) UpdateVisualBank(0f, 0f);   // computer controlled: no stick, the model's bank and tilt level out
                return;
            }
            if (useBuiltInInput && !inputLocked) ReadDodgeInput();
            if (Maneuver.Active && (inputLocked || steeringLocked)) Maneuver.Cancel();
            ManeuverSlide = Vector3.zero;
            StrafeSlide = Vector3.zero;
            if (Maneuver.Active)
            {
                // PlayerEgo::updateManeuver instead of handleShip (also over the autopilot's steering).
                Maneuver.Step(dtMs, Model.Handling, out float slide, out float heading, out float yawRate);
                var mr = Model.StepManeuver(dtMs, yawRate);
                SteerInput = Vector2.zero;
                transform.Rotate(0f, heading * Mathf.Rad2Deg, 0f, Space.Self);
                ManeuverSlide = -transform.right * (slide * metersPerUnit);
                transform.position += transform.forward * (mr.forwardUnits * metersPerUnit) + ManeuverSlide;
                SpeedMetersPerSecond = dtMs > 0f ? mr.forwardUnits * metersPerUnit / (dtMs / 1000f) : 0f;
                UpdateVisualBank();
                return;
            }

            // The launch / arrival camera: no steering, throttle, boost or levelling (the ship flies on).
            if (!useBuiltInInput || inputLocked) Model.Braking = false;
            Vector2 steer = useBuiltInInput && !inputLocked ? ReadInput() : Vector2.zero;
            if (!inputLocked && externalSteer.sqrMagnitude > steer.sqrMagnitude) steer = externalSteer;
            var mouseSteer = !inputLocked ? ReadMouseSteer() : Vector2.zero;
            if (mouseSteer.sqrMagnitude > steer.sqrMagnitude) steer = mouseSteer;
            // Remake: a motion controller's gyro (ControllerGyro); Level out re-centres it.
            if (useBuiltInInput && !inputLocked && GameControls.LevelOut.WasPressedThisFrame()) ControllerGyro.Recenter();
            var gyroSteer = useBuiltInInput && !inputLocked && autopilotTarget == null ? ControllerGyro.Steer(Time.timeScale > 0f ? Time.unscaledDeltaTime : 0f) : Vector2.zero;
            if (gyroSteer.sqrMagnitude > steer.sqrMagnitude) steer = gyroSteer;
            SteerInput = steer;
            if (autopilotTarget != null || steeringLocked) steer = Vector2.zero;

            // Model convention: +x = yaw left, +y = pitch down. Map "stick right = turn right".
            var model = new Vector2(invertYaw ? steer.x : -steer.x, invertPitch ? steer.y : -steer.y);

            var r = Model.Step(model, dtMs, transform.up, transform.right);

            if (autopilotTarget != null)
            {
                // PlayerEgo::moveToPosition 0xa8720: turn = min(handling + 2.7, 4), dir += (to - dir) * (int)(dt * turn) / 4096,
                // world up (the ship levels out, no roll).
                float turn = Mathf.Min(stats.handling / 100f + 0.2f * stats.handlingUpgrades + 2.7f, 4f);
                var to = (autopilotTarget() - transform.position).normalized;
                var dir = (transform.forward + (to - transform.forward) * ((int)(dtMs * turn) / 4096f)).normalized;
                if (dir.sqrMagnitude > 0f) transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
            }
            // Original: yaw positive = left. Unity yaw positive = right, so negate.
            else
            {
                transform.Rotate(r.pitchDeg, -r.yawDeg, r.rollDeg, Space.Self);
                // Remake: manual roll (keys 1 / 3, the touch Level button slid sideways); rolling ends an auto-level.
                float roll = inputLocked || steeringLocked ? 0f : Mathf.Clamp((useBuiltInInput ? GameControls.Roll.ReadValue<float>() : 0f) + touchRoll, -1f, 1f);
                if (Mathf.Abs(roll) > 0.01f)
                {
                    Model.StopLeveling();
                    transform.Rotate(0f, 0f, -roll * rollDegreesPerSecond * dtMs / 1000f, Space.Self);
                }
            }
            transform.position += transform.forward * (r.forwardUnits * metersPerUnit)
                                + transform.right * (r.sidePushUnits * metersPerUnit);
            // The PC version's held strafe (bindings 3350 / 3351 "Strafe left / right"): not while the controls are locked.
            // Remake: on the autopilot too (it slides the ship sideways; the autopilot keeps correcting the heading).
            if (useBuiltInInput && !inputLocked && !steeringLocked)
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

            UpdateVisualBank();
        }

        // The dodge bindings (the original: a touch swipe, FlightHud): GameControls' DodgeLeft / DodgeRight (no keyboard default:
        // A / D are the PC version's held strafe; the controller's right stick pushed left / right). A stick binding is left
        // alone in free look, where the right stick turns the camera.
        void ReadDodgeInput()
        {
            if (GameControls.DodgeLeft.WasPressedThisFrame() && !StickInFreeLook(GameControls.DodgeLeft)) RequestDodge(1);
            if (GameControls.DodgeRight.WasPressedThisFrame() && !StickInFreeLook(GameControls.DodgeRight)) RequestDodge(2);
        }

        bool StickInFreeLook(UnityEngine.InputSystem.InputAction a)
        {
            if (freeLook == null) freeLook = GetComponent<FreeLookCamera>();
            if (freeLook == null || !freeLook.FreeLookActive) return false;
            var c = a.activeControl;
            return c != null && c.path.Contains("Stick", System.StringComparison.OrdinalIgnoreCase);
        }

        bool brakeOverridden;

        Vector2 ReadInput()
        {
            float throttle = GameControls.Throttle.ReadValue<float>();
            if (Mathf.Abs(throttle) > 0.01f) Model.ChangeThrottle(throttle * throttleChangePerSecond * Time.deltaTime);
            // Brake (S): the engines stop while it is held (FlightModel.Braking); a boost overrides it until it is pressed again.
            bool brake = GameControls.Brake.IsPressed();
            if (!brake) brakeOverridden = false;
            if (brake && GameControls.Boost.WasPressedThisFrame()) brakeOverridden = true;
            Model.Braking = brake && !brakeOverridden;
            // The wheel: +- thrust, 10 % a notch (not while it zooms the free-look camera).
            var mouse = Mouse.current;
            if (freeLook == null) freeLook = GetComponent<FreeLookCamera>();
            if (mouse != null && (freeLook == null || !freeLook.FreeLookActive))
            {
                float wheel = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(wheel) > 0.01f) Model.ChangeThrottle(Mathf.Sign(wheel) * 0.1f);
            }
            if (GameControls.Boost.WasPressedThisFrame()) Model.Boost();
            if (GameControls.LevelOut.WasPressedThisFrame()) Model.AlignToHorizon();
            return Vector2.ClampMagnitude(GameControls.Steer.ReadValue<Vector2>(), 1f);
        }

        void UpdateVisualBank() => UpdateVisualBank(Model.VisualYawBank * bankDegreesPerH, Model.VisualPitchBank * pitchTiltDegreesPerH);

        void UpdateVisualBank(float targetBank, float targetTilt)
        {
            if (visualModel == null) return;
            float k = 1f - Mathf.Exp(-bankSmoothing * Time.deltaTime);
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
