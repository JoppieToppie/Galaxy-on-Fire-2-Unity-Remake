// MainMenuOptions.cs
// Part of MainMenu: the Options panel: its rows, tabs and their keys (NavigateOptions, LB / RB), Default settings,
// the language and RefreshTexts.

using System;
using System.Collections;
using System.Collections.Generic;
using GoF2Remake.Data;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using PointerType = UnityEngine.UIElements.PointerType;

namespace GoF2Remake.UI
{
    public partial class MainMenu
    {
        /// <summary>Default settings (497), remake (players' request): the dialog asks whether to reset only the open tab or
        /// every tab (a third button), Cancel = nothing; a stray press reset everything before.</summary>
        void ShowResetChoice()
        {
            var page = CurrentOptionPage();
            ShowDialog(Localization.Get(497), OptionsCatalog.ResetQuestion, () => { OptionsCatalog.ResetPage(page); RefreshTexts(); });
            dialogAlt = () => { Settings.ResetToDefaults(); RefreshTexts(); };
            var yes = root.Q<Button>("dialogYes");
            InputGlyph.SetText(yes, OptionsCatalog.ResetTabLabel(page).ToUpperInvariant());
            var alt = root.Q<Button>("dialogAlt");
            InputGlyph.SetText(alt, OptionsCatalog.ResetAllLabel.ToUpperInvariant());
            alt.style.display = DisplayStyle.Flex;
            InputGlyph.SetText(root.Q<Button>("dialogNo"), Localization.Get(425).ToUpperInvariant());   // Cancel
            Select(yes);
        }

        /// <summary>The Options tab on show (SelectTab).</summary>
        OptionPage CurrentOptionPage()
        {
            foreach (OptionPage p in Enum.GetValues(typeof(OptionPage)))
                if (PageName(p) == currentOptionsPage) return p;
            return OptionPage.Sound;
        }

        /// <summary>One row per OptionsCatalog entry on its tab, then the language buttons.</summary>
        void SetupOptions()
        {
            optionControls.Clear();
            foreach (var def in OptionsCatalog.All())
            {
                if (def.inGameOnly) continue;
                var c = new OptionControl(def);
                c.Field.AddToClassList("option-row");
                HookFocusSound(c.Field);
                if (def.kind == OptionKind.Choice || def.kind == OptionKind.Toggle) c.Changed += () => Play(buttonRelease);
                // Options depend on each other (STP turns MSAA off): every row follows a change.
                c.Changed += () => { foreach (var o in optionControls) if (o != c) o.Refresh(); UpdateDebugButton(); };
                // Original: the FX volume plays a sample on release; remake: the voice volume a voice line.
                if (def.id == "sfx") c.Field.RegisterCallback<PointerCaptureOutEvent>(_ => Play(infoSound));
                if (def.id == "voice") c.Field.RegisterCallback<PointerCaptureOutEvent>(_ => PlayVoicePreview());
                if (def.page == OptionPage.Language) c.Root.AddToClassList("language-voice-row");
                root.Q(PageName(def.page)).Add(c.Root);
                optionControls.Add(c);
            }

            var langList = root.Q("languageList");
            for (int i = 0; i < languageCodes.Length && i < languageNames.Length; i++)
            {
                string code = languageCodes[i];
                var b = new Button { text = languageNames[i], name = "lang_" + code };
                b.AddToClassList("menu-button");
                b.AddToClassList("language-button");
                b.clicked += () => { Play(buttonRelease); Settings.Language = code; LoadLanguage(code); RefreshTexts(); };
                HookFocusSound(b);
                langList.Add(b);
            }
        }

        void SelectTab(string page)
        {
            currentOptionsPage = page;
            foreach (var (tab, p) in OptionPages)
            {
                root.Q(tab).EnableInClassList("tab-button--active", p == page);
                root.Q(p).EnableInClassList("tab-page--active", p == page);
            }
        }

        void ApplySettings()
        {
            if (musicSource != null && screen != MenuState.Leaving && !fadingMusic) musicSource.volume = Settings.MusicVolume;
            if (sfxSource != null) sfxSource.volume = Settings.SfxVolume;
            if (voiceSource != null) voiceSource.volume = Settings.VoiceVolume;
        }

