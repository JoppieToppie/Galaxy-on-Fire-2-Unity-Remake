// ModCrates.cs
// Remake mods: a ship's own loot container (ships.json "crate": a glTF / GLB file in the mod; Database CustomShipData.crate),
// left where it dies or is robbed in place of its race's container (Crate, NpcShipDamage; the other players' copies through
// NetCrate's model key). Each file loads once (ships sharing it share the template), in the background with the other mod
// models when the mods change (Preload; ModLoading), into a hidden DontDestroyOnLoad holder; a crate made before its model
// is there shows the race's container. Instantiate fits the copy to the race's container (its largest extent) unless the
// entry gives "crateSize" (game units). A crate's model key is "<mod id>|<path>". The orbits' containers', asteroids' and
// wrecks' models (stations.json "orbit.containers" / "asteroids" / "wrecks", ModWorld.ContainerModels) load with them.

using System.Collections.Generic;
using System.Threading.Tasks;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using UnityEngine;

namespace GoF2Remake.Modding
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class ModCrates
    {
        const float M = 0.05f;

        static readonly Dictionary<string, GameObject> templates = new Dictionary<string, GameObject>();
        static readonly Dictionary<string, float> extents = new Dictionary<string, float>();   // a template's largest extent (Unity)
        static readonly Dictionary<string, List<CustomShipMaterial>> materials = new Dictionary<string, List<CustomShipMaterial>>();   // an orbit container model's own
        static readonly Dictionary<int, string> shipKeys = new Dictionary<int, string>();
        static readonly Dictionary<string, float> sizes = new Dictionary<string, float>();   // a key's "crateSize" (game units)
        static readonly Dictionary<int, float> raceExtents = new Dictionary<int, float>();
        static readonly List<GLTFast.GltfImport> imports = new List<GLTFast.GltfImport>();
        static GameObject holder;
        static GLTFast.TimeBudgetPerFrameDeferAgent agent;
        static int loadingRevision = -1, loadedRevision = -1, keysRevision = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            templates.Clear(); extents.Clear(); shipKeys.Clear(); sizes.Clear(); raceExtents.Clear(); imports.Clear();
            holder = null; agent = null; loadingRevision = loadedRevision = keysRevision = -1;
        }

        /// <summary>Every mod crate model is built (or there are none).</summary>
        public static bool Ready
        {
            get
            {
                if (loadedRevision == ModManager.Revision) return true;
                Preload();
                return false;
            }
        }

        public static void Preload()
        {
            int rev = ModManager.Revision;
            if (loadingRevision == rev) return;
            loadingRevision = rev;
            _ = LoadAll(rev);
        }

        /// <summary>The model key of a ship's own crate ("mod id|path"), null: the race's container.</summary>
        public static string KeyOf(int ship)
        {
            Keys();
            return shipKeys.TryGetValue(ship, out var key) ? key : null;
        }

        /// <summary>A copy of crate model 'key' at 'at', sized to the "crateSize" its ships give it or else to race 'race''s
        /// container; null while it isn't loaded (or with no key): the caller makes the race's container.</summary>
        public static GameObject Instantiate(string key, int race, Vector3 at, Quaternion rotation)
        {
            if (string.IsNullOrEmpty(key) || loadedRevision != ModManager.Revision) { Preload(); return null; }
            if (!templates.TryGetValue(key, out var template) || template == null) return null;
            Keys();
            var go = Object.Instantiate(template, at, rotation);
            float want = sizes.TryGetValue(key, out float units) ? units * M : RaceExtent(race);
            if (want > 0f && extents.TryGetValue(key, out float have) && have > 1e-6f) go.transform.localScale = Vector3.one * (want / have);
            return go;
        }

        /// <summary>A copy of model 'key' fitted to 'extent' (Unity, its largest side); null while it isn't loaded (an orbit's
        /// asteroids: OrbitBuilder fits it to the game asteroid it stands in for).</summary>
        public static GameObject InstantiateFitted(string key, Vector3 at, Quaternion rotation, float extent, Transform parent)
        {
            if (string.IsNullOrEmpty(key) || loadedRevision != ModManager.Revision) { Preload(); return null; }
            if (!templates.TryGetValue(key, out var template) || template == null) return null;
            var go = Object.Instantiate(template, at, rotation, parent);
            if (extent > 0f && extents.TryGetValue(key, out float have) && have > 1e-6f) go.transform.localScale = Vector3.one * (extent / have);
            return go;
        }

        /// <summary>The largest side of a hierarchy's mesh bounds (Unity, as it stands).</summary>
        public static float ExtentOf(GameObject root) => root != null ? LargestExtent(root) : 0f;

        /// <summary>Model 'key''s largest side as loaded (Unity, before any fitting); 0 while it isn't loaded.</summary>
        public static float ExtentOf(string key) => !string.IsNullOrEmpty(key) && loadedRevision == ModManager.Revision && extents.TryGetValue(key, out float e) ? e : 0f;

        /// <summary>A hierarchy's mesh bounds in its root's space, its root's scale applied (an orbit's wreck hulls: their
        /// collision box, turned with them).</summary>
        public static Bounds LocalBounds(GameObject root)
        {
            var b = RootBounds(root, out bool any);
            if (!any) return new Bounds();
            var s = root.transform.localScale;
            return new Bounds(Vector3.Scale(b.center, s), Vector3.Scale(b.size, s));
        }

        /// <summary>The largest extent of race 'race''s container (Unity), what a mod crate is fitted to.</summary>
        static float RaceExtent(int race)
        {
            if (raceExtents.TryGetValue(race, out float e)) return e;
            var assets = CombatAssets.Load();
            var prefab = assets != null ? assets.Crate(race) : null;
            e = prefab != null ? LargestExtent(prefab) : 0f;
            raceExtents[race] = e;
            return e;
        }

        /// <summary>The largest side of a hierarchy's mesh bounds in its root's space (scale 1).</summary>
        static float LargestExtent(GameObject root)
        {
            var b = RootBounds(root, out bool any);
            return any ? Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z)) : 0f;
        }

        static Bounds RootBounds(GameObject root, out bool any)
        {
            var b = new Bounds(); any = false;
            var toRoot = root.transform.worldToLocalMatrix;
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                var mb = mf.sharedMesh.bounds;
                var m = toRoot * mf.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    var c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = m.MultiplyPoint3x4(c);
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
                }
            }
            return b;
        }

        /// <summary>Which ship has which crate (the active mods' ships.json "crate"), again when the mods change.</summary>
        static void Keys()
        {
            if (keysRevision == ModManager.Revision) return;
            keysRevision = ModManager.Revision;
            shipKeys.Clear(); sizes.Clear();
            foreach (var (mod, ship) in ModContent.ActiveShips())
            {
                if (ship == null || string.IsNullOrEmpty(ship.crate)) continue;
                string key = mod.Id + "|" + ship.crate;
                shipKeys[ship.index] = key;
                if (ship.crateSize > 0f && !sizes.ContainsKey(key)) sizes[key] = ship.crateSize;   // the first ship's size wins
            }
        }

        static async Task LoadAll(int rev)
        {
            foreach (var t in templates.Values) if (t != null) Object.Destroy(t);
            templates.Clear(); extents.Clear(); raceExtents.Clear(); materials.Clear();
            foreach (var i in imports) i.Dispose();
            imports.Clear();
            var files = new Dictionary<string, ModInfo>();
            try
            {
                foreach (var (mod, ship) in ModContent.ActiveShips())
                    if (ship != null && !string.IsNullOrEmpty(ship.crate)) files[mod.Id + "|" + ship.crate] = mod;
                foreach (var (mod, path, mats) in ModWorld.ContainerModels())   // (the orbits' containers, with their materials)
                {
                    files[mod.Id + "|" + path] = mod;
                    if (mats != null && mats.Count > 0) materials[mod.Id + "|" + path] = mats;
                }
            }
            catch (System.Exception e) { Debug.LogException(e); }
            if (files.Count > 0)
            {
                if (holder == null)
                {
                    holder = new GameObject("Mod crates");
                    holder.SetActive(false);
                    Object.DontDestroyOnLoad(holder);
                }
                if (agent == null)
                {
                    var go = new GameObject("Mod crate loader") { hideFlags = HideFlags.HideInHierarchy };
                    Object.DontDestroyOnLoad(go);
                    agent = go.AddComponent<GLTFast.TimeBudgetPerFrameDeferAgent>();
                    agent.SetFrameBudget(0.2f);
                }
                var tasks = new List<Task>();
                foreach (var kv in files) tasks.Add(LoadOne(rev, kv.Key, kv.Value));
                await Task.WhenAll(tasks);
            }
            if (rev != ModManager.Revision) return;
            loadedRevision = rev;
        }

        static async Task LoadOne(int rev, string key, ModInfo mod)
        {
            string path = key.Substring(key.IndexOf('|') + 1);
            try
            {
                var bytes = await Task.Run(() => mod.Source.ReadBytes(path));
                if (rev != ModManager.Revision || holder == null) return;
                if (bytes == null) { Warn(mod, $"{ModContent.ShipsFile}: the crate \"{path}\" isn't in the mod (the race's container is used)"); return; }
                var import = await ModGltf.Load(mod, bytes, agent);
                if (import == null) { Warn(mod, $"{path}: not a glTF model glTFast can read"); return; }
                imports.Add(import);
                if (rev != ModManager.Revision || holder == null) return;
                var root = new GameObject($"{mod.Id}:{path}");
                root.transform.SetParent(holder.transform, false);
                if (!await ModGltf.InstantiateMain(import, root.transform)) { Object.Destroy(root); Warn(mod, $"{path}: no scene to show"); return; }
                if (rev != ModManager.Revision || holder == null) { Object.Destroy(root); return; }
                foreach (var c in root.GetComponentsInChildren<Collider>(true)) Object.Destroy(c);
                if (materials.TryGetValue(key, out var specs)) ApplyMaterials(mod, root, specs, $"{mod.Id}:{path}");
                templates[key] = root;
                extents[key] = LargestExtent(root);
            }
            catch (System.Exception e) { Warn(mod, $"{path}: {e.Message}"); }
        }

        /// <summary>An orbit container model's "materials" (like a station model's: ModStations), by mesh / material name.</summary>
        static void ApplyMaterials(ModInfo mod, GameObject root, List<CustomShipMaterial> specs, string label)
        {
            var mats = new Material[specs.Count];
            for (int i = 0; i < mats.Length; i++)
                mats[i] = ModMaterials.FromSpec(mod, specs[i], !string.IsNullOrEmpty(specs[i].material) ? $"{label} {specs[i].material}" : $"{label} {specs[i].mesh}{specs[i].submesh}");
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                var mesh = ModShipBuilder.MeshOf(r);
                var assigned = r.sharedMaterials;
                int count = Mathf.Max(1, mesh != null ? mesh.subMeshCount : assigned.Length);
                if (assigned.Length != count) System.Array.Resize(ref assigned, count);
                for (int sub = 0; sub < count; sub++) { var m = ModShipBuilder.MaterialFor(specs, mats, r.name, sub, assigned[sub] != null ? assigned[sub].name : null); if (m != null) assigned[sub] = m; }
                r.sharedMaterials = assigned;
            }
        }

        static void Warn(ModInfo mod, string message)
        {
            if (!mod.Warnings.Contains(message)) mod.Warnings.Add(message);
            Debug.LogWarning($"Mods: {mod.Id}: {message}");
        }
    }
}
