# Combat equipment

Notes moved from `CLAUDE.md` (its index lists every notes file). Code comments citing `CLAUDE.md "<section>"` mean the section or bullet of that name here.

## Combat equipment

Research: `Reference/research/combat_equipment.md`. All equipment is looked up by item category (= the original's sort, `Shop.FirstMounted`).

- **Cloak** (`Cloak` plain C# + `PlayerCloak`; items 94-96, integrated on ships 44 / 49 = item 95's stats; a mounted
  cloak replaces the integrated one; remake option, Options > Gameplay "Cloak bay on cloaking ships",
  `Settings.CloakBay`, on by default: on those two ships it goes into the integrated cloak's bay instead of an equipment
  slot (`Hangar.HasCloakBay` / `BaySlot` / `BayTakes`: one more equipment place while a cloak is mounted; off = the
  original, a slot; turned off, the next docking's `FitToSlots` moves what no longer fits to the hold)): the quick
  menu's entry (the item's name; half-transparent while not ready) or C / right stick press pays attr 38 energy cells
  (122, "-N t Energy Cells"; not enough = 583), charges attr 36 ms (the Khador charge bar with "Cloak charging" 317),
  cloaks for attr 35 ms (2 s fades), then a 7000 ms (Extreme 12000) cooldown and "Cloak ready" (316). Cloaked
  (`Target.cloaked`): NPCs keep chasing but don't fire, turrets don't aim, sleepers don't wake; nothing ends it early.
  Remake option (Options > Gameplay "EMP breaks cloaks", `Settings.EmpBreaksCloak`, on by default, a session rule;
  players' suggestion): an EMP hit (a shot or a blast) on a cloaked or charging cloak ends it at once, then the cooldown
  (`Cloak.Break`, `PlayerCloak.Break`: "Cloak disrupted!"; from NPC EMP shots, `NpcShip.EmpBreaksCloak`, and in
  multiplayer another player's, `NetPlayer.EmpRpc`; the EMP still drains nothing else of the player); a Specter's cloak
  drops the same and can't come back on its own for 6 s (`NpcCloak.Break`, `NpcShip.BreakCloak` from
  `WeaponSystem.ApplyEmp`, NPC EMP and `NpcShip.OnRemoteEmp`); a phase cloak is out of the EMP's reach. Multiplayer: an
  NPC's cloak reaches the other players' copies (`NetProxy.cloakPct`, drawn with `NpcCloak.Show` like another player's,
  off the radar and the lock from 25 %); the copy stays hittable while cloaked (it used to vanish whole, hull included),
  so anyone's EMP breaks a Specter's cloak and a blind shot lands. Remake option (Options > Gameplay "Cloak shakes off
  pursuers", `Settings.CloakLosesPursuers`, off by default): NPC ships lose a cloaked target (`NpcShip.Pursuable` in the
  targeting), fly to where they last saw it (12 s at most, until within 150 m) and go back to their routes; a finished
  route flies straight on instead of circling the cloaked player. In a session it is the session's rule (see
  "Multiplayer", Gameplay rules). Remake: the homing missiles locked on the ship lose their lock like on a boost (see
  "Missile warning and evasion"); in multiplayer the other players lose the ship's marker and lock (a held lock too,
  `CombatRadar`) from the moment it engages. Look: the hull switches to `GoF2/Cloak` (BumpShaderCloak's fragment shader
  as it is: dissolve by `cloak_map.png`'s veins into the refracted screen with a light-blue band along the front, over
  the lit hull up to 0.5 and into pure refraction by 0.75, so the pattern shows through most of each 2 s fade; #47: it
  ran at twice the speed; drawn by `CloakPass` after URP's transparents from a copy of the frame, like the original's
  refraction FBO (`Engine::CopyFBO` after every alpha / additive material), so the hull refracts the ship's own lights
  and glare, smeared by the wobble: the crawling coloured patches of a cloaked ship; Modified, #185: the picture behind
  stays bent in place by the normal map and only its bright part, the glow, wobbles, where the original's sweep slid
  the stars too; the hull's depth goes in before the transparents, `CloakPass.DepthPass`, but only where the hull
  hasn't dissolved yet, clipped by the dissolve map against `_AnimValue` like the colour: the ship's own lights behind
  the solid hull stay hidden, and a fully cloaked ship writes none, so the shots, explosions, smoke and flames behind it
  still show through it instead of a hole in its shape), the lights keep glowing and
  animating (the original's white at alpha max(50, ...) leaves its additive layers at full brightness; #47: the remake
  hid them), the NPC Specters' engine glow hides from 25 % (`CloakGlow`, `NpcCloak`; the code hides the ship group's
  last child), the players' ships keep theirs (`CloakGlow.KeepEngine`, #85: the reporter's screenshots of the original
  show it on cloaked player ships); sound 30. Medal 19 counts `Session.CloakMs`. **Phase cloak** (remake, for mods: a
  cloak item with attribute 104 = 1, `ItemStats` "phaseCloak"; the example is the `pegaphase` mod in the mods repo, the
  PegaPhase): while cloaked, fades included (`PlayerCloak.Phasing`), `PlayerCollision` skips the landmarks, freighters,
  other players' ships and asteroids (the wormhole still pulls); ending inside one collides as usual. `Target.phased`
  (set by `PlayerCloak`, and by `NetPlayer` from the owner's `phase` variable on the other players' copies, whose sphere
  `Obstacle` turns off): `Gun` hit tests and blasts skip it, `Target.Damage` and EMP do nothing.
