// UiCase.cs
// Remake: the remake's menus write their labels and titles in capitals; the original HD versions wrote them as the text
// tables have them ("Space Lounge", "Primary weapons"). With Options > Graphics > Interface style on Original
// (Settings.OriginalUi) the labels keep the table's case, so the Original style reads like the original game.

using GoF2Remake.Data;

namespace GoF2Remake.UI
{
    public static class UiCase
    {
        /// <summary>A label or title as the interface style writes it: capitals in the Remake style, the text's own case in
        /// the Original style.</summary>
        public static string UiCaps(this string text)
        {
            if (string.IsNullOrEmpty(text) || Settings.OriginalUi) return text;
            return text.ToUpperInvariant();
        }
    }
}
