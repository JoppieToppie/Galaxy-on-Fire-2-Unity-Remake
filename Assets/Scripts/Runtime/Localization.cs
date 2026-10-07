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
        static Dictionary<string, string> extra = new Dictionary<string, string>();

        public static string Language { get; private set; } = "en";
        public static event Action Changed;

        /// <summary>Loads text_{lang}.json content (array of strings).</summary>
        public static void Load(string language, TextAsset table)
        {
            if (table == null) return;
            var w = JsonUtility.FromJson<Wrapper>("{\"items\":" + table.text + "}");
            texts = w?.items ?? new string[0];
            Language = language;
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
            return id >= 0 && id < texts.Length && texts[id] != null ? Clean(texts[id]) : "#" + id;
        }

        static bool autoLoadTried;

        static void AutoLoad()
        {
            if (autoLoadTried) return;
            autoLoadTried = true;
            var t = LanguageTables.Load();
            if (t == null || t.tables == null || t.tables.Length == 0) return;
            int i = t.codes != null ? Array.IndexOf(t.codes, Settings.Language) : -1;
            if (i < 0 || i >= t.tables.Length || t.tables[i] == null) i = 0;
            Load(t.codes != null && i < t.codes.Length ? t.codes[i] : "en", t.tables[i]);
        }

        /// <summary>Remake-only strings (not in the original table), by key, with an English fallback.</summary>
        public static string Extra(string key, string english) => extra.TryGetValue(Language + "." + key, out var s) ? s : english;

        public static void SetExtra(string language, string key, string text) => extra[language + "." + key] = text;

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