- **Emergency system** (185): the hull running out sets it to 1 instead (`Target.SaveFromDeath`), 10 s invulnerable
  (attr 41) in the `v_shield` bubble (`GoF2/ShieldBubble`, `SimpleRefractionShader`, drawn by `CloakPass` from its copy
  of the frame after the transparents so the ship's lights and exhaust show through it, its noise read back in gamma
  space (a data texture imported as colour: linearised, the picture shifted down-left); Discord report 2026-10-18: an
  invisible sphere bending the screen behind it, the rim from the noise texture read as a tangent-space normal, no
  scroll; the scene it shows clamped to LDR (#45: the HDR sun core smeared over it bloomed the screen white); grows /
  shrinks over 5 %), sound 1115 looped until it ends; the item is used up (the one mounted,
  `PlayerHealth.emergencyItem`: a mod's emergency system based on 185 too, which the removal of 185 alone left mounted
  for good). Kills meanwhile count for medal 43 (`Session.GraveRiserKills`). The bubble is `Flight.EmergencyBubble`;
  multiplayer: the others see it on the ship too, with the loop at the ship (`NetPlayerEquipment`: the owner's clock as its
  end in server time and its length), and their copy takes no hits meanwhile (`Target.invulnerable`).
- **Time extender** (184): the fast-forward slot shows its clock outside the autopilot (touch), or X / left stick press:
  15 s (attr 42) of world ×0.3 (`Time.timeScale`, `Navigation.ApplyTimeScale`) with the player's own updates (flight,
  chase camera, guns, cloak, beams) at ×0.7 (`TimeExtender.PlayerFactor`), sounds pitched down, sound 1120
  TimeShift_Start, 1119 TimeShift_01b at the end; tap again ends it; 30 s cooldown (attr 43), then the icon flashes.
  Cinematics, jumps and death cancel it. Not in multiplayer (`TimeExtender.Attach`: the shared world can't slow for one
  player), where it isn't obtainable either (`Shop.NotInSession`: no shop, loot, crate, armory, bar seller or Purchase
  mission has it, and one sold into a shared stock is paid for but not put on sale); one a pilot already has stays.