        void LoadLanguage(string code)
        {
            int i = Array.IndexOf(languageCodes, code);
            if (i < 0 || languageTables == null || i >= languageTables.Length || languageTables[i] == null) i = 0;
            if (languageTables != null && languageTables.Length > 0) Localization.Load(languageCodes[i], languageTables[i]);
        }

        void RefreshTexts()
        {
            string T(int id) => Localization.Get(id).ToUpperInvariant();
            void Set(string name, string text)
            {
                var e = root.Q<TextElement>(name);
                if (e is Button b) InputGlyph.SetText(b, text);   // its text may be in a label beside a glyph (PadPrompt)
                else if (e != null) e.text = text;
            }

            Set("resumeButton", T(41));
            Set("newGameButton", T(28));
            Set("loadButton", T(29));
            Set("optionsButton", T(31));
            Set("modsButton", Localization.Extra("modsButton", "Mods").ToUpperInvariant());
            if (modBrowser != null && modPanelsLanguage != Settings.Language) BuildModPanels();   // Modified: the mod panels in the new language
            Set("aboutButton", T(43));
            Set("exitButton", T(33));
            Set("debugButton", Localization.Extra("debugButton", "Debug").ToUpperInvariant());
            foreach (var n in new[] { "campaignBack", "difficultyBack", "economyBack", "gameOptionsBack", "loadBack", "optionsBack", "aboutBack", "multiplayerBack" }) Set(n, "‹  " + T(170));
            string mp = Localization.Extra("multiplayer", "Multiplayer").ToUpperInvariant();
            Set("multiplayerButton", mp);
            Set("coopButton", Localization.Extra("coopTitle", "Co-op campaign").ToUpperInvariant());
            Set("multiplayerTitle", mp);
            Set("mpBadge", Localization.Extra("mpExperimental", "Experimental").ToUpperInvariant());
            Set("mpIntro", Localization.Extra("mpIntroShort", "Meet other pilots, form squads and fly missions together."));
            Set("mpNameLabel", Localization.Extra("mpNameLabel", "Pilot name").ToUpperInvariant());
            if (mpName != null) mpName.textEdition.placeholder = Localization.Extra("mpNamePlaceholder", "Your pilot name");
            Set("mpHostTitle", Localization.Extra("mpHostTitle", "Host a game").ToUpperInvariant());
            Set("mpModePublic", Localization.Extra("mpModePublic", "Public").ToUpperInvariant());
            Set("mpModePrivate", Localization.Extra("mpModePrivate", "Invite only").ToUpperInvariant());
            Set("mpModeLocal", Localization.Extra("mpModeLocal", "Local network").ToUpperInvariant());
            ApplyHostMode();
            var hostPw = root.Q<TextField>("mpHostPassword");
            if (hostPw != null) hostPw.textEdition.placeholder = Localization.Extra("mpHostPasswordNone", "No password");   // empty: anyone joins
            Set("mpMaxPlayersLabel", Localization.Extra("mpMaxPlayers", "Max players").ToUpperInvariant());
            Set("mpDebugLabel", Localization.Extra("mpDebugMenu", "Debug menu").ToUpperInvariant());
            Set("mpDebugOff", Localization.Extra("mpDebugOff", "Off").ToUpperInvariant());
            Set("mpDebugOn", Localization.Extra("mpDebugAllowed", "Allowed").ToUpperInvariant());
            Set("mpPvpLabel", Localization.Extra("mpCombat", "Combat").ToUpperInvariant());
            Set("mpPvpOff", Localization.Extra("mpPve", "PvE").ToUpperInvariant());
            Set("mpPvpOn", Localization.Extra("mpPvp", "PvP").ToUpperInvariant());
            Set("mpWorldLabel", Localization.Extra("mpWorld", "World").ToUpperInvariant());
            Set("mpWorldFresh", Localization.Extra("mpWorldFresh", "Fresh").ToUpperInvariant());
            Set("mpWorldPersistent", Localization.Extra("mpWorldPersistent", "Persistent").ToUpperInvariant());
            Set("mpCoopStrengthLabel", Localization.Extra("mpCoopStrength", "Enemies per player").ToUpperInvariant());
            Set("mpCoopStrength0", Localization.Extra("mpDebugOff", "Off").ToUpperInvariant());
            foreach (int pct in new[] { 25, 50, 100 }) Set("mpCoopStrength" + pct, "+" + pct + "%");
            Set("mpCoopRewardsLabel", Localization.Extra("mpCoopRewards", "Story credits").ToUpperInvariant());
            Set("mpCoopRewardsShared", Localization.Extra("mpCoopRewardsShared", "Shared").ToUpperInvariant());
            Set("mpCoopRewardsFull", Localization.Extra("mpCoopRewardsFull", "Each in full").ToUpperInvariant());
            Set("mpModsOff", Localization.Extra("mpDebugOff", "Off").ToUpperInvariant());
            Set("mpModsOn", Localization.Extra("mpDebugAllowed", "Allowed").ToUpperInvariant());
            ApplyHostMods();
            if (mpJoinPassword != null) mpJoinPassword.textEdition.placeholder = Localization.Extra("mpJoinPasswordHint", "Password");
            Set("mpPortLabel", Localization.Extra("mpPortLabel", "Port").ToUpperInvariant());
            Set("mpHost", Localization.Extra("mpHost", "Host").ToUpperInvariant());
            Set("mpJoinTitle", Localization.Extra("mpBrowserTitle", "Server browser").ToUpperInvariant());
            Set("mpJoinText", Localization.Extra("mpJoinCodeLabel", "Or join with a code or an address").ToUpperInvariant());
            if (mpAddress != null) mpAddress.textEdition.placeholder = "ABC123  /  192.168.1.20";
            Set("mpJoin", Localization.Extra("mpJoin", "Join").ToUpperInvariant());
            ApplyPanelMode();   // the Co-op campaign screen's own title and texts

            Set("campaignTitle", T(103));
            Set("debugTitle", Localization.Extra("debugTitle", "Debug").ToUpperInvariant());
            Set("debugBack", "‹  " + T(170));
            Set("debugCheatsTitle", Localization.Extra("debugCheats", "Cheats").ToUpperInvariant());
            Set("debugNote", Localization.Extra("debugNote", "Credits, repairs, ammo and more: the Debug page of the pause and station menus."));
            foreach (var c in debugControls) c.Refresh();
            Set("debugMissionsTitle", Localization.Extra("missionSelect", "Start at mission").ToUpperInvariant());
            if (missionFilter != null) missionFilter.textEdition.placeholder = Localization.Extra("missionSearch", "Search: a step number, a station, a word...");
            Set("difficultyTitle", T(517));
            Set("easyLabel", T(518));
            Set("easyDesc", Localization.Extra("easyDesc", "Weaker enemies in smaller groups."));
            Set("normalLabel", T(519));
            Set("normalDesc", Localization.Extra("normalDesc", "The classic Galaxy on Fire 2 experience."));
            Set("hardLabel", T(520));
            Set("hardDesc", Localization.Extra("hardDesc", "Tougher enemies in bigger groups; the economy as on Normal."));
            Set("economyTitle", Localization.Extra("economyTitle", "Select the economy").ToUpperInvariant());
            Set("gameOptionsTitle", Localization.Extra("gameOptionsTitle", "Game options").ToUpperInvariant());
            Set("gameOptionsStartLabel", Localization.Extra("gameOptionsStart", "Start game").ToUpperInvariant());
            Set("economyDefaultLabel", Session.EconomyName(Economy.Default).ToUpperInvariant());
            Set("economyDefaultDesc", Localization.Extra("economyDefaultDesc",
                "PC / Mac / iPhone prices: cheaper goods and blueprints, dearer ships."));
            Set("economyAndroidLabel", Session.EconomyName(Economy.Android).ToUpperInvariant());
            Set("economyAndroidDesc", Localization.Extra("economyAndroidDesc",
                "Android prices: dearer goods and gear, bigger blueprints, cheaper ships."));
            RefreshKaamoToggle();
            RefreshHardcoreToggle();
            RefreshTutorialToggle();
            RefreshCapitalToggle();
            Set("extremeLabel", T(25));
            Set("extremeDesc", Localization.Extra("extremeDesc", "For veterans: tougher enemies and a harsher economy."));
            Set("loadTitle", T(29));
            Set("exportSavesButton", Localization.Extra("exportSaves", "Export saves").ToUpperInvariant());
            Set("importSavesButton", Localization.Extra("importSaves", "Import saves").ToUpperInvariant());
            Set("importOriginalButton", Localization.Extra("importOriginalSave", "Import original save").ToUpperInvariant());
            Set("exportOriginalButton", Localization.Extra("exportOriginalSaves", "Export for original game").ToUpperInvariant());
            var originalRow = root.Q("originalSaveRow");
            if (originalRow != null) originalRow.style.display = Modding.ModImport.Available ? DisplayStyle.Flex : DisplayStyle.None;
            // Without a file dialog (Linux, Android) the files go to and come from the Transfer folder: shown under the buttons.
            var transferNote = root.Q<Label>("saveTransferNote");
            if (transferNote != null)
            {
                transferNote.text = SaveTransfer.HasFileDialog ? ""
                    : Localization.Extra("saveTransferFolder", "Exported saves go to, and are imported from:") + " " + SaveTransfer.TransferFolder;
                transferNote.style.display = SaveTransfer.HasFileDialog ? DisplayStyle.None : DisplayStyle.Flex;
            }
            Set("optionsTitle", T(31));
            Set("tabSound", OptionsCatalog.PageTitle(OptionPage.Sound).ToUpperInvariant());
            Set("tabGraphics", OptionsCatalog.PageTitle(OptionPage.Graphics).ToUpperInvariant());
            Set("tabControls", OptionsCatalog.PageTitle(OptionPage.Controls).ToUpperInvariant());
            Set("tabBindings", OptionsCatalog.PageTitle(OptionPage.Bindings).ToUpperInvariant());
            Set("tabGameplay", OptionsCatalog.PageTitle(OptionPage.Gameplay).ToUpperInvariant());
            Set("tabAccessibility", OptionsCatalog.PageTitle(OptionPage.Accessibility).ToUpperInvariant());
            Set("tabLanguage", T(0));
            Set("optionsDefaults", T(497));
            Set("aboutTitle", T(43));
            Set("dialogYes", T(134));
            Set("dialogNo", T(135));

            foreach (var c in optionControls) c.Refresh();
            foreach (var b in root.Q("languageList").Query<Button>().ToList())
                b.EnableInClassList("language-button--active", b.name == "lang_" + Localization.Language);

            UpdatePressAnyKey();
            versionLabel.text = VersionText;
            RefreshUpdateButton();
            hintLabel.text = Localization.Extra("hint", "↑ ↓  NAVIGATE     ENTER  SELECT     ESC  " + T(170));
            var aboutText = root.Q<Label>("aboutText");
            aboutText.text = $"{VersionText}\n\n{AboutText.Get()}\n\n{Localization.Get(48)}";
            AboutText.Hook(aboutText);
        }

