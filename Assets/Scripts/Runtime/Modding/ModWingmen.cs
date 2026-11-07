// ModWingmen.cs
// Remake mods (2026-11-05, after Galaxy on Fire 3's wingmen; Modding/README.md "Wingmen"): a mod's wingmen.json, one object:
//   "chance"  % of the bars' wingmen offers (AgentGenerator.CreateAgent) whose crew flies the mod's ships (default 40; the
//             mods with a crew take turns: each one rolled in load order, the first that comes up flies)
//   "ships"   the ships they fly ("mod:id" or numbers); default: the mod's ships with a "wingmanDevice"
//   "radio"   their lines, lists of text by kind: "kill" (it destroyed a ship), "lost" (another of them is gone), "help" (its
//             hull under a third), "thanks" (the player destroyed a ship that was after it), "warn" (an enemy after the
//             player close behind it), "playerDown" (the player's hull under a fifth)
// Each ships.json "wingmanDevice" ("repair", "ammo", "charged": Flight.WingmanDevice) is checked here too. Database.Load
// (ModContent.Apply, after the ships are numbered) fills db.WingmanCrews.

using System;
using System.Collections.Generic;
using GoF2Remake.Data;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace GoF2Remake.Modding
{
    public static class ModWingmen
    {
        public const string File = "wingmen.json";

        static readonly HashSet<string> Fields = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "chance", "ships", "radio" };

        public static void Apply(Database db)
        {
            db.WingmanCrews.Clear();
            var devices = new Dictionary<ModInfo, List<int>>();
            foreach (var (mod, ship) in ModContent.ActiveShips())
            {
                if (string.IsNullOrEmpty(ship.wingmanDevice)) continue;
                if (!Flight.WingmanDevice.Valid(ship.wingmanDevice))
                {
                    Warn(mod, $"ships.json: \"{ship.name}\": \"wingmanDevice\" \"{ship.wingmanDevice}\" isn't repair, ammo or charged (none)");
                    continue;
                }
                if (!devices.TryGetValue(mod, out var l)) devices[mod] = l = new List<int>();
                l.Add(ship.index);
            }
            foreach (var mod in ModManager.Active)
            {
                JToken token;
                try { token = ModJson.Read(mod.Source, File); }
                catch (ModJsonException e) { Warn(mod, e.Message); continue; }
                if (token == null) continue;
                if (!(token is JObject o)) { Warn(mod, $"{File}: must be an object {{ \"chance\", \"ships\", \"radio\" }}"); continue; }
                var crew = Read(mod, o, devices.TryGetValue(mod, out var own) ? own : new List<int>());
                if (crew.ships.Count == 0) { Warn(mod, $"{File}: no ships for its wingmen (\"ships\", or ships.json ships with a \"wingmanDevice\")"); continue; }
                db.WingmanCrews.Add(crew);
            }
        }

        static WingmanCrew Read(ModInfo mod, JObject o, List<int> own)
        {
            foreach (var p in o.Properties())
                if (!Fields.Contains(p.Name)) Warn(mod, $"{ModJson.Where(p, File)}: unknown field \"{p.Name}\" (ignored)");
            var crew = new WingmanCrew { mod = mod, chance = Mathf.Clamp(ModJson.Int(o, "chance", 40, File), 0, 100) };
            if (ModJson.Get(o, "ships") is JToken ships)
            {
                foreach (var s in ships is JArray arr ? (IEnumerable<JToken>)arr : new[] { ships })
                {
                    string r = s.Type == JTokenType.Integer ? ((int)s).ToString() : s.Type == JTokenType.String ? (string)s : null;
                    if (r != null && !r.Contains(":") && !int.TryParse(r, out _)) r = mod.Id + ":" + r;   // (its own ship by its id)
                    if (r == null || !ModContent.TryResolveShip(r, out int i)) { Warn(mod, $"{ModJson.Where(s, File)}: \"ships\": {s} is no ship"); continue; }
                    if (!crew.ships.Contains(i)) crew.ships.Add(i);
                }
            }
            else crew.ships.AddRange(own);
            if (ModJson.Get(o, "radio") is JToken radio)
            {
                if (!(radio is JObject ro)) Warn(mod, $"{ModJson.Where(radio, File)}: \"radio\" must be an object of line lists");
                else
                    foreach (var p in ro.Properties())
                    {
                        string kind = Array.Find(WingmanCrew.LineKinds, k => string.Equals(k, p.Name, StringComparison.OrdinalIgnoreCase));
                        if (kind == null) { Warn(mod, $"{ModJson.Where(p, File)}: \"radio\": no line kind \"{p.Name}\" ({string.Join(", ", WingmanCrew.LineKinds)})"); continue; }
                        var lines = new List<string>();
                        foreach (var t in p.Value is JArray la ? (IEnumerable<JToken>)la : new[] { p.Value })
                            if (t.Type == JTokenType.String && ((string)t).Trim().Length > 0) lines.Add(((string)t).Trim());
                        crew.lines[kind] = lines;
                    }
            }
            return crew;
        }

        /// <summary>A wingmen offer's crew: each mod's in turn by its chance ('random' n -> 0..n-1), null = the game's.</summary>
        public static WingmanCrew Roll(Database db, Func<int, int> random)
        {
            if (db == null) return null;
            foreach (var c in db.WingmanCrews)
                if (c.ships.Count > 0 && random(100) < c.chance) return c;
            return null;
        }

        /// <summary>The crew whose ships include this one (its radio lines), null = none.</summary>
        public static WingmanCrew Of(Database db, int ship)
        {
            if (db == null) return null;
            foreach (var c in db.WingmanCrews) if (c.ships.Contains(ship)) return c;
            return null;
        }

        static void Warn(ModInfo mod, string message)
        {
            if (!mod.Warnings.Contains(message)) mod.Warnings.Add(message);
            Debug.LogWarning($"Mods: {mod.Id}: {message}");
        }
    }
}
