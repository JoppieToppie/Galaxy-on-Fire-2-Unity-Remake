// ModContent.cs
// What the active mods add to and change in the game's tables, merged by Database.Load (Apply).
//
// items.json (a list; see Modding/README.md): each entry either adds an item or changes one.
//   { "id": "plasma_lance", "base": 12, "name": "Plasma Lance", "description": "...", "minPrice": 20000, ...,
//     "stats": { "damage": 14, "reloadMs": 450 } }
//       a new item "<mod id>:plasma_lance", a copy of its base (an original item's number, or "mod:id" of a modded one)
//       with the fields given changed. It keeps the base's type and category, and looks, sounds and works like it
//       (icon, projectile, shot sound, special behaviour), only with its own numbers and texts.
//   { "override": 12, "maxPrice": 9000, "stats": { "damage": 10 } }
//       changes an original item (or "mod:id", one of an earlier mod's): rebalancing.
// Fields: name, description (a string or { "en": ..., "de": ... }), techLevel, occurrence, minPrice, maxPrice (or
// "price": n / [min, max]), lowestPriceSystem, highestPriceSystem, vosskOnly, alwaysSoldAt, stats { name: value },
// attributes { "id": value } (the original's attribute numbers), defaultEconomy { the same fields, for the Default
// Economy }. Texts: text/<language>.json { "items.<id>.name": "...", "items.<id>.description": "..." } translates them.
//
// Numbers: single player keeps every key's number in ModRegistry (a mod turned off leaves a placeholder at its number,
// so saves stay valid); a multiplayer session numbers its mods afresh in the session's order.

