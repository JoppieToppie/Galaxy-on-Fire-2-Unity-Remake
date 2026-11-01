// ModWeapons.cs
// Remake mods: a weapon's own look and sound (items.json "fx" on a new item or an override). The projectile, the muzzle
// flash and the impact are each the base item's (the item's Look), a mod sprite (a PNG on a camera-facing quad, or
// stretched along the flight), a mod model (glTF / GLB, with the fx shader when it names a texture, else its own
// materials), the base item's recoloured (only "color" / "glow": its textures made grey and tinted, FxRecolor), or
// false (none). "shot" / "explosionSound" are the mod's audio files (one at random per shot), "shotLoops"
// whether the shot sound loops while the trigger is held. What the weapon does stays the base item's (a beam stays a
// beam, a mine a mine): only what it looks and sounds like changes. A part may also be another item's ("item": its base's
// part), recoloured or not, and any part without a sprite / model can be resized ("scale"); "shot" / "explosionSound" may
// name an item whose sounds it takes. Turrets: "turret" is the turret's own model (a glTF / GLB with a "pivot" node holding
// the base and a node named *_gun, the barrel along +Z; without them it turns as one piece), with "muzzle" / "shotStart"
// along the gun node (game units, on the model as made), "scale" and "offset" (the model moved off its mount, game units);
// "item" instead of "model" is another item's turret model (a game turret's: no 3D file needed). Any "model" (a part's, the
// turret's) may be "gof2:<model name>", one of the game's own models (Modding.GameModels).
// Weapon effects (weaponfx.json, EffectsFile): a look and sound of their own, based on a game weapon ("base": what they leave
// out is that weapon's), which any item takes with "fx": "<id>" (or "<mod id>:<id>" for another mod's); the item's turret
// model and blast type stay its own; "fx": <item number> takes a game weapon's whole look and sound the same way. The
// builder project's effects are also built on their own, on their base weapon, for
// the Mod Builder's firing range (Effect).
// Each item gets a WeaponFx of its own (a copy of the base item's, the parts and clips replaced), which WeaponFx.Load hands
// to every gun (the player's, NPCs', turrets, sentry guns, other players' mirrored shots). The parts are built under a
// hidden DontDestroyOnLoad holder (GunRig instantiates them); ModFxPart gives the sprites and models their lifetime,
// growth, fade and camera facing. Loaded in the background with the ships when the mods change (Preload; ModLoading).

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GoF2Remake.Flight;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace GoF2Remake.Modding
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class ModWeapons
    {
        const float M = 0.05f;
        /// <summary>The mods' weapon effects: a list of { "id", "name", "base", and the fx fields }.</summary>
        public const string EffectsFile = ModContent.WeaponEffectsFile;
        /// <summary>The file the entries being parsed come from (for the messages): items.json, weaponfx.json during an effect.</summary>
        static string File = ModContent.ItemsFile;

        /// <summary>One part of an "fx": a sprite or a model, the base item's recoloured, or removed.</summary>
        public class Part
        {
            /// <summary>copy: no sprite / model, the base item's part (or 'fromItem''s); recolor: with "color" / "glow".</summary>
            public bool remove, copy, recolor;
            /// <summary>Another item's part instead of the base item's (-1 = the base item's), and a size factor.</summary>
            public int fromItem = -1;
            public float scale = 1f;
            public string sprite, model, texture;
            public float size = 200f, glow = -1f, lifetimeMs, grow = 1f, spin;
            public Color color = Color.white;
            public bool additive = true, fade = true, alongFlight;
            public float stretch = 1f;
            public Vector3 rotate;
        }

        public class Def
        {
            public ModInfo mod;
            public int item;
            public string label, where;
            public Part projectile, muzzle, impact;
            public List<string> shots, explosionSounds;
            /// <summary>"shot" / "explosionSound" given as an item number: that item's sounds (-1 = none).</summary>
            public int shotItem = -1, explosionItem = -1;
            public bool? shotLoops;
            /// <summary>"trail": the shots' smoke trail (false: none), null = the base item's.</summary>
            public TrailLook trail;
            /// <summary>"turret": the turret's own model, its muzzle and where its shots start (game units, -1 = the base's).</summary>
            public string turretModel;
            public float turretMuzzle = -1f, turretShot = -1f;
            /// <summary>"turret" "scale": the model's size factor (1 = as made, 1 unit = 1 m).</summary>
            public float turretScale = 1f;
            /// <summary>"turret" "offset": the model moved off its mount, game units in the turret's frame (x toward the
            /// pilot's left, y along the turret's up, z forward).</summary>
            public Vector3 turretOffset;
            /// <summary>"turret" "item": another item's turret model (a game turret's, its muzzle and shot start too), -1 = none.</summary>
            public int turretItem = -1;
            /// <summary>The turret model is resized or moved (its own or the base item's).</summary>
            public bool TurretReshaped => !Mathf.Approximately(turretScale, 1f) || turretOffset != Vector3.zero;
            /// <summary>A weapon effect's "base": that item's parts and sounds where the effect leaves them out (-1 = the
            /// item's own base's).</summary>
            public int look = -1;
            /// <summary>An effect built on its own (its key, "mod id:id"; the item is its base): Effect, not Fx.</summary>
            public string effect;
        }

        /// <summary>A weapon effect of weaponfx.json.</summary>
        public sealed class EffectDef
        {
            public ModInfo mod;
            public JObject json;
            public string id;
            /// <summary>"base", the item its look starts from (-1: none given).</summary>
            public int look = -1;
        }

        static readonly Dictionary<int, WeaponFx> made = new Dictionary<int, WeaponFx>();
        static readonly Dictionary<string, WeaponFx> effectsMade = new Dictionary<string, WeaponFx>(System.StringComparer.OrdinalIgnoreCase);
        static readonly List<Object> owned = new List<Object>();
        static readonly List<GLTFast.GltfImport> imports = new List<GLTFast.GltfImport>();
        static GameObject holder;
        static GLTFast.TimeBudgetPerFrameDeferAgent agent;
        static Mesh quad;
        static int loadingRevision = -1, loadedRevision = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            made.Clear(); effectsMade.Clear(); owned.Clear(); imports.Clear(); greyed.Clear(); recolorBlit = null;
            File = ModContent.ItemsFile;
            holder = null; agent = null; quad = null; loadingRevision = loadedRevision = -1; Count = Done = 0;
        }

        public static int Count { get; private set; }
        public static int Done { get; private set; }

        /// <summary>Every mod weapon's fx is built (or there are none).</summary>
        public static bool Ready
        {
            get
            {
                if (loadedRevision == ModManager.Revision) return true;
                Preload();
                return false;
            }
        }

        /// <summary>A mod item's own fx (null: none, or not built yet: the base item's is used meanwhile).</summary>
        public static WeaponFx Fx(int item)
        {
            if (loadedRevision != ModManager.Revision) { Preload(); return null; }
            return made.TryGetValue(item, out var fx) ? fx : null;
        }

        /// <summary>The builder project's weapon effect 'key' ("mod id:id") built on its base weapon (null: none, or not built
        /// yet): the Mod Builder's range fires it.</summary>
        public static WeaponFx Effect(string key)
        {
            if (loadedRevision != ModManager.Revision) { Preload(); return null; }
            return key != null && effectsMade.TryGetValue(key, out var fx) ? fx : null;
        }

        public static void Preload()
        {
            int rev = ModManager.Revision;
            if (loadingRevision == rev) return;
            loadingRevision = rev;
            _ = LoadAll(rev);
        }

        // ---- the "fx" entries ---------------------------------------------------------------------------------------

        /// <summary>The "fx" of every item of the mods that are on (a later mod's fx for the same item wins), an effect's
        /// ("fx": "<id>") read from weaponfx.json; then the builder project's effects on their own (Effect).</summary>
        public static List<Def> Defs()
        {
            var effects = Effects();
            var byItem = new Dictionary<int, Def>();
            foreach (var mod in ModManager.Active)
                foreach (var d in ModContent.Parse(mod).items)
                {
                    if (!(ModJson.Get(d.json, "fx") is JToken fx)) continue;
                    int index = d.key != null ? ModContent.ItemIndexOf(d.key) : ModContent.TryResolveItem(d.overrideRef, out int t) ? t : -1;
                    if (index < 0) continue;
                    string label = d.localId ?? d.overrideRef;
                    try
                    {
                        if (fx.Type == JTokenType.Integer)
                        {
                            // A game weapon's look and sound, whole (as an effect with only a "base").
                            if (!ModContent.TryResolveItem(fx.ToString(), out int look))
                                throw new ModJsonException($"{ModJson.Where(fx, File)}: \"fx\": there is no item {fx}");
                            byItem[index] = new Def { mod = mod, item = index, label = label, where = ModJson.Where(fx, File), look = look };
                            continue;
                        }
                        if (fx.Type != JTokenType.String) { byItem[index] = Parse(mod, fx, index, label); continue; }
                        string name = (string)fx, key = name.Contains(':') ? name : mod.Id + ":" + name;
                        if (!effects.TryGetValue(key, out var e))
                            throw new ModJsonException($"{ModJson.Where(fx, File)}: \"fx\": there is no weapon effect \"{name}\" (in the {EffectsFile} of {(name.Contains(':') ? "a mod that is on" : "this mod")})");
                        byItem[index] = ParseEffect(e, index, label);
                    }
                    catch (ModJsonException e) { Warn(mod, e.Message); }
                }
            var defs = new List<Def>(byItem.Values);
            var builder = ModManager.BuilderProject;
            foreach (var e in effects.Values)
            {
                if (builder == null || e.mod != builder || e.look < 0) continue;
                try
                {
                    var d = ParseEffect(e, e.look, e.id);
                    d.effect = e.mod.Id + ":" + e.id;
                    defs.Add(d);
                }
                catch (ModJsonException x) { Warn(e.mod, x.Message); }
            }
            return defs;
        }

        /// <summary>Every active mod's weapon effects (weaponfx.json) by key, "mod id:id".</summary>
        public static Dictionary<string, EffectDef> Effects()
        {
            var all = new Dictionary<string, EffectDef>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var mod in ModManager.Active)
            {
                JToken file;
                try { file = ModJson.Read(mod.Source, EffectsFile); }
                catch (ModJsonException e) { Warn(mod, e.Message); continue; }
                if (file == null) continue;
                if (!(file is JArray list)) { Warn(mod, $"{EffectsFile}: a list [ {{ \"id\": ..., \"base\": ... }} ] of weapon effects"); continue; }
                foreach (var o in list.OfType<JObject>())
                {
                    string id = ModJson.Get(o, "id")?.ToString();
                    if (string.IsNullOrEmpty(id)) { Warn(mod, $"{ModJson.Where(o, EffectsFile)}: a weapon effect needs an \"id\""); continue; }
                    var e = new EffectDef { mod = mod, json = o, id = id };
                    if (ModJson.Get(o, "base") is JToken b && !ModContent.TryResolveItem(b.ToString(), out e.look))
                    {
                        Warn(mod, $"{ModJson.Where(b, EffectsFile)}: \"base\": there is no item {b}");
                        e.look = -1;
                    }
                    all[mod.Id + ":" + id] = e;
                }
            }
            return all;
        }

        static Def ParseEffect(EffectDef e, int item, string label)
        {
            string was = File;
            File = EffectsFile;
            try
            {
                var d = Parse(e.mod, e.json, item, label, true);
                d.look = e.look;
                return d;
            }
            finally { File = was; }
        }

        static readonly HashSet<string> FxFields = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
            { "projectile", "muzzle", "impact", "shot", "shotLoops", "explosionSound", "turret", "trail" };
        static readonly HashSet<string> EffectFields = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase) { "id", "name", "base" };
        static readonly HashSet<string> PartFields = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
            { "sprite", "model", "texture", "size", "color", "glow", "additive", "lifetime", "grow", "fade", "spin", "alongFlight", "stretch", "rotate",
              "item", "scale" };
        static readonly HashSet<string> CopyFields = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase) { "item", "scale", "color", "glow" };
        static readonly HashSet<string> TurretFields = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase) { "model", "item", "muzzle", "shotStart", "scale", "offset" };

        static Def Parse(ModInfo mod, JToken token, int item, string label, bool effect = false)
        {
            string where = ModJson.Where(token, File);
            if (!(token is JObject o)) throw new ModJsonException($"{where}: \"fx\" must be an object {{ ... }}, a weapon effect's id or a game item's number");
            foreach (var p in o.Properties())
                if (!FxFields.Contains(p.Name) && !(effect && EffectFields.Contains(p.Name))) mod.Warnings.Add($"{ModJson.Where(p, File)}: unknown fx field \"{p.Name}\" (ignored)");
            var d = new Def
            {
                mod = mod, item = item, label = label, where = where,
                projectile = ParsePart(mod, ModJson.Get(o, "projectile"), "projectile"),
                muzzle = ParsePart(mod, ModJson.Get(o, "muzzle"), "muzzle"),
                impact = ParsePart(mod, ModJson.Get(o, "impact"), "impact"),
                shots = Files(o, "shot", out int shotItem),
                explosionSounds = Files(o, "explosionSound", out int explosionItem),
                shotItem = shotItem, explosionItem = explosionItem,
            };
            if (ModJson.Has(o, "shotLoops")) d.shotLoops = ModJson.Bool(o, "shotLoops", false, File);
            d.trail = ParseTrail(mod, ModJson.Get(o, "trail"));
            foreach (var f in Concat(d.shots, d.explosionSounds))
                if (!mod.Source.Exists(f)) throw new ModJsonException($"{where}: the sound {f} isn't in the mod");
            ParseTurret(mod, d, ModJson.Get(o, "turret"));
            return d;
        }

        /// <summary>"trail": false (none) or { "color": [r, g, b] (0..1 / 0..255 / "#rrggbb"), "width": game units (half the
        /// ribbon's width; a rocket's 100), "length": ms it lasts (a rocket's 3000) }.</summary>
        static TrailLook ParseTrail(ModInfo mod, JToken t)
        {
            if (t == null) return null;
            if (t.Type == JTokenType.Boolean) return (bool)t ? new TrailLook() : new TrailLook { none = true };
            if (!(t is JObject o)) throw new ModJsonException($"{ModJson.Where(t, File)}: \"trail\" is false or {{ \"color\", \"width\", \"length\" }}");
            var look = new TrailLook
            {
                width = ModJson.Float(o, "width", -1f, File),
                lifeMs = ModJson.Float(o, "length", -1f, File),
            };
            if (ModJson.Get(o, "color") is JToken c) look.color = TrailColour(c);
            return look;
        }

        static Color TrailColour(JToken c)
        {
            if (c.Type == JTokenType.String && ColorUtility.TryParseHtmlString((string)c, out var html)) return html;
            if (c is JArray a && a.Count >= 3)
            {
                float r = (float)a[0], g = (float)a[1], b = (float)a[2], al = a.Count > 3 ? (float)a[3] : 1f;
                if (r > 1f || g > 1f || b > 1f || al > 1f) { r /= 255f; g /= 255f; b /= 255f; if (al > 1f) al /= 255f; }
                return new Color(r, g, b, al);
            }
            return Color.white;
        }

        /// <summary>"turret": "models/x.glb" or { "model": ..., "muzzle": units, "shotStart": units, "scale": f, "offset": [x, y, z] };
        /// "item": n instead of "model" takes that item's turret model (no 3D file needed); without either the base item's,
        /// resized and / or moved.</summary>
        static void ParseTurret(ModInfo mod, Def d, JToken t)
        {
            if (t == null) return;
            string where = ModJson.Where(t, File);
            if (t.Type == JTokenType.String) d.turretModel = (string)t;
            else if (t is JObject o)
            {
                foreach (var p in o.Properties())
                    if (!TurretFields.Contains(p.Name)) mod.Warnings.Add($"{ModJson.Where(p, File)}: unknown field \"{p.Name}\" in \"turret\" (ignored)");
                d.turretModel = ModJson.Str(o, "model");
                d.turretMuzzle = ModJson.Float(o, "muzzle", -1f, File);
                d.turretShot = ModJson.Float(o, "shotStart", -1f, File);
                d.turretScale = ModJson.Float(o, "scale", 1f, File);
                if (d.turretScale <= 0f) throw new ModJsonException($"{where}: \"turret\": \"scale\" must be above 0");
                if (ModJson.Get(o, "offset") is JToken off)
                {
                    if (!(off is JArray a) || a.Count != 3 || a.Any(v => v.Type != JTokenType.Integer && v.Type != JTokenType.Float))
                        throw new ModJsonException($"{ModJson.Where(off, File)}: \"turret\": \"offset\" is [x, y, z] (game units)");
                    d.turretOffset = new Vector3((float)a[0], (float)a[1], (float)a[2]);
                }
                if (ModJson.Get(o, "item") is JToken it)
                {
                    if (it.Type != JTokenType.Integer && it.Type != JTokenType.String || !ModContent.TryResolveItem(it.ToString(), out d.turretItem))
                        throw new ModJsonException($"{ModJson.Where(it, File)}: \"turret\": \"item\" is an item's number (there is no item {it})");
                    if (d.turretModel != null)
                        throw new ModJsonException($"{where}: \"turret\" has both \"model\" and \"item\"; give one of them");
                }
                if (d.turretModel == null && d.turretItem < 0 && !d.TurretReshaped && d.turretMuzzle < 0f && d.turretShot < 0f)
                    throw new ModJsonException($"{where}: \"turret\" needs \"model\" (a glTF / GLB) or \"item\" (a game turret's model), or \"scale\" / \"offset\" for the base item's");
                if (d.turretModel == null) return;   // a game turret's model or the base item's, resized / moved
            }
            else throw new ModJsonException($"{where}: \"turret\" is a model file or {{ \"model\": ..., \"muzzle\": ..., \"shotStart\": ..., \"scale\": ..., \"offset\": [x, y, z] }}");
            if (!GameModels.Is(d.turretModel) && !mod.Source.Exists(d.turretModel)) throw new ModJsonException($"{where}: the turret model {d.turretModel} isn't in the mod");
        }

        static IEnumerable<string> Concat(List<string> a, List<string> b)
        {
            if (a != null) foreach (var s in a) yield return s;
            if (b != null) foreach (var s in b) yield return s;
        }

        /// <summary>A sound field: a file, a list of files, or an item's number (its sounds: 'item', the files null).</summary>
        static List<string> Files(JObject o, string key, out int item)
        {
            item = -1;
            var t = ModJson.Get(o, key);
            if (t == null) return null;
            if (t.Type == JTokenType.String) return new List<string> { (string)t };
            if (t.Type == JTokenType.Integer)
            {
                if (!ModContent.TryResolveItem(t.ToString(), out item))
                    throw new ModJsonException($"{ModJson.Where(t, File)}: \"{key}\": there is no item {t}");
                return null;
            }
            var list = ModJson.Strings(o, key);
            if (list == null || list.Count == 0) throw new ModJsonException($"{ModJson.Where(t, File)}: \"{key}\" must be a file, a list of files or an item's number");
            return list;
        }

        static Part ParsePart(ModInfo mod, JToken t, string name)
        {
            if (t == null) return null;
            string where = ModJson.Where(t, File);
            if (t.Type == JTokenType.Boolean)
            {
                if ((bool)t) throw new ModJsonException($"{where}: \"{name}\": true means nothing; give a sprite / model, or false for none");
                return new Part { remove = true };
            }
            if (!(t is JObject o)) throw new ModJsonException($"{where}: \"{name}\" must be an object {{ \"sprite\": ... }} or false");
            foreach (var p in o.Properties())
                if (!PartFields.Contains(p.Name)) mod.Warnings.Add($"{ModJson.Where(p, File)}: unknown field \"{p.Name}\" in \"{name}\" (ignored)");
            var part = new Part
            {
                sprite = ModJson.Str(o, "sprite"), model = ModJson.Str(o, "model"), texture = ModJson.Str(o, "texture"),
                size = ModJson.Float(o, "size", 200f, File),
                glow = ModJson.Float(o, "glow", -1f, File),
                lifetimeMs = ModJson.Float(o, "lifetime", name == "projectile" ? 0f : name == "muzzle" ? 100f : 300f, File),
                grow = ModJson.Float(o, "grow", 1f, File),
                spin = ModJson.Float(o, "spin", 0f, File),
                additive = ModJson.Bool(o, "additive", true, File),
                fade = ModJson.Bool(o, "fade", name != "projectile", File),
                alongFlight = ModJson.Bool(o, "alongFlight", false, File),
                stretch = ModJson.Float(o, "stretch", 1f, File),
            };
            if (part.sprite != null && part.model != null)
                throw new ModJsonException($"{where}: \"{name}\" has both \"sprite\" and \"model\"; give one of them");
            if (part.sprite == null && part.model == null)
            {
                // Neither: the base item's own part (or another item's, "item"), recoloured ("color" / "glow") and / or
                // resized ("scale"). Only those four mean something then.
                part.recolor = ModJson.Has(o, "color") || ModJson.Has(o, "glow");
                if (!part.recolor && !ModJson.Has(o, "item") && !ModJson.Has(o, "scale"))
                    throw new ModJsonException($"{where}: \"{name}\" needs \"sprite\" (a PNG) or \"model\" (a glTF / GLB), or \"color\" / \"item\" / \"scale\" to change the original's");
                part.copy = true;
                if (ModJson.Get(o, "item") is JToken it && (it.Type != JTokenType.Integer || !ModContent.TryResolveItem(it.ToString(), out part.fromItem)))
                    throw new ModJsonException($"{ModJson.Where(it, File)}: \"{name}\": \"item\" is an item's number (there is no item {it})");
                part.scale = ModJson.Float(o, "scale", 1f, File);
                if (part.scale <= 0f) throw new ModJsonException($"{where}: \"{name}\": \"scale\" must be above 0");
                foreach (var p in o.Properties())
                    if (PartFields.Contains(p.Name) && !CopyFields.Contains(p.Name))
                        mod.Warnings.Add($"{ModJson.Where(p, File)}: \"{p.Name}\" in \"{name}\" only applies with a sprite or model; the original's takes \"item\", \"scale\", \"color\" and \"glow\" (ignored)");
            }
            else
                foreach (var p in o.Properties())
                    if (string.Equals(p.Name, "item", System.StringComparison.OrdinalIgnoreCase) || string.Equals(p.Name, "scale", System.StringComparison.OrdinalIgnoreCase))
                        mod.Warnings.Add($"{ModJson.Where(p, File)}: \"{p.Name}\" in \"{name}\" only applies without a sprite or model (\"size\" sizes those; ignored)");
            if (part.size <= 0f) throw new ModJsonException($"{where}: \"{name}\": \"size\" must be above 0");
            if (ModJson.Get(o, "color") is JToken c) part.color = ParseColor(c);
            if (ModJson.Get(o, "rotate") is JArray r && r.Count == 3) part.rotate = new Vector3((float)r[0], (float)r[1], (float)r[2]);
            foreach (var f in new[] { part.sprite, part.model, part.texture })
                if (f != null && !GameModels.Is(f) && !mod.Source.Exists(f)) throw new ModJsonException($"{where}: \"{name}\": {f} isn't in the mod");
            return part;
        }

        /// <summary>[r, g, b] / [r, g, b, a] from 0 to 1, or from 0 to 255 when any value is above 1 (what colour pickers
        /// give: [134, 59, 135]), or "#rrggbb" / "#rrggbbaa".</summary>
        static Color ParseColor(JToken t)
        {
            if (t.Type == JTokenType.String && ColorUtility.TryParseHtmlString((string)t, out var hc)) return hc;
            if (t is JArray a && (a.Count == 3 || a.Count == 4))
            {
                var v = new float[4] { 0f, 0f, 0f, 1f };
                bool bytes = false;
                for (int i = 0; i < a.Count; i++)
                {
                    if (a[i].Type != JTokenType.Integer && a[i].Type != JTokenType.Float)
                        throw new ModJsonException($"{ModJson.Where(t, File)}: a color's values are numbers");
                    v[i] = (float)a[i];
                    if (v[i] < 0f) throw new ModJsonException($"{ModJson.Where(t, File)}: a color's values can't be negative");
                    if (v[i] > 1f) bytes = true;
                }
                if (bytes)
                    for (int i = 0; i < a.Count; i++) v[i] = Mathf.Min(v[i], 255f) / 255f;
                return new Color(v[0], v[1], v[2], v[3]);
            }
            throw new ModJsonException($"{ModJson.Where(t, File)}: a color is [r, g, b] (0 to 1, or 0 to 255) or \"#rrggbb\"");
        }

        // ---- loading -------------------------------------------------------------------------------------------------

        static async Task LoadAll(int rev)
        {
            foreach (var o in owned) if (o != null) Object.Destroy(o);
            owned.Clear();
            greyed.Clear();
            made.Clear();
            effectsMade.Clear();
            foreach (var i in imports) i.Dispose();
            imports.Clear();
            List<Def> defs;
            try { defs = Defs(); }
            catch (System.Exception e) { Debug.LogException(e); defs = new List<Def>(); }
            Count = defs.Count;
            Done = 0;
            if (defs.Count > 0)
            {
                if (holder == null)
                {
                    holder = new GameObject("Mod weapon fx");
                    holder.SetActive(false);
                    Object.DontDestroyOnLoad(holder);
                }
                if (agent == null)
                {
                    var go = new GameObject("Mod weapon loader") { hideFlags = HideFlags.HideInHierarchy };
                    Object.DontDestroyOnLoad(go);
                    agent = go.AddComponent<GLTFast.TimeBudgetPerFrameDeferAgent>();
                    agent.SetFrameBudget(0.3f);
                }
                var tasks = new List<Task>();
                foreach (var d in defs) tasks.Add(LoadOne(rev, d));
                await Task.WhenAll(tasks);
            }
            if (rev != ModManager.Revision) return;
            loadedRevision = rev;
        }

        static async Task LoadOne(int rev, Def d)
        {
            try
            {
                // Everything of the item at once: textures, models and sounds.
                var parts = new[] { d.projectile, d.muzzle, d.impact };
                var built = new Task<GameObject>[3];
                for (int i = 0; i < 3; i++) built[i] = parts[i] == null || parts[i].remove || parts[i].copy ? Task.FromResult<GameObject>(null) : BuildPart(rev, d, parts[i], i);
                var shots = LoadClips(d.mod, d.shots);
                var booms = LoadClips(d.mod, d.explosionSounds);
                bool gameTurret = GameModels.Is(d.turretModel, out string turretGame);
                var turret = d.turretModel != null && !gameTurret ? BuildTurret(rev, d) : Task.FromResult<GameObject>(null);
                await Task.WhenAll(built[0], built[1], built[2], shots, booms, turret);
                if (rev != ModManager.Revision) return;
                var baseFx = BaseFx(d.item);
                var fx = baseFx != null ? Object.Instantiate(baseFx) : ScriptableObject.CreateInstance<WeaponFx>();
                fx.name = $"mod fx {d.mod.Id}:{d.label}";
                fx.item = d.item;
                if (d.look >= 0 && BaseFx(d.look) is WeaponFx look)
                {
                    // A weapon effect's base: its parts and sounds where the effect leaves them out (the item's turret
                    // model and blast type stay).
                    fx.projectile = look.projectile; fx.muzzleFlash = look.muzzleFlash; fx.impact = look.impact;
                    fx.shot = look.shot; fx.shots = look.shots; fx.shotLoops = look.shotLoops;
                    fx.explosionSound = look.explosionSound; fx.explosionSounds = look.explosionSounds;
                }
                if (d.projectile != null) fx.projectile = d.projectile.copy ? Copy(d, d.projectile, fx.projectile, 0) : built[0].Result;
                if (d.muzzle != null) fx.muzzleFlash = d.muzzle.copy ? Copy(d, d.muzzle, fx.muzzleFlash, 1) : built[1].Result;
                if (d.impact != null) fx.impact = d.impact.copy ? Copy(d, d.impact, fx.impact, 2) : built[2].Result;
                if (d.shotItem >= 0 && BaseFx(d.shotItem) is WeaponFx shotFx)
                {
                    // Another item's shot: its sounds, and whether they loop unless "shotLoops" says so.
                    fx.shot = shotFx.shot; fx.shots = shotFx.shots; fx.shotLoops = shotFx.shotLoops;
                }
                if (d.explosionItem >= 0 && BaseFx(d.explosionItem) is WeaponFx boomFx)
                {
                    fx.explosionSound = boomFx.explosionSound; fx.explosionSounds = boomFx.explosionSounds;
                }
                if (shots.Result != null && shots.Result.Length > 0) { fx.shots = shots.Result; fx.shot = shots.Result[0]; }
                if (booms.Result != null && booms.Result.Length > 0) { fx.explosionSounds = booms.Result; fx.explosionSound = booms.Result[0]; }
                if (d.shotLoops.HasValue) fx.shotLoops = d.shotLoops.Value;
                if (d.trail != null) fx.trail = d.trail;
                var turretModel = turret.Result;
                float muzzle = d.turretMuzzle, shotStart = d.turretShot;
                // A game turret's model ("item"), else the base item's when resized / moved: a copy of its ship-mounted model
                // (also shown in the hangar).
                var source = fx.turretMounted;
                if (d.turretItem >= 0)
                {
                    var other = BaseFx(d.turretItem);
                    source = other != null ? other.turretMounted : null;
                    if (source == null) Warn(d.mod, $"{d.where}: item {d.turretItem} has no turret model (the base item's is used)");
                    else
                    {
                        // Its own muzzle and shot start, unless given (TurretCore's per-look defaults).
                        var otherItem = Data.Database.Shared.Item(d.turretItem);
                        if (otherItem != null)
                        {
                            if (muzzle < 0f) muzzle = TurretCore.MuzzleUnits(otherItem, null);
                            if (shotStart < 0f) shotStart = TurretCore.ShotOffsetUnits(otherItem, null).z;
                        }
                    }
                }
                if (gameTurret)
                {
                    // Any of the game's models ("gof2:<name>"): a turret's own, or any other made to turn as one piece.
                    turretModel = GameModel(d, turretGame, "turret");
                    if (turretModel != null) Reshape(turretModel.transform, TurretRig(turretModel.transform), d);
                }
                else if (turretModel == null && d.turretModel == null && source != null && (d.turretItem >= 0 || d.TurretReshaped))
                {
                    turretModel = Object.Instantiate(source, holder.transform, false);
                    turretModel.name = $"{d.mod.Id}:{d.label} turret ({(d.turretItem >= 0 ? "item " + d.turretItem : "base")})";
                    owned.Add(turretModel);
                    Reshape(turretModel.transform, TurretRig(turretModel.transform), d);
                }
                if (turretModel != null)
                {
                    fx.turretMounted = turretModel;
                    fx.modTurret = true;
                }
                // The muzzle and shot start apply to the base item's model as well (PlayerTurret reads them either way).
                fx.turretMuzzleUnits = muzzle;
                fx.turretShotUnits = shotStart;
                owned.Add(fx);
                if (d.effect != null) effectsMade[d.effect] = fx;
                else made[d.item] = fx;
                Debug.Log($"Mods: {d.mod.Id}: weapon fx for \"{d.label}\" ({(d.effect != null ? "effect, on item " : "item ")}{d.item})");
            }
            catch (System.Exception e) when (rev == ModManager.Revision) { Warn(d.mod, $"{d.where}: {e.Message}"); if (!ModFileException.IsFileError(e)) Debug.LogException(e); }
            catch (System.Exception) { }   // a stale load (the mods changed meanwhile)
            finally { if (rev == ModManager.Revision) Done++; }
        }

        static async Task<AudioClip[]> LoadClips(ModInfo mod, List<string> files)
        {
            if (files == null || files.Count == 0) return null;
            var tasks = new List<Task<AudioClip>>();
            foreach (var f in files)
            {
                var tcs = new TaskCompletionSource<AudioClip>();
                ModAudio.Load(mod, ModAudio.Find(mod.Source, f) ?? f, c => tcs.TrySetResult(c), false);
                tasks.Add(tcs.Task);
            }
            var clips = await Task.WhenAll(tasks);
            var list = new List<AudioClip>();
            foreach (var c in clips) if (c != null) list.Add(c);   // the clips are ModAudio's (its cache), not owned here
            return list.ToArray();
        }

        /// <summary>An item's game fx (its base's for a mod item): the source of borrowed parts and sounds.</summary>
        static WeaponFx BaseFx(int item) =>
            Resources.Load<WeaponFx>($"{WeaponFx.ResourcesFolder}/item_{ModContent.ItemLook(item):000}");

        /// <summary>A turret's own model (fx "turret"), as PlayerTurret expects the game's *_ship_mounted prefabs: a "pivot"
        /// node holding the base and a node whose name contains "_gun" (the barrel along +Z). Its size as made (1 = 1 m) times
        /// "scale". A model without them turns as one piece: a "pivot" without a *_gun node gets one holding its children, a
        /// model without "pivot" one holding the whole model (so a plain GLB from the Mod Builder works).</summary>
        static async Task<GameObject> BuildTurret(int rev, Def d)
        {
            var bytes = await Task.Run(() => d.mod.Source.ReadBytes(d.turretModel));
            if (rev != ModManager.Revision || holder == null) return null;
            if (bytes == null) { Warn(d.mod, $"{d.where}: {d.turretModel} not found"); return null; }
            var import = await ModGltf.Load(d.mod, bytes, agent);
            bytes = null;
            if (import == null) { Warn(d.mod, $"{d.turretModel}: not a glTF model glTFast can read"); return null; }
            imports.Add(import);
            if (rev != ModManager.Revision || holder == null) return null;
            var root = new GameObject($"{d.mod.Id}:{d.label} turret");
            root.transform.SetParent(holder.transform, false);
            owned.Add(root);
            if (!await ModGltf.InstantiateMain(import, root.transform)) { Warn(d.mod, $"{d.turretModel}: no scene to show"); return null; }
            if (rev != ModManager.Revision || holder == null) return null;
            Reshape(root.transform, TurretRig(root.transform), d);
            return root;
        }

        /// <summary>A turret model's "pivot" right under its root (glTFast may put the scene under a node of its own) with a
        /// *_gun node in it: what's missing is made, so the whole model turns as one piece.</summary>
        static Transform TurretRig(Transform root)
        {
            var pivot = FindDeep(root, "pivot");
            if (pivot == null)
            {
                pivot = new GameObject("pivot").transform;
                pivot.SetParent(root, false);
                foreach (var c in Children(root)) if (c != pivot) c.SetParent(pivot, true);
            }
            else if (pivot.parent != root) pivot.SetParent(root, true);
            Transform gun = null;
            foreach (Transform c in pivot) if (c.name.Contains("_gun")) gun = c;
            if (gun == null)
            {
                gun = new GameObject("model_gun").transform;
                gun.SetParent(pivot, false);
                foreach (var c in Children(pivot)) if (c != gun) c.SetParent(gun, true);
            }
            return pivot;
        }

        /// <summary>One of the game's models ("gof2:<name>", Modding.GameModels) as a template under the holder, null = none.</summary>
        static GameObject GameModel(Def d, string name, string what, bool own = true)
        {
            var asm = GameModels.Find(Data.Database.Shared, name);
            var prefab = asm != null ? Visuals.AssembledObject.LoadPrefab(asm) : GameModels.Prop(name);
            if (prefab == null) { Warn(d.mod, $"{d.where}: {what}: the game has no model \"{name}\""); return null; }
            var copy = Object.Instantiate(prefab, holder.transform, false);
            copy.name = $"{d.mod.Id}:{d.label} {what} ({name})";
            if (own) owned.Add(copy);
            return copy;
        }

        /// <summary>A turret model's "scale" (on its root) and "offset": the offset moves the pivot (the root's own place is
        /// the mount's), game units, x mirrored like the models, undone by the root's scale so it stays in game units.</summary>
        static void Reshape(Transform root, Transform pivot, Def d)
        {
            root.localScale *= d.turretScale;
            var o = d.turretOffset;
            float scale = Mathf.Max(1e-4f, root.localScale.x);
            // In the mount's frame: the root may carry the model's own turn (the game's turrets: a half turn).
            pivot.localPosition += Quaternion.Inverse(root.localRotation) * new Vector3(-o.x, o.y, o.z) * (Flight.Gun.MetersPerUnit / scale);
        }

        static List<Transform> Children(Transform t)
        {
            var list = new List<Transform>();
            foreach (Transform c in t) list.Add(c);
            return list;
        }

        static Transform FindDeep(Transform t, string name)
        {
            foreach (Transform c in t)
            {
                if (c.name == name) return c;
                var f = FindDeep(c, name);
                if (f != null) return f;
            }
            return null;
        }

        static readonly string[] PartNames = { "projectile", "muzzle", "impact" };

        /// <summary>A beam laser's sprite (BuildPart): 'sizeUnits' wide and one game unit long from its parent's origin along
        /// +Z, which GunRig scales to the beam's length (the quad's y runs along the beam once ModFxPart.beam turns it).</summary>
        public static void FitBeamSprite(Transform sprite, float sizeUnits)
        {
            sprite.localScale = new Vector3(sizeUnits * M, M, M);
            sprite.localPosition = new Vector3(0f, 0f, 0.5f * M);
        }

        /// <summary>A part's template: a root (GunRig scales and turns it) holding the sprite / model with its ModFxPart.</summary>
        static async Task<GameObject> BuildPart(int rev, Def d, Part p, int kind)
        {
            // A beam laser's projectile (Gun.IsBeamLook): GunRig scales the part's root to (1, 1, the beam's length in game
            // units) along the beam (the game's beam mesh is one unit long on +Z), so the sprite / model is made one unit long
            // from the root forward, 'size' wide; a sprite turns about the beam to face the camera (ModFxPart.beam). Before, a
            // camera-facing sprite under that scale was smeared across the screen (a modder's report).
            bool beam = kind == 0 && Flight.Gun.IsBeamLook(ModContent.ItemLook(d.item));
            string tex = p.sprite ?? p.texture;
            if (tex != null) await ModMaterials.PreloadTexture(d.mod, tex, false);
            GameObject visual = null;
            bool gameModel = GameModels.Is(p.model, out string partGame);
            if (gameModel)
            {
                // One of the game's models ("gof2:<name>"): its copy instead of a file (the part's root owns it).
                visual = GameModel(d, partGame, PartNames[kind], own: false);
                if (visual == null) return null;
            }
            else if (p.model != null)
            {
                var bytes = await Task.Run(() => d.mod.Source.ReadBytes(p.model));
                if (rev != ModManager.Revision || holder == null) return null;
                if (bytes == null) { Warn(d.mod, $"{d.where}: {p.model} not found"); return null; }
                var import = await ModGltf.Load(d.mod, bytes, agent);
                bytes = null;
                if (import == null) { Warn(d.mod, $"{p.model}: not a glTF model glTFast can read"); return null; }
                imports.Add(import);
                if (rev != ModManager.Revision || holder == null) return null;
                visual = new GameObject("model");
                visual.transform.SetParent(holder.transform, false);
                if (!await ModGltf.InstantiateMain(import, visual.transform)) { Object.Destroy(visual); Warn(d.mod, $"{p.model}: no scene to show"); return null; }
                if (rev != ModManager.Revision || holder == null) return null;
            }
            var root = new GameObject($"{d.mod.Id}:{d.label} {PartNames[kind]}");
            root.transform.SetParent(holder.transform, false);
            owned.Add(root);
            Material mat = null;
            if (tex != null)
            {
                var texture = ModMaterials.Texture(d.mod, tex, false);
                var assets = ModAssets.Get();
                var template = assets != null ? (p.additive ? assets.fxAdditive : assets.fxAlpha) : null;
                if (texture != null && template != null)
                {
                    if (p.sprite != null) texture.wrapMode = TextureWrapMode.Clamp;
                    mat = new Material(template) { name = root.name };
                    mat.SetTexture("_MainTex", texture);
                    mat.SetTextureScale("_MainTex", Vector2.one);
                    mat.SetTextureOffset("_MainTex", Vector2.zero);
                    mat.SetColor("_Color", p.color);
                    if (p.glow >= 0f && mat.HasProperty("_Glow")) mat.SetFloat("_Glow", p.glow);
                    owned.Add(mat);
                }
                else if (texture == null) Warn(d.mod, $"{d.where}: {tex} isn't a PNG / JPG the game can read");
            }
            if (p.sprite != null)
            {
                if (mat == null) { Object.Destroy(root); return null; }
                visual = new GameObject("sprite");
                visual.AddComponent<MeshFilter>().sharedMesh = Quad();
                visual.AddComponent<MeshRenderer>().sharedMaterial = mat;
                visual.transform.localScale = new Vector3(p.size, p.size * Mathf.Max(0.01f, p.stretch), p.size) * M;
                visual.transform.SetParent(root.transform, false);
                if (beam) FitBeamSprite(visual.transform, p.size);
            }
            else
            {
                visual.name = "model";
                // A beam's model: on a node of its own, fitted below to 'size' across and one game unit along +Z.
                var fit = beam ? new GameObject("beam fit").transform : null;
                if (fit != null) fit.SetParent(root.transform, false);
                visual.transform.SetParent(fit != null ? fit : root.transform, false);
                visual.transform.localRotation = Quaternion.Euler(p.rotate);
                var renderers = visual.GetComponentsInChildren<Renderer>(true);
                if (mat != null)
                    foreach (var r in renderers)
                    {
                        var mats = r.sharedMaterials;
                        for (int i = 0; i < mats.Length; i++) mats[i] = mat;
                        r.sharedMaterials = mats;
                        // The GoF2 Shader Graphs multiply by the vertex colour (every game material has it on): white
                        // where the model has none.
                        var mesh = ModShipBuilder.MeshOf(r);
                        if (mesh != null && mesh.colors32.Length == 0 && mesh.isReadable)
                        {
                            // A game model's mesh is the game's own asset: a copy takes the colours.
                            if (gameModel)
                            {
                                if (!r.TryGetComponent<MeshFilter>(out var mf)) continue;
                                mesh = Object.Instantiate(mesh);
                                owned.Add(mesh);
                                mf.sharedMesh = mesh;
                            }
                            mesh.colors32 = White(mesh.vertexCount);
                        }
                    }
                // The largest extent to 'size' game units, centred on the part's origin.
                var b = ModShipBuilder.BoundsIn(root.transform, renderers);
                if (fit != null)
                {
                    float across = Mathf.Max(b.size.x, b.size.y);
                    var scale = new Vector3(across > 0f ? p.size * M / across : 1f, across > 0f ? p.size * M / across : 1f, b.size.z > 1e-6f ? M / b.size.z : 1f);
                    fit.localScale = scale;
                    fit.localPosition = new Vector3(0f, 0f, 0.5f * M) - Vector3.Scale(b.center, scale);
                }
                else
                {
                    float largest = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
                    if (largest > 0f) visual.transform.localScale *= p.size * M / largest;
                    b = ModShipBuilder.BoundsIn(root.transform, renderers);
                    visual.transform.localPosition -= b.center;
                }
            }
            foreach (var r in visual.GetComponentsInChildren<Renderer>(true))
            {
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
            var fxPart = visual.AddComponent<ModFxPart>();
            fxPart.lifetimeMs = p.lifetimeMs;
            fxPart.grow = p.grow;
            fxPart.fade = p.fade && mat != null;
            fxPart.additive = p.additive;
            fxPart.spin = p.spin;
            fxPart.beam = beam && p.sprite != null;
            fxPart.faceCamera = p.sprite != null && !p.alongFlight && !beam;
            fxPart.alongFlight = p.sprite != null && p.alongFlight && !beam;
            return root;
        }

        /// <summary>A part without a sprite / model: the base item's (or "item"'s) own, recoloured ("color" / "glow") and
        /// resized ("scale"; on a node under a new root, since GunRig sets the root's scale).</summary>
        static GameObject Copy(Def d, Part p, GameObject baseSource, int kind)
        {
            var source = p.fromItem >= 0 ? PartOf(BaseFx(p.fromItem), kind) : baseSource;
            if (source == null)
            {
                Warn(d.mod, $"{d.where}: {(p.fromItem >= 0 ? $"item {p.fromItem}" : "the base item")} has no {PartNames[kind]} to use (give a sprite or model)");
                return null;
            }
            var part = p.recolor ? Recolor(d, p, source, kind) : source;
            if (part == null || Mathf.Approximately(p.scale, 1f)) return part;
            var root = new GameObject($"{d.mod.Id}:{d.label} {PartNames[kind]} (x{p.scale.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)})");
            root.transform.SetParent(holder.transform, false);
            owned.Add(root);
            var scaler = new GameObject("scale").transform;
            scaler.SetParent(root.transform, false);
            scaler.localScale = Vector3.one * p.scale;
            Object.Instantiate(part, scaler, false).name = part.name;
            return root;
        }

        static GameObject PartOf(WeaponFx fx, int kind) =>
            fx == null ? null : kind == 0 ? fx.projectile : kind == 1 ? fx.muzzleFlash : fx.impact;

        // The base items' effect textures made grey (FxRecolor), one per texture whatever the colours asked of it.
        static readonly Dictionary<Texture, Texture> greyed = new Dictionary<Texture, Texture>();
        static Material recolorBlit;

        /// <summary>A part (the base item's or another's) with its effect materials recoloured: their texture the grey
        /// copy (each texel's brightest channel, so the bright core and soft edges stay), _Color the part's colour, _Glow
        /// its glow. Other materials (a missile's hull) keep theirs. PartAnimation's fades start from the copy's _Color.</summary>
        static GameObject Recolor(Def d, Part p, GameObject source, int kind)
        {
            var copy = Object.Instantiate(source, holder.transform, false);
            copy.name = $"{d.mod.Id}:{d.label} {PartNames[kind]} (recoloured)";
            owned.Add(copy);
            var done = new Dictionary<Material, Material>();
            foreach (var r in copy.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null || !IsEffect(m)) continue;
                    if (!done.TryGetValue(m, out var tinted))
                    {
                        tinted = new Material(m) { name = m.name + " (recoloured)" };
                        if (m.mainTexture != null) tinted.mainTexture = Grey(m.mainTexture);
                        var baseColor = m.GetColor("_Color");
                        tinted.SetColor("_Color", new Color(p.color.r, p.color.g, p.color.b, baseColor.a * p.color.a));
                        if (p.glow >= 0f && tinted.HasProperty("_Glow")) tinted.SetFloat("_Glow", p.glow);
                        owned.Add(tinted);
                        done[m] = tinted;
                    }
                    mats[i] = tinted;
                }
                r.sharedMaterials = mats;
            }
            return copy;
        }

        static bool IsEffect(Material m) =>
            m.HasProperty("_Color") && (m.shader.name == "GoF2/Additive" || m.shader.name == "GoF2/AlphaBlend");

        static Texture Grey(Texture src)
        {
            if (greyed.TryGetValue(src, out var grey)) return grey;
            if (recolorBlit == null)
            {
                var shader = Resources.Load<Shader>("GoF2Mods/FxRecolor");
                if (shader == null) return src;
                recolorBlit = new Material(shader);
            }
            var rt = new RenderTexture(src.width, src.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {
                name = src.name + " (grey)", useMipMap = true, autoGenerateMips = true,
                wrapMode = src.wrapMode, filterMode = src.filterMode, anisoLevel = src.anisoLevel,
            };
            Graphics.Blit(src, rt, recolorBlit);
            owned.Add(rt);
            greyed[src] = rt;
            return rt;
        }

        /// <summary>A 1 x 1 quad facing -Z, both sides drawn (the GoF2 Shader Graphs cull back faces).</summary>
        static Mesh Quad()
        {
            if (quad != null) return quad;
            quad = new Mesh { name = "mod fx quad" };
            var v = new[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(0.5f, 0.5f, 0), new Vector3(-0.5f, 0.5f, 0) };
            var uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            quad.vertices = new[] { v[0], v[1], v[2], v[3], v[0], v[1], v[2], v[3] };
            quad.uv = new[] { uv[0], uv[1], uv[2], uv[3], uv[0], uv[1], uv[2], uv[3] };
            quad.triangles = new[] { 0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7 };
            quad.colors32 = White(8);   // the GoF2 Shader Graphs multiply by it
            quad.RecalculateNormals();
            quad.RecalculateBounds();
            Object.DontDestroyOnLoad(quad);
            return quad;
        }

        static Color32[] White(int n)
        {
            var c = new Color32[n];
            for (int i = 0; i < n; i++) c[i] = new Color32(255, 255, 255, 255);
            return c;
        }

        static void Warn(ModInfo mod, string message)
        {
            if (!mod.Warnings.Contains(message)) mod.Warnings.Add(message);
            Debug.LogWarning($"Mods: {mod.Id}: {message}");
        }
    }
}
