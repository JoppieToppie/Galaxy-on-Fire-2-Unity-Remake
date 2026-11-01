// StationMenuSystemMenu.cs
// Part of StationMenu: the system menu (Menu 172): its pages, the Options and Debug pages, and the save / load slots.

using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Modding;
using GoF2Remake.World;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using PointerType = UnityEngine.UIElements.PointerType;

namespace GoF2Remake.UI
{
    public partial class StationMenu
    {
        Button SystemButton(string text, int index, System.Action action, VisualElement parent)
        {
            var b = new Button { text = text.ToUpperInvariant() };
            b.AddToClassList("station-button");
            b.AddToClassList("gof-semibold");
            b.RegisterCallback<PointerDownEvent>(_ => Play(buttonPush), TrickleDown.TrickleDown);
            b.clicked += () => { Play(buttonRelease); action(); };
            if (index < 0 || index > parent.childCount) parent.Add(b); else parent.Insert(index, b);
            return b;
        }

        void BuildSystemExtras()
        {
            newGameButton = SystemButton(Localization.Get(28), 0, () => ShowDialog(Localization.Get(523), () =>
            {
                MainMenu.OpenPanelOnStart = "campaignPanel";   // MenuTouchWindow: the new game's campaign choice
                BackToMenu();
            }), systemMain);
            loadGameButton = SystemButton(Localization.Get(29), 1, () => ShowSystemPage(SysPage.Load), systemMain);
            optionsButton = SystemButton(Localization.Get(31), 3, () => ShowSystemPage(SysPage.Options), systemMain);
            aboutButton = SystemButton(Localization.Get(43), 4, () => { ShowDialog(AboutText.Get(), null, true); AboutText.Hook(root.Q<Label>("dialogText")); }, systemMain);
            // Modified: no New game and About in the station's menu (the main menu has them); Back at the panel's top right,
            // like the shop's Back (StationMenu.uss .system-menu-close), on the menu's own page only.
            newGameButton.style.display = DisplayStyle.None;
            aboutButton.style.display = DisplayStyle.None;
            var menuPanel0 = root.Q(className: "system-menu");
            if (menuPanel0 != null && systemClose != null) { menuPanel0.Add(systemClose); systemClose.AddToClassList("system-menu-close"); }
            // Remake: the Debug page once the main menu's Debug panel has been opened (Cheats); in multiplayer whenever the
            // session allows it (made in every session: a server's admin may switch it on or off meanwhile, ShowSystemPage).
            if (Cheats.PageShown || GoF2Remake.Multiplayer.NetGame.Active)
                debugButton = SystemButton(Localization.Extra("debugTitle", "Debug"), 5, () => ShowSystemPage(SysPage.Debug), systemMain);
            // Multiplayer: a session's game is never saved, and no single-player save is loaded into it; the co-op
            // campaign's host saves its own game into the slots (SaveGame.HostSaves).
            if (GoF2Remake.Multiplayer.NetGame.Active)
                foreach (var b in new[] { loadGameButton, saveGameButton })
                    if (b != null && !(b == saveGameButton && SaveGame.HostSaves)) b.style.display = DisplayStyle.None;
            // Hardcore: the game saves itself when docking (the auto-save) and nowhere else.
            if (Session.Hardcore && saveGameButton != null) saveGameButton.style.display = DisplayStyle.None;
            // The Debug page's scroll list (Options is an OptionsView, BuildOptionsView).
            systemOptions = new VisualElement();
            systemOptions.AddToClassList("system-menu-page");
            optionsScroll = new ScrollView(ScrollViewMode.Vertical) { horizontalScrollerVisibility = ScrollerVisibility.Hidden, verticalScrollerVisibility = ScrollerVisibility.Hidden };
            optionsScroll.AddToClassList("slot-list");
            optionsScroll.AddToClassList("system-slot-list");
            new DragScroll(optionsScroll);
            systemOptions.Add(optionsScroll);
            // "‹ Back" at the bottom left, the Options window's (OptionsView); the keys go in the divided row under it
            // (BuildStationDebug).
            optionsBack = SystemButton("‹  " + Localization.Extra("hudBack", "BACK"), -1, () => ShowSystemPage(SysPage.Main), systemOptions);
            optionsBack.AddToClassList("system-menu-back");
            systemMain.parent.Add(systemOptions);
        }

