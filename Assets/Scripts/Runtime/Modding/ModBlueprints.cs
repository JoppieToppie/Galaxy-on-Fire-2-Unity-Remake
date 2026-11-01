// ModBlueprints.cs
// Remake mods: blueprints a mod adds (blueprints.json), built in the hangar's Blueprints tab like the original's:
//   [ { "id": "valkyrie",
//       "ship": "mod_id:ship_id" | number      (the product: a ship ...)
//       "item": "mod_id:item_id" | number      (... or an item; a secondary weapon comes 10 at a time),
//       "ingredients": [ { "item": 155, "amount": 120 }, ... ]   (in the hold; items too, demounted),
//       "requiresShip": "mod_id:ship_id"       (optional: only built while flying that ship, which it replaces: a skin),
//       "autocomplete": true | false | 1500000 (the price; true = the original's 1.25 x the product's price),
//       "available": "condition"               (EventRunner.Condition: before any source offers it),
//       "unlocked": false                      (true: known as soon as it is available),
//       "lounge": { "chance": 10, "price": 250000, "systemRace": -1 }   (a Space Lounge visitor sells it;
//                 "stations": [ 78, "mod:id" ]  only in those bars (then the chance defaults to 100): like the game's own
//                                               blueprint sellers, one at a fixed station;
//                 "seller": { "name": "Khador", "speaker": 20, "race": 7,   a named character instead of a local visitor, in
//                             "offer": "...", "thanks": "...", "known": "..." } those bars: their face (a story speaker), and
//                                               their own lines for the offer (#N the product, #C the price), the sale and
//                                               after it),
//       "derelict": { "chance": 5, "race": "terran" }                    (a hackable freighter wreck in an orbit holds it;
//                   "stations": [ 78, "mod:id" ]  only in those orbits (then the chance defaults to 100),
//                   "position": [ x, y, z ]       there, in game units like /pos, instead of somewhere out in front),
//       "drops": [ { "race": "pirate", "chance": 1.5 } ]                 (a ship of that race the player destroys drops it;
//                  { "ship": "mod_id:ship_id" | number, "chance": 100 }   or a ship of that type, whatever its race: a boss
//                                                                          hull dropping its own blueprint) } ]
// A ship product is a hidden "blueprint item" of its own (registered like a mod item, key "<mod>:blueprint:<id>"), so the
// Blueprints tab, saving (Session.UnlockedBlueprints / Blueprints) and the data crates work on it as on any product. Its
// name and icon are the ship's; finished, it never enters the hold: it waits at the production station and the player
// takes it there like a bought ship (Hangar.ReceiveBuiltShip). A blueprint found in a drop or a derelict comes as a data
// crate (the product's item in a Crate; CombatRadar's capture unlocks it instead of loading it).

