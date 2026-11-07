// Es2Contrails.cs
// Remake mods: EVERSPACE 2's wing-tip contrails on a mod ship with contrail mounts (slotType 6: the ES2 importer's
// SKT_Contrail sockets, 342 of them on its wing modules), as EVERSPACE 2 spawns and drives them (2026-11-02, decompiled:
// BP_ShipBase's InitializeContrails / UpdateContrails in its ubergraph, P_Vehicle_Contrail's emitter and M_Vehicle_Contrail's
// compiled shader; the ES2 importer's research/engine_exhaust.md). Each socket gets P_Vehicle_Contrail attached at the
// socket's transform (its scale sizes the ribbon): a Cascade ribbon of particles born 50 a second at the socket, each
// staying where it was born (world space), 80 cm wide, white with alpha 0.5, living "Opacity" seconds (the emitter's
// lifetime reads the Opacity parameter, 0.01 at least) and scaled in alpha by it again (ColorScaleOverLife). Per tick,
// with the ship's ContrailsEnabled (true by default, a mod's "es2Engine": { "contrails": false } turns it off):
//   Opacity = EngineVFXBoostCurrent x MapRangeClamped(forward . velocity^, 0.7, 1, 0, 1) x min(the socket's largest scale, 1)
// so the contrails show only while boosting and flying nose first (within 45 deg: cos 0.7), and a small socket's are
// fainter; EngineVFXBoostCurrent is the eased boost level the flames already follow (EngineFlame.Boost). Every ship has
// them in EVERSPACE 2 (BP_ShipBase), NPCs too. The material (GoF2/ModEs2Particle kind 5) fades the ribbon toward its end and
// its edges, hides it within 24 cm of the camera and bars it along its length. EVERSPACE 2's centimetres are its metres x
// the kit's scale here (es2Scale). Nothing in the docked station.

using System.Collections.Generic;
using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using UnityEngine;
using UnityEngine.Rendering;