        /// <summary>The Options page like the main menu's (OptionsView: its tabs, rows, Back and Default settings), over
        /// the menu's own panel; the rows take the panel's focus like the menu's buttons.</summary>
        void BuildOptionsView()
        {
            optionsView = new OptionsView(() => { Play(buttonRelease); ShowSystemPage(SysPage.Main); }, true);
            foreach (var b in new[] { optionsView.BackButton, optionsView.DefaultsButton, optionsView.CancelButton, optionsView.ResetTabButton, optionsView.ResetAllButton })
                b.RegisterCallback<PointerDownEvent>(_ => Play(buttonPush), TrickleDown.TrickleDown);
            optionsView.DefaultsRestored += () => Play(buttonRelease);
            optionsView.FooterChanged += () => Select(optionsView.FooterFocus);   // the defaults prompt opened / closed
            optionsView.Changed += c => { if (c.def.kind == OptionKind.Choice || c.def.kind == OptionKind.Toggle) Play(buttonRelease); };
            optionsView.TabChanged += () => { Play(buttonRelease); Select(optionsView.ActiveTab); };   // Modified: a tab's sound
            systemMenu.Add(optionsView.Root);
        }

        void BuildStationOptions()
        {
            optionsScroll.Clear();
            stationOptions.Clear();
            BuildStationDebug();
        }

