# Tests

Regression tests for the remake: run them after a change to see that nothing else broke. Unity's Test Framework
(Window > General > Test Runner), or the `unity` CLI.

| Assembly | Folder | What |
|---|---|---|
| `GoF2.Tests` (edit mode) | `EditMode/` | ~3000 tests of the game's rules and data, about 30 s |
| `GoF2.PlayModeTests` (play mode) | `PlayMode/` | the scenes running: menu, stations, flight, docking, saves, story levels, a hosted session |
| `GoF2.TestSupport` | `Support/` | `SandboxTest`, `SettingsScope` |

## Edit mode (`EditMode/<area>/`)

| Area | Covers |
|---|---|
| `Flight` | FlightModel (handling, turns, boost, brake, strafe, level-out, autopilot bank, heavy hulls), the dodge, the shared turret (TurretCore, TurretTargeting, mounts), Hitpoints, Cloak, CollisionVolume, MiningGame / MiningBeamExtraction, HackingGame, AutofireLatch, HapticMixer, Aspect, JavaRandom, Standing, Route |
| `Universe` | OrbitLayout's seeded orbits (gates, skies, suns, planets, asteroids) against `space_backdrop_sim.py`, empty / alien / supernova orbits, ore odds, GalaxyMap (routes, energy cells, visibility), SpacePoints, coordinates, the universe data |
| `Economy` | Shop prices against `prices.py`, stock and dealer ships, Hangar trading / mounting / ships / Kaamo storage, Blueprints against `blueprint_table.py`, KaamoClub, Database and the two economies, Cheats |
| `Npc` | NpcTables (hulls, guns, reloads), fighter pools, TrafficPlan against `npc_tables.py` (counts and every special orbit), capital ships, pirate events and tiers, Gun (missile homing), turret items on NPC ships and in the hangar |
| `Story` | Story steps 0-162 against `campaign_flow.md` (missions, side effects, rules, mission select), StoryTable, Freelance against `mission_tables.py`, AgentGenerator, Wingmen, WantedBoard, PirateReports, PirateBases, medals, New Game+, GameNames |
| `SavesMods` | SaveGame round trips, old versions, TryParse's refusals, SaveTransfer, the mod system (manifests, zips and CRCs, ModManager, items / ships / systems / stations, the registry, ModSaves, unlocks, blueprints, campaigns, characters), ImageImport's decoders |
| `Events` | the built-in event graphs and templates compiling, the graph compiler, the line form, expressions, ship orders, cutscene shots, speakers |
| `Multiplayer` | addresses and join codes, chat commands and selectors, NetGuard and rate limits, aggression, session rules, moderation, factions, trades, mod lists, news |
| `Data` | the shipped data: every JSON, items / ships / stations / assemblies / story assets present, localization tables and the extra texts |
| `Interface` | Settings and their per-tab reset, OptionsCatalog, GameControls bindings, flight hint key tokens, TextReveal, AlienText, versions, issue reports (their files, zip, log memory, GitHub address) and the Editor's UI hang guard |
| (root) | `CoreRulesTests`, `LayerHookTests` (every layer hook has an installer) |

Expected values come from the research notes and Python tools in `Reference/` (independent of the C# code), the decompiled
code, or CLAUDE.md; where only the code defines a behaviour, the tests check its properties (determinism, ranges,
round trips).

## Play mode (`PlayMode/`)

- `MouseDragInputTests`: virtual mouse states sent through the real Input System/EventSystem/UI Toolkit path; holding
  across multiple frames pans the map and turns the station ship, and a normal release still flings. Also injects an
  empty UI move button mask while the real mouse remains held, to check `UiPointerButtons`' recovery. Run in a standalone
  player too when diagnosing build-only input reports; direct handler tests alone do not exercise that path.
- `SceneSmokeTests` (category `Smoke`): the main menu, a station of every hangar type with its lounge, every station
  window, the star map in every mode, launch / fly / dock back, the auto-save, ~35 orbits.
- `StoryLevelTests`: `KeyStoryStepsRun` (`Smoke`, 43 steps over the three campaigns, ~5 min) and `EveryStoryStepRuns`
  (`Slow`, every step the mission select offers, ~20 min): `Story.StartAtMission`, then its scene runs 4 s.
- `MultiplayerSessionTests` (`Smoke`): hosting on the local network, the hangar, the shared orbit, docking, shutdown.

Any error or exception the game logs fails a play mode test.

## Rules for new tests

- Tests that touch the game's state derive from `SandboxTest` (edit mode) / `PlayModeTest` (play mode): `GamePaths.Data`
  points at an empty temporary folder (saves, the mod registry, presets, the Mods folder: never the player's), and every
  test starts and ends with a fresh new game.
- Put back any PlayerPrefs a test changes (Settings, `mods_enabled`, `controls_bindings`): `SettingsScope<T>` or try/finally.
- Static state needs `[Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]` like the game's code.
- A known difference from the original that isn't fixed yet: `[Ignore("why")]`, so the suite stays green and it shows as
  skipped.

## Running from the command line

```
unity command run_tests -- --mode EditMode
unity command run_tests -- --mode PlayMode --async_tests true --filter GoF2Remake.Tests.SceneSmokeTests
unity command test_status
```

Play mode runs are asynchronous: poll `test_status` until it says completed.

`python -I Reference/tools/tests/run_tests.py` does the same in one call and prints only the totals and each failed test
(its message and the stack lines from `Assets/`), for both modes (`--mode PlayMode` polls for you); `--filter` takes a
test, fixture or namespace, `--reload` recompiles first (right after Play mode, whose leftover statics fail tests), and
the full result is kept in `Temp/test_results.json`.
