// NetAdminArguments.cs
// NetAdmin: argument parsing (part of the partial NetAdmin; its fields are in NetAdmin.cs).

using System;
using System.Globalization;
using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.World;
using UnityEngine;
using GoF2Remake.Events;

namespace GoF2Remake.Multiplayer
{
    public static partial class NetAdmin
    {
        // ---- argument parsing --------------------------------------------------------------------------------
        static string Usage(string command) => NetCommands.UsageOf(command);
        /// <summary>Takes a trailing "at x y z" (game coordinates) off 'rest': 'at' = "x y z" (invariant), null without one;
        /// false = an "at" without three finite numbers.</summary>
        /// <summary>/spawn's "named &lt;name&gt;": the words after it (up to a trailing "at x y z", which stays in 'rest'), quotes
        /// stripped, cleaned (no rich text, no separator), at most MaxSpawnName characters; null when not given; false =
        /// "named" with no name.</summary>
        static bool TakeName(ref string rest, out string name)
        {
            name = null;
            var words = new List<string>(rest.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
            int i = words.FindIndex(w => string.Equals(w, "named", StringComparison.OrdinalIgnoreCase));
            if (i < 0) return true;
            var tail = words.GetRange(i + 1, words.Count - i - 1);
            var keep = words.GetRange(0, i);
            if (tail.Count >= 4 && string.Equals(tail[tail.Count - 4], "at", StringComparison.OrdinalIgnoreCase)
                && tail.GetRange(tail.Count - 3, 3).TrueForAll(w => float.TryParse(w, NumberStyles.Float, CultureInfo.InvariantCulture, out _)))
            {
                keep.AddRange(tail.GetRange(tail.Count - 4, 4));
                tail.RemoveRange(tail.Count - 4, 4);
            }
            string n = NetChat.Clean(string.Join(" ", tail)).Replace(NameSeparator.ToString(), "").Replace("@", "").Trim().Trim('"', '\'').Trim();
            if (n.Length == 0) return false;
            name = n.Length > MaxSpawnName ? n.Substring(0, MaxSpawnName).TrimEnd() : n;
            rest = string.Join(" ", keep);
            return true;
        }
        /// <summary>An order's text and the /spawn name after its NameSeparator (null when none).</summary>
        static string SplitName(string text, out string name)
        {
            name = null;
            if (string.IsNullOrEmpty(text)) return text;
            int i = text.IndexOf(NameSeparator);
            if (i < 0) return text;
            name = text.Substring(i + 1).Trim();
            if (name.Length == 0) name = null;
            else if (name.Length > MaxSpawnName) name = name.Substring(0, MaxSpawnName);
            return text.Substring(0, i);
        }
        /// <summary>/spawn's "turrets 48,182": turret items (category 8 / 35, a number, "mod:id" or a one-word name) for the
        /// ships' turret mounts; null when not given; false (with the reason when an item is wrong) = "turrets" with nothing
        /// or something else after it.</summary>
        static bool TakeTurrets(ref string rest, out List<int> turrets, out string error)
        {
            turrets = null;
            error = null;
            var words = new List<string>(rest.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
            int i = words.FindIndex(w => string.Equals(w, "turrets", StringComparison.OrdinalIgnoreCase) || string.Equals(w, "turret", StringComparison.OrdinalIgnoreCase));
            if (i < 0) return true;
            if (i + 1 >= words.Count) return false;
            turrets = new List<int>();
            foreach (var part in words[i + 1].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int item = FindItem(part, out error);
                if (item < 0) return false;
                var it = NetGame.Db.Item(item);
                if (it == null || (it.categoryId != 8 && it.categoryId != 35))
                {
                    error = string.Format(X("mpAdmNoTurret", "{0} is no turret."), UI.ItemInfo.ItemName(item));
                    return false;
                }
                if (turrets.Count < MaxSpawnTurrets) turrets.Add(item);
            }
            words.RemoveRange(i, 2);
            rest = string.Join(" ", words);
            return turrets.Count > 0;
        }
        /// <summary>The order text's turret items (TakeTurrets) taken off 'text' (the "at" part); null = none.</summary>
        static List<int> SplitTurrets(ref string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            int i = text.IndexOf(TurretSeparator);
            if (i < 0) return null;
            var list = new List<int>();
            foreach (var part in text.Substring(i + 1).Split(','))
                if (int.TryParse(part, out int item) && NetGuard.Item(item) && list.Count < MaxSpawnTurrets
                    && NetGame.Db.Item(item) is ItemData it && (it.categoryId == 8 || it.categoryId == 35)) list.Add(item);
            text = text.Substring(0, i);
            return list.Count > 0 ? list : null;
        }
        /// <summary>/spawn's "&lt;word&gt; 2.5" (hull, guns): the factor (0.1..MaxSpawnFactor) taken off 'rest', 1 without one;
        /// false = the word without a number after it.</summary>
        static bool TakeFactor(ref string rest, string word, out float factor)
        {
            factor = 1f;
            var words = new List<string>(rest.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
            int i = words.Count > 1 ? words.FindIndex(1, w => string.Equals(w, word, StringComparison.OrdinalIgnoreCase)) : -1;
            if (i < 0) return true;
            if (i + 1 >= words.Count) return false;
            string n = words[i + 1].TrimStart('x', 'X');
            if (!float.TryParse(n, NumberStyles.Float, CultureInfo.InvariantCulture, out factor) || !NetGuard.Finite(factor) || factor <= 0f) return false;
            factor = Mathf.Clamp(factor, 0.1f, MaxSpawnFactor);
            words.RemoveRange(i, 2);
            rest = string.Join(" ", words);
            return true;
        }
        /// <summary>The order text's hull and gun factors (TakeFactor) taken off 'text' (the "at" part); 1 = unchanged.</summary>
        static (float hull, float guns) SplitFactors(ref string text)
        {
            if (string.IsNullOrEmpty(text)) return (1f, 1f);
            int i = text.IndexOf(FactorSeparator);
            if (i < 0) return (1f, 1f);
            var parts = text.Substring(i + 1).Split(',');
            text = text.Substring(0, i);
            float F(int k) => parts.Length > k && float.TryParse(parts[k], NumberStyles.Float, CultureInfo.InvariantCulture, out float f) && NetGuard.Finite(f)
                ? Mathf.Clamp(f, 0.1f, MaxSpawnFactor) : 1f;
            return (F(0), F(1));
        }
        static bool TakeAt(ref string rest, out string at)
        {
            at = null;
            var words = new List<string>(rest.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
            int i = words.FindLastIndex(w => string.Equals(w, "at", StringComparison.OrdinalIgnoreCase));
            if (i < 0) return true;
            if (i != words.Count - 4) return false;
            var v = new float[3];
            for (int k = 0; k < 3; k++)
                if (!float.TryParse(words[i + 1 + k], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v[k])
                    || !NetGuard.Finite(v[k]) || Mathf.Abs(v[k]) > 2e7f) return false;
            at = string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0} {1} {2}", v[0], v[1], v[2]);
            rest = string.Join(" ", words.GetRange(0, i));
            return true;
        }
        /// <summary>"x y z" game coordinates (TakeAt) as a Unity position; null when empty or broken.</summary>
        static Vector3? ParseAt(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            var w = text.Split(' ');
            if (w.Length != 3) return null;
            var v = new float[3];
            for (int k = 0; k < 3; k++)
                if (!float.TryParse(w[k], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v[k]) || !NetGuard.Finite(v[k]))
                    return null;
            return OrbitLayout.ToUnity(new Vector3(v[0], v[1], v[2]));
        }
        /// <summary>A trailing whole number (with more before it) is the amount, else 'fallback'; false = nothing left or a
        /// number below 1.</summary>
        static bool ParseAmount(ref string rest, int fallback, int max, out int amount)
        {
            amount = fallback;
            rest = rest.Trim();
            int space = rest.LastIndexOf(' ');
            if (space > 0 && int.TryParse(rest.Substring(space + 1), out int n))
            {
                if (n < 1) return false;
                amount = Mathf.Min(n, max);
                rest = rest.Substring(0, space).Trim();
            }
            return rest.Length > 0;
        }
        /// <summary>An item by its index, its whole name or the one name it starts (any case); -1 with the reason.</summary>
        internal static int FindItem(string spec, out string error)
        {
            error = null;
            var db = NetGame.Db;
            if (int.TryParse(spec, out int index) && NetGuard.Item(index)) return index;
            // A mod's item by its key ("mod:id"), the same whatever number it got here (a story mod using another mod's items).
            if (spec.IndexOf(':') > 0 && Modding.ModContent.TryResolveItem(spec, out int modItem) && NetGuard.Item(modItem)) return modItem;
            int found = -1, matches = 0;
            foreach (var it in db.Items)
            {
                string n = UI.ItemInfo.ItemName(it.index);
                if (string.IsNullOrEmpty(n)) n = it.name ?? "";
                if (string.Equals(n, spec, StringComparison.OrdinalIgnoreCase)) return it.index;
                if (n.StartsWith(spec, StringComparison.OrdinalIgnoreCase)) { found = it.index; matches++; }
            }
            if (matches == 1) return found;
            error = matches > 1 ? string.Format(X("mpAdmItemAmbiguous", "\"{0}\" fits {1} items: write more of the name, or its number."), spec, matches)
                                : string.Format(X("mpAdmNoItem", "No item \"{0}\"."), spec);
            return -1;
        }
        /// <summary>An assembled object (assemblies.json) by its whole name or the one name it starts (any case); null with
        /// the reason.</summary>
        internal static AssemblyData FindAssembly(string spec, out string error)
        {
            error = null;
            var all = NetGame.Db.Assemblies;
            var asm = all.Find(a => string.Equals(a.name, spec, StringComparison.OrdinalIgnoreCase));
            if (asm != null) return asm;
            var starts = all.FindAll(a => a.name.StartsWith(spec, StringComparison.OrdinalIgnoreCase));
            if (starts.Count == 1) return starts[0];
            error = starts.Count > 1
                ? string.Format(X("mpAdmObjectAmbiguous", "\"{0}\" fits {1} objects (e.g. {2}): write more of the name."), spec, starts.Count, starts[0].name)
                : string.Format(X("mpAdmNoShipOrObject", "No ship or object \"{0}\" (a ship's number or name, or an assemblies.json name like station_083_terran)."), spec);
            return null;
        }
        /// <summary>A ship of ships.json by its index, its whole name or the one name it starts; -1 with the reason (-2: the
        /// name fits several ships).</summary>
        internal static int FindShip(string spec, out string error)
        {
            error = null;
            var db = NetGame.Db;
            if (int.TryParse(spec, out int index) && db.Ship(index) != null && CustomShips.Offered(index)) return index;
            // A mod's ship by its key ("mod:id"), the same whatever number it got here.
            if (spec.IndexOf(':') > 0 && Modding.ModContent.TryResolveShip(spec, out int modShip) && db.Ship(modShip) != null && CustomShips.Offered(modShip)) return modShip;
            int found = -1, matches = 0;
            foreach (var s in db.Ships)
            {
                if (!CustomShips.Offered(s.index)) continue;   // custom ships: not in multiplayer for now
                string n = DebugSpawner.ShipName(db, s.index);
                if (string.Equals(n, spec, StringComparison.OrdinalIgnoreCase)) return s.index;
                if (spec.Length > 0 && n.StartsWith(spec, StringComparison.OrdinalIgnoreCase)) { found = s.index; matches++; }
            }
            if (matches == 1) return found;
            error = matches > 1 ? string.Format(X("mpAdmShipAmbiguous", "\"{0}\" fits {1} ships: write more of the name, or its number."), spec, matches)
                                : string.Format(X("mpAdmNoShip", "No ship \"{0}\"."), spec);
            return matches > 1 ? -2 : -1;
        }
        internal static string HullName(PlayerHull.Hull h)
        {
            int i = h.label.IndexOf(" · ");
            return i >= 0 ? h.label.Substring(i + 3) : h.label;
        }
        /// <summary>A hull of the debug Ships tab by its ship number, its key, its whole name or the one name it starts; null
        /// with the reason.</summary>
        internal static PlayerHull.Hull FindHull(string spec, out string error)
        {
            error = null;
            var all = PlayerHull.Offered(NetGame.Db);
            // A mod's ship by its key ("mod:id"): its number here.
            if (spec.IndexOf(':') > 0 && Modding.ModContent.TryResolveShip(spec, out int modShip)) spec = modShip.ToString();
            if (int.TryParse(spec, out int index))
            {
                var byIndex = all.Find(h => h.key == "ship_" + index) ?? all.Find(h => h.stats == index);
                if (byIndex != null) return byIndex;
            }
            PlayerHull.Hull found = null;
            int matches = 0;
            foreach (var h in all)
            {
                string n = HullName(h);
                if (string.Equals(n, spec, StringComparison.OrdinalIgnoreCase) || string.Equals(h.key, spec, StringComparison.OrdinalIgnoreCase)) return h;
                if (n.StartsWith(spec, StringComparison.OrdinalIgnoreCase)) { found = h; matches++; }
            }
            if (matches == 1) return found;
            error = matches > 1 ? string.Format(X("mpAdmShipAmbiguous", "\"{0}\" fits {1} ships: write more of the name, or its number."), spec, matches)
                                : string.Format(X("mpAdmNoShip", "No ship \"{0}\"."), spec);
            return null;
        }
        static string FlagList()
        {
            var words = new List<string>();
            foreach (var f in Cheats.Flags) words.Add(f.word);
            return string.Join(", ", words);
        }
        static DebugSpawner.Behaviour? BehaviourWord(string w)
        {
            switch (w.ToLowerInvariant())
            {
                case "enemy": case "hostile": return DebugSpawner.Behaviour.Hostile;
                case "friendly": case "friend": return DebugSpawner.Behaviour.Friendly;
                case "neutral": return DebugSpawner.Behaviour.Neutral;
                case "standing": return DebugSpawner.Behaviour.Normal;
            }
            return null;
        }
        internal static int RaceWord(string w) => Standing.RaceWord(w);
    }
}