        /// <summary>The Debug page like the pause menu's (remake-only, CheatsCatalog): tabs Cheats / Actions / Give items (click,
        /// Q / E or LB / RB), the toggles and actions in two columns, the item pickers with "Add to hold" / "Add and mount";
        /// results as toasts.</summary>
        void BuildStationDebug()
        {
            string X(string key, string english) => Localization.Extra(key, english);
            var names = new[] { X("debugCheats", "Cheats"), X("debugActions", "Actions"), X("debugItems", "Give items"), X("debugShips", "Ships"), X("debugPresets", "Presets") };
            stationDebugTab = Mathf.Clamp(stationDebugTab, 0, names.Length - 1);
            var tabs = new VisualElement();
            tabs.AddToClassList("debug-tabs");
            for (int i = 0; i < names.Length; i++)
            {
                int tab = i;
                // Modified: focusable, so the keys / a controller reach the tab row (up from the first row; left / right switch).
                var b = new Button { text = names[i].ToUpperInvariant(), focusable = true };
                if (i == stationDebugTab) activeDebugTab = b;
                b.AddToClassList("debug-tab");
                b.AddToClassList("gof-semibold");
                b.EnableInClassList("debug-tab--active", i == stationDebugTab);
                b.clicked += () => { Play(buttonRelease); SwitchDebugTab(tab - stationDebugTab); };   // Modified: its sound
                tabs.Add(b);
            }
            // Modified: a "?" with what the page is for at the tab row's end. Its keys: the divided row at the page's bottom,
            // under Back and outside the scrolling list, as the Options window has them (they were under the tabs, at
            // the list's top: at its end they had been out of view).
            MenuKeys.AddDebugIcons(tabs);
            optionsScroll.Add(tabs);
            debugPrompts?.RemoveFromHierarchy();
            debugPrompts = MenuKeys.DebugPrompts();
            debugPrompts.AddToClassList("prompt-bar--row");
            systemOptions.Add(debugPrompts);

            void Row(OptionDef def, VisualElement parent, string cls = null)
            {
                var c = new OptionControl(def);
                c.Changed += () => { foreach (var o in stationOptions) if (o != c) o.Refresh(); };   // the item follows its type
                c.Root.AddToClassList("system-option");
                if (cls != null) c.Root.AddToClassList(cls);
                var root0 = c.Root;
                c.Field.RegisterCallback<FocusInEvent>(_ => { if (!DragScroll.PointerActive) optionsScroll.ScrollTo(root0); });
                parent.Add(c.Root);
                stationOptions.Add(c);
            }
            VisualElement Box(VisualElement parent, string cls)
            {
                var v = new VisualElement();
                v.AddToClassList(cls);
                parent.Add(v);
                return v;
            }
            var db = level.Database;
            void Notify(string text) { ShowToast(text); RefreshCredits(); }
            switch (stationDebugTab)
            {
                case 0:
                {
                    var grid = Box(optionsScroll.contentContainer, "debug-grid");
                    foreach (var def in CheatsCatalog.TogglesByColumn()) Row(def, grid, "debug-grid-cell");
                    break;
                }
                case 1:
                {
                    var grid = Box(optionsScroll.contentContainer, "debug-grid");
                    foreach (var def in CheatsCatalog.Actions(db, Notify)) Row(def, grid, "debug-grid-cell");
                    break;
                }
                case 3:
                {
                    // Fly any ship (World.PlayerHull): docked only a ship the player can normally own.
                    var defs = CheatsCatalog.Hulls(db, null, level, Notify);
                    var card = Box(optionsScroll.contentContainer, "debug-card");
                    var title = new Label(X("debugShipsTitle", "Fly any ship").ToUpperInvariant()) { pickingMode = PickingMode.Ignore };
                    title.AddToClassList("debug-card-title");
                    title.AddToClassList("gof-semibold");
                    card.Add(title);
                    foreach (var def in defs) if (def.kind != OptionKind.Button) Row(def, card);
                    var buttons = Box(card, "debug-buttons");
                    foreach (var def in defs) if (def.kind == OptionKind.Button) Row(def, buttons, "debug-action");
                    break;
                }
                case 4:
                {
                    // Ship presets (ShipPresets): docked, a preset of a ship the hangar can't take is refused.
                    var defs = CheatsCatalog.Presets(db, null, level, Notify);
                    var card = Box(optionsScroll.contentContainer, "debug-card");
                    var title = new Label(X("debugPresetsTitle", "Ship presets").ToUpperInvariant()) { pickingMode = PickingMode.Ignore };
                    title.AddToClassList("debug-card-title");
                    title.AddToClassList("gof-semibold");
                    card.Add(title);
                    foreach (var def in defs) if (def.kind != OptionKind.Button) Row(def, card);
                    var buttons = Box(card, "debug-buttons");
                    foreach (var def in defs) if (def.kind == OptionKind.Button) Row(def, buttons, "debug-action");
                    break;
                }
                default:
                {
                    var defs = CheatsCatalog.Items(db, level.Stock, Notify);
                    var card = Box(optionsScroll.contentContainer, "debug-card");
                    var title = new Label(names[2].ToUpperInvariant()) { pickingMode = PickingMode.Ignore };
                    title.AddToClassList("debug-card-title");
                    title.AddToClassList("gof-semibold");
                    card.Add(title);
                    foreach (var def in defs) if (def.kind != OptionKind.Button) Row(def, card);
                    var buttons = Box(card, "debug-buttons");
                    foreach (var def in defs) if (def.kind == OptionKind.Button) Row(def, buttons, "debug-action");
                    break;
                }
            }
        }

        /// <summary>The Debug page's tab 'step' tabs on (wrapping), rebuilt with the first row selected.</summary>
        void SwitchDebugTab(int step)
        {
            if (step == 0) return;
            stationDebugTab = ((stationDebugTab + step) % StationDebugTabs + StationDebugTabs) % StationDebugTabs;
            ShowSystemPage(SysPage.Debug);
        }

        void OpenSystemMenu()
        {
            if (SystemMenuOpen || level == null || ArrivalPending) return;
            CloseHangar();
            focusBeforeSystemMenu = root.focusController?.focusedElement as VisualElement;
            systemMenu.AddToClassList("station-dialog-backdrop--shown");
            ShowSystemPage(SysPage.Main);
        }

