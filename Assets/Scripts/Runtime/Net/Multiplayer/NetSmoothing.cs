// NetSmoothing.cs
// Remote objects' motion between network updates (NetPlayer, NetProxy, NetCrate): each received position is kept with its
// arrival time and a velocity estimate, measured on the sender's clock (the pose stream's gap, NetPoses: arrival times jitter)
// when it is known; the object is drawn a little ahead along that velocity (up to about one and a half of the sender's
// intervals, which the pose stream varies with distance: 0.1 s near, 0.5 s far; at most 0.75 s) and eases toward it, and
// snaps when it is far off (a respawn, a teleport).

using UnityEngine;

namespace GoF2Remake.Multiplayer
{
    public sealed class NetSmoothing
    {
        const float Ease = 12f, MinAhead = 0.25f, MaxAhead = 0.75f, SnapDistance = 250f;
        /// <summary>A gap longer than this (an object that stood still, a refresh) is no motion to measure.</summary>
        const float MaxGap = 1.5f;

        Vector3 position, velocity;
        float stamp, interval = 0.1f;
        bool has, snap = true;

        public bool Has => has;
        /// <summary>The last received position (Unity).</summary>
        public Vector3 Position => position;
        /// <summary>The estimated velocity (Unity metres a second): a handover starts the rebuilt ship with it.</summary>
        public Vector3 Velocity => velocity;

        /// <summary>A received position; 'gap' = the sender's seconds since its previous one (0 = unknown: arrival time).</summary>
        public void Push(Vector3 p, float gap = 0f)
        {
            float now = Time.unscaledTime;
            float dt = gap > 0f ? gap : now - stamp;
            if (has && (p - position).sqrMagnitude > SnapDistance * SnapDistance) { velocity = Vector3.zero; snap = true; }   // a jump, not motion
            else if (has && dt > 0.001f && dt <= MaxGap) velocity = Vector3.Lerp(velocity, (p - position) / dt, 0.5f);
            else if (has && dt > MaxGap) velocity = Vector3.zero;
            if (has && dt > 0.001f && dt <= MaxGap) interval = Mathf.Lerp(interval, dt, 0.3f);
            position = p;
            stamp = now;
            has = true;
        }

        /// <summary>The next Apply puts it straight at the target (shown again, a long way off).</summary>
        public void Snap() => snap = true;

        public void Apply(Transform t, Quaternion rotation)
        {
            if (!has) return;
            float ahead = Mathf.Clamp(interval * 1.5f, MinAhead, MaxAhead);
            var target = position + velocity * Mathf.Min(Time.unscaledTime - stamp, ahead);
            if (snap || (t.position - target).sqrMagnitude > SnapDistance * SnapDistance)
            {
                t.SetPositionAndRotation(target, rotation);
                snap = false;
                return;
            }
            float k = 1f - Mathf.Exp(-Time.unscaledDeltaTime * Ease);
            t.SetPositionAndRotation(Vector3.Lerp(t.position, target, k), Quaternion.Slerp(t.rotation, rotation, k));
        }
    }
}
