// EngineFx.cs
// Remake mods: Galaxy on Fire 3's engine effect at a mod ship's exhausts (ships.json "engineFx", Data.CustomEngineFx), in
// place of the glow disc and the exhaust particles, drawn as GoF3 draws it. GoF3 puts two things at each engine helper (its
// data.pdb's EngineAttachment and VfxList, e.g. NivelianEngine = /assets/vfx/engines/nivelian/vfx_engine_nivelian_000.ae4scene
// and TrailNivelian = particles/ship_engine_nivelian.psb):
//   - the engine scene ("meshes": EngineModel, the GoF3 importer's file of it): each part's own mesh drawn with
//     GoF2/ModEngineMesh, the options and values of the shader variant GoF3 compiled for its material, in the engine's frame
//     (GoF3 units x "meshScale", +z backward) at every exhaust, sized by the exhaust's scale (a mount's fxScale, an exhaust
//     style's fit to the nozzle) and spread to its nozzle's outline (all but the sprites); a little shorter at idle; its
//     animations played: "loop" always (the Nivelian spiral's turn), "ignite" as the engines light and a boost starts (the
//     Terran burst), "off" as they go out (the capital engines' flicker);
//   - the trail ("trailParticles" / "contrailParticles", Data.CustomForkEmitter): GoF3's Fork Particle Studio emitters
//     (romfs particles/ship_engine_<race>.psb, ship_engine_contrail.psb; each ship's VfxList names its own) run as the Fork
//     runtime runs them (libgofm.so: frkPEmitterEmit, _frkPEmitterEmitProcess, _frkParticleProcessFullSim): 'rate' particles a
//     second born at the exhaust (or the "contrails" point), spread along the path it moved since the last frame, never
//     moving themselves, so they lie along the wake (none while the ship stands still); each lives 'life' s, sized start ->
//     mid -> end and coloured through four keys by its age; the texture (a vertical streak) drawn on a quad facing the
//     camera, stretched along the wake. "boostParticles": GoF3's booster flash (ad_engine_booster_<race>.psb), emitted at
//     the exhausts only while boosting, riding with the ship ("local": the .psb's flag 0x20000).
// Each exhaust blows along its mount's "rotation" (ModHardpoints.MountTurn: GoF3's engine helper turned, the Vossk hulls'
// engines 8.5-10 deg outward). GoF3's engines stay rigid on the hull (ECS::EngineAttachmentSystem::tryToAttachEngines places
// each once at its helper; only the trails lag): on the player's ship the remake bends them as EVERSPACE 2 bends its flames
// (the user's request, 2026-11-04: EngineFlame.SteerBend, the tip against the velocity x the thrust's forward share), each
// exhaust's parts by u^2 along it (GoF2/ModEngineMesh's _SteerBend; u 0 at the nozzle, 1 at the parts' far end). An exhaust style's colours (ModExhausts: the Customize screen's two colour rows) recolour
// it (GoF2/ModEngineMesh's _Tint: each pixel's hue replaced, its white kept): the first every part but the helix, the second
// the helix and the particles.
// Level: EngineDrive like EngineFlame (0 idle .. 1 full throttle .. 2 a full boost; off while the engine glow part is, a
// quarter-second fade; in the docked station capped at full throttle). Every part is under the engine glow part, so it goes
// with the engines.
// (Until 2026-10-24 the remake drew its own imitation of these, camera-facing flare and cone cards, a ribbon helix and
// wavering ribbon streams; it was removed once GoF3's own were drawn.)

using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.Visuals;
using UnityEngine;
using UnityEngine.Rendering;

