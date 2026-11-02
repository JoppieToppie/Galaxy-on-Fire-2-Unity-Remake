# Debug tools

Notes moved from `CLAUDE.md` (its index lists every notes file). Code comments citing `CLAUDE.md "<section>"` mean the section or bullet of that name here.

## UI and platforms

- **Debug sizing** (remake): the main menu's Debug panel is 88 % of the screen high with the mission list and the cheats
  (a ScrollView following the focus) filling it; the pause menu's Debug page scrolls its rows (`.debug-scroll`, as tall
  as them, shrunk to the panel) with the selection kept in view; the station's Debug page is as tall as its 570 px list,
  at most 92 % of the screen (`.system-menu--wide`; it filled the 92 % on every screen from 2026-10-12 to 10-18). Both
  in-game Debug pages are windows like their Options pages (OptionsView, 2026-10-22): the cyan left edge, the title with
  its amber `.panel-accent` (`PauseMenu.accent`, `StationMenu.debugAccent`; cyan in flight, white in the station like
  its Options), “‹ Back” at the bottom left and the keys in the divided `.prompt-bar--row` under it; the open tab has no
  fill (the shared tab look); the cards (`.debug-card`) have the panel line and no fill, the item tiles a faint panel
  blue (white fills of 3-4 % come out as a flat grey: the UI blends in linear colour space). On a small high-density
  screen the screens' root gets `ui-large` (`UiScale.Large(height, reference)`, set with `layout-phone` in MainMenu /
  StationMenu / FlightHud) and the debug, pause, system-menu and choice rows grow about 1.35x.
