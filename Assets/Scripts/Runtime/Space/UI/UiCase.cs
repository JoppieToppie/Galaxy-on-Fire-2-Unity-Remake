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

        /// <summary>A label the text tables write in capitals ("BACK", "SWITCH TAB", the key hints and buttons): as it is in
        /// the Remake style, in sentence case in the Original style ("Back", "Switch tab"), as the original wrote its
        /// labels. Text with any lower-case letter stays as it is; abbreviations (OK, VR, MK II) keep their capitals.</summary>
        public static string OgCase(this string text)
        {
            if (string.IsNullOrEmpty(text) || !Settings.OriginalUi) return text;
            bool letters = false;
            foreach (char c in text)
            {
                if (char.IsLower(c)) return text;
                if (char.IsLetter(c)) letters = true;
            }
            if (!letters) return text;
            var sb = new System.Text.StringBuilder(text.Length);
            bool first = true;
            int i = 0;
            while (i < text.Length)
            {
                if (!char.IsLetter(text[i])) { sb.Append(text[i++]); continue; }
                int start = i;
                while (i < text.Length && char.IsLetter(text[i])) i++;
                string word = text.Substring(start, i - start);
                if (System.Array.IndexOf(Abbreviations, word) >= 0) sb.Append(word);
                else if (first) sb.Append(word.Substring(0, 1)).Append(word.Substring(1).ToLowerInvariant());
                else sb.Append(word.ToLowerInvariant());
                first = false;
            }
            return sb.ToString();
        }

        static readonly string[] Abbreviations = { "OK", "VR", "UI", "HUD", "FPS", "HDR", "MSAA", "DLSS", "FSR", "MK", "II", "III", "IV", "ESC", "LB", "RB", "LT", "RT", "ID", "AI", "NPC", "XP" };
    }
}