        /// <summary>The Options panel (a player's report; the in-game Options' rules, OptionsView): up / down walk the active
        /// tab, the active page's items and the footer (Back and Default settings as one row), stopping at the ends; left /
        /// right switch tabs on the tab row, flip a toggle (sliders, choices and bindings step themselves, above), move
        /// between the language buttons and between the footer's buttons, and leave the other rows alone. Before, the whole
        /// panel was one list: down stepped through the tabs, right ran on to the next row. LB / RB (Q / E) switch tabs
        /// anywhere (UpdateOptionTabs), A on a tab goes into it. False: not this panel, or a dialog is open.</summary>
        bool NavigateOptions(NavigationMoveEvent e, VisualElement focused)
        {
            if (dialog.ClassListContains("dialog-backdrop--shown")) return false;
            if (!panels.TryGetValue("optionsPanel", out var panel) || openPanel != panel) return false;
            var activeTab = ActiveOptionsTab();
            var items = OptionPageItems();
            var footer = Focusables(panel.Q(className: "options-footer"));
            bool onTab = focused is Button fb && OptionsTabPage(fb) != null;
            bool inFooter = focused != null && footer.Contains(focused);
            int i = focused != null ? items.IndexOf(focused) : -1;
            bool languagePage = currentOptionsPage == "languagePage";
            VisualElement to = focused;
            switch (e.direction)
            {
                case NavigationMoveEvent.Direction.Down:
                    if (onTab) to = items.Count > 0 ? items[0] : footer.Count > 0 ? footer[0] : focused;
                    else if (i >= 0)
                    {
                        var below = languagePage ? SpatialNav.Next(items, focused, 0, 1) : i + 1 < items.Count ? items[i + 1] : null;
                        to = below ?? (footer.Count > 0 ? footer[0] : focused);
                    }
                    else if (!inFooter) to = activeTab;   // nothing of this page had the focus
                    break;
                case NavigationMoveEvent.Direction.Up:
                    if (inFooter) to = items.Count > 0 ? items[items.Count - 1] : activeTab;
                    else if (i >= 0)
                    {
                        var above = languagePage ? SpatialNav.Next(items, focused, 0, -1) : i > 0 ? items[i - 1] : null;
                        to = above ?? activeTab;
                    }
                    else if (!onTab) to = activeTab;
                    break;
                case NavigationMoveEvent.Direction.Left:
                case NavigationMoveEvent.Direction.Right:
                {
                    int dir = e.direction == NavigationMoveEvent.Direction.Left ? -1 : 1;
                    if (onTab) { StepOptionsTab(dir, false); return Swallow(e); }
                    if (inFooter) to = footer[Mathf.Clamp(footer.IndexOf(focused) + dir, 0, footer.Count - 1)];
                    else if (focused is Toggle) optionControls.Find(c => c.Field == focused)?.Step(dir);
                    else if (languagePage && focused is Button && i >= 0) to = SpatialNav.Next(items, focused, dir, 0) ?? focused;
                    break;
                }
                default:
                    return false;
            }
            if (to != null && to != focused) Select(to);
            return Swallow(e);
        }

