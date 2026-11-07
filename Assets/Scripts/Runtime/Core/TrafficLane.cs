// TrafficLane.cs
// Remake (2026-11-03, for mods): a traffic lane's path as plain rules (World.TrafficLanes flies the ships; stations.json
// "orbit.lanes"), after EVERSPACE 2's BP_SplineTraffic (decompiled: a spline through its points, ClosedLoop or DoUTurns at
// its ends, TwoLanes LaneOffset 2000 cm apart, Count 10 units at BaseSpeed 8000 cm/s, 50 hit points each, RespawnDelay 30 s).
// The path is a Catmull-Rom curve through the points (closed when it loops), walked by distance: Sample(d) gives the place
// and heading 'd' along it, measured on a table of SamplesPerSegment chords a segment. A unit's lane: forward on the right
// of the path, back on its left (TwoLanes), turning over at an open path's ends (Position).

using System.Collections.Generic;
using UnityEngine;

namespace GoF2Remake.Flight
{
    public class TrafficLane
    {
        public const int SamplesPerSegment = 16;
        /// <summary>ES2's spline traffic: the units, their speed (m/s), the lanes' distance either side, the respawn.</summary>
        public const int DefaultCount = 10;
        public const float DefaultSpeedMps = 80f, LaneOffsetM = 20f * 3.6f, RespawnMs = 30000f;

        readonly List<Vector3> samples = new List<Vector3>();
        readonly List<float> along = new List<float>();
        public bool Loop { get; }
        public float Length { get; }

        /// <summary>A lane through 'points' (at least two; Unity space), closed when 'loop'.</summary>
        public TrafficLane(IList<Vector3> points, bool loop)
        {
            int n = points.Count;
            Loop = loop && n > 2;
            int segments = Loop ? n : n - 1;
            for (int s = 0; s < segments; s++)
            {
                Vector3 p0 = At(points, s - 1), p1 = At(points, s), p2 = At(points, s + 1), p3 = At(points, s + 2);
                for (int k = 0; k < SamplesPerSegment; k++) samples.Add(CatmullRom(p0, p1, p2, p3, k / (float)SamplesPerSegment));
            }
            samples.Add(Loop ? points[0] : points[n - 1]);
            float d = 0f;
            along.Add(0f);
            for (int i = 1; i < samples.Count; i++) { d += Vector3.Distance(samples[i - 1], samples[i]); along.Add(d); }
            Length = d;
        }

        Vector3 At(IList<Vector3> p, int i)
        {
            int n = p.Count;
            if (Loop) return p[((i % n) + n) % n];
            if (i < 0) return p[0] + (p[0] - p[1]);   // (the ends continue straight)
            if (i >= n) return p[n - 1] + (p[n - 1] - p[n - 2]);
            return p[i];
        }

        public static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * (2f * p1 + (p2 - p0) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (3f * p1 - p0 - 3f * p2 + p3) * t3);
        }

        /// <summary>The place 'd' along the path (wrapped when it loops, clamped when not) and the way it runs there.</summary>
        public void Sample(float d, out Vector3 position, out Vector3 forward)
        {
            if (Loop) d = Mathf.Repeat(d, Length); else d = Mathf.Clamp(d, 0f, Length);
            int lo = 0, hi = along.Count - 1;
            while (hi - lo > 1) { int mid = (lo + hi) / 2; if (along[mid] <= d) lo = mid; else hi = mid; }
            float span = along[hi] - along[lo];
            float f = span > 1e-6f ? (d - along[lo]) / span : 0f;
            position = Vector3.Lerp(samples[lo], samples[hi], f);
            var dir = samples[hi] - samples[lo];
            forward = dir.sqrMagnitude > 1e-12f ? dir.normalized : Vector3.forward;
        }

        /// <summary>A unit 'travelled' metres on from 'start' metres along: its place on the path and whether it runs backwards
        /// (an open path's units turn over at its ends, back and forth; a loop's run one way).</summary>
        public float Position(float start, float travelled, out bool backwards)
        {
            backwards = false;
            if (Loop || Length <= 0f) return Mathf.Repeat(start + travelled, Mathf.Max(Length, 1e-3f));
            float t = Mathf.Repeat(start + travelled, 2f * Length);
            if (t <= Length) return t;
            backwards = true;
            return 2f * Length - t;
        }
    }
}
