// Es2Headlights.cs
// Remake mods: a mod ship's own headlight fixtures (mounts of slotType 7: the ES2 importer's SKT_HeadLight sockets), as
// EVERSPACE 2 runs them on every pawn (2026-11-02, decompiled: BP_PawnBase's VFX_UpdateHeadlights / VFX_ToggleHeadlights,
// BP_PawnHeadlight and its P_LensFlare_001). Each socket carries a BP_PawnHeadlight: a lens flare (P_LensFlare_001, a
// screen-sized flare of radius 0.02 of the screen, white x 10, opacity the fixture's Intensity: the pawn's
// HeadlightsIntensity 1 shared out over its sockets) and a spot light that lights nothing (bAffectsWorld false: the
// flare's source), and the toggle sets every pawn mesh's "Headlight" material scalar to 1 / 0 (the fixtures' emissive). The
// real light is the player's one HeadLight at the nose (Flight.Headlights). Here: a flare sprite at each mount
// (GoF2/ModEs2Particle kind 6) at 1 / count alpha, and the emission of the materials named "Headlight" (the ES2 importer's
// kits' fixture material) off while the headlights are, through property blocks (the materials are shared with the
// NPCs' copies of the build). Only on the player's ship (Flight.Headlights.On; EngineFlame.Pilot), as EVERSPACE 2's
// NPCs fly with theirs off; nothing in the docked station.

using System.Collections.Generic;
using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using UnityEngine;
using UnityEngine.Rendering;

namespace GoF2Remake.Modding
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public class Es2Headlights : MonoBehaviour
    {
        const float FlareRadius = 0.02f;   // InfiniteScreenRadius: a share of the screen height
        const float FlareColour = 10f;     // the flare's colour parameter, white x 10
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        [SerializeField] Vector3[] sockets = new Vector3[0];
        [SerializeField] float es2Scale = 1f;

        EngineFlame flame;
        MeshFilter flares;
        bool ready, inStation, emissiveOn = true;
        readonly List<(Renderer r, int index)> fixtures = new List<(Renderer, int)>();
        MaterialPropertyBlock block;
        readonly List<Vector3> v = new List<Vector3>(), n = new List<Vector3>();
        readonly List<Vector2> uv = new List<Vector2>(), uv1 = new List<Vector2>();
        readonly List<Color> col = new List<Color>();
        readonly List<int> tri = new List<int>();

        /// <summary>Adds the fixtures for a ship template's headlight mounts (slotType 7; none: nothing).</summary>
        public static void Add(GameObject root, IEnumerable<WeaponMount> mounts, float es2Scale)
        {
            var list = mounts?.Where(m => m != null && m.slotType == 7).ToList();
            if (list == null || list.Count == 0 || Es2EngineVfx.Shader == null) return;
            var c = root.AddComponent<Es2Headlights>();
            c.es2Scale = es2Scale > 0f ? es2Scale : 1f;
            c.sockets = list.Select(WeaponSystem.MountToLocal).ToArray();
        }

        /// <summary>Each fixture's flare opacity: the pawn's HeadlightsIntensity 1 shared out over its sockets.</summary>
        public static float FlareAlpha(int count) => count > 0 ? 1f / count : 0f;

        void OnEnable() { inStation = Visuals.EngineDrive.IsStationModel(transform); }

        void OnDestroy()
        {
            if (flares != null) { Destroy(flares.sharedMesh); Destroy(flares.GetComponent<Renderer>().sharedMaterial); }
        }

        void Setup()
        {
            ready = true;
            flame = GetComponent<EngineFlame>();
            block = new MaterialPropertyBlock();
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                    if (mats[i] != null && mats[i].name != null && mats[i].name.Contains("Headlight")) fixtures.Add((r, i));
            }
            var go = new GameObject("es2_headlight_flares");
            go.transform.SetParent(transform, false);
            flares = go.AddComponent<MeshFilter>();
            flares.sharedMesh = new Mesh { name = "es2_headlight_flares_add" };
            flares.sharedMesh.MarkDynamic();
            var mr = go.AddComponent<MeshRenderer>();
            var mat = new Material(Es2EngineVfx.Shader) { name = "es2_headlight_flare" };
            mat.SetFloat("_Kind", 6);
            mat.SetFloat("_Gain", ModMaterials.Es2FlameGain / 1000f);
            mat.SetFloat("_Es2Scale", es2Scale);
            mat.SetFloat("_SrcBlend", (float)BlendMode.One);
            mat.SetFloat("_DstBlend", (float)BlendMode.One);
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.enabled = false;
            // the flares' mesh never moves: the corners all at the mounts, the shader pushes them out on screen
            int count = sockets.Length;
            float a = FlareAlpha(count);
            var c = new Color(FlareColour, FlareColour, FlareColour, a);
            for (int i = 0; i < count; i++)
            {
                int b = v.Count;
                foreach (var corner in new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) })
                {
                    v.Add(sockets[i]); n.Add(Vector3.forward); uv.Add(corner); uv1.Add(new Vector2(FlareRadius, 0f)); col.Add(c);
                }
                tri.AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3 });
            }
            var m = flares.sharedMesh;
            m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetUVs(1, uv1); m.SetColors(col); m.SetTriangles(tri, 0);
            var bounds = new Bounds(v[0], Vector3.zero);
            foreach (var p in v) bounds.Encapsulate(p);
            bounds.Expand(2f * es2Scale);
            m.bounds = bounds;
        }

        void LateUpdate()
        {
            if (!ready) Setup();
            bool pilot = flame != null && flame.Pilot && !inStation;
            bool on = pilot && Headlights.On;
            var r = flares.GetComponent<Renderer>();
            if (r.enabled != on) r.enabled = on;
            // the fixtures' emissive: EVERSPACE 2's "Headlight" scalar 1 / 0 (an NPC's or a parked ship's stay lit as built)
            bool emissive = !pilot || Headlights.On;
            if (emissive == emissiveOn) return;
            emissiveOn = emissive;
            foreach (var (f, index) in fixtures)
            {
                if (f == null) continue;
                if (emissive) { f.SetPropertyBlock(null, index); continue; }
                f.GetPropertyBlock(block, index);
                block.SetColor(EmissionId, Color.black);
                f.SetPropertyBlock(block, index);
            }
        }
    }
}
