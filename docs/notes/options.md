# Options and localization

Notes moved from `CLAUDE.md` (its index lists every notes file). Code comments citing `CLAUDE.md "<section>"` mean the section or bullet of that name here.

## UI and platforms

- **Cosmetics and Gameplay (final organization)**: Cosmetics has Presentation, UI, and HUD and map.
  Gameplay shows Difficulty beside Gameplay Experience, then expandable Game Rules: World, Progression and economy,
  Combat and NPC behavior, Traversal, and Missions and simulation. Mod traffic/settings belong to Missions; there is
  no separate Mods tab. Tools are always visible; every Debug enable asks Enable/Cancel independently of Expand.
  Nested headings stay cyan and are indented. Interface style and its reset belong to Cosmetics > UI.
  The UI portion of #345 and explicit selection interaction of #346 are included here to avoid competing edits.
  Complete preset values and default-reset policy are delivered separately by #346. Save-bound rules remain a
  proposal under #311; Difficulty is unchanged by reset. Future independently customizable difficulty rules should
  be considered inside Game Rules without changing current save/session ownership.

- **Cosmetics and Gameplay** (issue #311, UI organization only): Cosmetics has Special features (presentation and
  transition animations) and UI; Gameplay has QoL and Game Rules. Time speedup near hostiles, stored ships keeping
  equipment, and autopilot jumpgate confirmations are QoL (defaults: off, off and on respectively). Discord presence
  belongs to Accessibility. Gameplay Experience is always visible under Game Rules; Expand asks for confirmation
  before revealing the custom controls, with Debug tools first.
  Custom Game Rules stay hidden until explicit confirmation; Cancel/Back changes nothing. The same app-session consent
  gate covers legacy main-menu, station and flight Debug entries. Confirmation enables no cheat and applies no rule.
  Existing setting keys, save formats and multiplayer authority are retained. Cosmetics and Gameplay reset only
  their own keys. Cosmetic Experience and Gameplay Experience independently offer Original / Remake / Custom;
  they reuse their scope's values from the existing explicit preset table. Cosmetic Original sorts by ID, Remake
  by type. The legacy welcome screen retains its existing whole-experience API. Custom only reports a mixed state.
  Owner follow-up: bind Kaamo stacking/limit, world content, travel/simulation, informer rules, difficulty and future
  economy/balance profiles to the playthrough. Preserve purchased upgrades during migration. Autocomplete and
  combat/AI need UI separation but are not proposed as mandatory save-bound settings. Stored equipment is QoL.

- **Camera shake** (Graphics, `Settings.CameraShake`): 100 % by default, the ported shakes unscaled (#342 had made 30 % the
  default; back 2026-11-19); the Cosmetic and Remake experiences set 30 %. Saved values are kept; Graphics defaults: 100 %.

- **Camera cutout** (remake, 2026-11-19, the user's request; Graphics below "Screen edge margin", phones only:
  `UnityEngine.Device.Application.isMobilePlatform`, so the Device Simulator shows it; `Settings.ScreenCutout`, key
  `screenCutout`): Keep clear (0, the default: the behaviour since the iOS work, platform behaviour rather than a
  gameplay change, so not in the experience presets) / Both sides (1: left and right both the larger inset; Android insets
  only the camera hole's side, which moved the HUD's left half, the joystick and the FF / autopilot buttons, right) / Full
  screen (2: no device insets at all, the home indicator's included). Applied in `SafeArea.Device` (testable overload
  `Device(Rect, w, h, cutout)`, `ScreenCutoutTests`), which every safe-area user reads (the HUD, touch controls, station,
  main menu, star map, `KeepClear` overlays, FPS / speedrun / story-step labels); in `UiLayout.Revision`, so the screens
  lay out again when it changes. The edge margin and interface width still apply on top.

- **Original by default** (2026-11-19, after an audit of the 46 PRs merged that day): every remake addition starts off and
  the Original presets turn it off. The defaults put back: 3D planets off (`rebuiltPlanets`), camera shake 100 %. Behind new
  switches (default off): Gameplay > Combat "Turrets lead moving targets" (`turretLeadAim`: #354's turret aim, see
  weapons.md "Turrets"), "Remake free look" (`remakeFreeLook`: #336's zoom to 70,000 and #302's lock on the view's middle;
  off: the original's 20,000 and the ship's crosshair), Graphics "Remastered shield and cloak" (`remasteredEffects`: #340's
  PC version shield bubble and half-bent cloak, shader global `_GoF2RemasteredFx`; off: the phone original's). Kept on:
  #340's boost blur (the original's own effect 0x1400002) and its cloak fixes, #300's Kaamo purchase step, #298's keys.
  - The experience presets (`ExperienceKeys`, `Experiences`) gained rebuiltPlanets, cameraShake, remasteredEffects,
    blueprintAutocomplete, hostileAutopilotFastForward, turretLeadAim and remakeFreeLook (Original: off / 100 %; Cosmetic:
    the looks on, shake 30 %; Remake: all on). The Cosmetic Experience covers the Graphics page's experience options too
    (`Settings.InScope`). Migration version 10 (`AddedExperienceKeys`): an install matching a preset gets its values for
    the new options. A fresh install stores both Custom flags off, so its rows read Original, not Custom.

- Text comes from the original table via `Localization.Get(textId)`; remake-only strings via `Extra(key, english)`,
  translated per language in `Resources/GoF2Localization/extra_<lang>.json` (a { key: text } object, loaded with the
  table by `Localization.LoadExtra`; 11 languages, KiritoJPK, PR #54, then Chinese and Hindi; a key it lacks stays
  English: add new keys there when adding Extra texts, to every language, `ExtraTablesShareTheirKeys`; 169 keys of
  the cruise drive, rifts, encounters, devices, hazards and cargo jettison had gone into none and were translated into
  all 13 on 2026-11-13, with text_ja's 594 / 1719 / 1720 the original left in English). **Languages in the menu** (`LanguageTables.Offered`, the one
  list: `MainMenuBuilder.Languages` writes it into the main menu scene's `MainMenu.languageCodes / Names / Tables` for
  its language buttons, and the in-game Options' language row reads it; the station scene and
  `Resources/GoF2LanguageTables` take every `text_*.json`, `LanguageTables.TableFor`). **In a running game** the pause
  and station menus' Language tab has a language row (`OptionsCatalog` "language", `inGameOnly`: the main menu keeps its
  buttons): `SetLanguage` loads the table, `Localization.Changed` fires, and `StationMenu.RefreshTexts` (the header, the
  side menu, Launch's glyphs rebuilt, the system menu, then `HangarWindow` / `MissionsWindow` / `StatusWindow` /
  `LoungePanel.RefreshTexts`) and `FlightHud.RefreshTexts` re-label what they wrote while being built; everything
  written as it is shown already reads the table. A text a document sets once while building must go into its
  RefreshTexts too. "Default settings" keeps the language (`Settings.ResetToDefaults`; the row is in
  `OptionsCatalogTests.NotSettings`): English, German, French, Spanish, Italian, Dutch, Polish, Russian, Portuguese
  (Brazil), Simplified Chinese, Hindi, Korean, Japanese, Arabic (the last three the original's tables, offered since
  2026-11-04: every table is offered; Arabic's ~1250 texts the original left in English, mostly the add-on campaigns'
  dialogue (ids 2466-3228), translated by the remake the same day; then every original Arabic row checked against its
  English (2026-11-05): ~510 corrected (rows shifted by one, e.g. 229-237, 1236-1239, 1817-1820, 2191-2193, 2261-2265;
  older texts, e.g. an older Liberator mission; meanings turned round; key placeholders lost with the mobile controls
  texts; Latin and digits stored reversed, "RMA" for AMR, "00002" for 20000; "hangar" as المعسكر, now المرآب); the language names, credits and the gibberish / Void cipher lines (1838, 1917, 1945,
  1952, 3054...) stay as in every table), and the menus keep their left-to-right layout: TextCore joins the letters and orders the words
  right to left, but a closing full stop lands at the right end, checked in Play mode). The UI fonts (Klavika, Inter) have no CJK, Hangul,
  Arabic or Devanagari glyphs: those come from
  the system's fonts through TextCore's OS font fallback (`TextSettings.fallbackOSFontAssets`), with correct Devanagari
  shaping (verified on Windows; Android has Noto CJK / Devanagari; Linux and the Xbox depend on the fonts installed).
  Settings in `Settings`
  (PlayerPrefs), menu choices in `Session`.