namespace GoF2Remake.Modding
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public class EngineFx : MonoBehaviour
    {
        const float M = 0.05f;              // metres per game unit
        const float FadePerSecond = 4f;
        const float Response = 6f;          // 1/s: how fast the effect follows the throttle
        const float MinWakeSpeed = 2f;      // m/s: the Fork trails emit only faster than this
        const float MeshIdleLength = 0.6f;  // the parts' length at idle against full throttle (the remake's: GoF3's stay put)

        static readonly int LevelId = Shader.PropertyToID("_Level");
        static readonly int TintId = Shader.PropertyToID("_Tint");
        static readonly int SteerId = Shader.PropertyToID("_SteerBend");
        static readonly int BendOriginId = Shader.PropertyToID("_BendOrigin");
        static readonly int BendAxisId = Shader.PropertyToID("_BendAxis");

        [SerializeField] GameObject engineGlow;
        [SerializeField] List<Vector3> nozzles = new List<Vector3>();   // root space
        [SerializeField] List<float> scales = new List<float>();         // each exhaust's size (a mount's fxScale, a style's fit)
        [SerializeField] List<Profile> profiles = new List<Profile>();   // each nozzle's outline (ModExhausts' normalized profile)
        [SerializeField] List<Quaternion> turns = new List<Quaternion>();   // the mount's rotation (GoF3's engine helper's turn)
        [SerializeField] List<float> lengths = new List<float>();        // each exhaust's length against its width (an exhaust style's)
        [SerializeField] Color mainTint, secondTint;                      // an exhaust style's colours (alpha 0: GoF3's own)

        [System.Serializable] sealed class Profile { public float[] r; }

        // GoF3's own engine parts (EngineModel): shared meshes and materials, each part's place and animations
        [System.Serializable] sealed class MeshPart
        {
            public Vector3 pos, scale;
            public Quaternion rot;
            public bool sprite, second;
            public EngineAnim[] anims;
        }
        [SerializeField] List<Mesh> partMeshes = new List<Mesh>();
        [SerializeField] List<Material> partMaterials = new List<Material>();
        [SerializeField] List<MeshPart> partInfo = new List<MeshPart>();
        [SerializeField] float meshScale = 1f;
        [SerializeField] CustomForkEmitter forkTrail, forkContrail, forkBoost;
        [SerializeField] Material forkTrailMaterial, forkContrailMaterial, forkBoostMaterial;
        [SerializeField] List<Vector3> contrailPoints = new List<Vector3>();   // root space
        [SerializeField] float forkUnit = 1f;   // Unity metres per unit of the emitters' sizes

        class ForkParticle { public Vector3 pos, along; public float age, life, s0, s1, s2, spacing; }
        class ForkRun
        {
            public readonly List<ForkParticle> particles = new List<ForkParticle>();
            public CustomForkEmitter em;
            public Mesh mesh;
            public MeshRenderer renderer;
            public Vector3 point, last;   // the emitter in root space; its world place last frame
            public bool hasLast;
            public float carry, scale;
            public int engine = -1;       // the exhaust it follows (-1: a contrail point)
            public bool boost;            // emits only while boosting
        }
        readonly List<ForkRun> forkRuns = new List<ForkRun>();

        // each exhaust's holder, its scale and length, and its parts' far end along it (the holder's +z, its own units)
        readonly List<(Transform t, float s, float length, float reach)> meshHolders = new List<(Transform, float, float, float)>();
        readonly List<(Transform t, MeshRenderer r, int part, int engine)> meshParts = new List<(Transform, MeshRenderer, int, int)>();
        readonly List<(Vector4 origin, Vector4 axis)> bends = new List<(Vector4, Vector4)>();   // per holder, this frame
        float igniteMs = -1f, offMs = -1f, offLength, prevBoost;
        bool wasOn;
        readonly List<Vector3> verts = new List<Vector3>();
        readonly List<Vector2> uvs = new List<Vector2>();
        readonly List<Vector2> gains = new List<Vector2>();
        readonly List<Color> vcolours = new List<Color>();
        readonly List<int> tris = new List<int>();
        readonly List<Mesh> owned = new List<Mesh>();
        MaterialPropertyBlock block;
        EngineDrive drive;
        bool ready, inStation;
        float thrust = -1f, boost, level = -1f;
        ShipController ship;
        Vector3 lastPosition, velocity, pilotThrust;   // world m/s; the player's eased input (EngineFlame's)
        bool hasLast;

        /// <summary>Sets it up on a ship template: the parts under 'glowPart' (the engine glow part: they go with the engines),
        /// one per exhaust mount of 'c'.</summary>
        public static void Add(GameObject root, GameObject glowPart, CustomShipData c, ModInfo mod)
        {
            if (c?.engineFx == null || c.mounts == null) return;
            CreateShaped(root, glowPart, c.engineFx, c.mounts, mod, c.assembly ?? c.name);
        }

        /// <summary>Create with each exhaust sized by its mount's fxScale and shaped by its mount's "outline" (the nozzle's
        /// cross-section, ModExhausts.OutlineOf), as an exhaust style is on the same mounts; round without one.</summary>
        public static EngineFx CreateShaped(GameObject host, GameObject glowPart, CustomEngineFx fx, IEnumerable<WeaponMount> mounts, ModInfo mod, string label)
        {
            var shapes = new Dictionary<WeaponMount, ModExhausts.NozzleShape>();
            foreach (var m in mounts) if (m != null && m.slotType == 3 && ModExhausts.OutlineOf(m) is ModExhausts.NozzleShape o) shapes[m] = o;
            return Create(host, glowPart, fx, mounts, mod, label, m => m.fxScale > 0f ? m.fxScale : 1f,
                          centreOf: m => shapes.TryGetValue(m, out var o) ? new Vector2(o.centreX, o.centreY) : Vector2.zero,
                          profileOf: m => shapes.TryGetValue(m, out var o) ? o.Normalized() : null);
        }

        /// <summary>The effect 'fx' at every exhaust mount (slotType 3) of 'mounts' (ship-relative game units, as the host's
        /// own frame: 'host' is the ship's root, or a child of the engine glow part aligned with it, ModExhausts), its parts
        /// under 'glowPart'. 'scaleOf' sizes each exhaust, 'profileOf' gives its nozzle's outline (ModExhausts' normalized
        /// profile: the parts are spread to it; null = round), 'centreOf' moves it in its mount's plane (Unity metres);
        /// 'main' / 'second' (a customized exhaust's colours, ModExhausts) recolour every part but the helix / the helix and
        /// the particles; 'lengthOf' stretches an exhaust's parts along it (x its width's scale; null = 1). Null when the
        /// effect has nothing that can be drawn.</summary>
        public static EngineFx Create(GameObject host, GameObject glowPart, CustomEngineFx fx, IEnumerable<WeaponMount> mounts, ModInfo mod, string label,
                                      System.Func<WeaponMount, float> scaleOf, Color? main = null, Color? second = null,
                                      System.Func<WeaponMount, Vector2> centreOf = null, System.Func<WeaponMount, float[]> profileOf = null,
                                      System.Func<WeaponMount, float> lengthOf = null)
        {
            if (fx == null || host == null || glowPart == null || mounts == null) return null;
            if (!meshShaderLoaded) { meshShaderLoaded = true; meshShader = Resources.Load<Shader>("GoF2Mods/ModEngineMesh"); }
            if (meshShader == null || !meshShader.isSupported) return null;
            var e = host.AddComponent<EngineFx>();
            e.engineGlow = glowPart;
            foreach (var m in mounts)
            {
                if (m == null || m.slotType != 3) continue;
                var turn = ModHardpoints.MountTurn(m);
                var centre = centreOf?.Invoke(m) ?? Vector2.zero;
                e.nozzles.Add(WeaponSystem.MountToLocal(m) + turn * new Vector3(centre.x, centre.y, 0f));
                e.scales.Add(Mathf.Max(0.01f, scaleOf != null ? scaleOf(m) : 1f));
                e.profiles.Add(new Profile { r = profileOf?.Invoke(m) });
                e.turns.Add(turn);
                e.lengths.Add(Mathf.Max(0.1f, lengthOf != null ? lengthOf(m) : 1f));
            }
            e.mainTint = main ?? Color.clear;
            e.secondTint = second ?? Color.clear;
            if (!string.IsNullOrEmpty(fx.meshes)) e.MeshParts(fx, mod, label);
            e.ForkEmitters(fx, mod, label);
            if (e.partMeshes.Count == 0 && e.forkTrailMaterial == null && e.forkBoostMaterial == null && e.forkContrailMaterial == null)
            {
                DestroyImmediate(e);
                return null;
            }
            return e;
        }

        static Shader meshShader;
        static bool meshShaderLoaded;
        static readonly Dictionary<string, (Mesh mesh, Material mat)> partCache = new Dictionary<string, (Mesh, Material)>();
        static int partCacheRevision = -1;

        /// <summary>GoF3's engine parts from fx.meshes: a mesh and a material per part, shared by every ship with that file in
        /// the mod for the set of mods (an exhaust style's colours go on each renderer, not the material).</summary>
        void MeshParts(CustomEngineFx fx, ModInfo mod, string label)
        {
            var file = EngineModels.Get(mod, fx.meshes);
            if (file == null) return;
            if (partCacheRevision != ModManager.Revision) { partCache.Clear(); partCacheRevision = ModManager.Revision; }
            meshScale = fx.meshScale > 0f ? fx.meshScale : 1f;
            for (int k = 0; k < file.parts.Length; k++)
            {
                var p = file.parts[k];
                if (p?.v == null || p.i == null || p.v.Length < 9) continue;
                string key = $"{mod?.Id}|{fx.meshes}|{k}|{fx.meshGlow}";
                if (!partCache.TryGetValue(key, out var made) || made.mesh == null || made.mat == null)
                {
                    made = (PartMesh(p, $"{label} {p.name}"), PartMaterial(p, mod, fx, $"{label} {p.name}"));
                    if (made.mat == null) continue;
                    partCache[key] = made;
                }
                partMeshes.Add(made.mesh);
                partMaterials.Add(made.mat);
                partInfo.Add(new MeshPart
                {
                    pos = Vec(p.pos, Vector3.zero), scale = Vec(p.scale, Vector3.one),
                    rot = p.rot != null && p.rot.Length >= 4 ? new Quaternion(p.rot[0], p.rot[1], p.rot[2], p.rot[3]) : Quaternion.identity,
                    sprite = p.sprite, anims = p.anims, second = IsHelix(p),
                });
            }
        }

        /// <summary>The part an exhaust style's second colour recolours with the trails: GoF3's helix (the Nivelian
        /// engine's "spiral"), or a part a mod marks "tint": "second".</summary>
        static bool IsHelix(EnginePart p) => p.tint == "second" || p.tint != "main" && p.name != null && p.name.EndsWith("spiral");

        static Vector3 Vec(float[] a, Vector3 fallback) => a != null && a.Length >= 3 ? new Vector3(a[0], a[1], a[2]) : fallback;
        static Color Col(float[] a) => a != null && a.Length >= 4 ? new Color(a[0], a[1], a[2], a[3]) : a != null && a.Length >= 3 ? new Color(a[0], a[1], a[2], 1f) : Color.white;

        static Mesh PartMesh(EnginePart p, string name)
        {
            int n = p.v.Length / 3;
            var v = new Vector3[n]; var nm = new Vector3[n]; var uv = new Vector2[n]; var off = new Vector2[n]; var c = new Color[n];
            float reach = 0f;
            for (int j = 0; j < n; j++)
            {
                v[j] = new Vector3(p.v[3 * j], p.v[3 * j + 1], p.v[3 * j + 2]);
                nm[j] = p.n != null && p.n.Length >= 3 * n ? new Vector3(p.n[3 * j], p.n[3 * j + 1], p.n[3 * j + 2]) : Vector3.forward;
                if (p.uv != null && p.uv.Length >= 2 * n) uv[j] = new Vector2(p.uv[2 * j], p.uv[2 * j + 1]);
                if (p.o != null && p.o.Length >= 2 * n) { off[j] = new Vector2(p.o[2 * j], p.o[2 * j + 1]); reach = Mathf.Max(reach, off[j].magnitude); }
                c[j] = p.c != null && p.c.Length >= 4 * n ? new Color(p.c[4 * j], p.c[4 * j + 1], p.c[4 * j + 2], p.c[4 * j + 3]) : Color.white;
            }
            var m = new Mesh { name = name };
            if (n > 65000) m.indexFormat = IndexFormat.UInt32;
            m.SetVertices(v); m.SetNormals(nm); m.SetUVs(0, uv); m.SetUVs(1, off); m.SetColors(c);
            m.SetTriangles(p.i, 0);
            m.RecalculateBounds();
            // a sprite's corners and the wobble reach past the vertices
            float grow = 2f * (reach + (p.vertexAnim ? Mathf.Abs(p.vaStrength) : 0f));
            if (grow > 0f) { var b = m.bounds; b.Expand(grow); m.bounds = b; }
            return m;
        }

        static Material PartMaterial(EnginePart p, ModInfo mod, CustomEngineFx fx, string name)
        {
            var mat = new Material(meshShader) { name = name, renderQueue = (int)RenderQueue.Transparent };
            Texture2D Tex(string file)
            {
                if (string.IsNullOrEmpty(file)) return null;
                var t = ModMaterials.Texture(mod, file, true);   // raw: GoF3's maths runs on the stored values
                if (t == null) { mod?.Warnings.Add($"engineFx: {file} isn't a PNG / JPG the game can read"); return null; }
                t.wrapMode = TextureWrapMode.Repeat;
                return t;
            }
            var tex = p.useTex ? Tex(p.texture) : null;
            if (p.useTex && tex == null) return null;
            if (tex != null) mat.SetTexture("_MainTex", tex);
            var flow = p.flow ? Tex(p.flowMap) : null;
            if (flow != null) mat.SetTexture("_FlowMap", flow);
            mat.SetColor("_Diffuse", Col(p.diffuse));
            mat.SetColor("_C1", Col(p.c1));
            mat.SetColor("_C2", Col(p.c2));
            mat.SetFloat("_Opacity", p.opacity);
            mat.SetFloat("_ColorizeMul", p.colorizeMul);
            mat.SetFloat("_AngleMin", p.angleMin);
            mat.SetFloat("_AngleMax", p.angleMax);
            mat.SetFloat("_PulseMin", p.pulseMin);
            mat.SetFloat("_PulseMax", p.pulseMax);
            mat.SetFloat("_PulseFreq", p.pulseFreq);
            mat.SetVector("_UVSpeed", p.uvSpeed != null && p.uvSpeed.Length >= 2 ? new Vector4(p.uvSpeed[0], p.uvSpeed[1], 0f, 0f) : Vector4.zero);
            mat.SetFloat("_FlowTiling", p.flowTiling);
            mat.SetFloat("_FlowStrength", p.flowStrength);
            mat.SetFloat("_VAFreq", p.vaFreq);
            mat.SetFloat("_VAStrength", p.vaStrength);
            mat.SetFloat("_Alpha", p.alpha);
            mat.SetFloat("_Intensity", p.intensity > 0f ? p.intensity : 4f);
            mat.SetFloat("_Glow", fx.meshGlow > 0f ? fx.meshGlow : 1f);
            mat.SetFloat("_Sprite", p.sprite ? 1f : 0f);
            mat.SetFloat("_UseTex", tex != null ? 1f : 0f);
            mat.SetFloat("_LumAlpha", p.lumAlpha ? 1f : 0f);
            mat.SetFloat("_RgbFromDc", p.rgbFromDc ? 1f : 0f);
            mat.SetFloat("_MulDc", p.mulDc ? 1f : 0f);
            mat.SetFloat("_Flow", flow != null ? 1f : 0f);
            mat.SetFloat("_ScrollDirect", p.scrollDirect ? 1f : 0f);
            mat.SetFloat("_ScrollFlow", p.scrollFlow ? 1f : 0f);
            mat.SetFloat("_Colorize", p.colorize);
            mat.SetFloat("_ColorizeVtx", p.colorizeVtx ? 1f : 0f);
            mat.SetFloat("_Angle", p.angle);
            mat.SetFloat("_Pulsate", p.pulsate ? 1f : 0f);
            mat.SetFloat("_VertexAnim", p.vertexAnim ? 1f : 0f);
            mat.SetFloat("_OpaqueVtxAlpha", p.opaqueVtxAlpha || p.vertexAnim ? 1f : 0f);
            bool additive = p.blend != 1;
            mat.SetFloat("_Additive", additive ? 1f : 0f);
            mat.SetFloat("_SrcBlend", (float)(additive ? BlendMode.One : BlendMode.SrcAlpha));
            mat.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            // (both faces: the engine's frame is mirrored onto the remake's, which turns GoF3's winding over; its angle
            // blend already hides the faces it would cull)
            mat.SetFloat("_Cull", (float)CullMode.Off);
            return mat;
        }

        /// <summary>GoF3's trail / contrail / booster emitters: their materials (GoF2/ModEngineMesh, additive, the texture read
        /// raw) and the contrail points.</summary>
        void ForkEmitters(CustomEngineFx fx, ModInfo mod, string label)
        {
            forkUnit = (fx.meshScale > 0f ? fx.meshScale : 1f) * M;
            Material Mat(CustomForkEmitter em, string part)
            {
                if (em?.Any != true) return null;
                if (partCacheRevision != ModManager.Revision) { partCache.Clear(); partCacheRevision = ModManager.Revision; }
                string key = $"{mod?.Id}|fork|{part}|{em.texture}|{fx.meshGlow}";
                if (partCache.TryGetValue(key, out var made) && made.mat != null) return made.mat;
                var p = new EnginePart { name = part, texture = em.texture, useTex = true, blend = 2, opacity = 1f };
                var mat = PartMaterial(p, mod, fx, $"{label} {part}");
                if (mat != null) { mat.SetFloat("_VertexGain", 1f); mat.SetFloat("_Intensity", 1f); partCache[key] = (null, mat); }
                return mat;
            }
            forkTrailMaterial = Mat(fx.trailParticles, "trail particles");
            if (forkTrailMaterial != null) forkTrail = fx.trailParticles;
            forkBoostMaterial = Mat(fx.boostParticles, "boost particles");
            if (forkBoostMaterial != null) forkBoost = fx.boostParticles;
            forkContrailMaterial = Mat(fx.contrailParticles, "contrail particles");
            if (forkContrailMaterial != null && fx.contrails != null)
            {
                forkContrail = fx.contrailParticles;
                for (int k = 0; k + 2 < fx.contrails.Length; k += 3)
                    contrailPoints.Add(new Vector3(-fx.contrails[k], fx.contrails[k + 1], fx.contrails[k + 2]) * M);
            }
        }

        /// <summary>A copy of 'mesh' with each vertex pushed out from the axis to nozzle i's outline (its x / y; the
        /// engine frame's x is the mount's).</summary>
        Mesh Warped(Mesh mesh, int i)
        {
            var copy = Instantiate(mesh);
            copy.name = mesh.name + "_shaped";
            var v = copy.vertices;
            for (int j = 0; j < v.Length; j++)
            {
                if (v[j].x == 0f && v[j].y == 0f) continue;
                float f = Factor(i, Mathf.Atan2(v[j].y, v[j].x));
                v[j] = new Vector3(v[j].x * f, v[j].y * f, v[j].z);
            }
            copy.vertices = v;
            copy.RecalculateBounds();
            owned.Add(copy);
            return copy;
        }

        void OnEnable()
        {
            drive?.Reset();
            hasLast = false;
            ship = null;
            inStation = EngineDrive.IsStationModel(transform);
        }

        void OnDestroy()
        {
            foreach (var m in owned) if (m != null) Destroy(m);
        }

        void Setup()
        {
            ready = true;
            block = new MaterialPropertyBlock();
            drive = new EngineDrive(transform);
            if (engineGlow == null) return;
            var parent = engineGlow.transform;
            if (forkContrail?.Any == true && forkContrailMaterial != null)
                for (int k = 0; k < contrailPoints.Count; k++)
                    forkRuns.Add(ForkRunAt(parent, $"engine_fx_contrail_{k}", forkContrail, forkContrailMaterial, contrailPoints[k], forkUnit, -1));
            for (int i = 0; i < nozzles.Count; i++)
            {
                var at = transform.TransformPoint(nozzles[i]);
                if (partMeshes.Count > 0)
                {
                    // GoF3's engine frame on the exhaust: its x / y the mount's, its +z backward (the mount's -z), in GoF3 units
                    var holder = new GameObject($"engine_fx_mesh_{i}").transform;
                    holder.SetParent(parent, false);
                    holder.position = at;
                    holder.rotation = transform.rotation * turns[i];
                    float s = meshScale * M * scales[i] / Mathf.Max(1e-4f, parent.lossyScale.x);
                    float length = i < lengths.Count ? lengths[i] : 1f;
                    holder.localScale = new Vector3(s, s, -s * length);
                    float reach = 0f;
                    for (int k = 0; k < partMeshes.Count; k++)
                    {
                        var info = partInfo[k];
                        var mesh = Shaped(i) && !info.sprite ? Warped(partMeshes[k], i) : partMeshes[k];
                        var t = Child(holder, $"engine_fx_part_{i}_{k}", mesh, partMaterials[k], at, out var r);
                        t.localPosition = info.pos; t.localRotation = info.rot; t.localScale = info.scale;
                        Tint(r, info.second ? secondTint : mainTint);
                        meshParts.Add((t, r, k, meshHolders.Count));
                        reach = Mathf.Max(reach, FarEnd(mesh.bounds, info));
                        if (info.anims != null)
                            foreach (var a in info.anims) if (a?.kind == "off") offLength = Mathf.Max(offLength, a.length);
                    }
                    meshHolders.Add((holder, s, length, reach));
                    // the bend reaches past the parts' bounds (a share of the exhaust's length sideways): grow them so a
                    // bent plume isn't culled with its nozzle off screen
                    foreach (var (t, r, _, engine) in meshParts)
                        if (engine == meshHolders.Count - 1 && r != null)
                        {
                            var b = r.localBounds;
                            b.Expand(2f * EngineFlame.SteerShare * reach / Mathf.Max(1e-4f, MinAbs(t.localScale)));
                            r.localBounds = b;
                        }
                }
                if (forkTrail?.Any == true && forkTrailMaterial != null)
                    forkRuns.Add(ForkRunAt(parent, $"engine_fx_fork_{i}", forkTrail, forkTrailMaterial, nozzles[i], forkUnit * scales[i], i));
                if (forkBoost?.Any == true && forkBoostMaterial != null)
                {
                    var run = ForkRunAt(parent, $"engine_fx_boost_{i}", forkBoost, forkBoostMaterial, nozzles[i], forkUnit * scales[i], i);
                    run.boost = true;
                    forkRuns.Add(run);
                }
            }
            foreach (var run in forkRuns) Tint(run.renderer, secondTint);
        }

        /// <summary>How far a part reaches along its holder's +z (backward along the exhaust), in the holder's units: its mesh
        /// bounds' corners placed by the part's rest transform.</summary>
        static float FarEnd(Bounds b, MeshPart info)
        {
            float far = 0f;
            for (int c = 0; c < 8; c++)
            {
                var corner = new Vector3((c & 1) == 0 ? b.min.x : b.max.x, (c & 2) == 0 ? b.min.y : b.max.y, (c & 4) == 0 ? b.min.z : b.max.z);
                far = Mathf.Max(far, (info.pos + info.rot * Vector3.Scale(info.scale, corner)).z);
            }
            return far;
        }

        static float MinAbs(Vector3 v) => Mathf.Min(Mathf.Abs(v.x), Mathf.Min(Mathf.Abs(v.y), Mathf.Abs(v.z)));

        /// <summary>An exhaust style's colour on one renderer (GoF2/ModEngineMesh's _Tint; alpha 0 = GoF3's own).</summary>
        void Tint(Renderer r, Color c)
        {
            if (r == null || c.a <= 0f) return;
            r.GetPropertyBlock(block);
            block.SetColor(TintId, new Color(c.r, c.g, c.b, 1f));
            r.SetPropertyBlock(block);
        }

        ForkRun ForkRunAt(Transform parent, string name, CustomForkEmitter em, Material mat, Vector3 point, float scale, int engine)
        {
            var mesh = new Mesh { name = name };
            mesh.MarkDynamic();
            owned.Add(mesh);
            var t = Child(parent, name, mesh, mat, parent.position, out var r);
            t.localPosition = Vector3.zero; t.localRotation = Quaternion.identity; t.localScale = Vector3.one;
            return new ForkRun { em = em, mesh = mesh, renderer = r, point = point, scale = scale, engine = engine };
        }

        static Transform Child(Transform parent, string name, Mesh mesh, Material mat, Vector3 at, out MeshRenderer r)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = at;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go.transform;
        }

        void LateUpdate()
        {
            if (!ready) Setup();
            if (engineGlow == null || nozzles.Count == 0 && forkRuns.Count == 0) return;
            float dt = Time.deltaTime;
            float d = drive.Sample(dt, engineGlow);
            if (inStation && !drive.Previewing) d = Mathf.Min(d, 1f);
            float t = Mathf.Clamp01(d), b = Mathf.Clamp01(d - 1f);
            if (thrust < 0f) { thrust = t; boost = b; }
            else
            {
                float k = 1f - Mathf.Exp(-dt * Response);
                thrust = Mathf.Lerp(thrust, t, k);
                boost = Mathf.Lerp(boost, b, k);
            }
            bool on = engineGlow.activeInHierarchy && !drive.Off;
            level = level < 0f ? (on ? 1f : 0f) : Mathf.MoveTowards(level, on ? 1f : 0f, Time.unscaledDeltaTime * FadePerSecond);
            if (meshParts.Count > 0) { UpdatePilot(dt, on); UpdateMeshes(dt, on); }
            if (forkRuns.Count > 0) UpdateFork(dt, Camera.main, on);
        }

        /// <summary>The ship's velocity and, on the player's ship in space, its eased input (as EngineFlame eases it: EVERSPACE
        /// 2's EaseEngineVFX), for the steering bend.</summary>
        void UpdatePilot(float dt, bool on)
        {
            var p = transform.position;
            if (hasLast && dt > 0f)
            {
                var v = (p - lastPosition) / dt;
                // A jump or respawn moves the ship in one frame: ignore it.
                if (v.magnitude < FlightModel.BaseSpeed * 1000f * M * 20f) velocity = Vector3.Lerp(velocity, v, 1f - Mathf.Exp(-dt * 20f));
            }
            lastPosition = p; hasLast = true;
            if (ship == null) ship = GetComponentInParent<ShipController>();
            var target = Vector3.zero;
            if (ship != null && !inStation)
            {
                target = EngineFlame.PilotInput(ship);
                if (!on) target.z = Mathf.Min(0f, target.z);
            }
            float k = 1f - Mathf.Exp(-dt * (target.magnitude > pilotThrust.magnitude ? EngineFlame.RiseRate : EngineFlame.FallRate));
            pilotThrust = Vector3.Lerp(pilotThrust, target, k);
        }

        /// <summary>GoF3's Fork emitters: age and drop the particles, emit while the engines run (spread along the path the
        /// emitter moved), and build each run's quads facing the camera, stretched along the wake.</summary>
        void UpdateFork(float dt, Camera cam, bool on)
        {
            var eye = cam != null ? cam.transform.position : transform.position - transform.forward * 100f;
            foreach (var run in forkRuns)
            {
                var em = run.em; var ps = run.particles;
                for (int j = ps.Count - 1; j >= 0; j--) { ps[j].age += dt; if (ps[j].age >= ps[j].life) ps.RemoveAt(j); }
                var here = transform.TransformPoint(run.point);
                if (!run.hasLast || (here - run.last).sqrMagnitude > 1e8f) run.last = here;   // (a jump: no wake across it)
                // a wake needs a ship moving through it: standing still (the hangar's Customize preview, a ship at rest) the
                // particles piled up on the nozzle into a blot GoF3's ship viewer never shows (a local one rides along anyway)
                bool moving = em.local || (here - run.last).magnitude > MinWakeSpeed * dt;
                if (on && moving && level > 0.02f && dt > 0f && (!run.boost || boost > 0.05f))
                {
                    var moved = here - run.last;
                    var along = moved.sqrMagnitude > 1e-8f ? moved.normalized : (run.engine >= 0 ? -Back(run.engine) : -transform.forward);
                    run.carry += em.rate * dt;
                    int n = Mathf.Min((int)run.carry, Mathf.Max(0, em.max - ps.Count));
                    float spacing = n > 0 ? moved.magnitude / n : 0f;
                    run.carry -= (int)run.carry;
                    for (int j = 0; j < n; j++)
                    {
                        float f = (j + 1f) / n, age = (1f - f) * dt;   // born along the way, aged by when
                        ps.Add(new ForkParticle
                        {
                            pos = em.local ? run.point : Vector3.Lerp(run.last, here, f),
                            along = em.local ? transform.InverseTransformDirection(along) : along, age = age, life = Range(em.life),
                            s0 = Range(em.sizeStart), s1 = Range(em.sizeMid ?? em.sizeStart), s2 = Range(em.sizeEnd ?? em.sizeStart), spacing = spacing,
                        });
                    }
                }
                run.last = here; run.hasLast = true;
                BuildFork(run, eye);
            }
        }

        static float Range(float[] r) => r == null || r.Length == 0 ? 0f : r.Length == 1 ? r[0] : Random.Range(r[0], r[1]);

        static Color ForkColour(CustomForkEmitter em, float t)
        {
            var c = em.colors; var k = em.colorTimes;
            if (c == null || c.Length < 16) return Color.white;
            Color Key(int i) => new Color(c[4 * i], c[4 * i + 1], c[4 * i + 2], c[4 * i + 3]);
            float t1 = k != null && k.Length >= 4 ? k[1] : 1f / 3f, t2 = k != null && k.Length >= 4 ? k[2] : 2f / 3f;
            if (t < t1) return Color.Lerp(Key(0), Key(1), t1 > 0f ? t / t1 : 1f);
            if (t < t2) return Color.Lerp(Key(1), Key(2), t2 > t1 ? (t - t1) / (t2 - t1) : 1f);
            return Color.Lerp(Key(2), Key(3), t2 < 1f ? (t - t2) / (1f - t2) : 1f);
        }

        void BuildFork(ForkRun run, Vector3 eye)
        {
            var mesh = run.mesh; var space = run.renderer.transform; var em = run.em;
            verts.Clear(); uvs.Clear(); vcolours.Clear(); tris.Clear(); gains.Clear();
            foreach (var q in run.particles)
            {
                float t = Mathf.Clamp01(q.age / Mathf.Max(1e-4f, q.life)), m = em.sizeMidTime;
                float size = (t < m ? Mathf.Lerp(q.s0, q.s1, m > 0f ? t / m : 1f) : Mathf.Lerp(q.s1, q.s2, m < 1f ? (t - m) / (1f - m) : 1f)) * run.scale;
                if (size <= 0f) continue;
                var pos = em.local ? transform.TransformPoint(q.pos) : q.pos;   // (a local one rides with the ship)
                var dir = em.local ? transform.TransformDirection(q.along) : q.along;
                var view = (eye - pos).normalized;
                var along = dir - view * Vector3.Dot(dir, view);
                if (along.sqrMagnitude < 1e-8f) along = Vector3.Cross(view, Vector3.up);
                along.Normalize();
                var side = Vector3.Cross(along, view) * (0.5f * size);
                along *= 0.5f * size;
                int b = verts.Count;
                verts.Add(space.InverseTransformPoint(pos - along - side)); uvs.Add(new Vector2(0f, 0f));
                verts.Add(space.InverseTransformPoint(pos - along + side)); uvs.Add(new Vector2(1f, 0f));
                verts.Add(space.InverseTransformPoint(pos + along + side)); uvs.Add(new Vector2(1f, 1f));
                verts.Add(space.InverseTransformPoint(pos + along - side)); uvs.Add(new Vector2(0f, 1f));
                var c = ForkColour(em, t);
                vcolours.Add(c); vcolours.Add(c); vcolours.Add(c); vcolours.Add(c);
                // GoF3's gamma-space sum of the overlapping streaks (GoF2/ModEngineMesh's _VertexGain): N^1.2, N the overlap
                float overlap = Mathf.Clamp(q.spacing > 1e-4f ? size / q.spacing : em.max, 1f, Mathf.Max(1f, em.max));
                var g = new Vector2(Mathf.Pow(overlap, 1.2f), 0f);
                gains.Add(g); gains.Add(g); gains.Add(g); gains.Add(g);
                tris.AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3 });
            }
            mesh.Clear();
            if (verts.Count == 0) return;
            mesh.SetVertices(verts); mesh.SetUVs(0, uvs); mesh.SetUVs(1, gains); mesh.SetColors(vcolours); mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
        }

        /// <summary>GoF3's engine parts: shorter at idle, the animations played, the level (the engines' fade, or their
        /// "off" animation's flicker) on each part.</summary>
        void UpdateMeshes(float dt, bool on)
        {
            if (on && !wasOn) { igniteMs = 0f; offMs = -1f; }
            else if (!on && wasOn && offLength > 0f) offMs = 0f;
            wasOn = on;
            if (boost > 0.05f && prevBoost <= 0.05f) igniteMs = 0f;
            prevBoost = boost;
            float ms = dt * 1000f;
            if (igniteMs >= 0f) igniteMs += ms;
            if (offMs >= 0f) { offMs += ms; if (offMs > offLength) offMs = -1f; }
            float lengthScale = Mathf.Lerp(MeshIdleLength, 1f, thrust) * (1f + 0.3f * boost);
            Vector4 steer = ship != null && !inStation ? (Vector4)EngineFlame.SteerBend(velocity, pilotThrust) : Vector4.zero;
            bends.Clear();
            foreach (var (h, s, length, reach) in meshHolders)
            {
                if (h == null) { bends.Add((Vector4.zero, Vector4.zero)); continue; }
                h.localScale = new Vector3(s, s, -s * length * lengthScale);
                // the exhaust's nozzle and far end this frame (its length follows the throttle)
                var o = h.position;
                var along = h.TransformPoint(0f, 0f, reach) - o;
                float l = along.magnitude;
                var dir = l > 1e-5f ? along / l : Vector3.zero;
                bends.Add((new Vector4(o.x, o.y, o.z, 0f), new Vector4(dir.x, dir.y, dir.z, l > 1e-5f ? l : 0f)));
            }
            float shown = offMs >= 0f ? 1f : level;
            float loopMs = Time.time * 1000f;
            bool igniting = false;
            foreach (var (t, r, k, engine) in meshParts)
            {
                if (t == null) continue;
                var info = partInfo[k];
                float alpha = 1f;
                if (info.anims != null && info.anims.Length > 0)
                {
                    Vector3 pos = info.pos, scl = info.scale;
                    var q = new Vector4(info.rot.x, info.rot.y, info.rot.z, info.rot.w);
                    foreach (var a in info.anims)
                    {
                        if (a?.channels == null || a.length <= 0f) continue;
                        float at;
                        if (a.kind == "loop") at = loopMs % a.length;
                        else if (a.kind == "ignite") { if (igniteMs < 0f || igniteMs > a.length) continue; at = igniteMs; igniting = true; }
                        else if (a.kind == "off") { if (offMs < 0f) continue; at = offMs; }
                        else continue;
                        foreach (var c in a.channels)
                        {
                            if (c?.name == null) continue;
                            float v = c.Sample(at);
                            switch (c.name)
                            {
                                case "alpha": alpha *= v; break;
                                case "translation.x": pos.x = v; break;
                                case "translation.y": pos.y = v; break;
                                case "translation.z": pos.z = v; break;
                                case "scale.x": scl.x = v; break;
                                case "scale.y": scl.y = v; break;
                                case "scale.z": scl.z = v; break;
                                case "rotation.x": q.x = v; break;
                                case "rotation.y": q.y = v; break;
                                case "rotation.z": q.z = v; break;
                                case "rotation.w": q.w = v; break;
                            }
                        }
                    }
                    // (every frame: a part an animation left, the "off" flicker's end, comes back to its rest)
                    {
                        var n = q.sqrMagnitude > 1e-8f ? q.normalized : new Vector4(0f, 0f, 0f, 1f);
                        t.localPosition = pos; t.localRotation = new Quaternion(n.x, n.y, n.z, n.w); t.localScale = scl;
                    }
                }
                float lvl = shown * alpha;
                if (r == null) continue;
                r.enabled = lvl > 0.001f;
                if (!r.enabled) continue;
                r.GetPropertyBlock(block);
                block.SetFloat(LevelId, lvl);
                var (origin, axis) = bends[engine];
                block.SetVector(SteerId, steer);
                block.SetVector(BendOriginId, origin);
                block.SetVector(BendAxisId, axis);
                r.SetPropertyBlock(block);
            }
            if (!igniting && igniteMs >= 0f) igniteMs = -1f;
        }

        /// <summary>The way exhaust 'i' blows (world space): the ship's backward, turned by the mount.</summary>
        Vector3 Back(int i) => transform.rotation * (turns[i] * Vector3.back);

        /// <summary>Nozzle i's outline against its mean radius in a direction of its mount's plane (1: round).</summary>
        float Factor(int i, float angle) => i < profiles.Count ? ModExhausts.FactorOf(profiles[i]?.r, angle) : 1f;

        bool Shaped(int i) => i < profiles.Count && profiles[i]?.r != null;
    }
}