- **Repair / transfusion beams** (`RepairBeam`, sorts 37 / 41): automatic; every 2.5 s the attr 55 slots take the most
  damaged friendly ships (repair: hull +dt·0.03·attr54/100) or hostile ships (transfusion: −dt·0.01·attr54/100 as player
  damage, the same into the player's shield; both only while the shield isn't full, and only then are hostile ships
  picked: a full shield leaves the beams idle, which players took for switching on and off at random) within attr 53;
  beam meshes 19092 / 19093, loop sounds. Multiplayer: the other games' ships here (`Target.NetShips`) are picked too,
  after the game's own (they only saw those, so in an orbit another player ran they did nothing): a hostile one (an NPC
  another game runs; a player where players may fight) drained through its owner (`Target.RemoteDamage`, the player's
  hit), a friendly one (a squadmate, a friendly NPC another game runs) healed through its owner (`Target.RemoteHeal`:
  `NetProxy.HealUpRpc` / `NetPlayer.HealUpRpc`, the server checks the orbit, a squadmate for a player, the amount and the
  rate), whole points every 250 ms; their real max hull (`Target.netMaxHull`) keeps the most-damaged pick right. The beam
  is drawn only in the beam owner's game.
- **Missile defense** (remake, for mods: EVERSPACE 2's Missile Defense System, the user's idea; `Flight.PointDefense`,
  items.json stat `missileDefense`, attr 131, a reach in game units, on any mounted item, `PointDefense.ReachUnits`):
  the ship's auto turrets (`PlayerTurret.DefenseAim`, before `AutoAim`) turn to the nearest homing missile flying at the
  ship within the reach (`IncomingMissiles`, which now carries each missile's gun and bullet index), led by the turret
  shot's flight time (`PointDefense.Lead`), and fire when aligned; each shot claims one missile (`Claim`: several
  turrets spread over a salvo) and destroys it when the shot arrives (`Tick` -> `Gun.ShootDown`, the rocket impact's
  burst `ShipBurn.ManualBurst`); a claim keeps the missile's `Bullet.age`, so a new missile in the same bullet slot is
  neither skipped nor shot down by it, and the claims go with the scene (`SceneManager.sceneUnloaded`; review of #168:
  they kept the last level's guns and could burst there). Another game's mirrored missile (`Gun.Mirror`, set by
  `NetShotMirror`) is left alone: its real copy flies on in the shooter's game. An item with it is listed as "Missile defense" (`ItemInfo.Category`,
  Extra `catMissileDefense`) with its reach (`statMissileDefense`); the ES2 Ships mod's Missile Defense System is one on
  a seatless cabin (base 91, cabinSize 0: stackable, no other effect). Tests `PointDefenseTests`. Verified in Play mode
  (a Teneta with the Berger AGT and the ES2 item, a hostile Vossk battleship 700 m off): 12 missiles of its salvos shot
  down in a minute. Not tested in multiplayer.
