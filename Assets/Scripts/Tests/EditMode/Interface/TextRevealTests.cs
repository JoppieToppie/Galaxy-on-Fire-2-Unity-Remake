// TextRevealTests.cs
// CLAUDE.md "Story", Dialogue / Animated dialogue (TextReveal): all-caps shouts (4+ letters, or 2+ before "!", not EMP /
// HUD / AMR and the all-caps words of item / ship / station / system names: "Micro Gun MKII"), bigger and bold
// (<size=130%><b>); *Sigh* actions italic without the asterisks (Russian <...> the same); sentences ending in "!" type faster;
// names tinted (people pale gold with their titles, places light aqua with "Station" / "System", ships / items / story
// goods pink, a one-word item only when the texts never write it in lower case: not Gold; races in their emblem's colour,
// in either case except "void"; plurals too); inline icons: a coin before every amount ("N$", "N credits", each language's
// credit word, Chinese "个信用分" without a space), the race emblem / jumpgate / wormhole / blueprint / autopilot icon
// before the first mention on the page, an equipment category's icon ("mines" only as "mines"), "core" and "container" in
// English, none for "plasma array"; Arabic / Hebrew / Devanagari pages fade in whole, CJK doesn't. The tint colours are
// TextReveal's documented ones (Terran FF8C1A, Vossk 45E06A, people FFD37A...). The icons must be in the sprite asset
// Resources/Sprite Assets/gof2_text_icons (GoF2 > Build > Text Icons); a name whose icon the atlas lacks gets none, never
// a <sprite> tag naming a missing sprite (TextCore prints that as text): a mod's ship blueprint item is named as its ship
// and was given "item_" + its own number (a player's German lounge: "<sprite=... name="item_279" tint=1>Betty").

