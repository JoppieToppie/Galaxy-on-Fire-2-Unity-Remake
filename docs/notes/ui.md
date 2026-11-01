# UI

Notes moved from `CLAUDE.md` (its index lists every notes file). Code comments citing `CLAUDE.md "<section>"` mean the section or bullet of that name here.

## UI and platforms

- **Startup splash** (2026-10-28, `StartupSplash` in `Scenes/Splash.unity`, build index 0; Unity's own splash screen is
  off): FISHLABS, then Made with Unity, one after the other (0.5 s fade in, 2 s held under a slow zoom, 0.5 s fade out;
  a key or button skips to the next). The menu theme starts with it (`SplashMusic`, see "Sound"), the mods start loading
  (`ModLoading.Preload`) and `MainMenu` loads behind it. The menu builds its backdrop orbit as it wakes
  (`MenuBackground.Awake`: 2-6 s without a frame in the Editor), which after the logos was seconds of black: the menu
  scene now comes in while a logo is held (`allowSceneActivation` once progress is 0.9; the frozen frame shows the logo,
  the music plays on), the splash object stays over it (DontDestroyOnLoad, panel sorted +50) and `MainMenu.Run` waits for
  `StartupSplash.Running` before the title. A load started in the first scene's `Awake` ignored allowSceneActivation =
  false (the menu replaced the splash before its first frame), so it starts one frame in. Straight to the menu for
  -mpjoin / -mphost and a VR headset. Play mode started in MainMenu (the Editor) shows the same logos in the menu itself
  (`editorSplashLogos`, once a run: `StartupSplash.LogosShown`). The main menu is loaded by name everywhere
  (`LoadScene(0)` is the splash now); `StoryStepLabel` tells the game scenes by name.
