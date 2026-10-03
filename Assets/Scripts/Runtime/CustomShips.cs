// CustomShips.cs
// The remake's own ships (Resources/GoF2Data/custom_ships.json, remake-only): indices after the original 64, merged into
// Database.Ships / Assemblies / WeaponMounts by Database.Load. This is the economy-independent view for the code that
// has no Database at hand: names and descriptions (the original's 913 + index / 977 + index text ranges are back to
// back, so index 64 would read ship 0's description: custom ships carry their own texts, translatable with
// Localization.Extra "ship<N>Name" / "ship<N>Description"), the maker's race (Shop.ShipRace stops at 63), the hangar
// height (StationTables.ShipY) and who sells them.
// Models: "GoF2 > Build Custom Ships" (CustomShipBuilder) makes the prefab, the shop icon and the derived tables.

using System.Collections.Generic;

namespace GoF2Remake.Data
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class CustomShips
    {
        public const string FileName = "custom_ships";

        static List<CustomShipData> all;

        /// <summary>Play mode runs without a domain reload: re-read the file (it may have been edited).</summary>
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => all = null;

        /// <summary>Every custom ship (read once; the stats here are the Android economy's, use Database.Ship for prices).</summary>
        public static IReadOnlyList<CustomShipData> All => all ??= Database.ReadCustomShips();

        public static CustomShipData Get(int ship)
        {
            foreach (var c in All) if (c.index == ship) return c;
            return null;
        }

        public static bool IsCustom(int ship) => Get(ship) != null;

        /// <summary>Custom ships can be had (sold in lounges, offered by the debug and admin tools): the gameplay option on
        /// (Settings.CustomShipsEnabled) and not in a multiplayer session (not there for now). Ships already owned stay.</summary>
        public static bool Available => Settings.CustomShipsEnabled && !Multiplayer.NetGame.Active;

        /// <summary>The ship may be offered: an original ship, or a custom one while they're Available.</summary>
        public static bool Offered(int ship) => Available || !IsCustom(ship);

        /// <summary>The ship's name: 913 + index for the original ships, the custom entry's (or its Extra translation).</summary>
        public static string ShipName(int ship)
        {
            var c = Get(ship);
            return c == null ? Localization.Get(913 + ship) : Localization.Extra($"ship{ship}Name", c.name);
        }

        /// <summary>The ship's description: 977 + index for the original ships, the custom entry's otherwise.</summary>
        public static string ShipDescription(int ship)
        {
            var c = Get(ship);
            return c == null ? Localization.Get(977 + ship) : Localization.Extra($"ship{ship}Description", c.description ?? "");
        }
    }
}
