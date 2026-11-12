// InputMode.cs
// Which kind of input the player is using right now: touch, keyboard/mouse or a controller. Switches on the
// first real input of another kind (a touch, a key press, a mouse move or click, a button or a stick pushed past a
// dead zone), on phones and PCs alike, so UI can show touch controls, keyboard hints or controller hints.
// Starts as Touch on mobile, Gamepad if one is connected on other platforms, else KeyboardMouse.
// Remake: the controller hints' button style (PadStyle): the controller last used picks it (a Sony pad = PlayStation, a
// Nintendo one = Switch, any other = Xbox), unless Options > Controls "Controller button icons" (Settings.PadGlyphs)
// forces one. A Nintendo pad's A / B swapped in its events (Settings.NintendoLayout, #162): A confirms, as on a Switch.

using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using GoF2Remake.Data;

namespace GoF2Remake.UI
{
    public enum InputKind { Touch, KeyboardMouse, Gamepad }

    /// <summary>How the controller hints label the buttons: Xbox (A / B / X / Y, LB / RB, LT / RT, View / Menu),
    /// PlayStation (cross / circle / square / triangle, L1 / R1, L2 / R2, Create or Share / Options) or Switch (Nintendo's
    /// letters where its buttons sit, L / R, ZL / ZR, − / +; #162).</summary>
    public enum PadStyle { Xbox, PlayStation, Switch }

    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class InputMode
    {
        const float StickThreshold = 0.35f;   // sticks drift; only count a deliberate push
        const float MouseThreshold = 4f;      // pixels per event

        public static InputKind Current { get; private set; }

        /// <summary>A real keyboard was used this run (always on desktops): the key bindings' keyboard cells only take a
        /// capture then. A phone without one waited for a key that never came.</summary>
        public static bool KeyboardSeen { get; private set; }

        /// <summary>Raised on the main thread when Current changes.</summary>
        public static event Action Changed;

        /// <summary>The style of the controller last used (Xbox until a Sony or Nintendo one is used).</summary>
        public static PadStyle DetectedStyle { get; private set; }

        /// <summary>The controller last used is a DualShock 4: its Create button is called Share.</summary>
        public static bool DualShock4 { get; private set; }

        /// <summary>The controller hints' style: the option's (Settings.PadGlyphs), on Auto the controller last used's.</summary>
        public static PadStyle Style => Settings.PadGlyphs switch
        {
            Settings.PadGlyphsXbox => PadStyle.Xbox,
            Settings.PadGlyphsPlayStation => PadStyle.PlayStation,
            Settings.PadGlyphsSwitch => PadStyle.Switch,
            _ => DetectedStyle,
        };

        /// <summary>The controller last used is a Nintendo one whose A / B are swapped (Settings.NintendoLayout): the
        /// position-named buttonSouth is then its A (right), buttonEast its B (bottom), and the Switch labels follow.</summary>
        public static bool NintendoSwap => Settings.NintendoLayout && lastPadNintendo;

        /// <summary>Raised on the main thread when Style (or the Share / Create name, or the Nintendo swap) changes: another
        /// controller was used or an option changed. The pad glyphs redraw themselves; GameControls passes it on as its Changed.</summary>
        public static event Action StyleChanged;

        static InputDevice lastPad;
        static bool lastPadNintendo;
        static PadStyle shownStyle;
        static bool shownDs4, shownSwap;
        // the last device the swap looked at (events come from one pad at a time, so its answer is kept)
        static int nintendoCheckedId = -1;
        static bool nintendoChecked;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            Current = Application.isMobilePlatform ? InputKind.Touch
                    : Gamepad.current != null ? InputKind.Gamepad : InputKind.KeyboardMouse;
            KeyboardSeen = !Application.isMobilePlatform;
            lastPad = Gamepad.current;
            DetectedStyle = StyleOf(lastPad);
            DualShock4 = IsDualShock4(lastPad);
            lastPadNintendo = IsNintendo(lastPad);
            nintendoCheckedId = -1;
            shownStyle = Style;
            shownDs4 = DualShock4;
            shownSwap = NintendoSwap;
            InputSystem.onEvent -= OnEvent;   // domain reload may be off: never subscribe twice
            InputSystem.onEvent += OnEvent;
            Settings.Changed -= CheckStyle;
            Settings.Changed += CheckStyle;   // the option (and Default settings)
        }

