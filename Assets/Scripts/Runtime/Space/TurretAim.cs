// TurretAim.cs
// PlayerTurret::handleRotation / PlayerEgo::handleAutoTurret (Reference/research/weapons_special.md 5.2, 6.2;
// npc_combat_specials.md 1): a turret turns toward a point in steps, never snaps. The aim direction is taken in the gun's
// own frame: |x| > 0.05 -> yaw the base by yawRate * dt toward it, |y| > 0.05 -> pitch the gun by pitchRate * dt, limited
// to [pitchMin, pitchMax] (a turret that hits a limit can't reach the target: the owner picks another). Aligned (both
// within +-0.05, target in front) -> fire.
// Remake (#354, Options > Gameplay "Turrets lead moving targets", Settings.TurretLeadAim, off by default): the turret
// turns straight onto the target at its rates (no overshoot) and reports aligned within 0.14 deg of the updated pose.
// Plain C#: the owner hands in the yaw node (base pivot) and the gun node (its barrel along its local +Z) and gets the
// local rotations back.

using GoF2Remake.Data;
using UnityEngine;

namespace GoF2Remake.Flight
{
    public class TurretAim
    {
        /// <summary>2 pi / 4096 rad per ms (87.9 deg/s): the base unit of every turret rate.</summary>
        public const float RadPerMs = 2f * Mathf.PI / 4096f;
        const float DeadZone = 0.05f;
        const float AlignmentToleranceRad = 0.0025f; // about 0.14 degrees (Settings.TurretLeadAim)

        public float yawRatePerMs = RadPerMs, pitchRatePerMs = RadPerMs;
        /// <summary>Pitch limits in radians (up positive); NaN = unlimited (sentry guns).</summary>
        public float pitchMin = float.NaN, pitchMax = float.NaN;
        public float Yaw { get; private set; }
        public float Pitch { get; private set; }
        /// <summary>The last step ran into a pitch limit (PlayerEgo+0x190: the target is skipped at the next pick).</summary>
        public bool LimitHit { get; private set; }

        readonly Quaternion yawBase, gunBase;
        readonly Transform yawNode, gunNode;

        public TurretAim(Transform yawNode, Transform gunNode, float startYawRad = 0f)
        {
            this.yawNode = yawNode;
            this.gunNode = gunNode;
            yawBase = yawNode.localRotation;
            gunBase = gunNode.localRotation;
            Yaw = startYawRad;
            Apply();
        }

        public Vector3 BarrelForward => gunNode.forward;
        public Vector3 BarrelUp => gunNode.up;

        /// <summary>One step toward 'world'; true when aligned (the owner may fire).</summary>
        public bool Step(Vector3 world, float dtMs)
        {
            if (!Settings.TurretLeadAim) return StepOriginal(world, dtMs);
            LimitHit = false;

            // Yaw belongs to the base pivot's frame, not the already-pitched gun frame.
            // Apply yaw first, then measure pitch in the gun's updated frame; otherwise pitch
            // contaminates yaw and the two axes fight each other on elevated/depressed targets.
            var direction = (world - gunNode.position).normalized;
            var yawLocal = yawNode.InverseTransformDirection(direction);
            float yawError = Mathf.Atan2(yawLocal.x, yawLocal.z);
            Yaw += Mathf.Clamp(yawError, -yawRatePerMs * dtMs, yawRatePerMs * dtMs);
            Apply();

            direction = (world - gunNode.position).normalized;
            var pitchLocal = gunNode.InverseTransformDirection(direction);
            float pitchError = Mathf.Atan2(pitchLocal.y,
                Mathf.Sqrt(pitchLocal.x * pitchLocal.x + pitchLocal.z * pitchLocal.z));
            Turn(0f, Mathf.Clamp(pitchError, -pitchRatePerMs * dtMs, pitchRatePerMs * dtMs));
            Apply();

            // Verify each axis in the frame in which that axis is actually controlled.
            direction = (world - gunNode.position).normalized;
            yawLocal = yawNode.InverseTransformDirection(direction);
            pitchLocal = gunNode.InverseTransformDirection(direction);
            float remainingYaw = Mathf.Atan2(yawLocal.x, yawLocal.z);
            float remainingPitch = Mathf.Atan2(pitchLocal.y,
                Mathf.Sqrt(pitchLocal.x * pitchLocal.x + pitchLocal.z * pitchLocal.z));
            return yawLocal.z > 0f && Mathf.Abs(remainingYaw) <= AlignmentToleranceRad &&
                   Mathf.Abs(remainingPitch) <= AlignmentToleranceRad;
        }

        /// <summary>The original's step (PlayerTurret::handleRotation): a fixed-rate turn while the aim is outside the 0.05
        /// dead zone (or behind), aligned inside it.</summary>
        bool StepOriginal(Vector3 world, float dtMs)
        {
            var d = (world - gunNode.position).normalized;
            float x = Vector3.Dot(d, gunNode.right), y = Vector3.Dot(d, gunNode.up), z = Vector3.Dot(d, gunNode.forward);
            LimitHit = false;
            if (Mathf.Abs(x) > DeadZone || z < 0f) Yaw += (x >= 0f ? 1f : -1f) * yawRatePerMs * dtMs;
            if (Mathf.Abs(y) > DeadZone) Turn(0f, (y > 0f ? 1f : -1f) * pitchRatePerMs * dtMs);
            Apply();
            return Mathf.Abs(x) <= DeadZone && Mathf.Abs(y) <= DeadZone && z > 0f;
        }

        /// <summary>Manual aiming (the turret view): stick x yaws, stick y pitches.</summary>
        public void Drive(Vector2 stick, float dtMs)
        {
            LimitHit = false;
            Yaw += stick.x * yawRatePerMs * dtMs;
            Turn(0f, stick.y * pitchRatePerMs * dtMs);
            Apply();
        }

        void Turn(float yaw, float pitch)
        {
            float p = Pitch + pitch;
            if (!float.IsNaN(pitchMax) && p > pitchMax) { p = pitchMax; LimitHit = true; }
            if (!float.IsNaN(pitchMin) && p < pitchMin) { p = pitchMin; LimitHit = true; }
            Pitch = p;
        }

        void Apply()
        {
            yawNode.localRotation = yawBase * Quaternion.AngleAxis(Yaw * Mathf.Rad2Deg, Vector3.up);
            // The gun's barrel is its local +Z: pitching up is a negative turn about its local X.
            gunNode.localRotation = gunBase * Quaternion.AngleAxis(-Pitch * Mathf.Rad2Deg, Vector3.right);
        }
    }
}
