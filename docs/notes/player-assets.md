# Player-provided assets

The remake built so it ships **without any of Galaxy on Fire 2's own assets**: the player gives the game their own copy
of GoF2 at first start and the game converts it (the user's decision, 2026-11-15). Work on branch `player-assets` (fork
first, proposed upstream once complete); the current `Resources` path keeps working until the new one replaces it.

## Decisions (2026-11-15)

- **Sources:** Android HD (APK + content OBB, `GOF2CONTENT_ETC`) and iOS HD (the .ipa; 1.1.19 checked) now; the macOS
  "Full HD" release later (the user will provide its files; formats not yet looked at).
- **The conversion runs inside the game**, on every platform including phones: C# ports of
  `Reference/tools/asset_conversion` (no Python or Blender on the player's machine).
- **Derived assets** (made from the originals by the remake's tools) can't ship either. The cheap ones are made on the
  player's machine after the conversion (3D planets, sky layers, reflections, the assembled prefabs); the Real-ESRGAN sky
  upscales are an **optional desktop step** ("enhance"), without it the original skies (Settings.SkyStyle Enhanced /
  Combined then use the original images).
- **What the player's copy can't provide is set aside, distributed separately, not on GitHub** (the user, 2026-11-15):
  every file whose converted form the converter doesn't reproduce exactly (KiritoJPK's "Higher quality textures and
  metallic maps", 2026-10-07; the remake's resized or edited images; derived assets that can't be made on the player's
  machine, such as the Real-ESRGAN upscales) goes into a **supplement pack**: a download of its own, installed beside the
  converted cache, layered over it. The list comes from the importers' exact comparisons against the project's files,
  not from rules.
  - **Per source:** what one edition has and another lacks goes into that edition's own gap pack (the user,
  2026-11-15): a player with the iOS or macOS copy gets the Android-only assets that way, and the reverse. So the packs
  are a common one (nothing provides it) plus one per edition (what that edition lacks). Found so far (file names,
  2026-11-15): iOS lacks 21 Supernova `.bin` (docking points, ship weapon mounts), `ar.lang`, the Vossk 063 textures
  (named `..._prototype` on iOS: an alias to map, not necessarily a gap), two hangar bot animations and the iPad 1440
  interface images; Android lacks the German voice banks and `zs` / `zt.lang`, and ~90 meshes / 27 textures iOS keeps at
  other paths (asteroid fields, explosions: to map before calling them gaps).
  - **Lower-resolution copies** (the user, 2026-11-15): iOS 1.1.19's PVRTC textures are 1/2 (359), 1/4 (47) or 1/8 (9)
  of Android's resolution. An iOS player's game runs from the iOS copy's own textures; Android's full-resolution versions
  are an optional "HD textures" part of the iOS gap pack. The same rule for any edition: what it has at lower quality is
  an optional upgrade, what it lacks entirely is required.
  - The mapping is per project asset: its source entry in each edition (aliases included) and whether that edition's
  conversion reproduces it; `Reference/tools/player_assets` gets that table, and the packs are made from it.
- Not decided here: `Reference/binaries` (the Android APK, `libgof2hdaa.so`, `libfmodevent.so`, default economy .bin) and
  `Reference/decompiled` are in the repository too; whether they stay is upstream's call. They aren't part of a build.

## Inventory (`Reference/tools/player_assets/inventory.py`, output `Reference/player_assets/inventory.json`)

Per folder, by path rule, checked against the iOS 1.1.19 app (2026-11-15):