using System;
using System.Collections.Generic;
using System.Linq;
using GoF2Remake.Data;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace GoF2Remake.Modding
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class ModBlueprints
    {
        public const string File = "blueprints.json";

        static readonly HashSet<string> Fields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "id", "ship", "item", "ingredients", "requiresShip", "autocomplete", "available", "unlocked", "lounge", "derelict", "drops",
        };
        static readonly HashSet<string> DerelictFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "chance", "race", "stations", "position" };

        /// <summary>"lounge" "seller". The lines are optional: without one the chat says the game's own.</summary>
        public sealed class Seller
        {
            public string name, offer, thanks, known;
            public int speaker = -1, race;
        }

        public sealed class Def
        {
            public ModInfo mod;
            public string where, localId, shipRef, itemRef, requiresShipRef, available;
            public readonly List<(string item, int amount)> ingredients = new List<(string, int)>();
            public bool autocomplete = true, unlocked;
            public int autocompletePrice = -1;          // -1: the original's formula
            public float loungeChance, derelictChance;  // 0: no such source
            public int loungePrice, loungeRace = -1, derelictRace = -1;
            /// <summary>"lounge" "stations" as written (empty: any bar of its systemRace) and resolved (ModBlueprints.Apply).</summary>
            public readonly List<string> loungeStationRefs = new List<string>();
            public readonly List<int> loungeStations = new List<int>();
            /// <summary>"lounge" "seller": who sells it, null for a local visitor.</summary>
            public Seller seller;
            /// <summary>"derelict" "stations" as written (empty: any orbit) and resolved (ModBlueprints.Apply).</summary>
            public readonly List<string> derelictStationRefs = new List<string>();
            public readonly List<int> derelictStations = new List<int>();
            /// <summary>"derelict" "position": game units in the orbit (null: somewhere out in front of the station).</summary>
            public Vector3? derelictPosition;
            public readonly List<(int race, float chance)> drops = new List<(int, float)>();
            /// <summary>"drops" naming a "ship" (as written, then resolved by Apply): only that ship type drops it.</summary>
            public readonly List<(string ship, float chance)> shipDropRefs = new List<(string, float)>();
            public readonly List<(int ship, float chance)> shipDrops = new List<(int, float)>();
            // The current numbering's (ModContent.EnsureMapping / Apply):
            public int product = -1, ship = -1, requiresShip = -1;
            public string DeedKey => mod.Id + ":blueprint:" + localId;
            public bool IsShip => shipRef != null;
        }

        // ---- parsing (ModContent.Parse) ----------------------------------------------------------------------------

        internal static void Parse(ModInfo mod, List<Def> into)
        {
            var token = ModJson.Read(mod.Source, File);
            if (token == null) return;
            if (!(token is JArray list)) throw new ModJsonException($"{File}: must be a list [ {{ ... }}, ... ]");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var t in list)
            {
                string where = ModJson.Where(t, File);
                if (!(t is JObject o)) throw new ModJsonException($"{where}: each blueprint must be an object {{ ... }}");
                foreach (var prop in o.Properties())
                    if (!Fields.Contains(prop.Name)) mod.Warnings.Add($"{ModJson.Where(prop, File)}: unknown field \"{prop.Name}\" (ignored)");
                var d = new Def
                {
                    mod = mod, where = where, localId = ModJson.Str(o, "id"), shipRef = ModJson.Str(o, "ship"), itemRef = ModJson.Str(o, "item"),
                    requiresShipRef = ModJson.Str(o, "requiresShip"), available = ModJson.Str(o, "available"),
                    unlocked = ModJson.Bool(o, "unlocked", false, File),
                };
                if (string.IsNullOrEmpty(d.localId) || !ModManifest.ValidId(d.localId))
                    throw new ModJsonException($"{where}: \"id\" is missing or uses more than a-z, 0-9, _ and -");
                if (!ids.Add(d.localId)) throw new ModJsonException($"{where}: the id \"{d.localId}\" is used twice");
                if ((d.shipRef == null) == (d.itemRef == null))
                    throw new ModJsonException($"{where}: a blueprint makes either a \"ship\" or an \"item\"");
                if (!(ModJson.Get(o, "ingredients") is JArray ing) || ing.Count == 0)
                    throw new ModJsonException($"{where}: \"ingredients\" is missing: [ {{ \"item\": 155, \"amount\": 50 }}, ... ]");
                foreach (var e in ing)
                {
                    if (!(e is JObject eo) || ModJson.Str(eo, "item") == null)
                        throw new ModJsonException($"{ModJson.Where(e, File)}: an ingredient is {{ \"item\": 155, \"amount\": 50 }}");
                    int amount = ModJson.Int(eo, "amount", 1, File);
                    if (amount < 1) throw new ModJsonException($"{ModJson.Where(e, File)}: \"amount\" must be at least 1");
                    d.ingredients.Add((ModJson.Str(eo, "item"), amount));
                }
                if (ModJson.Get(o, "autocomplete") is JToken ac)
                {
                    if (ac.Type == JTokenType.Boolean) d.autocomplete = (bool)ac;
                    else if (ac.Type == JTokenType.Integer) { d.autocompletePrice = Math.Max(0, (int)ac); d.autocomplete = true; }
                    else throw new ModJsonException($"{ModJson.Where(ac, File)}: \"autocomplete\" is true, false or a price");
                }
                if (ModJson.Get(o, "lounge") is JObject lo)
                {
                    d.loungeStationRefs.AddRange(ModJson.Strings(lo, "stations"));
                    d.loungeChance = ModJson.Float(lo, "chance", d.loungeStationRefs.Count > 0 ? 100f : 10f, File);
                    d.loungePrice = Math.Max(0, ModJson.Int(lo, "price", 0, File));
                    d.loungeRace = ModJson.Int(lo, "systemRace", -1, File);
                    if (ModJson.Get(lo, "seller") is JObject se)
                        d.seller = new Seller
                        {
                            name = ModJson.Str(se, "name"), speaker = ModJson.Int(se, "speaker", -1, File), race = ModJson.Int(se, "race", 0, File),
                            offer = ModJson.Str(se, "offer"), thanks = ModJson.Str(se, "thanks"), known = ModJson.Str(se, "known"),
                        };
                }
                if (ModJson.Get(o, "derelict") is JObject de)
                {
                    foreach (var prop in de.Properties())
                        if (!DerelictFields.Contains(prop.Name)) mod.Warnings.Add($"{ModJson.Where(prop, File)}: unknown field \"{prop.Name}\" (ignored)");
                    d.derelictStationRefs.AddRange(ModJson.Strings(de, "stations"));
                    d.derelictChance = ModJson.Float(de, "chance", d.derelictStationRefs.Count > 0 ? 100f : 5f, File);
                    if (ModJson.Get(de, "position") is JToken pos)
                    {
                        if (!(pos is JArray pa) || pa.Count != 3 || pa.Any(v => v.Type != JTokenType.Integer && v.Type != JTokenType.Float))
                            throw new ModJsonException($"{ModJson.Where(pos, File)}: \"position\" is [ x, y, z ] in game units (/pos shows yours)");
                        d.derelictPosition = new Vector3((float)pa[0], (float)pa[1], (float)pa[2]);
                    }
                    string race = ModJson.Str(de, "race", "any");
                    d.derelictRace = RaceOf(race);
                    if (d.derelictRace == -2) throw new ModJsonException($"{ModJson.Where(de, File)}: unknown race \"{race}\" (terran, vossk, nivelian, midorian, any)");
                    if (d.derelictRace >= 4) throw new ModJsonException($"{ModJson.Where(de, File)}: a derelict is a Terran, Vossk, Nivelian or Midorian freighter");
                }
                if (ModJson.Get(o, "drops") is JArray dr)
                    foreach (var e in dr)
                    {
                        if (!(e is JObject eo)) throw new ModJsonException($"{ModJson.Where(e, File)}: a drop is {{ \"race\": \"pirate\", \"chance\": 1 }}");
                        if (ModJson.Str(eo, "ship") is string dropShip)
                        {
                            if (ModJson.Has(eo, "race")) mod.Warnings.Add($"{ModJson.Where(e, File)}: a drop names a \"ship\" or a \"race\"; the race is ignored");
                            d.shipDropRefs.Add((dropShip, ModJson.Float(eo, "chance", 1f, File)));
                            continue;
                        }
                        string race = ModJson.Str(eo, "race", "any");
                        int r = RaceOf(race);
                        if (r == -2) throw new ModJsonException($"{ModJson.Where(e, File)}: unknown race \"{race}\" (terran, vossk, nivelian, midorian, pirate, void, specter, any)");
                        d.drops.Add((r, ModJson.Float(eo, "chance", 1f, File)));
                    }
                into.Add(d);
            }
        }

        /// <summary>A race name to KIPlayer+0x24 (-1 any, -2 unknown).</summary>
        static int RaceOf(string name)
        {
            switch ((name ?? "").Trim().ToLowerInvariant())
            {
                case "any": case "": return -1;
                case "terran": return 0;
                case "vossk": return 1;
                case "nivelian": return 2;
                case "midorian": return 3;
                case "pirate": case "pirates": return 8;
                case "void": return 9;
                case "specter": case "specters": return 10;
            }
            return -2;
        }

        // ---- the current numbering ----------------------------------------------------------------------------------

        static readonly Dictionary<int, Def> byProduct = new Dictionary<int, Def>();
        static readonly Dictionary<int, int> deedShip = new Dictionary<int, int>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { byProduct.Clear(); deedShip.Clear(); }

        /// <summary>The active mods' blueprints in load order.</summary>
        public static IEnumerable<Def> All()
        {
            foreach (var mod in ModManager.Active)
                foreach (var d in ModContent.Parse(mod).blueprints) yield return d;
        }

        /// <summary>ModContent.Apply, after the ships: each blueprint's product resolved and given its recipe; a ship's
        /// blueprint item takes the ship's price (the autocomplete and the lounge's price use it).</summary>
        internal static void Apply(Database db)
        {
            byProduct.Clear();
            deedShip.Clear();
            foreach (var d in All())
            {
                d.product = d.ship = d.requiresShip = -1;
                try
                {
                    ItemData product;
                    if (d.IsShip)
                    {
                        if (!ModContent.TryResolveShip(d.shipRef, out d.ship) || db.Ship(d.ship) == null)
                        { Warn(d, $"\"ship\": {d.shipRef} is no ship"); continue; }
                        d.product = ModContent.ItemIndexOf(d.DeedKey);
                        product = db.Item(d.product);
                        if (product == null) { Warn(d, "its blueprint item wasn't made (a mod numbering problem)"); continue; }
                        var ship = db.Ship(d.ship);
                        product.minPrice = product.maxPrice = Math.Max(1, ship.price);
                        deedShip[d.product] = d.ship;
                    }
                    else
                    {
                        if (!ModContent.TryResolveItem(d.itemRef, out d.product) || db.Item(d.product) == null)
                        { Warn(d, $"\"item\": {d.itemRef} is no item"); continue; }
                        product = db.Item(d.product);
                    }
                    if (d.requiresShipRef != null && (!ModContent.TryResolveShip(d.requiresShipRef, out d.requiresShip) || db.Ship(d.requiresShip) == null))
                    { Warn(d, $"\"requiresShip\": {d.requiresShipRef} is no ship"); continue; }
                    var parts = new List<BlueprintPart>();
                    foreach (var (item, amount) in d.ingredients)
                    {
                        if (!ModContent.TryResolveItem(item, out int ii) || db.Item(ii) == null) { parts = null; Warn(d, $"ingredient {item} is no item"); break; }
                        parts.Add(new BlueprintPart { item = ii, name = db.Item(ii).name, amount = amount });
                    }
                    if (parts == null) continue;
                    d.loungeStations.Clear();
                    foreach (string st in d.loungeStationRefs)
                        if (ModWorld.TryResolveStation(st, out int ls)) d.loungeStations.Add(ls);
                        else Warn(d, $"\"lounge\" \"stations\": {st} is no station");
                    if (d.loungeStationRefs.Count > 0 && d.loungeStations.Count == 0) d.loungeChance = 0f;
                    d.derelictStations.Clear();
                    foreach (string st in d.derelictStationRefs)
                        if (ModWorld.TryResolveStation(st, out int si)) d.derelictStations.Add(si);
                        else Warn(d, $"\"derelict\" \"stations\": {st} is no station");
                    // Every station it names unknown: none at all, not everywhere.
                    if (d.derelictStationRefs.Count > 0 && d.derelictStations.Count == 0) d.derelictChance = 0f;
                    d.shipDrops.Clear();
                    foreach (var (sr, chance) in d.shipDropRefs)
                        if (ModContent.TryResolveShip(sr, out int si) && db.Ship(si) != null) d.shipDrops.Add((si, chance));
                        else Warn(d, $"\"drops\" \"ship\": {sr} is no ship");
                    product.blueprint = parts;
                    byProduct[d.product] = d;
                }
                catch (Exception e) { Warn(d, e.Message); }
            }
        }

        static void Warn(Def d, string message)
        {
            string m = $"{d.where}: {message}";
            if (d.mod.Warnings.Contains(m)) return;
            d.mod.Warnings.Add(m);
            Debug.LogWarning($"Mods: {d.mod.Id}: {m}");
        }

        // (Filled by Apply, i.e. whenever Database.Load runs: Database.Load reads every table afresh, so never call it here.)

        /// <summary>A mod blueprint by its product item (null: none, or the original's own).</summary>
        public static Def Of(int product) => byProduct.TryGetValue(product, out var d) ? d : null;

        /// <summary>The ship a ship blueprint's item stands for (-1: not one).</summary>
        public static int ShipOf(int item) => deedShip.TryGetValue(item, out int s) ? s : -1;

        /// <summary>Its "available" condition holds in this game.</summary>
        public static bool Available(Def d) => ModUnlocks.Holds(d.mod, d.available, d.where);

        /// <summary>Blueprints.AutoCompletePrice: a mod's own price (or none: false).</summary>
        public static bool AutocompleteOverride(int product, out bool allowed, out int price)
        {
            allowed = true;
            price = -1;
            var d = Of(product);
            if (d == null) return false;
            allowed = d.autocomplete;
            price = d.autocompletePrice;
            return true;
        }

        /// <summary>The blueprint can only be built while flying a certain ship (a skin): the refusal while not, else null.</summary>
        public static string RequiresShipRefusal(int product)
        {
            var d = Of(product);
            if (d == null || d.requiresShip < 0 || Session.ShipIndex == d.requiresShip) return null;
            return string.Format(Localization.Extra("bpRequiresShip", "You need to fly the {0} to build this."), GameNames.Ship(d.requiresShip));
        }

        /// <summary>Docking (Shop.EnterStation): "unlocked": true blueprints become known once available.</summary>
        public static void UnlockAvailable()
        {
            foreach (var d in All())
                if (d.unlocked && d.product >= 0 && !Blueprints.IsUnlocked(d.product) && Available(d)) Blueprints.Unlock(d.product);
        }

        /// <summary>A ship of 'race' and type 'ship' the player destroyed: the first mod blueprint whose drop for that ship
        /// type, else that race (or any), rolls its chance, -1 for none. A ship's own drops come first, so a boss hull's
        /// blueprint isn't beaten by a race-wide one.</summary>
        public static int RollDrop(int race, int ship = -1)
        {
            if (ship >= 0)
                foreach (var d in Offerable(b => b.shipDrops.Count > 0))
                    foreach (var (s, chance) in d.shipDrops)
                        if (s == ship && UnityEngine.Random.value * 100f < chance) return d.product;
            foreach (var d in Offerable(b => b.drops.Count > 0))
                foreach (var (r, chance) in d.drops)
                    if ((r < 0 || r == race) && UnityEngine.Random.value * 100f < chance) return d.product;
            return -1;
        }

        /// <summary>TrafficPlan.Build: the mod blueprint whose derelict this orbit visit holds (the first to roll its chance
        /// among those allowed at 'station'), null for none.</summary>
        public static Def RollDerelict(int station)
        {
            foreach (var d in Offerable(b => b.derelictChance > 0f && DerelictAllowedAt(b, station)))
                if (UnityEngine.Random.value * 100f < d.derelictChance) return d;
            return null;
        }

        /// <summary>The lounge seller may stand in this bar: its "stations" name it, or it names none and the system's race
        /// fits its "systemRace" (-1: any).</summary>
        public static bool LoungeAllowedAt(Def d, int station, int systemRace) =>
            d.loungeStationRefs.Count > 0 ? d.loungeStations.Contains(station) : d.loungeRace < 0 || d.loungeRace == systemRace;

        /// <summary>The derelict may lie in this orbit: its "stations" name it, or it names none.</summary>
        public static bool DerelictAllowedAt(Def d, int station) => d.derelictStationRefs.Count == 0 || d.derelictStations.Contains(station);

        /// <summary>The blueprints a source may offer now: resolved, available and not known yet.</summary>
        public static List<Def> Offerable(Func<Def, bool> source)
        {
            var list = new List<Def>();
            foreach (var d in All())
                if (d.product >= 0 && byProduct.ContainsKey(d.product) && source(d) && !Blueprints.IsUnlocked(d.product) && Available(d)) list.Add(d);
            return list;
        }
    }
}
