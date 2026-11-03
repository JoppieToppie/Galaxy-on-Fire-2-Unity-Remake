// MultiplayerWindowBuilding.cs
// MultiplayerWindow: building the window (part of the partial MultiplayerWindow; its fields are in MultiplayerWindow.cs).

using System;
using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Multiplayer;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace GoF2Remake.UI
{
    public partial class MultiplayerWindow
    {
        void Build(VisualElement parent)
        {
            sosButton?.RemoveFromHierarchy();
            sosButton = null;
            plate?.RemoveFromHierarchy();
            window?.RemoveFromHierarchy();
            sheet = Resources.Load<StyleSheet>("GoF2Net/Squad");
            host = parent;
            large = UiScale.Large(parent);
            scale = large ? LargeScale : 1f;

            // The button: in the top bar, left of the Menu button (its look); else under the station's information.
            plate = new VisualElement { name = "factionPlate" };
            var menu = parent.Q<Button>("menuButton");
            menuButton = menu;
            if (flight)
            {
                // In flight: on the right, under the HUD readout (top right) and over the squad window (26 %).
                plateButton = Btn(Localization.Extra("mpMultiplayer", "Multiplayer"), Toggle, large ? "squad-button--big" : null);
                plateButton.AddToClassList("mp-hud-button");   // the size of the station's Menu button and the Chat button
                if (sheet != null) plateButton.styleSheets.Add(sheet);
                // The two buttons stacked in one column on the right (bigger now, like the Menu and Chat buttons: placed
                // separately in percentages they overlapped on a phone).
                flightColumn?.RemoveFromHierarchy();
                flightColumn = new VisualElement { pickingMode = PickingMode.Ignore };
                flightColumn.style.position = Position.Absolute;
                flightColumn.style.right = 24;
                flightColumn.style.top = new Length(17, LengthUnit.Percent);
                flightColumn.style.alignItems = Align.FlexEnd;
                parent.Add(flightColumn);
                plateButton.style.marginLeft = 0;
                plate = plateButton;
                flightColumn.Add(plateButton);
                // The Chat button (ChatView) on the left side at the same height, the Multiplayer button's look.
                var chatTab = parent.Q<Button>("chatTab");
                if (chatTab != null)
                {
                    LookLike(chatTab, plateButton);
                    chatTab.style.position = Position.Absolute;
                    chatTab.style.left = 40;   // the status bars' left edge
                    chatTab.style.top = new Length(17, LengthUnit.Percent);
                    chatTab.style.marginLeft = chatTab.style.marginRight = chatTab.style.marginTop = chatTab.style.marginBottom = 0;
                    parent.Add(chatTab);
                }
                // The squad's distress call (NetDistress) under it: shown in a squad in space. A click / tap or the
                // "Distress call" binding (unbound by default) only: like the button above it never takes the focus, so
                // Space / Enter / a controller's A can't press it by accident.
                sosButton = Btn(Localization.Extra("mpDistressCall", "Distress call"), ToggleDistress, "squad-button--leave");
                if (large) sosButton.AddToClassList("squad-button--big");
                sosButton.AddToClassList("mp-hud-button");
                if (sheet != null) sosButton.styleSheets.Add(sheet);
                sosButton.focusable = false;
                sosButton.style.marginLeft = 0;
                sosButton.style.marginTop = 6;
                sosButton.style.display = DisplayStyle.None;
                flightColumn.Add(sosButton);
            }
            else if (menu != null && menu.parent != null)
            {
                plateButton = new Button(Toggle) { name = "netButton" };
                plateButton.AddToClassList("station-menu-button");
                plateButton.AddToClassList("gof-semibold");
                plateButton.style.marginRight = 12;
                plate = plateButton;
                menu.parent.Insert(menu.parent.IndexOf(menu), plateButton);
                // The Chat button (ChatView) next to it, on its left, the same size (the bar's buttons).
                var chatTab = parent.Q<Button>("chatTab");
                if (chatTab != null)
                {
                    LookLike(chatTab, plateButton);
                    chatTab.style.marginTop = chatTab.style.marginBottom = 0;
                    chatTab.style.marginRight = 12;
                    chatTab.style.marginLeft = new StyleLength(StyleKeyword.Auto);   // pushes the group right, like the bar's first button
                    menu.parent.Insert(menu.parent.IndexOf(plateButton), chatTab);
                    plateButton.style.marginLeft = 0;
                }
            }
            else
            {
                if (sheet != null) plate.styleSheets.Add(sheet);
                plateButton = Btn(Localization.Extra("mpFactionArena", "Faction · Arena"), Open, null);
                plateButton.style.marginTop = 8;
                plate.Add(plateButton);
                var info = parent.Q(className: "station-info");
                if (info != null && info.parent != null)
                {
                    int at = info.parent.IndexOf(info) + 1;
                    var code = info.parent.Q("joinCodePlate");
                    if (code != null && code.parent == info.parent) at = info.parent.IndexOf(code) + 1;
                    info.parent.Insert(at, plate);
                }
                else
                {
                    plate.style.position = Position.Absolute;
                    plate.style.left = 24; plate.style.top = 140;
                    parent.Add(plate);
                }
            }

            // A click / tap (or the "Multiplayer window" binding, N) opens it; never the focus (Space or a controller's A on a
            // focused button pressed it again in flight).
            if (plateButton != null) plateButton.focusable = false;

            window = new VisualElement { name = "factionWindow" };
            if (sheet != null) window.styleSheets.Add(sheet);
            var w = window.style;
            w.position = Position.Absolute;
            w.left = new Length(50, LengthUnit.Percent); w.top = new Length(50, LengthUnit.Percent);
            w.translate = new Translate(new Length(-50, LengthUnit.Percent), new Length(-50, LengthUnit.Percent));
            w.minWidth = 560;
            ApplyWindowSize();
            w.backgroundColor = new Color(Panel.r, Panel.g, Panel.b, 1f);   // opaque: at 0.96 the station labels behind still read through
            window.AddToClassList("mpw-window");   // High contrast's solid panel (GoF2Common.uss)
            w.borderTopWidth = w.borderBottomWidth = w.borderLeftWidth = w.borderRightWidth = 1;
            w.borderTopColor = w.borderBottomColor = w.borderLeftColor = w.borderRightColor = new Color(Accent.r, Accent.g, Accent.b, 0.45f);
            w.paddingTop = w.paddingBottom = 12; w.paddingLeft = w.paddingRight = 18;
            w.display = DisplayStyle.None;
            // A press on the window holds a new snapshot back until it is released (a rebuild under the finger dropped the
            // button being pressed: "Claim" did nothing).
            window.RegisterCallback<PointerDownEvent>(_ => pressedSince = Time.unscaledTime, TrickleDown.TrickleDown);
            window.RegisterCallback<NavigationMoveEvent>(OnNavigate, TrickleDown.TrickleDown);
            UiFocus.SkipScrollBars(window);   // the D-pad never stops on the list's scroll bar
            window.RegisterCallback<PointerUpEvent>(_ => pressedSince = -1f, TrickleDown.TrickleDown);
            window.RegisterCallback<PointerCancelEvent>(_ => pressedSince = -1f, TrickleDown.TrickleDown);
            // The list follows the focus (a controller's D-pad, the keys).
            window.RegisterCallback<FocusInEvent>(e =>
            {
                if (e.target is VisualElement v && body != null && body.Contains(v)) scroll.ScrollTo(v);
            });

            var head = Row();
            head.style.justifyContent = Justify.SpaceBetween;
            head.style.flexWrap = Wrap.NoWrap;          // the × stays at the right; the tabs take a second line instead
            head.style.alignItems = Align.FlexStart;
            tabs = Row();
            tabs.AddToClassList("tab-row");   // the shared tab row (docs/UI-DESIGN.md "Tabs")
            tabs.style.flexGrow = 1;
            tabs.style.flexShrink = 1;
            tabs.style.marginRight = 12;
            head.Add(tabs);
            head.Add(Btn("×", Close, "squad-button--leave"));
            window.Add(head);
            scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddManipulator(new DragScroll(scroll));   // a mouse drag moves the list like a finger (the menus' lists)
            scroll.style.flexGrow = 1;
            scroll.style.marginTop = 10;
            body = scroll.contentContainer;
            window.Add(scroll);
            BuildChatPane();
            window.Add(chatPane);
            status = Text("", 15, Accent);
            status.style.marginTop = 8;
            status.style.whiteSpace = WhiteSpace.Normal;
            window.Add(status);
            // Which build this is (testers compare it): the version and the code's branch / commit.
            var build = Text(string.Format(Localization.Extra("mpBuildLine", "build {0}"), BuildVersion.Text), 12, Dim);
            build.style.alignSelf = Align.FlexEnd;
            build.style.marginTop = 4;
            window.Add(build);
            // Rework: a modal's backdrop under it (it lay over the station's labels and the cargo plate); a press on it closes
            // the window.
            scrim = new VisualElement();
            scrim.style.position = Position.Absolute;
            scrim.style.left = scrim.style.right = scrim.style.top = scrim.style.bottom = 0;
            scrim.style.backgroundColor = new Color(0f, 0f, 0f, 0.6f);
            scrim.style.display = DisplayStyle.None;
            scrim.RegisterCallback<PointerDownEvent>(e => { Close(); e.StopPropagation(); });
            parent.Add(scrim);
            parent.Add(window);
            BuildTabs();
        }
        void LookLike(Button chatTab, Button neighbour)
        {
            movedChat = chatTab;
            chatTab.RemoveFromClassList("chat-tab");
            foreach (var c in neighbour.GetClasses()) chatTab.AddToClassList(c);
            if (sheet != null && !chatTab.styleSheets.Contains(sheet)) chatTab.styleSheets.Add(sheet);
        }
        void BuildTabs()
        {
            tabs.Clear();
            int role = NetPanel.Latest != null ? NetPanel.Latest.role : 0;
            shownRole = role;
            if (tab == Tab.Admin && role < NetModeration.Op) tab = Tab.Faction;
            foreach (Tab t in Enum.GetValues(typeof(Tab)))
            {
                if (t == Tab.Admin && role < NetModeration.Op) continue;   // the moderation tab: ops and admins only
                var name = t == Tab.Faction ? Localization.Extra("mpTabFaction", "Faction") : t == Tab.Arena ? Localization.Extra("mpTabArena", "Arena")
                         : t == Tab.Profile ? Localization.Extra("mpTabProfile", "Profile") : Localization.Extra("mpTabAdmin", "Admin");
                if (t == Tab.Chat) name = Localization.Extra("mpChat", "Chat");
                if (t == Tab.Squad) name = Localization.Extra("mpTabSquad", "Squad");
                if (t == Tab.Trade) name = Localization.Extra("mpTabTrade", "Trade");
                // Rework (docs/UI-DESIGN.md "Tabs"): the shared tab look (it was a row of small boxed buttons).
                var b = new Button(() => SelectTab(t)) { text = name.UiCaps() };
                b.AddToClassList("tab-button");
                b.AddToClassList("gof-semibold");
                b.EnableInClassList("tab-button--active", t == tab);
                tabs.Add(b);
                if (t == tab) currentTabButton = b;
            }
            if (isOpen && FocusesItself) FocusLater(currentTabButton);
        }
        void SelectTab(Tab t)
        {
            tab = t;
            confirmLeave = false;
            BuildTabs();
            ShowPane();
            Rebuild();
        }
        /// <summary>LB / RB (Q / E): the previous / next tab this player sees, wrapping.</summary>
        void StepTab(int dir)
        {
            var shown = new List<Tab>();
            int role = NetPanel.Latest != null ? NetPanel.Latest.role : 0;
            foreach (Tab t in Enum.GetValues(typeof(Tab))) if (t != Tab.Admin || role >= NetModeration.Op) shown.Add(t);
            int i = Mathf.Max(0, shown.IndexOf(tab));
            SelectTab(shown[(i + dir + shown.Count) % shown.Count]);
        }
        static void FocusLater(VisualElement e) => e?.schedule.Execute(() => { if (e.panel != null) e.Focus(); }).ExecuteLater(1);
        /// <summary>The window gives itself the focus when it opens: with keys or a controller, not under the mouse or a
        /// finger (where a click or tap picks, and a frame on the tab would only puzzle).</summary>
        static bool FocusesItself => InputMode.Current != InputKind.Touch && !InputMode.MousePointing;
        /// <summary>Everything in the window that can take the focus and is on screen (the tabs, the close button, the
        /// shown pane's buttons and fields), for its own navigation; a scroll bar is never one (UiFocus.SkipScrollBars).</summary>
        List<VisualElement> NavItems()
        {
            var items = new List<VisualElement>();
            if (window == null) return items;
            foreach (var e in window.Query<VisualElement>().ToList())
            {
                if (!e.focusable || !Navigable(e)) continue;
                bool inScroller = false;
                for (var p = e; p != null && !inScroller; p = p.parent) inScroller = p is Scroller;
                if (!inScroller) items.Add(e);
            }
            return items;
        }
        /// <summary>The focus is on one of the open window's own controls: only then do the keys reach its navigation
        /// (a navigation event travels from the panel's root to whatever has the focus).</summary>
        public static bool FocusInside
        {
            get
            {
                var w = current;
                if (w == null || !w.isOpen || w.window == null) return false;
                return w.window.focusController?.focusedElement is VisualElement f && (f == w.window || w.window.Contains(f));
            }
        }
        /// <summary>The focus steps into the open window (its tab row, else its first control), for a key pressed while it
        /// is still outside: the window was opened with the mouse or a tap.</summary>
        public static void FocusFirstAny()
        {
            var w = current;
            if (w == null || !w.isOpen) return;
            if (w.currentTabButton != null && Navigable(w.currentTabButton)) { w.currentTabButton.Focus(); return; }
            var items = w.NavItems();
            if (items.Count > 0) items[0].Focus();
        }
        /// <summary>On screen and usable: laid out (a pane that isn't shown has no size), enabled and visible.</summary>
        static bool Navigable(VisualElement e) =>
            e != null && e.enabledInHierarchy && e.visible && e.resolvedStyle.display != DisplayStyle.None
            && e.worldBound.height > 0.5f && e.worldBound.width > 0.5f;
        /// <summary>Keys / D-pad inside the window: the neighbour on screen among its own controls (SpatialNav), scrolled
        /// into view. The station menu walks its own buttons for the whole panel and yields to an open window
        /// (StationMenu.OnNavigate), so without this the arrows moved the menu behind it instead.</summary>
        void OnNavigate(NavigationMoveEvent e)
        {
            if (!isOpen) return;
            var from = window.focusController?.focusedElement as VisualElement;
            // A field keeps the keys it is typing with (left / right move the caret), but up / down still step out of it
            // and a controller always navigates (TextFieldKeys.IsTyping): the window opens on the Chat tab, whose line
            // takes the focus by itself, and a blanket "chat typing" check left the keys dead in the whole window.
            if (TextFieldKeys.IsTyping(e, from)) return;
            var items = NavItems();
            if (items.Count == 0) return;
            int dx = e.direction == NavigationMoveEvent.Direction.Left ? -1 : e.direction == NavigationMoveEvent.Direction.Right ? 1 : 0;
            int dy = e.direction == NavigationMoveEvent.Direction.Up ? -1 : e.direction == NavigationMoveEvent.Direction.Down ? 1 : 0;
            var next = from == null || !items.Contains(from) ? items[0] : SpatialNav.Next(items, from, dx, dy, Navigable);
            if (next != null)
            {
                next.Focus();
                if (body != null && body.Contains(next)) scroll.ScrollTo(next);
            }
            e.StopPropagation();
            window.focusController?.IgnoreEvent(e);
        }
        /// <summary>The window's size: 64 x 78 % centred; large, almost the whole screen; raised (a phone typing into a
        /// field), the top half, clear of the on-screen keyboard.</summary>
        void ApplyWindowSize()
        {
            var w = window.style;
            // Large: under the top-right buttons (the flight HUD's pause button, the station's Menu): its × overlapped them.
            // Rework: 84 % up to 1500 units (64 % took the seven tabs onto two lines on a TV, at 1:1 and on phones).
            w.width = new Length(large ? 96 : 84, LengthUnit.Percent);
            w.maxWidth = large ? StyleKeyword.None : new StyleLength(1500f);
            w.top = new Length(raised ? 1 : large ? 55 : 50, LengthUnit.Percent);
            w.height = new Length(raised ? 50 : large ? 82 : 78, LengthUnit.Percent);
            w.translate = new Translate(new Length(-50, LengthUnit.Percent), new Length(raised ? 0 : -50, LengthUnit.Percent));
            window.EnableInClassList("mpw--large", large);
        }
        /// <summary>Once a second: the large variant (a resolution change); every frame: raised while a phone types.</summary>
        void UpdateLayout()
        {
            bool phoneTyping = typing && Application.isMobilePlatform;
            if (phoneTyping != raised)
            {
                raised = phoneTyping;
                ApplyWindowSize();
                if (raised && window.focusController?.focusedElement is VisualElement f)
                    f.schedule.Execute(() => { if (body != null && body.Contains(f)) scroll.ScrollTo(f); }).ExecuteLater(50);
            }
            if (Time.unscaledTime < nextScaleCheck || host == null || host.panel == null) return;
            nextScaleCheck = Time.unscaledTime + 1f;
            bool l = UiScale.Large(host);
            if (l == large) return;
            large = l;
            scale = l ? LargeScale : 1f;
            ApplyWindowSize();
            plateButton?.EnableInClassList("squad-button--big", l && flight);
            movedChat?.EnableInClassList("squad-button--big", l && flight);
            sosButton?.EnableInClassList("squad-button--big", l);
            if (isOpen) Rebuild();
        }
        /// <summary>LB / RB and Q / E switch the tabs while the window is open and no field is being typed into.</summary>
        void UpdateTabKeys()
        {
            if (!isOpen || typing || GoF2Remake.Flight.GameControls.BlocksMenus) return;
            var pad = Gamepad.current;
            var kb = Keyboard.current;
            if ((pad != null && pad.leftShoulder.wasPressedThisFrame) || (kb != null && kb.qKey.wasPressedThisFrame)) StepTab(-1);
            else if ((pad != null && pad.rightShoulder.wasPressedThisFrame) || (kb != null && kb.eKey.wasPressedThisFrame)) StepTab(1);
        }
    }
}
