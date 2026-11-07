// ModShips.cs
// The models of the active mods' ships: each ships.json entry's glTF / GLB file loaded with glTFast (materials through
// ModGltfMaterials) and built into an AssembledObject template (ModShipBuilder), kept under a hidden DontDestroyOnLoad
// holder for the whole run; AssembledObject.LoadPrefab hands the template out for the ship's assembly ("ship_NNN_mod",
// pack "mod"), so every place that shows a ship (hangar, flight, NPCs, the item window, other players) instantiates it
// like a prefab. glTF loading is asynchronous: Preload starts it (the main menu and Bootstrap, again whenever the
// active mods change) and the menu waits for Ready before a game scene loads. A ship whose model isn't there (still
// loading, or broken) shows the Phantom's. Also each ship's shop icon (the entry's "icon" PNG, 180 x 88 like the
// originals; none = the Phantom's).
// The ships load side by side (at most ShipSlots at once: each holds its whole GLB file until glTFast has read it, and the
// GoF3 Ships mod's 79 came to 430 MB): its files are read on worker threads, its textures decoded in the background
// (ModMaterials.PreloadTexture; a GLB's embedded images the same way, ModGltf), its glTF parsed by glTFast's jobs, and the
// main-thread work of all of them shares one time budget per frame (glTFast's TimeBudgetPerFrameDeferAgent), so the game
// keeps running (the main menu's loading screen shows Progress / Current, ModLoading).
// Customizable ships (ModShipKits): a kit's part models load once each (partTemplates), and only those a build uses: the
// kit ships' default builds and the player's saved builds at the start (LoadAll), any other when a build first needs it
// (EnsureBuild / Template start the load; KitPartsLoaded and ModelsChanged follow, and the hangar / flight rebuild the
// player's ship). A kit ship's template is its default build assembled from them; a player's build ("ship_NNN_mod#<build>",
// PlayerHull.Assembly) is assembled the first time Template asks for it (variants, the most recent VariantCap kept).
// A kit's hardpoint models (ModHardpoints: weapons, turret) load with the start's parts, as part templates too. A build's skin
// (ModShipKits skins: texture sets) loads its textures with the build's parts, and ApplySkin puts them on copies of the
// materials it names when the build is assembled.
// The exhaust styles' flame models (ModExhausts) load with the rest, each as the one part of its style's own kit (their
// pictures too); one not there yet when a ship needs it loads on demand (EnsureExhaust).

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GoF2Remake.Data;
using UnityEngine;

