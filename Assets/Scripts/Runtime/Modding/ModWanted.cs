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
            "escorts", "music", "taunt",
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
            if (ModJson.Get(o, "portraitParts") is JArray pp && pp.Count == 5) w.portraitParts = new[] { (int)pp[0], (int)pp[1], (int)pp[2], (int)pp[3], (int)pp[4] };
            else w.portraitParts = new[] { Mathf.Clamp(w.race, 0, 3), 0, w.male ? 0 : 1, 0, 0 };
            if (w.character != null && ModCharacters.Find(w.character) == null)
            {
                Warn(mod, $"{where}: \"character\" {w.character} is no character (the game's face instead)");
                w.character = null;
            }
            return w;
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
