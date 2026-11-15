// Es2ChaseRig.cs
// Remake, free flight (Graphics option "Free flight camera", Settings.Es2FreeFlightCamera): EVERSPACE 2's third-person
// camera behind the player's ship, plain C# for ChaseCamera (and the tests). From BP_Ship_Player (the blueprint dump's
// ubergraph, CameraDynamicsTick 273795) and the shipping exe (docs/notes/free-flight.md "Free flight camera"):
//  * The rig: CollisionRoot -> CameraTPRotationPivot (PivotXOffset ahead of the ship, its rotation absolute) ->
//    SpringArmTPFar (DT_ShipSettings: 2800 cm, socket (0, 0, 600)) -> the camera, looking along the pivot's rotation (no
//    look-at). The arm's root sits at the ship (ArmRelLoc = -(PivotX + BoxExtent.X / 2)), so the camera hangs behind the
//    ship and swings round a point PivotX ahead of it: in a turn the ship slides across the view, the aim stays near the
//    middle. Unit: UE centimetres; the remake scales the rig by the hull (Scale: its chase offset's distance over ES2's arm).
//  * UpdateCameraRotationLag (270308): pivot rotation = UMathLib::ESRInterpToDynamicTarget(pivot, last target, the ship's
//    rotation, dt, 10^X) (0x1418b6544, LagRotation): an exponential lag of the whole rotation, roll included (no horizon:
//    the camera rolls with the ship, later). X = the stiffness slider's Low / Default / High 3 / 5 / 10 at 0 / 0.5 / 1; the
//    remake takes the default 5 (lag ~ w / (5 ln 10) = w x 0.087 s, the measured ~0.07 s; DT_ShipSettings' 12 would be
//    0.036 s).
//  * The velocity offset (same block): the pivot's place = VInterpTo(it, (PivotX, 0, 0) + the ship-frame -velocity /
//    lerp(15, 25, f) (f the slider, 0.5: / 20), clamped X -1000..300, Y +-2000, Z -2000..300 cm, speed 4): the camera trails
//    the speed and a strafe; no look-ahead.
//  * UpdateFOV (281965) / UpdateBoostCameraBehavior (275355): horizontal FOV = base + boost + cruise; boost toward
//    15 x boosting x max(camera forward . velocity direction, 0), cruise toward 20, both FInterpTo speed 2.

using UnityEngine;

namespace GoF2Remake.Flight
{
    public static class Es2ChaseRig
    {
        /// <summary>CamTPRotationLagPivotXOffset (DT_ShipSettings, cm): the pivot this far ahead of the ship.</summary>
        public const float PivotCm = 3000f;
        /// <summary>CamTPFarSpringArmLength (cm): the far third-person arm (the default view), its socket's height.</summary>
        public const float ArmCm = 2800f, SocketCm = 600f;
        /// <summary>CamTPCloseSpringArmLength / socket (cm): the close view (CurrentCameraView 1).</summary>
        public const float CloseArmCm = 2000f, CloseSocketCm = 500f;
        /// <summary>The arms' ProbeSize (cm): a sphere swept from the arm's root pulls the camera in front of what it meets.</summary>
        public const float ProbeCm = 200f;
        /// <summary>The ship is hidden while a sphere this big at the camera overlaps it (CameraDynamicsTick, cm).</summary>
        public const float HideRadiusCm = 50f;
        /// <summary>Free look (DT CamFreelookSpringArmLength; ISetFreelookEnabled, InputRotateComponent, the zoom): the arm, its
        /// starting pitch (the camera above), the pitch's limit, the zoom's step, limits and interp speed, the blend's rate
        /// (FreelookBlendTimeine: 1 s at play rate 3) and its ease's exponent (EaseInOut, 2).</summary>
        public const float FreeLookArmCm = 2000f, FreeLookPitch = 15f, FreeLookPitchMax = 89f;
        public const float ZoomStepCm = 200f, ZoomMinCm = 200f, ZoomMaxCm = 10000f, ZoomSpeed = 12f;
        public const float LookBlendRate = 3f, LookBlendExp = 2f;
        /// <summary>The stiffness slider's default (CameraTPRotationLagStiffnessDefault): the lag decays by 10^X per second.</summary>
        public const float Stiffness = 5f;
        /// <summary>UPhysicsSettings.MaxPhysicsDeltaTime's default (s): the lag's dt is clamped to it.</summary>
        public const float MaxDt = 1f / 30f;
        /// <summary>The velocity offset: -velocity / this (lerp(15, 25, 0.5)), its interp speed, its clamps (cm, UE axes).</summary>
        public const float VelocityDivisor = 20f, OffsetSpeed = 4f;
        public const float BackMinCm = -1000f, AheadMaxCm = 300f, SideCm = 2000f, DownMinCm = -2000f, UpMaxCm = 300f;
        /// <summary>BoostFOVOffset / CruiseFOVMax (horizontal degrees) and their FInterpTo speed.</summary>
        public const float BoostFov = 15f, CruiseFov = 20f, FovSpeed = 2f;
        /// <summary>The substep of ESRInterpToDynamicTarget (s).</summary>
        const float Step = 0.001f;

        /// <summary>The angle between two rotations as ESRInterpToDynamicTarget measures it: acos(2 dot^2 - 1), radians.</summary>
        public static float Angle(Quaternion a, Quaternion b)
        {
            float d = Quaternion.Dot(a, b);
            return Mathf.Acos(Mathf.Clamp(2f * d * d - 1f, -1f, 1f));
        }

