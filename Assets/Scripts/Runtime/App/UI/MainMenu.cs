// MainMenu.cs
// Main menu controller (UI Toolkit). Follows the original flow (MTitle -> ModMainMenu -> MenuTouchWindow(0)):
//   1. Splash: FISHLABS then (instead of ABYSS ENGINE) "Made with Unity". These are the Splash scene's (StartupSplash,
//      build index 0), still on top while this scene starts; when Play mode starts in this scene (the Editor) the menu
//      shows the same logos itself.
//   2. Title: the GoF2 logo fades in over the live 3D scene (3.9 s), "press any key" pulses under it.
//   3. Menu: Resume (only with a save), Start new game -> Select Campaign -> difficulty (Easy / Normal / Hard / Extreme: the PC version's four),
//      Load game (save slots, slot 0 = Auto-save), Options (Sound & Graphics, Controls, Language), About, Exit.
// Text comes from the original table (Localization, text IDs in comments). Sounds: Button_Push on focus
// changes, Button_Release on confirm, Message_Info_Screen for dialogs (FMOD events 124 / 123 / 126).
// Modern additions: keyboard/gamepad navigation, animated transitions, bloom/brightness options.

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
    [RequireComponent(typeof(PanelRenderer))]
    [DefaultExecutionOrder(-1000)]   // register for the UI load before PanelRenderer loads the UXML
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public partial class MainMenu : MonoBehaviour
    {
        [Header("Flow")]
        public bool showSplash = true;
        [Tooltip("Fallback scene for Leave() without a scene name.")]
        public string gameScene = "Space";
        /// <summary>The credit line under the menu and on the About page (the heart is the text icons' sprite, the version
        /// the build's date and time, BuildVersion).</summary>
        static string VersionText =>
            "Galaxy on Fire 2 Unity Remake created with <sprite=\"gof2_text_icons\" name=\"heart\"> by JoppieToppie  ·  " + BuildVersion.Text;

        [Tooltip("Editor only: pretend this build version (e.g. 2026.09.29.2200) so the update check runs; empty = no check.")]
        public string editorTestVersion = "";

        /// <summary>The version the update check compares with the newest release (the Editor's only if pretended).</summary>
        string CheckVersion => Application.isEditor ? editorTestVersion : BuildVersion.Text;

        [Header("Editor splash (when Play mode starts here; players show the Splash scene's same logos)")]
        [Tooltip("MTitle image 7001 (FISHLABS), then Made with Unity. Each logo: 1 s fade in, 2 s hold, 1 s fade out.")]
        public Texture2D[] editorSplashLogos;

        [Header("Audio")]
        public AudioSource musicSource;
        public AudioSource sfxSource;
        public AudioClip menuMusic;      // event 145 Space_NoCombat_Void
        public AudioClip buttonPush;     // event 124
        public AudioClip buttonRelease;  // event 123
        public AudioClip infoSound;      // event 126
        // Voice volume preview (remake: the original only previewed FX). Same lines in both voice banks;
        // German voices play with the German voice language (by default the German text, like the original's voice bank switch).
        public AudioSource voiceSource;
        public AudioClip[] voicePreviewEnglish;
        public AudioClip[] voicePreviewGerman;

        [Header("Localization (text_<code>.json)")]
        public string[] languageCodes = { "en" };
        public string[] languageNames = { "English" };
        public TextAsset[] languageTables;

        [Header("Post-processing")]
        [Tooltip("The menu's volume (bloom and brightness are applied to every global volume by Bootstrap).")]
        public Volume postVolume;

        enum MenuState { Splash, Title, Menu, Leaving }

        /// <summary>A panel to open as soon as the menu loads ("campaignPanel": the station menu's Start new game).</summary>
        public static string OpenPanelOnStart { get => Data.MenuRequest.OpenPanelOnStart; set => Data.MenuRequest.OpenPanelOnStart = value; }

        VisualElement root, logo, splash, splashLogo, fade, dialog, mainColumn, mainButtons;
        Label pressAnyKey, versionLabel, hintLabel;
        int escFrame = -10, cancelFrame = -10;
        VisualElement menuPrompts;

        /// <summary>The main menu's prompts: select and confirm, back with a panel open, a value and the tabs on Options.</summary>
        MenuKeys.Rows MenuPromptRows()
        {
            var rows = MenuKeys.Rows.Select | MenuKeys.Rows.Confirm;
            if (openPanel == null) return rows;
            rows |= MenuKeys.Rows.Back;
            if (panels.TryGetValue("optionsPanel", out var options) && openPanel == options) rows |= MenuKeys.Rows.Change | MenuKeys.Rows.Tabs;
            return rows;
        }

        Button resumeButton, newGameButton, coopButton, multiplayerButton, loadButton, optionsButton, modsButton, aboutButton, debugButton, exitButton;
        ModBrowser modBrowser;
        VisualElement updateRow;
        Button updateButton;
        readonly Dictionary<string, VisualElement> panels = new Dictionary<string, VisualElement>();
        VisualElement openPanel;
        readonly List<OptionControl> optionControls = new List<OptionControl>();
        /// <summary>The multiplayer panel is open as the Co-op campaign screen (the main menu's Co-op campaign button,
        /// SetPanelMode): its own title, co-op games only, the co-op host settings.</summary>
        bool coopPanelMode;
        Action dialogYes, dialogNo, dialogAlt;   // dialogAlt: the third button, only for ShowResetChoice
        Func<bool> dialogCheck;   // the Yes button closes the dialog only when this passes (null = always)
        TextField dialogField;
        MenuState screen = MenuState.Splash;
        bool skipRequested;
        Campaign pendingCampaign;
        /// <summary>Remake mods: the mod campaign picked (null: one of the three GoF2 campaigns).</summary>
        Modding.ModCampaigns.Def pendingModCampaign;

        ScrollView campaignCardScroll;

        ScrollView modOptionCards;
        Label modOptionHint;

        IVisualElementScheduledItem pulse;
        IDisposable anyKey;
        PanelSettings runtimePanel;
        PanelRenderer panelRenderer;
        bool started;
        int voicePreviewIndex;
        bool touchMode;   // last input was a finger: no hover styles, no focus highlight (see SetTouchMode)
        VisualElement safeArea;
        Vector2Int lastScreen;
        Rect lastSafeArea;
        int lastLayout = -1;

        // ---- setup ----------------------------------------------------------------------------

        void OnEnable()
        {
            if (GoF2Remake.Multiplayer.DedicatedServer.ShutOff(gameObject.scene)) return;   // a dedicated server has no menu
            // Per-instance panel settings: scaling is adapted to the screen shape (see UpdateLayout).
            panelRenderer = GetComponent<PanelRenderer>();
            if (!started) touchMode = Application.isMobilePlatform;
            Settings.Changed += ApplySettings;
            InputMode.Changed -= UpdatePressAnyKey;
            InputMode.Changed += UpdatePressAnyKey;
            UpdateCheck.Changed -= RefreshUpdateButton;
            UpdateCheck.Changed += RefreshUpdateButton;
            // PanelRenderer hands out the UI root when it (re)loads the UXML, including live reloads.
            // Register first: assigning the panel settings below reloads the UI.
            panelRenderer.RegisterUIReloadCallback(OnUIReload);
            if (runtimePanel == null && panelRenderer.panelSettings != null)
            {
                runtimePanel = Instantiate(panelRenderer.panelSettings);
                panelRenderer.panelSettings = runtimePanel;
            }
        }

        [Tooltip("Force the phone layout (for testing in the editor).")]
        public bool simulatePhone;

        /// <summary>The loaded UI root (PanelRenderer has no rootVisualElement; set by the reload callback).</summary>
        public VisualElement Root => root;

        /// <summary>Size the panel renders at: the screen, or its target texture when rendering off-screen.</summary>
        Vector2Int ScreenSize()
        {
            var rt = runtimePanel != null ? runtimePanel.targetTexture : null;
            return rt != null ? new Vector2Int(rt.width, rt.height) : new Vector2Int(Screen.width, Screen.height);
        }

#if UNITY_WSA
        // UWP (#20): the Xbox's system keyboard closed with B counts as Cancel, and the text field then puts back the text
        // it had before (the pilot name typed in vanished). The text the keyboard had typed while it was open is kept.
        TextField keyboardField;
        string keyboardText;
        bool keyboardWasOpen;

        void KeepKeyboardText()
        {
            bool open = TouchScreenKeyboard.visible;
            if (open && root.focusController?.focusedElement is TextField f) { keyboardField = f; keyboardText = f.value; }
            else if (!open && keyboardWasOpen && keyboardField != null && keyboardText != null)
            {
                if (keyboardField.value != keyboardText) keyboardField.value = keyboardText;
                // The pilot name prompt: a name typed and the keyboard closed is the answer (#20: only Start kept it, and
                // B left the prompt without the name, asking again and again).
                if (keyboardField == dialogField && dialog.ClassListContains("dialog-backdrop--shown")
                    && GoF2Remake.Multiplayer.NetGame.Clean(keyboardText).Length > 0)
                    root.schedule.Execute(ConfirmDialog);
            }
            keyboardWasOpen = open;
        }
#endif

        void Update()
        {
            if (root == null) return;
            SyncHover();
            // The layout first: the screen and the options change whatever has the keys (a capture, the controller's keyboard).
            if (lastScreen != ScreenSize() || lastSafeArea != Screen.safeArea || lastLayout != UiLayout.Revision) UpdateLayout();
            if (GoF2Remake.Flight.GameControls.BlocksMenus) return;
#if UNITY_WSA
            KeepKeyboardText();
#endif
            DpadTapNavigation.Pump(root);   // D-pad taps the panel's own navigation drops (the Steam controller)
            // Remake: the debug panel (F10, LB + RB or three fingers held for a second, or five taps on the version text).
            if (screen == MenuState.Menu && Keyboard.current != null && Keyboard.current.f10Key.wasPressedThisFrame) OpenDebug();
            EscBack();
            var pad = Gamepad.current;
            if (screen == MenuState.Menu && ((pad != null && pad.leftShoulder.isPressed && pad.rightShoulder.isPressed) || FingersDown() >= 3))
            {
                debugHoldTime += Time.unscaledDeltaTime;
                if (debugHoldTime >= 1f && debugHoldTime - Time.unscaledDeltaTime < 1f) OpenDebug();
            }
            else debugHoldTime = 0f;
            // Remake mods: the controller's X turns the selected mod on or off while the mod browser is open (no dialog over it).
            if (pad != null && pad.buttonWest.wasPressedThisFrame && modBrowser != null && openPanel == modBrowser.Panel
                && !dialog.ClassListContains("dialog-backdrop--shown"))
                modBrowser.ToggleSelected();
            UpdateOptionTabs(pad);
            KeepControllerFocus();
        }

        /// <summary>#74: the panel only sends a controller's navigation to a focused element, and nothing focuses one when the
        /// controller takes over from touch (Android starts in touch mode) or a closed panel took the focus with it: the
        /// D-pad then did nothing until a touch. With the controller in use and nothing focused, the open dialog's Yes, the
        /// open panel's first item or the main column's.</summary>
        void KeepControllerFocus()
        {
            if (screen != MenuState.Menu || root.focusController == null || root.focusController.focusedElement != null) return;
            // Also on a navigation press outside the controller input mode (Android: B arrives as Escape, some pads send the
            // D-pad as arrow keys; Discord report 2026-10-18).
            if (InputMode.Current != InputKind.Gamepad && !MenuFocus.NavPressed(Gamepad.current, Keyboard.current)) return;
            pointerDriven = false;   // Modified: the keys took over (Select marks again)
            if ((focusRetry -= Time.unscaledDeltaTime) > 0f) return;   // a page with nothing to focus: not every frame
            focusRetry = 0.25f;
            SetTouchMode(false);
            if (dialog.ClassListContains("dialog-backdrop--shown")) Select(root.Q<Button>("dialogYes"));
            else FocusFirst(openPanel ?? mainButtons);
        }

        float focusRetry;

        static int FingersDown()
        {
            var ts = Touchscreen.current;
            if (ts == null) return 0;
            int n = 0;
            foreach (var t in ts.touches) if (t.press.isPressed) n++;
            return n;
        }

        // ---- aspect ratios ---------------------------------------------------------------------

        /// <summary>
        /// Landscape only. UiLayout scales the UI (a 1080-unit-high design, larger on phones, handhelds and TVs, never under
        /// 1280 units wide) and sets the USS layout classes; the safe area keeps it clear of cutouts, a TV's edges and,
        /// on an ultrawide screen, inside the interface width (Options > Graphics).
        /// </summary>
        void UpdateLayout()
        {
            lastScreen = ScreenSize();
            lastSafeArea = Screen.safeArea;
            lastLayout = UiLayout.Revision;
            bool offscreen = runtimePanel != null && runtimePanel.targetTexture != null;
            float w = Mathf.Max(1, lastScreen.x), h = Mathf.Max(1, lastScreen.y);
            // The scale and the layout classes (device, aspect, ui-large) by the shared model, docs/UI-DESIGN.md "Scaling".
            UiLayout.Configure(runtimePanel, root, w, h, simulatePhone);
            ApplySafeArea(w, h, offscreen);
        }

        /// <summary>Safe area insets (the notch / Dynamic Island, the home indicator, cutouts) as the safe area's margins,
        /// see SafeArea (its children are all absolutely placed: the padding it had before never moved them).</summary>
        void ApplySafeArea(float w, float h, bool offscreen) => SafeArea.Apply(root, safeArea, w, h, offscreen, SafeArea.Mode.Margins);

        void OnDestroy()
        {
            // The per-scene PanelSettings clone: left alive, every menu entry kept another UI Toolkit panel (its atlas and
            // GPU buffers) for the rest of the run.
            if (runtimePanel != null) Destroy(runtimePanel);
        }

        void OnDisable()
        {
            panelRenderer?.UnregisterUIReloadCallback(OnUIReload);
            Settings.Changed -= ApplySettings;
            InputMode.Changed -= UpdatePressAnyKey;
            UpdateCheck.Changed -= RefreshUpdateButton;
            if (OptionsCatalog.SaveFolderPressed == (Action)ChooseSaveFolder) OptionsCatalog.SaveFolderPressed = null;
            anyKey?.Dispose();
        }

        /// <summary>Modified: Esc goes back as the footer says. The panel only sends its Cancel to a focused element, and after a
        /// click outside the buttons (the mouse) nothing has the focus: Esc did nothing. A press the panel didn't turn into
        /// a Cancel within two frames goes back here (closing an option's open list first).</summary>
        void EscBack()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) escFrame = Time.frameCount;
            if (escFrame < 0 || Time.frameCount < escFrame + 2) return;
            bool handled = cancelFrame >= escFrame;
            escFrame = -10;
            if (handled || screen != MenuState.Menu) return;
            if (ChoicePopup.IsOpen) { ChoicePopup.Close(); return; }
            Back();
        }

        /// <summary>The title prompt follows the input scheme: any key on the keyboard, any button on a controller, a tap
        /// on touch.</summary>
        void UpdatePressAnyKey()
        {
            // The controller focus look (GoF2Common.uss .input-gamepad, like the station and the flight HUD).
            root?.EnableInClassList("input-gamepad", InputMode.Current == InputKind.Gamepad);
            root?.EnableInClassList("input-touch", InputMode.Current == InputKind.Touch);   // the key bindings' × stays shown (no hover)
            if (pressAnyKey == null) return;
            pressAnyKey.text = InputMode.Current switch
            {
                InputKind.Gamepad => Localization.Extra("pressAnyButton", "PRESS ANY BUTTON"),
                InputKind.Touch => Localization.Extra("tapToStart", "TAP TO START"),
                _ => Localization.Extra("pressAnyKey", "PRESS ANY KEY"),
            };
        }

        Button Bind(string name, Action onClick)
        {
            var b = root.Q<Button>(name);
            if (b == null) { Debug.LogWarning($"MainMenu: button '{name}' missing"); return null; }
            b.clicked += () => { Play(buttonRelease); onClick(); };
            HookFocusSound(b);
            return b;
        }

        void HookFocusSound(VisualElement e)
        {
            // Mouse only: a finger sliding over the UI must not drag the selection along with it.
            // Only when the mouse moved: a row that slides under a resting cursor as the list scrolls doesn't take the focus.
            e.RegisterCallback<PointerEnterEvent>(ev => { if (ev.pointerType == PointerType.mouse && InputMode.MouseMovedRecently && UiHover.CanChangeFocus(e, ev.pressedButtons) && e.enabledInHierarchy && e.focusable) e.Focus(); });
            // Modified: with the mouse in use, marked only while the mouse is over it (it stayed marked after the mouse left,
            // and, already focused, made no sound when the mouse came back).
            e.RegisterCallback<PointerLeaveEvent>(ev => { if (ev.pointerType == PointerType.mouse && ev.pressedButtons == 0 && !UiHover.KeepFocus(e) && pointerDriven && InputMode.Current != InputKind.Gamepad && e.focusController?.focusedElement == e) e.Blur(); });
            // Modified: under the mouse only the main column's buttons and the tabs sound (UiHover.MenuTarget); the keys / a
            // controller sound on every move (they clear pointerDriven first: OnNavigate, KeepControllerFocus).
            e.RegisterCallback<FocusInEvent>(_ =>
            {
                if (screen != MenuState.Menu) return;
                if (pointerDriven && !(mainButtons != null && mainButtons.Contains(e)) && UiHover.MenuTarget(e) == null) return;
                Play(buttonPush, 0.45f);
            });
        }

        // Touch has no hover, and a highlight that follows the finger (or stays on whatever was last pressed)
        // looks broken. In touch mode the selection highlight is off: hover styles need the root's can-hover
        // class, presses don't move focus and menus don't pre-select their first item. A mouse or
        // keyboard/controller navigation switches back.
        void SetTouchMode(bool on)
        {
            if (root == null || on == touchMode && root.ClassListContains("can-hover") != on) return;
            touchMode = on;
            SyncHover();
            if (on && root.focusController?.focusedElement is VisualElement focused) focused.Blur();
        }

        /// <summary>Hover highlights only while the mouse is the thing in use (InputMode.MousePointing; see StationMenu).</summary>
        void SyncHover() => root?.EnableInClassList("can-hover", !touchMode && InputMode.MousePointing);

        void OnPointerDown(PointerDownEvent e)
        {
            DragScroll.NotePointer();
            if (e.pointerType == PointerType.mouse) { SetTouchMode(false); pointerDriven = true; return; }
            // Don't focus what the finger presses, except a text field (the address, the code, the debug search): it needs
            // the focus for the on-screen keyboard. Touch mode without the blur there: tapping the focused field again blurred
            // it first, which closed the keyboard.
            if (TextFieldKeys.InTextField(e.target)) { touchMode = true; root.EnableInClassList("can-hover", false); return; }
            SetTouchMode(true);
            root.focusController?.IgnoreEvent(e);
        }

        void Select(VisualElement e)
        {
            if (touchMode) return;
            // Modified: with the mouse in use a panel opens with nothing marked (its first button looked hovered); the keys
            // and a controller still get their selection (KeepControllerFocus, OnNavigate).
            if (pointerDriven && InputMode.Current != InputKind.Gamepad)
            {
                if (root?.focusController?.focusedElement is Button b) b.Blur();
                return;
            }
            e?.Focus();
        }

        /// <summary>Modified: the mouse moved or pressed last; the keys / a controller clear it.</summary>
        bool pointerDriven = true;

        // ---- flow ------------------------------------------------------------------------------

        /// <summary>Remake mods: a mod's menu theme that finished loading takes over from the original (ModMusic).</summary>
        void SwapModMusic()
        {
            if (this == null || musicSource == null || menuMusic == null || musicSource.clip != menuMusic) return;
            var mod = Modding.ModMusic.Replace(menuMusic);
            if (mod == menuMusic) return;
            if (musicSource.isPlaying && musicSource.volume > 0.05f) { StartCoroutine(SwapMusic(mod)); return; }
            musicSource.clip = mod;
            musicSource.Play();
        }

        /// <summary>Modified: the playing theme fades out and 'clip' fades in (a mod's menu theme after the original played
        /// through the splash: it cut in hard). Given up when another fade takes over meanwhile (leaving the menu).</summary>
        IEnumerator SwapMusic(AudioClip clip)
        {
            int mine = musicFadeGeneration + 1;   // the fade below takes this number when it starts
            yield return FadeMusic(0f, 1f);
            if (musicFadeGeneration != mine || musicSource == null || screen == MenuState.Leaving) yield break;
            if (musicSource.clip != menuMusic) yield break;   // already swapped (SwapModMusic and Run both asked)
            musicSource.clip = clip;
            musicSource.Play();
            yield return FadeMusic(Settings.MusicVolume, 1.5f);
        }

        ModLoadingView splashLoading, leaveLoading;

        /// <summary>Remake mods: waits on the loading screen in 'view' while the mods that are on load (at most 'limit' s).</summary>
        IEnumerator WaitForMods(ModLoadingView view, float limit)
        {
            if (!Modding.ModLoading.Busy) yield break;   // (this first check starts every loader)
            if (!Modding.ModLoading.HasWork)
            {
                // Nothing to load (no mods, or only quests): the loaders finish within a frame or two, without the screen.
                for (float t = 0f; t < limit && Modding.ModLoading.Busy; t += Time.unscaledDeltaTime) yield return null;
                yield break;
            }
            view.Show(true);
            for (float t = 0f; t < limit && Modding.ModLoading.Busy; t += Time.unscaledDeltaTime)
            {
                view.Update();
                yield return null;
            }
            view.Update();
            yield return new WaitForSecondsRealtime(0.35f);   // the full bar for a moment
            view.Show(false);
            yield return new WaitForSecondsRealtime(0.3f);
        }

        IEnumerator Run()
        {
            // Whatever the last scene left: never a muted listener here (a pause menu open when a multiplayer session
            // ended loads the menu without closing it), and a finished session's game is gone.
            AudioListener.pause = false;
            GoF2Remake.Multiplayer.NetGame.OnMainMenu();
            // The active mods' models, fx, sounds, skins, portraits and music, built while the menu shows (already loading
            // since the startup splash; again when the mods changed); a replaced menu theme swaps in when it has loaded.
            Modding.ModLoading.Preload();
            Modding.ModMusic.Changed -= SwapModMusic;
            Modding.ModMusic.Changed += SwapModMusic;
            // Modified: the theme already plays from the Splash scene (SplashMusic): its source becomes the
            // menu's music source as it is (no second stream to seek, so no gap or doubling); a mod's theme fades in after it.
            var splashSource = SplashMusic.Take(gameObject.scene);
            bool carried = false;
            if (musicSource != null && menuMusic != null && splashSource != null && splashSource.clip == menuMusic)
            {
                splashSource.outputAudioMixerGroup = musicSource.outputAudioMixerGroup;
                splashSource.ignoreListenerPause = musicSource.ignoreListenerPause;
                Destroy(musicSource);
                musicSource = splashSource;
                carried = true;
                var mod = Modding.ModMusic.Replace(menuMusic);
                if (mod != menuMusic) StartCoroutine(SwapMusic(mod));
            }
            else
            {
                if (splashSource != null) Destroy(splashSource.gameObject);
                if (musicSource != null && menuMusic != null)
                {
                    musicSource.clip = Modding.ModMusic.Replace(menuMusic);
                    musicSource.loop = true;
                    musicSource.volume = 0f;
                    musicSource.Play();      // MTitle starts the menu theme with the first logo
                }
            }
            if (!carried) StartCoroutine(FadeMusic(Settings.MusicVolume, 3f));

            // The station menu's Start new game (MenuTouchWindow mode 2, 28): straight to the menu with that panel open.
            if (OpenPanelOnStart != null && panels.ContainsKey(OpenPanelOnStart))
            {
                string name = OpenPanelOnStart;
                OpenPanelOnStart = null;
                splash.AddToClassList("splash--gone");
                splash.AddToClassList("splash--removed");
                EnterMenu();
                // Out of a multiplayer session (the host left, the connection dropped): the reason, taken now (a -mpjoin
                // client's next try starts a new session at once, which clears the status).
                string reason = null;
                bool popup = name == "multiplayerPanel" && GoF2Remake.Multiplayer.NetGame.TakePopup(out reason);
                // Back where the session was joined: the Co-op campaign panel after a co-op session.
                if (name == "multiplayerPanel") SetPanelMode(GoF2Remake.Multiplayer.NetCoop.LastSessionWasCoop);
                root.schedule.Execute(() =>
                {
                    OpenPanel(name);
                    if (popup) ShowNotice(coopPanelMode ? CoopTitle : Localization.Extra("multiplayer", "Multiplayer"), reason);
                }).ExecuteLater(400);
                if (GoF2Remake.Multiplayer.NetGame.AutoJoinAddress != null) StartCoroutine(AutoJoin(GoF2Remake.Multiplayer.NetGame.AutoJoinAddress));
                yield break;
            }
            OpenPanelOnStart = null;
            // Multiplayer testing (-mpjoin <address>, e.g. a second Windows player next to the Editor's host): straight to the
            // menu, then join.
            if (GoF2Remake.Multiplayer.NetGame.AutoJoinAddress != null || GoF2Remake.Multiplayer.NetGame.TestHost)
            {
                splash.AddToClassList("splash--gone");
                splash.AddToClassList("splash--removed");
                EnterMenu();
                root.schedule.Execute(() => OpenPanel("multiplayerPanel")).ExecuteLater(400);   // its status shows the tries
                if (GoF2Remake.Multiplayer.NetGame.AutoJoinAddress != null) StartCoroutine(AutoJoin(GoF2Remake.Multiplayer.NetGame.AutoJoinAddress));
                else StartCoroutine(TestHostSoon());   // -mphost (development builds)
                yield break;
            }
            WatchAnyKey();
            // The Splash scene's logos are still over this scene (it came in while one was held): the title after them.
            while (StartupSplash.Running) yield return null;
            // The logos only when the Splash scene didn't show them (Play mode started here), once.
            bool splashLogos = showSplash && !StartupSplash.LogosShown && editorSplashLogos != null;
            if (splashLogos)
            {
                StartupSplash.LogosShown = true;
                foreach (var tex in editorSplashLogos)
                {
                    if (tex == null) continue;
                    skipRequested = false;
                    splashLogo.style.backgroundImage = new StyleBackground(tex);
                    splashLogo.AddToClassList("splash-logo--visible");
                    yield return Wait(3f);                        // 1 s fade in + 2 s hold
                    splashLogo.RemoveFromClassList("splash-logo--visible");
                    yield return Wait(1f);                        // 1 s fade out
                }
            }
            // Remake mods: everything they bring loads at once in the background since the menu opened; the black splash
            // shows the loading screen until it is done, then the title comes.
            yield return WaitForMods(splashLoading, 60f);
            splash.AddToClassList("splash--gone");
            screen = MenuState.Title;
            yield return new WaitForSeconds(0.2f);
            logo.AddToClassList("logo--title-visible");
            yield return new WaitForSeconds(splashLogos ? 1.5f : 0.3f);
            splash.AddToClassList("splash--removed");
            pulse = pressAnyKey.schedule.Execute(() => pressAnyKey.ToggleInClassList("press-any-key--on")).Every(1050);
            skipRequested = false;
            while (!skipRequested) yield return null;
            EnterMenu();
        }

        IEnumerator Wait(float seconds)
        {
            for (float t = 0f; t < seconds && !skipRequested; t += Time.unscaledDeltaTime) yield return null;
        }

        void WatchAnyKey()
        {
            anyKey?.Dispose();
            anyKey = InputSystem.onAnyButtonPress.Call(_ => { if (screen == MenuState.Splash || screen == MenuState.Title) skipRequested = true; });
        }

        /// <summary>
        /// The logo lives at its menu spot (top-left); on the title screen it is moved to the centre and enlarged
        /// with translate + scale only, so the move to the menu is a cheap GPU transform animation.
        /// </summary>
        void PlaceTitleLogo()
        {
            if (screen == MenuState.Menu || screen == MenuState.Leaving || logo.parent == null) return;
            var p = logo.parent.layout;
            var el = logo.layout;
            if (el.width <= 0f || el.height <= 0f || p.width <= 0f) return;
            // logo_gof2_remake.png's own proportions (the remake's title logo; #67 redrew it at 953 x 360)
            var logoTex = logo.resolvedStyle.backgroundImage.texture;
            float aspect = logoTex != null && logoTex.height > 0 ? (float)logoTex.width / logoTex.height : 953f / 360f;
            float imgW = Mathf.Min(el.width, el.height * aspect);     // scale-to-fit, left aligned
            var imgCenter = new Vector2(el.x + imgW * 0.5f, el.y + el.height * 0.5f);
            float targetW = Mathf.Min(p.width * 0.56f, p.height * 0.34f * aspect);
            float k = targetW / imgW;
            var target = new Vector2(p.width * 0.5f, p.height * 0.41f);
            logo.style.transformOrigin = new TransformOrigin(Length.Pixels(imgW * 0.5f), Length.Percent(50f));
            logo.style.translate = new Translate(target.x - imgCenter.x, target.y - imgCenter.y);
            logo.style.scale = new Scale(new Vector2(k, k));
            // First placement happens without animation; transitions are enabled afterwards.
            if (logo.ClassListContains("logo--instant"))
                logo.schedule.Execute(() => logo.RemoveFromClassList("logo--instant")).ExecuteLater(50);
        }

        void EnterMenu()
        {
            screen = MenuState.Menu;
            anyKey?.Dispose();
            pulse?.Pause();
            pressAnyKey.AddToClassList("press-any-key--hidden");
            // Back to the logo's own (menu) placement: a transform-only transition, no relayout per frame.
            // Swap classes first and start the move once the styles are resolved again: a transition that starts
            // before that still uses the title fade's 3.9 s duration instead of the 0.9 s of .logo.
            logo.RemoveFromClassList("logo--title-visible");
            logo.AddToClassList("logo--menu");
            IVisualElementScheduledItem move = null;
            move = logo.schedule.Execute(() =>
            {
                foreach (var d in logo.resolvedStyle.transitionDuration)
                    if ((d.unit == TimeUnit.Millisecond ? d.value / 1000f : d.value) > 2f) return;
                logo.style.translate = StyleKeyword.Null;
                logo.style.scale = StyleKeyword.Null;
                move.Pause();
            }).Every(0);
            root.AddToClassList("menu-root--menu");
            mainButtons.AddToClassList("main-buttons--revealing");
            // Remake: is a newer release out (once per run; the button shows when the answer comes, RefreshUpdateButton)?
            UpdateCheck.Start(CheckVersion);
            RefreshUpdateButton();
            root.schedule.Execute(() =>
            {
                foreach (var b in mainButtons.Query<Button>().ToList()) b.RemoveFromClassList("menu-button--hidden");
                FocusFirst(mainButtons);
            }).ExecuteLater(200);
            root.schedule.Execute(() => mainButtons.RemoveFromClassList("main-buttons--revealing")).ExecuteLater(1200);
        }

        // ---- panels ----------------------------------------------------------------------------

        void OpenPanel(string name)
        {
            if (openPanel != null) HidePanel(openPanel);
            var p = panels[name];
            openPanel = p;
            p.AddToClassList("panel--shown");
            p.schedule.Execute(() => p.AddToClassList("panel--visible")).ExecuteLater(16);
            mainColumn.AddToClassList("main-column--dimmed");
            root.AddToClassList("menu-root--panel");
            root.schedule.Execute(() => MenuKeys.Refresh(menuPrompts));
            SetFocusable(mainButtons, false);
            RefreshUpdateButton();
            p.schedule.Execute(() => FocusFirst(p)).ExecuteLater(30);
            if (name == "campaignPanel") { RefreshNgPlus(true); RefreshModCampaigns(); RefreshChallenge(); }
            if (name == "modsPanel") modBrowser?.Open();
            if (name == Modding.ModBuilder.ProjectsPanel) modProjects?.Open();
            if (name == "multiplayerPanel")
            {
                RefreshAddresses();   // the adapters may have changed
                if (serverBrowse == null) serverBrowse = StartCoroutine(BrowseServers());
            }
        }

        void HidePanel(VisualElement p)
        {
            p.RemoveFromClassList("panel--visible");
            p.RemoveFromClassList("panel--shown");
        }

        void Back()
        {
            if (screen != MenuState.Menu) return;
            if (dialog.ClassListContains("dialog-backdrop--shown")) { var no = dialogNo; CloseDialog(); no?.Invoke(); return; }
            if (openPanel == null) return;
            Play(buttonRelease);
            if (openPanel == panels["gameOptionsPanel"]) { OpenPanel("economyPanel"); return; }
            if (openPanel == panels["economyPanel"]) { OpenPanel("difficultyPanel"); return; }
            if (openPanel == panels["difficultyPanel"]) { OpenPanel(pendingStartIndex >= 0 && panels.ContainsKey("debugPanel") ? "debugPanel" : "campaignPanel"); return; }
            // The Mod Browser and the Mod Builder go back to the Mods hub.
            if (panels.TryGetValue(Modding.ModBuilder.ProjectsPanel, out var mbp) && openPanel == mbp)
            {
                if (modProjects != null && modProjects.CancelImport()) return;   // the import list: the projects again
                modProjects?.Close();
            }
            if (panels.ContainsKey("modsHubPanel") && (openPanel == panels["modsPanel"] || openPanel == mbp))
            {
                var from = openPanel == mbp ? modsHubBuilder : modsHubBrowser;
                OpenPanel("modsHubPanel");
                openPanel.schedule.Execute(() => Select(from)).ExecuteLater(60);
                return;
            }
            var closing = openPanel;
            HidePanel(closing);
            openPanel = null;
            mainColumn.RemoveFromClassList("main-column--dimmed");
            root.RemoveFromClassList("menu-root--panel");
            root.schedule.Execute(() => MenuKeys.Refresh(menuPrompts));
            SetFocusable(mainButtons, true);
            RefreshUpdateButton();
            var target = closing == panels["campaignPanel"] || panels.TryGetValue("debugPanel", out var ap) && closing == ap ? newGameButton
                : closing == panels["loadPanel"] ? loadButton
                : closing == panels["optionsPanel"] ? optionsButton
                : closing == panels["multiplayerPanel"] ? (coopPanelMode ? coopButton : multiplayerButton)
                : panels.TryGetValue("modsPanel", out var mp) && closing == mp || panels.TryGetValue("modsHubPanel", out var mh) && closing == mh ? modsButton : aboutButton;
            Select(target);
        }

        float pendingDifficulty = Session.DifficultyNormal;

        Economy pendingEconomy = Economy.Default;

        /// <summary>Remake: the next new game is hardcore (permadeath, Session.Hardcore): off whenever a difficulty is picked.</summary>
        bool hardcoreNext;

        // Remake (GitHub #4, NewGamePlus): with a finished game in the save slots, the campaign panel offers New Game+.
        bool newGamePlus;
        SaveData ngPlusSave;

        /// <summary>Remake (GitHub #8): the original's Kaamo Club expansion (an in-app purchase, texts 78 / 88 / 93) as a new
        /// game's choice: Status::resetGame sets the club's state to 3 (owned) while it is bought, so no siege, no purchase.
        /// Remembered for the next new game (PlayerPrefs "newgame_kaamo").</summary>
        static bool KaamoFromStart
        {
            get => PlayerPrefs.GetInt("newgame_kaamo", 0) == 1;
            set { PlayerPrefs.SetInt("newgame_kaamo", value ? 1 : 0); PlayerPrefs.Save(); }
        }

        // ---- remake: the Mods hub and the Mod Builder ----

        ModBuilderProjects modProjects;
        Button modsHubBrowser, modsHubBuilder;

        // ---- multiplayer (remake-only MVP, NetGame: host or join by address, one shared orbit) ----

        TextField mpAddress, mpName, mpPort;
        Label mpStatus;
        VisualElement mpAddressList, mpLocalBox;
        TextField mpJoinPassword, mpSessionName;

        ScrollView mpServerList;
        Coroutine serverBrowse;
        bool serverQueryRunning;
        string serverListKey = "";
        /// <summary>One per listed row: writes its ping again when a measurement lands (NetPing.Revision), so a new
        /// reading never rebuilds the list under the player's hand.</summary>
        readonly List<Action> serverPingRows = new List<Action>();
        int serverPingRevision = -1;

        // The host card's choice (PlayerPrefs "mp_mode"): online and listed in the server browser, online by join code only,
        // or on the local network by address.
        enum HostMode { Public = 0, Private = 1, Local = 2 }
        static HostMode Mode
        {
            get { int m = PlayerPrefs.GetInt("mp_mode", 0); return m >= 0 && m <= 2 ? (HostMode)m : HostMode.Public; }
            set => PlayerPrefs.SetInt("mp_mode", (int)value);
        }
        const float ServerRefreshSeconds = 5f;

        // ---- debug panel (remake-only testing tools: F10 or five taps on the version text) -------

        int pendingStartIndex = -1;
        ScrollView missionList;
        TextField missionFilter;
        /// <summary>The list's rows with their search text, and each campaign heading with its rows.</summary>
        readonly List<(VisualElement row, string text)> missionRows = new List<(VisualElement, string)>();
        readonly List<(VisualElement heading, List<VisualElement> rows)> missionSections = new List<(VisualElement, List<VisualElement>)>();
        int versionTaps;
        float versionTapTime, debugHoldTime;
        readonly List<OptionControl> debugControls = new List<OptionControl>();

        /// <summary>A hidden panel next to the others: the mission list (a new game from any story step, no intro) on the
        /// left, the cheat toggles on the right.</summary>
        VisualElement debugMissionFocus, debugCheatFocus;   // where the focus was in each Debug column (OnNavigate)

        /// <summary>Resume and Debug both shown (every row, eight): the column moves up so Exit stays clear of the version
        /// text and its tap zone at the bottom left (.main-column--full).</summary>
        void UpdateColumnFit()
        {
            if (mainColumn == null) return;
            bool resume = resumeButton != null && !resumeButton.ClassListContains("menu-button--gone");
            bool debug = debugButton != null && !debugButton.ClassListContains("menu-button--gone");
            mainColumn.EnableInClassList("main-column--full", resume && debug);
        }

        /// <summary>"Update available" (UpdateCheck): in the menu, a newer release out and no panel open (the panels reach
        /// the bottom centre). Keys / controller reach it with Down from the last menu row.</summary>
        void RefreshUpdateButton()
        {
            if (updateRow == null || updateButton == null) return;
            bool shown = UpdateCheck.Available && screen == MenuState.Menu && openPanel == null;
            if (UpdateCheck.Available)
                updateButton.text = string.IsNullOrEmpty(UpdateCheck.LatestTag)
                    ? Localization.Extra("updateAvailable", "Update available").ToUpperInvariant()
                    : $"{Localization.Extra("updateAvailable", "Update available").ToUpperInvariant()}  ·  {UpdateCheck.LatestTag}";
            updateRow.EnableInClassList("update-row--shown", shown);
            updateButton.focusable = shown;
        }

        IEnumerator Leave(string scene = null)
        {
            scene ??= gameScene;
            screen = MenuState.Leaving;
            fade.AddToClassList("fade--on");
            StartCoroutine(FadeMusic(0f, 1.2f));
            yield return new WaitForSeconds(1.3f);
            // The mods (Modding.ModLoading: ship models, music; loading since the menu opened or the mods changed), at most
            // 60 s more, with the loading screen on the black fade.
            yield return WaitForMods(leaveLoading, 60f);
            if (Application.CanStreamedLevelBeLoaded(scene)) SceneManager.LoadScene(scene);
            else
            {
                Debug.LogWarning($"MainMenu: scene '{scene}' is not in the build settings.");
                fade.RemoveFromClassList("fade--on");
                screen = MenuState.Menu;
                StartCoroutine(FadeMusic(Settings.MusicVolume, 1f));
            }
        }

        void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ---- dialog ----------------------------------------------------------------------------

        /// <summary>What had the focus when the dialog opened: closing gives it back (CloseDialog). The panel's first item
        /// got it instead, and in the Mod Browser focusing the top row selects it, so No on Delete moved the selection.</summary>
        VisualElement dialogReturn;

        void ShowDialog(string title, string text, Action onYes)
        {
            if (!dialog.ClassListContains("dialog-backdrop--shown")) dialogReturn = root.focusController?.focusedElement as VisualElement;
            dialogYes = onYes;
            root.Q<Label>("dialogTitle").text = title.ToUpperInvariant();
            root.Q<Label>("dialogText").text = text;
            dialog.AddToClassList("dialog-backdrop--shown");
            Play(infoSound);
            Select(root.Q<Button>("dialogNo"));
        }

        /// <summary>The dialog as a notice: one button (the Yes button as OK), no question.</summary>
        void ShowNotice(string title, string text)
        {
            Debug.Log($"MainMenu: notice '{text}'");
            ShowDialog(title, text, null);
            var no = root.Q<Button>("dialogNo");
            var yes = root.Q<Button>("dialogYes");
            no.style.display = DisplayStyle.None;
            InputGlyph.SetText(yes, "OK");
            Select(yes);
        }

        void ConfirmDialog()
        {
            if (dialogCheck != null && !dialogCheck()) return;
            var a = dialogYes;
            CloseDialog();
            a?.Invoke();
        }

        string currentOptionsPage = "soundPage";

        void CloseDialog()
        {
            var no = root.Q<Button>("dialogNo");
            var alt = root.Q<Button>("dialogAlt");
            if (alt != null && alt.style.display == DisplayStyle.Flex)
            {
                // The reset choice: back to Yes / No.
                alt.style.display = DisplayStyle.None;
                InputGlyph.SetText(no, Localization.Get(135).ToUpperInvariant());
                InputGlyph.SetText(root.Q<Button>("dialogYes"), Localization.Get(134).ToUpperInvariant());
            }
            dialogAlt = null;
            if (no.style.display == DisplayStyle.None || dialogField != null && dialogField.style.display == DisplayStyle.Flex)
            {
                no.style.display = StyleKeyword.Null;
                InputGlyph.SetText(no, Localization.Get(135).ToUpperInvariant());
                InputGlyph.SetText(root.Q<Button>("dialogYes"), Localization.Get(134).ToUpperInvariant());
            }
            if (dialogField != null) dialogField.style.display = StyleKeyword.Null;
            dialog.RemoveFromClassList("dialog-backdrop--shown");
            dialogYes = dialogNo = null;
            dialogCheck = null;
            var back = dialogReturn;
            dialogReturn = null;
            if (back != null && back.panel != null && openPanel != null && openPanel.Contains(back) && back.enabledInHierarchy
                && back.resolvedStyle.display != DisplayStyle.None) Select(back);
            else if (openPanel != null) FocusFirst(openPanel);
            else Select(exitButton);
        }

        // ---- save slots (RecordHandler: 12 slots, slot 0 = Auto-save; SaveGame) ------------

        /// <summary>Modified: a save clicked to load flashes for this long, then fades back while the load waits as long again;
        /// clicks meanwhile are ignored (slotChosen).</summary>
        const long SlotFlashMs = 180;
        bool slotChosen;

        // ---- options ---------------------------------------------------------------------------

        static readonly (string tab, string page)[] OptionPages =
        {
            ("tabSound", "soundPage"), ("tabGraphics", "graphicsPage"), ("tabControls", "controlsPage"), ("tabBindings", "bindingsPage"), ("tabGameplay", "gameplayPage"),
            ("tabAccessibility", "accessibilityPage"), ("tabLanguage", "languagePage"),
        };

        static string PageName(OptionPage p) => p switch
        {
            OptionPage.Sound => "soundPage",
            OptionPage.Graphics => "graphicsPage",
            OptionPage.Controls => "controlsPage",
            OptionPage.Bindings => "bindingsPage",   // remake: the key bindings on their own tab
            OptionPage.Language => "languagePage",   // under the language buttons
            OptionPage.Accessibility => "accessibilityPage",
            _ => "gameplayPage",
        };

        // ---- the original game's saves (OriginalSaveTransfer) ----------------------------------------------------------

        bool pickingOriginal;
        string OriginalImportTitle => Localization.Extra("importOriginalSave", "Import original save");
        string OriginalExportTitle => Localization.Extra("exportOriginalSaves", "Export for original game");

        // ---- text ------------------------------------------------------------------------------

        // ---- navigation ------------------------------------------------------------------------

        VisualElement lastOptionCard, lastOptionToggle;

        // ---- audio -----------------------------------------------------------------------------

        bool fadingMusic;
        int musicFadeGeneration;   // Modified: the newest fade wins (SwapMusic's fade-out against leaving's)

        IEnumerator FadeMusic(float target, float seconds)
        {
            if (musicSource == null) yield break;
            int generation = ++musicFadeGeneration;
            fadingMusic = true;
            float start = musicSource.volume;
            for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
            {
                if (generation != musicFadeGeneration || musicSource == null) yield break;
                musicSource.volume = Mathf.Lerp(start, target, t / seconds);
                yield return null;
            }
            if (generation != musicFadeGeneration || musicSource == null) yield break;
            musicSource.volume = target;
            fadingMusic = false;
        }

        void PlayVoicePreview()
        {
            var clips = Settings.GermanVoices && voicePreviewGerman != null && voicePreviewGerman.Length > 0
                ? voicePreviewGerman : voicePreviewEnglish;
            if (voiceSource == null || clips == null || clips.Length == 0) return;
            voiceSource.Stop();   // one line at a time, a new release restarts it
            voiceSource.clip = GoF2Remake.Modding.ModSounds.Get(clips[voicePreviewIndex++ % clips.Length]);
            voiceSource.volume = Settings.VoiceVolume;
            voiceSource.Play();
        }

        void Play(AudioClip clip, float volume = 1f)
        {
            if (clip != null && sfxSource != null) sfxSource.PlayOneShot(GoF2Remake.Modding.ModSounds.Get(clip), volume);
        }

        // ---- save folder (remake, a player's request; Data.SaveFolder) ------------------------------------
        // The Options > Gameplay row's press (OptionsCatalog.SaveFolderPressed) asks whether to pick another folder or go back
        // to this device only, runs the system's folder picker, and for a folder that already holds saves (or with saves on
        // this device) asks how to start: keep both (the newer save of each slot wins, the other is kept in Saves/Deleted)
        // or only the folder's (this device's set aside in Saves/BeforeFolder). The menu also syncs with the folder on entry
        // and before the Load list, and says once per run when the folder can't be reached.

        static bool saveFolderNoticeShown;
        bool pickingSaveFolder;

        static string SaveFolderTitle => X("saveFolderTitle", "Save folder");

        /// <summary>The dialog with two answers and Cancel (the reset choice's third button).</summary>
        void ShowChoice(string title, string text, string yesLabel, Action onYes, string altLabel, Action onAlt)
        {
            ShowDialog(title, text, onYes);
            dialogAlt = onAlt;
            var yes = root.Q<Button>("dialogYes");
            InputGlyph.SetText(yes, yesLabel.ToUpperInvariant());
            var alt = root.Q<Button>("dialogAlt");
            InputGlyph.SetText(alt, altLabel.ToUpperInvariant());
            alt.style.display = DisplayStyle.Flex;
            InputGlyph.SetText(root.Q<Button>("dialogNo"), Localization.Get(425).ToUpperInvariant());   // Cancel
            Select(yes);
        }

    }
}
