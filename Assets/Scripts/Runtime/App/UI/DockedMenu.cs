// DockedMenu.cs
// Remake (players' wish): docked at an object in space with something to offer (the carrier's / Vossk battleship's resupply
// dock, the Mido Mining Plant: ObjectDocking.Options), the flight HUD shows a menu in the station menu's look instead of
// opening the object's one window at once: the object's name as the title, a column of buttons on the left (the object's
// own options, each opening its own window: the carrier's resupply window, the plant's ore and waste pages), then Inspect
// ship (the photo tools, PauseMenu.OpenInspect) and Undock. FlightHud opens it, hides it under an option's window and shows
// it again once that closes.
// Controls: tap / click a button; up / down (W / S, the D-pad, the left stick) select, Enter / Space / A choose, 1-9 the
// numbered buttons (keyboard), Esc / Backspace / B / Menu undock. Built in code with inline styles (the station menu's USS
// isn't loaded in flight).

using System;
using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace GoF2Remake.UI
{
    public class DockedMenu
    {
        readonly VisualElement panel;
        readonly Label title;
        readonly VisualElement column;
        readonly List<(Button button, Action action)> buttons = new List<(Button, Action)>();
        readonly Action<bool> playButton;
        readonly Action hover;
        int selected;
        int openedFrame;
        int moveDirection;
        /// <summary>The mouse moved last: no key highlight until a key is used again.</summary>
        bool pointer;
        bool shownNow;

        static readonly Color Panel = new Color(0.02f, 0.05f, 0.08f, 0.82f);
        static readonly Color Line = new Color(0.36f, 0.62f, 0.78f, 0.55f);
        static readonly Color Idle = new Color(0.05f, 0.12f, 0.18f, 0.9f);
        static readonly Color Marked = new Color(0.16f, 0.36f, 0.5f, 0.95f);
        static readonly Color Amber = new Color(1f, 0.74f, 0.25f, 1f);

        public bool IsOpen { get; private set; }

        public DockedMenu(VisualElement root, Action<bool> button, Action hoverSound)
        {
            playButton = button;
            hover = hoverSound;
            panel = new VisualElement { name = "dockedMenu", pickingMode = PickingMode.Position };
            var s = panel.style;
            s.position = Position.Absolute;
            s.left = Length.Percent(3);
            s.top = Length.Percent(14);
            s.width = Length.Percent(30);
            s.minWidth = 260;
            s.maxWidth = 460;
            s.paddingLeft = s.paddingRight = 14;
            s.paddingTop = s.paddingBottom = 12;
            s.backgroundColor = Panel;
            s.borderLeftWidth = s.borderRightWidth = s.borderTopWidth = s.borderBottomWidth = 1;
            s.borderLeftColor = s.borderRightColor = s.borderTopColor = s.borderBottomColor = Line;
            s.display = DisplayStyle.None;
            title = new Label { pickingMode = PickingMode.Ignore };
            title.AddToClassList("gof-semibold");
            title.style.fontSize = 26;
            title.style.color = Amber;
            title.style.marginBottom = 10;
            title.style.whiteSpace = WhiteSpace.Normal;
            panel.Add(title);
            // A long list (a mod's object with many options) scrolls, like the autopilot menu.
            var scroll = new ScrollView { focusable = false };
            scroll.style.flexShrink = 1;
            scroll.style.maxHeight = Length.Percent(100);
            column = scroll.contentContainer;
            panel.Add(scroll);
            root.Add(panel);
        }

        /// <summary>Opens with 'heading' over 'entries' (each a button's text and its action, in order).</summary>
        public void Open(string heading, List<(string label, Action action)> entries)
        {
            title.text = (heading ?? "").UiCaps();
            column.Clear();
            buttons.Clear();
            bool numbers = InputMode.Current == InputKind.KeyboardMouse;
            for (int i = 0; i < entries.Count; i++)
            {
                int index = i;
                var e = entries[i];
                string text = (e.label ?? "").UiCaps();
                var b = new Button(() => { pointer = true; Choose(index); }) { text = numbers && i < 9 ? $"{i + 1}. {text}" : text, focusable = false };
                b.AddToClassList("gof-semibold");
                var bs = b.style;
                bs.unityTextAlign = TextAnchor.MiddleLeft;
                bs.fontSize = 20;
                bs.height = 46;
                bs.marginLeft = bs.marginRight = 0;
                bs.marginTop = bs.marginBottom = 3;
                bs.paddingLeft = 14;
                bs.color = Color.white;
                bs.borderLeftWidth = 4;
                bs.borderRightWidth = bs.borderTopWidth = bs.borderBottomWidth = 0;
                bs.borderTopLeftRadius = bs.borderTopRightRadius = bs.borderBottomLeftRadius = bs.borderBottomRightRadius = 0;
                b.RegisterCallback<PointerEnterEvent>(_ => { pointer = true; selected = index; hover?.Invoke(); Highlight(); });
                column.Add(b);
                buttons.Add((b, e.action));
            }
            selected = 0;
            pointer = false;
            IsOpen = true;
            openedFrame = Time.frameCount;
            panel.style.display = DisplayStyle.Flex;
            shownNow = true;
            panel.BringToFront();
            Highlight();
        }

        /// <summary>Closed (an undock, the docking ended another way).</summary>
        public void Hide()
        {
            IsOpen = false;
            shownNow = false;
            panel.style.display = DisplayStyle.None;
        }

        /// <summary>Hidden under an option's window (still open), shown again once it closes.</summary>
        public void SetShown(bool shown)
        {
            if (shown == shownNow) return;
            shownNow = shown;
            panel.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;
            if (shown) openedFrame = Time.frameCount;   // the key that closed the window doesn't choose here too
        }

        void Highlight()
        {
            bool keys = InputMode.Current != InputKind.Touch && !pointer;
            for (int i = 0; i < buttons.Count; i++)
            {
                bool on = i == selected && (keys || pointer);
                var bs = buttons[i].button.style;
                bs.backgroundColor = on ? Marked : Idle;
                bs.borderLeftColor = on ? Amber : Line;
            }
        }

        void Choose(int index)
        {
            if (!IsOpen || index < 0 || index >= buttons.Count) return;
            selected = index;
            Highlight();
            playButton?.Invoke(true);
            buttons[index].action?.Invoke();
        }

        /// <summary>The keys (real time: the game is paused under it). Esc / B choose the last button (Undock).</summary>
        public void Tick()
        {
            if (!IsOpen || buttons.Count == 0 || Time.frameCount - openedFrame < 2) return;
            var kb = GoF2Remake.Multiplayer.NetChat.Keys;
            var pad = Gamepad.current;
            if ((kb != null && (kb.escapeKey.wasPressedThisFrame || kb.backspaceKey.wasPressedThisFrame))
                || (pad != null && (pad.buttonEast.wasPressedThisFrame || pad.startButton.wasPressedThisFrame)))
            {
                Choose(buttons.Count - 1);
                return;
            }
            int dir = 0;
            if (kb != null && (kb.upArrowKey.isPressed || kb.wKey.isPressed)) dir = -1;
            if (kb != null && (kb.downArrowKey.isPressed || kb.sKey.isPressed)) dir = 1;
            if (pad != null)
            {
                float y = pad.dpad.ReadValue().y + pad.leftStick.ReadValue().y;
                if (y > 0.5f) dir = -1; else if (y < -0.5f) dir = 1;
            }
            if (dir != moveDirection)
            {
                moveDirection = dir;
                if (dir != 0)
                {
                    // The first key press after the mouse only shows the highlight where it is.
                    if (!pointer) selected = (selected + dir + buttons.Count) % buttons.Count;
                    pointer = false;
                    hover?.Invoke();
                    Highlight();
                }
            }
            if (kb != null)
            {
                for (int i = 0; i < Mathf.Min(9, buttons.Count); i++)
                    if (kb[Key.Digit1 + i].wasPressedThisFrame || kb[Key.Numpad1 + i].wasPressedThisFrame) { pointer = false; Choose(i); return; }
            }
            if (MenuConfirm.Pressed(kb) || (pad != null && pad.buttonSouth.wasPressedThisFrame)) { pointer = false; Choose(selected); }
        }
    }
}