        /// <summary>The page an Options tab button shows (null: not a tab).</summary>
        static string OptionsTabPage(Button b)
        {
            if (b == null) return null;
            foreach (var (tab, page) in OptionPages) if (b.name == tab) return page;
            return null;
        }

        Button ActiveOptionsTab()
        {
            foreach (var (tab, page) in OptionPages) if (page == currentOptionsPage) return root.Q<Button>(tab);
            return null;
        }

        /// <summary>What the keys walk on the active Options page, top to bottom (a greyed-out row not).</summary>
        List<VisualElement> OptionPageItems()
        {
            var page = root.Q(currentOptionsPage);
            return page != null ? Focusables(page, scopeShown: true) : new List<VisualElement>();
        }

        /// <summary>The Options tab beside the active one, wrapping round (LB / RB, Q / E, left / right on the tab row). The focus
        /// goes with it: to the new tab from the tab row, else to the new page's first item.</summary>
        void StepOptionsTab(int dir, bool intoPage)
        {
            int at = 0;
            for (int k = 0; k < OptionPages.Length; k++) if (OptionPages[k].page == currentOptionsPage) at = k;
            var (tab, page) = OptionPages[((at + dir) % OptionPages.Length + OptionPages.Length) % OptionPages.Length];
            SelectTab(page);
            var tabButton = root.Q<Button>(tab);
            Select(tabButton);
            if (intoPage) FocusPageStart(tabButton);
        }

