// ShipClassRules.cs
// Remake mods: ship classes and the equipment that only some classes may mount. Optional everywhere: no original ship has a
// class and no original item a list, so without a mod using them nothing changes (AI_PROJECT_CONTEXT.md 6.1 / 6.2).
//   ships.json (a new ship or an "override" of an original one):
//       "shipClass": "light_fighter"            the ship's class, a free word the mod picks (what a class scanner shows)
//       "classTags": ["small", "mymod:agile"]   more free words the ship answers to (a mod's own, or shared with other mods)
//   items.json (a new item or an "override"):
//       "allowedClasses":  ["frigate", "capital"]   only a ship whose class or tags name one of these may mount it
//       "excludedClasses": ["small"]                a ship whose class or tags name one of these may not
//   Docking role tags (ships.json "classTags"): "fighter", "interceptor" or "bomber" = internal hangar only;
//       "frigate", "cruiser" or "carrier" = external station attachment only. Mod-specific class ids can keep their own
//       names and include one of these shared role tags. External docking also requires a slotType 8 ship hardpoint and a
//       station's manually authored orbit.externalDocks point. Stations may offer both routes: their normal hangar and
//       externalDocks are independent. Unclassified small ships retain legacy internal docking; unclassified ships cannot
//       use external docking. A ship with conflicting internal and external role tags is not dockable until corrected.
//       "className": "Light fighter"            optional display text for the class (a word or { "en": ..., "de": ... }, or the key
//                                               "shipclass.<id>.name" in text/<lang>.json); without it the id is shown capitalised
//   A ship without a class or tags is **unclassified** (every original ship unless a mod labels it): it takes every item, even
//   one with an allow-list, so the lists only engage on ships a mod classified (owner's decision, AI_PROJECT_CONTEXT.md 6.2).
//   For a classified ship: an item with neither list fits; an exclude-list blocks the ships it names; an allow-list refuses a
//   ship that matches none of it; with both a ship must match the allow-list and not the exclude-list. Words compare without
//   regard to case, as classes and tags are free-form. The class id is the machine identity (saves, network, scanners' logic),
//   the display text is only shown.
//   Hangar.CanMount refuses a misfit, a ship swap (Hangar.SwitchTo) and docking (Hangar.FitToSlots) put the misfits in the hold.
// ItemStats "scanClass" (attribute 143 = 1): a scanner that shows the locked ship's class on its lock plate (CombatView).

using System;
using System.Collections.Generic;
using GoF2Remake.Data;
using Newtonsoft.Json.Linq;

namespace GoF2Remake.Modding
{
    public static class ShipClassRules
    {
        /// <summary>Item attribute 143 (stats "scanClass"): 1 = a scanner that shows a locked ship's class on the plate.</summary>
        public const int ScanClassAttr = 143;

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

        /// <summary>Did a mod give the ship a class or tags? An unclassified ship (every original one) is unrestricted.</summary>
        public static bool IsClassified(ShipData ship)
        {
            foreach (var _ in Labels(ship)) return true;
            return false;
        }

        /// <summary>May this ship mount the item? True for any item without a class list (the original game's, every one) and
        /// for an unclassified ship, whatever the lists say.</summary>
        public static bool Fits(ItemData item, ShipData ship)
        {
            if (item == null) return true;
            var allow = item.allowedClasses;
            var deny = item.excludedClasses;
            bool hasAllow = allow != null && allow.Length > 0, hasDeny = deny != null && deny.Length > 0;
            if (!hasAllow && !hasDeny) return true;   // the common case: nothing to check
            if (!IsClassified(ship)) return true;     // unclassified: the lists don't engage
            if (hasDeny && Matches(ship, deny)) return false;
            return !hasAllow || Matches(ship, allow);
        }