- **Debug panel** (remake-only testing tools, `MainMenu.BuildDebugPanel`; F10, LB + RB or three fingers held for 1 s, or
  five taps on the version text, each within 1 s of the last; its tap zone reaches 40-60 px past the text; or Options >
  Gameplay "Debug tools", `Cheats.Unlocked`, which also shows a Debug button in the main menu; with Resume shown too the
  column starts higher, `.main-column--full`, so Exit stays clear of the version text and its tap zone): on the left the
  mission list: a search field (every word must match; a number matches the step index) and every step 0-162 by campaign
  (53 / 129 skipped), "index · title · station" over an optional subtitle (`StepSummaries`,
  `Resources/GoF2Data/step_summaries.json` from the hand-written table in
  `Reference/tools/campaign/build_step_summaries.py`: a one-line title per step, the subtitle for extra info; both cut
  to one line); a row → the difficulty panel → `Story.StartAtMission`: the step's campaign start (main / Valkyrie from
  45 / Supernova from 84), every step up to it through `Advance` (side effects) with its reward credited, Betty past the
  rescue, the tutorial's free weapon and armor past step 6 and its hints shown past 8; a replay of what each step's task
  leaves the player with (`Story.PlayStep`: Betty, the tutorial gear and hints, 20's EMP GL I, 50's Vossk standing, 58's
  Liberators, 77's Cronus, 91's miners and cabin, 118's mutagen) and where it leaves them (`PlaceAfter` / `ExitTo`, the
  Void return station); then the step's story orbit (launched when docked there, else an arrival from the previous
  place) or that place (docked, never at an empty orbit or a refusing station). Steps only reached inside another (42,
  46, 53, 107, 129, 149 / 150) aren't offered (`Story.MissionSelectStart`). On the right the cheat toggles (`Cheats`,
  PlayerPrefs `cheat_*`, rows from `CheatsCatalog` through `OptionControl`): god mode (`Target.Damage` /
  `PlayerHealth.Kill`, no gamma drain, volatile goods safe), infinite ammo (`WeaponSystem`), no boost cooldown
  (`FlightModel.UpdateBoost`: ready again as a boost ends), no primary weapon cooldown (`WeaponSystem` reloads the held
  primaries as fast as their bullet pool sustains, `Gun.SustainedReloadMs` = lifetime / pool: reloaded every frame they
  emptied the pool in a burst and waited out a lifetime), no secondary weapon cooldown (`WeaponSystem.FireSecondary`:
  the reload skipped; the pool still limits rockets / bombs in flight), one-hit kills (the player's hits;
  script-invulnerable ships still survive), instant locks (`Cheats.LockMs` in `CombatRadar` / `Mining` / `Navigation`),
  free shopping (items and ships, `Hangar`), free jumps (`GalaxyMap.EnergyCells`, `SystemJump`, the cloak). Once opened
  (`Cheats.Unlocked`) the pause menu and the station's system menu get a Debug page with the toggles and the actions:
  +100 000 / +1 000 000 credits, repair (hull, shield, armor, gamma), secondaries to 50, +20 energy cells, reveal all
  systems, make peace (both standing axes 0, `AttackedStations` cleared), fill the Kaamo Club (`Cheats.FillKaamoClub`:
  owned, one of every item the storage takes (50 of each secondary; not the unsaleable story items) and a hull of every
  ownable ship type, not 13 / 14 / 15 or the one flown; topped up, never doubled); in flight under them **Destroy around
  you** (`CheatsCatalog.Destroy`, `DebugSpawner.KillAround`, 2026-11-02): a range of 10 m - 30 km (`KillRanges`, measured
  to the nearest hit box, so a capital ship counts by its hull) and Hostile only (`hostileToPlayer`: turrets and sentry
  guns too) / All ships / Everything (asteroids, every Target); each takes the One-hit kills hit from the player (loot,
  bounties and standing as if shot; the story's invulnerable ships survive; never the player or another player,
  `netPilot`); **Give items** (item type, item (a
  grid of the type's shop icons with their names: a tap / click picks, the picked one framed and scrolled into view, two
  rows visible, the cells sharing each row's width; its stepper is an invisible focus stop for keys / controllers:
  selected, the grid is framed amber, left / right step through the icons and up / down a row
  (`CheatsCatalog.MoveVertical`, the columns counted from the cells' layout; the grid has no scroll bar: one appearing
  narrowed the rows by a cell, so down went one to the right; from the top / bottom row on to Item type / Amount, a
  short last row takes its last icon); `CheatsCatalog.ItemPicker` on the shared `GridPicker`, which the Ships tab's Ship
  row uses too (the shop icons; the hulls not normally flyable with Build Hull Icons' renders), #82; bigger text in the
  debug cards on phones; the Debug pages are 1180 wide, the station's 570 high, so the buttons fit); keys / D-pad on the
  Debug pages move on screen (`SpatialNav`: up / down the nearest row, left / right the item beside, no wrapping), left
  / right only step steppers, segments and sliders (`OptionControl.StepsSideways`; a toggle flipped on left / right, and
  down from Add to cargo hold went to Add and mount), amount 1-1000: into the
  hold; docked also "Add and mount", `Cheats.GiveAndMount` through `Hangar.Mount` / `Swap`); in flight **Spawn** (`DebugSpawner`):
  any ship by race / model / behaviour (hostile, by standing, friendly) 400 m ahead as ordinary traffic (a "Turrets"
  row: their own, or any turret item on every turret mount and on a spawned capital ship's turret objects), and any
  assembly of
  `assemblies.json` by category as scenery ahead of the player, far enough out for its size; **Presets** tab
  (`ShipPresets`, `CheatsCatalog.Presets`; players' suggestion): 14 slots, two full rows of the grid (8 before;
  `ship_presets.json` is only ever padded up to `Slots`, never cut, so a file either way loads) (picked in the same icon
  grid, `GridPicker`: the ship's icon and "N. ship", an empty slot by name; under the grid a hint panel, `GridPicker`'s
  hint / `CheatsCatalog.PresetHint`, for the picked slot (it followed the pointer too, so a preset's equipment changed
  as the mouse crossed the slots): the ship, its mods and save time over its equipment as shop icons with names and
  amounts, the same item once with the amounts added) in `persistentDataPath/ship_presets.json` (outside the saves),
  each the hull flown (its PlayerHull key, debug hulls too), the mounted equipment with its amounts and the ship mods;
  Save the ship flown now, Load (equipment and mods set, the hull flown through `PlayerHull.Fly` or rebuilt in place
  when it is the same; docked a hull the hangar can't take is refused first; in flight `PlayerHealth.RefreshLoadout`
  sets the maxima from the new loadout, full), Delete; items no longer in the game are left out; **Ships** tab
  (`PlayerHull`, `CheatsCatalog.Hulls`, `SpaceLevel.SwapPlayerShip`, which also sets the hull, shield and armor maxima
  from the new hull and equipment, each pool keeping its share: `PlayerHealth.RefreshLoadout(db, true)`): fly any of the
  game's 68 hulls where the player is, picked like Give items by Ship type (the races from the model names, Other, Modded (the mods' ships.json
  ships; the list is made again when the mods change, `PlayerHull.CheckMods`) and Not normally flyable) and Ship: ships
  0-63 with a model (50 / 53 have none), the Vossk freighter 13, the three race
  freighters 15 (`Database.ShipAssembly` now resolves 13 / 15 like `Globals::getShipGroup`), the Terran battleship 14, and
  the capital ships outside ships.json flying with ship 14's stats: the Terran carrier, the Vossk battleship, the Valkyrie
  battlestation and the Void mother ship (held at its full-size pose like the alien orbit, `PlayerHull.PrepareModel`). The
  capital ships' turrets (`TrafficPlan.BattleshipTurrets` / `CarrierTurrets` / `VosskTurrets`, `ValkyrieLevels.StationTurrets`
  with the shield generators as scenery) ride on the model as the player's auto turrets (not the NPCs' fighter turret of
  45 / 51: the player's Bloodstar / Rhino have a turret slot, #45)
  (`NpcShip.MakePlayerTurret`: aim at what is hostile to the player, shots pass the player, off the radar; they fire the item
  their model is, `Hull.turretGun`: the battleship's and carrier's `turret_002` the Hammerhead D2A2 48, the Vossk
  battleship's `turret_003` the L'ksaar 49, not the NPCs' race blaster). Remake (players' suggestion): the capital hulls
  (battleship, carrier, Vossk battleship, Valkyrie) have no weapon slots, so the fire button launches the enhanced capital
  ships' salvo (`PlayerCapitalMissiles`, `Hull.missileItem`: 4 homing missiles of the race, the Intelli Jet 37 / S'koonn
  38, 250 ms apart from the turret mounts, one salvo per 4 s while held) at the locked ship when hostile, else the nearest
  hostile ship within 2 km of the hull; never a neutral or friendly one; the item's damage, x3 on hit boxes. Hulls over 150 m
  (sized without the additive glow layers) get a chase camera behind the stern (its height x `Hull.cameraHeight`: the
  carrier 0.4, just above its deck), the model centred on the pivot and a longer
  far plane, and are shrunk on the hangar turntable. The hulls the player can't normally own (13 / 14 / 15, the capital
  ships; never an ownable or mod ship) fly with weight (`PlayerHull.ApplyMass`: `ShipController.mass` = log(radius / 150
  m) / log(2500 / 150), the flown model's size: race freighters ~0.2, the Terran battleship 0.72, the carrier 0.91):
  `FlightModel.Mass` lowers the top turn rate (`TurnScale`, the carrier 4.9 deg/s against 21.6) and gives the turn rates
  inertia (`Inertia`: 2.2 s to full turn, they coast on after release), eases the speed toward the throttle
  (`MoveSpeed`, `AccelMs` 7 s x mass, the boost 2.5x faster) and slows the level-out / roll (`RollScale`) and the strafe
  / dodge (`SideScale`); less and slower banking, a heavier chase camera (its coefficients x0.35 at 1; a slow engine
  sway of the view read as shaking and was taken out again), a controller rumble while the engines strain and boost, a
  smaller boost FOV, a deeper engine (pitch x0.6, boost x0.7) heard as from a normal chase distance (`engineEarScale`:
  the fitted camera km out was past the 500 m rolloff). Mass 0 = the original's numbers everywhere. The pick is
  PlayerPrefs `cheat_hull` while `Session.ShipIndex` is its
  ship; "Back to your own ship" returns to the one flown before (`cheat_previousShip`). Hulls the player can't normally own
  (13, 14, 15 and the capital ships) never land in a hangar: no Dock prompt, the autopilot and `SpaceLevel.Dock(true)` refuse,
  the story's moves into a station (`Dock(false)`) and a station loaded with one (`StationLevel`) put the player back in their
  own ship (`PlayerHull.ForceOwnShip`); docked, the tab only swaps to ships the player can own. Debug spawns call
  `Traffic.ConnectPlayers` so they get their targets. Medals still count what cheats bring.