        /// <summary>The button style of a controller: PlayStation for Sony's (the Input System's DualShock / DualSense
        /// layouts on every platform, or a pad that reports Sony as its maker, e.g. an Android one without its own layout),
        /// Switch for Nintendo's (IsNintendo), else Xbox. Null = Xbox.</summary>
        public static PadStyle StyleOf(InputDevice device) =>
            device == null ? PadStyle.Xbox : StyleOf(device.layout, device.description.manufacturer, device.description.product);

        /// <summary>StyleOf for a layout name and the device's reported maker / product (testable without a device).</summary>
        public static PadStyle StyleOf(string layout, string manufacturer, string product)
        {
            if (IsNintendo(layout, manufacturer, product)) return PadStyle.Switch;
            if (!string.IsNullOrEmpty(layout) && BasedOn(layout, "DualShockGamepad")) return PadStyle.PlayStation;
            if (Has(manufacturer, "Sony")) return PadStyle.PlayStation;
            if (Has(product, "DualSense") || Has(product, "DualShock") || Has(product, "PlayStation")) return PadStyle.PlayStation;
            return PadStyle.Xbox;
        }

        /// <summary>A DualShock 4 (Share), not a DualSense (Create).</summary>
        public static bool IsDualShock4(InputDevice device) =>
            device != null && IsDualShock4(device.layout, device.description.product);

        public static bool IsDualShock4(string layout, string product) =>
            (!string.IsNullOrEmpty(layout) && layout.IndexOf("DualShock4", StringComparison.OrdinalIgnoreCase) >= 0)
            || Has(product, "DualShock 4") || Has(product, "DualShock4");

        /// <summary>A Nintendo controller (#162): the Input System's Switch Pro layout (desktop HID), or a pad reporting
        /// Nintendo as its maker or a Pro Controller / Joy-Con as its product (Android, other layouts). Their buttons are
        /// named by position there too: buttonSouth is the pad's B, buttonEast its A.</summary>
        public static bool IsNintendo(InputDevice device) =>
            device != null && IsNintendo(device.layout, device.description.manufacturer, device.description.product);

