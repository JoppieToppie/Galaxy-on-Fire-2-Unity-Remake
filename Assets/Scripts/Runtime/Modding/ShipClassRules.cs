// ShipClassRules.cs
// Class assignments and docking roles are opt-in in ships.json. The original ships remain unclassified unless a mod assigns
// them. External docking always also requires a slotType 8 ship hardpoint and a station orbit.externalDocks attachment.

using System;
using System.Collections.Generic;
using GoF2Remake.Data;
using Newtonsoft.Json.Linq;

namespace GoF2Remake.Modding
{
    public static class ShipClassRules
    {
        static readonly string[] InternalDockClasses = { "fighter", "interceptor", "bomber" };
        static readonly string[] ExternalDockClasses = { "corvette", "frigate", "freighter", "cruiser", "carrier" };

        /// <summary>The words a ship answers to: its class, then its tags.</summary>
        static IEnumerable<string> Labels(ShipData ship)
        {
            if (ship == null) yield break;
            if (!string.IsNullOrEmpty(ship.shipClass)) yield return ship.shipClass;
            if (ship.classTags != null)
                foreach (var tag in ship.classTags)
                    if (!string.IsNullOrEmpty(tag)) yield return tag;
        }

        static bool Matches(ShipData ship, string[] words)
        {
            if (words == null || words.Length == 0) return false;
            foreach (var label in Labels(ship))
                foreach (var word in words)
                    if (string.Equals(label, word, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public static bool IsInternalDockClass(ShipData ship) =>
            ShipClassCatalog.DockingRoleOf(ship?.shipClass) == ShipDockingRole.InternalOnly
            || Matches(ship, InternalDockClasses);

        public static bool IsExternalDockClass(ShipData ship) =>
            ShipClassCatalog.DockingRoleOf(ship?.shipClass) == ShipDockingRole.ExternalOnly
            || Matches(ship, ExternalDockClasses);

        /// <summary>External-only classes cannot fall back into a hangar; conflicting roles are rejected.</summary>
        public static bool CanDockInternally(ShipData ship, bool largeHull)
        {
            bool internalRole = IsInternalDockClass(ship);
            bool externalRole = IsExternalDockClass(ship);
            if (internalRole && externalRole) return false;
            if (externalRole) return false;
            return !largeHull;
        }

        /// <summary>An explicit external class or role tag is required; size and hardpoints alone never grant access.</summary>
        public static bool CanDockExternally(ShipData ship) =>
            IsExternalDockClass(ship) && !IsInternalDockClass(ship);

        /// <summary>ModContent.ParseShips: validate the docking class fields and report their source on errors.</summary>
        public static void CheckShip(JObject o, string where, ModInfo mod, string file)
        {
            if (ModJson.Get(o, "shipClass") is JToken c && c.Type != JTokenType.String)
                throw new ModJsonException($"{ModJson.Where(c, file)}: \"shipClass\" must be a word, e.g. \"light_fighter\"");
            CheckWords(o, "classTags", where, mod, file);
        }

        static void CheckWords(JObject o, string key, string where, ModInfo mod, string file)
        {
            var token = ModJson.Get(o, key);
            if (token == null) return;
            if (token is JArray list)
            {
                foreach (var entry in list)
                    if (entry.Type != JTokenType.String)
                        throw new ModJsonException($"{ModJson.Where(entry, file)}: \"{key}\" must be a list of words");
            }
            else if (token.Type != JTokenType.String)
                throw new ModJsonException($"{ModJson.Where(token, file)}: \"{key}\" must be a word or a list of words");
            if (Words(o, key).Length == 0 && mod != null)
                mod.Warnings.Add($"{where}: \"{key}\" has no words (ignored)");
        }

        static string[] Words(JObject o, string key)
        {
            var words = new List<string>();
            foreach (var word in ModJson.Strings(o, key))
                if (!string.IsNullOrWhiteSpace(word)) words.Add(word.Trim());
            return words.ToArray();
        }

        /// <summary>Apply assignments to a new ship or override; omitted fields preserve earlier values.</summary>
        public static void ApplyShip(ShipData ship, JObject o, ModInfo mod = null)
        {
            if (ModJson.Get(o, "shipClass") is JToken token && token.Type == JTokenType.String)
                ship.shipClass = ShipClassCatalog.CanonicalId(string.IsNullOrWhiteSpace((string)token) ? null : ((string)token).Trim());
            if (ModJson.Has(o, "classTags"))
            {
                var tags = Words(o, "classTags");
                ship.classTags = tags.Length > 0 ? tags : null;
            }
        }
    }
}
