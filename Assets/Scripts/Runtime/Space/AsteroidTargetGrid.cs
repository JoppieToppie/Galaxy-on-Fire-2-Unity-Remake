// AsteroidTargetGrid.cs
// Shared spatial index for stationary asteroid targets. Registration follows Target's enable/death
// lifecycle, so capital ships query nearby cells instead of independently scanning Target.All.

using System.Collections.Generic;
using UnityEngine;

namespace GoF2Remake.Flight
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class AsteroidTargetGrid
    {
        const float CellSize = 2500f;
        const float Margin = 50f;

        static readonly Dictionary<long, List<Target>> cells = new Dictionary<long, List<Target>>();
        static readonly Dictionary<Target, long> memberships = new Dictionary<Target, long>();
        static float maxRadius;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset()
        {
            cells.Clear();
            memberships.Clear();
            maxRadius = 0f;
        }

        public static void Register(Target target)
        {
            if (target == null || !target.isAsteroid) return;
            // The asteroid hit cube extends to its diagonal, not just its face.
            maxRadius = Mathf.Max(maxRadius, Mathf.Max(0f, target.radius) * 1.7320508f);
            if (memberships.ContainsKey(target)) return;
            long key = Key(target.transform.position);
            if (!cells.TryGetValue(key, out var cell))
            {
                cell = new List<Target>(4);
                cells.Add(key, cell);
            }
            cell.Add(target);
            memberships.Add(target, key);
        }

        public static void Unregister(Target target)
        {
            if (ReferenceEquals(target, null) || !memberships.TryGetValue(target, out long key)) return;
            memberships.Remove(target);
            if (!cells.TryGetValue(key, out var cell)) return;
            cell.Remove(target);
            if (cell.Count == 0) cells.Remove(key);
            // maxRadius is deliberately a conservative high-water mark. A stale larger value can
            // only cause a few extra cell lookups; it cannot omit an asteroid.
        }

        /// <summary>Fills 'into' with live asteroids whose hit cubes may reach the query sphere.</summary>
        public static void Query(Vector3 centre, float reach, List<Target> into)
        {
            float search = Mathf.Max(0f, reach) + maxRadius + Margin;
            int x0 = Cell(centre.x - search), x1 = Cell(centre.x + search);
            int y0 = Cell(centre.y - search), y1 = Cell(centre.y + search);
            int z0 = Cell(centre.z - search), z1 = Cell(centre.z + search);

            for (int x = x0; x <= x1; x++)
                for (int y = y0; y <= y1; y++)
                    for (int z = z0; z <= z1; z++)
                    {
                        if (!cells.TryGetValue(Pack(x, y, z), out var cell)) continue;
                        for (int i = 0; i < cell.Count; i++)
                        {
                            var asteroid = cell[i];
                            if (asteroid == null || !asteroid.isActiveAndEnabled || !asteroid.Alive) continue;
                            float r = Mathf.Max(0f, asteroid.radius) * 1.7320508f;
                            float limit = Mathf.Max(0f, reach) + Margin + r;
                            if ((asteroid.transform.position - centre).sqrMagnitude <= limit * limit)
                                into.Add(asteroid);
                        }
                    }
        }

        static int Cell(float value) => Mathf.FloorToInt(value / CellSize);

        static long Key(Vector3 p) => Pack(Cell(p.x), Cell(p.y), Cell(p.z));

        static long Pack(int x, int y, int z) =>
            ((long)(x & 0x1FFFFF) << 42) | ((long)(y & 0x1FFFFF) << 21) | (long)(z & 0x1FFFFF);
    }
}
