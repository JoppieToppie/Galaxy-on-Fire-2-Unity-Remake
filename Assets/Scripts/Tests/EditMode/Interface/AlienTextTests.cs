// AlienTextTests.cs
// CLAUDE.md "Story", Dialogue: the Void (19) and Corny (56) talk in the alien font (Globals::fontAlien,
// StoryTable.UsesAlienFont; dialogue_cutscenes.md 1.2 / 2.3): the 26 glyphs A..Z cut to Resources/GoF2Hud/alien_A..Z
// ("GoF2 > Build > HUD Images"), shown as image rows after the hidden Label, one row per word (wrapped at spaces like
// Globals::getLineArray), letters case-insensitively, anything else (digits, punctuation, U+FFFD) left out. Remake
// guesses: 2 px between glyphs, 12 px spaces. Plain text again removes the glyph box.

using GoF2Remake.Data;
using GoF2Remake.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace GoF2Remake.Tests
{
    public class AlienTextTests
    {
        [Test]
        public void EveryAlienGlyphExists()
        {
            for (char c = 'A'; c <= 'Z'; c++)
                Assert.IsNotNull(GoF2Remake.Data.OriginalTextures.Image("GoF2Hud/alien_" + c), "GoF2Hud/alien_" + c);
        }

        [Test]
        public void OnlyTheVoidAndCornyTalkAlien()
        {
            for (int s = 0; s < 64; s++) Assert.AreEqual(s == 19 || s == 56, StoryTable.UsesAlienFont(s), "speaker " + s);
        }

        static (VisualElement parent, Label label) Box()
        {
            var parent = new VisualElement();
            var label = new Label { name = "text" };
            label.AddToClassList("dialogue-text");
            parent.Add(label);
            parent.Add(new VisualElement { name = "after" });
            return (parent, label);
        }

        [Test]
        public void AlienTextBecomesGlyphRowsPerWord()
        {
            var (parent, label) = Box();
            AlienText.Set(label, "Ab cd9! E", true);
            Assert.AreEqual(DisplayStyle.None, label.style.display.value, "the label hides");
            Assert.AreEqual("Ab cd9! E", label.text, "the label keeps the text");
            var box = parent[1];
            Assert.IsTrue(box.ClassListContains("alien-text"), "the glyph box sits right after the label");
            Assert.IsTrue(box.ClassListContains("dialogue-text"), "with the label's classes");
            Assert.AreEqual(3, box.childCount, "one row per word");
            Assert.AreEqual(2, box[0].childCount, "A b");
            Assert.AreEqual(2, box[1].childCount, "c d (9 and ! aren't in the font)");
            Assert.AreEqual(1, box[2].childCount, "E");
            Assert.AreEqual("after", parent[2].name);

            var a = GoF2Remake.Data.OriginalTextures.Image("GoF2Hud/alien_A");
            var g = box[0][0];
            Assert.AreEqual(a.width, g.style.width.value.value, "glyphs at their pixel size");
            Assert.AreEqual(a.height, g.style.height.value.value);
            Assert.AreEqual(2f, g.style.marginRight.value.value, "2 px between glyphs");
            Assert.AreEqual(10f, box[0].style.marginRight.value.value, "a 12 px space after the last glyph's 2");
            Assert.AreEqual(GoF2Remake.Data.OriginalTextures.Image("GoF2Hud/alien_B"), box[0][1].style.backgroundImage.value.texture, "lower case b = B");
        }

        [Test]
        public void CollectGlyphsListsThemInReadingOrder()
        {
            var (parent, label) = Box();
            AlienText.Set(label, "HELLO WORLD", true);
            var list = new System.Collections.Generic.List<VisualElement>();
            Assert.IsTrue(AlienText.CollectGlyphs(label, list));
            Assert.AreEqual(10, list.Count);
            Assert.AreSame(parent[1][1][0], list[5], "W after HELLO");
        }

        [Test]
        public void SettingTheTextAgainReplacesTheGlyphs()
        {
            var (parent, label) = Box();
            AlienText.Set(label, "ABC DEF", true);
            AlienText.Set(label, "XY", true);
            Assert.AreEqual(3, parent.childCount, "one box, reused");
            Assert.AreEqual(1, parent[1].childCount);
            Assert.AreEqual(2, parent[1][0].childCount);
        }

        [Test]
        public void PlainTextRemovesTheBox()
        {
            var (parent, label) = Box();
            AlienText.Set(label, "ABC", true);
            AlienText.Set(label, "Hello", false);
            Assert.AreEqual(2, parent.childCount, "the box is gone");
            Assert.AreEqual(StyleKeyword.Null, label.style.display.keyword, "the label shows again");
            Assert.AreEqual("Hello", label.text);
            var list = new System.Collections.Generic.List<VisualElement>();
            Assert.IsFalse(AlienText.CollectGlyphs(label, list));
            Assert.AreEqual(0, list.Count);
        }
    }
}
