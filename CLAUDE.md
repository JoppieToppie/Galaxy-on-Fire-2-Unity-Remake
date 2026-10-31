# Galaxy on Fire 2 remake (Unity)

A remake of the 2010 mobile game *Galaxy on Fire 2* (Fishlabs / Deep Silver) in Unity. The goal is a faithful port of the original gameplay (flight, combat, trading, stations, missions), built on the original assets and on logic ported from the decompiled game code.

## Project notes (read the area's file before working on it)

This file holds only what every task needs. Everything about one area of the game lives in `docs/notes/`, moved there unchanged from this file (2026-10-28):

- **Before working on an area, read its notes file** (several for a cross-cutting change). When unsure which, grep `docs/notes` for the class, option or command name.
- **New notes go into the area's file**, under its headings and in its style; add a new file (and a row here) only for a new area. Keep this file to project-wide rules. Wrap note lines at about 120 characters: a grep hit prints the whole line.
- The Grep tool skips what `.ignore` lists (the embedded Netcode package, four one-line data tables); search those with `grep -r` in a shell or read them directly.
- Code comments cite `CLAUDE.md "<section>"` (a heading or a bullet's bold title): the names are unchanged, so look them up with `grep -rn "<section>" docs/notes`.

| File | What it covers |
| --- | --- |
| [editor-tools.md](docs/notes/editor-tools.md) | Every GoF2 menu item in `Scripts/Editor`: what it builds and where it writes |
| [assembled-prefabs.md](docs/notes/assembled-prefabs.md) | Assembled objects, ship variants, LODs and the generated levels of detail |
| [space-scene.md](docs/notes/space-scene.md) | Orbit layout, sky, reflections, hull shader, backdrop, lights, menu background, docking, wormhole, the Void |
| [navigation.md](docs/notes/navigation.md) | Locks, autopilot, autopilot / quick menus, planet jump, fast-forward and cruise |
| [star-map.md](docs/notes/star-map.md) | Star map, jumpgates, Khador Drive, arrivals, mission maps, Loma, interstellar space |
| [npcs-and-combat.md](docs/notes/npcs-and-combat.md) | Traffic, NPC stats and AI, damage, radar and HUD markers, music, collisions, radio, fines, NPC guns and turrets, missile warning, cargo stealing |
| [capital-ships-and-pirates.md](docs/notes/capital-ships-and-pirates.md) | Capital ships, their enhancements, fleet battles and missiles, pirate bases, pirate events, threat tiers, the pirate board |
| [story.md](docs/notes/story.md) | Campaign rules, dialogue, radio, cutscenes, the main and Valkyrie levels, the ending |
| [supernova.md](docs/notes/supernova.md) | Steps 84-162, object docking, hacking, gas clouds, Most Wanted, Specters, the Supernova Challenge |
| [bar-and-freelance.md](docs/notes/bar-and-freelance.md) | Agents, lounge chat, freelance missions, Missions window, blueprints, wingmen, medals |
| [kaamo-club.md](docs/notes/kaamo-club.md) | The siege, purchase, storage and stored hulls |
| [mining.md](docs/notes/mining.md) | Asteroids, the lock, approach, minigame, payout, the mining beam |
| [station-scene.md](docs/notes/station-scene.md) | Hangar, lounge, station menu, Inspect ship, hangar flights, parking heights, ship shadows, the remodelled hangars |
| [shop.md](docs/notes/shop.md) | Economies, prices, stock, cargo, mounting, ships, held trades, item details |
| [weapons.md](docs/notes/weapons.md) | Guns, special weapons, turrets, homing, smoke trails, explosions, pooling |
| [combat-equipment.md](docs/notes/combat-equipment.md) | Cloak, emergency system, time extender, repair beams, shield injector, gamma |
| [sound.md](docs/notes/sound.md) | FMOD data, volumes, engine loops, cycle events, volatile goods, shot voices |
| [ui.md](docs/notes/ui.md) | UI Toolkit, the design document, scaling, accessibility, aspect ratios |
| [input.md](docs/notes/input.md) | Keyboard, key bindings, controllers, input modes, pause menu, Action Freeze and photo tools, touch controls, hints, cameras, cockpit view, gyro, haptics, mouse steering |
| [free-flight.md](docs/notes/free-flight.md) | The EVERSPACE 2-style flight option and its key layout |
| [saves.md](docs/notes/saves.md) | Difficulty, save slots, original-game saves, deleting, export / import, save folder, Hardcore, New Game+ |
| [options.md](docs/notes/options.md) | Text tables and languages, the story step label, every option, bloom, frame rate, graphics presets, texture quality, upscalers |
| [platforms.md](docs/notes/platforms.md) | Discord, app icon, Android, iOS, UWP, Vulkan pre-transform, version, update check, hitch logger |
| [debug-tools.md](docs/notes/debug-tools.md) | The Debug panel and in-game Debug pages: missions, cheats, give items, spawn, presets, ships |
| [multiplayer.md](docs/notes/multiplayer.md) | Session model, menu, Relay / Lobby, version check, players, PvP, leaving, rules, testing |
| [multiplayer-sync.md](docs/notes/multiplayer-sync.md) | Orbit authority, NPCs and players, proxies, shots, crates, mining, hangars, shared stock |
| [multiplayer-server.md](docs/notes/multiplayer-server.md) | Dedicated server, settings, moderation, profiles, persistent host, MOTD, news, web admin, server checks |
| [multiplayer-factions.md](docs/notes/multiplayer-factions.md) | Factions, bank, claims, sieges, garrisons, tolls, VAT |
| [multiplayer-social.md](docs/notes/multiplayer-social.md) | Squads, squad bar missions, arena matches, trades, distress calls |
| [chat-and-commands.md](docs/notes/chat-and-commands.md) | Chat, chat commands, admin tools, selectors |
| [event-graphs.md](docs/notes/event-graphs.md) | Events, event graph files and nodes, quests, bar missions, cutscenes, ship orders, wormholes |
| [mods.md](docs/notes/mods.md) | Mod format, items, ships, numbers, saves, campaigns, options, flight styles, conditions, traffic, capital ships, mobile stations, ES2 docking, blueprints, browser, multiplayer |
| [mods-models.md](docs/notes/mods-models.md) | Ship models, how a ship is seen, engine effects, exhaust styles, textures / skins, image formats, loading |
| [mods-ship-kits.md](docs/notes/mods-ship-kits.md) | Ship kits, builds, colours, skins, hardpoint models, engine flames, lateral thrusters, liveries, decals, engine trails |
| [mods-world.md](docs/notes/mods-world.md) | Systems, stations, station models, planets, suns, skies, interiors, characters, mod races |
| [mods-weapons-audio.md](docs/notes/mods-weapons-audio.md) | Weapon fx, missile turrets, game models, sounds, voices, music |
| [mod-builder.md](docs/notes/mod-builder.md) | The in-game mod editor, its kinds, quest graph, optimizer, imports |
| [mod-builder-mcp.md](docs/notes/mod-builder-mcp.md) | The builder's MCP server for AI assistants |
| [vr.md](docs/notes/vr.md) | OpenXR VR mode, rig, panels, cockpit, controls |
| [flight-model.md](docs/notes/flight-model.md) | Recovered flight constants, chase camera, bank, dodge, field offsets |
| [open-items.md](docs/notes/open-items.md) | Open issues and the suggested order of work |

## Environment

- Unity **7000.0** (alpha 7000.0.0a12, from 6000.7.0b3 on 2026-10-06, a8 on 2026-10-10, a9 on 2026-10-14, a10 on 2026-10-16, a12 on 2026-10-23; .NET CoreCLR instead of Mono), **URP 17.7**, Windows.
- **Embedded `Packages/com.unity.netcode`** (Netcode for Entities 7.1.0, which Netcode for GameObjects 3.1 pulls in; copied
  without its docs and tests): a9's built-in Entities 6.7 dropped the DOTS Hierarchy window, so the registry package's
  `Editor/Authoring/HierarchyDrawers.cs` didn't compile; its `DotsHierarchyItemDecorator` is removed there. Delete the folder
  once a newer Netcode for Entities compiles on the Editor in use.
- **Scripting backend:** IL2CPP for Windows / Linux (Standalone; the Editor modules "Windows / Linux Build Support (IL2CPP)";
  Linux builds from Windows use `com.unity.toolchain.win-x86_64-linux` + `com.unity.sdk.linux-x86_64`, which the first Linux
  IL2CPP build added) and Android; native callbacks must be static `[AOT.MonoPInvokeCallback]` methods (`WinConsole`). A
  build folder made with Mono needs clearing before an IL2CPP build. Never ship the `*_BackUpThisFolder_ButDontShipItWithYourGame`
  folder (IL2CPP's debug data) in a release.
- **Input System package only.** Active Input Handling is set to the new system. Never use `UnityEngine.Input` or the legacy Input Manager.
- Packages include `com.unity.pipeline` (Unity CLI bridge; 0.8.0-exp.1 or later: 0.7 listened on `http://+:<port>/`, which
  CoreCLR's HttpListener on Windows refuses without admin rights, "No available ports in range 7800-7849"). The package
  doesn't stop its server before a code reload, and the old listener's http.sys registration outlives the reload, so each
  recompile used to keep one port of that range until the Editor quit; `PipelineReloadGuard` (Editor) stops it first. `com.unity.ai.assistant` was removed (its AI Generators logged
  "NoSubscription" errors), so there is no Unity MCP server.
- Driving the Editor: if the `unity` CLI is available, use it to read the Console, run menu items, enter Play mode and inspect the scene. Check with `unity status` first. When an Editor is connected, don't hand-edit `.unity` / `.prefab` / `.asset` YAML.

## Layout

```
Assets/
  Scripts/Runtime/   the runtime code in layered assemblies, lowest first (Scripts/Runtime/README.md: the layers, the hooks):
    Core/            GoF2.Core: pure rules / utilities (JavaRandom, MiningGame, Cloak, Hitpoints, CollisionVolume...)
    Game/, Modding/  GoF2.Game: state and rules (Session, Database, Story, Shop, Freelance, saves, Settings), mod loading
    Space/           GoF2.Space: ship controller, chase camera, combat, keyframe player (FlightModel is in Game); Space/UI: the
                     helpers the screens share (ItemInfo, Portrait, StarMapTypes, UiHooks); Space/ModBuilding: mod models
    Space/World/     station orbits: OrbitLayout (seeded layout, in Game), OrbitBuilder (layout -> scene, shared by
                     the flight level and the menu background), SpaceLevel, Backdrop, SpaceDust,
                     SystemJump (jumpgate / Khador travel), Traffic + NpcShip (NPC ships), Wormhole (the Void's wormhole);
                     story: StorySpace, CampaignLevel, IntroCutscenes (0 / 1), MainCampaignLevels (14-42),
                     ValkyrieLevels (48-81), SupernovaLevels (87-158), ShuttleRoutes (their NPC shuttles), ChallengeOrbit (the
                     Supernova Challenge), GasCloudField, NpcCloak;
                     docked station: StationLevel + StationTables; SpaceHooks (what the levels need from Net / App)
    Net/Multiplayer/ GoF2.Net: multiplayer (see "Multiplayer")
    Net/Events/      the event graphs (single player quests / bar missions and multiplayer events alike):
                     EventRunner (runs them), EventGraphFile + EventGraphScript (read / compile a .gof2event),
                     EventHost / IPilot / LocalPilot / LocalEvents (session or single player), EventMissions, EventScreen
                     (titles, dialogues, rewards, questions), EventCutscene, EventWaypoint, EventRespawn, EventRules,
                     EventAudio, EventNames; the commands they run stay in Multiplayer (NetCommands, NetAdmin)
    App/             GoF2.App: Bootstrap, AppHooksInstaller; App/UI the screens, App/Vr VR
  Scripts/Editor/    import settings, prefab builder, asset pack installer, menu items
  Models/            1,125 converted .fbx meshes, each with a .gof2mesh.json sidecar (pivots, keyframes)
  Textures/          1,281 .png (diffuse, *_normal_specular = normal map, *_metallic_smoothness generated)
  Audio/             4,416 .ogg, one folder per original FMOD bank (English + German voices)
  Resources/GoF2Data/ all game data as JSON (see "Data" below)
  Localization/      14 languages: text_<lang>.json + GoF2_strings.csv (text_nl.json = the remake's Dutch translation of the
                     whole table, text_hi.json its Hindi one, text_zh.json Simplified Chinese from PR #50 (FantasyToLife)
                     with the entries it left in English translated; the remake's own `Localization.Extra` texts per
                     language in `Resources/GoF2Localization/extra_<lang>.json`, see "UI and platforms")
  Shaders/           Shader Graphs (URP Unlit): GoF2/Unlit, Additive, AlphaBlend, AlphaTest; _Glow > 1 = HDR for bloom.
                     Hand-written HLSL where Shader Graph can't: GoF2/SpaceSky (skybox), Backdrop (far-plane quads), SpaceDust,
                     Cloak and ShieldBubble (screen refraction: they switch the camera's opaque texture on, `OpaqueTexture`),
                     HangarShadow (the hangar ships' contact shadows, projected from the camera depth texture), Hull (URP Lit + the
                     original's hull reflection and rim, see "Space scene")
  UI/                UI Toolkit: GoF2Theme.tss + GoF2Common.uss (tokens, control styles), GoF2InputGlyphs.uss (hint rows,
                     keycaps, Xbox buttons), GoF2PanelSettings, fonts: Klavika (the original HD versions' interface typeface, 2026-10-24, the user's files: Medium the default, as the font has no upright Regular; Bold through the Medium asset's weight table (`.gof-semibold` is `-unity-font-style: bold`), italic likewise; Options > Accessibility "Interface font" replaces it, see "UI and platforms") falling back to Inter (OFL) for Cyrillic and the like, MainMenu/ (UXML, USS, cut images,
                     menu post-processing profile), Flight/ (HUD), Station/ (station menu), StarMap/ (star map overlay)
  Skyboxes/          skybox_0XX.png (6-face strip -> Cubemap) + .mat, old combined bakes (only the Flight Test scene uses one)
  Materials/, Prefabs/ generated by the "GoF2 > Build > Materials And Prefabs" menu item (one prefab per game mesh)
  Resources/Assembled/ one prefab per game object (ship, station, jumpgate, hangar...), see "Assembled prefabs".
                     In Resources so levels load only what they use (AssembledObject.LoadPrefab)
  Resources/GoF2Sky/ flight-level sky: stars_00X + nebula_0XX cubemaps, SpaceSky.mat template
  Resources/GoF2Backdrop/ sun / planet / ring / dust materials, one per texture (loaded by name)
  Resources/GoF2Icons/ shop icons item_XXX / ship_XXX (frame + icon, 180x88), made by "Build Item Icons"
  Resources/GoF2Hud/ flight HUD images (lock ring, plate, minigame, brackets, autopilot / fast-forward, star map rings and
                     icons, race logos), made by "Build HUD Images"
  Resources/GoF2StarMap/ StarMapAssets: what the star map can't load by name (overlay UXML, sun materials, Khador fx, sounds)
  Resources/GoF2Combat/ CombatAssets: crates, wrecks, explosion, tractor beams, hit / death sounds, space and battle music
  Resources/GoF2LanguageTables.asset references every text table so any scene loads text on first use (Localization)
  Scenes/            Splash (startup logos, build index 0), MainMenu, Space (flight level), Station (docked: hangar + bar)
  Settings/          URP assets, GoF2_VolumeProfile (bloom)
Reference/           decompiled original code, binaries and conversion tools (see Reference/README.md)
Modding/             the modders' guide (README.md) and example mods (Examples/plasma_arsenal); Scripts/Runtime/Modding the loader
Mods/                the mods the Editor loads (git-ignored)
```

Menu items (from `Scripts/Editor`), grouped in submenus; **GoF2 > Tools Overview** (`GoF2ToolsWindow`) lists them all with what each makes and a Run button (keep its table in step when adding or renaming a tool):

The full list with what each item makes and writes: [docs/notes/editor-tools.md](docs/notes/editor-tools.md).


## Conventions

- **Layered assemblies** (`Scripts/Runtime/README.md`): GoF2.Core <- GoF2.Game <- GoF2.Space <- GoF2.Net <- GoF2.App, each
  referencing only the ones below. The folder decides the assembly, not the namespace. A lower layer never names a type of
  a higher one: it adds a member to its hook class (`GameHooks`, `NetSession`, `SpaceHooks`, `UiHooks`) with a single-player
  fallback, and the higher layer's installer (`SpaceHooksInstaller`, `NetHooksInstaller`, `AppHooksInstaller`) fills it
  (`LayerHookTests` checks none is left unset). Only GoF2.Net and GoF2.App reference Netcode. New files in `Space/` keep the
  `GoF2Remake.Flight` / `World` / `Visuals` / `UI` namespaces: never `GoF2Remake.Space` (see below), whatever an IDE derives
  from the folder.
- Namespaces: `GoF2Remake.Flight`, `GoF2Remake.Data`, `GoF2Remake.Visuals`, `GoF2Remake.World` (flight level), `GoF2Remake.UI`, `GoF2Remake.Events` (the event graphs), `GoF2Remake.Multiplayer`, `GoF2Remake.Modding`, `GoF2Remake.EditorTools`. Never a namespace segment that shadows a Unity type (`GoF2Remake.Space` broke `Space.Self`).
- **One MonoBehaviour/ScriptableObject per file, and the file name must equal the class name.** Otherwise prefabs save it as a missing script.
- Big classes are partial, split by feature into `<Class><Area>.cs` beside the main file (ModBuilderView, StationMenu,
  MainMenu, EventRunner, NpcShip, SupernovaLevels): the fields and Unity messages stay in the main file (initializer
  order across parts is undefined); find a member with a grep. Split a class again before it passes ~60 KB a file:
  `python -I Reference/tools/split_partial.py sections <File.cs>` lists its `// ----` sections, `split <File.cs> <plan.json>`
  moves their methods and properties into `<Class><Area>.cs` (a dry run with sizes; `--write` writes, then compile).
- Gameplay logic is a clean C# reimplementation of the behaviour in the decompiled code, not a line-by-line transliteration. Comment the original function names and constants you based it on.
- Keep game-logic classes plain C# where possible (like `FlightModel`) so they can be unit-tested. MonoBehaviours adapt them to Unity.
- A type with static fields, auto-properties or events carries `[Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]`
  (Unity 6.7's UAL0010 / UAL0013 analyzer): Play mode runs without a domain reload and the code resets what it needs itself
  (`RuntimeInitializeOnLoadMethod(SubsystemRegistration)`). Use `FindObjectsByType<T>()` / `(FindObjectsInactive)`, never the
  obsolete `FindObjectsSortMode` overloads.
- Game time is in **milliseconds**. Convert with `Time.deltaTime * 1000f`.
- **Tests** (`Scripts/Tests/README.md`): ~3000 edit-mode tests of the rules and data (`EditMode/<area>/`, ~30 s) and play-mode
  smoke tests of the scenes, story levels and a hosted session (`PlayMode/`). Run the edit-mode suite after a change
  (`python -I Reference/tools/tests/run_tests.py [--filter X] [--reload]`: the totals and each failure only); add a
  test with a new feature. Tests touching state derive from `SandboxTest` / `PlayModeTest` (`GamePaths.Override`: a
  temporary data folder, never the player's saves or mods); code reaching persistentDataPath goes through `GamePaths.Data`.

## Coordinates and scale

- **Game to Unity: `(x, y, z) -> (x, y, -z) * 0.05`** (0.05 m per game unit). Models face **+Z**.
  - The FBX files themselves face -Z. `ModelOrientationPostprocessor` rotates them 180° about Y at import.
- `ImportSettings.ModelScale` (0.05) must match `ShipController.metersPerUnit`.
- Data files with positions (`weapons_hd.json`, `docks_hd.json`) contain `position_file` (as stored) and `position_engine` (game space). Apply the rule above to world positions from `position_engine`; **ship-relative offsets** (weapon mounts) map to Unity ship space as `(-x, y, z) * 0.05` (the models are mirrored and turned 180 deg on import).
- The original stores animation position keys Z-up. The engine uses `(c0, c2, -c1)`. Keyframe rotations are in radians, Rx * Ry * Rz in the file's Z-up frame; `PartAnimation` turns them about Unity x / z / y with the file-Z angle negated (the part frame is the file's through (-x, z, -y), a mirror; see `rotationMap`).

## Data (`Assets/Resources/GoF2Data`, loaded with `Database.Load()`, JsonUtility)

- `Database.Load()` parses every table again (~6 ms and several MB of garbage): use it once per scene (the levels keep
  their own, `level.Database`) or where the result is changed; read-only lookups that run more often (UI rebuilds, spawns,
  multiplayer mirrors, the Mod Builder) take `Database.Shared`, made again only when the economy or the mods change, and
  never write to it. While a Load applies the mods, `Shared` answers with the database being loaded (`applying`): a kit
  ship's mounts read through `Shared` while `ModContent.ApplyNpcTurrets` counted them, so `Shared` loaded itself again
  without end (a stack overflow that took the Editor down, 2026-10-24). Tests: `SandboxTest` drops `ModManager`'s lists
  too, else a scan of the player's own Mods folder made before the run leaked the player's mods into the tests.

- `ships`, `items`, `systems`, `stations`: the economy and universe.
  - `items[].statList` has named stats (damage, range, boostSpeed, agility…).
  - `items[].rawAttributes` has the original attribute IDs.
- `item_attributes.json`: every item's original attribute pairs as lists (`ItemData.Attr(id)`), generated by `Reference/tools/shop/build_item_attributes.py` because JsonUtility can't read the `rawAttributes` dictionary.
- `agents` (bar characters), `wanted` (bounty targets), `names`, `ticker`.
- `weapons_hd` (gun mounts per ship: slotType 0 primary, 1 secondary, 2 turret, 3 = engine exhaust points, not turrets), `docks_hd` (docking points), `shipparts` / `stationparts`, collision tables, DLC variants `sn_*` / `v_*`.
- `economy_default.json`: the Default Economy overlay on `items` / `item_attributes` / `ships` (see "Shop").
- `resources.json`: the original resource table. `meshes[]` (id, model path, materialId), `materials[]` (shading type, textures), `textures[]`. IDs match the decompiled code; most main-game ship meshes are 17000 + ship index (a few special ships differ, so look them up by model path).
- Text IDs (`Localization/text_en.json`):
  - ship name = 913 + index, ship description = 977 + index
  - item name = 1274 + index, item description = 1041 + index
  - item category = 221 + categoryId, race = 406 + raceId
- Item attribute keys:
  - 9 damage, 10 EMP damage, 11 loading time (ms), 12 range, 13 projectile speed
  - 18/19 shield capacity/regen, 20 armor, 22 cargo bonus
  - 25/26/27 boost speed/recharge/duration, 28 agility, 31 radar
  - 39/40 fire-rate/damage factor

## Working with Reference/

- `python Reference/tools/show.py "^PlayerFighter::update$"` prints a decompiled function.
- Grep `Reference/decompiled/native/_ALL_FUNCTIONS.c` for callers, strings and IDs.
- `python Reference/tools/rf.py 000a8178` reads a `DAT_` constant (Ghidra image base 0x10000).
- Class overview: `Reference/decompiled/native/INDEX.md` and `CLASS_HIERARCHY.txt`.

