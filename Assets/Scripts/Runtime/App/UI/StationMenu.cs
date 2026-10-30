// StationMenu.cs
// The docked-station screen (ModStation::OnRender2D 0xef208) over StationLevel's hangar / Space Lounge:
//   header "<station> Station" (136), left panel with "<system> System" (137), "Tech level: N" (133), the race (406+),
//   the screen buttons Hangar (167: opens the shop window, HangarWindow, over the 3D hangar), Space Lounge (398) and
//   Map (177: the star map, StarMap; refused with an overloaded hold, 204; picking a station leaves at once with it as
//   the programmed destination, StarMap::depart),
//   and the launch button bottom-right (the original asks "Depart the station?" (397) first; the remake launches at once);
//   launching with more cargo than the hold takes is refused (204, ModStation::leaveStation 0xec1ec).
// Dragging over the hangar turns the player's ship (1 rad per 120 px of a 480 px high screen, with a fling);
// a tap in the lounge skips its camera intro. Adapts to InputMode like the flight HUD:
//   Touch:      buttons, drag to turn the ship, Menu button.
//   Keyboard:   keycap hints; A/D or arrows turn the ship, 1 hangar, 2 lounge, M map, L launch, Esc back.
//   Controller: Xbox / PlayStation hints (InputMode.Style; Xbox names below); right stick turns the ship, LB hangar / RB lounge, Y map, X launch, B back, Menu to the main menu.
//   In the hangar window: up / down select, left / right sell / buy, Enter / A confirm, Q / E or LB / RB switch tabs.
// Esc / B: dialog -> no, hangar window / lounge -> main view, main view -> the system menu.
// System menu (MenuTouchWindow, "Menu" 172; the Menu button, Esc on the main view, controller Menu): Save game (30) with the
// slot list (slot 0 "This slot is reserved for the auto-save game." 487; a used slot asks "Are you sure you want to
// overwrite this game?" 49; then "Game saved." 50, MenuTouchWindow::saveGame 0x14bcf8), Back to Main Menu (522, confirm
// "Are you sure? Your progress won't be saved." 523). The original's Options / Help entries are in the main menu here.
// Story (ModStation::OnUpdate 0xed2a8 / OnTouchEnd 0xea4ec, campaign_flow.md 3.1): while no window is open, a completed
// campaign mission (Story.IsComplete, docked; the lounge types need the lounge with its intro over) opens its
// success conversation (DialogueView); closing it advances the story, then by the new index: reload the station
// (9, 44, 75, 76, 83), launch into a story orbit (78, 89, 99, 109, 119, 133, 144, 160) or credit the reward. The menu
// buttons unlock with the story: Hangar from 5, Map from 9, Space Lounge from 12.
// Space Lounge: the agents and their chat (LoungePanel). Missions (129): the Missions window (MissionsWindow).
// Freelance delivery (ModStation::OnUpdate, Status::missionCompleted / missionFailed docked): a finished or failed freelance
// mission opens the client's message; closing it pays (reward message + sound 36) or cleans up (Freelance).
// Not yet (the original's other buttons): Status; the ending after index 43 (credits) is a plain advance.

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
    [RequireComponent(typeof(PanelRenderer))]
    public partial class StationMenu : MonoBehaviour
    {
        public StationLevel level;
        public string menuScene = "MainMenu";
        [Header("Audio (FMOD events 124 / 123 / 126)")]
        public AudioSource sfxSource;
        public AudioClip buttonPush;
        public AudioClip buttonRelease;
        public AudioClip infoSound;
        [Header("Hangar sounds (FMOD events 0x65 / 0x64 / 0x62 / 0x60)")]
        public AudioClip shopBuy;
        public AudioClip shopSell;
        public AudioClip shopMount;
        public AudioClip shopDemount;
        [Header("Text (used when the scene is started without the main menu)")]
        public string[] languageCodes;
        public TextAsset[] languageTables;
        [Header("Turning the ship")]
        [Tooltip("Keyboard / controller turn speed, game radians per second.")]
        public float keyTurnSpeed = 1.6f;

        PanelRenderer panelRenderer;
        PanelSettings runtimePanel;
        VisualElement root, safeArea, dragZone, hints, dialog;
        Button hangarButton, loungeButton, mapButton, missionsButton, statusButton, inspectButton, launchButton, dialogYes, dialogNo;
        Button customizeButton;   // remake mods: a customizable ship's Customize screen (ShipCustomizePanel)
        StatusWindow status;
        LoungePanel lounge;
        bool safeAreaHidden;
        MissionsWindow missions;
        Label tickerText;
        float tickerX, tickerUnitWidth;
        bool tickerReady, tickerHooked;
        int tickerPointer = -1;
        float tickerLastX;
        string tickerSingle = "";
        string tickerBase;          // the game's own items, rolled once per docking
        bool tickerNewsDirty;       // the session's news changed: rebuilt when the strip wraps (no jump)
        float tickerBuiltAt;
        VisualElement systemMenu, systemMain, systemSave;
        ScrollView saveSlotList;
        Button saveGameButton, mainMenuButton, systemClose, saveBack;
        int lastSavedSlot = -1;
        DialogueView storyDialogue;
        AudioSource voiceSource;
        Label viewTitle, toast;
        HangarWindow hangarWindow;
        System.Action dialogAction, dialogNoAction;
        float toastMs;
        Vector2Int lastScreen;
        Rect lastSafeArea;
        int lastLayout = -1;
        bool touchMode;

        int dragPointer = -1;
        float dragLastX, dragLastTime, dragVelocity;   // velocity in game rad/s, sampled per frame
        float dragFrameRadians;                         // turned since the last frame (touch can send several moves per frame)
        const float MaxFling = 25f;                     // rad/s

        void OnEnable()
        {
            panelRenderer = GetComponent<PanelRenderer>();
            panelRenderer.RegisterUIReloadCallback(OnUIReload);
            if (runtimePanel == null && panelRenderer.panelSettings != null)
            {
                runtimePanel = Instantiate(panelRenderer.panelSettings);
                panelRenderer.panelSettings = runtimePanel;
            }
            InputMode.Changed += ApplyInputMode;
            Localization.Changed += RefreshTexts;   // Modified: the in-game Options' language row applies at once
            if (level != null) level.ViewChanged += OnViewChanged;
        }

        void OnDisable()
        {
            EndDrag(dragPointer, false);
            panelRenderer?.UnregisterUIReloadCallback(OnUIReload);
            InputMode.Changed -= ApplyInputMode;
            Localization.Changed -= RefreshTexts;
            if (level != null) level.ViewChanged -= OnViewChanged;
        }

        void OnApplicationFocus(bool focused)
        {
            if (!focused) EndDrag(dragPointer, false);
        }

        void OnUIReload(PanelRenderer renderer, VisualElement rootElement, int version)
        {
            // The first load can come before StationLevel.Awake has built the station: try again next frame.
            if (level != null && level.Layout == null)
            {
                rootElement.schedule.Execute(() => OnUIReload(renderer, rootElement, version)).ExecuteLater(1);
                return;
            }
            ScrollBars.Watch(rootElement);   // Modified: the hangar's scrollbar on every scroll view that needs one
            StickScroll.Watch(rootElement, () => level == null || !level.Inspect.Active);   // the right stick: Inspect ship's camera
            if (!Localization.IsLoaded && languageTables != null && languageTables.Length > 0)
            {
                int i = languageCodes != null ? System.Array.IndexOf(languageCodes, Settings.Language) : -1;
                if (i < 0) i = 0;
                Localization.Load(languageCodes != null && i < languageCodes.Length ? languageCodes[i] : "en", languageTables[i]);
            }
            root = rootElement;
            root.name = "stationPanel";
            root.style.flexGrow = 1;
            root.pickingMode = PickingMode.Ignore;
            safeArea = root.Q("safeArea");
            dragZone = root.Q("dragZone");
            hints = root.Q("hints");
            fundsCargo = root.Q<Label>("fundsCargo");
            fundsCredits = root.Q<Label>("fundsCredits");
            shownCredits = shownLoad = shownMaxLoad = int.MinValue;   // a UI reload: write them again
            SetupUiAnimation();
            if (GoF2Remake.Multiplayer.NetGame.Active)
            {
                ChatView.Attach(gameObject, safeArea ?? root);   // multiplayer chat
                SquadView.Attach(gameObject, safeArea ?? root, level != null && level.Layout != null ? level.Layout.stationIndex : -1);   // squad, pilots here
                MultiplayerWindow.Attach(gameObject, safeArea ?? root);   // the Faction / Arena / Profile window
            }

            InputGlyph.TrackHintsOption(hints);
            dialog = root.Q("dialog");
            viewTitle = root.Q<Label>("viewTitle");
            toast = root.Q<Label>("toast");
            hangarButton = Bind("hangarButton", OpenHangar);
            loungeButton = Bind("loungeButton", OpenLounge);
            mapButton = Bind("mapButton", OpenMap);
            missionsButton = Bind("missionsButton", OpenMissions);
            statusButton = Bind("statusButton", OpenStatus);
            inspectButton = Bind("inspectButton", BeginInspect);
            // Remake mods: Customize, under Inspect ship, only for a customizable ship (Modding.ModShipKits).
            customizeButton = new Button { name = "customizeButton" };
            customizeButton.AddToClassList("station-button");
            customizeButton.AddToClassList("gof-semibold");
            inspectButton.parent.Insert(inspectButton.parent.IndexOf(inspectButton) + 1, customizeButton);
            customizeButton.RegisterCallback<PointerDownEvent>(_ => Play(buttonPush), TrickleDown.TrickleDown);
            customizeButton.clicked += () => { Play(buttonRelease); BeginCustomize(); };
            SetupInspect();   // after SetupUiAnimation: its footer sits over the menu's blocker
            launchButton = Bind("launchButton", AskLaunch);
            // The lounge's footer Back (lounge_ui.md 1.2): back to the main view, like Esc / B.
            Bind("loungeBack", () => { if (level != null && level.View == StationView.Lounge) level.SetView(StationView.Hangar); });
            dialogYes = Bind("dialogYes", () => { var a = dialogAction; CloseDialog(); a?.Invoke(); });
            dialogNo = Bind("dialogNo", () => { var a = dialogNoAction; CloseDialog(); a?.Invoke(); });
            InputGlyph.PadPrompt(root.Q<Button>("loungeBack"), PadButton.B);
            // Rework (docs/UI-DESIGN.md "Button prompts"): with a controller, A on the focused answer and B on No.
            InputGlyph.PadPrompt(dialogYes, PadButton.A, true);
            InputGlyph.PadPrompt(dialogNo, PadButton.B);
            hangarWindow = new HangarWindow(this, level, root);
            GoF2Remake.Multiplayer.NetStock.Changed -= OnSharedStock;
            GoF2Remake.Multiplayer.NetStock.Changed += OnSharedStock;
            GoF2Remake.Multiplayer.NetNews.Changed -= OnNews;
            GoF2Remake.Multiplayer.NetNews.Changed += OnNews;
            infoWindow = new ItemInfoWindow(this, root);
            lounge = new LoungePanel(this, level, root);
            SetupTicker();
            missions = new MissionsWindow(this, level, root);
            status = new StatusWindow(this, level, root);
            root.Q("storyDialogue").pickingMode = PickingMode.Ignore;
            if (voiceSource == null)
            {
                voiceSource = gameObject.AddComponent<AudioSource>();
                voiceSource.playOnAwake = false;
                voiceSource.spatialBlend = 0f;
            }
            storyDialogue = new DialogueView(root, voiceSource) { ButtonSound = push => Play(push ? buttonPush : buttonRelease) };
            storyDialogue.PageShown = id => { if (id == 1833) StartVoidAlarm(); };
            Bind("menuButton", OpenSystemMenu);
            // Rework (docs/UI-DESIGN.md "Button prompts"): the shop's controls are the visible hint row under the window again
            // (it was an "i" left of Menu).
            var menuB = root.Q<Button>("menuButton");
            if (menuB != null && menuB.parent.Q(className: "station-lounge-help") == null)
            {
                // A "?" with the lounge's help (627) while the lounge shows (StationMenu.uss .station-lounge-help).
                var loungeHelp = OptionInfo.Icon(() => Localization.Get(627));
                loungeHelp.AddToClassList("station-lounge-help");
                loungeHelp.style.position = Position.Relative;
                loungeHelp.style.width = loungeHelp.style.height = 40f;
                menuB.parent.Insert(menuB.parent.IndexOf(menuB), loungeHelp);
            }
            // Rework (docs/UI-DESIGN.md "Button prompts"): the lounge's keys at its bottom right while the visitor list is up
            // (Lounge.uss .lounge-prompts; a holder, since the bar sets its own display for the input in use).
            var safeHost = root.Q("safeArea");
            if (safeHost != null && safeHost.Q(className: "lounge-prompts") == null)
            {
                var holder = new VisualElement { pickingMode = PickingMode.Ignore };
                holder.AddToClassList("lounge-prompts");
                holder.Add(MenuKeys.Bar(MenuKeys.Rows.Select | MenuKeys.Rows.Confirm | MenuKeys.Rows.Back));
                safeHost.Add(holder);
            }
            PadKeyboard.Attach(root);   // a controller's keyboard for the text fields (chat, the multiplayer window, Mod Builder)
            KeyboardLift.Watch(root);   // phones: the focused text field kept above the on-screen keyboard
            systemMenu = root.Q("systemMenu");
            systemMain = root.Q("systemMenuMain");
            systemSave = root.Q("systemMenuSave");
            saveSlotList = root.Q<ScrollView>("saveSlotList");
            saveSlotList.mode = ScrollViewMode.Vertical;
            saveSlotList.verticalScrollerVisibility = ScrollerVisibility.Hidden;   // drag / wheel / focus scrolling instead
            saveSlotList.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            new DragScroll(saveSlotList);
            saveGameButton = Bind("saveGameButton", () => ShowSystemPage(SysPage.Save));
            mainMenuButton = Bind("mainMenuButton", () =>
            {
                if (Modding.ModBuilder.PlayTesting) { ModBuilderPlay.End(); return; }   // a Mod Builder play test: back to the builder
                ShowDialog(Localization.Get(523), BackToMenu);
            });
            systemClose = Bind("systemMenuClose", CloseSystemMenu);
            saveBack = Bind("saveBack", () => ShowSystemPage(SysPage.Main));
            InputGlyph.PadPrompt(systemClose, PadButton.B);   // B for a controller (docs/UI-DESIGN.md "Button prompts")
            InputGlyph.PadPrompt(saveBack, PadButton.B);
            BuildSystemExtras();

            RefreshTexts();

            HookDrag();
            root.RegisterCallback<PointerDownEvent>(OnPointerDown, TrickleDown.TrickleDown);
            // Modified: the main menu's click sound on every button and toggle clicked (those without one had none).
            root.RegisterCallback<ClickEvent>(e => { if (UiHover.ClickSounds(e.target)) Play(buttonRelease); });
            root.RegisterCallback<PointerMoveEvent>(e => { if (e.pointerType == PointerType.mouse && InputMode.MousePointing) { SetTouchMode(false); pointerDriven = true; pointerPos = e.position; } }, TrickleDown.TrickleDown);
            // The mouse takes the selection along to the button it is over (like MainMenu.HookFocusSound): a menu opened with
            // the mouse focused its first entry, and that highlight stayed beside the hovered one.
            root.RegisterCallback<PointerOverEvent>(e =>
            {
                if (e.pointerType != PointerType.mouse || !InputMode.MouseMovedRecently || !(e.target is VisualElement t)) return;   // not a list scrolling under it
                var b = t as Button ?? t.GetFirstAncestorOfType<Button>();
                if (b != null && UiHover.CanChangeFocus(b, e.pressedButtons) && b.ClassListContains("station-button") && Navigable(b) && b.focusController?.focusedElement != b) b.Focus();
                // Modified: the hover sound when the mouse comes onto a tab or a side menu button (the main view's); anything
                // else is silent under the mouse (UiHover.MenuTarget). The keys still sound on every move (FocusIn below).
                var h = UiHover.MenuTarget(t);
                if (h != lastHover) { lastHover = h; if (h != null && h.enabledInHierarchy) PlayHover(); }
            });
            root.RegisterCallback<NavigationMoveEvent>(OnNavigate, TrickleDown.TrickleDown);
            // Modified: the hover sound also when the keys / a controller move the selection to a button.
            root.RegisterCallback<FocusInEvent>(e => { if (!pointerDriven && !touchMode && e.target is VisualElement v && IsHoverable(v)) PlayHover(); }, TrickleDown.TrickleDown);
            UiFocus.SkipScrollBars(root);   // the controller walk: the hangar list's scroll bar took the D-pad's focus
            // The hangar window keeps its own selection; Enter / A are read in Update, so no button may also take them.
            root.RegisterCallback<NavigationSubmitEvent>(e =>
            {
                if (!HangarOpen || DialogOpen || GoF2Remake.Multiplayer.NetChat.Typing) return;
                e.StopPropagation();
                root.focusController?.IgnoreEvent(e);
            }, TrickleDown.TrickleDown);

            OnViewChanged();
            ApplyInputMode();
            UpdateLayout();

            // Remake: the Mod Builder's hangar (Modding.ModBuilder): its own screen instead of the station menu's.
            if (Modding.ModBuilder.Active && !Modding.ModBuilder.PlayTesting && builderView == null)
            {
                foreach (var child in root.Children()) child.style.display = DisplayStyle.None;
                builderView = new ModBuilderView(level, root, push => Play(push ? buttonPush : buttonRelease));
            }
        }

        /// <summary>The Mod Builder's screen while a project is open (it runs this scene; the station menu stays hidden).</summary>
        ModBuilderView builderView;

        Button Bind(string name, System.Action action)
        {
            var b = root.Q<Button>(name);
            b.RegisterCallback<PointerDownEvent>(_ => Play(buttonPush), TrickleDown.TrickleDown);
            b.clicked += () => { Play(buttonRelease); action(); };
            return b;
        }

        // ---- screens -----------------------------------------------------------------------------------

        bool viewWasLounge;

        /// <summary>The Enter / A press that opened the hangar window is still held: the window ignores Enter / A until it is
        /// released (#104: the Hangar button took A on its press, and in the same frame the window bought its first row).</summary>
        bool hangarConfirmHeld;

        bool medalsChecked, wantedChecked;

        readonly System.Collections.Generic.Queue<int> medalQueue = new System.Collections.Generic.Queue<int>();
        VisualElement dialogPicture;

        // ---- medal toasts --------------------------------------------------------------------------------------

        const float MedalToastFadeSeconds = 0.4f;
        /// <summary>A toast with more medals waiting after it: shorter, so a backlog doesn't take half a minute.</summary>
        const float MedalToastQueuedSeconds = 3f;
        VisualElement medalToast;
        AudioClip medalSound;
        float medalToastLeft;
        bool medalToastFading, medalToastHovered;

        /// <summary>Modified: the star map's help (the first-visit hint 628 / 631) for its "?" (also the Missions window's map).</summary>
        public string MapHelpText => HintText(Session.WorldIndex > 15 ? 628 : 631, true);

        /// <summary>Remake: whether the Missions window's map may travel: what the Map button needs (unlocked, a controlling
        /// device, the map not refused by the story).</summary>
        public bool CanTravelFromMap => level != null && Story.MapUnlocked && GoF2Remake.Multiplayer.NetProfileClient.Refusal == null
                                        && Story.MapRefusal == null;

        bool DialogOpen => dialog != null && dialog.ClassListContains("station-dialog-backdrop--shown");
        public bool IsDialogOpen => DialogOpen;

        int boozeAtOpen;

        AudioSource alarm;

        /// <summary>DialogueWindow::loadContent, text 1833 ("Alert! Void fighters..."): the music stops, 136 Space_Combat_Void
        /// plays as music and 162 Alert loops (event volume 0.198) until the station is left.</summary>
        /// <summary>Multiplayer: the station's shared stock changed.</summary>
        void OnSharedStock(int station)
        {
            if (level != null && level.Stock != null && level.Stock.station == station) hangarWindow?.StockChanged();
        }

        void OnDestroy()
        {
            GoF2Remake.Multiplayer.NetStock.Changed -= OnSharedStock;
            GoF2Remake.Multiplayer.NetNews.Changed -= OnNews;
            builderView?.Dispose();
            if (runtimePanel != null) Destroy(runtimePanel);   // the per-scene clone: its panel (atlas, GPU buffers) goes with it
        }

        public void PlayPush() => Play(buttonPush);
        public void PlayRelease() => Play(buttonRelease);
        public void PlayClip(AudioClip clip) => Play(clip);

        /// <summary>A squad popup is on screen (SquadView): the menu takes the selection back when it is answered, and
        /// gives it to the popup's Accept over the first frames, until the focus lands (the popup is laid out a frame
        /// after it appears).</summary>
        bool squadPopupShown;
        int squadPopupTries;
        const int PopupFocusTries = 20;

        ItemInfoWindow infoWindow;
        /// <summary>The full-screen item / ship details (ListItemWindow), shared by the hangar and the lounge.</summary>
        public ItemInfoWindow InfoWindow => infoWindow;

        bool baseChecked;

        bool fineChecked;

        /// <summary>Remake (#37, #46): the menu's Hangar, Lounge, Map, Launch and system menu wait until the docking checks
        /// (the pirate base, the fine, the story's conversation, Kaamo) have had their first go after the landing and the
        /// settle: they don't run while the hangar window or the system menu is open, so opening one first traded at an
        /// enemy station without its fine, saved the game with it unpaid and sold a loaner before its conversation took it.</summary>
        bool arrivalChecksRun;
        bool ArrivalPending => !arrivalChecksRun;
        const float ArrivalSettleMs = 1000f;
        float settleMs;

        bool kaamoChecked;

        bool wingmenChecked;

        bool pendingChecked, rescueChecked;

        // ---- system menu (MenuTouchWindow: Save game, Back to Main Menu) -----------------------------------

        bool SystemMenuOpen => systemMenu != null && systemMenu.ClassListContains("station-dialog-backdrop--shown");
        bool SavePageOpen => SystemMenuOpen && (sysPage == SysPage.Save || sysPage == SysPage.Load);

        // MenuTouchWindow mode 2 (the station's Menu): 28 Start new game, 29 Load game, 30 Save game, 31 Options,
        // 43 About, 522 Back to Main Menu (the language, iPad only, is in Options; the original has no Back button).
        enum SysPage { Main, Save, Load, Options, Debug }
        SysPage sysPage;
        Button newGameButton, loadGameButton, optionsButton, aboutButton, optionsBack, debugButton;
        /// <summary>The Debug page's scroll list (remake-only cheats, CheatsCatalog).</summary>
        bool DebugPage => sysPage == SysPage.Debug;
        VisualElement systemOptions;
        /// <summary>The Options page: the main menu's Options panel (OptionsView) in place of the menu's panel, built anew
        /// each time it opens.</summary>
        OptionsView optionsView;
        ScrollView optionsScroll;
        readonly System.Collections.Generic.List<OptionControl> stationOptions = new System.Collections.Generic.List<OptionControl>();

        /// <summary>The Debug page: the cheat toggles and actions (CheatsCatalog); an action reports in a toast.</summary>
        int stationDebugTab;
        Button activeDebugTab;
        VisualElement debugPrompts, debugAccent;

        const int StationDebugTabs = 5;   // Cheats, Actions, Give items, Ships, Presets

        VisualElement focusBeforeSystemMenu;

        // ---- turning the ship (ModStation::OnTouchMove / OnTouchEnd) ----------------------------------------

        /// <summary>Panel units per game radian: the original's 120 px on a 480 px high screen, a quarter of the height.</summary>
        float UnitsPerRadian => Mathf.Max(1f, root.layout.height) * (StationTables.TurntablePixelsPerRadian / 480f);

        /// <summary>Modified: the mouse moved or pressed last (Select leaves nothing marked); the keys / a controller clear it.</summary>
        bool pointerDriven = true;
        Vector2 pointerPos;   // the mouse's last place (panel coordinates)

        VisualElement lastHover;

        /// <summary>The main view's last selected button before Launch: left goes back to it.</summary>
        VisualElement lastSideItem;

        // ---- remake: Inspect ship (World.HangarInspect, StationLevel.BeginInspect) ----------------------------

        // The hangar's camera zooms in on the player's ship and orbits it like the flight's Action Freeze (PhotoMode): a drag
        // or a finger turns it (with a fling, 0.9 an original frame), the wheel, a pinch, + / - (Page Up / Down) and the triggers
        // zoom, the arrows / W A S D and the sticks turn it too. The menu flies out meanwhile (the UI animation); a footer
        // keeps Back and the controls' hints. Back: Esc, Backspace, B, or the key that opened it (6 / I, the right stick press).
        // Hide UI (H, the controller's Y) clears the screen (a short click / tap or H / Y again brings it back; a drag still
        // turns); Screenshot (Enter / P, the controller's A) saves the view without the UI, like Action Freeze's 60 Save to
        // library (PhotoMode.Store: PC Pictures/Galaxy on Fire 2, Android MediaStore; 55 / 56). Their hints are clickable
        // (a tap on touch, which has no keys). Remake (players' suggestion, 2026-10-17): Action Freeze's photo tools
        // (PhotoTools): the free camera (F / X) within six ship radii of the hull, above the pad, and the Camera settings
        // panel (Tab / View); not in the Customize screen.
        VisualElement inspectOverlay, inspectHints, inspectFooter;
        PhotoTools inspectTools;
        Label inspectTitle, inspectMessage;
        Button inspectBack;
        bool inspectDragging, inspectUiHidden, inspectCapturing;
        Vector2 inspectLast, inspectFling;
        float inspectPinch = -1f, inspectDragTravel, inspectMessageMs;

        // ---- remake mods: Customize (a customizable ship's build, Modding.ModShipKits, ShipCustomizePanel) ----------------

        ShipCustomizePanel customize;
        bool customizing;
        string customizeBefore;   // the player's build text before the screen opened (null: none saved)
        float customizeCheckMs;

        bool CanCustomize => level != null && level.View == StationView.Hangar && !Vr.VrMode.Enabled && !level.PlayerFlying
                             && ModShipKits.CustomizationOf(Session.ShipIndex) is ModShipKits.Kit kk   // a kit ship, or one a mod customizes
                             && kk.HasChoices(ModShipKits.TypeOf(Session.ShipIndex));

        // ---- remake: the menu flies / fades out for the hangar take-off and back in after the landing ----

        // The original cuts straight between space and the parked ship, so its menu never moves; the remake's hangar flights
        // hid it at once (display: none) and showed it again the same way. Now the top bar slides up, Launch right and the
        // ticker down while the safe area fades, and the side panel's entries (the station information, then each shown
        // button) come in one by one like the main menu's buttons (MainMenu.uss .menu-button--hidden / .delay-N: 40 px from
        // the left and faded, 180 ms ease-out cubic each, 50 ms apart; going away the same run backwards, the last entry
        // first). Nothing takes input meanwhile (a blocker over the panel, the navigation events stopped, Update returns
        // early). The state at the station's first frame is taken as it is: a fly-in starts with the menu away, a ship that
        // is simply on its pad (a load, a new game, the flights off) shows it at once.
        const float UiAnimMs = 450f;
        const float UiTopOffset = 90f, UiLaunchOffset = 220f, UiTickerOffset = 60f;   // panel px
        const float UiFundsOffset = 160f;   // the credits and cargo under the bar: off the top edge too
        const float UiItemOffset = 40f, UiItemMs = 180f, UiItemStepMs = 50f;            // the main menu's buttons
        float uiShown = 1f;   // 0 away .. 1 in place
        bool uiStateTaken;
        VisualElement uiTop, uiFunds, uiTicker, uiBlocker, uiInfo, uiButtons;
        readonly System.Collections.Generic.List<VisualElement> uiItems = new System.Collections.Generic.List<VisualElement>();

        /// <summary>The menu is away or on its way in / out: no input.</summary>
        bool UiBlocked => uiShown < 1f;

        // ---- credits and cargo (remake, players' request) -----------------------------------------------------------
        // The main view and the Space Lounge show the credits and the hold at the top right under the bar, the hangar
        // window footer's look ("CARGO 65 / 195 t", the credits in amber; red when overloaded), so a lounge deal, a
        // mission payout, a medal reward, a fine, a multiplayer trade or a faction toll shows without opening the hangar.
        // Read every frame and written only when a value changed. Hidden under the hangar, Missions and Status windows
        // (StationMenu.uss) and with the rest of the menu under the star map and while it is flown away.
        Label fundsCargo, fundsCredits;
        int shownCredits = int.MinValue, shownLoad = int.MinValue, shownMaxLoad = int.MinValue;

        void Update()
        {
            if (root == null || level == null) return;
            // The layout first: the Mod Builder's screen below shares this panel and its scale.
            if (lastScreen != ScreenSize() || lastSafeArea != Screen.safeArea || lastLayout != UiLayout.Revision) UpdateLayout();
            if (builderView != null) { builderView.Update(); return; }   // the Mod Builder's hangar
            SyncHover();
            // Release velocity: the turn per frame, smoothed (the original uses the last frame's dx). Modified: first thing, so
            // the hangar's window (Ship / Shop, open most of the time in the hangar) and the other early returns below no
            // longer skip it: a drag let go there always measured 0 and the ship never kept turning.
            if (dragPointer >= 0)
            {
                dragVelocity = Mathf.Lerp(dragVelocity, dragFrameRadians / Mathf.Max(Time.unscaledDeltaTime, 1e-3f), 0.6f);
                dragFrameRadians = 0f;
            }
            UpdateTicker();   // scrolls on under dialogs and windows (it used to wait, then fly in again)
            UpdateFunds();
            // Remake hangar flights: the menu hides while the ship flies in or out; the conversations, windows and hints
            // wait until it has landed. Any key / tap / button skips the flight.
            bool flying = level.PlayerFlying;
            bool inspecting = level.Inspect.Active;
            UpdateUiAnimation(flying || inspecting);   // Inspect ship: the menu flies out like for a flight
            // Modified: in the lounge before its bar shows (the camera intro), the hangar's menu is hidden at once, as the
            // hangar window hides it (Lounge.uss .lounge-intro); lounge-open takes over in the same frame it comes.
            root.EnableInClassList("lounge-intro", level.View == StationView.Lounge && !root.ClassListContains("lounge-open"));
            KeysTakeOver();
            UnmarkLeftButton();
            RefreshViewTitle();
            if (inspectOverlay != null) inspectOverlay.EnableInClassList("inspect-overlay--shown", inspecting);
            if (flying)
            {
                if (World.SpaceLevel.PlayerTriedToFly()) level.SkipPlayerFlight();
                settleMs = ArrivalSettleMs;
                return;
            }
            if (inspecting)
            {
                if (customizing)
                {
                    customize.HandleInput(GoF2Remake.Multiplayer.NetChat.Keys, Gamepad.current, Time.unscaledDeltaTime * 1000f);
                    level.PreviewEngineDrive(customize.BoostRowSelected ? 2f : 1f);   // a boost colour picked: the engines boost
                    // The ship in the middle of the screen right of the panel, not behind it.
                    var panelBox = customize.Root.worldBound;
                    var screenBox = root.worldBound;
                    if (screenBox.width > 0f && !float.IsNaN(panelBox.xMax))
                        level.InspectFrameX = Mathf.Clamp(((panelBox.xMax - screenBox.x) + screenBox.width) * 0.5f / screenBox.width, 0.5f, 0.8f);
                    if (customizing) UpdateInspect(true);
                }
                else UpdateInspect();
                return;
            }
            if ((customizeCheckMs -= Time.unscaledDeltaTime * 1000f) <= 0f) { customizeCheckMs = 500f; RefreshCustomizeButton(); }
            if (UiBlocked)
            {
                // The menu still flying in after the landing: no input yet; the settle runs on meanwhile.
                if (settleMs > 0f) settleMs -= Time.unscaledDeltaTime * 1000f;
                return;
            }
            UpdateMedalToast();
            if (Flight.GameControls.BlocksMenus) return;   // a key binding is being captured (Options): its key isn't a menu key
            DpadTapNavigation.Pump(root);   // D-pad taps the panel's own navigation drops (the Steam controller)
            // The Debug and Options pages' tabs: Q / E, LB / RB.
            if (SystemMenuOpen && (sysPage == SysPage.Debug || sysPage == SysPage.Options && optionsView != null) && !DialogOpen)
            {
                var dkb = GoF2Remake.Multiplayer.NetChat.Keys;
                var dpad = Gamepad.current;
                int tab = 0;
                if ((dkb != null && dkb.qKey.wasPressedThisFrame) || (dpad != null && dpad.leftShoulder.wasPressedThisFrame)) tab = -1;
                if ((dkb != null && dkb.eKey.wasPressedThisFrame) || (dpad != null && dpad.rightShoulder.wasPressedThisFrame)) tab = 1;
                if (tab != 0)
                {
                    if (sysPage == SysPage.Debug) { Play(buttonRelease); SwitchDebugTab(tab); } else optionsView.StepTab(tab);
                    return;
                }
            }
            // Modified: the Missions window's tabs too (Q / E, LB / RB), like the hangar's and the menu's.
            if (missions != null && missions.IsOpen && !DialogOpen && !SystemMenuOpen && !StarMap.IsOpen)
            {
                var mkb = GoF2Remake.Multiplayer.NetChat.Keys;
                var mpad = Gamepad.current;
                int mt = 0;
                if ((mkb != null && mkb.qKey.wasPressedThisFrame) || (mpad != null && mpad.leftShoulder.wasPressedThisFrame)) mt = -1;
                if ((mkb != null && mkb.eKey.wasPressedThisFrame) || (mpad != null && mpad.rightShoulder.wasPressedThisFrame)) mt = 1;
                if (mt != 0) { missions.StepTab(mt); return; }
            }
            // Modified: the Status window's tabs too (Status, Medals).
            if (status != null && status.IsOpen && !DialogOpen && !SystemMenuOpen)
            {
                var skb = GoF2Remake.Multiplayer.NetChat.Keys;
                var spad = Gamepad.current;
                int st = 0;
                if ((skb != null && skb.qKey.wasPressedThisFrame) || (spad != null && spad.leftShoulder.wasPressedThisFrame)) st = -1;
                if ((skb != null && skb.eKey.wasPressedThisFrame) || (spad != null && spad.rightShoulder.wasPressedThisFrame)) st = 1;
                if (st != 0) { status.StepTab(st); return; }
            }
            if (lounge != null && lounge.Active != root.ClassListContains("lounge-open")) lounge.OnViewChanged();   // also under a dialog
            if (StarMap.IsOpen) return;   // the map has its own input
            if (storyDialogue != null && storyDialogue.IsOpen)
            {
                // The Menu button over a conversation: it waits (voice, typing, auto-advance) and the menu takes the keys.
                storyDialogue.Paused = SystemMenuOpen;
                if (!SystemMenuOpen) { storyDialogue.Tick(Time.unscaledDeltaTime * 1000f); return; }
            }
            // Remake: a moment on the pad after the hangar flight lands before anything talks (the original, without the
            // flight, opens them as the station loads).
            if (settleMs > 0f) settleMs -= Time.unscaledDeltaTime * 1000f;
            else if (RunDockingChecks()) return;
            lounge?.Update();
            // A squad invitation / trade request (SquadView): with keys or a controller its Accept takes the focus as it
            // shows, so it can be answered at all, and the menu's selection comes back when it is answered. The popup is
            // laid out a frame after it appears (a focus set before that doesn't take), so it is tried again until it
            // lands; Select itself does nothing while the mouse is the one in use.
            bool popupNow = SquadView.PopupOpen;
            bool canFocusPopup = !HangarOpen && !DialogOpen && !SystemMenuOpen && !MultiplayerWindow.IsOpenAny
                                 && InputMode.Current != InputKind.Touch && !InputMode.MousePointing;
            if (popupNow != squadPopupShown)
            {
                squadPopupShown = popupNow;
                squadPopupTries = popupNow ? PopupFocusTries : 0;
                if (!popupNow && canFocusPopup) Select(DefaultFocus());
            }
            if (popupNow && squadPopupTries > 0 && canFocusPopup)
            {
                var popupButtons = SquadView.PopupButtons;
                if (popupButtons != null && popupButtons.Length > 0 && Navigable(popupButtons[0]))
                {
                    Select(popupButtons[0]);
                    squadPopupTries = root.focusController?.focusedElement == popupButtons[0] ? 0 : squadPopupTries - 1;
                }
                else squadPopupTries--;
            }

            var kb = GoF2Remake.Multiplayer.NetChat.Keys;
            var pad = Gamepad.current;
            if ((kb != null && kb.escapeKey.wasPressedThisFrame) || (pad != null && pad.buttonEast.wasPressedThisFrame)) { Back(); return; }
            if (infoWindow != null && infoWindow.IsOpen) { infoWindow.Tick(); return; }   // it takes all input
            if (MultiplayerWindow.IsOpenAny) return;   // the faction window's buttons and text fields take the input
            if (pad != null && pad.startButton.wasPressedThisFrame && !DialogOpen)
            {
                Play(buttonRelease);
                if (SystemMenuOpen) CloseSystemMenu(); else OpenSystemMenu();
                return;
            }
            if (toastMs > 0f && (toastMs -= Time.unscaledDeltaTime * 1000f) <= 0f) toast.RemoveFromClassList("station-toast--shown");
            // #74: the panel only sends a controller's navigation to a focused element. ApplyInputMode focuses the default
            // button when the controller becomes the input, but a station loaded with the controller already in use (the menu
            // still away behind the hangar flight) or a closed window can leave nothing focused: the D-pad then did nothing
            // until a dialog, a menu or a touch set the focus again.
            // Remake (Discord report 2026-10-18, still on 10.18): also on a navigation press while the input mode isn't the
            // controller (Android sends a controller's B as Escape, some pads the D-pad as arrow keys), with touch mode left
            // first (Select does nothing in it), and into the open Missions / Status window or lounge chat.
            if (!HangarOpen && root.focusController != null && root.focusController.focusedElement == null
                && (InputMode.Current == InputKind.Gamepad || MenuFocus.NavPressed(pad, kb)))
            {
                SetTouchMode(false);
                var target = missions != null && missions.IsOpen ? MenuFocus.First(missions.NavItems())
                           : status != null && status.IsOpen ? MenuFocus.First(status.NavItems())
                           : lounge != null && lounge.ChatOpen ? MenuFocus.First(lounge.NavItems())
                           : DefaultFocus();
                target?.Focus();
            }
            if (DialogOpen || SystemMenuOpen) return;
            if (missions != null && missions.IsOpen) return;
            if (status != null && status.IsOpen) return;
            if (lounge != null && lounge.NumberKey(kb)) return;   // #75: in the lounge the number keys pick its visitors
            // The PC version's "Menu button 1 - 9" (3356) tap Globals::sub_menu_buttons: the main view's Hangar / Lounge / Map /
            // Missions / Status, the hangar window's three tabs (HangarWindow::initialize): 4 / 5 do nothing there.
            if (kb != null && kb.digit5Key.wasPressedThisFrame && !HangarOpen) { Play(buttonRelease); OpenStatus(); return; }
            if (lounge != null && lounge.ChatOpen) return;
            if ((kb != null && kb.digit4Key.wasPressedThisFrame && !HangarOpen) || (pad != null && pad.selectButton.wasPressedThisFrame))
            {
                Play(buttonRelease);
                OpenMissions();
                return;
            }

            if (HangarOpen)
            {
                float dtMs = Time.unscaledDeltaTime * 1000f;
                int v = 0, h = 0;
                if (kb != null)
                {
                    if (kb.wKey.isPressed || kb.upArrowKey.isPressed) v -= 1;
                    if (kb.sKey.isPressed || kb.downArrowKey.isPressed) v += 1;
                    if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) h -= 1;
                    if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) h += 1;
                }
                if (pad != null)
                {
                    var stick = pad.leftStick.ReadValue() + pad.dpad.ReadValue();
                    if (stick.y > 0.5f) v -= 1; else if (stick.y < -0.5f) v += 1;
                    if (stick.x < -0.5f) h -= 1; else if (stick.x > 0.5f) h += 1;
                }
                hangarWindow.HoldDirections(System.Math.Sign(v), System.Math.Sign(h), dtMs);
                hangarWindow.Update(dtMs);
                if (hangarConfirmHeld && !(kb != null && (kb.enterKey.isPressed || kb.numpadEnterKey.isPressed || kb.spaceKey.isPressed))
                    && !(pad != null && pad.buttonSouth.isPressed))
                    hangarConfirmHeld = false;
                if (!hangarConfirmHeld && ((kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)) || (pad != null && pad.buttonSouth.wasPressedThisFrame)))
                    hangarWindow.Action();
                else if ((kb != null && kb.xKey.wasPressedThisFrame) || (pad != null && pad.buttonWest.wasPressedThisFrame))
                    hangarWindow.SecondaryAction();   // Sell a stored hull (Kaamo Club)
                // Remake: LT / RT = Sell all / Buy all (Store all / Take all), the keyboard's Shift + left / right: held for a
                // second, like the buttons (HangarWindow.Update polls them).
                else if ((kb != null && kb.qKey.wasPressedThisFrame) || (pad != null && pad.leftShoulder.wasPressedThisFrame))
                {
                    Play(buttonPush);
                    hangarWindow.PrevTab();   // Q / LB to the left, E / RB to the right, like the options' tabs
                }
                else if ((kb != null && kb.eKey.wasPressedThisFrame) || (pad != null && pad.rightShoulder.wasPressedThisFrame))
                {
                    Play(buttonPush);
                    hangarWindow.NextTab();
                }
                // 1 / 2 / 3 = Ship / Shop (the Kaamo Club's Store) / Blueprints (#30, the PC version's menu buttons).
                else if (kb != null && kb.digit1Key.wasPressedThisFrame) { Play(buttonPush); hangarWindow.SetTab(HangarWindow.Tab.Ship); }
                else if (kb != null && kb.digit2Key.wasPressedThisFrame) { Play(buttonPush); hangarWindow.SetTab(HangarWindow.Tab.Shop); }
                else if (kb != null && kb.digit3Key.wasPressedThisFrame) { Play(buttonPush); hangarWindow.SetTab(HangarWindow.Tab.Blueprints); }
                else if ((kb != null && kb.iKey.wasPressedThisFrame) || (pad != null && pad.buttonNorth.wasPressedThisFrame))
                    hangarWindow.OpenInfo();   // remake keys for the row's info button
                return;
            }

            if ((kb != null && kb.digit1Key.wasPressedThisFrame) || (pad != null && pad.leftShoulder.wasPressedThisFrame))
                OpenHangar();
            if ((kb != null && kb.digit2Key.wasPressedThisFrame) || (pad != null && pad.rightShoulder.wasPressedThisFrame))
                OpenLounge();
            if ((kb != null && (kb.mKey.wasPressedThisFrame || kb.digit3Key.wasPressedThisFrame)) || (pad != null && pad.buttonNorth.wasPressedThisFrame))
            {
                Play(buttonRelease);
                OpenMap();
                return;
            }
            if ((kb != null && kb.lKey.wasPressedThisFrame) || (pad != null && pad.buttonWest.wasPressedThisFrame))
            {
                Play(buttonRelease);
                AskLaunch();
                return;
            }
            // Remake: Inspect ship, 6 (the next "Menu button") / I, the controller's right stick press.
            if (level.View == StationView.Hangar
                && ((kb != null && (kb.digit6Key.wasPressedThisFrame || kb.iKey.wasPressedThisFrame)) || (pad != null && pad.rightStickButton.wasPressedThisFrame)))
            {
                Play(buttonRelease);
                BeginInspect();
                return;
            }

            if (dragPointer >= 0) { }   // the release velocity: at the top (Modified)
            else if (level.View == StationView.Hangar)
            {
                float turn = 0f;
                if (kb != null)
                {
                    if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) turn -= 1f;
                    if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) turn += 1f;
                }
                if (pad != null && Mathf.Abs(pad.rightStick.x.ReadValue()) > 0.15f) turn += pad.rightStick.x.ReadValue();
                if (turn != 0f) level.RotateShip(Mathf.Clamp(turn, -1f, 1f) * keyTurnSpeed * Time.deltaTime);
            }
            else if (level.IntroPlaying && ((kb != null && kb.anyKey.wasPressedThisFrame) || (pad != null && pad.buttonSouth.wasPressedThisFrame)))
                level.SkipIntro();
        }

        /// <summary>Modified: a click sounds as in the main menu: only the release, on the click (a pointer press's push is
        /// left out, but in the shop window), once per frame (the generic click sound, UiHover.ClickSounds, and a button's
        /// own).</summary>
        int pressFrame = -1, releaseFrame = -1;

        void Play(AudioClip clip)
        {
            if (clip != null && clip == buttonPush && pressFrame == Time.frameCount) return;
            if (clip != null && clip == buttonRelease)
            {
                if (releaseFrame == Time.frameCount) return;
                releaseFrame = Time.frameCount;
            }
            if (sfxSource != null && clip != null) sfxSource.PlayOneShot(GoF2Remake.Modding.ModSounds.Get(clip), Settings.SfxVolume);
        }

    }
}
