// OriginalModels.cs
// Feature: docs/notes/player-assets.md "The content layer": the game's own models built at run time from the player's
// converted copy (GameContent), in place of what the Editor's builders made from the imported originals:
//   OriginalTextures (GoF2.Game, OriginalTextures.cs) a texture of the cache, as the importer set it up
//   OriginalMaterials  a material of resources.json (PrefabBuilder.CreateMaterial + PostProcessing.ApplyGlow)
//   OriginalModels     a model (the cache's Models/<rel>.aem + .gof2mesh.json) as its per-mesh prefab: the parts as the FBX
//                  import gave them (aem2fbx's parts, ModelOrientationPostprocessor's turn), PartAnimation, ResourceInfo
//   OriginalAssemblies an assembled object of assemblies.json (AssemblyBuilder), kept as a template like a mod ship's
// AssembledObject.LoadPrefab asks OriginalAssemblies when Resources has no prefab (a build without the originals).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using GoF2Remake.Data;
using UnityEngine;
using UnityEngine.Rendering;

namespace GoF2Remake.Visuals
{
    // ---- resources.json / assemblies.json (JsonUtility; as the Editor's ResTable / AsmTable) -------------------------
    [Serializable] public class OriginalResMesh { public int id; public string model; public string source; public int materialId; }
    [Serializable] public class OriginalResMaterial { public int id; public int shaderId; public string shading; public string[] textures; }
    [Serializable] public class OriginalResTable { public OriginalResMesh[] meshes; public OriginalResMaterial[] materials; }
    [Serializable] public class OriginalAsmPart
    {
        public string key, parent, model, role, variant, condition;
        public int mesh, material;
        public float[] position, rotation, scale;
        public int[] lods;
    }
    [Serializable] public class OriginalAsmEntry
    {
        public string name, pack, category, origin, notes, explosionModel;
        public int root;
        public OriginalAsmPart[] parts;
        public float[] lodDistances, scale, spawnRotation;
        public float lastVisibleDistance;
    }
    [Serializable] public class OriginalAsmTable { public OriginalAsmEntry[] entries; }

    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class OriginalMaterials
    {
        // PostProcessing's: a material's _Glow by what it is
        const float EmissiveGlow = 4f, AdditiveGlow = 2.5f, EngineGlow = 3.5f;
        static readonly Regex GlowMesh = new Regex(@"(emissive|lights|engine|glow|_add)", RegexOptions.IgnoreCase);

        static OriginalResTable table;
        static Dictionary<int, OriginalResMaterial> byId;
        static HashSet<int> glowMats, engineGlow;
        static readonly Dictionary<int, Material> made = new Dictionary<int, Material>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { table = null; byId = null; glowMats = null; engineGlow = null; made.Clear(); }

        public static OriginalResTable Table
        {
            get
            {
                if (table != null) return table;
                var text = GameContent.Text("GoF2Data/resources");
                table = text != null ? JsonUtility.FromJson<OriginalResTable>(text) : new OriginalResTable();
                table.meshes ??= new OriginalResMesh[0];
                table.materials ??= new OriginalResMaterial[0];
                byId = table.materials.GroupBy(m => m.id).ToDictionary(g => g.Key, g => g.First());
                glowMats = new HashSet<int>(table.meshes.Where(m => m.model != null && GlowMesh.IsMatch(Path.GetFileNameWithoutExtension(m.model))).Select(m => m.materialId));
                engineGlow = new HashSet<int>(table.materials.Where(m => m.textures != null && m.textures.Any(t => t != null && t.EndsWith("ship_engine_glow.png"))).Select(m => m.id));
                return table;
            }
        }

        /// <summary>The material of resources.json's id, made once (null for an unknown id).</summary>
        public static Material For(int id)
        {
            if (made.TryGetValue(id, out var mat)) return mat;
            _ = Table;
            if (!byId.TryGetValue(id, out var m)) { made[id] = null; return null; }
            made[id] = mat = Create(m);
            return mat;
        }

        static Material Create(OriginalResMaterial m)
        {
            var tex = m.textures ?? new string[0];
            var main = OriginalTextures.Get(tex.Length > 0 ? tex[0] : null);
            Shader sh;
            switch (m.shading)
            {
                case "unlit": sh = Shader.Find("GoF2/Unlit"); break;
                case "additive": case "additive_anim": sh = Shader.Find("GoF2/Additive"); break;
                case "alpha": case "alpha_anim": sh = Shader.Find("GoF2/AlphaBlend"); break;
                case "lit_alpha_test": sh = Shader.Find("GoF2/AlphaTest"); break;
                default: sh = Shader.Find("Universal Render Pipeline/Lit"); break;
            }
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Lit");
            var mat = new Material(sh) { name = $"mat_{m.id}_{(main != null ? main.name : "none")}" };
            bool lit = sh.name.StartsWith("Universal Render Pipeline/Lit");
            if (lit)
            {
                mat.SetTexture("_BaseMap", main);
                string nrmRel = tex.FirstOrDefault(t => t != null && t.Contains("_normal"));
                var nrm = OriginalTextures.Get(nrmRel);
                if (nrm != null) { mat.SetTexture("_BumpMap", nrm); mat.EnableKeyword("_NORMALMAP"); }
                if (!string.IsNullOrEmpty(nrmRel) && nrmRel.Contains("_normal_specular"))
                {
                    var ms = OriginalTextures.Get(nrmRel.Replace("_normal_specular", "_metallic_smoothness"));
                    if (ms != null)
                    {
                        mat.SetTexture("_MetallicGlossMap", ms);
                        bool interior = nrmRel.Contains("/hangars/") || nrmRel.Contains("/bars/") || nrmRel.Contains("/skyboxes/");
                        if (!interior) mat.shader = Shader.Find("GoF2/Hull") ?? mat.shader;
                        if (!interior && nrmRel.Contains("v_station_battlestation")) mat.EnableKeyword("_GOF2_BICUBIC");
                        mat.EnableKeyword("_METALLICSPECGLOSSMAP");
                        mat.SetFloat("_Smoothness", 0.8f);
                    }
                }
                mat.SetFloat("_Metallic", 0f);
                if (m.shading == "bump_alpha_glow") LitAlphaGlow(mat, main);
            }
            else mat.SetTexture("_MainTex", main);
            if (mat.HasProperty("_Glow"))
            {
                float glow = 1f;
                if (engineGlow.Contains(m.id)) glow = EngineGlow;
                else if (m.shading == "additive" || m.shading == "additive_anim") glow = AdditiveGlow;
                else if (m.shading == "lit_alpha_test") glow = 1f;
                else if (glowMats.Contains(m.id)) glow = EmissiveGlow;
                mat.SetFloat("_Glow", glow);
            }
            return mat;
        }

        /// <summary>PrefabBuilder.MakeLitAlphaGlow: URP Lit blended by straight alpha, no depth write or shadows, glowing.</summary>
        static void LitAlphaGlow(Material mat, Texture2D emission)
        {
            mat.SetFloat("_Surface", 1f); mat.SetFloat("_Blend", 0f); mat.SetFloat("_BlendModePreserveSpecular", 0f);
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_SrcBlendAlpha", (float)BlendMode.One); mat.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.renderQueue = (int)RenderQueue.Transparent;
            mat.SetShaderPassEnabled("DepthOnly", false);
            mat.SetShaderPassEnabled("ShadowCaster", false);
            mat.SetTexture("_EmissionMap", emission);
            mat.SetColor("_EmissionColor", Color.white);
            mat.EnableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeDirectEmission;
        }
    }

    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class OriginalModels
    {
        public const float MetersPerUnit = 0.05f;

        /// <summary>A vertex of the original's mesh in the converted model's space: the FBX import (aem2fbx's axes, Unity's
        /// handedness, ImportSettings.ModelScale) and ModelOrientationPostprocessor's 180-degree turn (checked against the
        /// imported models: OriginalModelsTests).</summary>
        public static Vector3 Map(float x, float y, float z) => new Vector3(-x, y, z) * MetersPerUnit;

        /// <summary>Normals the same way, without the scale.</summary>
        public static Vector3 MapNormal(float x, float y, float z) => new Vector3(-x, y, z);

        /// <summary>Whether the mapping mirrors (the triangles' order is turned round to keep their front).</summary>
        public static bool Mirrors = true;

        static readonly Dictionary<string, (AemMesh mesh, TextAsset meta)> parsed = new Dictionary<string, (AemMesh, TextAsset)>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, Mesh[]> meshes = new Dictionary<string, Mesh[]>(StringComparer.OrdinalIgnoreCase);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            parsed.Clear();
            meshes.Clear();
            GameContent.Refreshed -= ResetStatics;
            GameContent.Refreshed += ResetStatics;   // (a new copy converted)
        }