using System;
using System.Collections.Generic;
using System.Linq;
using GoF2Remake.Data;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace GoF2Remake.Modding
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static partial class ModContent
    {
        /// <summary>The asset pack of the mods' models (AssemblyData.pack; AssembledObject.LoadPrefab hands out the built
        /// templates for it: ModShips, ModStations).</summary>
        public const string Pack = "mod";
        /// <summary>A mod's folder of replacement textures (ModTextures).</summary>
        public const string TexturesFolder = "textures";
        /// <summary>A new ship's own textures when it flies one of the game's models: textures/ships/&lt;ship id&gt;/&lt;game texture
        /// name&gt;.png replaces that texture on this ship alone (Database.ShipOwnTextures, Modding.ModTextures).</summary>
        public const string ShipTexturesFolder = TexturesFolder + "/ships";

        /// <summary>The mod has a picture in textures/ships/&lt;localId&gt;/.</summary>
        public static bool HasShipTextures(ModInfo mod, string localId)
        {
            if (mod?.Source == null || string.IsNullOrEmpty(localId)) return false;
            string prefix = ShipTexturesFolder + "/" + localId + "/";
            foreach (string f in mod.Source.Files)
                if (f.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && f.IndexOf('/', prefix.Length) < 0
                    && Array.Exists(ImageImport.FileExtensions, e => f.EndsWith(e, StringComparison.OrdinalIgnoreCase)))
                    return true;
            return false;
        }

        public const string ItemsFile = "items.json";
        /// <summary>The mod's weapon effects (Modding.ModWeapons): looks and sounds an item takes with "fx": "<id>".</summary>
        public const string WeaponEffectsFile = "weaponfx.json";

        /// <summary>One entry of a mod's items.json.</summary>
        public class ItemDef
        {
            public ModInfo mod;
            public JObject json;
            public string where;            // "items.json line N", for messages
            public string localId, key;     // a new item
            public string overrideRef;      // a changed item: "12" or "mod:id"
            public string baseRef;
            public Dictionary<string, string> name, description;
        }

        /// <summary>One entry of a mod's ships.json: a new ship (the PR #34 custom ship format, CustomShipData) or changes to
        /// an existing one ("override": armor, cargo, price, priceDefault, slots, handling, race, hangarHeight).</summary>
        public class ShipDef
        {
            public ModInfo mod;
            public JObject json;
            public string where, localId, key, overrideRef, data;   // data: the entry as CustomShipData JSON (no texts / ids)
            public Dictionary<string, string> name, description;
            public bool hasLounge, hasDealer;
            public object sounds;   // "sounds" (ModShipSounds.ShipSounds, GameHooks.ParseShipSounds), null = none
            /// <summary>Remake mods: "race" naming a mod race (ModRaces), and "traffic": { "races" } entries that are
            /// mod races or race words, resolved in ShipData (the mod races are numbered once every mod is read).</summary>
            public string raceRef;
            public List<string> trafficRaceRefs;
            /// <summary>Remake: an unknown mod race among them was reported (once: ShipData runs at every Database.Load).</summary>
            public bool refsReported;
        }

        /// <summary>The parsed content of one mod (read when the mods are scanned, so a broken file shows in the browser).</summary>
        public class Parsed
        {
            public readonly List<ItemDef> items = new List<ItemDef>();
            public readonly List<ShipDef> ships = new List<ShipDef>();
            public readonly List<ModBlueprints.Def> blueprints = new List<ModBlueprints.Def>();
            public readonly List<ModWorld.Def> systems = new List<ModWorld.Def>(), stations = new List<ModWorld.Def>();
            public readonly List<ModRaces.Def> races = new List<ModRaces.Def>();
            public readonly List<ModEncounters.Def> encounters = new List<ModEncounters.Def>();   // a cruise drive's encounters
            public readonly List<ModNpcShips.Def> npcShips = new List<ModNpcShips.Def>();   // npcships.json: capital ships, freighters, bosses
            public readonly Dictionary<string, Dictionary<string, string>> text =
                new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);   // language -> key -> text
        }

        static readonly Dictionary<ModInfo, Parsed> parsed = new Dictionary<ModInfo, Parsed>();

        // The numbering for ModManager.Revision.
        static int mappedRevision = -1;
        static int originalItems = -1;
        static readonly Dictionary<string, int> itemIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        static readonly Dictionary<int, ItemDef> itemDefAt = new Dictionary<int, ItemDef>();
        static readonly Dictionary<int, string> placeholderItems = new Dictionary<int, string>();
        static readonly Dictionary<int, int> itemLook = new Dictionary<int, int>();
        static readonly Dictionary<int, ItemDef> itemRenames = new Dictionary<int, ItemDef>();   // overrides with a name / description
        static readonly Dictionary<int, ModBlueprints.Def> deedAt = new Dictionary<int, ModBlueprints.Def>();   // ship blueprints' items
        static int originalShips = -1;
        static readonly Dictionary<string, int> shipIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        static readonly Dictionary<int, ShipDef> shipDefAt = new Dictionary<int, ShipDef>();
        static readonly Dictionary<int, string> placeholderShips = new Dictionary<int, string>();
        static readonly Dictionary<int, ShipDef> shipRenames = new Dictionary<int, ShipDef>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            parsed.Clear();
            mappedRevision = -1;
            originalItems = -1;
            originalShips = -1;
        }

        public static void ClearParsed() { parsed.Clear(); mappedRevision = -1; }

        // ---- parsing -------------------------------------------------------------------------------------------------

        /// <summary>Reads a mod's content files; errors go to the mod (and make it unusable).</summary>
        public static Parsed Parse(ModInfo mod)
        {
            if (parsed.TryGetValue(mod, out var p)) return p;
            p = new Parsed();
            parsed[mod] = p;
            if (mod.Manifest == null) return p;
            try
            {
                ParseItems(mod, p);
                ParseShips(mod, p);
                ModBlueprints.Parse(mod, p.blueprints);
                ModRaces.Parse(mod, p.races);
                ModEncounters.Parse(mod, p.encounters);
                ModNpcShips.Parse(mod, p.npcShips);
                ModWorld.Parse(mod, p.systems, p.stations);
                foreach (var f in mod.Source.FilesIn("text", ".json"))
                {
                    string lang = System.IO.Path.GetFileNameWithoutExtension(f);
                    if (!(ModJson.Read(mod.Source, f) is JObject o)) throw new ModJsonException($"{f}: must be an object {{ \"key\": \"text\" }}");
                    var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var prop in o.Properties()) if (prop.Value.Type == JTokenType.String) d[prop.Name] = (string)prop.Value;
                    p.text[lang] = d;
                }
            }
            catch (ModJsonException e) { mod.Errors.Add(e.Message); }
            return p;
        }

        static readonly HashSet<string> ItemFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "id", "override", "base", "name", "description", "techLevel", "occurrence", "stock", "minPrice", "maxPrice", "price",
            "lowestPriceSystem", "highestPriceSystem", "vosskOnly", "alwaysSoldAt", "stats", "attributes", "defaultEconomy", "fx", "icon",
            "available",
        };

        static void ParseItems(ModInfo mod, Parsed p)
        {
            var token = ModJson.Read(mod.Source, ItemsFile);
            if (token == null) return;
            if (!(token is JArray list)) throw new ModJsonException($"{ItemsFile}: must be a list [ {{ ... }}, ... ]");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var t in list)
            {
                string where = ModJson.Where(t, ItemsFile);
                if (!(t is JObject o)) throw new ModJsonException($"{where}: each item must be an object {{ ... }}");
                var d = new ItemDef
                {
                    mod = mod, json = o, where = where,
                    localId = ModJson.Str(o, "id"), overrideRef = ModJson.Str(o, "override"), baseRef = ModJson.Str(o, "base"),
                    name = ModJson.Text(o, "name"), description = ModJson.Text(o, "description"),
                };
                if ((d.localId == null) == (d.overrideRef == null))
                    throw new ModJsonException($"{where}: an item needs either \"id\" (a new item) or \"override\" (change an existing one)");
                if (d.localId != null)
                {
                    if (!ModManifest.ValidId(d.localId)) throw new ModJsonException($"{where}: the id \"{d.localId}\" may only use a-z, 0-9, _ and -");
                    if (!ids.Add(d.localId)) throw new ModJsonException($"{where}: the id \"{d.localId}\" is used twice");
                    if (d.baseRef == null) throw new ModJsonException($"{where}: a new item needs \"base\", the item it is made from (e.g. 12)");
                    d.key = mod.Id + ":" + d.localId;
                }
                foreach (var prop in o.Properties())
                    if (!ItemFields.Contains(prop.Name)) mod.Warnings.Add($"{ModJson.Where(prop, ItemsFile)}: unknown field \"{prop.Name}\" (ignored)");
                CheckFields(o, where, mod);
                if (ModJson.Get(o, "defaultEconomy") is JObject de) CheckFields(de, where + " (defaultEconomy)", mod);
                if (ModJson.Has(o, "icon"))
                {
                    string icon = ModJson.Str(o, "icon");
                    if (GameModels.Is(icon)) { if (GameModels.Icon(icon) == null) mod.Warnings.Add($"{where}: the game has no icon \"{icon}\" (the base item's is used)"); }
                    else if (string.IsNullOrEmpty(icon) || !mod.Source.Exists(icon))
                        mod.Warnings.Add($"{where}: the icon \"{icon}\" isn't in the mod (the base item's is used)");
                }
                p.items.Add(d);
            }
        }

        public const string ShipsFile = "ships.json";

        static readonly HashSet<string> ShipFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "id", "override", "name", "description", "race", "armor", "cargo", "price", "priceDefault", "slots", "handling",
            "hangarHeight", "mounts", "model", "icon", "modelLength", "modelYaw", "modelPitch", "modelRoll", "modelOffset", "engineGlowRadius", "engineGlowColor", "materials", "throttleGlow",
            "extraGlows", "lounge", "dealer", "traffic", "available", "npcTurrets", "flightStyle", "flight", "kit", "kitType", "build",
            "skins", "weaponColors",   // customizations of a ship without a kit (ModShipKits.CustomizationOf)
            "engineFx",   // Galaxy on Fire 3's engine effect (EngineFx)
            "addParts",   // the original's additive layers beside the hull: lights, engine glows (ModShipBuilder)
            "sounds",     // the ship's own engine and boost sounds (ModShipSounds)
            "integrated", // a built-in Khador Drive / cloak (ApplyIntegrated)
            "camera", "cockpit", "smoke", "explosionScale",   // how the ship is seen (ModShipView)
            "subsystems", "shieldMaterial", "shieldedDamage", "engineStyle", "es2Engine", "hullFactor", "retreat",   // a capital ship's subsystems (World.CapitalSubsystems)
        };

        static void ParseShips(ModInfo mod, Parsed p)
        {
            var token = ModJson.Read(mod.Source, ShipsFile);
            if (token == null) return;
            if (!(token is JArray list)) throw new ModJsonException($"{ShipsFile}: must be a list [ {{ ... }}, ... ]");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var t in list)
            {
                string where = ModJson.Where(t, ShipsFile);
                if (!(t is JObject o)) throw new ModJsonException($"{where}: each ship must be an object {{ ... }}");
                var d = new ShipDef
                {
                    mod = mod, json = o, where = where, localId = ModJson.Str(o, "id"), overrideRef = ModJson.Str(o, "override"),
                    name = ModJson.Text(o, "name"), description = ModJson.Text(o, "description"), hasLounge = ModJson.Has(o, "lounge"),
                    hasDealer = ModJson.Has(o, "dealer"),
                };
                if ((d.localId == null) == (d.overrideRef == null))
                    throw new ModJsonException($"{where}: a ship needs either \"id\" (a new ship) or \"override\" (change an existing one)");
                if (ModJson.Get(o, "integrated") is JToken ig && !(ig is JObject))
                    throw new ModJsonException($"{where}: \"integrated\" must be an object {{ \"khadorDrive\": true, \"cloak\": 95 }}");
                if (ModJson.Get(o, "npcTurrets") is JToken nt && !(nt is JArray))
                    throw new ModJsonException($"{where}: \"npcTurrets\" must be a list of turret items [ 48, \"mod:id\" ]");
                if (ModJson.Get(o, "subsystems") is JToken ss && !(ss is JArray))
                    throw new ModJsonException($"{where}: \"subsystems\" must be a list [ {{ \"kind\": \"turret\", \"position_engine\": [x, y, z], \"item\": 48 }} ]");
                CheckAddParts(o, where, mod);
                CheckEngineFx(ModJson.Get(o, "engineFx"), where, mod);
                foreach (var prop in o.Properties())
                    if (!ShipFields.Contains(prop.Name)) mod.Warnings.Add($"{ModJson.Where(prop, ShipsFile)}: unknown field \"{prop.Name}\" (ignored)");
                if (ModJson.Get(o, "race") is JToken rt && rt.Type == JTokenType.String) d.raceRef = (string)rt;   // a mod race (ModRaces)
                foreach (var f in new[] { "race", "armor", "cargo", "price", "priceDefault", "handling", "hangarHeight" })
                    if (f != "race" || d.raceRef == null) ModJson.Int(o, f, 0, ShipsFile);
                ModFlight.CheckShip(o, where, mod, ShipsFile);   // "flightStyle", "flight": per flight style
                ModShipView.CheckShip(o, where, mod, ShipsFile);   // "camera", "cockpit", "smoke", "explosionScale"
                d.sounds = GameHooks.ParseShipSounds?.Invoke(o, mod, ShipsFile);
                if (d.localId != null)
                {
                    if (!ModManifest.ValidId(d.localId)) throw new ModJsonException($"{where}: the id \"{d.localId}\" may only use a-z, 0-9, _ and -");
                    if (!ids.Add(d.localId)) throw new ModJsonException($"{where}: the id \"{d.localId}\" is used twice");
                    string model = ModJson.Str(o, "model"), kit = ModJson.Str(o, "kit");
                    if (!string.IsNullOrEmpty(kit))
                    {
                        // A customizable ship (ModShipKits): its parts come from the kit, its default build from "build".
                        if (string.IsNullOrEmpty(ModJson.Str(o, "kitType"))) throw new ModJsonException($"{where}: a kit ship needs \"kitType\" (which parts it may fit)");
                        if (ModJson.Get(o, "build") is JToken bt && !(bt is JObject)) throw new ModJsonException($"{where}: \"build\" must be an object {{ \"wings\": \"part_id\", \"tier\": 1 }}");
                    }
                    else if (string.IsNullOrEmpty(model)) throw new ModJsonException($"{where}: a new ship needs \"model\", its glTF / GLB file or \"gof2:<model>\" (one of the game's), or \"kit\" (a customizable ship)");
                    else if (!GameModels.Is(model) && !mod.Source.Exists(model)) throw new ModJsonException($"{where}: the model \"{model}\" isn't in the mod");
                    d.key = mod.Id + ":" + d.localId;
                }
                else if (ModJson.Has(o, "model"))
                {
                    // An override with a model replaces an original ship's model (ModelOverrides).
                    string model = ModJson.Str(o, "model");
                    if (string.IsNullOrEmpty(model) || !GameModels.Is(model) && !mod.Source.Exists(model)) throw new ModJsonException($"{where}: the model \"{model}\" isn't in the mod");
                }
                // The rest as CustomShipData (JsonUtility reads it; the texts and ids are the mod's own; npcTurrets: ApplyShips).
                var data = (JObject)o.DeepClone();
                foreach (var k in new[] { "id", "override", "name", "description", "npcTurrets", "build", "sounds", "integrated", "subsystems" }) data.Remove(k);
                if (d.raceRef != null) data["race"] = -1;
                if (data["traffic"] is JObject traffic && traffic["races"] is JArray races && races.Any(r => r.Type == JTokenType.String))
                {
                    // mod race keys and race words: kept aside (ShipData resolves them), the numbers stay
                    d.trafficRaceRefs = races.Select(r => r.Type == JTokenType.String ? (string)r : ((int)r).ToString()).ToList();
                    traffic.Remove("races");
                }
                try { JsonUtility.FromJson<CustomShipData>(data.ToString()); }
                catch (Exception e) { throw new ModJsonException($"{where}: {e.Message}"); }
                d.data = data.ToString();
                p.ships.Add(d);
            }
        }

        /// <summary>The engineFx fields of before 2026-10-24 (the imitation drawn from PNGs) and what replaced each.</summary>
        static readonly (string field, string instead)[] RetiredEngineFx =
        {
            ("glow", "meshes"), ("cone", "meshes"), ("spiral", "meshes"), ("trail", "trailParticles"),
        };

        /// <summary>Remake: an "engineFx" (a ship's, an exhaust style's) still giving the imitation's fields, which are ignored
        /// now: a warning naming what replaced each (Modding/README.md "Engine effect").</summary>
        public static void CheckEngineFx(JToken engineFx, string where, ModInfo mod)
        {
            if (!(engineFx is JObject fx) || mod == null) return;
            foreach (var (field, instead) in RetiredEngineFx)
                if (ModJson.Has(fx, field))
                    mod.Warnings.Add($"{where}: \"engineFx\": \"{field}\" is no longer read (ignored): Galaxy on Fire 3's engines use \"{instead}\" now");
        }

        /// <summary>ships.json "addParts": a list of { model, role, texture } (CustomAddPart). A wrong shape or role is an error,
        /// a file missing from the mod a warning (that part is left out).</summary>
        static void CheckAddParts(JObject o, string where, ModInfo mod)
        {
            var t = ModJson.Get(o, "addParts");
            if (t == null) return;
            if (!(t is JArray list)) throw new ModJsonException($"{where}: \"addParts\" must be a list [ {{ \"model\": \"models/x_lights.glb\", \"role\": \"lights\" }} ]");
            foreach (var e in list)
            {
                string at = ModJson.Where(e, ShipsFile);
                if (!(e is JObject p)) throw new ModJsonException($"{at}: each of \"addParts\" must be an object {{ \"model\", \"role\" }}");
                string model = ModJson.Str(p, "model"), role = ModJson.Str(p, "role"), texture = ModJson.Str(p, "texture");
                if (Array.IndexOf(CustomAddPart.Roles, role) < 0)
                    throw new ModJsonException($"{at}: \"role\" must be \"lights\", \"engineGlow\" or \"engine\"");
                if (string.IsNullOrEmpty(model)) throw new ModJsonException($"{at}: an add part needs \"model\", its glTF / GLB file");
                if (!mod.Source.Exists(model)) mod.Warnings.Add($"{at}: the model \"{model}\" isn't in the mod (the part is left out)");
                if (!string.IsNullOrEmpty(texture) && !mod.Source.Exists(texture)) mod.Warnings.Add($"{at}: the texture \"{texture}\" isn't in the mod (the game's is used)");
            }
        }

        /// <summary>A field written as a number or [least, most] (items.json "stock"): false = not given.</summary>
        static bool Range(JObject o, string key, out int min, out int max)
        {
            min = max = 0;
            var t = ModJson.Get(o, key);
            if (t == null) return false;
            if (t is JArray a)
            {
                bool Number(JToken n) => n is JValue v && (v.Type == JTokenType.Integer || v.Type == JTokenType.Float);
                if (a.Count != 2 || !Number(a[0]) || !Number(a[1]))
                    throw new ModJsonException($"{ModJson.Where(t, ItemsFile)}: \"{key}\" must be a number or [least, most]");
                min = (int)a[0]; max = (int)a[1];
            }
            else min = max = ModJson.Int(o, key, 0, ItemsFile);
            return true;
        }

        static void CheckFields(JObject o, string where, ModInfo mod)
        {
            foreach (var f in new[] { "techLevel", "occurrence", "minPrice", "maxPrice", "lowestPriceSystem", "highestPriceSystem", "alwaysSoldAt" })
                ModJson.Int(o, f, 0, ItemsFile);
            // Tech levels: the original's run 1-10; a mod's may go up to Shop.MaxItemTech (above 10: stocked by the top tech
            // level stations, Shop.StationStocks).
            if (ModJson.Has(o, "techLevel") && ModJson.Int(o, "techLevel", 0, ItemsFile) is int tl && (tl < 0 || tl > Shop.MaxItemTech))
                mod.Warnings.Add($"{where}: \"techLevel\" {tl} is outside 0-{Shop.MaxItemTech} (clamped)");
            // "stock": how many units a shop holds (Shop.ModStock), a number or [least, most].
            if (Range(o, "stock", out int smin, out int smax) && (smin < 1 || smax < smin || smax > Shop.MaxStock))
                mod.Warnings.Add($"{where}: \"stock\" {smin} to {smax} must be 1 to {Shop.MaxStock}, least first (clamped)");
            if (ModJson.Get(o, "stats") is JToken st)
            {
                if (!(st is JObject so)) throw new ModJsonException($"{where}: \"stats\" must be an object {{ \"damage\": 12 }}");
                foreach (var prop in so.Properties())
                {
                    if (ItemStats.IdOf(prop.Name) < 0)
                        throw new ModJsonException($"{ModJson.Where(prop, ItemsFile)}: unknown stat \"{prop.Name}\" (see Modding/README.md, or use \"attributes\")");
                    ModJson.Int(so, prop.Name, 0, ItemsFile);
                }
            }
            if (ModJson.Get(o, "attributes") is JToken at)
            {
                if (!(at is JObject ao)) throw new ModJsonException($"{where}: \"attributes\" must be an object {{ \"9\": 12 }}");
                foreach (var prop in ao.Properties())
                {
                    if (!int.TryParse(prop.Name, out int id) || id < 0 || id > 99)
                        throw new ModJsonException($"{ModJson.Where(prop, ItemsFile)}: attribute \"{prop.Name}\" must be a number 0-99");
                    if (id <= 8) mod.Warnings.Add($"{ModJson.Where(prop, ItemsFile)}: attributes 0-8 follow the item's fields (index, type, category, prices); set those instead");
                    ModJson.Int(ao, prop.Name, 0, ItemsFile);
                }
            }
        }

        // ---- numbering -----------------------------------------------------------------------------------------------

        static readonly Dictionary<int, ItemDef> itemIcons = new Dictionary<int, ItemDef>(), inheritedIcons = new Dictionary<int, ItemDef>();

    }
}
