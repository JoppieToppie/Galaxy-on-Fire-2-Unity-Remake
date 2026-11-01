// ArrivalScale.cs
// Remake: what stands in an orbit, growing as the ship flies in from between the systems (InterstellarSpace.ApproachSize,
// ArrivalSize): each thing about its own centre, so nothing moves and the look alone changes; hit boxes, routes and distances
// stay the real ones. SystemApproach holds the orbit's stand-ins, Visuals.WarpSky the real ones, at the same size across the load.
// A shrunk object is small on the screen, which a LODGroup takes for far away: it culled them until they had grown, and they
// popped in. So nothing here is ever culled by size again; what is that small is below a pixel anyway.

using System.Collections.Generic;
using UnityEngine;

namespace GoF2Remake.World
{
    public sealed class ArrivalScale
    {
        /// <summary>How long what comes in late takes to grow out of nothing (GrowIn).</summary>
        const float GrowInSeconds = 2.5f;

        readonly List<(Transform t, Vector3 full, float born)> items = new List<(Transform, Vector3, float)>();
        float size = 1f;

        /// <summary>'t' in its place at the size all else has now. 'growIn': it wasn't there a moment ago (whatever could not be
        /// built ahead, a mod's model still loading): it grows out of nothing, so it can't pop in.</summary>
        public void Add(Transform t, bool growIn = false)
        {
            if (t == null) return;
            items.Add((t, t.localScale, growIn ? Time.unscaledTime : float.NegativeInfinity));
            t.localScale *= growIn ? 0f : size;
            foreach (var group in t.GetComponentsInChildren<LODGroup>(true)) KeepShown(group);
        }

        static void KeepShown(LODGroup group)
        {
            var levels = group.GetLODs();
            if (levels.Length == 0) return;
            levels[levels.Length - 1].screenRelativeTransitionHeight = 0f;
            group.SetLODs(levels);
        }

        public void AddChildren(Transform parent)
        {
            if (parent == null) return;
            foreach (Transform child in parent) Add(child);
        }

        public void Apply(float size)
        {
            this.size = size;
            foreach (var (t, full, born) in items)
                if (t != null) t.localScale = full * (size * Mathf.SmoothStep(0f, 1f, (Time.unscaledTime - born) / GrowInSeconds));
        }

        /// <summary>Everything back at its own size, for good.</summary>
        public void Restore()
        {
            Apply(1f);
            items.Clear();
        }
    }
}
