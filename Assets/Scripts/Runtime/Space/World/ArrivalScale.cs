// ArrivalScale.cs
// Remake: what stands in an orbit, growing as the ship flies in from between the systems (InterstellarSpace.ApproachSize,
// ArrivalSize): each thing about its own centre, so nothing moves and the look alone changes; hit boxes, routes and distances
// stay the real ones. SystemApproach holds the orbit's stand-ins, Visuals.WarpSky the real ones, at the same size across the load.
// A shrunk object is small on the screen, which a LODGroup takes for far away: it culled them until they had grown, and they
// popped in. So while one is scaled its last level is never culled by size (what is that small is below a pixel anyway);
// Restore puts the cull back as it was. The first value seen is kept (Originals), as a stand-in's model is scaled by the
// approach and then again by the arrival's traffic, which restores it.

using System.Collections.Generic;
using UnityEngine;

namespace GoF2Remake.World
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public sealed class ArrivalScale
    {
        /// <summary>How long what comes in late takes to grow out of nothing (GrowIn).</summary>
        const float GrowInSeconds = 2.5f;

        /// <summary>The last level's own cull height of every group kept shown, until it is restored.</summary>
        static readonly Dictionary<LODGroup, float> Originals = new Dictionary<LODGroup, float>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Originals.Clear();

        readonly List<(Transform t, Vector3 full, float born)> items = new List<(Transform, Vector3, float)>();
        readonly List<LODGroup> groups = new List<LODGroup>();
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

        void KeepShown(LODGroup group)
        {
            var levels = group.GetLODs();
            if (levels.Length == 0) return;
            if (!Originals.ContainsKey(group))
            {
                Originals[group] = levels[levels.Length - 1].screenRelativeTransitionHeight;
                levels[levels.Length - 1].screenRelativeTransitionHeight = 0f;
                group.SetLODs(levels);
            }
            groups.Add(group);
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

        /// <summary>Everything back at its own size and culled as before, for good.</summary>
        public void Restore()
        {
            Apply(1f);
            items.Clear();
            foreach (var group in groups)
            {
                if (group == null || !Originals.TryGetValue(group, out float height)) continue;
                Originals.Remove(group);
                var levels = group.GetLODs();
                if (levels.Length == 0) continue;
                levels[levels.Length - 1].screenRelativeTransitionHeight = height;
                group.SetLODs(levels);
            }
            groups.Clear();
            // Groups destroyed while kept shown (a stand-in nobody took over) leave nothing behind.
            var gone = new List<LODGroup>();
            foreach (var group in Originals.Keys) if (group == null) gone.Add(group);
            foreach (var group in gone) Originals.Remove(group);
        }
    }
}
