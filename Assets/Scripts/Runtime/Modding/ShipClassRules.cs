// ShipClassRules.cs
// Remake mods: ship classes and the equipment that only some classes may mount. Optional everywhere: no original ship has a
// class and no original item a list, so without a mod using them nothing changes (AI_PROJECT_CONTEXT.md 6.1 / 6.2).
//   ships.json (a new ship or an "override" of an original one):
//       "shipClass": "light_fighter"            the ship's class, a free word the mod picks (what a class scanner shows)
//       "classTags": ["small", "mymod:agile"]   more free words the ship answers to (a mod's own, or shared with other mods)
//   items.json (a new item or an "override"):
//       "allowedClasses":  ["frigate", "capital"]   only a ship whose class or tags name one of these may mount it
//       "excludedClasses": ["small"]                a ship whose class or tags name one of these may not
//   An item with neither list fits every ship; an exclude-list only blocks what it names (a ship without a class still fits);
//   an allow-list refuses a ship that matches none of it (so a ship without a class). With both, a ship must match the
//   allow-list and not the exclude-list. Words compare without regard to case, as classes and tags are free-form.
//   Hangar.CanMount refuses a misfit, a ship swap (Hangar.SwitchTo) and docking (Hangar.FitToSlots) put the misfits in the hold.
// ItemStats "scansShipClass" (attribute 175 = 1): a scanner that shows the locked ship's class (Flight.CombatRadar).

using System;
using System.Collections.Generic;
using GoF2Remake.Data;
using Newtonsoft.Json.Linq;

namespace GoF2Remake.Modding
{
    public static class ShipClassRules
    {
        /// <summary>Item attribute 175 (stats "scansShipClass"): 1 = a scanner that shows a locked ship's class.</summary>
        public const int ScansClassAttr = 175;

        /// <summary>The words a ship answers to: its class, then its tags (none for a ship without either).</summary>
        public static IEnumerable<string> Labels(ShipData ship)
        {
            if (ship == null) yield break;
            if (!string.IsNullOrEmpty(ship.shipClass)) yield return ship.shipClass;
            if (ship.classTags != null)
                foreach (var t in ship.classTags) if (!string.IsNullOrEmpty(t)) yield return t;
        }

        /// <summary>Does the ship answer to one of the words (its class or a tag)?</summary>
        public static bool Matches(ShipData ship, string[] words)
        {
            if (words == null || words.Length == 0) return false;
            foreach (var label in Labels(ship))
                foreach (var w in words)
                    if (string.Equals(label, w, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>May this ship mount the item? True for any item without a class list (the original game's, every one).</summary>
        public static bool Fits(ItemData item, ShipData ship)
        {
            if (item == null) return true;
            var allow = item.allowedClasses;
            var deny = item.excludedClasses;
            bool hasAllow = allow != null && allow.Length > 0, hasDeny = deny != null && deny.Length > 0;
            if (!hasAllow && !hasDeny) return true;   // the common case: nothing to check
            if (hasDeny && Matches(ship, deny)) return false;
            return !hasAllow || Matches(ship, allow);
        }

        /// <summary>The ship's class as a scanner shows it, null for a ship without one.</summary>
        public static string ClassOf(ShipData ship) => string.IsNullOrEmpty(ship?.shipClass) ? null : ship.shipClass;

        // ---- ships.json / items.json -------------------------------------------------------------------------------------

        /// <summary>ModContent.ParseShips: the fields are readable (errors with file and line).</summary>
        public static void CheckShip(JObject o, string where, ModInfo mod, string file)
        {
            if (ModJson.Get(o, "shipClass") is JToken c && c.Type != JTokenType.String)
                throw new ModJsonException($"{ModJson.Where(c, file)}: \"shipClass\" must be a word, e.g. \"light_fighter\"");
            CheckWords(o, "classTags", where, mod, file);
        }

        /// <summary>ModContent.ParseItems: the lists are readable.</summary>
        public static void CheckItem(JObject o, string where, ModInfo mod, string file)
        {
            CheckWords(o, "allowedClasses", where, mod, file);
            CheckWords(o, "excludedClasses", where, mod, file);
        }

        static void CheckWords(JObject o, string key, string where, ModInfo mod, string file)
        {
            var t = ModJson.Get(o, key);
            if (t == null) return;
            if (t is JArray list)
            {
                foreach (var e in list)
                    if (e.Type != JTokenType.String) throw new ModJsonException($"{ModJson.Where(e, file)}: \"{key}\" must be a list of words [ \"small\", \"mymod:agile\" ]");
            }
            else if (t.Type != JTokenType.String) throw new ModJsonException($"{ModJson.Where(t, file)}: \"{key}\" must be a word or a list of words [ \"small\", \"mymod:agile\" ]");
            if (Words(o, key).Length == 0 && mod != null) mod.Warnings.Add($"{where}: \"{key}\" has no words (ignored)");
        }

        /// <summary>The trimmed, non-empty words of a field (a word or a list); empty without the field.</summary>
        static string[] Words(JObject o, string key)
        {
            var words = new List<string>();
            foreach (var w in ModJson.Strings(o, key))
                if (!string.IsNullOrWhiteSpace(w)) words.Add(w.Trim());
            return words.ToArray();
        }

        /// <summary>ModContent.ApplyShips: the fields onto the ship (a new ship or an override; an override without them keeps
        /// what an earlier mod gave).</summary>
        public static void ApplyShip(ShipData s, JObject o)
        {
            if (ModJson.Get(o, "shipClass") is JToken c && c.Type == JTokenType.String)
                s.shipClass = string.IsNullOrWhiteSpace((string)c) ? null : ((string)c).Trim();
            if (ModJson.Has(o, "classTags"))
            {
                var tags = Words(o, "classTags");
                s.classTags = tags.Length > 0 ? tags : null;
            }
        }

        /// <summary>ModContent.ApplyFields: the lists onto the item (a new item or an override; an override without them keeps
        /// what an earlier mod gave, an empty list lifts the restriction).</summary>
        public static void ApplyItem(ItemData it, JObject o)
        {
            if (ModJson.Has(o, "allowedClasses")) { var w = Words(o, "allowedClasses"); it.allowedClasses = w.Length > 0 ? w : null; }
            if (ModJson.Has(o, "excludedClasses")) { var w = Words(o, "excludedClasses"); it.excludedClasses = w.Length > 0 ? w : null; }
        }
    }
}
