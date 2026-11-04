// LocalizationDataTests.cs
// CLAUDE.md "Data" (Localization/text_<lang>.json, a plain array: index = the original's text id) and "UI and platforms"
// (14 tables; the menu offers English, German, French, Spanish, Italian, Dutch, Polish, Russian, Portuguese (Brazil),
// Simplified Chinese, Hindi, Korean, Japanese and Arabic; Resources/GoF2LanguageTables references every text_*.json; the remake's own texts per
// language in Resources/GoF2Localization/extra_<lang>.json, a { key: text } object of the Extra calls, English being the
// code's fallback). Every table must carry the English table's ids (a missing id shows as "#id"); every extra_<lang>.json
// key must be one the code asks for (a key no code uses is a stale translation).

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using GoF2Remake.Data;
using NUnit.Framework;
using UnityEngine;

namespace GoF2Remake.Tests
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public class LocalizationDataTests
    {
        static readonly string[] MenuLanguages = { "en", "de", "fr", "es", "it", "nl", "pl", "ru", "pt", "zh", "hi", "ko", "ja", "ar" };
        /// <summary>Tables that exist but the menu doesn't offer (none since 2026-11-04).</summary>
        static readonly string[] OtherLanguages = { };

        [Test]
        public void TheMenuOffersTheseLanguages()
        {
            CollectionAssert.AreEqual(MenuLanguages, LanguageTables.Offered.Select(l => l.code).ToArray());
        }

        [Test]
        public void EveryOfferedLanguageHasAFlag()
        {
            // The Mod Builder's voice line languages show one per language (LanguageFlags); Arabic has none (no one
            // country's).
            foreach (var (code, _) in LanguageTables.Offered)
                if (code == "ar") Assert.IsNull(GoF2Remake.UI.LanguageFlags.Of(code), code);
                else Assert.IsNotNull(GoF2Remake.UI.LanguageFlags.Of(code), code);
        }

        static List<object> Table(string code) =>
            (List<object>)DataUiTestKit.ParseJson(DataUiTestKit.ReadAsset($"Assets/Localization/text_{code}.json"));

        static int englishCount = -1;
        static int EnglishCount => englishCount >= 0 ? englishCount : englishCount = Table("en").Count;

        [Test]
        public void TheTablesAreTheFourteenLanguages()
        {
            CollectionAssert.AreEquivalent(MenuLanguages.Concat(OtherLanguages), DataUiTestKit.TextTableCodes());
            Assert.AreEqual(3401, EnglishCount, "the English table's ids 0..3400");
        }

        [Test]
        public void LanguageTablesReferencesEveryTable()
        {
            var t = LanguageTables.Load();
            Assert.IsNotNull(t, "Resources/GoF2LanguageTables");
            Assert.AreEqual(t.codes.Length, t.tables.Length, "codes / tables");
            CollectionAssert.AreEquivalent(DataUiTestKit.TextTableCodes(), t.codes);
            for (int i = 0; i < t.codes.Length; i++)
            {
                Assert.IsNotNull(t.tables[i], t.codes[i]);
                Assert.AreEqual("text_" + t.codes[i], t.tables[i].name, "code " + t.codes[i] + " points at its own table");
            }
            Assert.AreEqual("en", t.codes[0], "English first: Localization.AutoLoad's fallback is table 0");
        }

        static IEnumerable<TestCaseData> AllLanguages()
        {
            foreach (var c in MenuLanguages) yield return new TestCaseData(c).SetName("TextTableHasEveryId_" + c);
            foreach (var c in OtherLanguages) yield return new TestCaseData(c).SetName("TextTableHasEveryId_" + c);
        }

        [TestCaseSource(nameof(AllLanguages))]
        public void TextTableHasEveryId(string code)
        {
            var table = Table(code);
            Assert.AreEqual(EnglishCount, table.Count, $"text_{code}.json: as many texts as English");
            for (int i = 0; i < table.Count; i++) Assert.IsInstanceOf<string>(table[i], $"text_{code}.json[{i}] is a string");
        }

        [Test]
        public void TheGameLoadsTablesLikeThisReader()
        {
            using (new DataUiTestKit.TextLanguage("en"))
            {
                Assert.AreEqual(EnglishCount, Localization.Count, "Localization.Load's JsonUtility read");
                Assert.AreEqual("Start new game", Localization.Get(28));
                Assert.AreEqual("Back", Localization.Get(170));
                Assert.AreEqual("#" + EnglishCount, Localization.Get(EnglishCount), "past the table");
                Assert.AreEqual("#-1", Localization.Get(-1));
            }
        }

        [Test]
        public void ExtraFallsBackToTheEnglishText()
        {
            using (new DataUiTestKit.TextLanguage("en"))
                Assert.AreEqual("English text", Localization.Extra("noSuchKeyForTests", "English text"));
            using (new DataUiTestKit.TextLanguage("de"))
                Assert.AreEqual("English text", Localization.Extra("noSuchKeyForTests", "English text"), "a key the table lacks stays English");
        }

        [Test]
        public void ExtraTablesExistForTheTranslatedLanguages()
        {
            foreach (var c in MenuLanguages.Where(c => c != "en").Concat(OtherLanguages))
                Assert.IsNotNull(Resources.Load<TextAsset>($"{Localization.ExtraFolder}/extra_{c}"), "extra_" + c + ".json");
        }

        static IEnumerable<string> ExtraFiles() =>
            Directory.GetFiles(DataUiTestKit.FullPath("Assets/Resources/" + Localization.ExtraFolder), "extra_*.json").Select(Path.GetFileName).OrderBy(f => f);

        static Dictionary<string, object> Extra(string file, List<string> duplicates = null) =>
            (Dictionary<string, object>)DataUiTestKit.ParseJson(DataUiTestKit.ReadAsset($"Assets/Resources/{Localization.ExtraFolder}/{file}"), duplicates);

        [TestCaseSource(nameof(ExtraFiles))]
        public void ExtraTableIsAFlatObjectOfTexts(string file)
        {
            var duplicates = new List<string>();
            Dictionary<string, object> d = null;
            Assert.DoesNotThrow(() => d = Extra(file, duplicates), file);
            Assert.Greater(d.Count, 0, file);
            Assert.IsEmpty(duplicates, file + ": duplicate keys");
            foreach (var kv in d)
            {
                Assert.IsInstanceOf<string>(kv.Value, $"{file}: {kv.Key}");
                Assert.IsFalse(string.IsNullOrWhiteSpace((string)kv.Value), $"{file}: {kv.Key} is empty");
            }
        }

        static string sources;

        /// <summary>Every runtime / editor script's text (not the tests), where the Extra keys are written as "key".</summary>
        static string Sources()
        {
            if (sources != null) return sources;
            var sb = new StringBuilder();
            string root = DataUiTestKit.FullPath("Assets/Scripts");
            string tests = Path.Combine(root, "Tests") + Path.DirectorySeparatorChar;
            foreach (var f in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
                if (!Path.GetFullPath(f).StartsWith(tests)) sb.Append(File.ReadAllText(f)).Append('\n');
            return sources = sb.ToString();
        }

        static List<string> builtPrefixes, builtSuffixes;

        /// <summary>The literal starts ("mbKind" + kind) and ends (key + "Desc") the code joins keys from: the Mod Builder
        /// names its race, slot-type, room and part texts by a prefix and the name.</summary>
        static void BuiltKeyParts(out List<string> prefixes, out List<string> suffixes)
        {
            if (builtPrefixes == null)
            {
                string src = Sources();
                builtPrefixes = System.Text.RegularExpressions.Regex.Matches(src, "\"([A-Za-z_]{3,})\"\\s*\\+")
                    .Cast<System.Text.RegularExpressions.Match>().Select(m => m.Groups[1].Value).Distinct().ToList();
                builtSuffixes = System.Text.RegularExpressions.Regex.Matches(src, "\\+\\s*\"([A-Z][A-Za-z]{2,})\"")
                    .Cast<System.Text.RegularExpressions.Match>().Select(m => m.Groups[1].Value).Distinct().ToList();
            }
            prefixes = builtPrefixes;
            suffixes = builtSuffixes;
        }

        [TestCaseSource(nameof(ExtraFiles))]
        public void ExtraTableHasNoKeyTheCodeDoesNotUse(string file)
        {
            // As of 2026-10-14 every table carries 7 stale keys (exportSavesHelp, exportSavesHelpFolder, importSavesHelp,
            // importSavesHelpFolder, modsTitle, mpGuest, resupplyAmmoFull).
            string src = Sources();
            BuiltKeyParts(out var prefixes, out var suffixes);
            var unused = Extra(file).Keys.Where(k => src.IndexOf("\"" + k + "\"", System.StringComparison.Ordinal) < 0
                                                   && !prefixes.Any(p => k.Length > p.Length && k.StartsWith(p, System.StringComparison.Ordinal))
                                                   && !suffixes.Any(s => k.Length > s.Length && k.EndsWith(s, System.StringComparison.Ordinal)))
                                     .OrderBy(k => k).ToList();
            Assert.IsEmpty(unused, $"{file}: keys no code asks for");
        }

        [Test]
        public void ExtraTablesShareTheirKeys()
        {
            // Every translation was made from the same list of keys: one language lacking keys the others have shows English
            // where the rest are translated.
            var files = ExtraFiles().ToList();
            var first = new HashSet<string>(Extra(files[0]).Keys);
            foreach (var f in files.Skip(1))
                CollectionAssert.AreEquivalent(first, Extra(f).Keys, $"{f} against {files[0]}");
        }
    }
}
