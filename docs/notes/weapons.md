# Weapons

Notes moved from `CLAUDE.md` (its index lists every notes file). Code comments citing `CLAUDE.md "<section>"` mean the section or bullet of that name here.

## Weapons

Research: `Reference/research/weapons.md` (functions, per-item table, fx, sounds, lock-on, HUD rects). `weapon_fx.json`
is generated from it by `Reference/tools/weapons/build_weapon_fx.py`; **GoF2 > Build > Weapon Fx** turns it into
`Resources/GoF2Weapons/item_XXX` (`WeaponFx`: projectile / muzzle / impact prefabs + shot sound) and cuts the crosshair.

- **Shop with a controller / keyboard** (remake): on a shop or ingredient row A / Enter buys (takes, adds) one unit and X
  sells (stores) one, like the right / left arrows (the original has only the arrows; its 101 / 100 sounds play).
- **Controller focus** (remake, `GoF2Common.uss` `.input-gamepad`): the focused control gets an amber fill and frame with a
  controller (the main menu root carries the class too); a focused choice row (`.choice-row`, the Debug pages' Give
  items / Spawn / Ships) its fill, and its drop-down box lights up (#24; the stepper's amber arrows went with the
  stepper). The main menu's Debug panel: left / right jump between the mission list and the cheats, back to where the
  focus was in that column (`MainMenu.OnNavigate`). The Status window's medal grid scrolls to the focused medal (#45).
