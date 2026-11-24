// ModDriftFx.cs
// Remake mods (2026-11-19, after Chorus's Forsaken: BP_Player_Ship_Phoenix's "FX Drift Started" activates its four Cascade
// systems, P_Drift_L / _R on the skeleton's SCK_VFX_Drift_L2 / R2 sockets and P_Drift_L_Top / _R_Top on L4 / R4, and "FX
// Drift Recovery Started" deactivates them): the look of Chorus flight's drift on the player's ship, ships.json "driftFx"
// (CustomDriftFx; docs/notes/mods-models.md "Drift energy field"). Flight.PlayerEngine starts it as FlightModel.Drifting
// turns on and stops it as it turns off. Each part is a root on a node of the ship's model (by name, else the model
// itself), placed there and moving with it (the Forsaken's wings fold out as it drifts), its world scale kept at 1 so the
// emitters' game units hold:
//   - its particle emitters (ModDeathFx.Build, FxFlash for a light): a "loop" one emits again every 'duration' until the
//     drift ends (Cascade's EmitterLoops 0: the dots, the light), the rest once (the burst of sparks); "local" particles
//     move with the part (bUseLocalSpace);
//   - its energy-field shells: a mesh (CustomFxMesh, JSON in the mod, made in the part's frame) on GoF2/ChorusDriftField,
//     shown while the drift lasts (Chorus emits the shell again every 0.2 s with a 0.2 s life: one that stays) and for
//     its last 0.2 s after.
// Stopping lets the particles live out (Cascade's Deactivate without bKillOnDeactivate); the roots go after the longest
// life. The player's own look only: other players' ships and NPCs don't drift.

using System.Collections.Generic;
using GoF2Remake.Data;
using UnityEngine;

