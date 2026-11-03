# Mining

Notes moved from `CLAUDE.md` (its index lists every notes file). Code comments citing `CLAUDE.md "<section>"` mean the section or bullet of that name here.

## Mining

Research: `Reference/research/mining.md` (+ `Reference/tools/mining/mining_tables.py`). `Mining` (on the player ship,
set up by `SpaceLevel`) runs lock -> autopilot approach -> landing -> minigame -> payout; `MiningGame` is the minigame
logic (plain C#); `MiningView` draws the lock ring, ore plate, HUD messages and the minigame in the flight HUD.

- **Asteroids** get an ore (`Galaxy::getAsteroidProbabilities`: ores near their cheapest system dominate, e.g. Mido =
  Pyresium, Gold) and a class from the scale (D/C/B, big ones 50 % A) in `OrbitBuilder.SpawnAsteroids`; stored on
  `Target` (`oreItem`, `quality`, `scale`). Naneroh (109, `Status::inSupernovaOrbit`) after step 0x59
  (`Session.WorldIndex`, so the finished game's world too): every ore is Novanium (217, its core 218) on the type-3
  magma asteroid (`sn_asteroid_magma` + its explosion; `OrbitBuilder.NovaniumOrbit`); the remake had left it out, so
  Novanium couldn't be mined. Remake: the explosion billboard (0x4213, `asteroid_explosion.png`) is shared by Explosion
  types 2 and 3 and its fragment cell is Void-crystal purple, so the ordinary asteroids (type 0) use a rock-coloured
  copy (`Resources/GoF2Combat/asteroid_explosion_rock.png` from `Reference/tools/combat/make_rock_explosion.py`,
  `Target.explosionTexture`); the Void asteroids keep the purple.
- **Lock:** needs a drill (category 19) mounted; the crosshair box (+-w/16) must hold the asteroid for the scanner's
  lock time (attr 29, 8000 ms without) - 200 ms. The action prompt then says Mine. Without a drill (`Radar::draw`
  0x157b00: hudEvent 0x14, the original keeps the ring full and repeats it every frame) the remake says 541 "No drill
  installed." once and drops the lock; that asteroid isn't tried again until it has left the crosshair box
  (`Mining.refusedNoDrill`), so looking away and back starts a new lock. The original takes the nearest rock in 3D whose
  centre is in the box; remake, with the Smart target lock (Options > Gameplay "Target lock", Discord report 2026-10-24:
  it locked a rock beside the crosshair, not the one under it): a rock the line through the crosshair meets comes first,
  the front-most (its centre may be outside the box: a big rock over the crosshair), else of those centred in the box the
  one whose edge is nearest the crosshair in degrees, the rock already locking 0.5 deg ahead so two side by side don't
  restart the ring by turns (`TargetLockRules.AsteroidScore`, the visible radius `Mining.SurfaceRadius`).
- **Approach:** player steering off, full throttle, autopilot turn; last 2000 units: exhaust off, landing sound, chase
  camera frozen, the model pitches ~80 deg nose up; stops at `scale * 2500` units, asteroid spin off. The booster works
  on the way in and the landing (MGame::OnTouchBegin refuses it only while drilling: PlayerEgo::isMining is the
  MiningGame), and the approach flies at moveToPosition's dt x throttle x the current speed, so a boost speeds it up
  (`ShipController.externalControl` keeps the boost timer running, `FlightModel.TickBoost`).
- **Minigame:** layers = class (A 7 .. D 4), 6 s each inside the ring; ore rate `yield * ((layer+1)/7*2.35+0.15)` t/s; a
  2.5 s off-target energy budget per session (empty = no ore). Drill (`Mining.ReadDrillInput`, the stronger wins): the
  touch stick or the Drill row (`GameControls.Drill`: arrows, W A S D, the left stick), squared per axis like
  `Hud::getAnalog`, or with mouse steering on the mouse (the PC version: `PlayerEgo::update` feeds the cursor's offset,
  clamped to +-0.7 of half the screen, linear; centred when the approach docks); `MiningGame.SetInput` = 3 x the shaped
  value (it squared again: the stick ran at raw^4). Perfect runs with IMT Extract 1.3: D 14 t, C 20, B 28, A 37 + 1 core
  (verified in Play mode).
- **Payout:** ore capped to free cargo (core first), "12t Pyresium" messages, the asteroid explodes (no crate); ores and
  cores are normal commodities in the shop.
- **Shot asteroids** (`PlayerAsteroid::update` 0xf7060, `Target.DropAsteroidCrate`): an asteroid destroyed by a hit
  (guns, rockets, blasts, NPC shots, ramming; not mining, whose `Target.Explode` clears the loot, nor another game's
  destruction in multiplayer) leaves a crate for the tractor beam: class A 4 % with 1 core (Void Crystals' Void Essence,
  Novanium's 218), the others 20 % with 1-3 t of the ore, so ore comes without a drill too. Only the game's ores
  (154-164, 217) have cores (`Target.CoreItem`, -1 for any other): a mod's ore in an orbit's field (stations.json
  `orbit.asteroids.ores`) gives none from mining, the beam or a class-A crate (ore + 11 handed out whatever item came 11
  numbers after it until 2026-10-25). The model is `createCrate(1)`'s rock container 0x421e `asteroid_01_junk`, Void
  Crystals (2) 0x421f `asteroid_void_junk` (`Crate.look`, `CombatAssets.CrateModel`, synced by `NetCrate`; the junk's
  0x4218 is look 3); race -1 (no standing or Alien Hunter rules). The remake had left the drop out until 2026-10.
- **The fire button** (`MGame::OnTouchBegin` 0x1a838c: the original's fire button is the mining button too): during the
  approach, the landing and the settle it turns the autopilot off (571 + 39, sound 29, no ore) and that press fires
  nothing (`Hud::releaseAllKeys`, `WeaponSystem.SwallowPrimaryPress`); in the minigame it stops mining with the ore so
  far and, still held, the guns fire at once. The primary guns are silent from the approach on
  (`WeaponSystem.PrimaryBlocked`), missiles still fire until the minigame (`MGame::OnTouchEnd`), which blocks
  everything. The touch fire button stays on screen meanwhile. Not done: the original also starts the approach with the
  fire button while an asteroid is locked (the remake: the action prompt).
- **Mining beam** (remake, for mods: a drill item with attribute 100 = 1; stats `miningBeam` / `miningBeamRange` 101 /
  `miningBeamLayerMs` 102 / `miningBeamLook` 103, `ItemStats`; the example is the `mining_beam` mod in the mods repo, the
  IMT Extract Beam 5.0): `Mining`'s beam mode keeps the ship in free flight (State stays Idle: no prompt, `Interact` and the
  action arrow do nothing). The lock is the usual one; holding fire on the locked asteroid claims the fire button
  (`WeaponSystem.FireClaimed`, `Mining.BeamClaimsFire`: the primaries stay silent; only while the beam cuts or could start:
  the nose on the rock, in reach, room in the hold, else the guns keep the press; a press still held when the beam stops
  is swallowed until released, so it doesn't turn into gunfire at the next rock) and cuts it within attr 101 units of its surface (default 24000,
  the beam lasers' object reach), the lock following it in a ±w/5 box while the beam holds. `MiningBeamExtraction` (plain
  C#) is the minigame's ore logic without the minigame: the layers one by one at `MiningGame.OreRate` × attr 33, attr 102 ms
  each with the rate scaled to it (a whole asteroid = a perfect minigame run × the yield); the progress stays per asteroid
  (`Mining.BeamProgress`, shown on the lock plate as a percentage); depleted: the core (class A), `Explode` with
  `MiningOut`. The beam is fixed to the ship: straight along its heading (the unbanked root, like the guns), cutting
  where that line meets the rock's visible surface (a sphere of 0.85 × the mesh's bounding radius = hit radius / 0.7 × 0.85;
  the hit radius alone hid the impact and the chunks inside the rock), the beams ending a fifth deeper; off the rock or
  beyond the reach (counted along the beam) it runs straight ahead at full length and cuts nothing.
  `MiningBeamFx`: beams from the leftmost and rightmost primary mounts (the attr-103 beam laser's projectile:
  a steady looping core without its fade + two pulses replayed with it), its impact every 300 ms, particles.png star
  sparks, every ton a chunk of the asteroid's own mesh with a glow and a sparkling trail spiralling into the ship (0.7 s +
  1 s per 900 m); the ton enters the hold as it is cut (`Mining.Launch`; the chunk is the look only, its arrival a haptic:
  delivering on arrival put chunks still flying at a scene change into the next scene's state, after a failed mission the
  reloaded save's), a class-A rock keeping one ton of room free for its core (`BeamRoom`). Extreme: half the ore while
  the rock stands, the withheld half when it is depleted (`MiningBeamExtraction` halveUnfinished, like
  `PlayerEgo::stopMining` halving an unfinished run). The mod's icon is its own: items.json `icon` (any mod
  item, or an override's for an original item, a new item without one its nearest base's; `ModContent.ItemIcon`, read by
  `ItemInfo.ItemIcon`, which every shop / HUD
  icon goes through; uncompressed, clamped), here Gunant's Drill's icon with the beam turned green. Sound: the beam laser's shot on ignition, the repair
  beam's hum and the drill's `DrillSound` by layer; haptics as the drill on target. Not counted: Ore Athlete (38: it
  rewards the minigame's perfect runs, which the beam has none of), the multiplayer ore split (each player keeps what their
  own beam cuts).
- **Mining time** (remake, for mods, 2026-10-25: EVERSPACE 2's DT_Mining_Yield durations; items.json stat `miningTime`,
  attr 132, % of `MiningGame.LayerMs`, 10..1000, default 100): an ore's layers last that much longer
  (`MiningGame.LayerDurationMs`) at a rate scaled down to match, so a whole asteroid's ore stays the same; the beam's
  layer time the same (`Mining`). The ES2 importer gives its crystals 200 (ES2's 10 s against 5 s). Test
  `MiningGameTests.AnOresMiningTimeStretchesTheLayersForTheSameOre`.
- **Ore into cores at the Mido Mining Plant** (`MiningPlant`, 2026-11-01; players' report: the remake never had it): docked
  at the Coromesk plant (103) with an empty campaign mission (steps 107, 128-130 and from 162 on), not in the alien orbit
  (`PlayerEgo::isDockedToMiningPlant` 0xadcb8), the docking asks 290 ("#" = 30, Extreme 100) with every ore in the hold
  (286 without; `MGame::OnUpdate` 0x1ac778, `Traffic.Ask` from `SpaceLevel`'s `Docking.Docked`). Yes
  (`MGame::OnTouchEnd` 0x1a98d8): ores 154-164 -> core = ore + 11 (Void crystals -> Void essence), Novanium 217 -> 218,
  each full 30 t (100) of one ore -> 1 t of its core, the rest stays ore; then 291 + the cores, or 292. A mod's ore is
  listed but not converted (the original's table). Test `MiningPlantTests`.
- **The Mining Plant's menu** (remake, 2026-11-02; players' wish: every option at once, not one question after the
  other, then "make the text better"): docking there opens one menu (`SpaceLevel`'s `PlantMenu`, `Traffic.ShowMenu`,
  `PauseMenu.Choose`) titled 3210, in place of 290's question: two sections (`MenuSection`, drawn by
  `PauseMenu.BuildSection` with the cargo page's rows): ORE INTO CORES (the ratio, then each ore in the hold with what it
  makes, "→ 2 t Titanium Core" in cyan, or "18 t more for a core" dimmed; `MiningPlant.Section`) and SPACE WASTE SORTING
  (the fee a ton, then the waste with "all for 5,100$", "20 t for 2,040$" or "not enough credits";
  `WasteProcessing.Section`), each with a line when there is nothing to list. Then only the actions that do something
  now: CONVERT ORE TO CORES with a core to make (`MiningPlant.CoresMade`; the original's Yes: 291 / 292), SORT SPACE
  WASTE: n t for the fees with a ton paid for (choosing it pays), LEAVE (Back too). Each action comes back to the menu.
  Its entries are centred (the first and last carry the A / B prompts, which centre a button).
- **The docked menu** (remake, 2026-11-03; players' wish: the station hangar's kind of menu when docked at the carrier or
  the Mining Plant): docked at an object with options, the flight HUD shows `UI.DockedMenu` (built in code, inline styles):
  the object's name over a column of buttons, its own options, then Inspect ship and Undock. The carrier / Vossk
  battleship: Resupply (`CarrierShopWindow`, its close button and Esc / B now BACK to the menu); the Mining Plant
  (`ObjectDocking.Options`, set by `SpaceLevel`): Convert ore to cores and Sort space waste, each its own page of the pause
  menu's question window (`PauseMenu.Choose`: that section, its action when it does something, Back). Inspect ship is
  Action Freeze's photo tools opened straight (`PauseMenu.OpenInspect`: Back closes the pause menu, back to the docked
  menu); `ObjectDocking.CameraHeld` (set by `PhotoMode`) keeps the docking's look-at off the photo camera, the pause
  menu's own Action Freeze while docked too. Keys: up / down, Enter / A, 1-9, Esc / B undock (the guns blocked on the way
  out as before). The game waits under it (`Navigation.OpenMenu(force)`; multiplayer runs on). VR keeps the old flow
  (the resupply window at once, the plant's one menu). Other dock types (story transfers, hacking, pads) are unchanged.
- **Space Waste sorting at the Mining Plant** (`WasteProcessing`, remake-only, 2026-11-02; players' suggestion on Discord):
  the plant menu's entry sorts the Space Waste (99) in the hold for a fee, a gamble. Every ton is sorted on its own
  (players: "just go through each item"; first 10 t lots, dropped): it pays the fee and is junk or one ton of something
  (gear: one piece); all of the hold's waste, as much as the credits pay for. The waste sells for 5-6$ and stations
  stock hundreds of tons, so a ton must never pay: the fee comes from the active item table so that a ton returns
  `Payback` (93 %; 92.3 % after rounding the fee up to 1$) of fee + waste in the long run, both economies (Android 99$ a
  ton, Default 102-103$; Extreme 1.5x). Tiers (`FindTier`) by what a find is worth a ton in the active table
  (`ScrapFrom` ..): Scrap 25-99$, Salvage 100-399$, Valuable 400-1,999$, Rare 2,000-4,999$ goods or one piece of shop
  gear up to tech level 5 (none built from a blueprint), Exceptional 5,000$ and up. The pools are every commodity, ore
  and core the shops stock (occurrence > 0), so they follow the economy (the Default Economy's Void crystals, 400$, are
  Valuable; Collectible Figures, 5,005$, Exceptional) and hold 12-32 goods a tier; never junk under 25$ (towels, basic
  food), never mod items. Contraband-sounding goods are in (drugs, explosives, radioactive goods, organs): the game has
  no rule against cargo. The four plasmas (201-204) only once the story is past 142 (its plasma kit tutorial counts any
  plasma aboard), Red Plasma volatile in the hold (`VolatileCargo`); never the K'mirkk Toad Mutagen, the other volatile
  good (a story item, 117-122, worth 512,500$ in the Android tables). Odds a ton: junk 64 %, Scrap 21, Salvage 11,
  Valuable 4 of the rest; a rare find 0.3 % (gear 12 : rare goods 6 : exceptional 4). Bad-luck caps (not shown to the
  player): at most 6 empty tons in a row, a rare find at the latest in the 500th ton since the last. With them 38 % of
  tons find something, 1 in ~260 t is rare, 1 in ~1,400 t exceptional (`LongRun`, the steady state the fee is worked
  out from, about 0.1 s once per table). Simulated (1,000 players x 30 holds of 150 t, both economies): a hold finds 57
  things and gets back 77 % in the median, 53-54 % in the worst tenth, 1 in 4 a profit; after 30 holds the median player
  is at 91-92 %; 70-80 % of the return is in the common tiers (tuned for "more rewarding": with 10 t lots and the rare
  finds carrying half the return the median hold got back 50 %, then 72 %). The rolls come from `Session.RunId` and the
  saved counters `WasteTons` / `WasteDry` / `WasteSinceRare`: reloading finds the same. The result page
  (`PauseMenu.ShowSorting`, `Traffic.WasteSorted`; a plain message without the HUD): one big grid, a square a ton, forty
  a row (players: "each one a square", "one big grid"), gone through one by one (70 ms a ton, faster for a big load so
  it takes about 8 s, a rare find after a 1.4 s pause with its square charging, the last five slowing down like a reel),
  junk dark, a find lit in its tier's colour (grey, green, blue, purple, gold) as it is reached; the grid scrolls past
  196 px and follows, the finds' list (best first, with the tier names, so not the colour alone; each row shown once its
  ton is reached) past 220 px, so the page stays put; then the fees beside what the finds are worth (no find dressed up
  as a gain); OK / Back skip the rest, then close. Sound: a soft generated tone as a find lights (`LotTones`,
  `PauseMenu.LotSound`: a sine pip pitched by tier on a pentatonic scale, a two-note chime for Rare / Exceptional; about
  -25 to -27 dB RMS; at most every 90 ms, none while skipping), then one sound for the best find
  (`FlightHud.PlaySortingSound`, `CombatAudio.wasteFound` / `wasteRare` / `wasteExceptional`): Button_Sell for Scrap ..
  Valuable, Hacking_Solved for Rare, Mission_Accomplished for Exceptional, the question window's for nothing usable.
  Tried and dropped: game clips per lot, a start sound (the hatch, the extractor loop) and a charge before a rare lot
  (too much); the page silent between (too quiet). Space Waste's item details name the plant once it is built (campaign
  > 0x54, not free play). The texts say "found", not "won". Test `WasteProcessingTests`.
- **Remake-only:** Var Hastra (78) always stocks a drill (IMT Extract 1.3) while there is no campaign; releasing the
  stick stops the drill's player movement (the original keeps the last input). The mining plant at 103 is a docking
  target (see "Supernova add-on"). The drill sound (`DrillSound`, event 1's layers by drill_speed = (LAYER_SPEEDS[layer]
  − 5) / 33 · 3): Slow_1 always, Add_1 from 1 (pitch ×0.896 → ×1 at 2, then ×1.196), Add_2 from 2, a Switch click on
  entering 0 / 1 / 2, event volume 0.244; off target (`MiningGame::update`: stop(1) + play(3), back on play(1) +
  stop(3)) event 3 Mining_Drill_Broken is one loop (10 s wave) at 0.245 until the drill is back on target or mining
  ends; the landing (2) at 0.183. **Ultrascan** (84, attr 30, `Mining.ClassAMarkers`, `Radar::draw` 0x1577de): inside
  the field's 100 000-unit sphere (the level's field, `OrbitLayout.asteroidCentre`: also the Void's crystal field, which
  has no autopilot "Asteroid field" entry; the remake took the entry's position and showed no letters there), not
  docking / mining, every class-A asteroid on screen gets the "A" letter (0x44e frame 0) with its top-left on the
  asteroid.
