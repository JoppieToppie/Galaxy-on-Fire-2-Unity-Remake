// ChaseCamera.cs
// Damped third-person chase camera in the spirit of GoF2's TargetFollowCamera.
//
// Recovered facts:
//  * Two damping coefficients (TargetFollowCamera::update 0x186e40): +0x12c eases the camera position, +0x128 the
//    look-at point. Fixed 0.006 / 0.005 (TargetFollowCamera::resetShipHandling) except with the mouse cursor (PlayerEgo
//    ::update, Globals::mouseCursorActivated), where they depend on handling h (setShipHandling):
//      position  0.01h * 0.011 + 0.001        look-at  (1 - 0.01h) * 0.015 + 0.003
//    The damping curve (aproximateCooefficientsForAproximationOfDampingFunktion: per coefficient x, a factor
//    k(dt) = c0 / dt + c1 + c2 dt + c3 dt^2 + c4 dt^3, the c's fitted polynomials in x) is 1 - e^(-x dt / 2) within 2 % for
//    dt up to 50 ms (Ease; ChaseCameraTests checks it against the polynomial).
//  * The camera looks at a point (+0x14, eased toward the ship's matrix x the target offset +0x2c) through
//    MatrixGetLookAt from where the camera really is; translateNoUpdate moves both. The dodge (PlayerEgo::updateManeuver)
//    shifts the target offset sideways by -3 x the yaw rate: with the look point only ~34 m ahead of the camera (offsets
//    (0, 600, -1338) / (0, 600, -650) units) the camera turns ~40 deg and keeps the sliding ship in view.
//  * Two follows (Graphics option "Chase camera", Settings.OriginalChaseCamera): Original (#114, the 2026.10.17 release)
//    = the above, Ease and the look point: in a turn the ship swings out to the side and the crosshair off centre. Remake
//    (default; players found #114 worse) = 2026.10.09's: 1 - e^(-x dt) (Follow, twice the rate) and a rotation slerped
//    toward the look-at point as seen from where the camera should be, during a dodge from where it is (aimed from where
//    it should be, the sliding ship left the frame).
//  * Boost (MGame::OnUpdate): the vertical FOV grows by 0.35 rad x the boost percentage, and
//    TargetFollowCamera::setBoostPercentage(pct, clamp(boost speed, 2, 8)) jitters the look-at point by
//    pct x n units per axis each frame. The radial blur on top (post effect 0x1400002): RadialBlurPass.
//  * The cockpit view (MGame::switchCamera mode 2, TargetFollowCamera::enableFirstPersonCam +0xf0, unreachable in this
//    build: switchCamera and nextCamId turn 2 into 3): the camera on the ship's matrix, looking along its direction, the
//    ship hidden (PlayerEgo::hideShipForFirstPersonCameraView), the hit shake x0.001 and the rumble x0.0015. Remake
//    (FreeLookCamera.Mode.FirstPerson): reachable, on the bow instead of the pivot (FirstPersonSeat), the shake and the
//    rumble as small turns of the view, the boost's FOV at half.
//  * The shake and the rumble draw a new random offset each frame of the original (OriginalFrame.Ms, 60 Hz). Remake (players' reports
//    2026-10-18, extreme shaking): each offset is held for that frame (HeldJitter) instead of a new one every rendered
//    frame (at 144 fps an explosion's rumble jolted the view 2.8 deg every 7 ms), and laid on top of the follow, taken
//    off again the next frame (UndoJitter): drawn into the eased pose, it piled up through the easing.
//  * Remake, free flight (Graphics "EVERSPACE 2 camera in free flight", Settings.Es2FreeFlightCamera, on by default):
//    EVERSPACE 2's rig instead of the follow (Es2ChaseRig, PlaceEs2): the camera swings round a pivot ahead of the ship
//    whose rotation lags the ship's (roll too), trails the velocity, and the boost widens the view by ES2's 15 deg.

using UnityEngine;