namespace GoF2Remake.World
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public sealed class ModDriftFx
    {
        const float M = 0.05f;
        /// <summary>A shell's life after the drift ends (its emitter's last particle, Lifetime 0.2).</summary>
        public const float FieldTailS = 0.2f;

        static int revision = -1;
        static readonly Dictionary<int, (Modding.ModInfo mod, CustomDriftFx fx)> byShip = new Dictionary<int, (Modding.ModInfo, CustomDriftFx)>();
        static readonly Dictionary<string, Mesh> meshes = new Dictionary<string, Mesh>();
        static readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();
        static Shader shader;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { revision = -1; byShip.Clear(); meshes.Clear(); materials.Clear(); shader = null; }

        readonly List<GameObject> roots = new List<GameObject>();
        readonly List<ParticleSystem> systems = new List<ParticleSystem>();
        readonly List<FxFlash> lights = new List<FxFlash>();
        readonly List<GameObject> fields = new List<GameObject>();
        float longestS;

        /// <summary>Its particle systems, lights and shells (tests).</summary>
        public IReadOnlyList<ParticleSystem> Systems => systems;
        public IReadOnlyList<FxFlash> Lights => lights;
        public IReadOnlyList<GameObject> Fields => fields;
        public IReadOnlyList<GameObject> Roots => roots;

        /// <summary>Ship 'ship''s drift look (its mod's ships.json "driftFx"); null = none.</summary>
        public static CustomDriftFx Of(int ship) => Find(ship, out _);

        static CustomDriftFx Find(int ship, out Modding.ModInfo mod)
        {
            if (revision != Modding.ModManager.Revision)
            {
                revision = Modding.ModManager.Revision;
                byShip.Clear(); meshes.Clear(); materials.Clear();
                foreach (var (m, s) in Modding.ModContent.ActiveShips())
                    if (s?.driftFx?.parts != null && s.driftFx.parts.Length > 0) byShip[s.index] = (m, s.driftFx);   // (JsonUtility makes an empty one when absent)
            }
            if (byShip.TryGetValue(ship, out var hit)) { mod = hit.mod; return hit.fx; }
            mod = null;
            return null;
        }

        /// <summary>Starts ship 'ship''s drift look on 'model' (the ship's model: its parts on its nodes); null when it has none.</summary>
        public static ModDriftFx Start(int ship, Transform model)
        {
            var spec = Find(ship, out var mod);
            return spec != null && model != null ? Start(mod, spec, model) : null;
        }

        /// <summary>Starts 'spec' (of 'mod': its files; null = none read) on 'model'.</summary>
        public static ModDriftFx Start(Modding.ModInfo mod, CustomDriftFx spec, Transform model)
        {
            if (spec?.parts == null || model == null) return null;
            var d = new ModDriftFx();
            foreach (var part in spec.parts)
            {
                if (part == null) continue;
                var node = Node(model, part.node) ?? model;
                var root = new GameObject("Drift effect" + (string.IsNullOrEmpty(part.node) ? "" : " " + part.node));
                root.transform.SetPositionAndRotation(node.position, node.rotation);
                root.transform.SetParent(node, true);   // (it rides its part's animation; its world scale stays 1)
                d.roots.Add(root);
                if (part.fx?.emitters != null)
                    foreach (var e in part.fx.emitters)
                    {
                        if (e == null) continue;
                        if (e.light > 0f)
                        {
                            var flash = FxFlash.Spawn(e, root.transform, 1f);
                            d.lights.Add(flash);
                            d.longestS = Mathf.Max(d.longestS, flash.Lasts);
                            continue;
                        }
                        var ps = ModDeathFx.Build(mod, e, root.transform, Vector3.zero, 1f, out float lasts);
                        if (ps == null) continue;
                        d.longestS = Mathf.Max(d.longestS, e.life != null && e.life.Length > 0 ? e.life[e.life.Length - 1] : 1f);
                        if (!e.loop) d.longestS = Mathf.Max(d.longestS, lasts);
                        ps.Play(true);
                        d.systems.Add(ps);
                    }
                if (part.fields != null)
                    foreach (var f in part.fields)
                    {
                        var go = f != null ? Field(mod, f, root.transform) : null;
                        if (go != null) d.fields.Add(go);
                    }
                Layer(root.transform, node.gameObject.layer);
            }
            return d;
        }

        /// <summary>The drift is over: the emitters stop (their particles live out), the lights end with their flash, the
        /// shells go after their last 0.2 s, the roots after the longest life.</summary>
        public void Stop()
        {
            foreach (var ps in systems) if (ps != null) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            foreach (var l in lights) if (l != null) l.StopLooping();
            foreach (var f in fields) if (f != null) Gone(f, FieldTailS);
            foreach (var r in roots) if (r != null) Gone(r, Mathf.Max(longestS, FieldTailS) + 0.5f);
        }

        /// <summary>Everything gone at once (the ship gone, the engine off).</summary>
        public void Clear()
        {
            foreach (var r in roots) if (r != null) Gone(r, 0f);
            roots.Clear(); systems.Clear(); lights.Clear(); fields.Clear();
        }

        static void Gone(GameObject go, float afterS)
        {
            if (!Application.isPlaying) { if (afterS <= 0f) Object.DestroyImmediate(go); return; }   // (edit mode: no timers; the caller clears)
            if (afterS > 0f) Object.Destroy(go, afterS); else Object.Destroy(go);
        }

        /// <summary>The model's node named 'name' (case aside); null when it has none or none is named.</summary>
        static Transform Node(Transform model, string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            foreach (var t in model.GetComponentsInChildren<Transform>(true))
                if (string.Equals(t.name, name, System.StringComparison.OrdinalIgnoreCase)) return t;
            return null;
        }

        static void Layer(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            for (int i = 0; i < t.childCount; i++) Layer(t.GetChild(i), layer);
        }

        /// <summary>One shell under 'parent': its mesh on its material; null when its mesh isn't there.</summary>
        static GameObject Field(Modding.ModInfo mod, CustomDriftField f, Transform parent)
        {
            var mesh = MeshOf(mod, f.mesh);
            var mat = mesh != null ? MaterialOf(mod, f) : null;
            if (mat == null) return null;
            var go = new GameObject("Drift field");
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go;
        }

        static Mesh MeshOf(Modding.ModInfo mod, string path)
        {
            if (mod == null || string.IsNullOrEmpty(path)) return null;
            string key = mod.Id + "|" + path;
            if (meshes.TryGetValue(key, out var hit)) return hit;
            Mesh mesh = null;
            try
            {
                string text = mod.Source.ReadText(path);
                mesh = string.IsNullOrEmpty(text) ? null : BuildMesh(JsonUtility.FromJson<CustomFxMesh>(text), path);
                if (mesh == null) Warn(mod, $"{Modding.ModContent.ShipsFile}: the drift field mesh \"{path}\" isn't in the mod or has no triangles");
            }
            catch (System.Exception e) { Warn(mod, $"{path}: {e.Message}"); mesh = null; }
            meshes[key] = mesh;
            return mesh;
        }

        /// <summary>A CustomFxMesh as a Unity mesh (game units to metres); null without positions or triangles.</summary>
        public static Mesh BuildMesh(CustomFxMesh m, string name = "Drift field")
        {
            if (m?.positions == null || m.triangles == null || m.positions.Length < 9 || m.triangles.Length < 3) return null;
            int n = m.positions.Length / 3;
            var v = new Vector3[n];
            for (int i = 0; i < n; i++) v[i] = new Vector3(m.positions[i * 3], m.positions[i * 3 + 1], m.positions[i * 3 + 2]) * M;
            foreach (int i in m.triangles) if (i < 0 || i >= n) return null;
            var mesh = new Mesh { name = name };
            if (n > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = v;
            if (m.uvs != null && m.uvs.Length >= n * 2)
            {
                var uv = new Vector2[n];
                for (int i = 0; i < n; i++) uv[i] = new Vector2(m.uvs[i * 2], m.uvs[i * 2 + 1]);
                mesh.uv = uv;
            }
            mesh.triangles = m.triangles;
            if (m.normals != null && m.normals.Length >= n * 3)
            {
                var nr = new Vector3[n];
                for (int i = 0; i < n; i++) nr[i] = new Vector3(m.normals[i * 3], m.normals[i * 3 + 1], m.normals[i * 3 + 2]).normalized;
                mesh.normals = nr;
            }
            else mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        static Material MaterialOf(Modding.ModInfo mod, CustomDriftField f)
        {
            string key = (mod != null ? mod.Id : "") + "|" + JsonUtility.ToJson(f);
            if (materials.TryGetValue(key, out var hit) && hit != null) return hit;
            var m = BuildMaterial(mod, f);
            materials[key] = m;
            return m;
        }

        /// <summary>A shell's material on GoF2/ChorusDriftField: its maps as data (linear), repeating; its values from 'f'.</summary>
        public static Material BuildMaterial(Modding.ModInfo mod, CustomDriftField f)
        {
            if (shader == null) shader = Resources.Load<Shader>("GoF2Fx/ChorusDriftField");
            if (shader == null || f == null) return null;
            var m = new Material(shader) { name = "Drift field" };
            void Map(string prop, string path)
            {
                var t = mod != null && !string.IsNullOrEmpty(path) ? Modding.ModMaterials.Texture(mod, path, true) : null;
                if (t == null) return;
                t.wrapMode = TextureWrapMode.Repeat;
                m.SetTexture(prop, t);
            }
            Map("_Band", f.band); Map("_Mask", f.mask); Map("_Distort", f.distort); Map("_Line", f.line); Map("_Background", f.background);
            float At(float[] a, int i, float d) => a != null && a.Length > i ? a[i] : d;
            m.SetVector("_DriftTint", new Vector4(At(f.color, 0, 0.4f), At(f.color, 1, 1f), At(f.color, 2, 0.9f), At(f.color, 3, 1f)));
            m.SetVector("_DriftA", new Vector4(f.offset, f.pan, At(f.tiling, 0, 1f), At(f.tiling, 1, 0.12f)));
            m.SetVector("_DriftB", new Vector4(At(f.core, 0, 1f), At(f.core, 1, 5f), At(f.coreLine, 0, 2f), f.distortRing));
            m.SetVector("_DriftC", new Vector4(At(f.coreLine, 1, 1f), At(f.coreLine, 2, 1f), At(f.coreLine, 3, 12f), f.fresnel));
            m.SetVector("_DriftD", new Vector4(At(f.tilingBg, 0, 1.5f), At(f.tilingBg, 1, 0.45f), At(f.bg, 0, 1.5f), At(f.bg, 1, 2f)));
            m.SetVector("_DriftE", new Vector4(At(f.textures, 0, 1f), At(f.textures, 1, 4f), Mathf.Max(0f, f.exposure), 0f));
            return m;
        }

        static void Warn(Modding.ModInfo mod, string message)
        {
            if (!mod.Warnings.Contains(message)) mod.Warnings.Add(message);
            Debug.LogWarning($"Mods: {mod.Id}: {message}");
        }
    }
}
