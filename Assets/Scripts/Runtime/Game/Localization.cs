// Localization.cs
// The game's text table (GameText::getText): Localization/text_<lang>.json is a plain array, index = text ID
// as used in the decompiled code (e.g. 28 "Start new game", 170 "Back"). Plain C#; menus pass in the
// TextAssets they reference, and the first Get without a loaded table loads the settings language from
// Resources/GoF2LanguageTables (scenes started directly in the editor).

using System;
using System.Collections.Generic;
using UnityEngine;

namespace GoF2Remake.Data
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class Localization
    {
        [Serializable] class Wrapper { public string[] items; }

        static string[] texts = new string[0];
        // The remake's texts per language, then by key: Extra runs every frame in places (the HUD's action prompt), and a
        // single table keyed by language + "." + key built that string on every call.
        static readonly Dictionary<string, Dictionary<string, string>> extra = new Dictionary<string, Dictionary<string, string>>();
        static Dictionary<string, string> extraNow;   // the current language's

        public static string Language { get; private set; } = "en";
        public static event Action Changed;

        /// <summary>Loads text_{lang}.json content (array of strings).</summary>
        public static void Load(string language, TextAsset table)
        {
            // the player's converted copy of the language first (docs/notes/player-assets.md), else the build's own table
            string json = GameContent.LanguageText(language) ?? (table != null ? table.text : null);
            if (json == null) return;
            var w = JsonUtility.FromJson<Wrapper>("{\"items\":" + json + "}");
            texts = w?.items ?? new string[0];
            for (int i = 0; i < texts.Length; i++) if (texts[i] != null) texts[i] = Clean(texts[i]);   // once, not on every Get
            Language = language;
            extraNow = null;
            LoadExtra(language);
            Changed?.Invoke();
        }

        /// <summary>False until a table is loaded (the main menu loads one; otherwise the first Get does).</summary>
        public static bool IsLoaded => texts.Length > 0;

        /// <summary>The number of texts in the loaded table (ids 0 .. Count - 1).</summary>
        public static int Count { get { if (!IsLoaded) AutoLoad(); return texts.Length; } }

        /// <summary>Text by original ID; falls back to "#id" when missing.</summary>
        public static string Get(int id)
        {
            if (!IsLoaded) AutoLoad();
            return id >= 0 && id < texts.Length && texts[id] != null ? texts[id] : "#" + id;
        }

        static bool autoLoadTried;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void HookContent()
        {
            GameContent.Refreshed -= Forget;
            GameContent.Refreshed += Forget;
        }

        /// <summary>The player's copy converted: the next Get loads its table (a table found missing before, too).</summary>
        static void Forget()
        {
            texts = new string[0];
            autoLoadTried = false;
        }

        static void AutoLoad()
        {
            if (autoLoadTried) return;
            autoLoadTried = true;
            var t = LanguageTables.Load();
            if (GameContent.LanguageText(Settings.Language) != null || GameContent.LanguageText("en") != null)
            {
                // the converted copy has the language (or English to fall back to): no table asset needed
                string code = GameContent.LanguageText(Settings.Language) != null ? Settings.Language : "en";
                Load(code, null);
                return;
            }
            if (t == null || t.tables == null || t.tables.Length == 0) return;
            int i = t.codes != null ? Array.IndexOf(t.codes, Settings.Language) : -1;
            if (i < 0 || i >= t.tables.Length || t.tables[i] == null) i = 0;
            Load(t.codes != null && i < t.codes.Length ? t.codes[i] : "en", t.tables[i]);
        }

        /// <summary>Remake-only strings (not in the original table), by key, with an English fallback.</summary>
        public static string Extra(string key, string english)
        {
            if (extraNow == null && !extra.TryGetValue(Language, out extraNow)) return english;
            return extraNow.TryGetValue(key, out var s) ? s : english;
        }

        public static void SetExtra(string language, string key, string text)
        {
            if (!extra.TryGetValue(language, out var table)) extra[language] = table = new Dictionary<string, string>();
            table[key] = text;
            if (language == Language) extraNow = table;
        }

        /// <summary>Folder under Resources with the remake-only texts per language: extra_LANG.json (LANG = the language code), an object
        /// { "key": "text" } with the keys of the Extra calls. Missing keys keep their English text.</summary>
        public const string ExtraFolder = "GoF2Localization";

        static void LoadExtra(string language)
        {
            var asset = Resources.Load<TextAsset>(ExtraFolder + "/extra_" + language);
            if (asset == null) return;
            try
            {
                var d = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, string>>(asset.text);
                if (d != null)
                    foreach (var kv in d)
                        if (!string.IsNullOrEmpty(kv.Value)) SetExtra(language, kv.Key, kv.Value.Replace("\r\n", "\n"));
            }
            catch (Exception e) { Debug.LogWarning($"Localization: {asset.name}.json can't be read: {e.Message}"); }
            Resources.UnloadAsset(asset);
        }

        // The converted table lost a few non-ASCII symbols (U+FFFD); restore the ones the menus show.
        static string Clean(string s) => s.Replace("Fire 2�", "Fire 2®").Replace("� 20", "© 20").Replace("\r\n", "\n");
    }
}
