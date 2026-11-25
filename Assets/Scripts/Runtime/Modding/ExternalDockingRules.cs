// ExternalDockingRules.cs
// External docking follows canonical large ship classes. The hardpoint and station attachment are also required.
using System;
using GoF2Remake.Data;
using Newtonsoft.Json.Linq;

namespace GoF2Remake.Modding
{
    public static class ExternalDockingRules
    {
        /// <summary>Only the four canonical large classes may dock externally, including when a legacy role field is absent.</summary>
        public static bool CanDockExternally(ShipData ship) => ShipClassRules.IsLargeShipClass(ship);

        /// <summary>Noncapital roles remain internal only. Legacy hull geometry remains a physical safety limit.</summary>
        public static bool CanDockInternally(ShipData ship, bool largeHull) =>
            !largeHull && !CanDockExternally(ship);

        /// <summary>ModContent.ParseShips: accept only the documented per-ship docking roles.</summary>
        public static void CheckShip(JObject o, string where, ModInfo mod, string file)
        {
            var token = ModJson.Get(o, "dockingRole");
            if (token == null || token.Type == JTokenType.Null) return;
            if (token.Type != JTokenType.String)
                throw new ModJsonException($"{ModJson.Where(token, file)}: \"dockingRole\" must be \"internal\" or \"external\"");
            string role = ((string)token ?? "").Trim();
            if (role.Length == 0) return;
            if (!role.Equals("internal", StringComparison.OrdinalIgnoreCase) &&
                !role.Equals("external", StringComparison.OrdinalIgnoreCase))
                throw new ModJsonException($"{ModJson.Where(token, file)}: \"dockingRole\" must be \"internal\" or \"external\"");
        }

        /// <summary>Apply an optional role; null or an empty string clears an earlier mod's setting.</summary>
        public static void ApplyShip(ShipData ship, JObject o)
        {
            if (!ModJson.Has(o, "dockingRole")) return;
            string role = ModJson.Str(o, "dockingRole")?.Trim();
            ship.dockingRole = string.IsNullOrEmpty(role) ? null : role.ToLowerInvariant();
        }
    }
}
