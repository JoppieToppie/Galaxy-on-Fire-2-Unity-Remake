// FlightHud.cs
// In-flight HUD (UI Toolkit) that adapts to the input in use (InputMode):
//   Touch:      the original's controls (TouchControls: the fixed stick, the right cluster with fire / secondary / quick
//               menu / camera, boost, pause, the throttle drag and the dodge on the empty screen); fire is the action button.
//   Keyboard:   keycap hints (WASD, Q/E, Ctrl, F, Space, R, Esc).
//   Controller: Xbox or PlayStation button hints (PadGlyph, InputMode.Style: LS / L, LB / L1, RT / R2, A / cross...).
// Always: speed, throttle and boost readout, and the crosshair: the screen projection of ship + forward * 22000
// units (where the bullets are after 22000 units), orange for 200 ms after a hit (weapons.md section 9). The chase camera uses the original's fixed touch-mode damping for
// touch and the handling-dependent damping otherwise (TargetFollowCamera::resetShipHandling / setShipHandling).
// Esc, the Android back button or the controller's Menu button returns to the main menu (no pause menu yet).
// One action prompt (tap it, Enter, or the controller's X): "Mine" with a locked asteroid, "Abort" during the
// autopilot approach, "Stop mining" in the minigame (Mining, the original's fire button), else "Dock" near the
// station (SpaceLevel.CanDock). MiningView draws the lock ring, the ore plate, HUD messages and the minigame.
// The autopilot button (touch), Tab or the controller's View button opens the autopilot menu (game paused): pick an entry
// by tap / click, W/S + Enter or D-pad + A; Esc / B / the same button closes it.
// The star map (StarMap) covers everything while open; jump scenes and docking to the gate hide the HUD
// (SystemJump.Cinematic); the Khador Drive's charge shows as a bar; after a gate / Khador jump the arrival camera shows
// the orbit information (Hud::drawOrbitInformation: race logo, station, "<System> System", security level).
// Combat (CombatView): ship / crate markers, the ship lock plate, the player's shield / hull / armor bars and hit arcs;
// radio and salvage messages; after the player's death the "Game Over" screen (319) with "Tap to load last savegame."
// (196) 7 s later, which reloads the last docked state (Session.LoadAutosave), or the main menu without one (199).