- **Story step label** (remake, `StoryStepLabel`, installed by `Bootstrap` like `FpsCounter`, its own panel; only with
  Options > Gameplay "Show story step", `Settings.ShowStoryStep`, on by default since 2026-10-11 (was off, #40); a
  player who turned it off keeps it off): bottom right in
  every game scene (not the main menu, not in photo mode: `FpsCounter.Suppressed`), small and half-transparent, never
  picking: "Step 23: <StepSummaries title> · <station> #<index>" (the add-ons prefixed with the campaign, "Free play"
  without a story), refreshed twice a second, so a bug report's screenshot shows where the player was.
- **Options** (`Settings`, PlayerPrefs, cached): `OptionsCatalog` lists every option (label, kind, range, get / set) and
  both the main menu's Options panel (tabs Sound / Graphics / Controls / Gameplay / Language, rows built at runtime) and
  the in-game ones build their rows from it (`OptionControl`: Slider, Toggle or `ChoiceRow`; every choice is a drop-down
  box at the row's right that opens a `ChoicePopup` list, the current value ticked, a value too long for the box sliding
  back and forth (`TextMarquee`) — the keys and the controller still step it with left / right, and
  `OptionDef.segmented` is no longer read. Every row has a description (the key binding rows aside; rows built without
  one take theirs from `OptionsCatalog.Help`, test `EveryOptionHasADescription`); it gets a "?" (`OptionInfo`) that
  opens it after 1 s of the mouse resting on it or on a tap (a tap anywhere else closes it; on phones the tap lands in a
  fingertip-sized pad round the icon, `.option-info-pad`, and the list's scroll bar takes touches only right of its
  track: centred, its 56-unit touch strip lay over the icons and took their taps, 2026-10-23); with a controller the
  description line under the row shows instead. A slider's value sits in its own box, with a mute speaker on the Sound
  page. Remake (the user's request, 2026-10-22): the box takes a typed number in its own unit (degrees, %, kelvin,
  metres; `OptionDef.fromShown`, else a straight line through what `format` shows at the range's ends): a click / tap on
  it, Enter / Space on the row with keys and mouse, or a digit / minus / point typed on the selected row opens an edit
  box over it (`OptionControl.BeginEdit`; on the row, not in the slider, which takes a child's focus), Enter or leaving
  it sets the value (clamped), Esc drops it; meanwhile `NetChat.SetTyping` and `GameControls.TextEntryOpen` rest the
  game's and the menus' keys. A row also resets alone: the arrow before a changed slider's value (drawn by Painter2D) or
  Backspace / Delete / the controller's X on the selected row (choices and toggles too, not a choice that asks to be
  kept) puts back its default: `OptionDef.defaultValue`, else Settings' own (`Settings.TryDefault`: the getter read with
  every `Get` answering its fallback; false when it reads no option). The photo panel gives every row its default and a
  hint line under the rows by input. Esc / B close an open list or "?" window before the page). The pause menu's and the
  station system menu's Options page is `OptionsView`, the same panel in code (title + amber accent, the tabs, Back /
  Default settings) with the main menu's classes, shared in `GoF2Common.uss`; Q / E or LB / RB switch tabs, left / right
  on the tab row too. Keys and controller (a player's
report, 2026-10-18; the main menu's panel walked every focusable in it as one list: Down stepped through the tabs, Right ran
on to the next row, LB / RB did nothing): every Options page walks the active tab, its rows and the footer (one row),
stopping at the ends (`MainMenu.NavigateOptions`, `OptionsView.NavItems`); left / right switch tabs on the tab row, step a
slider / choice / binding, flip a toggle, move between the footer's buttons (and the language buttons) and otherwise do
nothing; A on a tab goes into it; A on a choice keeps its value (it cycled on, `ChoiceRow.Cycle` is gone); LB / RB (Q / E)
switch tabs anywhere (`MainMenu.UpdateOptionTabs`, not with both held: the Debug panel), from a page onto the new page's
first row (`FocusPageStart`, a frame later: a page just shown has no layout). The flight pause menu reads the devices
itself and repeats a held direction (`HeldRepeat`: 0.4 s, then every 0.08 s; a press wraps its main list, a held key stops
at the ends; a toggle, the tab row and the Debug grid move once per press). A row can be greyed out and skipped
(`OptionDef.enabled`, `.option-row.unity-disabled`): MSAA while a temporal upscaler does the anti-aliasing, with a note
(Extra `msaaByUpscaler`). The menus give the focus to what the mouse enters only after a real mouse move
(`InputMode.MouseMovedRecently`): a list scrolled by the keys slid its rows under a resting cursor (the report's selection
jumping back to the bottom; the likely cause, not reproduced). Original options: Music / FX / Voice, Brightness
(513-515), the original's **Quality** 504 split into its parts (2026-10-23, the user's request: it doubled the Graphics
preset; a saved `quality` is read for them until each is set, `Settings.OldQuality`): Fire and smoke
(`Settings.FireSmoke`: `ShipSmoke`, `ShipBurn`, `WreckBurn`), Fog (`Settings.Fog`, `Bootstrap.SetSceneFog`), Detail
distance (`Settings.DetailDistance`: LOD bias ×0.35 / ×0.6 / ×1, the original's Low / Medium / High, and Ultra ×2), and
(remake) Texture filtering (`Settings.TextureFiltering`: Bilinear / Anisotropic / Forced / Forced 16×,
`QualitySettings.anisotropicFiltering` and `Texture.SetGlobalAnisotropicFilteringLimits`; the desktop's default Forced,
its quality level's), Sensitivity, Invert (500, remake: split into up / down and left / right, and the mining drill's
own pair), Default settings 497 (remake, players' request: it asks first whether to reset only the tab on show or every
tab, or Cancel: the main menu's dialog with a third button (`ShowResetChoice`), the in-game `OptionsView` in its
footer's place, Esc / B cancels; the keys per tab are `Settings.SoundKeys` / `GraphicsKeys` / `ControlsKeys` /
`GameplayKeys` / `LanguageKeys`, `OptionsCatalog.ResetPage`; a new option's key goes in its tab's list: the old single
list had missed 19 later options). Remake: master volume, window mode + resolution (desktop players only), frame rate,
render scale, upscaler (Off / FSR 1 / STP / DLSS / FSR 2-4 / MetalFX / MetalFX Temporal, only those the device supports:
FSR needs shader model 4.5, STP compute and no GLES, so Vulkan on Android; DLSS / FSR 2-4 see below; the temporal ones
force MSAA off) and its quality (DLSS / FSR 2+: Native = DLAA / native AA, Quality, Balanced, Performance, Ultra
performance; they pick their resolution from it, not the render scale) and MSAA (the URP asset; 0 = the platform's own),
bloom (Off / Remake: URP Bloom on the HDR glow, threshold 1 / Original: `ClassicBloomPass`, see below / Both: the two
together, #45; "Bloom strength" 25..200 %, a player's wish, 2026-10-18: `Settings.BloomStrength`, a factor on each scene
volume's own Bloom intensity in `Bootstrap.ApplyPostProcessing`, shown with Remake and Both, the original's pass is not
scaled), hangar depth of field (see "Station scene"), lens flare, skies (`Settings.SkyStyle`: Original (default) /
Enhanced / EVERSPACE 2 style / Combined, not in the presets, space-scene.md "Skies"), nebula clouds
(`Settings.NebulaClouds`, off by default, not in the presets: space-scene.md "Nebula clouds"), hangar ship shadows (Off / Player ship only /
All ships, see "Station scene"), levels of detail (see "Assembled prefabs"), field of view (chase camera, default the
original's 1.22 rad), camera shake, vibration (see **Haptics**), stick dead zone, launch / arrival camera, hangar
arrival and take-off (`HangarFlight`), auto-play dialogue (the dialogue's AUTO pill), animated dialogue (`TextReveal`),
game runs in conversations (`Settings.PlayOnInConversations`, off by default, see "Dialogue" in story.md),
button hints (Gameplay, `Settings.InputHints`; "Button hints in flight" until 2026-10-24, the user's request to cover
the menus: the flight HUD's, the station's and the star map's hint rows through `InputGlyph.TrackHintsOption`, which
sets `display: none` inline as well as `.hints--off`, since a screen's own rules beat the class (`.hangar-open .station-hints`
kept the hangar window's row); the menus' prompt bars in `MenuKeys.Bar`; Inspect ship's / Action Freeze's in
`OrbitViewUi.BuildHints`, whose tappable hints stay on touch, their only Hide UI / Screenshot buttons), button hint
size (#171, Gameplay, `Settings.HintScalePercent` 50-100 %: the flight HUD's, station's and star map's rows drawn smaller
by a transform around their anchor, `InputGlyph.ScaleHints`, so their neighbours stay put; in flight times the HUD size,
`FlightHud.ApplyHudLook`; the menus' bars and the station windows' rows keep the Interface / Text size), the flight HUD
(Gameplay tab, `Settings.HudMode`, players' request "for maximum immersion": Full / Minimal / Off, also the Toggle HUD
binding, F1, which steps through them with a HUD message; `FlightHud` puts `.hud-minimal` / `.hud-off` on the root,
FlightHud.uss hides by `visibility`, since the markers' layers, the status panel and the readout set their display in
code: Minimal hides the status bars, the radar ellipse, every marker (ships, landmarks, missiles), the lock plate, the
readouts, the hints, the cooldown icons, the hit arcs and, with keys or a controller, the throttle gauge / secondary
plate / weapon strip; Off also the crosshair, lock ring, action prompt, missile warning, charge bar, mouse reticle /
stick circle and the arrival's orbit information; always kept: the HUD messages, the radio, conversations, the menus,
the mining and hacking games, the touch controls (and on touch their secondary plate) and game over; in Minimal a new
lock's plate shows for 4 s and fades (a player's wish, 2026-10-17, for the Informer's / Hijacker's name:
`FlightHud.LateUpdate`, `.lock-plate--peek`; the hull's percentage changing doesn't show it again); the lens flare is
its own option), Show FPS (`FpsCounter`, its own panel over every screen; where, Graphics "FPS counter position",
`Settings.FpsPosition`, shown while Show FPS is on: the four corners and the middles of the four sides, top centre by
default, clear of the safe area; at the top centre the flight HUD's lock plate and messages move under it, `.hud-fps`,
#137), every weapon's own shot sound (`Settings.EachWeaponSound`, Sound tab), the Supernova Challenge's combo sounds
(#165, Sound tab, `Settings.ChallengeComboSounds`, on: SV_Mode_01..10 in `ChallengeOrbit.PlaySound`), the medal sound
(#165, Sound tab, `Settings.MedalSound`: the remake's chime, the original's info blip, or none; the toast in
`StationMenu`), the four battle music groups in story order
(Sound tab, `MusicTracks`, "Music tracks off" in npcs-and-combat.md), mute in background (desktop,
`Settings.MuteInBackground`: `Bootstrap` zeroes the listener volume while the window is unfocused), double-press fire
for auto-fire (`Settings.KeyAutofire`, off by default: the touch fire button's 249 ms latch, `AutofireLatch`, on the
keyboard / mouse / controller fire binding; the PC version had none), the voice language (the Language tab under the
language buttons: As text / English / Deutsch, `Settings.VoiceLanguage` / `GermanVoices`; every voice goes through
`StoryAssets.Voice`). `Bootstrap` applies the process-wide ones on every change (and bloom / exposure to every scene's
global volumes on load) and restores the Editor's values when Play mode ends. The PC and Mobile URP assets are saved
with STP selected on purpose: URP strips STP's compute shaders from a build unless a pipeline asset uses it
(`STPResourceStripper`); at runtime the option replaces it (off = URP's automatic filter; on Android FSR 1 only below
100 %, #45: at 100 % or more part of the screen froze). `UrpStpGuard` (Editor) puts STP back on the project's URP assets
on Editor load, whenever Play mode ends (a recompile during Play mode skips `Bootstrap`'s restore) and before every
build; at runtime `Bootstrap.StpSupported` also needs `STP.RuntimeResources` in the build (checked by reflection), so a
build without them hides STP instead of freezing. **DLSS / FSR 2-4** (`UpscalerFramework`): URP's upscaler framework
behind the undocumented `ENABLE_UPSCALER_FRAMEWORK` define (Player settings, Standalone only; Android keeps the asset's
upscaling filter) on the built-in modules `com.unity.modules.nvidia` / `com.unity.modules.amd`. With it compiled in URP
ignores the old upscaling filter: every upscaler choice goes through `UniversalRenderPipeline.SetUpscaler` (Off =
`unity.auto`, `amd.fsr1`, `unity.stp`,
vidia.dlss4`, the newest of `amd.fsr4` / `fsr3` / `fsr2`), set again on
`RenderPipelineManager.activeRenderPipelineCreated`; the quality mode on the framework's options; device support from
`unity.auto`, `amd.fsr1`, `unity.stp`, `nvidia.dlss4`, the newest of `amd.fsr4` / `fsr3` / `fsr2`), set again on
`RenderPipelineManager.activeRenderPipelineCreated`; the quality mode on the framework's options (on Windows only since 2026-11-16: `GoF2.App.asmdef` lacked the
`ENABLE_AMD_MODULE` / `ENABLE_NVIDIA_MODULE` version defines, which only the render pipeline package had, so the DLSS / FSR
quality code never compiled and both ran at the framework's default mode); device support from
the framework's own `isSupportedOnDevice` (read through the internal `UniversalRenderPipeline.upscaling`; DLSS: RTX,
D3D11 / 12 / Vulkan; FSR 2 D3D11 / 12 / Vulkan; FSR 3 / 4 D3D12, FSR 4 AMD's newest GPUs; Windows builds only; only
known once URP has made its pipeline at the first frame, after the main menu built its rows: the Upscaler row asks again
on every refresh, the Upscaler quality row shows only while DLSS / FSR 2+ is the active upscaler and the Render scale
row only while it isn't (`OptionDef.visible`; they pick their own resolution), and the main menu refreshes its rows when
Options opens). `UrpStpGuard` keeps every URP asset's upscaler priority list (STP, DLSS, FSR 4 / 3 / 2, FSR 1, Auto) so
`isStpUsed` holds and nothing is stripped; the scaling mode stays None (the Editor's views render plain). The PC asset's
opaque texture is full size (opaque downsampling None: FSR's reactive mask warned every frame with 2x). Verified in the
Editor on an RTX 5070 laptop (D3D12): DLSS (all modes) and FSR 3 in the menu, station and flight; no frame generation (that is "Frame generation" below).
**Context leak** (`UpscalerFramework.FlushContexts`, hooked on `endCameraRendering` by `Bootstrap`): the framework keeps
one DLSS / FSR context per camera (~190 MB at 3440 x 1440) and drops it after 400 frames unused or on a resolution /
quality change, but `UniversalRenderPipeline.Render` records those DestroyFeature calls after the frame's last Submit
and never submits them, so every new camera (the star map, the item window, each scene's) and every camera off for 400
frames (the level's behind the map) leaked its context: a player build reached 7 GB in 15 map visits (reported on
50-series cards, measured on a 4080 at 143 Hz). The hook runs the cleanup after each camera and submits it; verified in
a build: flat over 12 map visits, memory back on leaving the station. **MetalFX** (macOS and iOS): the core package's
own `apple.metalfx-spatial` / `apple.metalfx-temporal` upscalers, compiled in with the built-in module
`com.unity.modules.metalfx` (manifest) on macOS / iOS targets with the framework define (iOS: `IosUpscalerDefine`
(Editor) keeps `ENABLE_UPSCALER_FRAMEWORK` in the iOS define symbols on Editor load and stops an iOS build that lacked
it; on iOS the framework offers Off / FSR 1 / STP / MetalFX, the Mobile URP asset keeps its upscaler list through
`UrpStpGuard`; iOS minimum 16.0); options 5 / 6 (`Settings.UpscalerMetalFxSpatial` / `Temporal`), offered only when
`isSupportedOnDevice` (Metal, macOS 13+ / iOS 16+). No quality modes: the Render scale row stays and sets their input
resolution (URP clamps it into MetalFX Temporal's supported range); Temporal counts as temporal (MSAA off). Not yet
verified on a Mac or an iPhone.
- **FSR on Linux** (remake, 2026-11-16, the user's request; `LinuxFsrUpscaler` / `LinuxFsrOptions`, native plugin
  `Native/FsrLinux` → `Assets/Plugins/Linux/x86_64`): Unity's AMD module is Windows only, so a Linux player had no FSR 2-4.
  [ffx-vk-linux](https://github.com/DuIslingr/ffx-vk-linux) (the user's library: AMD's Windows FidelityFX Vulkan DLLs loaded
  natively, no Wine) runs them; `LinuxFsrUpscaler` registers FSR 4 / 3 / 2 with URP's upscaler framework under the
  Windows ids (`amd.fsr4` / `fsr3` / `fsr2`), so `UpscalerFramework.BestFsr`, the Upscaler option and its quality modes
  work unchanged (Linux players on Vulkan only; compiled for `UNITY_STANDALONE_LINUX` without the AMD module).
  - **Vulkan 1.2+ and FSR's features:** Unity asks Vulkan for 1.1 and enables only its own device features (no project
    setting changes that). `libgof2_fsr.so` is preloaded (`isPreloaded`) and hooks Unity's Vulkan initialization
    (`IUnityGraphicsVulkanV2::AddInterceptInitialization`): `vkCreateInstance` asks for 1.3 (or the loader's version),
    `vkCreateDevice` adds what FSR's shaders and passes use where the GPU has it: float16 / int8, 8 / 16-bit storage,
    integer dot product, subgroup size control and extended types, the memory model, descriptor indexing (FSR 4's
    descriptor sets bind partially), separate depth / stencil layouts (its barriers name a D32S8 depth's depth aspect
    alone), storage images without a format, int16 / int64, compute shader derivatives, AMD coherent memory; never the
    robustness features. Set into Unity's own feature structs where its chain has them (put back after the call), else
    added; Unity's request again if the device fails. Below Vulkan 1.2 nothing changes and FSR stays off.
  - **Rendering:** create / dispatch / destroy are plugin events on the render graph's command buffer (outside a render
    pass, Unity's state rebuilt after), their data in an unmanaged ring; the textures by `GetNativeTexturePtr` (the depth by
    its render buffer) through `IUnityGraphicsVulkanV2::AccessTexture` (barriers into shader-read / general); a
    destroyed context is released once Unity's `safeFrameNumber` passes its last use; all at device shutdown.
    Jitter, mip bias, flags and motion-vector scale as Unity's FSR (`FSRUtils`); render size from FSR's ratios.
  - **The DLLs are the player's** (like the original assets): `amd_fidelityfx_loader_vk.dll` +
    `amd_fidelityfx_upscaler_vk.dll` (AMD FidelityFX SDK 2.4, `Kits/FidelityFX/signedbin`: FSR 3.1.6 / 2.3.4) and, for FSR
    4, `amdvkffx64.dll` from AMD's Windows driver (Adrenalin 26.10.1), in `GamePaths.Data/FidelityFX` (`$FFXVK_DLL_DIR`
    overrides; `$FFXVK_DRIVER_DLL` for the driver's elsewhere). The log line "FSR (Linux): …" says what ran.
  - **Building the plugin:** `cmake -S Native/FsrLinux -B Native/FsrLinux/build -DCMAKE_BUILD_TYPE=Release
    -DFFXVK_DIR=<ffx-vk-linux checkout>`, `cmake --build …`, `cmake --install …` (builds ffx-vk-linux too, from its
    checkout: commit db8277d or later for the fixes below, e06f017 or later for FSR 4 Lite). Against OpenSSL 3 where the system has it (Arch's `openssl-3`):
    ffx-vk-linux builds with 3 or 4, but players' systems mostly have `libcrypto.so.3`. Its other runtime needs:
    `libSPIRV-Tools.so`, `libdrm_amdgpu`.
  - **Performance** (2026-11-16, RX 7900 XTX, RADV / Mesa 26.2.6). The first build's FSR 4 cost 4.3 ms of GPU time at 4K
    Quality, FSR 3 1.15 ms: AMD's Vulkan code, not the plugin (its CPU part < 0.1 ms) nor ffx-vk-linux (AMD's DLLs as
    Windows code under Wine cost the same). Found with a per-dispatch GPU-timing Vulkan layer and RADV's shader
    statistics, and fixed in ffx-vk-linux (for every program using it), the output byte-identical (its probe):
    - **Uncached memory:** AMD's allocator took RADV's "device coherent, device uncached" types (~300 MB per 4K
      context), which the GPU's caches skip: the cached type instead (4.3 → 3.2 ms).
    - **Wave size:** RADV compiles AMD's compute shaders as wave32 (FSR 4 also asks for 32 on 13 of 30); those with a
      64-multiple workgroup and no subgroup operations run as wave64 (→ 2.7 ms).
    - **Host-visible buffers** in system memory (RADV lists that type first): in VRAM with Resizable BAR (→ 2.6 ms).
    - **DCC stores:** RADV's compressed writes to storage images cost FSR 4 ~0.3 ms; the plugin sets Mesa's
      `radv_disable_dcc_stores` for the game before Unity creates its Vulkan instance (unless set, or
      `GOF2_KEEP_DCC_STORES=1`); FSR 3 and the game's own rendering measured the same either way (→ 2.28 ms).
    - **Against D3D12** (the Windows build under Proton CachyOS, Unity's own FSR through vkd3d-proton, same GPU),
      back to back in desktop windows, GPU time (FrameTimingManager) with FSR minus bilinear from the same render
      resolution, five views: FSR 4 2.30 ms here against 2.54 (that one's 4.1.1), FSR 3 0.72 against 0.74. vkd3d-proton
      does the first three itself (cached and Resizable BAR VRAM types only, wave64 for those passes: the same layer);
      with `radv_disable_dcc_stores=true` too its FSR 4 takes 2.28 ms, level with this path (2026-11-16): nothing else
      it does differently (images kept in GENERAL, fewer barriers, no constant-buffer copies) measures.
    - **No Unity overhead:** CR3-U (a native C++ port using ffx-vk-linux) costs the same FSR 4 time at the same clock.
      A frame cap (vsync) lets the GPU clock down: FSR 4 then takes ~4.1 ms (still within the frame).
    - **Not taken:** skipping read-only barriers, wave64 for the 32-thread passes, auto exposure off (no gain);
      RDNA 3's matrix units (WMMA) - AMD's matrix path is FSR 4's RDNA 4 FP8 model; async compute - FSR 4's passes
      already overlap fill-bound work on the graphics queue (it helps FSR 3: ffx-vk-linux's README, "Asynchronous
      compute"); the plugin can't split Unity's frame for it anyway.
  - **FSR 4 Lite** (remake, 2026-11-16, the user's "sure why not"; Graphics "FSR 4 Lite", `Settings.Fsr4Lite`, off by
    default, in the Graphics reset list): AMD's smaller FSR 4 model, the one its driver DLL gives integrated GPUs, in
    place of the GPU's own; 1.36 ms against 2.27 at 4K Quality here (329 → 466 fps in the test bed's orbit), a picture
    of its own. The row shows only while FSR 4 is the upscaler and it runs through ffx-vk-linux
    (`UpscalerFramework.Fsr4LiteAvailable`: Linux, `Gof2Fsr_LiteAvailable`; not Windows, where Unity's AMD module or
    AMD's own loader picks the model). It switches while playing: `LinuxFsrOptions.lite` is part of what a context
    is valid for, so URP's framework makes a new one, and `CreateData.lite` has the plugin call ffx-vk-linux's
    `ffxvkFsr4Model` right before `ffxCreateContext` on the render thread. The driver DLL settles on a model once per
    instance, so the library keeps a second instance of `amdvkffx64.dll` for the other model (72 MB more, loaded at the
    first switch) and gives its provider a version id of its own for AMD's SDK (ffx-vk-linux's README, "FSR 4 Lite").
    A switch that fails (the DLL not loadable again) keeps the model and logs why. `$FFXVK_FSR4_MODEL` doesn't apply
    in the game: the option sets the model for every FSR 4 context. Label not translated (a product name); the help
    in all 13 languages (`fsr4LiteHelp`). Checked on the RX 7900 XTX only.
  - **The check:** `PlayerAssetsCheck -gof2fsr <quality>` (FSR for the run; `$GOF2_FSR_VERSION=3` / `2` that one where FSR
    4 also runs), `-gof2fps <s>` (frame-rate samples with the GPU frame time and, on Linux, FSR's own GPU / CPU time),
    `-gof2scale <s>` (that render scale, upscaler off: the bilinear baseline), `-gof2cap <fps>` (a frame cap for the
    samples), `-gof2fsrlite 1` (FSR 4 Lite for the run) / `2` (the option switched on and off again in the hangar, a
    picture and a sample each). Development builds turn on frame timing stats for it (PlayerAssetsBuild).
  - **Windows comparison builds:** `PlayerAssetsBuild.WindowsDevelopment` (CoreCLR: IL2CPP for Windows can't be built on
    Linux; D3D12, or Vulkan with `$GOF2_BUILD_API=Vulkan`). On Vulkan the same plugin, `gof2_fsr.dll`
    (`Assets/Plugins/Windows/x86_64`, cross-built with llvm-mingw: `x86_64-w64-mingw32-g++ -std=c++20 -O2 -shared -static
    … Native/FsrLinux/gof2_fsr.cpp` with the Unity plugin, FFX and Vulkan headers), loads AMD's DLLs as they are and
    registers as `ffxvk.fsr4` / `fsr3` / `fsr2` (Unity's AMD module has `amd.*` there); it does the library's memory and
    wave64 changes itself. Proton: in a desktop window (Wine under headless gamescope hangs at the D3D12 / Vulkan device),
    `PROTON_FSR4_UPGRADE=0` (no download) and `amdxcffx64.dll` from the same driver in the prefix's `system32` for D3D12 FSR
    4 (Unity's SDK DLL also runs 4.1.1 without it).
  - **Checked** (2026-11-16, Linux development build): Vulkan 1.3, providers 4.1.2 / 3.1.6 / 2.3.4, FSR 4 Quality in the
    menu, orbits and hangar, pictures as native; no validation error from the hook or FSR (gamescope's WSI layer has its
    own). Not checked: other GPUs / vendors, the FSR option changed while playing.
- **Frame generation** and **Reflex** (remake, 2026-11-20, the user's request; Graphics "Frame generation" / "NVIDIA
  Reflex", `Settings.FrameGeneration` 0 off, 1..5 generated frames per rendered one, 6 dynamic / `Settings.Reflex` 0 off,
  1 on, 2 on + boost; `FrameGeneration`, `FrameGenerationPass`, native plugin `Native/Streamline` →
  `Assets/Plugins/Windows/x86_64/gof2_streamline.dll`): NVIDIA DLSS Frame Generation (DLSS-G) and Reflex through NVIDIA
  Streamline 2.14.16, in Windows D3D12 players on NVIDIA GPUs. Unity's NVIDIA module is the upscaler alone, and DLSS-G has
  to own the swap chain (it presents the generated frames), which Unity makes itself.
  - **Attaching** (the plugin's header): preloaded (`isPreloaded`), so `UnityPluginLoad` runs before Unity makes its
    device; Streamline's "manual hooking" mode (`sdk/docs/ProgrammingGuideManualHooking.md`): Unity keeps its native
    device, queues and factory, and only three calls go through Streamline's proxies. `D3D12CreateDevice`
    (UnityPlayer.dll's delay import, ordinal 101) → the device's `CreateCommandQueue` vtable slot; Unity's first
    `CreateCommandQueue` gives Streamline the device (`slSetD3DDevice`) and runs through the proxy device (DLSS-G's hook
    wants the queues; Unity gets the native queue back); `CreateDXGIFactory` / `2` (UnityPlayer.dll's imports) → the
    factory's `CreateSwapChain(ForHwnd)` slots: a D3D12 swap chain Unity makes goes through the proxy factory, so Unity
    holds Streamline's swap chain proxy. Only calls whose return address is in UnityPlayer.dll are redirected
    (Streamline's own queues and swap chain use the same vtables). `sl.interposer.dll` is loaded after its signature
    check, delay-linked from an import library made of its export names (`sl_interposer.def`), `EngineType::eUnity`
    (sl.reflex then takes the render-submit-end marker for the frame being presented: Unity presents out of reach).
  - **Armed:** whether to start Streamline has to be known before any managed code runs: `HKCU\Software\GoF2Remake\
    FrameGeneration` `Armed` (written on every option change, `Gof2Sl_SetArmed`: either option on). Unarmed the plugin
    does nothing at all, so turning an option on for the first time needs a restart (the row's help says so); once
    Streamline runs, the multiplier, off and Reflex's mode change at once (DLSS-G stays loaded with mode off: a copy a
    frame, no restart). `Starting` is set before Streamline starts and cleared once the game has run 120 frames
    (`Gof2Sl_StartedOk`): a start that never got there is not repeated (the next one stays unarmed, turns both options off
    and says so). Never armed with `-batchmode` / `-nographics` / `-server` / another `-force-` API / `-vr` / `-vrsim`;
    `-noframegen` / `-framegen` force it either way.
  - **Per frame:** player loop steps (`FrameGeneration.HookPlayerLoop`, only while armed): first thing in a frame
    `slReflexSleep` + the simulation-start marker; before `PostLateUpdate.FinishFrameRendering` simulation end / render
    submit start; after it a render thread event (`EventFrameEnd`): render submit end, `slDLSSGSetOptions` when the mode
    changes (off on a frame nothing was tagged for: loading, no screen camera), `slDLSSGGetState` (what the options and
    the frame rate counter show: "118 FPS (59 × 2)"). `FrameGenerationPass` (`RenderPassEvent.AfterRendering`: after the
    final blit, before URP draws the interface's overlay; the first full-screen screen camera of a frame, asking URP for
    depth and motion vectors) copies the camera's depth and motion vectors into two textures of its own
    (`FrameGenInputs.shader`), the way up the back buffer is: on Direct3D URP's render textures are usually the other way
    up (the render graph's `TextureUVOrigin`; the motion's y negated with the turn), and DLSS-G interpolates the back
    buffer. Its tag event (`EventTag`) gives Streamline those two (ours: valid until the present, in the state
    `RequestResourceState` put them in), the camera constants (Unity's matrices are for column vectors: Streamline's rows
    are our columns; `GL.GetGPUProjectionMatrix(…, false)`, no jitter; clip to previous clip from the frame before, reset
    on another camera or a skipped frame; motion vector scale -1, -1: URP's are current minus previous in texture
    coordinates), and the back buffer as it is then, before the interface, as the HUD-less colour (Streamline copies it
    on Unity's command list: the HUD is not smeared along with the scene behind it).
  - **The options:** shown where the plugin and Streamline's DLLs are in the build on an NVIDIA GPU under D3D12
    (`FrameGeneration.Offered`). Frame generation: Off, 2× … the GPU's most (`numFramesToGenerateMax`: 2× alone on RTX
    40, more on RTX 50), Dynamic where the driver has it; a stored choice the GPU doesn't run is clamped. Reflex: Off /
    On / On + Boost; frame generation keeps it at least on (DLSS-G needs it). The help line names what stops it (the
    driver, Windows, hardware-accelerated GPU scheduling off, the GPU). DLSS-G stops by itself while the window has no
    focus and where VSync already fills the display's refresh rate.
  - **The DLLs** (`Assets/Plugins/Windows/x86_64`, Win64 only): `sl.interposer.dll`, `sl.common.dll`, `sl.dlss_g.dll`,
    `sl.reflex.dll`, `sl.pcl.dll` (Streamline 2.14.16, production, NVIDIA-signed) and `nvngx_dlssg.dll` (310.9.16), under
    NVIDIA's RTX SDKs licence (kept with the SDK's in `Native/Streamline/redist`, git-ignored); without them the options
    are not shown. The plugin builds against the SDK's headers of the same version (`Native/Streamline/sdk`, git-ignored:
    the clone command and the build are in `Native/Streamline/CMakeLists.txt`). `DlssRedist` (Editor, after a Windows
    build) puts a newer `nvngx_dlss.dll` from `Native/Streamline/redist` in place of the one Unity ships (310.5.3 →
    310.9.16: the upscaler's newer models).
  - **Testing:** `$GOF2_FRAMEGEN` (0..6) / `$GOF2_REFLEX` (0..2) in place of the options for one run, with `-framegen`;
    `$GOF2_SL_VERBOSE` a status line every 512 frames and Streamline's own log lines in the Player.log;
    `$GOF2_SL_LOGDIR` Streamline's log files; `$GOF2_FRAMEGEN_ONLY_GENERATED` shows the generated frames alone.
  - **Checked** (2026-11-20, RTX 4080, driver 621.24, Windows 11 26300, a build): Streamline starts before the device,
    "Unity's swap chain is Streamline's", DLSS-G supported (2× the most there), Reflex low latency available, the menu
    camera tagged without errors, and in the focused menu on the 3440 x 1440, 143 Hz display with VSync: 2.00 frames
    shown per rendered one, 68 - 70 rendered a second (half the refresh rate: DLSS-G's own pacing), status OK. A window
    without focus is not interpolated (Streamline: "DLSS-G disabled: window not focused"), so a test started from a
    script has to bring the window to the front. Not checked: the picture in flight (motion vectors and the HUD by
    eye), the resolution or the window mode changed while it generates, multi frame generation (RTX 50), HDR.
- **Apply (the Graphics tab)** (remake, 2026-11-20, the user's request: "an apply button … that applies but does not
  close the settings"; the user chose the Graphics tab alone, the other tabs apply as you change them): while an options
  screen is open the Graphics tab's rows wait for the footer's Apply; the screen stays open. Leaving with changes not
  applied asks "Apply the changed graphics settings?" (Apply / Discard / Cancel).
  - **Staged options** (`Settings`: `BeginStaging` / `EndStaging`, `ViewStaged`, `ApplyStaged`, `DiscardStaged`,
    `HasStaged`, `StagedChanged`): inside `ViewStaged` a `Set` / `SetQuiet` goes to a pending table and a `Get` answers
    from it; outside it everything reads the applied values. `OptionsCatalog.All` wraps every delegate of the Graphics
    rows in it (`Stage`, `OptionDef.staged`), so a row shows what was picked and the rows depending on it follow (the
    upscaler's quality row, MSAA greyed out, the preset row), while the game and `Bootstrap`'s appliers see nothing
    until Apply: one `Changed` for the lot. A value put back to the applied one leaves nothing waiting; a preset stages
    all its options; Default settings applies at once and drops what waited for those keys. `Changed`'s listeners always
    read the applied values (`RaiseChanged`), also when raised inside the view. Without an open screen (the tests, rows
    built elsewhere) the view does nothing: at once, as before.
  - **The screens:** `OptionsView` (pause menu, station, Mod Builder) stages while it is on a panel (dropped when it
    goes another way than Back); Apply is in the footer on the Graphics tab, greyed out and skipped by the keys while
    nothing waits; the question takes the footer's place like the defaults prompt, and the hosts' Back
    (`CloseResetPrompt`) opens it. `MainMenu` stages while its Options panel is open (`OpenPanel` / `Back`), its
    `optionsApply` button and the dialog's three buttons (`AskApplyOptions`).
  - **Keep these settings?** (`SettingsConfirm`): a display mode or resolution asks once it is applied
    (`OptionsCatalog.ApplyStaged`), with the applied value to go back to; Revert writes the old values at once.
  - **Checked** (2026-11-20, the Editor's Play mode, main menu): the button on the Graphics tab only, lit by a change,
    the option applied with the panel open, Back with a change waiting asks. The in-game page by its tests
    (`SettingsTests.OptionsPageApplyAndLeavePrompt`), not played.
- **Graphics categories** (remake, 2026-11-20, the user's request: "in categories just like gameplay has"): the Graphics
  tab's rows under headings like the Cosmetics and Gameplay tabs' (`OptionSection.Graphics*`, `OptionSectionBlock`):
  the preset row above them, then Display, Upscaling and frame generation, Image quality, Effects, Skies and planets,
  Camera, UI. `OptionsOrganization.GraphicsLayout` lists each category's rows in the order shown (`OrganizeGraphics`
  sorts the list to it: the keys walk the rows in list order); a row not listed lands at the end of Effects, and
  `SettingsTests.GraphicsCategoriesAndNewOptions` fails until a new row is placed (`HasGraphicsPlace`). A section's
  heading hides while none of its rows shows (`OptionSectionBlock.RefreshGroups`).
- **DLSS preset** (remake, 2026-11-20, the user's request; Graphics "DLSS preset", `Settings.DlssPreset`, with DLSS on):
  DLSS's model in place of NVIDIA's pick for the quality mode: Default, then the letters the mode offers (F, J, K, L, M in
  this NVIDIA module: `UpscalerFramework.DlssPresets`, the module's `GetAvailableDLSSPresetsForQuality` bit mask); the
  value is the module's `DLSSPreset` bit, set on all five of `DLSSOptions`' per-mode presets where the mode offers it
  (`SetQuality`), NVIDIA's default elsewhere. The help line adds NVIDIA's own (English) line about the picked preset.
- **Neural rendering** (remake, 2026-11-20, the user's request: "DLSS 5", "only on 50xx and up"; Graphics "Neural
  rendering (DLSS 5)" and its strengths / quality, `Settings.NeuralRendering*`; `NeuralRenderingPass`, the plugin's
  `OnNeural`): NVIDIA's DLSS 3D-guided neural rendering (Streamline's `sl.dlss_nr`, NGX `nvngx_dlssnr.dll`) on the
  finished picture.
  - **No public header:** Streamline 2.14's public SDK has only the feature id and the three "uplift" buffer types; the
    API is in NVIDIA's NDA SDK. The plugin declares it itself (`gof2_streamline.cpp`, top): the one function the DLL
    exports by name (`slDLSSNRSetOptions`), the options struct's id (found byte for byte in `sl.dlss_nr.dll` 2.13), its
    members in the order Gaijin's Dagor Engine sets them (`streamline_adapter.cpp`, built against the real header) and a
    community reconstruction lists them. Replace it with NVIDIA's `sl_dlss_nr.h` once there is one.
  - **Only where it runs:** the two DLLs are not in the repository (NVIDIA has not published them; a builder who has
    them puts them in `Native/Streamline/redist`, and `DlssRedist` copies them beside the plugin). The plugin asks for
    the feature only when both are there and logs NVIDIA's own answer ("DLSS neural rendering: supported / not supported
    on this GPU", state bits 128 / 256): the options show only where it is supported (`FrameGeneration.NeuralOffered`;
    before Streamline has run, by the card's name: not GTX, RTX 20 / 30 / 40).
  - **The frame** (as Dagor evaluates it): `NeuralRenderingPass` after the post-processing, before the final blit, on
    the camera `FrameGenerationPass` picked: the frame's inputs first (the constants, the depth and motion vector
    copies: shared with DLSS-G, tagged once), the picture (linear SDR there) sRGB-encoded into a 10-bit texture the way
    up the back buffer is (DLSS-NR takes tonemapped SDR at the output resolution), the event (`slSetTagForFrame` with
    the uplift input / output colour valid until the evaluate, `slEvaluateFeature`), and back into a camera colour
    texture, linear again. A failure copies the picture through; after 8 the pass stops for the session. Not with HDR
    output (the picture is not SDR then). All its options change in a running game (the rows are in the pause and
    station menus too): `slDLSSNRSetOptions` when a value changes.
  - **Checked** (2026-11-20, RTX 4080): with the genuine NVIDIA-signed 2.13 DLLs beside the plugin Streamline loads
    `sl.dlss_nr`, reports "DLSS-NR feature is not supported" and ignores it, the options stay hidden and nothing else
    changes. **Not checked at all:** the feature running (no RTX 50 card here): the struct layout, the picture's
    encoding and the orientation are from public code, not from a run.
- **HDR** (remake, 2026-11-20, the user's request: "HDR support with all its settings"; Graphics, Display: "HDR" and its
  rows, `Settings.Hdr*`; `Bootstrap.ApplyHdr`): HDR display output on desktop players. The player allows it
  (`allowHDRDisplaySupport`, 10 bit: HDR10, the format DLSS frame generation needs; not "use HDR display output", so a
  start is in SDR until the option asks). The option (off by default; greyed out until the display has HDR on in
  Windows, `HDROutputSettings.main.available`) requests the mode (`RequestHDRModeChange`; frame generation pauses 90
  frames across the swap chain's change, `FrameGeneration.Pause`). While it is on a global volume of the game's own
  (priority 1000, kept across scenes) carries URP's `Tonemapping` override with the options' values: paper white (80 -
  400 nits, default 200), the display's own limits or the player's peak (400 - 4000) and black level, the tonemapper
  (None / Neutral, the default / ACES), ACES's display preset (1000 / 2000 / 4000 nits), Neutral's range reduction
  (BT.2390 / Reinhard) and hue shift. Off, the volume is inactive and the scenes are as built (they have no tonemapping).
  With HDR on URP draws the interface into the picture before the final blit, so DLSS-G's HUD-less colour holds the HUD
  too (the HUD is interpolated with the scene); neural rendering does not run. Not checked on an HDR display.
  - **Calibration** (the "HDR calibration…" row, `HdrCalibration` + `Resources/GoF2PostFx/HdrCalibration.shader`; only
    with HDR on and applied): a test pattern in place of the scene (`PatternPass`, before the post-processing: nits
    through the same tonemapping the game gets, the bloom and colour adjustments held neutral) with two sliders. Left:
    a diamond at the display's full signal in a block at the peak picked: raise the peak until the diamond is gone and
    no further. Right: a band at paper white beside reference greys. The sliders write the options at once
    (`Settings.Unstage`: not waiting for Apply); the other panels hide while it is up. Not checked on an HDR display.
- **Frame generation's later additions** (remake, 2026-11-20, the user's requests):
  - **Latency readout:** the frame rate counter adds "PCL 6 ms": Reflex's own reports (`slReflexGetState`, the median
    of the last whole reports' GPU render end minus simulation start; `FrameGeneration.LatencyMs`). The reports stay
    empty past the render submit without the present markers, and Unity presents by itself: the swap chain proxy's
    `Present` / `Present1` vtable slots are wrapped (`Hook_Present`: present start, the proxy's present, present end,
    for the frame whose FrameEnd event ran last). Before that the counter showed ~445 ms from stale entries. Checked:
    whole reports ("Reflex report:" with `$GOF2_SL_VERBOSE`), ~5.6 ms sim start to GPU end at 70 rendered frames.
  - **Reflex's frame limiter:** while Streamline runs, a frame rate cap (30 .. 360) is Reflex's
    (`ReflexOptions.frameLimitUs`, the driver paces it in `slReflexSleep` in step with frame generation) and Unity's
    `targetFrameRate` is off (`FrameGeneration.ReflexLimits`, `Bootstrap.Limit`). Checked: `$GOF2_FRAMELIMIT=60` under
    V-Sync held 59 - 60 rendered frames a second.
  - **Dynamic's target** ("Frame generation target", `Settings.FrameGenerationTarget`, with Dynamic picked): the
    display's refresh rate or 60 .. 360 frames a second (`DLSSGOptions.dynamicTargetFrameRate`). Not checked (the RTX 40
    driver has no Dynamic).
  - **No generated frames behind a menu** (`FrameGeneration.SetMenu`): the pause menu, the station's system menu and a
    main menu panel hold frame generation off while open (Streamline's guide: menus are still interface; DLSS-G keeps
    its resources, `eRetainResourcesWhenOff`, so it is back at once). `$GOF2_FRAMEGEN_MENUS=1` leaves it on, for tests.
  - **The interface's own layer** (`FrameGenerationPass.RecordInterface`): the overlay (UI Toolkit / uGUI) drawn once
    more into a texture cleared to nothing, its alpha copied into an R8 texture the way up the back buffer is and
    tagged as `kBufferTypeUIAlpha` beside the HUD-less colour; `DLSSGOptions.enableUserInterfaceRecomposition` then has
    DLSS-G interpolate the scene and the interface apart. An unsafe pass with the target bound by hand
    (`SetRenderTarget`), as URP's own off-screen overlay with a backdrop filter: in a raster pass two UI Toolkit panels
    came out opposite ways up (UI Toolkit reads the active render target when a panel is drawn). Not with HDR output
    (URP composites the interface before the back buffer there); `$GOF2_FRAMEGEN_UI=0` leaves it out;
    `$GOF2_FRAMEGEN_DUMP=<folder>` writes the mask and a screenshot of one frame. Checked: the mask lies exactly over
    the menu's logo and the counter, 2.00 frames shown per rendered one with it, no tag errors. Not compared by eye
    against frames without it.
- **FSR frame generation** (remake, 2026-11-20, the user's request; Graphics "Frame generation" with "Frame generation
  type" `Settings.FrameGenerationProvider` 0 NVIDIA DLSS / 1 AMD FSR; `FrameGeneration.UsesFsr`, the plugin's "AMD FSR
  frame generation" part): the other provider of generated frames, on any vendor's GPU under D3D12. One generated
  frame per rendered one (Off / 2×). On an NVIDIA card the type row picks (DLSS by default); elsewhere FSR is the
  only one and the row hides.
  - **One or the other per run:** two frame generators can't share a swap chain, and FSR's has to be made in place of
    Unity's, so the provider is a start's choice like "armed" (`HKCU\...\FrameGeneration` `Provider`, written with every
    option change: FSR only while frame generation is on with it; `-fsrframegen` forces it). In an FSR run Streamline
    is not started: no Reflex (its row says so), Image Scaling, Dynamic Vibrance or neural rendering; Anti-Lag 2 is
    separate and stays. Changing the type, or turning FSR's frame generation on, takes a restart.
  - **Attaching:** AMD's FidelityFX API through the loader Unity's AMD module already puts beside the player
    (`amd_fidelityfx_loader_dx12.dll`, 2.3.0; its five functions by name, nothing linked) plus
    `amd_fidelityfx_framegeneration_dx12.dll` of the same SDK build (AMD-signed, `Native/FsrFrameGen/sdk`, git-ignored:
    the clone command is in `Native/Streamline/CMakeLists.txt`; `DlssRedist` copies it beside the loader when the two
    loaders' versions match). The same `CreateDXGIFactory` / `CreateSwapChainForHwnd` hooks as Streamline's: Unity's
    swap chain is FSR's frame generation swap chain (`ffxCreateContext`, `..._FRAMEGENERATIONSWAPCHAIN_FOR_HWND_DX12`);
    Unity's own when FSR can't make one.
  - **Per frame** (render thread, `FsrOnTag` / `FsrOnFrameEnd`, the Tag and FrameEnd events `FrameGenerationPass` and
    `FrameGeneration` already issue): the frame generation context for the back buffer's size and format (made again
    when they change, after `WAIT_FOR_PRESENTS`); the back buffer before the interface copied into a texture of ours,
    the HUD-less colour (kept until the present); `ffxConfigure` (enabled, the HUD-less colour, the frame's number, the
    generation callback: `ffxDispatch` on our context when the swap chain presents); the "prepare" dispatch with the
    depth and motion vector copies DLSS-G gets (inverted depth; motion scale minus the render size: ours are current
    minus previous in texture coordinates), the jitter and the camera. Off (`ffxConfigure`) on a frame that tagged
    nothing, behind a menu, or after a failure (5 and it stays off). The counter's "× 2" there is the plugin's own
    count of frames it asked for, not a measurement.
  - **Checked** (2026-11-20, RTX 4080, a build with `-fsrframegen`): "Unity's swap chain is FSR's", the context made,
    no errors, a clean exit; with `$GOF2_FSR_DEBUG` FSR's debug view on the screen itself (a capture of the display):
    the generated frame, its optical flow, the HUD-less picture without the menu's logo beside the frame with it.
    **Not checked:** the picture in flight by eye, a Radeon or Arc card, V-Sync's pacing (the game rendered 144 frames
    a second on the 143 Hz display with it on: how many generated frames the display then shows was not measured), a
    resolution or HDR change while it runs, the options' restart flow played through the menu.
- **Anti-Lag 2** (remake, 2026-11-20, the user's request; Graphics "AMD Anti-Lag 2", `Settings.AntiLag`): Reflex's
  counterpart on Radeon cards. AMD's SDK is one header (`Native/AntiLag2/sdk/ffx_antilag2_dx12.h`, git-ignored: the
  clone is `github.com/GPUOpen-LibrariesAndSDKs/AntiLag2-SDK`) talking to the driver (`amdxc64.dll`) through the D3D12
  device; the plugin initializes it when Unity's device exists (state bit 512: only there the row shows) and
  `Gof2Sl_AntiLag` runs first thing in a frame, before the input is read (`FrameGeneration.FrameStart`; the player loop
  step is hooked for it alone when Streamline is not started). Its limiter takes a frame rate cap like Reflex's. Changes
  at once. **Not checked:** no Radeon card here (on the RTX 4080 the driver's DLL is not there and nothing happens).
- **Frame rate limits** (remake, 2026-11-20, the user's "add more FrameRate limit options"): 30, 40, 45, 50, 60, 72,
  75, 90, 100, 120, 144, 165, 180, 200, 240, 300, 360, Uncapped, V-Sync (`Settings.FrameRateChoices`: the order shown;
  the enum's first five keep their stored numbers, the new ones follow; `Settings.FrameRateLimit`). A phone or tablet
  shows only the limits its screen reaches (`Bootstrap.FrameRateChoices`), a desktop all of them.
- **Image Scaling and Dynamic Vibrance** (remake, 2026-11-20, the user's request; Graphics, Effects: "NVIDIA Image
  Scaling sharpening" `Settings.NisSharpness` 0 off .. 1, "RTX Dynamic Vibrance" `Settings.DynamicVibrance` with its
  intensity and saturation; `PictureFiltersPass`, the plugin's `OnPicture`): Streamline's two filters of the finished
  picture. NIS as a sharpener (`NISMode::eSharpen`: the picture is at its final size; DLSS has no sharpening of its own)
  and DeepDVC in place on the result, on the SDR picture sRGB-encoded into a 10-bit texture between the post-processing
  and the final blit, like neural rendering's pass and after it. Neither needs depth, motion vectors or constants. Like
  frame generation they need Streamline started with the game (a restart the first time); a failure leaves the picture
  as it was and after 8 the pass stops. Not with HDR output.
  - **The DLLs:** `sl.nis.dll` (Streamline's release, NVIDIA-signed) in `Native/Streamline/redist` is copied into a
    build by `DlssRedist`. **DeepDVC's public DLL is watermarked** ("DeepDVC - DO NOT DISTRIBUTE - CONTACT NVIDIA TO
    OBTAIN DLLs FOR YOUR APPLICATION" in the picture's corner): `sl.deepdvc.dll` / `nvngx_deepdvc.dll` of the release
    are kept in `redist/watermarked` and not copied, so builds have no Dynamic Vibrance row until NVIDIA's production
    DLLs are in `redist`.
  - **Checked** (2026-11-20, RTX 4080): both supported, 6000 frames filtered without an error (`$GOF2_NIS=60`,
    `$GOF2_VIBRANCE=1` in place of the options), the picture as expected plus DeepDVC's watermark. Not compared by eye
    at different strengths.
- **XeSS** (remake, 2026-11-20, the user's request; the Upscaler option's "XeSS", `Settings.UpscalerXess` = 7;
  `XessUpscaler` / `XessOptions`, native plugin `Native/XeSS` → `Assets/Plugins/Windows/x86_64/gof2_xess.dll`): Intel
  XeSS Super Resolution through URP's upscaler framework (id `intel.xess`), in Windows D3D12 players on any GPU with
  Shader Model 6.4: the one AI upscaler every recent card has. Temporal like DLSS (MSAA off, the Upscaler quality option
  picks the render resolution: XeSS's own answer, `xessGetOptimalInputResolution`; native = its AA mode). Contexts and
  dispatches are plugin events on the render graph's command buffer like `LinuxFsrUpscaler`'s; a destroyed context is
  kept two seconds first (the GPU may still run its last frame). Inputs: the camera colour, depth (inverted) and motion
  vectors at the render resolution, FSR's jitter and motion scale conventions (XeSS's are the same). Intel's
  `libxess.dll` (the XeSS SDK, `Native/XeSS/sdk`, git-ignored; Intel's licence lets it be redistributed unchanged with
  its notice) is delay-loaded from beside the plugin, where `DlssRedist` copies it; without it or the plugin the option
  has no XeSS. A stored XeSS on a device without it falls to FSR 2+ or STP. `$GOF2_UPSCALER=7` picks it for a run.
  **Checked** (2026-11-20, RTX 4080, XeSS 2.0.2): quality mode at 3440 x 1440 renders 2024 x 848, a clean picture in
  the menu, no errors. Not checked: flight (ghosting by eye), an Intel or AMD card, a resolution change.
- **Original bloom** (`ClassicBloomPass` + `Resources/GoF2PostFx/ClassicBloom.shader`; the bloom option's "Original"):
  the original's only post effect (`AbyssEngine::BloomShader`, on from `MGame::OnInitialize` via
  `Engine::SetPostEffect(0x1400000, true)`; the glow 0x1400001 exists but is never switched on; the radial blur 0x1400002
  runs while boosting, `RadialBlurPass`, below) as a URP render-graph pass after the post-processing, queued from `beginCameraRendering` on every base screen
  camera with post-processing (no renderer-asset change): the scene sampled into 256 × 256 through the original's
  gamma-space bright pass (Luminance 0.08, middle gray 0.005, white cutoff 0.014: about everything above half
  brightness), 6 × the horizontal blur (the phone's offsets are vec2(o), a diagonal blur; Modified 2026-11-17: truly
  horizontal, as the PC version's BloomShader.fx, the user's files) and the vertical one (9 taps,
  texSize 256), then clamp(scene + glow) (`Engine::switchBloom` = 3 in the binary); URP's Bloom is off meanwhile. The
  original also blooms its HUD (drawn into the same FBO); the remake's UI Toolkit HUD doesn't.
- **Boost blur** (`RadialBlurPass` + `Resources/GoF2PostFx/RadialBlur.shader`, remake 2026-11-17, always on): the
  original's post effect 0x1400002, which `MGame::OnUpdate` switches on while `PlayerEgo::boosting` (not docking to a
  planet): the screen smeared toward the player's ship on screen, mixed in by clamp(distance x the boost percentage);
  the pixel and 5 samples 0.01-0.03 of the screen along the line, x 0.1429. A render-graph pass after the
  post-processing on the camera with a `ChaseCamera` whose ship boosts (`BoostVisualPercent`). The PC version's
  BlurShader.fx is another, zoom-style blur (6 scaled samples, `u_radial_bright`) whose strength and brightness aren't
  known; the phone's is the one ported.
- Frame rate option (`Settings.FrameRate`: 30 / 60 / 120 / Uncapped / V-Sync, default V-Sync; 60 on Android, the user's choice
  2026-10-22: a 120 Hz phone at 120 heats up and throttles; 60 on iOS too since 2026-11-14, `Settings.IosDefaults`) is
  applied by `Bootstrap.ApplyFrameRate`.
- **Game experience** (remake, 2026-11-04, the Gameplay tab's first row, after a player's request: "a preset 'Vanilla'
  toggle ... vanilla should be default"; `Settings.ExperienceKeys` / `ApplyExperience` / `CurrentExperience`): Original /
  Cosmetic / Remake set the experience options at once; the row shows the one they match, else Custom (picking it sets
  nothing; one changed option makes it Custom, like the graphics preset). **Original is every option's default**, so a
  fresh install plays the original game: no hangar flights or animated dialogue, the original's lock, informer rule, ship
  collision and Kaamo Club, no pirate events, capital ship enhancements, cloak rules, smarter NPCs, conversations running,
  ship cruise, Khador Slipstream or bomb arming (weapons.md "Bomb arming"). Cosmetic = that plus hangar flights and animated dialogue; Remake = every addition on
  (Mixed collision, the VIP Card bought at the club). The original's own launch camera and salvage markers are on in all
  three (turned off, Custom). Not in it: utilities and HUD looks (tutorials, button hints, HUD mode / size / opacity, far
  ship marker style, auto-play dialogue, which AUTO also switches, speedrun, story step, Discord, difficulty, mods).
  **Every row reads off (or its first choice) = the original**: the launch camera and salvage markers became "Skip ..." /
  "Hide ..." toggles, the target lock and informer rule toggles ("Smart target lock", "Lenient informer missions"), the
  collision and VIP Card choices list Original / Off first; only the UI maps them, the stored keys keep their meaning.
  **Players from before keep their options**: `MigrateExperienceDefaults` (Bootstrap, once, key `experienceDefaults`) stores
  the old default of every experience option never set when the install was played before (any option stored or a save
  slot; Android's collision stays Original), so their row shows Remake-like values or Custom. A dedicated server's rules are
  `NetRules.Defaults`, untouched; a player host's are its options (`LocalRules`). Tests `GameExperienceTests`.
  **An option added to the presets later** goes into `AddedExperienceKeys` (and a column of every row and of
  `DefaultsBefore`): the migration's version 2 (`ExperienceVersion`) gives an install migrated before it the preset's
  value when its other options match one (`KeepPresetForAddedKeys`), so a Remake player's row doesn't turn Custom; bump
  the version for the next one. First: `bombArming` (2026-11-05); `derelictWrecks` (2026-11-13, version 3; "Derelict
  freighters", see supernova.md).
- **Welcome screen** (remake, 2026-11-04, `MainMenuWelcome`): on the first start, 1.3 s after the main buttons come in (not
  over a panel), the menu's own dialog: what the remake is and where things are (Options > Gameplay, Controls / Key
  bindings, Mods, Multiplayer and Co-op, named by their buttons' own texts), Next, then "How do you want to play?" with
  Remake / Cosmetic / Original (No = Original, so Back / B / Esc keep the default; the focus starts there).
  `Settings.WelcomeShown` marks it seen. An install played before (`Settings.FreshInstall` false) gets one notice that adds
  where the new row is and that its options are kept.
- **Station scale** (remake, experimental, 2026-11-15, Gameplay, below "Skip launch and arrival camera";
  `Settings.StationScale`, Original / 1.5x .. 6x): the orbit's stations, gates and other structures grown, the orbit
  around them with them, single player only, from the next orbit on; see "Station scale" in space-scene.md.
- **Graphics preset** (remake, 2026-10-22, the Graphics tab's first row; `Settings.PresetKeys` / `ApplyPreset`,
  `OptionsCatalog.CurrentPreset`): Low / Medium / High (the original's texts 507-509) and Ultra (2026-10-23) set render
  scale, upscaler, MSAA, post-process AA, bloom, hangar shadows, hangar depth of field, lens flare, live mod skies (off on
  Low only, mods-world.md "Chorus skies"), other ships' engines,
  levels of detail, texture quality, fire and smoke, fog, detail distance and texture filtering at once; the row
  shows the preset the options match (render scale and MSAA as applied, 0 = the platform asset's), else Custom (picking it
  sets nothing). High = the desktop defaults (100 %, no upscaler, MSAA off with SMAA, Remake bloom, the
  player's hangar shadow, lens flare, the player-style NPC engines, LODs, fire, smoke and fog, detail High, forced
  filtering); Medium = fire, smoke and fog off, detail Medium, anisotropic filtering, 80 % with FSR 1, NPC engines
  original; Low = that with detail Low, 67 % with FSR 1, FXAA, bloom off, no hangar shadows or lens flare, half-size
  textures;
  Ultra = every option at its most (MSAA 8×, every ship's hangar shadow, depth of field, no made levels of detail, detail
  Ultra, forced 16×) but the render scale at 150 % (the row goes to 200 %: four times 100 %'s pixels with MSAA 8× is past
  most GPUs). Android's defaults are Low (`Settings.AndroidDefaults`: each preset key's fallback, `Default`;
  keys a player set stay theirs; `OverrideAndroidDefaults` for tests) and the Original ship collision. iOS's are Medium
  (`Settings.IosDefaults`, `OverrideIosDefaults`; 2026-11-14, the user's "optimize the game for the iPhone Duo": High's
  100 % on its 1878 x 2670 inner screen at 120 Hz is five megapixels 120 times a second; Apple's chips carry Medium's
  80 % easily, so not Android's Low). On iOS and macOS players the presets' FSR 1 is MetalFX Spatial (`Settings.
  AppleUpscaler`, swapped in `PresetValue`, so `ApplyPreset`, the defaults and the row's match all take it; the user's
  "why fsr when metalfx is a thing"): Apple's own spatial upscaler, like for like; not Temporal, as the temporal paths
  ghost on fast flight and neither MetalFX is yet seen on a device (Temporal stays in the Upscaler row). A MetalFX the
  device lacks falls back to its FSR counterpart (`Bootstrap.ActiveUpscaler`: Spatial to FSR 1, Temporal to FSR
  temporal or FSR 1), no longer to no upscaling. iOS's Medium takes FXAA in SMAA's place (`PresetValue`; one pass for three,
  MetalFX takes the edges on again as it upscales, 430 - 460 ppi screens); High and Ultra keep SMAA. The Mobile URP asset's
  soft shadows are at Low (`m_SoftShadowQuality` 1, was Medium: fewer taps a shadowed pixel; Android and iOS). The audio
  needed nothing: 3866 clips compressed in memory and loaded in the background, none preloaded, 93 short ones decompressed
  on load, 41 tracks streamed.
- **Post-process AA** (remake, 2026-11-05, Graphics, below MSAA; `Settings.PostAntialiasing`, key `postAa`: 0 SMAA,
  1 FXAA, 2 TAA, 3 Off; `Visuals.ScreenAntialiasing`): the URP camera anti-aliasing of the cameras that draw to the screen.
  They were all SMAA as built (Space / MainMenu / Station scenes, the star map) and stay so by default; FXAA is the Low
  preset's (Android's default). A camera is registered once (`Register`: `Bootstrap.OnSceneLoaded` takes each loaded
  scene's base cameras without a target texture that have anti-aliasing; `StarMapScene`; the VR eye `Follow`s its camera)
  and `Bootstrap.ApplyUpscaling` re-applies the option to all of them after setting MSAA. URP's TAA needs MSAA off and
  is pointless under a temporal upscaler, so TAA draws as SMAA while either is on (`ScreenAntialiasing.Mode`); picking TAA
  turns MSAA off, picking MSAA takes TAA back to SMAA, and under a temporal upscaler the help says SMAA is used. The
  station camera still prefers TAA under SMAA (see "Station scene"). Ghosting of TAA on fast flight is the player's trade.
- **Texture quality** (remake, Graphics, `Settings.TextureQuality`: Full / Half / Quarter = `QualitySettings.
  globalTextureMipmapLimit` 0 / 1 / 2, Bootstrap; half in the Low preset): the space skies, reflection cubemaps and HUD / UI
  images keep every level (`AssetImport.KeepsFullMips` sets their importers' ignoreMipmapLimit: stars blurred smaller, the
  reflections pick their blur by mip level); textures made at run time (the mods') aren't affected. Android already
  compresses everything as ASTC (the project's Android default format). Mobile is always display-synced: V-Sync there
  means `targetFrameRate` = display refresh rate.

- Interface style belongs to Cosmetics > UI, including reset ownership. Remake is the default even with Original presets. Original UI is a manual legacy appearance option; experience presets preserve that choice.
- Original and Remake experience presets activate only through explicit selection. Any manual change in the corresponding Cosmetics or Gameplay section persistently selects Custom, even after values return to a preset match. Gameplay Custom and Expand share the same consent gate: accepting selects Custom without altering any rule or cheat; cancelling changes nothing.

- Kaamo search (#315) starts read-only and inactive. Only primary click, F or controller Search activates entry. Hover/incidental focus cannot capture hangar keys. Enter, Escape, outside click, tab change and reopening end entry.

- Kaamo numeric shortcuts follow visible order: 1 Ship, 2 Store, 3 Ships, 4 Blueprints when the extra tab is present. Ordinary hangars keep 1/2/3 and ignore 4. Cycling and tooltips use the same order.
