// ModNpcShips.cs
// Remake mods: ships nobody flies (Modding/README.md "NPC ships", npcships.json): the capital ships, freighters and bosses
// of the orbits' traffic, as ships.json is for the ships a player flies. An entry adds one ("id") or changes one of the game's
// own or another mod's ("override": a game key below or "mod:id"):
//   [ { "id": "dreadnought", "kind": "capital" | "freighter" | "boss", "ship": n | "mod:id" (its hull: a ships.json ship)
//       or "looksLike": "battleship" | "carrier" | "vossk_battleship" | "freighter" (a game NPC hull), "name", "race",
//       "hostile", "hull" (hitpoints, x the difficulty) or "hullFactor", "gunFactor", "speed" (u/ms), "explosionScale",
//       "missiles", "retreat",
//       "hardpoints": [ { "kind": "turret" | "object" | "hangar", "position": [x, y, z], "item", "upsideDown", "hull", ... } ]
//                     (ships.json "subsystems" in the ship's frame; they replace its turrets),
//       "escorts": { "count": n | [min, max], "ships": [n | "mod:id"], "race", "strength" (hull and gun x), "hull", "gun" },
//       "loot": false | { "items": [ { "item", "amount": n | [min, max], "chance": % } ], "cargoRolls": n | [min, max],
//                 "keep": the game's own loot too },
//       "spawn": { "chance": % of the orbits, "count": n | [min, max], "races", "systems", "stations", "security", "minRank",
//                  "maxRank", "minStep", "maxStep", "freePlayOnly", "storyOnly", "condition", "position": [x, y, z],
//                  "distance": [min, max] km, "replaceGame": in the game's capital ship's place },
//       "docking": { "pads": [ { "position", "approach", "facing" } ], "standing", "raceShip", "signature", "repair",
//                    "repairPrice", "markup", "energyCells": [min, max], "ammo": [min, max], "gear", "sells": [ { "item",
//                    "amount" } ] },
//       "launches": { "ships": [n | "mod:id"], "perWave", "total", "every" (seconds) } } ]
// The game's own: "battleship" (the Terran battleship, ship 14), "carrier" (the Terran carrier), "vossk_battleship",
// "freighter" (every race's freighters) and "pirate_boss" (the pirate events' boss). Their fields start unset: the game's
// rules stay wherever a mod leaves a field out (TrafficPlan, CapitalShips and CapitalShip read the set ones), so the
// game's own random draws are the same as without mods. An entry's "spawn" places a new one (TrafficPlan.AddModNpcShips); a game
// one takes "chance" (its 30 %) and "count" (freighters), its races stay the game's.
// Plain C# (the Game layer): references are resolved per ModManager.Revision against the mods' numbering.