        /// <summary>UMathLib::ESRInterpToDynamicTarget (0x1418b6544): 'current' lagging behind a target that moved from
        /// 'lastTarget' to 'target' over 'dt' seconds, decaying by 'stiffness' (10^X) a second. The lag angle starts at the
        /// one to the last target, grows with the target's move spread over the frame and decays in 1 ms substeps; the result
        /// lies that share of the way from the target back toward 'current'. 'maxDt' (MaxDt by default) caps the frame: the
        /// remake's fast-forward passes it scaled, so a sped-up frame doesn't lag the view further behind.</summary>
        public static Quaternion LagRotation(Quaternion current, Quaternion lastTarget, Quaternion target, float dt, float stiffness, float maxDt = MaxDt)
        {
            dt = Mathf.Min(dt, maxDt);
            if (dt <= 0f || current == target) return current;
            float d0 = Angle(current, lastTarget), dT = Angle(current, target);
            if (dT <= 1e-8f) return target;
            float k = Mathf.Max(stiffness, 1f), d = d0, rem = dt;
            while (rem > 1e-8f)
            {
                float h = Mathf.Min(rem, Step);
                d = (d + (dT - d0) * h / dt) * Mathf.Pow(k, -h);
                rem -= h;
            }
            return Quaternion.SlerpUnclamped(target, current, Mathf.Clamp01(d / dT));
        }

        /// <summary>The pivot's goal in the ship's frame (Unity metres, x right, y up, z forward): 'pivotM' ahead, moved by the
        /// ship-frame velocity 'localVelocity' (m/s) / VelocityDivisor within the clamps, all at the rig's 'scale'.</summary>
        public static Vector3 PivotGoal(Vector3 localVelocity, float pivotM, float scale)
        {
            // cm/s / 20 = cm; to metres: m/s x 100 / 20 / 100 = m/s / 20
            var o = -localVelocity / VelocityDivisor;
            o.z = Mathf.Clamp(o.z, BackMinCm / 100f, AheadMaxCm / 100f);   // UE X: forward
            o.x = Mathf.Clamp(o.x, -SideCm / 100f, SideCm / 100f);         // UE Y: right
            o.y = Mathf.Clamp(o.y, DownMinCm / 100f, UpMaxCm / 100f);      // UE Z: up
            return new Vector3(0f, 0f, pivotM) + o * scale;
        }

        /// <summary>UE's FMath::VInterpTo: 'clamp(dt x speed, 0, 1)' of the way to 'target'.</summary>
        public static Vector3 InterpTo(Vector3 current, Vector3 target, float dt, float speed)
        {
            if (speed <= 0f) return target;
            var dist = target - current;
            if (dist.sqrMagnitude < 1e-8f) return target;
            return current + dist * Mathf.Clamp01(dt * speed);
        }

        /// <summary>UE's FMath::FInterpTo.</summary>
        public static float InterpTo(float current, float target, float dt, float speed)
        {
            if (speed <= 0f) return target;
            float dist = target - current;
            if (dist * dist < 1e-8f) return target;
            return current + dist * Mathf.Clamp01(dt * speed);
        }

        /// <summary>The camera's place from the pivot (world) and its lagged rotation: the arm 'behindM' back from the pivot
        /// ('pivotM' ahead plus the ship-to-camera distance) and 'heightM' up, both in the pivot's rotation.</summary>
        public static Vector3 CameraPosition(Vector3 pivotWorld, Quaternion pivotRotation, float pivotM, float behindM, float heightM) =>
            pivotWorld + pivotRotation * new Vector3(0f, heightM, -(pivotM + behindM));

        /// <summary>UpdateFOV's dolly: the arm 'arm' shortened by D - D tan(base / 2) / tan(fov / 2), so a wider field of view
        /// keeps the ship the size it is at 'baseFov' (ES2's 75 against its FOV setting; the remake's original FOV against
        /// the Field of view option, vertical: the ratio of the tangents).</summary>
        public static float DollyArm(float arm, float baseFov, float fov)
        {
            float t = Mathf.Tan(Mathf.Clamp(fov, 1f, 170f) * 0.5f * Mathf.Deg2Rad);
            return arm * Mathf.Tan(Mathf.Clamp(baseFov, 1f, 170f) * 0.5f * Mathf.Deg2Rad) / t;
        }

        /// <summary>UE's FMath::InterpEaseInOut from 0 to 1 at 't' with exponent 'exp'.</summary>
        public static float EaseInOut(float t, float exp)
        {
            t = Mathf.Clamp01(t);
            return t < 0.5f ? 0.5f * Mathf.Pow(2f * t, exp) : 1f - 0.5f * Mathf.Pow(2f * (1f - t), exp);
        }

        /// <summary>Vertical FOV (degrees) at 16:9 with 'addHorizontal' degrees added to its horizontal one (ES2's FOVs are
        /// horizontal: the boost's 15 widens the width by that).</summary>
        public static float WidenVertical(float verticalAt16x9, float addHorizontal)
        {
            const float Aspect16x9 = 16f / 9f;
            float h = 2f * Mathf.Atan(Mathf.Tan(verticalAt16x9 * 0.5f * Mathf.Deg2Rad) * Aspect16x9) * Mathf.Rad2Deg;
            h = Mathf.Clamp(h + addHorizontal, 1f, 170f);
            return 2f * Mathf.Atan(Mathf.Tan(h * 0.5f * Mathf.Deg2Rad) / Aspect16x9) * Mathf.Rad2Deg;
        }
    }
}
