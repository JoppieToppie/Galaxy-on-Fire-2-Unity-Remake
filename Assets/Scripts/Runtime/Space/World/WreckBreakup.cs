// WreckBreakup.cs
// Remake mods (2026-11-11, after Chorus's wrecks: SK_*_Wreck skeletal meshes, one bone a piece, flung apart by its
// ABP_Explosion_* anim blueprints' ExplosionPhysics node): a ship with ships.json "wreck" (CustomShipData.wreck) breaks
// into its wreck model's pieces where it explodes (NpcShipDamage.UpdateDying). The model (Modding.ModCrates, the ship's
// own materials) is placed exactly where the hull was; each mesh is a piece that flies out from the wreck's middle at
// "speed" x a tenth of the ship's length a second (each 0.6-1.4 x that) on top of the ship's own motion, slowing by a
// third a second, turning about its own middle at "spin" x 20-90 deg a second; the largest two burn (ShipBurn) for the
// first half; the pieces shrink away over the last quarter of "seconds" and are gone. A visual only: no hull, no
// collision, nothing a multiplayer copy sees (each game breaks its own copy). Remake (2026-11-11, Chorus's wreck
// materials, MF_WreckBurn and the masters' Dissolve): on the hull shader the pieces glow where they burn ("burn", cooling
// over the first half) and disintegrate over the last 40 % with a glowing edge instead of shrinking (HullWreck.hlsl,
// _WreckFx per piece); shadows off once they do. With a "burnMap" (2026-11-11, MF_WreckBurn) the burn is Chorus's own from
// that noise (_WreckBurnMap / _WreckBurn, the temperature in _WreckBurnColor.a; HullWreck.hlsl HullWreckBurn).

using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using UnityEngine;

namespace GoF2Remake.World
{
    public class WreckBreakup : MonoBehaviour
    {
        class Piece
        {
            public Transform t;
            public Renderer r;
            public Vector3 velocity, axis, scale;
            public float spin;
            public ShipBurn burn;
            public bool hull;      // every material on GoF2/Hull: it burns and disintegrates (else it shrinks away)
            public float cells;    // the burn pattern's cells a unit of its mesh (Chorus's Burn_Tiling 4 across the piece)
        }

        /// <summary>Chorus's 1500 K burn (Burn_Temperature) as the remake's HDR glow.</summary>
        public static readonly Color DefaultBurnColor = new Color(3f, 0.9f, 0.2f);
        public const float BurnTiling = 4f, DissolveFrom = 0.6f, EdgeWidth = 0.08f;
        static readonly int WreckFxId = Shader.PropertyToID("_WreckFx"), WreckBurnColorId = Shader.PropertyToID("_WreckBurnColor"),
            WreckBurnId = Shader.PropertyToID("_WreckBurn"), WreckBurnMapId = Shader.PropertyToID("_WreckBurnMap");
        Texture2D burnMap;
        Vector4 burnParams;
        float burnTemperature = 1500f;
        MaterialPropertyBlock block;
        float burnAmount = 1f;
        bool dissolve = true;
        Color burnColor = DefaultBurnColor;

        readonly List<Piece> pieces = new List<Piece>();
        float ageS, lifeS;

        /// <summary>Ship 'ship''s wreck where 'hull' (its model's hull node) is, moving at 'velocity' (Unity / s); null when the
        /// ship has none or it isn't loaded (the plain explosion alone).</summary>
        public static WreckBreakup Spawn(int ship, Transform hull, Vector3 velocity)
        {
            var spec = CustomShips.Get(ship)?.wreck;
            if (spec == null || string.IsNullOrEmpty(spec.model) || hull == null) return null;
            var go = Modding.ModCrates.InstantiateWreck(ship, hull);
            if (go == null) return null;
            go.name = "Wreck";
            return Break(go, spec, velocity, Modding.ModCrates.WreckBurnMap(ship));
        }

        /// <summary>Breaks 'go' (a wreck model placed where the ship was) into its meshes' pieces; 'burnMap' the spec's
        /// "burnMap" loaded (none: the value-noise burn).</summary>
        public static WreckBreakup Break(GameObject go, CustomWreck spec, Vector3 velocity, Texture2D burnMap = null)
        {
            var w = go.AddComponent<WreckBreakup>();
            w.burnMap = burnMap;
            w.Setup(spec, velocity);
            return w;
        }

        /// <summary>How many pieces fly; how long the wreck lasts (s).</summary>
        public int Pieces => pieces.Count;
        public float LifeS => lifeS;

