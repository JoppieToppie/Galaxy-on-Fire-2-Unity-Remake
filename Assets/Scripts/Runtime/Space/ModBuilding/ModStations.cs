// ModStations.cs
// The models of the mods' stations (stations.json "model", ModWorld.StationModel): each glTF / GLB loaded with glTFast
// (materials through ModGltfMaterials, or the entry's "materials" like ships.json's) and built into an AssembledObject
// template under a hidden DontDestroyOnLoad holder: turned by modelYaw, its bounds' centre on the station's origin
// (modelCentre), scaled so its largest extent is modelSize game units, always visible (no LOD cull: a station is seen from
// far). OrbitBuilder.StationAssembly names it "modstation_NNN" (pack "mod"), AssembledObject.LoadPrefab hands the template
// out; while it isn't built (or failed) the "looksLike" station stands in. Also its collision (Volumes) and the mods'
// planet / sun textures, decoded in the background beside the models (ModBackdrop). Loaded like the ships, all at once in
// the background from the main menu's start (Preload; ModLoading's loading screen waits for Ready).

using System.Collections.Generic;
using System.Threading.Tasks;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.Visuals;
using UnityEngine;
using UnityEngine.Rendering;

namespace GoF2Remake.Modding
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class ModStations
    {
        const float M = 0.05f;
        const string Prefix = "modstation_", GatePrefix = "modgate_";

        static readonly Dictionary<int, GameObject> templates = new Dictionary<int, GameObject>();
        static readonly Dictionary<int, GameObject> lifeUnits = new Dictionary<int, GameObject>();
        static readonly List<GLTFast.GltfImport> imports = new List<GLTFast.GltfImport>();
        static GameObject holder;
        static GLTFast.TimeBudgetPerFrameDeferAgent agent;
        static int loadingRevision = -1, loadedRevision = -1;
        static readonly List<string> current = new List<string>();
        static int steps, stepsDone;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            templates.Clear(); lifeUnits.Clear(); imports.Clear(); current.Clear();
            holder = null; agent = null; loadingRevision = loadedRevision = -1; steps = stepsDone = 0; Count = Done = Backdrops = 0;
        }

        public static int Count { get; private set; }
        public static int Done { get; private set; }
        /// <summary>The planet, sun and sky images being loaded with them (ModLoading.HasWork).</summary>
        public static int Backdrops { get; private set; }
        public static float Progress => steps == 0 ? 1f : Mathf.Clamp01((float)stepsDone / steps);
        public static string Current => current.Count > 0 ? current[current.Count - 1] : null;

        /// <summary>The mods' station models and backdrop textures are loaded (or there are none).</summary>
        public static bool Ready
        {
            get
            {
                if (loadedRevision == ModManager.Revision) return true;
                Preload();
                return false;
            }
        }

        /// <summary>Starts loading when the mods changed since the last time.</summary>
        public static void Preload()
        {
            int rev = ModManager.Revision;
            if (loadingRevision == rev) return;
            loadingRevision = rev;
            _ = LoadAll(rev);
        }

        static async Task LoadAll(int rev)
        {
            foreach (var t in templates.Values) if (t != null) Object.Destroy(t);
            templates.Clear();
            foreach (var t in lifeUnits.Values) if (t != null) Object.Destroy(t);
            lifeUnits.Clear();
            foreach (var i in imports) i.Dispose();
            imports.Clear();
            if (holder == null)
            {
                holder = new GameObject("Mod station models");
                holder.SetActive(false);
                Object.DontDestroyOnLoad(holder);
            }
            if (agent == null)
            {
                var go = new GameObject("Mod station loader") { hideFlags = HideFlags.HideInHierarchy };
                Object.DontDestroyOnLoad(go);
                agent = go.AddComponent<GLTFast.TimeBudgetPerFrameDeferAgent>();
                agent.SetFrameBudget(0.4f);
            }
            ModInteriors.Clear();
            var models = new List<ModWorld.StationModel>(ModWorld.Models());
            var interiors = new List<ModInteriors.Def>(ModInteriors.All());
            var backdrops = new List<string>(ModWorld.BackdropTextures());
            Count = models.Count + interiors.Count;
            Backdrops = backdrops.Count;
            Done = 0;
            steps = (models.Count + interiors.Count) * 3 + backdrops.Count;
            stepsDone = 0;
            current.Clear();
            var tasks = new List<Task>();
            foreach (var m in models) tasks.Add(LoadOne(rev, m));
            foreach (var d in interiors) tasks.Add(LoadInterior(rev, d));
            foreach (var b in backdrops) tasks.Add(LoadBackdrop(rev, b));
            await Task.WhenAll(tasks);
            if (rev != ModManager.Revision) return;
            loadedRevision = rev;
        }

        static async Task LoadBackdrop(int rev, string name)
        {
            try { await ModBackdrop.Preload(name); }
            catch (System.Exception e) { if (!ModFileException.IsFileError(e)) Debug.LogException(e); else Debug.LogWarning($"Mods: {name}: {e.Message}"); }
            finally { if (rev == ModManager.Revision) stepsDone++; }
        }

        /// <summary>The PNGs a station's materials use (as ModMaterials.FromSpec reads them).</summary>
        static IEnumerable<(string path, bool linear, bool normal)> TexturesOf(ModWorld.StationModel s)
        {
            foreach (var m in s.materials)
            {
                if (m == null) continue;
                if (!string.IsNullOrEmpty(m.diffuse)) yield return (m.diffuse, false, false);
                if (!string.IsNullOrEmpty(m.normal)) yield return (m.normal, true, true);
                if (!string.IsNullOrEmpty(m.metallicSmoothness)) yield return (m.metallicSmoothness, true, false);
                if (!string.IsNullOrEmpty(m.specularGlossiness)) yield return (m.specularGlossiness, false, false);
                if (!string.IsNullOrEmpty(m.emission)) yield return (m.emission, false, false);
                if (!string.IsNullOrEmpty(m.detailAlbedo)) yield return (m.detailAlbedo, true, false);
                if (!string.IsNullOrEmpty(m.detailNormal)) yield return (m.detailNormal, true, true);
                // EVERSPACE 2's structure layers (ModMaterials.Es2Layers)
                if (!string.IsNullOrEmpty(m.wear)) yield return (m.wear, true, false);
                if (!string.IsNullOrEmpty(m.wearNormal)) yield return (m.wearNormal, true, true);
                if (!string.IsNullOrEmpty(m.wearNoise)) yield return (m.wearNoise, true, false);
                if (!string.IsNullOrEmpty(m.grunge)) yield return (m.grunge, true, false);
            }
            // a mobile station's engine effect (MobileStation)
            if (s.gateSystem < 0)
                foreach (var f in ModShips.EngineFxTextures(s.mod, ModWorld.MobileOf(s.station)?.engineFx)) yield return (f, true, false);
        }

        static async Task LoadOne(int rev, ModWorld.StationModel s)
        {
            string label = s.label ?? s.model;
            current.Add(label);
            int counted = 0;
            try
            {
                var textures = new List<Task>();
                foreach (var (path, linear, normal) in TexturesOf(s)) textures.Add(ModMaterials.PreloadTexture(s.mod, path, linear, normal: normal));
                if (GameModels.Is(s.model, out string gameModel))
                {
                    // One of the game's own models ("gof2:<name>"): a copy of its prefab instead of a file.
                    await Task.WhenAll(textures);
                    counted += 2; stepsDone += 2;
                    if (rev != ModManager.Revision || holder == null) return;
                    var asmData = GameModels.Find(Data.Database.Shared, gameModel);
                    var prefab = asmData != null ? AssembledObject.LoadPrefab(asmData) : null;
                    if (prefab == null) { Warn(s.mod, $"stations.json: \"{label}\": the game has no model \"{gameModel}\""); return; }
                    var copy = Object.Instantiate(prefab, holder.transform, false);
                    var built = Build(s, copy);
                    built.transform.SetParent(holder.transform, false);
                    templates[s.station] = built;
                    Debug.Log($"Mods: {s.mod.Id}: station {s.station} \"{label}\" on the game's {gameModel}");
                    return;
                }
                var file = Task.Run(() => s.mod.Source.ReadBytes(s.model));
                await Task.WhenAll(textures);
                var bytes = await file;
                counted++; stepsDone++;
                if (rev != ModManager.Revision) return;
                if (bytes == null) { Warn(s.mod, $"stations.json: \"{label}\": model {s.model} not found"); return; }
                var import = await ModGltf.Load(s.mod, bytes, agent);
                bytes = null;
                if (import == null) { Warn(s.mod, $"{s.model}: not a glTF model glTFast can read"); return; }
                imports.Add(import);
                counted++; stepsDone++;
                if (rev != ModManager.Revision || holder == null) return;
                var scene = new GameObject("scene");
                scene.transform.SetParent(holder.transform, false);
                if (!await ModGltf.InstantiateMain(import, scene.transform)) { Object.Destroy(scene); Warn(s.mod, $"{s.model}: no scene to show"); return; }
                if (rev != ModManager.Revision || holder == null) return;
                ModGltf.LoopAnimations(scene);   // its glTF node animations (a gate's turning rings, a station's moving parts) loop, as a ship's do
                var template = Build(s, scene);
                template.transform.SetParent(holder.transform, false);
                templates[s.station] = template;
                if (s.life != null) await LoadLifeUnit(s, import);
                Debug.Log($"Mods: {s.mod.Id}: station {s.station} \"{label}\" built from {s.model}");
            }
            catch (System.Exception e) when (rev == ModManager.Revision) { Warn(s.mod, $"{s.model}: {e.Message}"); if (!ModFileException.IsFileError(e)) Debug.LogException(e); }
            catch (System.Exception) { }   // a stale load (the mods changed meanwhile)
            finally
            {
                if (rev == ModManager.Revision) { stepsDone += 3 - counted; Done++; }
                current.Remove(label);
            }
        }

        /// <summary>Remake (2026-11-04): a station's life unit (stations.json "life", World.StationLife): its model file's scene
        /// "life.scene", dressed in the station's materials, kept beside the template.</summary>
        static async Task LoadLifeUnit(ModWorld.StationModel s, GLTFast.GltfImport import)
        {
            int index = -1;
            for (int i = 0; i < import.SceneCount; i++) if (import.GetSceneName(i) == s.life.scene) index = i;
            if (index < 0) { Warn(s.mod, $"stations.json: \"{s.label ?? s.model}\": {s.model} has no scene \"{s.life.scene}\" for its life"); return; }
            var unit = new GameObject("life unit");
            unit.transform.SetParent(holder.transform, false);
            if (!await import.InstantiateSceneAsync(unit.transform, index) || holder == null) { Object.Destroy(unit); return; }
            DressMaterials(s, unit.GetComponentsInChildren<Renderer>(true));
            lifeUnits[s.station] = unit;
        }

        /// <summary>The station's life unit (LoadLifeUnit) to copy, null without one.</summary>
        public static GameObject LifeUnit(int station) => lifeUnits.TryGetValue(station, out var u) ? u : null;

        /// <summary>A custom hangar / bar (ModInteriors): its textures and file at once, then glTFast, then the room.</summary>
        static async Task LoadInterior(int rev, ModInteriors.Def d)
        {
            string label = d.label;
            current.Add(label);
            int counted = 0;
            try
            {
                var textures = new List<Task>();
                foreach (var m in d.materials)
                {
                    if (m == null) continue;
                    foreach (var (path, linear, normal) in new[] { (m.diffuse, false, false), (m.normal, true, true), (m.metallicSmoothness, true, false), (m.specularGlossiness, false, false), (m.emission, false, false), (m.detailAlbedo, true, false), (m.detailNormal, true, true) })
                        if (!string.IsNullOrEmpty(path)) textures.Add(ModMaterials.PreloadTexture(d.mod, path, linear, normal: normal));
                }
                if (!string.IsNullOrEmpty(d.dust)) textures.Add(ModMaterials.PreloadTexture(d.mod, d.dust, true));
                if (GameModels.Is(d.model, out string gameModel))
                {
                    // One of the game's rooms ("gof2:hangar_terran"): a copy of its prefab, as the game places it (the bars
                    // turned 180 deg like StationLevel's), its markers from "markers".
                    await Task.WhenAll(textures);
                    counted += 2; stepsDone += 2;
                    if (rev != ModManager.Revision || holder == null) return;
                    var asmData = GameModels.Find(Data.Database.Shared, gameModel);
                    var prefab = asmData != null ? AssembledObject.LoadPrefab(asmData) : null;
                    if (prefab == null) { Warn(d.mod, $"{ModInteriors.File}: \"{d.id}\": the game has no model \"{gameModel}\""); return; }
                    var holderScene = new GameObject("scene");
                    holderScene.transform.SetParent(holder.transform, false);
                    var copy = Object.Instantiate(prefab, holderScene.transform, false);
                    copy.transform.localRotation = d.hangar ? Quaternion.identity : World.OrbitLayout.RotationToUnity(Vector3.zero);
                    var gameRoom = ModInteriors.Build(d, holderScene);
                    if (gameRoom != null)
                    {
                        gameRoom.transform.SetParent(holder.transform, false);
                        Debug.Log($"Mods: {d.mod.Id}: {(d.hangar ? "hangar" : "bar")} \"{d.id}\" on the game's {gameModel}");
                    }
                    return;
                }
                var file = Task.Run(() => d.mod.Source.ReadBytes(d.model));
                await Task.WhenAll(textures);
                var bytes = await file;
                counted++; stepsDone++;
                if (rev != ModManager.Revision) return;
                if (bytes == null) { Warn(d.mod, $"{ModInteriors.File}: \"{d.id}\": model {d.model} not found"); return; }
                var import = await ModGltf.Load(d.mod, bytes, agent);
                bytes = null;
                if (import == null) { Warn(d.mod, $"{d.model}: not a glTF model glTFast can read"); return; }
                imports.Add(import);
                counted++; stepsDone++;
                if (rev != ModManager.Revision || holder == null) return;
                var scene = new GameObject("scene");
                scene.transform.SetParent(holder.transform, false);
                if (!await ModGltf.InstantiateMain(import, scene.transform)) { Object.Destroy(scene); Warn(d.mod, $"{d.model}: no scene to show"); return; }
                if (rev != ModManager.Revision || holder == null) return;
                var room = ModInteriors.Build(d, scene);
                if (room != null)
                {
                    ModMaterials.PlainLit(room);   // a room: no hull reflection (the game's hangars and bars have none)
                    room.transform.SetParent(holder.transform, false);
                    Debug.Log($"Mods: {d.mod.Id}: {(d.hangar ? "hangar" : "bar")} \"{d.id}\" built from {d.model}");
                }
            }
            catch (System.Exception e) when (rev == ModManager.Revision) { Warn(d.mod, $"{d.model}: {e.Message}"); if (!ModFileException.IsFileError(e)) Debug.LogException(e); }
            catch (System.Exception) { }   // a stale load (the mods changed meanwhile)
            finally
            {
                if (rev == ModManager.Revision) { stepsDone += 3 - counted; Done++; }
                current.Remove(label);
            }
        }

        /// <summary>The template around the instantiated glTF scene: materials, turn, centre, size.</summary>
        static GameObject Build(ModWorld.StationModel s, GameObject model)
        {
            var root = new GameObject(s.Assembly);
            var asm = root.AddComponent<AssembledObject>();
            asm.origin = $"mod {s.mod.Id}";
            asm.lodDistancesGameUnits = new float[0];
            asm.lastVisibleDistanceGameUnits = 0f;   // always visible
            AutoLods.MarkRuntime(root, s.mod.Id);   // levels of detail made from the hull at run time (ModLodCache)
            asm.playerVariantParts = new GameObject[0];
            asm.npcVariantParts = new GameObject[0];
            asm.conditionalParts = new GameObject[0];
            asm.conditions = new string[0];
            model.name = "hull";
            model.transform.SetParent(root.transform, false);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.Euler(0f, s.yaw, 0f);
            var renderers = model.GetComponentsInChildren<Renderer>(true);
            DressMaterials(s, renderers);
            foreach (var r in renderers) { r.shadowCastingMode = ShadowCastingMode.On; r.receiveShadows = true; }
            // Scale: the largest extent to modelSize game units; then the bounds' centre onto the origin.
            var b = ModShipBuilder.BoundsIn(root.transform, renderers);
            float largest = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
            if (largest > 0f && s.size > 0f) model.transform.localScale *= s.size * M / largest;   // 0: as made (a game model)
            if (s.centre)
            {
                b = ModShipBuilder.BoundsIn(root.transform, renderers);
                model.transform.localPosition -= b.center;
            }
            return root;
        }

        /// <summary>The entry's "materials" onto a station model's renderers (as a ship's).</summary>
        static void DressMaterials(ModWorld.StationModel s, Renderer[] renderers)
        {
            if (s.materials.Count == 0) return;
            var mats = new Material[s.materials.Count];
            for (int i = 0; i < mats.Length; i++)
                mats[i] = ModMaterials.FromSpec(s.mod, s.materials[i], !string.IsNullOrEmpty(s.materials[i].material) ? $"{s.Assembly} {s.materials[i].material}" : $"{s.Assembly} {s.materials[i].mesh}{s.materials[i].submesh}");
            foreach (var r in renderers)
            {
                var mesh = ModShipBuilder.MeshOf(r);
                var assigned = r.sharedMaterials;
                int count = Mathf.Max(1, mesh != null ? mesh.subMeshCount : assigned.Length);
                if (assigned.Length != count) System.Array.Resize(ref assigned, count);
                // (the model's own material name too: an entry's "material" filter, as on a ship; stations ignored it)
                for (int sub = 0; sub < count; sub++) { var m = ModShipBuilder.MaterialFor(s.materials, mats, r.name, sub, assigned[sub] != null ? assigned[sub].name : null); if (m != null) assigned[sub] = m; }
                r.sharedMaterials = assigned;
            }
        }

        static void Warn(ModInfo mod, string message)
        {
            if (!mod.Warnings.Contains(message)) mod.Warnings.Add(message);
            Debug.LogWarning($"Mods: {mod.Id}: {message}");
        }

        /// <summary>A "modstation_NNN" assembly's template; null while it isn't built (the caller falls back to looksLike).</summary>
        public static GameObject Template(string assembly)
        {
            if (assembly != null && assembly.StartsWith(GatePrefix) && int.TryParse(assembly.Substring(GatePrefix.Length), out int system))
                return templates.TryGetValue(ModWorld.GateKey(system), out var g) ? g : null;
            if (assembly == null || !assembly.StartsWith(Prefix) || !int.TryParse(assembly.Substring(Prefix.Length), out int station)) return null;
            return templates.TryGetValue(station, out var t) ? t : null;
        }

        /// <summary>A mod station's or a mod system gate's assembly ("modstation_NNN" / "modgate_NNN").</summary>
        public static bool IsStationAssembly(string assembly) => assembly != null && (assembly.StartsWith(Prefix) || assembly.StartsWith(GatePrefix));

        /// <summary>The station's model is built.</summary>
        public static bool Built(int station) => templates.TryGetValue(station, out var t) && t != null;

        /// <summary>The collision of a placed mod station (OrbitBuilder.AddObstacles): its "volumes" (game units around the
        /// station), else a box / sphere of the model's bounds, or none. Axis-aligned like the originals'.</summary>
        public static List<CollisionVolume> Volumes(ModWorld.StationModel s, GameObject placed)
        {
            var list = new List<CollisionVolume>();
            var pos = placed.transform.position;
            if (s.volumes != null)
            {
                foreach (var v in s.volumes)
                {
                    var c = pos + new Vector3(v.centre.x, v.centre.y, -v.centre.z) * M;
                    list.Add(v.sphere ? CollisionVolume.Sphere(c, v.radius * M) : CollisionVolume.Box(c, v.half * M));
                }
                return list;
            }
            if (s.collision == "none") return list;
            var b = new Bounds(pos, Vector3.zero);
            bool any = false;
            foreach (var r in placed.GetComponentsInChildren<MeshRenderer>())
            {
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            if (!any) return list;
            if (s.collision == "sphere") list.Add(CollisionVolume.Sphere(b.center, Mathf.Max(b.extents.x, Mathf.Max(b.extents.y, b.extents.z))));
            else list.Add(CollisionVolume.Box(b.center, b.extents));
            return list;
        }
    }
}