namespace GoF2Remake.Modding
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public class Es2Contrails : MonoBehaviour
    {
        const float Rate = 50f;                // particles a second (ParticleModuleSpawn Rate 50)
        const float HalfWidth = 0.4f;          // EVERSPACE 2 metres: StartSize 80 cm
        const float Alpha = 0.5f;              // StartAlpha 0.5
        const float MinLife = 0.01f;           // the lifetime distribution's MinOutput
        const float AlignLow = 0.7f;           // MapRangeClamped(dot, 0.7, 1, 0, 1)
        /// <summary>The ribbon's colour: EVERSPACE 2's white fog lit by its scene (volumetric per-vertex lighting), which the
        /// remake's unlit blend can't do; a light grey stands in (matched by eye against ES2's boosting wings).</summary>
        public static Color Colour = new Color(0.62f, 0.66f, 0.72f, 1f);

        [System.Serializable]
        struct Socket { public Vector3 at; public float scale; }
        [SerializeField] Socket[] sockets = new Socket[0];
        [SerializeField] float es2Scale = 1f;

        struct Particle { public Vector3 pos; public float age, life; }
        class Trail
        {
            public readonly List<Particle> points = new List<Particle>();
            public float spawnDue;
            public Vector3 last;
            public bool hasLast;
        }

        EngineFlame flame;
        Trail[] trails;
        MeshFilter ribbon;
        bool ready, inStation;
        readonly List<Vector3> v = new List<Vector3>(), n = new List<Vector3>();
        readonly List<Vector2> uv = new List<Vector2>(), uv1 = new List<Vector2>();
        readonly List<Color> col = new List<Color>();
        readonly List<int> tri = new List<int>();

        /// <summary>EVERSPACE 2's contrail opacity: the eased boost level x how nose-first the ship flies (the forward's dot with
        /// the velocity's direction, 0 at 0.7 to 1 at 1) x the socket's scale capped at 1.</summary>
        public static float Opacity(float boost, float forwardDot, float socketScale) =>
            Mathf.Clamp01(boost) * Mathf.Clamp01((forwardDot - AlignLow) / (1f - AlignLow)) * Mathf.Min(Mathf.Max(socketScale, 0f), 1f);

        /// <summary>A particle's life: the opacity in seconds (the emitter's Lifetime reads the Opacity parameter), 0.01 at least.</summary>
        public static float Life(float opacity) => Mathf.Max(MinLife, opacity);

        /// <summary>Adds the contrails for a ship template's contrail mounts (slotType 6; none: nothing). 'es2Scale' is Unity
        /// metres per EVERSPACE 2 metre (the kit's scale); 'effects' the ship's ES2 engine switches (null: all on).</summary>
        public static void Add(GameObject root, IEnumerable<WeaponMount> mounts, float es2Scale, CustomEs2Engine effects = null)
        {
            var list = mounts?.Where(m => m != null && m.slotType == 6).ToList();
            if (list == null || list.Count == 0 || Es2EngineVfx.Shader == null || (effects != null && !effects.contrails)) return;
            var c = root.AddComponent<Es2Contrails>();
            c.es2Scale = es2Scale > 0f ? es2Scale : 1f;
            c.sockets = list.Select(m => new Socket { at = WeaponSystem.MountToLocal(m), scale = m.socketScale > 0f ? m.socketScale : 1f }).ToArray();
        }

        void OnEnable()
        {
            inStation = Visuals.EngineDrive.IsStationModel(transform);
            if (trails != null) foreach (var t in trails) { t.points.Clear(); t.hasLast = false; t.spawnDue = 0f; }
        }

        void OnDestroy()
        {
            if (ribbon != null) { Destroy(ribbon.sharedMesh); Destroy(ribbon.GetComponent<Renderer>().sharedMaterial); }
        }

        void Setup()
        {
            ready = true;
            flame = GetComponent<EngineFlame>();
            trails = sockets.Select(_ => new Trail()).ToArray();
            var go = new GameObject("es2_contrail");
            go.transform.SetParent(transform, false);
            ribbon = go.AddComponent<MeshFilter>();
            // ("_add": the hull collision, shadows, levels of detail and aim spots skip it like the game's additive layers)
            ribbon.sharedMesh = new Mesh { name = "es2_contrail_add" };
            ribbon.sharedMesh.MarkDynamic();
            var r = go.AddComponent<MeshRenderer>();
            var mat = new Material(Es2EngineVfx.Shader) { name = "es2_contrail" };
            mat.SetFloat("_Kind", 5);
            mat.SetFloat("_Gain", 1f);
            mat.SetFloat("_Es2Scale", es2Scale);
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            var noise = ModAssets.Get()?.engineFlame;
            if (noise != null && noise.HasProperty("_NoiseMap")) mat.SetTexture("_NoiseMap", noise.GetTexture("_NoiseMap"));
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.enabled = false;
        }

        void LateUpdate()
        {
            if (!ready) Setup();
            if (sockets.Length == 0) return;
            float dt = Time.deltaTime;
            // (no flames: no eased boost to read, so no contrails; a ship without engines in the hangar has none either)
            float boost = flame != null && !inStation ? flame.Boost : 0f;
            var vel = flame != null ? flame.Velocity : Vector3.zero;
            float dot = vel.sqrMagnitude > 1e-6f ? Vector3.Dot(transform.forward, vel.normalized) : 0f;
            v.Clear(); n.Clear(); uv.Clear(); uv1.Clear(); col.Clear(); tri.Clear();
            float s = es2Scale;
            for (int i = 0; i < sockets.Length; i++)
            {
                var t = trails[i];
                float opacity = Opacity(boost, dot, sockets[i].scale);
                var at = transform.TransformPoint(sockets[i].at);
                for (int k = t.points.Count - 1; k >= 0; k--)
                {
                    var p = t.points[k];
                    p.age += dt;
                    if (p.age >= p.life) t.points.RemoveAt(k); else t.points[k] = p;
                }
                if (opacity > 0f && t.hasLast)
                {
                    // a jump moves the ship in one frame: start again
                    if ((at - t.last).sqrMagnitude > 1e6f) { t.points.Clear(); t.spawnDue = 0f; }
                    else
                    {
                        t.spawnDue += Rate * dt;
                        float life = Life(opacity);
                        while (t.spawnDue >= 1f)
                        {
                            t.spawnDue -= 1f;
                            // (spread along the way flown this frame: the oldest of this frame's births first)
                            float age = t.spawnDue / Rate;
                            t.points.Add(new Particle { pos = Vector3.Lerp(at, t.last, dt > 0f ? Mathf.Clamp01(age / dt) : 0f), age = age, life = life });
                        }
                    }
                }
                else if (opacity <= 0f) t.spawnDue = 0f;
                t.last = at; t.hasLast = true;
                if (t.points.Count == 0) continue;
                // newest first, the live socket as the head; u by length, 0 at the socket to 1 at the end
                var c = new Color(Colour.r, Colour.g, Colour.b, Alpha * opacity);
                var pts = t.points;
                float total = Vector3.Distance(at, pts[pts.Count - 1].pos);
                for (int k = pts.Count - 1; k > 0; k--) total += Vector3.Distance(pts[k].pos, pts[k - 1].pos);
                if (total <= 1e-5f) continue;
                int first = v.Count, count = 0;
                float run = 0f;
                var prev = at;
                var local = transform.InverseTransformPoint(at);
                AddRibbonPoint(local, transform.InverseTransformPoint(pts[pts.Count - 1].pos) - local, 0f, HalfWidth * sockets[i].scale * s, c); count++;
                for (int k = pts.Count - 1; k >= 0; k--)
                {
                    var p = pts[k];
                    run += Vector3.Distance(p.pos, prev);
                    var pl = transform.InverseTransformPoint(p.pos);
                    var next = k > 0 ? transform.InverseTransformPoint(pts[k - 1].pos) : pl + (pl - transform.InverseTransformPoint(prev));
                    AddRibbonPoint(pl, next - pl, run / total, HalfWidth * sockets[i].scale * s, c);
                    prev = p.pos; count++;
                }
                for (int k = 0; k + 1 < count; k++)
                {
                    int a = first + k * 2;
                    tri.AddRange(new[] { a, a + 1, a + 3, a, a + 3, a + 2 });
                }
            }
            EndMesh();
        }

        void AddRibbonPoint(Vector3 at, Vector3 tangent, float u, float halfWidth, Color c)
        {
            var dir = tangent.sqrMagnitude > 1e-10f ? tangent.normalized : Vector3.back;
            for (int side = -1; side <= 1; side += 2)
            {
                v.Add(at); n.Add(dir); uv.Add(new Vector2(u, side < 0 ? 0f : 1f)); uv1.Add(new Vector2(halfWidth, side)); col.Add(c);
            }
        }

        void EndMesh()
        {
            var r = ribbon.GetComponent<Renderer>();
            bool any = tri.Count > 0;
            if (r.enabled != any) r.enabled = any;
            if (!any) return;
            var m = ribbon.sharedMesh;
            m.Clear();
            if (v.Count > 65000) m.indexFormat = IndexFormat.UInt32;
            m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetUVs(1, uv1); m.SetColors(col); m.SetTriangles(tri, 0);
            var bounds = new Bounds(v[0], Vector3.zero);
            float reach = 0f;
            for (int i = 0; i < v.Count; i++) { bounds.Encapsulate(v[i]); reach = Mathf.Max(reach, Mathf.Abs(uv1[i].x)); }
            bounds.Expand(reach * 2f);
            m.bounds = bounds;
        }
    }
}
