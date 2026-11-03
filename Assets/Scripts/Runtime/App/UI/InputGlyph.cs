// InputGlyph.cs
// Small UI Toolkit elements that show an input: a keyboard keycap or a controller button in the Xbox or the PlayStation
// style (InputMode.Style: the controller last used, or Options > Controls "Controller button icons"). A controller glyph
// (PadGlyph) redraws itself when the style changes, so glyphs built once (dialogue buttons, Launch) follow it too.
// Styled by UI/GoF2InputGlyphs.uss (.keycap, .pad-*; the PlayStation face buttons .pad-face--ps, their symbols drawn).

using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace GoF2Remake.UI
{
    /// <summary>A controller button by its place, named as on an Xbox pad (south = A ...); PadGlyph shows it in the
    /// current style. The stick presses are their own (Xbox LS / RS, PlayStation L3 / R3).</summary>
    public enum PadButton { A, B, X, Y, LeftStick, RightStick, DPad, LeftTrigger, RightTrigger, LeftBumper, RightBumper, Menu, View,
        LeftStickPress, RightStickPress, Touchpad }

    public static class InputGlyph
    {
        /// <summary>The button hints option (Settings.InputHints): hides a screen's hint row container while it is off (the HUD,
        /// the station, the star map; the menus' prompt bars read it in MenuKeys.Bar, the orbit views in OrbitViewUi).</summary>
        public static void TrackHintsOption(VisualElement hints)
        {
            if (hints == null) return;
            void Apply()
            {
                hints.EnableInClassList("hints--off", !Settings.InputHints);
                // Inline too: a screen's own rules for the row (".hangar-open .station-hints", with the input classes) beat the
                // class, so the hangar window kept its hints with the option off.
                hints.style.display = Settings.InputHints ? new StyleEnum<DisplayStyle>(StyleKeyword.Null) : DisplayStyle.None;
            }
            Apply();
            Settings.Changed += Apply;
            hints.RegisterCallback<DetachFromPanelEvent>(_ => Settings.Changed -= Apply);
        }

        public static VisualElement Key(string text, bool wide = false)
        {
            var l = Text(text, "keycap");
            if (wide) l.AddToClassList("keycap--wide");
            return l;
        }

        /// <summary>A controller button in the current style (and again whenever it changes).</summary>
        public static VisualElement Pad(PadButton button) => new PadGlyph(button);

        /// <summary>What an action is bound to right now (GameControls, rebindable) for this input kind: the keyboard's first
        /// slot (the second too with 'both', or when the first is unbound), a composite part by part; the controller's slot.
        /// Empty when nothing is bound (the hint is left out).</summary>
        public static List<VisualElement> For(InputAction action, InputKind kind, bool both = false)
        {
            var list = new List<VisualElement>();
            var row = GameControls.RowOf(action);
            if (row == null || kind == InputKind.Touch) return list;
            if (kind == InputKind.Gamepad)
            {
                if (row.HasSlot(BindSlot.Pad)) foreach (var p in GameControls.Paths(row, BindSlot.Pad)) AddPath(list, p, true);
                return list;
            }
            bool first = GameControls.IsBound(row, BindSlot.Key1);
            if (first) foreach (var p in GameControls.Paths(row, BindSlot.Key1)) AddPath(list, p, false);
            if (!first || both) foreach (var p in GameControls.Paths(row, BindSlot.Key2)) AddPath(list, p, false);
            return list;
        }

        static void AddPath(List<VisualElement> list, string path, bool pad)
        {
            if (string.IsNullOrEmpty(path)) return;
            if (!pad) { string name = GameControls.ShortName(path); list.Add(Key(name, name.Length > 3)); return; }
            PadButton? button = GameControls.PadControl(path) switch
            {
                "buttonsouth" => PadButton.A, "buttoneast" => PadButton.B, "buttonwest" => PadButton.X, "buttonnorth" => PadButton.Y,
                "lefttrigger" => PadButton.LeftTrigger, "righttrigger" => PadButton.RightTrigger,
                "leftshoulder" => PadButton.LeftBumper, "rightshoulder" => PadButton.RightBumper,
                "leftstick" => PadButton.LeftStick, "rightstick" => PadButton.RightStick,
                "leftstickpress" => PadButton.LeftStickPress, "rightstickpress" => PadButton.RightStickPress,
                "start" => PadButton.Menu, "select" => PadButton.View, "dpad" => PadButton.DPad, "touchpadbutton" => PadButton.Touchpad,
                _ => null,
            };
            list.Add(button.HasValue ? new PadGlyph(button.Value) : new PadGlyph(path));   // a stick or D-pad direction: its name
        }

        /// <summary>Glyphs before a button's text: the text moves into a child Label (a Button measures its own text only
        /// while it has no children: with a glyph child it shrank to its padding) laid out in a row with them
        /// (.glyph-button in GoF2InputGlyphs.uss). Returns the label: set the text there from now on.</summary>
        public static Label WithGlyphs(Button button, params VisualElement[] glyphs)
        {
            button.AddToClassList("glyph-button");
            foreach (var g in glyphs)
            {
                g.pickingMode = PickingMode.Ignore;
                g.AddToClassList("glyph-button-glyph");
                button.Add(g);
            }
            var label = new Label(button.text) { pickingMode = PickingMode.Ignore };
            label.AddToClassList("glyph-button-label");
            button.text = "";
            button.Add(label);
            return label;
        }

        /// <summary>
        /// Rework (docs/UI-DESIGN.md "Button prompts"): a controller glyph inside a button, before its label, shown only while
        /// a controller is the input (GoF2InputGlyphs.uss .pad-only, under the root's input-gamepad). 'focusOnly': only on
        /// the focused / selected button (A, which presses whichever has the focus); its space is kept meanwhile, so the
        /// label doesn't move as the focus does. B for a Back / No / Cancel button is always shown (B always presses it).
        /// The text moves into a label (WithGlyphs): set it with SetText from then on. Called again: nothing new.
        /// </summary>
        public static Label PadPrompt(Button button, PadButton pad, bool focusOnly = false)
        {
            if (button == null) return null;
            if (button.Q(className: "pad-only") != null) return button.Q<Label>(className: "glyph-button-label");
            var g = Pad(pad);
            g.AddToClassList("pad-only");
            if (focusOnly) g.AddToClassList("pad-focus-only");
            return WithGlyphs(button, g);
        }

        /// <summary>A button's text, also once PadPrompt / WithGlyphs moved it into a label.</summary>
        public static void SetText(Button button, string text)
        {
            if (button == null) return;
            var label = button.Q<Label>(className: "glyph-button-label");
            text = text.OgCase();   // Original style: "Back", not "BACK" (UiCase)
            if (label != null) label.text = text;
            else button.text = text;
        }

        static Label Text(string text, string cls)
        {
            var l = new Label(text) { pickingMode = PickingMode.Ignore };
            l.AddToClassList(cls);
            l.AddToClassList("gof-semibold");
            return l;
        }
    }

    /// <summary>A controller button glyph that follows InputMode.Style: Xbox's coloured A / B / X / Y and grey LB / RT /
    /// MENU..., or PlayStation's dark face buttons with their drawn symbols (cross blue, circle red, square pink, triangle
    /// green; the UI font has no ✕) and L1 / R2 / OPTIONS... Classes the owner adds stay; only its own (.pad-*) are swapped.</summary>
    public class PadGlyph : Label
    {
        public enum Shape { None, Cross, Circle, Square, Triangle }

        static readonly Color CrossColour = new Color32(0x7C, 0xA8, 0xF2, 0xFF);
        static readonly Color CircleColour = new Color32(0xFF, 0x5E, 0x66, 0xFF);
        static readonly Color SquareColour = new Color32(0xEA, 0x8F, 0xD0, 0xFF);
        static readonly Color TriangleColour = new Color32(0x3F, 0xD2, 0xA4, 0xFF);

        readonly PadButton? button;
        readonly string path;   // a binding the button list doesn't name (a stick or D-pad direction)
        readonly List<string> own = new List<string>();

        /// <summary>The PlayStation face symbol drawn (None: the glyph is text).</summary>
        public Shape Symbol { get; private set; }

        /// <summary>The style it shows now.</summary>
        public PadStyle Style { get; private set; }

        public PadGlyph(PadButton button) { this.button = button; Init(); }
        public PadGlyph(string path) { this.path = path; Init(); }

        void Init()
        {
            pickingMode = PickingMode.Ignore;
            AddToClassList("gof-semibold");
            generateVisualContent += DrawSymbol;
            RegisterCallback<AttachToPanelEvent>(_ => { InputMode.StyleChanged -= Apply; InputMode.StyleChanged += Apply; Apply(); });
            RegisterCallback<DetachFromPanelEvent>(_ => InputMode.StyleChanged -= Apply);
            Apply();
        }

        /// <summary>Shows the current style (InputMode.Style).</summary>
        public void Apply() => Apply(InputMode.Style);

        public void Apply(PadStyle style)
        {
            Style = style;
            foreach (var c in own) RemoveFromClassList(c);
            own.Clear();
            Symbol = Shape.None;
            bool ps = style == PadStyle.PlayStation;
            if (!button.HasValue)
            {
                string xbox = GameControls.ShortName(path, PadStyle.Xbox);
                Set(GameControls.ShortName(path, style), xbox.StartsWith("D-PAD") ? "pad-menu" : "pad-stick");
            }
            else switch (button.Value)
            {
                case PadButton.A: Face("A", "pad-face--a", Shape.Cross, ps); break;
                case PadButton.B: Face("B", "pad-face--b", Shape.Circle, ps); break;
                case PadButton.X: Face("X", "pad-face--x", Shape.Square, ps); break;
                case PadButton.Y: Face("Y", "pad-face--y", Shape.Triangle, ps); break;
                case PadButton.LeftStick: Set(ps ? "L" : "LS", "pad-stick"); break;
                case PadButton.RightStick: Set(ps ? "R" : "RS", "pad-stick"); break;
                case PadButton.LeftStickPress: Set(ps ? "L3" : "LS", "pad-stick"); break;
                case PadButton.RightStickPress: Set(ps ? "R3" : "RS", "pad-stick"); break;
                case PadButton.DPad: Set("D-PAD", "pad-menu"); break;
                case PadButton.LeftTrigger: Set(ps ? "L2" : "LT", "pad-trigger"); break;
                case PadButton.RightTrigger: Set(ps ? "R2" : "RT", "pad-trigger"); break;
                case PadButton.LeftBumper: Set(ps ? "L1" : "LB", "pad-bumper"); break;
                case PadButton.RightBumper: Set(ps ? "R1" : "RB", "pad-bumper"); break;
                case PadButton.View: Set(ps ? (InputMode.DualShock4 ? "SHARE" : "CREATE") : "VIEW", "pad-menu"); break;
                case PadButton.Touchpad: Set("TOUCHPAD", "pad-menu"); break;
                default: Set(ps ? "OPTIONS" : "MENU", "pad-menu"); break;
            }
            MarkDirtyRepaint();
        }

        void Face(string letter, string xboxClass, Shape shape, bool ps)
        {
            if (!ps) { Set(letter, "pad-face", xboxClass); return; }
            Symbol = shape;
            Set("", "pad-face", "pad-face--ps", "pad-face--" + shape.ToString().ToLowerInvariant());
        }

        void Set(string text, params string[] classes)
        {
            this.text = text;
            foreach (var c in classes) { AddToClassList(c); own.Add(c); }
        }

        void DrawSymbol(MeshGenerationContext ctx)
        {
            if (Symbol == Shape.None) return;
            var r = contentRect;
            float size = Mathf.Min(r.width, r.height);
            if (size <= 0f) return;
            var c = r.center;
            float s = size * 0.25f;
            var p = ctx.painter2D;
            p.lineWidth = Mathf.Max(1.5f, size * 0.085f);
            p.lineCap = LineCap.Round;
            p.lineJoin = LineJoin.Round;
            p.strokeColor = Symbol switch
            {
                Shape.Cross => CrossColour, Shape.Circle => CircleColour, Shape.Square => SquareColour, _ => TriangleColour,
            };
            p.BeginPath();
            switch (Symbol)
            {
                case Shape.Cross:
                    p.MoveTo(c + new Vector2(-s, -s)); p.LineTo(c + new Vector2(s, s));
                    p.MoveTo(c + new Vector2(s, -s)); p.LineTo(c + new Vector2(-s, s));
                    break;
                case Shape.Circle:
                    p.Arc(c, s * 1.05f, Angle.Degrees(0f), Angle.Degrees(360f));
                    p.ClosePath();
                    break;
                case Shape.Square:
                    float h = s * 0.92f;
                    p.MoveTo(c + new Vector2(-h, -h)); p.LineTo(c + new Vector2(h, -h)); p.LineTo(c + new Vector2(h, h)); p.LineTo(c + new Vector2(-h, h));
                    p.ClosePath();
                    break;
                default:   // the triangle, point up, a little low so it sits in the middle of the circle
                    p.MoveTo(c + new Vector2(0f, -s * 1.05f)); p.LineTo(c + new Vector2(s * 1.12f, s * 0.8f)); p.LineTo(c + new Vector2(-s * 1.12f, s * 0.8f));
                    p.ClosePath();
                    break;
            }
            p.Stroke();
        }
    }
}
