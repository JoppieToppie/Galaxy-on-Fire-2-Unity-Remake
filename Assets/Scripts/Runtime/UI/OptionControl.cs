// OptionControl.cs
// The row of one OptionDef: a Slider ("Label: value"), a Toggle or a ChoiceRow, plus an optional description line.
// Root is the row's container, Field the focusable control inside it. Refresh re-reads Settings (after "Default
// settings", a language change or a change made elsewhere) without raising the control's own callbacks.

using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace GoF2Remake.UI
{
    public sealed class OptionControl
    {
        public readonly OptionDef def;
        public readonly VisualElement Root;
        public readonly VisualElement Field;
        readonly Slider slider;
        readonly Toggle toggle;
        readonly ChoiceRow choice;
        readonly Button button;
        readonly BindingRow binding;
        readonly Label description;

        /// <summary>The player changed the value through this row.</summary>
        public event Action Changed;

        public OptionControl(OptionDef d)
        {
            def = d;
            Root = new VisualElement();
            Root.AddToClassList("option-item");
            switch (d.kind)
            {
                case OptionKind.Slider:
                    slider = new Slider(d.min, d.max);
                    slider.RegisterValueChangedCallback(e => { d.set(e.newValue); UpdateSliderLabel(); Changed?.Invoke(); });
                    Field = slider;
                    break;
                case OptionKind.Toggle:
                    toggle = new Toggle();
                    toggle.RegisterValueChangedCallback(e => { d.setBool(e.newValue); Changed?.Invoke(); });
                    Field = toggle;
                    break;
                case OptionKind.Binding:
                    binding = new BindingRow(d.control);
                    Field = binding;
                    break;
                case OptionKind.Button:
                    button = new Button(() => { d.action?.Invoke(); Changed?.Invoke(); });
                    button.AddToClassList("option-button");
                    Field = button;
                    break;
                default:
                    choice = new ChoiceRow(d.segmented);
                    choice.Changed += i => { d.setIndex(i); UpdateDescription(); Changed?.Invoke(); };
                    Field = choice;
                    break;
            }
            Root.Add(Field);
            if (d.description != null)
            {
                description = new Label { pickingMode = PickingMode.Ignore };
                description.AddToClassList("option-description");
                Root.Add(description);
            }
            if (d.extra != null) Root.Add(d.extra());
            Refresh();
        }

        /// <summary>Left / right change the row's value (a slider, a choice, a binding), not move to the next item.</summary>
        public bool StepsSideways => slider != null || choice != null || binding != null;

        /// <summary>The row is shown (OptionDef.visible; always without one).</summary>
        public bool Shown => def.visible == null || def.visible();

        public void Refresh()
        {
            if (def.visible != null) Root.style.display = Shown ? DisplayStyle.Flex : DisplayStyle.None;
            if (slider != null)
            {
                slider.SetValueWithoutNotify(def.get());
                UpdateSliderLabel();
            }
            else if (toggle != null)
            {
                toggle.label = def.label();
                toggle.SetValueWithoutNotify(def.getBool());
            }
            else if (button != null) button.text = def.label();
            else if (binding != null) binding.Refresh();
            else
            {
                choice.LabelText = def.label();
                choice.SetWithoutNotify(def.choices(), def.getIndex());
            }
            UpdateDescription();
        }

        void UpdateSliderLabel() => slider.label = def.format != null ? $"{def.label()}: {def.format(slider.value)}" : def.label();

        void UpdateDescription()
        {
            if (description != null) description.text = def.description();
        }

        /// <summary>Left / right on the row (keys, D-pad): a slider moves a twentieth of its range, a choice one value,
        /// a toggle flips.</summary>
        public void Step(int dir)
        {
            if (slider != null)
                slider.value = Mathf.Clamp(slider.value + dir * (slider.highValue - slider.lowValue) / 20f, slider.lowValue, slider.highValue);
            else if (toggle != null) toggle.value = !toggle.value;
            else if (binding != null) binding.Step(dir);
            else choice?.Step(dir);
        }

        /// <summary>Confirm on the row (Enter / A): a toggle flips, a choice takes the next value.</summary>
        public void Activate()
        {
            if (toggle != null) toggle.value = !toggle.value;
            else if (button != null) { def.action?.Invoke(); Changed?.Invoke(); }
            else if (binding != null) binding.Activate();
            else choice?.Cycle();
        }
    }
}
