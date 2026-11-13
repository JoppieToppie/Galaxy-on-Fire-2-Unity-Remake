// ModMedals.cs
// Remake mods (a player's request on Discord 2026-10-23: mods' own medals with their own conditions, mod items included,
// tiers of their own like platinum, and medals that unlock content such as the fireworks, the VoidX or the Specter;
// Modding/README.md "Medals"). A mod's medals.json: a list of medals, or { "tiers": [...], "medals": [...] }.
//   tiers   { "id", "name" (text or per language), "rank" (where it sits: bronze 1, silver 2, gold 3, supernova 4;
//           default above the highest so far), "color" ("#rrggbb"), "reward" (credits, default 5000), "plate" (bronze |
//           silver | gold | supernova | an image of the mod; default the silver plate tinted in the colour) }
//   medals  { "id", "name", "hint" ("#" = the threshold of the grade earned or next), "icon" (an image of the mod, or a
//           game medal's number 0-44 for its symbol), "counter" (an expression's value: kills, killswith(item),
//           produced(item), stat(ore)...), "grades" { tier: a number the counter must reach, or a condition },
//           "strict" (more than the number), "reward" (credits for every grade, or { tier: credits }), "available" (a
//           condition: hidden and not counted while false), "hidden" (shown only once earned), "unlocks" [ { "tier",
//           "blueprint": item } | { "tier", "ship": ship, "stations": [..] } | { "tier", "item": item, "amount" } ] }
// The game's tiers are bronze, silver, gold and supernova (the elite medals' plate). A medal keeps its best tier
// (Session.ModMedals, by key "mod_id:id"); Achievements.Check checks them on docking after the game's 45, numbered after
// them (Achievements.Count + position in All), with the same toast, rewards (none on Extreme) and Status window. Like
// the game's medals, none in multiplayer or a game without medals (Achievements.Enabled). Conditions read them:
// medal(mod_id:id[, tier]), medals(tier).