        /// <summary>A model's converted file and its sidecar (as a TextAsset, PartAnimation's key) by its project path
        /// ("Models/main/ships/ship_000.fbx"); null mesh when the cache has none.</summary>
        static (AemMesh mesh, TextAsset meta) Load(string model)
        {
            if (parsed.TryGetValue(model, out var p)) return p;
            string stem = model.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase) ? model.Substring(0, model.Length - 4) : model;
            var aem = GameContent.CachePath(stem + ".aem");
            var side = GameContent.CachePath(stem + ".gof2mesh.json");
            AemMesh mesh = null;
            try { if (aem != null) mesh = AemMesh.Read(File.ReadAllBytes(aem)); }
            catch (FormatException e) { Debug.LogWarning($"OriginalModels: {model}: {e.Message}"); }
            TextAsset meta = side != null ? new TextAsset(File.ReadAllText(side)) { name = Path.GetFileNameWithoutExtension(side) } : null;
            return parsed[model] = (mesh, meta);
        }

        /// <summary>The Unity mesh of one part, as the FBX import gives it.</summary>
        public static Mesh BuildMesh(AemMesh.Part p, string name)
        {
            int nv = p.VertexCount;
            var v = new Vector3[nv];
            for (int i = 0; i < nv; i++) v[i] = Map(p.Positions[i * 3], p.Positions[i * 3 + 1], p.Positions[i * 3 + 2]);
            var idx = new List<int>();
            if (p.Indices != null && p.Indices.Length > 0) foreach (var i in p.Indices) idx.Add(i);
            else for (int i = 0; i < nv - nv % 3; i++) idx.Add(i);
            idx.RemoveRange(idx.Count - idx.Count % 3, idx.Count % 3);
            var tris = new List<int>(idx.Count);
            for (int i = 0; i < idx.Count; i += 3)
            {
                int a = idx[i], b = idx[i + 1], c = idx[i + 2];
                if (a >= nv || b >= nv || c >= nv) continue;
                if (Mirrors) { tris.Add(a); tris.Add(c); tris.Add(b); } else { tris.Add(a); tris.Add(b); tris.Add(c); }
            }
            var m = new Mesh { name = name };
            if (nv > 65535) m.indexFormat = IndexFormat.UInt32;
            m.SetVertices(v);
            if (p.Uvs != null)
            {
                var uv = new Vector2[nv];
                for (int i = 0; i < nv; i++) uv[i] = new Vector2(p.Uvs[i * 2], p.Uvs[i * 2 + 1]);
                m.SetUVs(0, uv);
            }
            if (p.Colors != null)
            {
                var c = new Color[nv];
                for (int i = 0; i < nv; i++) c[i] = new Color(p.Colors[i * 4], p.Colors[i * 4 + 1], p.Colors[i * 4 + 2], p.Colors[i * 4 + 3]);
                m.SetColors(c);
            }
            m.SetTriangles(tris, 0);
            if (p.Normals != null)
            {
                var n = new Vector3[nv];
                for (int i = 0; i < nv; i++) n[i] = MapNormal(p.Normals[i * 3], p.Normals[i * 3 + 1], p.Normals[i * 3 + 2]).normalized;
                m.SetNormals(n);
            }
            else m.RecalculateNormals();
            if (p.Uvs != null) m.RecalculateTangents();
            m.RecalculateBounds();
            return m;
        }

        /// <summary>A model as its per-mesh prefab made it (PrefabBuilder): a root named after the file, its parts
        /// (<name>_part<i>, under their parent part) with the material, PartAnimation from the sidecar, ResourceInfo.
        /// Null when the cache doesn't have the model.</summary>
        public static GameObject Build(OriginalResMesh res, Material material = null)
        {
            if (res == null || string.IsNullOrEmpty(res.model)) return null;
            using var timing = ContentTimings.Measure("mesh built");
            var (aem, meta) = Load(res.model);
            if (aem == null) return null;
            string name = Path.GetFileNameWithoutExtension(res.model);
            if (!meshes.TryGetValue(res.model, out var built))
            {
                var list = new List<Mesh>(); int i = 0;
                foreach (var (part, _) in aem.AllParts()) list.Add(part.VertexCount > 0 ? BuildMesh(part, $"{name}_part{i++}") : null);
                meshes[res.model] = built = list.ToArray();
            }
            var root = new GameObject(name);
            var mat = material ?? OriginalMaterials.For(res.materialId);
            var nodes = new Dictionary<AemMesh.Part, Transform>();
            int k = 0;
            foreach (var (part, parent) in aem.AllParts())
            {
                var go = new GameObject($"{name}_part{k}");
                go.transform.SetParent(parent != null && nodes.TryGetValue(parent, out var pt) ? pt : root.transform, false);
                nodes[part] = go.transform;
                var mesh = built[k++];
                if (mesh != null)
                {
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var r = go.AddComponent<MeshRenderer>();
                    if (mat != null) r.sharedMaterial = mat;
                }
            }
            if (meta != null && meta.text.Contains("\"target\""))
            {
                var anim = root.AddComponent<PartAnimation>();
                anim.meta = meta;
                anim.metersPerUnit = MetersPerUnit;
                anim.play = res.model.Contains("_anim") || AnimatedHull(res.model);
            }
            var info = root.AddComponent<ResourceInfo>();
            info.meshId = res.id; info.materialId = res.materialId; info.originalPath = res.source;
            return root;
        }

        /// <summary>ImportPostprocessor.IsAnimatedShipHull: a living ship whose hull plays its own keys.</summary>
        static bool AnimatedHull(string model) => model.Replace('\\', '/').EndsWith("/v_ship_037_deep_science.fbx");

        /// <summary>The model of resources.json's mesh with that model path.</summary>
        public static GameObject Build(string model, int material = 0)
        {
            var res = OriginalMaterials.Table.meshes.FirstOrDefault(m => string.Equals(m.model, model, StringComparison.OrdinalIgnoreCase))
                      ?? new OriginalResMesh { model = model };
            return Build(res, material > 0 ? OriginalMaterials.For(material) : null);
        }
    }

    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class OriginalAssemblies
    {
        const float ReferenceFov = 60f;

        static OriginalAsmTable table;
        static Dictionary<string, OriginalAsmEntry> byName;
        static readonly Dictionary<string, GameObject> templates = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
        static GameObject holder;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            table = null; byName = null; templates.Clear(); explosions.Clear(); holder = null;
            GameContent.Refreshed -= ResetStatics;
            GameContent.Refreshed += ResetStatics;   // (a new copy converted)
        }

        /// <summary>True when the converted copy has the assembly (what Template builds).</summary>
        public static bool Has(string name) => GameContent.Installed && Entry(name) != null;

        static OriginalAsmEntry Entry(string name)
        {
            if (byName == null)
            {
                var text = GameContent.Text("GoF2Data/assemblies");
                table = text != null ? JsonUtility.FromJson<OriginalAsmTable>(text) : new OriginalAsmTable();
                byName = new Dictionary<string, OriginalAsmEntry>(StringComparer.OrdinalIgnoreCase);
                if (table.entries != null) foreach (var e in table.entries) if (e.name != null && !byName.ContainsKey(e.name)) byName[e.name] = e;
            }
            return name != null && byName.TryGetValue(name, out var en) ? en : null;
        }

        /// <summary>An assembled object built from the converted copy, kept (inactive, under a holder) like a prefab for
        /// Instantiate; null when the copy isn't installed or the assembly is unknown.</summary>
        public static GameObject Template(AssemblyData entry)
        {
            if (entry == null || !GameContent.Installed) return null;
            if (templates.TryGetValue(entry.name, out var t) && t != null) return t;
            using var timing = ContentTimings.Measure("assembly built");
            var e = Entry(entry.name);
            if (e == null) return null;
            var parent = Holder;
            t = Build(e);
            if (t != null) t.transform.SetParent(parent, false);
            templates[entry.name] = t;
            return t;
        }

        static GameObject Build(OriginalAsmEntry e)
        {
            var root = new GameObject(e.name);
            var asm = root.AddComponent<AssembledObject>();
            asm.rootMeshId = e.root;
            asm.origin = e.origin;
            asm.lodDistancesGameUnits = e.lodDistances;
            asm.lastVisibleDistanceGameUnits = e.lastVisibleDistance;
            if (e.spawnRotation != null && e.spawnRotation.Length == 3)
                asm.spawnRotationEngine = new Vector3(e.spawnRotation[0], e.spawnRotation[1], e.spawnRotation[2]);
            if (!string.IsNullOrEmpty(e.explosionModel)) asm.explosionPrefab = ExplosionTemplate(e.explosionModel);
            var nodes = new Dictionary<string, GameObject>();
            var created = new List<(OriginalAsmPart part, GameObject go)>();
            foreach (var p in e.parts ?? new OriginalAsmPart[0])
            {
                var go = p.mesh == 0 ? new GameObject(p.role) : OriginalModels.Build(p.model, p.material);
                if (go == null) continue;
                nodes[p.key] = go;
                created.Add((p, go));
            }
            if (created.Count == 0) { Gone(root); return null; }
            var byLevel = new Dictionary<int, List<Renderer>>();
            var player = new List<GameObject>(); var npc = new List<GameObject>();
            var conditional = new List<GameObject>(); var conditions = new List<string>();
            foreach (var (p, go) in created)
            {
                var parent = !string.IsNullOrEmpty(p.parent) && nodes.TryGetValue(p.parent, out var pg) ? pg.transform : root.transform;
                go.transform.SetParent(parent, false);
                go.transform.localPosition = p.position == null || p.position.Length != 3 ? Vector3.zero : new Vector3(p.position[0], p.position[1], -p.position[2]) * OriginalModels.MetersPerUnit;
                go.transform.localRotation = p.rotation == null || p.rotation.Length != 3 ? Quaternion.identity : Quaternion.Euler(-p.rotation[0], -p.rotation[1], p.rotation[2]);
                if (p.scale != null && p.scale.Length == 3) go.transform.localScale = new Vector3(p.scale[0], p.scale[1], p.scale[2]);
                if (p.variant == "player") player.Add(go);
                else if (p.variant == "npc") { npc.Add(go); go.SetActive(false); }
                if (!string.IsNullOrEmpty(p.condition))
                {
                    conditional.Add(go); conditions.Add(p.condition);
                    if (!p.condition.StartsWith("random")) go.SetActive(false);
                }
                if (p.lods != null)
                    foreach (int l in p.lods)
                    {
                        if (!byLevel.TryGetValue(l, out var list)) byLevel[l] = list = new List<Renderer>();
                        list.AddRange(go.GetComponents<Renderer>().Concat(OwnChildRenderers(go, nodes.Values)));
                    }
            }
            asm.playerVariantParts = player.ToArray();
            asm.npcVariantParts = npc.ToArray();
            asm.conditionalParts = conditional.ToArray();
            asm.conditions = conditions.ToArray();
            if (e.scale != null && e.scale.Length == 3) root.transform.localScale = new Vector3(e.scale[0], e.scale[1], e.scale[2]);
            if (e.lodDistances != null && e.lodDistances.Length > 0 && byLevel.Count > 0) SetupLods(root, e, byLevel);
            return root;
        }

        static readonly Dictionary<string, GameObject> explosions = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);

        static GameObject ExplosionTemplate(string model) => ModelTemplate(model);

        /// <summary>A model as its per-mesh prefab (Prefabs/<rel>.prefab), built from the converted copy and kept inactive
        /// like a prefab for Instantiate (OriginalContent: the tables' references to Prefabs/...); null without it.</summary>
        public static GameObject ModelTemplate(string model)
        {
            if (string.IsNullOrEmpty(model) || !GameContent.Installed) return null;
            if (explosions.TryGetValue(model, out var t) && t != null) return t;
            using var timing = ContentTimings.Measure("model template built");
            var parent = Holder;
            t = OriginalModels.Build(model);
            if (t != null) t.transform.SetParent(parent, false);
            return explosions[model] = t;
        }

        /// <summary>The inactive holder the templates live under (DontDestroyOnLoad in Play mode). Made from a scene the game
        /// loaded: under Unity 7's entity-backed transforms one made before (Bootstrap's tables, the test runner's own first
        /// scene) is in another Entities World than the scenes after it, and every model parented under it logged "A transform
        /// hierarchy holds entities of more than one Entities World" (OriginalRefs defers its prefabs for that).</summary>
        static Transform Holder
        {
            get
            {
                // Edit mode (the tests): a holder of its own for each template, made right before it. One kept holder ended up
                // in another Entities World than the models a later test made, even in the same scene ("A transform hierarchy
                // holds entities of more than one Entities World").
                if (!Application.isPlaying)
                {
                    var own = new GameObject("Game model") { hideFlags = HideFlags.DontSave };   // (never saved into a scene)
                    own.SetActive(false);
                    return own.transform;
                }
                if (holder == null)
                {
                    holder = new GameObject("Game models");
                    holder.SetActive(false);
                    UnityEngine.Object.DontDestroyOnLoad(holder);
                }
                return holder.transform;
            }
        }

        /// <summary>Destroyed in Play mode, at once in edit mode (the tests).</summary>
        public static void Gone(UnityEngine.Object o)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(o); else UnityEngine.Object.DestroyImmediate(o);
        }

        static IEnumerable<Renderer> OwnChildRenderers(GameObject part, IEnumerable<GameObject> allParts)
        {
            var others = new HashSet<Transform>(allParts.Where(g => g != part).Select(g => g.transform));
            foreach (var r in part.GetComponentsInChildren<Renderer>(true))
            {
                if (r.gameObject == part) continue;
                bool foreign = false;
                for (var t = r.transform; t != null && t != part.transform; t = t.parent)
                    if (others.Contains(t)) { foreign = true; break; }
                if (!foreign) yield return r;
            }
        }

        /// <summary>AssemblyBuilder.SetupLods: the original's LOD distances as LODGroup screen heights for ReferenceFov.</summary>
        static void SetupLods(GameObject root, OriginalAsmEntry e, Dictionary<int, List<Renderer>> byLevel)
        {
            var group = root.AddComponent<LODGroup>();
            int levels = e.lodDistances.Length + 1;
            var lods = new LOD[levels];
            for (int l = 0; l < levels; l++)
                lods[l] = new LOD(1f - (l + 1f) / (levels + 1f), byLevel.TryGetValue(l, out var list) ? list.Distinct().ToArray() : new Renderer[0]);
            group.SetLODs(lods);
            group.RecalculateBounds();
            float tanHalf = Mathf.Tan(ReferenceFov * 0.5f * Mathf.Deg2Rad);
            float worldSize = group.size * root.transform.lossyScale.x;
            float Height(float gameUnits) => Mathf.Clamp01(worldSize / (2f * gameUnits * OriginalModels.MetersPerUnit * tanHalf));
            float prev = 1f;
            for (int l = 0; l < levels; l++)
            {
                float d = l < e.lodDistances.Length ? e.lodDistances[l] : e.lastVisibleDistance;
                float h = d > 0f ? Height(d) : 0f;
                h = Mathf.Min(h, prev * 0.999f);
                lods[l].screenRelativeTransitionHeight = h;
                prev = h;
            }
            group.SetLODs(lods);
        }
    }
}
