// AlienText.cs
// The alien font (Globals::fontAlien = resource 1310, the second font of gof2_interface, loaded with spacing 0 by
// Globals::loadFont 0xf8d20): the dialogue window and the radio draw the text in it when the speaker is 19 (Void) or 56
// (Corny) (StoryTable.UsesAlienFont; Reference/research/dialogue_cutscenes.md 1.2 / 2.3). Those texts are uppercase
// gibberish; the font has 26 magenta glyphs, A..Z, cut to Resources/GoF2Hud/alien_A..Z by "GoF2 > Build > HUD Images".
// A Label can't draw a bitmap font, so the text becomes glyph images in a box right after the Label (which is hidden
// meanwhile and takes the box's classes), one row per word, wrapped at spaces like Globals::getLineArray. Letters map
// case-insensitively; anything else (!, digits, the U+FFFD the conversion left) isn't in the font and is left out.
// Remake guesses, since the .aei glyph table with the advances wasn't converted: 2 px between glyphs, 12 px spaces,
// 4 px between lines, the glyphs at their HD pixel size.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace GoF2Remake.UI
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class AlienText
    {
        const string BoxClass = "alien-text";
        const float GlyphGap = 2f, SpaceWidth = 12f, LineGap = 4f;

        static Texture2D[] glyphs;

        /// <summary>Shows 'text' in 'label', in the alien font when 'alien' (and the glyph images exist).</summary>
        public static void Set(Label label, string text, bool alien)
        {
            var box = BoxOf(label);
            if (!alien || !Load())
            {
                box?.RemoveFromHierarchy();
                label.style.display = StyleKeyword.Null;
                label.text = text;
                return;
            }
            label.text = text;
            label.style.display = DisplayStyle.None;
            if (box == null)
            {
                box = new VisualElement { pickingMode = label.pickingMode };
                foreach (var c in label.GetClasses())
                    if (!c.StartsWith("unity-")) box.AddToClassList(c);
                box.AddToClassList(BoxClass);
                box.style.flexDirection = FlexDirection.Row;
                box.style.flexWrap = Wrap.Wrap;
                box.style.alignItems = Align.FlexStart;
                box.style.alignContent = Align.FlexStart;
                label.parent.Insert(label.parent.IndexOf(label) + 1, box);
            }
            else box.Clear();

            VisualElement word = null;
            foreach (char ch in text ?? "")
            {
                if (char.IsWhiteSpace(ch)) { word = null; continue; }
                char u = char.ToUpperInvariant(ch);
                if (u < 'A' || u > 'Z' || glyphs[u - 'A'] == null) continue;
                if (word == null)
                {
                    word = new VisualElement { pickingMode = PickingMode.Ignore };
                    word.style.flexDirection = FlexDirection.Row;
                    word.style.flexShrink = 0;
                    word.style.marginRight = SpaceWidth - GlyphGap;
                    word.style.marginBottom = LineGap;
                    box.Add(word);
                }
                var tex = glyphs[u - 'A'];
                var g = new VisualElement { pickingMode = PickingMode.Ignore };
                g.style.backgroundImage = tex;
                g.style.width = tex.width;
                g.style.height = tex.height;
                g.style.marginRight = GlyphGap;
                word.Add(g);
            }
        }

        /// <summary>The glyph images Set made for 'label', in reading order (TextReveal); false when it shows plain text.</summary>
        public static bool CollectGlyphs(Label label, List<VisualElement> into)
        {
            var box = BoxOf(label);
            if (box == null) return false;   // Set removes the box for plain text
            foreach (var word in box.Children())
                foreach (var g in word.Children()) into.Add(g);
            return into.Count > 0;
        }

        /// <summary>The glyph box Set put right after the label, if any.</summary>
        static VisualElement BoxOf(Label label)
        {
            var parent = label.parent;
            if (parent == null) return null;
            int next = parent.IndexOf(label) + 1;
            return next < parent.childCount && parent[next].ClassListContains(BoxClass) ? parent[next] : null;
        }

        static bool Load()
        {
            if (glyphs == null)
            {
                glyphs = new Texture2D[26];
                for (int i = 0; i < 26; i++) glyphs[i] = Resources.Load<Texture2D>("GoF2Hud/alien_" + (char)('A' + i));
                if (glyphs[0] == null) Debug.LogWarning("AlienText: no GoF2Hud/alien_* images, run GoF2 > Build > HUD Images");
            }
            return glyphs[0] != null;
        }
    }
}