namespace GoF2Remake.Modding
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class ModShips
    {
        public const string Pack = ModContent.Pack;
        const int FallbackShip = 10;   // the Phantom

        static readonly Dictionary<int, GameObject> templates = new Dictionary<int, GameObject>();
        static readonly Dictionary<int, Texture2D> icons = new Dictionary<int, Texture2D>();
        static readonly Dictionary<int, string> names = new Dictionary<int, string>();
        static readonly List<GLTFast.GltfImport> imports = new List<GLTFast.GltfImport>();
        static GameObject holder;
        static GLTFast.TimeBudgetPerFrameDeferAgent agent;
        static int loadingRevision = -1, loadedRevision = -1;
        static Task loading;
        static int steps, stepsDone;
        static readonly List<string> current = new List<string>();
        static System.Threading.SemaphoreSlim shipSlots;
        // Kits: the parts' models by "kit key|path", the kit ships, the players' builds made so far.
        static readonly Dictionary<string, GameObject> partTemplates = new Dictionary<string, GameObject>();
        static readonly Dictionary<int, (ModInfo mod, CustomShipData ship, ModShipKits.Kit kit)> kitShips = new Dictionary<int, (ModInfo, CustomShipData, ModShipKits.Kit)>();
        static readonly Dictionary<string, GameObject> variants = new Dictionary<string, GameObject>();
        static readonly Queue<string> variantOrder = new Queue<string>();
        static readonly HashSet<string> partsLoading = new HashSet<string>();   // "kit key|file" being loaded on demand

        /// <summary>Parts a build needed have loaded (the hangar's Customize screen shows the build then).</summary>
        public static event System.Action KitPartsLoaded;
        const int VariantCap = 48;

        /// <summary>Ships loading at once.</summary>
        static System.Threading.SemaphoreSlim ShipSlots => shipSlots ??= new System.Threading.SemaphoreSlim(Application.isMobilePlatform ? 3 : 8);

        /// <summary>How far the models are (0..1; 1 with nothing to load).</summary>
        public static float Progress => steps == 0 ? 1f : Mathf.Clamp01((float)stepsDone / steps);

        /// <summary>A ship being loaded now (its name; null: none).</summary>
        public static string Current => current.Count > 0 ? current[current.Count - 1] : null;

        /// <summary>The ships to load and those done.</summary>
        public static int Count { get; private set; }
        public static int Done { get; private set; }

        /// <summary>The mods' ship models were (re)built: anything that showed a mod ship before (another player's ship built
        /// when they joined, before the session's mods were on) builds it again (NetPlayer).</summary>
        public static event System.Action ModelsChanged;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            templates.Clear(); icons.Clear(); imports.Clear(); names.Clear();
            partTemplates.Clear(); kitShips.Clear(); variants.Clear(); variantOrder.Clear(); partsLoading.Clear(); KitPartsLoaded = null; notifyPending = false;
            skinned.Clear(); skinnedOrder.Clear();
            holder = null; agent = null; loading = null; ModelsChanged = null;
            loadingRevision = loadedRevision = -1;
            steps = stepsDone = 0; current.Clear(); Count = Done = 0;
        }

        /// <summary>The active mods' ship models are built (or there are none).</summary>
        public static bool Ready
        {
            get
            {
                if (loadedRevision == ModManager.Revision) return true;
                if (!ModContent.AnyShipModels() && templates.Count == 0 && !ModExhausts.Any) return true;
                Preload();
                return false;
            }
        }

        /// <summary>Starts building the active mods' ship models when the mods changed since the last time.</summary>
        public static void Preload()
        {
            int rev = ModManager.Revision;
            if (loadingRevision == rev) return;
            loadingRevision = rev;
            loading = LoadAll(rev);
        }

        const int StepsPerShip = 4;   // textures, file, glTF, built

        static async Task LoadAll(int rev)
        {
            Release();
            if (holder == null)
            {
                holder = new GameObject("Mod ship models");
                holder.SetActive(false);
                Object.DontDestroyOnLoad(holder);
            }
            if (agent == null)
            {
                // Active (the holder isn't): the agent measures the frame in its Update.
                var go = new GameObject("Mod ship loader") { hideFlags = HideFlags.HideInHierarchy };
                Object.DontDestroyOnLoad(go);
                agent = go.AddComponent<GLTFast.TimeBudgetPerFrameDeferAgent>();
            }
            agent.SetFrameBudget(0.6f);   // of a frame: the loading screen only animates a bar meanwhile
            var ships = ModContent.ActiveShips();
            ships.AddRange(ModContent.ModelOverrides());   // the originals whose model a mod replaces
            // Customizable ships: their kits' parts load instead of a model of their own.
            var kitted = ships.Where(x => !string.IsNullOrEmpty(x.ship.kit)).ToList();
            ships.RemoveAll(x => !string.IsNullOrEmpty(x.ship.kit));
            var kits = new List<ModShipKits.Kit>();
            foreach (var (mod, c) in kitted)
            {
                var kit = ModShipKits.KitOf(c);
                if (kit == null) { Warn(mod, $"ships.json: \"{c.name}\": no kit \"{c.kit}\" (shipkits.json of a mod that is on)"); continue; }
                kitShips[c.index] = (mod, c, kit);
                if (!kits.Contains(kit)) kits.Add(kit);
            }
            Count = ships.Count + kitted.Count;
            Done = 0;
            // Only the parts the default builds and the player's saved builds use (the rest on demand: EnsureBuild).
            var needed = new Dictionary<ModShipKits.Kit, HashSet<string>>();
            var skinTextures = new List<(ModInfo mod, string path, bool linear, bool normal)>();
            foreach (var kv in kitShips)
            {
                var (_, c, kit) = kv.Value;
                if (!needed.TryGetValue(kit, out var set)) needed[kit] = set = new HashSet<string>();
                var builds = new List<ModShipKits.Build> { ModShipKits.DefaultBuild(c, kit) };
                string key = ModContent.ShipKey(kv.Key);
                if (key != null && Session.ShipBuilds.TryGetValue(key, out var text))
                    builds.Add(ModShipKits.Normalize(kit, c.kitType, ModShipKits.Build.Parse(text), ModShipKits.DefaultBuild(c, kit)));
                foreach (var b in builds)
                {
                    set.UnionWith(BuildFiles(kit, b));
                    skinTextures.AddRange(SkinTextures(kit, b));
                }
                set.UnionWith(ModHardpoints.Files(kit));   // the weapon / turret models: few, all at the start
            }
            var exhausts = ModExhausts.All();
            steps = ships.Count * StepsPerShip + needed.Values.Sum(v => v.Count) + kitted.Count + exhausts.Count(e => e.IsFlame);
            stepsDone = 0;
            current.Clear();
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var tasks = new List<Task>();
            foreach (var (mod, c) in ships) tasks.Add(LoadShip(rev, mod, c));
            foreach (var kit in kits) tasks.Add(LoadKit(rev, kit, needed.TryGetValue(kit, out var files) ? files : new HashSet<string>()));
            foreach (var t in skinTextures.Distinct()) tasks.Add(ModMaterials.PreloadTexture(t.mod, t.path, t.linear, false, t.normal));
            // The exhaust styles: the flame models, and the pictures of Galaxy on Fire 3's effects.
            foreach (var e in exhausts)
            {
                if (e.IsFlame) tasks.Add(LoadKit(rev, e.kit, new[] { e.model }));
                else
                    foreach (var f in EngineFxTextures(e.mod, e.fx)) tasks.Add(ModMaterials.PreloadTexture(e.mod, f, true, false, false));
            }
            await Task.WhenAll(tasks);
            if (rev != ModManager.Revision) return;   // the mods changed meanwhile: a newer load runs
            // The kit ships' default builds (what dealers sell and NPCs fly), their icons and textures.
            foreach (var (mod, c) in kitted)
            {
                try
                {
                    await Task.WhenAll(TexturesOf(c, mod).Select(t => ModMaterials.PreloadTexture(mod, t.path, t.linear, t.readable, t.normal)));
                    if (rev != ModManager.Revision || holder == null) return;
                    var icon = ModMaterials.Texture(mod, c.icon, false);
                    if (icon != null) icons[c.index] = icon;
                    if (!kitShips.TryGetValue(c.index, out var info)) continue;
                    templates[c.index] = Assemble(info.mod, info.ship, info.kit, ModShipKits.DefaultBuild(c, info.kit), c.assembly);
                    names[c.index] = c.name;
                }
                catch (System.Exception e) { Warn(mod, $"ships.json: \"{c.name}\": {e.Message}"); Debug.LogException(e); }
                finally { stepsDone++; Done++; }
            }
            loadedRevision = rev;
            if (ships.Count > 0) Debug.Log($"Mods: {ships.Count} ship model(s) loaded in {watch.ElapsedMilliseconds} ms");
            ModelsChanged?.Invoke();
        }

        /// <summary>The PNGs a ship's entry uses, with how they are read (ModMaterials.FromSpec, the throttle glow masks).</summary>
        static IEnumerable<(string path, bool linear, bool readable, bool normal)> TexturesOf(CustomShipData c, ModInfo mod)
        {
            if (!string.IsNullOrEmpty(c.icon) && !GameModels.Is(c.icon)) yield return (c.icon, false, false, false);
            if (c.materials != null)
                foreach (var m in c.materials)
                {
                    if (m == null) continue;
                    if (!string.IsNullOrEmpty(m.diffuse)) yield return (m.diffuse, false, false, false);
                    if (!string.IsNullOrEmpty(m.normal)) yield return (m.normal, true, false, true);
                    if (!string.IsNullOrEmpty(m.metallicSmoothness)) yield return (m.metallicSmoothness, true, false, false);
                    if (!string.IsNullOrEmpty(m.specularGlossiness)) yield return (m.specularGlossiness, false, false, false);
                    if (!string.IsNullOrEmpty(m.emission)) yield return (m.emission, false, false, false);
                    if (!string.IsNullOrEmpty(m.detailAlbedo)) yield return (m.detailAlbedo, true, false, false);
                    if (!string.IsNullOrEmpty(m.detailNormal)) yield return (m.detailNormal, true, false, true);
                    if (!string.IsNullOrEmpty(m.flameNoise)) yield return (m.flameNoise, true, false, false);
                    if (!string.IsNullOrEmpty(m.damageEmission)) yield return (m.damageEmission, false, false, false);
                    if (!string.IsNullOrEmpty(m.damageDetail)) yield return (m.damageDetail, false, false, false);
                    if (!string.IsNullOrEmpty(m.damageNormal)) yield return (m.damageNormal, true, false, true);
                }
            if (c.throttleGlow != null && !string.IsNullOrEmpty(c.throttleGlow.mask)) yield return (c.throttleGlow.mask, false, true, false);
            if (c.extraGlows != null)
                foreach (var g in c.extraGlows) if (g != null && !string.IsNullOrEmpty(g.mask)) yield return (g.mask, false, true, false);
            foreach (var f in EngineFxTextures(mod, c.engineFx)) yield return (f, true, false, false);
            if (c.addParts != null)
                foreach (var p in c.addParts) if (p != null && !string.IsNullOrEmpty(p.texture)) yield return (p.texture, false, false, false);
        }

        /// <summary>An engineFx's pictures (EngineFx): its engine meshes' and its particles', read raw as GoF3 reads them.</summary>
        public static IEnumerable<string> EngineFxTextures(ModInfo mod, CustomEngineFx fx)
        {
            if (fx == null) yield break;
            if (!string.IsNullOrEmpty(fx.meshes))
                foreach (var f in EngineModels.Textures(mod, fx.meshes)) yield return f;
            foreach (var em in new[] { fx.trailParticles, fx.contrailParticles, fx.boostParticles })
                if (em?.Any == true) yield return em.texture;
        }

        /// <summary>A ship's add parts (ships.json "addParts": its lights and engine glows beside the hull) as glTF scenes
        /// under 'parent', the ones that load; a part that doesn't is a warning and left out.</summary>
        static async Task<List<(CustomAddPart spec, GameObject scene)>> LoadAddParts(int rev, ModInfo mod, CustomShipData c, Transform parent)
        {
            var parts = new List<(CustomAddPart, GameObject)>();
            if (c.addParts == null) return parts;
            foreach (var p in c.addParts)
            {
                if (p == null || string.IsNullOrEmpty(p.model) || rev != ModManager.Revision) continue;
                var bytes = await Task.Run(() => mod.Source.Exists(p.model) ? mod.Source.ReadBytes(p.model) : null);
                if (bytes == null || rev != ModManager.Revision || parent == null) continue;
                var import = await ModGltf.Load(mod, bytes, agent);
                if (import == null) { Warn(mod, $"{p.model}: not a glTF model glTFast can read"); continue; }
                imports.Add(import);
                if (rev != ModManager.Revision || parent == null) continue;
                var scene = new GameObject("scene");
                scene.transform.SetParent(parent, false);
                if (!await ModGltf.InstantiateMain(import, scene.transform)) { Object.Destroy(scene); Warn(mod, $"{p.model}: no scene to show"); continue; }
                parts.Add((p, scene));
            }
            return parts;
        }

        /// <summary>One ship: its textures and model file at once, then glTFast, then the template.</summary>
        static async Task LoadShip(int rev, ModInfo mod, CustomShipData c)
        {
            var slots = ShipSlots;
            await slots.WaitAsync();
            string label = c.name;
            current.Add(label);
            int counted = 0;
            void Step() { counted++; stepsDone++; }
            try
            {
                if (rev != ModManager.Revision) return;
                var textures = new List<Task>();
                foreach (var (path, linear, readable, normal) in TexturesOf(c, mod)) textures.Add(ModMaterials.PreloadTexture(mod, path, linear, readable, normal));
                bool gameModel = GameModels.Is(c.model);
                var file = string.IsNullOrEmpty(c.model) || gameModel ? Task.FromResult<byte[]>(null) : Task.Run(() => mod.Source.ReadBytes(c.model));
                await Task.WhenAll(textures);
                Step();
                var bytes = await file;
                Step();
                if (rev != ModManager.Revision) return;
                var icon = GameModels.Is(c.icon) ? GameModels.Icon(c.icon) : ModMaterials.Texture(mod, c.icon, false);   // "gof2:ship_005": the game's
                if (icon != null) icons[c.index] = icon;
                // One of the game's own models (GameModels): nothing to build, Database.ShipAssembly hands the game's out.
                if (gameModel) return;
                if (bytes == null) { Warn(mod, $"ships.json: \"{c.name}\": model {c.model} not found"); return; }
                var import = await ModGltf.Load(mod, bytes, agent);
                bytes = null;
                if (import == null) { Warn(mod, $"{c.model}: not a glTF model glTFast can read"); return; }
                imports.Add(import);
                Step();
                if (rev != ModManager.Revision || holder == null) return;
                var scene = new GameObject("scene");
                scene.transform.SetParent(holder.transform, false);
                if (!await ModGltf.InstantiateMain(import, scene.transform)) { Object.Destroy(scene); Warn(mod, $"{c.model}: no scene to show"); return; }
                if (rev != ModManager.Revision || holder == null) return;
                if (ModGltf.HasNoMeshes(scene)) Warn(mod, $"{c.model}: the model has no meshes to show");   // #134: an empty turntable said nothing
                var addParts = await LoadAddParts(rev, mod, c, holder != null ? holder.transform : null);
                if (rev != ModManager.Revision || holder == null) return;
                var template = ModShipBuilder.Build(c, mod, scene, addParts: addParts);
                template.transform.SetParent(holder.transform, false);
                templates[c.index] = template;
                names[c.index] = c.name;
                Debug.Log($"Mods: {mod.Id}: ship {c.index} \"{c.name}\" built from {c.model}");
            }
            catch (System.Exception e) when (rev == ModManager.Revision) { Warn(mod, $"{c.model}: {e.Message}"); if (!ModFileException.IsFileError(e)) Debug.LogException(e); }
            catch (System.Exception) { }   // a load the mods changed under (ModManager.Scan closes the old archives): a newer one runs
            finally
            {
                if (rev == ModManager.Revision)
                {
                    stepsDone += StepsPerShip - counted;   // the steps a failed ship skipped
                    Done++;
                }
                current.Remove(label);
                slots.Release();
            }
        }

        /// <summary>A kit's model files: every part's and its tier extensions'.</summary>
        static IEnumerable<string> KitFiles(ModShipKits.Kit kit) =>
            kit.parts.SelectMany(p => new[] { p.model }.Concat(p.tiers)).Where(f => !string.IsNullOrEmpty(f)).Distinct();

        /// <summary>A kit's parts: each model file loaded once (ShipSlots of them at once) into a part template under the
        /// holder, the kit's materials applied.</summary>
        static async Task LoadKit(int rev, ModShipKits.Kit kit, ICollection<string> files)
        {
            await Task.WhenAll(kit.materials.SelectMany(m => new[] { (m.diffuse, false, false), (m.normal, true, true), (m.metallicSmoothness, true, false), (m.specularGlossiness, false, false),
                                                                     (m.emission, false, false), (m.detailAlbedo, true, false), (m.detailNormal, true, true), (m.flameNoise, true, false), (m.damageEmission, false, false),
                                                                     (m.damageDetail, false, false), (m.damageNormal, true, true) })
                                            .Where(t => !string.IsNullOrEmpty(t.Item1))
                                            .Select(t => ModMaterials.PreloadTexture(kit.mod, t.Item1, t.Item2, false, t.Item3)));
            string label = kit.Name;
            current.Add(label);
            try
            {
                await Task.WhenAll(files.Select(f => LoadPart(rev, kit, f)));
            }
            finally { current.Remove(label); }
        }

        static async Task LoadPart(int rev, ModShipKits.Kit kit, string file)
        {
            var slots = ShipSlots;
            await slots.WaitAsync();
            try
            {
                if (rev != ModManager.Revision) return;
                var bytes = await Task.Run(() => kit.mod.Source.ReadBytes(file));
                if (bytes == null) { Warn(kit.mod, $"{ModShipKits.File}: {kit.localId}: {file} not found"); return; }
                var import = await ModGltf.Load(kit.mod, bytes, agent);
                bytes = null;
                if (import == null) { Warn(kit.mod, $"{file}: not a glTF model glTFast can read"); return; }
                imports.Add(import);
                if (rev != ModManager.Revision || holder == null) return;
                var part = new GameObject(file);
                part.transform.SetParent(holder.transform, false);
                if (!await ModGltf.InstantiateMain(import, part.transform)) { Object.Destroy(part); Warn(kit.mod, $"{file}: no scene to show"); return; }
                if (rev != ModManager.Revision || holder == null) return;
                ModShipBuilder.ApplyMaterials(kit.mod, kit.materials, part.GetComponentsInChildren<Renderer>(true), kit.Key);
                partTemplates[kit.Key + "|" + file] = part;
            }
            catch (System.Exception e) { Warn(kit.mod, $"{file}: {e.Message}"); Debug.LogException(e); }
            finally
            {
                if (rev == ModManager.Revision) stepsDone++;
                slots.Release();
            }
        }

        /// <summary>The model files a build shows: each slot's part and, at a tier, its extension.</summary>
        static IEnumerable<string> BuildFiles(ModShipKits.Kit kit, ModShipKits.Build build)
        {
            foreach (string slot in kit.slots)
            {
                if (!build.parts.TryGetValue(slot, out var id)) continue;
                var part = kit.Find(id);
                if (part == null) continue;
                yield return part.model;
                if (build.tier > 0 && build.tier <= part.tiers.Count) yield return part.tiers[build.tier - 1];
            }
        }

        /// <summary>The textures a build's skin uses (none without a skin).</summary>
        static IEnumerable<(ModInfo mod, string path, bool linear, bool normal)> SkinTextures(ModShipKits.Kit kit, ModShipKits.Build build)
        {
            var skin = kit?.FindSkin(build?.skin);
            var livery = kit?.LiveryOf(build);
            var all = Enumerable.Empty<(ModInfo, string, bool, bool)>();
            if (skin != null) { var mod = skin.mod ?? kit.mod; all = skin.Textures().Select(t => (mod, t.path, t.linear, t.normal)); }
            if (livery != null) all = all.Append((livery.mod ?? kit.mod, livery.texture, true, false));   // a mask: linear
            if (kit?.DecalOf(build) is ModShipKits.Decal decal)
                foreach (var path in new[] { decal.insignia, decal.type })
                    if (!string.IsNullOrEmpty(path)) all = all.Append((decal.mod ?? kit.mod, path, true, false));   // masks: linear
            // the engine trails' textures (the player's ship only shows them, but they load with the build)
            foreach (var trail in new[] { kit?.TrailOf(build, false), kit?.TrailOf(build, true) })
                if (trail != null)
                    foreach (var t in trail.Textures()) all = all.Append((trail.mod ?? kit.mod, t.path, t.linear, t.normal));
            return all;
        }

        static bool SkinReady(ModShipKits.Kit kit, ModShipKits.Build build) =>
            SkinTextures(kit, build).All(t => ModMaterials.Has(t.mod, t.path, t.linear, false, t.normal));

        /// <summary>A kit's loaded model file (a part, an extension or a hardpoint model; ModHardpoints), null = not loaded.</summary>
        internal static GameObject PartTemplate(ModShipKits.Kit kit, string file) =>
            kit != null && !string.IsNullOrEmpty(file) && partTemplates.TryGetValue(kit.Key + "|" + file, out var t) ? t : null;

        static bool PartsReady(ModShipKits.Kit kit, ModShipKits.Build build) =>
            BuildFiles(kit, build).All(f => partTemplates.ContainsKey(kit.Key + "|" + f)) && SkinReady(kit, build);

        /// <summary>The parts a kit ship's build shows are loaded (true; also for a ship that is no kit ship), or start loading
        /// (false: KitPartsLoaded and ModelsChanged follow once they are).</summary>
        public static bool EnsureBuild(int ship, ModShipKits.Build build)
        {
            if (build == null) return true;
            if (!kitShips.TryGetValue(ship, out var info))
            {
                // A ship with customizations but no kit: only its skin's textures.
                var custom = ModShipKits.KitShip(ship) == null ? ModShipKits.CustomizationOf(ship) : null;
                if (custom == null || SkinReady(custom, build)) return true;
                LoadOnDemand(custom, new List<string>(), SkinTextures(custom, build).ToList());
                return false;
            }
            if (PartsReady(info.kit, build)) return true;
            LoadOnDemand(info.kit, BuildFiles(info.kit, build).ToList(), SkinTextures(info.kit, build).ToList());
            return false;
        }

        /// <summary>An exhaust style's flame model, loading it when it isn't (ModShips.ModelsChanged follows).</summary>
        internal static void EnsureExhaust(ModExhausts.Style style)
        {
            if (style?.kit == null || PartTemplate(style.kit, style.model) != null) return;
            LoadOnDemand(style.kit, new List<string> { style.model }, new List<(ModInfo, string, bool, bool)>());
        }

        /// <summary>Remake mods (ModTraffic): a kit ship's build is loaded, its parts and skin (true for a ship that is no kit ship).</summary>
        internal static bool BuildLoaded(int ship, ModShipKits.Build build) =>
            build == null || !kitShips.TryGetValue(ship, out var info) || PartsReady(info.kit, build);

        /// <summary>Remake mods (ModTraffic): a kit part's model file is loaded.</summary>
        internal static bool PartLoaded(ModShipKits.Kit kit, ModShipKits.Part part) => PartTemplate(kit, part?.model) != null;

        /// <summary>A kit's part files loaded so far.</summary>
        internal static int LoadedParts(ModShipKits.Kit kit) => kit == null ? 0 : partTemplates.Keys.Count(k => k.StartsWith(kit.Key + "|"));

        /// <summary>Remake mods (ModTraffic.Warm): loads a build's parts in the background without rebuilding anything shown
        /// (no KitPartsLoaded / ModelsChanged of its own), so NPCs can fly it later.</summary>
        internal static void Warm(int ship, ModShipKits.Build build)
        {
            if (build == null || !kitShips.TryGetValue(ship, out var info) || PartsReady(info.kit, build)) return;
            LoadOnDemand(info.kit, BuildFiles(info.kit, build).ToList(), SkinTextures(info.kit, build).ToList(), quiet: true);
        }

        // A request found its files already being loaded by a quiet warm-up: that load tells when it is done.
        static bool notifyPending;

        static async void LoadOnDemand(ModShipKits.Kit kit, List<string> files, List<(ModInfo mod, string path, bool linear, bool normal)> skinTextures,
                                       bool quiet = false)
        {
            if ((holder == null || agent == null) && files.Count > 0) return;
            int rev = ModManager.Revision;
            if (!quiet && (files.Any(f => partsLoading.Contains(kit.Key + "|" + f))
                           || skinTextures.Any(t => partsLoading.Contains(t.mod.Id + "|texture|" + t.path)))) notifyPending = true;
            var missing = files.Where(f => !partTemplates.ContainsKey(kit.Key + "|" + f) && partsLoading.Add(kit.Key + "|" + f)).ToList();
            var textures = skinTextures.Where(t => !ModMaterials.Has(t.mod, t.path, t.linear, false, t.normal)
                                                   && partsLoading.Add(t.mod.Id + "|texture|" + t.path)).ToList();
            if (missing.Count == 0 && textures.Count == 0) return;
            try
            {
                await Task.WhenAll(missing.Select(f => LoadPart(rev, kit, f))
                    .Concat(textures.Select(t => ModMaterials.PreloadTexture(t.mod, t.path, t.linear, false, t.normal))));
                // A texture the background way couldn't make: the plain way (it warns, and stays null), so the build isn't
                // waiting for it for good.
                if (rev == ModManager.Revision) foreach (var t in textures) ModMaterials.Texture(t.mod, t.path, t.linear, false, t.normal);
            }
            finally
            {
                foreach (var f in missing) partsLoading.Remove(kit.Key + "|" + f);
                foreach (var t in textures) partsLoading.Remove(t.mod.Id + "|texture|" + t.path);
            }
            if (rev != ModManager.Revision || (quiet && !notifyPending)) return;
            notifyPending = false;
            KitPartsLoaded?.Invoke();
            ModelsChanged?.Invoke();
        }

        /// <summary>A kit ship's template for 'build': its parts (and the tier's extensions) copied from the part templates into
        /// one scene, built like any mod ship at the kit's scale (ModShipBuilder).</summary>
        static GameObject Assemble(ModInfo mod, CustomShipData c, ModShipKits.Kit kit, ModShipKits.Build build, string name)
        {
            var scene = new GameObject("scene");
            scene.transform.SetParent(holder.transform, false);
            // The anchors the build's parts offer (glTF coordinates; glTFast mirrors x into Unity's).
            var anchors = new Dictionary<string, Vector3>(System.StringComparer.OrdinalIgnoreCase);
            foreach (string slot in kit.slots)
                if (build.parts.TryGetValue(slot, out var aid) && kit.Find(aid) is ModShipKits.Part ap)
                    foreach (var kv in ap.anchors) anchors[kv.Key] = new Vector3(-kv.Value.x, kv.Value.y, kv.Value.z);
            void Add(string file, Vector3 offset)
            {
                if (string.IsNullOrEmpty(file) || !partTemplates.TryGetValue(kit.Key + "|" + file, out var t) || t == null) return;
                var go = Object.Instantiate(t, scene.transform, false);
                go.name = file;
                go.transform.localPosition += offset;
            }
            foreach (string slot in kit.slots)
            {
                if (!build.parts.TryGetValue(slot, out var id)) continue;
                var part = kit.Find(id);
                if (part == null) continue;
                var offset = !string.IsNullOrEmpty(part.anchor) && anchors.TryGetValue(part.anchor, out var a) ? a : Vector3.zero;
                Add(part.model, offset);
                if (build.tier > 0 && build.tier <= part.tiers.Count) Add(part.tiers[build.tier - 1], offset);
            }
            // The build's mounts (its parts' exhausts carry the engine glow) and colours (ModShipKits colour slots: the engine
            // glow through the ship data, the rest on the materials).
            var data = JsonUtility.FromJson<CustomShipData>(JsonUtility.ToJson(c));
            data.mounts = ModShipKits.MountsFor(c, kit, build);
            var skin = kit.FindSkin(build.skin);
            if (skin?.engineColor != null && skin.engineColor.Length >= 3)
            {
                // The skin's engine colour on every exhaust (copies: the mounts are the kit's / the ship's own).
                data.engineGlowColor = skin.engineColor;
                data.mounts = data.mounts.Select(m => m.slotType != 3 ? m : CopyWithoutGlowColor(m)).ToList();
            }
            foreach (var cs in kit.colorSlots)
                if (cs.targets.Exists(t => t.property == "engine") && build.colors.TryGetValue(cs.id, out var ev)
                    && ModShipKits.Resolve(kit, cs, ev, out var engine, out _, out _))
                    data.engineGlowColor = new[] { engine.r, engine.g, engine.b };
            var template = ModShipBuilder.Build(data, mod, scene, kit.scale, kit.yaw, kit.materials.Exists(m => m != null && m.flame));
            template.name = name;
            template.transform.SetParent(holder.transform, false);
            ApplySkin(template, kit, skin);
            ApplyLivery(template, kit, build);   // (before the colours: a "livery" colour slot recolours it)
            ApplyDecals(template, kit, build);   // (the decal colours too)
            ApplyColors(template, kit, build);
            DamageEmbers.Add(template, kit.materials.Where(m => m != null && (!string.IsNullOrEmpty(m.damageEmission) || !string.IsNullOrEmpty(m.damageDetail)) && !string.IsNullOrEmpty(m.material)).Select(m => m.material));
            EngineMaterialGlow.Add(template, kit.materials.Where(m => m != null && m.engine && !string.IsNullOrEmpty(m.material)).Select(m => m.material),
                kit.materials.Where(m => m != null && m.boost && !string.IsNullOrEmpty(m.material)).Select(m => m.material),
                kit.materials.Where(m => m != null && m.engine && m.engineIdle > 0f && !string.IsNullOrEmpty(m.material))
                    .GroupBy(m => m.material).ToDictionary(g => g.Key, g => g.First().engineIdle));
            return template;
        }

        static WeaponMount CopyWithoutGlowColor(WeaponMount m)
        {
            var copy = JsonUtility.FromJson<WeaponMount>(JsonUtility.ToJson(m));
            copy.glowColor = null;
            return copy;
        }

        /// <summary>A skin's textures onto copies of the materials it names (the name contains its 'material'): diffuse =
        /// _BaseMap, emission = _EmissionMap (its colour x intensity, white without one), metallicSmoothness = _MetallicGlossMap
        /// (URP Lit's layout: R metallic, A smoothness), specularGlossiness = _SpecGlossMap (a material in URP's specular setup:
        /// RGB specular, A smoothness), normal = _BumpMap, color = _BaseColor; each texture keeps the
        /// material's tiling. A texture that doesn't load keeps the model's own.</summary>
        static void ApplySkin(GameObject root, ModShipKits.Kit kit, ModShipKits.Skin skin)
        {
            if (skin == null || skin.materials.Count == 0) return;
            var copies = new Dictionary<Material, Material>();
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null) continue;
                    if (copies.TryGetValue(m, out var done)) { mats[i] = done; changed = true; continue; }
                    string n = m.name.ToLowerInvariant();
                    var spec = skin.materials.Find(x => n.Contains(x.material.ToLowerInvariant()));
                    if (spec == null) continue;
                    var copy = new Material(m) { name = m.name };
                    var mod = skin.mod ?? kit.mod;
                    Texture2D Tex(string path, bool linear, bool normal = false) => string.IsNullOrEmpty(path) ? null : ModMaterials.Texture(mod, path, linear, false, normal);
                    if (Tex(spec.diffuse, false) is Texture2D d) { copy.SetTexture("_BaseMap", d); if (copy.HasProperty("_MainTex")) copy.SetTexture("_MainTex", d); }
                    if (spec.color != null && copy.HasProperty("_BaseColor"))
                        copy.SetColor("_BaseColor", new Color(spec.color[0], spec.color[1], spec.color[2], copy.GetColor("_BaseColor").a));
                    if (Tex(spec.emission, false) is Texture2D e)
                    {
                        copy.SetTexture("_EmissionMap", e);
                        var ec = spec.emissionColor != null ? new Color(spec.emissionColor[0], spec.emissionColor[1], spec.emissionColor[2]) : Color.white;
                        copy.SetColor("_EmissionColor", ec * spec.emissionIntensity);
                        copy.EnableKeyword("_EMISSION");
                    }
                    else if (spec.emissionColor != null)
                    {
                        copy.SetColor("_EmissionColor", new Color(spec.emissionColor[0], spec.emissionColor[1], spec.emissionColor[2]) * spec.emissionIntensity);
                        copy.EnableKeyword("_EMISSION");
                    }
                    if (Tex(spec.metallicSmoothness, true) is Texture2D ms)
                    {
                        copy.SetTexture("_MetallicGlossMap", ms);
                        if (copy.HasProperty("_Smoothness")) copy.SetFloat("_Smoothness", 1f);
                    }
                    if (copy.IsKeywordEnabled("_SPECULAR_SETUP") && Tex(spec.specularGlossiness, false) is Texture2D sg)
                    {
                        copy.SetTexture("_SpecGlossMap", sg);
                        if (copy.HasProperty("_Smoothness")) copy.SetFloat("_Smoothness", 1f);
                    }
                    if (Tex(spec.normal, true, true) is Texture2D nm) copy.SetTexture("_BumpMap", nm);   // the templates keep their map keywords
                    copies[m] = copy;
                    mats[i] = copy;
                    changed = true;
                }
                if (changed) r.sharedMaterials = mats;
            }
        }

        /// <summary>A build's colours onto its renderers: each slot's targets recolour copies of the materials whose name
        /// contains the target's 'material' (base: _BaseColor x intensity and the value's metallic / smoothness; emission:
        /// _EmissionColor x intensity; boost: an engine flame's _BoostColor x intensity).</summary>
        /// ('cache': copies to reuse and keep, ModHardpoints' weapon models per build; null = new ones for this root.)
        internal static void ApplyColors(GameObject root, ModShipKits.Kit kit, ModShipKits.Build build, Dictionary<Material, Material> cache = null)
        {
            if (kit.colorSlots.Count == 0) return;
            var copies = cache ?? new Dictionary<Material, Material>();
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null) continue;
                    if (copies.TryGetValue(m, out var done)) { mats[i] = done; changed = true; continue; }
                    Material copy = null;
                    string n = m.name.ToLowerInvariant();
                    foreach (var cs in kit.colorSlots)
                    {
                        if (cs.IsStrength || !build.colors.TryGetValue(cs.id, out var v) || !ModShipKits.Resolve(kit, cs, v, out var col, out float metal, out float smooth)) continue;
                        float strength = ModShipKits.StrengthOf(kit, build, cs.id);
                        foreach (var t in cs.targets)
                        {
                            if (t.property == "engine" || string.IsNullOrEmpty(t.material) || !n.Contains(t.material.ToLowerInvariant())) continue;
                            copy ??= new Material(m) { name = m.name };
                            if (t.property == "emission")
                            {
                                copy.SetColor("_EmissionColor", col.linear * (t.intensity * strength));
                                copy.EnableKeyword("_EMISSION");
                            }
                            else if (t.property == "decal1" || t.property == "decal2" || t.property == "decal3")
                            {
                                // A decal's colour 1, 2 or border (ApplyDecals: GoF2/Hull's _DecalColor1..3, linear).
                                if (!copy.HasProperty("_DecalColor1")) continue;
                                var dcol = col.linear * t.intensity; dcol.a = 1f;
                                copy.SetColor("_DecalColor" + t.property.Substring(5), dcol);
                            }
                            else if (t.property == "livery")
                            {
                                // The livery's colour (ApplyLivery: GoF2/Hull's _LiveryColor rgb, metallic in a; its smoothness
                                // and the mask's exponent, 1 + 1.2 luminance, in _LiveryParams2), only where a livery shows.
                                if (!copy.HasProperty("_LiveryColor") || copy.GetVector("_LiveryParams").y <= 0f) continue;
                                var lc = col * t.intensity;
                                float lm = metal >= 0f ? metal : copy.GetColor("_LiveryColor").a;
                                copy.SetColor("_LiveryColor", new Color(lc.r, lc.g, lc.b, lm));
                                var p2 = copy.GetVector("_LiveryParams2");
                                p2.z = 1f + 1.2f * Mathf.Clamp01(lc.r * 0.2126f + lc.g * 0.7152f + lc.b * 0.0722f);
                                if (smooth >= 0f) p2.w = smooth;
                                copy.SetVector("_LiveryParams2", p2);
                            }
                            else if (t.property == "boost")
                            {
                                // An engine flame's boost colour (GoF2/ModEngineFlame: the boost cards, the manoeuvring jets).
                                if (copy.HasProperty("_BoostColor")) { var b = col.linear * t.intensity; b.a = 1f; copy.SetColor("_BoostColor", b); }
                            }
                            else
                            {
                                var c = col * t.intensity; c.a = copy.HasProperty("_BaseColor") ? copy.GetColor("_BaseColor").a : 1f;
                                if (copy.HasProperty("_BaseColor")) copy.SetColor("_BaseColor", c);
                                if (metal >= 0f && copy.HasProperty("_Metallic")) copy.SetFloat("_Metallic", metal);
                                if (smooth >= 0f && copy.HasProperty("_Smoothness")) copy.SetFloat("_Smoothness", smooth);
                            }
                        }
                    }
                    if (copy == null) continue;
                    copies[m] = copy;
                    mats[i] = copy;
                    changed = true;
                }
                if (changed) r.sharedMaterials = mats;
            }
        }

        /// <summary>A build's livery (ModShipKits.Kit.LiveryOf) onto copies of the kit's "livery" materials: GoF2/Hull's
        /// _Livery* properties (HullForwardPass.hlsl), the projection over the ship's largest half-extent in the parts' object
        /// space (EVERSPACE 2's ShipExtent, the ship's bounding box; the parts share one origin).</summary>
        static void ApplyLivery(GameObject root, ModShipKits.Kit kit, ModShipKits.Build build)
        {
            var livery = kit.LiveryOf(build);
            var names = kit.materials.Where(m => m != null && m.livery && !string.IsNullOrEmpty(m.material)).Select(m => m.material.ToLowerInvariant()).ToList();
            if (livery == null || names.Count == 0) return;
            var tex = ModMaterials.Texture(livery.mod ?? kit.mod, livery.texture, true);
            if (tex == null) return;
            tex.wrapMode = TextureWrapMode.Repeat;
            // The ship's extent in object space (the parts' meshes share it; LOD and glow layers left out).
            var bounds = new Bounds();
            bool any = false;
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null || mf.name.Contains("_lod_gen") || mf.name.EndsWith("_add")) continue;
                var b = mf.sharedMesh.bounds;
                if (!any) { bounds = b; any = true; } else bounds.Encapsulate(b);
            }
            if (!any) return;
            var ext = bounds.extents;   // BP_ShipBase: ShipExtent = the bounds' BoxExtent (half its size); the projection stays on the origin
            float extent = Mathf.Max(ext.x, Mathf.Max(ext.y, ext.z));
            if (extent <= 0f) return;
            float lum = Mathf.Clamp01(livery.color.r * 0.2126f + livery.color.g * 0.7152f + livery.color.b * 0.0722f);
            var copies = new Dictionary<Material, Material>();
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null || !m.HasProperty("_LiveryMap")) continue;
                    if (copies.TryGetValue(m, out var done)) { mats[i] = done; changed = true; continue; }
                    string n = m.name.ToLowerInvariant();
                    if (!names.Exists(x => n.Contains(x))) continue;
                    var copy = new Material(m) { name = m.name };
                    copy.SetTexture("_LiveryMap", tex);
                    copy.SetVector("_LiveryParams", new Vector4(0.5f / extent, livery.opacity, livery.mirrorX ? 1f : 0f, livery.mirrorY ? 1f : 0f));
                    copy.SetVector("_LiveryTransform", new Vector4(livery.scale.x, livery.scale.y, livery.offset.x, livery.offset.y));
                    copy.SetVector("_LiveryParams2", new Vector4(livery.tiling ? 1f : 0f, 1f - Mathf.Clamp01(livery.clipAngle / 90f), 1f + 1.2f * lum, livery.smoothness));
                    copy.SetColor("_LiveryColor", new Color(livery.color.r, livery.color.g, livery.color.b, livery.metallic));
                    copy.SetVector("_LiveryForward", kit.liveryForward);
                    copy.SetVector("_LiveryRight", kit.liveryRight);
                    copy.SetVector("_LiveryUp", kit.liveryUp);
                    copies[m] = copy;
                    mats[i] = copy;
                    changed = true;
                }
                if (changed) r.sharedMaterials = mats;
            }
        }

        /// <summary>A build's decal (ModShipKits.Kit.DecalOf) onto copies of the kit's "decal" materials: its insignia or
        /// type mask as the base map in GoF2/Hull's decal mode (_DecalParams.x; the colours from the "decal1..3" colour slots,
        /// ApplyColors). No decal: the decal faces stay empty (a clear map, cut away by the alpha clip).</summary>
        static void ApplyDecals(GameObject root, ModShipKits.Kit kit, ModShipKits.Build build)
        {
            var specs = kit.materials.Where(m => m != null && !string.IsNullOrEmpty(m.decal) && !string.IsNullOrEmpty(m.material)).ToList();
            if (specs.Count == 0) return;
            var decal = kit.DecalOf(build);
            Texture2D Mask(string kind)
            {
                string path = decal == null ? null : kind == "type" ? decal.type : decal.insignia;
                return string.IsNullOrEmpty(path) ? null : ModMaterials.Texture(decal.mod ?? kit.mod, path, true);
            }
            var copies = new Dictionary<Material, Material>();
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null || !m.HasProperty("_DecalParams")) continue;
                    if (copies.TryGetValue(m, out var done)) { mats[i] = done; changed = true; continue; }
                    string n = m.name.ToLowerInvariant();
                    var spec = specs.Find(x => n.Contains(x.material.ToLowerInvariant()));
                    if (spec == null) continue;
                    var copy = new Material(m) { name = m.name };
                    var tex = Mask(spec.decal.ToLowerInvariant());
                    if (tex != null)
                    {
                        copy.SetTexture("_BaseMap", tex);
                        if (copy.HasProperty("_MainTex")) copy.SetTexture("_MainTex", tex);
                        copy.SetVector("_DecalParams", new Vector4(1f, 0f, 0f, 0f));
                    }
                    else copy.SetVector("_DecalParams", new Vector4(1f, 1f, 0f, 0f));   // y: none, every pixel cut away
                    copies[m] = copy;
                    mats[i] = copy;
                    changed = true;
                }
                if (changed) r.sharedMaterials = mats;
            }
        }

        static void Warn(ModInfo mod, string message)
        {
            if (!mod.Warnings.Contains(message)) mod.Warnings.Add(message);
            Debug.LogWarning($"Mods: {mod.Id}: {message}");
        }

        /// <summary>The old templates and their resources go (the mods changed).</summary>
        static void Release()
        {
            foreach (var t in templates.Values) if (t != null) Object.Destroy(t);
            templates.Clear();
            partTemplates.Clear(); kitShips.Clear(); variants.Clear(); variantOrder.Clear();   // under the holder: destroyed below
            skinned.Clear(); skinnedOrder.Clear();
            icons.Clear();
            names.Clear();
            foreach (var i in imports) i.Dispose();
            imports.Clear();
            if (holder != null) foreach (Transform child in holder.transform) Object.Destroy(child.gameObject);
            loadedRevision = -1;
        }

        /// <summary>The ship number in a "ship_NNN_mod" assembly name, -1 = not one.</summary>
        public static int ShipOf(string assembly) =>
            assembly != null && assembly.StartsWith("ship_") && assembly.Length > 8 && int.TryParse(assembly.Substring(5, 3), out int n) ? n : -1;

        /// <summary>The template for a mod ship's assembly; while the model isn't built the game's own model of that ship (a
        /// replaced original's), else the Phantom's. Always the game's prefabs: asking ShipAssembly for the Phantom when a mod
        /// replaces the Phantom's model handed back this very assembly, and the two called each other until the stack
        /// overflowed (the Mod Builder's Exit with the Phantom's model replaced crashed the Editor).</summary>
        public static GameObject Template(string assembly)
        {
            int hash = assembly?.IndexOf(ModShipKits.VariantSeparator) ?? -1;
            if (hash > 0 && ModShipKits.ParseShipCopy(assembly, out _, out _, out _))
                return SkinnedTemplate(new AssemblyData { name = assembly, pack = Pack, category = "ships" });
            if (hash > 0)
            {
                // A player's build of a kit ship: assembled the first time, then kept (the most recent VariantCap).
                if (variants.TryGetValue(assembly, out var v) && v != null) return v;
                int s = ShipOf(assembly);
                if (loadedRevision == ModManager.Revision && holder != null && kitShips.TryGetValue(s, out var info)
                    && EnsureBuild(s, ModShipKits.Normalize(info.kit, info.ship.kitType, ModShipKits.Build.Parse(assembly.Substring(hash + 1)),
                                                            ModShipKits.DefaultBuild(info.ship, info.kit))))
                {
                    var build = ModShipKits.Normalize(info.kit, info.ship.kitType, ModShipKits.Build.Parse(assembly.Substring(hash + 1)),
                                                      ModShipKits.DefaultBuild(info.ship, info.kit));
                    var made = Assemble(info.mod, info.ship, info.kit, build, assembly);
                    variants[assembly] = made;
                    variantOrder.Enqueue(assembly);
                    while (variantOrder.Count > VariantCap)
                    {
                        string old = variantOrder.Dequeue();
                        if (variants.TryGetValue(old, out var go) && go != null) Object.Destroy(go);
                        variants.Remove(old);
                    }
                    return made;
                }
                assembly = assembly.Substring(0, hash);   // its parts loading (or not built yet): the default build meanwhile
            }
            int ship = ShipOf(assembly);
            if (ship >= 0 && templates.TryGetValue(ship, out var t) && t != null) return t;
            if (!Ready) Debug.LogWarning($"Mods: ship {ship}'s model is still loading: the game's model stands in");
            var db = Database.Shared;
            var stand = ship >= 0 ? db.ShipAssembly(ship, original: true) : null;
            // A new mod ship's own number matches its own "ship_NNN_mod" assembly: the Phantom then (#134: a model that
            // failed to build left an empty turntable instead of the stand-in).
            if (stand == null || stand.pack == Pack) stand = db.ShipAssembly(FallbackShip, original: true);
            return stand != null && stand.pack != Pack ? Visuals.AssembledObject.LoadPrefab(stand) : null;
        }

        // Skinned copies of ships without a kit (ModShipKits.CustomizationOf): "<model>#ship=N;skin=id"; a ship's own copy of
        // a game model for its own textures (Database.ShipOwnTextures): "<model>#ship=N".
        static readonly Dictionary<string, GameObject> skinned = new Dictionary<string, GameObject>();
        static readonly Queue<string> skinnedOrder = new Queue<string>();

        static GameObject Holder()
        {
            if (holder == null)
            {
                holder = new GameObject("Mod ship models");
                holder.SetActive(false);
                Object.DontDestroyOnLoad(holder);
            }
            return holder;
        }

        /// <summary>A ship's own model with a skin of its customizations ("<model>#ship=N;skin=id", ModShipKits.VariantAssembly):
        /// a copy of the model (the game's prefab, or a mod ship's template) under the hidden holder with the skin's textures on
        /// copies of its materials (ApplySkin), made the first time and kept (the most recent VariantCap). While the skin's
        /// textures load (EnsureBuild starts it; ModelsChanged follows) the model itself stands in.
        /// Without a skin ("&lt;model&gt;#ship=N", a mod ship with textures of its own, Database.ShipAssembly) a plain copy: its name
        /// is the scope its objects take (AssembledObject.ModScope), so ModTextures gives them the ship's own textures
        /// (textures/ships/&lt;id&gt;/) while the game's ship of that model keeps its look.</summary>
        public static GameObject SkinnedTemplate(AssemblyData entry)
        {
            if (entry == null || !ModShipKits.ParseShipCopy(entry.name, out string model, out int ship, out string skinId)) return null;
            var plain = new AssemblyData { name = model, pack = entry.pack, category = entry.category, root = entry.root, origin = entry.origin };
            var source = Visuals.AssembledObject.LoadPrefab(plain);
            if (skinned.TryGetValue(entry.name, out var made) && made != null) return made;
            if (source == null) return null;
            bool ownTextures = Database.Shared.ShipOwnTextures.Contains(ship);
            // Not there (yet): the ship's own copy when it has textures of its own, else the model itself.
            GameObject Fallback() => ownTextures && skinId != null
                ? SkinnedTemplate(new AssemblyData { name = $"{model}{ModShipKits.VariantSeparator}{ModShipKits.ShipToken}{ship}", pack = entry.pack,
                                                     category = entry.category, root = entry.root, origin = entry.origin }) ?? source
                : source;
            ModShipKits.Kit kit = null;
            ModShipKits.Skin skin = null;
            if (skinId != null)
            {
                kit = ModShipKits.CustomizationOf(ship);
                skin = kit?.FindSkin(skinId);
                if (skin == null) return Fallback();
                var build = new ModShipKits.Build { skin = skin.id };
                if (!EnsureBuild(ship, build)) return Fallback();   // its textures load: the model itself meanwhile
            }
            else if (!ownTextures) return source;
            var copy = Object.Instantiate(source, Holder().transform, false);
            copy.name = entry.name;
            if (skin != null) ApplySkin(copy, kit, skin);
            skinned[entry.name] = copy;
            skinnedOrder.Enqueue(entry.name);
            while (skinnedOrder.Count > VariantCap)
            {
                string old = skinnedOrder.Dequeue();
                if (skinned.TryGetValue(old, out var go) && go != null) Object.Destroy(go);
                skinned.Remove(old);
            }
            return copy;
        }

        /// <summary>Runs the action once the models are built (at most 'timeoutSeconds' later).</summary>
        public static void WhenReady(System.Action action, float timeoutSeconds = 30f) => ModShipsWaiter.Run(action, timeoutSeconds);

        /// <summary>The built templates (ship number, name, template), for the loading screen's hangar shadows
        /// (World.ShipShadowBaker); only complete once Ready.</summary>
        public static IEnumerable<(int ship, string name, GameObject template)> Built
        {
            get
            {
                foreach (var kv in templates)
                    if (kv.Value != null) yield return (kv.Key, names.TryGetValue(kv.Key, out var n) ? n : kv.Value.name, kv.Value);
            }
        }

        /// <summary>A mod ship's shop icon, null = none.</summary>
        public static Texture2D Icon(int ship) => icons.TryGetValue(ship, out var t) ? t : null;

        /// <summary>A ship's icon replaced in place (the Mod Builder's generated one, until the project is read again).</summary>
        public static void SetIcon(int ship, Texture2D icon) => icons[ship] = icon;
    }
}