        void CloseSystemMenu()
        {
            if (!SystemMenuOpen) return;
            systemMenu.RemoveFromClassList("station-dialog-backdrop--shown");
            if (root.focusController?.focusedElement is VisualElement f) f.Blur();
            // Rework (docs/UI-DESIGN.md "Focus"): back where it was before the menu opened, else the view's button. It went to
            // Launch, so Menu, B and then A out of habit took off.
            var back = focusBeforeSystemMenu;
            focusBeforeSystemMenu = null;
            Select(back != null && back.panel != null && back.enabledInHierarchy && back.resolvedStyle.display != DisplayStyle.None ? back : DefaultFocus());
            BuildHints(InputMode.Current);
        }

        void ShowSystemPage(SysPage page)
        {
            sysPage = page;
            bool slots = page == SysPage.Save || page == SysPage.Load;
            systemMain.EnableInClassList("system-menu-page--shown", page == SysPage.Main);
            if (systemClose != null) systemClose.style.display = page == SysPage.Main ? StyleKeyword.Null : DisplayStyle.None;
            systemSave.EnableInClassList("system-menu-page--shown", slots);
            systemOptions?.EnableInClassList("system-menu-page--shown", page == SysPage.Debug);
            var menuPanel = root.Q(className: "system-menu");
            menuPanel?.EnableInClassList("system-menu--wide", page == SysPage.Debug);
            menuPanel?.EnableInClassList("system-menu--slots", slots);
            if (optionsView != null) { optionsView.Root.RemoveFromHierarchy(); optionsView = null; }
            if (menuPanel != null) menuPanel.style.display = page == SysPage.Options ? DisplayStyle.None : StyleKeyword.Null;
            if (page == SysPage.Options) BuildOptionsView();
            int title = page == SysPage.Save ? 30 : page == SysPage.Load ? 29 : page == SysPage.Options ? 31 : 172;   // Menu
            var titleLabel = root.Q<Label>("systemMenuTitle");
            titleLabel.text = page == SysPage.Debug ? Localization.Extra("debugTitle", "Debug").ToUpperInvariant()
                                                    : Localization.Get(title).ToUpperInvariant();
            // The Debug page is a window like Options (OptionsView): the amber accent under its title.
            if (debugAccent == null && titleLabel.parent != null)
            {
                debugAccent = new VisualElement { pickingMode = PickingMode.Ignore };
                debugAccent.AddToClassList("panel-accent");
                titleLabel.parent.Insert(titleLabel.parent.IndexOf(titleLabel) + 1, debugAccent);
            }
            if (debugAccent != null) debugAccent.style.display = page == SysPage.Debug ? DisplayStyle.Flex : DisplayStyle.None;
            if (slots) BuildSaveSlots();
            if (debugButton != null) debugButton.style.display = Cheats.PageShown ? StyleKeyword.Null : DisplayStyle.None;
            if (page == SysPage.Debug && !Cheats.PageShown) { ShowSystemPage(SysPage.Main); return; }   // switched off meanwhile
            if (page == SysPage.Debug) BuildStationOptions();
            if (root.focusController?.focusedElement is VisualElement f) f.Blur();
            Select(page == SysPage.Save ? saveSlotList.contentContainer.ElementAt(1)     // slot 1: the first manual slot
                 : page == SysPage.Load ? saveSlotList.contentContainer.ElementAt(0)
                 : page == SysPage.Options ? optionsView.ActiveTab
                 : page == SysPage.Debug ? (stationOptions.Count > 0 ? stationOptions[0].Field : optionsBack)
                 : SystemMenuItems()[0]);   // Modified: New game is hidden: the first shown one
            BuildHints(InputMode.Current);
        }

