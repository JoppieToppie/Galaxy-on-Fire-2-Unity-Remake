// LanguageTables.cs
// References every Localization/text_<lang>.json (Resources/GoF2LanguageTables, made by the scene builders) so any scene
// can load the text table on first use, also when it is started without the main menu (Localization.Get).

using UnityEngine;

namespace GoF2Remake.Data
{
    public class LanguageTables : ScriptableObject
    {
        public const string ResourceName = "GoF2LanguageTables";

        public string[] codes;
        public TextAsset[] tables;

        public static LanguageTables Load() => Resources.Load<LanguageTables>(ResourceName);

        /// <summary>Modified: the languages the menus offer, in their order (every table the asset above references); the
        /// main menu's scene builder writes its language buttons from this list and Options reads its language row from it,
        /// so the two agree. Korean, Japanese and Arabic (the original's own tables, the remake's texts in extra_<lang>.json)
        /// offered since 2026-11-04; what Arabic's table had left in English (the add-on campaigns) translated by the remake.</summary>
        public static readonly (string code, string name)[] Offered =
        {
            ("en", "English"), ("de", "Deutsch"), ("fr", "Français"), ("es", "Español"),
            ("it", "Italiano"), ("nl", "Nederlands"), ("pl", "Polski"), ("ru", "Русский"), ("pt", "Português (Brasil)"),
            ("zh", "简体中文"), ("hi", "हिन्दी"), ("ko", "한국어"), ("ja", "日本語"), ("ar", "العربية"),
        };

        /// <summary>Modified: the text table for a language code (null when the asset has none).</summary>
        public static TextAsset TableFor(string code)
        {
            var t = Load();
            if (t == null || t.codes == null || t.tables == null) return null;
            int i = System.Array.IndexOf(t.codes, code);
            return i >= 0 && i < t.tables.Length ? t.tables[i] : null;
        }
    }
}
