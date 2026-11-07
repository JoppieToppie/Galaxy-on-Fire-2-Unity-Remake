// ModWanted.cs
// Remake mods: criminals of their own on the Most Wanted boards (a mod's wanted.json; Modding/README.md "Most Wanted").
// Each entry joins db.Wanted after the game's 25 with its key ("mod_id:id"), so the board, its spawn in the criminal's orbit
// (TrafficPlan.AddWanted), its radio lines and the Missions window treat it like the game's; WantedBoard keeps its state by
// that key (Session.ModWanted, saved by key: a mod's entries are numbered afresh each load). Unlike the game's (the
// Supernova add-on's campaign steps, never in free play) a mod's criminal is on its board whenever its "available"
// condition holds, in the campaign and in free play alike, once enough of its board's mod criminals are dead
// ("requiredBounties", counted apart from the game's: Session.ModBounties).

using System;
using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace GoF2Remake.Modding
{
    public static class ModWanted
    {
        public const string File = "wanted.json";

        static readonly HashSet<string> Fields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "id", "name", "description", "board", "race", "ship", "weapon", "secondary", "hitpoints", "reward", "loot", "lootAmount",
            "wingmen", "requiredBounties", "available", "systems", "route", "character", "portraitParts", "gender",
            "escorts", "music", "taunt", "damageTaken", "devices", "turrets",
        };

        /// <summary>Database.Load (ModContent.Apply, after the ships, items and systems are numbered): every active mod's
        /// criminals onto db.Wanted.</summary>
        public static void Apply(Database db)
        {
            foreach (var mod in ModManager.Active)
            {
                JToken token;
                try { token = ModJson.Read(mod.Source, File); }
                catch (ModJsonException e) { Warn(mod, e.Message); continue; }
                if (token == null) continue;
                if (!(token is JArray list)) { Warn(mod, $"{File}: must be a list [ {{ ... }}, ... ]"); continue; }
                foreach (var t in list)
                {
                    try { var w = Read(db, mod, t); if (w != null) db.Wanted.Add(w); }
                    catch (ModJsonException e) { Warn(mod, e.Message); }
                }
            }
        }

        static WantedData Read(Database db, ModInfo mod, JToken t)
        {
            string where = ModJson.Where(t, File);
            if (!(t is JObject o)) throw new ModJsonException($"{where}: each criminal must be an object {{ ... }}");
            foreach (var p in o.Properties())
                if (!Fields.Contains(p.Name)) Warn(mod, $"{ModJson.Where(p, File)}: unknown field \"{p.Name}\" (ignored)");
            string id = ModJson.Str(o, "id");
            if (!ModManifest.ValidId(id ?? "")) throw new ModJsonException($"{where}: \"id\" is missing or uses more than a-z, 0-9, _ and -");
            string key = mod.Id + ":" + id;
            if (db.Wanted.Exists(x => x.key == key)) throw new ModJsonException($"{where}: the id \"{id}\" is used twice");
            var w = new WantedData
            {
                index = db.Wanted.Count, key = key, mod = mod,
                name = ModContent.TranslatedText(mod, $"wanted.{id}.name", ModJson.Text(o, "name")) ?? id,
                description = ModContent.TranslatedText(mod, $"wanted.{id}.description", ModJson.Text(o, "description")),
                hitpoints = Mathf.Clamp(ModJson.Int(o, "hitpoints", 1000, File), 1, 1000000),
                reward = Mathf.Clamp(ModJson.Int(o, "reward", 50000, File), 0, 100000000),
                lootAmount = Mathf.Clamp(ModJson.Int(o, "lootAmount", 1, File), 0, 9999),
                numWingmen = Mathf.Clamp(ModJson.Int(o, "wingmen", 0, File), 0, 12),
                requiredBounties = Mathf.Max(0, ModJson.Int(o, "requiredBounties", 0, File)),
                available = ModJson.Str(o, "available"), availableWhere = where,
                character = ModJson.Str(o, "character"),
                male = !"female".Equals(ModJson.Str(o, "gender"), StringComparison.OrdinalIgnoreCase),
                loot = -1, secondary = -1, weapon = 0,
            };
            // the pilot's race (escorts, standing, the face's race) and the board that lists it
            string race = ModJson.Str(o, "race");
            w.race = race == null ? Flight.Standing.Pirate : Flight.Standing.RaceWord(race);
            if (w.race < 0) throw new ModJsonException($"{where}: \"race\" {race} isn't a race (terran, vossk, nivelian, midorian, pirate, void)");
            string board = ModJson.Str(o, "board");
            w.board = board != null ? Flight.Standing.RaceWord(board) : w.race <= 3 ? w.race : 0;
            if (w.board < 0 || w.board > 3) throw new ModJsonException($"{where}: \"board\" is terran, vossk, nivelian or midorian (the races with a Most Wanted board)");
            // the ship (any ships.json ship: the game's or a mod's) and its guns
            string ship = ModJson.Get(o, "ship")?.ToString();
            if (ship == null || !ModContent.TryResolveShip(ship, out w.ship) || db.Ship(w.ship) == null)
                throw new ModJsonException($"{where}: \"ship\" {ship ?? "(missing)"} is no ship");
            w.weapon = Item(db, o, "weapon", where, 0);
            w.secondary = Item(db, o, "secondary", where, -1);
            w.loot = Item(db, o, "loot", where, -1);
            if (w.loot < 0) w.lootAmount = 0;
            if (ModJson.Get(o, "systems") is JToken sys)
            {
                if (!(sys is JArray sa)) throw new ModJsonException($"{where}: \"systems\" is a list of systems");
                var list = new List<int>();
                foreach (var s in sa)
                {
                    if (ModWorld.TryResolveSystem(s.ToString(), out int si) && db.Systems.Exists(x => x.index == si)) list.Add(si);
                    else Warn(mod, $"{ModJson.Where(s, File)}: \"{s}\" is no system (left out)");
                }
                if (list.Count > 0) w.systems = list.ToArray();
            }
            if (ModJson.Get(o, "route") is JToken r)
            {
                if (!(r is JArray ra) || ra.Count != 2) throw new ModJsonException($"{where}: \"route\" is [least, most] systems a trip crosses");
                w.routeMin = Mathf.Clamp((int)ra[0], 1, 20);
                w.routeMax = Mathf.Clamp((int)ra[1], w.routeMin, 20);
            }
            // its escorts' ships (one ship, or a list the wingmen take in turn), its fight music and its line when uncovered
            if (ModJson.Get(o, "escorts") is JToken es)
            {
                var list = new List<int>();
                foreach (var s in es is JArray ea ? (IEnumerable<JToken>)ea : new[] { es })
                {
                    if (ModContent.TryResolveShip(s.ToString(), out int si) && db.Ship(si) != null) list.Add(si);
                    else Warn(mod, $"{ModJson.Where(s, File)}: \"escorts\" {s} is no ship (left out)");
                }
                if (list.Count > 0) w.escorts = list.ToArray();
            }
            w.music = ModJson.Str(o, "music");
            if (w.music != null && !ModMusic.Has(w.music))
            {
                Warn(mod, $"{where}: \"music\" {w.music} is no track in a mod's music folder (the game's wanted music instead)");
                w.music = null;
            }
            w.taunt = ModContent.TranslatedText(mod, $"wanted.{id}.taunt", ModJson.Text(o, "taunt"));
            if (ModJson.Get(o, "damageTaken") is JToken dt) w.damageTaken = ReadDamageTaken(db, mod, dt, where);
            if (ModJson.Get(o, "devices") is JToken dv) w.devices = ReadDevices(mod, dv, where);
            if (ModJson.Get(o, "turrets") is JToken tu) w.turrets = ReadTurrets(db, mod, tu, w.ship, where);
            if (ModJson.Get(o, "portraitParts") is JArray pp && pp.Count == 5) w.portraitParts = new[] { (int)pp[0], (int)pp[1], (int)pp[2], (int)pp[3], (int)pp[4] };
            else w.portraitParts = new[] { Mathf.Clamp(w.race, 0, 3), 0, w.male ? 0 : 1, 0, 0 };
            if (w.character != null && ModCharacters.Find(w.character) == null)
            {
                Warn(mod, $"{where}: \"character\" {w.character} is no character (the game's face instead)");
                w.character = null;
            }
            return w;
        }

        /// <summary>"damageTaken": { "primary" | "secondary" | "turret" | a weapon category | an item: percent, ... } (Data.DamageTaken);
        /// null when it changes nothing.</summary>
        internal static DamageTaken ReadDamageTaken(Database db, ModInfo mod, JToken t, string where)
        {
            if (!(t is JObject o)) throw new ModJsonException($"{where}: \"damageTaken\" is {{ \"primary\": percent, \"Laser\": percent, \"<item>\": percent, ... }}");
            var taken = new DamageTaken();
            foreach (var p in o.Properties())
            {
                if (p.Value.Type != JTokenType.Integer && p.Value.Type != JTokenType.Float)
                { Warn(mod, $"{ModJson.Where(p, File)}: \"damageTaken\" {p.Name} must be a percentage (ignored)"); continue; }
                float f = Mathf.Clamp((float)p.Value, 0f, 1000f) / 100f;
                int kind = Array.FindIndex(DamageTaken.Kinds, k => k.Equals(p.Name, StringComparison.OrdinalIgnoreCase));
                if (kind >= 0) taken.SetKind(kind, f);
                else if (db.Items.Exists(i => p.Name.Equals(i.category, StringComparison.OrdinalIgnoreCase))) taken.SetCategory(p.Name, f);
                else if (ModContent.TryResolveItem(p.Name, out int item) && db.Item(item) != null) taken.SetItem(item, f);
                else Warn(mod, $"{ModJson.Where(p, File)}: \"damageTaken\" {p.Name} is no weapon kind, category or item (ignored)");
            }
            return taken.Any ? taken : null;
        }

        static readonly HashSet<string> DeviceFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "type", "durationMs", "cooldownMs", "onHit", "everyMs", "chance", "range", "ahead", "notAhead", "facing",
            "delayMs", "strength", "distance", "blockPercent", "shieldAngle", "healPercent", "bombEveryMs", "damage",
            "hull", "lifeMs", "seekSpeed", "seekRange", "triggerRadius", "blastRadius", "layBehind", "slowMs", "slowPercent",
            "cloudRadius", "hullPercent", "count", "hullPercentOfShip", "radius", "exitDistance",
            "shieldPercent", "minRank", "maxRank", "share", "hullBelow",
        };

        /// <summary>"devices": [ { "type": "teleport" | "frontShield" | "backShield" | "energyAbsorber" | "radarJammer" |
        /// "flashGrenade" | "mindControl" | "bombSpreader" | "mineLayer" | "cloud" | "shadowClone" | "minions" | "wormhole", the run, its triggers and its numbers }, ... ] (Flight.NpcDeviceSpec);
        /// null when none is valid.</summary>
        internal static List<NpcDeviceSpec> ReadDevices(ModInfo mod, JToken t, string where, string file = File)
        {
            if (!(t is JArray list)) throw new ModJsonException($"{where}: \"devices\" is a list [ {{ \"type\": ... }}, ... ]");
            var specs = new List<NpcDeviceSpec>();
            foreach (var d in list)
            {
                string at = ModJson.Where(d, file);
                if (!(d is JObject o)) { Warn(mod, $"{at}: each device is an object {{ \"type\": ... }} (left out)"); continue; }
                foreach (var p in o.Properties())
                    if (!DeviceFields.Contains(p.Name)) Warn(mod, $"{ModJson.Where(p, file)}: unknown device field \"{p.Name}\" (ignored)");
                string type = ModJson.Str(o, "type");
                if (!Enum.TryParse(type ?? "", true, out NpcDeviceSpec.Kind kind) || int.TryParse(type, out _))
                { Warn(mod, $"{at}: device \"type\" {type ?? "(missing)"} is none of teleport, frontShield, backShield, energyAbsorber, radarJammer, flashGrenade, mindControl, bombSpreader, mineLayer, cloud, shadowClone, minions, wormhole (left out)"); continue; }
                var s = new NpcDeviceSpec { kind = kind };
                s.durationMs = Mathf.Clamp(ModJson.Float(o, "durationMs", s.durationMs, file), 1f, 600000f);
                s.cooldownMs = Mathf.Clamp(ModJson.Float(o, "cooldownMs", s.cooldownMs, file), 0f, 600000f);
                s.onHit = ModJson.Bool(o, "onHit", false, file);
                s.everyMs = Mathf.Clamp(ModJson.Float(o, "everyMs", 0f, file), 0f, 600000f);
                s.chance = Mathf.Clamp01(ModJson.Float(o, "chance", 100f, file) / 100f);
                (s.rangeMin, s.rangeMax) = Pair(mod, file, o, "range", 0f, 0f);
                s.aheadDeg = Mathf.Clamp(ModJson.Float(o, "ahead", 0f, file), 0f, 180f);
                s.notAheadDeg = Mathf.Clamp(ModJson.Float(o, "notAhead", 0f, file), 0f, 180f);
                s.facingDeg = Mathf.Clamp(ModJson.Float(o, "facing", 0f, file), 0f, 180f);
                s.hullBelow = Mathf.Clamp01(ModJson.Float(o, "hullBelow", 0f, file) / 100f);
                s.delayMs = Mathf.Clamp(ModJson.Float(o, "delayMs", kind == NpcDeviceSpec.Kind.FlashGrenade ? 3000f : 0f, file), 0f, 60000f);
                s.strength = Mathf.Clamp01(ModJson.Float(o, "strength", 100f, file) / 100f);
                (s.distanceMin, s.distanceMax) = Pair(mod, file, o, "distance", s.distanceMin, s.distanceMax);
                s.blockShare = Mathf.Clamp01(ModJson.Float(o, "blockPercent", 100f, file) / 100f);
                s.shieldDeg = Mathf.Clamp(ModJson.Float(o, "shieldAngle", s.shieldDeg, file), 1f, 180f);
                s.healShare = Mathf.Clamp(ModJson.Float(o, "healPercent", 50f, file) / 100f, 0f, 10f);
                s.bombEveryMs = Mathf.Clamp(ModJson.Float(o, "bombEveryMs", s.bombEveryMs, file), 100f, 60000f);
                s.damage = Mathf.Clamp(ModJson.Float(o, "damage", s.damage, file), 0f, 100000f);
                s.hull = Mathf.Clamp(ModJson.Float(o, "hull", s.hull, file), 1f, 100000f);
                s.lifeMs = Mathf.Clamp(ModJson.Float(o, "lifeMs", kind == NpcDeviceSpec.Kind.Cloud ? 12000f : s.lifeMs, file), 500f, 120000f);
                s.seekSpeed = Mathf.Clamp(ModJson.Float(o, "seekSpeed", s.seekSpeed, file), 0f, 20000f);
                s.seekRange = Mathf.Clamp(ModJson.Float(o, "seekRange", s.seekRange, file), 0f, 200000f);
                s.triggerRadius = Mathf.Clamp(ModJson.Float(o, "triggerRadius", s.triggerRadius, file), 50f, 50000f);
                s.blastRadius = Mathf.Clamp(ModJson.Float(o, "blastRadius", s.blastRadius, file), 50f, 50000f);
                bool hole = kind == NpcDeviceSpec.Kind.Wormhole;
                s.layBehind = Mathf.Clamp(ModJson.Float(o, "layBehind", hole ? 50f * 11.4f : s.layBehind, file), -20000f, 20000f);   // (< 0: ahead, thrown)
                s.slowMs = Mathf.Clamp(ModJson.Float(o, "slowMs", 0f, file), 0f, 60000f);
                s.slowShare = Mathf.Clamp01(ModJson.Float(o, "slowPercent", kind == NpcDeviceSpec.Kind.Cloud ? 50f : 0f, file) / 100f);
                s.cloudRadius = Mathf.Clamp(ModJson.Float(o, "cloudRadius", s.cloudRadius, file), 100f, 100000f);
                s.hullPercent = Mathf.Clamp(ModJson.Float(o, "hullPercent", 0f, file), 0f, 100f);
                s.shieldPercent = Mathf.Clamp(ModJson.Float(o, "shieldPercent", 0f, file), 0f, 1000f);
                s.minRank = Mathf.Max(0, ModJson.Int(o, "minRank", 0, file));
                s.maxRank = Mathf.Max(0, ModJson.Int(o, "maxRank", 0, file));
                s.share = Mathf.Clamp01(ModJson.Float(o, "share", 100f, file) / 100f);
                bool minions = kind == NpcDeviceSpec.Kind.Minions;
                s.count = Mathf.Clamp(ModJson.Int(o, "count", minions ? 5 : 2, file), 1, 12);
                s.hullShare = Mathf.Clamp(ModJson.Float(o, "hullPercentOfShip", minions ? 7f : 20f, file), 1f, 100f) / 100f;
                s.radius = Mathf.Clamp(ModJson.Float(o, "radius", hole ? 60f * 11.4f : s.radius, file), 200f, 50000f);
                s.exitDistance = Mathf.Clamp(ModJson.Float(o, "exitDistance", s.exitDistance, file), 0f, 50000f);
                specs.Add(s);
            }
            return specs.Count > 0 ? specs : null;
        }

        /// <summary>[ least, most ] (game units), sorted; the fallback when it is missing or not two numbers (warned).</summary>
        static (float, float) Pair(ModInfo mod, string file, JObject o, string field, float lo, float hi)
        {
            var t = ModJson.Get(o, field);
            if (t == null) return (lo, hi);
            if (t is JArray a && a.Count == 2 && a[0].Type is JTokenType.Integer or JTokenType.Float && a[1].Type is JTokenType.Integer or JTokenType.Float)
            {
                float x = Mathf.Max(0f, (float)a[0]), y = Mathf.Max(0f, (float)a[1]);
                return (Mathf.Min(x, y), Mathf.Max(x, y));
            }
            Warn(mod, $"{ModJson.Where(t, file)}: \"{field}\" is [ least, most ] (ignored)");
            return (lo, hi);
        }

        /// <summary>"turrets": [ item, ... ], the turret items (category 8 / 35, numbers or "mod:id") on its ship's turret mounts,
        /// the n-th on the n-th (World.NpcMountedTurrets), in place of what its ship type carries (ships.json "npcTurrets");
        /// [] = none. More than the ship's mounts: the rest left off.</summary>
        static List<int> ReadTurrets(Database db, ModInfo mod, JToken t, int ship, string where)
        {
            if (!(t is JArray list)) throw new ModJsonException($"{where}: \"turrets\" is a list of turret items");
            var items = new List<int>();
            foreach (var e in list)
            {
                if (!ModContent.TryResolveItem(e.ToString(), out int i) || db.Item(i) == null)
                { Warn(mod, $"{ModJson.Where(e, File)}: \"turrets\" {e} is no item (left out)"); continue; }
                var it = db.Item(i);
                if (it.categoryId != 8 && it.categoryId != 35)
                { Warn(mod, $"{ModJson.Where(e, File)}: \"turrets\" {it.name} ({i}) is no turret (left out)"); continue; }
                items.Add(i);
            }
            int mounts = db.MountsOf(ship, 2).Count;
            if (items.Count > mounts)
                Warn(mod, $"{where}: \"turrets\" lists {items.Count} turrets, its ship has {mounts} turret mounts (the rest is left off)");
            return items;
        }

        static int Item(Database db, JObject o, string field, string where, int fallback)
        {
            var t = ModJson.Get(o, field);
            if (t == null) return fallback;
            if (!ModContent.TryResolveItem(t.ToString(), out int i) || db.Item(i) == null)
                throw new ModJsonException($"{where}: \"{field}\" {t} is no item");
            return i;
        }

        static void Warn(ModInfo mod, string message)
        {
            if (!mod.Warnings.Contains(message)) mod.Warnings.Add(message);
            Debug.LogWarning($"Mods: {mod.Id}: {message}");
        }
    }
}