        public static bool IsNintendo(string layout, string manufacturer, string product)
        {
            if (!string.IsNullOrEmpty(layout) && (BasedOn(layout, "SwitchProControllerHID") || Has(layout, "SwitchPro"))) return true;
            if (Has(manufacturer, "Nintendo")) return true;
            if (Has(product, "Nintendo") || Has(product, "Joy-Con")) return true;
            return string.Equals(product?.Trim(), "Pro Controller", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>A Nintendo pad's event with its buttonSouth and buttonEast exchanged (Settings.NintendoLayout): the
        /// Input System names them by position (south = the pad's B), so before the event is applied its A (right) becomes
        /// what every binding and direct read takes as the confirm button. Runs from InputSystem.onEvent, which the Input
        /// System calls after a device's own pre-processing (a Switch Pro's HID report is its own state by then) and before
        /// the state is written. An event that doesn't hold both buttons (a delta elsewhere) is left alone.</summary>
        internal static void SwapFaceButtons(InputEventPtr e, Gamepad pad)
        {
            if (pad == null) return;
            if (!pad.buttonSouth.ReadValueFromEvent(e, out float south) || !pad.buttonEast.ReadValueFromEvent(e, out float east)) return;
            if (south == east) return;
            pad.buttonSouth.WriteValueIntoEvent(east, e);
            pad.buttonEast.WriteValueIntoEvent(south, e);
        }

        static bool Has(string s, string part) => !string.IsNullOrEmpty(s) && s.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0;

        static bool BasedOn(string layout, string baseLayout)
        {
            try { return string.Equals(layout, baseLayout, StringComparison.OrdinalIgnoreCase) || InputSystem.IsFirstLayoutBasedOnSecond(layout, baseLayout); }
            catch (ArgumentException) { return false; }   // a layout the Input System doesn't know
        }

        static void UsedPad(InputDevice device)
        {
            if (device == lastPad) return;
            lastPad = device;
            DetectedStyle = StyleOf(device);
            DualShock4 = IsDualShock4(device);
            lastPadNintendo = IsNintendo(device);
            CheckStyle();
        }

        static void CheckStyle()
        {
            var style = Style;
            bool swap = NintendoSwap;
            if (style == shownStyle && DualShock4 == shownDs4 && swap == shownSwap) return;
            shownStyle = style;
            shownDs4 = DualShock4;
            shownSwap = swap;
            StyleChanged?.Invoke();
        }

        static void OnEvent(InputEventPtr e, InputDevice device)
        {
            if (!e.IsA<StateEvent>() && !e.IsA<DeltaStateEvent>()) return;
            if (device is Gamepad nintendoPad && Settings.NintendoLayout)
            {
                if (device.deviceId != nintendoCheckedId) { nintendoCheckedId = device.deviceId; nintendoChecked = IsNintendo(device); }
                if (nintendoChecked) SwapFaceButtons(e, nintendoPad);
            }
            InputKind kind;
            switch (device)
            {
                case Touchscreen _:
                    kind = InputKind.Touch;
                    MousePointing = false;
                    break;
                case Gamepad _:
                    if (!HasControlAbove(e, device, StickThreshold)) return;
                    kind = InputKind.Gamepad;
                    MousePointing = false;
                    UsedPad(device);
                    break;
                case Keyboard keyboard:
                    if (!HasControlAbove(e, device, 0.5f)) return;
                    kind = InputKind.KeyboardMouse;
                    MousePointing = false;
                    // Android's back button arrives as Escape: only another key shows a real keyboard.
                    if (!KeyboardSeen)
                        foreach (var c in e.EnumerateChangedControls(device, 0.5f))
                            if (c != keyboard.escapeKey) { KeyboardSeen = true; break; }
                    break;
                case Mouse mouse:
                    // Touches on some platforms also arrive as a simulated mouse: ignore those.
                    if (Current == InputKind.Touch && Touchscreen.current != null && Touchscreen.current.primaryTouch.isInProgress) return;
                    if (mouse.delta.ReadValueFromEvent(e, out Vector2 moved) && moved != Vector2.zero) { lastMouseMove = Time.unscaledTime; MousePointing = true; }
                    if (!MouseUsed(e, mouse)) return;
                    MousePointing = true;
                    kind = InputKind.KeyboardMouse;
                    break;
                default:
                    return;
            }
            ApplyCursor(kind);
            if (kind == Current) return;
            Current = kind;
            Changed?.Invoke();
        }

        static float lastMouseMove = -1f;

        /// <summary>The mouse itself moved in the last moment. A menu that gives the focus to what the pointer enters asks this
        /// first: a list scrolled by the keys or a controller slides its rows under a resting cursor, and they took the focus
        /// (a player's report: scrolling up, the selection jumped back to the row under the cursor at the bottom).</summary>
        public static bool MouseMovedRecently => MousePointing && Time.unscaledTime - lastMouseMove < 0.15f;

        /// <summary>The mouse was the last thing used (moved or clicked; a key, a controller or a touch since turns it off).
        /// The menus show hover highlights (.can-hover) only then: a cursor resting over a row kept that row lit while the
        /// keys or a controller moved the selection elsewhere, two rows highlighted at once, at every station docked at with
        /// the cursor still there (a player's report, 2026-10-18).</summary>
        public static bool MousePointing { get; private set; }

        /// <summary>Remake (a player's report): the mouse cursor shows only while the keyboard and mouse (or touch) are in use,
        /// not with a controller; a mouse move brings it back. Desktop only; a captured cursor (mouse steering) stays hidden.</summary>
        public static bool CursorShown => Application.isMobilePlatform || Current != InputKind.Gamepad;

        static void ApplyCursor(InputKind kind)
        {
            if (Application.isMobilePlatform || Cursor.lockState == CursorLockMode.Locked) return;
            bool shown = kind != InputKind.Gamepad;
            if (Cursor.visible != shown) Cursor.visible = shown;
        }

        /// <summary>True if the event sets any control of the device to at least this magnitude (a pressed button = 1).</summary>
        static bool HasControlAbove(InputEventPtr e, InputDevice device, float threshold)
        {
            foreach (var control in e.EnumerateChangedControls(device, threshold)) return true;
            return false;
        }

        static bool MouseUsed(InputEventPtr e, Mouse mouse)
        {
            if (mouse.delta.ReadValueFromEvent(e, out Vector2 d) && d.sqrMagnitude >= MouseThreshold * MouseThreshold) return true;
            if (mouse.scroll.ReadValueFromEvent(e, out Vector2 scroll) && scroll != Vector2.zero) return true;
            if (mouse.middleButton.ReadValueFromEvent(e, out float m) && m > 0.5f) return true;
            if (mouse.leftButton.ReadValueFromEvent(e, out float l) && l > 0.5f) return true;
            return mouse.rightButton.ReadValueFromEvent(e, out float r) && r > 0.5f;
        }
    }
}
