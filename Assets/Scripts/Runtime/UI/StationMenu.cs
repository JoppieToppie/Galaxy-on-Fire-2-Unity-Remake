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
//   Controller: Xbox hints; right stick turns the ship, LB hangar / RB lounge, Y map, X launch, B back, Menu to the main menu.
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

using GoF2Remake.Data;
using GoF2Remake.World;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using PointerType = UnityEngine.UIElements.PointerType;

namespace GoF2Remake.UI
{
    [RequireComponent(typeof(PanelRenderer))]
    public class StationMenu : MonoBehaviour
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
        Button hangarButton, loungeButton, mapButton, missionsButton, statusButton, launchButton, dialogYes, dialogNo;
        StatusWindow status;
        LoungePanel lounge;
        bool safeAreaHidden;
        MissionsWindow missions;
        Label tickerText;
        float tickerX, tickerUnitWidth;
        bool tickerReady;
        string tickerSingle = "";
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
            if (level != null) level.ViewChanged += OnViewChanged;
        }

        void OnDisable()
        {
            panelRenderer?.UnregisterUIReloadCallback(OnUIReload);
            InputMode.Changed -= ApplyInputMode;
            if (level != null) level.ViewChanged -= OnViewChanged;
        }

        void OnUIReload(PanelRenderer renderer, VisualElement rootElement, int version)
        {
            // The first load can come before StationLevel.Awake has built the station: try again next frame.
            if (level != null && level.Layout == null)
            {
                rootElement.schedule.Execute(() => OnUIReload(renderer, rootElement, version)).ExecuteLater(1);
                return;
            }
            if (!Localization.IsLoaded && languageTables != null && languageTables.Length > 0)
            {
                int i = languageCodes != null ? System.Array.IndexOf(languageCodes, Settings.Language) : -1;
                if (i < 0) i = 0;
                Localization.Load(languageCodes != null && i < languageCodes.Length ? languageCodes[i] : "en", languageTables[i]);
            }
            root = rootElement;
            root.style.flexGrow = 1;
            root.pickingMode = PickingMode.Ignore;
            safeArea = root.Q("safeArea");
            dragZone = root.Q("dragZone");
            hints = root.Q("hints");
            if (GoF2Remake.Multiplayer.NetGame.Active)
            {
                ChatView.Attach(gameObject, safeArea ?? root);   // multiplayer chat
                SquadView.Attach(gameObject, safeArea ?? root, level != null && level.Layout != null ? level.Layout.stationIndex : -1);   // squad, pilots here
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
            launchButton = Bind("launchButton", AskLaunch);
            // The lounge's footer Back (lounge_ui.md 1.2): back to the main view, like Esc / B.
            Bind("loungeBack", () => { if (level != null && level.View == StationView.Lounge) level.SetView(StationView.Hangar); });
            dialogYes = Bind("dialogYes", () => { var a = dialogAction; CloseDialog(); a?.Invoke(); });
            dialogNo = Bind("dialogNo", () => { var a = dialogNoAction; CloseDialog(); a?.Invoke(); });
            hangarWindow = new HangarWindow(this, level, root);
            GoF2Remake.Multiplayer.NetStock.Changed -= OnSharedStock;
            GoF2Remake.Multiplayer.NetStock.Changed += OnSharedStock;
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
            systemMenu = root.Q("systemMenu");
            systemMain = root.Q("systemMenuMain");
            systemSave = root.Q("systemMenuSave");
            saveSlotList = root.Q<ScrollView>("saveSlotList");
            saveSlotList.mode = ScrollViewMode.Vertical;
            saveSlotList.verticalScrollerVisibility = ScrollerVisibility.Hidden;   // drag / wheel / focus scrolling instead
            saveSlotList.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            new DragScroll(saveSlotList);
            saveGameButton = Bind("saveGameButton", () => ShowSystemPage(SysPage.Save));
            mainMenuButton = Bind("mainMenuButton", () => ShowDialog(Localization.Get(523), BackToMenu));
            systemClose = Bind("systemMenuClose", CloseSystemMenu);
            saveBack = Bind("saveBack", () => ShowSystemPage(SysPage.Main));
            BuildSystemExtras();

            var st = level != null ? level.Station : null;
            string T(int id) => Localization.Get(id);
            root.Q<Label>("stationTitle").text = st == null ? "" :
                (st.index == 101 ? st.name : $"{st.name} {T(136)}").ToUpperInvariant();   // no suffix for station 101
            root.Q<Label>("systemName").text = st == null ? "" : $"{st.systemName} {T(137)}";
            root.Q<Label>("techLevel").text = st == null ? "" : $"{T(133)}: {st.techLevel}";
            int race = level != null ? level.Layout.raceId : -1;
            var raceLabel = root.Q<Label>("raceName");
            raceLabel.text = race >= 0 && race < 4 ? T(406 + race) : "";
            raceLabel.style.display = raceLabel.text.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            hangarButton.text = T(167).ToUpperInvariant();
            loungeButton.text = T(398).ToUpperInvariant();
            mapButton.text = T(177).ToUpperInvariant();
            missionsButton.text = T(129).ToUpperInvariant();
            statusButton.text = T(169).ToUpperInvariant();
            launchButton.text = Localization.Extra("stationLaunch", "LAUNCH");
            root.Q<Button>("loungeBack").text = Localization.Extra("hudBack", "BACK");
            dialogNo.text = T(135).ToUpperInvariant();
            root.Q<Button>("menuButton").text = Localization.Extra("hudMenu", "MENU");
            root.Q<Label>("systemMenuTitle").text = T(172).ToUpperInvariant();       // Menu
            saveGameButton.text = T(30).ToUpperInvariant();                          // Save game
            mainMenuButton.text = T(522).ToUpperInvariant();                         // Back to Main Menu
            systemClose.text = saveBack.text = Localization.Extra("hudBack", "BACK");

            HookDrag();
            root.RegisterCallback<PointerDownEvent>(OnPointerDown, TrickleDown.TrickleDown);
            root.RegisterCallback<PointerMoveEvent>(e => { if (e.pointerType == PointerType.mouse) SetTouchMode(false); }, TrickleDown.TrickleDown);
            root.RegisterCallback<NavigationMoveEvent>(OnNavigate, TrickleDown.TrickleDown);
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
        }

        Button Bind(string name, System.Action action)
        {
            var b = root.Q<Button>(name);
            b.RegisterCallback<PointerDownEvent>(_ => Play(buttonPush), TrickleDown.TrickleDown);
            b.clicked += () => { Play(buttonRelease); action(); };
            return b;
        }

        // ---- screens -----------------------------------------------------------------------------------

        void OnViewChanged()
        {
            if (root == null || level == null) return;
            bool inLounge = level.View == StationView.Lounge;
            hangarButton.EnableInClassList("station-button--current", HangarOpen);
            loungeButton.EnableInClassList("station-button--current", inLounge);
            viewTitle.text = HangarOpen ? Localization.Get(167).ToUpperInvariant() : inLounge ? Localization.Get(398).ToUpperInvariant() : "";
            viewTitle.style.display = viewTitle.text.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            dragVelocity = 0f;
            lounge?.OnViewChanged();
            ApplyStoryLocks();
            BuildHints(InputMode.Current);
        }

        /// <summary>ModStation::OnInitialize: Hangar from index 5, Map from 9, Space Lounge from 12 (half transparent before).</summary>
        void ApplyStoryLocks()
        {
            int station = level != null && level.Station != null ? level.Station.index : -1;
            hangarButton.SetEnabled(Story.HangarUnlocked);
            mapButton.SetEnabled(Story.MapUnlocked);
            missionsButton.SetEnabled(Story.MapUnlocked);   // Missions: locked before campaign 9 like the Map
            loungeButton.SetEnabled(Story.LoungeUnlocked(station));
            if (hints != null) BuildHints(InputMode.Current);   // the key hints follow the enabled buttons
        }

        bool HangarOpen => hangarWindow != null && hangarWindow.IsOpen;

        /// <summary>Station button 0 (ModStation::OnKeyPress): the Hangar window over the main view.</summary>
        void OpenHangar()
        {
            if (HangarOpen || level == null || !Story.HangarUnlocked) return;
            if (level.View != StationView.Hangar) level.SetView(StationView.Hangar);
            FirstVisitHint(8, 622);   // before the first row's selection hint
            hangarWindow.Open();
            level.SetHangarWindowOpen(true);
            boozeAtOpen = BoozeInHold();   // ModStation::OnKeyPress: ModStation+0xcc
            root.AddToClassList("hangar-open");
            if (root.focusController?.focusedElement is VisualElement f) f.Blur();
            OnViewChanged();
        }

        /// <summary>ModStation::OnKeyPress: a window's first opening shows its help once (Layout::initHelpWindow; the flag
        /// in Globals::hints): hangar 622 (8), lounge 627 (0xd), map 628 (0xe, campaign &gt; 15) or the system map 631
        /// (0xf, earlier), missions 635 (0x13), status 640 (0x18, phones only). Keyboard / controller: the +1 variants
        /// where the original has them.</summary>
        void FirstVisitHint(int flag, int text, bool keyVariant = false)
        {
            if (DialogOpen || !Settings.TutorialHints || !Session.Hints.Add(flag)) return;   // the tutorial popups option
            ShowDialog(HintText(text, keyVariant), null, true);
        }

        static string HintText(int text, bool keyVariant) =>
            World.FlightHints.KeyTokens(Localization.Get(keyVariant && InputMode.Current != InputKind.Touch ? text + 1 : text));

        public void CloseHangar()
        {
            if (!HangarOpen) return;
            if (!hangarWindow.ReadyToClose()) return;   // an uncommitted blueprint shipment asks first
            hangarWindow.Close();
            level?.SetHangarWindowOpen(false);
            // ModStation::OnTouchEnd: the booze gained during this visit counts for Personal Need (Status+0xa8).
            int gained = BoozeInHold() - boozeAtOpen;
            if (gained > 0) Session.BoozeBought += gained;
            root.RemoveFromClassList("hangar-open");
            OnViewChanged();
            Select(hangarButton);
        }

        /// <summary>NewsTicker: built once per docking (ModStation::OnInitialize state 0x3c), main view only.</summary>
        void SetupTicker()
        {
            tickerText = root.Q<Label>("tickerText");
            var st = level != null ? level.Station : null;
            bool shown = st != null && NewsTicker.ShownAt(st.index, st.system);
            root.EnableInClassList("ticker-off", !shown);
            if (!shown) return;
            tickerSingle = NewsTicker.Build(level.Database, st.system, level.Layout.raceId) ?? "";
            tickerText.text = tickerSingle;
            tickerX = 0f;
            tickerReady = false;
            tickerText.style.visibility = Visibility.Hidden;   // until it starts at the strip's right edge
        }

        /// <summary>NewsTicker::update: x -= dt * 50 px/s. Remake: the strip is never empty: the news (one copy = the items +
        /// separator) is repeated to cover the strip plus one copy, starts already filled, and wraps by exactly one copy
        /// (the original scrolled one text in from the right edge).</summary>
        void UpdateTicker()
        {
            if (tickerText == null || root.ClassListContains("ticker-off")) return;
            float strip = tickerText.parent.resolvedStyle.width;
            if (float.IsNaN(strip) || strip <= 0f) return;
            if (!tickerReady)
            {
                string unit = tickerSingle + NewsTicker.Separator;
                var size = tickerText.MeasureTextSize(unit, 0f, VisualElement.MeasureMode.Undefined, 0f, VisualElement.MeasureMode.Undefined);
                if (tickerSingle.Length == 0 || float.IsNaN(size.x) || size.x <= 0f) return;
                tickerReady = true;
                tickerUnitWidth = size.x;
                int copies = Mathf.CeilToInt(strip / tickerUnitWidth) + 1;
                var sb = new System.Text.StringBuilder(unit.Length * copies);
                for (int i = 0; i < copies; i++) sb.Append(unit);
                tickerText.text = sb.ToString();
                tickerX = 0f;
                tickerText.style.left = tickerX;
                tickerText.style.visibility = StyleKeyword.Null;
                return;
            }
            tickerX -= Time.unscaledDeltaTime * NewsTicker.ScrollPxPerSecond;
            if (tickerX <= -tickerUnitWidth) tickerX += tickerUnitWidth;
            tickerText.style.left = tickerX;
        }

        /// <summary>The Missions window (129) over the current view.</summary>
        void OpenMissions()
        {
            if (level == null || missions == null || missions.IsOpen || !Story.MapUnlocked) return;
            CloseHangar();
            lounge?.CloseChat(false);
            if (root.focusController?.focusedElement is VisualElement f) f.Blur();
            missions.Open();
            BuildHints(InputMode.Current);
            FirstVisitHint(0x13, 635);
        }

        /// <summary>The Status window (169).</summary>
        void OpenStatus()
        {
            if (level == null || status == null || status.IsOpen) return;
            CloseHangar();
            lounge?.CloseChat(false);
            if (missions != null && missions.IsOpen) missions.Close();
            if (root.focusController?.focusedElement is VisualElement f) f.Blur();
            status.Open();
            BuildHints(InputMode.Current);
            if (Application.isMobilePlatform) FirstVisitHint(0x18, 640, true);
        }

        public void OnStatusClosed()
        {
            Select(statusButton);
            BuildHints(InputMode.Current);
        }

        bool medalsChecked, wantedChecked;

        /// <summary>ModStation::OnInitialize / checkHints, the Most Wanted boards (wingmen_wanted.md 2.3, 2.8): criminals newly
        /// active on this board (3230 / 3231), the board hints 601 (the Terran board, from 128) and 613 (all boards, 162),
        /// and a board boss's ship on sale at Quineros (107, 3232).</summary>
        bool CheckWanted()
        {
            if (wantedChecked || level == null || level.Station == null || Session.FreePlay) return false;
            wantedChecked = true;
            var db = level.Database;
            int station = level.Station.index;
            int n = WantedBoard.ActivateNew(db, station);
            if (n == 1) ShowToast(Localization.Get(3230));
            else if (n > 1) ShowToast(Localization.Get(3231).Replace("#N", n.ToString()));
            if (station == 107)
                foreach (int ship in WantedBoard.QuinerosShips(db)) Story.AddDealerShip(107, ship);
            string note = null;
            if ((Session.WantedHints & 1) == 0 && Session.CampaignMission >= WantedBoard.AllBoards) Session.WantedHints |= 1;   // 613 says it all
            if ((Session.WantedHints & 1) == 0 && Session.CampaignMission >= WantedBoard.StorylineFirst && WantedBoard.Accessible(db, station))
            { Session.WantedHints |= 1; note = Localization.Get(601); }
            else if ((Session.WantedHints & 2) == 0 && Session.CampaignMission >= WantedBoard.AllBoards)
            { Session.WantedHints |= 2; note = Localization.Get(613); }
            else
            {
                int[] bosses = { 6, 12, 18, 24 };
                for (int k = 0; k < 4 && note == null; k++)
                {
                    int bit = 4 << k;
                    var st = WantedBoard.State(db, bosses[k]);
                    if ((Session.WantedHints & bit) != 0 || st == null || !st.terminated || bosses[k] >= db.Wanted.Count) continue;
                    Session.WantedHints |= bit;
                    note = Localization.Get(3232).Replace("#WANTED_NAME", db.Wanted[bosses[k]].name).Replace("#SHIP_NAME", Localization.Get(913 + 45 + k));
                }
            }
            if (note == null) return false;
            CloseHangar();
            storyDialogue.ShowMessage(note, 16, () => Select(launchButton));
            return true;
        }

        /// <summary>Achievements::checkForNewMedal on docking: "New medal!" (353) with each improved medal.</summary>
        bool CheckMedals()
        {
            if (medalsChecked || level == null) return false;
            medalsChecked = true;
            var improved = Achievements.Check(level.Database);
            medalQueue.Clear();
            foreach (int m in improved) if (m != 0) medalQueue.Enqueue(m);
            if (medalQueue.Count == 0) return false;
            if (medalToast == null) ShowNextMedal();
            return false;   // remake: the toasts don't hold up the station (the original's ChoiceWindows waited for OK)
        }

        /// <summary>ModStation::checkHints 0xee500: once each, one per frame: 0x1a all base medals (649), 0x1b all gold (650),
        /// all gold + the Supernova medals while blueprint 232 is locked (651, unlocked, autosave), 0x3a after campaign 0xa1
        /// with all gold + Supernova medals (or on Extreme) the Specter on sale at Katashun (3233, autosave).</summary>
        bool CheckMedalHints()
        {
            if (level == null || Session.FreePlay) return false;
            string text = null;
            if (!Session.Hints.Contains(0x1a) && Achievements.GotAllMedals) { Session.Hints.Add(0x1a); text = Localization.Get(649); }
            else if (!Session.Hints.Contains(0x1b) && Achievements.GotAllGoldMedals) { Session.Hints.Add(0x1b); text = Localization.Get(650); }
            else if (!Session.UnlockedBlueprints.Contains(232) && Achievements.GotAllGoldMedals && Achievements.GotAllSupernovaMedals)
            { Blueprints.Unlock(232); Session.Autosave(); text = Localization.Get(651); }
            else if (!Session.Hints.Contains(0x3a) && Session.CampaignMission > 0xa1 && (Session.IsExtreme || (Achievements.GotAllGoldMedals && Achievements.GotAllSupernovaMedals)))
            { Session.Hints.Add(0x3a); Session.Autosave(); text = Localization.Get(3233); }
            if (text == null) return false;
            ShowDialog(text, null, true);
            return true;
        }

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

        /// <summary>How long a medal's toast stays: the rarer, the longer (bronze 4 s, silver 5 s, gold 6 s, elite 7 s).</summary>
        static float MedalToastSeconds(int medal, int grade) =>
            medal >= Achievements.BaseCount ? 7f : grade == 1 ? 6f : grade == 2 ? 5f : 4f;

        /// <summary>ModStation::checkMedals 0xebe74: each new medal pays DAT_00251ff0[grade] (5000 gold / 2500 silver / 1000
        /// bronze; nothing on Extreme). The original shows one ChoiceWindow per medal (353, the plate, the hint 1552 + i)
        /// that waits for OK; remake: a toast at the top of the screen (the plate, 353, the name, the reward) that fades out
        /// after MedalToastSeconds (by grade; MedalToastQueuedSeconds while more are waiting, the last one its full time; the
        /// pointer resting on it pauses the countdown), one medal after another, and a tap / click on it opens the Status
        /// window on that medal.</summary>
        void ShowNextMedal()
        {
            if (medalToast != null) { medalToast.RemoveFromHierarchy(); medalToast = null; }
            if (medalQueue.Count == 0) return;
            int m = medalQueue.Dequeue();
            int grade = Achievements.Grade(m);
            int reward = Session.IsExtreme ? 0 : grade == 1 ? 5000 : grade == 2 ? 2500 : grade == 3 ? 1000 : 0;
            Session.Credits += reward;
            RefreshCredits();

            var toast = new VisualElement { name = "medalToast" };
            toast.AddToClassList("medal-toast");
            var plate = StatusWindow.MedalPlate(m, grade);
            plate.AddToClassList("medal-toast-plate");
            plate.pickingMode = PickingMode.Ignore;
            foreach (var c in plate.Children()) c.pickingMode = PickingMode.Ignore;
            toast.Add(plate);
            var texts = new VisualElement { pickingMode = PickingMode.Ignore };
            texts.AddToClassList("medal-toast-texts");
            Label Line(string text, string cls)
            {
                var l = new Label(text) { pickingMode = PickingMode.Ignore };
                l.AddToClassList(cls);
                texts.Add(l);
                return l;
            }
            // The grade's colour (gold / silver / bronze, the elite orange) on the border, the top band and the title; an
            // unearned grade (elite 0) in the theme's amber.
            var tint = grade > 0 ? StatusWindow.MedalTint(m >= Achievements.BaseCount, grade) : new Color(0.94f, 0.70f, 0.35f);
            tint.a = 1f;
            toast.style.borderTopColor = tint;
            toast.style.borderLeftColor = toast.style.borderRightColor = toast.style.borderBottomColor = new Color(tint.r, tint.g, tint.b, 0.55f);
            Line(Localization.Get(353).ToUpperInvariant(), "medal-toast-title").style.color = tint;
            Line(Localization.Get(1507 + m), "medal-toast-name");
            if (reward > 0) Line($"+{reward:N0} $", "medal-toast-reward");
            Line(Localization.Extra(InputMode.Current == InputKind.Touch ? "medalToastTap" : "medalToastClick",
                InputMode.Current == InputKind.Touch ? "Tap to see your medals" : "Click to see your medals"), "medal-toast-hint");
            toast.Add(texts);
            toast.RegisterCallback<PointerDownEvent>(e =>
            {
                e.StopPropagation();
                Play(buttonRelease);
                OpenMedal(m);
            });
            toast.RegisterCallback<PointerEnterEvent>(_ => medalToastHovered = true);
            toast.RegisterCallback<PointerLeaveEvent>(_ => medalToastHovered = false);
            (safeArea ?? root).Add(toast);
            medalToast = toast;
            medalToastLeft = medalQueue.Count > 0 ? MedalToastQueuedSeconds : MedalToastSeconds(m, grade);
            medalToastFading = medalToastHovered = false;
            // Remake's own medal sound (the original used the info window's blip): a rising synthesized chime with echoes.
            if (medalSound == null) medalSound = Resources.Load<AudioClip>("GoF2Sfx/MedalToast");
            Play(medalSound != null ? medalSound : infoSound);
            // Drops in and grows (the transition needs a frame), with a short bright flash.
            toast.schedule.Execute(() => toast.AddToClassList("medal-toast--shown"));
            toast.AddToClassList("medal-toast--flash");
            toast.schedule.Execute(() => toast.RemoveFromClassList("medal-toast--flash")).ExecuteLater(350);
        }

        /// <summary>Every frame: the shown toast's time, its fade-out, then the next medal.</summary>
        void UpdateMedalToast()
        {
            if (medalToast == null) return;
            if (medalToastFading || !medalToastHovered) medalToastLeft -= Time.unscaledDeltaTime;   // read while pointed at
            if (!medalToastFading && medalToastLeft <= 0f)
            {
                medalToastFading = true;
                medalToast.RemoveFromClassList("medal-toast--shown");
                medalToastLeft = MedalToastFadeSeconds;
            }
            else if (medalToastFading && medalToastLeft <= 0f) ShowNextMedal();
        }

        /// <summary>The toast tapped / clicked: the Status window with that medal selected (the rest of the queue goes on).</summary>
        void OpenMedal(int medal)
        {
            if (medalToast != null) { medalToast.RemoveFromHierarchy(); medalToast = null; }
            if (DialogOpen || SystemMenuOpen || status == null) return;
            if (!status.IsOpen) OpenStatus();
            status.SelectMedal(medal);
            if (medalQueue.Count > 0) ShowNextMedal();
        }

        public void OnMissionsClosed()
        {
            Select(missionsButton);
            BuildHints(InputMode.Current);
        }

        /// <summary>A lounge voice greeting (2D, SFX not paused); null stops the current one.</summary>
        public void PlayVoice(AudioClip clip)
        {
            if (voiceSource == null) return;
            voiceSource.Stop();
            if (clip == null) return;
            voiceSource.clip = clip;
            voiceSource.volume = Settings.VoiceVolume;
            voiceSource.Play();
        }

        /// <summary>Focus for keyboard / controller (not in touch mode).</summary>
        public void Focus(VisualElement e) => Select(e);

        /// <summary>Credits changed outside the hangar window (a deal in the lounge).</summary>
        public void RefreshCredits() => BuildHints(InputMode.Current);

        void OpenLounge()
        {
            if (level == null || !Story.LoungeUnlocked(level.Station != null ? level.Station.index : -1)) return;
            CloseHangar();
            level?.SetView(StationView.Lounge);
            FirstVisitHint(0xd, 627);
        }

        /// <summary>ModStation::OnKeyPress case 2: the star map (station mode; jump mode with a Khador Drive). Refused while
        /// the hold is overloaded (204). A picked station departs at once (StarMap::depart): the launch sequence, then the
        /// autopilot to it or, for another system with a drive, the Khador charge.</summary>
        void OpenMap()
        {
            if (level == null || StarMap.IsOpen || !Story.MapUnlocked) return;
            if (new Hangar(level.Database, level.Stock).Overloaded) { ShowDialog(Localization.Get(204), null, true); return; }
            if (Story.MapRefusal is string refusal) { ShowDialog(refusal, null, true); return; }   // index 77: take the Cronus
            CloseHangar();
            if (root.focusController?.focusedElement is VisualElement f) f.Blur();
            root.AddToClassList("station-map-open");
            var map = StarMap.Open(level.Database, StarMapMode.Station, GalaxyMap.HasJumpDrive(level.Database), OnMapClosed);
            if (map == null) { root.RemoveFromClassList("station-map-open"); return; }
            int cm = Session.FreePlay ? 20 : Session.CampaignMission;
            if (!Settings.TutorialHints) { }   // the tutorial popups option
            else if (cm > 15 && Session.Hints.Add(0xe)) map.ShowHint(HintText(628, true));
            else if (cm < 16 && Session.Hints.Add(0xf)) map.ShowHint(HintText(631, true));
        }

        void OnMapClosed(StarMapResult result)
        {
            root.RemoveFromClassList("station-map-open");
            if (result.station < 0) { ApplyInputMode(); return; }
            Session.ProgrammedStation = result.station == Session.StationIndex ? -1 : result.station;
            Session.InstantJump = result.instantJump;
            Session.EnergyCellsForNextJump = result.instantJump ? result.cells : 0;
            if (RefuseLaunchForStory()) return;
            level.Launch();
        }

        /// <summary>ModStation::leaveStation at campaign index 21: "Equip the EMP bombs before leaving." (531) without an EMP
        /// bomb (items 41-43) mounted.</summary>
        /// <summary>ModStation::leaveStation 0xec1ec: at 6 / 7 a ship with no fire power or no shield / armor (combined HP =
        /// base HP) stays: 529 (nothing to mount in the hold) or 530 (mount what is in the hold); at 20 / 21 in the target
        /// station 531 (21: without an EMP bomb 41-43); at 77 anything but the Cronus 326.</summary>
        bool RefuseLaunchForStory()
        {
            if (Session.FreePlay) return false;
            var db = level.Database;
            int n = Story.Index, station = level.Station != null ? level.Station.index : -1;
            int text = -1;
            if ((n == 6 || n == 7) && (Shop.FirePower(db) == 0f || Shop.CombinedHp(db) == Shop.BaseHp(db)))
                text = Session.Cargo.Exists(c => { int t = db.Item(c.item)?.TypeId ?? 4; return t == 0 || t == 3; }) ? 530 : 529;
            else if (n == 20 && station == Story.Mission.station) text = 531;
            else if (n == 21 && station == Story.Mission.station && !Session.Equipment.Exists(e => e.item >= 41 && e.item <= 43)) text = 531;
            else if (n == 77 && Session.ShipIndex != 37) text = 326;
            if (text < 0) return false;
            ShowDialog(Localization.Get(text), null, true);
            return true;
        }

        /// <summary>ModStation::leaveStation: refused while the cargo hold is overloaded (204). Remake: launches at once, without
        /// the original's "Depart the station?" (397).</summary>
        void AskLaunch()
        {
            if (new Hangar(level.Database, level.Stock).Overloaded) { ShowDialog(Localization.Get(204), null, true); return; }
            if (RefuseLaunchForStory()) return;
            level.Launch();
        }

        /// <summary>ChoiceWindow: yes / no, or a message with one button ('info').</summary>
        public void ShowDialog(string text, System.Action onYes, bool info = false)
        {
            dialogAction = onYes;
            dialogNoAction = null;
            root.Q<Label>("dialogText").text = text;
            dialogYes.text = info ? Localization.Extra("ok", "OK") : Localization.Get(134).ToUpperInvariant();
            dialogNo.text = Localization.Get(135).ToUpperInvariant();
            dialogNo.style.display = info ? DisplayStyle.None : DisplayStyle.Flex;
            dialog.AddToClassList("station-dialog-backdrop--shown");
            Play(infoSound);
            if (root.focusController?.focusedElement is VisualElement f) f.Blur();
            Select(dialogYes);
        }

        /// <summary>ChoiceWindow with its own two labels (327: 330 "Sell" / 331 "Keep").</summary>
        public void ShowChoice(string text, string yes, string no, System.Action onYes, System.Action onNo)
        {
            ShowDialog(text, onYes);
            dialogNoAction = onNo;
            dialogYes.text = yes.ToUpperInvariant();
            dialogNo.text = no.ToUpperInvariant();
        }

        void CloseDialog()
        {
            dialogPicture?.RemoveFromHierarchy();
            dialogPicture = null;
            dialog.RemoveFromClassList("station-dialog-backdrop--shown");
            dialogAction = null;
            dialogNoAction = null;
            if (SystemMenuOpen)
            {
                // Back to the slot just picked (or the first item of the page).
                var items = SystemMenuItems();
                int i = SavePageOpen && lastSavedSlot >= 0 && lastSavedSlot < items.Length ? lastSavedSlot : 0;
                Select(items[i]);
            }
            else if (missions != null && missions.IsOpen) Select(missions.NavItems()[0]);
            else if (lounge != null && lounge.ChatOpen) lounge.FocusFirst();
            else if (!HangarOpen) Select(launchButton);
            else if (root.focusController?.focusedElement is VisualElement f) f.Blur();
        }

        bool DialogOpen => dialog != null && dialog.ClassListContains("station-dialog-backdrop--shown");
        public bool IsDialogOpen => DialogOpen;

        /// <summary>A short message at the top ("#N mounted.", "You need an additional #C." ...), 3 s.</summary>
        public void ShowToast(string text)
        {
            toast.text = text;
            toast.AddToClassList("station-toast--shown");
            toastMs = 3000f;
        }

        int boozeAtOpen;

        static int BoozeInHold()
        {
            int n = 0;
            foreach (var s in Session.Cargo) if (Session.IsBooze(s.item)) n += s.amount;
            return n;
        }

        AudioSource alarm;

        /// <summary>DialogueWindow::loadContent, text 1833 ("Alert! Void fighters..."): the music stops, 136 Space_Combat_Void
        /// plays as music and 162 Alert loops (event volume 0.198) until the station is left.</summary>
        /// <summary>Multiplayer: the station's shared stock changed.</summary>
        void OnSharedStock(int station)
        {
            if (level != null && level.Stock != null && level.Stock.station == station) hangarWindow?.StockChanged();
        }

        void OnDestroy() => GoF2Remake.Multiplayer.NetStock.Changed -= OnSharedStock;

        void StartVoidAlarm()
        {
            var story = StoryAssets.Load();
            if (story == null || level == null) return;
            if (level.musicSource != null)
            {
                level.musicSource.Stop();
                level.musicSource.clip = story.voidBattle;
                level.musicSource.loop = true;
                if (story.voidBattle != null) level.musicSource.Play();
            }
            if (alarm == null) { alarm = gameObject.AddComponent<AudioSource>(); alarm.playOnAwake = false; alarm.loop = true; alarm.spatialBlend = 0f; }
            alarm.clip = story.alert;
            alarm.volume = 0.198f * GoF2Remake.Flight.Sfx.EventGain * Settings.SfxVolume;
            if (alarm.clip != null) { alarm.Stop(); alarm.Play(); }
        }

        public void PlayPush() => Play(buttonPush);
        public void PlayRelease() => Play(buttonRelease);
        public void PlayClip(AudioClip clip) => Play(clip);

        ItemInfoWindow infoWindow;
        /// <summary>The full-screen item / ship details (ListItemWindow), shared by the hangar and the lounge.</summary>
        public ItemInfoWindow InfoWindow => infoWindow;

        void Back()
        {
            if (infoWindow != null && infoWindow.IsOpen) { Play(buttonRelease); infoWindow.Close(); }
            else if (DialogOpen)
            {
                // The original's ChoiceWindow can't be dismissed (ModStation::OnKeyPress ignores every key while it is open):
                // back = its No button, or the OK of a message, so the choice's action always runs (the docking fine's No
                // launches; closing it without one kept the player docked without paying).
                Play(buttonRelease);
                var a = dialogNo.resolvedStyle.display != DisplayStyle.None ? dialogNoAction : dialogAction;
                CloseDialog();
                a?.Invoke();
            }
            else if (missions != null && missions.IsOpen) { Play(buttonRelease); missions.Close(); }
            else if (status != null && status.IsOpen) { Play(buttonRelease); status.Close(); }
            else if (lounge != null && lounge.ChatOpen) { Play(buttonRelease); lounge.CloseChat(); }
            else if (HangarOpen) { Play(buttonRelease); if (!hangarWindow.Back()) CloseHangar(); }
            else if (SystemMenuOpen && sysPage != SysPage.Main) { Play(buttonRelease); ShowSystemPage(SysPage.Main); }
            else if (SystemMenuOpen) { Play(buttonRelease); CloseSystemMenu(); }
            else if (level != null && level.View == StationView.Lounge) { Play(buttonRelease); level.SetView(StationView.Hangar); }
            else { Play(buttonRelease); OpenSystemMenu(); }
        }

        void BackToMenu()
        {
            GoF2Remake.Multiplayer.NetGame.Shutdown();   // leaving a multiplayer session
            if (Application.CanStreamedLevelBeLoaded(menuScene)) SceneManager.LoadScene(menuScene);
        }

        // ---- story (ModStation::OnUpdate / OnTouchEnd) ---------------------------------------------------

        /// <summary>Status::missionCompleted(docked): the completed campaign mission's success conversation.</summary>
        bool CheckStory()
        {
            if (level == null || level.Station == null || Session.FreePlay) return false;
            var ctx = new StoryContext
            {
                docked = true,
                inLounge = level.View == StationView.Lounge && !level.IntroPlaying,
                station = level.Station.index,
            };
            if (ctx.inLounge && SpecialLounge(ctx.station)) return true;
            if (!Story.IsComplete(level.Database, ctx)) return false;
            Story.Mission.won = true;
            CloseHangar();
            if (root.focusController?.focusedElement is VisualElement f) f.Blur();
            var step = Story.Step;
            var success = step != null ? StoryTable.Shown(step.success) : null;
            if (success != null && success.Count > 0)
                storyDialogue.Show(success, skipped =>
                {
                    // DialogueWindow::OnTouchEnd: skipping step 15's conversation starts the alarm too.
                    if (skipped && !Session.FreePlay && Session.CampaignMission == 0xf) StartVoidAlarm();
                    AfterStorySuccess();
                });
            else AfterStorySuccess();
            return true;
        }

        /// <summary>ModStation::OnUpdate's lounge searches (campaign_flow.md 3.1 5): at 116 the Pescal Inartu bars other than
        /// Maissa (90, 91, 92, 94) each play their flavour line once (2716 + slot, bit slot of the mission value); at 148 the
        /// brokers' bars Kappa (55), Inari Onu (66) and Coppolite (9) each play conversation 148 / 149 / 150 once (bits 1 / 2 /
        /// 4), and Kalun Amir (96) sets the index to 151 and plays its conversation (closing it advances to 152).</summary>
        bool SpecialLounge(int station)
        {
            var m = Story.Mission;
            if (Story.Index == 116 && (station == 90 || station == 91 || station == 92 || station == 94))
            {
                int slot = station == 94 ? 3 : station - 90;
                if ((m.value & (1 << slot)) != 0) return false;
                m.value |= 1 << slot;
                storyDialogue.Show(new System.Collections.Generic.List<DialoguePage> { new DialoguePage { speaker = 0, text = 2716 + slot, voice = $"MISSION_ALT_116_{slot}" } },
                                   _ => Select(launchButton));
                return true;
            }
            if (Story.Index != 148) return false;
            if (station == 96)
            {
                Session.CampaignMission = 151;
                Session.StoryMission = StoryMission.From(StoryTable.Step(151));
                return false;   // the normal path: 151 (lounge at Kalun Amir) is complete right here
            }
            int k = station == 55 ? 0 : station == 66 ? 1 : station == 9 ? 2 : -1;
            if (k < 0 || (m.value & (1 << k)) != 0) return false;
            m.value |= 1 << k;
            var pages = StoryTable.Step(148 + k)?.success;
            if (pages == null || pages.Count == 0) return false;
            storyDialogue.Show(pages, _ => Select(launchButton));
            return true;
        }

        /// <summary>ModStation::OnTouchEnd after a campaign success conversation.</summary>
        void AfterStorySuccess()
        {
            var db = level.Database;
            int reward = Story.Mission.reward;
            if (Story.Index == 43)
            {
                // ModStation::OnTouchEnd: index 43 starts the ending (the credits over the space backdrop, EndingCredits in the
                // main menu scene); nextCampaignMission (-> 44) and the station module follow when it is over.
                Session.Credits += reward;
                Session.EndingPending = true;
                BackToMenu();
                return;
            }
            int n = Story.Advance(db);
            Story.AfterDockedAdvance(n, level.Station.index);                // Khador's ships and rum at Kothar (77, 84)
            if (n == WantedBoard.StorylineFirst) wantedChecked = false;      // Status::activateNewWanted + hint 601 at step 128
            if (Story.ShipSwapped(n)) level.ReplacePlayerShip(Session.ShipIndex);   // the loaner / the own ship on the turntable
            if (n == 9 || n == 44 || n == 75 || n == 76 || n == 83)
            {
                // ModStation restarts the station module here so that OnInitialize runs again and the next conversation
                // (the new step is "docked here") follows. The remake stays in the scene instead: the per-docking story
                // tweaks, the autosave and the menu locks again; the next frame's CheckStory opens the conversation.
                int station = level.Station.index;
                Story.OnDocked(db, station, level.Stock);
                if (Story.AutosaveAllowed(station)) Session.Autosave();
                ApplyStoryLocks();
                return;
            }
            int launchTo = n switch { 89 => 109, 99 => 10, 109 => 114, 119 => 10, 133 => 120, 144 => 112, 160 => 10, _ => -1 };
            if (n == 78) { level.Launch(); return; }   // escape from Valkyrie: departStation + space
            if (launchTo >= 0)
            {
                // initStreamOutPosition + departStation(target): straight into the story orbit.
                Session.PreviousStationIndex = Session.StationIndex;
                Session.StationIndex = launchTo;
                Session.ArrivedByTravel = n != 144 && n != 160;
                Session.LaunchedFromStation = false;
                SceneManager.LoadScene(level.spaceScene);
                return;
            }
            Session.Credits += reward;
            Session.Autosave();
            ApplyStoryLocks();
            Select(launchButton);
        }

        // ---- freelance (ModStation::OnUpdate: Status::missionCompleted / missionFailed, docked) --------------

        bool CheckFreelance()
        {
            if (level == null || level.Station == null) return false;
            var result = Freelance.CheckDocked(level.Station.index);
            if (result == Freelance.DockResult.None) return false;
            CloseHangar();
            lounge?.CloseChat(false);
            if (missions != null && missions.IsOpen) missions.Close();
            if (root.focusController?.focusedElement is VisualElement f) f.Blur();
            var m = Freelance.Mission;
            bool success = result == Freelance.DockResult.Success;
            int textId;
            string text = success ? Freelance.SuccessText(out textId) : Freelance.FailureText(out textId);
            string voiceLine = Freelance.Voice(textId);
            storyDialogue.ShowAgentMessage(text, m.clientName, m.clientPortrait, () =>
            {
                // Multiplayer: a squadmate may have ended it meanwhile (their result already paid this player's share).
                if (Freelance.Mission != m) { Select(launchButton); return; }
                if (success)
                {
                    // Layout::showMissionRewardMessage + Mission_accomplished (36), changeCredits(reward + bonus).
                    int paid = Freelance.Succeed(true);
                    ShowToast($"{Localization.Get(216)} +{ItemInfo.Credits(paid)}");
                    var combat = GoF2Remake.Flight.CombatAssets.Load();
                    Play(combat != null ? combat.missionAccomplished : null);
                }
                else Freelance.Fail();
                Select(launchButton);
            }, voiceLine);
            return true;
        }

        bool baseChecked;

        /// <summary>ModStation::OnInitialize / checkHints (npc_combat_specials.md 3.6, 3.7): a pirate base's station is unmanned
        /// (434 from Security, closing it relaunches at once); after an outpost kill the next docking pays 20000 (442).</summary>
        bool CheckPirateBase()
        {
            if (baseChecked || level == null || level.Station == null) return false;
            baseChecked = true;
            int st = level.Station.index;
            if (PirateBases.StationHasBase(st))
            {
                CloseHangar();
                storyDialogue.ShowAgentMessage(Localization.Get(434), Localization.Get(PirateBases.SecurityName), PirateBases.Portrait, level.Launch);
                return true;
            }
            if (!Session.PirateBaseRewardPending) return false;
            Session.PirateBaseRewardPending = false;
            storyDialogue.ShowAgentMessage(Localization.Get(442), Localization.Get(PirateBases.NivelianName), PirateBases.Portrait, () =>
            {
                Session.Credits += PirateBases.Reward;
                RefreshCredits();
                ShowToast($"+{ItemInfo.Credits(PirateBases.Reward)}");
                Session.Autosave();
            });
            return true;
        }

        bool fineChecked;
        const float ArrivalSettleMs = 1000f;
        float settleMs;

        /// <summary>ModStation::OnInitialize 0xe8080 (combat_equipment.md 7): an enemy race's station (205: |standing| / 100 *
        /// 2800 +- 100) or one whose forces the player attacked (206: rank * 150 + 1000) demands a bribe, x10 hardcore. Yes =
        /// pay, the attack forgotten (the standing stays); not enough credits = 203; No = straight back into space.</summary>
        bool CheckDockingFine()
        {
            if (fineChecked || level == null || level.Station == null) return false;
            fineChecked = true;
            int st = level.Station.index;
            var sys = level.Database.Systems.Find(s => s.index == level.Station.system);
            int race = sys != null ? sys.raceId : -1;
            if (st == 100 || st == 101 || st == 108 || level.Station.system == 25 || race < 0 || race > 3 || PirateBases.StationHasBase(st)) return false;
            if (!Session.FreePlay && Session.CampaignMission == 0x30) return false;
            bool enemy = GoF2Remake.Flight.Standing.IsEnemy(race);
            bool attacked = Session.AttackedStations.Contains(st);
            if (!enemy && !attacked) return false;
            int fine = enemy ? (int)(Mathf.Abs(GoF2Remake.Flight.Standing.Toward(race)) / 100f * 2800f) + Random.Range(0, 200) - 100
                             : Session.Rank * 150 + 1000;
            if (Session.IsExtreme) fine *= 10;
            CloseHangar();
            ShowChoice(Localization.Get(enemy ? 205 : 206).Replace("#C", ItemInfo.Credits(fine)), Localization.Get(134), Localization.Get(135), () =>
            {
                if (Session.Credits < fine)
                {
                    ShowDialog(Localization.Get(203).Replace("#C", ItemInfo.Credits(fine - Session.Credits)), level.Launch, true);
                    return;
                }
                Session.Credits -= fine;
                Session.AttackedStations.Remove(st);
                RefreshCredits();
                Session.Autosave();
            }, level.Launch);
            return true;
        }

        bool kaamoChecked;

        /// <summary>ModStation::OnInitialize 0xe8080 at the Kaamo Club (kaamo_club.md 4): state 1 -> the 18-page first
        /// visit and state 2; state 2 -> 476 (not enough credits / buskat) or 477 -> Yes: pay, 479-484 (remake), 485, owned.</summary>
        bool CheckKaamo()
        {
            if (kaamoChecked || level == null || level.Station == null) return false;
            kaamoChecked = true;
            if (level.Station.index != KaamoClub.Station || Session.KaamoState < 1 || Session.KaamoState > 2) return false;
            CloseHangar();
            if (root.focusController?.focusedElement is VisualElement f) f.Blur();
            if (Session.KaamoState == 1)
            {
                Session.KaamoState = 2;
                storyDialogue.Show(KaamoClub.FirstVisitPages(), _ => { Session.Autosave(); Select(launchButton); });
                return true;
            }
            if (!KaamoClub.CanBuy) { ShowDialog(Localization.Get(476), null, true); return true; }
            ShowDialog(Localization.Get(477), () =>
            {
                KaamoClub.Buy();
                level.Stock.items = Session.KaamoItems;   // the (cleared) storage is the station's stock now
                RefreshCredits();
                Session.Autosave();
                storyDialogue.Show(KaamoClub.PurchasePages(), _ => ShowDialog(Localization.Get(485), null, true));
            });
            return true;
        }

        bool wingmenChecked;

        /// <summary>ModStation::checkHints: an expired wingman contract ends here, 313 from the first wingman.</summary>
        bool CheckWingmenContract()
        {
            if (wingmenChecked) return false;
            wingmenChecked = true;
            Session.WingmanShowEmp = true;   // Status+0xf8 reset on the station visit
            if (!Wingmen.Expired) return false;
            // The first wingman's voice (race + portrait; body 10 is the only female face, AgentGenerator.CreatePortrait).
            var face = Session.WingmanPortrait;
            string voiceLine = GenericVoice.For(313, Session.WingmanRace, face == null || face.Length == 0 || face[0] != 10, face);
            storyDialogue.ShowAgentMessage(Localization.Get(313), Session.Wingmen[0], face, Wingmen.Dismiss, voiceLine);
            return true;
        }

        bool pendingChecked, rescueChecked;

        /// <summary>ModStation::OnInitialize: stranded in a system without gate routes (campaign &gt; 16, not at 101, no jump
        /// drive, no Khador Drive in the hold) -> 335, the interstellar shuttle to Dis (Magnetar, station 70) for 25 000;
        /// without the credits 203 (ModStation::OnTouchEnd 0xea4ec).</summary>
        bool CheckRescue()
        {
            if (rescueChecked || level == null || level.Station == null) return false;
            rescueChecked = true;
            var db = level.Database;
            if (Session.FreePlay || level.Station.index == 101 || Story.Index <= 16) return false;
            var sys = db.Systems.Find(s => s.index == level.Station.system);
            if (sys == null || (sys.jumpRoutesTo != null && sys.jumpRoutesTo.Count > 0)) return false;
            if (GalaxyMap.HasJumpDrive(db) || Session.Cargo.Exists(c => c.item == GalaxyMap.KhadorDriveItem)) return false;
            ShowDialog(Localization.Get(335), () =>
            {
                if (Session.Credits < 25000) { ShowDialog(Localization.Get(203).Replace("#C", ItemInfo.Credits(25000 - Session.Credits)), null, true); return; }
                Session.Credits -= 25000;
                Session.PreviousStationIndex = Session.StationIndex;
                Session.StationIndex = 70;
                SceneManager.LoadScene(gameObject.scene.name);   // SetCurrentApplicationModule(5): the station module at Dis
            });
            return true;
        }

        /// <summary>ModStation::checkPendingProducts 0xee258 (once per docking): blueprint products waiting here move to the
        /// hold, 213 "The following items have been moved to your cargo hold:" + one line each.</summary>
        bool CheckPendingProducts()
        {
            if (pendingChecked || level == null || level.Station == null) return false;
            pendingChecked = true;
            var moved = Blueprints.CollectPending(level.Database, level.Station.index);
            if (moved.Count == 0) return false;
            string text = Localization.Get(213);
            foreach (var p in moved) text += $"\n{p.quantity}x {ItemInfo.ItemName(p.item)}";
            ShowDialog(text, null, true);
            return true;
        }

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
            // Remake: the Debug page once the main menu's Debug panel has been opened (Cheats); in multiplayer only when
            // the session allows it.
            if (Cheats.PageShown) debugButton = SystemButton(Localization.Extra("debugTitle", "Debug"), 5, () => ShowSystemPage(SysPage.Debug), systemMain);
            // Multiplayer: a session's game is never saved, and no single-player save is loaded into it.
            if (GoF2Remake.Multiplayer.NetGame.Active)
                foreach (var b in new[] { loadGameButton, saveGameButton }) if (b != null) b.style.display = DisplayStyle.None;
            // The Debug page's scroll list (Options is an OptionsView, BuildOptionsView).
            systemOptions = new VisualElement();
            systemOptions.AddToClassList("system-menu-page");
            optionsScroll = new ScrollView(ScrollViewMode.Vertical) { horizontalScrollerVisibility = ScrollerVisibility.Hidden, verticalScrollerVisibility = ScrollerVisibility.Hidden };
            optionsScroll.AddToClassList("slot-list");
            optionsScroll.AddToClassList("system-slot-list");
            new DragScroll(optionsScroll);
            systemOptions.Add(optionsScroll);
            optionsBack = SystemButton(Localization.Extra("hudBack", "BACK"), -1, () => ShowSystemPage(SysPage.Main), systemOptions);
            optionsBack.AddToClassList("system-menu-back");
            systemMain.parent.Add(systemOptions);
        }

        /// <summary>The Options page like the main menu's (OptionsView: its tabs, rows, Back and Default settings), over
        /// the menu's own panel; the rows take the panel's focus like the menu's buttons.</summary>
        void BuildOptionsView()
        {
            optionsView = new OptionsView(() => { Play(buttonRelease); ShowSystemPage(SysPage.Main); }, true);
            foreach (var b in new[] { optionsView.BackButton, optionsView.DefaultsButton })
                b.RegisterCallback<PointerDownEvent>(_ => Play(buttonPush), TrickleDown.TrickleDown);
            optionsView.DefaultsRestored += () => Play(buttonRelease);
            optionsView.Changed += c => { if (c.def.kind == OptionKind.Choice || c.def.kind == OptionKind.Toggle) Play(buttonRelease); };
            optionsView.TabChanged += () => Select(optionsView.ActiveTab);
            systemMenu.Add(optionsView.Root);
        }

        void BuildStationOptions()
        {
            optionsScroll.Clear();
            stationOptions.Clear();
            BuildStationDebug();
        }

        /// <summary>The Debug page: the cheat toggles and actions (CheatsCatalog); an action reports in a toast.</summary>
        int stationDebugTab;

        /// <summary>The Debug page like the pause menu's (remake-only, CheatsCatalog): tabs Cheats / Actions / Give items (click,
        /// Q / E or LB / RB), the toggles and actions in two columns, the item pickers with "Add to hold" / "Add and mount";
        /// results as toasts.</summary>
        void BuildStationDebug()
        {
            string X(string key, string english) => Localization.Extra(key, english);
            var names = new[] { X("debugCheats", "Cheats"), X("debugActions", "Actions"), X("debugItems", "Give items"), X("debugShips", "Ships") };
            stationDebugTab = Mathf.Clamp(stationDebugTab, 0, names.Length - 1);
            var tabs = new VisualElement();
            tabs.AddToClassList("debug-tabs");
            for (int i = 0; i < names.Length; i++)
            {
                int tab = i;
                var b = new Button { text = names[i].ToUpperInvariant(), focusable = false };
                b.AddToClassList("debug-tab");
                b.AddToClassList("gof-semibold");
                b.EnableInClassList("debug-tab--active", i == stationDebugTab);
                b.clicked += () => SwitchDebugTab(tab - stationDebugTab);
                tabs.Add(b);
            }
            var hint = new Label(InputMode.Current == InputKind.Gamepad ? "LB  ◂  ▸  RB" : InputMode.Current == InputKind.Touch ? "" : "Q  ◂  ▸  E")
                { pickingMode = PickingMode.Ignore };
            hint.AddToClassList("debug-tab-hint");
            tabs.Add(hint);
            optionsScroll.Add(tabs);

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
                    foreach (var def in CheatsCatalog.Toggles()) Row(def, grid, "debug-grid-cell");
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

        const int StationDebugTabs = 4;   // Cheats, Actions, Give items, Ships

        /// <summary>The Debug page's tab 'step' tabs on (wrapping), rebuilt with the first row selected.</summary>
        void SwitchDebugTab(int step)
        {
            if (step == 0) return;
            stationDebugTab = ((stationDebugTab + step) % StationDebugTabs + StationDebugTabs) % StationDebugTabs;
            ShowSystemPage(SysPage.Debug);
        }

        void OpenSystemMenu()
        {
            if (SystemMenuOpen || level == null) return;
            CloseHangar();
            systemMenu.AddToClassList("station-dialog-backdrop--shown");
            ShowSystemPage(SysPage.Main);
        }

        void CloseSystemMenu()
        {
            if (!SystemMenuOpen) return;
            systemMenu.RemoveFromClassList("station-dialog-backdrop--shown");
            if (root.focusController?.focusedElement is VisualElement f) f.Blur();
            Select(launchButton);
            BuildHints(InputMode.Current);
        }

        void ShowSystemPage(SysPage page)
        {
            sysPage = page;
            bool slots = page == SysPage.Save || page == SysPage.Load;
            systemMain.EnableInClassList("system-menu-page--shown", page == SysPage.Main);
            systemSave.EnableInClassList("system-menu-page--shown", slots);
            systemOptions?.EnableInClassList("system-menu-page--shown", page == SysPage.Debug);
            var menuPanel = root.Q(className: "system-menu");
            menuPanel?.EnableInClassList("system-menu--wide", page == SysPage.Debug);
            if (optionsView != null) { optionsView.Root.RemoveFromHierarchy(); optionsView = null; }
            if (menuPanel != null) menuPanel.style.display = page == SysPage.Options ? DisplayStyle.None : StyleKeyword.Null;
            if (page == SysPage.Options) BuildOptionsView();
            int title = page == SysPage.Save ? 30 : page == SysPage.Load ? 29 : page == SysPage.Options ? 31 : 172;   // Menu
            root.Q<Label>("systemMenuTitle").text = page == SysPage.Debug ? Localization.Extra("debugTitle", "Debug").ToUpperInvariant()
                                                                          : Localization.Get(title).ToUpperInvariant();
            if (slots) BuildSaveSlots();
            if (page == SysPage.Debug) BuildStationOptions();
            if (root.focusController?.focusedElement is VisualElement f) f.Blur();
            Select(page == SysPage.Save ? saveSlotList.contentContainer.ElementAt(1)     // slot 1: the first manual slot
                 : page == SysPage.Load ? saveSlotList.contentContainer.ElementAt(0)
                 : page == SysPage.Options ? optionsView.ActiveTab
                 : page == SysPage.Debug ? (stationOptions.Count > 0 ? stationOptions[0].Field : optionsBack)
                 : newGameButton);
            BuildHints(InputMode.Current);
        }

        void BuildSaveSlots()
        {
            saveSlotList.Clear();
            for (int i = 0; i < SaveGame.SlotCount; i++)
            {
                int slot = i;
                var save = SaveGame.Preview(i);
                var row = SaveSlotRow.Build(level.Database, i, save, Localization.Extra("autosaveHint", "Saved automatically when you dock"));
                row.RegisterCallback<PointerDownEvent>(_ => Play(buttonPush), TrickleDown.TrickleDown);
                row.clicked += () => { Play(buttonRelease); if (sysPage == SysPage.Load) PickLoadSlot(slot, save != null); else PickSaveSlot(slot, save != null); };
                row.RegisterCallback<FocusInEvent>(_ => { if (!DragScroll.PointerActive) saveSlotList.ScrollTo(row); });
                saveSlotList.Add(row);
            }
        }

        /// <summary>MenuTouchWindow load mode: a used slot, after 523 (the progress since the last save is lost), is loaded
        /// (a save is always docked: the station module restarts).</summary>
        void PickLoadSlot(int slot, bool used)
        {
            if (!used) return;
            ShowDialog(Localization.Get(523), () => { if (SaveGame.Load(slot)) SceneManager.LoadScene(gameObject.scene.name); });
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

        // ---- turning the ship (ModStation::OnTouchMove / OnTouchEnd) ----------------------------------------

        /// <summary>Panel units per game radian: the original's 120 px on a 480 px high screen, a quarter of the height.</summary>
        float UnitsPerRadian => Mathf.Max(1f, root.layout.height) * (StationTables.TurntablePixelsPerRadian / 480f);

        void HookDrag()
        {
            dragZone.RegisterCallback<PointerDownEvent>(e =>
            {
                if (level == null || dragPointer >= 0) return;
                if (level.View == StationView.Lounge) { level.SkipIntro(); return; }   // OnTouchEnd case 0: jump to B
                dragPointer = e.pointerId;
                dragZone.CapturePointer(e.pointerId);
                dragLastX = e.position.x;
                dragLastTime = Time.unscaledTime;
                dragVelocity = dragFrameRadians = 0f;
                level.RotateShip(0f);   // stops a fling
            });
            dragZone.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (e.pointerId != dragPointer) return;
                float rad = (e.position.x - dragLastX) / UnitsPerRadian;
                level.RotateShip(rad);
                dragFrameRadians += rad;
                dragLastX = e.position.x;
                dragLastTime = Time.unscaledTime;
            });
            dragZone.RegisterCallback<PointerUpEvent>(e => EndDrag(e.pointerId, true));
            dragZone.RegisterCallback<PointerCancelEvent>(e => EndDrag(e.pointerId, false));
        }

        void EndDrag(int pointerId, bool fling)
        {
            if (pointerId != dragPointer) return;
            if (dragZone.HasPointerCapture(dragPointer)) dragZone.ReleasePointer(dragPointer);
            dragPointer = -1;
            // OnTouchEnd: fling only if the finger was still moving (the original: last dx > 3 px).
            bool moving = Time.unscaledTime - dragLastTime < 0.08f;
            float minSpeed = 3f / StationTables.TurntablePixelsPerRadian / 0.02f;
            if (fling && moving && Mathf.Abs(dragVelocity) > minSpeed) level.FlingShip(Mathf.Clamp(dragVelocity, -MaxFling, MaxFling));
        }

        // ---- input mode, focus and navigation -------------------------------------------------------------

        void ApplyInputMode()
        {
            if (root == null) return;
            var kind = InputMode.Current;
            root.EnableInClassList("input-touch", kind == InputKind.Touch);
            root.EnableInClassList("input-keyboard", kind == InputKind.KeyboardMouse);
            root.EnableInClassList("input-gamepad", kind == InputKind.Gamepad);
            SetTouchMode(kind == InputKind.Touch);
            if (kind == InputKind.Gamepad && !HangarOpen && root.focusController?.focusedElement == null)
                Select(DialogOpen ? dialogYes : SystemMenuOpen ? SystemMenuItems()[0] : level != null && level.View == StationView.Lounge ? loungeButton : hangarButton);
            BuildHints(kind);
        }

        // Touch has no hover and shouldn't leave a focus highlight on what the finger pressed (see MainMenu).
        void SetTouchMode(bool on)
        {
            touchMode = on;
            root.EnableInClassList("can-hover", !on);
            if (on && root.focusController?.focusedElement is VisualElement focused) focused.Blur();
        }

        void OnPointerDown(PointerDownEvent e)
        {
            if (e.pointerType == PointerType.mouse) { SetTouchMode(false); return; }
            SetTouchMode(true);
            root.focusController?.IgnoreEvent(e);
        }

        void Select(VisualElement e)
        {
            if (!touchMode) e?.Focus();
        }

        /// <summary>A button the keys / controller can move to: enabled (the story locks grey some out) and shown.</summary>
        static bool Navigable(VisualElement v) => v != null && v.enabledInHierarchy && v.canGrabFocus && v.resolvedStyle.display != DisplayStyle.None;

        /// <summary>The next navigable item from index i in the step's direction (disabled ones are jumped over); from nothing
        /// focused the first navigable one. Null = none that way (the selection stays).</summary>
        static VisualElement NextNavigable(VisualElement[] items, int i, int step)
        {
            if (i < 0) step = 1;
            for (int k = i < 0 ? 0 : i + step; k >= 0 && k < items.Length; k += step)
                if (Navigable(items[k])) return items[k];
            return null;
        }

        /// <summary>The main view's last selected button before Launch: left goes back to it.</summary>
        VisualElement lastSideItem;

        /// <summary>Up/down walks the screen buttons and launch, jumping over the locked ones (left/right: yes/no in the
        /// dialog). On the main view (and with the lounge's visitors) a controller's right goes to Launch and left from
        /// Launch back to the button selected before (the ship turns with the right stick); on the keyboard left / right
        /// stay the ship's turning keys (A / D and the arrows).</summary>
        void OnNavigate(NavigationMoveEvent e)
        {
            if (GoF2Remake.Multiplayer.NetChat.Typing) return;   // the arrows move the chat line's cursor
            SetTouchMode(false);
            bool vertical = e.direction == NavigationMoveEvent.Direction.Up || e.direction == NavigationMoveEvent.Direction.Down;
            bool horizontal = e.direction == NavigationMoveEvent.Direction.Left || e.direction == NavigationMoveEvent.Direction.Right;
            if (HangarOpen && !DialogOpen)
            {
                // Read in Update (HoldDirections): navigation events only arrive while a UI element has focus.
                e.StopPropagation();
                root.focusController?.IgnoreEvent(e);
                return;
            }
            var stationItems = new VisualElement[] { hangarButton, loungeButton, mapButton, missionsButton, statusButton, launchButton };
            VisualElement[] items;
            if (DialogOpen) items = new VisualElement[] { dialogYes, dialogNo };
            else if (SystemMenuOpen) items = SystemMenuItems();
            else if (missions != null && missions.IsOpen) { items = missions.NavItems(); vertical |= horizontal; }
            else if (status != null && status.IsOpen) { items = status.NavItems(); vertical |= horizontal; }
            else if (lounge != null && lounge.ChatOpen) items = lounge.NavItems();
            else if (lounge != null && lounge.Active)
            {
                var l = new System.Collections.Generic.List<VisualElement>(lounge.NavItems());
                l.AddRange(stationItems);
                items = l.ToArray();
            }
            else items = stationItems;
            if (!DialogOpen && SystemMenuOpen && sysPage == SysPage.Options && optionsView != null && (horizontal || vertical))
            {
                // Like the main menu's Options: up / down walk the tab row, the tab's rows and the footer (stopping at the
                // ends, Default settings beside Back); left / right switch tabs on the tab row, step a row, or move
                // between Back and Default settings.
                var f = root.focusController?.focusedElement as VisualElement;
                var nav = optionsView.NavItems();
                int at = nav.IndexOf(f);
                if (horizontal)
                {
                    int dir = e.direction == NavigationMoveEvent.Direction.Left ? -1 : 1;
                    var row = optionsView.RowOf(f);
                    if (optionsView.IsTab(f)) optionsView.StepTab(dir);
                    else if (row != null) row.Step(dir);
                    else if (f == optionsView.BackButton || f == optionsView.DefaultsButton)
                        Select(dir < 0 ? optionsView.BackButton : optionsView.DefaultsButton);
                    else Select(optionsView.ActiveTab);
                }
                else
                {
                    int step = e.direction == NavigationMoveEvent.Direction.Up ? -1 : 1;
                    int next = at < 0 ? 0 : at + step;
                    if (step > 0 && f == optionsView.BackButton) next = at;
                    if (step < 0 && f == optionsView.DefaultsButton) next = at - 2;
                    Select(nav[Mathf.Clamp(next, 0, nav.Count - 1)]);
                }
                e.StopPropagation();
                root.focusController?.IgnoreEvent(e);
                return;
            }
            if (!DialogOpen && SystemMenuOpen && DebugPage && horizontal)
            {
                // Left / right steps the focused option (OptionControl.Step, like the pause menu).
                var f = root.focusController?.focusedElement as VisualElement;
                var c = stationOptions.Find(o => o.Field == f);
                if (c != null) c.Step(e.direction == NavigationMoveEvent.Direction.Left ? -1 : 1);
                e.StopPropagation();
                root.focusController?.IgnoreEvent(e);
                return;
            }
            var focusedNow = root.focusController?.focusedElement as VisualElement;
            int index = System.Array.IndexOf(items, focusedNow);
            bool mainView = !DialogOpen && !SystemMenuOpen && !(missions != null && missions.IsOpen) && !(status != null && status.IsOpen)
                            && !(lounge != null && lounge.ChatOpen);
            if (mainView && index >= 0 && focusedNow != launchButton) lastSideItem = focusedNow;
            if (mainView && horizontal && InputMode.Current == InputKind.Gamepad)
            {
                if (e.direction == NavigationMoveEvent.Direction.Right) { if (Navigable(launchButton)) launchButton.Focus(); }
                else if (focusedNow == launchButton)
                {
                    // Back to the button selected before Launch, or (locked meanwhile) the nearest one above it.
                    var back = Navigable(lastSideItem) && System.Array.IndexOf(items, lastSideItem) >= 0 ? lastSideItem
                                                                                                         : NextNavigable(items, index, -1);
                    back?.Focus();
                }
            }
            else if (DialogOpen ? horizontal : vertical)
            {
                int step = e.direction == NavigationMoveEvent.Direction.Up || e.direction == NavigationMoveEvent.Direction.Left ? -1 : 1;
                NextNavigable(items, index, step)?.Focus();
            }
            e.StopPropagation();
            root.focusController?.IgnoreEvent(e);
        }

        void BuildHints(InputKind kind)
        {
            if (hints == null) return;
            hints.Clear();
            string T(string key, string english) => Localization.Extra(key, english);
            if (SystemMenuOpen)
            {
                string select = T("hudSelect", "SELECT"), confirm = T("hudConfirm", "CONFIRM"), close = T("hudBack", "BACK");
                if (kind == InputKind.KeyboardMouse)
                {
                    Hint(select, InputGlyph.Key("W"), InputGlyph.Key("S"));
                    Hint(confirm, InputGlyph.Key("ENTER", true));
                    Hint(close, InputGlyph.Key("ESC"));
                }
                else if (kind == InputKind.Gamepad)
                {
                    Hint(select, InputGlyph.Pad(PadButton.DPad));
                    Hint(confirm, InputGlyph.Pad(PadButton.A));
                    Hint(close, InputGlyph.Pad(PadButton.B));
                }
                return;
            }
            if (HangarOpen)
            {
                bool store = hangarWindow != null && hangarWindow.StorageMode;   // the Kaamo Club's storage
                string select = T("hudSelect", "SELECT"), trade = store ? $"{T("shopStore", "STORE")} / {T("shopTake", "TAKE")}"
                                                                        : $"{T("shopSell", "SELL")} / {T("shopBuy", "BUY")}";   // ingredients: ADD
                string tabs = $"{Localization.Get(183)} / {Localization.Get(store ? 186 : 185)} / {Localization.Get(272)}".ToUpperInvariant(), confirm = T("hudConfirm", "CONFIRM");
                string sellShip = T("kaamoSellShip", "SELL SHIP");
                if (kind == InputKind.KeyboardMouse)
                {
                    Hint(select, InputGlyph.Key("W"), InputGlyph.Key("S"));
                    Hint(trade, InputGlyph.Key("A"), InputGlyph.Key("D"));
                    Hint(confirm, InputGlyph.Key("ENTER", true));
                    Hint(tabs, InputGlyph.Key("Q"), InputGlyph.Key("E"));
                    if (store) Hint(sellShip, InputGlyph.Key("X"));
                    Hint(T("hudBack", "BACK"), InputGlyph.Key("ESC"));
                }
                else if (kind == InputKind.Gamepad)
                {
                    Hint($"{select} / {trade}", InputGlyph.Pad(PadButton.DPad));
                    Hint(confirm, InputGlyph.Pad(PadButton.A));
                    Hint(trade, InputGlyph.Pad(PadButton.X), InputGlyph.Pad(PadButton.A));   // a shop row: X sells, A buys
                    Hint(tabs, InputGlyph.Pad(PadButton.LeftBumper), InputGlyph.Pad(PadButton.RightBumper));
                    if (store) Hint(sellShip, InputGlyph.Pad(PadButton.X));
                    Hint(T("hudBack", "BACK"), InputGlyph.Pad(PadButton.B));
                }
                return;
            }
            // The main view shows no hints: the buttons cover hangar / lounge / map / launch / menu (their keys still work:
            // 1 / 2 / M / L, LB / RB / Y / X). Only the lounge's way back.
            bool hangar = level == null || level.View == StationView.Hangar;
            if (hangar) return;
            if (kind == InputKind.KeyboardMouse) Hint(T("hudBack", "BACK"), InputGlyph.Key("ESC"));
            else if (kind == InputKind.Gamepad) Hint(T("hudBack", "BACK"), InputGlyph.Pad(PadButton.B));
        }

        void Hint(string label, params VisualElement[] glyphs)
        {
            var h = new VisualElement { pickingMode = PickingMode.Ignore };
            h.AddToClassList("hint");
            foreach (var g in glyphs) h.Add(g);
            var l = new Label(label) { pickingMode = PickingMode.Ignore };
            l.AddToClassList("hint-label");
            l.AddToClassList("gof-semibold");
            h.Add(l);
            hints.Add(h);
        }

        // ---- per frame -----------------------------------------------------------------------------------

        /// <summary>ModStation::OnInitialize and ModStation::checkHints: what the station has to say, one at a time, while no
        /// window is open; true when something opened.</summary>
        bool RunDockingChecks()
        {
            if (DialogOpen || SystemMenuOpen || HangarOpen) return false;
            if (CheckPirateBase() || CheckDockingFine() || CheckStory() || CheckFreelance() || CheckKaamo() || CheckPendingProducts()) return true;
            if (!storyDialogue.IsOpen && (CheckRescue() || CheckMedals() || CheckMedalHints())) return true;
            return CheckWanted() || CheckWingmenContract();
        }

        void Update()
        {
            if (root == null || level == null) return;
            if (lastScreen != ScreenSize() || lastSafeArea != Screen.safeArea) UpdateLayout();
            UpdateTicker();   // scrolls on under dialogs and windows (it used to wait, then fly in again)
            // Remake hangar flights: the menu hides while the ship flies in or out; the conversations, windows and hints
            // wait until it has landed. Any key / tap / button skips the flight.
            bool flying = level.PlayerFlying;
            // Not flying: no inline value at all, so the stylesheet's own rules (.station-map-open hides the menu under the
            // star map) apply again; an inline Flex here kept the station HUD over the map.
            if (safeArea != null && flying != safeAreaHidden)
            {
                safeAreaHidden = flying;
                safeArea.style.display = flying ? DisplayStyle.None : StyleKeyword.Null;
            }
            if (flying)
            {
                if (World.SpaceLevel.PlayerTriedToFly()) level.SkipPlayerFlight();
                settleMs = ArrivalSettleMs;
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
                    if (sysPage == SysPage.Debug) SwitchDebugTab(tab); else optionsView.StepTab(tab);
                    return;
                }
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

            var kb = GoF2Remake.Multiplayer.NetChat.Keys;
            var pad = Gamepad.current;
            if ((kb != null && kb.escapeKey.wasPressedThisFrame) || (pad != null && pad.buttonEast.wasPressedThisFrame)) { Back(); return; }
            if (infoWindow != null && infoWindow.IsOpen) { infoWindow.Tick(); return; }   // it takes all input
            if (pad != null && pad.startButton.wasPressedThisFrame && !DialogOpen)
            {
                Play(buttonRelease);
                if (SystemMenuOpen) CloseSystemMenu(); else OpenSystemMenu();
                return;
            }
            if (toastMs > 0f && (toastMs -= Time.unscaledDeltaTime * 1000f) <= 0f) toast.RemoveFromClassList("station-toast--shown");
            if (DialogOpen || SystemMenuOpen) return;
            if (missions != null && missions.IsOpen) return;
            if (status != null && status.IsOpen) return;
            if (kb != null && kb.digit5Key.wasPressedThisFrame) { Play(buttonRelease); OpenStatus(); return; }
            if (lounge != null && lounge.ChatOpen) return;
            if ((kb != null && kb.digit4Key.wasPressedThisFrame) || (pad != null && pad.selectButton.wasPressedThisFrame))
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
                if ((kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)) || (pad != null && pad.buttonSouth.wasPressedThisFrame))
                    hangarWindow.Action();
                else if ((kb != null && kb.xKey.wasPressedThisFrame) || (pad != null && pad.buttonWest.wasPressedThisFrame))
                    hangarWindow.SecondaryAction();   // Sell a stored hull (Kaamo Club)
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
                else if (kb != null && kb.digit2Key.wasPressedThisFrame) OpenLounge();
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

            if (dragPointer >= 0)
            {
                // Release velocity: the turn per frame, smoothed (the original uses the last frame's dx).
                dragVelocity = Mathf.Lerp(dragVelocity, dragFrameRadians / Mathf.Max(Time.unscaledDeltaTime, 1e-3f), 0.6f);
                dragFrameRadians = 0f;
            }
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

        void Play(AudioClip clip)
        {
            if (sfxSource != null && clip != null) sfxSource.PlayOneShot(clip, Settings.SfxVolume);
        }

        // ---- layout (same scaling and safe-area handling as the flight HUD) ---------------------------------

        Vector2Int ScreenSize()
        {
            var rt = runtimePanel != null ? runtimePanel.targetTexture : null;
            return rt != null ? new Vector2Int(rt.width, rt.height) : new Vector2Int(Screen.width, Screen.height);
        }

        void UpdateLayout()
        {
            lastScreen = ScreenSize();
            lastSafeArea = Screen.safeArea;
            tickerReady = false;   // the strip's width changes: fill it again
            bool offscreen = runtimePanel != null && runtimePanel.targetTexture != null;
            float w = Mathf.Max(1, lastScreen.x), h = Mathf.Max(1, lastScreen.y);
            float inches = Screen.dpi > 0f ? Mathf.Sqrt(w * w + h * h) / Screen.dpi : 20f;
            bool phone = Application.isMobilePlatform && inches < 7.5f;
            if (runtimePanel != null)
            {
                runtimePanel.referenceResolution = phone ? new Vector2Int(1600, 900) : new Vector2Int(1920, 1080);
                runtimePanel.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
                runtimePanel.match = 1f;
            }
            root.EnableInClassList("layout-phone", phone);
            ApplySafeArea(w, h, offscreen);
        }

        /// <summary>Device safe-area insets; retries until the panel has a real (non-NaN) size, see FlightHud.</summary>
        void ApplySafeArea(float w, float h, bool offscreen)
        {
            root.schedule.Execute(() =>
            {
                if (safeArea == null) return;
                if (!(root.layout.width > 0f)) { ApplySafeArea(w, h, offscreen); return; }
                float k = root.layout.width / w;
                var raw = offscreen ? new Rect(0f, 0f, w, h) : Screen.safeArea;
                var sa = Rect.MinMaxRect(Mathf.Clamp(raw.xMin, 0f, w), Mathf.Clamp(raw.yMin, 0f, h),
                                         Mathf.Clamp(raw.xMax, 0f, w), Mathf.Clamp(raw.yMax, 0f, h));
                if (sa.width < w * 0.5f || sa.height < h * 0.5f) sa = new Rect(0f, 0f, w, h);
                safeArea.style.left = sa.xMin * k;
                safeArea.style.right = (w - sa.xMax) * k;
                safeArea.style.top = (h - sa.yMax) * k;
                safeArea.style.bottom = sa.yMin * k;
            }).ExecuteLater(1);
        }
    }
}
