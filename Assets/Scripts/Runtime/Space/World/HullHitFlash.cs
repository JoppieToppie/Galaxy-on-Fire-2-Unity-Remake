// HullHitFlash.cs
// Remake mods (2026-11-11, Chorus's M_Phoenix hit flash, its MF_HitEffect): the player's hull flashes where it is hit
// (Flight.PlayerHealth.OnHit, armour or hull hits). Its renderers whose materials have a "hit" block (ModMaterials: GoF2/Hull
// _HitFx.w > 0) get the hit's world point (_HitPos) and the flash's age 0..1 over its duration (_HitFx.x; 1 = done) through
// a property block; the hull shader draws the ring and the glow (HullForwardPass.hlsl HitFlash). The point: where the shot
// came in, on the hull's bounds (Target.lastHitVector against the renderers' box).

using System.Collections.Generic;
using UnityEngine;

namespace GoF2Remake.World
{
    public class HullHitFlash : MonoBehaviour
    {
        static readonly int HitFxId = Shader.PropertyToID("_HitFx"), HitPosId = Shader.PropertyToID("_HitPos");
        readonly List<(Renderer r, Vector4 fx)> flashing = new List<(Renderer, Vector4)>();
        MaterialPropertyBlock block;
        float ageS = float.MaxValue;
        bool scanned;
        int scannedNodes;   // the hierarchy's size at the last scan: a new model rescans

        /// <summary>The ship's (added once).</summary>
        public static HullHitFlash For(GameObject ship) =>
            ship.TryGetComponent<HullHitFlash>(out var f) ? f : ship.AddComponent<HullHitFlash>();

        /// <summary>The renderers with a hit block, found again (a new hull, its model loaded later).</summary>
        public int Rescan()
        {
            flashing.Clear();
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                var ms = r.sharedMaterials;
                foreach (var m in ms)
                {
                    if (m == null || !m.HasProperty(HitFxId)) continue;
                    var fx = m.GetVector(HitFxId);
                    if (fx.w > 0f) { flashing.Add((r, fx)); break; }
                }
            }
            scanned = true;
            scannedNodes = transform.hierarchyCount;
            return flashing.Count;
        }

        /// <summary>A hit from 'incoming' (the shot's direction of travel; zero: the nose).</summary>
        public void Flash(Vector3 incoming)
        {
            if (!scanned || transform.hierarchyCount != scannedNodes || flashing.Exists(f => f.r == null)) Rescan();
            if (flashing.Count == 0) return;
            var box = flashing[0].r.bounds;
            foreach (var f in flashing) box.Encapsulate(f.r.bounds);
            var from = incoming.sqrMagnitude > 1e-10f ? -incoming.normalized : transform.forward;
            ageS = 0f;
            block ??= new MaterialPropertyBlock();
            var at = PointOn(box, from);
            foreach (var (r, _) in flashing)
            {
                r.GetPropertyBlock(block);
                block.SetVector(HitPosId, at);
                r.SetPropertyBlock(block);
            }
            Apply();
        }

        /// <summary>Where a shot from 'from' (unit, toward the shooter) meets the box: its centre pushed out to the face.</summary>
        public static Vector3 PointOn(Bounds box, Vector3 from)
        {
            var e = box.extents;
            float t = float.MaxValue;
            if (Mathf.Abs(from.x) > 1e-6f) t = Mathf.Min(t, e.x / Mathf.Abs(from.x));
            if (Mathf.Abs(from.y) > 1e-6f) t = Mathf.Min(t, e.y / Mathf.Abs(from.y));
            if (Mathf.Abs(from.z) > 1e-6f) t = Mathf.Min(t, e.z / Mathf.Abs(from.z));
            return box.center + from * (t < float.MaxValue ? t : 0f);
        }

        /// <summary>The flash's age now (_HitFx.x), each renderer by its own duration.</summary>
        void Apply()
        {
            block ??= new MaterialPropertyBlock();
            foreach (var (r, fx) in flashing)
            {
                if (r == null) continue;
                r.GetPropertyBlock(block);
                block.SetVector(HitFxId, new Vector4(Mathf.Clamp01(ageS / Mathf.Max(1e-3f, fx.z)), fx.y, fx.z, fx.w));
                r.SetPropertyBlock(block);
            }
        }

        /// <summary>The flash's age (s) as the shader reads it: 1 = over (tests).</summary>
        public float AgeOf(int i) => i >= 0 && i < flashing.Count && flashing[i].r != null && Fx(i).x < 1f ? Fx(i).x : 1f;
        Vector4 Fx(int i) { block ??= new MaterialPropertyBlock(); flashing[i].r.GetPropertyBlock(block); return block.GetVector(HitFxId); }

        /// <summary>'dt' seconds on (Update; the tests step it).</summary>
        public void Step(float dt)
        {
            if (ageS == float.MaxValue) return;
            float longest = 0f;
            foreach (var (_, fx) in flashing) longest = Mathf.Max(longest, fx.z);
            ageS += dt;
            Apply();
            if (ageS >= longest) ageS = float.MaxValue;
        }

        void Update() => Step(Time.deltaTime);
    }
}
