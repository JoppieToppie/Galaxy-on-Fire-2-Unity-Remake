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
//    pct x n units per axis each frame. The radial blur on top (post effect 0x1400002) isn't reproduced.
//  * The cockpit view (MGame::switchCamera mode 2, TargetFollowCamera::enableFirstPersonCam +0xf0, unreachable in this
//    build: switchCamera and nextCamId turn 2 into 3): the camera on the ship's matrix, looking along its direction, the
//    ship hidden (PlayerEgo::hideShipForFirstPersonCameraView), the hit shake x0.001 and the rumble x0.0015. Remake
//    (FreeLookCamera.Mode.FirstPerson): reachable, on the bow instead of the pivot (FirstPersonSeat), the shake and the
//    rumble as small turns of the view, the boost's FOV at half.
//  * The shake and the rumble draw a new random offset each frame of the original (OriginalFrame.Ms, 60 Hz). Remake (players' reports
//    2026-10-18, extreme shaking): each offset is held for that frame (HeldJitter) instead of a new one every rendered
//    frame (at 144 fps an explosion's rumble jolted the view 2.8 deg every 7 ms), and laid on top of the follow, taken
//    off again the next frame (UndoJitter): drawn into the eased pose, it piled up through the easing.

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

        void Awake() => cam = GetComponent<Camera>();
        void OnEnable() { lookValid = false; cruiseTracked = false; }
        // Disabled = another camera holds it (the launch / arrival fly-in, a cutscene, a jump, mining, object docking, the
        // death camera, Action Freeze): the hull shows; enabled again, the cockpit view comes back.
        void OnDisable()
        {
            bool wasShown = fpShown;
            EndFirstPerson();
            // Some of them leave the camera where it is (the death camera, mining's landing, the planet jump): from behind the
            // ship as in the standard view, not inside the bow of a tumbling or pitching hull.
            if (!wasShown || target == null || follow != null) return;
            PlaceBehind();
            if (cam != null) cam.fieldOfView = GoF2Remake.Visuals.Aspect.VerticalFov(baseFov, cam.aspect);
        }
        void OnDestroy() => EndFirstPerson();

        void LateUpdate()
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
            if (follow != null)
            {
                var fp = follow.TransformPoint(followOffset);
                var fr = Quaternion.LookRotation(follow.TransformPoint(followLookOffset) - fp, followUsesUp ? follow.up : Vector3.up);
                float k = followRigid ? 1f : 1f - Mathf.Exp(-0.01f * dtMs);
                transform.position = Vector3.Lerp(transform.position, fp, k);
                transform.rotation = Quaternion.Slerp(transform.rotation, fr, k);
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
                return;
            }

            if (firstPerson) { PlaceFirstPerson(ship, dtMs, boosting, boostRumble, mass); return; }
            EndFirstPerson();

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
            bool original = GoF2Remake.Data.Settings.OriginalChaseCamera;
            float posEase = original ? Ease(posK, dtMs) : Follow(posK, dtMs), rotEase = original ? Ease(rotK, dtMs) : Follow(rotK, dtMs);
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
                if (!lookValid || (desiredLook - lookPoint).sqrMagnitude > 1000f * 1000f)
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
        public void Snap()
        {
            if (target == null) return;
            var ship = target.transform;
            PlaceBehind();
            if (GoF2Remake.Data.VrFlags.Enabled) transform.SetPositionAndRotation(ship.TransformPoint(GoF2Remake.Data.VrFlags.CockpitSeat), ship.rotation);
            else if (firstPerson && follow == null) transform.SetPositionAndRotation(ship.TransformPoint(FirstPersonSeat()), ship.rotation);
        }

        /// <summary>Remake: the view an arrival at speed flew in with, in the ship's frame (the follow's lag), after Snap.</summary>
        public void CarryView(Vector3 local, Quaternion turn)
        {
            if (target == null || firstPerson || GoF2Remake.Data.VrFlags.Enabled) return;
            var ship = target.transform;
            transform.SetPositionAndRotation(ship.TransformPoint(local), ship.rotation * turn);
            lookValid = false;
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

        // ---- the cockpit view (remake) ----------------------------------------------------------------------------

        const int HiddenLayer = 28;   // VrRig.HiddenLayer: VR's cockpit and Action Freeze hide the hull on it too
        const float FirstPersonNear = 0.3f;
        readonly System.Collections.Generic.Dictionary<GameObject, int> fpLayers = new System.Collections.Generic.Dictionary<GameObject, int>();
        readonly System.Collections.Generic.List<Renderer> fpRenderers = new System.Collections.Generic.List<Renderer>();
        bool fpShown, fpMaskCleared;
        float fpNear, fpScanMs;
        Transform fpModel;
        Vector3 fpSeat;

        void PlaceFirstPerson(Transform ship, float dtMs, bool boosting, float boostRumble, float mass)
        {
            if (!fpShown)
            {
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
            }
            var rot = ship.rotation;
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
        /// is taken as it is, only kept behind the frontmost primary gun too.</summary>
        public Vector3 FirstPersonSeat()
        {
            if (target == null) return Vector3.zero;
            var ship = target.transform;
            var model = target.visualModel != null ? target.visualModel : ship;
            var b = World.PlayerHull.HullBounds(ship, model);
            if (b.size == Vector3.zero) return new Vector3(0f, 1f, 2f);
            bool big = b.extents.z > 150f;   // the debug capital hulls (PlayerHull.Big): no guns, the bow
            var own = big || World.PlayerHull.Big ? null : GoF2Remake.Data.Database.Shared.Ship(GoF2Remake.Data.Session.ShipIndex)?.cockpit;
            float z = own.HasValue ? own.Value.z * target.metersPerUnit : Mathf.Lerp(b.center.z, b.max.z, big ? 0.9f : 0.8f);
            var weapons = big ? null : target.GetComponent<WeaponSystem>();
            if (weapons != null)
            {
                float gunZ = float.NegativeInfinity;
                foreach (var g in weapons.Guns) if (!g.isSecondary) gunZ = Mathf.Max(gunZ, g.mountLocal.z);
                if (own.HasValue) { if (gunZ > float.NegativeInfinity) z = Mathf.Min(z, gunZ - 0.5f); }
                else if (gunZ > b.center.z) z = Mathf.Max(Mathf.Lerp(b.center.z, b.max.z, 0.5f), Mathf.Min(z, gunZ - 0.5f));
            }
            if (own.HasValue)
            {
                var seat = Modding.ModShipView.ToLocal(own.Value, target.metersPerUnit);
                return new Vector3(seat.x, seat.y, z);
            }
            return new Vector3(0f, Mathf.Clamp(b.center.y + 0.2f * b.extents.y, -1.5f, 1.5f), z);
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

        /// <summary>The hull's layers, the near plane and the culling mask back.</summary>
        void EndFirstPerson()
        {
            if (!fpShown) return;
            fpShown = false;
            if (fpActive == this) fpActive = null;
            foreach (var kv in fpLayers) if (kv.Key != null && kv.Key.layer == HiddenLayer) kv.Key.layer = kv.Value;
            fpLayers.Clear();
            fpModel = null;
            if (cam != null)
            {
                cam.nearClipPlane = fpNear;
                if (fpMaskCleared) cam.cullingMask |= 1 << HiddenLayer;
            }
            lookValid = false;
        }
    }
}
