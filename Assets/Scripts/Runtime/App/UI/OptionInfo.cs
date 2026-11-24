// OptionInfo.cs
// Modified: the "?" icon beside an option's name (OptionControl, an OptionDef with a description) and the window with its
// description. The mouse: resting on the icon for 1 s opens it, leaving the icon closes it. Touch: a tap on the icon opens
// it (another tap closes it), a tap anywhere outside the window closes it. One window at a time, laid over the whole UI
// document (the scroll views would clip it), under the icon and kept on screen; it follows the icon while the page
// scrolls and closes when the icon is hidden (another tab, the menu closed). With a controller the description line
// under the row shows instead (GoF2Common.uss .input-gamepad .option-description).

using System;
using UnityEngine;
using UnityEngine.UIElements;
using PointerType = UnityEngine.UIElements.PointerType;

namespace GoF2Remake.UI
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]   // the window rebuilds itself when its panel is gone
    public static class OptionInfo
    {
        /// <summary>How long the mouse rests on the icon before the window opens.</summary>
        public const long HoverDelayMs = 1000;
        public const float IconSize = 22f;
        const float Gap = 6f, Margin = 12f, MaxWidth = 420f;

        static Texture2D iconTexture;
        static VisualElement window;
        static VisualElement owner, host;
        static IVisualElementScheduledItem follow;
        static bool windowIsText;

        /// <summary>A description window is open (Esc / B close it first).</summary>
        public static bool IsOpen => owner != null;

        /// <summary>The icon for one option; 'text' is read each time the window opens (a choice can change it).</summary>
        public static VisualElement Icon(Func<string> text) => Icon(text, null, null);

        /// <summary>Modified: an icon ('texture', else the "?") whose window holds what 'content' builds each time it opens
        /// (the star map's key hints); nothing opens while it builds nothing.</summary>
        public static VisualElement Icon(Func<VisualElement> content, Texture2D texture) => Icon(null, content, texture);

        static VisualElement Icon(Func<string> text, Func<VisualElement> content, Texture2D texture)
        {
            if (iconTexture == null) iconTexture = GoF2Remake.Data.OriginalTextures.Image("GoF2Hud/option_info");
            var icon = new VisualElement { pickingMode = PickingMode.Position, focusable = false };
            icon.AddToClassList("option-info");
            icon.style.position = Position.Absolute;
            icon.style.width = IconSize;
            icon.style.height = IconSize;
            var tex = texture != null ? texture : iconTexture;
            if (tex != null) icon.style.backgroundImage = new StyleBackground(tex);
            // Phones (GoF2Common.uss shows it under layout-phone): a fingertip-sized hit area round the 22-unit icon, more to
            // the left and up and down than to the right (the row's edge). A tap anywhere in it is the icon's.
            var pad = new VisualElement { pickingMode = PickingMode.Position };
            pad.AddToClassList("option-info-pad");
            pad.style.position = Position.Absolute;
            pad.style.left = -14f;
            pad.style.right = -6f;
            pad.style.top = pad.style.bottom = -12f;
            icon.Add(pad);

            // One scheduled item, resumed: Execute(...) registers a new one on the icon every time and Pause only stops it,
            // so a mouse sweeping back and forth over a page's icons left a dead item on each for the panel to walk.
            IVisualElementScheduledItem pending = icon.schedule.Execute(() => Show(icon, text, content));
            pending.Pause();
            icon.RegisterCallback<PointerEnterEvent>(e =>
            {
                if (e.pointerType == PointerType.touch) return;
                pending.ExecuteLater(HoverDelayMs);
            });
            icon.RegisterCallback<PointerLeaveEvent>(e =>
            {
                if (e.pointerType == PointerType.touch) return;
                pending.Pause();
                if (owner == icon) Hide();
            });
            // The icon sits on the row's control: its presses mustn't flip a toggle or start a drag scroll. Modified: a press
            // (touch or mouse) opens it at once, another closes it.
            icon.RegisterCallback<PointerDownEvent>(e =>
            {
                e.StopPropagation();
                pending.Pause();
                if (owner == icon) Hide();
                else Show(icon, text, content);
            });
            icon.RegisterCallback<PointerUpEvent>(e => e.StopPropagation());
            icon.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            icon.RegisterCallback<DetachFromPanelEvent>(_ => { pending.Pause(); if (owner == icon) Hide(); });
            return icon;
        }

        static void Show(VisualElement icon, Func<string> text, Func<VisualElement> content)
        {
            if (icon.panel == null || !Visible(icon)) return;
            VisualElement body;
            if (content != null)
            {
                body = content();
                if (body == null || body.childCount == 0) return;
                body.style.flexShrink = 0f;   // its own size: the window grows to it (it overflowed the 420 px cap)
            }
            else
            {
                string s = text?.Invoke();
                if (string.IsNullOrEmpty(s)) return;
                var l = new Label(s) { pickingMode = PickingMode.Ignore };
                l.style.fontSize = UiLayout.Type(18f);   // the small step: running text (docs/UI-DESIGN.md "Type scale")
                l.style.whiteSpace = WhiteSpace.Normal;
                l.style.color = new Color(0.85f, 0.92f, 0.97f);
                body = l;
            }
            Hide();
            var root = icon;
            while (root.parent != null && root.parent != icon.panel.visualTree) root = root.parent;
            if (window == null || window.panel != null && window.panel != icon.panel) window = Build();
            window.Clear();
            window.Add(body);
            windowIsText = content == null;
            root.Add(window);
            window.BringToFront();
            owner = icon;
            host = root;
            host.RegisterCallback<PointerDownEvent>(OutsidePress, TrickleDown.TrickleDown);
            Place();
            follow = window.schedule.Execute(Follow).Every(100);
        }

        /// <summary>Closes the window (if open).</summary>
        public static void Hide()
        {
            follow?.Pause();
            follow = null;
            host?.UnregisterCallback<PointerDownEvent>(OutsidePress, TrickleDown.TrickleDown);
            host = null;
            owner = null;
            window?.RemoveFromHierarchy();
        }

        static VisualElement Build()
        {
            // Inline styles: the window lands in whichever document the options are in, with or without GoF2Common.uss.
            var l = new VisualElement { pickingMode = PickingMode.Position };
            l.AddToClassList("option-info-window");
            l.style.position = Position.Absolute;
            l.style.paddingLeft = l.style.paddingRight = 14f;
            l.style.paddingTop = l.style.paddingBottom = 10f;
            l.style.backgroundColor = new Color(0.02f, 0.05f, 0.09f, 0.95f);
            l.style.borderLeftWidth = l.style.borderRightWidth = l.style.borderTopWidth = l.style.borderBottomWidth = 2f;
            var grey = new Color(0.59f, 0.62f, 0.65f, 0.8f);
            l.style.borderLeftColor = l.style.borderRightColor = l.style.borderTopColor = l.style.borderBottomColor = grey;
            l.style.borderTopLeftRadius = l.style.borderTopRightRadius = l.style.borderBottomLeftRadius = l.style.borderBottomRightRadius = 4f;
            l.RegisterCallback<GeometryChangedEvent>(_ => Place());
            return l;
        }

        /// <summary>Under the icon; above it when there's no room below; inside the screen. Modified: an icon on the right
        /// half (the map's, the shop's) anchors the window's right edge to its own, so it grows to the left: anchored by its
        /// left edge near the screen's right, the layout squeezed it to the room left and its text ran out of it. A text
        /// window is at most 420 px wide, any window at most the screen less the margins.</summary>
        static void Place()
        {
            if (owner == null || host == null || window == null) return;
            var r = host.WorldToLocal(owner.worldBound);
            float w = window.resolvedStyle.width, h = window.resolvedStyle.height;
            float hostW = host.resolvedStyle.width, hostH = host.resolvedStyle.height;
            if (float.IsNaN(hostW) || float.IsNaN(hostH)) return;   // the follow tick places it once laid out
            float room = Mathf.Max(100f, hostW - 2f * Margin);
            window.style.maxWidth = windowIsText ? Mathf.Min(MaxWidth, room) : room;
            if (r.center.x > hostW * 0.5f)
            {
                window.style.left = StyleKeyword.Auto;
                window.style.right = Mathf.Max(Margin, hostW - r.xMax);
            }
            else
            {
                window.style.right = StyleKeyword.Auto;
                window.style.left = float.IsNaN(w) ? Mathf.Max(Margin, r.xMin) : Mathf.Clamp(r.xMin, Margin, Mathf.Max(Margin, hostW - w - Margin));
            }
            float y = r.yMax + Gap;
            if (!float.IsNaN(h) && y + h > hostH - Margin) y = Mathf.Max(Margin, r.yMin - Gap - h);
            window.style.top = y;
        }

        static void Follow()
        {
            if (owner == null) return;
            if (owner.panel == null || !Visible(owner)) { Hide(); return; }
            Place();
        }

        /// <summary>A touch outside the window (and its icon, which toggles it itself) closes it; the press goes on.</summary>
        static void OutsidePress(PointerDownEvent e)
        {
            if (!(e.target is VisualElement t)) return;
            if (window != null && (t == window || window.Contains(t))) return;
            if (owner != null && (t == owner || owner.Contains(t))) return;
            Hide();
        }

        static bool Visible(VisualElement e)
        {
            for (var v = e; v != null; v = v.parent)
                if (v.resolvedStyle.display == DisplayStyle.None || v.resolvedStyle.visibility == Visibility.Hidden) return false;
            var b = e.worldBound;
            return !float.IsNaN(b.width) && b.width > 0f;
        }
    }
}
