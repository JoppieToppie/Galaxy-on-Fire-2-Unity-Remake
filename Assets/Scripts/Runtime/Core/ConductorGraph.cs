// ConductorGraph.cs
// Remake (2026-11-06, for mods): which conductors carry energy (World.ConductorNet, a secure container's "conductors"), after
// EVERSPACE 2's UConductorComponent (ES2_classes.hpp; BP_Conductor_Source / _Static / _Portable / _Receiver defaults): two
// conductors link when either lies inside the other's conduction sphere (ConductionSphereRadius: 20 000 cm for sources,
// statics and portables, 200 cm for a receiver); a node is energized when a chain of links reaches a source
// (bEmitsElectricity). A receiver needs ConductorSourcesNeeded (1) sources and charges for ChargeTime (1.5 s) once reached.

using System.Collections.Generic;

namespace GoF2Remake.Flight
{
    public static class ConductorGraph
    {
        /// <summary>BP_Conductor_Receiver's ChargeTime (s); the default spheres (m, at the orbit places' scale).</summary>
        public const float ChargeS = 1.5f, ConductorRadiusM = 200f, ReceiverRadiusM = 2f;

        /// <summary>Whether nodes a and b link: the distance within the larger of their spheres.</summary>
        public static bool Linked(float distance, float radiusA, float radiusB) => distance <= System.Math.Max(radiusA, radiusB);

        /// <summary>Which nodes the sources reach: positions (x, y, z), their spheres, which are sources; 'feeder' = the node each
        /// got its energy from (-1 = a source or dark), for the beams.</summary>
        public static bool[] Energized(IList<(float x, float y, float z)> at, IList<float> radius, IList<bool> source, out int[] feeder)
        {
            int n = at.Count;
            var on = new bool[n];
            feeder = new int[n];
            var queue = new Queue<int>();
            for (int i = 0; i < n; i++) { feeder[i] = -1; if (source[i]) { on[i] = true; queue.Enqueue(i); } }
            while (queue.Count > 0)
            {
                int i = queue.Dequeue();
                for (int j = 0; j < n; j++)
                {
                    if (on[j]) continue;
                    float dx = at[i].x - at[j].x, dy = at[i].y - at[j].y, dz = at[i].z - at[j].z;
                    if (!Linked((float)System.Math.Sqrt(dx * dx + dy * dy + dz * dz), radius[i], radius[j])) continue;
                    on[j] = true;
                    feeder[j] = i;
                    queue.Enqueue(j);
                }
            }
            return on;
        }
    }
}
