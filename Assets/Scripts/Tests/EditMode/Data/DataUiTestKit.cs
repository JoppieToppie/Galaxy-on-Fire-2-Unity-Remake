// DataUiTestKit.cs
// Helpers of the Data and Interface tests (nested in one class so their names can't clash with other test folders'):
//   Json          a small strict JSON reader (the test assembly has no Newtonsoft): any GoF2Data / localization file as
//                 Dictionary<string, object> / List<object> / string / double / bool / null, duplicate keys reported
//   TextLanguage  loads a text table (Assets/Localization/text_<lang>.json) for a test and puts the previous one back
//   PrefsSnapshot the PlayerPrefs of every option, the key bindings and the language, put back afterwards (Settings'
//                 read cache cleared, the key binding overrides loaded again)
//   reflection    the private helpers the tests reach (TextReveal's parsers, GameControls.Load, InputMode.Current)

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GoF2Remake.Tests
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    internal static class DataUiTestKit
    {
        public const BindingFlags AnyStatic = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        /// <summary>A static method by name (private ones too), invoked; an exception inside comes out unwrapped.</summary>
        public static object CallStatic(Type type, string name, params object[] args)
        {
            var m = type.GetMethod(name, AnyStatic);
            Assert.IsNotNull(m, $"{type.Name}.{name} not found (renamed?)");
            try { return m.Invoke(null, args); }
            catch (TargetInvocationException e) when (e.InnerException != null) { throw e.InnerException; }
        }

        // ---- files ------------------------------------------------------------------------------------------------

        /// <summary>An asset path ("Assets/...") as a file on disk.</summary>
        public static string FullPath(string assetPath) => Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath));

        /// <summary>A text asset's contents; in a checkout without the originals (docs/notes/player-assets.md "The test bed")
        /// the converted copy's version of an original table ($GOF2_ORIGINAL_CACHE: the text tables, the GoF2Data files).</summary>
        public static string ReadAsset(string assetPath)
        {
            string full = FullPath(assetPath);
            if (File.Exists(full)) return File.ReadAllText(full, Encoding.UTF8);
            string fromCopy = FromCopy(assetPath);
            if (fromCopy != null) return fromCopy;
            return File.ReadAllText(full, Encoding.UTF8);   // (the file's own error)
        }

        /// <summary>The converted copy's version of an original text asset, null when it has none.</summary>
        public static string FromCopy(string assetPath)
        {
            const string Lang = "Assets/Localization/text_", Data = "Assets/Resources/GoF2Data/";
            if (assetPath.StartsWith(Lang) && assetPath.EndsWith(".json"))
                return GameContent.LanguageText(assetPath.Substring(Lang.Length, assetPath.Length - Lang.Length - 5));
            if (assetPath.StartsWith(Data) && assetPath.EndsWith(".json"))
                return GameContent.Text("GoF2Data/" + assetPath.Substring(Data.Length, assetPath.Length - Data.Length - 5));
            return null;
        }

        /// <summary>Skips the test in a checkout without the originals when it checks one of them (or what the editor
        /// tools derive from them: the per-mesh prefabs, the imported models, the HD skies), which only the full project has.</summary>
        public static void NeedsOriginal(string assetPath)
        {
            if (!File.Exists(FullPath(assetPath)) && !Directory.Exists(FullPath(assetPath)))
                Assert.Ignore($"{assetPath}: an original (or made from one), not in this checkout (docs/notes/player-assets.md)");
        }

        public static bool AssetExists(string assetPath) =>
            !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(assetPath, AssetPathToGUIDOptions.OnlyExistingAssets));

        /// <summary>An assembled object's prefab (Resources/Assembled, built by "GoF2 > Build > Assembled Prefabs"), or in a
        /// checkout without the originals the converted copy's assembly (OriginalAssemblies, what AssembledObject.LoadPrefab
        /// builds then; PlayerCopyTests builds every one).</summary>
        public static bool AssembledExists(AssemblyData a) =>
            a != null && (AssetExists("Assets/Resources/" + Visuals.AssembledObject.PrefabPath(a) + ".prefab") || Visuals.OriginalAssemblies.Has(a.name));

        /// <summary>The language codes of Assets/Localization/text_*.json.</summary>
        public static List<string> TextTableCodes()
        {
            // (a checkout without the originals: the converted copy's languages)
            string dir = Directory.Exists(FullPath("Assets/Localization")) ? FullPath("Assets/Localization") : Path.Combine(GameContent.Root, "Localization");
            if (!Directory.Exists(dir)) return new List<string>();
            return Directory.GetFiles(dir, "text_*.json")
                .Select(f => Path.GetFileNameWithoutExtension(f).Substring("text_".Length)).OrderBy(c => c, StringComparer.Ordinal).ToList();
        }

        // ---- text tables ------------------------------------------------------------------------------------------

        /// <summary>Loads one language's text table for the test (English by default) and the previous one afterwards.</summary>
        public sealed class TextLanguage : IDisposable
        {
            readonly bool wasLoaded;
            readonly string previous;

            public TextLanguage(string code = "en")
            {
                wasLoaded = Localization.IsLoaded;
                previous = Localization.Language;
                Use(code);
            }

            public static void Use(string code)
            {
                var table = AssetDatabase.LoadAssetAtPath<TextAsset>($"Assets/Localization/text_{code}.json");
                // (Localization.Load takes the converted copy's table first, a checkout without the originals has only that)
                Assert.IsTrue(table != null || GameContent.LanguageText(code) != null, $"text_{code}.json missing");
                Localization.Load(code, table);
            }

            public void Dispose()
            {
                if (wasLoaded && previous != Localization.Language) Use(previous);
            }
        }

        // ---- PlayerPrefs ------------------------------------------------------------------------------------------

        public const string SettingsPrefix = "gof2.";
        public const string ControlsPrefsKey = "controls_bindings";

        /// <summary>Every Settings key stored as a float (the five reset groups but the voice language, plus the tilt
        /// calibration, which no group resets).</summary>
        public static IEnumerable<string> SettingFloatKeys =>
            Settings.SoundKeys.Concat(Settings.GraphicsKeys).Concat(Settings.ControlsKeys).Concat(Settings.GameplayKeys).Concat(Settings.CosmeticsKeys).Concat(Settings.AccessibilityKeys)
                .Concat(new[] { "tiltCalX", "tiltCalZ", "tiltCalibrated" }).Distinct();

        /// <summary>The keys in those groups Settings keeps as text (restored as floats they lost their value).</summary>
        static readonly HashSet<string> TextKeys = new HashSet<string> { "uiFont", "speedrunCompare" };

        /// <summary>Settings' PlayerPrefs read cache (private): emptied after the prefs are written behind its back.</summary>
        public static void ClearSettingsCache()
        {
            var field = typeof(Settings).GetField("cache", AnyStatic);
            Assert.IsNotNull(field, "Settings.cache not found (renamed?)");
            ((Dictionary<string, float>)field.GetValue(null)).Clear();
        }

        /// <summary>Puts back the options, the language, the voice language and the key bindings as they were.</summary>
        public sealed class PrefsSnapshot : IDisposable
        {
            struct Saved { public string key; public bool had, text; public float number; public string str; }
            readonly List<Saved> saved = new List<Saved>();

            public PrefsSnapshot()
            {
                foreach (var k in SettingFloatKeys) Keep(SettingsPrefix + k, TextKeys.Contains(k));
                Keep(SettingsPrefix + "language", true);
                Keep(SettingsPrefix + "voiceLanguage", true);
                // The game experience's migration and the welcome screen (Settings.MigrateExperienceDefaults, WelcomeShown).
                foreach (var k in new[] { "experienceDefaults", "experienceDefaults.played", "welcomeShown", "musicTrackGroups.v1" }) Keep(SettingsPrefix + k, false);
                // Every flight style's layout (GameControls' "_free" / "_chorus" keys): ResetToDefaults below drops the loaded one.
                foreach (var k in ControlsLayoutKeys) Keep(k, true);
            }

            static readonly string[] ControlsLayoutKeys = { ControlsPrefsKey, ControlsPrefsKey + "_free", ControlsPrefsKey + "_chorus" };

            void Keep(string key, bool text) => saved.Add(new Saved
            {
                key = key, text = text, had = PlayerPrefs.HasKey(key),
                number = text ? 0f : PlayerPrefs.GetFloat(key, 0f), str = text ? PlayerPrefs.GetString(key, "") : null,
            });

            public void Dispose()
            {
                foreach (var s in saved)
                {
                    if (!s.had) PlayerPrefs.DeleteKey(s.key);
                    else if (s.text) PlayerPrefs.SetString(s.key, s.str);
                    else PlayerPrefs.SetFloat(s.key, s.number);
                }
                PlayerPrefs.Save();
                ClearSettingsCache();
                // The bindings: no overrides, then the saved ones as GameControls.Start loads them.
                GameControls.ResetToDefaults();
                foreach (var k in ControlsLayoutKeys)
                {
                    var key = saved.Find(s => s.key == k);
                    if (key.had) PlayerPrefs.SetString(k, key.str);
                }
                PlayerPrefs.Save();
                ReloadBindings();
            }
        }

        /// <summary>GameControls.Load (private): the overrides in PlayerPrefs applied to the map.</summary>
        public static void ReloadBindings() => CallStatic(typeof(GameControls), "Load");

        /// <summary>A flight action (an InputAction, as object: this assembly doesn't reference the Input System).</summary>
        public static object FlightAction(string field)
        {
            var f = typeof(GameControls).GetField(field, BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(f, $"GameControls.{field} not found");
            return f.GetValue(null);
        }

        public static ControlRow Row(string id)
        {
            foreach (var r in GameControls.Rows) if (r.id == id) return r;
            Assert.Fail($"no key bindings row '{id}'");
            return null;
        }

        /// <summary>GameControls.KeyText(action, pad) through reflection (its parameter is an InputAction).</summary>
        public static string KeyText(string actionField, bool pad) =>
            (string)CallStatic(typeof(GameControls), "KeyText", FlightAction(actionField), pad);

        /// <summary>Sets InputMode.Current (private setter) and gives the old value back.</summary>
        public static InputKind SetInputMode(InputKind kind)
        {
            var p = typeof(InputMode).GetProperty("Current", AnyStatic);
            Assert.IsNotNull(p, "InputMode.Current not found");
            var old = (InputKind)p.GetValue(null);
            p.GetSetMethod(true).Invoke(null, new object[] { kind });
            return old;
        }

        // ---- JSON -------------------------------------------------------------------------------------------------

        /// <summary>A strict JSON reader (RFC 8259; a leading BOM allowed). Objects keep the last of duplicate keys and
        /// report them in 'duplicates'.</summary>
        public static object ParseJson(string text, List<string> duplicates = null) => new JsonReader(text, duplicates).Document();

        sealed class JsonReader
        {
            readonly string s;
            readonly List<string> duplicates;
            int i;

            public JsonReader(string text, List<string> duplicates)
            {
                s = text ?? throw new ArgumentNullException(nameof(text));
                this.duplicates = duplicates;
                i = s.Length > 0 && s[0] == '﻿' ? 1 : 0;
            }

            public object Document()
            {
                Ws();
                var v = Value();
                Ws();
                if (i != s.Length) throw Error("data after the document");
                return v;
            }

            FormatException Error(string what)
            {
                int line = 1;
                for (int k = 0; k < i && k < s.Length; k++) if (s[k] == '\n') line++;
                return new FormatException($"{what} (line {line}, offset {i})");
            }

            void Ws() { while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\n' || s[i] == '\r')) i++; }

            object Value()
            {
                if (i >= s.Length) throw Error("unexpected end");
                switch (s[i])
                {
                    case '{': return Obj();
                    case '[': return Arr();
                    case '"': return Str();
                    case 't': Literal("true"); return true;
                    case 'f': Literal("false"); return false;
                    case 'n': Literal("null"); return null;
                    default: return Num();
                }
            }

            void Literal(string word)
            {
                if (i + word.Length > s.Length || string.CompareOrdinal(s, i, word, 0, word.Length) != 0) throw Error("bad literal");
                i += word.Length;
            }

            Dictionary<string, object> Obj()
            {
                var d = new Dictionary<string, object>();
                i++;
                Ws();
                if (i < s.Length && s[i] == '}') { i++; return d; }
                while (true)
                {
                    Ws();
                    if (i >= s.Length || s[i] != '"') throw Error("a key expected");
                    string key = Str();
                    Ws();
                    if (i >= s.Length || s[i] != ':') throw Error("':' expected");
                    i++;
                    Ws();
                    var v = Value();
                    if (d.ContainsKey(key)) duplicates?.Add(key);
                    d[key] = v;
                    Ws();
                    if (i >= s.Length) throw Error("unexpected end in an object");
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == '}') { i++; return d; }
                    throw Error("',' or '}' expected");
                }
            }

            List<object> Arr()
            {
                var l = new List<object>();
                i++;
                Ws();
                if (i < s.Length && s[i] == ']') { i++; return l; }
                while (true)
                {
                    Ws();
                    l.Add(Value());
                    Ws();
                    if (i >= s.Length) throw Error("unexpected end in an array");
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == ']') { i++; return l; }
                    throw Error("',' or ']' expected");
                }
            }

            string Str()
            {
                var sb = new StringBuilder();
                i++;
                while (true)
                {
                    if (i >= s.Length) throw Error("unterminated string");
                    char c = s[i++];
                    if (c == '"') return sb.ToString();
                    if (c < 0x20) throw Error("control character in a string");
                    if (c != '\\') { sb.Append(c); continue; }
                    if (i >= s.Length) throw Error("unterminated escape");
                    char e = s[i++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (i + 4 > s.Length || !int.TryParse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int u))
                                throw Error("bad \\u escape");
                            sb.Append((char)u);
                            i += 4;
                            break;
                        default: throw Error("bad escape");
                    }
                }
            }

            double Num()
            {
                int start = i;
                if (i < s.Length && s[i] == '-') i++;
                if (i >= s.Length || !char.IsDigit(s[i])) throw Error("a value expected");
                if (s[i] == '0') i++; else while (i < s.Length && char.IsDigit(s[i])) i++;
                if (i < s.Length && s[i] == '.')
                {
                    i++;
                    if (i >= s.Length || !char.IsDigit(s[i])) throw Error("digits expected after '.'");
                    while (i < s.Length && char.IsDigit(s[i])) i++;
                }
                if (i < s.Length && (s[i] == 'e' || s[i] == 'E'))
                {
                    i++;
                    if (i < s.Length && (s[i] == '+' || s[i] == '-')) i++;
                    if (i >= s.Length || !char.IsDigit(s[i])) throw Error("digits expected in the exponent");
                    while (i < s.Length && char.IsDigit(s[i])) i++;
                }
                return double.Parse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture);
            }
        }
    }
}