        void Setup(CustomWreck spec, Vector3 velocity)
        {
            lifeS = Mathf.Max(0.5f, spec.seconds);
            burnAmount = Mathf.Clamp01(spec.burn);
            dissolve = spec.dissolve;
            if (spec.burnColor != null && spec.burnColor.Length >= 3) burnColor = new Color(spec.burnColor[0], spec.burnColor[1], spec.burnColor[2]);
            if (burnMap != null)
            {
                burnParams = new Vector4(Mathf.Max(1e-4f, spec.burnTiling), spec.burnOffset, Mathf.Max(1e-3f, spec.burnFalloff),
                                         (spec.burnVertexRed ? 1f : 0f) + (spec.burnUv1 ? 2f : 0f));
                burnTemperature = Mathf.Max(1f, spec.burnTemperature);
            }
            var renderers = GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return;
            var all = renderers[0].bounds;
            foreach (var r in renderers) all.Encapsulate(r.bounds);
            float length = Mathf.Max(all.size.x, Mathf.Max(all.size.y, all.size.z));
            float speed = Mathf.Max(0f, spec.speed) * length * 0.1f;
            foreach (var r in renderers)
            {
                var piece = new Piece { t = r.transform, r = r, hull = OnHullShader(r) };
                var mesh = r.TryGetComponent<MeshFilter>(out var mf) && mf.sharedMesh != null ? mf.sharedMesh.bounds : r.localBounds;
                piece.cells = BurnTiling / Mathf.Max(1e-4f, Mathf.Max(mesh.size.x, Mathf.Max(mesh.size.y, mesh.size.z)));
                var out_ = r.bounds.center - all.center;
                var dir = out_.sqrMagnitude > 1e-6f ? out_.normalized : Random.onUnitSphere;
                piece.velocity = velocity + (dir + Random.insideUnitSphere * 0.35f).normalized * speed * Random.Range(0.6f, 1.4f);
                piece.axis = Random.onUnitSphere;
                piece.spin = Random.Range(20f, 90f) * Mathf.Max(0f, spec.spin);
                pieces.Add(piece);
            }
            // (each piece on its own: the wreck's root no longer carries them)
            foreach (var p in pieces) { p.t.SetParent(transform, true); p.scale = p.t.localScale; }
            pieces.Sort((a, b) => b.r.bounds.size.sqrMagnitude.CompareTo(a.r.bounds.size.sqrMagnitude));
            for (int i = 0; i < Mathf.Min(2, pieces.Count); i++)
            {
                pieces[i].burn = new ShipBurn(pieces[i].t);
                pieces[i].burn.SetBurning(true);
            }
            Paint();
        }

        static bool OnHullShader(Renderer r)
        {
            var ms = r.sharedMaterials;
            if (ms.Length == 0) return false;
            foreach (var m in ms) if (m == null || m.shader == null || m.shader.name != "GoF2/Hull") return false;
            return true;
        }

        /// <summary>The burn's amount at 'u' (0..1 of the wreck's life): 'amount' cooling to none by halfway.</summary>
        public static float BurnAt(float u, float amount) => Mathf.Clamp01(amount) * Mathf.Clamp01(1f - u / 0.5f);

        /// <summary>The share disintegrated at 'u': none until DissolveFrom, all at the end.</summary>
        public static float DissolveAt(float u) => Mathf.Clamp01((u - DissolveFrom) / (1f - DissolveFrom));

        /// <summary>The pieces' burn and disintegration now (the hull shader's _WreckFx).</summary>
        void Paint()
        {
            float u = ageS / lifeS;
            float b = BurnAt(u, burnAmount), d = dissolve ? DissolveAt(u) : 0f;
            block ??= new MaterialPropertyBlock();
            foreach (var p in pieces)
            {
                if (!p.hull || p.r == null) continue;
                p.r.GetPropertyBlock(block);
                block.SetVector(WreckFxId, new Vector4(b, d, p.cells, EdgeWidth));
                var c = burnColor; c.a = burnTemperature;
                block.SetColor(WreckBurnColorId, c);
                if (burnMap != null)
                {
                    block.SetTexture(WreckBurnMapId, burnMap);
                    block.SetVector(WreckBurnId, burnParams);
                }
                p.r.SetPropertyBlock(block);
                if (d > 0f) p.r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;   // (the shadow pass cuts nothing)
            }
        }

        /// <summary>A piece's _WreckBurn now (tests): the burn map's tiling, offset, falloff (0: no map), by vertex red.</summary>
        public Vector4 BurnOf(int piece)
        {
            if (piece < 0 || piece >= pieces.Count || pieces[piece].r == null || !pieces[piece].hull) return Vector4.zero;
            block ??= new MaterialPropertyBlock();
            pieces[piece].r.GetPropertyBlock(block);
            return block.GetVector(WreckBurnId);
        }

        /// <summary>A piece's _WreckFx now (tests); zero when it isn't on the hull shader.</summary>
        public Vector4 FxOf(int piece)
        {
            if (piece < 0 || piece >= pieces.Count || pieces[piece].r == null || !pieces[piece].hull) return Vector4.zero;
            block ??= new MaterialPropertyBlock();
            pieces[piece].r.GetPropertyBlock(block);
            return block.GetVector(WreckFxId);
        }

        void Update() => Step(Time.deltaTime);

        /// <summary>'dt' seconds on: the pieces fly, turn, slow and shrink; the wreck gone at its end.</summary>
        public void Step(float dt)
        {
            ageS += dt;
            float slow = Mathf.Pow(2f / 3f, dt);
            float shrink = Mathf.Clamp01((lifeS - ageS) / (lifeS * 0.25f));
            foreach (var p in pieces)
            {
                if (p.t == null) continue;
                var c = p.r.bounds.center;
                p.t.position += p.velocity * dt;
                p.velocity *= slow;
                p.t.RotateAround(c + p.velocity * dt, p.axis, p.spin * dt);
                if (shrink < 1f && !(p.hull && dissolve))
                {
                    // shrunk about its own middle (the piece's origin may be the model's)
                    var before = p.r.bounds.center;
                    p.t.localScale = p.scale * Mathf.Max(0.001f, shrink);
                    p.t.position += before - p.r.bounds.center;
                }
                if (p.burn != null && ageS > lifeS * 0.5f) { p.burn.SetBurning(false); }
            }
            if (ageS >= lifeS) { if (Application.isPlaying) Destroy(gameObject); else DestroyImmediate(gameObject); return; }
            Paint();
        }
    }
}