- **Boss devices** (remake, 2026-11-04, the GoF3 roadmap's bosses: GoF3's DeviceEquip /
  DeviceBehaviorConfigurationUniversal, their lore BD_*_Lore; Modding/README.md "Most Wanted"): wanted.json "devices"
  (`ModWanted.ReadDevices`) as `Flight.NpcDeviceSpec` on `WantedData.devices`; `NpcShip.Setup` adds `World.NpcDevices`
  to the criminal's ship. Plain C# `NpcDeviceState.Step`: events (onHit, everyMs x chance) OR, conditions (range, ahead,
  notAhead, facing, hullBelow: the ship's own `HullFraction`, ES2's bosses' phases) AND, no event = the conditions
  alone; run, then the cooldown (from the run's end). Effects: teleport (outside the player's front 60 deg, nose on
  them, `Explosion` flashes), front / back shield and energy absorber through `Target.HitFilter` (the hit's direction
  from the bullet velocity; the absorber mends the hull, GoF2's NPCs have no shield), radar jammer (`Flight.RadarJam`:
  `Navigation.RadarOff`, `CombatRadar` drops the lock, `CombatView` hides the markers), flash grenade
  (`Flight.ScreenFlash`, `FlightHud`'s white layer, opacity x cos of the player's look off the ship, fading by the
  square of what is left), mind control (`NpcShip.MindControl` on `Traffic.LivingWingmen`: `UpdateMindControlled` makes
  the player its target, `UpdateRelations` hostile; the player's hit ends it in `OnDamaged`; the player is already in a
  wingman's enemy list, so its shots hit), bomb spreader (`NpcDeviceState.DueBombs`: one at the start, then every
  bombEveryMs while it runs; the rifts' `RiftMine` behind the ship), mine layer (2026-11-05, GoF3's boss mine layers,
  ArmingSmartBomb + MovingBomb: the same DueBombs, `NpcDeviceSpec.Lays`; `World.SeekerMine` layBehind the tail chases
  the player within seekRange at seekSpeed, goes off within triggerRadius or after lifeMs, blasts blastRadius for damage
  and slows by slowShare for slowMs; a `Target` of its own, untargetable, so the player shoots it down for its hull and
  the NPCs leave it), cloud (2026-11-05, GoF3's smart bombs' EffectZone, Mudshark's nanobite fog: `World.NpcCloud`, a
  sphere of cloudRadius for lifeMs; inside, `Target.SlowBy` kept up 250 ms at a time and hullPercent of maxHp a second
  past the shield), shadow clone (GoF3's DeviceConfigurationCloning, Khador's: `Traffic.SpawnShip` copies of its
  SpawnSpec, hull x hullShare, no loot, no devices; `NpcShip.Vanish` at the run's end or its death) and minions
  (DeviceConfigurationShieldingMinions, Sh'gaal's: `Target`s in `Target.RadarObjects` circling it with a LineRenderer
  beam each; `Filter` takes blockShare off while one lives; local to the authority, unlike the clones, which are
  ordinary NPCs to the other players too), wormhole (Khador's DynamicWormholeGenerator: `World.NpcWormhole`, the mouth
  layBehind its tail for lifeMs; the local player within its radius goes to exitDistance ahead of the ship's nose by
  `SpaceLevel.WarpPlayer`, the pose and the chase camera only, not `MovePlayer`'s arrival camera and Void wormhole).
  Only the orbit's authority runs `NpcShip`s (clients show proxies), so its own player alone is jammed, blinded or
  betrayed. Not done: GoF3's insanity, Mortak's smart bombs (no configuration), the shields' and absorber's own look
  (only a HUD line says it is up). Traffic hulls too (2026-11-05, GoF3's kamikazes): ships.json "traffic": { "devices" }
  read by `ModContent` with `ModWanted.ReadDevices` (file "ships.json") onto `CustomTraffic.devices` (NonSerialized);
  `NpcShip.Setup` attaches them to any NPC of the hull but wingmen and a boss's clones (`SpawnSpec.noDevices`),
  `NpcDevices.Say` only for a criminal; a negative layBehind lays the mine ahead (thrown). `NpcDeviceSpec.Carried`
  (minRank / maxRank against `Session.Rank`, share rolled at Attach: GoF3's SW_DL_* loadouts by danger level) picks
  which devices an NPC carries; a cloud's shieldPercent drains `Hitpoints.shield` alone (GoF3's sphere mines). Tests
  `NpcDeviceTests`, `ModWantedTests`, `ModCapitalShipsTests.ATrafficHullsOwnDevices`.
- **Devices** (remake, for mods: Galaxy on Fire 3's player devices, the GoF3 Ships importer's items; `Flight.Device` plain
  C# + `Flight.Slow`, `PlayerDevices` / `PlayerDevicesEffects`; Modding/README.md "Devices"): a mounted item with
  `device` (attr 150, 1-7; 150-160, past the tractor beam's 136 / 137) on a seatless cabin, its own one-per-ship kind per type (`Shop.KindOf` = `Device.KindBase`
  1100 + type, past the cruise drive's 1000). Fired from the quick menu (`Navigation.Kind.Device`, after the time
  extender, half-transparent while not ready) or 4 / 5 / 6 (`GameControls.Device1-3`, the first three in mount order; no
  pad default, every button is taken: D-pad left's quick menu), refused while `ExtenderBlocked` or the guns are blocked;
  runs `deviceDurationMs`, then `deviceCooldownMs` (GoF3: 60 s each) and "{0} ready"; a death or a cutscene cancels a run
  (`SpaceLevel.Update`). `CooldownView` shows each after the time extender. The effects: EMP wave (1) and EMP shot (2)
  slow hostile ships (`Target.SlowBy` -> `NpcShip`'s `Slow`: speed x factor, no turning, EMP sparks; multiplayer
  `NetProxy.SlowUpRpc` to the owner, rate-limited and orbit-checked like an EMP); the shots are an NPC-style homing `Gun`
  (the Dia EMP's, item 18; look = the item's fx or 18's) owned by the player; shield disruptor (3) drains a shield,
  `deviceDamagePercent` of it as damage on a shieldless ship (every game NPC); overchargers (4, 5) spend a share of the
  player's shield (refused with less) as damage within the radius / cone (beam look: transfusion beam instances, 400 ms;
  area: the shock blast explosion, type 11, scaled radius / 80000); strike charge (6) fills from
  `WeaponSystem.PrimaryDamageDealt` (primary hits on ships; GoF3: "Hit enemies to recharge device"), then
  `WeaponSystem.DamageBoost` on primaries; deflector (7) sets `Target.Deflects`: `Gun.Strike` (every hit site) asks it
  first, and an NPC's shot (not a mirror) turns into a homing shot back at its shooter from a pool `Gun` per
  look (a beam is only stopped), credited through `WeaponSystem.DeviceHit` (kill credit, standing, hit flash). Re-attached with a new hull
  (`SpaceLevel.AttachDevices`, `DevicesChanged` for the HUD's messages). Remake 2026-11-02, EVERSPACE 2's consumables as
  types 8-12 (decompiled BP_Consumable_SmokeBomb + BP_SmokeBombFIeld + BP_Condition_Debuff_SmokeScreen (the field hides the
  pawns inside it, IsHiddenByActor, and drops their sensing to 200 m), _ArmorRepair (armour to ratio 1), _NanoInjector (the
  base class's condition by EffectPercent: here a hull share), _SecondariesRestock (ammo + trunc(max x EffectPercent)),
  _DeviceCharger (Devices.ResetCooldowns); the mods give the numbers, the ES2 importer ES2's own from its items.CSV (its
  DataFiles in pakchunk0-Windows.pak, 2026-11-04), its Restock flat rounds (GoF2's secondaries have no maximum to take
  ES2's share of)): `SensorCloud` sets
  the player's `Target.cloaked` while inside (restored from `PlayerCloak` after), `HullRepair` / `ArmorRepair`
  (`RepairPoints`), `Restock` (`WeaponSystem.Restock`), `DeviceCharger` (`Device.ResetCooldown`); `deviceConsumable` (164):
  one use per unit, the mounted stack's amount (Hangar.Stacked: mounted whole, bought ones join it, `PlayerDevices.Spend`
  removes the item at 0; a running cloud's item is dropped when its run ends); `deviceAmmo` (165). The cloud itself isn't
  drawn (only the shock burst at the drop); the enemies inside the cloud don't lose their aim as in ES2. Types 13-18, ES2's
  cooldown devices (decompiled BP_Device_Corrosion_Injector + BP_Condition_Debuff_Corrosion (a 0.95 s timer dealing
  shield-piercing kinetic damage = the intensity), BP_Device_Teleporter (GetTargetOffset: a sphere trace forward by
  DEVICE_RANGE, the way kept SafetyMargin 1000 cm + the radius short of a hit), BP_Device_Spectral_Shift (the blink and a
  sphere trace of DEVICE_RANGE_2 along it dealing the device's energy + kinetic damage), BP_Device_FusionHook (aim dot 0.95,
  PullDelay 0.3, MinDistance 20000 cm, MaxAngleDot 0.8, MaxVelocity 80000 cm/s, the target pulled by 80000 cm/s^2 x
  clamp(d / 20000), the owner by AccelerationForward^1.5 / 15), BP_Condition_Debuff_QuantumTether (the tethered ship's damage
  dealt to the enemies within Range as a chain reaction; a jump to a random one when it dies), BP_Device_Quantum_Entangler
  (the owner's incoming damage summed, dealt to the target at the end x ReturnDamageMultiplier)): `Corrosion` (DeviceHit with
  shieldFactor 0), `Teleport` / `SpectralShift` (`BlinkDistance`: HullCollision.Sweep, the landmarks' volumes sampled, the
  asteroids as spheres; `Device.Blink`), `FusionHook` (both pulled by position each frame, speeds ramping by ES2's
  accelerations; another game's ship isn't moved), `QuantumTether` (Target.Damaged / Died), `Entangler` (the player's
  Target.Damaged summed). `deviceRange2` (166). The legendary modes and masteries of these devices are left out.
  Weapon debuffs (remake 2026-11-02, EVERSPACE 2's debuff conditions, decompiled BP_Condition_Debuff_Frozen (DisableControls),
  _Webbed (linear / angular damping +2: a slow), _Destabilized (+0.2 incoming damage per token on every layer, intensity
  stacking), _Corrosion (0.95 s timer) and _Infested (1 s timer), shield-piercing damage = the intensity; the launchers'
  ApplyConditionClass: Cryo -> Frozen, Web -> Webbed, Destabilizer -> Destabilized, Corrosion -> Corrosion, Spore -> Confused,
  Exorciser -> an Exorcism buildup (a slow at its end)): `Gun.slowFreeze` (167, `SlowBy`'s noTurn), `dotDamage` / `dotMs`
  (168 / 169) and `vulnerablePercent` / `vulnerableMs` (170 / 171) on the player's direct hits on NPC ships
  (`WeaponSystem.OnHit` -> `TargetDebuffs` running `Debuffs`: stacks, `Target.vulnerability` in `Target.Damage`). Not taken:
  Confused (the spore's: NPCs turning on each other), the armour breaker (no condition: a plain missile in ES2's blueprint),
  blasts and mines applying them (direct hits only). Not yet: the shots / beams / waves seen by the
  other players, the deflector against other players, NPC devices (GoF3's bosses), translations of the texts.
  `cloakCooldownMs` (attr 160) gives a cloak its own cooldown (`Cloak` constructor; GoF3's Hiroto Cloak 60 s, no cells).
- **Shield injector** (227): an empty shield takes 30 t Blue Plasma (202, "-30t Blue Plasma") and refills at 0.15 per ms; sounds 2258 / 2257 / 2259.
- **Gamma** (supernova stations 109-113, `PlayerHealth`): a 0..100 pool drains at the station's rate by campaign
  progress (gamma shields 205 / 206 cut it by attr 52 %), "Warning: Gamma shield low" (3201) below 15, death at 0,
  carried between those orbits (`Session.PlayerGamma`); the HUD's third status row (icon 0x1f59, frame 0x1f5a, fill
  0x1f5b, `GoF2Hud/status_gamma*`) shows the pool.
  The blaze (`PlayerHealth.SetupBlaze`, `PlayerEgo::PlayerEgo` 0xa5d8c / `PlayerEgo::update` 0xa9b7c): in the supernova system or
  Luur's orbit with a gamma shield mounted, meshes 18803 + 18802 (`sn_ship_blaze_flames / glow_anim_add`, `CombatAssets.gammaBlaze*`)
  around the ship, scaled by its bounding radius x 1.75 (`PlayerEgo::setShip` +0x3c), turned to the sun's light direction (up
  0, 1, 0), ship 8 300 units ahead, animated; gone on death and the planet jump. Its textures with the colour ^ 1.5
  (Hidden/GoF2/SunGamma, as the star map's suns: the dim tail showed to the planes' edges in linear space) and a second
  material, GoF2/BlazeHaze (Resources/GoF2Combat), bending the frame behind it through CloakPass's copy with a rising
  ripple of noise or, by default, sn_ship_blaze_flames_normal (texture 11759: in Android's resource list, used by no
  material; `CombatAssets.gammaBlazeNormal`, `_UseNormalMap`), a heat haze over the ship (the iOS version's, from a video; Android's data has the blaze plain
  additive); the flames are drawn after it in that pass (GoF2/BlazeAdditive), so they stay unbent; its cones rounded at
  load (`MeshSmoother`: smooth normals, two levels of Phong tessellation; the models imported readable) and their
  edge-on sides faded (`_EdgeFade`), no polygons on the outline.
- Spectral filters, gas clouds and plasma collectors: see "Supernova add-on".
- Not yet: the cloak's exhaust colour and the "not enough cells" window (a HUD message stands in), the menu button's ready flash.