using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.World;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace GoF2Remake.UI
{
    [RequireComponent(typeof(PanelRenderer))]
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]   // RadarScale: set again by every layout pass
    public partial class FlightHud : MonoBehaviour
    {
        public string menuScene = "MainMenu";

        PanelRenderer panelRenderer;
        PanelSettings runtimePanel;
        bool lastTurretView, lastTurretAuto, lastShipLocked;
        VisualElement lockPlate;
        VisualElement root, safeArea, hints;
        bool hudFading;
        VisualElement crosshair, dockPrompt, dockGlyph;
        VisualElement lockRingElement, navMarkers;   // queried once: two root.Q per frame were a fifth of the HUD's update

        // The secondary plate's "<name> (<amount>)" as last built: every frame made the string and looked the name up again.
        int secondaryItem = int.MinValue, secondaryAmmo = int.MinValue;
        string secondaryLanguage, secondaryLine;

        string SecondaryText(int ammo)
        {
            int item = weapons.SelectedSecondary;
            if (item != secondaryItem || ammo != secondaryAmmo || !ReferenceEquals(Localization.Language, secondaryLanguage))
            {
                secondaryItem = item; secondaryAmmo = ammo; secondaryLanguage = Localization.Language;
                secondaryLine = $"{weapons.SecondaryName} ({ammo})";
            }
            return secondaryLine;
        }
        Label dockLabel;
        SpaceLevel level;
        Mining mining;
        MiningView miningView;
        ObjectDocking docking;
        HackingView hackingView;
        Label transferCounter;
        Mining.Phase lastPhase;
        Navigation nav;
        NavigationView navView;
        SystemJump jump;
        CombatView combatView;
        MissileWarningView missileWarning;
        float evadedMessageAt = -10f;
        CooldownView cooldownView;   // remake: the booster / cloak recharge for keys and controllers
        PlayerHealth health;
        CombatRadar radar;
        Traffic traffic;
        StorySpace story;
        DialogueView storyDialogue;
        LensFlareView lensFlare;
        PauseMenu pauseMenu;
        bool voicePausedByMenu;
        GoF2Remake.World.FreelanceOrbit freelance;
        AudioSource voiceSource;
        VisualElement radioBox, radioPortrait, screenFade;
        Label radioSpeaker, radioText;
        TextReveal radioReveal;
        int radioShown = -1;
        Traffic.Chatter shownChatter;
        VisualElement gameOver;
        Label gameOverText;
        float gameOverMs = -1f;
        VisualElement jumpCharge, jumpChargeFill, jumpChargeStripes, orbitInfo;
        Label jumpChargeLabel;
        bool lastCloakCharging, lastDriveCharging;
        bool orbitInfoFilled;
        bool lastAutopilot;
        VisualElement autopilotMenu, autopilotMenuItems;
        readonly System.Collections.Generic.List<(Button button, Navigation.Target target)> menuButtons = new System.Collections.Generic.List<(Button, Navigation.Target)>();
        int menuIndex, lastMenuMove, menuOpenedFrame;
        WeaponSystem weapons;
        float hitFlashMs;
        const float CrosshairDistanceMeters = 22000f * 0.05f;   // 0x46abe000
        TouchControls touch;
        float quickMenuCheck;
        bool quickMenuEntries;
        ShipController ship;
        ChaseCamera chase;
        Vector2Int lastScreen;
        Rect lastSafeArea;
        int lastLayout = -1;

        /// <summary>Remake: the radar ellipse's size against the original's 657 x 491 (a 1080-high canvas): the phone layout's
        /// panel is 900 high, so there it shrinks by 900 / 1080 to keep the original's share of the screen height (at full size
        /// it reached past the top and bottom edges, and the off-screen dots there weren't seen). CombatView, NavigationView and
        /// MissileWarningView place their markers and hit arcs on it with this.</summary>
        public static float RadarScale { get; private set; } = 1f;

        void OnEnable()
        {
            panelRenderer = GetComponent<PanelRenderer>();
            panelRenderer.RegisterUIReloadCallback(OnUIReload);   // before the panel settings swap, which reloads
            if (runtimePanel == null && panelRenderer.panelSettings != null)
            {
                runtimePanel = Instantiate(panelRenderer.panelSettings);   // per-scene scaling (phone reference)
                panelRenderer.panelSettings = runtimePanel;
            }
            InputMode.Changed += ApplyInputMode;
            Localization.Changed += RefreshTexts;   // Modified: the in-game Options' language row applies at once
            GameControls.Changed += ApplyInputMode;   // a rebound key: the hints show it
            Gun.LockShaken += OnLockShaken;
            ShipController.DampenersChanged += OnDampeners;
        }

        /// <summary>Free flight's inertial dampeners toggled (ShipController.Dampeners).</summary>
        void OnDampeners(bool on) =>
            miningView?.ShowMessage(on ? Localization.Extra("dampenersOn", "Inertial dampeners on")
                                       : Localization.Extra("dampenersOff", "Inertial dampeners off"));

        void OnDestroy()
        {
            // The per-scene PanelSettings clone: left alive, every Space load (jumps, launches) kept another UI Toolkit
            // panel (its atlas and GPU buffers) for the rest of the run.
            if (runtimePanel != null) Destroy(runtimePanel);
        }

        /// <summary>The HUD going on across a level swap (SpaceLevel.SwapScene). A new panel's first styling and layout took 54 ms
        /// of the swap's frame, the largest part of its stall (621 elements, measured 2026-11-01), so the old HUD stays and takes
        /// the new level the way a new one does, in Update. Not in a session or in VR: their views hang on a level's HUD.</summary>
        static FlightHud kept;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetKept() => kept = null;

        public static void KeepForSwap()
        {
            kept = null;
            if (NetSession.Active || VrFlags.Enabled) return;
            var hud = FindAnyObjectByType<FlightHud>();
            if (hud == null || hud.root == null) return;
            kept = hud;
            DontDestroyOnLoad(hud.gameObject);
        }

        /// <summary>The old level is gone, the new one's Awake is starting: its scene's HUD goes before it builds a panel, and the kept
        /// one lets go of the old level. Real nulls: a destroyed object passes a '?.'.</summary>
        public static void DropSceneHud()
        {
            if (kept == null) return;
            foreach (var hud in FindObjectsByType<FlightHud>(FindObjectsInactive.Include))
                if (hud != kept) DestroyImmediate(hud.gameObject);
            kept.level = null;
            kept.ship = null;
            kept.weapons = null;
            kept.mining = null;
            kept.docking = null;
            kept.nav = null;
            kept.jump = null;
            kept.health = null;
            kept.radar = null;
            kept.traffic = null;
            kept.story = null;
            kept.freelance = null;
            kept.chase = null;
            kept.volatileCargo = null;
            kept.orbitInfoFilled = false;
            kept.radioShown = -1;
            kept.shownChatter = null;
        }

        /// <summary>Back in a scene, so that leaving this level takes the HUD with it.</summary>
        public static void SettleKept(Scene scene)
        {
            if (kept != null) SceneManager.MoveGameObjectToScene(kept.gameObject, scene);
            kept = null;
        }

        /// <summary>The Mod Builder's screen in space (ModBuilder.InSpace), null otherwise.</summary>
        ModBuilderSpaceView builderSpace;

        void OnDisable()
        {
            builderSpace?.Dispose();
            builderSpace = null;
            pauseMenu?.Close();   // the scene is going: sounds and time back to normal
            UnityEngine.Cursor.lockState = CursorLockMode.None;   // the captured mouse (mouse steering) is released
            UnityEngine.Cursor.visible = InputMode.CursorShown;
            if (mining != null) mining.Message -= OnKindMessage;
            if (docking != null) docking.Message -= OnMiningMessage;
            if (nav != null) nav.Message -= OnKindMessage;
            if (jump != null) jump.Message -= OnMiningMessage;
            if (traffic != null) traffic.Message -= OnMiningMessage;
            if (traffic != null) traffic.ChoiceRequested -= OnChoiceRequested;
            if (radar != null) radar.Message -= OnRadarMessage;
            if (health != null) health.GameOverStarted -= OnGameOver;
            panelRenderer?.UnregisterUIReloadCallback(OnUIReload);
            InputMode.Changed -= ApplyInputMode;
            Localization.Changed -= RefreshTexts;
            GameControls.Changed -= ApplyInputMode;
            Gun.LockShaken -= OnLockShaken;
            ShipController.DampenersChanged -= OnDampeners;
        }

        /// <summary>Remake: a boost shook off missiles homing on the player (any shooter, Gun): "Missiles evaded!" in green, at
        /// most every 1.5 s.</summary>
        void OnLockShaken(Gun gun, Target target)
        {
            if (target == null || !target.isPlayer || Time.unscaledTime - evadedMessageAt < 1.5f) return;
            evadedMessageAt = Time.unscaledTime;
            miningView?.ShowMessage(Localization.Extra("missilesEvaded", "Missiles evaded!"), 2);
        }

        void OnUIReload(PanelRenderer renderer, VisualElement rootElement, int version)
        {
            root = rootElement;
            ScrollBars.Watch(rootElement);   // Modified: the hangar's scrollbar on every scroll view that needs one
            PadKeyboard.Attach(rootElement);   // a controller's keyboard for the chat's text field (docs/UI-DESIGN.md "Text entry")
            KeyboardLift.Watch(rootElement);   // phones: the focused text field kept above the on-screen keyboard
            root.style.flexGrow = 1;   // no default theme to stretch the document root
            root.pickingMode = PickingMode.Ignore;
            safeArea = root.Q("safeArea");
            hints = root.Q("hints");
            carrierShop = null;   // its elements went with the old tree (made again when the carrier is docked at)
            InputGlyph.TrackHintsOption(hints);   // Options > Gameplay: "Button hints in flight"
            if (GoF2Remake.Multiplayer.NetGame.Active)
            {
                ChatView.Attach(gameObject, safeArea ?? root);    // multiplayer chat
                SquadView.Attach(gameObject, safeArea ?? root);   // the squad window, invitations
                ArenaView.Attach(gameObject, safeArea ?? root);   // an arena match: score, timer, respawn, result
                TerritoryView.Attach(gameObject, safeArea ?? root);   // a faction station's toll, a siege's banner
                MultiplayerWindow.Attach(gameObject, safeArea ?? root, true);   // the multiplayer window (N): chat, squad, distress, admin
            }

            InputGlyph.TrackHintsOption(hints);
            crosshair = root.Q("crosshair");
            lockRingElement = root.Q("lockRing");
            navMarkers = root.Q("navMarkers");
            dockPrompt = root.Q("dockPrompt");
            dockGlyph = root.Q("dockGlyph");
            dockLabel = root.Q<Label>("dockLabel");
            jumpCharge = root.Q("jumpCharge");
            jumpChargeFill = root.Q("jumpChargeFill");
            orbitInfo = root.Q("orbitInfo");
            jumpChargeLabel = root.Q<Label>("jumpChargeLabel");
            jumpChargeLabel.text = Localization.Get(318).ToUpperInvariant();   // Drive charging
            jumpChargeStripes = root.Q("jumpChargeStripes");
            var chargeFrame = Resources.Load<Texture2D>("GoF2Hud/charge_frame");
            var chargeFill = Resources.Load<Texture2D>("GoF2Hud/charge_fill");
            if (chargeFrame != null) jumpCharge.style.backgroundImage = new StyleBackground(chargeFrame);
            if (chargeFill != null) jumpChargeStripes.style.backgroundImage = new StyleBackground(chargeFill);
            orbitInfoFilled = false;

            HookPress(dockPrompt, null, Interact);
            miningView = new MiningView(root);
            readout = new HudReadout(safeArea);
            challengeView = new ChallengeView(safeArea);   // the Supernova Challenge's score and combo
            hackingView = new HackingView(root);
            transferCounter = new Label { pickingMode = PickingMode.Ignore };
            transferCounter.AddToClassList("transfer-counter");
            transferCounter.AddToClassList("gof-semibold");
            transferCounter.AddToClassList("transfer-counter--hidden");
            root.Add(transferCounter);
            navView = new NavigationView(root);
            combatView = new CombatView(root);
            // Remake: on touch a tap on the lock plate unlocks the ship locked (Unlock target); pickable only meanwhile.
            lockPlate = root.Q("lockPlate");
            lockPlate.RegisterCallback<PointerDownEvent>(e =>
            {
                if (InputMode.Current != InputKind.Touch || radar == null || radar.Locked == null) return;
                UnlockTarget();
                e.StopPropagation();
            });
            missileWarning = new MissileWarningView(root, gameObject);
            BuildRadarEllipse(root.Q("radarEllipse"));
            var speedPanel = root.Q("speedPanel");
            if (speedPanel != null) cooldownView = new CooldownView(speedPanel);
            lensFlare = new LensFlareView(root);
            root.Q("storyDialogue").pickingMode = PickingMode.Ignore;
            if (voiceSource == null)
            {
                voiceSource = gameObject.AddComponent<AudioSource>();
                voiceSource.playOnAwake = false;
                voiceSource.spatialBlend = 0f;
                voiceSource.ignoreListenerPause = true;
            }
            storyDialogue = new DialogueView(root, voiceSource) { ButtonSound = PlayButton };
            pauseMenu?.Close();   // a UI reload rebuilds it: don't leave the game paused
            pauseMenu = new PauseMenu(root, BackToMenu);
            ButtonSounds(root);   // Modified: every button and toggle of the HUD's menus (was the pause menu's and the autopilot menu's)
            UiSounds.Attach(root, false);   // Rework: a focus move by the keys / a controller in its windows (the multiplayer window...)
            UiFocus.SkipScrollBars(root);
            pauseMenu.InfoSound = () => PlayUi(CombatAudio.Load()?.messageInfo);
            pauseMenu.HoverSound = PlayHover;   // Modified: the menus' hover / arrow sound
            pauseMenu.ButtonSound = PlayButton;
            pauseMenu.Photo = new PhotoMode(root, this) { ButtonSound = PlayButton };
            radioBox = root.Q("radio");
            screenFade = root.Q("screenFade");
            radioPortrait = root.Q("radioPortrait");
            radioSpeaker = root.Q<Label>("radioSpeaker");
            radioText = root.Q<Label>("radioText");
            radioReveal = new TextReveal(radioText);
            radioShown = -1;
            gameOver = root.Q("gameOver");
            gameOverText = root.Q<Label>("gameOverText");
            root.Q<Label>("gameOverTitle").text = Localization.Get(319).ToUpperInvariant();   // Game Over
            gameOver.RegisterCallback<PointerDownEvent>(_ => LoadLastSave());
            navView.AutopilotButton += OnAutopilotButton;
            autopilotMenu = root.Q("autopilotMenu");
            // Modified: the mouse drives the menu's highlight while it moves (only the entry under it, none over nothing; no hover
            // sound: the cursor crossing it made a burst of them); the keys / a controller bring the highlight back
            // (UpdateAutopilotMenu, with the arrows' sound).
            autopilotMenu.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (e.pointerType != UnityEngine.UIElements.PointerType.mouse || menuPointer) return;
                menuPointer = true;
                HighlightMenu();
            }, TrickleDown.TrickleDown);
            autopilotMenu.RegisterCallback<PointerOverEvent>(e =>
            {
                if (e.pointerType != UnityEngine.UIElements.PointerType.mouse || !(e.target is VisualElement t)) return;
                var b = MenuButtonAt(t);
                if (b == menuHover) return;
                menuHover = b;
                if (menuPointer) HighlightMenu();
            });
            autopilotMenu.RegisterCallback<PointerLeaveEvent>(e =>
            {
                if (e.target != autopilotMenu) return;
                menuHover = null;
                if (menuPointer) HighlightMenu();
            });
            autopilotMenuItems = root.Q("autopilotMenuItems");
            root.Q<Label>("autopilotMenuTitle").text = Localization.Get(571).ToUpperInvariant();   // Autopilot
            var menuIcon = Resources.Load<Texture2D>("GoF2Hud/autopilot_title");
            if (menuIcon != null) root.Q("autopilotMenuIcon").style.backgroundImage = new StyleBackground(menuIcon);
            BuildTouchControls();

            RefreshTexts();

            ApplyInputMode();
            UpdateLayout();
        }

        /// <summary>Modified: the few labels this HUD writes once while it is built, read again from the table loaded now
        /// (Localization.Changed), so the in-game Options' language row applies in flight without leaving the level. Every
        /// other text here is written as it is shown (the lock plate, HUD messages, the menu titles, the orbit information)
        /// and already reads the table each time; the key hints are rebuilt with BuildHints.</summary>
        public void RefreshTexts()
        {
            if (root == null) return;
            if (jumpChargeLabel != null) jumpChargeLabel.text = Localization.Get(318).ToUpperInvariant();   // Drive charging
            root.Q<Label>("gameOverTitle").text = Localization.Get(319).ToUpperInvariant();                // Game Over
            root.Q<Label>("autopilotMenuTitle").text = Localization.Get(571).ToUpperInvariant();           // Autopilot
            root.Q<Label>("dockLabel").text = Localization.Extra("hudDock", "DOCK");
            BuildHints(InputMode.Current);
        }

        /// <summary>Radar::draw's faint radar ellipse (image 0x4c7, the top-left quarter, 657 x 491): drawn four times mirrored
        /// around the screen centre in every input mode, except while drilling (MGame::OnRender2D skips Radar::draw then).
        /// Off-screen markers and the hit arcs sit on it (CombatView / NavigationView EllipseX / Y).</summary>
        static void BuildRadarEllipse(VisualElement host)
        {
            var tex = Resources.Load<Texture2D>("GoF2Hud/radar_ellipse");
            if (host == null || tex == null) return;
            host.Clear();
            for (int q = 0; q < 4; q++)
            {
                bool right = (q & 1) != 0, bottom = (q & 2) != 0;   // DrawImage2D's flip 1 = mirrored, 2 = flipped, 3 = both
                var e = new VisualElement { pickingMode = PickingMode.Ignore };
                e.AddToClassList("radar-quarter");
                e.style.backgroundImage = new StyleBackground(tex);
                e.style.left = right ? 0f : -tex.width;
                e.style.top = bottom ? 0f : -tex.height;
                e.style.width = tex.width;
                e.style.height = tex.height;
                e.style.scale = new Scale(new Vector3(right ? -1f : 1f, bottom ? -1f : 1f, 1f));
                host.Add(e);
            }
        }

        // ---- touch controls (TouchControls, touch_hud.md) ----------------------------------------------------------

        readonly List<int> stripItems = new List<int>();

        float lastThrust = -1f;

        /// <summary>The PC version's cursor mode (Globals::mouseCursorActivated off: M or the middle mouse button, the manual's
        /// "Mouse control menu / ship"): with keys and mouse and the mouse not steering, Hud::draw's buttons show and take
        /// clicks (no stick, the cluster in the bottom-right corner). While the mouse steers they are invisible (alpha 0).</summary>
        bool cursorMode;

        /// <summary>The touch controls show this frame with the autopilot / fast-forward block (UpdateTouch): not under the
        /// pause menu, a conversation, the star map or death, nor in a cutscene, jump or the launch camera (pause button
        /// only). NavigationView shows the block by it; it only asked for touch input, so it stayed over the pause menu.</summary>
        bool touchNavShown;

        float readoutRight = -1f;

        // ---- input mode ------------------------------------------------------------------------------------

        InputKind hintKind;

        // ---- per frame -----------------------------------------------------------------------------------

        VisualElement mouseReticle;

        VisualElement stickRing;
        /// <summary>The reach circle's fade (0 hidden .. 1 shown).</summary>
        float stickRingShown;
        const float StickRingFadeInMs = 150f, StickRingFadeOutMs = 400f;
        const float StickRingFrom = 0.6f, StickRingFull = 0.9f;

        void Update()
        {
            if (root == null) return;
            // The layout first: the Mod Builder's space screen below shares this panel and its scale.
            if (lastScreen != ScreenSize() || lastSafeArea != Screen.safeArea || lastLayout != UiLayout.Revision) UpdateLayout();
            // Remake: the Mod Builder's work in space (Stations, Weapon fx): its own screen instead of the HUD.
            if (Modding.ModBuilder.InSpace)
            {
                if (builderSpace == null)
                {
                    level ??= FindAnyObjectByType<SpaceLevel>();
                    if (level == null || level.Player == null) return;
                    foreach (var child in root.Children()) child.style.display = DisplayStyle.None;
                    builderSpace = new ModBuilderSpaceView(level, root, PlayButton);
                }
                builderSpace.Update();
                return;
            }
            // Remake mods: docked inside the orbit (EVERSPACE 2's dock): the station's menu runs over the orbit, no HUD
            bool dockedInOrbit = level != null && level.DockedInOrbit;
            if (dockedInOrbit != hudHiddenDocked)
            {
                hudHiddenDocked = dockedInOrbit;
                root.style.display = dockedInOrbit ? DisplayStyle.None : DisplayStyle.Flex;
                if (dockedInOrbit) { touch?.ReleaseAll(); ship?.SetSteer(Vector2.zero); weapons?.SetPrimaryHeld(false); }
            }
            if (dockedInOrbit) return;
            missileWarning?.Hide();   // shown again below while missiles fly at the player and the HUD shows
            // The pause menu pauses the voice too: the shared voice source ignores the listener pause (it has to play while
            // a conversation pauses the game), so the radio's line kept talking over the menu. Multiplayer doesn't pause.
            bool menuPause = pauseMenu != null && pauseMenu.IsOpen && !GoF2Remake.Multiplayer.NetGame.Active;
            if (menuPause != voicePausedByMenu && voiceSource != null)
            {
                voicePausedByMenu = menuPause;
                if (menuPause) voiceSource.Pause(); else voiceSource.UnPause();
            }
            // The root's input classes follow InputMode.Changed; should they ever disagree with the input in use (a change
            // missed while the HUD was being built), the touch layer, the readout's place and the hints went by the wrong
            // input (a phone showed the cargo readout over the pause button): put them right.
            if (root.ClassListContains("input-touch") != (InputMode.Current == InputKind.Touch)) ApplyInputMode();
            UpdateTouch();   // every frame: the controls hide and let go under menus, dialogues and cutscenes
            UpdateMouseSteering();
            // StarSystem::render2D, under the HUD; Action Freeze can switch it for the photo (PhotoSettings.Flare).
            lensFlare?.Update(level != null ? level.Backdrop : null,
                              StarMap.IsOpen || !PhotoSettings.FlareShown(pauseMenu != null && pauseMenu.Photo != null && pauseMenu.Photo.Active));

            bool mapOpen = StarMap.IsOpen;
            root.EnableInClassList("hud-map", mapOpen);
            root.EnableInClassList("hud-paused", pauseMenu != null && pauseMenu.IsOpen);   // the menu has its own prompts
            root.EnableInClassList("hud-shop", carrierShop != null && carrierShop.IsOpen);   // the carrier's resupply window
            int hudMode = Settings.HudMode;   // Options > Gameplay "Flight HUD" (immersion): FlightHud.uss hides the markings
            root.EnableInClassList("hud-minimal", hudMode == Settings.HudMinimal);
            root.EnableInClassList("hud-off", hudMode == Settings.HudOff);
            // The FPS counter at the top centre (its default place, FpsCounter): the lock plate and the HUD messages move
            // under it (#137: a mission's reward line was drawn over the counter).
            root.EnableInClassList("hud-fps", Settings.ShowFps && Settings.FpsPosition == Settings.FpsTop);
            ApplyHudLook();
            if (mapOpen) return;   // the map has its own input

            // MGame::OnUpdate: DialogueWindow::update runs only while the player lives; a conversation open at the death waits.
            if (storyDialogue != null) storyDialogue.Paused = health != null && health.Dead;
            if (storyDialogue != null && storyDialogue.IsOpen)
            {
                // No radio box under a conversation: the success dialogue can open on the frame a radio line ends, before
                // UpdateRadio hid it (the dialogue's voice takes over the shared voice source). A line still due shows again
                // when the window closes.
                if (radioBox.ClassListContains("radio--shown"))
                {
                    radioBox.RemoveFromClassList("radio--shown");
                    shownChatter = null;
                    radioShown = -1;
                }
                // Nor the docking's transfer counter or hacking board (their state comes back with the next HUD frame).
                transferCounter?.EnableInClassList("transfer-counter--hidden", true);
                hackingView?.Update(null);
                ship?.SetSteer(Vector2.zero);
                storyDialogue.Tick(Time.unscaledDeltaTime * 1000f);
                return;
            }

            if (volatileCargo == null && level != null && level.Player != null) volatileCargo = level.Player.GetComponent<VolatileCargo>();
            readout?.Update(level, true, volatileCargo != null ? volatileCargo.Force : 0f);
            bool challenge = level != null && level.Challenge != null;
            challengeView?.Update(challenge);
            root.EnableInClassList("hud-challenge", challenge);   // the top plates move under the score
            if (carrierShop != null && carrierShop.IsOpen)
            {
                // The docking ended another way (the carrier destroyed, a level script): the window goes without an undock.
                if (docking == null || !docking.IsDocked)
                {
                    carrierShop.Hide();
                    if (shopAmbience != null) shopAmbience.Stop();
                    nav?.CloseMenu();
                    BuildHints(InputMode.Current);
                }
                else { carrierShop.Tick(); return; }
            }
            if (nav != null && nav.MenuOpen)
            {
                UpdateAutopilotMenu();
                return;
            }

            if (pauseMenu.IsOpen) { pauseMenu.Tick(); return; }
            // Multiplayer: the multiplayer window (its button, or the "Multiplayer window" binding, N): Esc / B close it;
            // meanwhile the flight keys wait (Navigation.InputHalted), and in one of its text fields every key is typing
            // (NetChat.SetTyping: the binding's key is a letter there).
            if (MultiplayerWindow.IsOpenAny)
            {
                var k = GoF2Remake.Multiplayer.NetChat.Keys;
                if ((k != null && k.escapeKey.wasPressedThisFrame) || (Gamepad.current != null && (Gamepad.current.buttonEast.wasPressedThisFrame || Gamepad.current.startButton.wasPressedThisFrame))
                    || (!MultiplayerWindow.TypingAny && GameControls.MultiplayerWindow.WasPressedThisFrame()))
                    MultiplayerWindow.CloseAny();
                return;
            }
            if (GoF2Remake.Multiplayer.NetGame.Active && !GoF2Remake.Multiplayer.NetChat.Typing && (health == null || !health.Dead))
            {
                if (GameControls.MultiplayerWindow.WasPressedThisFrame()) { MultiplayerWindow.ToggleAny(); return; }
                // The distress call: only its button or its own binding (unbound by default), never a menu key.
                if (GameControls.DistressCall.WasPressedThisFrame()) MultiplayerWindow.ToggleDistressAny();
            }
            if ((GoF2Remake.Multiplayer.NetChat.Keys != null && GoF2Remake.Multiplayer.NetChat.Keys.escapeKey.wasPressedThisFrame)
                || (Gamepad.current != null && Gamepad.current.startButton.wasPressedThisFrame))
            {
                // MenuTouchWindow(1): the pause menu; after the player's death straight back to the main menu.
                if (health != null && health.Dead) BackToMenu(); else OpenPause();
                return;
            }
            // The PC version's keys (its help texts 1731 / 1732, 638, 18 and the default list), rebindable (GameControls):
            // Q Autopilot (the target list, again = off) and E Actions (the quick menu), V Wingmen, K Khador Drive, M or the middle mouse button: mouse control (not the mouse while it orbits the
            // free-look camera).
            // Not during a level's cutscene (#46: Q opened the autopilot menu over it, which pauses the game, or turned a
            // scripted autopilot leg off); the touch buttons are hidden then anyway. Nor during the launch / arrival fly-in:
            // the HUD is hidden then, so the menu opened unseen and its pause looked like a frozen game (players' report).
            bool menuKeys = nav != null && (level == null || (!level.Cutscene && level.LaunchCameraOver));
            if (menuKeys && GameControls.AutopilotMenu.WasPressedThisFrame()) OnAutopilotButton();
            else if (menuKeys && GameControls.ActionsMenu.WasPressedThisFrame()) OnActionsButton();
            else if (menuKeys && GameControls.Wingmen.WasPressedThisFrame()) OpenMenuEntry(Navigation.Kind.Wingmen);
            else if (menuKeys && GameControls.KhadorDrive.WasPressedThisFrame()) OpenMenuEntry(nav != null && nav.MenuEntries(true).Exists(t => t.kind == Navigation.Kind.KhadorDrive) ? Navigation.Kind.KhadorDrive : Navigation.Kind.Slipstream);
            bool freeLookNow = level != null && level.FreeLook != null && level.FreeLook.FreeLookActive;
            if (GameControls.MouseSteering.WasPressedThisFrame()
                && !(freeLookNow && GameControls.MouseSteering.activeControl?.device is Mouse))
            {
                Settings.MouseSteering = !Settings.MouseSteering;
                miningView?.ShowMessage(Localization.Extra("mouseSteering", "Mouse steering") + ": "
                                        + (Settings.MouseSteering ? Localization.Extra("on", "On") : Localization.Extra("off", "Off")));
            }
            // Remake: the Toggle HUD key (F1) steps the "Flight HUD" option Full -> Minimal -> Off; the messages stay in every mode.
            if (GameControls.HudToggle.WasPressedThisFrame())
            {
                Settings.HudMode = (Settings.HudMode + 1) % 3;
                miningView?.ShowMessage(Localization.Extra("hudMode", "Flight HUD") + ": " + Settings.HudMode switch
                {
                    Settings.HudMinimal => Localization.Extra("hudMinimal", "Minimal"),
                    Settings.HudOff => Localization.Extra("hudOff", "Off"),
                    _ => Localization.Extra("hudFull", "Full"),
                });
            }

            if (ship == null)
            {
                level = FindAnyObjectByType<SpaceLevel>();
                ship = level != null ? level.Player : null;
                if (ship == null) return;
                weapons = level.Weapons;
                if (weapons != null) weapons.mouseOverControls = MouseOverControls;
                mining = level.Mining;
                if (mining != null) mining.Message += OnKindMessage;
                docking = level.Docking;
                if (docking != null) docking.Message += OnMiningMessage;
                if (level.GasClouds != null) level.GasClouds.Message += OnKindMessage;
                nav = level.Navigation;
                if (nav != null) nav.Message += OnKindMessage;
                jump = level.SystemJump;
                if (jump != null) jump.Message += OnMiningMessage;
                health = level.Health;
                radar = level.Radar;
                traffic = level.Traffic;
                if (traffic != null) traffic.Message += OnMiningMessage;
                if (traffic != null) traffic.ChoiceRequested += OnChoiceRequested;
                if (level.Hints != null) level.Hints.HintRequested += text => OnChoiceRequested(text, null, null, null, null);
                if (level.Hints != null) level.Hints.Message += text => OnCombatMessage(text, 1);
                if (level.FreeLook != null) level.FreeLook.Message += OnMiningMessage;
                // Layout::showMissionRewardMessage(reward, bounty): "Bounty collected" (3206) and the credits, sound 36.
                if (traffic != null) traffic.BountyCollected += reward =>
                {
                    miningView?.ShowMessage($"{Localization.Get(3206)}  +{ItemInfo.Credits(reward)}", 2);
                    var ca = CombatAssets.Load();
                    if (ca != null && ca.missionAccomplished != null) Sfx.PlayAt(ca.missionAccomplished, Camera.main != null ? Camera.main.transform.position : Vector3.zero);
                };
                if (radar != null) radar.Message += OnRadarMessage;
                if (health != null) health.GameOverStarted += OnGameOver;
                story = level.StorySpace;
                if (story != null)
                {
                    story.DialogueRequested += (pages, closed) => { touch?.ReleaseAll(); weapons?.SetPrimaryHeld(false); storyDialogue.Show(pages, closed); };
                    story.MessageRequested += (text, speaker, closed) => { touch?.ReleaseAll(); weapons?.SetPrimaryHeld(false); storyDialogue.ShowMessage(text, speaker, closed); };
                }
                freelance = level.FreelanceOrbit;
                if (freelance != null)
                {
                    freelance.MessageRequested += (text, name, portrait, voice, closed) => { touch?.ReleaseAll(); weapons?.SetPrimaryHeld(false); storyDialogue.ShowAgentMessage(text, name, portrait, closed, voice); };
                    freelance.RewardMessage += OnMiningMessage;
                }
                if (weapons != null) weapons.Hit += () => hitFlashMs = 200f;
                // #48 and a player's report: the turrets' hits too, aimed in the turret view or firing by themselves: Level::createGun makes
                // every player gun a player gun (setPlayerGun, the turret items' case 0x23 too), and Gun::calcCharacterCollision
                // sets Level+0x30 for any player gun's hit on a ship.
                if (level.Turrets != null)
                    foreach (var tr in level.Turrets)
                        if (tr != null) tr.Hit += () => hitFlashMs = 200f;
                if (level.Turret != null) level.Turret.Message += OnMiningMessage;   // HUD event 0x20 / 0x21 (auto fire on / off)
                if (health != null) health.Message += OnMiningMessage;             // injector / gamma messages
                if (level.Cloak != null) level.Cloak.Message += OnMiningMessage;   // cells paid, "Cloak ready", 583
                chase = Camera.main != null ? Camera.main.GetComponent<ChaseCamera>() : null;
                ApplyInputMode();
            }

            if (health != null && health.Dead)
            {
                // Dead: no HUD, no controls (PlayerEgo::explode); then the game-over screen.
                root.EnableInClassList("hud-cinematic", true);
                dockPrompt.EnableInClassList("dock-prompt--hidden", true);
                // The transfer counter sits on the root, outside what hud-cinematic hides (#131: "9 / 10 unloaded" stayed up through
                // 92's Specter cutscene, which starts the frame the last evacuee is unloaded; Hud::draw doesn't run then).
                transferCounter?.EnableInClassList("transfer-counter--hidden", true);
                ship.SetSteer(Vector2.zero);
                UpdateGameOver();
                return;
            }

            // The launch / arrival camera: no HUD but the orbit information until it ends or is skipped (MGame+0x5f).
            root.EnableInClassList("hud-launch", !level.LaunchCameraOver);
            if (level.Cutscene)
            {
                // A LevelScript cutscene (MGame+0x5f): no HUD, no controls; the radio and fades still show.
                root.EnableInClassList("hud-cinematic", true);
                dockPrompt.EnableInClassList("dock-prompt--hidden", true);
                // The transfer counter sits on the root, outside what hud-cinematic hides (#131: "9 / 10 unloaded" stayed up through
                // 92's Specter cutscene, which starts the frame the last evacuee is unloaded; Hud::draw doesn't run then).
                transferCounter?.EnableInClassList("transfer-counter--hidden", true);
                ship.SetSteer(Vector2.zero);
                weapons?.SetPrimaryHeld(false);
                combatView.Update(radar, traffic, health, Camera.main, true, false);
                UpdateRadio();
                UpdateFade();
                return;
            }

            // The action prompt: navigation (autopilot / jump) first, then mining (lock / approach / minigame), else docking.
            string prompt = docking != null ? docking.PromptText : null;
            if (prompt == null && DockBeforeLock) prompt = Localization.Extra("hudDock", "DOCK");
            if (prompt == null && nav != null) prompt = nav.PromptText;
            if (prompt == null && mining != null) prompt = mining.PromptText;
            if (prompt == null && level.CanDock) prompt = Localization.Extra("hudDock", "DOCK");
            // Touch: no prompt, the fire button is the action button (its arrow shows when fire acts).
            dockPrompt.EnableInClassList("dock-prompt--hidden", prompt == null || InputMode.Current == InputKind.Touch || cursorMode);   // touch / cursor: the arrow on fire
            if (prompt != null) dockLabel.text = prompt;
            // A session: Enter opens the chat (ChatView), not the prompt (F, the controller's X still act).
            if (prompt != null && GameControls.Action.WasPressedThisFrame() && !(GoF2Remake.Multiplayer.NetGame.Active && ActionByEnter()))
            {
                Interact();
                if (level == null || !level.isActiveAndEnabled) return;
            }

            var phase = mining != null ? mining.State : Mining.Phase.Idle;
            bool autopilot = nav != null && nav.Autopilot;
            if (phase != lastPhase || autopilot != lastAutopilot) { lastPhase = phase; lastAutopilot = autopilot; BuildHints(InputMode.Current); }
            // The turret: the touch button (turret view / auto-fire) and the hints of the turret view.
            var turret = level != null ? level.Turret : null;
            var autoT = FirstAuto();
            bool tv = turret != null && turret.InTurretView, ta = autoT != null ? autoT.AutoEnabled : turret != null && turret.AutoEnabled;
            if (tv != lastTurretView || ta != lastTurretAuto) { lastTurretView = tv; lastTurretAuto = ta; BuildHints(InputMode.Current); }
            // Remake: Unlock target (U / B, a tap on the lock plate), its hint while a ship is locked.
            if (radar != null && !Navigation.PressesBlocked && GameControls.UnlockTarget.WasPressedThisFrame()) UnlockTarget();
            bool shipLocked = radar != null && radar.Locked != null;
            if (shipLocked != lastShipLocked) { lastShipLocked = shipLocked; BuildHints(InputMode.Current); }
            lockPlate.pickingMode = shipLocked && InputMode.Current == InputKind.Touch ? PickingMode.Position : PickingMode.Ignore;
            float hudShown = level != null ? level.HudShown : 1f;
            root.EnableInClassList("hud-cinematic", (nav != null && nav.Jumping) || (jump != null && jump.Cinematic) || hudShown <= 0f);   // jumps: no HUD
            if (hudShown < 1f != hudFading || hudFading) { hudFading = hudShown < 1f; safeArea.style.opacity = hudFading ? hudShown : StyleKeyword.Null; }
            // The Khador Drive's charge bar, shared with the cloak's "Cloak charging" (317, Hud::draw 0x1933f6).
            var cloak = level != null && level.Cloak != null ? level.Cloak.Rules : null;
            bool jumpCharging = jump != null && jump.Charging;
            bool cloakCharging = cloak != null && cloak.State == Cloak.Phase.Charging && !jumpCharging;
            // Remake: a cruise drive's charge too (NavigationCruiseDrive), "<item> charging".
            var drive = nav != null ? nav.Drive : null;
            bool driveCharging = drive != null && drive.Charging && !jumpCharging && !cloakCharging;
            jumpCharge.EnableInClassList("jump-charge--shown", jumpCharging || cloakCharging || driveCharging);
            if (cloakCharging != lastCloakCharging || driveCharging != lastDriveCharging)
            {
                lastCloakCharging = cloakCharging;
                lastDriveCharging = driveCharging;
                jumpChargeLabel.text = (driveCharging ? string.Format(Localization.Extra("hudCruiseDriveCharging", "{0} charging"), GoF2Remake.Data.GameNames.Item(drive.item))
                                        : cloakCharging ? Localization.Get(317) : Localization.Get(318)).ToUpperInvariant();
            }
            // Hud::draw: half-width min(1, rate x 1.05) x 194 either side of the centre, the stripes' middle first.
            float chargeRate = jumpCharging ? jump.ChargeRate : cloakCharging ? cloak.ChargeRate : driveCharging ? drive.ChargeRate : -1f;
            if (chargeRate >= 0f)
            {
                float half = Mathf.Min(1f, chargeRate * 1.05f) * 194f;
                jumpChargeFill.style.left = 204f - half;
                jumpChargeFill.style.width = 2f * half;
                jumpChargeStripes.style.left = half - 194f;
            }
            // The time extender: the fast-forward slot's clock (touch) while it isn't fast-forward.
            if (navView.ConsumeExtenderTap()) level?.Extender?.Toggle();
            UpdateOrbitInfo();
            // Fast-forward: the touch button, or hold Tab (the PC version's "Speed up") / controller Y (MGame key 0x100,
            // hold-to-use; rebindable).
            bool ffHeld = navView.FastForwardPressed || GameControls.FastForward.IsPressed();
            nav?.SetFastForwardHeld(ffHeld);
            root.EnableInClassList("hud-docking", phase != Mining.Phase.Idle);
            root.EnableInClassList("hud-mining", phase == Mining.Phase.Mining);
            // Touch: the stick, its value squared per axis like Hud::getAnalogX / Y; with tilt steering chosen the
            // accelerometer instead (MGame::handleAccelerometer, not at campaign 48), the stick shown but idle.
            bool tilt = InputMode.Current == InputKind.Touch && TiltSteering.Active && (Session.FreePlay || Session.CampaignMission != 48);
            root.EnableInClassList("hud-tilt", tilt);
            ship.tiltMode = tilt;
            bool touchMode = InputMode.Current == InputKind.Touch;
            var raw = touchMode && touch != null ? touch.Stick : Vector2.zero;
            var touchStick = tilt ? TiltSteering.Steer() : new Vector2(Mathf.Sign(raw.x) * raw.x * raw.x, Mathf.Sign(raw.y) * raw.y * raw.y);
            if (Vr.VrControls.Steer.HasValue) touchStick = Vr.VrControls.Steer.Value;   // VR: the cockpit's stick, held
            ship.SetSteer(touchStick);
            mining?.SetTouchInput(touchStick);

            if ((touchMode || cursorMode) && touch != null) weapons?.SetPrimaryHeld(touch.FireHeld);   // fire held or autofire latched

            // Weapons: crosshair and hit flash (the secondary's name is the plate at the bottom centre, TouchControls).
            UpdateCrosshair();
            miningView.UpdateLock(mining, crosshair.style.left, crosshair.style.top, !crosshair.ClassListContains("crosshair--hidden") && phase == Mining.Phase.Idle);
            miningView.UpdateGame(mining, Time.deltaTime * 1000f);
            hackingView?.Update(docking);
            // Remake (CapitalShips): docked at the carrier, its resupply window opens (closing it undocks).
            bool atCarrier = docking != null && docking.IsDocked && docking.Target != null && docking.Target.DockingType == ObjectDocking.Resupply;
            if (atCarrier && !resupplyShown && nav != null && !nav.MenuOpen) { resupplyShown = true; OpenCarrierShop(); }
            else if (docking == null || !docking.Busy) resupplyShown = false;
            if (transferCounter != null)
            {
                bool on = docking != null && docking.TransferLabel != null;
                transferCounter.EnableInClassList("transfer-counter--hidden", !on);
                if (on) transferCounter.text = $"{docking.TransferLabel.ToUpperInvariant()}  {docking.TransferDone} / {docking.TransferTotal}";
            }
            lockRingElement.style.left = crosshair.style.left;
            lockRingElement.style.top = crosshair.style.top;
            navView.Update(nav, Camera.main, touchNavShown, phase,
                           level.Layout.alienOrbit ? Standing.Void : level.Layout.raceId, level.SystemJumpgateStation, level.StationInfo != null ? level.StationInfo.techLevel : 0);
            bool cinematic = (nav != null && nav.Jumping) || (jump != null && jump.Cinematic);
            // Radar::drawCurrentLock's order: an asteroid, then a ship lock, then a landmark (the ship's plate goes over it).
            bool plateFree = mining == null || (mining.State == Mining.Phase.Idle && mining.Locked == null);
            // Radar::draw isn't called while the launch / arrival camera runs: no ship markers (their layer sets its display
            // inline, which the .hud-launch rule can't override).
            combatView.Update(radar, traffic, health, Camera.main, cinematic, plateFree, !level.LaunchCameraOver);
            missileWarning?.Update(level.Player != null ? level.Player.transform : null, Camera.main,
                                   !cinematic && level.LaunchCameraOver && health != null && !health.Dead,
                                   level.Player != null && level.Player.Model != null && level.Player.Model.HasBooster, Time.deltaTime * 1000f);
            cooldownView?.Update(level.Database, level.Player, level.Cloak, level.Extender, !cinematic && level.LaunchCameraOver && InputMode.Current != InputKind.Touch);
            // The Ultrascan's class-A letters: Radar::draw too, so not during the launch camera or a cinematic. The range is the
            // level's asteroid field (Level+0xc4), not the autopilot's "Asteroid field" entry: the Void's crystal field has none
            // (players' report: no letters there).
            miningView.UpdateMarkers(mining, level.Asteroids != null && level.Layout != null ? OrbitLayout.ToUnity(level.Layout.asteroidCentre) : (Vector3?)null, Camera.main,
                                     level.LaunchCameraOver && !cinematic && !(docking != null && docking.Busy), navMarkers);
            UpdateRadio();
            PlaceDockPrompt();
            UpdateFade();
        }

        // ---- autopilot menu (Hud::initHudMenu(3)) --------------------------------------------------------------

        bool resupplyShown;
        CarrierShopWindow carrierShop;

        AudioSource shopAmbience;

        /// <summary>Modified: the mouse moved last in the autopilot / actions menu (HighlightMenu); the entry under it.</summary>
        bool menuPointer;
        VisualElement menuHover;

        readonly System.Collections.Generic.List<System.Action> menuActions = new System.Collections.Generic.List<System.Action>();

        /// <summary>Hud::draw's top readout: the time limit (Junk removal, a campaign level's LevelScript+0), else the cargo
        /// hold, the volatile bar and the mission counters (HudReadout).</summary>
        HudReadout readout;
        ChallengeView challengeView;
        VolatileCargo volatileCargo;

        // ---- game over (MGame game-over state) ------------------------------------------------------------

        bool hardcoreDead;

        int hudLook = -1;

        /// <summary>The HUD's panels scaled from the corner or edge they hang on (Options > Gameplay "HUD size"; the ship
        /// markers, the radar ellipse and the touch controls keep their sizes: they sit on what they mark, or have their
        /// own options).</summary>
        static readonly (string cls, float x, float y)[] HudScaled =
        {
            ("status-panel", 0f, 0f), ("speed-panel", 0f, 0f), ("orbit-info", 0f, 0f), ("hud-readout", 100f, 0f),
            ("lock-plate", 50f, 0f), ("hud-messages", 50f, 0f), ("jump-charge", 50f, 0f), ("missile-warning", 50f, 0f),
            ("hints", 0f, 100f), ("crosshair", 50f, 50f), ("lock-ring", 50f, 50f),
        };

        /// <summary>The markings Options > Gameplay "HUD opacity" fades (not the messages, the radio, the menus, the action
        /// prompt or the warnings; the radar ellipse through its quarters, which keep its own 0.6, and not the crosshair,
        /// whose rules fade it themselves).</summary>
        static readonly string[] HudFaded = { "status-panel", "speed-panel", "hud-readout", "lock-plate", "hints", "nav-markers", "radar-quarter", "lock-ring" };

        /// <summary>MGame::OnSuspend 0x1b1000: the app goes to the background -> the options are saved, the sounds pause and
        /// the pause menu opens (a running game never continues unseen). Not while the Editor / a desktop build keeps running
        /// in the background.</summary>
        void OnApplicationPause(bool paused)
        {
            if (!paused) return;
            PlayerPrefs.Save();
            if (Application.isMobilePlatform || !Application.runInBackground) OpenPause();
        }

        void OnApplicationFocus(bool focused)
        {
            if (!focused && Application.isMobilePlatform) OnApplicationPause(true);
        }

        bool hudHiddenDocked;

            // ---- button sounds (TouchButton::OnTouchBegin 124 Button_Push / OnTouchEnd 123 Button_Release) -------------

        AudioSource uiSource;

        /// <summary>Modified: a click sounds as in the main menu: only the release, on the click (a pointer press's push is left
        /// out, but in the carrier's shop window), once per frame.</summary>
        int pressFrame = -1, releaseFrame = -1;

    }
}
