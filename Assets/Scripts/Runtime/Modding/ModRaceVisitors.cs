// ModRaceVisitors.cs
// A custom race's optional lounge visitor model (races.json "visitor"): one GLB scene per race, loaded with the same
// glTFast / ModMaterials pipeline as ships and stations. The import stays alive for as long as its instantiated meshes do.
// Models use the game's model units (the station scene's 0.05 world scale), with their origin at the visitor's feet.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace GoF2Remake.Modding
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class ModRaceVisitors
    {
        const float GameModelScale = 0.05f;
        static readonly Dictionary<int, GameObject> templates = new Dictionary<int, GameObject>();
        static readonly List<GLTFast.GltfImport> imports = new List<GLTFast.GltfImport>();
        static GameObject holder;
        static GLTFast.TimeBudgetPerFrameDeferAgent agent;
        static int loadingRevision = -1, loadedRevision = -1;
        static Task loadTask;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            templates.Clear();
            imports.Clear();
            holder = null;
            agent = null;
            loadingRevision = loadedRevision = -1;
            loadTask = null;
            Count = Done = 0;
        }

        public static int Count { get; private set; }
        public static int Done { get; private set; }
        public static float Progress => Count == 0 ? 1f : Mathf.Clamp01((float)Done / Count);

        /// <summary>All custom race visitor models are ready (or none are configured).</summary>
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
            int revision = ModManager.Revision;
            if (loadingRevision == revision) return;
            loadingRevision = revision;
            // A project or session can change while a GLB is still importing. Serialize revisions so an older load
            // cannot clear a newer holder or dispose meshes while the next revision is building.
            loadTask = LoadAfterPrevious(revision, loadTask);
        }

        static async Task LoadAfterPrevious(int revision, Task previous)
        {
            if (previous != null)
            {
                try { await previous; }
                catch (Exception e) { Debug.LogException(e); }
            }
            if (revision != ModManager.Revision) return;
            await LoadAll(revision);
        }

        static async Task LoadAll(int revision)
        {
            foreach (var template in templates.Values) if (template != null) UnityEngine.Object.Destroy(template);
            templates.Clear();
            foreach (var import in imports) import.Dispose();
            imports.Clear();

            if (holder == null)
            {
                holder = new GameObject("Mod race visitor models");
                holder.SetActive(false);
                UnityEngine.Object.DontDestroyOnLoad(holder);
            }
            else
            {
                for (int i = holder.transform.childCount - 1; i >= 0; i--)
                    UnityEngine.Object.Destroy(holder.transform.GetChild(i).gameObject);
            }
            if (agent == null)
            {
                var go = new GameObject("Mod race visitor loader") { hideFlags = HideFlags.HideInHierarchy };
                UnityEngine.Object.DontDestroyOnLoad(go);
                agent = go.AddComponent<GLTFast.TimeBudgetPerFrameDeferAgent>();
                agent.SetFrameBudget(0.4f);
            }

            var races = ModRaces.All.Where(r => !string.IsNullOrEmpty(r.visitor)).ToList();
            Count = races.Count;
            Done = 0;
            var tasks = new List<Task>();
            foreach (var race in races) tasks.Add(LoadOne(revision, race));
            await Task.WhenAll(tasks);
            if (revision == ModManager.Revision) loadedRevision = revision;
        }

        static async Task LoadOne(int revision, ModRaces.Def race)
        {
            try
            {
                byte[] bytes = await Task.Run(() => race.mod.Source.ReadBytes(race.visitor));
                if (revision != ModManager.Revision || holder == null) return;
                if (bytes == null) { Warn(race, $"visitor model {race.visitor} could not be read"); return; }

                var import = await ModGltf.Load(race.mod, bytes, agent);
                bytes = null;
                if (import == null) { Warn(race, $"{race.visitor} is not a GLB model glTFast can read"); return; }
                if (revision != ModManager.Revision || holder == null) { import.Dispose(); return; }
                imports.Add(import);

                var root = new GameObject($"Visitor ({race.key})");
                root.transform.SetParent(holder.transform, false);
                if (!await ModGltf.InstantiateMain(import, root.transform))
                {
                    UnityEngine.Object.Destroy(root);
                    imports.Remove(import);
                    import.Dispose();
                    Warn(race, $"{race.visitor} has no scene");
                    return;
                }
                if (revision != ModManager.Revision || holder == null) { UnityEngine.Object.Destroy(root); return; }
                if (ModGltf.HasNoMeshes(root))
                {
                    UnityEngine.Object.Destroy(root);
                    imports.Remove(import);
                    import.Dispose();
                    Warn(race, $"{race.visitor} contains no mesh renderers");
                    return;
                }
                foreach (var collider in root.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.Destroy(collider);
                ModGltf.LoopAnimations(root);
                root.transform.localScale = Vector3.one * (GameModelScale * race.visitorScale);
                templates[race.race] = root;
            }
            catch (Exception e) when (revision == ModManager.Revision)
            {
                Warn(race, $"{race.visitor}: {e.Message}");
                if (!ModFileException.IsFileError(e)) Debug.LogException(e);
            }
            catch (Exception) { } // a stale load after the mods changed
            finally { if (revision == ModManager.Revision) Done++; }
        }

        static void Warn(ModRaces.Def race, string message)
        {
            string warning = $"races.json: race \"{race.localId}\": {message}";
            if (!race.mod.Warnings.Contains(warning)) race.mod.Warnings.Add(warning);
            Debug.LogWarning($"Mods: {race.mod.Id}: {warning}");
        }

        /// <summary>The custom race's loaded lounge visitor prefab, null when it has no usable custom model.</summary>
        public static GameObject Prefab(int race) =>
            loadedRevision == ModManager.Revision && templates.TryGetValue(race, out var prefab) ? prefab : null;
    }
}
