// HullWarpIn.cs
// Remake mods (2026-11-11, Chorus's Omen tissue warping in: M_JTU_Faceless's Driver, Edge Thickness and Edge Glow Color):
// an NPC ship with ships.json "warpIn" (CustomShipData.warpIn) arriving in flight materializes over "seconds": its
// renderers on the hull shader are cut away where the noise lies under 1 - progress, their edge glowing in "color" (the
// wreck cut of HullWreck.hlsl run backward: _WreckFx y, w, _WreckBurnColor through a property block), shadows off until it
// is whole. The model may load later than the ship: the renderers are found again while the hierarchy grows.

using System.Collections.Generic;
using GoF2Remake.Data;
using UnityEngine;

namespace GoF2Remake.World
{
    public class HullWarpIn : MonoBehaviour
    {
        static readonly int WreckFxId = Shader.PropertyToID("_WreckFx"), WreckBurnColorId = Shader.PropertyToID("_WreckBurnColor");
        readonly List<(Renderer r, float cells, UnityEngine.Rendering.ShadowCastingMode shadows)> parts = new List<(Renderer, float, UnityEngine.Rendering.ShadowCastingMode)>();
        MaterialPropertyBlock block;
        float ageS, seconds = 2f, edge = 0.02f;
        Color color = new Color(3.45f, 0f, 0f);
        int scannedNodes = -1;

        /// <summary>Starts 'ship''s warp-in (replacing one running).</summary>
        public static HullWarpIn Begin(GameObject ship, CustomWarpIn spec)
        {
            var w = ship.TryGetComponent<HullWarpIn>(out var had) ? had : ship.AddComponent<HullWarpIn>();
            w.ageS = 0f;
            w.seconds = Mathf.Max(0.05f, spec.seconds);
            w.edge = Mathf.Max(1e-3f, spec.edge);
            // (the shader doubles the edge's glow, Chorus's DissolveBoost on the wreck cut)
            if (spec.color != null && spec.color.Length >= 3) w.color = new Color(spec.color[0], spec.color[1], spec.color[2]) * 0.5f;
            w.Step(0f);
            return w;
        }

        /// <summary>How far it has come in, 0..1.</summary>
        public float Progress => Mathf.Clamp01(ageS / seconds);
        public int Parts => parts.Count;

        void Scan()
        {
            if (transform.hierarchyCount == scannedNodes) return;
            scannedNodes = transform.hierarchyCount;
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                if (parts.Exists(p => p.r == r) || !OnHull(r)) continue;
                var b = r.TryGetComponent<MeshFilter>(out var mf) && mf.sharedMesh != null ? mf.sharedMesh.bounds : r.localBounds;
                parts.Add((r, WreckBreakup.BurnTiling / Mathf.Max(1e-4f, Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z))), r.shadowCastingMode));
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        static bool OnHull(Renderer r)
        {
            var ms = r.sharedMaterials;
            if (ms.Length == 0) return false;
            foreach (var m in ms) if (m == null || m.shader == null || m.shader.name != "GoF2/Hull") return false;
            return true;
        }

        /// <summary>'dt' seconds on (Update; the tests step it): the cut shrinks; whole at the end, the component gone.</summary>
        public void Step(float dt)
        {
            ageS += dt;
            Scan();
            block ??= new MaterialPropertyBlock();
            bool done = ageS >= seconds;
            float cut = done ? 0f : 1f - Progress;
            foreach (var (r, cells, shadows) in parts)
            {
                if (r == null) continue;
                r.GetPropertyBlock(block);
                block.SetVector(WreckFxId, new Vector4(0f, cut, cells, edge));
                block.SetColor(WreckBurnColorId, color);
                r.SetPropertyBlock(block);
                if (done) r.shadowCastingMode = shadows;
            }
            if (done) { if (Application.isPlaying) Destroy(this); else DestroyImmediate(this); }
        }

        void Update() => Step(Time.deltaTime);
    }
}
