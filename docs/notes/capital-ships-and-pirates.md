# Capital ships and pirates

Notes moved from `CLAUDE.md` (its index lists every notes file). Code comments citing `CLAUDE.md "<section>"` mean the section or bullet of that name here.

## NPCs and combat

- **Capital ships and pirate bases** (`Reference/research/npc_combat_specials.md`): in Terran space 30 % of orbits with
  freighters get the battleship (7 turrets) or, past campaign 0x67, the carrier (8); Vossk space past 0x8c the Vossk
  battleship (5); turrets (`NpcShip` turret mode, `TurretAim`) are 1000-HP static objects that die with their host.
  Remake (players' reports): a shot inside a big hull's hit boxes and a smaller target's cube hits the smaller one
  (`Gun.SmallerInside`): `Gun::calcCharacterCollision` takes the first target of the list and the host comes before its
  turrets, so the Terran battleship's turrets, all 7 inside its 11 boxes, took no hits (the hull did), and the enhanced
  carrier's / Vossk battleship's new boxes shielded theirs; a shot elsewhere on the hull still hits the hull. The big
  battle (8 %, campaign > 0x1f): 9 raiders against 9 locals. Pirate bases at stations 1 / 33 / 47 / 86 (`PirateBases`):
  the system has pirate escorts instead of raiders, the base orbit a sleeping outpost with 5 (10 Extreme) guards in red
  fog; waking a guard = radio 435-437, the outpost's death = radio 438-440, its crate, and 20000 (442) at the next
  docking; its station is unmanned (434, relaunch; 434 and 442 voiced by the Nivelian set,
  MSG_PIRATE_STATION_NO_ENTRANCE / _REWARD). Nivelian hints 443 / 444.
- **Capital ship enhancements** (remake, players' suggestion; option "Capital ship enhancements",
  `Settings.CapitalShips`, off by default; Options > Gameplay and the new game's Game options panel
  (`MainMenu.RefreshCapitalToggle`); rules in `World.CapitalShips` (plain C#), the orbit side in `World.CapitalShip` on
  the host, added by `Traffic.Create` for a spec built with the option on, `SpawnSpec.capitalEnhanced`): the capital
  ships of `TrafficPlan.AddCapitalShip` (`SpawnSpec.capital`; their turrets and escorts `capitalPart` / `capitalHost`)
  get escorts (3 + rank / 7, +1 on Hard / Extreme, +2 for the carrier, of the dearer half of the race's fighters,
  `NpcTables.StrongFighter`, hull x1.5, gun x1.3, looping round the host, `SpawnSpec.routeAround`) and turrets of (500 +
  50 rank) x difficulty hull with the gun x(1 + 0.1 rank), the Vossk battleship's 5 x1.6 both (`NpcShip.SetupTurret`
  applies `gunFactor`). Balanced after players' feedback (2026-10-11: a maxed ship killed the carrier barely losing its
  shield; the turrets had 4000 hull at rank 20, a third of the battleship's, and nothing in a battle aimed at them; the
  Vossk battleship lost every battle; the resupply was unlimited). The carrier and the Vossk battleship (the original's
  indestructible decor) become killable: the battleship's hull (`NpcTables.Hull(1, 14)`, 12 000 at rank 20 on Normal) x8
  (96 000), the enhanced Terran battleship x5 (60 000), EMP x3 a freighter's, their collision boxes 2005 / 2006 as hit
  boxes (`Target.boxes`; before, only the +-1000 cube at the centre was hit), 8 s of explosions along the hull
  (`SpawnSpec.deathMs`) then an x14 blast, nothing left to collide with, their own turrets with them
  (`Traffic.DestroyCapitalTurrets`); a crate (`CapitalShips.RollLoot`, every enhanced capital ship, the Terran
  battleship too): two tech level 5+ items and 8-15 t of rare goods at most once an hour of play
  (`Session.CapitalLootReadyAt`, saved), else 4-8 t of rare goods, then energy cells and a hold of 2-3 freighter cargo
  rolls (a player's report 2026-10-16: within the hour the crate was empty, and the battleship had a freighter's roll);
  the player's kill is a delict of 20 (on top of the kill's 5), the Vossk one counts for medal 39. The player's first
  hit on a friendly or neutral one radios "Hold your fire!", 3 hits or 0.5 % of the hull turn the race
  (`Traffic.AlarmAllFriends`; a hostile one at once). The carrier and the Vossk battleship, attacked in the last 20 s
  (by NPCs, or by a player who provoked it), launch 5 fighters every 20 s from their pads' approach points, 15 in all:
  the carrier Inflicts (ship 5) from its deck (docking set 5), the Vossk battleship H'Socs (ship 9) from its four wing
  pads (set 8, the original's own points on that hull) (`CapitalShips.HasDeck` / `LaunchShip` / `PadSet`), hostile to
  the player once provoked (multiplayer: to the players who shot it, `NpcShip.aggressors`). Resupply: the carrier
  ("Carrier", `CapitalShips.CarrierName`; not text 1512, a medal that reads "Spediteur" / "Курьер" in the other
  languages) and the Vossk battleship ("Battleship", 1667) are docking targets (`ObjectDocking.Resupply`, their pads;
  hidden while hostile, in a battle or still coasting after one, and then ships to mark, lock and scan:
  `NpcShip.OffersDocking` / `CapitalShips.DeckOpen`, used by the radar, the HUD markers and Navigation; the docking type
  kept them off the radar for good until 2026-10-16) for a pilot the race trusts (standing 71+) in a ship of that race,
  or with its signature (`CapitalShips.DockRefusal`); docked, a shop window like the hangar's Shop tab
  (`UI.CarrierShopWindow`, built in code with `HangarWindow.uss`, linked from `FlightHud.uxml`;
  `FlightHud.OpenCarrierShop`, `Navigation.OpenMenu(force)`: the game paused; the root's `hud-shop` hides the HUD's
  markings and lays the key hints out in one row) lists Repairs (hull and armor, 10 $ a point, unlimited), 266 Secondary
  weapons (each mounted one of its race, up to 50 mounted), its race's gear under the hangar's type headers 265 / 266 /
  267 / 269 (players' suggestion: the Vossk ship sells Vossk items, the carrier Terran ones; `CapitalShips.Store.Gear`:
  the Vossk battleship 5 of the 17 Vossk-made items, attr 60, which the original sells only in Vossk systems; the
  carrier 6 of the Terran items of tech level 4+ (`CapitalShips.RaceItem`: not Vossk-made and their home system, attr 4
  `lowestPriceSystem`, the cheapest, a Terran one; the original names no other maker; until 2026-10-21 every non-Vossk
  item, Nivelian and Midorian ones too), not the Khador Drive or the signatures; the mounted secondaries' rounds by the
  same rule; 1-2 of each, 10-20 rounds of a secondary, into the hold; the other race's mounted secondaries NOT STOCKED,
  `resupplyWrongRace`) and energy cells (the hold's room) with their shop icons and unit prices (the orbit station's
  x1.25, `CapitalShips.ResupplyOffers` / `Buy`), from the ship's store for the visit (`CapitalShips.Store` on
  `CapitalShip.store`: 10-15 energy cells, 5-10 rounds per secondary, none of a blueprint product, rows SOLD OUT / NOT
  STOCKED; players' feedback: unlimited Extinctors made the blueprint pointless); the details show the trade box (what
  the ship has left, a held buy arrow: faster and faster, all there is room for in two seconds, `HeldTrade`, the unit
  price, what the ship has), the stats and description, or the Repair button; up / down, right / Enter / A buy, Esc / B
  undock (leaving it undocks). Sounds: the approach's autopilot sound as with any object, 0x65 Button_to_ship on a
  purchase, the station hangar's ambience loop (Station_Atmo_Hangar3) under the window (`CombatAudio.shopBuy` /
  `hangarAtmo`, filled by `WeaponBuilder`); the button release on Undock; the deck takes the ship without a sound for
  now (the objects' Docking_Landing sounded like mining there, the hangar's mount clunk didn't fit either). Multiplayer
  proxies now take the owner's hit boxes (`NetProxy.hitVolume`: the capital ships, freighters and the battleship;
  before, the other players hit only their centre cube). Fixed on the way: a fixed object dropped its crate twice (at
  its death and the end of its dying), so a pirate outpost's loot came twice (`NpcShip.DropCrate`, one per death).
  Verified in Play mode (spawned through `AddCapitalShip` at Emisto): the boxes against the model, the warning and
  provoking, both launch cases, the carrier's and the Vossk battleship's deaths (turrets, one crate, standing,
  cooldown), the battleship's escorts, docking refused and allowed, the menu's purchases, the undock. Not tested in
  multiplayer.
