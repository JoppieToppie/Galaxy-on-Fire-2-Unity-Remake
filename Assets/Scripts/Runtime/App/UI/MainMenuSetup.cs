// MainMenuSetup.cs
// Part of MainMenu: the UI tree set up on every (re)load: OnUIReload wires the buttons and panels, RestoreState puts the
// flow back after a live reload; the Mods hub panel (Mod Browser / Mod Builder) and the mod cache rebuild.

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
        void OnUIReload(PanelRenderer renderer, VisualElement rootElement, int version)
        {
            root = rootElement;
            ScrollBars.Watch(rootElement);   // Modified: the hangar's scrollbar on every scroll view that needs one
            StickScroll.Watch(rootElement);   // Remake: the right stick scrolls the long texts
            root.style.flexGrow = 1;   // the default theme would stretch the document root; ours is custom
            root.EnableInClassList("can-hover", !touchMode);
            safeArea = root.Q("safeArea");
            LoadLanguage(Settings.Language);

            logo = root.Q("logo");
            logo.usageHints = UsageHints.DynamicTransform;
            logo.RegisterCallback<GeometryChangedEvent>(_ => PlaceTitleLogo());
            splash = root.Q("splash");
            splashLogo = root.Q("splashLogo");
            fade = root.Q("fade");
            // Remake mods: the loading screen while the mods load (on the startup splash, and on the fade before a game).
            splashLoading = new ModLoadingView(splash);
            leaveLoading = new ModLoadingView(fade);
            dialog = root.Q("dialog");
            dialogField = root.Q<TextField>("dialogField");
            if (dialogField != null)
            {
                dialogField.maxLength = GoF2Remake.Multiplayer.NetGame.MaxNameLength;
                dialogField.RegisterCallback<KeyDownEvent>(e =>
                {
                    if (e.keyCode != KeyCode.Return && e.keyCode != KeyCode.KeypadEnter) return;
                    e.StopPropagation();
                    ConfirmDialog();
                }, TrickleDown.TrickleDown);
            }
            mainColumn = root.Q("mainColumn");
            mainButtons = root.Q("mainButtons");
            pressAnyKey = root.Q<Label>("pressAnyKey");
            versionLabel = root.Q<Label>("versionLabel");
            hintLabel = root.Q<Label>("hintLabel");
            // Rework (docs/UI-DESIGN.md "Button prompts"): the keys as a prompt row at the footer's right, for the keyboard or
            // the controller in use (none on touch), following the open panel; the top-right "i" that held them went at the
            // user's request (2026-10-22).
            hintLabel.style.display = DisplayStyle.None;
            // Each UI reload builds a new tree: a new bar in its footer (the old one went with the old tree).
            menuPrompts = MenuKeys.Bar(MenuPromptRows);
            menuPrompts.AddToClassList("prompt-bar--footer");
            hintLabel.parent.Add(menuPrompts);

            resumeButton = Bind("resumeButton", () => LoadSlot(SaveGame.MostRecentSlot()));
            newGameButton = Bind("newGameButton", () => OpenPanel("campaignPanel"));
            coopButton = Bind("coopButton", OpenCoop);
            multiplayerButton = Bind("multiplayerButton", OpenMultiplayer);
            loadButton = Bind("loadButton", () => { SyncSaveFolder(true); BuildSlots(); OpenPanel("loadPanel"); });
            // Remake: every save to one file and back (SaveTransfer), under the Load game list; main menu only (no game is
            // loaded here). After the click: the system's file dialog is modal.
            Bind("exportSavesButton", () => root.schedule.Execute(ExportSaves).ExecuteLater(1));
            Bind("importSavesButton", () => root.schedule.Execute(PickImport).ExecuteLater(1));
            // Remake: a save of the original game in, every slot out as the original's files (OriginalSaveTransfer).
            Bind("importOriginalButton", () => root.schedule.Execute(PickOriginalImport).ExecuteLater(1));
            Bind("exportOriginalButton", () => root.schedule.Execute(AskOriginalExport).ExecuteLater(1));
            optionsButton = Bind("optionsButton", () =>
            {
                // The rows follow what is known now (DLSS / FSR only once URP has made its pipeline, after these rows were built).
                foreach (var c in optionControls) c.Refresh();
                OpenPanel("optionsPanel");
                SelectTab(OptionPages[0].page);
            });
            // Remake: Mods opens a small hub, the Mod Browser (ModBrowser) and the Mod Builder (ModBuilderProjects).
            modsButton = Bind("modsButton", () => OpenPanel(panels.ContainsKey("modsHubPanel") ? "modsHubPanel" : "modsPanel"));
            aboutButton = Bind("aboutButton", () => OpenPanel("aboutPanel"));
            debugButton = Bind("debugButton", OpenDebug);
            UpdateDebugButton();
            exitButton = Bind("exitButton", () => ShowDialog(Localization.Get(390), Localization.Get(53), Quit));
            // Remake: the chosen save folder's newer saves in first (SaveFolder; a no-op without one), then Resume's test.
            OptionsCatalog.SaveFolderPressed = ChooseSaveFolder;
            SyncSaveFolder(true);
            resumeButton.EnableInClassList("menu-button--gone", SaveGame.MostRecentSlot() < 0);   // only with a save
            UpdateColumnFit();
            updateRow = root.Q("updateRow");
            updateButton = Bind("updateButton", () => Application.OpenURL(UpdateCheck.ReleaseUrl ?? UpdateCheck.ReleasesPage));

            foreach (var n in new[] { "campaignPanel", "difficultyPanel", "economyPanel", "gameOptionsPanel", "loadPanel", "optionsPanel", "aboutPanel", "multiplayerPanel" })
            {
                panels[n] = root.Q(n);
                panels[n].usageHints = UsageHints.DynamicTransform;
            }
            foreach (var n in new[] { "aboutScroll", "slotList" })
            {
                var sv = root.Q<ScrollView>(n);
                sv.mode = ScrollViewMode.Vertical;
                sv.verticalScrollerVisibility = ScrollerVisibility.Hidden;     // drag / wheel / focus scrolling instead
                sv.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
                new DragScroll(sv);
            }
            // The option pages and the host card's settings scroll by wheel, touch and focus only (a drag would fight the
            // sliders, and the host card's address rows copy on a tap).
            var noDragScrolls = new List<string> { "mpHostScroll" };
            foreach (var (_, p) in OptionPages) noDragScrolls.Add(p);
            foreach (var pg in noDragScrolls)
            {
                if (!(root.Q(pg) is ScrollView sv)) continue;
                sv.mode = ScrollViewMode.Vertical;
                sv.verticalScrollerVisibility = ScrollerVisibility.Hidden;
                sv.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            }
            foreach (var n in new[] { "campaignBack", "difficultyBack", "economyBack", "gameOptionsBack", "loadBack", "optionsBack", "aboutBack", "multiplayerBack" })
            {
                var b = root.Q<Button>(n);
                b.clicked += Back;   // Back() plays the release sound itself (also used by Esc)
                HookFocusSound(b);
            }

            Bind("cardGof2", () => PickCampaign(Campaign.GalaxyOnFire2));
            Bind("cardValkyrie", () => PickCampaign(Campaign.Valkyrie));
            Bind("cardSupernova", () => PickCampaign(Campaign.Supernova));
            BuildDebugPanel();
            UpdateDebugButton();
            // The mod browser (remake, ModBrowser): built in code next to the other panels.
            BuildModPanels();
            SetupMultiplayerPanel();
            Bind("easyButton", () => PickDifficulty(Session.DifficultyEasy));
            Bind("normalButton", () => PickDifficulty(Session.DifficultyNormal));
            Bind("hardButton", () => PickDifficulty(Session.DifficultyHard));
            Bind("extremeButton", () => ShowDialog(Localization.Get(25), Localization.Get(26),
                () => PickDifficulty(Session.DifficultyExtreme)));
            Bind("economyDefaultButton", () => PickEconomy(Economy.Default));
            Bind("economyAndroidButton", () => PickEconomy(Economy.Android));
            Bind("gameOptionsStart", () => BeginGame(pendingEconomy));
            Bind("tutorialToggle", () => { Settings.TutorialHints = !Settings.TutorialHints; RefreshTutorialToggle(); });
            Bind("capitalToggle", () => { Settings.CapitalShips = !Settings.CapitalShips; RefreshCapitalToggle(); });
            Bind("kaamoToggle", () => { KaamoFromStart = !KaamoFromStart; RefreshKaamoToggle(); });
            Bind("hardcoreToggle", () => { hardcoreNext = !hardcoreNext; RefreshHardcoreToggle(); });
            Bind("ngPlusToggle", () => { newGamePlus = !newGamePlus && ngPlusSave != null; RefreshNgPlus(false); });
            // The Supernova Challenge (293, SupernovaChallenge): its description (298) asks "Start Supernova Challenge?".
            Bind("challengeButton", () => ShowDialog(Localization.Get(293), Localization.Get(298).Replace("\r\n", "\n"),
                                                     () => StartCoroutine(Leave(SupernovaChallenge.Start(false)))));
            Bind("dialogYes", ConfirmDialog);
            Bind("dialogNo", () => { var a = dialogNo; CloseDialog(); a?.Invoke(); });
            Bind("dialogAlt", () => { var a = dialogAlt; CloseDialog(); a?.Invoke(); });

            foreach (var (tab, pg) in OptionPages) Bind(tab, () => SelectTab(pg));
            Bind("optionsDefaults", ShowResetChoice);   // 497: asks first (this tab or every tab)
            SetupOptions();

            root.RegisterCallback<NavigationCancelEvent>(_ =>
            {
                cancelFrame = Time.frameCount;
                if (ChoicePopup.IsOpen) { ChoicePopup.Close(); return; }   // an option's open list closes first
                Back();
            }, TrickleDown.TrickleDown);
            root.RegisterCallback<NavigationMoveEvent>(OnNavigate, TrickleDown.TrickleDown);
            root.RegisterCallback<PointerMoveEvent>(e => { if (e.pointerType == PointerType.mouse && InputMode.MousePointing) pointerDriven = true; }, TrickleDown.TrickleDown);
            root.RegisterCallback<NavigationSubmitEvent>(e =>
            {
                // Space typed into a field is a space, not a press of the menu's button.
                if (TextFieldKeys.IsTyping(e, root.focusController?.focusedElement as VisualElement)) { e.StopPropagation(); root.focusController?.IgnoreEvent(e); return; }
                // A choice keeps its value (left / right pick it; A cycled it, a player's report).
                if (screen == MenuState.Menu && root.focusController?.focusedElement is ChoiceRow) { e.StopPropagation(); root.focusController?.IgnoreEvent(e); return; }
                // A on an Options tab opens it and goes into it (it only selected the tab).
                if (screen == MenuState.Menu && root.focusController?.focusedElement is Button tab && OptionsTabPage(tab) is string tabPage
                    && !dialog.ClassListContains("dialog-backdrop--shown"))
                {
                    Play(buttonRelease);
                    SelectTab(tabPage);
                    FocusPageStart(tab);
                    e.StopPropagation();
                    root.focusController?.IgnoreEvent(e);
                }
            }, TrickleDown.TrickleDown);
            root.RegisterCallback<PointerDownEvent>(OnPointerDown, TrickleDown.TrickleDown);
            root.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (e.pointerType == PointerType.mouse && !InputMode.MousePointing) return;
                DragScroll.NotePointer();
                if (e.pointerType == PointerType.mouse) SetTouchMode(false);
            }, TrickleDown.TrickleDown);
            root.RegisterCallback<WheelEvent>(_ => DragScroll.NotePointer(), TrickleDown.TrickleDown);
            // Keys / controller: the focused row scrolls into view (not for the pointer's own focus: DragScroll.PointerActive).
            root.RegisterCallback<FocusInEvent>(e => { if (e.target is VisualElement v && !DragScroll.PointerActive) EnsureVisible(v); });

            ApplySettings();

            // Rework (docs/UI-DESIGN.md "Button prompts"): with a controller, B on every Back and the dialog's No / Cancel,
            // A on the focused dialog button (InputGlyph.PadPrompt; their texts go through InputGlyph.SetText from here).
            root.Query<Button>(className: "back-button").ForEach(b => { if (b.name != "optionsDefaults") InputGlyph.PadPrompt(b, PadButton.B); });
            InputGlyph.PadPrompt(root.Q<Button>("dialogYes"), PadButton.A, true);
            InputGlyph.PadPrompt(root.Q<Button>("dialogAlt"), PadButton.A, true);
            InputGlyph.PadPrompt(root.Q<Button>("dialogNo"), PadButton.B);
            PadKeyboard.Attach(root);   // a controller's keyboard for the text fields (docs/UI-DESIGN.md "Text entry")
            KeyboardLift.Watch(root);   // phones: the focused text field kept above the on-screen keyboard
            UiFocus.SkipScrollBars(root);   // no stop on a scroll bar (docs/UI-DESIGN.md "Focus")
            RefreshTexts();
            lastScreen = Vector2Int.zero;
            UpdateLayout();
            if (!started)
            {
                started = true;
                // The main story's ending plays over this scene's backdrop (ModStation's CutScene(2)), then the station.
                if (Session.EndingPending) { EndingCredits.Begin(gameObject, root, musicSource); return; }
                foreach (var b in mainButtons.Query<Button>().ToList()) b.AddToClassList("menu-button--hidden");
                StartCoroutine(Run());
            }
            else RestoreState();
        }

        /// <summary>After a live UI reload the tree is new: put it back in the current flow state.</summary>
        void RestoreState()
        {
            if (screen == MenuState.Splash) return;
            splash.AddToClassList("splash--gone");
            splash.AddToClassList("splash--removed");
            logo.RemoveFromClassList("logo--instant");
            if (screen == MenuState.Title) { logo.AddToClassList("logo--title-visible"); return; }
            logo.AddToClassList("logo--menu");
            pressAnyKey.AddToClassList("press-any-key--hidden");
            root.AddToClassList("menu-root--menu");
            FocusFirst(mainButtons);
            RefreshUpdateButton();
        }

        /// <summary>The mod browser, the Mod Builder's projects and the Mods hub (remake), built in code. Modified: built
        /// again when the language changes (RefreshTexts), since their texts are taken as they are built.</summary>
        void BuildModPanels()
        {
            if (!(root.Q("panelHost") is VisualElement modHost)) return;
            modBrowser?.Panel.RemoveFromHierarchy();
            modProjects?.Panel.RemoveFromHierarchy();
            if (panels.TryGetValue("modsHubPanel", out var oldHub)) oldHub.RemoveFromHierarchy();
            modPanelsLanguage = Settings.Language;
            modBrowser = new ModBrowser(modHost, Back, HookFocusSound, () => Play(buttonRelease), () => StartCoroutine(RebuildModCache()),
                                        ShowDialog, ShowNotice);
            panels["modsPanel"] = modBrowser.Panel;
            // The Mod Builder's projects (remake, Modding.ModBuilder): opening one starts its hangar behind the fade.
            modProjects = new ModBuilderProjects(modHost, Back, HookFocusSound, () => Play(buttonRelease), OpenModProject, ShowDialog, ShowNotice);
            panels[Modding.ModBuilder.ProjectsPanel] = modProjects.Panel;
            // The Mod Browser's Edit in Mod Builder: the Mod Builder's page, making a project of that mod.
            modBrowser.EditInBuilder = mod =>
            {
                OpenPanel(Modding.ModBuilder.ProjectsPanel);
                modProjects.ImportInstalled(mod);
            };
            panels["modsHubPanel"] = BuildModsHub(modHost);
        }
        string modPanelsLanguage;

        /// <summary>The Mods button's panel: the Mod Browser (installed mods) and the Mod Builder (your own projects).</summary>
        VisualElement BuildModsHub(VisualElement host)
        {
            var p = new VisualElement { name = "modsHubPanel" };
            p.AddToClassList("panel");
            p.usageHints = UsageHints.DynamicTransform;
            var title = new Label(Localization.Extra("modsButton", "Mods").ToUpperInvariant());
            title.AddToClassList("panel-title");
            title.AddToClassList("gof-semibold");
            p.Add(title);
            var accent = new VisualElement();
            accent.AddToClassList("panel-accent");
            p.Add(accent);
            Button Choice(string heading, string desc, Action onClick)
            {
                var b = new Button();
                b.AddToClassList("choice-button");
                var t = new Label(heading.ToUpperInvariant()) { pickingMode = PickingMode.Ignore };
                t.AddToClassList("choice-title");
                t.AddToClassList("gof-semibold");
                b.Add(t);
                var d = new Label(desc) { pickingMode = PickingMode.Ignore };
                d.AddToClassList("choice-desc");
                b.Add(d);
                b.clicked += () => { Play(buttonRelease); onClick(); };
                HookFocusSound(b);
                p.Add(b);
                return b;
            }
            modsHubBrowser = Choice(Localization.Extra("modBrowser", "Mod Browser"),
                Localization.Extra("modBrowserDesc", "Turn installed mods on and off, set their order, import new ones."),
                () => OpenPanel("modsPanel"));
            modsHubBuilder = Choice(Localization.Extra("mbTitle", "Mod Builder"),
                Localization.Extra("modBuilderDesc", "Make ships, items, characters and more in the game, test them, then export them."),
                () => OpenPanel(Modding.ModBuilder.ProjectsPanel));
            var back = new Button(Back) { text = "‹  " + Localization.Get(170).ToUpperInvariant() };
            back.AddToClassList("menu-button");
            back.AddToClassList("back-button");
            back.AddToClassList("gof-semibold");
            HookFocusSound(back);
            p.Add(back);
            host.Add(p);
            return p;
        }

        /// <summary>A Mod Builder project opened: its hangar (Modding.ModBuilder), once the project's models have loaded.</summary>
        void OpenModProject(Modding.ModProject project)
        {
            if (screen != MenuState.Menu) return;
            Modding.ModBuilder.Begin(project);
            StartCoroutine(Leave("Station"));
        }

        /// <summary>The mod browser's "Rebuild cache": every mod cache goes (ModTextureCache.ClearAll: textures, hangar shadows,
        /// unpacked zips) and the mods load again, behind the black fade with the loading screen.</summary>
        IEnumerator RebuildModCache()
        {
            if (screen != MenuState.Menu) yield break;
            screen = MenuState.Leaving;   // no menu input meanwhile
            fade.AddToClassList("fade--on");
            yield return new WaitForSecondsRealtime(0.4f);
            Modding.ModTextureCache.ClearAll();
            Modding.ModManager.Scan();   // a new revision: every loader starts again
            yield return WaitForMods(leaveLoading, 120f);
            fade.RemoveFromClassList("fade--on");
            modBrowser?.Rebuild();
            screen = MenuState.Menu;
        }
    }
}
