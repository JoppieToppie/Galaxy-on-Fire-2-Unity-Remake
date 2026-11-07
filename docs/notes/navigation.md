# Navigation

Notes moved from `CLAUDE.md` (its index lists every notes file). Code comments citing `CLAUDE.md "<section>"` mean the section or bullet of that name here.

## Navigation (locks, autopilot, planet jump, fast-forward)

Research: `Reference/research/autopilot_travel.md` (+ `Reference/tools/autopilot/autopilot_tables.py`). `Navigation` (on
the player ship) and `NavigationView` (flight HUD).

- **Locks** (`Radar::draw`): the station and the visible jumpgate (on screen, within +-w/6 of the centre and +-w/16 of
  the crosshair) and the other stations' planets (+-w/32 of the crosshair; the current station's own planet never).
  Scanner lock time (attr 29, 8000 ms without), no -200 ms and no ring delay (unlike asteroids). Landmarks beat planets,
  planets beat asteroids (`Mining` asks `BlocksAsteroidLock`). HUD: bracket + name + "Tech level" + distance
  (`Radar::calcDistance`, the original's own "m/km") near the centre, jumpgate icon elsewhere (off screen on the radar
  ellipse), planet names only while in the lock box, top plate with the system's race icon.
- **Crosshair on the autopilot** (`PlayerEgo::draw` 0xae4d0, #85): not drawn while the autopilot flies (+0x158), outside the turret
  view; the remake makes it transparent (`.crosshair--autopilot`) so the throttle gauge and lock rings keep its place.
- **Menu presses** (remake, #85): `Navigation.PressesBlocked`, true while a menu, conversation or map halts the controls and for
  the frame after it closes: the boost, level out, brake override, dodge, gyro recenter and the secondary switch ignore their
  presses then (A on the pause menu's Resume, an autopilot or quick menu entry boosted the ship).
- **Autopilot** (station / jumpgate locked + action): "Target: X" + sound 28; `ShipController.autopilotTarget` steers by
  `moveToPosition` (world up; the model banks into the turn by its return value: the signed turn per frame averaged over
  5 samples × H·750/63·1.2 × 15.14, clamped, eased by dt·H/81 into the yaw rate, `FlightModel.AutopilotBank`, taken per
  original frame (`OriginalFrame.Ms`, 16.7 ms) of real time: the frame rate doesn't change it, but fast-forward does, as in the original (its update runs
  with dt x 5, so each sample carries five times the turn and the ship banks hard, to the limit 1.2 x a full stick's; a
  player's report with the original's video: the remake divided it away by the scaled dt); #45: it flew the turns flat;
  the rate is also the yaw rate +0x27c, which the model banks from, so handed back the ship eases out of the turn),
  throttle reset to 100 % once, stick ignored, throttle / boost / guns still work. Within 16000 units of the station it
  docks by itself. Action again (or the touch autopilot button) = "Autopilot Off" + sound 29. Reaching the gate opens
  the jumpgate flow (see "Star map and system travel"; with a destination set it jumps on unasked by default, the option
  "Autopilot takes jumpgates", star-map.md "Jumpgate").
- **Programmed destination** (`Session.ProgrammedStation`, from the star map;
  `LevelScript::setAutoPilotToProgrammedStation`): at the end of the launch / arrival camera the autopilot flies to its
  planet in this system (the planet lock then jumps by itself), or in another system to the gate (gate orbit) or the
  gate station's planet. Arriving at the destination's orbit clears it. Remake: a destination past the gate neighbours
  is flown to gate by gate, each later gate jumping on unasked (star-map.md "Jumpgate route planning").
- **Planet jump** (planet locked + action, no confirmation): sound 5, the camera freezes and looks at the ship, straight
  on at 8 u/ms for 3 s, HUD hidden, then `Space` reloads in that station's orbit (`Session.StationIndex`,
  `PreviousStationIndex`, `ArrivedByTravel`): arrival at 4x the previous station's planet billboard (~4 km), or the
  hidden gate in the gate orbit, facing the station, with the travel fly-in camera.
- **Planet run** (remake, `NavigationPlanetRun`, `World.PlanetApproach`, 2026-11-01, the user's): a cruise to another station's
  planet loads that orbit in ahead of the ship as a flight into a system does (`SystemApproach`). By hand: the cruise button
  held with the nose within 8 deg of a planet (a locked one only when it is that close too), any cruise drive (35 deg off the
  course or the drive's end stops it, and the orbit fades back out). In place of the planet jump: with a drive that flies between the systems mounted, the action on a locked
  planet or the autopilot reaching one starts the run instead of the scene, the autopilot flying it and holding the drive;
  where the drive can't run (hostiles) or stops on the way, the original jump goes (`JumpsBlocked` and `PlanetJumpRefused`
  asked again first). Both on the planet jump's terms, the user's rule: no charge and no energy cells (`CruiseDrive.Engage`,
  `RunFree`; by hand only within 15 deg of the course, so a planet ahead is no free cruise to elsewhere). A run keeps the
  programmed destination as the planet jump does (`SystemJump.LeaveInterstellar`'s `hopping`), and ends instead of leaving
  while `SystemJump` charges or runs a jump (`Navigation.TravelBusy`). Backdrop planets stay with the camera, so a run
  flies at a point `RunApproachUnits` (14 km; `ApproachUnits` 20 km until the user's 2026-11-01 "quicker") along the way to
  the planet, fixed at its start, and leaves at `ArriveUnits` (8 km): 6 km of cruise. `Session.WarpLocal`:
  the arrival is `OrbitLayout.PlanetArrivalPose`, facing the orbit's planet (unlike a system's), the plain warp with no web, and
  the orbit's planet starts at the size it had here. This orbit's planets and sun hand over to the next's in the first
  eighth of the blend (`SystemApproach.HandOverBy`, 0.125; they went only near the arrival, the user, 2026-11-01).
  Never into an orbit a script sets up on arrival (`Navigation.ScriptedArrival`: a story mission's level, a freelance
  mission's target, an event quest's; the user, 2026-11-01): its opening (a placed start, an ambush, a cutscene) is written
  for the planet jump, not a warp's held fire and carried speed, so the jump on its planet is the plain one and a cruise
  aimed at it no run; an arrival from between the systems loads such an orbit as after a jump (`SystemJump.LeaveInterstellar`).
  Single player only. No encounters roll during one.
- **Autopilot menu** (`Hud::initHudMenu(3)`): the touch autopilot button, Q or the controller's View button while
  nothing else flies the ship (1-8 pick an entry; with keys and mouse the entries read "1." to "8.", like the PC
  version's numbered menus; remake: Q / E / V / K do nothing during a level's cutscene, #46); the game pauses
  (`Time.timeScale` 0). Entries: "Destination: X" (programmed station), Asteroid field (the field centre; like the
  original the autopilot keeps flying there until switched off), "<name> Station", Jumpgate (gate orbit only). Pick =
  "Target: X" + sound 28 + autopilot.
- **Quick menu** (`Hud::initHudMenu(0)`, "Menu" 172): the touch quick menu button, E (the PC version's "Actions") or the
  controller's D-pad left; refused only while mining, so it opens on the autopilot too (`MGame::OnTouchEnd` key 4).
  Entries in its order: 266 **Secondary weapons** with any secondary mounted (its list `initHudMenu(1)`: "<name>
  (<amount>)", a pick selects it), 306 Wingmen, the cloak (the item's name), 1359 Khador Drive, and (remake) the time
  extender (the item's name; half-transparent while recharging), so one key reaches every device. V / K open Wingmen /
  Khador Drive directly.
- **Fast-forward** (hold the touch button, Tab, or controller Y) while the autopilot, an asteroid approach or an object
  docking's approach runs, the target is >= 20000 units (1 km) away and no hostile ship is around: `Time.timeScale` = 5
  for the whole game; stops on release, arrival or when the autopilot ends. Multiplayer (remake): the shared world can't
  run 5x for one player, so the held button is a **cruise mode** under the same rules (`Navigation.Cruise`,
  `CruiseFactor`): only the player's forward movement up to x5 (`ShipController`'s step, `Mining`'s approach; turns,
  timers and the camera at their own pace), eased in over 3 s (smoothstep) and out over 1.2 s (`CruiseFactor`), the HUD
  hint "CRUISE" (Extra `hudCruise`); it looks and sounds like a boost (`FlightModel.CruiseVisual`, eased over 400 ms, in
  `BoostVisualPercent`: the FOV, exhaust, engine glows and mod flames, the other players' copies; `LooksBoosting`: the
  camera / controller rumble, the boost sound's start and stop; without a booster item 71's boost sound,
  `PlayerEngine.cruiseClip`: a new pilot's Betty has none), while `IsBoosting` (missile evasion, the recharge, volatile
  goods) stays the real boost's.
  **To a stationary lock** (remake, 2026-11-02, the user's; `Navigation.LockFastForward`): in manual flight too, under the
  same rules (>= 20000 units from it, no hostiles, not the time extender), with a stationary object locked
  (`StillLockUnits`): the station, the jumpgate or a waypoint, or a docking target, the locked asteroid (`Mining.Locked`)
  or the locked ship (`CombatRadar` publishes `Navigation.ShipLocked`) once it has moved under 0.01 units / ms for 500 ms
  (`Core/StillLock`, so a proxy's jitter passes and a ship on its way doesn't). Not a planet (the backdrop's, never reached:
  its lock jumps), nor with a cruise drive fitted (the button stays the drive's), mining, docking or a planet run. The
  touch button shows (`NavigationView`) and the hint row gains "FAST FORWARD (HOLD)" / "CRUISE (HOLD)" (`FlightHud`'s
  `lastLockFastForward`). No heading check: away from it the distance rule alone holds.
- **Cruise drive** (remake, `Flight.CruiseDrive` rules + `NavigationCruiseDrive`; a mod item based on the Khador Drive
  with `cruiseSpeed` 133 > 0, `Shop.IsCruiseDrive`, its own one-per-ship kind `Shop.KindOf` = `CruiseDrive.Kind`, but one
  drive a ship (the user, 2026-11-03): mounting it over a Khador Drive or another cruise drive is the hangar's swap (287,
  `Shop.Replaces` / `IsDrive`), and so is a Khador Drive over it; a ship that carried both before keeps them; the ES2
  importer's `drives` part, Modding/README.md "Cruise drives"): the fast-forward
  button in manual flight (the original fast-forward keeps it on the autopilot and the approaches). A fresh press held for
  attr 37 ms charges (the Khador bar, Extra `hudCruiseDriveCharging`), then one leg's energy cells (attr 38, x2 Extreme, 0
  with free jumps; `GalaxyMap.RemoveCells`, "-1t Energy Cells", 583 + the cells when short) and the forward speed x attr
  133 / 100 joins `CruiseFactor` (its own ramp 800 / 1200 ms, `Cruising` and the boost look follow it); every attr 134
  units pay a leg. Drops (a HUD line with the item's name, Extra `cruiseDrive*`): release, hostiles (`HostilesPresent`), the
  autopilot / jump / time extender / mining / docking / a halt, a hull the player is pushed out of (`HullBody.obstacle`:
  stations, freighters, objects; `HullCollision.Sweep` along the nose, and their collision volumes by `Obstacle.RayDistance`
  where the hull takes no part under the ship-collision setting, 2026-10-31) within `StopUnits` 8000 (the speed eased down from
  `SlowUnits` 40000), a live asteroid's cube (the ship collision's, grown by `CruiseDrive.SweepRadius`; asteroids have no
  hull, so `CruiseDrive.CubeAhead`'s slab test along the nose finds them, 2026-10-31) the same way, the locked landmark within
  `ArriveUnits` 20000, a hit (`PlayerHealth.Hit`). Touch: the drive takes the fast-forward slot while usable (the clock keeps it while
  the extender runs); hint "CRUISE (HOLD)" (Extra `hudCruiseDrive`). Modelled on No Man's Sky's pulse travel and
  EVERSPACE 2's cruise mode (behaviour only).
  **Encounters** (`Flight.CruiseEncounters` rules, `Modding.ModEncounters` the table with the mods' encounters.json,
  `World.CruiseEncounterSpawner` the spawns, `SpaceLevel` gates them: `EncountersAllowed` = the orbit's own traffic
  (`!ownPassive`, not a multiplayer orbit another player runs, not the prologue) and `CruiseEncounterRules.Allowed` (not
  the alien orbit, stations 10 / 100-104 / 108, systems 25 / 27 / 32 / 33, a mods-only system)): after `MinCruiseMs` 6000
  of a cruise a roll every `CheckIntervalMs` 5000 at `ChancePercent` 10, `CooldownMs` 90000 between two, `MaxPerOrbit` 2;
  the pick by weight among the entries whose rank / security / free-play conditions hold (`CanHappen`). An ambush spawns
  `AmbushAheadUnits` 22000 ahead and drops the drive (`Stop.Encounter`, Extra `cruiseDriveEncounter`; a flyby doesn't); a
  visible entry is a signal: a `Kind.Waypoint` target (the white freelance icon, lockable, the autopilot flies to it)
  `SignalAheadUnits` 45000 ahead and up to `SignalSideUnits` 25000 aside, "Signal detected: X" (Extra
  `cruiseSignalFound`), spawned when the ship comes within `SignalReachUnits` 12000, gone after `SignalLifeMs` 180 s or
  `SignalLostUnits` 200000 away ("Signal lost"); a "quest" entry starts its quest graph (`GameHooks.StartQuest` ->
  `EventRunner.StartQuest`, single player). Built-ins: pirates 2 + rank / 6 raiders (2..5, hostile), trader a freighter of
  the system's race (`NpcTables.RandomFreighter`) + 2 locals, derelict 2-4 junk crates with a freighter's loot roll (empty:
  energy cells), distress a local under attack by 1 + rank / 8 raiders, flyby 3 locals of a random race; names Extra
  `encounter*`. Modelled on No Man's Sky's pulse encounters and EVERSPACE 2's signals (behaviour only).
  Remake 2026-11-03: six more kinds for mods' entries (not in the game's table, whose weights still add to 100), after
  EVERSPACE 2's encounters (decompiled BP_Encounter_FactionBattle (a battle task, GivesCreditsReward / GiveRenown),
  _Trap (SpawnFactorNPCs 1.25, ChanceForTwoGroups 0.2, the second group 650 m ahead in a 20 deg cone), _HiddenStash (an
  invisible container of spawn group 75 found by a detect-signal task), _ResourceDepot (SpawnFactorNPCs 1.5, Resources 3,
  Containers 2), _HighRiskArea (waves, respawns, boss loot, bFailIfPlayerLeaves), BP_EncounterBase's
  CalculateRewardCredits): `battle`, `trap` (named as an unknown signal), `stash`, `depot`, `highrisk`, `lootdrone` (a
  mod's loot drone, `ModTraffic.PickLootDrone`, else a pirate fighter with `SpawnSpec.role` "lootDrone"; neutral as a pirate
  (`alwaysNeutral`), after ES2's Freelancer drone that never turns hostile; see the drone roles in npcs-and-combat.md)
  (`CruiseEncounterSpawner`; `EncounterWatch` pays `BattlePay` when the battle's raiders are gone: ES2's own UMissionLib::CalculateRewardCredits, (800 + 4200 x level / MaxPlayerLevel 30) x RewardCreditsFactor 1, down to 50, the level the orbit's `level` (stations.json: an ES2 location's own, the importer's) or else the station's tech level x 3 (`Es2Level`); the high-risk area the same, runs the
  high-risk area's three waves and its crate of 8-14 loot rolls, ES2's HighRiskAreaBoss drops in its loot_types.CSV,
  `EncounterWatch.BossRolls*`, and stops unpaid past 15 km). Counts are the remake's picks (ES2 places its groups by
  spawn_groups.CSV at the location's level). The ES2 importer's mod writes them (encounters.json). Not done: ES2's
  Minesweeper (mines), Scavenger Baiting, Incursion and Rift (Leviathan encounters are
  left out on purpose).
  The `stash` and `depot` crates stay until emptied (`Crate.keep`), not the crates' 60 s.
  **Between the systems** (2026-11-03, the user: "encounters should pull you out either in their own space or the nearest
  system"; `CruiseEncounters.Between`): the same table rolls on an interstellar leg (single player, as that space is), the
  security the system left's, but not on the way out, on a dive into the Void, or while a system's orbit is built ahead
  (`Navigation.ApproachRuns`, set by `InterstellarField`). Every entry pulls the ship out, the drive dropped and the
  autopilot off ("X disengaged: Pirates"): **deep space** (`InSpace`): `OutOfSlipstream`, the warp eases out over
  `PullOutMs` 2500 (`WarpSky.Strength`; `SpaceLevel.WarpHolds` holds fire and damage until it ends, and the drive is blocked),
  then the encounter is there `AmbushAheadUnits` ahead; the warp comes back with the drive's ramp and the flag clears at
  full cruise. **Nearest system** (`AtSystem`): "Pulled out of the slipstream at X" (Extra `interstellarPulledOut`), the
  fall-out's way in (`Leave`), the entry carried in `Session.CarriedEncounter` and met ahead once the arrival's warp has
  ended (`ArrivalHeld`), counted as that orbit's (`Came`: its cooldown); dropped if that orbit rolls none. Only to a system
  `CruiseEncounterRules` allows, with no scripted arrival (`ScriptedArrival`) or story refusal (`PlanetJumpRefused`).
  An entry's `interstellar`: pirates, derelict and flyby `space`, trader and distress either (50 / 50, deep space when the
  system won't have it), a quest always the system (its graph needs an orbit), `never` keeps it out.
- **Wreck fields** (remake 2026-11-03, for mods; stations.json `orbit.wrecks` -> `ModWorld.OrbitSpec.wreckCount /
  wreckCentre / wreckSpread`, `World.WreckField.Spawn` with the gas clouds in `SpaceLevelWormholes`): after EVERSPACE 2's wreck
  field locations (Wreck Field, Ghost Fleet, Warship Remains: war wrecks among loot containers), each visit lays out
  `count` (1-60, default 12) wreck containers (the junk crate look) at random in the field's cube (`spread`, default
  60000; a third as tall), each a freighter's loot roll or 1-5 Space Waste (item 99), kept until emptied (`Crate.keep`). Not
  in the alien orbit or interstellar space. Wreck hulls (2026-11-05, `models` / `materials` / `hulks` / `size` ->
  `wreckModels` ...; `WreckField.SpawnHulks`): `hulks` (default 2 x the models, at most 8) of the mod's glTF models among
  the containers, the largest model `size` game units at its largest side (default 8000) and the others in proportion
  as authored (x0.85-1.15; `ModCrates.ExtentOf(key)`), turned at random, placed by a
  `System.Random` seeded by the station (the same for every player), each an `Obstacle` with one box of its bounds
  (`ModCrates.LocalBounds`, `rotates`: turned with it); loaded with the mod crates (`ModWorld.ContainerModels`), none
  placed before they are (`ModCrates.Ready`). The ES2 importer gives its wreck-field locations one, with ES2's wreck
  meshes; its outlaw strongholds are pirate-held orbits (`pirateHeld`: outposts and guards).
- **Rifts** (remake 2026-11-04, for mods; encounter kind `rift`, `Flight.RiftRun` plain + `World.RiftEncounter`; the HUD's
  transfer counter line shows `RiftEncounter.HudLine`): after EVERSPACE 2's Ancient Rifts, read in its code (decompiled
  BP_Encounter_Rift and UGameplayLib::GetRandomRiftMutatorForNextStage, mutators.csv's LootQualityBonus / bAllowedInHighRiskAreas /
  bAllowedInRifts / bAllowedOnlyForRiftBosses, attributes.CSV's mutator names and texts, the values on its Signal Decoder
  items): stages (ships, default 4; the last the boss's) in the orbit where the signal was (ES2 jumps through portals into
  new locations: here the stage's survivors vanish); a fight stage's target GetTargetKillPointCount 15, 21, 26, 30, 35, 35,
  40, 40, 45, 45 + 0..5, MinEnemiesAlive trunc(x 0.4) raiders kept alive (a fifth strong fighters), kill points by
  EPawnType (drone 0.5, fighter 1, cruiser 1.5, capital 2), resolve +0.10 / 0.12 / 0.15 / 0.20 a kill, drained 0.016/s
  after RESOLVE_DELAY 10 s (not on the boss's stage); run dry, the stage goes on unmastered; one mutator a stage from the
  second on, uniformly among the rift's not yet in play (the boss's stage among the boss's; ES2's three portals offer one
  each and the pilot picks, here the rift does). Mutators done: Enemy Regen (GetScaledHitpoints(5)/s against a raider's 200:
  2.5 % of the hull a second), Evasive (+50 % speed: `NpcShip.roleSpeedFactor`), No EMP (`SpawnSpec.empPoints`), Jarred
  Sensors (the plate's race), Hidden Health (`plateNameOnly`), Explosive Death (50 kinetic + 70 energy against a raider's 31 +
  24 DPS: 2.2 s of the dying ship's gun, `NpcShip.GunDps`, within its size clamp 20-50 m x 15 x 3.6, falling off), Repair On
  Kill (20 %), Strength in Death (+5 % speed and hull, at most 10), Nearsighted / Farsighted (`Target.damageFactor` 0.5
  beyond 1080 m / within 1800 m), No Lock (`untargetable`), Vengeance (a `detonator` role drone a kill, invulnerable with
  Explosive Death); the boss's Quantum Mend (teleport every 15-30 s, repairing until hit), Evasive Drift (`NpcShip.BoostAway`
  as often), Guardians (two strong fighters, the boss invulnerable while one lives). The boss: one of the entry's `bosses` (ES2's GetBossClassAndLevel picks its destroyer, drone carriers, Okkar corvette or
  Redeemer battleship), a capital ship parked (`ModCapitalShips.Spawn`), its hull `raiders` x `NpcTables.Hull(0, 0)`; else the
  pirate boss's hull formula and guns. Won: a crate of loot_types.CSV's RiftBoss 14-18 rolls x `RiftRun.LootFactor` (0.5 + 0.5 x the
  mastered share, after GetLootQuantityFactor) and `BattlePay`. Not done: Mines on Death (2-3 mines, 40 + 40, 100 m), Overheated
  Systems, Corrosive Death, Burst Bulbs, Phase Barrier, Stasis Snares, Apocalypse; the portals' choice; ES2's lunacy,
  nightmare mode and loot quality.
- **Containers** (remake 2026-11-03, for mods; stations.json `orbit.containers` -> `ModWorld.OrbitSpec.containerCount /
  containerCentre / containerSpread / containerBulletproof`, `World.LootContainer.SpawnOrbit` with the wreck field): after
  EVERSPACE 2's BP_ItemContainer (EContainerType OpenOnHit: ReceiveAnyDamage by the player or within 2 km ->
  OpenContainerAndDropItsLoot; OpenOnInteract, "Bulletproof Container", CanBeHit off; their InteractComponent:
  InteractRadius 10000 cm, InteractChargeDuration 0, bMustBeOnScreen, from the native constructor), the game's container
  model x3 as a radar object (`Target.RadarObjects`, neutral, lock plate "Container" / "Bulletproof container"): a shootable
  one has 1 hp and opens when it dies (the junk's burst), a bulletproof one is `invulnerable`; the action key (`FlightHud`'s
  prompt chain after mining: "OPEN", `LootContainer.InReach`, 360 m ahead of the ship) opens either. Opened, each stack
  is a crate of its own (`Crate.keep`, launched x2); the opened bulletproof shell stays without its Target. Loot: a
  freighter loot rolls (`WreckField.Loot`) by ES2's loot_types.CSV: a Container's 1-3 drops, a RareContainer's (the
  bulletproof one here) 2-4 (`LootContainer.RollsMin` ...). Not done:
  ES2's secure containers (OpenOnUnlocked, energized through conductors; the importer counts its armoured ones as
  bulletproof), its loot menu (everything spills), the open animation.
  Fixed loot (2026-11-05, EVERSPACE 2's unique_loot.csv): `containers.loot` (`OrbitSpec.containerLoot`, item references
  and amounts) on top of the rolls, an entry a container in turn (the bulletproof ones first), skipped once its key
  (`LootContainer.FixedLootKey`: station:entry) is in `Session.FixedLootTaken`, saved as `fixedLootTaken`, added when its
  container opens. Test `ModWorldTests.AnOrbitsFixedContainerLoot`. Models (2026-11-05, ES2's SK_Container_Generic_*):
  `containers.model` / `bulletproofModel` (`OrbitSpec.containerModel` / `containerProofModel`, checked in the mod) load with
  the mod crates (`ModWorld.ContainerModels` -> `ModCrates.LoadAll`; "<mod id>|<path>" keys), `LootContainer.Spawn`'s
  modelKey through `Crate.Model`, fitted to the race's container x `ModelScale`; `containers.materials` (ships.json's form)
  applied to the template as on a station model (`ModCrates.ApplyMaterials`). Test `AnOrbitsContainerModels`.
- **Traffic lanes** (remake 2026-11-03, for mods; stations.json `orbit.lanes` -> `ModWorld.LaneSpec`, `Flight.TrafficLane`
  plain (a Catmull-Rom path walked by distance, 16 chords a segment; open paths turn over at their ends),
  `World.TrafficLanes` attached with the containers): after EVERSPACE 2's BP_SplineTraffic (Count 10, BaseSpeed 8000 cm/s,
  TwoLanes with LaneOffset 2000 cm (x3.6 here, for the ships' size), ClosedLoop / DoUTurns, UnitHealth 50, RespawnDelay
  30 s, a destroyed unit TryTriggerWantedSystem(1); its units are SM_Traffic_* static meshes, physics-knocked when rammed):
  the ships are the lane's `ships` or the system race's fighters (`NpcTables.RandomFighter`), models only plus a Target of
  `TrafficLanes.HitPoints` (ES2's UnitHealth 50 over its raider's 310 of a pirate raider's hull, `NpcTables.Hull(0, 0)`) with no marker; destroyed: `Explosion.Spawn` x0.6,
  `Standing.ApplyDelict(system race, 1)` unless an NPC did it, back after 30 s. Not done: ramming them aside, their engine
  sound, ES2's random glow colours and its traffic meshes.
- **Hazard volumes** (remake 2026-11-03, for mods; stations.json `orbit.hazards` -> `ModWorld.HazardSpec`, `Flight.Hazards`
  plain, `World.HazardZones` on the player's ship, attached with the wreck field): after EVERSPACE 2's BP_HeatDamageVolume
  (DamagePerSecondMax 200 x (1 - d / SphereRadius)^DamageFalloffExponent 2 each DamageInterval 0.25 s; its overheat buildup
  into Scorching, equipment efficiency, not taken) and BP_RadiationField (BuildupDuration 5 s into
  BP_Condition_Debuff_Radiated: MaxHitpoints_Hull x 0.85 a token, TokenLimit 10, kept beyond the location, docking removes
  it): `heat` (dps x falloff, through the shield, `Target.Damage` as an NPC hit; `raiderDps` instead: that many x
  `NpcTables.RaiderGunDps`, ES2's DamagePerSecondMax 200 being GetScaledDamage by the location level like its NPCs' DPS, so
  200 / the Outlaw raider's 55), `radiation` (`Hazards.RadiationTokens`,
  the hull capped at maxHull x 0.85^n in every orbit, `StationLevel` clears it on docking). HUD lines on entering and per
  token. 2026-11-06, ES2's BP_FrostField / BP_CorrosionField / BP_DamageField:
  `frost` (BP_Condition_Buildup_Frost: `Hazards.FrostStep`, 5 s inside -> frozen 6 s as the web's slow, `Target.SlowBy` at
  `FrozenFactor` 0.15; drains over DrainDuration 60 s), `corrosion` (the rift field's rule: `RiftRun.CorrosionDebuffS` 8 s
  after leaving, a tick every `CorrosionTickS` 0.95 s past the shield of `raiderDps` x `RaiderGunDps`, default
  `CorrosionTickSeconds` = intensity 5 / 55; `PlayerDebuffs` lifts it), `damage` (DamagePerSecond 20 a second, no falloff;
  the importer gives ES2's capsule as a sphere of its radius). Not done: ES2's lightning fields (a storm effect), acid
  sacks, lightning plants and meltable ice (props with ES2's models), the energy restore fields (in no ES2 level).
  Looks (2026-11-06, `World.HazardLooks`, after ES2's fields: particles and an inside post-process, no surface; the
  materials' compiled shaders read in es2_re/fxmat, ShaderDump + disasm): Unity particle systems in the volume's space, its
  sphere the emitter, prewarmed. Clouds are GoF2/HazardCloud (additive; the Center vertex stream seeds each one;
  procedural noise for ES2's textures): mode 0 M_CorrosionField_Cloud (2 octaves of |gradient noise| at uv x 3 drifting
  with time x 0.2, ^1.5 squared, in the disc 1 - 2 r), mode 1 M_RadiationField_Cloud (dots where one octave of simplex
  noise at uv x 20 peaks, x 10 saturated, in a noise-wobbled disc), mode 2 M_WebField_Plasma (a flowed noise squared,
  ^ 1 / disc^3: wisps). Colours ES2's: corrosion P_CorrosionField (0.84, 1, 0.09; 5 clouds/s for 2-4 s, 1-2 x the radius;
  bubbles of particles.png cell 3), radiation NS_RadiationField (RadiationColor (1, 1, 0.39); sparks, cell 61), frost
  NS_FrostField (0.19, 0.77, 1; wisps, glints), heat a faint turbulence haze (remake: ES2's heat volumes have none),
  damage GoF2's fire sprites and embers (ES2's: the burning mining prop). The corrosion clouds' refraction is left out.
  Inside (GoF2/HazardVolume, added, the volume's sphere drawn Cull Front / ZTest Always while the camera is within it):
  frost M_Condition_Frozen_PostProcess (vignette (|uv - 0.5|)^1.5 through the contrast curve 3, x the frost revealed by
  saturate((f - (1 - Opacity)) / Opacity), x f^2; procedural ice cells for T_Grunge_Frost_001's R; Opacity = the buildup,
  whole while frozen), radiation M_Condition_Radiated_PostProcess (the edges' glow, two scrolling noises ^ 1 / distance x
  RadiationColor x 5 x Opacity^1.5, a grain x 0.3 x distance^3 and sparse noise blobs x Opacity^2; Opacity 0.3 -> 1
  toward the next dose); heat BP_HeatDamageVolume's BloomIntensity 2: a global Volume at priority 60 with the scene's bloom
  x 2, its weight the camera's depth (1 within 85 % of the radius).
- **Lightning storms** (remake 2026-11-06, for mods; stations.json `orbit.storm` -> `ModWorld.StormSpec`, `World.OrbitStorm`
  attached with the ambience): after EVERSPACE 2's BP_LightningField (AttachToCamera, Frequency 1, Radius 400 000 cm,
  HeightPercent 1, VolumeSurfaceOnly, ThunderSoundSpawnChance 0.2) and P_LightningField: flashes at random
  (`NextFlashMs`: exponential, `frequency` a second) on a cylinder round the camera (`FlashOffset`, `radius` game units,
  kept within 0.8 x the far plane), each a glow (particles.png cell 3, 1-10 km for 0.1-0.2 s) and a bolt (khador_jump's
  big bolt, EmpSparks' tile 11; 2-6 km for 0.25-1 s) x `scale`, blue-violet (0.1, 0.34, 1) to orange (1, 0.29, 0.05) over
  the life x `intensity` (ES2's ColorOverLife); a mod `thunder` sound 0.4-2 s after a flash at `thunderChance`. Effects
  only: no damage or EMP.
- **Ambience zones** (remake 2026-11-05, for mods; stations.json `orbit.ambience` -> `ModWorld.AmbienceSpec`,
  `World.OrbitAmbience`, attached with the hazards): after EVERSPACE 2's BP_MusicTrigger OverrideAmbience (its inner /
  outer spheres round stations, factories, cargo wrecks, wreck fields: a SoundCue looping one of its waves by
  SoundNodeRandom, a SoundNodeDelay of DelayMin..Max over a SoundNodeRandom of adds mixed in at its InputVolume). Each zone:
  two 2D AudioSources (its loop from a random start, its adds as PlayOneShot), volume x `OrbitAmbience.Weight` (1 within
  `radius` of `centre`, fading to 0 over half the radius more; no centre: everywhere) x `Settings.SfxVolume`; the clips are
  the mods' (a loop `ModMusic.Track`, kept compressed, else `ModSounds.Named`, decoded; the adds sounds), looked up each
  frame until loaded. ES2's planet-side ambiences don't apply (GoF2
  never lands). Test `ModWorldTests.AnOrbitsAmbienceZones`.
- **Menu order** (`Hud::initHudMenu(3)`): outside the alien orbit Asteroid field, Station, Jumpgate, 573 **Waypoint** (a
  player route not finished: the autopilot follows the route, "Target: Waypoint"), "Destination: X"; then every named
  docking target (also in the alien orbit). The original's only restriction is no menu in mission type 0xb7 (the
  Supernova Challenge, see "Supernova add-on").
  Remake: where the menu would be empty (the alien orbit: no docking target there, `Navigation.MenuEmpty`) the autopilot
  button is dimmed (`nav-button--dim`) and Q / the button don't open it; it still turns a running autopilot off.
