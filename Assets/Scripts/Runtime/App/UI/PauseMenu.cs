// PauseMenu.cs
// The in-flight pause menu (MenuTouchWindow mode 1, Reference/research/mainmenu_notes.md 2.4): header 40 "Pause", then
// 41 Resume, 129 Missions (from campaign 16; remake: also in the alien orbit, and the Most Wanted criminals on the move),
// 166 Cargo hold (from 2), 31 Options, 59 Action Freeze (PhotoMode), 395 Skip (LevelScript::canSkipCutsceneNow: the prologue / rescue, 154, 157, 158) and 522 Back to Main Menu
// (confirm 523). The game and its sounds pause while it is open. Options is the main menu's Options panel (OptionsView:
// the same tabs and rows, OptionsCatalog) but the text language. Also the ChoiceWindow (Ask: Loma's toll 448, the flight hints). The share buttons (60 / 61) are dead
// code in the original (the remake's 60 saves the picture).
// Plain class driven by FlightHud: Esc / controller Menu / the touch Menu button open it; Esc / B step back; a click / tap
// beside the menu resumes (remake; not on a question, which wants its answer).

using System;
using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.World;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using PointerType = UnityEngine.UIElements.PointerType;

namespace GoF2Remake.UI
{
    public class PauseMenu
    {
        enum Page { Main, Missions, Cargo, Options, Quit, Photo, Choice, Debug }

        readonly VisualElement backdrop, panel;
        VisualElement body, pagePrompts;
        readonly Label title;
        /// <summary>The panel shape's amber line under the title (docs/UI-DESIGN.md "Shapes"): only on the Debug page, a
        /// window like the Options page (OptionsView); the short menus keep their plain title.</summary>
        readonly VisualElement accent;
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
        /// <summary>Modified: the hover sound (the mouse onto an entry, the keys moving the highlight); set by FlightHud.</summary>
        public Action HoverSound;
        /// <summary>Modified: the button sounds (true push, false release): a tab switched with Q / E, an entry taken with the
        /// keys (a click has FlightHud.ButtonSounds); set by FlightHud.</summary>
        public Action<bool> ButtonSound;

