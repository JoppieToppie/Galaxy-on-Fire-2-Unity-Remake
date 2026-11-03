// OrbitViewUi.cs
// Remake: the overlay of the two orbit cameras around the player's ship, the hangar's Inspect ship (StationMenu) and the
// flight's Action Freeze (PhotoMode): a title at the top left, the controls as hints at the top right (Hide UI, Screenshot
// and Back clickable, a tap on touch), the Back button in the footer, "Screenshot saved." above it. Styles: .orbit-view-* in
// GoF2Common.uss, so both panels have them.

using System;
using GoF2Remake.Data;
using UnityEngine.UIElements;

namespace GoF2Remake.UI
{
    public static class OrbitViewUi
    {
        public static Label Title()
        {
            var l = new Label { pickingMode = PickingMode.Ignore };
            l.AddToClassList("orbit-view-title");
            l.AddToClassList(SafeArea.EdgeClass);
            l.AddToClassList("gof-semibold");
            return l;
        }

        /// <summary>The footer's Back button; 'push' plays the press sound (the click plays its own).</summary>
        public static Button Back(Action back, Action push)
        {
            var b = new Button(back) { text = Localization.Extra("hudBack", "BACK").OgCase(), focusable = false };
            b.AddToClassList("orbit-view-back");
            b.AddToClassList("gof-semibold");
            if (push != null) b.RegisterCallback<PointerDownEvent>(_ => push(), TrickleDown.TrickleDown);
            return b;
        }

        public static Label Message()
        {
            var l = new Label { pickingMode = PickingMode.Ignore };
            l.AddToClassList("orbit-view-message");
            l.AddToClassList("gof-semibold");
            return l;
        }

        public static VisualElement Hints()
        {
            var h = new VisualElement { pickingMode = PickingMode.Ignore };
            h.AddToClassList("orbit-view-hints");
            h.AddToClassList(SafeArea.EdgeClass);
            return h;
        }