        void BuildSaveSlots()
        {
            SaveFolder.Sync();   // remake: the chosen save folder's newer saves first (a no-op without one)
            saveSlotList.Clear();
            for (int i = 0; i < SaveGame.SlotCount; i++)
            {
                int slot = i;
                var save = SaveGame.Preview(i);
                var row = SaveSlotRow.Build(level.Database, i, save, Localization.Extra("autosaveHint", "Saved automatically when you dock"), DeleteSlot);
                row.RegisterCallback<PointerDownEvent>(_ => Play(buttonPush), TrickleDown.TrickleDown);
                row.clicked += () =>
                {
                    if (SaveSlotRow.HoldUsed(row)) return;
                    Play(buttonRelease);
                    if (sysPage == SysPage.Load) PickLoadSlot(slot, save != null); else PickSaveSlot(slot, save != null);
                };
                row.RegisterCallback<FocusInEvent>(_ => { if (!DragScroll.PointerActive) saveSlotList.ScrollTo(row); });
                saveSlotList.Add(row);
            }
            SaveSlotRow.AttachHint(saveSlotList);
        }

        /// <summary>Remake: a slot held down (SaveSlotRow): deleted, the list rebuilt with the same row selected.</summary>
        void DeleteSlot(int slot)
        {
            if (!SaveGame.Delete(slot)) return;
            Play(buttonRelease);
            root.schedule.Execute(() =>
            {
                if (!SavePageOpen) return;
                BuildSaveSlots();
                if (slot < saveSlotList.contentContainer.childCount) Select(saveSlotList.contentContainer.ElementAt(slot));
            });
        }

        /// <summary>MenuTouchWindow load mode: a used slot, after 523 (the progress since the last save is lost), is loaded
        /// (a save is always docked: the station module restarts).</summary>
        void PickLoadSlot(int slot, bool used)
        {
            if (!used) return;
            // A save made with mods that aren't on now says so too (Modding.ModSaves: their items are removed and refunded).
            var missing = Modding.ModSaves.MissingMods(SaveGame.Preview(slot));
            string text = missing.Count == 0 ? Localization.Get(523) : Localization.Get(523) + "\n\n" + string.Format(Localization.Extra("modsMissingShort",
                "Saved with mods that are off: {0}. Their items will be removed and refunded."), string.Join(", ", missing));
            ShowDialog(text, () => { if (SaveGame.Load(slot)) SceneManager.LoadScene(gameObject.scene.name); });
        }

        /// <summary>MenuTouchWindow::OnTouchEnd save mode: slot 0 is reserved, a used slot asks before overwriting.</summary>
        void PickSaveSlot(int slot, bool used)
        {
            lastSavedSlot = slot;
            if (slot == SaveGame.AutoSaveSlot) { ShowDialog(Localization.Get(487), null, true); return; }
            if (used) ShowDialog(Localization.Get(49), () => SaveTo(slot));
            else SaveTo(slot);
        }

        /// <summary>MenuTouchWindow::saveGame: write the slot, refresh the list, "Game saved." (50).</summary>
        void SaveTo(int slot)
        {
            bool ok = SaveGame.Save(slot);
            BuildSaveSlots();
            lastSavedSlot = slot;
            ShowDialog(ok ? Localization.Get(50) : Localization.Extra("saveFailed", "The game could not be saved."), null, true);
        }

        /// <summary>The system menu's focusable items (its buttons, or the slot rows plus Back).</summary>
        VisualElement[] SystemMenuItems()
        {
            if (sysPage == SysPage.Options && optionsView != null) return optionsView.NavItems().ToArray();
            if (DebugPage)
            {
                var o = new System.Collections.Generic.List<VisualElement>();
                if (activeDebugTab != null) o.Add(activeDebugTab);   // Modified: the tab row first
                foreach (var c in stationOptions) o.Add(c.Field);
                o.Add(optionsBack);
                return o.ToArray();
            }
            if (!SavePageOpen)
            {
                var items = debugButton != null
                    ? new VisualElement[] { newGameButton, loadGameButton, saveGameButton, optionsButton, aboutButton, debugButton, mainMenuButton, systemClose }
                    : new VisualElement[] { newGameButton, loadGameButton, saveGameButton, optionsButton, aboutButton, mainMenuButton, systemClose };
                return System.Array.FindAll(items, e => e != null && e.resolvedStyle.display != DisplayStyle.None);
            }
            var list = new System.Collections.Generic.List<VisualElement>(saveSlotList.contentContainer.Children()) { saveBack };
            return list.ToArray();
        }
    }
}