using System;
using System.Collections.Generic;
using System.Linq;
using GoF2Remake.Data;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace GoF2Remake.Modding
{
    /// <summary>One NPC ship type: a game one (its unset fields the game's) or a mod's.</summary>
    public class NpcShipType
    {
        public string key;              // "battleship" ... or "mod:id"
        public bool game;               // one of the game's own (GameKeys)
        public string kind;             // Capital / Freighter / Boss
        public ModInfo mod;             // the last mod that set it (null: untouched game one)
        public bool changed;            // a mod changed it (a game one)
        public int index;               // its place in All() (custom docking pad sets: SpacePoints.NpcPadBase + index)
        public int ship = -1;           // its hull: a ships.json ship, -1 = looksLike's / the game's
        public string looksLike;        // a game type's hull (new ones), null = by kind
        public string name;             // in the current language, null = the game's / the hull's
        public int race = -1;           // -1 = the orbit's
        public bool? hostile;
        public int hull = -1;           // hitpoints before the difficulty factor, -1 = the default x hullFactor
        public float hullFactor = 1f;
        public float gunFactor = 1f;
        public float speed = -1f;
        public float explosionScale = -1f;
        public bool? missiles;          // capital ships' missile salvos (null: enhanced capital ships have them, new ones too)
        public bool retreat;
        public List<CapitalSubsystem> hardpoints;   // null = its own turrets
        public Escorts escorts;
        public Loot loot;
        public Spawn spawn;
        public Docking docking;
        public Launches launches;

        /// <summary>The hull's default kind of game type (a new one's look).</summary>
        public string BaseKey => game ? key : looksLike ?? (kind == ModNpcShips.Freighter ? ModNpcShips.FreighterKey
                                                         : kind == ModNpcShips.Boss ? ModNpcShips.PirateBoss : ModNpcShips.Battleship);
        /// <summary>A capital ship that can be destroyed: every mod's own, a game one once a mod gave it a hull.</summary>
        public bool Killable => kind == ModNpcShips.Capital && (!game || hull >= 0 || !Mathf.Approximately(hullFactor, 1f) || ship >= 0);
        /// <summary>It needs World.CapitalShip on its host (killable, a dock, launches or missiles of its own).</summary>
        public bool NeedsCapitalShip => kind == ModNpcShips.Capital && (Killable || docking != null || launches != null || missiles == true);
        /// <summary>Its missile salvos (World.CapitalShip): "missiles", else on.</summary>
        public bool Missiles => missiles ?? true;

        public class Escorts
        {
            public int min = -1, max = -1;   // -1 = the game's count
            public List<int> ships = new List<int>();
            public int race = -1;
            public float hull = 1f, gun = 1f;
        }
        public class LootEntry { public int item, min = 1, max = 1; public float chance = 100f; }
        public class Loot
        {
            public bool none, keep;
            public readonly List<LootEntry> items = new List<LootEntry>();
            public int rollsMin, rollsMax;   // a freighter's cargo rolls (NpcTables.RollLoot)
        }
        public class Spawn
        {
            public float chance = -1f;       // % of the orbits (a game type: in place of its own), -1 = the game's / never
            public int min = 1, max = 1;     // how many (freighters: in place of rnd(5))
            public bool countSet;
            public int[] races, systems, stations, security;
            public int minRank, maxRank = int.MaxValue, minStep = -1, maxStep = -1;
            public bool freePlayOnly, storyOnly, replaceGame;
            public string condition;
            public Vector3? position;
            public float minKm = -1f, maxKm = -1f;
        }
        public class Pad { public Vector3 position, approach, facing = Vector3.forward; }
        public class Docking
        {
            public readonly List<Pad> pads = new List<Pad>();
            public int standing = 71;        // the least standing (Standing.Toward) that docks
            public bool standingSet, raceShip = true, signature = true, repair = true;
            public int repairPrice = 10;     // per point of hull / armour
            public float markup = 1.25f;     // x the orbit station's price
            public int cellsMin = 10, cellsMax = 15, ammoMin = 5, ammoMax = 10;
            public int gear = -1;            // how many of its race's weapons and equipment, -1 = the game's
            public readonly List<LootEntry> sells = new List<LootEntry>();
        }
        public class Launches
        {
            public readonly List<int> ships = new List<int>();
            public int perWave = 5, total = 15;
            public float everyMs = 20000f;
        }
    }

    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class ModNpcShips
    {
        public const string File = "npcships.json";
        public const string Capital = "capital", Freighter = "freighter", Boss = "boss";
        public const string Battleship = "battleship", Carrier = "carrier", VosskBattleship = "vossk_battleship", FreighterKey = "freighter",
                            PirateBoss = "pirate_boss";
        /// <summary>The game's own NPC ship types, which "override" names.</summary>
        public static readonly string[] GameKeys = { Battleship, Carrier, VosskBattleship, FreighterKey, PirateBoss };
        public static readonly string[] Kinds = { Capital, Freighter, Boss };
        /// <summary>The hulls "looksLike" takes.</summary>
        public static readonly string[] Looks = { Battleship, Carrier, VosskBattleship, FreighterKey };

        public class Def
        {
            public ModInfo mod;
            public JObject json;
            public string where, localId, overrideRef;
            public Dictionary<string, string> name;
            public string key => localId != null ? mod.Id + ":" + localId : null;
        }

        static readonly HashSet<string> Fields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "id", "override", "kind", "ship", "looksLike", "name", "race", "hostile", "hull", "hullFactor", "gunFactor", "speed",
            "explosionScale", "missiles", "retreat", "hardpoints", "escorts", "loot", "spawn", "docking", "launches",
        };
        static readonly string[] SpawnFields = { "chance", "count", "races", "systems", "stations", "security", "minRank", "maxRank",
                                                 "minStep", "maxStep", "freePlayOnly", "storyOnly", "condition", "position", "distance", "replaceGame" };
        static readonly string[] DockFields = { "pads", "standing", "raceShip", "signature", "repair", "repairPrice", "markup", "energyCells",
                                                "ammo", "gear", "sells" };

        static int revision = -1;
        static List<NpcShipType> all;
        static readonly Dictionary<string, NpcShipType> byKey = new Dictionary<string, NpcShipType>(StringComparer.OrdinalIgnoreCase);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { revision = -1; all = null; byKey.Clear(); }

        /// <summary>Forget the resolved types (a test's or the builder's change to the mods).</summary>
        public static void Invalidate() => revision = -1;

        // ---- parsing ------------------------------------------------------------------------------------------------

        public static void Parse(ModInfo mod, List<Def> into)
        {
            var token = ModJson.Read(mod.Source, File);
            if (token == null) return;
            if (!(token is JArray list)) throw new ModJsonException($"{File}: must be a list [ {{ ... }}, ... ]");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var t in list)
            {
                string where = ModJson.Where(t, File);
                if (!(t is JObject o)) throw new ModJsonException($"{where}: each NPC ship must be an object {{ ... }}");
                var d = new Def { mod = mod, json = o, where = where, localId = ModJson.Str(o, "id"), overrideRef = ModJson.Str(o, "override"), name = ModJson.Text(o, "name") };
                if ((d.localId == null) == (d.overrideRef == null))
                    throw new ModJsonException($"{where}: an NPC ship needs either \"id\" (a new one) or \"override\" (a game one: {string.Join(", ", GameKeys)}, or \"mod:id\")");
                if (d.localId != null)
                {
                    if (!ModManifest.ValidId(d.localId)) throw new ModJsonException($"{where}: \"id\" must be lower-case letters, digits, _ or -");
                    if (!ids.Add(d.localId)) throw new ModJsonException($"{where}: \"id\" \"{d.localId}\" is used twice");
                    string kind = (ModJson.Str(o, "kind") ?? Capital).ToLowerInvariant();
                    if (Array.IndexOf(Kinds, kind) < 0) throw new ModJsonException($"{where}: \"kind\" must be one of {string.Join(", ", Kinds)}");
                }
                else if (ModJson.Has(o, "kind")) mod.Warnings.Add($"{where}: \"kind\" is the changed ship's own (ignored)");
                if (ModJson.Str(o, "looksLike") is string look && Array.IndexOf(Looks, look.ToLowerInvariant()) < 0)
                    throw new ModJsonException($"{where}: \"looksLike\" must be one of {string.Join(", ", Looks)}");
                foreach (var f in new[] { "hull" }) ModJson.Int(o, f, 0, File);
                foreach (var f in new[] { "hullFactor", "gunFactor", "speed", "explosionScale" }) ModJson.Float(o, f, 0f, File);
                foreach (var f in new[] { "hostile", "missiles", "retreat" }) ModJson.Bool(o, f, false, File);
                foreach (var f in new[] { "hardpoints" })
                    if (ModJson.Get(o, f) is JToken h && !(h is JArray)) throw new ModJsonException($"{where}: \"{f}\" must be a list [ {{ \"position\": [x, y, z] }} ]");
                foreach (var f in new[] { "escorts", "spawn", "docking", "launches" })
                    if (ModJson.Get(o, f) is JToken h && !(h is JObject)) throw new ModJsonException($"{where}: \"{f}\" must be an object {{ ... }}");
                if (ModJson.Get(o, "loot") is JToken lt && !(lt is JObject) && lt.Type != JTokenType.Boolean)
                    throw new ModJsonException($"{where}: \"loot\" must be false or an object {{ \"items\": [ ... ] }}");
                if (ModJson.Get(o, "spawn") is JObject sp) Unknown(mod, sp, SpawnFields, where, "spawn");
                if (ModJson.Get(o, "docking") is JObject dk) Unknown(mod, dk, DockFields, where, "docking");
                foreach (var prop in o.Properties())
                    if (!Fields.Contains(prop.Name)) mod.Warnings.Add($"{ModJson.Where(prop, File)}: unknown field \"{prop.Name}\" (ignored)");
                into.Add(d);
            }
        }

        static void Unknown(ModInfo mod, JObject o, string[] known, string where, string what)
        {
            foreach (var prop in o.Properties())
                if (!known.Contains(prop.Name, StringComparer.OrdinalIgnoreCase)) mod.Warnings.Add($"{where}: \"{what}\": unknown field \"{prop.Name}\" (ignored)");
        }

        // ---- the table ---------------------------------------------------------------------------------------------

        /// <summary>The game's types (changed by the mods that are on) and the mods' own, in load order; made again when the
        /// mods change.</summary>
        public static List<NpcShipType> All()
        {
            if (all != null && revision == ModManager.Revision) return all;
            revision = ModManager.Revision;
            byKey.Clear();
            var list = new List<NpcShipType>();
            foreach (var k in GameKeys)
                list.Add(new NpcShipType { key = k, game = true, kind = k == FreighterKey ? Freighter : k == PirateBoss ? Boss : Capital });
            foreach (var t in list) byKey[t.key] = t;
            var db = Database.Shared;
            foreach (var mod in ModManager.Active)
                foreach (var d in ModContent.Parse(mod).npcShips)
                {
                    try
                    {
                        if (d.overrideRef != null)
                        {
                            if (!byKey.TryGetValue(d.overrideRef, out var target))
                            { Warn(mod, $"{d.where}: no NPC ship \"{d.overrideRef}\" to change ({string.Join(", ", GameKeys)} or a mod's \"mod:id\") (ignored)"); continue; }
                            Apply(db, d, target);
                            continue;
                        }
                        var e = new NpcShipType { key = d.key, kind = (ModJson.Str(d.json, "kind") ?? Capital).ToLowerInvariant() };
                        Apply(db, d, e);
                        list.Add(e);
                        byKey[e.key] = e;
                    }
                    catch (ModJsonException ex) { Warn(mod, ex.Message + " (the NPC ship is left out)"); }
                }
            for (int i = 0; i < list.Count; i++) list[i].index = i;
            all = list;
            return all;
        }

        /// <summary>A type by key ("battleship", "mod:id"), null = none.</summary>
        public static NpcShipType Get(string key)
        {
            All();
            return key != null && byKey.TryGetValue(key, out var t) ? t : null;
        }

        /// <summary>A game type when a mod changed it, else null (the game's own rules, untouched).</summary>
        public static NpcShipType Changed(string gameKey) => Get(gameKey) is NpcShipType t && t.changed ? t : null;

        /// <summary>The mods' own types (not the game's).</summary>
        public static IEnumerable<NpcShipType> ModTypes() => All().Where(t => !t.game);

        /// <summary>A game type's spawn chance (%): a mod's "spawn.chance", else 'fallback' (the game's).</summary>
        public static int Chance(string gameKey, int fallback) =>
            Changed(gameKey)?.spawn is NpcShipType.Spawn s && s.chance >= 0f ? Mathf.RoundToInt(s.chance) : fallback;

        /// <summary>The docking pads of the custom set 'n' (SpacePoints.Set past SpacePoints.NpcPadBase): approach and dock
        /// points like docks_hd.json's, in the ship's engine frame.</summary>
        public static List<SpacePoints.Point> Pads(int index)
        {
            var list = new List<SpacePoints.Point>();
            var types = All();
            if (index < 0 || index >= types.Count || types[index].docking == null) return list;
            foreach (var p in types[index].docking.pads)
            {
                list.Add(new SpacePoints.Point { type = SpacePoints.Approach, engine = p.approach, dir = p.facing });
                list.Add(new SpacePoints.Point { type = SpacePoints.Dock, engine = p.position, dir = p.facing });
            }
            return list;
        }

        static void Warn(ModInfo mod, string message)
        {
            if (mod.Warnings.Contains(message)) return;
            mod.Warnings.Add(message);
            Debug.LogWarning($"Mods: {mod.Id}: {message}");
        }

        // ---- applying an entry ---------------------------------------------------------------------------------------

        static void Apply(Database db, Def d, NpcShipType t)
        {
            var o = d.json;
            var mod = d.mod;
            t.mod = mod;
            t.changed = true;
            if (ModJson.Get(o, "ship") is JToken sh)
            {
                string r = RefOf(sh);
                if (r == null || !ModContent.TryResolveShip(r, out int s)) Warn(mod, $"{d.where}: \"ship\": {sh} is no ship (its own hull stays)");
                else t.ship = s;
            }
            if (ModJson.Str(o, "looksLike") is string look) { t.looksLike = look.ToLowerInvariant(); if (!t.game) t.ship = -1; }
            if (d.name != null) t.name = ModContent.TranslatedText(mod, "npcships." + (d.localId ?? d.overrideRef.Replace(':', '_')) + ".name", d.name);
            if (ModJson.Str(o, "race") is string race)
            {
                if (ModRaces.TryResolve(race, mod, out int r)) t.race = r; else Warn(mod, $"{d.where}: \"race\": no race \"{race}\"");
            }
            if (ModJson.Has(o, "hostile")) t.hostile = ModJson.Bool(o, "hostile", false, File);
            t.hull = ModJson.Int(o, "hull", t.hull, File);
            t.hullFactor = Mathf.Max(0.01f, ModJson.Float(o, "hullFactor", t.hullFactor, File));
            t.gunFactor = Mathf.Max(0f, ModJson.Float(o, "gunFactor", t.gunFactor, File));
            t.speed = ModJson.Float(o, "speed", t.speed, File);
            t.explosionScale = ModJson.Float(o, "explosionScale", t.explosionScale, File);
            if (ModJson.Has(o, "missiles")) t.missiles = ModJson.Bool(o, "missiles", true, File);
            t.retreat = ModJson.Bool(o, "retreat", t.retreat, File);
            if (ModJson.Get(o, "hardpoints") is JArray hp) t.hardpoints = ModContent.ReadSubsystems(db, mod, hp, File, "turret", true);
            if (ModJson.Get(o, "escorts") is JObject es) t.escorts = ReadEscorts(db, mod, es, d.where);
            if (ModJson.Get(o, "loot") is JToken lt) t.loot = ReadLoot(db, mod, lt, d.where);
            if (ModJson.Get(o, "spawn") is JObject sp) t.spawn = ReadSpawn(mod, sp, d.where);
            if (ModJson.Get(o, "docking") is JObject dk) t.docking = ReadDocking(db, mod, dk, d.where);
            if (ModJson.Get(o, "launches") is JObject ln) t.launches = ReadLaunches(mod, ln, d.where);
            if (t.kind != Capital && (t.docking != null || t.launches != null))
                Warn(mod, $"{d.where}: \"docking\" and \"launches\" are for capital ships only (ignored)");
        }

        static string RefOf(JToken t) => t == null ? null : t.Type == JTokenType.Integer ? ((int)t).ToString() : t.Type == JTokenType.String ? (string)t : null;

        /// <summary>n or [min, max] (whole numbers).</summary>
        static bool Range(JToken t, out int min, out int max)
        {
            min = max = 0;
            if (t == null) return false;
            if (t.Type == JTokenType.Integer || t.Type == JTokenType.Float) { min = max = Mathf.RoundToInt((float)t); return true; }
            if (t is JArray a && a.Count >= 1 && a.All(v => v.Type == JTokenType.Integer || v.Type == JTokenType.Float))
            {
                min = Mathf.RoundToInt((float)a[0]);
                max = a.Count > 1 ? Mathf.RoundToInt((float)a[1]) : min;
                if (max < min) (min, max) = (max, min);
                return true;
            }
            throw new ModJsonException($"{ModJson.Where(t, File)}: a number or [least, most] expected");
        }

        static float? Fraction(JObject o, string k) => ModJson.Get(o, k) is JToken v ? ModJson.Float(o, k, 1f, File) : (float?)null;

        static Vector3? Xyz(JToken t)
        {
            if (t == null) return null;
            if (!(t is JArray a) || a.Count < 3 || a.Take(3).Any(v => v.Type != JTokenType.Integer && v.Type != JTokenType.Float))
                throw new ModJsonException($"{ModJson.Where(t, File)}: [x, y, z] expected (game units)");
            return new Vector3((float)a[0], (float)a[1], (float)a[2]);
        }

        static int[] Ints(JObject o, string k, Func<string, int> resolve, ModInfo mod, string where)
        {
            if (!(ModJson.Get(o, k) is JToken v)) return null;
            var list = new List<int>();
            foreach (var e in v is JArray a ? (IEnumerable<JToken>)a : new[] { v })
            {
                string r = RefOf(e);
                int n = r != null ? resolve(r) : -1;
                if (n < 0) Warn(mod, $"{where}: \"{k}\": {e} is unknown (left out)"); else list.Add(n);
            }
            return list.ToArray();
        }

        static int ShipRef(JToken e, ModInfo mod, string where, string what)
        {
            string r = RefOf(e);
            if (r != null && ModContent.TryResolveShip(r, out int s)) return s;
            Warn(mod, $"{where}: \"{what}\": {e} is no ship (left out)");
            return -1;
        }

        static int ItemRef(Database db, JToken e, ModInfo mod, string where, string what)
        {
            string r = RefOf(e);
            if (r != null && ModContent.TryResolveItem(r, out int i) && (db == null || db.Item(i) != null)) return i;
            Warn(mod, $"{where}: \"{what}\": {e} is no item (left out)");
            return -1;
        }

        static NpcShipType.Escorts ReadEscorts(Database db, ModInfo mod, JObject o, string where)
        {
            var e = new NpcShipType.Escorts();
            if (Range(ModJson.Get(o, "count"), out int a, out int b)) { e.min = Mathf.Max(0, a); e.max = Mathf.Clamp(b, e.min, 30); }
            if (ModJson.Get(o, "ships") is JToken ships)
                foreach (var s in ships is JArray arr ? (IEnumerable<JToken>)arr : new[] { ships })
                    if (ShipRef(s, mod, where, "escorts.ships") is int n && n >= 0) e.ships.Add(n);
            if (ModJson.Str(o, "race") is string race && !ModRaces.TryResolve(race, mod, out e.race)) Warn(mod, $"{where}: \"escorts.race\": no race \"{race}\"");
            float strength = Fraction(o, "strength") ?? 1f;
            e.hull = Mathf.Max(0.05f, (Fraction(o, "hull") ?? 1f) * strength);
            e.gun = Mathf.Max(0f, (Fraction(o, "gun") ?? 1f) * strength);
            return e;
        }

        static NpcShipType.Loot ReadLoot(Database db, ModInfo mod, JToken t, string where)
        {
            var l = new NpcShipType.Loot();
            if (t.Type == JTokenType.Boolean) { l.none = !(bool)t; l.keep = (bool)t; return l; }
            var o = (JObject)t;
            l.keep = ModJson.Bool(o, "keep", false, File);
            if (Range(ModJson.Get(o, "cargoRolls"), out int a, out int b)) { l.rollsMin = Mathf.Clamp(a, 0, 10); l.rollsMax = Mathf.Clamp(b, l.rollsMin, 10); }
            if (ModJson.Get(o, "items") is JToken items)
            {
                if (!(items is JArray arr)) throw new ModJsonException($"{where}: \"loot.items\" must be a list [ {{ \"item\", \"amount\" }} ]");
                foreach (var e in arr) if (ReadEntry(db, mod, e, where, "loot.items") is NpcShipType.LootEntry le) l.items.Add(le);
            }
            return l;
        }

        static NpcShipType.LootEntry ReadEntry(Database db, ModInfo mod, JToken e, string where, string what)
        {
            var o = e as JObject;
            int item = ItemRef(db, o != null ? ModJson.Get(o, "item") : e, mod, where, what);
            if (item < 0) return null;
            var le = new NpcShipType.LootEntry { item = item };
            if (o != null)
            {
                if (Range(ModJson.Get(o, "amount"), out int a, out int b)) { le.min = Mathf.Max(1, a); le.max = Mathf.Max(le.min, b); }
                le.chance = Mathf.Clamp(ModJson.Float(o, "chance", 100f, File), 0f, 100f);
            }
            return le;
        }

        static NpcShipType.Spawn ReadSpawn(ModInfo mod, JObject o, string where)
        {
            var s = new NpcShipType.Spawn();
            s.chance = Mathf.Clamp(ModJson.Float(o, "chance", -1f, File), -1f, 100f);
            if (Range(ModJson.Get(o, "count"), out int a, out int b)) { s.min = Mathf.Clamp(a, 0, 20); s.max = Mathf.Clamp(b, s.min, 20); s.countSet = true; }
            s.races = Ints(o, "races", r => ModRaces.TryResolve(r, mod, out int n) ? n : -1, mod, where);
            s.systems = Ints(o, "systems", r => ModWorld.TryResolveSystem(r, out int n) ? n : -1, mod, where);
            s.stations = Ints(o, "stations", r => ModWorld.TryResolveStation(r, out int n) ? n : -1, mod, where);
            s.security = Ints(o, "security", r => int.TryParse(r, out int n) && n >= 0 && n <= 3 ? n : -1, mod, where);
            s.minRank = ModJson.Int(o, "minRank", 0, File);
            s.maxRank = ModJson.Int(o, "maxRank", int.MaxValue, File);
            s.minStep = ModJson.Int(o, "minStep", -1, File);
            s.maxStep = ModJson.Int(o, "maxStep", -1, File);
            s.freePlayOnly = ModJson.Bool(o, "freePlayOnly", false, File);
            s.storyOnly = ModJson.Bool(o, "storyOnly", false, File);
            s.replaceGame = ModJson.Bool(o, "replaceGame", false, File);
            s.condition = ModJson.Str(o, "condition");
            s.position = Xyz(ModJson.Get(o, "position"));
            if (Range(ModJson.Get(o, "distance"), out int km0, out int km1)) { s.minKm = Mathf.Max(0, km0); s.maxKm = Mathf.Max(s.minKm, km1); }
            return s;
        }

        static NpcShipType.Docking ReadDocking(Database db, ModInfo mod, JObject o, string where)
        {
            var d = new NpcShipType.Docking();
            if (ModJson.Get(o, "pads") is JToken pads)
            {
                if (!(pads is JArray arr)) throw new ModJsonException($"{where}: \"docking.pads\" must be a list [ {{ \"position\": [x, y, z] }} ]");
                foreach (var p in arr)
                {
                    var po = p as JObject;
                    var at = Xyz(po != null ? ModJson.Get(po, "position") : p);
                    if (at == null) { Warn(mod, $"{where}: \"docking.pads\": a pad needs \"position\""); continue; }
                    var pad = new NpcShipType.Pad { position = at.Value };
                    var facing = po != null ? Xyz(ModJson.Get(po, "facing")) : null;
                    if (facing is Vector3 f && f.sqrMagnitude > 1e-6f) pad.facing = f.normalized;
                    // The approach point: given, else 2000 units (100 m) above the pad (the ship drops onto it).
                    pad.approach = (po != null ? Xyz(ModJson.Get(po, "approach")) : null) ?? pad.position + new Vector3(0f, PadApproachUnits, 0f);
                    d.pads.Add(pad);
                }
            }
            d.standingSet = ModJson.Has(o, "standing");
            d.standing = Mathf.Clamp(ModJson.Int(o, "standing", 71, File), -100, 100);
            d.raceShip = ModJson.Bool(o, "raceShip", true, File);
            d.signature = ModJson.Bool(o, "signature", true, File);
            d.repair = ModJson.Bool(o, "repair", true, File);
            d.repairPrice = Mathf.Max(0, ModJson.Int(o, "repairPrice", 10, File));
            d.markup = Mathf.Max(0f, ModJson.Float(o, "markup", 1.25f, File));
            if (Range(ModJson.Get(o, "energyCells"), out int c0, out int c1)) { d.cellsMin = Mathf.Max(0, c0); d.cellsMax = Mathf.Max(d.cellsMin, c1); }
            if (Range(ModJson.Get(o, "ammo"), out int a0, out int a1)) { d.ammoMin = Mathf.Max(0, a0); d.ammoMax = Mathf.Max(d.ammoMin, a1); }
            d.gear = Mathf.Clamp(ModJson.Int(o, "gear", -1, File), -1, 30);
            if (ModJson.Get(o, "sells") is JToken sells)
            {
                if (!(sells is JArray arr)) throw new ModJsonException($"{where}: \"docking.sells\" must be a list [ {{ \"item\", \"amount\" }} ]");
                foreach (var e in arr) if (ReadEntry(db, mod, e, where, "docking.sells") is NpcShipType.LootEntry le) d.sells.Add(le);
            }
            return d;
        }

        /// <summary>A pad's approach point when it names none: this far (game units) above it.</summary>
        public const float PadApproachUnits = 2000f;

        static NpcShipType.Launches ReadLaunches(ModInfo mod, JObject o, string where)
        {
            var l = new NpcShipType.Launches();
            if (ModJson.Get(o, "ships") is JToken ships)
                foreach (var s in ships is JArray arr ? (IEnumerable<JToken>)arr : new[] { ships })
                    if (ShipRef(s, mod, where, "launches.ships") is int n && n >= 0) l.ships.Add(n);
            l.perWave = Mathf.Clamp(ModJson.Int(o, "perWave", 5, File), 1, 20);
            l.total = Mathf.Clamp(ModJson.Int(o, "total", 15, File), 0, 200);
            l.everyMs = Mathf.Max(3f, ModJson.Float(o, "every", 20f, File)) * 1000f;
            return l;
        }
    }
}