- **Fleet battles** (remake, with the capital ship enhancements; `TrafficPlan.FleetBattleHere` / `AddFleetBattle`,
  `CapitalShip.UpdateBattle`): 3 % of the free-flight orbits in Terran and Vossk systems past step 103, not in a Secure
  one (`TrafficPlan.SecureSystem`, a player's report 2026-10-16) (the finished game's world too; not the special orbits,
  a pirate base system, the Kaamo siege or a storyline's orbit while a story runs) hold a battle instead of the single
  capital ship: a Terran carrier or battleship and the Vossk battleship side by side (both facing game +Z) 80 000 units
  apart, 110 000-150 000 in front of the station, each with its turrets and escorts and a wing of 4 + rank / 5 fighters
  looping round the other (`SpawnSpec.fleetBattle` / `battleFoe`). The two sail a shared course
  (`CapitalShip.BattleCourse`, one object for both: a point at 0.35 u/ms whose heading curves gently left and right, an
  S every 2 min at up to 1 deg/s, turned back past 60 000 units from where the battle began and away from the station
  within 100 000) in lanes 21 000 units either side of it (+-6000 weaving, +-3000 up and down), each steering toward a
  point 20 000 units ahead in its lane: an eased turn rate of at most 2 deg/s, banked into the turn up to 15 deg,
  pitching at most ~5 deg, a little faster or slower to stay abreast (players' feedback: closing in straight looked
  artificial); their turrets parented to them, their collision volumes turning with them (`Obstacle.rotates` /
  `baseRotation`: the original's volumes are never rotated), the escorts' and wings' loops moved along every 2 s
  (`SpawnSpec.routeAround`, `NpcShip.ShiftRoute`); once the enemy is gone the winner levels out and coasts to a stop
  over 12 s (`CapitalShip.Underway`: no docking until it has) (`TrafficPlan.AddCapitalShipAt` builds any capital ship at
  a point). The enhanced turrets measure and aim at the nearest point of a big ship's hull (`Target.NearestPoint` over
  its boxes, `NpcShip.TurretAimPoint`; the original aims at the centre, out of reach of a 3 km hull) and their shots fly
  3600 ms; every other turret is a turret hunter (`SpawnSpec.turretHunter`: an enemy turret within its reach counts as
  0.3 x its distance); the carrier and the Vossk battleship launch their fighters as defenders. The battle's fighters
  (escorts, wings, launched) take the nearest race-hostile ship within 60 000 units of the course
  (`CapitalShips.BattleLeashUnits`, `CapitalShip.BattleArea`), else fly their loops: they took the first of the enemy
  list, and in a Terran system the station's patrols came first, so the Vossk side's fighters flew off to the station
  and never hit the enemy capital ship (the Terran side won every battle there). The system race calls the battle 6 s in
  and the winners call its end (`Traffic.RaceRadio`, a generic face; only while they still stand,
  `CapitalShip.WinnerStands`: with both destroyed both called victory); battle music while both stand
  (`Traffic.FleetBattleRaging`); no docking at the carrier meanwhile (`CapitalShips.InBattle`). The player picks a side
  through the standing, or by shooting one (provoked); helping destroy one (the kill or 5 % of its hull) while the
  winners aren't hostile pays 30 000 + 3000 x rank (`Traffic.PayBounty`). An enhanced Terran battleship's death takes
  only its own turrets (the original's `DestroyTurrets` took every turret of the level). Debug page (Spawn): "Capital
  ship" + "Spawn capital ship" (any of the three, enhanced) and "Spawn fleet battle" (`DebugSpawner.SpawnCapital` /
  `SpawnFleetBattle`). Verified in Play mode: the battle built and closing in, the turrets switching to the enemy hull
  in reach and wearing it down, the carrier's defenders, the refused dock, the bounty; after the rebalance (rank 20,
  neutral standing): the course, lanes and banking, the fighters at the battle, damage both ways (the battleship's side
  ~120 / s, the Vossk's ~150 / s: about 8 minutes to the battleship's death). Not played through as a player; not tested
  in multiplayer.