        /// <summary>Modified: the mouse moved last: only the entry under it is highlighted (none when it is over nothing), as
        /// in the station's and the main menu's menus; the keys / a controller bring the highlight back.</summary>
        bool pointerDriven;
        /// <summary>The entry (or the Options page's row / tab / footer button) under the mouse, null over nothing.</summary>
        VisualElement pointerItem;

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
            body = NewBody();
            accent = new VisualElement { pickingMode = PickingMode.Ignore };
            accent.AddToClassList("panel-accent");
            accent.style.display = DisplayStyle.None;
            panel.Add(title);
            panel.Add(accent);
            panel.Add(body);
            backdrop.Add(panel);
            root.Add(backdrop);
            // Modified: the mouse drives the highlight while it moves (see pointerDriven), with the menus' hover sound.
            backdrop.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (e.pointerType != PointerType.mouse || pointerDriven || !IsOpen) return;
                pointerDriven = true;
                Rehighlight();
            }, TrickleDown.TrickleDown);
            backdrop.RegisterCallback<PointerOverEvent>(e =>
            {
                if (e.pointerType != PointerType.mouse || !IsOpen || !(e.target is VisualElement t)) return;
                var item = ItemAt(t);
                if (item == pointerItem) return;
                pointerItem = item;
                if (pointerDriven) Rehighlight();
            });
            UiHover.Hook(backdrop, () => HoverSound?.Invoke());   // the tabs only (Options, Debug)
            // Remake: a click / tap on the backdrop beside the menu resumes, as Resume. On the release, so the mouse button
            // isn't still down as fire when the flight takes over, and only for a press that began there too.
            backdrop.RegisterCallback<PointerDownEvent>(e => outsidePress = outsideArmed && e.target == backdrop);
            backdrop.RegisterCallback<PointerUpEvent>(e =>
            {
                bool resume = outsidePress && e.target == backdrop && IsOpen && page != Page.Choice && page != Page.Photo;
                outsidePress = false;
                if (resume) Close();
            });
        }

        /// <summary>The press began outside the menu with nothing over it to close first (outsideArmed).</summary>
        bool outsidePress;
        /// <summary>Taken at the very top of the panel before the press reaches anything: an open drop-down list, "?" window,
        /// "Keep these settings?" or text entry closes on a press outside it, and that press shouldn't also resume.</summary>
        bool outsideArmed;
        IPanel armedPanel;

        void ArmOutsidePress(PointerDownEvent e) =>
            outsideArmed = !ChoicePopup.IsOpen && !OptionInfo.IsOpen && !Flight.GameControls.BlocksMenus;

        /// <summary>Modified: the page's entry at 't' (the Options page: the row, tab or footer button), null when none.</summary>
        VisualElement ItemAt(VisualElement t)
        {
            if (page == Page.Options && options != null)
            {
                foreach (var nav in options.NavItems())
                {
                    var area = options.RowOf(nav)?.Root ?? nav;
                    if (area == t || area.Contains(t)) return nav;
                }
                return null;
            }
            for (var v = t; v != null && v != backdrop; v = v.parent) if (items.Contains(v)) return v;
            return null;
        }

        void Rehighlight()
        {
            if (page == Page.Options) HighlightOptions();
            else Highlight();
        }

        /// <summary>Modified: a key / button moved the highlight: the keys take over from the mouse. True when the mouse was
        /// over nothing: this press only shows the highlight where it was (it doesn't move it yet).</summary>
        bool KeysTakeOver()
        {
            if (!pointerDriven) return false;
            pointerDriven = false;
            return pointerItem == null;
        }

        // ---- open / close -----------------------------------------------------------------------------------

        string choiceText, choiceYes, choiceNo, choiceTitle;
        Action choiceOnYes, choiceOnNo;

        /// <summary>A ChoiceWindow in flight (MGame+0x90, e.g. Loma's toll 448): the game and its sounds pause (MGame+0x5d,
        /// pauseSounds) until it is answered; Back picks the second answer. 'no' null = a message with one button.</summary>
        public void Ask(SpaceLevel spaceLevel, string text, string yes, string no, Action onYes, Action onNo, string title = null)
        {
            choiceTitle = title;   // Modified: a titled question has its answers side by side
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
            backdrop.BringToFront();   // over what was added to the HUD after it (the chat, the multiplayer buttons)
            if (backdrop.panel != null && armedPanel != backdrop.panel)
            {
                armedPanel?.visualTree.UnregisterCallback<PointerDownEvent>(ArmOutsidePress, TrickleDown.TrickleDown);
                armedPanel = backdrop.panel;
                armedPanel.visualTree.RegisterCallback<PointerDownEvent>(ArmOutsidePress, TrickleDown.TrickleDown);
            }
            outsidePress = false;
            pointerDriven = false;   // Modified: opened with a key / button: its first entry is highlighted
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
            accent.style.display = DisplayStyle.None;
            if (options != null) { options.Root.RemoveFromHierarchy(); options = null; }
            panel.style.display = StyleKeyword.Null;
            var oldBody = body;   // a new body per page: FitToContent only grows a scroll view
            body = NewBody();
            oldBody.parent?.Insert(oldBody.parent.IndexOf(oldBody), body);
            oldBody.RemoveFromHierarchy();
            pagePrompts?.RemoveFromHierarchy();
            pagePrompts = null;
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
                    // MenuTouchWindow::MenuTouchWindow 0x14816c: neither in the Supernova Challenge (mission type 0xb7).
                    int cm = SupernovaChallenge.Active ? 0 : Session.WorldIndex;
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
                    // Remake: a Mod Builder test flight goes back to the builder's hangar instead (Modding.ModBuilder).
                    if (Modding.ModBuilder.TestFlying)
                        Item(Localization.Extra("mbBackToBuilder", "Back to Mod Builder").ToUpperInvariant(), () => { Close(); Modding.ModBuilder.EndTestFlight(false); });
                    else if (Modding.ModBuilder.PlayTesting)   // a play test: back to the builder's hangar
                        Item(Localization.Extra("mbBackToBuilder", "Back to Mod Builder").ToUpperInvariant(), () => { Close(); ModBuilderPlay.End(); });
                    else Item(T(522), () => Show(Page.Quit));
                    break;
                case Page.Photo:
                    backdrop.RemoveFromClassList("pause-backdrop--shown");
                    Photo.Enter(level);
                    if (!Photo.Active) { backdrop.AddToClassList("pause-backdrop--shown"); Show(Page.Main); }
                    return;
                case Page.Choice:
                    title.text = choiceTitle ?? "";
                    Text(choiceText);
                    InfoSound?.Invoke();
                    Item((choiceYes ?? Localization.Extra("ok", "OK")).ToUpperInvariant(), () => { Close(); var a = choiceOnYes; choiceOnYes = choiceOnNo = null; a?.Invoke(); });
                    InputGlyph.PadPrompt(items[items.Count - 1] as Button, PadButton.A, true);
                    if (choiceNo != null)
                    {
                        Item(choiceNo.ToUpperInvariant(), () => { Close(); var a = choiceOnNo; choiceOnYes = choiceOnNo = null; a?.Invoke(); });
                        InputGlyph.PadPrompt(items[items.Count - 1] as Button, PadButton.B);
                        // Modified: a titled question (the Supernova Challenge's end) has Yes and No side by side, as the
                        // original's ChoiceWindow; left / right move between them (TickKeys).
                        if (choiceTitle != null)
                        {
                            var row = new VisualElement { pickingMode = PickingMode.Ignore };
                            row.style.flexDirection = FlexDirection.Row;
                            row.style.justifyContent = Justify.Center;
                            row.style.marginTop = 12f;
                            body.Add(row);
                            foreach (var b in new[] { items[items.Count - 2], items[items.Count - 1] })
                            {
                                row.Add(b);
                                b.style.flexGrow = 1f;
                                b.style.flexBasis = 0f;
                                b.style.marginLeft = b.style.marginRight = 8f;
                                b.style.unityTextAlign = TextAnchor.MiddleCenter;
                            }
                        }
                    }
                    break;
                case Page.Quit:
                    title.text = T(522);
                    Text(Localization.Get(523));
                    InfoSound?.Invoke();
                    Item(T(134), () => { Close(); backToMenu?.Invoke(); });
                    InputGlyph.PadPrompt(items[items.Count - 1] as Button, PadButton.A, true);
                    Item(T(135), () => Show(Page.Main));
                    InputGlyph.PadPrompt(items[items.Count - 1] as Button, PadButton.B);
                    index = 1;
                    break;
                case Page.Missions:
                    title.text = T(129);
                    BuildMissions();
                    BackItem(() => Show(Page.Main));
                    break;
                case Page.Cargo:
                    title.text = T(166);
                    BuildCargo();
                    BackItem(() => Show(Page.Main));
                    break;
                case Page.Options:
                    BuildOptions();
                    return;
                case Page.Debug:
                    title.text = Localization.Extra("debugTitle", "Debug").ToUpperInvariant();
                    accent.style.display = DisplayStyle.Flex;
                    BuildDebug();
                    BackItem(() => Show(Page.Main));
                    items[items.Count - 1].AddToClassList("debug-back");   // at the bottom left, as the Options page's
                    break;
            }
            // Rework (docs/UI-DESIGN.md "Button prompts"): the page's keys under it, outside its scrolling body, in the divided
            // row the Options page has (the Debug page's with its tabs and left / right; a question's answers carry their own
            // A / B).
            if (p != Page.Choice && p != Page.Quit)
            {
                pagePrompts = p == Page.Debug ? MenuKeys.DebugPrompts() : MenuKeys.Bar(MenuKeys.Rows.Select | MenuKeys.Rows.Confirm | MenuKeys.Rows.Back);
                pagePrompts.AddToClassList("prompt-bar--row");
                pagePrompts.AddToClassList("prompt-bar--stacked");   // Modified: label under the keys, centred (GoF2InputGlyphs.uss)
                panel.Add(pagePrompts);
            }
            Highlight();
        }

        /// <summary>The "‹ Back" entry, with B on it for a controller (InputGlyph.PadPrompt).</summary>
        void BackItem(Action action)
        {
            Item("‹  " + T(170), action);
            InputGlyph.PadPrompt(items[items.Count - 1] as Button, PadButton.B);
        }

        /// <summary>Rework: a page's body scrolls when it is taller than the panel (a 720-unit-high screen at a large text
        /// size: the last entries and the prompts went under the screen's edge): as tall as its content, shrunk by the panel's
        /// 86 %, the selection kept in view (Highlight).</summary>
        static ScrollView NewBody()
        {
            var sv = new ScrollView(ScrollViewMode.Vertical)
            {
                horizontalScrollerVisibility = ScrollerVisibility.Hidden,
                verticalScrollerVisibility = ScrollerVisibility.Hidden,
            };
            sv.AddToClassList("pause-body");
            FitToContent(sv, float.MaxValue);
            return sv;
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
            FitToContent(scroll, 560f);
            body.Add(scroll);
            return scroll;
        }

        /// <summary>Sizes a ScrollView to its content, up to 'max'. Only ever grows (a page builds a new one each time): the
        /// content's height also follows the view's (in 7000.0.0a10 it alternated between two heights 2 px apart as each set
        /// the other), so following it both ways never settled ("Layout update failed to stabilize", ~2000 errors per page).</summary>
        static void FitToContent(ScrollView sv, float max)
        {
            float set = 0f;
            sv.contentContainer.RegisterCallback<GeometryChangedEvent>(e =>
            {
                // Rework: never more than half the screen's height, so the title, the buttons and the prompts stay on a
                // short panel (720 units on a phone or at a large interface size: 560 pushed Back off the bottom).
                float screen = sv.panel != null ? sv.panel.visualTree.layout.height : 0f;
                float limit = screen > 0f && max < float.MaxValue ? Mathf.Min(max, screen * 0.5f) : max;   // the Debug page: the panel's own limit
                float want = Mathf.Min(limit, Mathf.Ceil(e.newRect.height));
                if (want <= set + 0.5f) return;
                set = want;
                sv.style.height = want;
            });
        }

        /// <summary>MissionsWindow's two panels as text: the story objective and the freelance offer.</summary>
        void BuildMissions()
        {
            var db = level != null ? level.Database : null;
            Scroll();
            Text(T(555), "pause-heading");
            bool story = !Session.FreePlay && Story.Step != null && Story.Step.objectiveText >= 0 && db != null;   // as MissionsWindow
            Text(story ? Story.ObjectiveText(db) : Localization.Extra("missionsNoStory", "No story mission right now."));
            Text(T(556), "pause-heading");
            var m = Freelance.Mission;
            if (Freelance.Active && db != null)
            {
                string station = db.Stations.Find(s => s.index == m.clientStation)?.name ?? "";
                Text($"{m.clientName} · {station} · {m.Name}", "pause-subheading");
                Text(MissionsWindow.FreelanceText(db, m));
            }
            else Text(Localization.Extra("missionsNoFreelance", "No mission taken. The visitors in a Space Lounge offer work."));
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
            if (shown == 0) Text(Localization.Extra("wantedNoneMoving", "No criminals are on the move."));
        }

        /// <summary>The cargo hold: every cargo stack with its tonnage, and the load against the capacity.</summary>
        void BuildCargo()
        {
            var db = level != null ? level.Database : null;
            int load = Shop.CargoLoad(), max = db != null ? Shop.MaxLoad(db) : 0;
            Text($"{load} / {max} t", load > max ? "pause-heading--red" : "pause-heading");
            Scroll();
            if (Session.Cargo.Count == 0) { Text(Localization.Extra("cargoEmpty", "The cargo hold is empty.")); return; }
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
            if (InputMode.Current == InputKind.Touch) { options.Select(null); return; }
            if (pointerDriven)
            {
                // Modified: with the mouse only the row under it (the keys go on from there).
                int under = pointerItem != null ? items.IndexOf(pointerItem) : -1;
                if (under >= 0) optionIndex = under;
                options.Select(under >= 0 ? pointerItem : null, false);
                return;
            }
            options.Select(items[optionIndex]);
        }

        // Held directions repeat (HeldRepeat): up / down walk on, left / right step a slider or a stepper on; a toggle, the tab
        // row and the Debug page's grid move once per press.
        readonly HeldRepeat moveRepeat = new HeldRepeat(), sideRepeat = new HeldRepeat();

        static int Dir(bool negative, bool positive) => positive ? 1 : negative ? -1 : 0;

        int Vertical(UnityEngine.InputSystem.Keyboard kb, Gamepad pad)
        {
            bool upPressed = (kb != null && (kb.wKey.wasPressedThisFrame || kb.upArrowKey.wasPressedThisFrame))
                             || (pad != null && (pad.dpad.up.wasPressedThisFrame || pad.leftStick.up.wasPressedThisFrame));
            bool downPressed = (kb != null && (kb.sKey.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame))
                               || (pad != null && (pad.dpad.down.wasPressedThisFrame || pad.leftStick.down.wasPressedThisFrame));
            bool upHeld = (kb != null && (kb.wKey.isPressed || kb.upArrowKey.isPressed)) || (pad != null && (pad.dpad.up.isPressed || pad.leftStick.up.isPressed));
            bool downHeld = (kb != null && (kb.sKey.isPressed || kb.downArrowKey.isPressed)) || (pad != null && (pad.dpad.down.isPressed || pad.leftStick.down.isPressed));
            return moveRepeat.Tick(Dir(upPressed, downPressed), Dir(upHeld, downHeld));
        }

        int Sideways(UnityEngine.InputSystem.Keyboard kb, Gamepad pad)
        {
            bool leftPressed = (kb != null && (kb.aKey.wasPressedThisFrame || kb.leftArrowKey.wasPressedThisFrame))
                               || (pad != null && (pad.dpad.left.wasPressedThisFrame || pad.leftStick.left.wasPressedThisFrame));
            bool rightPressed = (kb != null && (kb.dKey.wasPressedThisFrame || kb.rightArrowKey.wasPressedThisFrame))
                                || (pad != null && (pad.dpad.right.wasPressedThisFrame || pad.leftStick.right.wasPressedThisFrame));
            bool leftHeld = (kb != null && (kb.aKey.isPressed || kb.leftArrowKey.isPressed)) || (pad != null && (pad.dpad.left.isPressed || pad.leftStick.left.isPressed));
            bool rightHeld = (kb != null && (kb.dKey.isPressed || kb.rightArrowKey.isPressed)) || (pad != null && (pad.dpad.right.isPressed || pad.leftStick.right.isPressed));
            return sideRepeat.Tick(Dir(leftPressed, rightPressed), Dir(leftHeld, rightHeld));
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
            if (tab != 0) { ButtonSound?.Invoke(false); options.StepTab(tab); return; }   // Modified: the tab's sound

            var items = options.NavItems();
            int move = Vertical(kb, pad);
            // Modified: the keys take over from the mouse (the first press only shows the highlight when it was over nothing).
            if (move != 0 && KeysTakeOver()) { HighlightOptions(); HoverSound?.Invoke(); return; }
            if (move != 0)
            {
                // The footer (Back and Default settings, or the defaults prompt's buttons) is one row: down from it stays,
                // up from any of its buttons goes to the item before it.
                int next = optionIndex + move;
                bool inFooter = options.IsFooter(items[optionIndex]);
                if (move > 0 && inFooter) next = optionIndex;
                if (move < 0 && inFooter) next = options.FooterStart(items) - 1;
                next = Mathf.Clamp(next, 0, items.Count - 1);
                if (next != optionIndex) HoverSound?.Invoke();   // Modified: the arrows' sound
                optionIndex = next;
                HighlightOptions();
                return;
            }
            int side = Sideways(kb, pad);
            if (side != 0 && KeysTakeOver()) { HighlightOptions(); HoverSound?.Invoke(); return; }
            var current = items[optionIndex];
            var row = options.RowOf(current);
            if (side != 0)
            {
                if (options.IsTab(current)) { if (sideRepeat.Fresh) { ButtonSound?.Invoke(false); options.StepTab(side); } }
                else if (row != null) { if (sideRepeat.Fresh || row.StepsSideways) row.Step(side); KeepOptionSelected(current); }
                else if (options.IsFooter(current))
                {
                    int next = items.IndexOf(options.FooterStep(current, side));
                    if (next != optionIndex) HoverSound?.Invoke();
                    optionIndex = next;
                    HighlightOptions();
                }
                return;
            }
            bool confirm = (kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame))
                           || (pad != null && pad.buttonSouth.wasPressedThisFrame);
            if (!confirm) return;
            if (pointerDriven && pointerItem == null) { KeysTakeOver(); HighlightOptions(); return; }   // Modified: nothing marked yet
            ButtonSound?.Invoke(false);   // Modified: the entry's sound (a click has its own)
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
            // Modified: a "?" with what the page is for at the tab row's end; its keys as a prompt row under the page.
            MenuKeys.AddDebugIcons(tabs);
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

            // The rows scroll when the screen is short (FlightHud.uss .debug-scroll: the panel's 86 % of the screen is the limit,
            // the list as tall as its rows up to there); the selection keeps itself in view (Highlight).
            var debugScroll = new ScrollView(ScrollViewMode.Vertical) { horizontalScrollerVisibility = ScrollerVisibility.Hidden };
            debugScroll.AddToClassList("debug-scroll");
            debugScroll.AddManipulator(new DragScroll(debugScroll));
            FitToContent(debugScroll, float.MaxValue);
            body.Add(debugScroll);
            scroll = debugScroll;
            var content = new VisualElement();
            content.AddToClassList("debug-content");
            debugScroll.Add(content);
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
                    foreach (var def in CheatsCatalog.TogglesByColumn()) Row(def, grid, "debug-grid-cell");
                    break;
                }
                case 1:
                {
                    var grid = Grid(content);
                    foreach (var def in CheatsCatalog.Actions(db, Notify, level)) Row(def, grid, "debug-grid-cell");
                    // Destroy around you (World.DebugSpawner.KillAround): range and kind, then the button.
                    if (level == null) break;
                    var defs = CheatsCatalog.Destroy(level, Notify);
                    var card = Card(content, X("debugKillTitle", "Destroy around you"));
                    card.AddToClassList("debug-card--below");
                    foreach (var def in defs) if (def.kind != OptionKind.Button) Row(def, card);
                    var buttons = new VisualElement();
                    buttons.AddToClassList("debug-buttons");
                    card.Add(buttons);
                    foreach (var def in defs) if (def.kind == OptionKind.Button) Row(def, buttons, "debug-action");
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
            // Modified: with the mouse only the entry under it (none over nothing); the keys go on from there.
            int under = pointerDriven && pointerItem != null ? items.IndexOf(pointerItem) : -1;
            if (under >= 0) index = under;
            int marked = !keys ? -1 : pointerDriven ? under : index;
            for (int i = 0; i < items.Count; i++) items[i].EnableInClassList("autopilot-menu-item--selected", i == marked);
            // A long page (the Debug page on a short screen): the selected row scrolls into view (not under the mouse).
            if (keys && !pointerDriven && scroll != null && index >= 0 && index < items.Count && scroll.contentContainer.Contains(items[index])) scroll.ScrollTo(items[index]);
            else if (keys && !pointerDriven && body is ScrollView page && index >= 0 && index < items.Count && page.contentContainer.Contains(items[index])) page.ScrollTo(items[index]);
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
                    // An option's open drop-down or its "?" window closes first, as in the main menu (MainMenu.EscBack):
                    // both are laid over the whole document, so a page closed under one left it drawing over the next.
                    if (ChoicePopup.IsOpen) ChoicePopup.Close();
                    else if (OptionInfo.IsOpen) OptionInfo.Hide();
                    else if (!options.CloseResetPrompt()) Show(Page.Main);   // back closes the defaults prompt first
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
                if (tab != 0) { ButtonSound?.Invoke(false); debugTab = (debugTab + tab + debugTabCount) % debugTabCount; Show(Page.Debug); return; }   // Modified: the tab's sound
            }
            int move = Vertical(kb, pad);
            int sideFirst = Sideways(kb, pad);
            // Modified: the keys take over from the mouse; over nothing the first press only shows the highlight.
            if ((move != 0 || sideFirst != 0) && KeysTakeOver()) { Highlight(); HoverSound?.Invoke(); return; }
            int indexBefore = index;
            bool gridMoved = false;
            bool debugGrid = page == Page.Debug && items.Count > 0 && index >= 0 && index < items.Count;
            if (debugGrid && move != 0 && optionRows.TryGetValue(items[index], out var gridRow) && CheatsCatalog.MoveVertical(gridRow.Field, move))
            {
                move = 0;   // the item grid moved its pick a row
                gridMoved = true;
            }
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
                // A press wraps round at the ends, a held key stops there.
                index = moveRepeat.Fresh ? (index + move + items.Count) % items.Count : Mathf.Clamp(index + move, 0, items.Count - 1);
                Highlight();
                if (scroll != null && scroll.contentContainer.Contains(items[index])) scroll.ScrollTo(items[index]);
                else if (scroll != null) scroll.scrollOffset += new Vector2(0f, move * 120f);
            }
            int side = sideFirst;
            // Modified: a titled question's answers stand side by side: left / right move between them.
            if (side != 0 && page == Page.Choice && choiceTitle != null && items.Count > 1)
            {
                int next = Mathf.Clamp(index + side, 0, items.Count - 1);
                if (next != index) { index = next; Highlight(); HoverSound?.Invoke(); }
                side = 0;
            }
            var current = index < items.Count ? items[index] : null;
            optionRows.TryGetValue(current ?? backdrop, out var option);
            if (side != 0 && debugGrid && (option == null || !option.StepsSideways))
            {
                // Left / right move to the item beside (a toggle used to flip); steppers and sliders still step.
                var next = SpatialNav.Next(items, current, side, 0, v => v.resolvedStyle.display != DisplayStyle.None);
                if (next != null) { index = items.IndexOf(next); Highlight(); current = next; optionRows.TryGetValue(current, out option); }
            }
            else if (side != 0 && option != null && (sideRepeat.Fresh || option.StepsSideways)) option.Step(side);
            if (index != indexBefore || gridMoved) HoverSound?.Invoke();   // Modified: the arrows' sound
            bool confirm = (kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame))
                           || (pad != null && pad.buttonSouth.wasPressedThisFrame);
            if (!confirm || current == null) return;
            if (pointerDriven && pointerItem == null) { KeysTakeOver(); Highlight(); return; }   // Modified: nothing marked yet
            ButtonSound?.Invoke(false);   // Modified: the entry's sound (a click has its own)
            if (option != null) option.Activate();
            else actions[index]?.Invoke();
        }
    }
}