        // Docking roles use the same class ids / tags as equipment restrictions. The class ids are intentionally
        // free-form; these conventional role tags let modders keep their own class names while declaring the docking role.
        static readonly string[] InternalDockClasses = { "fighter", "interceptor", "bomber" };
        static readonly string[] ExternalDockClasses = { "frigate", "cruiser", "carrier" };

        public static bool IsInternalDockClass(ShipData ship) =>
            ShipClassCatalog.DockingRoleOf(ship?.shipClass) == ShipDockingRole.InternalOnly || Matches(ship, InternalDockClasses);

        public static bool IsExternalDockClass(ShipData ship) =>
            ShipClassCatalog.DockingRoleOf(ship?.shipClass) == ShipDockingRole.ExternalOnly || Matches(ship, ExternalDockClasses);

        /// <summary>
        /// Internal hangars accept fighter / interceptor / bomber classes. For backward compatibility, an otherwise
        /// unclassified or custom small player hull may still use an internal hangar; an external-only class may not.
        /// Conflicting internal + external role tags are rejected rather than guessing.
        /// </summary>
        public static bool CanDockInternally(ShipData ship, bool largeHull)
        {
            bool internalRole = IsInternalDockClass(ship), externalRole = IsExternalDockClass(ship);
            if (internalRole && externalRole) return false;
            if (externalRole) return false;
            return !largeHull;
        }

        /// <summary>External docking is reserved for frigate / cruiser / carrier roles; an unclassified ship cannot use it.</summary>
        public static bool CanDockExternally(ShipData ship) => IsExternalDockClass(ship) && !IsInternalDockClass(ship);

        /// <summary>The ship's class id (the machine identity), null for a ship without one (tags alone are not a class).</summary>
        public static string ClassOf(ShipData ship) => string.IsNullOrEmpty(ship?.shipClass) ? null : ship.shipClass;

        /// <summary>The class as a scanner shows it: the mod's display text, else the id with its words capitalised
        /// ("light_fighter" = "Light Fighter"); null for a ship without a class.</summary>
        public static string ClassLabel(ShipData ship)
        {
            string id = ClassOf(ship);
            if (id == null) return null;
            return string.IsNullOrEmpty(ship.shipClassLabel)
                ? ShipClassCatalog.Get(id)?.DisplayName ?? Humanize(id)
                : ship.shipClassLabel;
        }

        /// <summary>"light_fighter" / "mymod:light-fighter" -> "Light Fighter" (a mod prefix is left off).</summary>
        public static string Humanize(string id)
        {
            if (string.IsNullOrEmpty(id)) return "";
            int colon = id.LastIndexOf(':');
            if (colon >= 0 && colon < id.Length - 1) id = id.Substring(colon + 1);
            var words = id.Split(new[] { '_', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < words.Length; i++) words[i] = char.ToUpperInvariant(words[i][0]) + words[i].Substring(1);
            return string.Join(" ", words);
        }

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
        public static void ApplyShip(ShipData s, JObject o, ModInfo mod = null)
        {
            if (ModJson.Get(o, "shipClass") is JToken c && c.Type == JTokenType.String)
            {
                s.shipClass = ShipClassCatalog.CanonicalId(string.IsNullOrWhiteSpace((string)c) ? null : ((string)c).Trim());
                if (s.shipClass == null) s.shipClassLabel = null;
            }
            if (s.shipClass != null && (ModJson.Has(o, "className") || mod != null && ModJson.Has(o, "shipClass")))
            {
                var inline = ModJson.Has(o, "className") ? ModJson.Text(o, "className") : null;
                s.shipClassLabel = mod != null ? ModContent.TranslatedText(mod, $"shipclass.{s.shipClass}.name", inline)
                                               : FirstValue(inline);
            }
            if (ModJson.Has(o, "classTags"))
            {
                var tags = Words(o, "classTags");
                s.classTags = tags.Length > 0 ? tags : null;
            }
        }

        static string FirstValue(Dictionary<string, string> values)
        {
            if (values == null) return null;
            foreach (var value in values.Values) return value;
            return null;
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
