// GameNames.cs
// Item and ship names and descriptions. The original keeps them as numbered blocks of the text table (item names
// 1274 + index, descriptions 1041 + index; ship names 913 + index, descriptions 977 + index), back to back, so a number
// past the original tables reads the next block (item 233's name would be the first medal's). Every name goes through
// here: an original entry reads its block, a mod's entry (or one a mod renamed) its mod's text (Modding.ModContent).

namespace GoF2Remake.Data
{
    public static class GameNames
    {
        public const int ItemNameBase = 1274, ItemDescriptionBase = 1041, ShipNameBase = 913, ShipDescriptionBase = 977;
        public const int OriginalItems = 233, OriginalShips = 64;

        public static string Item(int item) =>
            Modding.ModContent.ItemText(item, false, out var t) ? t : item >= 0 && item < OriginalItems ? Localization.Get(ItemNameBase + item) : "#item" + item;

        public static string ItemDescription(int item) =>
            Modding.ModContent.ItemText(item, true, out var t) ? t : item >= 0 && item < OriginalItems ? Localization.Get(ItemDescriptionBase + item) : "";

        public static string Ship(int ship) =>
            Modding.ModContent.ShipText(ship, false, out var t) ? t : ship >= 0 && ship < OriginalShips ? Localization.Get(ShipNameBase + ship) : "#ship" + ship;

        /// <summary>Remake: the tag of the mod that adds it ("GoF3", "HD"), shown dim after its name; null = none
        /// (Modding.ModContent.TagOf).</summary>
        public static string ShipTag(int ship) => Modding.ModContent.ShipTag(ship);
        public static string ItemTag(int item) => Modding.ModContent.ItemTag(item);
        public static string SystemTag(int system) => Modding.ModWorld.SystemTag(system);
        public static string StationTag(int station) => Modding.ModWorld.StationTag(station);

        public static string ShipDescription(int ship) =>
            Modding.ModContent.ShipText(ship, true, out var t) ? t : ship >= 0 && ship < OriginalShips ? Localization.Get(ShipDescriptionBase + ship) : "";

        /// <summary>Layout::formatCredits: thousands separated, "$" after ("," in English, "." in the other languages).</summary>
        public static string Credits(int value)
        {
            string sep = Localization.Language == "en" ? "," : ".";
            string digits = UnityEngine.Mathf.Abs(value).ToString("#,0", System.Globalization.CultureInfo.InvariantCulture).Replace(",", sep);
            return (value < 0 ? "-" : "") + digits + "$";
        }
    }
}
