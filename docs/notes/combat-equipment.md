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
  (attr 41) in the `v_shield` bubble (`GoF2/ShieldBubble`, a port of `AbyssEngine::EnergyShield` 0x9519c: material
  27150 is type 0x22, which `PaintCanvas::End3d` draws with flag 0x200000 after `Engine::CopyFBO`; the screen behind it
  bent by `v_shield_normal` and the clip-space normal, (nt.xy * 0.4 + n.xy * rim) * 150 px, plus a blue rim
  smoothstep(1 - dot(e, n * nt)) * (0, 0.6, 1); the noise texture unused. #251: it was drawn with
  `SimpleRefractionShader`'s formula, no colour, hard to see. Drawn by `CloakPass` from its copy of the frame after the
  transparents so the ship's lights and exhaust show through it; the normal map sampled by three planar projections, as
  the mesh's UVs cut it where they wrap; the scene clamped to LDR (#45: the HDR sun core smeared over it bloomed the
  screen white); grows / shrinks over 5 %), sound 1115 looped until it ends; the item is used up (the one mounted,
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
  Shield Overcharge (22, remake 2026-11-11, Chorus's player capital ship: BP_Player_Auxiliary_ShieldOvercharge_PlayerCapitalShip,
  Duration 5, Cooldown 10; its native AAuxiliaryShieldOvercharge::OnActivation isn't readable, so the effect is the name's
  and the HUD's overcharge bar's): the shield filled, then `Hitpoints.shieldLocked` until its end (`Damage` stops every hit
  at the shield, `shieldHit` for the hit feedback, nothing taken off); its look the ship's own shell where ships.json has
  an `"overcharge"` (`ShieldShell`, Chorus's BP_Effect_Auxiliary_Shield_PlayerCapitalShip on M_Shield: mods-models.md
  "Chorus shield shell"), else the emergency system's `EmergencyBubble` while it runs.
- **Rites** (remake 2026-11-13, the user's request "start the rites": Chorus's powers as devices that also pay Rite
  energy; Rite of the Hunt first). Read from Chorus's BP_PowersMasterData, the BP_ActivePower_* defaults and the decompiled
  UEnergyComponent (AddRegularEnergy / AddBonusEnergy / RemoveEnergy / CanPayEnergy) and ABlinkActivePower
  (GetCurrentTarget, UpdateTargetTransform):
  - **Void energy** (`Flight.VoidEnergy`, one per ship in `PlayerDevices.VoidPool` once a device with `deviceVoidCost` (178)
    is mounted): BP_Player_Ship_Base's EnergyComponent, MaxRegularEnergy 3, RegenRate 0.22 a second (4.5 s a pip),
    RegenDelay 0; bonus pips past the maximum (AddBonusEnergy, Blink's upgrade A; each may time out) are paid first, the
    oldest first, then the regular ones; a cost above the total refuses the Rite ("{0}: not enough Void energy",
    `deviceNoVoidEnergy`). Full at the start. `PlayerDevices.Ready` / `Charge` add the pool to a Rite's readiness (the
    quick menu's half-transparency, "{0} ready", CooldownView's sweep toward the cost); CooldownView shows the pool as
    pips after the slots (`.void-energy`, Chorus's PlayerEnergyWidget segments). Cheats.NoEquipmentCooldown also
    waives the cost (Chorus's InfiniteVoidEnergy debug pref). Renamed from "Rite energy" the same day (the user's
    request; Chorus's own debug name), its cost stat `deviceVoidCost`. **Fuel** (the user's choice of three, over Void
    Crystals as the pool itself or as an instant refill): the regrowth burns Void Crystals (item 164, the Khador blueprint's
    ore) from the hold, `VoidEnergy.CrystalsPerPip` (1 t) a pip finished (`Burn`); with none aboard (`HasFuel`) the next pip
    doesn't grow and its progress is dropped (`Starved`: "No Void Crystals aboard: the Void energy can't grow back",
    `voidNoCrystals`, once each time); CooldownView shows the crystals aboard after the pips (`.void-fuel`, red starved).
    The debug's free equipment needs no crystals.
  - **Rite of the Hunt** (`Device.Type.RiteHunt` 23, Chorus's Blink: Range 100000 cm, BlinkLocationOffset -18000 cm,
    EnergyCost 2, Cooldown 0.5): the target is the lock if huntable and within reach, else the nearest huntable ship within
    `HuntAimDot` (10 deg) of the nose (TargetableRadius 0.1 is a screen share in UpdateActivePowerTargets, not decompiled:
    read as that cone). Huntable = `Affects`, not a capital ship's part, not a weak spot (`forwardTo`), not invulnerable,
    not the pursuer (ValidTargetsBlacklist: BP_NPC_Ship_Fortress and BP_NPC_Guilt). The landing is the offset back along
    the target's forward, facing its way (UpdateTargetTransform takes the target's rotation), swept back from the target
    with the teleporter's `BlinkDistance` (its own hull left out) and refused with no room ("no room behind the target");
    the speed is kept along the new nose (RestorePlayerSpeed), the chase camera snapped behind. Distances left at 0 are
    seconds of the ship's top speed as Chorus flight's (`HuntRangeTops` 7.14, `HuntBehindTops` 1.29 of 140 m/s);
    `deviceRadius` / `deviceRange2` set them in game units. Its timelines (later the same day, the user's "both"): the
    model shrinks away over Blink_Disappear (0.067 s, `Device.HuntVanish` ~ u^3 from its keys), the leap, the model grows
    back over Blink_Reappear (0.33 s, `HuntAppear`), both at BlinkPlayRateCurve's rate (1.5x for a leap of nothing to 1x at
    the full reach, `HuntPlayRate`); the chase camera glides from where it was over Timeline_1's 1 s on real time
    (`ChaseCamera.Glide` after `Snap`, read as an ease-out 1 - (1 - t)^3: the curves' tangents in the dump don't agree
    with their Auto mode). The ship's model is scaled (Chorus's Transition drives its material, not read); a death, a
    cutscene or a jump puts it back (`PlayerDevices.Cancel` -> `EndHunt`); a target gone meanwhile: no leap. Not ported
    yet: the HUD's target highlight, BlinkTargets (interactables), upgrade A (bonus energy after the leap), upgrade B
    (+50 % damage for 5 s, BP_Player_Buff_Blink_UpgradeB), the supercharged version's 2200 m final blow.
  - **Rite of the Storm** (`Device.Type.RiteStorm` 24, Chorus's Pulse Zap: BP_ActivePower_Pulse_Zap Range 100000 cm,
    ShieldDamage 300, ChainRange 10000, ChainInterval 0.25, EnergyCost 2, Cooldown 0.5, Duration 0; BP_Buff_Pulse Duration 4
    with JamTarget / JamWeakpoint / JamMissile): the target as the Hunt's (`RiteTarget`) but `Stormable` (turrets and weak
    spots allowed, a capital ship's hull, the invulnerable and the pursuer not: its whitelist SpaceNPC / turrets / weak
    points, its blacklist the Fortress, Guilt, the Disruptor and the bosses' weak points); `ShieldHit` (the shield
    disruptor's rule, factored out) with 'damage', `empDamage` too; jammed for deviceDurationMs (`Entry.jamMs`; the device
    itself instant): `Target.SlowBy` (deviceSlowPercent, default 0, no turning) and `Target.jammedUntil` (NpcShip.HoldFire:
    no guns, missiles or turrets); a beam from the ship (`ShowBeam(t, from)`: a beam may start at another ship now); with
    `deviceChain` (179; upgrade A's ChainLimit 2 at 50000 cm) the nearest other stormable ship within deviceRange2 (default
    0.71 s of the top speed) of the last one every deviceGrowMs (default 250), beamed from it. Not ported: the occlusion
    trace (OcclusionCollisionChannel), the 1000 collision damage of a jammed ship, missiles destroyed, upgrade B's shield
    steal (0.25 for 0.5 s), the Faceless corruption clusters (no such objects in the remake); a jam on another game's NPC
    only slows it (the slow goes over the network, the jam doesn't).
  - **Looks** (items.json fx `"rite"`, `Data.CustomRiteFx` read by `ModWeapons.ParseRite` into `WeaponFx.rite`, a
    `RiteLook`): a JSON file of `start` / `vanish` / `appear` / `hit` effects (`ModDeathFx.Play`'s emitters; `hit` parented
    to the struck ship) with `vanishSound` / `appearSound` / `hitSound` (loaded with the item's other sounds); the start's
    sound is the fx's "shot". The Chorus importer writes them from the blueprints' Cascade systems and FMOD events.
  - **Rite of the Star** (`Device.Type.RiteStar` 25, Chorus's Drill: ADrillActivePower's constructor 0x140a4fd40, found
    through its GetPrivateStaticClassBody call's InternalConstructor (FUN_1408e1260 -> 0x140be1780 -> jne 0x140a4fd40),
    its fields named by the Dumper-7 SDK: RotationSpeed 50, CheckForCollisionsInterval 0.33, PostDeactivation 0.4,
    CollisionDamage 500, MinDurationTime 0.5, MaxDurationTime 3, CooldownTime 0.5, DrillForwardVelocity 42000 cm/s,
    MaxAngularVelocity 72 deg/s, the Suspended slow motion 0.5 / 0.1 / 0.2; BP_ActivePower_Drill overrides EnergyCost 3,
    the interval 0.2, the tail 0.3, the damage 750 and the sphere trace (10000 cm round 2500 cm ahead);
    ApplyOrRestoreSpeedFromDrillValues sets the LinearSpeedX and MaxAngularSpeed modifiers to DrillForwardVelocity / the
    top speed and the turn / the steering's). Here: deviceDurationMs (default 3000, the device's run), a second press
    after `StarMinMs` ends it (`PlayerDevices.Use`; Min / MaxDurationTime read as that); `ShipController.DriveMs` /
    `DriveTurnDegS` (both flight styles: the forward move replaced, the turn held to 72 deg/s by `CapTurn`; the speed it
    had kept for after, as Chorus restores it) at `StarSpeedTops` 3 of the top speed; the model shrunk away over 0.15 s
    and back (the Hunt's scaling, `ApplyModelShare` taking the smaller of the two; Drill_Activate's timeline not read);
    `Target.invulnerable` while it runs (EnableOrDisablePlayerShip, its partial collisions; the previous value restored:
    the Supernova Challenge sets it too); every deviceGrowMs (200) and `StarTailMs` 300 past its end each `Affects` ship
    within deviceRange2 (default `StarRadiusTops`) plus its radius of the point `StarAheadTops` ahead takes 'damage' / its
    EMP (`Device.InStar`), with the look's `hit`. Looks: `loop` on the ship (removed at the end) with `loopSound` looping,
    `end` and `endSound` where it ends. Not ported: the StartDrillMovementSpeeCurve ramp, the slow motion when suspended
    (OnDriftStarted / OnSuspended), the hit sounds' kill and lock variants, upgrade A's four side stars (RotationSpeed 100)
    and B's 25 % heal a kill.
  - **Rite of Control** (`Device.Type.RiteControl` 26, Chorus's Telekinesis: BP_ActivePower_Telekinesis GrabRange 70000,
    TossRange 80000, TossSpeed 56000 cm/s, EnergyCost 3 (the powers screen says 4), OffsetLocations (5000, +-2500, +-2500);
    BP_Telekinesis_Debuff CollisionDamageOtherTargets 500, from geometry / another target 500000, Duration 1e6; the native
    GrabTargets / CollectTossToTarget take the targeting component's current targets): `Controllable` (the Hunt's
    `Huntable`, a ship, not another game's: `RemoteDamage` null); held at OffsetLocations[0] (`ControlHoldTops`, eased
    there each frame), jammed (`Target.jammedUntil` and a 0 slow without turning), beamed; a second press (`Use`) throws it
    (`Toss`) at `RiteTarget` within `ControlTossTops` of it, else ahead, at `ControlSpeedTops`, homing; each frame the
    first `Affects` ship along its step takes 'damage' and the thrown one dies (`DeviceHit` 1e7 past the shield, the
    player's kill), a hull / landmark / asteroid on its way (`BlinkDistance` with it left out) kills it; nothing met in
    `ControlFlightMs` (the toss's reach at its speed + 0.5 s): let go (`Unjam`: the jam ended, `Target.ClearSlow`, NpcShip's
    slow cleared). The hold's end (deviceDurationMs, default 8 s: Chorus holds for good) lets it go too. Not ported: more
    than one ship at a time (the debuff's MaxNumberOfStacks 2, four offsets), turrets, mines and missiles grabbed, the
    connection's spline mesh (the device beam stands in), the toss sounds' variants.
  - **Rite of the Senses** (`Device.Type.RiteSenses` 27, Chorus's Scan / SeventhSense: BP_ActivePower_Scan
    ShortRangeScanDistance 600000 cm, ShortRangeScanDuration 3 s, Cooldown 0, the powers screen's cost 1): instant; a wave
    (`Device.WaveRadius` over `SensesWaveMs`) out to deviceRadius (default `SensesTops`) uncovers every hostile NpcShip it
    reaches that is `RadarHidden` by its cloak (`NpcShip.Uncloak`: the cloak broken whatever `Settings.EmpBreaksCloak`; a
    script's radar-hidden ship is left so), with the look's `hit`; and for deviceDurationMs (default 20 s, the remake's:
    Chorus's short scan shows its finds 3 s) `RiteSenses.Until` gives the ship the Hiroto Ultrascan's sight
    (`Mining.Ultrascan`: the class-A asteroids' letters) and the cargo scanners' readout on a new lock (`CombatRadar`,
    attr 31's `ReadCargo`) — what the remake hides that a scan can show. Its wave also reveals the orbits' cloaked
    containers (`LootContainer.Reveal`, as `CloakedContainers` does within 144 m; "{0}: {1} hidden container(s) found",
    `riteSensesFound`). The long range (the same day, the user's "continue": AScanActivePower's constructor 0x140c3df70
    gives LongRangeChargeDuration 1, LongRangeScanDuration 3, ShortRangeChargeDuration 0.5, ShortRangeScanDuration 2,
    ShortRangeScanDistance 180000, LongRangeScanDistance 1e8, EnergyCost 1; the blueprint's 0.001 / 3 / 600000 / 2e7):
    the Senses' key (`GameControls.Device1-3`) held `SensesChargeMs` fires it with `Use(slot, true)`, a wave to
    `SensesLongTops` (the whole orbit) over `SensesLongWaveMs`; let go sooner, the short scan; the quick menu and touch
    always the short one. Chorus's long range marks its finds on the star map; here the finds are the orbit's own (the
    containers' markers, the ships uncovered). Not ported: the charge's look and sound, the post-process pulse,
    P_Sphere_Expansion (a mesh sphere).
  - **Upgrades** (`deviceUpgrade` 180, bits `Device.UpgradeA` / `UpgradeB`; `Entry.A` / `B`), read from their blueprints'
    graphs: the exporter's `CHORUS_SCRIPT=1` turns on CUE4Parse's ReadScriptData, so the JSON carries each function's
    Kismet bytecode (the scratch kis.py lists its calls). Hunt A: `VoidEnergy.AddBonus(1)` after the leap (AddBonusEnergy(1);
    a bonus pip lives BonusEnergyLifetime 10 s, UEnergyComponent's constructor 0x140be1d60, which also has RegenDelay 5 and
    RegenRate 0.5 the blueprint overrides). Hunt B: `StartFury`, `WeaponSystem.DamageBoost` 1.5 and the Rites' damage x
    `RiteFury` for 5 s (BP_Player_Buff_Blink_UpgradeB; back to 1 at its end unless the strike charge runs). Storm A:
    `deviceChain` 2 and the jump `StormChainTopsA` (500 m) by default. Storm B: `ShieldHit` returns what it took (a ship
    without a shield: what it dealt), a quarter onto the ship's own shield up to its maximum (`GainShield`; Chorus
    IncreaseShield's overcharge past it not taken). Star A: four more spheres (`Device.StarSide`: (10000, +-6000, +-6000)
    cm, RotationSpeed 100 deg/s about the nose), a ship hit once a tick whichever it is in. Star B: a ship the Star
    destroys repairs 25 % of the maximum hull (`HealShare`, past `Repair`'s nanobot count). Their looks: the rite file's
    `fury` (on the ship while it lasts, `furySound` looping; `StartFury` / `EndFuryLook`), `absorb` and `heal` (one-offs
    on the ship, `PlayOnShip`); the Chorus importer writes each upgraded item its own file (its UPGRADE_LOOKS: the
    upgrade blueprints' particles and Start / Appear / Muzzle / Duration / Re-Appear events; Pulse B's
    P_Pulse_UpgrB_ShieldAbsorb is a mesh particle, so only its Shield_Stolen sound). Not taken: Blink AB's
    energy for a kill within 5 s (BP_Buff_GiveEnergyForKillAfterBlink: its EnergyAmount unset), the upgrades' own looks.
  - **Passive** (`Device.Type.RitePassive` 28; Chorus's BP_PassivePower_ElementalShield: BonusEnergy 2,
    VoidEnergyIncreaseEffect, "Prevents all elemental damage"): not one of `PlayerDevices.Devices` (no key, quick menu
    entry or HUD slot); `deviceVoidPips` (181) adds to the pool's maximum (`VoidEnergy(ChorusMax + pips)`, made once all are
    read), `deviceElementalProof` (182) sets `Hazards.ElementalProof` (HazardZones skips heat, frost and corrosion
    volumes; radiation and plain damage fields still harm). HavocCore and SacredMark have no numbers: not made.
  - Not taken: BP_Buff_GiveEnergyForKillAfterBlink (the base Blink's: a kill within 5 s refunds EnergyAmount, floor of
    a mastery value: Chorus's mastery levels, which the remake hasn't).
  Tests `VoidEnergyTests` (the regrowth, refusals, bonus pips, the delay, Blink's numbers, landing and timelines, the
  camera's glide, Pulse Zap's numbers, the jam, the Drill's numbers, the star's reach, the turn cap, Telekinesis's numbers, Scan's numbers and the sight, the passive's pips, the bonus pip's life, the
  fury, the Star's side spheres),
  `ModWeaponsTests.ARitesLooksAreAFileOfEffectsAndSounds`.
- **Hull regeneration** (remake 2026-11-11, for mods): ships.json `"hullRegen"` (points a second; Chorus's PCLS
  PlayerHealthComponent HealthRegenRate 250 of its 5000) -> `PlayerHealth` (`Hitpoints.RegenerateHull`, fractions carried,
  nothing at 0 or full), beside a repair bot.
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

- **Beam range labels:** the original's details window shows attrs 51 and 53 x 0.1f cut to an int (`ListItemWindow::set`
  0x159f7e, constant DAT_00159818 = 0.1, read from the disassembly: the decompile drops the multiply), and so does
  `ItemInfo`: SPP-C1 1500 m, SPP-M50 6000 m, Crimson Drain 2000 m, Pandora Leech 1000 m, the plasma collectors (51)
  5000 / 6000 m and the PE Proton's 2750 (Android) or 4000 m (Default Economy). Only the label: RepairBeam and PlayerTurret still use the raw game units.