using System.Collections.Generic;
using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace GoF2Remake.Tests
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public class TextRevealTests : SandboxTest
    {
        DataUiTestKit.PrefsSnapshot prefs;
        DataUiTestKit.TextLanguage english;

        [SetUp]
        public void On()
        {
            prefs = new DataUiTestKit.PrefsSnapshot();
            english = new DataUiTestKit.TextLanguage("en");
            Settings.AnimatedDialogue = true;
        }

        [TearDown]
        public void Off()
        {
            english?.Dispose();
            prefs?.Dispose();
        }

        static bool[] Shouts(string s) => (bool[])DataUiTestKit.CallStatic(typeof(TextReveal), "Shouts", s);
        static bool[] Exclaimed(string s) => (bool[])DataUiTestKit.CallStatic(typeof(TextReveal), "ExclaimedSentences", s);
        static bool Shaped(string s) => (bool)DataUiTestKit.CallStatic(typeof(TextReveal), "IsShapedScript", s);
        static string[] Icons(string s) => (string[])DataUiTestKit.CallStatic(typeof(TextReveal), "Icons", s);
        static Color?[] Tints(string s, string speaker = null) => (Color?[])DataUiTestKit.CallStatic(typeof(TextReveal), "Highlight", s, speaker);

        static string ShoutedWords(string s)
        {
            var b = Shouts(s);
            return new string(s.Select((c, i) => b[i] ? c : '_').ToArray());
        }

        static string Rendered(string page, string speaker = null)
        {
            var label = new Label(page);
            new TextReveal(label).Begin(page, false, null, instant: true, speaker: speaker);
            return label.text;
        }

        [Test]
        public void ANoteShownBeforeItsStyleResolvesIsNotBlack()
        {
            // A player's report (2026-10-24): the courier's thanks and the pirate base's 434 opened as the station loaded,
            // the label still at the initial colour (opaque black), and every plain letter was tagged black.
            string page = "*Sigh* Mr. Maxwell? You finished your mission.\n\nMission accomplished!";
            string text = Rendered(page);
            StringAssert.DoesNotContain("<color=#000000", text);
            StringAssert.Contains("You finished your mission.\n\nMission accomplished!", text, "plain letters take the label's own colour");
            StringAssert.Contains("<color=#FFD37AFF>Maxwell</color>", text, "the tints stay");
        }

        static readonly Color32 Gold =new Color32(0xFF, 0xD3, 0x7A, 0xFF), Aqua = new Color32(0x7F, 0xE3, 0xF0, 0xFF),
            Pink = new Color32(0xF0, 0xA8, 0xDC, 0xFF), Terran = new Color32(0xFF, 0x8C, 0x1A, 0xFF), Vossk = new Color32(0x45, 0xE0, 0x6A, 0xFF),
            Midorian = new Color32(0x9A, 0xA0, 0xA6, 0xFF), Void = new Color32(0xA7, 0x7B, 0xFF, 0xFF);

        static void AssertTint(Color32 want, Color?[] tints, string page, string word, int occurrence = 0)
        {
            int at = -1;
            for (int k = 0; k <= occurrence; k++) at = page.IndexOf(word, at + 1, System.StringComparison.Ordinal);
            Assert.GreaterOrEqual(at, 0, word);
            for (int i = at; i < at + word.Length; i++)
            {
                if (char.IsWhiteSpace(page[i])) continue;
                Assert.IsTrue(tints[i].HasValue, $"'{word}' letter {i - at} untinted in \"{page}\"");
                Assert.AreEqual((Color)want, tints[i].Value, $"'{word}' letter {i - at}");
            }
        }

        static void AssertPlain(Color?[] tints, string page, string word)
        {
            int at = page.IndexOf(word, System.StringComparison.Ordinal);
            Assert.GreaterOrEqual(at, 0, word);
            for (int i = at; i < at + word.Length; i++) Assert.IsFalse(tints[i].HasValue, $"'{word}' is tinted in \"{page}\"");
        }

        // ---- shouting -------------------------------------------------------------------------------------------

        [Test]
        public void AllCapsWordsShout()
        {
            Assert.AreEqual("KEITH!____", ShoutedWords("KEITH! Run"));
            Assert.AreEqual("____KABOOM", ShoutedWords("And KABOOM"));
            Assert.AreEqual("NO!_______", ShoutedWords("NO! Never."), "two letters before a '!'");
            Assert.AreEqual("___________", ShoutedWords("HI there, I"), "two letters without a '!' aren't");
            Assert.AreEqual("________", ShoutedWords("Run now!"), "not capitals");
        }

        [Test]
        public void AcronymsAndNameCapsDontShout()
        {
            Assert.IsFalse(Shouts("Ready the EMP!").Any(b => b), "EMP");
            Assert.IsFalse(Shouts("The HUD shows it").Any(b => b), "HUD");
            Assert.IsFalse(Shouts("An AMR Tormentor").Any(b => b), "AMR");
            Assert.IsFalse(Shouts("Buy the Micro Gun MKII now").Any(b => b), "an item name's all-caps word");
        }

        [Test]
        public void ShoutsRenderBigAndBold()
        {
            string t = Rendered("BANG! Go.");
            StringAssert.Contains("<size=130%><b>BANG!</b></size>", t);
            StringAssert.DoesNotContain("<voffset", t, "the shake is over once the page is shown whole");
        }

        // ---- actions --------------------------------------------------------------------------------------------

        [Test]
        public void ActionsAreItalicWithoutTheirMarkers()
        {
            string t = Rendered("*Sigh* Fine.");
            StringAssert.Contains("<i>Sigh</i>", t);
            StringAssert.DoesNotContain("*", t);
            StringAssert.Contains("Fine.", t);
        }

        [Test]
        public void RussianAngleBracketActionsToo()
        {
            string t = Rendered("<шепотом> Да.");
            StringAssert.Contains("<i>шепотом</i>", t);
            StringAssert.DoesNotContain("<шепотом>", t);
        }

        [Test]
        public void ALoneMarkerStaysText()
        {
            // no closing '*' on the line: not an action; a lone '<' is escaped, the texts have no rich text of their own
            string t = Rendered("5 * 3 and 2 < 4");
            StringAssert.Contains("*", t);
            StringAssert.Contains("<noparse><</noparse>", t);
            StringAssert.DoesNotContain("<i>", t);
        }

        // ---- pacing ---------------------------------------------------------------------------------------------

        [Test]
        public void ExclaimedSentencesAreMarked()
        {
            string s = "Go now! Wait.";
            var e = Exclaimed(s);
            for (int i = 0; i <= 6; i++) Assert.IsTrue(e[i], "\"Go now!\" letter " + i);
            for (int i = 7; i < s.Length; i++) Assert.IsFalse(e[i], "\" Wait.\" letter " + i);
            var cjk = Exclaimed("走！停。");
            Assert.IsTrue(cjk[0] && cjk[1], "the full-width '！' ends a sentence");
            Assert.IsFalse(cjk[2] || cjk[3]);
        }

        [Test]
        public void TheOptionOffLeavesThePagePlain()
        {
            Settings.AnimatedDialogue = false;
            var label = new Label("BANG! *Sigh* 1,800$");
            var r = new TextReveal(label);
            r.Begin(label.text, false);
            Assert.AreEqual("BANG! *Sigh* 1,800$", label.text);
            Assert.IsFalse(r.Running);
            Assert.AreEqual(1f, r.Progress);
        }

        [Test]
        public void TheRevealRunsUntilFinished()
        {
            var label = new Label("Hello there.");
            var r = new TextReveal(label);
            r.Begin(label.text, false);
            Assert.IsTrue(r.Running);
            Assert.Less(r.Progress, 1f);
            r.Tick(100f);
            Assert.IsTrue(r.Running);
            r.Finish();
            Assert.IsFalse(r.Running);
            Assert.AreEqual(1f, r.Progress);
            r.Begin(label.text = "Hello there.", false);
            r.Tick(60000f);
            Assert.IsFalse(r.Running, "a minute later the page is done");
        }

        // ---- shaped scripts -------------------------------------------------------------------------------------

        [TestCase("नमस्ते दोस्त", true)]
        [TestCase("مرحبا يا صديقي", true)]
        [TestCase("שלום חבר", true)]
        [TestCase("你好，朋友。", false)]
        [TestCase("こんにちは", false)]
        [TestCase("Привет!", false)]
        [TestCase("Hello!", false)]
        public void ShapedScriptsFadeInWhole(string page, bool shaped)
        {
            Assert.AreEqual(shaped, Shaped(page));
            var label = new Label(page);
            var r = new TextReveal(label);
            r.Begin(page, false);
            Assert.IsTrue(r.Running);
            if (shaped) Assert.AreEqual(page, label.text, "no per-letter tags in a shaped script");
            else StringAssert.Contains("<color=", label.text);
        }

        // ---- names ----------------------------------------------------------------------------------------------

        [Test]
        public void PeopleAreGoldWithTheirTitles()
        {
            string page = "Lieutenant Commander Brent Snocom is here.";
            AssertTint(Gold, Tints(page), page, "Lieutenant Commander Brent Snocom");
            page = "Keith T. Maxwell, again.";
            AssertTint(Gold, Tints(page), page, "Keith T. Maxwell");
        }

        [Test]
        public void ASpeakerGivenByNameIsGold()
        {
            string page = "Ask Zorbo Quixley about it, or Zorbo.";
            var t = Tints(page, "Zorbo Quixley");
            AssertTint(Gold, t, page, "Zorbo Quixley");
            AssertTint(Gold, t, page, "Zorbo", 1);
            AssertPlain(Tints(page), page, "Zorbo Quixley");
        }

        [Test]
        public void PlacesAreAquaWithStationOrSystem()
        {
            string page = "Dock at Var Hastra Station in the Mido System.";
            var t = Tints(page);
            AssertTint(Aqua, t, page, "Var Hastra Station");
            AssertTint(Aqua, t, page, "Mido System");
            AssertPlain(t, page, "Dock at");
        }

        [Test]
        public void RacesTakeTheirEmblemColours()
        {
            string page = "The Terrans and the vossk met the Midorian and the Void, not the void.";
            var t = Tints(page);
            AssertTint(Terran, t, page, "Terrans");
            AssertTint(Vossk, t, page, "vossk");
            AssertTint(Midorian, t, page, "Midorian");
            AssertTint(Void, t, page, "Void");
            AssertPlain(t, page, "void.");
            page = "For the Vossk Empire!";
            AssertTint(Vossk, Tints(page), page, "Vossk Empire");
        }

        [Test]
        public void ItemsAndShipsArePink()
        {
            string page = "Bring energy cells to the Phantom, and some Drugs.";   // "drugs" is written in lower case in the texts
            var t = Tints(page);
            AssertTint(Pink, t, page, "energy cells");
            AssertTint(Pink, t, page, "Phantom");
            AssertPlain(t, page, "Drugs");
        }

        [Test]
        public void TintsRenderAsColourTags()
        {
            string t = Rendered("The Vossk are here.");
            StringAssert.Contains("<color=#45E06AFF>Vossk</color>", t);
        }

        // ---- icons ----------------------------------------------------------------------------------------------

        [TestCase("It costs 1,800$.", "1,800$")]
        [TestCase("It costs 1800 $ now", "1800 $")]
        [TestCase("Take 20,000 credits.", "20,000 credits")]
        [TestCase("Nimm 20.000 Credits.", "20.000 Credits")]
        [TestCase("Je te donne 500 crédits.", "500 crédits")]
        [TestCase("奖励：20,000个信用分。", "20,000")]
        public void ACoinBeforeEveryAmount(string page, string amount)
        {
            var icons = Icons(page);
            int at = page.IndexOf(amount, System.StringComparison.Ordinal);
            Assert.AreEqual("coin", icons[at], page);
            Assert.AreEqual(1, icons.Count(i => i == "coin"));
        }

        [Test]
        public void ACoinBeforeAChineseAmountRightAfterAWord()
        {
            // The Chinese texts write the amount straight after a character ("您20,000个信用分", texts 335, 676, 1902, 1968,
            // 2077...). The amount pattern's (?<![\p{L}\p{N}]) refuses a number after a CJK letter, and then matches the
            // "000" after the comma: the coin lands inside the number.
            string page = "获得20,000个信用分。";
            var icons = Icons(page);
            Assert.AreEqual("coin", icons[2], "the coin before 20,000");
            Assert.AreEqual(1, icons.Count(i => i == "coin"));
        }

        [Test]
        public void NoCoinWithoutMoney()
        {
            Assert.IsFalse(Icons("Kill 5 pirates in 3 minutes.").Contains("coin"));
            Assert.IsFalse(Icons("Item 1,800 is here.").Contains("coin"));
        }

        [Test]
        public void TheCoinRendersAsATintedSprite()
        {
            StringAssert.Contains("<sprite=\"gof2_text_icons\" name=\"coin\" tint=1>", Rendered("It costs 1,800$."));
        }

        [Test]
        public void IconsBeforeTheFirstMentionOnly()
        {
            string page = "Terran ships and Terran stations.";
            var icons = Icons(page);
            Assert.AreEqual("terran", icons[0]);
            Assert.AreEqual(1, icons.Count(i => i == "terran"), "the first mention on the page only");
        }

        [TestCase("Fly through the jumpgate now.", "jumpgate", "gate")]
        [TestCase("A Wormhole opened.", "Wormhole", "wormhole")]
        [TestCase("Find the blueprint.", "blueprint", "blueprint")]
        [TestCase("Use the autopilot.", "autopilot", "autopilot")]
        [TestCase("The Vossk attack.", "Vossk", "vossk")]
        [TestCase("Pirates everywhere.", "Pirates", "pirate")]
        [TestCase("Open the container.", "container", "container")]
        [TestCase("Bring the core back.", "core", "core")]
        public void WordIcons(string page, string word, string sprite)
        {
            Assert.AreEqual(sprite, Icons(page)[page.IndexOf(word, System.StringComparison.Ordinal)], page);
        }

        [Test]
        public void MinesOnlyAsAPlural()
        {
            // category 11 "Mine": the icon of its first item (AMR Saber, 60); "mine" alone is "a friend of mine"
            int first = Enumerable.Range(0, GameNames.OriginalItems).First(i => Db.Items[i].categoryId == 11);
            Assert.AreEqual($"item_{first:000}", Icons("Drop the mines here.")[9]);
            Assert.IsFalse(Icons("A friend of mine.").Any(i => i != null));
        }

        [Test]
        public void ACategoryWhosePluralIsAnOrdinaryWordTakesNoPlural()
        {
            // PR #211: Portuguese category 3 "Termo" (the thermo guns) matched "termos" in Brent's "até termos um plano" (1870,
            // step 20: "until we have a plan"); the texts use "termos" in lower case and never "termo", so no plural.
            using var portuguese = new DataUiTestKit.TextLanguage("pt");
            Assert.AreEqual("Termo", Localization.Get(221 + 3).Trim(), "the test's premise");
            string brent = Localization.Get(1870);
            int at = brent.IndexOf("termos", System.StringComparison.Ordinal);
            Assert.GreaterOrEqual(at, 0, "the test's premise");
            Assert.IsNull(Icons(brent)[at], brent);
            int first = Enumerable.Range(0, GameNames.OriginalItems).First(i => Db.Items[i].categoryId == 3);
            Assert.AreEqual($"item_{first:000}", Icons("Compre um Termo.")[10], "the singular keeps its icon");
        }

        [Test]
        public void PlasmaArrayTakesNoIcon()
        {
            Assert.IsFalse(Icons("Destroy the plasma array.").Any(i => i != null));
        }

        [Test]
        public void EveryIconTheTextUsesIsInTheSpriteAsset()
        {
            var asset = AssetDatabase.LoadMainAssetAtPath("Assets/Resources/Sprite Assets/gof2_text_icons.asset");
            Assert.IsNotNull(asset, "Resources/Sprite Assets/gof2_text_icons (GoF2 > Build > Text Icons)");
            var table = new SerializedObject(asset).FindProperty("m_SpriteCharacterTable");
            Assert.IsNotNull(table);
            var names = new HashSet<string>();
            for (int i = 0; i < table.arraySize; i++) names.Add(table.GetArrayElementAtIndex(i).FindPropertyRelative("m_Name").stringValue);
            foreach (var n in new[] { "coin", "terran", "vossk", "nivelian", "midorian", "pirate", "void", "gate", "wormhole", "blueprint", "autopilot", "core", "container" })
                Assert.IsTrue(names.Contains(n), n);
            for (int i = 0; i < GameNames.OriginalItems; i++) Assert.IsTrue(names.Contains($"item_{i:000}"), $"item_{i:000}");
            for (int i = 0; i < GameNames.OriginalShips; i++) Assert.IsTrue(names.Contains($"ship_{i:000}"), $"ship_{i:000}");
        }
    }

    /// <summary>The icons with mods on: only sprites the atlas has (CLAUDE.md "Mods", Mod blueprints: a ship blueprint's
    /// hidden item reads as its ship).</summary>
    public class TextRevealModIconTests : SavesModsTest
    {
        static string[] Icons(string s) => (string[])DataUiTestKit.CallStatic(typeof(TextReveal), "Icons", s);

        void ModOn()
        {
            string dir = WriteMod("icons", "t_icons", "1.0", "",
                ("items.json", "[ { 'id': 'gun', 'base': 2, 'name': 'Zapper Gun' } ]"),
                ("ships.json", "[ { 'id': 'hawk', 'model': 'h.glb', 'name': 'Hawk', 'price': 990000 } ]"),
                ("blueprints.json", "[ { 'id': 'betty_bp', 'ship': 0, 'ingredients': [ { 'item': 155 } ] } ]"));
            WriteFile(dir, "h.glb", Dummy);
            ScanAndEnable("t_icons");
            Assert.IsFalse(Installed("t_icons").Broken, AllMessages(Installed("t_icons")));
        }

        [TestCase("en", "I offer you 1x Hawk for only 990,000$. Trading in your Betty makes it 973,800$.")]
        [TestCase("de", "Ich biete Ihnen 1x Hawk für nur 990.000$ an. Mit deiner Betty in Zahlung macht das 973.800$.")]
        public void AShipBlueprintsItemGivesNoMissingSprite(string language, string page)
        {
            using var text = new DataUiTestKit.TextLanguage(language);
            ModOn();
            // The rules as built with the mod on (the old code kept rules built before the mods for good).
            typeof(TextReveal).GetField("iconRules", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                .SetValue(null, null);
            var db = Database.Load();
            Assert.AreEqual(GameNames.Ship(0), GameNames.Item(db.Items.Count - 1), "the blueprint's item reads as Betty (the test's premise)");
            var rules = (System.Collections.IList)DataUiTestKit.CallStatic(typeof(TextReveal), "IconRules");
            foreach (var r in rules)
            {
                string word = (string)r.GetType().GetField("word").GetValue(r), sprite = (string)r.GetType().GetField("sprite").GetValue(r);
                if (sprite == null) continue;
                if (sprite.StartsWith("item_")) Assert.Less(int.Parse(sprite.Substring(5)), GameNames.OriginalItems, $"'{word}' -> {sprite}: not in the atlas");
                if (sprite.StartsWith("ship_")) Assert.Less(int.Parse(sprite.Substring(5)), GameNames.OriginalShips, $"'{word}' -> {sprite}: not in the atlas");
            }
            var icons = Icons(page);
            Assert.AreEqual("ship_000", icons[page.IndexOf("Betty", System.StringComparison.Ordinal)], "the game ship's own icon");
            Assert.IsNull(icons[page.IndexOf("Hawk", System.StringComparison.Ordinal)], "a mod ship: no icon in the atlas, no tag");
            foreach (var icon in icons.Where(i => i != null))
            {
                if (icon.StartsWith("item_")) Assert.Less(int.Parse(icon.Substring(5)), GameNames.OriginalItems, icon);
                if (icon.StartsWith("ship_")) Assert.Less(int.Parse(icon.Substring(5)), GameNames.OriginalShips, icon);
            }
            Assert.AreEqual("item_002", Icons("Fire the Zapper Gun.")[9], "a mod item: its base's icon");
        }
    }
}
