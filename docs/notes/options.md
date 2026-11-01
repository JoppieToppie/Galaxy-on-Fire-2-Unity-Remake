# Options and localization

Notes moved from `CLAUDE.md` (its index lists every notes file). Code comments citing `CLAUDE.md "<section>"` mean the section or bullet of that name here.

## UI and platforms

- Text comes from the original table via `Localization.Get(textId)`; remake-only strings via `Extra(key, english)`,
  translated per language in `Resources/GoF2Localization/extra_<lang>.json` (a { key: text } object, loaded with the
  table by `Localization.LoadExtra`; 11 languages, KiritoJPK, PR #54, then Chinese and Hindi; a key it lacks stays
  English: add new keys there when adding Extra texts). **Languages in the menu** (`LanguageTables.Offered`, the one
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
  (Brazil), Simplified Chinese, Hindi. The UI fonts (Klavika, Inter) have no CJK or Devanagari glyphs: those come from
  the system's fonts through TextCore's OS font fallback (`TextSettings.fallbackOSFontAssets`), with correct Devanagari
  shaping (verified on Windows; Android has Noto CJK / Devanagari; Linux and the Xbox depend on the fonts installed).
  The Japanese, Korean and Arabic tables exist but aren't offered (not checked in game). Settings in `Settings`
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
together, #45), hangar depth of field (see "Station scene"), lens flare, hangar ship shadows (Off / Player ship only /
All ships, see "Station scene"), levels of detail (see "Assembled prefabs"), field of view (chase camera, default the
original's 1.22 rad), camera shake, vibration (see **Haptics**), stick dead zone, launch / arrival camera, hangar
arrival and take-off (`HangarFlight`), auto-play dialogue (the dialogue's AUTO pill), animated dialogue (`TextReveal`),
game runs in conversations (`Settings.PlayOnInConversations`, off by default, see "Dialogue" in story.md),
button hints (Gameplay, `Settings.InputHints`; "Button hints in flight" until 2026-10-24, the user's request to cover
the menus: the flight HUD's, the station's and the star map's hint rows through `InputGlyph.TrackHintsOption`, which
sets `display: none` inline as well as `.hints--off`, since a screen's own rules beat the class (`.hangar-open .station-hints`
kept the hangar window's row); the menus' prompt bars in `MenuKeys.Bar`; Inspect ship's / Action Freeze's in
`OrbitViewUi.BuildHints`, whose tappable hints stay on touch, their only Hide UI / Screenshot buttons), the flight HUD
(Gameplay tab, `Settings.HudMode`, players' request "for maximum immersion": Full / Minimal / Off, also the Toggle HUD
binding, F1, which steps through them with a HUD message; `FlightHud` puts `.hud-minimal` / `.hud-off` on the root,
FlightHud.uss hides by `visibility`, since the markers' layers, the status panel and the readout set their display in
code: Minimal hides the status bars, the radar ellipse, every marker (ships, landmarks, missiles), the lock plate, the
readouts, the hints, the cooldown icons, the hit arcs and, with keys or a controller, the throttle gauge / secondary
plate / weapon strip; Off also the crosshair, lock ring, action prompt, missile warning, charge bar, mouse reticle /
stick circle and the arrival's orbit information; always kept: the HUD messages, the radio, conversations, the menus,
the mining and hacking games, the touch controls (and on touch their secondary plate) and game over; the lens flare is
its own option), Show FPS (`FpsCounter`, its own panel over every screen; where, Graphics "FPS counter position",
`Settings.FpsPosition`, shown while Show FPS is on: the four corners and the middles of the four sides, top centre by
default, clear of the safe area; at the top centre the flight HUD's lock plate and messages move under it, `.hud-fps`,
#137), every weapon's own shot sound (`Settings.EachWeaponSound`, Sound tab), mute in background (desktop,
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
`unity.auto`, `amd.fsr1`, `unity.stp`, `nvidia.dlss4`, the newest of `amd.fsr4` / `fsr3` / `fsr2`), set again on
`RenderPipelineManager.activeRenderPipelineCreated`; the quality mode on the framework's options; device support from
the framework's own `isSupportedOnDevice` (read through the internal `UniversalRenderPipeline.upscaling`; DLSS: RTX,
D3D11 / 12 / Vulkan; FSR 2 D3D11 / 12 / Vulkan; FSR 3 / 4 D3D12, FSR 4 AMD's newest GPUs; Windows builds only; only
known once URP has made its pipeline at the first frame, after the main menu built its rows: the Upscaler row asks again
on every refresh, the Upscaler quality row shows only while DLSS / FSR 2+ is the active upscaler and the Render scale
row only while it isn't (`OptionDef.visible`; they pick their own resolution), and the main menu refreshes its rows when
Options opens). `UrpStpGuard` keeps every URP asset's upscaler priority list (STP, DLSS, FSR 4 / 3 / 2, FSR 1, Auto) so
`isStpUsed` holds and nothing is stripped; the scaling mode stays None (the Editor's views render plain). The PC asset's
opaque texture is full size (opaque downsampling None: FSR's reactive mask warned every frame with 2x). Verified in the
Editor on an RTX 5070 laptop (D3D12): DLSS (all modes) and FSR 3 in the menu, station and flight; no frame generation.
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
- **Original bloom** (`ClassicBloomPass` + `Resources/GoF2PostFx/ClassicBloom.shader`; the bloom option's "Original"):
  the original's only post effect (`AbyssEngine::BloomShader`, on from `MGame::OnInitialize` via
  `Engine::SetPostEffect(0x1400000, true)`; the radial blur 0x1400002 and glow 0x1400001 exist but are never switched
  on) as a URP render-graph pass after the post-processing, queued from `beginCameraRendering` on every base screen
  camera with post-processing (no renderer-asset change): the scene sampled into 256 × 256 through the original's
  gamma-space bright pass (Luminance 0.08, middle gray 0.005, white cutoff 0.014: about everything above half
  brightness), 6 × the "horizontal" blur (its offsets are vec2(o), a diagonal blur) and the vertical one (9 taps,
  texSize 256), then clamp(scene + glow) (`Engine::switchBloom` = 3 in the binary); URP's Bloom is off meanwhile. The
  original also blooms its HUD (drawn into the same FBO); the remake's UI Toolkit HUD doesn't.
- Frame rate option (`Settings.FrameRate`: 30 / 60 / 120 / Uncapped / V-Sync, default V-Sync; 60 on Android, the user's choice
  2026-10-22: a 120 Hz phone at 120 heats up and throttles) is applied by `Bootstrap.ApplyFrameRate`.
- **Graphics preset** (remake, 2026-10-22, the Graphics tab's first row; `Settings.PresetKeys` / `ApplyPreset`,
  `OptionsCatalog.CurrentPreset`): Low / Medium / High (the original's texts 507-509) and Ultra (2026-10-23) set render
  scale, upscaler, MSAA, bloom, hangar shadows, hangar depth of field, lens flare, other ships' engines, levels of detail,
  texture quality, fire and smoke, fog, detail distance and texture filtering at once; the row
  shows the preset the options match (render scale and MSAA as applied, 0 = the platform asset's), else Custom (picking it
  sets nothing). High = the desktop defaults (100 %, no upscaler, MSAA off with the flight's SMAA, Remake bloom, the
  player's hangar shadow, lens flare, the player-style NPC engines, LODs, fire, smoke and fog, detail High, forced
  filtering); Medium = fire, smoke and fog off, detail Medium, anisotropic filtering, 80 % with FSR 1, NPC engines
  original; Low = that with detail Low, 67 % with FSR 1, bloom off, no hangar shadows or lens flare, half-size textures;
  Ultra = every option at its most (MSAA 8×, every ship's hangar shadow, depth of field, no made levels of detail, detail
  Ultra, forced 16×) but the render scale at 150 % (the row goes to 200 %: four times 100 %'s pixels with MSAA 8× is past
  most GPUs). Android's defaults are Low (`Settings.AndroidDefaults`: each preset key's fallback, `Default`;
  keys a player set stay theirs; `OverrideAndroidDefaults` for tests) and the Original ship collision.
- **Texture quality** (remake, Graphics, `Settings.TextureQuality`: Full / Half / Quarter = `QualitySettings.
  globalTextureMipmapLimit` 0 / 1 / 2, Bootstrap; half in the Low preset): the space skies, reflection cubemaps and HUD / UI
  images keep every level (`AssetImport.KeepsFullMips` sets their importers' ignoreMipmapLimit: stars blurred smaller, the
  reflections pick their blur by mip level); textures made at run time (the mods') aren't affected. Android already
  compresses everything as ASTC (the project's Android default format). Mobile is always display-synced: V-Sync there
  means `targetFrameRate` = display refresh rate.