        /// <summary>The controls for this input kind: rotate (the drag and 'rotateKeys'), zoom (the wheel, + / -), Hide UI (H / Y),
        /// Screenshot (Enter / A), Back (Esc / B). The actions play their own release sound; 'push' the press. The photo tools
        /// (PhotoTools): 'freeCam' (F / X) and 'settings' (Tab / View) when given; 'free': the free camera's controls instead of
        /// the orbit's (move, look, up / down, the lens's zoom).</summary>
        public static void BuildHints(VisualElement hints, InputKind kind, string[] rotateKeys,
                                      Action hideUi, Action screenshot, Action back, Action push,
                                      Action freeCam = null, Action settings = null, bool free = false)
        {
            if (hints == null) return;
            hints.Clear();
            string T(string key, string english) => Localization.Extra(key, english);
            void Add(string label, Action click, params VisualElement[] glyphs)
            {
                // The button hints option off: no key hints; on touch the tappable ones stay, they are its only buttons.
                if (!Settings.InputHints && (click == null || kind != InputKind.Touch)) return;
                var h = new VisualElement { pickingMode = click != null ? PickingMode.Position : PickingMode.Ignore };
                h.AddToClassList("hint");
                // Modified: the glyphs in a row of their own (hint-keys), the label under them (GoF2InputGlyphs.uss).
                var keys = new VisualElement { pickingMode = PickingMode.Ignore };
                keys.AddToClassList("hint-keys");
                foreach (var g in glyphs) { g.pickingMode = PickingMode.Ignore; keys.Add(g); }
                h.Add(keys);
                var l = new Label(label) { pickingMode = PickingMode.Ignore };
                l.AddToClassList("hint-label");
                l.AddToClassList("gof-semibold");
                h.Add(l);
                if (click != null)
                {
                    h.AddToClassList("orbit-view-hint--action");
                    if (push != null) h.RegisterCallback<PointerDownEvent>(_ => push());
                    h.RegisterCallback<ClickEvent>(_ => click());
                }
                hints.Add(h);
            }
            string rotate = T("inspectRotate", "ROTATE"), zoom = T("inspectZoom", "ZOOM"), backText = T("hudBack", "BACK");
            string hide = T("inspectHideUi", "HIDE UI"), shot = T("inspectScreenshot", "SCREENSHOT");
            string camText = free ? T("photoOrbitCamera", "ORBIT CAMERA") : T("photoFreeCamera", "FREE CAMERA");
            string settingsText = T("photoSettingsHint", "CAMERA SETTINGS");
            string move = T("photoMove", "MOVE"), look = T("photoLook", "LOOK"), upDown = T("photoUpDown", "UP / DOWN");
            if (free && kind == InputKind.KeyboardMouse)
            {
                Add(move, null, InputGlyph.Key("W"), InputGlyph.Key("A"), InputGlyph.Key("S"), InputGlyph.Key("D"));
                Add(upDown, null, InputGlyph.Key("E"), InputGlyph.Key("Q"));
                Add(look, null, InputGlyph.Key(T("inspectDrag", "DRAG"), true), InputGlyph.Key("↑"), InputGlyph.Key("←"), InputGlyph.Key("↓"), InputGlyph.Key("→"));
                Add(zoom, null, InputGlyph.Key(T("inspectWheel", "WHEEL"), true), InputGlyph.Key("+"), InputGlyph.Key("-"));
            }
            else if (free && kind == InputKind.Gamepad)
            {
                Add(move, null, InputGlyph.Pad(PadButton.LeftStick));
                Add(look, null, InputGlyph.Pad(PadButton.RightStick));
                Add(upDown, null, InputGlyph.Pad(PadButton.RightBumper), InputGlyph.Pad(PadButton.LeftBumper));
                Add(zoom, null, InputGlyph.Pad(PadButton.LeftTrigger), InputGlyph.Pad(PadButton.RightTrigger));
            }
            else if (free)
                Add(T("photoTouchFree", "DRAG TO LOOK · PINCH TO MOVE"), null);
            if (free)
            {
                bool k = kind == InputKind.KeyboardMouse, g = kind == InputKind.Gamepad;
                if (freeCam != null) Add(camText, freeCam, k ? new[] { InputGlyph.Key("F") } : g ? new[] { InputGlyph.Pad(PadButton.X) } : new VisualElement[0]);
                if (settings != null) Add(settingsText, settings, k ? new[] { InputGlyph.Key("TAB", true) } : g ? new[] { InputGlyph.Pad(PadButton.View) } : new VisualElement[0]);
                if (k) { Add(hide, hideUi, InputGlyph.Key("H")); Add(shot, screenshot, InputGlyph.Key("ENTER", true)); Add(backText, back, InputGlyph.Key("ESC")); }
                else if (g) { Add(hide, hideUi, InputGlyph.Pad(PadButton.Y)); Add(shot, screenshot, InputGlyph.Pad(PadButton.A)); Add(backText, back, InputGlyph.Pad(PadButton.B)); }
                else { Add(hide, hideUi); Add(shot, screenshot); }
                return;
            }
            if (kind == InputKind.KeyboardMouse)
            {
                var keys = new VisualElement[rotateKeys.Length + 1];
                keys[0] = InputGlyph.Key(T("inspectDrag", "DRAG"), true);
                for (int i = 0; i < rotateKeys.Length; i++) keys[i + 1] = InputGlyph.Key(rotateKeys[i]);
                Add(rotate, null, keys);
                Add(zoom, null, InputGlyph.Key(T("inspectWheel", "WHEEL"), true), InputGlyph.Key("+"), InputGlyph.Key("-"));
                if (freeCam != null) Add(camText, freeCam, InputGlyph.Key("F"));
                if (settings != null) Add(settingsText, settings, InputGlyph.Key("TAB", true));
                Add(hide, hideUi, InputGlyph.Key("H"));
                Add(shot, screenshot, InputGlyph.Key("ENTER", true));
                Add(backText, back, InputGlyph.Key("ESC"));
            }
            else if (kind == InputKind.Gamepad)
            {
                Add(rotate, null, InputGlyph.Pad(PadButton.LeftStick), InputGlyph.Pad(PadButton.RightStick));
                Add(zoom, null, InputGlyph.Pad(PadButton.LeftTrigger), InputGlyph.Pad(PadButton.RightTrigger));
                if (freeCam != null) Add(camText, freeCam, InputGlyph.Pad(PadButton.X));
                if (settings != null) Add(settingsText, settings, InputGlyph.Pad(PadButton.View));
                Add(hide, hideUi, InputGlyph.Pad(PadButton.Y));
                Add(shot, screenshot, InputGlyph.Pad(PadButton.A));
                Add(backText, back, InputGlyph.Pad(PadButton.B));
            }
            else
            {
                Add(T("inspectTouch", "DRAG TO ROTATE · PINCH TO ZOOM"), null);
                if (freeCam != null) Add(camText, freeCam);
                if (settings != null) Add(settingsText, settings);
                Add(hide, hideUi);
                Add(shot, screenshot);
            }
        }

        /// <summary>A screen point (pixels, y up) on the Back button or a clickable hint: a press there isn't a drag.</summary>
        public static bool OverControl(VisualElement root, Button back, VisualElement hints, UnityEngine.Vector2 screen)
        {
            if (root?.panel == null) return false;
            var p = RuntimePanelUtils.ScreenToPanel(root.panel, new UnityEngine.Vector2(screen.x, UnityEngine.Screen.height - screen.y));
            if (back != null && back.resolvedStyle.display != DisplayStyle.None && back.worldBound.Contains(p)) return true;
            if (hints != null)
                foreach (var h in hints.Children())
                    if (h.pickingMode == PickingMode.Position && h.worldBound.Contains(p)) return true;
            return false;
        }
    }
}
