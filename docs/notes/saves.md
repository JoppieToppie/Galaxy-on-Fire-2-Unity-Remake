# Saves and new game

Notes moved from `CLAUDE.md` (its index lists every notes file). Code comments citing `CLAUDE.md "<section>"` mean the section or bullet of that name here.

## UI and platforms

- **Difficulty** (`Session.Difficulty`, options+0x2c; the new game's difficulty panel): Easy 0 / Normal 0.5 / Hard 1 /
  Extreme 1.5, the PC / Mac Full HD version's four (texts 518-520, 25); the phone menu offers only Normal and Extreme, but its
  code still handles all four. `Session.DifficultyFactor` = the original's `x + x·(difficulty − 0.5)` (×0.5 / 1 / 1.5 / 2):
  NPC hulls and gun damage (`NpcTables`), raider counts, pirate-base guards `int((d − 0.5)·5 + 5)` (2 / 5 / 7 / 10), the
  Wanted, the Kaamo / pirate outposts, the hull-270 missions, the freelance enemy counts; "difficulty < 1" (a secure system's
  1-2 raiders: Easy / Normal) and "> 0.7" (Escort / Intercept freighters ×1.4: Hard / Extreme); the cloak cooldown 5000 /
  7000 / 9000 / 12000 ms; Loma's toll 2 / 5 / 10 / 20 %. `Session.IsExtreme` (`Status::hardCoreMode`, == 1.5) keeps the
  Extreme-only rules (economy, stock, mining, standing, energy cells, hit cube 650, medals, fines, self-damage). The save
  slot rows show the difficulty. Remake: the pause and station menus' Options > Gameplay has a Difficulty row
  (`OptionDef.inGameOnly`, not in the main menu or multiplayer) that changes the game in progress; spawns and the
  Extreme rules follow from the next orbit or docking.
- **Save games** (`SaveGame`, the original's RecordHandler slots; v12 records the mods, see "Mods"): one JSON file per
  slot in `Application.persistentDataPath/Saves` (remake format, `SaveData`, versioned); slot 0 = auto-save on docking
  (`ModStation::autosave`), 1..11 manual. Only saved while docked, so loading opens the Station scene. The main menu's
  Resume loads the newest slot, Load lists the slots with the original's preview fields; game over reloads slot 0.
  Manual saves: the station's system menu (Menu 172 via the Menu button, Esc on the main view or the controller's Menu:
  Save game 30 with the slot list, slot 0 refused with 487, overwrite asks 49, then 50; Back to Main Menu 522 / 523).
- **The co-op campaign's host** (2026-11-01, `SaveGame.HostSaves`; docs/notes/multiplayer-coop.md "Persistence"): the one
  session game that saves into the slots. The host continues a slot or starts a new game; while the session runs it
  auto-saves on docking, the station's Save game is there, and leaving docked saves slot 0, each save under the slot's own
  difficulty and economy (the session plays Normal / Android). Every other session game is never saved.
- **Original game saves** (remake; the format `OriginalSave` + `OriginalRecord`, the mapping `OriginalSaveConvert`, the
  files `OriginalSaveTransfer`, all GoF2.Game; `OriginalSaveTests`, `OriginalSaveTransferTests`): the Android original's
  `gof2_save_game_N` (+ the unsigned `gof2_save_game_preview_N`) in
  `Android/data/net.fishlabs.gof2hdallandroid2012/files`, `RecordHandler::recordStoreWrite` 0xdf760 / `recordStoreRead`:
  raw little-endian fields (int 4, bool 1, long 8, float 4, strings an int length + UTF-16LE), the later versions'
  fields after the int 0x6e6a78, blueprints' invested amounts without a count (the product's recipe length, Android and
  Default economy differ: `OriginalSave.IngredientCounts`), and a SHA-256 signature over the data, `addHash`'s 25-byte
  salt and the key `g_android_origami_super_club` (the community build: "9506fe987e36474d"; Fishlabs' store build: the
  device's android_id, so those saves are tied to a phone; `recordStoreRead` refuses a wrong one, `OriginalSave.Read`
  doesn't check, `Write` signs). Checked against the decompile and the community tool gof2edit's layout (AGPL, not
  copied). A PC Full HD save (VGTimes' "all medals" save, 2016; same file names in `%APPDATA%\Galaxy On Fire 2 Full HD`)
  is the same stream unsigned with its own tables (109 stations, 196 items, 36 medals, 17 blueprints, Default economy,
  no 0x6e6a78 fields): both its slots and previews read and wrote back byte for byte (not in the repository: a third
  party's file). 965 real Android saves (XylonDrag's GitHub mission / fast-start saves for the community builds, RE-FHD
  and OG, steps up to 162; not in the repository, no licence) all read to their last byte (964 Android economy, 1
  Default); the 773 signed with the community key wrote back byte for byte, the other 192 (`gof2_save_game_-1` / `_1-1`,
  no previews, presumably the store build's device-ID signature) differ only in the signature; all 773 previews byte for
  byte. **Import / export** (the Load panel's second row, "Import original save" / "Export for original game", hidden
  without a file picker, `ModImport.Available`; `MainMenu.PickOriginalImport` / `AskOriginalExport`): import picks one
  `gof2_save_game_N` (`ModImport.Pick`), `OriginalSaveConvert.Read` tries signed / unsigned x Android / Default recipes
  and takes the reading that ends on the last byte and looks sane (`Plausible`), `ToSave` maps it (the campaign from the
  index: 84+ Supernova, 45+ Valkyrie; the station the stack's current one; the story mission rebuilt from the step with
  the save's counters; the freelance mission, blueprints, Wanted board, Kaamo storage, medals' counters, Status+0xc8..
  stats, known prices; the bar visitors re-rolled; the save's economy), checked by `SaveGame.TryParse` like an imported
  save, then a question with the station / step / credits and which game wrote it, written into the first empty manual
  slot (1-11; none = "delete a save first"). Export (`OriginalSaveTransfer.Export`) writes every slot as
  `gof2_save_game_N` + `gof2_save_game_preview_N` (same slot numbers) into `Transfer/Original saves <date>` signed with
  the community key (`ToRecord`: Android item / station tables, equipment past the stock slot counts (the cloak bay,
  stacked upgrades) to the hold, one of each Kaamo mod, the 27 story agents, mod items left out, a note for each): they
  load in the community builds, not in Fishlabs' store build (device-ID signature). Checked: all 967 real saves (773
  community, 192 store-build, 2 PC) convert and pass TryParse; three imported (a Valkyrie loaner at 56, Supernova 128,
  the PC save) loaded docked in Play mode without errors; an export of four slots read back signed with every field
  matching. Not yet: an exported save loaded in the original game on a phone.
- **Deleting saves** (remake, players' suggestion; `SaveSlotRow`, the main menu's Load list and the station's Save /
  Load lists): holding a used slot for 3 s (`SaveSlotRow.HoldSeconds`; a tap / click held without dragging, or Delete /
  the controller's X held while the row is selected) fills it red from the left ("Hold to delete...") and deletes it; a
  line under the list says how (`AttachHint`, by input mode); a key must be let go before the next row can start (the
  selection stays on the emptied row); the click that ends a hold doesn't load / save (`HoldUsed`). `SaveGame.Delete`
  moves the file to `Saves/Deleted` (time-stamped, the newest 20 kept) so a slip can be undone by hand.
- **Save export / import** (remake, `SaveTransfer`, main menu only: the Export saves / Import saves buttons under the
  Load game list, `MainMenu`; the list is rebuilt after an import; without a file dialog a note under them names the
  Transfer folder): Export writes every slot's file unchanged into one `.gof2saves` JSON (format tag, format version,
  game version, the slots). Import replaces every slot: the file is read and checked whole first (`SaveTransfer.Check`:
  format, at most 12 unique slots 0-11, each save through `SaveGame.TryParse`: version 1..current, campaign, economy,
  finite times, difficulty 0-2, story index 0-162, and every station / ship / item / system index it names, cargo,
  equipment, Kaamo storage, the parked ship, shop memory and bar visitors, blueprints, production, the Wanted board,
  against the game's tables), then a warning that it overwrites ALL saves (the auto-save too); only Yes writes, after
  checking again: the new slots to `.import` files, the old ones moved to `Saves/BeforeImport` (put back if writing
  fails), then the new ones into place. Windows and the Editor pick the file with the system dialog (`FileDialog`:
  comdlg32 / EditorUtility); Linux and Android use a Transfer folder (`~/Documents/GoF2 Remake`; on Android the app's
  `files/Transfer`, reachable over USB) and import the newest `.gof2saves` there.
- **A finished game imports** (#117): `SaveGame.TryParse` accepts -10 as the invaded system / station (`Story.OnDepart`
  writes it once the game is won: nothing attacked any more); every finished game's auto-save was refused.
- **Save folder** (remake, a player's request: Android's Transfer folder in Android/data is out of other apps' reach;
  `SaveFolder` + `SaveFolderPicker` (GoF2.Game), the "save folder" part of `MainMenu`; Options > Gameplay "Save
  folder: ...", a Button row with `OptionDef.menuOnly` (not in the pause / station menus), default none = unchanged): the
  slots and `ship_presets.json` are mirrored into a folder the player picks, for file managers and sync tools (Syncthing).
  A mirror, not a move: the game always reads and writes its own Saves (`GamePaths.Data`, so the tests' sandbox and the
  caches are untouched) and `SaveFolder.Sync` keeps the folder in step after every save / delete / hardcore death / import
  (`AfterLocalChange` in `SaveGame`, `SaveTransfer`, `OriginalSaveTransfer`, `ShipPresets`, which reads its file again on
  `SaveFolder.Revision`), on main-menu entry and before the main menu's and the station's save lists. Rules per file against
  the hash both sides had at the last sync (`save_folder_sync.json` beside Saves, with the files' sizes / times so unchanged
  ones aren't read): one side changed it = that side wins, one side deleted it = deleted on the other (a local copy to
  Saves/Deleted as `_removed`), both changed or no common past = the newer `savedAtUtc` (other files the file time) wins and
  the loser goes to Saves/Deleted as `_conflict` (newest 20 kept); a pulled slot's file time is its `savedAtUtc` (Resume's
  newest slot). Choosing a folder (`Inspect` / `Choose`): an empty one asks Copy them / Start empty (this device's set
  aside in Saves/BeforeFolder/<time>), one with saves Keep the newer / Only the folder's; "This device only" (`UseDefault`)
  keeps everything. An unreachable folder (unplugged, deleted, grant revoked: `LastProblem`) never blocks a save: a notice
  once per run, the row's description, and the next sync catches up; a missing folder is never created (a drive's mount
  point). Android: Storage Access Framework (`Plugins/Android/SaveFolderActivity.java`, ACTION_OPEN_DOCUMENT_TREE +
  `takePersistableUriPermission`, its static list / read / write / delete over the ContentResolver as UTF-8 text; registered
  by the Editor's `AndroidModImportActivity`), no manifest permission; MANAGE_EXTERNAL_STORAGE (All files access) was
  rejected: Play-restricted, a settings trip, and plain paths can't read other apps' files without it. iOS: the document
  picker in folder mode (`Plugins/iOS/SaveFolderPicker.mm`), the folder kept as a security-scoped bookmark (target
  `ios-bookmark:<base64>`), opened again for each sync and then plain file calls. UWP: FolderPicker, the folder in the
  FutureAccessList under a token of its own per pick (`uwp-folder:<token>`), its files through Windows.Storage (waited on
  from Unity's app thread). Desktop: a plain folder (Editor OpenFolderPanel, Windows SHBrowseForFolderW on an STA worker
  thread, Linux zenity / kdialog, macOS osascript). Not mirrored: Mods, the Transfer folder, mod_registry.json (`ModSaves.Fix` renumbers by key). Tests
  `SaveFolderTests` (a sandbox folder as the other device; `SaveFolder.TestTarget` / `StoreOverride`, never the player's
  choice while `GamePaths.Override` is set). Verified in the Editor (Windows); not yet run on Android, iOS, UWP, Linux or
  macOS.
- **Hardcore** (remake, permadeath; the new game's toggle in the Game options panel after the economy
  (`MainMenu.PickEconomy` -> `gameOptionsPanel`: the Kaamo Club, Hardcore, Tutorials, then Start game, amber like the
  station's Launch (`.choice-button--start`), with the difficulty and economy picked), `MainMenu.hardcoreNext`, off
  whenever a difficulty is picked; not the original's "hardcore mode", which is Extreme): `Session.Hardcore` with
  `Session.RunId` (a new id at every new game; save v15 `hardcore` / `runId`); it saves only to the auto-save
  (`SaveGame.Save` refuses other slots, the station hides Save game); the player's death (`FlightHud.OnGameOver`, not in
  sessions or with an event's respawn point) deletes every slot of the run at once (`SaveGame.DeleteRun`: closing the
  game on the death screen keeps nothing) and Tap returns to the main menu; mission and quest failures still reload the
  auto-save; the slot rows say HARDCORE.
- **New Game+** (remake, GitHub #4, `NewGamePlus`): with a finished game in the slots (main game past 44, Valkyrie past
  83, Supernova at 162; the newest one) the campaign panel shows a "New Game+" toggle (off each time it opens). Turned
  on, the new game (after the campaign's first step) gets the old credits added, the unlocked blueprints and the medals
  (the better grade of each), the Kaamo Club owned with the old storage plus the old ship (its mods), its equipment and
  cargo (not the story's unsaleable items) parked in it; story, map, standings and the starting ship are new.