- **Capital ship missiles** (remake, with the capital ship enhancements; `CapitalShip.UpdateMissiles`): every 12-15 s
  each capital ship launches a salvo of 6 homing missiles, 250 ms apart (the first 3-15 s in, then every 12-17 s, so two
  ships fire out of step), from beside its turret mounts, climbing out a little (up x0.35 + the target's direction),
  with the player's smoke trails (`GunRig.EnableTrails`; the original's NPC rockets have none), each race its own: the
  Terrans' Intelli Jet (37, fast, 18 u/ms) and the Vossk-made S'koonn (38, attr 60, 10 u/ms), at the item's speed with
  its damage against 100, 10 s of flight. Targets: in a fleet battle the enemy capital ship, a missile in three a
  hostile fighter within 40 000 units of the hull instead (`Gun.SetTarget`: its own target), else the nearest hostile
  ship within 40 000 units (2 km) of its hull (the player only while hostile to it). On a big ship each missile homes on
  its own spot (`CapitalShips.SpreadAim`, `Gun.SetAim`: 2 times in 5 one of its live turrets, else a random point in its
  hit boxes, the bigger likelier), so a salvo spreads over the hull and its turrets; the player's capital hull salvos
  (`PlayerCapitalMissiles`) spread the same. Damage: the NPC gun's (rank, difficulty) x10 on a big ship's hull, x5 on a
  fighter or turret, x0.2 a stray hit on a player it isn't after. A boost shakes them off like any homing missile (see
  "Missile warning and evasion"), and the rest of the salvo at that ship is called off. They are `NpcShip.ExtraGuns`, so
  multiplayer's shot mirrors show them to the other players (whose own ships they don't hit). Verified in Play mode: the
  salvos in a battle hitting both hulls, the trails, a hostile Vossk battleship firing at the player, a boost with three
  in flight (no hits).