| Kind | Folders | Files | Source in the player's copy |
| --- | --- | --- | --- |
| original | `Textures/` | 1,262 | AEI; 1,115 named as a file in the app (the rest: the remake's generated `_metallic_smoothness` maps, derived) |
| original | `Models/` (.fbx + .gof2mesh.json) | 2,255 | AEM; 1,124 of 1,127 meshes named as in the app |
| original | `Audio/` | 4,417 | FSB banks: all 22 banks' sample counts equal the folders' file counts |
| original | `Skyboxes/`, `Resources/GoF2Sky`, `GoF2Backdrop`, `GoF2Station` | 198 | AEI (cubemap strips, sun / planet images) |
| original | `Resources/GoF2Icons`, `GoF2Hud`, `GoF2Races`, `UI/Original`, `Sprite Assets/gof2_text_icons.png` | 672 | regions of the interface AEI atlases (AEI region tables) |
| original | `UI/MainMenu/card_*` | ~9 | the app's logo / key art: to be replaced by the remake's own, not converted |
| original | `Localization/text_*`, `GoF2_strings.csv` | 15 | `.lang` files (the remake's own translations nl / hi / zh / ar...: see below) |
| original | `Resources/GoF2Data/` | 53 | `.bin` files and the resource table (per file to sort out: some tables are the remake's) |
| original | `Shaders_Original_GLSL/` | 23 | the app's shader source; reference only, nothing converts it |
| derived | `Resources/Assembled/`, `Prefabs/`, `Materials/` | 1,950 | built at run time from the converted meshes, textures and `assemblies.json` / `resources.json` |
| derived | `Resources/GoF2SkyHd`, `GoF2SkyLayers/patch_*`, `GoF2Planets3D`, `GoF2Reflections` | 254 | made on the player's machine (SkyHd and its patches: the optional step; Planets3D: distributed separately, below) |
| own | everything else (scripts, shaders, settings, fonts, the app icon (`UI/AppIcon`), `GoF2Mods`, `GoF2Localization` extras, the `GoF2Weapons/item_*` fx tables: their references refilled...) | ~1,550 | ships |

**Hard references** (an own asset pointing at an original or derived one by GUID; these can't stay): 19 files.
- Sound tables: `GoF2Story/StoryAssets` (4,307 clips), `GoF2ModBuilder/GameAudioIndex` (330), `GoF2Combat/CombatAssets`
  (120), `GoF2Events/EventAudio`, `GoF2Story/SupernovaAssets`, `GoF2Weapons/CombatAudio`, `GoF2StarMap/StarMapAssets`,
  `GoF2Music/MenuTheme`; the Station (69 clips) and MainMenu (31) scenes. All name lookups: the content layer serves a clip
  by its bank and name.
- `GoF2LanguageTables` (the 14 text tables), `GoF2Mods/ModAssets` (3 materials), the Space scene's `SpaceSky.mat`.
- The six build profiles' app icons (`UI/AppIcon`): the remake's own icon art (icon_source.png, made 2026-10-05, every
  platform's icons built from it by MainMenuBuilder.BuildAppIcons) ships, the user's call 2026-11-15 (like the title logo).

**`Resources.Load`:** 161 calls in 102 files; those loading originals go through the content layer.

## Formats in the player's copy

- **iOS HD 1.1.19** (`Payload/Galaxy on Fire 2 HD.app/`, data files not encrypted; only the executable is, FairPlay):
  `data/textures`, `data/meshes`, `data/assets/{main,supernova,valkyrie}/...` (1,215 .aem, 1,163 .aei), `data/bin` (30
  .bin), 12 `.lang`, 22 FSB5 banks + one .fev, `data/shader` (.vert / .frag).
  - AEI types: 0x01 RGBA8 (702), 0x03 RGBA8 + alpha (2), 0x81 RGBA8 cubemap strip (23), and PVRTC: 0x10 (126), 0x12
    (307), 0x0d (2), 0x24 (1). `aei.py` reads only the RGBA8 and ETC1 types: PVRTC 2 / 4 bpp needs a decoder.
  - FSB5 codecs: MPEG (4,121 samples: voices, music), IMA ADPCM (291: effects), PCM16 (4). No Vorbis (no setup-header
    rebuilding); MP3 needs a managed decoder (or Unity's own where the platform has one), IMA ADPCM is a few lines.
- **Android HD 2.0.16** (`net.fishlabs.gof2hdallandroid2012`; checked from the user's XAPK, 2026-11-15): the APK holds the
  code only; the content is the OBB `main.47947006.net.fishlabs.gof2hdallandroid2012.obb`, a zip (2,466 entries) of
  `47947_GOF2CONTENT_ETC/assets/...`: 1,145 .aei, 1,127 .aem, 51 .bin, 11 .lang, 17 FSB5 banks + .fev, the shaders.
  - AEI types: 0x01 (684) and 0x03 (258) RGBA8, 0x81 cubemap strips (23), 0x42 ETC1 with mips (180): all read by `aei.py`.
  - FSB5: the same codecs as iOS (MPEG, IMA ADPCM, PCM16).
  - An XAPK is a zip holding the APK and `Android/obb/<package>/main.*.obb`: the converter takes the XAPK, the OBB or a
    folder holding either.
- **What each copy has** (against the project's 22 banks, 2026-11-15): Android has no German voices (`VOICE_deu`,
  `DLC_VOICE_deu`, `DLC2_VOICE_deu`, `GENERIC_deu`, `LOUNGE_deu`) and no Chinese text; iOS has no Arabic text (`ar.lang`)
  but both Chinese tables (`zs`, `zt`). Every other bank has the same samples in both. The converter takes either or both
  (layered); without German voices the game falls back to the English ones.

## Importers so far

- **AeiImage** (`Scripts/Runtime/Core/Content/AeiImage.cs`, GoF2.Core, plain C#; 2026-11-15): the header and regions,
  RGBA8 / cubemap strips, ETC1, PVRTC 4 and 2 bpp, level 0 only. Checked outside Unity (a console harness on the
  Editor's own .NET SDK): Android 2.0.16's 1,145 textures, 0 failures, byte-identical to the project's original
  conversion wherever the remake didn't edit a texture later (438 of 447 against the files before KiritoJPK's 2026-10-07
  update; the 9 others and 12 resized are the remake's edits: the Vossk hangar and stations, the suns); iOS 1.1.19's 435
  PVRTC textures byte-identical to texture2ddecoder. The words' Morton order puts y in the even bits (x there decoded each
  image mirrored about its diagonal). Type 0x24 (iOS' unused cloak map) isn't read.
- **AemMesh** (`Scripts/Runtime/Core/Content/AemMesh.cs`, 2026-11-15): parts, geometry, keyframes and the
  `.gof2mesh.json` sidecar (convert_remodel.py's format, what PartAnimation reads). Android's 1,127 and iOS' 1,215 meshes
  read with geometry identical to aem.py's (one iOS file ends inside its last part, as the game's own may: dropped as
  aem.py drops it; `v_ship_040_deep_science` carries 12 NaN UVs in both copies). The sidecars equal the project's for
  1,093 of 1,126; the other 33 are the remake's remodels (the contributed Midorian hangar room 2026-10-07, the Vossk /
  Nivelian hangars, the remade Vossk bar 2026-10-25, a few Supernova / Valkyrie add meshes): common-pack material.
  Still to do: the Unity mesh itself, in the space the FBX import gives (aem2fbx's axes, Blender's validate, Unity's
  x flip and ModelOrientationPostprocessor's 180 deg).
- **FsbBank** (`Scripts/Runtime/Core/Content/FsbBank.cs`, 2026-11-15): FSB5 banks, each sample as a file ModAudio loads:
  MPEG as its own frames (`.mp3`, the bank's zero padding between frames dropped: lossless), IMA ADPCM decoded to a 16-bit
  `.wav` (Xbox IMA: per channel and 0x24-byte block the header's sample, then the first 63 nibbles; the 64th is never
  played), PCM16 as a `.wav` trimmed to the sample count (its data is padded). Checked on the user's copies: 4,416 iOS and
  2,373 Android samples, every name one of the project's audio files; the 224 IMA / PCM samples sample-identical to
  vgmstream's decode, the MPEG ones the same length and within 2 LSB of vgmstream's (mpg123 vs ffmpeg rounding, 73 dB+).
- **LangFile** (`Scripts/Runtime/Core/Content/LangFile.cs`, 2026-11-15): the `.lang` text tables (big-endian u16 length +
  UTF-8, string after string) to text_<lang>.json (gb -> en, ptl -> pt, zs -> zh, zt -> zh-Hant). Android's tables equal
  the project's except the remake's corrections (3,392-3,399 of 3,401 a language: "Rockets", "S'kolptorr", "your next
  target", lowercase gods, 2026-11-01) and Arabic (Android's `ar.lang` is an older 3,291-string table; the project's was
  realigned and completed, #224, 2026-11-03 / 05). iOS' tables have 3,402 strings and wording of its own (English 3,281
  the same, Portuguese 2,961, Chinese: the original's `zs` vs the remake's PR #50 table 2,124). The corrections, the
  remake's own translations (ar, nl, hi, zh) and an edition's differing strings are string overlays in the packs.
  - iOS' `v_galaxymap_planets` has no alpha (Android's ring quadrant is translucent): a real difference between the
  copies, so that texture is in the iOS gap pack.

- **GameTables** (`Scripts/Runtime/Core/Content/GameTables.cs`, 2026-11-15): the data tables from the copy's .bin files and
  English .lang (ports of parse_bins.py, parse.py + export.py with items.json's statList / `"blueprint": []`, and
  build_item_attributes.py), values written in the scripts' key order (JsonObj, TableJson). Android: 40 tables, 39 equal
  to the project's by value, items.json differing only by the remake's text fix (so the string overlay goes on the
  English table before the tables are built); iOS: 19 tables, 15 equal, the other 4 (items, item_attributes, ships,
  weapons_hd) its Default Economy's numbers, as traced.

## The packs (`Reference/tools/player_assets/packs.py` -> `Reference/player_assets/packs.json`, 2026-11-15)

From the converter's own output for each edition (`Reference/tools/player_assets/convert`), against the project's files:
exact / lower (the same picture at a lower resolution, or the same size encoded worse, 30 dB+) / differs / missing. Per
edition an asset is required (missing or differs) or optional (lower: the edition's version works, the project's is an
upgrade). First run (Android 2.0.16 XAPK, iOS 1.1.19 ipa):

| | Android | iOS |
| --- | --- | --- |
| exact | 4,165 | 5,993 |
| lower | 369 | 399 |
| differs | 87 | 261 |
| missing | 2,047 | 15 |

- **Common pack:** required 58 (the remake's remodelled meshes, edited textures, `sn_sun_012`, `hangar_terran_alpha_diffuse`
  and the like: in no copy as the project has them); **optional 388** (KiritoJPK's 2026-10-07 textures: upscales and
  cleaner encodes of pictures both copies have, an "HD textures" pack; perhaps the macOS Full HD release's own, to check
  when its files come).
- **Android's gap (two editions, before macOS and the tables; see "The macOS copy" for the current numbers):** 2,076, nearly all the five German voice banks (2,043 samples). **iOS' gap:** 218: Android's versions
  of hangar meshes iOS 1.1.19 has in other versions (143 main-game sidecars differ: vertex counts, keyframes), the
  interface images it lacks, 17 more optional upgrades.
- **Text:** a string overlay per language of the remake's corrections and translations (see LangFile); iOS' own wording
  where it differs (English 120 strings, Portuguese 440, French 33).

## Data tables (`Resources/GoF2Data`, traced 2026-11-15 by re-running the generators on both copies)

- **42 rebuild from the player's .bin / .lang** byte-identical (`parse_bins.py`: agents, wanted, names, ticker, collisions,
  docks, weapons_hd / sd, the `sn_*` / `v_*` tables; `parse.py` + `export.py`: ships, systems, stations, items (plus 3 text
  fixes: an overlay)); `item_attributes`, `economy_default`, `weapons_hd_default` from those.
  - The economies differ per copy: iOS' `items.bin` / `ships.bin` / `weapons_hd.bin` are the Default Economy's (byte-
    identical to `Reference/binaries/default_economy`), Android's the main one; so each edition's gap carries the other
    economy's numbers. The 21 `.bin` iOS lacks are the Supernova / Valkyrie tables (`sn_*`, `v_*`, docking points and weapons), read at run time: in iOS' gap pack (packs.py compares the tables since 2026-11-15; the macOS app lacks the same 21).
- **No original bytes, recovered facts:** `resources.json` (the resource table, from emulating Android's
  `BuildResourceList` + disassembly IDs + the remake's additions), `assemblies.json` (the remake's composition rules),
  `story.json` and `voice_table` (IDs and FMOD event names from the .so); weapon_fx (a hand table). Not rebuildable from
  an iOS copy (its executable is encrypted). **They ship with the game** (the user, 2026-11-15: facts, no art, text or
  code of the original's, and every edition needs them).
- **The remake's own:** step_summaries, hangar_heights, nozzle_shapes, void_station_extra, generated_lods (derived from
  the meshes: rebuildable on the player's machine with the run-time simplifier). `_bin_decode_report`: unused, drop.

## The content layer: what has to run in the game (mapped 2026-11-15)

The originals reach the game two ways: loaded as they are (textures, clips, text, data), and through **editor builders**
that make Resources folders from them. Each builder becomes a run-time step over the converted cache:

| Builder (editor-tools.md) | Makes | Run-time replacement |
| --- | --- | --- |
| Materials And Prefabs (`PrefabBuilder`) | `Materials/`, `Prefabs/` (a material per game material, a prefab per mesh, from resources.json) | meshes from the cache's .aem in the FBX import's space, materials by shading kind |
| Assembled Prefabs (`AssemblyBuilder`) | `Resources/Assembled/` (319) from assemblies.json | templates built on first use, as ModShips.Template does for mods |
| HUD Images, Item Icons, Hull Icons, Text Icons | `GoF2Hud`, `GoF2Icons`, `GoF2Races`, `Sprite Assets/gof2_text_icons` | cut from the interface atlases (AEI regions) after the conversion; the text icons' atlas drawn into its placeholder ("Text icons") |
| Space Skies, Sky Layers, the skyboxes | `GoF2Sky`, `GoF2Backdrop` sky materials, `Skyboxes/` | cubemaps from the cubemap strips |
| Combat / Star Map / Event Audio, the story's | `CombatAssets`, `StarMapAssets`, `EventAudio`, `StoryAssets` (4,307 clips)... | clips by bank and name from the cache |
| Hangar Shadows, reflections, 3D planets, generated LODs | `GoF2Station/ShipShadows`, `GoF2Reflections`, `GoF2Planets3D`, `generated_lods` | made after the conversion (cheap) |

Testing it needs a Unity project without the originals: this branch's worktree with the original and derived folders
left out (sparse checkout) is exactly a shipped build's content, a fast import, and the cache supplies the rest.
- **The test bed** (2026-11-15): a worktree (`git worktree add`, `GIT_LFS_SKIP_SMUDGE=1`) with a non-cone sparse checkout
  made from the inventory (every original / derived file and its .meta left out; `stripped_checkout.py` makes and
  updates it);
  1,470 asset files, 53 MB. **Don't run a plain `git lfs pull` there:** git-lfs writes every LFS file into the working
  tree, the sparse ones too; remove them and `git sparse-checkout reapply` if it happens (or pull with `--include`).

## Content layer so far

- **GameContent** (`Scripts/Runtime/Core/GameContent.cs`, 2026-11-15): the converted cache (`GamePaths.Data/Original`, its
  `_manifest.tsv` marks it installed) first, Resources as the fallback: `Text` / `Bytes` (a table by its Resources path),
  `LanguageText` (text_<code>.json). The data tables (Database, CollisionVolume, Agent, GenericVoice, NewsTicker,
  SpacePoints, StepSummaries, StoryTable, GameLods, the Mod Builder's voice list) and Localization's tables read through it
  (Localization takes the cache's language without a table asset). `PlayerCopyTests` (EditMode, ignored without a
  converted copy: `$GOF2_ORIGINAL_CACHE` or the tools' cache_android) pass in the test bed: the database and the text from
  the player's Android copy with no original in the project.
- **OriginalModels** (`Scripts/Runtime/Space/Content/OriginalModels.cs`, 2026-11-15): OriginalTextures (a cache PNG as the
  importer set it up; the game's normal maps, specular in alpha, rewritten (1, G, B, R) so URP reads X right on desktop and
  Android), OriginalMaterials (PrefabBuilder.CreateMaterial + PostProcessing.ApplyGlow), OriginalModels (a model from the
  cache's .aem as its per-mesh prefab: parts, PartAnimation from the sidecar as a run-time TextAsset, ResourceInfo) and
  OriginalAssemblies (AssemblyBuilder: templates kept under an inactive holder); AssembledObject.LoadPrefab falls back to
  them when Resources has no prefab. The converter now writes the `_metallic_smoothness` maps (split_spec.py's: metallic
  0, smoothness the normal map's alpha). **The mesh space** (OriginalModelsTests, 2026-11-15, three models imported into
  the test bed against the same models built from the cache: parts, bounds, signed volume all equal): a vertex (x, y, z)
  of the original -> (-x, y, z) x 0.05, normals (-x, y, z), the triangles' order reversed. All 319 assemblies build from
  the Android copy in the test bed (1,937 renderers, 47 s for all of them in edit mode, their textures included): the
  remodelled ones from the original meshes until the common pack replaces them.
- **Sounds** (2026-11-15): OriginalAudio (GoF2.Game): a cache sound as an AudioClip on the spot: WAV read as it is, MP3 by
  **NLayer 3.0.1** (`Assets/Plugins/NLayer`, MIT, the user's choice over background preloading; matches ffmpeg on all 2,078
  Android MP3s: equal lengths, no dropped frames), over 30 s streamed (a PCM reader on the MP3). **OriginalRefs**: the own
  assets' references to originals, recorded by `Reference/tools/player_assets/original_refs.py` in
  `Resources/GoF2Content/original_refs.json` (field paths -> the original's project path; 4,314 in StoryAssets, the combat,
  event, star map, menu, Supernova and weapon tables, the language table, the Station / MainMenu scenes' components), filled
  when a table loads (`OriginalRefs.Load` in place of Resources.Load) or a scene opens (sceneLoaded, by class and GameObject
  name); only empty fields, so a build with the originals is unchanged. Lists of more than 64 entries (the story's voices,
  the Mod Builder's audio index) are filled entry by entry when asked (`OriginalRefs.Entry`, StoryAssets.Voice,
  GameAudioIndex.Clip): all at once took ~20 s. Materials and prefabs: see "Materials, prefabs and the backdrop". Not
  yet: ShipShadows and the story's portrait parts (the build profiles' app icons are the remake's own: they ship).
- **Interface images** (2026-11-15): DerivedImages (GoF2.Core, plain C#): the HUD images (HudImageBuilder's table, moved
  here and shared with the builder) and the shop's item / ship icons and bare ship frame (ItemIconBuilder's; the rects in
  `ItemIconTable.cs`, generated from `Reference/research/item_icons.json` by `Reference/tools/player_assets/item_icon_table.py`),
  cut by the converter from the decoded interface atlases (both copies have all 11, the same sizes) into
  `Resources/GoF2Hud/*.png` / `Resources/GoF2Icons/*.png` in the cache: against the project's, 237 HUD images pixel-
  identical and 45 within 4 / 255 (the edge bleed's float averaging), the 297 icons and the frame within 2 / 255.
  `OriginalTextures.Image(<Resources path>)` (Resources first, else the cache, set up as UI images) replaces the
  `Resources.Load<Texture2D>("GoF2Hud/..." / "GoF2Icons/...")` calls. Not made: the remake's own `option_*` icons and
  `ship_ring_open_*` markers (2026-10-17 / 21: common pack), `HullIconBuilder`'s rendered hull icons (rendered models). The race emblems (`GoF2Races`, KiritoJPK 2026-10-13, added
  by hand) aren't crops of any atlas (searched both copies' 11 atlases for each, within 2 / 255: none): resized or edited,
  so common-pack material.
- **Skies** (2026-11-15): OriginalSkies (GoF2.Space): `Sky("stars_N" / "nebula_N")` renders SkyboxBaker's layers (the
  original's sky meshes and textures, GoF2/Unlit stars or GoF2/Additive nebula, seen from inside) exactly as
  `SkyboxBaker.BakeCube` does (a camera per face, 90 degrees, black behind, read back into a 6:1 strip; 2048 faces, 1024
  on phones; nothing without a GPU device): against the editor's baked strips the same layout and orientation, colours
  within ~1 / 255 on average (the source texture's compression). The strip becomes a cubemap as Unity's import makes one
  (`FromStrip`: +X, -X, +Y, -Y, +Z, -Z left to right, or top to bottom in a 1:6 strip, each face's rows from the top;
  `CubeStripTests` compare it face by face with a cubemap the importer made of the same strip), each face DXT1 / ETC2
  with mipmaps (ModTextureEncoder), copied into the cube on the GPU and stored (below). `Reflection(name)` makes a Cubemap
  of the original's own 6-face strip (128 px faces; Resources/GoF2Reflections are 4x upscales of them: an optional
  upgrade). OrbitBuilder's skies and SkyReflection read through them.
- **Materials, prefabs and the backdrop** (2026-11-15): `OriginalBackdrop` (GoF2.Space) makes the far-plane materials
  of `Resources/GoF2Backdrop` from the converted copy as `SpaceSceneBuilder` made them: suns (GoF2/Backdrop, One One,
  queue 2900), planets and rings (alpha, 2901; `sn_planet_ring` 2902), space dust and fog (GoF2/SpaceDust), the sky
  layers' `sky_*` (GoF2/SkyLayer; the asteroid belt with its normal and specular maps); the copy's 67 planet / sun / ring
  textures are exactly the project's 67 backdrop materials. Backdrop, ModBackdrop's templates, the star map's supernova
  sun and the Mod Builder's texture list read through it. `OriginalContent.Resolve` (reached from OriginalRefs through
  `GameHooks.ResolveOriginal`) makes every material and prefab the remake's own assets point at (106 distinct in the
  manifest, `PlayerCopyTests`): `Materials/mat_<id>_*` (OriginalMaterials.For), `Prefabs/*` (a per-mesh prefab,
  `OriginalAssemblies.ModelTemplate`), `Resources/Assembled/*`, the backdrop's, `Materials/sn_galaxymap_sun_012`
  (StarMapBuilder's copy of 27307 with sn_sun_012) and the story intros' `Skyboxes/skybox_N` (the old combined bakes:
  `OriginalSkies.Skybox`, stars_(N % 3) under nebula_N rendered into one cube). Checked by eye on three orbits (the
  backdrop planets, the Supernova system's sun and haze).
  - **Templates and Entities worlds:** the templates live under a DontDestroyOnLoad holder made from a scene the game
    loaded. Bootstrap's BeforeSceneLoad installers load tables (FpsCounter -> StarMapAssets), so OriginalRefs holds their
    prefab references until the game's first scene (not the test runner's InitTestScene): a holder made before it is in
    another Entities World, and every model parented under it logged "A transform hierarchy holds entities of more than
    one Entities World" (Unity 7's entity-backed transforms), which failed the play-mode tests.
- **Weapon fx, hangar shadows, sky masks, 3D planets** (2026-11-15):
  - `Resources/GoF2Weapons/item_*` (WeaponBuilder's fx tables from `weapon_fx.json`: projectile / muzzle / impact
    prefabs, shot and explosion sounds) are the remake's own: they ship with their references emptied, and `WeaponFx.Load`
    goes through `OriginalRefs.Load` (their 383 references in `original_refs.json`: sounds, `Prefabs/`, `Resources/
    Assembled/`, all made by OriginalContent). Reclassified in inventory.py / inventory.json and checked out in the test bed.
  - The hangar ships' shadow maps (`GoF2Station/ShipShadows`): the game's ships built from the copy have readable meshes,
    so `ShipShadowBaker` bakes them as it bakes the mods' ships, kept in `GameContent.GpuCache/shadows` (baked once).
  - `GoF2SkyLayers/star_mask`, `nebula_masks`, `cloud_masks` are seeded noise (sky_layers.py, nebula_clouds.py): own, they
    ship. The `patch_*` come from the Real-ESRGAN skies: with the optional desktop step.
  - `GoF2Planets3D` (the 3D planets' maps): rebuilt from the planet pngs by tools that aren't in the repository
    (planet_rebuild, 2026-11-10), so the game can't make them: set aside to be distributed separately (the user's rule for
    what the player's copy doesn't provide). Without them `RebuiltPlanets.Has` is false and the planets are the flat
    images (OriginalBackdrop), as with the option off.
- **Builds without the originals** (2026-11-15): a checkout without the original and derived assets (the test bed's
  sparse checkout) is exactly what such a build ships; building from it needs only:
  - **The runtime materials' shader variants:** a player build keeps the variants some material in it uses, and no
    material of the build uses URP/Lit with a normal map, transparent with emission, GoF2/Hull's bicubic variant...
    `Resources/GoF2Content/ShaderTemplates` holds one template material per shader and keyword set the content layer
    makes (`OriginalContent.EveryMaterial`: the resource table's, the backdrop's, the sky layers', the galaxy map's sun,
    the skies' plain GoF2/Unlit / Additive and Skybox/Cubemap; 14 from the Android copy), each texture slot that had an
    image given a 4 x 4 placeholder of its own (URP's material check drops `_NORMALMAP` / `_METALLICSPECGLOSSMAP` from a
    material saved without the texture). **GoF2 > Build > Runtime Shader Templates** (`RuntimeShaderTemplates`, needs a
    converted copy; batch: `-executeMethod GoF2Remake.EditorTools.RuntimeShaderTemplates.BuildAndQuit` with
    `$GOF2_ORIGINAL_CACHE`) writes them; `PlayerCopyTests.EveryRuntimeMaterialHasItsShaderTemplate` fails when the content
    layer makes a variant without one.
  - **The checkout to build from:** `Reference/tools/player_assets/stripped_checkout.py create <dir> [rev]` makes a
    worktree without every file inventory.py calls original or derived (a non-cone sparse checkout: whole folders where
    nothing else is in them, single files with their .meta otherwise; 722 rules) and fetches only the LFS files kept (126,
    ~60 MB of assets); `apply [<dir>]` rewrites an existing one's rules after the inventory changed (and removes what a
    stray full `git lfs pull` wrote); `patterns` prints them. Its rules are the test bed's exactly. Release builds made in
    such a checkout (the usual GoF2 > Build menu items, Runtime Shader Templates current) ship without the originals;
    CI needs no LFS originals for them either.
  - **A build to check:** `PlayerAssetsBuild.LinuxDevelopment` (batch: `-buildTarget Linux64 -executeMethod
    GoF2Remake.EditorTools.PlayerAssetsBuild.LinuxDevelopment`, `$GOF2_BUILD_OUT`) makes a Linux development build of the
    checkout; run as `gamescope --backend headless -- GoF2Remake.x86_64 -gof2check <folder>` (a desktop session would show its window) with `$GOF2_ORIGINAL_CACHE`, `PlayerAssetsCheck`
    (development builds only: `Debug.isDebugBuild`, Unity 7 defines no DEVELOPMENT_BUILD for scripts; so does `$GOF2_ORIGINAL_CACHE`) waits for the menu, flies three orbits and docks a station, renders each view into
    `<folder>/<name>.png`, counts the renderers whose material lost its shader and writes `<folder>/report.txt`. `-gof2fps <seconds>` adds a frame-rate sample of each view (uncapped; average, median, 1 % low) at the window's size: e.g. `gamescope --backend headless -W 3840 -H 2160 -r 500 -- ./GoF2Remake.x86_64 -force-vulkan -screen-fullscreen 1 -screen-width 3840 -screen-height 2160 -gof2check <folder> -gof2fps 15` (headless gamescope hosts Vulkan; without `-force-vulkan` the player took OpenGL there). 2026-11-16, HD on, RX 7900 XTX: 1440p 1170–1430 fps average, 4K 640–940.
- **The macOS copy** (2026-11-15, the user's "Galaxy on Fire 2 Full HD.zip", the Mac App Store app zipped): converted
  in-game like the others (`X.app/Contents/Resources/` is its data root: `DataPath` strips it, so its `.bin` tables are read).
  - **DXT textures:** its `data/assets/<pack>/3d/textures/high/dx5` (and a smaller `low/dx5` set the converter skips) are
    AEI types 0x20 / 0x22 = DXT1, 0x24 / 0x26 = DXT5 (bit 1 mipmaps; 4 / 8 bpp by their sizes; DXT5 rather than DXT3 by the
    Android copy's same textures: colour within 0.1 / 255, alpha equal, rows top first). `Dxt` in AeiImage.cs decodes
    them; its interface atlases are RGBA8 / PVRTC like iOS'.
  - **Its text tables are laid out differently:** 3,385 strings to Android's 3,401 (the same strings in the same order with
    the Android version's later additions missing, a few reworded). `LangMaps` (generated by
    `Reference/tools/player_assets/lang_map.py` from the two English tables: equal runs, reworded strings paired by
    similarity) puts each macOS language's strings at the Android ids the remake uses, before the data tables are built
    from them; 34 Android ids have no macOS string (the Kaamo Club storage, blueprint autocomplete, newer add-on blurbs, a
    few reworded texts): left empty for the packs' text overlay. Without the map every name came out shifted (ship 0 was
    "Hernstein").
  - **What it provides** (packs.py with all three editions): exact 6273, lower 145, differs 240,
    missing 50; every sound including the German voices (Android lacks those). Its gap: the 21 Supernova /
    Valkyrie data tables (no `.bin` for them in the app, like iOS), the Default Economy's `ships` / `item_attributes` /
    `weapons_hd` (as iOS), four portrait parts it has only at the smaller `_ipad` size, and its own versions of some
    textures. `PlayerCopyTests` on the macOS copy: everything but those portrait parts.
  - **Packs with three editions:** common required 30 (with macOS only what no edition has), optional 137; gap
    Android 2105, iOS 271, macOS 260; optional HD Android 249, iOS 268, macOS 14. packs.py now compares the
    data tables too (`items.json` is in the common pack: the remake's three text fixes in its names).
- **Packs** (2026-11-15): what the player's copy doesn't provide, distributed separately, never on GitHub.
  - **Format:** a zip (`.gof2pack`) of files at their converted copy's paths ("Textures/main/x.png", "Resources/
    GoF2Planets3D/009/albedo.png") and `gof2pack.json` (name, title, version, order: common 0, gap 1, HD 2; files, bytes).
    `OriginalPacks`: a picked pack (the setup screen tells it from a copy by its manifest) is kept in `GamePaths.Data/
    Packs` (an older version of it replaced) and its files written over the converted copy, recorded in the copy's
    `_packs.tsv`; a conversion writes every kept pack again (before the copy is swapped in), and a pack kept since (put in
    the Packs folder) is written at the next start (StartupSplash: the setup screen's progress, then straight on). Paths
    outside the copy are refused.
  - **The HD pack** (the user's choice 2026-11-15, for the 3D planets: "add them to the hd pack"):
    `Reference/tools/player_assets/make_packs.py --project <the full project>` writes `gof2_hd.gof2pack` (to
    `~/GOF2UnityRemakeMods/packs` by default): the HD textures (every edition's optional list in packs.json and the common
    optional ones, each normal map with its metallic / smoothness map), the Real-ESRGAN skies and their star lists
    (GoF2SkyHd: in the pack rather than a desktop step), the sky patches made from them (GoF2SkyLayers/patch_*) and the 3D
    planets (GoF2Planets3D, made by the planet_rebuild tools in ~/Downloads/planet_rebuild/pfit, the user's own): 759
    files, 2.3 GB. Loaded from the copy where the build has none: RebuiltPlanets (planet.json via GameContent.Text, the
    maps via `OriginalTextures.Data`: linear, uncompressed as their importer's BC7 / DXT1 choice demanded), OrbitBuilder's
    HD skies (`OriginalSkies.Hd`: the 6:1 strip made a compressed cubemap once, in the GPU cache) and sky patches, SkyStars'
    star lists (GameContent.Bytes). Checked in a Linux build without the originals: the pack written at start, 26 3D
    planets, the HD skies and star lists, no lost shaders, the renders right.
  - **HD resources** (the user's request 2026-11-15: "a window prompt asking if the user wants to supply them", in "a
    seperate folder from the original games assets", "an override unless the user turns them off", or replacing the
    originals "to conserve space"): the HD pack isn't written over the copy like the others.
    - **The question:** after the first conversion (and at a start with a copy but no HD resources, until answered) the
      setup window asks whether the player wants to supply the HD pack (`OriginalSetup.HdPromptDue`, PlayerPrefs
      `hdAsked`): "Choose the HD pack…" or "Not now". Options > Graphics "HD resources…" opens the same window over the
      options (`HdWindow`), and "Add a pack…" given the HD pack goes there too. An HD pack picked on the first-start
      screen waits for the conversion, then the same choice.
    - **Two ways to install it** (the window shows what each takes, `OriginalPacks.HdSpace`): *Keep the originals too*:
      into `GamePaths.Data/HD` (`GameContent.HdRoot`, its own `_packs.tsv`), made as `HD.new` and swapped in; the content
      layer reads it before the copy (`GameContent.CachePath`) while Options > Graphics "Use the HD resources"
      (`Settings.HdResources`, PlayerPrefs `gof2.hdResources`, default on, reset by the Graphics tab's defaults) is on, from the next start
      (`HdActive` is read once; a rendered sky's GPU cache file carries `_hd`). It outlives a conversion of the copy.
      *Replace the originals*: written over the copy (`_packs.tsv` records it): the space of the originals it replaces
      saved, no way to turn it off but converting the copy again (which then asks again: `Job.HdLost`). PlayerPrefs
      `hdMode` remembers the choice for a kept HD pack (the server's `-pack`).
    - **Later:** with the HD folder installed, the window offers to move it over the copy (`OriginalSetup.MergeHd`: the
      files moved, the originals' space freed) or to remove it. The pack file itself is never kept (2.3 GB): a phone
      picker's copy is deleted after the install, the player's own file left where it is.
    - Tests: `OriginalSetupTests.TheHdResourcesLiveApartAndOverrideTheCopyUntilTurnedOffOrMerged`; FirstStartTests
      answers the question with "Not now".
  - **The common and gap packs** (2026-11-15): `make_packs.py --project <full project> --edition android=<cache>
    --edition ios=<cache> --edition macos=<cache>` writes `gof2_common` (order 0), `gof2_gap_<edition>` (order 1) and
    `gof2_hd` (order 2). Each asset in the converted copy's format: textures and data tables the project's (and the Default
    Economy overlays `economy_default` / `weapons_hd_default`, which no copy makes); a sound or model another edition's
    conversion has exactly as the project has it, that edition's converted file (Android's German voices from the Apple
    copies as .mp3; the Apple editions' meshes from Android as .aem + sidecar); the remake's own remodels (13: the
    remodelled hangars, the Deep Science ship and hangar, sn_station_113's addition, the Supernova wreck, a Vossk bar bot)
    as .aem written by `PackModelExporter` (Editor, batch `-executeMethod
    GoF2Remake.EditorTools.PackModelExporter.ExportListed`, `$GOF2_PACK_MODELS` a list of sidecars, `$GOF2_PACK_OUT`) from
    Unity's import of the project's FBX (OriginalModels.Map inverted, the parts as the sidecar has them; each file read back
    with the runtime's AemMesh + BuildMesh and compared with the FBX: all 13 the same to 0 m), with the project's sidecar;
    the text tables the project's, every language, in the common pack. Sizes: common 51 files, 30 MB; gap Android 2,121,
    215 MB; iOS 454, 94 MB; macOS 444, 123 MB; HD 759, 2.3 GB. **Checked:** each edition's converted copy with the common
    pack and its gap pack is the project's everywhere but the "lower" assets (packs.py on the packed copies: required 0,
    the text exact), and every "lower" asset is in the HD pack; PlayerCopyTests pass on the packed macOS copy.
  - **packs.py's comparison** no longer counts what the game reads the same: Blender's `.001` suffixes on the FBX-made
    parents' names, an all-zero bounding sphere / pivot against none, infinities in keyframes (inf - inf is NaN).
  - **Development builds:** `$GOF2_DATA` points `GamePaths.Data` elsewhere (PlayerAssetsCheck with its own Packs folder).
- **The menu's images** (2026-11-15): the inventory's rule for the campaign cards named `UI/MainMenu/card_`, but they are
  in `UI/MainMenu/Images/`, so the stripped builds carried them, and the FISHLABS and GoF2 logos (all cut from the
  original's atlases by MainMenuBuilder): now derived. `OriginalTextures.MenuImage` cuts the same rectangles of the converted
  copy's atlases (`gof2_campaign_select_ipad_large`: 605 x 943 cards on a 626 x 966 pitch; `gof2_logos_1440`) at run time
  (an atlas a pack brought too, the macOS copy lacking the cards' sheet): MainMenuSetup puts the cards on their elements
  in a build without the originals, StartupSplash the FISHLABS logo when its own is missing. The cards come out identical
  to the project's; the project's FISHLABS logo is a larger version (1561 x 1741) than the atlas' 266 x 303 the cut gives.
  `logo_gof2_remake.png` (the title logo, also the photo mode's) is #67's redraw of the game's logo with "REMAKE" under it:
  it ships with the build (the user's call, 2026-11-15, after a moment in the HD pack).
- **Text icons** (2026-11-15): the dialogue's inline `<sprite>` icons (race emblems, gate, coin, every shop icon) come
  from `Resources/Sprite Assets/gof2_text_icons`. Its sprite table (`.asset`: names and 64 px cells) is the remake's own
  and ships; its atlas (`.png`, cut from the HUD and shop icons) is a placeholder in a build without the originals
  (`stripped_checkout.py` ASSET_SLOTS), which UiImageSlots draws from the copy's `GoF2Hud` / `GoF2Icons` cuts by the
  table's cells (`TextIcons.Compose`, the drawing code the editor's Build Text Icons uses too; the coin and the heart
  are drawn). Found by the stripped bed's edit-mode run (`TextRevealTests.EveryIconTheTextUsesIsInTheSpriteAsset`).
- **Stylesheet images** (2026-11-15): 58 images the USS name by path (`url("project://database/...")`) are the
  original's or cut from it: the Original interface style's pieces (`UI/Original/og_*`), the flight HUD's crosshairs
  (`UI/Flight/Images/crosshair*`: cut from gof2_interface_iphone4, now classified derived), the plasma gun's
  (`GoF2Hud/plasma_crosshair*`), the campaign cards. Code can't swap them (their states, :hover / :focus / the `_on`
  images, live in the stylesheets), so `stripped_checkout.py` keeps each one's .meta (GUID) and writes a 4 x 4
  transparent placeholder of its own at its path (skip-worktree, `sparse.expectFilesOutsideOfPatterns`, so git never
  sees it as a change), and `UiImageSlots` (App) loads the real pixels into each placeholder texture after every scene
  load (LoadImage into the asset texture: every state then draws it): exact atlas rectangles of the converted copy
  (found by matching the project's images against the Android copy's atlases: og_check / _on, og_footer, og_header,
  og_panel_bg, og_title_plate and the crosshairs, all 0 / 255 off), the converter's GoF2Hud images, the cards
  (`MenuImage`), and the common pack's `Resources/GoF2UiSlots/<name>.png` for the 44 og_* the remake processed (3-slices
  put together, the amber `_on` states recoloured); the HD pack's large FISHLABS logo over the copy's atlas cut.
  `PlayerAssetsBuild` re-imports every stylesheet first: imported while the images were missing, they kept no reference.
  Two traps found in the Linux build: UI Toolkit's dynamic atlas copies a small texture the first time it draws it, so a
  placeholder drawn before it was filled stayed clear there (`UiImageSlots.KeepOutOfAtlas`: the slots' textures stay out
  of every panel's atlas); a placeholder imported as a sprite keeps its 4 x 4 rectangle whatever its texture becomes (the
  crosshairs), so `stripped_checkout.py` imports those as plain textures (their .meta edited, its GUID kept, git told to
  look away with assume-unchanged; `apply` puts such a .meta and a placeholder whose file is kept again back as
  committed). Checked: og_button 304 x 70, the crosshairs 81 x 81, the cards 605 x 943, no lost shaders.
- **The interface font** (2026-11-15, the user's choice): Klavika (Process Type Foundry) is commercial, and neither the
  project holds a licence to ship it nor does any copy of the game contain it (Android has bitmap glyph sheets for
  Arabic / CJK / Korean only, iOS and macOS no font files: the original drew its text from images), so it can't come from
  the player's copy either. Inter (OFL, already the fallback) is the theme's font now (GoF2Common.uss :root; its weight
  table's 700 the SemiBold face), Klavika's files are classified original (left out of builds without the originals), and
  players who own Klavika import it (Options > Accessibility "Import font file…", UiFont). The option's texts say so in
  every language.
- **Once converted, it stays converted** (2026-11-15, "close the gaps"): nothing is decoded, compressed or rendered twice.
  - **The format stamp:** `OriginalConverter.FormatVersion` (1); the manifest's first line is
    `# gof2 original cache v<N>`. `GameContent.Installed` only for this version's stamp; `GameContent.Stale` for a
    manifest of another (or none): the first-start screen offers the conversion again. Raise FormatVersion whenever the
    converter's output changes.
  - **The GPU cache** (`GameContent.GpuCache` = `<cache>/_gpu/v<N>`, dropped with a new FormatVersion): textures as the
    GPU takes them (`textures/<hash>.tex`, ModTextureCache's GXT1: DXT on desktop, ETC2 on phones, with mipmaps; the hash
    of the path, the source PNG's size and time, and the variant: normal map, linear), the rendered skies
    (`skies/<name>_<face>_<device>.sky`: six compressed faces). Measured in the test bed: the 319 assemblies 23 s cold,
    1.5 s from the cache; a sky rendered 1.2 s, read back 7 ms. Size: what the GPU holds (a 2048 px DXT5 with mipmaps
    5.6 MB; 399 textures 1.5 GB, a sky 17 MB), next to the 2.3 GB of PNGs (kept: another device or format re-encodes).

- **First start** (2026-11-15): a build without the original's content (`OriginalSetup.BuiltIn`: no `GoF2Data/ships` in
  Resources) and without a converted copy of this version (`OriginalSetup.Needed`) shows `OriginalSetupView` on the
  startup splash before anything else: the splash's music, the mods' preload and the menu scene wait for it.
  - **Choosing:** the system's file picker only (`ModImport.Pick` with `OriginalSetup.Extensions`; no scan of the
    player's folders, the user's call): Android's and iOS' document pickers copy the file into
    `temporaryCachePath/OriginalImport` (deleted after the conversion); the desktops' dialogs hand over the file where it
    is (`copyIn: false`). Several packages: a later one's files win (iOS, then Android for its HD textures).
  - **Converting:** `OriginalSetup.Convert` on a worker thread into `<cache>.new`, its phases ("read <file>", "tables")
    and entries shown with a bar; checked (no data tables or English text: nothing installed, the half-made folder
    deleted, the reason shown); then the old cache (its GPU caches with it) deleted and the new one moved in. 
    `GameContent.Refresh` raises `GameContent.Refreshed`: Database.Shared, Localization, OriginalTextures / Audio /
    Skies / Models / Assemblies drop what they read (or found missing) before. The result: files per kind, what the
    converter couldn't read, Continue to the logos and the menu. A copy of an older format (`GameContent.Stale`): the
    same screen, its own title.
  - **An XAPK's OBB is read where it lies** (`ZipSlice`: the 1.4 GB OBB is stored in the XAPK, so its bytes are found
    from the central directory and opened as a window of the file); a compressed one goes through a temporary file in
    the cache folder, never memory (the converter used to copy it into a MemoryStream).
  - **Tests:** `OriginalSetupTests` (EditMode: the stored zip in a zip, a file that isn't the game installs nothing);
    `FirstStartTests` (PlayMode, Smoke; only where nothing is built in, i.e. the test bed): the setup screen comes up
    instead of the menu; with `$GOF2_ORIGINAL_PACKAGE` (the XAPK) it converts it and goes on to the menu and its
    backdrop. The conversion writes ~4 GB into the play-mode sandbox (`Path.GetTempPath()`): run with `TMPDIR` on a disk.
  - **The Editor / development builds:** `$GOF2_ORIGINAL_CACHE` points `GameContent.Root` at a converted copy (the test
    bed's Play mode and play-mode tests).
  - **The dedicated server** (2026-11-15): no screen, so `-convert <package>` (repeatable), `-pack <file.gof2pack>`
    (repeatable) and `-convertonly` (DedicatedServer.PrepareContent, before Boot's settings: the conversion on its worker
    thread, its phases logged by tenths); without a converted copy it says what to run and quits (SERVER.md). Checked
    with the Linux development build as `-server`: no copy, the message; `-convert` the XAPK with `-pack` the common pack,
    converted (36 s) and the pack written; then it booted and listened.
  - **Options > Graphics "Add a pack…"** (main menu, builds without the originals): the file picker, the pack kept and
    written at the next start (`OriginalPacks.Add`); the row lists the kept packs.
  - **Not yet:** a VR headset shows the splash's panel on the monitor only; the setup texts are in English
    (`setup.*` keys of `Localization.Extra`, which loads with a text table: there is none before the conversion).

## Load times (measured 2026-11-15, Linux development build, RX 7900 XTX, the Android copy, warm caches)

`ContentTimings` (Core) counts every content-layer load per kind (inclusive: an assembly's time holds its meshes' and
textures'); `PlayerAssetsCheck` prints them per scene. The game's own hitch log (`hitches.log`) gives the load frames.
- **Found and fixed:** the interface placeholders (`UiImageSlots.Fill`) cost ~0.86 s of the menu's first frame on every
  start: each atlas rectangle decoded its whole atlas again (13 crops, ~0.37 s) and the text icons' atlas was drawn pixel
  by pixel. Now every made image is kept as PNG in the copy's GPU cache (`<GpuCache>/ui`, keyed by its sources' files)
  and `Crop` decodes an atlas once per fill: ~0.09 s. `OriginalRefs.FillScene` gathers a scene's components once (it
  searched them all per manifest entry). WAV samples are converted in one pass.
- **Result:** the menu's first frame ~2.7 s -> ~1.4-1.6 s; orbits 0.15-0.35 s; the hangar ~0.3 s.
- **What remains**, and why: the menu's sound tables make ~170 clips (~0.4 s: clip creation, not decoding; decoding them
  side by side on worker threads was no faster in IL2CPP, whose GC serialises the threads' large arrays); meshes and
  their materials built on first use (~0.25 s on the menu); textures and skies a run hasn't met yet (the menu backdrop's
  and the orbits' random ships) are decoded and compressed once, then come from the GPU cache.

## Plan

1. Inventory and this note (done 2026-11-15); the importers, the converter, the packs (done 2026-11-15).
2. **Content layer:** one API serving the game's own content (textures, meshes, materials, clips, text, data) from the
   converted cache in `GamePaths.Data`, with the `Resources` path behind it until the cache path is complete; the
   assembled objects built at run time from `assemblies.json` the way mod ships are (`ModShips.Template`); the sound
   tables become name lookups.
3. **Importers in C#** (GoF2.Game, plain C#, testable): AEI (RGBA8, ETC1, PVRTC 2 / 4 bpp, regions, cubemap strips), AEM
   (meshes, the .gof2mesh.json animation data), FSB5 (MPEG, IMA ADPCM, PCM), `.lang`, `.bin`. Each checked against the
   current project's converted files (the reference output) in edit-mode tests that skip without a source copy.
4. **First start** (the screen and the conversion done 2026-11-15, see "First start" above): "Show the game your Galaxy on Fire 2" with a file picker (desktop), Android's document picker, iOS's
   document picker; the conversion with progress, a check of what came out, then the derived assets.
5. **Derived assets on the player's machine:** assembled objects, 3D planets, sky layers, reflections; the optional
   desktop sky upscale.
6. **Builds without the originals:** the build strips every original and derived folder; a clean install, given only the
   player's copy, runs end to end. CI builds then need none of the LFS assets.