using System;
using System.Collections.Generic;
using System.Globalization;
using GoF2Remake.Data;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace GoF2Remake.Modding
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class ModMedals
    {
        public const string File = "medals.json";

        public sealed class Tier
        {
            public string id;
            public ModInfo mod;   // null: the game's
            public Dictionary<string, string> name;
            public string fallbackName;
            public float rank;
            public Color color;
            public int reward;
            /// <summary>bronze / silver / gold / supernova, or an image path of 'mod'.</summary>
            public string plate;
            public bool tintPlate;
            public bool Elite => plate == "supernova";
            public string Name => mod != null ? ModContent.TranslatedText(mod, $"medals.tiers.{id}.name", name) ?? id
                                              : Localization.Extra("medalTier_" + id, fallbackName);
        }

        public sealed class Grade
        {
            public Tier tier;
            public double? threshold;   // the counter's target, or ...
            public string condition;    // ... a condition
        }

        public sealed class Unlock
        {
            public Tier tier;
            public int blueprint = -1, ship = -1, item = -1, amount = 1;
            public List<int> stations;   // ship: those dealers; null = every dealer with ships
        }

        public sealed class Medal
        {
            public ModInfo mod;
            public string id, key, where;
            public Dictionary<string, string> name, hint;
            public string icon;
            public int symbol = -1;
            public string counter, available;
            public bool strict, hidden;
            public readonly List<Grade> grades = new List<Grade>();   // lowest rank first
            public readonly Dictionary<string, int> rewards = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            public int reward = -1;
            public readonly List<Unlock> unlocks = new List<Unlock>();
            public string Name => ModContent.TranslatedText(mod, $"medals.{id}.name", name) ?? id;
            public string HintText => ModContent.TranslatedText(mod, $"medals.{id}.hint", hint) ?? "";
        }

        static readonly Tier[] GameTiers =
        {
            new Tier { id = "bronze", fallbackName = "Bronze", rank = 1, color = new Color32(0xce, 0x82, 0x58, 0xff), reward = 1000, plate = "bronze" },
            new Tier { id = "silver", fallbackName = "Silver", rank = 2, color = Color.white, reward = 2500, plate = "silver" },
            new Tier { id = "gold", fallbackName = "Gold", rank = 3, color = new Color32(0xfa, 0xd1, 0x0e, 0xff), reward = 5000, plate = "gold" },
            new Tier { id = "supernova", fallbackName = "Supernova", rank = 4, color = new Color32(0xfa, 0x79, 0x21, 0xff), reward = 5000, plate = "supernova" },
        };

        static readonly Dictionary<string, Tier> tiers = new Dictionary<string, Tier>(StringComparer.OrdinalIgnoreCase);
        static readonly List<Medal> all = new List<Medal>();
        static readonly Dictionary<int, List<string>> notes = new Dictionary<int, List<string>>();
        static int builtRevision = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { tiers.Clear(); all.Clear(); notes.Clear(); builtRevision = -1; }

        static readonly HashSet<string> TopFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "tiers", "medals" };
        static readonly HashSet<string> TierFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "id", "name", "rank", "color", "reward", "plate" };
        static readonly HashSet<string> MedalFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "id", "name", "hint", "icon", "counter", "grades", "strict", "reward", "available", "hidden", "unlocks" };
        static readonly HashSet<string> UnlockFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "tier", "blueprint", "ship", "item", "amount", "stations" };

        /// <summary>The active mods' medals in load order (index k is medal Achievements.Count + k).</summary>
        public static IReadOnlyList<Medal> All { get { Build(); return all; } }

        /// <summary>A tier by id (the game's four or a mod's), null = none.</summary>
        public static Tier TierOf(string id) { Build(); return id != null && tiers.TryGetValue(id.Trim(), out var t) ? t : null; }

        /// <summary>The medal of a combined number (Achievements.Count and up), null = not a mod's.</summary>
        public static Medal At(int medal)
        {
            Build();
            int k = medal - Achievements.Count;
            return k >= 0 && k < all.Count ? all[k] : null;
        }

        /// <summary>A medal by key ("mod_id:id"), null = none active.</summary>
        public static Medal Find(string key)
        {
            Build();
            return all.Find(m => string.Equals(m.key, key?.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        public static int IndexOf(Medal m) { Build(); int k = all.IndexOf(m); return k < 0 ? -1 : Achievements.Count + k; }

        // ---- reading -------------------------------------------------------------------------------------------------

        static void Build()
        {
            if (builtRevision == ModManager.Revision) return;
            builtRevision = ModManager.Revision;
            tiers.Clear();
            all.Clear();
            notes.Clear();
            foreach (var t in GameTiers) tiers[t.id] = t;
            tiers["elite"] = GameTiers[3];
            var files = new List<(ModInfo mod, JToken token)>();
            foreach (var mod in ModManager.Active)
            {
                JToken token;
                try { token = ModJson.Read(mod.Source, File); }
                catch (ModJsonException e) { Warn(mod, e.Message); continue; }
                if (token == null) continue;
                if (!(token is JArray) && !(token is JObject)) { Warn(mod, $"{File}: must be a list of medals or {{ \"tiers\", \"medals\" }}"); continue; }
                files.Add((mod, token));
            }
            // The tiers first (a mod's medals may use another mod's tier, whatever the order), then the medals.
            float top = 4;
            foreach (var (mod, token) in files)
            {
                if (!(token is JObject o)) continue;
                foreach (var p in o.Properties())
                    if (!TopFields.Contains(p.Name)) Warn(mod, $"{ModJson.Where(p, File)}: unknown field \"{p.Name}\" (ignored)");
                if (ModJson.Get(o, "tiers") is JToken tl)
                    foreach (var t in tl is JArray ta ? (IEnumerable<JToken>)ta : new[] { tl })
                    {
                        var tier = ReadTier(mod, t, ref top);
                        if (tier != null) tiers[tier.id] = tier;   // a later mod's tier of the same id wins
                    }
            }
            foreach (var (mod, token) in files)
            {
                var list = token is JArray a ? a : ModJson.Get((JObject)token, "medals");
                if (list == null) continue;
                if (!(list is JArray medals)) { Warn(mod, $"{ModJson.Where(list, File)}: \"medals\" must be a list"); continue; }
                foreach (var t in medals)
                {
                    var m = ReadMedal(mod, t);
                    if (m == null) continue;
                    if (all.Exists(x => string.Equals(x.key, m.key, StringComparison.OrdinalIgnoreCase)))
                    { Warn(mod, $"{m.where}: a second medal \"{m.id}\" (ignored)"); continue; }
                    all.Add(m);
                }
            }
        }

        static Tier ReadTier(ModInfo mod, JToken t, ref float top)
        {
            string where = ModJson.Where(t, File);
            if (!(t is JObject o)) { Warn(mod, $"{where}: a tier must be an object {{ \"id\", \"name\", \"rank\", \"color\" }}"); return null; }
            foreach (var p in o.Properties())
                if (!TierFields.Contains(p.Name)) Warn(mod, $"{ModJson.Where(p, File)}: unknown field \"{p.Name}\" (ignored)");
            string id = ModJson.Str(o, "id")?.Trim();
            if (string.IsNullOrEmpty(id)) { Warn(mod, $"{where}: a tier needs an \"id\""); return null; }
            if (Array.Exists(GameTiers, g => string.Equals(g.id, id, StringComparison.OrdinalIgnoreCase)) || string.Equals(id, "elite", StringComparison.OrdinalIgnoreCase))
            { Warn(mod, $"{where}: \"{id}\" is one of the game's tiers (ignored)"); return null; }
            var tier = new Tier { id = id, mod = mod, name = ModJson.Text(o, "name") };
            tier.rank = ModJson.Has(o, "rank") ? ModJson.Float(o, "rank", top + 1, File) : top + 1;
            top = Mathf.Max(top, tier.rank);
            tier.reward = Mathf.Max(0, ModJson.Int(o, "reward", 5000, File));
            string color = ModJson.Str(o, "color");
            tier.color = Color.white;
            if (color != null && !ColorUtility.TryParseHtmlString(color.StartsWith("#") ? color : "#" + color, out tier.color))
            { Warn(mod, $"{where}: \"color\" \"{color}\" isn't a colour like \"#e5e4e2\" (white)"); tier.color = Color.white; }
            tier.color.a = 1f;
            string plate = ModJson.Str(o, "plate")?.Trim();
            if (string.IsNullOrEmpty(plate)) { tier.plate = "silver"; tier.tintPlate = true; }
            else
            {
                string lower = plate.ToLowerInvariant();
                tier.plate = lower == "elite" ? "supernova" : lower is "bronze" or "silver" or "gold" or "supernova" ? lower : plate;
            }
            return tier;
        }

        static Medal ReadMedal(ModInfo mod, JToken t)
        {
            string where = ModJson.Where(t, File);
            if (!(t is JObject o)) { Warn(mod, $"{where}: a medal must be an object {{ \"id\", \"name\", \"grades\" }}"); return null; }
            foreach (var p in o.Properties())
                if (!MedalFields.Contains(p.Name)) Warn(mod, $"{ModJson.Where(p, File)}: unknown field \"{p.Name}\" (ignored)");
            string id = ModJson.Str(o, "id")?.Trim();
            if (string.IsNullOrEmpty(id) || id.Contains(":")) { Warn(mod, $"{where}: a medal needs an \"id\" (without \":\")"); return null; }
            var m = new Medal
            {
                mod = mod, id = id, key = mod.Id + ":" + id, where = $"{File}: \"{id}\"",
                name = ModJson.Text(o, "name"), hint = ModJson.Text(o, "hint"),
                counter = ModJson.Str(o, "counter"), available = ModJson.Str(o, "available"),
                strict = ModJson.Bool(o, "strict", false, File), hidden = ModJson.Bool(o, "hidden", false, File),
            };
            if (string.IsNullOrWhiteSpace(m.counter)) m.counter = null;
            if (ModJson.Get(o, "icon") is JToken icon)
            {
                if (icon.Type == JTokenType.Integer) m.symbol = Mathf.Clamp((int)icon, 0, Achievements.Count - 1);
                else if (icon.Type == JTokenType.String) m.icon = (string)icon;
            }
            if (!(ModJson.Get(o, "grades") is JObject grades) || !grades.HasValues)
            { Warn(mod, $"{m.where}: \"grades\" must be an object {{ \"bronze\": 10, \"gold\": \"condition\" }}"); return null; }
            foreach (var p in grades.Properties())
            {
                var tier = TierOf(p.Name);
                if (tier == null) { Warn(mod, $"{ModJson.Where(p, File)}: no tier \"{p.Name}\" (bronze, silver, gold, supernova or a mod's)"); continue; }
                var g = new Grade { tier = tier };
                if (p.Value.Type == JTokenType.Integer || p.Value.Type == JTokenType.Float)
                {
                    if (m.counter == null) { Warn(mod, $"{ModJson.Where(p, File)}: a number needs the medal's \"counter\" (ignored)"); continue; }
                    g.threshold = (double)p.Value;
                }
                else if (p.Value.Type == JTokenType.String && ((string)p.Value).Trim().Length > 0) g.condition = (string)p.Value;
                else { Warn(mod, $"{ModJson.Where(p, File)}: \"{p.Name}\" must be a number or a condition"); continue; }
                m.grades.Add(g);
            }
            if (m.grades.Count == 0) { Warn(mod, $"{m.where}: no grade it can be earned at (ignored)"); return null; }
            m.grades.Sort((a, b) => a.tier.rank.CompareTo(b.tier.rank));
            if (ModJson.Get(o, "reward") is JToken reward)
            {
                if (reward.Type == JTokenType.Integer) m.reward = Mathf.Max(0, (int)reward);
                else if (reward is JObject ro)
                    foreach (var p in ro.Properties())
                    {
                        if (p.Value.Type == JTokenType.Integer && TierOf(p.Name) != null) m.rewards[p.Name] = Mathf.Max(0, (int)p.Value);
                        else Warn(mod, $"{ModJson.Where(p, File)}: \"reward\" per tier must be {{ \"gold\": 5000 }}");
                    }
                else Warn(mod, $"{ModJson.Where(reward, File)}: \"reward\" must be credits or {{ tier: credits }}");
            }
            if (ModJson.Get(o, "unlocks") is JToken unlocks)
                foreach (var u in unlocks is JArray ua ? (IEnumerable<JToken>)ua : new[] { unlocks })
                {
                    var unlock = ReadUnlock(mod, m, u);
                    if (unlock != null) m.unlocks.Add(unlock);
                }
            return m;
        }

        static Unlock ReadUnlock(ModInfo mod, Medal m, JToken t)
        {
            string where = ModJson.Where(t, File);
            if (!(t is JObject o)) { Warn(mod, $"{where}: an unlock must be an object {{ \"blueprint\" | \"ship\" | \"item\" }}"); return null; }
            foreach (var p in o.Properties())
                if (!UnlockFields.Contains(p.Name)) Warn(mod, $"{ModJson.Where(p, File)}: unknown field \"{p.Name}\" (ignored)");
            var u = new Unlock { tier = m.grades[0].tier };
            if (ModJson.Str(o, "tier") is string tierId)
            {
                u.tier = m.grades.Find(g => string.Equals(g.tier.id, tierId.Trim(), StringComparison.OrdinalIgnoreCase))?.tier;
                if (u.tier == null) { Warn(mod, $"{where}: \"tier\" \"{tierId}\" isn't one of the medal's grades (ignored)"); return null; }
            }
            string Ref(string field)
            {
                var v = ModJson.Get(o, field);
                if (v == null) return null;
                string r = v.Type == JTokenType.Integer ? ((int)v).ToString(CultureInfo.InvariantCulture) : v.Type == JTokenType.String ? ((string)v).Trim() : null;
                if (r != null && !r.Contains(":") && !int.TryParse(r, out _)) r = mod.Id + ":" + r;   // its own content by id
                return r ?? "";
            }
            int kinds = 0;
            if (Ref("blueprint") is string bp)
            {
                kinds++;
                if (!ModContent.TryResolveItem(bp, out u.blueprint)) { Warn(mod, $"{where}: \"blueprint\" {bp} is no item"); return null; }
            }
            if (Ref("item") is string item)
            {
                kinds++;
                if (!ModContent.TryResolveItem(item, out u.item)) { Warn(mod, $"{where}: \"item\" {item} is no item"); return null; }
                u.amount = Mathf.Max(1, ModJson.Int(o, "amount", 1, File));
            }
            if (Ref("ship") is string ship)
            {
                kinds++;
                if (!ModContent.TryResolveShip(ship, out u.ship)) { Warn(mod, $"{where}: \"ship\" {ship} is no ship"); return null; }
                if (ModJson.Get(o, "stations") is JToken sl)
                {
                    u.stations = new List<int>();
                    foreach (var s in sl is JArray sa ? (IEnumerable<JToken>)sa : new[] { sl })
                    {
                        string r = s.Type == JTokenType.Integer ? ((int)s).ToString(CultureInfo.InvariantCulture) : s.Type == JTokenType.String ? (string)s : null;
                        if (r != null && ModWorld.TryResolveStation(r, out int st)) u.stations.Add(st);
                        else Warn(mod, $"{ModJson.Where(s, File)}: \"stations\": {s} is no station");
                    }
                }
            }
            if (kinds != 1) { Warn(mod, $"{where}: an unlock names one of \"blueprint\", \"ship\" or \"item\" (ignored)"); return null; }
            return u;
        }

        static void Warn(ModInfo mod, string message)
        {
            if (mod == null) return;
            if (!mod.Warnings.Contains(message)) mod.Warnings.Add(message);
            Debug.LogWarning($"Mods: {mod.Id}: {message}");
        }

        // ---- state ---------------------------------------------------------------------------------------------------

        /// <summary>The tier the medal is earned at, null = not earned. A tier no longer known (its mod off) counts as the
        /// medal's lowest grade.</summary>
        public static Tier Earned(Medal m)
        {
            if (m == null || !Session.ModMedals.TryGetValue(m.key, out string id)) return null;
            return TierOf(id) ?? m.grades[0].tier;
        }

        /// <summary>Shown in the Status window: available (or earned), and not hidden until earned.</summary>
        public static bool Shown(Medal m) => Earned(m) != null || (!m.hidden && Available(m));

        static bool Available(Medal m) => ModUnlocks.Holds(m.mod, m.available, m.where);

        /// <summary>The counter's value now, null = no counter or it can't be read (warned once).</summary>
        public static double? Count(Medal m)
        {
            if (m.counter == null || GameHooks.Number == null) return null;
            if (GameHooks.Number(m.counter, out double v, out string error)) return v;
            Warn(m.mod, $"{m.where}: \"counter\" \"{m.counter}\": {error}");
            return null;
        }

        static bool Met(Medal m, Grade g, double? count)
        {
            if (g.threshold == null) return ModUnlocks.Holds(m.mod, g.condition, m.where);
            if (count == null) return false;
            double c = count.Value, t = g.threshold.Value;
            return m.strict ? c > t : c >= t && (t > 0 || c > 0);
        }

        /// <summary>Achievements.Check on docking: each available medal's best grade met now; the ones that went up (combined
        /// numbers), their unlocks given (blueprints learnt, items into the hold). Blueprints of medals already earned are
        /// learnt too (a mod that added one later).</summary>
        public static List<int> Check(Database db)
        {
            Build();
            var improved = new List<int>();
            if (!Achievements.Enabled) return improved;   // multiplayer, a game without medals
            for (int k = 0; k < all.Count; k++)
            {
                var m = all[k];
                var had = Earned(m);
                if (Available(m))
                {
                    double? count = m.counter != null ? Count(m) : null;
                    Tier best = null;
                    for (int g = m.grades.Count - 1; g >= 0 && best == null; g--)
                        if (Met(m, m.grades[g], count)) best = m.grades[g].tier;
                    if (best != null && (had == null || best.rank > had.rank))
                    {
                        Session.ModMedals[m.key] = best.id;
                        improved.Add(Achievements.Count + k);
                        Give(db, Achievements.Count + k, m, had?.rank ?? float.NegativeInfinity, best.rank);
                        had = best;
                    }
                }
                if (had != null)
                    foreach (var u in m.unlocks)
                        if (u.blueprint >= 0 && u.tier.rank <= had.rank && !Blueprints.IsUnlocked(u.blueprint)) Blueprints.Unlock(u.blueprint);
            }
            return improved;
        }

        /// <summary>The unlocks of the tiers just passed (above 'from', up to 'to'), with a line each for the toast.</summary>
        static void Give(Database db, int medal, Medal m, float from, float to)
        {
            foreach (var u in m.unlocks)
            {
                if (u.tier.rank <= from || u.tier.rank > to) continue;
                string line = null;
                if (u.blueprint >= 0 && !Blueprints.IsUnlocked(u.blueprint))
                {
                    Blueprints.Unlock(u.blueprint);
                    line = string.Format(Localization.Extra("medalUnlockBlueprint", "Blueprint: {0}"), GameNames.Item(u.blueprint));
                }
                else if (u.item >= 0 && db?.Item(u.item) != null)
                {
                    int room = Mathf.Clamp(Shop.FreeCargo(db), 0, u.amount);
                    if (room > 0) Shop.AddToCargo(u.item, room);
                    // What the hold has no room for is paid at the item's average price.
                    var it = db.Item(u.item);
                    int paid = (u.amount - room) * Mathf.Max(0, (it.minPrice + it.maxPrice) / 2);
                    if (paid > 0) Session.Credits += paid;
                    line = string.Format(Localization.Extra("medalUnlockItem", "{0} x {1}"), u.amount, GameNames.Item(u.item))
                         + (paid > 0 ? " " + string.Format(Localization.Extra("medalUnlockItemPaid", "(no room in the hold: +{0:N0} $)"), paid) : "");
                }
                else if (u.ship >= 0)
                    line = string.Format(Localization.Extra("medalUnlockShip", "On sale: {0}"), GameNames.Ship(u.ship));
                if (line == null) continue;
                if (!notes.TryGetValue(medal, out var l)) notes[medal] = l = new List<string>();
                l.Add(line);
            }
        }

        /// <summary>The lines of what the medal's new grade unlocked (the toast shows them once).</summary>
        public static List<string> TakeNotes(int medal)
        {
            if (!notes.TryGetValue(medal, out var l)) return null;
            notes.Remove(medal);
            return l;
        }

        /// <summary>Shop.GenerateShips (and the docking that earned the medal): the ships the earned medals put on sale at this
        /// station, first in the list; without "stations" at every dealer that has ships.</summary>
        public static void AddShips(int station, List<int> ships)
        {
            if (ships == null || !Achievements.Enabled) return;
            Build();
            bool dealer = ships.Count > 0;
            foreach (var m in all)
            {
                var tier = Earned(m);
                if (tier == null) continue;
                foreach (var u in m.unlocks)
                {
                    if (u.ship < 0 || u.tier.rank > tier.rank || ships.Contains(u.ship)) continue;
                    if (u.stations != null ? !u.stations.Contains(station) : !dealer) continue;
                    if (!ModCampaigns.ShipAllowed(u.ship) || !ModUnlocks.ShipAvailable(u.ship)) continue;
                    ships.Insert(0, u.ship);
                }
            }
        }

        /// <summary>The credits the medal's earned grade pays: its "reward" for the tier, else for every grade, else the
        /// tier's (the game's 1000 / 2500 / 5000).</summary>
        public static int Reward(Medal m)
        {
            var tier = Earned(m);
            if (tier == null) return 0;
            return m.rewards.TryGetValue(tier.id, out int r) ? r : m.reward >= 0 ? m.reward : tier.reward;
        }

        /// <summary>The grade the medal earns next and the counter toward it (the Status window's bar); false at its top
        /// grade or when the next grade is a condition.</summary>
        public static bool Next(Medal m, out Tier tier, out long count, out long target, out float fraction)
        {
            tier = null;
            count = target = 0;
            fraction = 0f;
            var had = Earned(m);
            var next = m.grades.Find(g => had == null || g.tier.rank > had.rank);
            if (next == null) return false;
            tier = next.tier;
            if (next.threshold == null || next.threshold.Value <= 1) return false;
            var c = Count(m);
            if (c == null) return false;
            target = (long)Math.Ceiling(next.threshold.Value);
            count = Math.Max(0, (long)Math.Floor(c.Value));
            fraction = Mathf.Clamp01((float)(c.Value / next.threshold.Value));
            if (m.strict && c.Value <= next.threshold.Value) fraction = Mathf.Min(fraction, 0.99f);
            return true;
        }

        /// <summary>The Status window's hint: the medal's text ("#" = the threshold of the grade earned, else of the next),
        /// the progress line, then its grades in a row ("Bronze 10 · Silver 50 · Gold 100").</summary>
        public static string Hint(Medal m, string progress)
        {
            var had = Earned(m);
            var shown = had != null ? m.grades.FindLast(g => g.tier.rank <= had.rank) ?? m.grades[0] : m.grades[0];
            string text = m.HintText;
            if (shown?.threshold != null) text = text.Replace("#", shown.threshold.Value.ToString("#,0.##", CultureInfo.CurrentCulture));
            if (progress != null) text += "\n" + progress;
            var parts = new List<string>();
            foreach (var g in m.grades)
                parts.Add(g.threshold != null ? $"{g.tier.Name} {g.threshold.Value.ToString("#,0.##", CultureInfo.CurrentCulture)}" : g.tier.Name);
            if (parts.Count > 1 || m.grades[0].threshold != null) text += "\n" + string.Join(" · ", parts);
            return text.Trim('\n');
        }

        /// <summary>The medal's symbol: its own image, or a game medal's (GoF2Hud/medal_NN), null = medal_00.</summary>
        public static Texture2D Icon(Medal m) => string.IsNullOrEmpty(m.icon) ? null : ModMaterials.Texture(m.mod, m.icon, false);

        /// <summary>A tier's own plate image (a mod's), null = one of the game's plates.</summary>
        public static Texture2D PlateImage(Tier t) =>
            t.mod != null && t.plate is not ("bronze" or "silver" or "gold" or "supernova") ? ModMaterials.Texture(t.mod, t.plate, false) : null;

        // ---- conditions ----------------------------------------------------------------------------------------------

        /// <summary>A game tier's rank for a game medal's grade (1 gold, 2 silver, 3 bronze; an elite medal's gold is
        /// supernova), 0 = none.</summary>
        static float GameRank(int medal, int grade) =>
            grade == 0 ? 0 : medal >= Achievements.BaseCount ? 4 : grade == 1 ? 3 : grade == 2 ? 2 : 1;

        /// <summary>The conditions' medal(&lt;medal&gt;[, &lt;tier&gt;]): 1 when the medal is earned (at that tier or a higher one);
        /// a game medal by number (0-44), a mod's by key "mod_id:id".</summary>
        public static int Reached(string arg, out string error)
        {
            error = null;
            string reference = arg.Trim(), tierId = null;
            int comma = reference.LastIndexOf(',');
            if (comma >= 0) { tierId = reference.Substring(comma + 1).Trim(); reference = reference.Substring(0, comma).Trim(); }
            float need = 0;
            if (tierId != null)
            {
                var t = TierOf(tierId);
                if (t == null) { error = $"medal({arg}): no tier \"{tierId}\" (bronze, silver, gold, supernova or a mod's)."; return 0; }
                need = t.rank;
            }
            float have;
            if (int.TryParse(reference, NumberStyles.Integer, CultureInfo.InvariantCulture, out int game))
            {
                if (game < 0 || game >= Achievements.Count) { error = $"medal({arg}): the game's medals are 0-{Achievements.Count - 1}."; return 0; }
                have = GameRank(game, Achievements.Grade(game));
            }
            else
            {
                if (!reference.Contains(":")) { error = $"medal({arg}): a game medal's number or a mod's \"mod_id:id\"."; return 0; }
                // A medal of a mod that is off still counts when earned (its key is kept).
                var m = Find(reference);
                if (m != null) have = Earned(m)?.rank ?? 0;
                else have = Session.ModMedals.TryGetValue(reference, out string id) ? TierOf(id)?.rank ?? 1 : 0;
            }
            return have > 0 && have >= need ? 1 : 0;
        }

        /// <summary>The mods' medals earned at exactly this tier (the conditions' medals(tier)); "any" counts all.</summary>
        public static int CountAt(string tierId)
        {
            Build();
            int n = 0;
            foreach (var m in all)
            {
                var t = Earned(m);
                if (t != null && (tierId == "any" || string.Equals(t.id, tierId, StringComparison.OrdinalIgnoreCase)
                                  || (t == GameTiers[3] && string.Equals(tierId, "elite", StringComparison.OrdinalIgnoreCase)))) n++;
            }
            return n;
        }

        /// <summary>New Game+: the old game's mod medals, each at the better tier of the two.</summary>
        public static void Carry(List<string> old)
        {
            if (old == null || Session.NoMedals) return;
            foreach (var e in old)
            {
                int bar = e?.LastIndexOf('|') ?? -1;
                if (bar <= 0 || bar >= e.Length - 1) continue;
                string key = e.Substring(0, bar), id = e.Substring(bar + 1);
                float have = Session.ModMedals.TryGetValue(key, out string mine) ? TierOf(mine)?.rank ?? 1 : 0;
                if ((TierOf(id)?.rank ?? 1) > have) Session.ModMedals[key] = id;
            }
        }
    }
}
