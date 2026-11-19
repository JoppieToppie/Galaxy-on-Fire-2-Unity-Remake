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
