// OptionControl.cs
// The row of one OptionDef: a Slider ("Label: value"), a Toggle or a ChoiceRow, plus an optional description line.
// Modified: an option with a description gets a "?" icon at the right end of its row (OptionInfo: its window opens after
// 1 s of the mouse on it, or on a tap); every row but the key bindings keeps that room free, so the controls line up.
// The description line under the row shows only with a controller (GoF2Common.uss).
// Modified: a slider is the name, then at the row's right its value in a small outlined box and the bar (as wide as the
// choices' drop-downs); on the Sound page a speaker before the value mutes the sound (a click: 0, another click: the
// volume it had).
// Modified (the user's request, 2026-10-22): the value box takes a typed number. A click / tap on it, Enter / Space on the
// row with keys and mouse, or a digit / minus / point typed on the selected row opens an edit box in its place (Enter or
// leaving it sets the value, clamped, Esc drops it); the number is in the box's own unit (OptionDef.fromShown). And a row
// resets alone: the arrow before a changed slider's value, or Backspace / Delete / the controller's X on the selected row,
// puts back its default (OptionDef.defaultValue, else Settings'), choices and toggles by the keys too.
// Root is the row's container, Field the focusable control inside it. Refresh re-reads Settings (after "Default
// settings", a language change or a change made elsewhere) without raising the control's own callbacks.

