// ShipClassRules.cs
// Remake mods: ship classes and the equipment that only some classes may mount. Optional everywhere: no original ship has a
// class and no original item a list, so without a mod using them nothing changes (AI_PROJECT_CONTEXT.md 6.1 / 6.2).
//   ships.json (a new ship or an "override" of an original one):
//       "shipClass": "light_fighter"            one of the 16 canonical project classes (what a class scanner shows)
//       "classTags": ["small", "mymod:agile"]   more free words the ship answers to (a mod's own, or shared with other mods)
//   items.json (a new item or an "override"):
//       "allowedClasses":  ["frigate", "capital"]   only a ship whose class or tags name one of these may mount it
//       "excludedClasses": ["small"]                a ship whose class or tags name one of these may not
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
        // Canonical project taxonomy. These are available to every mod, but no original hull is assigned
        // automatically; a mod explicitly sets shipClass on each hull it wants classified.
        public const string LightFighter = "light_fighter";
        public const string HeavyFighter = "heavy_fighter";
        public const string LightBomber = "light_bomber";
        public const string HeavyBomber = "heavy_bomber";
        public const string Interceptor = "interceptor";
        public const string StealthBomber = "stealth_bomber";
        public const string StealthFighter = "stealth_fighter";
        public const string Scout = "scout";
        public const string Hauler = "hauler";
        public const string LightPrivateer = "light_privateer";
        public const string Privateer = "privateer";
        public const string HeavyPrivateer = "heavy_privateer";
        public const string Frigate = "frigate";
        public const string Freighter = "freighter";
        public const string Cruiser = "cruiser";
        public const string Carrier = "carrier";

        /// <summary>The 16 canonical project classes, in the agreed taxonomy order.</summary>
        public static readonly string[] CanonicalClassIds =
        {
            LightFighter, HeavyFighter, LightBomber, HeavyBomber, Interceptor, StealthBomber,
            StealthFighter, Scout, Hauler, LightPrivateer, Privateer, HeavyPrivateer,
            Frigate, Freighter, Cruiser, Carrier
        };

        /// <summary>Whether an id is one of the 16 canonical classes. Custom class tags remain valid compatibility labels.</summary>
        public static bool IsCanonicalClass(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return false;
            foreach (var known in CanonicalClassIds)
                if (string.Equals(id, known, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

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
            // Optional ship-specific primary lock; tags use the fully qualified item key.
            if (item.TypeId == 0 && ship?.classTags != null)
            {
                bool locked = false, allowed = false;
                foreach (string tag in ship.classTags)
                {
                    if (tag == null || !tag.StartsWith("primaryOnly:", StringComparison.OrdinalIgnoreCase)) continue;
                    locked = true;
                    if (string.Equals(item.modKey, tag.Substring("primaryOnly:".Length), StringComparison.OrdinalIgnoreCase)) allowed = true;
                }
                if (locked && !allowed) return false;
            }
            var allow = item.allowedClasses;
            var deny = item.excludedClasses;
            bool hasAllow = allow != null && allow.Length > 0, hasDeny = deny != null && deny.Length > 0;
            if (!hasAllow && !hasDeny) return true;   // the common case: nothing to check
            if (!IsClassified(ship)) return item.Attr(ItemStats.StrictShipClassAttr) == 0 || !hasAllow;     // unclassified: the lists don't engage
            if (hasDeny && Matches(ship, deny)) return false;
            return !hasAllow || Matches(ship, allow);
        }

        /// <summary>Optional hardpoint reservations encoded in ship class tags:
        /// secondarySlot:7:mod:item reserves S7 for that item; a reserved item may only use its reserved slots.
        /// S1 is the first player-fittable slot; integrated hull weapons are separate, additional mounts.
        /// Existing untagged ships are unchanged.</summary>
        public static bool HasSecondaryReservations(ShipData ship)
        {
            if (ship?.classTags == null) return false;
            foreach (string tag in ship.classTags)
                if (tag != null && tag.StartsWith("secondarySlot:", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public static bool SecondarySlotFits(ItemData item, ShipData ship, int slot)
        {
            if (item == null || ship?.classTags == null) return true;
            bool reservedItem = false, matchesSlot = false;
            foreach (string tag in ship.classTags)
            {
                const string prefix = "secondarySlot:";
                if (tag == null || !tag.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                int separator = tag.IndexOf(':', prefix.Length);
                if (separator < 0 || !int.TryParse(tag.Substring(prefix.Length, separator - prefix.Length), out int number)) continue;
                bool matches = string.Equals(item.modKey, tag.Substring(separator + 1), StringComparison.OrdinalIgnoreCase);
                if (number == slot && !matches) return false;
                if (matches) { reservedItem = true; if (number == slot) matchesSlot = true; }
            }
            return !reservedItem || matchesSlot;
        }

        /// <summary>The ship's class id (the machine identity), null for a ship without one (tags alone are not a class).</summary>
        public static string ClassOf(ShipData ship) => string.IsNullOrEmpty(ship?.shipClass) ? null : ship.shipClass;

        /// <summary>Canonical large hull roles; tags alone cannot grant capital-only capabilities.</summary>
        public static bool IsLargeShipClass(ShipData ship) => ClassOf(ship)?.ToLowerInvariant() is Frigate or Freighter or Cruiser or Carrier;

        /// <summary>The class as a scanner shows it: the mod's display text, else the id with its words capitalised
        /// ("light_fighter" = "Light Fighter"); null for a ship without a class.</summary>
        public static string ClassLabel(ShipData ship)
        {
            string id = ClassOf(ship);
            if (id == null) return null;
            return string.IsNullOrEmpty(ship.shipClassLabel) ? Humanize(id) : ship.shipClassLabel;
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
            var classToken = ModJson.Get(o, "shipClass");
            if (classToken != null && classToken.Type != JTokenType.String)
                throw new ModJsonException($"{ModJson.Where(classToken, file)}: \"shipClass\" must be one of the 16 canonical class ids, e.g. \"light_fighter\"");
            if (classToken != null && classToken.Type == JTokenType.String &&
                !string.IsNullOrWhiteSpace((string)classToken) && !IsCanonicalClass((string)classToken))
                throw new ModJsonException($"{ModJson.Where(classToken, file)}: unknown ship class \"{(string)classToken}\"; use one of the 16 canonical class ids");
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
                s.shipClass = string.IsNullOrWhiteSpace((string)c) ? null : ((string)c).Trim();
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