- `Gun` (plain C#): one per equipped weapon on its mount (remake: weapons past the model's mount points, a mod's or a
  Kaamo upgrade's extra slots, reuse the mounts in turn, the centre without any; they were skipped, so an extra
  secondary could be neither selected nor fired); bullet pool; attr 11 reload, attr 9 damage, **attr 13 speed in
  units/ms, attr 12 "range" = lifetime in ms**; straight along the nose (no convergence/aim assist); axis-aligned cube
  hit test (or local boxes for freighters; remake: the hull's meshes, see "Mesh collision"), never hitting its `owner`;
  rockets/missiles coast 2 s past their lifetime; missiles home on `LockTarget` (the radar's ship lock; remake: also
  while it is off screen or the camera is in free look, `WeaponSystem.HomingLock`: `RocketGun::seekEnemy` needs the
  radar's on-screen flag, KIPlayer+0x72, so a missile went straight once its target left the view, a shortcut of the
  original's radar pass). NPC guns use the same class (see "NPCs and combat"); `GunRig` draws projectiles, muzzle
  flashes and impacts for both; the muzzle flash points along the ship's nose with the camera's up (`ObjectGun::update`;
  turrets: the turret's matrix). Blasters, thermo guns and turret shots face the camera (`ObjectGun::ObjectGun` +0x4c),
  but not a turret shot modelled as a streak along its flight (`GunRig.AlongFlight`: meshes over twice as long along +Z
  as across, the Archimedes' projectile_023): facing the camera its tail ran out from the screen centre, 2026-11-03).
- `WeaponSystem` (player): primaries fire independently while held (the looping shot sounds end like their FMOD events
  on release: "loop and play to end" cannons / thermo / Hammerhead turrets finish the shot in progress, the "cutoff" DLC
  turrets fade 50-100 ms; `LoopRelease`), secondary one per release (ammo = item amount, `Session.EquipmentAmounts`).
  Keys: Space / LMB fire, R / RMB missile; controller RT fire, LT missile (throttle moved to LB/RB).
- `Target`: hittable objects (asteroids: radius meshRadius*scale*0.7, HP scale*100+30, rockets kill asteroids instantly,
  explosion prefab + sound 21; ships and the player with `Hitpoints`). Stations are never hit.
- **Special weapons** (`Reference/research/weapons_special.md`, `Gun.Kind`): beams (9-11, 228: a stretched beam mesh,
  instant hit, auto-aim within a cone; `BeamGun::update`: the beam keeps the direction and length it was fired with
  while its start rides on the mount, so through a turn it stays where it was shot; re-aimed every frame from 2026-10-01
  until players asked for the original back on 2026-10-11; an aimed beam hits its target even through a gap in the hull,
  `Gun.AimedBeamHit`, #192: its sweep ends at the target's centre, and the Midorian freighter's lies between its four
  parts, so ~6 % of the angles did nothing, where the original's beam parked at the centre is in the cube), scatter guns
  (burst cube: a target's cube x1.5 / x2 / x3 by the shooter's distance; remake, `Gun.InScatterCube`, 2026-10-30: not a
  big object's, a hit radius past 2000 units, which keeps its plain cube: the pirate outpost's +-7500 grown x3 reached
  1.1 km out, so under Original collision every scatter shot near it burst at the ship's nose, a player's video; the
  original grows it too, only stations, KIPlayer+0x6d, get x0.5), EMP bombs and nukes (a second
  press detonates, area damage with falloff, explosion types 7 / 11; see "Bomb arming" below), mines (drift, proximity), cluster missiles
  (salvo), the Shock Blast (226: damage + EMP + push, `NpcShip.InitPush`), ionizing missiles, plasma collectors, weapon
  mods (sort 28 factors on primaries). Several secondaries: G / D-pad right (or tapping the HUD label) cycles
  `Session.SelectedSecondary`.
- **Bomb arming** (remake option, players' report 2026-11-04: nukes, EMP bombs and Ion Lambdas "explode right in front
  of my ship" with enemies about; Options > Gameplay "Bomb arming distance", `Settings.BombArming`, a Game experience key:
  off in Original / Cosmetic, on in Remake): the original ignites a bomb in any ship's hit cube from the launch
  (weapons_special.md 3.1), and a fighter's cube is +-1000 units (50 m; Mixed collision tests fighters on it), so an
  enemy near the nose set it off 35-65 m ahead, right after the release (a play-mode repro; not the input, which fires
  once on the release, `WeaponSystem.FireSecondary`). On, the contact fuse arms after `Gun.BombArmingUnits` (2000, 100 m)
  of flight (`Gun.BombArmed`: no hull, probe or cube ignition before; asteroids still die on contact; a second press still
  detonates at once). A blast past 100 m still adds to the volatile meter and Hardcore self-damage. Tests `BombArmingTests`.
- **Beam auto-aim** (#100, `WeaponSystem.BeamTarget`): with the Smart target lock (the default) a friendly or neutral ship is
  only aimed at when it is the radar's lock; the research says the original takes the nearest *enemy*, the remake took any
  ship and a friendly crossing the box took the shots. Original lock: as before. Never an asteroid (a player's report): `Radar::draw` sets
  the auto-aim flag (KIPlayer+0x6f) only in its loop over `Level::getEnemies` (the ±24000 cube branch is for near ships),
  its asteroid loop never does; the remake's beams took the nearest rock in the box while enemies were about.
- **Liberator** (guided rocket): the ship holds still and the stick steers the rocket, the camera follows it; ends on
  impact or timeout. Remake: steered as seen through the camera (`WeaponSystem.UpdateLiberator`, `Gun.SteerBullet`'s
  axes: the camera's up and right; the camera follows it upright, world up, like the original's
  `useTargetsUpVector(false)`), so stick right turns it right on screen whatever the ship's roll at launch (turned about
  its own launch-time axes, a Liberator fired upside down steered mirrored); the invert options apply; mouse steering
  steers it too (`FlightHud.UpdateMouseSteering` switched it off while a missile was steered), also on the autopilot and
  in free flight (a player's report: `ShipController.ReadMouseSteer` centred the mouse whenever the autopilot flew,
  before its steering-locked case, and free flight's `ReadVirtualStick` whenever steering was locked, the Liberator's
  own lock; the turret view the same), the virtual stick centred as it starts and ends.
- **Turrets** (`PlayerTurret`, `TurretAim`): the turret-slot item on the ship's turret mount (in space the
  `*_ship_mounted` assemblies, in the hangar `hangar_turret_item_N`; remake fix: the Archimedes' (182) and the 181's gun
  offsets are written turned with their base in the ship-mounted rules, (-34, 88, -1.6) / (0, 88, 11), since the base
  turns inside the pivot there; unturned the Archimedes' gun sat on the far side of its base in space, while the hangar,
  which turns the whole object, was right; every turret's gun-to-base offset now matches between the two); auto turrets
  (180-182) aim and fire at hostiles, the others by the turret view (T / D-pad up, touch button), Y / D-pad down toggles
  auto fire; animations only while firing. **Sentry guns** (`SentryGun`, 211-213): up to 3 deployed as secondaries, 100
  HP, 3 s invulnerable, hostile NPCs attack them. Multiplayer (`NetPlayerEquipment`): the others in the orbit see them
  (model, aim, hull, explosion; a marker and a lock with the player's relation) and the NPCs another game runs attack
  them like their player (`NetOrbit`: among the other players' targets, `NetPlayer.SentryOwner`); hits on a copy go to
  the owner's sentry (`SentryHitUpRpc`, checked like a hit on the ship; a player's only where players may fight).
- **Turret view inversion** (#103): the flight's invert options apply to the manual turret view too (`PlayerTurret`), as they
  already did to the steered Liberator (`WeaponSystem`).
- **One turret for everyone** (remake, `TurretCore` + `TurretTargeting`, plain C#; edit-mode `TurretCoreTests`,
  `NpcTurretTests`): the player's turret items (`PlayerTurret`), the NPC turret objects (`NpcShip` turret mode: the
  capital ships', the outposts' and pirate events', the fighter turrets of 45 / 51, the NPC sentry guns), the player's
  sentries (`SentryGun`) and every hangar's still copy share one core: the model's "pivot" / "*_gun" nodes
  (`FindNodes`), the mount placement (`PlaceOnMount`, `WeaponMount.TurretTurn`), the aim (`ItemAim`: attr 17's yaw,
  pitch -70 .. +500; `StaticAim`: -100 .. +600; a sentry's unlimited), the muzzle and shot start (`ShotFrom.GunFrame`:
  the item's (0, 0, 300), 48's alternating x +-80, 181's z -300, a mod model's own; `AheadOfBarrel`: an NPC turret
  object's 600 units; `Muzzle`: a sentry's), firing (`Fire`) and the animations that run only while firing; the pick is
  `TurretTargeting` with the original's two rule sets kept apart: `Retarget` (`PlayerEgo::handleAutoTurret`, the
  sentries: every 3000 ms or at once when the target is lost, the unreachable one skipped) and `Timed`
  (`PlayerTurret::pickEnemy` / `handleTurret`, the NPC turrets: only after more than 3000 ms, the unreachable one as a
  fallback; its `Measure` sees the best distance so far, which the turret hunters' x0.3 needs). What stays per owner:
  the turret view, auto switch, collectors and loop sound (`PlayerTurret`); the Target, hull, death, host sync and
  capital enhancements (`NpcShip`); deploying and death (`SentryGun`). `TurretCore.Display` / `DisplayAll` is the
  hangar's (`CutScene::checkForTurret`), `PlayerTurret.BuildStatic` calls it. On top (remake): **any turret item on an
  NPC** — `SpawnSpec.turretItems` (turret items on the ship's own turret mounts, `World.NpcMountedTurrets`: the item's
  model, rates, gun, missiles and salvos, aimed and fired by the NPC turrets' rules, range attr 109 else 50000, while
  flying, awake, unparked, not EMP-disabled; manual items aim by themselves, collectors only sit there; their guns in
  `NpcShip.ExtraGuns` for the shot mirrors; on 45 / 51 they take the fighter turret's place), else the ship type's
  (`Database.NpcTurrets`, a mod's ships.json "npcTurrets"); `SpawnSpec.turretItem` builds a turret object from an item
  (`TurretCore.ForItemModel`: its model at the object's scale, its gun x `gunFactor`, its aim and shot start; ModelPath
  "turret:N", `NpcShip.TurretModelPrefix`, which `NetProxy` loads); `/spawn ... turrets 48,182`, the debug Spawn page's
  "Turrets" row (the spawned ship's mounts, a capital ship's turret objects). The original's NPC ships carry no turret
  items, so nothing changes for them. Multiplayer: another player's copy of such an NPC shows its turret items still
  (`NetProxy.turrets`, "ship:item,item", `NpcMountedTurrets.Pack`); not tested with a second player. Verified in Play
  mode (a test mod's GLB turret, item 236 based on 182 with missiles, as the Veteran's `npcTurrets`): the player's auto
  turret (the mod model) killing two hostile Veterans, the NPC Veterans' mounted mod turrets firing in a fleet battle,
  the capital turrets aiming and firing (9 of 13 at once), a battleship built from item 48's turrets firing at the Vossk
  side, a sentry picking a raider, the manual turret view (47) with the crosshair on the barrel, the hangar's turntable
  with the mod turret, a parked NPC Veteran and Rhino with theirs.
- **Shield bypass and slowing weapons** (remake, for mods, 2026-11-02: GoF3's AbilityBypassShield / AbilitySlowdown, the GoF3
  Ships mod's Hunter-XT and Slowdeath): `shieldBypass` (161) makes `Gun.ShieldFactor` 0, which `Hitpoints.Damage` takes as past
  the shield (armor, then hull; the shield untouched; `NetPlayer.HitUpRpc` lets the 0 through); `slowMs` / `slowPercent`
  (162 / 163, `Gun.slowMs` / `slowFactor`) slow the ship hit in `WeaponSystem.OnHit` through `Target.SlowBy` with turning
  still allowed (`Slow.Apply`'s noTurn false; the EMP devices' stop the turning too). NPCs have no shield, so the bypass
  counts against players. Tests `HitpointsTests.AFactorOfZeroGoesPastTheShield`, `DeviceTests.AWeaponsSlowLetsTheShipTurn`.
- **Homing missiles near their goal** (remake, `Gun.Update`; user report): `RocketGun::seekEnemy` steers by (goal dir -
  dir) / k a frame at a constant speed, which can't turn tighter than ~150 m at 18 u/ms, so a missile passing its target
  (no ±1000-unit hit cube with the hull meshes) or reaching a salvo's spot in empty space inside a hull's hit boxes
  (`CapitalShips.SpreadAim`) looped round it. Within 250 ms of flight of its goal it now turns straight at it
  (`TerminalHomingMs`), and reaching the goal (this frame's path within 3 m of it) is a hit on its homing target when
  the gun has a hit list (not the multiplayer mirrors).
- **Smoke trails** (`RocketTrail`, `GunRig.EnableTrails`; `RocketGun::setRadar` / `update`, `ParticleSystemMesh::emitTrail`):
  the player's rockets and missiles (and other players' mirrored ones) trail record 39, a ribbon of particles.png's white
  smoke strip, a new section every 125 units, the last 29 kept (~180 m), 200 units wide, white -> transparent over 3000
  ms, drawn 2000 ms more after the rocket dies; the cluster missiles and thermo guns 28 / 29 / 30 records 25 / 26 / 27
  (the gold / red / purple strips, 50 / 100 / 150 units, every 50, 25 sections, 1000 ms). NPC rockets have none (setRadar
  is the player's); the SunFire o50 (193) record 28, its own flame strip on v_projectiles.png (material 24096,
  Level+0x98; #45). Every BombGun sort (EMP bombs, AMR nukes, ionizing missiles, the Shock Blast) trails record 12, the
  fire sprites (the Liberator's emission stays off), the Fireworks record 47, the same on the sparks sheet (material
  27321, Level+0x9c) (`RocketTrail.HasMissileTrail` / `MissileTrail`, `MaterialFor`; `CombatAssets.sunfireTrailMaterial`
  / `fireworksSparkMaterial`). A nuke's blast is the plain type-0 ship explosion at scale 1 without fire streaks, like
  the original (`BombGun::BombGun`: `Explosion(0)`, `setScaling` only for the Shock Blast). Remake: one camera-facing
  ribbon for the
  original's two crossed ones. The rocket meshes' `_add` flame children were already there.
- Explosions (`Explosion.Spawn(type, ...)`): 0 ship, 7 EMP, 8-10 scatter (one mesh, each gun its own colour: materials
  20151 / 20152 / 20153 for 176 / 177 / 178, `CombatAssets.scatterMaterials`; the one per-mesh prefab carries only the
  last id's, so every scatter burst was the Icarus' until they were swapped in), 11 shock blast, 13 fireworks. The fx
  atlases are imported clamped (repeat wrapping drew lines on explosions; except khador_jump and hyper_drive, whose
  meshes map past 0..1: `AssetImport.FxRepeats`, the original never clamps) with mipmaps, trilinear and 8x anisotropic
  like the original (`AssetImport.IsFxAtlas`; every fx .aei is type 3 / 0x42, image flag 2 = glGenerateMipmap): without
  mips the exhaust and engine glow shimmered.
- **Pooling** (remake, 2026-10-11): explosions are pooled (`Explosion`: a played-out one gives its parts back, keyed by
  prefab, material and pivot, and its root waits for the next Spawn; the pool is an inactive holder in the active scene
  and goes with it; a caller that keeps an explosion checks `Done(Serial)`, as `NpcShip` does, since the root is
  reused), one-shot sounds are 24 voices (`Sfx`: a free one, else the quietest when the new sound is louder), rocket-hit
  bursts one emitter per kind (`ShipBurn.ManualBurst`), and `PartAnimation` parses each model's sidecar once (`Parse`,
  shared read-only by every instance: every Instantiate of an animated model parsed it again, ~300 KB of garbage per
  ship explosion). Measured (Editor, 50 explosions/s for 20 s): garbage 193 KB/frame -> none after warm-up, the worst
  spawning frame 36 ms -> 4 ms, p95 frame 21.3 -> 17.8 ms. Projectiles, impacts, rocket trails (`GunRig`) and shot
  sounds (`ShotVoices`) were pooled already.