- **Pirate events** (remake, GitHub #6; Options > Gameplay "Pirate outposts and bosses", `Settings.PirateEvents`, on by
  default; `TrafficPlan.AddPirateEvent`, players' suggestions): per free-flight orbit entry 15 % in Dangerous systems
  and 7 % in Risky ones (the system's own security level, texts 402-405; never Average or Secure), not below rank 2,
  before campaign 0x10, in Mido in free play, at any station a step of the three campaigns sends the player to
  (story.json's stations, the Void-attacked station, step 59's targets: `InStoryline`), in a pirate base system, the
  Kaamo siege, Loma, the empty and special orbits, systems 27 / 32 / 33: half the time a sleeping pirate outpost 110-160
  km out with its guards (as a pirate base: radio 435-437 / 438-440, red fog, its crate), else a Pirate Boss (1606; hull
  (15 rank + 1680) x difficulty, speed 3.5, a gun by rank x2, 3-8 t of rare goods) with 2-4 escorts in front of the
  station, 30 km further out than the raiders, who calls 8 s in (speaker 9). Dangerous: the outpost has 6 pirate turrets
  (`turret_002_static`, on the hub's deck and under it, measured on the mesh; `SpawnSpec.EventTurret`, they go with it),
  3 more guards and one piece of equipment in its crate by rank (`DangerousLootLow` / `High`), the boss 2 more escorts
  and 6-12 t Implants / Vossk Organs, and the bounty x1.5. Destroying either pays a bounty at once
  (`Traffic.PirateEventDone`: outpost 10 000, boss 4000, + 1500 per rank). `AddPirateEventShips` builds the ships alone
  (tests: spawned with `Traffic.SpawnShip`).
- **Threat tiers** (remake, players' suggestion "Better Pirates", 2026-10): every pirate event has a tier 1 Minor / 2
  Low / 3 Moderate / 4 High / 5 Extremely dangerous (`PirateReports.RollTier`: Dangerous systems lean high, Risky ones
  low; below rank 4 no tier 5 and tier 4 a third as often; from rank 12 tier 1 half as often and tier 5 more), replacing
  the Dangerous / not split above (`SpawnSpec.eventTier`, `TrafficPlan.AddPirateEventShips`): outposts x0.4 .. x2 hull,
  guards base -3 / -1 / 0 / +3 / +6 (the dearer fighters from tier 4, `NpcTables.StrongFighter`), turrets 0 / 0 / 2 / 4
  / 6, the crate 1 half the pirate base's goods, 2 those, 3 + 2-4 t rare goods, 4 a piece of equipment (by rank) + 3-6 t
  of the costliest goods, 5 two pieces of the best equipment + 8-12 t, and at every tier the pirates' stash, freighter
  cargo rolls 1 / 2 / 2 / 3 / 4 (`TrafficPlan.OutpostStashRolls`; a player's report 2026-10-16: an outpost gave 1 t of
  something); bosses x0.35 .. x2.4 hull, gun x1 .. x3, 0 / 1 / 2-3 / 4-5 / 6-7 escorts, 1-2 .. 10-16 t of goods (the
  costliest from tier 4), speed 4 at tier 5; the bounty x0.5 / 0.8 / 1 / 1.5 / 2.5 (`PirateReports.Bounty`). Players'
  feedback 2026-10-22 ("bosses aren't worth it beside outposts", "the droppable blueprint weapons are too limited"): a
  boss's stash rolls 0 / 0 / 1 / 2 / 3 (`TrafficPlan.BossStashRolls`), and from tier 4 the outpost's equipment
  (`OutpostGear`) too; tier 4 / 5 events of both kinds add a munitions stack (`TrafficPlan.Munitions`: Amour Rocket /
  Intelli Jet 20-40, from rank 10 or tier 5 Intelli Jet 30-50 / AMR Extinctor 15-30); the equipment tables gained the
  128MJ Railgun (low) and Gunant's Drill (high); multiplayer's finished world (`Session.CompletedWorld`, so a single
  player still meets them in their campaigns) adds the add-ons' gear (low Berger FlaK 9-9, AGT 20mm; high Icarus,
  Disruptor, SunFire o50, Archimedes, Matador TS, Pandora Leech, PE Proton / Ambipolar-5) and munitions (SG-100; high
  Shock Blast, SG-400); never the Kaamo Club's own items or a time extender. Loot of
  several kinds drops a crate per kind, thrown apart at 3x a crate's push in directions spread over a sphere
  (`NpcShip.DropCrate`, `Crate.Launch`; a crate holds all of one thing), and a crate of several stacks gives every one
  (`CombatRadar.Capture`; the capital ships' crates gave only the first).
- **Pirate activity board** (remake, same suggestion; part of the option "Pirate outposts and bosses", so a session
  rule; `PirateReports` plain C#, `MissionsWindow`'s third tab "Pirate activity"): the authorities report 3-5 sightings
  (3 + rank / 8) of an outpost or a named boss (names combined from preset epithets, first names and surnames in seven
  patterns, untranslated), each at a hidden station of a visible system where `TrafficPlan.PirateEventAllowed` lets an
  event happen (one per system), with its tier, a bounty set when reported (the event's x1.25, a contract) and 40-90 min
  of playing time on the board; refreshed on docking (`StationLevel`), saved (v17 `pirateSightings`). The board names
  the system, never the station (Show on map routes to the system; the mission map marks no planet); entering the
  sighting's orbit spawns it (`TrafficPlan.AddPirateEvent`, whatever the pilot's rank), destroying it pays its bounty
  and marks it done (`Traffic.PirateEventDone`, `PirateReports.Done`); with the board the unannounced roll drops to 5 %
  / 2 %, and never on top of a raider group (`AddPirateEvent`'s hasRaiders; a reported sighting still comes; same
  report: on Extreme the two together overwhelmed a pilot on arrival). Warnings: the station ticker reports the
  sightings in this system and its gate neighbours (`PirateReports.Ticker`, `StationMenu.SetupTicker`), and 5 s into an
  orbit of a sighting's system the authorities radio once (`Traffic.UpdatePirateEvent`, `RaceRadio`; a boss's own call
  waits behind it); the boss's lock plate and call carry its name. Multiplayer: one board for the session, kept by the
  server (`PirateReports.ServerTick` every 5 s from `NetState.Update`: 4 reports, bounties at rank 10, times on the
  server clock `PirateReports.Now`) and published as JSON in `NetState.pirateBoard` (mirrored into every game's
  `Session.PirateSightings`, `ApplyShared`, a profile load too); the orbit's authority spawns the reported event, its
  destruction goes to the server (`NetState.PirateSightingDoneRpc`, checked against the board and the sender's orbit),
  which pays the killer, or the killer's squad split evenly (every member online, `PirateBountyRpc`, a chat notice for a
  share). Verified in Play mode: the board, the tab, the warning and the call, a kill paying the report (single player
  and a hosted session: 59 400 paid by the server, the board refilled), save and load, the tiers' numbers and a tier-5
  outpost's three crates flying apart. Not tested with a second player (the squad split).