using System;
using System.Globalization;
using System.Text.RegularExpressions;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace GoF2Remake.UI
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
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
        readonly VisualElement info;
        Label sliderValue;
        VisualElement muteIcon;
        VisualElement resetIcon;
        TextField editField;
        bool editTookTyping, editHadFocus;
        int editFocusTries;
        float unmutedValue = -1f;
        static Texture2D soundIcon, mutedIcon;

        /// <summary>The player changed the value through this row.</summary>
        public event Action Changed;

        /// <summary>The button row whose action is running (null outside it): an action that opens a window over its panel
        /// (HdWindow) finds the panel through it.</summary>
        public static OptionControl Pressing { get; private set; }

        void Press()
        {
            Pressing = this;
            try { def.action?.Invoke(); }
            finally { Pressing = null; }
            NotifyChanged();
        }

        void NotifyChanged()
        {
            // Opening a picker/folder or resetting a run is an action, not an experience setting edit.
            // An action that really writes a Settings value marks its scope through Settings.Set itself.
            if (def.kind != OptionKind.Button && def.section != OptionSection.Tools)
            {
                if (def.page == OptionPage.Cosmetics && def.id != "cosmeticExperience") Settings.SelectCustomExperience(true);
                if (def.page == OptionPage.Gameplay && def.id != "gameplayExperience" && def.id != "difficulty") Settings.SelectCustomExperience(false);
            }
            Changed?.Invoke();
        }

        /// <summary>Every row on show reads its setting again: a change that finishes later than its row's press (the
        /// interface font's import, after the file picker).</summary>
        public static void RefreshShown() => refreshShown?.Invoke();
        static event Action refreshShown;

        public OptionControl(OptionDef d)
        {
            def = d;
            Root = new VisualElement();
            Root.AddToClassList("option-item");
            Root.RegisterCallback<AttachToPanelEvent>(_ => refreshShown += Refresh);
            Root.RegisterCallback<DetachFromPanelEvent>(_ => refreshShown -= Refresh);
            switch (d.kind)
            {
                case OptionKind.Slider:
                    slider = new Slider(d.min, d.max);
                    slider.RegisterValueChangedCallback(e => { d.set(e.newValue); UpdateSliderLabel(); NotifyChanged(); });
                    Field = slider;
                    BuildSliderBox(d.volume);
                    // Enter / Space on a focused slider with keys and mouse: type its value (the main menu's and the
                    // station's pages navigate by focus; the pause menu and the photo panel call Activate).
                    slider.RegisterCallback<NavigationSubmitEvent>(e =>
                    {
                        if (GameControls.BlocksMenus || InputMode.Current == InputKind.Gamepad) return;
                        BeginEdit(null);
                        e.StopPropagation();
                        slider.focusController?.IgnoreEvent(e);
                    });
                    break;
                case OptionKind.Toggle:
                    toggle = new Toggle();
                    toggle.RegisterValueChangedCallback(e => { d.setBool(e.newValue); NotifyChanged(); });
                    Field = toggle;
                    break;
                case OptionKind.Binding:
                    binding = new BindingRow(d.control);
                    Field = binding;
                    break;
                case OptionKind.Button:
                    button = new Button(Press);
                    button.AddToClassList("option-button");
                    Field = button;
                    break;
                default:
                    // Modified: every choice is a drop-down (ChoiceRow, ChoicePopup), the few-value ones too (they were segments).
                    choice = new ChoiceRow(false);
                    choice.InterceptPick = i =>
                    {
                        if (i != 2 || d.customRulesRequested == null) return false;
                        d.customRulesRequested();
                        return true;
                    };
                    choice.Changed += i =>
                    {
                        int before = d.getIndex();
                        d.setIndex(i);
                        UpdateDescription();
                        NotifyChanged();
                        // A display or size change asks to be kept, else it goes back by itself (docs/UI-DESIGN.md "Options").
                        if (d.confirm && before != i) SettingsConfirm.Changed(this, before);
                    };
                    Field = choice;
                    break;
            }
            Root.Add(Field);
            if (d.kind != OptionKind.Binding) Root.AddToClassList("option-item--info-room");
            if (d.description != null)
            {
                description = new Label { pickingMode = PickingMode.Ignore };
                description.AddToClassList("option-description");
                Root.Add(description);
                info = OptionInfo.Icon(d.description);
                Root.Add(info);   // on the row, not its control: a greyed-out option's description still opens
                Field.RegisterCallback<GeometryChangedEvent>(_ => PlaceInfo());
            }
            if (d.extra != null) Root.Add(d.extra());
            // The selected row's keys (a typed digit, the reset), every frame while it is on a panel.
            if (slider != null || toggle != null || choice != null) Root.schedule.Execute(PollKeys).Every(0);
            Root.RegisterCallback<DetachFromPanelEvent>(_ => EndEdit(false));
            Refresh();
        }

        /// <summary>Left / right change the row's value (a slider, a choice, a binding), not move to the next item.</summary>
        public bool StepsSideways => slider != null || choice != null || binding != null;

        /// <summary>The row is shown (OptionDef.visible; always without one).</summary>
        public bool Shown => def.visible == null || def.visible();

        /// <summary>The row takes input (OptionDef.enabled; always without one): greyed out and skipped by the keys while not.</summary>
        public bool Enabled => def.enabled == null || def.enabled();

        public void Refresh()
        {
            if (def.visible != null) Root.style.display = Shown ? DisplayStyle.Flex : DisplayStyle.None;
            if (def.enabled != null) Field.SetEnabled(Enabled);
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

        void UpdateSliderLabel()
        {
            // Modified: the name alone; the value in its box (it was "Name: value"). Without the box (BuildSliderBox found
            // no "unity-base-slider__input" to insert it into, e.g. a Unity version that renamed it) the label keeps the old
            // "Name: value", so the value is never simply missing.
            float v = slider.value;
            string shown = def.format != null ? def.format(v) : Mathf.RoundToInt(v).ToString();
            slider.label = sliderValue != null ? def.label() : $"{def.label()}: {shown}";
            if (sliderValue != null) sliderValue.text = shown;
            if (resetIcon != null) resetIcon.style.visibility = IsDefault() ? Visibility.Hidden : Visibility.Visible;
            if (muteIcon != null)
            {
                bool muted = v <= slider.lowValue + 0.0001f;
                var tex = muted ? mutedIcon : soundIcon;
                if (tex != null) muteIcon.style.backgroundImage = new StyleBackground(tex);
                muteIcon.EnableInClassList("option-slider-mute--muted", muted);
            }
        }

        /// <summary>The slider's parts: the value box (and a volume's speaker, OptionDef.volume) before the bar.</summary>
        void BuildSliderBox(bool sound)
        {
            slider.AddToClassList("option-slider");
            var input = slider.Q(className: "unity-base-slider__input");
            if (input?.parent == null) return;
            int at = input.parent.IndexOf(input);
            if (sound)
            {
                if (soundIcon == null) soundIcon = GoF2Remake.Data.OriginalTextures.Image("GoF2Hud/option_sound");
                if (mutedIcon == null) mutedIcon = GoF2Remake.Data.OriginalTextures.Image("GoF2Hud/option_sound_muted");
                muteIcon = new VisualElement { pickingMode = PickingMode.Position };
                muteIcon.AddToClassList("option-slider-mute");
                // Its own press: not the row's (a drag) or the slider's.
                muteIcon.RegisterCallback<PointerDownEvent>(e => e.StopPropagation());
                muteIcon.RegisterCallback<PointerUpEvent>(e => e.StopPropagation());
                muteIcon.RegisterCallback<ClickEvent>(e => { e.StopPropagation(); ToggleMute(); });
                input.parent.Insert(at++, muteIcon);
            }
            // The reset arrow: before the value while it isn't the default (its room always kept, so the rows line up).
            if (HasDefault())
            {
                resetIcon = new VisualElement { pickingMode = PickingMode.Position };
                resetIcon.AddToClassList("option-slider-reset");
                resetIcon.generateVisualContent += DrawReset;
                resetIcon.RegisterCallback<PointerDownEvent>(e => e.StopPropagation());
                resetIcon.RegisterCallback<PointerUpEvent>(e => e.StopPropagation());
                resetIcon.RegisterCallback<ClickEvent>(e => { e.StopPropagation(); ResetToDefault(); });
                input.parent.Insert(at++, resetIcon);
            }
            // Takes its own presses (a press there would move the slider, and lost the row's highlight until the mouse left
            // and came back); a click types a value.
            sliderValue = new Label { pickingMode = PickingMode.Position };
            sliderValue.RegisterCallback<PointerDownEvent>(e => e.StopPropagation());
            sliderValue.RegisterCallback<PointerUpEvent>(e => e.StopPropagation());
            sliderValue.RegisterCallback<ClickEvent>(e => { e.StopPropagation(); BeginEdit(null); });
            sliderValue.AddToClassList("option-slider-value");
            sliderValue.AddToClassList("gof-semibold");
            input.parent.Insert(at, sliderValue);
        }

        /// <summary>The speaker: to 0, or back to the volume it had before (half the range when it had none).</summary>
        void ToggleMute()
        {
            if (slider == null || !Enabled) return;
            if (slider.value > slider.lowValue + 0.0001f) { unmutedValue = slider.value; slider.value = slider.lowValue; }
            else slider.value = unmutedValue > slider.lowValue ? unmutedValue : (slider.lowValue + slider.highValue) * 0.5f;
        }

        /// <summary>The "?" icon at the row's right end (the room .option-item--info-room keeps), centred on the control.</summary>
        void PlaceInfo()
        {
            if (info == null) return;
            var box = Field.layout;
            if (float.IsNaN(box.height) || box.height <= 0f) return;
            info.style.right = 4f;
            info.style.top = box.y + (box.height - OptionInfo.IconSize) * 0.5f;
        }

        void UpdateDescription()
        {
            if (description == null) return;
            description.text = def.description();
            bool has = !string.IsNullOrEmpty(description.text);
            // Shown or not by the stylesheet (a controller only); an empty one never.
            description.style.display = has ? StyleKeyword.Null : DisplayStyle.None;
            if (info != null) info.style.display = has ? StyleKeyword.Null : DisplayStyle.None;
        }

        /// <summary>Left / right on the row (keys, D-pad): a slider moves a twentieth of its range, a choice one value,
        /// a toggle flips.</summary>
        public void Step(int dir)
        {
            if (!Enabled) return;
            if (slider != null)
                slider.value = Mathf.Clamp(slider.value + dir * (slider.highValue - slider.lowValue) / 20f, slider.lowValue, slider.highValue);
            else if (toggle != null) toggle.value = !toggle.value;
            else if (binding != null) binding.Step(dir);
            else choice?.Step(dir);
        }

        /// <summary>Confirm on the row (Enter / A): a toggle flips, a button presses, a binding captures, a slider (keys and
        /// mouse) opens its value for typing. A choice keeps its value: left / right pick it (a player's report: A cycled on
        /// to the next value).</summary>
        public void Activate()
        {
            if (!Enabled) return;
            if (slider != null) { if (InputMode.Current != InputKind.Gamepad) BeginEdit(null); }
            else if (toggle != null) toggle.value = !toggle.value;
            else if (button != null) Press();
            else if (binding != null) binding.Activate();
        }
    
        // ---- typing a value -------------------------------------------------------------------------------------

        /// <summary>A value is being typed into this row's box (the menus' keys rest meanwhile: GameControls.TextEntryOpen).</summary>
        public bool Editing => editField != null;

        /// <summary>The value box becomes an edit box: the shown number selected, or 'first' typed into it.</summary>
        public void BeginEdit(string first)
        {
            if (slider == null || sliderValue == null || editField != null || !Enabled || sliderValue.parent == null) return;
            var f = new TextField { maxLength = 16 };
            f.AddToClassList("option-slider-edit");
            f.AddToClassList("gof-semibold");
            f.selectAllOnFocus = first == null;
            f.selectAllOnMouseUp = false;
            f.SetValueWithoutNotify(first ?? EditText(sliderValue.text));
            // Its own presses: not the slider's (a drag) or the row's.
            f.RegisterCallback<PointerDownEvent>(e => e.StopPropagation());
            f.RegisterCallback<PointerUpEvent>(e => e.StopPropagation());
            f.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            // Every key stays in the box: no menu navigation or submit; Enter sets the value, Esc drops it.
            void Swallow(EventBase e) { e.StopPropagation(); f.focusController?.IgnoreEvent(e); }
            f.RegisterCallback<NavigationMoveEvent>(Swallow, TrickleDown.TrickleDown);
            f.RegisterCallback<NavigationSubmitEvent>(Swallow, TrickleDown.TrickleDown);
            f.RegisterCallback<NavigationCancelEvent>(e => { Swallow(e); EndEdit(false); }, TrickleDown.TrickleDown);
            f.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter || e.keyCode == KeyCode.Tab) { Swallow(e); EndEdit(true); }
                else if (e.keyCode == KeyCode.Escape) { Swallow(e); EndEdit(false); }
                else if (e.character == '\n' || e.character == '\r' || e.character == '\t') Swallow(e);
            }, TrickleDown.TrickleDown);
            f.RegisterCallback<FocusOutEvent>(_ => EndEdit(true));
            // On the row over the value box, not in the slider: a field inside the slider can't take the focus (the slider,
            // focusable itself, takes it). The box keeps its room, unseen.
            f.style.position = Position.Absolute;
            Root.Add(f);
            PlaceEdit(f);
            sliderValue.RegisterCallback<GeometryChangedEvent>(OnValueMoved);
            sliderValue.style.visibility = Visibility.Hidden;
            editHadFocus = Field.focusController != null && Field.focusController.focusedElement == Field;
            editField = f;
            editFocusTries = 0;
            // The game's and the menus' keys rest while typing (NetChat.Keys null, GameControls.BlocksMenus).
            editTookTyping = !Multiplayer.NetChat.Typing;
            if (editTookTyping) Multiplayer.NetChat.SetTyping(true);
            GameControls.TextEntryOpen = true;
            // Focused once laid out (a field focused before its first layout doesn't take it); the caret after a typed key.
            f.schedule.Execute(() =>
            {
                if (editField != f || IsFocusedInside(f)) return;
                if (++editFocusTries > 30) { EndEdit(false); return; }   // never took the focus (the panel went away)
                f.Focus();
                if (first != null) f.SelectRange(f.value.Length, f.value.Length);
            }).Every(0).Until(() => editField != f || IsFocusedInside(f));
        }

        void OnValueMoved(GeometryChangedEvent _) { if (editField != null) PlaceEdit(editField); }

        /// <summary>The edit box over the value box, in the row's own coordinates.</summary>
        void PlaceEdit(VisualElement f)
        {
            var box = sliderValue.layout;
            if (float.IsNaN(box.width) || box.width <= 0f) return;
            var at = sliderValue.parent.ChangeCoordinatesTo(Root, box);
            f.style.left = at.x;
            f.style.top = at.y;
            f.style.width = at.width;
            f.style.height = at.height;
        }

        static bool IsFocusedInside(VisualElement f)
        {
            for (var v = f.focusController?.focusedElement as VisualElement; v != null; v = v.parent)
                if (v == f) return true;
            return false;
        }

        /// <summary>The edit box goes, the value set from it ('commit') or not.</summary>
        void EndEdit(bool commit)
        {
            var f = editField;
            if (f == null) return;
            editField = null;
            if (commit && TryNumber(f.value, out float typed))
            {
                float v = Mathf.Clamp(FromShown(typed), slider.lowValue, slider.highValue);
                if (!float.IsNaN(v))
                {
                    def.set(v);
                    Refresh();
                    NotifyChanged();
                }
            }
            // Back on the row unless a click took the focus elsewhere (the menu's selection stays on it).
            bool refocus = editHadFocus && (f.focusController?.focusedElement == null || IsFocusedInside(f));
            f.RemoveFromHierarchy();
            if (sliderValue != null)
            {
                sliderValue.UnregisterCallback<GeometryChangedEvent>(OnValueMoved);
                sliderValue.style.visibility = StyleKeyword.Null;
            }
            if (refocus && Field.panel != null) Field.Focus();
            if (editTookTyping) Multiplayer.NetChat.SetTyping(false);
            editTookTyping = false;
            GameControls.TextEntryOpen = false;
        }

        /// <summary>A typed number (in the box's unit) to the slider's value: the option's own conversion, else a straight
        /// line through what the box shows at the range's two ends (a percent of 0..1, a vignette's 0..0.6 as 0..100 %).</summary>
        float FromShown(float typed)
        {
            if (def.fromShown != null) return def.fromShown(typed);
            if (def.format != null && TryNumber(def.format(slider.lowValue), out float a) && TryNumber(def.format(slider.highValue), out float b)
                && Mathf.Abs(b - a) > 1e-6f)
                return slider.lowValue + (typed - a) / (b - a) * (slider.highValue - slider.lowValue);
            return typed;
        }

        static readonly Regex NumberPattern = new Regex(@"[-+]?(\d+([.,]\d*)?|[.,]\d+)");

        /// <summary>The number in a box's text ("46°", "+0.5 EV", "x1.0", "6500 K"; "1.2 km" = 1200).</summary>
        public static bool TryNumber(string text, out float value)
        {
            value = 0f;
            if (string.IsNullOrEmpty(text)) return false;
            text = text.Replace('−', '-');   // the minus sign
            var m = NumberPattern.Match(text);
            if (!m.Success || !float.TryParse(m.Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value)) return false;
            if (text.IndexOf("km", StringComparison.OrdinalIgnoreCase) >= 0) value *= 1000f;
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        /// <summary>What the edit box starts with: the shown number without its unit or plus sign (a distance in metres).</summary>
        public static string EditText(string shown)
        {
            if (!TryNumber(shown, out float v)) return "";
            if (shown.IndexOf("km", StringComparison.OrdinalIgnoreCase) >= 0) return Mathf.RoundToInt(v).ToString(CultureInfo.InvariantCulture);
            string m = NumberPattern.Match(shown.Replace('−', '-')).Value;
            return m.StartsWith("+") ? m.Substring(1) : m;
        }

        // ---- reset ---------------------------------------------------------------------------------------------

        bool TryDefault(out float value)
        {
            if (def.defaultValue != null) { value = def.defaultValue(); return true; }
            value = 0f;
            if (slider != null) return Settings.TryDefault(def.get, out value);
            if (toggle != null) { bool ok = Settings.TryDefault(def.getBool, out bool b); value = b ? 1f : 0f; return ok; }
            if (choice != null) { bool ok = Settings.TryDefault(def.getIndex, out int i); value = i; return ok; }
            return false;
        }

        bool HasDefault() => TryDefault(out _);

        /// <summary>The row shows its default (or has none).</summary>
        public bool IsDefault()
        {
            if (!TryDefault(out float d)) return true;
            if (slider != null)
            {
                float v = def.get();
                // Showing the default counts (a setter that rounds: the field of view stores whole degrees, its default is
                // the original's 1.22 rad = 69.9°, so it never came back to exactly that and kept its arrow).
                if (def.format != null && def.format(v) == def.format(d)) return true;
                return Mathf.Abs(v - d) <= Mathf.Max(1e-5f, (slider.highValue - slider.lowValue) * 0.002f);
            }
            if (toggle != null) return def.getBool() == d > 0.5f;
            if (choice != null) return def.getIndex() == Mathf.RoundToInt(d);
            return true;
        }

        /// <summary>This row alone back to its default (a choice that asks to be kept is left to Default settings).</summary>
        public void ResetToDefault()
        {
            if (!Enabled || IsDefault() || !TryDefault(out float d)) return;
            if (slider != null) def.set(d);
            else if (toggle != null) def.setBool(d > 0.5f);
            else if (choice != null && !def.confirm) def.setIndex(Mathf.RoundToInt(d));
            else return;
            Refresh();
            NotifyChanged();
        }

        /// <summary>The selected row (focused, or the pause menu's / photo panel's highlight) takes a typed digit, and
        /// Backspace / Delete / the controller's X reset it.</summary>
        void PollKeys()
        {
            if (editField != null || Root.panel == null || GameControls.BlocksMenus || !IsSelectedRow()) return;
            var kb = Multiplayer.NetChat.Keys;
            var pad = Gamepad.current;
            if ((kb != null && (kb.backspaceKey.wasPressedThisFrame || kb.deleteKey.wasPressedThisFrame))
                || (pad != null && pad.buttonWest.wasPressedThisFrame && InputMode.Current == InputKind.Gamepad))
            {
                ResetToDefault();
                return;
            }
            if (slider == null || kb == null) return;
            string typed = TypedStart(kb);
            if (typed != null) BeginEdit(typed);
        }

        bool IsSelectedRow()
        {
            if (Field.ClassListContains("options-selected")) return true;   // the photo panel
            if (Field.focusController != null && Field.focusController.focusedElement == Field) return true;
            for (VisualElement v = Field; v != null; v = v.parent)
                if (v.ClassListContains("autopilot-menu-item--selected")) return true;   // the pause menu
            return false;
        }

        /// <summary>A number's first key pressed this frame: a digit (the row or the keypad), a minus or a point.</summary>
        static string TypedStart(Keyboard kb)
        {
            for (int i = 0; i <= 9; i++)
            {
                if (kb[Key.Digit0 + i].wasPressedThisFrame && !kb.shiftKey.isPressed) return i.ToString(CultureInfo.InvariantCulture);
                if (kb[Key.Numpad0 + i].wasPressedThisFrame) return i.ToString(CultureInfo.InvariantCulture);
            }
            if (kb.minusKey.wasPressedThisFrame || kb.numpadMinusKey.wasPressedThisFrame) return "-";
            if (kb.periodKey.wasPressedThisFrame || kb.numpadPeriodKey.wasPressedThisFrame || kb.commaKey.wasPressedThisFrame) return ".";
            return null;
        }

        /// <summary>The reset arrow, an anticlockwise circle open at the left with its head (the fonts have no ↺).</summary>
        void DrawReset(MeshGenerationContext ctx)
        {
            var r = resetIcon.contentRect;
            if (r.width <= 0f || r.height <= 0f) return;
            var p = ctx.painter2D;
            var c = resetIcon.resolvedStyle.color;
            float rad = Mathf.Min(r.width, r.height) * 0.3f;
            var centre = r.center + new Vector2(0f, rad * 0.08f);
            p.strokeColor = c;
            p.fillColor = c;
            p.lineWidth = Mathf.Max(1.5f, rad * 0.26f);
            p.lineCap = LineCap.Round;
            // Screen angles (y down): from the lower left round the bottom, the right and the top to the upper left.
            p.BeginPath();
            p.Arc(centre, rad, Angle.Degrees(135f), Angle.Degrees(-150f), ArcDirection.CounterClockwise);
            p.Stroke();
            // The head at the upper left end, pointing on along the turn (down and to the left).
            float a = -150f * Mathf.Deg2Rad;
            var outward = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            var along = new Vector2(Mathf.Sin(a), -Mathf.Cos(a));
            var end = centre + outward * rad;
            float h = rad * 0.62f;
            p.BeginPath();
            p.MoveTo(end + along * h);
            p.LineTo(end - along * h * 0.25f + outward * h * 0.85f);
            p.LineTo(end - along * h * 0.25f - outward * h * 0.85f);
            p.ClosePath();
            p.Fill();
        }
    }
}