- **Small key hints** (#175; review 2026-10-28): the flight's hint row (`FlightHud.uss .hints`), the station's own
  screens (`StationMenu.uss .station-root .hint`: the main view's row, the hangar window, the Missions / Status prompt
  rows) and the star map's footer (`StarMap.uss .map-hints`) show their labels at `--text-caption` and the keys and
  controller buttons on the `--glyph-sm-*` tokens (GoF2Common.uss: 22 units keycaps and face buttons at 100 %, scaled
  like the caption by each `ui-text-N`, 44 at 200 %; fixed px clipped the 32 px caption at "Text size" 200 %). The
  station's rules are three classes deep under `.station-root`, not under the panel root's id `#stationPanel`: an id beat
  every class variant, and the windows built on the panel root outside it (the "i" windows, the Mod Builder) keep the
  shared sheets' sizes.
- **Layout settle** (`UiLayout`, #175): a screen configured within 1.2 s of a scene load is checked 0.3 s and 1.2 s in;
  when its panel is drawn at another size than `Configure` asked for (the Editor's Play mode kept the last session's
  scale), `UiLayout.Revision` changes and every screen lays out again. It changed at both points after every scene load
  before, and each time the station refilled its news ticker from the start (`StationMenu.UpdateLayout`).
- **Mouse drags and focus** (2026-10-26): `UiPointerButtons` combines UI move button flags with the Input System's held
  mouse buttons. A zero UI button mask alone must not cancel a held mouse drag; preserve event flags too, since the
  EventSystem delivers movement before release on the release frame (discarding it loses deliberate flings). Include
  `wasReleasedThisFrame` too when that last move has an empty mask. Only mouse pointer 0 reads `Mouse.current`;
  touch/pen use their own event state. Shared by the map, station ship/ticker, lists, scrollbars and builder viewports.
  Primary drags cannot be kept alive by right/middle buttons. Clear IDs before releasing capture; cancel map/turntable
  drags on capture loss, detach, disable or focus loss. `DragScroll` leaves sliders/text selection their own gestures.
  `UiHover` preserves edits/captured drags; stationary-pointer events cannot take keyboard navigation back. Tests:
  `MouseGestureTests` and `MouseDragInputTests` (real Input System/EventSystem/UI Toolkit path, with empty move button
  masks injected and normal release/fling checks). The build-only physical-mouse trigger was not automatically
  reproduced; verify it in the affected rebuilt game. `MouseDragInputTests` (Play mode, input to the Game view with the
  focus ignored) switches the real mouse / pen / touch screen off for each test (`InputSystem.DisableDevice`): pressing
  the physical mouse took `Mouse.current`. Not `InputTestFixture`: it replaces the input runtime under the loaded
  scene. The edit-mode `MouseGestureTests` run synchronously: no input update between their injected state and the read.
- **UI Toolkit only** (runtime): shared theme `UI/GoF2Theme.tss`; per-screen UXML/USS; controllers in
  `Scripts/Runtime/App/UI` (`GoF2Remake.UI`). The theme doesn't include Unity's default runtime theme, so built-in
  controls are styled in `GoF2Common.uss` and the document root is stretched in code.
- **UI design document: `docs/UI-DESIGN.md`** (2026-10-22 rework after the published game UI guidelines: XAG, Steam Deck,
  Material / Apple): the tokens, the type scale (every font-size a `var(--text-*)`, 16..64, nothing under the caption;
  code uses `UiLayout.Type`), the scaling model, layout, contrast, focus, navigation and button prompts, and how to check
  a screen at ten sizes (`UiShots`, GoF2 > Tools > UI Screenshots). Follow it for any UI change.
- **Scaling** (`UiLayout`, Space/UI): every panel is laid out in units of the 1920 x 1080 design and scaled by S = the
  device class (desktop 1, handheld 1.25 (Steam Deck), TV 1.3 (Xbox, `UwpDisplay` sets `TvDetected`), phone 1.2 (1.5
  until 2026-10-23: too big on a 20:9 phone, the
  main menu's rows ran off it; on phones its rows are centred between the logo and the version line and touch,
  `.layout-phone .main-buttons`)), at least
  enough for the 16-unit caption to take 11 real pixels, x Options > Graphics "Interface size" (80-150 %); the panel shows
  1080 / S units of height but never under 1280 of width (`PanelScreenMatchMode.Expand`: 1:1 / 4:3 shrink instead of
  clipping). Root classes `layout-narrow` / `-square` / `-ultrawide` / `-compact` (< 1800 units wide: the main menu hides
  its column while a panel is open) / `-phone` / `-handheld` / `-tv`, `ui-large`. The screens call `UiLayout.Configure` (the
  overlays `UiLayout.Keep`) when the size or `UiLayout.Revision` changes. PlayerPrefs `debug_ui_device` forces a device class.
  A TV leaves the main menu's panels ~690 units of height, a handheld ~800 (2026-11-01, MainMenu.uss): the campaign panel
  there has smaller cards, New Game+ beside the Supernova Challenge (`.campaign-choices`) and, everywhere, up to the whole
  height above the key hints (`#campaignPanel`, its content past 80 % with New Game+); the Mods browser's picture shrinks
  to nothing on a TV so Delete stays above the footer.
  The safe area (`SafeArea.Pixels` = `UiLayout.OptionInsets`) adds Options > Graphics "Screen edge margin" (automatic: 5 %
  on a TV) and "Interface width" (16:9 / 21:9 by default / full: side bands on wider screens) to the device's cutouts.
  Button prompts are visible (no "i" popups any more): `MenuKeys.Bar` rows in menus and windows, the hint rows of the HUD,
  hangar, star map and orbit views, B / A glyphs inside Back and dialog buttons (`InputGlyph.PadPrompt`, text through
  `InputGlyph.SetText`).
- **Accessibility** (Options' own tab, `OptionPage.Accessibility`, `Settings.AccessibilityKeys`): Interface size, Text
  size 100-200 % (root classes `ui-text-N` redefine the `--text-*` / `--glyph-*` tokens; `ui-root` re-applies the body size
  under them), High contrast (`ui-high-contrast`), Reduce motion (`ui-reduce-motion`, the station's / Mod Builder's
  menu animations), Colour-blind markers (friends blue, `CombatView.MarkerTex`). **Interface font** (2026-10-24, the
  user's request; `UiFont`, Space/UI; `Settings.UiFont`: "" Klavika, "file:<name>" a file imported into
  persistentDataPath/Fonts, "system:<family> - <style>" an installed font, `FontEngine.GetSystemFontNames`, each
  family's Regular listed): the row, "Import font file…" (`ModImport.Pick`, checked with `FontEngine.LoadFontFace`,
  copied in and chosen) and "Remove this font file" (a file the font engine holds open is deleted at the next start,
  `Fonts/remove-at-start.txt`); `UiLayout.Configure` puts it inline on every panel's root and top element
  (`UiFont.Apply`, `UiFont.Revision` in `UiLayout.Revision`), so the theme names a font only on `:root` and
  `.gof-semibold` is just `-unity-font-style: bold` (a font set in USS would beat the choice). An imported file needs a
  Font object (`new Font(path)` loads the file): `FontAsset.CreateFontAsset(path, ...)` makes an asset the text
  generator refuses ("FontAsset is invalid", every text blank), and DynamicOS looks the family up among the installed
  fonts. Bold / italic: the family's other imported files (the same family, or one named after it plus the style,
  "LDFComicSansBold") or its installed styles, through the asset's weight table, else faux bold; Klavika is the fallback
  (found by name among the loaded assets). Checked in Play mode with Arial and an imported LDF Comic Sans (regular +
  bold); not yet on Android / iOS (their pickers, their system fonts). Symbols neither Klavika nor Inter has show as
  boxes where the system has no fallback (phones): ▾ ✕ ✖ ➔ were replaced by ▼ × × → (2026-10-24, the host card's "More
  options" on a phone); check a new symbol against both fonts. **Controller keyboard** (`PadKeyboard`,
  `Attach(root)` per screen): A on a text field with a controller where there's no system keyboard opens a key grid;
  `GameControls.TextEntryOpen` pauses the menus' keys meanwhile. **Controller walk** (`UiWalk`, Editor): presses on a
  virtual gamepad (McpInput's, which ignores the window's focus while it holds a button) and the focused element after
  each, for checking navigation; the pause menu and the hangar window keep their own selection, check those by
  screenshot. **Buttons** come in three kinds on the `--button-*` tokens (list row, secondary `.gof-button`, primary
  `.gof-button--primary`); padding and margins on the 4-unit grid, single values as `var(--space-*)`. **Keep these
  settings?** (`SettingsConfirm`, `OptionDef.confirm`: display mode, resolution, interface / text size; 15 s, then the
  old values; `GameControls.ModalOpen`). **HUD size / opacity** (Options > Gameplay, `Settings.HudScalePercent` /
  `HudOpacityPercent`, `FlightHud.ApplyHudLook`). Shared pieces in Space/UI: `UiSounds` (focus move / click / back for
  panels without their own), `UiBusy` (the waiting arc), `UiFocus.SkipScrollBars`; `.gof-toast` for notices. Empty lists
  say what to do (no "-BLANK-").
- **Landscape only** (portrait disallowed in Player Settings). Supported from 1:1 to 32:9 and phones (see Scaling
  above), device safe-area insets are applied (`UI.SafeArea`: the notch / Dynamic Island, the home indicator, cutouts;
  see **iOS**). The phone test reads `UnityEngine.Device.Application.isMobilePlatform` (PR #108),
  so the Device Simulator shows the phone layout (`UnityEngine.Application` stays the Editor there); the same in a
  build. Cameras use `Aspect.VerticalFov` (Hor+ on wide screens, never narrower than 16:9); the bars
  `Aspect.VerticalFovKeepWidth` (wider screens keep the 16:9 width and lose a little top and bottom: the bars are only
  modelled as far as the original's camera sees); the hangars are back on Hor+ (`StationLevel.SetLens(..., keepWidth: false)`):
  a new hangar model is being made, so their cut-off edges on 21:9 / 32:9 are left for it.
