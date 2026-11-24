// ChoiceRow.cs
// One option with a fixed set of values (remake UI, styled by GoF2Common.uss .choice-*): a label, then either
// segments (every value as a clickable button, for a few short values) or a stepper (‹ value ›, for long lists).
// Modified: the long lists are a drop-down instead of the stepper: a box with the value and a down chevron; a click / tap
// opens the list of values under it (ChoicePopup, the current one ticked), a pick or a press outside closes it. The keys
// and the controller still step through the values (left / right). The box sits at the row's right end, a set share of
// its width; a value longer than the box slides left and back (a pause at each end) so it can be read: TextMarquee.
// The row itself is the focusable element: left / right step through it (MainMenu, PauseMenu call Step); Enter / A leave
// it as it is (they cycled it, a player's report: A should only take the value shown).

using System;
using UnityEngine.UIElements;

namespace GoF2Remake.UI
{
    public class ChoiceRow : VisualElement
    {
        readonly Label label;
        readonly VisualElement segments;
        readonly Label value;
        readonly Button dropdown;
        readonly VisualElement textClip;

        string[] choices = Array.Empty<string>();
        int index;

        /// <summary>The player picked another value (not raised by SetWithoutNotify).</summary>
        public event Action<int> Changed;
        /// <summary>A command choice may handle a pick without changing the stored selection.</summary>
        public Func<int, bool> InterceptPick;

        public bool Segmented { get; }
        public int Index => index;
        public int Count => choices.Length;

        public ChoiceRow(bool segmented)
        {
            Segmented = segmented;
            focusable = true;
            AddToClassList("choice-row");
            label = new Label { pickingMode = PickingMode.Ignore };
            label.AddToClassList("choice-label");
            Add(label);
            if (segmented)
            {
                segments = new VisualElement();
                segments.AddToClassList("choice-segments");
                Add(segments);
            }
            else
            {
                dropdown = new Button { focusable = false };
                dropdown.AddToClassList("choice-dropdown");
                value = new Label { pickingMode = PickingMode.Ignore };
                value.AddToClassList("choice-value");
                value.AddToClassList("gof-semibold");
                textClip = new VisualElement { pickingMode = PickingMode.Ignore };
                textClip.AddToClassList("choice-dropdown-text");
                textClip.Add(value);
                dropdown.Add(textClip);
                // Modified: the shared marquee (TextMarquee), which the Debug page's item grids use too; this was a second
                // copy of the same slide, hold and measure.
                TextMarquee.Attach(textClip, value);
                var chevron = new VisualElement { pickingMode = PickingMode.Ignore };
                chevron.AddToClassList("choice-dropdown-chevron");
                dropdown.Add(chevron);
                dropdown.clicked += () =>
                {
                    if (ChoicePopup.IsOpenFor(dropdown)) ChoicePopup.Close();
                    else if (enabledInHierarchy) ChoicePopup.Open(dropdown, choices, index, Pick);
                };
                Add(dropdown);
            }
        }


        public string LabelText { get => label.text; set => label.text = value; }

        /// <summary>New value texts and the current index, without raising Changed.</summary>
        public void SetWithoutNotify(string[] texts, int current)
        {
            texts ??= Array.Empty<string>();
            bool rebuild = segments != null && segments.childCount != texts.Length;
            choices = texts;
            index = texts.Length == 0 ? 0 : Math.Clamp(current, 0, texts.Length - 1);
            if (rebuild)
            {
                segments.Clear();
                for (int i = 0; i < texts.Length; i++)
                {
                    int pick = i;
                    var b = new Button { focusable = false };
                    b.AddToClassList("choice-segment");
                    if (i == texts.Length - 1) b.AddToClassList("choice-segment--last");   // USS has no :last-child in Unity 7
                    b.AddToClassList("gof-semibold");
                    b.clicked += () => Pick(pick);
                    segments.Add(b);
                }
            }
            UpdateView();
        }

        /// <summary>One step left (-1) or right (+1), clamped to the ends.</summary>
        public void Step(int dir) => Pick(index + dir);

        void Pick(int i)
        {
            if (choices.Length == 0) return;
            i = Math.Clamp(i, 0, choices.Length - 1);
            if (InterceptPick?.Invoke(i) == true) return;
            if (i == index) return;
            index = i;
            UpdateView();
            Changed?.Invoke(i);
        }

        void UpdateView()
        {
            if (segments != null)
            {
                for (int i = 0; i < segments.childCount && i < choices.Length; i++)
                {
                    var b = (Button)segments[i];
                    b.text = choices[i];
                    b.EnableInClassList("choice-segment--active", i == index);
                }
            }
            else if (value != null) value.text = choices.Length > 0 ? choices[index] : "";
        }
    }
}