        /// <summary>The active page's first item takes the focus once the page is laid out (a page just shown has no layout
        /// in this frame, so its items don't count as shown yet); the tab keeps it meanwhile, and when the page has none.</summary>
        void FocusPageStart(Button tab)
        {
            string page = currentOptionsPage;
            root.schedule.Execute(() =>
            {
                if (currentOptionsPage != page || !panels.TryGetValue("optionsPanel", out var panel) || openPanel != panel) return;
                var first = OptionPageItems().Find(v => true);
                if (first != null && root.focusController?.focusedElement == tab) Select(first);
            }).ExecuteLater(1);
        }

        /// <summary>LB / RB (Q / E) on the Options panel switch tabs, like the in-game Options and the hangar's (they did
        /// nothing here). Not with both shoulders held (the Debug panel's LB + RB).</summary>
        void UpdateOptionTabs(Gamepad pad)
        {
            if (screen != MenuState.Menu || dialog.ClassListContains("dialog-backdrop--shown")) return;
            if (!panels.TryGetValue("optionsPanel", out var panel) || openPanel != panel) return;
            var kb = Keyboard.current;
            int dir = 0;
            if (pad != null && pad.leftShoulder.wasPressedThisFrame && !pad.rightShoulder.isPressed) dir = -1;
            else if (pad != null && pad.rightShoulder.wasPressedThisFrame && !pad.leftShoulder.isPressed) dir = 1;
            else if (kb != null && kb.qKey.wasPressedThisFrame) dir = -1;
            else if (kb != null && kb.eKey.wasPressedThisFrame) dir = 1;
            if (dir == 0) return;
            var focused = root.focusController?.focusedElement as VisualElement;
            StepOptionsTab(dir, !(focused is Button b && OptionsTabPage(b) != null));
        }
    }
}
