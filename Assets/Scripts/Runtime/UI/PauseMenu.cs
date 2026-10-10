// PauseMenu.cs
// The in-flight pause menu (MenuTouchWindow mode 1, Reference/research/mainmenu_notes.md 2.4): header 40 "Pause", then
// 41 Resume, 129 Missions (from campaign 16; remake: also in the alien orbit, and the Most Wanted criminals on the move),
// 166 Cargo hold (from 2), 31 Options, 59 Action Freeze (PhotoMode), 395 Skip (LevelScript::canSkipCutsceneNow: the prologue / rescue, 154, 157, 158) and 522 Back to Main Menu
// (confirm 523). The game and its sounds pause while it is open. Options is the main menu's Options panel (OptionsView:
// the same tabs and rows, OptionsCatalog) but the text language. Also the ChoiceWindow (Ask: Loma's toll 448, the flight hints). The share buttons (60 / 61) are dead
// code in the original (the remake's 60 saves the picture).
// Plain class driven by FlightHud: Esc / controller Menu / the touch Menu button open it; Esc / B step back.

using System;
using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.World;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace GoF2Remake.UI
{
    public class PauseMenu
    {
        enum Page { Main, Missions, Cargo, Options, Quit, Photo, Choice, Debug }

        readonly VisualElement backdrop, panel, body;
        readonly Label title;
        readonly Action backToMenu;
        readonly List<VisualElement> items = new List<VisualElement>();
        readonly List<Action> actions = new List<Action>();
        Page page;
        int index, openedFrame;
        ScrollView scroll;
        SpaceLevel level;
        bool audioWasPaused;
        float previousTimeScale = 1f;
        /// <summary>The Options page (built anew each time it opens), in place of the panel while it shows.</summary>
        OptionsView options;
        int optionIndex;

        public bool IsOpen { get; private set; }
        /// <summary>ChoiceWindow::set: sound 126 when the confirmation shows.</summary>
        public Action InfoSound;
        /// <summary>59 Action Freeze (MenuTouchWindow button 0x13 -> state 0xd); set by FlightHud.</summary>
        public PhotoMode Photo;

        static string T(int id) => Localization.Get(id).ToUpperInvariant();

        public PauseMenu(VisualElement root, Action backToMenu)
        {
            this.backToMenu = backToMenu;
            backdrop = new VisualElement();
            backdrop.AddToClassList("pause-backdrop");
            panel = new VisualElement();
            panel.AddToClassList("pause-panel");
            title = new Label { pickingMode = PickingMode.Ignore };
            title.AddToClassList("autopilot-menu-title");
            title.AddToClassList("gof-semibold");
            title.AddToClassList("pause-title");
            body = new VisualElement();
            body.AddToClassList("pause-body");
            panel.Add(title);
            panel.Add(body);
            backdrop.Add(panel);
            root.Add(backdrop);
        }

        // ---- open / close -----------------------------------------------------------------------------------

        string choiceText, choiceYes, choiceNo;
        Action choiceOnYes, choiceOnNo;

        /// <summary>A ChoiceWindow in flight (MGame+0x90, e.g. Loma's toll 448): the game and its sounds pause (MGame+0x5d,
        /// pauseSounds) until it is answered; Back picks the second answer. 'no' null = a message with one button.</summary>
        public void Ask(SpaceLevel spaceLevel, string text, string yes, string no, Action onYes, Action onNo)
        {
            choiceText = text;
            choiceYes = yes;
            choiceNo = no;
            choiceOnYes = onYes;
            choiceOnNo = onNo;
            if (!IsOpen) Open(spaceLevel);
            Show(Page.Choice);
        }

        public void Open(SpaceLevel spaceLevel)
        {
            if (IsOpen) return;
            level = spaceLevel;
            IsOpen = true;
            openedFrame = Time.frameCount;
            previousTimeScale = Time.timeScale;
            if (!GoF2Remake.Multiplayer.NetGame.Active) Time.timeScale = 0f;   // multiplayer: the world keeps running
            if (level != null && level.Navigation != null) level.Navigation.PauseMenuOpen = true;
            if (level != null && level.Weapons != null) level.Weapons.SetPrimaryHeld(false);
            audioWasPaused = AudioListener.pause;
            if (!GoF2Remake.Multiplayer.NetGame.Active) AudioListener.pause = true;   // multiplayer: the world (and its sound) goes on
            backdrop.AddToClassList("pause-backdrop--shown");
            Show(Page.Main);
        }

        /// <summary>A scene change with the menu open (a multiplayer session ending): the sound comes back.</summary>
        void OnDestroy()
        {
            if (IsOpen) AudioListener.pause = audioWasPaused;
        }

        public void Close()
        {
            if (!IsOpen) return;
            if (Photo != null && Photo.Active) Photo.Exit();
            IsOpen = false;
            backdrop.RemoveFromClassList("pause-backdrop--shown");
            AudioListener.pause = audioWasPaused;
            if (level != null && level.Navigation != null) level.Navigation.PauseMenuOpen = false;   // restores its own time scale
            else Time.timeScale = previousTimeScale;
            // Options changed here reach the ship at once (SpaceLevel.ApplyOptions).
        }

        // ---- pages --------------------------------------------------------------------------------------------

        void Show(Page p)
        {
            page = p;
            panel.RemoveFromClassList("pause-panel--wide");   // the Debug page's
            if (options != null) { options.Root.RemoveFromHierarchy(); options = null; }
            panel.style.display = StyleKeyword.Null;
            body.Clear();
            items.Clear();
            actions.Clear();
            scroll = null;
            index = 0;
            switch (p)
            {
                case Page.Main:
                    title.text = T(40);
                    Item(T(41), Close);
                    // MenuTouchWindow mode 1: Missions from campaign 16, the cargo hold from 2. The original leaves Missions
                    // out in the alien orbit; the remake keeps it (the objective is needed there too).
                    int cm = Session.WorldIndex;
                    if (cm >= 16) Item(T(129), () => Show(Page.Missions));
                    if (cm >= 2) Item(T(166), () => Show(Page.Cargo));
                    Item(T(31), () => Show(Page.Options));
                    // Remake: the Debug page once the main menu's Debug panel has been opened (Cheats); in multiplayer only
                    // when the session allows it.
                    if (Cheats.PageShown) Item(Localization.Extra("debugTitle", "Debug").ToUpperInvariant(), () => Show(Page.Debug));
                    var campaign = level != null ? level.Campaign : null;
                    if (campaign != null && campaign.CanSkipCutscene) Item(T(395), () => { Close(); campaign.SkipCutscene(); });
                    // MGame::setCinematicMode: not while a cutscene holds the camera.
                    if (Photo != null && level != null && !level.Cutscene) Item(T(59), () => Show(Page.Photo));
                    Item(T(522), () => Show(Page.Quit));
                    break;
                case Page.Photo:
                    backdrop.RemoveFromClassList("pause-backdrop--shown");
                    Photo.Enter(level);
                    if (!Photo.Active) { backdrop.AddToClassList("pause-backdrop--shown"); Show(Page.Main); }
                    return;
                case Page.Choice:
                    title.text = "";
                    Text(choiceText);
                    InfoSound?.Invoke();
                    Item((choiceYes ?? Localization.Extra("ok", "OK")).ToUpperInvariant(), () => { Close(); var a = choiceOnYes; choiceOnYes = choiceOnNo = null; a?.Invoke(); });
                    if (choiceNo != null) Item(choiceNo.ToUpperInvariant(), () => { Close(); var a = choiceOnNo; choiceOnYes = choiceOnNo = null; a?.Invoke(); });
                    break;
                case Page.Quit:
                    title.text = T(522);
                    Text(Localization.Get(523));
                    InfoSound?.Invoke();
                    Item(T(134), () => { Close(); backToMenu?.Invoke(); });
                    Item(T(135), () => Show(Page.Main));
                    index = 1;
                    break;
                case Page.Missions:
                    title.text = T(129);
                    BuildMissions();
                    Item("‹  " + T(170), () => Show(Page.Main));
                    break;
                case Page.Cargo:
                    title.text = T(166);
                    BuildCargo();
                    Item("‹  " + T(170), () => Show(Page.Main));
                    break;
                case Page.Options:
                    BuildOptions();
                    return;
                case Page.Debug:
                    title.text = Localization.Extra("debugTitle", "Debug").ToUpperInvariant();
                    BuildDebug();
                    Item("‹  " + T(170), () => Show(Page.Main));
                    break;
            }
            Highlight();
        }

        void Item(string text, Action action)
        {
            var b = new Button { text = text, focusable = false };
            b.AddToClassList("autopilot-menu-item");
            b.AddToClassList("gof-semibold");
            b.clicked += action;
            body.Add(b);
            items.Add(b);
            actions.Add(action);
        }

        Label Text(string text, string cls = "pause-text")
        {
            var l = new Label(text) { pickingMode = PickingMode.Ignore };
            l.AddToClassList(cls);
            (scroll != null ? (VisualElement)scroll : body).Add(l);
            return l;
        }

        ScrollView Scroll()
        {
            scroll = new ScrollView(ScrollViewMode.Vertical) { horizontalScrollerVisibility = ScrollerVisibility.Hidden };
            scroll.AddToClassList("pause-scroll");
            // A ScrollView doesn't grow with its content: size it to the content, up to 560 px, then it scrolls.
            var sv = scroll;
            sv.contentContainer.RegisterCallback<GeometryChangedEvent>(e => sv.style.height = Mathf.Min(560f, e.newRect.height));
            body.Add(scroll);
            return scroll;
        }

        /// <summary>MissionsWindow's two panels as text: the story objective and the freelance offer.</summary>
        void BuildMissions()
        {
            var db = level != null ? level.Database : null;
            Scroll();
            Text(T(555), "pause-heading");
            bool story = !Session.FreePlay && Story.Step != null && Story.Step.objectiveText >= 0 && db != null;   // as MissionsWindow
            Text(story ? Story.ObjectiveText(db) : Localization.Get(174));
            Text(T(556), "pause-heading");
            var m = Freelance.Mission;
            if (Freelance.Active && db != null)
            {
                string station = db.Stations.Find(s => s.index == m.clientStation)?.name ?? "";
                Text($"{m.clientName} · {station} · {m.Name}", "pause-subheading");
                Text(MissionsWindow.FreelanceText(db, m));
            }
            else Text(Localization.Get(174));
            if (!Session.FreePlay && Session.CampaignMission >= WantedBoard.StorylineFirst && db != null) BuildWanted(db);
        }

        /// <summary>Remake: the Most Wanted criminals on the move (the board itself is only in the stations' Missions window,
        /// WantedWindow): name, bounty, where last seen and where they travel to.</summary>
        void BuildWanted(Database db)
        {
            Text(T(3219), "pause-heading");
            string Place(int station)
            {
                var st = db.Stations.Find(x => x.index == station);
                return st == null ? T(3229) : $"{st.name} ({st.systemName})";
            }
            int shown = 0;
            foreach (var w in db.Wanted)
            {
                var state = WantedBoard.State(db, w.index);
                if (state == null || !state.active || state.terminated) continue;
                Text($"{w.name} · {T(3225)} {ItemInfo.Credits(w.reward)}", "pause-subheading");
                Text($"{T(3223)} {Place(state.lastSeen)}\n{T(3224)} {Place(state.travelsTo)}");
                shown++;
            }
            if (shown == 0) Text(Localization.Get(174));
        }

        /// <summary>The cargo hold: every cargo stack with its tonnage, and the load against the capacity.</summary>
        void BuildCargo()
        {
            var db = level != null ? level.Database : null;
            int load = Shop.CargoLoad(), max = db != null ? Shop.MaxLoad(db) : 0;
            Text($"{load} / {max} t", load > max ? "pause-heading--red" : "pause-heading");
            Scroll();
            if (Session.Cargo.Count == 0) { Text(Localization.Get(174)); return; }
            foreach (var s in Session.Cargo)
            {
                var row = new VisualElement { pickingMode = PickingMode.Ignore };
                row.AddToClassList("pause-cargo-row");
                var icon = new VisualElement { pickingMode = PickingMode.Ignore };
                icon.AddToClassList("pause-cargo-icon");
                var tex = ItemInfo.ItemIcon(s.item);
                if (tex != null) icon.style.backgroundImage = new StyleBackground(tex);
                row.Add(icon);
                var name = new Label(GameNames.Item(s.item)) { pickingMode = PickingMode.Ignore };
                name.AddToClassList("pause-cargo-name");
                row.Add(name);
                var amount = new Label($"{s.amount} t") { pickingMode = PickingMode.Ignore };
                amount.AddToClassList("pause-cargo-amount");
                row.Add(amount);
                scroll.Add(row);
            }
        }

        readonly Dictionary<VisualElement, OptionControl> optionRows = new Dictionary<VisualElement, OptionControl>();

        /// <summary>The Options page: the main menu's Options panel (OptionsView) in place of the pause panel, starting on the
        /// first tab. Its rows don't take focus: Tick moves the highlight (TickOptions).</summary>
        void BuildOptions()
        {
            panel.style.display = DisplayStyle.None;
            options = new OptionsView(() => Show(Page.Main), false);
            options.TabChanged += () => { optionIndex = 0; HighlightOptions(); };
            options.FooterChanged += () => KeepOptionSelected(options.FooterFocus);   // the defaults prompt opened / closed
            options.Root.EnableInClassList("can-hover", InputMode.Current == InputKind.KeyboardMouse);
            backdrop.Add(options.Root);
            optionIndex = 0;
            HighlightOptions();
        }

        /// <summary>After a change rows may come or go (the render scale while DLSS / FSR is on): the highlight stays on 'item'.</summary>
        void KeepOptionSelected(VisualElement item)
        {
            int i = options.NavItems().IndexOf(item);
            if (i >= 0) optionIndex = i;
            HighlightOptions();
        }

        void HighlightOptions()
        {
            if (options == null) return;
            var items = options.NavItems();
            optionIndex = Mathf.Clamp(optionIndex, 0, items.Count - 1);
            options.Select(InputMode.Current != InputKind.Touch ? items[optionIndex] : null);
        }

        /// <summary>The Options page's keys, like the main menu's: up / down walk the tab row, the tab's rows and the footer
        /// (stopping at the ends); left / right switch tabs on the tab row, step a row, or move between Back and Default
        /// settings (or the defaults prompt's buttons); Q / E and LB / RB switch tabs anywhere; Enter / A takes the row or button.</summary>
        void TickOptions(UnityEngine.InputSystem.Keyboard kb, Gamepad pad)
        {
            options.Root.EnableInClassList("can-hover", InputMode.Current == InputKind.KeyboardMouse);
            int tab = 0;
            if ((kb != null && kb.qKey.wasPressedThisFrame) || (pad != null && pad.leftShoulder.wasPressedThisFrame)) tab = -1;
            if ((kb != null && kb.eKey.wasPressedThisFrame) || (pad != null && pad.rightShoulder.wasPressedThisFrame)) tab = 1;
            if (tab != 0) { options.StepTab(tab); return; }

            var items = options.NavItems();
            int move = 0;
            if (kb != null && (kb.wKey.wasPressedThisFrame || kb.upArrowKey.wasPressedThisFrame)) move = -1;
            if (kb != null && (kb.sKey.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame)) move = 1;
            if (pad != null && (pad.dpad.up.wasPressedThisFrame || pad.leftStick.up.wasPressedThisFrame)) move = -1;
            if (pad != null && (pad.dpad.down.wasPressedThisFrame || pad.leftStick.down.wasPressedThisFrame)) move = 1;
            if (move != 0)
            {
                // The footer (Back and Default settings, or the defaults prompt's buttons) is one row: down from it stays,
                // up from any of its buttons goes to the item before it.
                int next = optionIndex + move;
                bool inFooter = options.IsFooter(items[optionIndex]);
                if (move > 0 && inFooter) next = optionIndex;
                if (move < 0 && inFooter) next = options.FooterStart(items) - 1;
                optionIndex = Mathf.Clamp(next, 0, items.Count - 1);
                HighlightOptions();
                return;
            }
            int side = 0;
            if (kb != null && (kb.aKey.wasPressedThisFrame || kb.leftArrowKey.wasPressedThisFrame)) side = -1;
            if (kb != null && (kb.dKey.wasPressedThisFrame || kb.rightArrowKey.wasPressedThisFrame)) side = 1;
            if (pad != null && (pad.dpad.left.wasPressedThisFrame || pad.leftStick.left.wasPressedThisFrame)) side = -1;
            if (pad != null && (pad.dpad.right.wasPressedThisFrame || pad.leftStick.right.wasPressedThisFrame)) side = 1;
            var current = items[optionIndex];
            var row = options.RowOf(current);
            if (side != 0)
            {
                if (options.IsTab(current)) options.StepTab(side);
                else if (row != null) { row.Step(side); KeepOptionSelected(current); }
                else if (options.IsFooter(current)) { optionIndex = items.IndexOf(options.FooterStep(current, side)); HighlightOptions(); }
                return;
            }
            bool confirm = (kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame))
                           || (pad != null && pad.buttonSouth.wasPressedThisFrame);
            if (!confirm) return;
            if (row != null) { row.Activate(); KeepOptionSelected(current); }
            else if (options.IsFooter(current)) options.Activate(current);   // Back, Default settings, the defaults prompt
        }

        // ---- the Debug page ----------------------------------------------------------------------------------

        int debugTab, debugTabCount;
        string debugStatus = "";

        /// <summary>The Debug page (remake-only, CheatsCatalog) on a wide panel: tabs (Cheats, Actions, Give items, Ships and,
        /// in flight, Spawn; click, Q / E or LB / RB), a line with the last action's result, then the tab's rows: the toggles
        /// and actions in two columns, the item and ship pickers with their buttons, the ship and object spawners side by
        /// side.</summary>
        void BuildDebug()
        {
            optionRows.Clear();
            panel.AddToClassList("pause-panel--wide");
            var db = level != null ? level.Database : Database.Load();
            string X(string key, string english) => Localization.Extra(key, english);

            var names = new List<string> { X("debugCheats", "Cheats"), X("debugActions", "Actions"), X("debugItems", "Give items"), X("debugShips", "Ships"), X("debugPresets", "Presets") };
            if (level != null) names.Add(X("debugSpawn", "Spawn"));
            debugTabCount = names.Count;
            debugTab = Mathf.Clamp(debugTab, 0, debugTabCount - 1);
            var tabs = new VisualElement();
            tabs.AddToClassList("debug-tabs");
            for (int i = 0; i < names.Count; i++)
            {
                int tab = i;
                var b = new Button { text = names[i].ToUpperInvariant(), focusable = false };
                b.AddToClassList("debug-tab");
                b.AddToClassList("gof-semibold");
                b.EnableInClassList("debug-tab--active", i == debugTab);
                b.clicked += () => { debugTab = tab; Show(Page.Debug); };
                tabs.Add(b);
            }
            var hint = new Label(InputMode.Current == InputKind.Gamepad ? "LB  ◂  ▸  RB" : InputMode.Current == InputKind.Touch ? "" : "Q  ◂  ▸  E")
                { pickingMode = PickingMode.Ignore };
            hint.AddToClassList("debug-tab-hint");
            tabs.Add(hint);
            body.Add(tabs);

            var status = new Label(debugStatus) { pickingMode = PickingMode.Ignore };
            status.AddToClassList("debug-status");
            status.EnableInClassList("debug-status--empty", string.IsNullOrEmpty(debugStatus));
            body.Add(status);
            void Notify(string text)
            {
                debugStatus = text ?? "";
                status.text = debugStatus;
                status.EnableInClassList("debug-status--empty", debugStatus.Length == 0);
            }

            var content = new VisualElement();
            content.AddToClassList("debug-content");
            body.Add(content);
            void Row(OptionDef def, VisualElement parent, string cls = null)
            {
                var c = new OptionControl(def);
                c.Field.focusable = false;
                c.Changed += () => { foreach (var o in optionRows.Values) o.Refresh(); };   // the item follows its type, labels change
                c.Root.AddToClassList("pause-option");
                if (cls != null) c.Root.AddToClassList(cls);
                parent.Add(c.Root);
                items.Add(c.Root);
                actions.Add(null);
                optionRows[c.Root] = c;
            }
            VisualElement Grid(VisualElement parent)
            {
                var g = new VisualElement();
                g.AddToClassList("debug-grid");
                parent.Add(g);
                return g;
            }
            VisualElement Card(VisualElement parent, string heading)
            {
                var card = new VisualElement();
                card.AddToClassList("debug-card");
                var h = new Label(heading.ToUpperInvariant()) { pickingMode = PickingMode.Ignore };
                h.AddToClassList("debug-card-title");
                h.AddToClassList("gof-semibold");
                card.Add(h);
                parent.Add(card);
                return card;
            }

            switch (debugTab)
            {
                case 0:
                {
                    var grid = Grid(content);
                    foreach (var def in CheatsCatalog.Toggles()) Row(def, grid, "debug-grid-cell");
                    break;
                }
                case 1:
                {
                    var grid = Grid(content);
                    foreach (var def in CheatsCatalog.Actions(db, Notify, level)) Row(def, grid, "debug-grid-cell");
                    break;
                }
                case 2:
                {
                    var defs = CheatsCatalog.Items(db, null, Notify);
                    var card = Card(content, X("debugItems", "Give items"));
                    foreach (var def in defs) if (def.kind != OptionKind.Button) Row(def, card);
                    var buttons = new VisualElement();
                    buttons.AddToClassList("debug-buttons");
                    card.Add(buttons);
                    foreach (var def in defs) if (def.kind == OptionKind.Button) Row(def, buttons, "debug-action");
                    break;
                }
                case 3:
                {
                    // Fly any ship (World.PlayerHull): the picker, then its buttons.
                    var defs = CheatsCatalog.Hulls(db, level, null, Notify);
                    var card = Card(content, X("debugShipsTitle", "Fly any ship"));
                    foreach (var def in defs) if (def.kind != OptionKind.Button) Row(def, card);
                    var buttons = new VisualElement();
                    buttons.AddToClassList("debug-buttons");
                    card.Add(buttons);
                    foreach (var def in defs) if (def.kind == OptionKind.Button) Row(def, buttons, "debug-action");
                    break;
                }
                case 4:
                {
                    // Ship presets (ShipPresets): the slot, then Save / Load / Delete.
                    var defs = CheatsCatalog.Presets(db, level, null, Notify);
                    var card = Card(content, X("debugPresetsTitle", "Ship presets"));
                    foreach (var def in defs) if (def.kind != OptionKind.Button) Row(def, card);
                    var buttons = new VisualElement();
                    buttons.AddToClassList("debug-buttons");
                    card.Add(buttons);
                    foreach (var def in defs) if (def.kind == OptionKind.Button) Row(def, buttons, "debug-action");
                    break;
                }
                default:
                {
                    // CheatsCatalog.Spawns: the ship rows up to "Spawn ship", then the object rows.
                    var defs = CheatsCatalog.Spawns(db, level, Notify);
                    var columns = new VisualElement();
                    columns.AddToClassList("debug-columns");
                    content.Add(columns);
                    var ship = Card(columns, X("debugSpawnShips", "Ship"));
                    var obj = Card(columns, X("debugSpawnObjects", "Object"));
                    obj.AddToClassList("debug-card--last");
                    bool shipDone = false;
                    foreach (var def in defs)
                    {
                        Row(def, shipDone ? obj : ship, def.kind == OptionKind.Button ? "debug-action" : null);
                        if (def.id == "debugSpawnShip") shipDone = true;
                    }
                    break;
                }
            }
        }

        void Highlight()
        {
            bool keys = InputMode.Current != InputKind.Touch;
            for (int i = 0; i < items.Count; i++) items[i].EnableInClassList("autopilot-menu-item--selected", keys && i == index);
        }

        // ---- input (unscaled: the game is paused) -------------------------------------------------------------

        public void Tick()
        {
            if (!IsOpen || Time.frameCount - openedFrame < 1) return;   // the key that opened it
            if (Flight.GameControls.BlocksMenus) return;   // a key binding is being captured (Options)
            if (page == Page.Photo)
            {
                // Back leaves state 0xd for the pause page; the game stays paused.
                if (Photo == null || !Photo.Tick()) { backdrop.AddToClassList("pause-backdrop--shown"); Show(Page.Main); openedFrame = Time.frameCount; }
                return;
            }
            var kb = GoF2Remake.Multiplayer.NetChat.Keys;
            var pad = Gamepad.current;
            if (page == Page.Options && options != null)
            {
                if ((kb != null && (kb.escapeKey.wasPressedThisFrame || kb.backspaceKey.wasPressedThisFrame))
                    || (pad != null && (pad.buttonEast.wasPressedThisFrame || pad.startButton.wasPressedThisFrame)))
                {
                    if (!options.CloseResetPrompt()) Show(Page.Main);   // back closes the defaults prompt first
                    return;
                }
                TickOptions(kb, pad);
                return;
            }
            bool back = (kb != null && (kb.escapeKey.wasPressedThisFrame || kb.backspaceKey.wasPressedThisFrame))
                        || (pad != null && (pad.buttonEast.wasPressedThisFrame || pad.startButton.wasPressedThisFrame));
            if (back)
            {
                if (page == Page.Choice) actions[actions.Count - 1]?.Invoke();   // the second answer (or the only one)
                else if (page == Page.Main) Close(); else Show(Page.Main);
                return;
            }
            // The Debug page's tabs: Q / E, LB / RB.
            if (page == Page.Debug && debugTabCount > 1)
            {
                int tab = 0;
                if ((kb != null && kb.qKey.wasPressedThisFrame) || (pad != null && pad.leftShoulder.wasPressedThisFrame)) tab = -1;
                if ((kb != null && kb.eKey.wasPressedThisFrame) || (pad != null && pad.rightShoulder.wasPressedThisFrame)) tab = 1;
                if (tab != 0) { debugTab = (debugTab + tab + debugTabCount) % debugTabCount; Show(Page.Debug); return; }
            }
            int move = 0;
            if (kb != null && (kb.wKey.wasPressedThisFrame || kb.upArrowKey.wasPressedThisFrame)) move = -1;
            if (kb != null && (kb.sKey.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame)) move = 1;
            if (pad != null && (pad.dpad.up.wasPressedThisFrame || pad.leftStick.up.wasPressedThisFrame)) move = -1;
            if (pad != null && (pad.dpad.down.wasPressedThisFrame || pad.leftStick.down.wasPressedThisFrame)) move = 1;
            bool debugGrid = page == Page.Debug && items.Count > 0 && index >= 0 && index < items.Count;
            if (debugGrid && move != 0 && optionRows.TryGetValue(items[index], out var gridRow) && CheatsCatalog.MoveVertical(gridRow.Field, move))
                move = 0;   // the item grid moved its pick a row
            if (debugGrid && move != 0)
            {
                // The Debug page moves on screen (SpatialNav): the toggles stand two to a row, the Give items buttons
                // side by side; no wrapping round.
                var next = SpatialNav.Next(items, items[index], 0, move, v => v.resolvedStyle.display != DisplayStyle.None);
                if (next != null)
                {
                    index = items.IndexOf(next);
                    Highlight();
                    if (scroll != null && scroll.contentContainer.Contains(next)) scroll.ScrollTo(next);
                }
                move = 0;
            }
            if (move != 0 && items.Count > 0)
            {
                index = (index + move + items.Count) % items.Count;
                Highlight();
                if (scroll != null && scroll.contentContainer.Contains(items[index])) scroll.ScrollTo(items[index]);
                else if (scroll != null) scroll.scrollOffset += new Vector2(0f, move * 120f);
            }
            int side = 0;
            if (kb != null && (kb.aKey.wasPressedThisFrame || kb.leftArrowKey.wasPressedThisFrame)) side = -1;
            if (kb != null && (kb.dKey.wasPressedThisFrame || kb.rightArrowKey.wasPressedThisFrame)) side = 1;
            if (pad != null && (pad.dpad.left.wasPressedThisFrame || pad.leftStick.left.wasPressedThisFrame)) side = -1;
            if (pad != null && (pad.dpad.right.wasPressedThisFrame || pad.leftStick.right.wasPressedThisFrame)) side = 1;
            var current = index < items.Count ? items[index] : null;
            optionRows.TryGetValue(current ?? backdrop, out var option);
            if (side != 0 && debugGrid && (option == null || !option.StepsSideways))
            {
                // Left / right move to the item beside (a toggle used to flip); steppers and sliders still step.
                var next = SpatialNav.Next(items, current, side, 0, v => v.resolvedStyle.display != DisplayStyle.None);
                if (next != null) { index = items.IndexOf(next); Highlight(); current = next; optionRows.TryGetValue(current, out option); }
            }
            else if (side != 0 && option != null) option.Step(side);
            bool confirm = (kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame))
                           || (pad != null && pad.buttonSouth.wasPressedThisFrame);
            if (!confirm || current == null) return;
            if (option != null) option.Activate();
            else actions[index]?.Invoke();
        }
    }
}
