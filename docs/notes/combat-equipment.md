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
- **Shield injector** (227): an empty shield takes 30 t Blue Plasma (202, "-30t Blue Plasma") and refills at 0.15 per ms; sounds 2258 / 2257 / 2259.
- **Gamma** (supernova stations 109-113, `PlayerHealth`): a 0..100 pool drains at the station's rate by campaign
  progress (gamma shields 205 / 206 cut it by attr 52 %), "Warning: Gamma shield low" (3201) below 15, death at 0,
  carried between those orbits (`Session.PlayerGamma`); the HUD's third status row (icon 0x1f59, frame 0x1f5a, fill
  0x1f5b, `GoF2Hud/status_gamma*`) shows the pool.
  The blaze (`PlayerHealth.SetupBlaze`, `PlayerEgo::PlayerEgo` 0xa5d8c / `PlayerEgo::update` 0xa9b7c): in the supernova system or
  Luur's orbit with a gamma shield mounted, meshes 18803 + 18802 (`sn_ship_blaze_flames / glow_anim_add`, `CombatAssets.gammaBlaze*`)
  around the ship, scaled by its bounding radius x 1.75 (`PlayerEgo::setShip` +0x3c), turned to the sun's light direction (up
  0, 1, 0), ship 8 300 units ahead, animated; gone on death and the planet jump. Its textures with the colour ^ 1.2
  (Hidden/GoF2/SunGamma, as the star map's suns: the dim tail showed to the planes' edges in linear space) and a second
  material, GoF2/BlazeHaze (Resources/GoF2Combat), bending the frame behind it through CloakPass's copy, a heat haze
  over the ship (a player's comparison with the original; its data has the blaze plain additive).
- Spectral filters, gas clouds and plasma collectors: see "Supernova add-on".
- Not yet: the cloak's exhaust colour and the "not enough cells" window (a HUD message stands in), the menu button's ready flash.
