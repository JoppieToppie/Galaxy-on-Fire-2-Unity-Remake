// Shared native contact geometry for the measured player hull and classified NPC hulls.
// Asteroids retain the game's world-aligned hit cube (or explicit local hit boxes).
using UnityEngine;

namespace GoF2Remake.Flight
{
    internal static class AsteroidHullContact
    {
        internal readonly struct Box
        {
            internal readonly Vector3 centre, half, x, y, z;
            internal Box(Vector3 centre, Vector3 half, Quaternion rotation)
            {
                this.centre = centre;
                this.half = Abs(half);
                x = rotation * Vector3.right;
                y = rotation * Vector3.up;
                z = rotation * Vector3.forward;
            }

            internal float RadiusOn(Vector3 axis) =>
                half.x * Mathf.Abs(Vector3.Dot(x, axis)) +
                half.y * Mathf.Abs(Vector3.Dot(y, axis)) +
                half.z * Mathf.Abs(Vector3.Dot(z, axis));

            internal Vector3 ClosestPoint(Vector3 point)
            {
                Vector3 d = point - centre;
                return centre + x * Mathf.Clamp(Vector3.Dot(d, x), -half.x, half.x) +
                    y * Mathf.Clamp(Vector3.Dot(d, y), -half.y, half.y) +
                    z * Mathf.Clamp(Vector3.Dot(d, z), -half.z, half.z);
            }
        }

        internal static Vector3 Abs(Vector3 value) =>
            new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));

        static Box TargetBox(Target target, Bounds bounds) =>
            new Box(target.transform.TransformPoint(bounds.center),
                Vector3.Scale(bounds.extents, Abs(target.transform.lossyScale)), target.transform.rotation);

        static Box TargetCube(Target target) =>
            new Box(target.transform.position, Vector3.one * Mathf.Max(0f, target.radius), Quaternion.identity);

        internal static bool Touches(Box hull, Target asteroid, Vector3 movement, out Vector3 normal)
        {
            if (asteroid.boxes != null && asteroid.boxes.Length > 0)
            {
                foreach (var b in asteroid.boxes)
                    if (Sweep(hull, TargetBox(asteroid, b), movement, out normal)) return true;
                normal = Vector3.zero;
                return false;
            }
            return Sweep(hull, TargetCube(asteroid), movement, out normal);
        }

        internal static bool TouchesSphere(Vector3 centre, float radius, Target asteroid)
        {
            if (asteroid.boxes != null && asteroid.boxes.Length > 0)
            {
                foreach (var b in asteroid.boxes)
                    if ((TargetBox(asteroid, b).ClosestPoint(centre) - centre).sqrMagnitude <= radius * radius)
                        return true;
                return false;
            }
            return (TargetCube(asteroid).ClosestPoint(centre) - centre).sqrMagnitude <= radius * radius;
        }

        // Continuous separating-axis test for two oriented boxes at fixed orientations.
        // 'moving' is the end pose; movement is the translation from start to end.
        // Unlike an expanded AABB this does not hit the empty corners of rotated hulls.
        internal static bool Sweep(Box moving, Box other, Vector3 movement, out Vector3 normal)
        {
            Vector3 start = moving.centre - movement - other.centre;
            float enter = 0f, exit = 1f, smallest = float.PositiveInfinity;
            Vector3 entryNormal = Vector3.zero, overlapNormal = Vector3.zero;
            normal = Vector3.zero;
            for (int i = 0; i < 15; i++)
            {
                Vector3 axis;
                if (i < 3) axis = Axis(moving, i);
                else if (i < 6) axis = Axis(other, i - 3);
                else
                {
                    axis = Vector3.Cross(Axis(moving, (i - 6) / 3), Axis(other, (i - 6) % 3));
                    float squared = axis.sqrMagnitude;
                    if (squared < 1e-10f) continue;
                    axis /= Mathf.Sqrt(squared);
                }

                float extent = moving.RadiusOn(axis) + other.RadiusOn(axis);
                float offset = Vector3.Dot(start, axis);
                float speed = Vector3.Dot(movement, axis);
                float depth = extent - Mathf.Abs(offset);
                if (depth < smallest)
                {
                    smallest = depth;
                    overlapNormal = offset < 0f || (offset == 0f && speed > 0f) ? -axis : axis;
                }
                if (Mathf.Abs(speed) < 1e-6f)
                {
                    if (Mathf.Abs(offset) > extent) return false;
                    continue;
                }

                float a = (-extent - offset) / speed;
                float b = (extent - offset) / speed;
                float first = Mathf.Min(a, b), last = Mathf.Max(a, b);
                if (first > enter)
                {
                    enter = first;
                    entryNormal = speed > 0f ? -axis : axis;
                }
                exit = Mathf.Min(exit, last);
                if (enter > exit) return false;
            }
            normal = entryNormal.sqrMagnitude > 0f ? entryNormal : overlapNormal;
            return true;
        }

        static Vector3 Axis(Box box, int i) => i == 0 ? box.x : i == 1 ? box.y : box.z;
    }
}
