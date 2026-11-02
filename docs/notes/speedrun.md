# Speedrun timer

The remake's speedrun timer (players' request, 2026-11-02). Code comments citing `CLAUDE.md "<section>"` mean the section
or bullet of that name here.

## Timer and splits

- **Option** (Options > Gameplay "Speedrun timer", `Settings.SpeedrunTimer`, off by default; the other rows show only
  while it is on, `OptionsCatalogSpeedrun.cs`): Splits (Story chapters / Every story step, `SpeedrunEverySplit`), Run
  length (the campaign started / on to the end of Supernova, `SpeedrunAllCampaigns`), Timing (Real time / Without
  loading, `SpeedrunGameTime`), Compare against (`SpeedrunCompare`, a string key), Timer position (FpsPosition's eight
  places, `SpeedrunPosition`, top left by default), Connect to LiveSplit (desktop, `SpeedrunLiveSplit`), Reset the run,
  Open the runs folder (desktop). Turning the timer off ends a run. Keys in `Settings.GameplayKeys`;
  `DataUiTestKit.PrefsSnapshot` keeps `speedrunCompare` (and `uiFont`) as text.
- **A run** (`Speedrun`, plain C# in GoF2.Game): the main menu's single-player new game starts it after
  `Story.StartCampaign` (`MainMenuNewGame` -> `Speedrun.NewGame`; not in multiplayer, not the mission select or a mod
  campaign / DLC). Any new game (`Story.StartNewGame`) first ends a run still going, unfinished, so the add-ons'
  `StartCampaign` advances (45 / 84 calls of `Advance`) never split. `Story.Advance` calls `Speedrun.Reached(next)`; the
  main game's ending (`StationMenuDockingChecks`, step 43 won: the credits) calls `Reached(SpeedrunRoute.MainEndingStep)`
  = 44, so the run ends when the credits start, not after them. A split is made once, the first time its step is
  reached: game over's reload of slot 0, the main menu and loaded saves keep the run going (real time runs on). A save
  further on skips the splits between (LiveSplit's skipped splits: no time, the next segment covers them). Reset (the
  button, a new game, quitting the game: `Application.quitting`) keeps the run as an unfinished attempt when it ran 1 s.
- **Routes** (`SpeedrunRoute`): the main game runs from step 0 to the ending (44), Valkyrie from 45 to 84
  (`Story.Dlc1WonIndex`), Supernova from 84 to 162 (`Story.LastIndex`); "every campaign" runs on to 162 (Valkyrie +
  Supernova from a Valkyrie start; a Supernova start is the same). Every story step: one segment per step, named
  "23. <StepSummaries title>" (53 and 129 never reached: `Advance` skips them). Chapters: a hand-made table
  (10 main, 6 Valkyrie, 9 Supernova), the segment ending on reaching its step. Names, the category ("Main game",
  "Valkyrie", "All campaigns", "(New Game+)") and the difficulty (Easy..Extreme) stay English: they go into the shared
  files.
- **Clocks**: real time, and game time = real time without loading: `SpeedrunOverlay.Update` ticks the run with the
  real time between frames, and the first frame after `SceneManager.sceneLoaded` counts as loading (the scene loads are
  synchronous: the load and the new scene's Awake / Start fall into that frame's delta). The Void's additive station
  load (`SpaceLevelWormholes`) is asynchronous and counts as game time. Pauses count in both, as LiveSplit's do.
- **Overlay** (`SpeedrunOverlay`, App/UI, installed by `Bootstrap` like `FpsCounter`, its own panel): the category and
  difficulty, five splits around the running one (a finished split's delta green ahead / red behind / gold for a best
  segment, then its time; the running one highlighted with its live delta once past the comparison's split; the next
  ones the comparison's times), the timer (green / red against the comparison's split, gold for a new personal best),
  then the comparison and the clock (and "LiveSplit" while connected). Shown while a run is on or finished, in every
  scene but the startup logos (the main menu too: the run goes on there), never in photo mode.

## Runs folder and LiveSplit

- **Files** (`GamePaths.Data/Speedruns`): one LiveSplit splits file (.lss, `LiveSplitRun`) per route: "Galaxy on Fire 2
  - Main game - Normal - Chapters.lss". It keeps every attempt (AttemptHistory, a finished one with its RealTime /
  GameTime), each segment's personal best split time, best segment and history per attempt, both clocks: LiveSplit
  opens it (and can run on with it), splits.io takes it as an upload. Saving rewrites only those parts into the read
  document, so what LiveSplit added (layout path, its own comparisons, auto splitter settings) stays. A file whose
  segment count differs from the route (the chapter table changed) starts anew. The personal best goes by the chosen
  clock; best segments after a skipped split don't count (as in LiveSplit).
- **Comparisons** (`Speedrun.Comparisons`): Personal best, Best segments (their sum), Last run (the newest attempt with
  a split, finished or not), None, the last 10 finished attempts ("Run 12 · 1:23:45.6 · date", rebuilt from the
  segment history), and every other .lss in the folder with as many segments (a downloaded run, by file name; its
  personal best, in the other clock when it has only one). Switchable during a run (the pause menu's Options).
- **LiveSplit's TCP server** (`LiveSplitLink`, desktop): with "Connect to LiveSplit" on, a background thread connects to
  127.0.0.1:16834 (LiveSplit 1.8+: Control > Start TCP Server; older: the LiveSplit Server component) and retries every
  3 s; the run sends `reset`, `starttimer`, `initgametime`, `split` / `skipsplit` (with `setgametime h:mm:ss.ff` before
  them and after each load) and `reset`. Commands while nothing listens are dropped. LiveSplit's own splits need as
  many segments as the route (export the run's .lss from the folder into LiveSplit once).
- Not built: a key binding for split / reset (the story splits by itself; Reset is in the options), an auto splitter
  (.asl) for LiveSplit's memory reading (the TCP link replaces it), timing pauses.
