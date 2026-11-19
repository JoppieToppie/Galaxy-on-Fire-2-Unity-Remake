// TurretAim.cs
// PlayerTurret::handleRotation / PlayerEgo::handleAutoTurret (Reference/research/weapons_special.md 5.2, 6.2;
// npc_combat_specials.md 1): a turret turns toward a point in steps, never snaps. The aim direction is taken in the gun's
// own frame: the turret turns toward the target at its configured yaw and pitch rates, limited to [pitchMin, pitchMax].
// Alignment is checked after each step at a tight angular tolerance, so a turret does not fire several degrees off target.
// Plain C#: the owner hands in the yaw node (base pivot) and the gun node (its
// barrel along its local +Z) and gets the local rotations back.

using UnityEngine;

namespace GoF2Remake.Flight
{
    public class TurretAim
    {
        /// <summary>2 pi / 4096 rad per ms (87.9 deg/s): the base unit of every turret rate.</summary>
        public const float RadPerMs = 2f * Mathf.PI / 4096f;
        const float AlignmentToleranceRad = 0.0025f; // about 0.14 degrees

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
            var d = (world - gunNode.position).normalized;
            var local = gunNode.InverseTransformDirection(d);
            float yawError = Mathf.Atan2(local.x, local.z);
            float pitchError = Mathf.Atan2(local.y, Mathf.Sqrt(local.x * local.x + local.z * local.z));
            LimitHit = false;
            Yaw += Mathf.Clamp(yawError, -yawRatePerMs * dtMs, yawRatePerMs * dtMs);
            Turn(0f, Mathf.Clamp(pitchError, -pitchRatePerMs * dtMs, pitchRatePerMs * dtMs));
            Apply();

            // Check the updated pose. The old dot-product dead zone was roughly 2.9 degrees wide and used stale angles,
            // so turrets could report alignment and fire after crossing that wide zone while still substantially off target.
            local = gunNode.InverseTransformDirection((world - gunNode.position).normalized);
            float remainingYaw = Mathf.Atan2(local.x, local.z);
            float remainingPitch = Mathf.Atan2(local.y, Mathf.Sqrt(local.x * local.x + local.z * local.z));
            return local.z > 0f && Mathf.Abs(remainingYaw) <= AlignmentToleranceRad &&
                   Mathf.Abs(remainingPitch) <= AlignmentToleranceRad;
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
