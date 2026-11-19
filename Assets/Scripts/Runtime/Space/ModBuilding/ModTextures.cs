// ModTextures.cs
// Remake mods: texture replacements (skins). A mod that is on ships files in its textures folder (textures/<name>.png | .jpg,
// or .tga / .bmp / .dds / .aei: ImageImport)
// named like one of the game's textures (the file names in Assets/Textures/<pack>/..., e.g. ship_028_terran_diffuse for the
// Veteran's hull, ship_028_terran_normal_specular for its normal map): each replaces that texture on every assembled object
// that uses it (ships in flight, the NPCs, the hangar, the item window; AssembledObject.Awake -> Apply) and on the backdrop's
// planets, suns and rings (planet_000_small...; Backdrop.Load -> Replace), the object getting a
// copy of the material with the mod's texture (the game's material assets are never changed). The image must have the
// original's layout (the same UV mapping); its size may differ. A later mod in the load order wins. Names with "_normal" or
// "_metallic" load as linear data, the rest as colour. Loaded in the background when the mods change (Preload; the main
// menu's loading screen waits, ModLoading). When the mods change in a scene (the Mod Builder's skins), the objects already
// there go back to the game's materials and get the new textures once they have loaded (Revert / ReapplyAll): their old
// copies used to be destroyed under them, which drew them magenta.
// Per object (the Mod Builder's "this ship only"): textures/<assembly>/<name>.png (e.g. textures/ship_028_terran/glow_terran.png)
// replaces the texture only on that assembled object (AssembledObject.ModScope, its assembly's name), before any textures/<name>
// of every object; such an object gets material copies of its own.
// Per ship (a mod's new ship on one of the game's models, ships.json "model": "gof2:<model>"): textures/ships/<ship id>/<name>.png
// replaces the texture on that ship alone. The ship then shows a copy of the model of its own ("<model>#ship=N",
// Database.ShipAssembly / ModShips.SkinnedTemplate), whose scope leads here to the ship's key ("<mod id>:<ship id>"); the game's
// ship of that model and every other ship on it keep their look. Its model's textures/<model>/ files and the every-object ones
// still apply to what the ship's own folder doesn't replace.
// The combat HUD's images (Resources/GoF2Hud, the ship / crate markers...) are replaced by name too (Hud, read by CombatView).

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace GoF2Remake.Modding
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class ModTextures
    {
        public const string Folder = ModContent.TexturesFolder;
        static readonly string[] Extensions = ImageImport.FileExtensions;   // PNG, JPG, TGA, BMP, DDS, the game's .aei
        // The texture slots of the game's materials (URP Lit copies and the GoF2 Shader Graphs).
        static readonly string[] Slots = { "_BaseMap", "_MainTex", "_BumpMap", "_MetallicGlossMap", "_EmissionMap" };

        // The replacements by key: "<texture name>" for every object, "<scope>/<texture name>" for one assembly's objects,
        // "<mod id>:<ship id>/<texture name>" for one mod ship's (textures/ships/<ship id>/).
        static readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
        static readonly HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // The scopes (assembly names, mod ship keys) with replacements of their own.
        static readonly HashSet<string> scopes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // (scope, game material) -> its copy with the mods' textures (null: none of its textures is replaced); scope "" for
        // objects without replacements of their own.
        static readonly Dictionary<(string, Material), Material> copies = new Dictionary<(string, Material), Material>();
        static readonly HashSet<Material> made = new HashSet<Material>();
        static int revision = -1, loading;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { textures.Clear(); names.Clear(); scopes.Clear(); copies.Clear(); made.Clear(); revision = -1; loading = 0; }

        public static int Count => names.Count;
        public static int Loading => loading;

        /// <summary>Every texture of the mods that are on is loaded (or failed).</summary>
        public static bool Ready { get { Preload(); return loading == 0; } }

        /// <summary>Loads the mods' textures again when the mods changed (no-op otherwise).</summary>
        public static void Preload()
        {
            if (revision == ModManager.Revision) return;
            revision = ModManager.Revision;
            // Loads of the earlier revision no longer count (their finally leaves 'loading' alone): it used to stay above 0
            // for good after the mods changed while textures were loading, so ModLoading stayed Busy and the Mod Builder's
            // preview waited forever (its camera refused new drags meanwhile).
            loading = 0;
            textures.Clear();
            names.Clear();
            scopes.Clear();
            Revert();
            foreach (var c in copies.Values) if (c != null) UnityEngine.Object.Destroy(c);
            copies.Clear();
            made.Clear();
            var files = new Dictionary<string, (ModInfo mod, string file)>(StringComparer.OrdinalIgnoreCase);
            foreach (var mod in ModManager.Active)
            {
                foreach (string f in mod.Source.FilesIn(Folder, Extensions))
                    files[Path.GetFileNameWithoutExtension(f)] = (mod, f);
                // textures/<scope>/<name>: one level of folders
                string prefix = Folder + "/";
                foreach (string f in mod.Source.Files)
                {
                    if (!f.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                    var parts = f.Substring(prefix.Length).Split('/');
                    if (!Array.Exists(Extensions, e => f.EndsWith(e, StringComparison.OrdinalIgnoreCase))) continue;
                    // textures/ships/<ship id>/<name>: one of this mod's ships only, keyed by the ship's key
                    if (parts.Length == 3 && parts[1].Length > 0
                        && string.Equals(Folder + "/" + parts[0], ModContent.ShipTexturesFolder, StringComparison.OrdinalIgnoreCase))
                    {
                        files[mod.Id + ":" + parts[1] + "/" + Path.GetFileNameWithoutExtension(parts[2])] = (mod, f);
                        continue;
                    }
                    if (parts.Length != 2 || parts[0].Length == 0) continue;
                    files[parts[0] + "/" + Path.GetFileNameWithoutExtension(parts[1])] = (mod, f);
                }
            }
            int started = revision;
            foreach (var kv in files)
            {
                string key = kv.Key;
                names.Add(key);
                int slash = key.IndexOf('/');
                if (slash > 0) scopes.Add(key.Substring(0, slash));
                Load(kv.Value.mod, kv.Value.file, key, started);
            }
            if (files.Count > 0) Debug.Log($"Mods: {files.Count} texture replacement(s) from the mods");
        }

        /// <summary>Every renderer still drawing one of the copies gets the game's material back (before the copies go).</summary>
        static void Revert()
        {
            if (copies.Count == 0 || !Application.isPlaying) return;
            var original = new Dictionary<Material, Material>();
            foreach (var kv in copies) if (kv.Value != null) original[kv.Value] = kv.Key.Item2;
            if (original.Count == 0) return;
            foreach (var r in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include))
            {
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                    if (mats[i] != null && original.TryGetValue(mats[i], out var o)) { mats[i] = o; changed = true; }
                if (changed) r.sharedMaterials = mats;
            }
        }

        /// <summary>The objects already in the scene take the textures that have just loaded.</summary>
        static void ReapplyAll()
        {
            if (textures.Count == 0 || !Application.isPlaying) return;
            foreach (var a in UnityEngine.Object.FindObjectsByType<Visuals.AssembledObject>(FindObjectsInactive.Include))
                Apply(a.gameObject, a.ModScope);
        }

        static async void Load(ModInfo mod, string file, string key, int started)
        {
            loading++;
            string name = key.Substring(key.LastIndexOf('/') + 1);
            bool normal = name.IndexOf("_normal", StringComparison.OrdinalIgnoreCase) >= 0;   // the game's normal maps (Android layout)
            bool linear = normal || name.IndexOf("_metallic", StringComparison.OrdinalIgnoreCase) >= 0;
            try
            {
                await ModMaterials.PreloadTexture(mod, file, linear, normal: normal);
                if (started != revision) return;
                var tex = ModMaterials.Texture(mod, file, linear, normal: normal);
                if (tex != null) { tex.name = name; textures[key] = tex; }
                else Debug.LogWarning($"Mods: {mod.Id}: could not load {file}");
            }
            catch (Exception e) { Debug.LogWarning($"Mods: {mod.Id}: {file}: {e.Message}"); }
            finally
            {
                if (started == revision && --loading == 0) ReapplyAll();
            }
        }

        /// <summary>Puts the mods' textures on an assembled object's renderers (its own material copies), the ones for its
        /// scope (its assembly's name, textures/&lt;scope&gt;/) first. Cheap without any.</summary>
        public static void Apply(GameObject root, string scope = null)
        {
            if (root == null || !Application.isPlaying) return;
            if (revision != ModManager.Revision) Preload();
            if (textures.Count == 0) return;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var copy = CopyFor(mats[i], scope);
                    if (copy != null) { mats[i] = copy; changed = true; }
                }
                if (changed) r.sharedMaterials = mats;
            }
        }

        /// <summary>The game's material a copy was made from (null when 'm' isn't one of the copies).</summary>
        public static Material OriginalOf(Material m)
        {
            if (m == null || !made.Contains(m)) return null;
            foreach (var kv in copies) if (kv.Value == m) return kv.Key.Item2;
            return null;
        }

        /// <summary>Remake mods: a material that isn't on an assembled object (the backdrop's planets, suns and rings,
        /// Backdrop.Load; a wreck's burnt material, with its object's scope): its copy with the mods' textures, or the
        /// material itself when none of them is replaced.</summary>
        public static Material Replace(Material m, string scope = null)
        {
            if (m == null || !Application.isPlaying) return m;
            if (revision != ModManager.Revision) Preload();
            if (textures.Count == 0) return m;
            return CopyFor(m, scope) ?? m;
        }

        /// <summary>Remake mods: a flight HUD image (Resources/GoF2Hud/&lt;name&gt;, e.g. ship_dot_enemy, ship_ring_open_friend)
        /// replaced by a mod's textures/&lt;name&gt; file, else null. Read by the combat HUD (CombatView: the ship and crate
        /// markers, the lock ring, the status bars, the hit arcs, the lock plate's race icons) when it first draws an image.</summary>
        public static Texture2D Hud(string name)
        {
            if (!Application.isPlaying || string.IsNullOrEmpty(name)) return null;
            if (revision != ModManager.Revision) Preload();
            return textures.TryGetValue(name, out var t) ? t : null;
        }

        /// <summary>Remake (2026-11-14): one of the layered skies' images (Resources/GoF2SkyLayers/&lt;name&gt;, a sky's "gof2:&lt;name&gt;":
        /// star_mask, nebula_masks, patch_0XX) replaced by a mod's textures/&lt;name&gt; file, else null (World.GoF2SkyLayers,
        /// OrbitBuilder.SetChorusSky). The EVERSPACE 2 importer's mod gives ES2's own star and nebula masks this way.</summary>
        public static Texture2D SkyLayer(string name) => Hud(name);

        /// <summary>The replacement for a texture on an object of 'scope': its ship's own (a game model's copy for one mod ship),
        /// its own, its model's (a copy's), else every object's.</summary>
        static Texture2D Find(string texture, string scope)
        {
            if (scope != null)
            {
                string ship = ShipScope(scope);
                if (ship != null && textures.TryGetValue(ship + "/" + texture, out var mine)) return mine;
                if (textures.TryGetValue(scope + "/" + texture, out var own)) return own;
                string model = ModelScope(scope);
                if (model != scope && textures.TryGetValue(model + "/" + texture, out var its)) return its;
            }
            return textures.TryGetValue(texture, out var all) ? all : null;
        }

        /// <summary>The key of the mod ship a game model's copy is for ("&lt;model&gt;#ship=N[;skin=id]" -> "&lt;mod id&gt;:&lt;ship id&gt;"),
        /// when that ship has textures of its own; else null.</summary>
        static string ShipScope(string scope)
        {
            if (scope.IndexOf(ModShipKits.VariantSeparator) <= 0 || !ModShipKits.ParseShipCopy(scope, out _, out int ship, out _)) return null;
            string key = ModContent.ShipKey(ship);
            return key != null && scopes.Contains(key) ? key : null;
        }

        /// <summary>A copy's model ("&lt;model&gt;#..." -> "&lt;model&gt;"), else the scope itself.</summary>
        static string ModelScope(string scope)
        {
            int hash = scope.IndexOf(ModShipKits.VariantSeparator);
            return hash > 0 ? scope.Substring(0, hash) : scope;
        }

        static Material CopyFor(Material m, string scope)
        {
            if (m == null || made.Contains(m)) return null;   // already a copy (Apply again on the same object)
            // Objects without their own (nor their ship's or model's) share copies.
            string key = scope != null && (scopes.Contains(scope) || ShipScope(scope) != null || scopes.Contains(ModelScope(scope))) ? scope : "";
            if (copies.TryGetValue((key, m), out var copy)) return copy;
            foreach (var slot in Slots)
            {
                if (!m.HasProperty(slot)) continue;
                var t = m.GetTexture(slot);
                var mod = t != null ? Find(t.name, key.Length > 0 ? key : null) : null;
                if (mod == null) continue;
                if (copy == null) { copy = new Material(m) { name = m.name + (key.Length > 0 ? $" (mod {key})" : " (mod)") }; }
                copy.SetTexture(slot, mod);
            }
            if (copy != null) made.Add(copy);
            if (copy != null || loading == 0) copies[(key, m)] = copy;   // "none" only once everything has loaded
            return copy;
        }
    }
}