namespace GoF2Remake.Flight
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public class ChaseCamera : MonoBehaviour
    {
        /// <summary>TargetFollowCamera::update's damping for coefficient 'x' over a frame of 'dtMs': the share of the way to the
        /// goal (the original's fitted polynomial is 1 - e^(-x dt / 2) within 2 % up to 50 ms frames; frame-rate independent).</summary>
        public static float Ease(float x, float dtMs) => 1f - Mathf.Exp(-0.5f * x * dtMs);

        /// <summary>The remake's follow for coefficient 'x' over 'dtMs' (2026.10.09's): 1 - e^(-x dt), twice the original's
        /// rate (Ease); a tighter camera players preferred.</summary>
        public static float Follow(float x, float dtMs) => 1f - Mathf.Exp(-x * dtMs);

        public ShipController target;

        [Header("Placement (meters, ship local space)")]
        public Vector3 offset = new Vector3(0f, 2.5f, -9f);
        public Vector3 lookOffset = new Vector3(0f, 1f, 6f);

        [Header("Damping (per millisecond coefficients, like the original)")]
        public bool handlingDependent = false; // false = touch-mode constants
        public float positionCoefficient = 0.006f;
        public float rotationCoefficient = 0.005f;

        [Header("Turn offset: camera slides sideways while turning")]
        public float turnSlideMeters = 1.5f;
        [Tooltip("Share of the original's dodge target offset (-3 x the yaw rate, on the look-at point) the camera takes; 1 = as decoded.")]
        public float dodgeLookScale = 1f;
        [Tooltip("Share of the dodge's sideways move the camera takes back each frame (translateNoUpdate(-0.9 x the slide)).")]
        public float dodgeLag = 0.9f;

        [Header("Boost")]
        public float baseFov = 60f;
        [Tooltip("Degrees added at full boost: MGame::OnUpdate's 0.35 rad x the boost percentage.")]
        public float boostFovAdd = 20.05f;

        Camera cam;
        Vector3 slide;
        // The Original follow: TargetFollowCamera+0x14, the eased look-at point (world); invalid = start from the view.
        Vector3 lookPoint;
        bool lookValid;
        float shakeMs, shakeUnits, rumble;
        bool wasBoosting;
        Vector3 lastShipPosition;
        bool cruiseTracked;
        // The shake / rumble on top of the follow (header): the held random directions, the pose without them and the one
        // shown with them.
        Vector3 shakeDir, rumbleDir;
        float jitterAgeMs = JitterMs;
        Vector3 basePos, shownPos;
        Quaternion baseRot, shownRot;
        bool jittered;
        // EVERSPACE 2's rig (PlaceEs2): the pivot's lagged rotation, the ship's rotation it last chased, the pivot's place in
        // the ship's frame (metres), the boost's and the cruise's eased extra FOV (horizontal degrees); invalid = start anew.
        bool es2Valid;
        Quaternion es2Rot, es2LastTarget;
        Vector3 es2Pivot;
        float es2BoostFov, es2CruiseFov;

        /// <summary>The original's frame (OriginalFrame.Ms, 60 Hz): a shake or rumble offset lasts this long.</summary>
        public const float JitterMs = OriginalFrame.Ms;

        /// <summary>A random direction in [-1, 1] per axis, drawn again once 'ageMs' reaches JitterMs (the original's frame)
        /// rather than every rendered frame. Shared with the cutscene camera's rumble.</summary>
        public static Vector3 HeldJitter(ref Vector3 dir, ref float ageMs, float dtMs)
        {
            ageMs += dtMs;
            if (ageMs >= JitterMs || dir == Vector3.zero)
            {
                ageMs = ageMs >= 2f * JitterMs ? 0f : ageMs - JitterMs;
                dir = new Vector3(Random.Range(-1f, 1f), Random.Range(-1f, 1f), Random.Range(-1f, 1f));
            }
            return dir;
        }

        /// <summary>Last frame's shake / rumble comes off before the follow eases on (unless something else has moved the
        /// camera since: a snap, another camera).</summary>
        void UndoJitter()
        {
            if (jittered && transform.position == shownPos && transform.rotation == shownRot)
                transform.SetPositionAndRotation(basePos, baseRot);
            jittered = false;
        }

        void BeginJitter()
        {
            basePos = transform.position;
            baseRot = transform.rotation;
        }

        void EndJitter()
        {
            shownPos = transform.position;
            shownRot = transform.rotation;
            jittered = shownPos != basePos || shownRot != baseRot;
        }

        /// <summary>The rumble as a turn of the view: the look-at 'look' moved by 'jitter' (metres), seen from the camera.</summary>
        void TurnBy(Vector3 look, Vector3 jitter)
        {
            var from = look - transform.position;
            if (from.sqrMagnitude < 1e-6f) return;
            transform.rotation = Quaternion.FromToRotation(from, from + jitter) * transform.rotation;
        }

        /// <summary>TargetFollowCamera::hit / hitSmall: the camera position jitters rnd(2a) - a units per axis per frame
        /// ('ms' 1000 / a 6 when the player is hit, 50 / 2 per own shot).</summary>
        public void Shake(float ms, float amplitudeUnits)
        {
            // EVERSPACE 2's camera: a hit or a collision (the 6-unit shake) is BP_CamShake_Hit_001 at 0.5; ES2's own shots
            // don't shake the camera
            if (Es2Mode) { if (amplitudeUnits >= 4f) es2HitPending = true; return; }
            if (ms >= shakeMs) { shakeMs = ms; shakeUnits = amplitudeUnits; }
        }

        /// <summary>TargetFollowCamera::setRumblePercentage (explosions, each frame): the look-at point jitters by
        /// p * (rnd(100) - 50) units per axis. The strongest caller this frame wins.</summary>
        public void Rumble(float p) => rumble = Mathf.Max(rumble, p);

        /// <summary>TargetFollowCamera on another object (the Liberator; the turret view): the camera sits at
        /// follow.TransformPoint(followOffset) looking at follow.TransformPoint(followLookOffset), world up unless
        /// followUsesUp; 'followRigid' snaps instead of easing. Null = behind the ship.</summary>
        [System.NonSerialized] public Transform follow;
        [System.NonSerialized] public Vector3 followOffset, followLookOffset;
        [System.NonSerialized] public bool followRigid, followUsesUp;
        /// <summary>Modified: moves the eased 'follow' position (pivot = follow's position, the camera's place) clear of what
        /// it mustn't enter (FreeLookCamera.ClampCamera: the docked object's hull). Null = none.</summary>
        [System.NonSerialized] public System.Func<Vector3, Vector3, Vector3> followClamp;
        /// <summary>A constant rumble on top (PlayerEgo::update: 0.2 while the Liberator is steered).</summary>
        [System.NonSerialized] public float constantRumble;
        /// <summary>A level script's look-at camera (CutsceneCamera) holds the camera: the launch / arrival camera leaves it
        /// alone and doesn't hand it back (LevelScript: M0 / M1 never end the fly-in, their scripts own the camera).</summary>
        [System.NonSerialized] public bool scriptCamera;
        /// <summary>Remake: the cockpit view (FreeLookCamera.Mode.FirstPerson): rigidly on the bow with the hull hidden while
        /// this camera drives (not while 'follow' or another camera holds it: the hull shows again then).</summary>
        [System.NonSerialized] public bool firstPerson;
        /// <summary>The cockpit view is on screen now (the hull hidden).</summary>
        public bool FirstPersonShown => fpShown;
        static ChaseCamera fpActive;

        /// <summary>Where the aim line along the ship's nose starts for the HUD's crosshair and the lock boxes (FlightHud,
        /// CombatRadar, Mining, Navigation, the beams' auto-aim; 22000 units ahead): the ship's pivot, or the cockpit camera
        /// while it shows (on the same line: a big hull's bow camera sits past the pivot's aim point, which hid the crosshair).</summary>
        public static Vector3 AimOrigin(Transform ship) =>
            fpActive != null && fpActive.fpShown && fpActive.target != null && fpActive.target.transform == ship ? fpActive.transform.position : ship.position;

        public static Vector3 AimDirection(Transform ship) =>
            fpActive != null && fpActive.fpShown && fpActive.target != null && fpActive.target.transform == ship ? fpActive.transform.forward : ship.forward;

        void Awake() => cam = GetComponent<Camera>();
        void OnEnable() { lookValid = false; cruiseTracked = false; es2Valid = false; }
        // Disabled = another camera holds it (the launch / arrival fly-in, a cutscene, a jump, mining, object docking, the
        // death camera, Action Freeze): the hull shows; enabled again, the cockpit view comes back.
        void OnDisable()
        {
            bool wasShown = fpShown;
            SetHullHidden(false);
            EndFirstPerson();
            // Some of them leave the camera where it is (the death camera, mining's landing, the planet jump): from behind the
            // ship as in the standard view, not inside the bow of a tumbling or pitching hull.
            if (!wasShown || target == null || follow != null) return;
            PlaceBehind();
            if (cam != null) cam.fieldOfView = GoF2Remake.Visuals.Aspect.VerticalFov(baseFov, cam.aspect);
        }
        void OnDestroy() { SetHullHidden(false); EndFirstPerson(); }

        void LateUpdate()
        {
            Place();
            ApplyGlide();
        }

        // Remake (2026-11-13, Chorus's Rite of the Hunt: BP_ActivePower_Blink's 1 s camera timeline, its RotationAlpha and
        // CameraLocationAlpha, bIgnoreTimeDilation): after a leap the camera comes over from where it was to its place
        // behind the ship, fast at first and settling (the curves read as an ease-out, 1 - (1 - t)^3), on real time.
        Vector3 glideFromPos; Quaternion glideFromRot; float glideMs, glideTotalMs;

        /// <summary>The camera glides from 'fromPos' / 'fromRot' to its own place over 'ms' (real time). Snap ends it.</summary>
        public void Glide(Vector3 fromPos, Quaternion fromRot, float ms)
        {
            glideFromPos = fromPos;
            glideFromRot = fromRot;
            glideMs = glideTotalMs = Mathf.Max(1f, ms);
        }

        /// <summary>The glide's share of the way at 'u' (0..1) of its time.</summary>
        public static float GlideAlpha(float u) { u = Mathf.Clamp01(u); float r = 1f - u; return 1f - r * r * r; }

        void ApplyGlide()
        {
            if (glideMs <= 0f) return;
            if (Time.timeScale > 0f) glideMs = Mathf.Max(0f, glideMs - Time.unscaledDeltaTime * 1000f);
            float a = GlideAlpha(1f - glideMs / glideTotalMs);
            transform.SetPositionAndRotation(Vector3.Lerp(glideFromPos, transform.position, a), Quaternion.Slerp(glideFromRot, transform.rotation, a));
        }

        void Place()
        {
            if (target == null) return;
            var ship = target.transform;
            var model = target.Model;
            float dtMs = Time.deltaTime * 1000f * TimeExtender.PlayerFactor;   // TargetFollowCamera gets the player's dt
            UndoJitter();
            // TargetFollowCamera::update 0x186e40 does everything inside `if (0 < dt)`: paused, the camera neither follows,
            // shakes nor rumbles (the shake timer would never run out, the jitter piled up every frame).
            if (dtMs <= 0f) { rumble = 0f; return; }
            HeldJitter(ref shakeDir, ref jitterAgeMs, dtMs);
            rumbleDir = new Vector3(shakeDir.y, shakeDir.z, shakeDir.x);   // drawn with it, its own mix of the axes
            if (constantRumble > 0f) rumble = Mathf.Max(rumble, constantRumble);
            // Remake: the same rumble on the controller / phone (Haptics; its own option, not the camera shake's): explosions,
            // the Liberator, the boost below; the boost's start as a pulse.
            bool boosting = model.LooksBoosting;   // the multiplayer cruise mode too (FlightModel.CruiseVisual)
            if (boosting && !wasBoosting) Haptics.Play(Haptics.Boost);
            wasBoosting = boosting;
            float boostRumble = boosting ? model.BoostVisualPercent * Mathf.Clamp(model.CurrentSpeed, 2f, 8f) / 50f : 0f;
            // Remake debug: a heavy hull's engines (ShipController.mass): how hard they work = the gap between the speed and
            // the one the throttle asks for (FlightModel.MoveSpeed eases toward it), plus the boost.
            float mass = target.mass;
            float strain = mass > 0f ? Mathf.Clamp01(Mathf.Abs((model.Braking ? 0f : model.Throttle * model.CurrentSpeed) - model.MoveSpeed) / FlightModel.BaseSpeed) : 0f;
            float engineRumble = mass * (0.06f + 0.25f * strain + 0.3f * model.BoostVisualPercent);
            Haptics.Rumble(Mathf.Max(rumble, Mathf.Max(boostRumble, engineRumble)));
            rumble *= GoF2Remake.Data.Settings.CameraShake;   // the camera shake option
            // EVERSPACE 2's camera (free flight): its chase views and its own free look, blended over them
            if (Es2Mode && !firstPerson && (follow == null || es2FreeLook))
            {
                EndFirstPerson();
                PlaceEs2(ship, model, dtMs, boosting, boostRumble);
                return;
            }
            SetHullHidden(false);
            if (follow != null)
            {
                var fp = follow.TransformPoint(followOffset);
                var fr = Quaternion.LookRotation(follow.TransformPoint(followLookOffset) - fp, followUsesUp ? follow.up : Vector3.up);
                float k = followRigid ? 1f : 1f - Mathf.Exp(-0.01f * dtMs);
                transform.position = Vector3.Lerp(transform.position, fp, k);
                transform.rotation = Quaternion.Slerp(transform.rotation, fr, k);
                if (followClamp != null) transform.position = followClamp(follow.position, transform.position);
                BeginJitter();
                if (rumble > 0f)
                {
                    float j = rumble * 0.6f;
                    transform.rotation *= Quaternion.Euler(rumbleDir.x * j, rumbleDir.y * j, 0f);
                    rumble = 0f;
                }
                EndJitter();
                if (cam != null) cam.fieldOfView = GoF2Remake.Visuals.Aspect.VerticalFov(baseFov, cam.aspect);
                lookValid = false;
                es2Valid = false;
                EndFirstPerson();   // the turret view, free look, the Liberator: the hull shows
                return;
            }

            // VR: the camera is the pilot's seat (Vr.VrCockpit), rigidly with the ship: no lag, slide, shake or boost zoom.
            if (GoF2Remake.Data.VrFlags.Enabled)
            {
                transform.SetPositionAndRotation(ship.TransformPoint(GoF2Remake.Data.VrFlags.CockpitSeat), ship.rotation);
                rumble = 0f;
                shakeMs = 0f;
                if (cam != null) cam.fieldOfView = GoF2Remake.Visuals.Aspect.VerticalFov(baseFov, cam.aspect);
                lookValid = false;
                es2Valid = false;
                return;
            }

            if (firstPerson) { es2Valid = false; PlaceFirstPerson(ship, dtMs, boosting, boostRumble, mass); return; }
            EndFirstPerson();
            es2Valid = false;

            Coefficients(out float posK, out float rotK);
            float speedup = model.FreeTurnSpeedup;

            // Slide opposite to the turn, proportional to turn rate (approximation of the target offset); free flight's
            // faster rates no further than the original's full stick.
            float maxRate = Mathf.Max(1f, 750f * model.Handling / 63f) * speedup;
            Vector3 targetSlide = new Vector3(-model.YawRate / maxRate * turnSlideMeters,
                                              model.PitchRate / maxRate * turnSlideMeters * 0.5f, 0f);
            // PlayerEgo::updateManeuver: during a dodge the target offset's x is -3 x the yaw rate (game units; the game's
            // +x is the ship's left, the remake's -x), y and z kept: the look-at point leads the slide. The camera's own
            // offset stays (no turn slide meanwhile); LevelScript::resetCamera puts the target offset back at the end.
            Vector3 lookShift = Vector3.zero;
            if (target.Maneuver.Active)
            {
                targetSlide = Vector3.zero;
                lookShift = new Vector3(3f * dodgeLookScale * model.YawRate * target.metersPerUnit, 0f, 0f);
            }
            bool returning = returnLookMs > 0f;   // Modified: ReturnLookingAt
            if (returning) returnLookMs = Mathf.Max(0f, returnLookMs - dtMs);
            bool original = GoF2Remake.Data.Settings.OriginalChaseCamera || returning;
            float posEase = original ? Ease(posK, dtMs) : Follow(posK, dtMs), rotEase = original ? Ease(rotK, dtMs) : Follow(rotK, dtMs);
            if (easeInMs > 0f)   // Modified: SoftReturn
            {
                easeInMs = Mathf.Max(0f, easeInMs - dtMs);
                float f = Mathf.Lerp(0.06f, 1f, Mathf.SmoothStep(0f, 1f, 1f - easeInMs / Mathf.Max(1f, easeInTotalMs)));
                posEase *= f;
                rotEase *= f;
            }
            slide = Vector3.Lerp(slide, targetSlide, posEase);

            // Remake: a cruise's extra speed is carried, so the follow trails as at the ship's own speed: its lag grows with the
            // speed, and at a cruise drive's x8 the ship shrank to a dot far ahead (EVERSPACE 2's stays where it is).
            float cruise = Navigation.CruiseFactor;
            if (cruiseTracked && cruise > 1f)
            {
                var extra = (ship.position - lastShipPosition) * (1f - 1f / cruise);
                transform.position += extra;
                lookPoint += extra;
            }
            lastShipPosition = ship.position;
            cruiseTracked = true;

            Vector3 desiredPos = ship.TransformPoint(offset + slide);
            Vector3 desiredLook = ship.TransformPoint(lookOffset + lookShift);
            transform.position = Vector3.Lerp(transform.position, desiredPos, posEase);
            // ... and translateNoUpdate(-0.9 x the slide) every frame: the camera takes back most of the sideways move, so
            // the ship slides across the view and the camera turns after it.
            var noUpdate = -dodgeLag * target.ManeuverSlide + target.StrafeSlide;   // handleShip: the strafe too
            transform.position += noUpdate;
            if (original)
            {
                // A new start (the chase taking over from a fly-in or another camera, the option switched) or a jump: the look
                // point starts where the camera looks now, at the ship's distance (no turn in one frame).
                if (!lookValid || (!returning && (desiredLook - lookPoint).sqrMagnitude > 1000f * 1000f))
                    lookPoint = transform.position + transform.forward * (desiredLook - transform.position).magnitude;
                lookValid = true;
                lookPoint = Vector3.Lerp(lookPoint, desiredLook, rotEase) + noUpdate;
                // MatrixGetLookAt(position, look-at point, the ship's up), from where the camera is.
                var toPoint = lookPoint - transform.position;
                if (toPoint.sqrMagnitude > 1e-6f) transform.rotation = Quaternion.LookRotation(toPoint, ship.up);
            }
            else
            {
                lookValid = false;
                // The rotation eases toward the look-at point as seen from where the camera should be; during a dodge from
                // where it is (MatrixGetLookAt), so it turns after the sliding ship.
                var toLook = desiredLook - (target.Maneuver.Active ? transform.position : desiredPos);
                if (toLook.sqrMagnitude > 1e-6f)
                    transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(toLook, ship.up), rotEase);
            }

            BeginJitter();
            if (shakeMs > 0f)
            {
                shakeMs -= dtMs;
                float a = shakeUnits * target.metersPerUnit * GoF2Remake.Data.Settings.CameraShake;
                transform.position += shakeDir * a;
                // The Original follow looks at its point from where the camera is: the shake turns the view a little too.
                if (original && lookValid && (lookPoint - transform.position).sqrMagnitude > 1e-6f)
                    transform.rotation = Quaternion.LookRotation(lookPoint - transform.position, ship.up);
            }
            if (boosting)
                rumble = Mathf.Max(rumble, boostRumble * GoF2Remake.Data.Settings.CameraShake);
            if (rumble > 0f)
            {
                // 50 units at the look-at point (about 690 units away) = about 4 degrees.
                TurnBy(lookValid ? lookPoint : ship.TransformPoint(lookOffset), rumbleDir * (rumble * 50f * target.metersPerUnit));
                rumble = 0f;
            }
            EndJitter();

            if (cam != null)   // a heavy hull's boost pushes the view out less (it gathers speed slowly anyway)
                cam.fieldOfView = GoF2Remake.Visuals.Aspect.VerticalFov(baseFov + boostFovAdd * model.BoostVisualPercent * Mathf.Lerp(1f, 0.5f, mass), cam.aspect);
        }

        /// <summary>The follow's position / look coefficients this frame (per ms; LateUpdate).</summary>
        void Coefficients(out float posK, out float rotK)
        {
            var model = target.Model;
            posK = positionCoefficient;
            rotK = rotationCoefficient;
            if (handlingDependent)
            {
                float h = model.Handling;
                posK = 0.01f * h * 0.011f + 0.001f;
                rotK = (1f - 0.01f * h) * 0.015f + 0.003f;
            }
            float mass = target.mass;
            if (mass > 0f) { posK *= Mathf.Lerp(1f, 0.35f, mass); rotK *= Mathf.Lerp(1f, 0.35f, mass); }   // a heavy, unhurried follow
            // Remake, free flight: it turns several times faster than the original (an Inflict 127 deg/s against 54), so the
            // camera follows that much faster and the ship trails a turn by the original's angle (players' report: at full
            // deflection it swung to the edge of the picture and out).
            float speedup = model.FreeTurnSpeedup;
            posK *= speedup;
            rotK *= speedup;
        }

        /// <summary>Modified: where this camera settles behind a ship flying straight at 'velocity' (Unity m per ms): the
        /// standard view's place trailing by the follow's lag (a lerp by k a frame toward a point moving v dt a frame settles
        /// (1 - k) / k x v dt behind it), looking as it would. A camera that hands over here goes on without a jump (the
        /// Supernova Challenge's fly-in).</summary>
        public void SteadyPose(Vector3 velocity, float dtMs, out Vector3 position, out Quaternion rotation)
        {
            var ship = target.transform;
            dtMs = Mathf.Max(dtMs, 0.1f);
            if (Es2Active)
            {
                // flying straight: the pivot's rotation on the ship's, the pivot settled at its velocity offset
                var r = Es2Rig();
                var pivot = Es2ChaseRig.PivotGoal(Quaternion.Inverse(ship.rotation) * velocity * 1000f, r.pivot, r.scale);
                rotation = ship.rotation;
                position = Es2ChaseRig.CameraPosition(ship.position + ship.rotation * pivot, rotation, r.pivot, r.behind, r.height);
                return;
            }
            Coefficients(out float posK, out float rotK);
            bool original = GoF2Remake.Data.Settings.OriginalChaseCamera;
            float kp = original ? Ease(posK, dtMs) : Follow(posK, dtMs), kr = original ? Ease(rotK, dtMs) : Follow(rotK, dtMs);
            Vector3 desiredPos = ship.TransformPoint(offset), desiredLook = ship.TransformPoint(lookOffset);
            position = desiredPos - velocity * ((1f - kp) / Mathf.Max(kp, 1e-4f) * dtMs);
            var look = original ? desiredLook - velocity * ((1f - kr) / Mathf.Max(kr, 1e-4f) * dtMs) : desiredLook;
            var from = original ? position : desiredPos;
            var dir = look - from;
            rotation = dir.sqrMagnitude > 1e-8f ? Quaternion.LookRotation(dir, ship.up) : ship.rotation;
        }

        /// <summary>Snap behind the ship (use after spawning / undocking).</summary>
        /// <summary>Modified: the follow comes back gently over 'ms' (its easing slowed to 6 % at first, back to normal by the
        /// end): the camera gliding over to its place behind the ship from where another view left it (the undock, a
        /// player's request: the plain follow got there too briskly). Snap cancels it.</summary>
        public void SoftReturn(float ms) { easeInMs = easeInTotalMs = ms; }
        float easeInMs, easeInTotalMs;

        /// <summary>Modified: a cutscene handing the camera back where it is (the prologue's fight, LevelScript's
        /// setLookAtCam(false) + setTarget(player), no setPosition): TargetFollowCamera eases its look point too, from what
        /// the cutscene looked at, so the view stays on the pirates and swings over to the ship as the camera glides in
        /// (the original's frames, the user's capture). For 'ms' the follow is the Original one (Ease, the eased look
        /// point) whatever the option, starting from 'from', under SoftReturn's slow start.</summary>
        public void ReturnLookingAt(Vector3 from, float ms)
        {
            SoftReturn(ms);
            lookPoint = from;
            lookValid = true;
            returnLookMs = ms;
        }
        float returnLookMs;

        public void Snap()
        {
            easeInMs = 0f;
            glideMs = 0f;
            returnLookMs = 0f;
            if (target == null) return;
            var ship = target.transform;
            PlaceBehind();
            if (GoF2Remake.Data.VrFlags.Enabled) transform.SetPositionAndRotation(ship.TransformPoint(GoF2Remake.Data.VrFlags.CockpitSeat), ship.rotation);
            else if (firstPerson && follow == null) transform.SetPositionAndRotation(ship.TransformPoint(FirstPersonSeat()), ship.rotation);
            else if (Es2Active)
            {
                StartEs2(ship.rotation, Es2Velocity(0f));
                var r = Es2Rig();
                transform.SetPositionAndRotation(Es2ChaseRig.CameraPosition(ship.position + ship.rotation * es2Pivot, es2Rot, r.pivot, r.behind, r.height), es2Rot);
            }
        }

        /// <summary>Remake: the view an arrival at speed flew in with, in the ship's frame (the follow's lag), after Snap.</summary>
        public void CarryView(Vector3 local, Quaternion turn)
        {
            if (target == null || firstPerson || GoF2Remake.Data.VrFlags.Enabled) return;
            var ship = target.transform;
            transform.SetPositionAndRotation(ship.TransformPoint(local), ship.rotation * turn);
            lookValid = false;
            es2Valid = false;
            lastShipPosition = ship.position;
            cruiseTracked = true;
        }

        /// <summary>The standard view's place behind the ship, at once.</summary>
        void PlaceBehind()
        {
            var ship = target.transform;
            transform.position = ship.TransformPoint(offset);
            transform.rotation = Quaternion.LookRotation(ship.TransformPoint(lookOffset) - transform.position, ship.up);
            lookPoint = ship.TransformPoint(lookOffset);
            lookValid = true;
            slide = Vector3.zero;
        }

        // ---- EVERSPACE 2's camera (remake, free flight) -------------------------------------------------------------

        /// <summary>Free flight with "EVERSPACE 2 camera in free flight" on (not in VR): ES2's views, free look and shakes.</summary>
        public bool Es2Mode => target != null && target.FreeFlightActive && GoF2Remake.Data.Settings.Es2FreeFlightCamera
                               && !GoF2Remake.Data.VrFlags.Enabled;
        /// <summary>ES2's chase view places the camera now (no other camera holding this one, not the cockpit).</summary>
        bool Es2Active => Es2Mode && follow == null && !firstPerson;

        /// <summary>ES2's close view (CurrentCameraView 1, SpringArmTPClose) instead of the far one; FreeLookCamera's cycle.</summary>
        [System.NonSerialized] public bool es2Close;
        /// <summary>FreeLookCamera: free look is ES2's on the 'follow' anchor (rigid with the ship, blended in and out over
        /// LookBlendRate while the chase rig keeps running underneath).</summary>
        [System.NonSerialized] public bool es2FreeLook;
        Transform es2LookAnchor;
        Vector3 es2LookOffset;
        float es2LookBlend;   // 0 the chase view .. 1 free look (FreelookBlendTimeine)

        /// <summary>The rig's scale: the hull's chase distance over ES2's far arm (Es2Rig).</summary>
        public float Es2Scale => Mathf.Max(0.5f, -offset.z) / (Es2ChaseRig.ArmCm / 100f);
        /// <summary>How far the hull reaches from its pivot (metres; free look's closest zoom adds ES2's 2 m to it).</summary>
        public float Es2HullReach => hullBoundsKnown ? hullBounds.extents.magnitude : 0f;

        /// <summary>ES2's rig at this hull's size (metres): the far view's ship-to-camera distance and height are the hull's
        /// chase offset (PlayerHull sizes it); the close view's ES2's shorter arm and lower socket in proportion; the pivot ES2's
        /// PivotX at that scale ahead; the arm then dollied for the Field of view option (UpdateFOV).</summary>
        (float scale, float pivot, float behind, float height) Es2Rig()
        {
            float scale = Es2Scale, far = scale * Es2ChaseRig.ArmCm / 100f;
            float behind = es2Close ? far * Es2ChaseRig.CloseArmCm / Es2ChaseRig.ArmCm : far;
            float height = offset.y * (es2Close ? Es2ChaseRig.CloseSocketCm / Es2ChaseRig.SocketCm : 1f);
            behind = Es2ChaseRig.DollyArm(behind, GoF2Remake.Data.Settings.OriginalFov, baseFov);
            return (scale, Es2ChaseRig.PivotCm / 100f * scale, behind, height);
        }

        /// <summary>The ship's velocity (world, m/s) for the rig: free flight's own, plus a dodge's slide this frame.</summary>
        Vector3 Es2Velocity(float dtS)
        {
            var v = target.Model.FreeVelocity * (target.metersPerUnit * 1000f);
            if (dtS > 0f) v += target.ManeuverSlide / dtS;
            return v;
        }

        /// <summary>The rig starts at 'rotation' (the view it takes over, no turn in one frame), its pivot settled at the
        /// velocity's offset.</summary>
        void StartEs2(Quaternion rotation, Vector3 velocity)
        {
            var ship = target.transform;
            var r = Es2Rig();
            es2Rot = rotation;
            es2LastTarget = ship.rotation;
            es2Pivot = Es2ChaseRig.PivotGoal(Quaternion.Inverse(ship.rotation) * velocity, r.pivot, r.scale);
            es2BoostFov = es2CruiseFov = 0f;
            es2LookBlend = 0f;
            es2LookAnchor = null;
            es2Valid = true;
            slide = Vector3.zero;
            lookValid = false;
        }

        /// <summary>Where the chase rig puts the camera this frame (advancing its lag and pivot): BP_Ship_Player's
        /// UpdateCameraRotationLag, the arm, then the arm's collision probe from its root.</summary>
        void Es2Pose(Transform ship, float dtMs, out Vector3 pos, out Quaternion rot, out Vector3 pivotWorld, out Vector3 velocity)
        {
            float dtS = dtMs / 1000f;
            velocity = Es2Velocity(dtS);
            if (!es2Valid) StartEs2(transform.rotation, velocity);
            var r = Es2Rig();
            // the lag's frame cap goes with fast-forward (TimeExtender.PlayerFactor scales dtMs), so a sped-up frame doesn't
            // leave the view further behind than a normal one
            float factor = Mathf.Max(1f, TimeExtender.PlayerFactor);
            es2Rot = Es2ChaseRig.LagRotation(es2Rot, es2LastTarget, ship.rotation, dtS, Mathf.Pow(10f, Es2ChaseRig.Stiffness), Es2ChaseRig.MaxDt * factor);
            es2LastTarget = ship.rotation;
            var goal = Es2ChaseRig.PivotGoal(Quaternion.Inverse(ship.rotation) * velocity, r.pivot, r.scale);
            es2Pivot = Es2ChaseRig.InterpTo(es2Pivot, goal, dtS, Es2ChaseRig.OffsetSpeed);
            pivotWorld = ship.position + ship.rotation * es2Pivot;
            pos = Es2ChaseRig.CameraPosition(pivotWorld, es2Rot, r.pivot, r.behind, r.height);
            rot = es2Rot;
            var root = pivotWorld + es2Rot * new Vector3(0f, 0f, -r.pivot);   // the arm's root, at the ship
            pos = Probe(root, pos, Es2ChaseRig.ProbeCm / 100f * r.scale, ship, 0.5f);
        }

        /// <summary>BP_Ship_Player's CameraDynamicsTick for the third-person views (Es2ChaseRig): the chase rig, ES2's free look
        /// blended over it, ES2's shakes (or the remake's rumble), the ship hidden with the camera inside it, ES2's FOV.</summary>
        void PlaceEs2(Transform ship, FlightModel model, float dtMs, bool boosting, float boostRumble)
        {
            float dtS = dtMs / 1000f;
            Es2Pose(ship, dtMs, out var pos, out var rot, out var pivotWorld, out var velocity);
            bool looking = follow != null && es2FreeLook;
            if (looking) { es2LookAnchor = follow; es2LookOffset = followOffset; }
            // the blend runs on real time (the timeline isn't the ship's: fast-forward doesn't hurry it)
            es2LookBlend = Mathf.MoveTowards(es2LookBlend, looking ? 1f : 0f, Time.unscaledDeltaTime * Es2ChaseRig.LookBlendRate);
            if (es2LookBlend > 0f && es2LookAnchor != null)
            {
                // ES2's free look: the arm on the pivot at the ship, rigid with it, the camera looking along it at the ship
                var lp = Probe(es2LookAnchor.position, es2LookAnchor.TransformPoint(es2LookOffset), Es2ChaseRig.ProbeCm / 100f * Es2Scale, ship, 0.5f);
                float e = Es2ChaseRig.EaseInOut(es2LookBlend, Es2ChaseRig.LookBlendExp);
                pos = Vector3.Lerp(pos, lp, e);
                rot = Quaternion.Slerp(rot, es2LookAnchor.rotation, e);
            }
            else es2LookAnchor = null;
            if (easeInMs > 0f)   // Modified: SoftReturn, gliding over from where another view left the camera
            {
                easeInMs = Mathf.Max(0f, easeInMs - dtMs);
                float f = Mathf.Lerp(0.06f, 1f, Mathf.SmoothStep(0f, 1f, 1f - easeInMs / Mathf.Max(1f, easeInTotalMs)));
                float k = 1f - Mathf.Pow(1f - f, dtMs / OriginalFrame.Ms);
                pos = Vector3.Lerp(transform.position, pos, k);
                rot = Quaternion.Slerp(transform.rotation, rot, k);
            }
            transform.SetPositionAndRotation(pos, rot);
            lastShipPosition = ship.position;
            cruiseTracked = true;

            BeginJitter();
            Es2Shakes(ship, model, dtMs, velocity, pivotWorld, looking, false);
            EndJitter();
            Es2HideHull(ship, transform.position);

            // UpdateBoostCameraBehavior / UpdateFOV: the boost's 15 deg while boosting the way the camera looks, the cruise's 20
            float along = velocity.sqrMagnitude > 1e-6f ? Mathf.Max(0f, Vector3.Dot(transform.forward, velocity.normalized)) : 0f;
            es2BoostFov = Es2ChaseRig.InterpTo(es2BoostFov, model.IsBoosting ? Es2ChaseRig.BoostFov * along : 0f, dtS, Es2ChaseRig.FovSpeed);
            es2CruiseFov = Es2ChaseRig.InterpTo(es2CruiseFov, model.CruiseVisual > 0f ? Es2ChaseRig.CruiseFov : 0f, dtS, Es2ChaseRig.FovSpeed);
            if (cam != null)
                cam.fieldOfView = GoF2Remake.Visuals.Aspect.VerticalFov(Es2ChaseRig.WidenVertical(baseFov, es2BoostFov + es2CruiseFov), cam.aspect);
        }

        // ES2's shakes (Es2CameraShake): the assets, the eased idle and sway intensities, the boost / cruise edges, a hit asked
        // for this frame, the last velocity (the sway's acceleration).
        readonly Es2CameraShake es2Idle = Es2CameraShake.IdleFlight(), es2Boost = Es2CameraShake.Boost(), es2BoostInit = Es2CameraShake.BoostInit(),
            es2Hit = Es2CameraShake.Hit(), es2CruiseStart = Es2CameraShake.CruiseStart(), es2CruiseRumble = Es2CameraShake.CruiseRumble(),
            es2Sway = Es2CameraShake.AccelerationSway();
        float es2IdleI, es2SwayI;
        bool es2HitPending, es2WasBoosting, es2WasCruising;
        Vector3 es2LastVelocity;

        /// <summary>BP_Ship_Player's per-frame shakes (UpdateIdleFlightCameraShake, UpdateCameraAccelerationShake in the cockpit,
        /// UpdateBoostCameraBehavior, OnBoostStart, the cruise's start and rumble, a hit) through LIB_F_General.PlayCameraShake
        /// (x the ship class's scale and the Camera shake option), then their sum laid on the camera (local: ES2's play space).
        /// Explosions keep the remake's rumble (ES2's player has no shake of its own for them).</summary>
        void Es2Shakes(Transform ship, FlightModel model, float dtMs, Vector3 velocity, Vector3 rumbleAt, bool looking, bool cockpit)
        {
            float dtS = dtMs / 1000f;
            float s = GoF2Remake.Data.Settings.CameraShake * Es2CameraShake.ClassScale(target.mass);
            // the idle shake: the speed's share of 10000 cm/s, eased at 1; nothing in free look
            if (looking) es2IdleI = 0f;
            else
            {
                es2IdleI = Es2ChaseRig.InterpTo(es2IdleI, Mathf.Clamp01(velocity.magnitude / 100f), dtS, 1f);
                es2Idle.Play(es2IdleI * s);
            }
            // the acceleration sway, the cockpit only: the acceleration's share of 10000 cm/s^2, eased at 8
            if (cockpit && dtS > 0f)
            {
                es2SwayI = Es2ChaseRig.InterpTo(es2SwayI, Mathf.Clamp01((velocity - es2LastVelocity).magnitude / dtS / 100f), dtS, 8f);
                es2Sway.Play(es2SwayI * s);
            }
            else es2SwayI = 0f;
            es2LastVelocity = velocity;
            if (model.IsBoosting) es2Boost.Play(s);
            if (model.IsBoosting && !es2WasBoosting) es2BoostInit.Play(s);
            es2WasBoosting = model.IsBoosting;
            bool cruising = model.CruiseVisual > 0f;
            if (cruising && !es2WasCruising) es2CruiseStart.Play(s);
            if (cruising) es2CruiseRumble.Play(0.15f * s);
            es2WasCruising = cruising;
            if (es2HitPending) { es2Hit.Play(0.5f * s); es2HitPending = false; }

            Vector3 rot = Vector3.zero, loc = Vector3.zero;
            es2Idle.Tick(dtS, cockpit, ref rot, ref loc);
            es2Sway.Tick(dtS, cockpit, ref rot, ref loc);
            es2Boost.Tick(dtS, cockpit, ref rot, ref loc);
            es2BoostInit.Tick(dtS, cockpit, ref rot, ref loc);
            es2Hit.Tick(dtS, cockpit, ref rot, ref loc);
            es2CruiseStart.Tick(dtS, cockpit, ref rot, ref loc);
            es2CruiseRumble.Tick(dtS, cockpit, ref rot, ref loc);
            // cm at ES2's ship size -> metres at this hull's; UE pitch up / roll right -> Unity's
            transform.position += transform.rotation * (loc * (0.01f * Es2Scale));
            transform.rotation *= Quaternion.Euler(-rot.x, rot.y, -rot.z);
            shakeMs = 0f;
            if (rumble > 0f)
            {
                TurnBy(rumbleAt, rumbleDir * (rumble * 50f * target.metersPerUnit));
                rumble = 0f;
            }
        }

        /// <summary>A sphere of 'radius' swept from 'from' to 'to' (the hull meshes, HullCollision.CameraHit, 'ignore' left out,
        /// and the collision volumes of objects without them): the camera kept on this side of the first thing it meets, at
        /// least 'minM' from 'from'. ES2's spring arm probe; FreeLookCamera's docked orbit.</summary>
        public static Vector3 Probe(Vector3 from, Vector3 to, float radius, Transform ignore, float minM)
        {
            var d = to - from;
            float len = d.magnitude;
            if (len < 0.01f) return to;
            var dir = d / len;
            float hit = HullCollision.CameraHit(from, dir, len, radius, ignore) - radius * 0.5f;
            var all = Obstacle.All;
            for (int i = 0; i < all.Count; i++)
            {
                var o = all[i];
                if (o == null || (o.hull != null && o.hull.Active)) continue;   // a hull: CameraHit
                float t = o.RayDistance(from, dir);
                if (t > 0f && t < float.MaxValue) hit = Mathf.Min(hit, t - radius);
            }
            if (hit >= len) return to;
            return from + dir * Mathf.Max(minM, hit);
        }

        // The ship hidden while the camera is inside it (CameraDynamicsTick: a 50 cm sphere at the camera overlapping the
        // ship): the hull's bounds, read again now and then (a hull swap, kit parts).
        Bounds hullBounds;
        bool hullBoundsKnown, hullHidden, hullMaskCleared;
        float hullScanMs;

        void Es2HideHull(Transform ship, Vector3 camera)
        {
            hullScanMs -= Time.unscaledDeltaTime * 1000f;
            if (hullScanMs <= 0f || !hullBoundsKnown)
            {
                hullScanMs = 500f;
                hullBounds = World.PlayerHull.HullBounds(ship, target.visualModel != null ? target.visualModel : ship);
                hullBoundsKnown = hullBounds.size != Vector3.zero;
            }
            bool inside = false;
            if (hullBoundsKnown)
            {
                var b = hullBounds;
                b.Expand(2f * Es2ChaseRig.HideRadiusCm / 100f);
                inside = b.Contains(ship.InverseTransformPoint(camera));
            }
            SetHullHidden(inside);
        }

        void SetHullHidden(bool hide)
        {
            if (hide == hullHidden || fpShown) return;
            hullHidden = hide;
            if (hide)
            {
                HideHull();
                if (cam != null)
                {
                    hullMaskCleared = (cam.cullingMask & (1 << HiddenLayer)) != 0;
                    cam.cullingMask &= ~(1 << HiddenLayer);
                }
            }
            else
            {
                RestoreHullLayers();
                if (cam != null && hullMaskCleared) cam.cullingMask |= 1 << HiddenLayer;
            }
        }

        // ---- the cockpit view (remake) ----------------------------------------------------------------------------

        const int HiddenLayer = 28;   // VrRig.HiddenLayer: VR's cockpit and Action Freeze hide the hull on it too
        const float FirstPersonNear = 0.3f;
        readonly System.Collections.Generic.Dictionary<GameObject, int> fpLayers = new System.Collections.Generic.Dictionary<GameObject, int>();
        readonly System.Collections.Generic.List<Renderer> fpRenderers = new System.Collections.Generic.List<Renderer>();
        bool fpShown, fpMaskCleared;
        float fpNear, fpScanMs;
        Transform fpModel;
        Vector3 fpSeat;
        GameObject fpInterior, fpInteriorTemplate;

        void PlaceFirstPerson(Transform ship, float dtMs, bool boosting, float boostRumble, float mass)
        {
            if (!fpShown)
            {
                SetHullHidden(false);
                fpShown = true;
                fpActive = this;
                fpScanMs = 0f;
                if (cam != null)
                {
                    fpNear = cam.nearClipPlane;
                    cam.nearClipPlane = Mathf.Min(fpNear, FirstPersonNear);
                    fpMaskCleared = (cam.cullingMask & (1 << HiddenLayer)) != 0;
                    cam.cullingMask &= ~(1 << HiddenLayer);
                }
            }
            // The hull's renderers onto the hidden layer: again now and then, since turrets, kit weapons and a hull swap add
            // renderers (the engine parts are switched every frame: their layer holds, Renderer.enabled wouldn't).
            fpScanMs -= Time.unscaledDeltaTime * 1000f;
            if (fpScanMs <= 0f || target.visualModel != fpModel)
            {
                fpScanMs = 500f;
                HideHull();
                fpSeat = FirstPersonSeat();
                RefreshFirstPersonInterior();
            }
            var rot = ship.rotation;
            if (Es2Mode)
            {
                // EVERSPACE 2's cockpit: its shakes, the location ones x FirstPersonScale, the acceleration sway
                transform.SetPositionAndRotation(ship.TransformPoint(fpSeat), rot);
                Es2Shakes(ship, target.Model, dtMs, Es2Velocity(dtMs / 1000f), ship.TransformPoint(fpSeat) + ship.forward * 30f, false, true);
                slide = Vector3.zero;
                lookValid = false;
                if (cam != null)
                    cam.fieldOfView = GoF2Remake.Visuals.Aspect.VerticalFov(baseFov + 0.5f * boostFovAdd * target.Model.BoostVisualPercent * Mathf.Lerp(1f, 0.5f, mass), cam.aspect);
                return;
            }
            // The original keeps a thousandth of the hit shake: the remake turns the view a little instead (a hit 0.5 deg, an
            // own shot 0.16 deg), and the explosions' / the boost's rumble at a third of the chase view's.
            float j = 0f;
            if (shakeMs > 0f) { shakeMs -= dtMs; j += shakeUnits * 0.08f; }
            if (boosting) rumble = Mathf.Max(rumble, boostRumble);
            j += rumble * 1.3f;
            rumble = 0f;
            j *= GoF2Remake.Data.Settings.CameraShake;
            if (j > 0f) rot *= Quaternion.Euler(rumbleDir.x * j, rumbleDir.y * j, 0f);
            transform.SetPositionAndRotation(ship.TransformPoint(fpSeat), rot);
            slide = Vector3.zero;
            lookValid = false;
            if (cam != null)
                cam.fieldOfView = GoF2Remake.Visuals.Aspect.VerticalFov(baseFov + 0.5f * boostFovAdd * target.Model.BoostVisualPercent * Mathf.Lerp(1f, 0.5f, mass), cam.aspect);
        }

        /// <summary>The cockpit view's place in the ship's frame (metres): on the guns' line (the pivot: the HUD's crosshair,
        /// the locks and the beams aim along the root's nose from there), a fifth of the hull's half height above its middle
        /// but at most 1.5 m off that line (a capital hull's 100 m dropped the crosshair 5 deg), four fifths of the way to the
        /// bow (a big hull's nine tenths), but behind the frontmost primary gun (so its muzzle flash and shots start in view)
        /// and never behind the middle of the bow half. From the hull's meshes (no glow layers: PlayerHull.HullBounds), so
        /// game ships, mods' glTF and kit builds and the debug capital hulls alike. A mod ship's own "cockpit" (ModShipView)
        /// takes precedence exactly, including capital bridges; only automatic seats are constrained by gun placement.</summary>
        public Vector3 FirstPersonSeat()
        {
            if (target == null) return Vector3.zero;
            var ship = target.transform;
            var own = GoF2Remake.Data.Database.Shared.Ship(GoF2Remake.Data.Session.ShipIndex)?.cockpit;
            if (own.HasValue && !World.PlayerHull.Big)
                return Modding.ModShipView.ToLocal(own.Value, target.metersPerUnit);
            var model = target.visualModel != null ? target.visualModel : ship;
            var b = World.PlayerHull.HullBounds(ship, model);
            if (b.size == Vector3.zero) return new Vector3(0f, 1f, 2f);
            bool big = b.extents.z > 150f;   // the debug capital hulls (PlayerHull.Big): no guns, the bow
            float z = Mathf.Lerp(b.center.z, b.max.z, big ? 0.9f : 0.8f);
            var weapons = big ? null : target.GetComponent<WeaponSystem>();
            if (weapons != null)
            {
                float gunZ = float.NegativeInfinity;
                foreach (var g in weapons.Guns) if (!g.isSecondary) gunZ = Mathf.Max(gunZ, g.mountLocal.z);
                if (gunZ > b.center.z) z = Mathf.Max(Mathf.Lerp(b.center.z, b.max.z, 0.5f), Mathf.Min(z, gunZ - 0.5f));
            }
            return new Vector3(0f, Mathf.Clamp(b.center.y + 0.2f * b.extents.y, -1.5f, 1.5f), z);
        }

        // Local visual only: the GLB origin is the pilot's eye, metres, +Z forward, +Y up.
        void RefreshFirstPersonInterior()
        {
            var template = Modding.ModShips.CockpitTemplate(GoF2Remake.Data.Session.ShipIndex);
            if (template == fpInteriorTemplate && fpInterior != null) return;
            ClearFirstPersonInterior();
            if (template == null) return;
            fpInteriorTemplate = template;
            fpInterior = Instantiate(template, transform, false);
            fpInterior.transform.localPosition = Vector3.zero;
            fpInterior.transform.localRotation = Quaternion.identity;
            fpInterior.SetActive(true);
            if (cam != null) cam.nearClipPlane = Mathf.Min(fpNear, 0.03f);
        }

        void ClearFirstPersonInterior()
        {
            if (fpInterior != null) { fpInterior.SetActive(false); Destroy(fpInterior); }
            fpInterior = null;
            fpInteriorTemplate = null;
            if (fpShown && cam != null) cam.nearClipPlane = Mathf.Min(fpNear, FirstPersonNear);
        }

        void HideHull()
        {
            fpModel = target != null ? target.visualModel : null;
            if (fpModel == null) return;
            fpModel.GetComponentsInChildren(true, fpRenderers);
            foreach (var r in fpRenderers)
            {
                var go = r.gameObject;
                if (go.layer == HiddenLayer) continue;
                fpLayers[go] = go.layer;
                go.layer = HiddenLayer;
            }
            fpRenderers.Clear();
        }

        /// <summary>The hull's renderers back on their own layers.</summary>
        void RestoreHullLayers()
        {
            foreach (var kv in fpLayers) if (kv.Key != null && kv.Key.layer == HiddenLayer) kv.Key.layer = kv.Value;
            fpLayers.Clear();
            fpModel = null;
        }

        /// <summary>The hull's layers, the near plane and the culling mask back.</summary>
        void EndFirstPerson()
        {
            if (!fpShown) return;
            ClearFirstPersonInterior();
            fpShown = false;
            if (fpActive == this) fpActive = null;
            RestoreHullLayers();
            if (cam != null)
            {
                cam.nearClipPlane = fpNear;
                if (fpMaskCleared) cam.cullingMask |= 1 << HiddenLayer;
            }
            lookValid = false;
        }
    }
}
