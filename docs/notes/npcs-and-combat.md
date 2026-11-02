# NPCs and combat

Notes moved from `CLAUDE.md` (its index lists every notes file). Code comments citing `CLAUDE.md "<section>"` mean the section or bullet of that name here.

## NPCs and combat

Research: `Reference/research/npc_traffic_ai.md` (+ `Reference/tools/npc/`: `npc_tables.py orbit <station> <level>`
simulates an orbit's traffic) and `ship_combat.md` (+ `Reference/tools/combat/combat_tables.py`). Plain C#: `Hitpoints`
(pools), `Standing`, `NpcTables`, `Route`, `TrafficPlan`; MonoBehaviours: `Traffic` (level), `NpcShip` (one ship),
`PlayerHealth` + `CombatRadar` (player), `Crate`, `Explosion`; HUD `CombatView`. `Target` is the shared hittable object
(asteroids, NPCs, the player), `GunRig` the shared gun visuals. Collisions: `CollisionVolume` (plain C#), `Obstacle`,
`PlayerCollision` (see "Collisions" below).

- **Traffic** (`Level::createMission`, random per visit; remake #45: a capital ship's point is rolled again while its
  volumes would touch the station's, `TrafficPlan.ClearOfStation`: Valadon's hull had the battleship through it in 17 %
  of rolls): local fighters of the system race (security + rnd(2) + freighters/4), 0-1 jumpers (relaunch from the
  station every 10 s, fly off after 20 s), 0-4 freighters (fly game +Z at 1 u/ms, unarmed), raiders (90/65/35/10 % by
  security; 75 % pirates, else the system's enemy race; one model per group). Var Hastra: no freighters or jumpers.
  Special orbits: 102-104, Loma, systems 32/33 pirates only; 100, 101, 108, 10 empty. Every 45 s dead local fighters
  relaunch from the station and a destroyed raider group comes back (max 2 waves, security 0 / 1). `TrafficPlan.Build`
  keeps the original's order: the Mido rule (system 15 before campaign 16), 4 locals checked before the special orbits
  (102-104 keep escorts), 100 / 101 / 108 / 10 zeroed without an early return, the S'kolptorr and 0x2a / 0x2b rules,
  Loma and the loot orbits (pirates, Extreme rnd(3) + 2n; loot orbits double hull, speed 3.5, around the player), the
  supernova system zeroed after the local check, the pirate ambush on arrival (the raiders' point moved toward the
  arriving player; how far is lost in the decompile, the remake takes a quarter of the way, `TrafficPlan.AmbushShare`:
  half the way put up to 19 Extreme pirates on a player within a minute of arriving, a player's report 2026-10-16), the
  carrier box (±40000, ±20000, 100000 + rnd 80000), Specters around one shared point. In free flight Specters target
  only the player (`ConnectPlayers`).
- **NPC stats**: hull only, `4·campaign + 14·rank + 20` (freighters ×5, Extreme ×2), hit cube ±1000 units (±650
  Extreme); one gun per fighter: 4 bullets, 16 u/ms, 3000 ms, reload `600 − 2·campaign`, damage 3..22 by rank, the
  race's projectile and shot sound. Engine loop per ship: sound 46 = the FEV sound definition `Engine_Enemy_01`, one of
  `Engine_09` / `Engine_newnew_05` / `02` / `06_mixdown` / `03` at random; freighters 47 = `Engine_Freighter_03` / `02`.
  `Session.Rank` comes from `Session.Xp` (`Status::getXP`: ore / 50 + kills + wingmen hired / 3 + cores + 2 · freelance
  missions + campaign index + visited stations).
- **Aim spots** (remake, the user's request 2026-10-22; the original aims at the centre): NPC fighters and turrets aim at a
  spot on the target's model (`Target.AimSpot`: up to 24 vertices of each readable mesh, not the additive / light / glow /
  LOD layers, kept on their part; else the hit boxes' inner corners), a new one every 1.5-3.5 s and with each new target
  (`NpcShip.AimAt`); fighters turn and fire at it, the break-off range still to the centre; turrets lead by it.
- **Performance** (2026-10-22, 20 fleet battles at once, ~860-930 NPC objects: 82 -> 43 -> 31 ms a frame in the Editor): a
  fleet battle fighter's nearest pick is held 0.4-0.8 s (`NpcShip.NearestHoldMs`; every fighter scanned every enemy's hull
  every frame) and skips a ship whose sphere can't beat the best so far before the engine's checks (`Target.BroadSphere`:
  `MayContain`'s sphere, measured once a frame); the NPC turrets' pick the same (`TurretTargeting.boundFactor`, 0.3 for the
  turret hunters' x0.3); a shot looks only at the targets near it (`TargetGrid`: once a frame `Target.All` filed by those
  spheres into 250 m / 2500 m hash grids, the biggest in a list every shot sees; the first in the gun's list wins as the
  walk found it; lists under 24 and Edit mode walk the list, scatter guns too); avoidance tests a per-frame bounding sphere
  first (`Obstacle.MayTouch`); weapon effect animations pose only while seen (`PartAnimation.onlyWhenSeen`, from
  `GunRig.EnableFades`: `Renderer.isVisible`, always the first frame after a restart or enable); GunRig keeps what it set
  on each projectile (shown, scale) instead of asking the engine; other ships' exhaust particles only within 4 km of the
  camera (`ShipExhaust.RemoteReach`: under 4 pixels past it, the engine glow stays; 590 emitters cost ~4 ms); the HUD's
  markers project with the camera's matrices read once a frame (`CombatView.CameraFor`). Left (ms a frame): NpcShip.Update
  ~8 (guns 3, gun visuals 1, steering 0.8, avoidance 0.7, turrets 0.7, targeting 0.4), the HUD's markers 1.7 (style writes
  for every ship's marker), PartAnimation 1.4, rendering ~4, physics 2. Not done: Burst jobs for the bullets' hit tests and
  a `SpherecastCommand` batch for the hull casts (~1.5 ms between them).
- **AI** (`PlayerFighter::update`): patrol the box in front of the station; attack anything inside ±50 000 units
  (hostile ships the player first, neutral / friendly ships the race-hostile ships: pirates / Void vs everyone, Terran
  vs Vossk, Nivelian vs Midorian); turn toward the target at `dt·48/65536` (like a handling-100 ship, no inertia); fire
  inside a ±0.0076 cone within ±35 000; circle away inside ±8000; boost 5 % per 5 s or after losing 40 %; re-roll the
  target every 5 s. Same-race ships never target or hit each other. Remake option (Options > Gameplay "Smarter NPC
  targeting", `Settings.SmarterNpcTargeting`, off by default since 2026-10-18 (#121: players found the fights too easy;
  it was on), a session rule; players' report 2026-10-17: a raider group arriving ignored the patrols beside it and all
  seven shredded the player, since the player is first in every enemy list): a fighter takes the nearest target in its
  box (`NpcShip.UpdateSmartTargeting`, rules in `NpcTargetRules`, plain C#): the player and its sentry guns while it is
  hostile to the player, counted at 0.7 x their distance, else the ships of a race hostile to its own (the hull's
  nearest point); a target that already has 2 + the difficulty rounded up attackers (Easy 2, Normal / Hard 3, Extreme 4)
  is passed over while another candidate in the box is free; the target is kept between the 5 s re-rolls while it can
  still be taken; with nothing in the box the original's rules (its route, a non-hostile ship's far race-hostile pick).
  Left to the original: story and freelance mission orbits, a script's only enemy (`SetOnlyEnemy`), mission ships, a
  turned ship (it goes for the player who shot it), the Specters, the fleet battles' fighters (their own nearest pick),
  wingmen, turrets, the multiplayer remote players' pick. Off = the original exactly.
- **Turned ships pick at any range** (`PlayerFighter::update` 0xf131e / 0xf1444, `NpcShip.TakesAtAnyRange`): a turned
  ship (`turnedEnemy`, Player+0xe0) of any race but the pirates' takes the player outside its ±50 000 box when it picks
  (not attacking, or the re-roll's random tries); the 5 s re-roll still drops a pick outside the box, and as it leaves
  the attacking flag set, the ship flies its route (the default patrol) until a re-roll finds the player in the box.
  `Player::setAlwaysEnemy` (0xaf206) sets +0xe0 too: a level script's call is `NpcShip.SetAlwaysEnemy`;
  `SpawnSpec.alwaysEnemy` sets only the always-enemy flag (2026-11-01; see "Supernova add-on", 158).
- **Relations**: standing axes Terran/Vossk and Nivelian/Midorian (new game 30 / 0), hostile beyond ±70, pirates / Void
  always. Kills: standing −5 with the race (a pirate: +1 toward the system race), kills stat for hostile ships. Friendly
  fire on system-race / attack-race ships: 33 % of their hull → "Hold your fire!", 50 % → that ship turns, 66 % → the
  whole race turns and the station remembers it (next visit ≥ 7 hostile local fighters, "He's back!"). Radio texts show
  as HUD messages.
- **Damage** (`Player::damage`): shield → armor → hull, no reduction (armor is a second pool). Player: hull =
  `ships.json armor`, shield attr 18 (regen: full in attr 19 ms, ≥ 101 ms ticks, no delay), armor attr 20, repair bots;
  invulnerable during the launch / arrival camera and jump scenes; a non-hostile NPC's stray hit does 20 %; touching an
  asteroid destroys it and costs 20. Hit feedback: camera shake 1000 ms, sounds 25 / 23 / 24 by layer, red shield icon
  500 ms, blue / red hit arcs 300 ms. Hull / shield / armor persist between levels; docking repairs (assumed) and
  autosaves.
- **Damage smoke** (`ShipSmoke`, `Reference/research/prologue_particles.md`): below 33 % of the hull an NPC fighter
  trails smoke (`sprite_smoke`, alpha) and fire (`sprite_fire`, additive), the prologue wreck's records 15 / 42; off
  when repaired, burning through the death tumble, off at the explosion. The player has none. **Death burn**
  (`ShipBurn`, `PlayerEgo::explode` / `PlayerFighter`): record 9 SET_EXPLOSION (sprite_explosion, 8/s, 700 ms, 100..1100
  +500/s) from the first dying frame through the tumble, then record 11's one particle (2000..3000, 1500 ms;
  `emitManual` writes one) at the explosion; the player's tumble is the original's fixed +0.03 rad per 30-fps frame on
  each Euler axis. **Exhaust particles** (`ShipExhaust`, `Level::initParticleSystems`; the player only): one system per
  slot-3 anchor (scale = its `turretAngles`), particles.png cell by ship (table 0x252ba0), one per 8 units, 80 ms, ×(1 +
  0.5 ramp) while boosting, grey by the cloak, off with the engine glow; each particle's backward speed (4000 k u/s) is
  set at emission along the nozzle and kept in the world (`IParticleSystem::emit` takes the emitter's matrix only then;
  #114: a local-space velocity swung the plume round with the ship, so it never bent behind it in a turn); matched by
  eye to a screenshot of the
  original (the Inflict on the chase camera): each particle's quad is 1.3 × its size and the exhaust has its own copy of the
  particles material at `_Glow` 7 (the shared one 2.5: the original's gamma-space additive blending saturates the plume
  to white, the linear pipeline doesn't), so the plume is the original's bright three-lobed flame and covers the engine
  glow's dashed ring (before: thin, dim plumes with the glow discs showing). Remake (#85, `ShipExhaust.PlumeSetback`):
  the plume starts a quarter of a particle's size
  behind the anchor: from the side or a rear corner the newest camera-facing quad lay over the engine housing (the Dark
  Angel's); half the size cleared it but uncovered the glow rings in the chase view. Each particle's cell is copied out
  of particles.png into its own 128 px render texture (clamped, its own mipmaps, `ShipExhaust.CellMaterial`): the glows
  are the atlas's top row with the trail strips right under them, and with the fx atlases' mipmaps a UV inset only held
  at full size, so lines showed under the plume. Only the cell's middle is copied (`CellInset`: 10 texels off each side,
  the quad shrunk to match): Android (OnePlus 7T report, 2026-10-24) still drew a thin line under each plume, as its Low
  preset loads textures at half size and ASTC 6x6 blocks straddle the cell's edge, so the copy's bottom row took half the
  strip (the png's row 127 already holds the strip's soft edge). NPC engine trails don't exist in this build (the `Trail` class is never
  constructed). Remake option (Graphics, "Other ships' engines like yours", `Settings.NpcPlayerEngines`, on by default):
  NPC ships use the player's engine system instead, the `*_engine_glow_add` mesh and these exhaust particles
  (`ShipExhaust.AttachRemote`); so do the hangar flights and the bar flybys (`StationLevel.SpawnShip` attaches the
  particles, `scaled` so the plume shrinks with the flight's ship, following the glow; `HangarFlight.UsesPlayerEngine`;
  the player's own ship always: some hulls' `_engine_add` showed no flame there, the VoidX's was blue where the player's
  is purple); ships without a glow mesh (the battleship, static objects) keep their `_engine_add` (39 / 41 have one in
  the remake: the original skips their player engine child, its resource table lacks 17939 / 17941, but the OBB's meshes
  map onto v_ship_engine_glow.png's green Vossk glows; `patch_resources.py` registers them with material 34815, so the
  player flying the S'Kanarr / K'Suukk sees them too). The engine glow materials (34813 / 34815) bloom at 3.5, the other
  additive layers 2.5. Materials in `CombatAssets`.
- **Death**: NPC fighters tumble 1.5-3 s, explode (`Explosion`: camera-facing blast + debris, sound 18/19, camera rumble
  within 30 000 units) and drop a race container with their cargo (2/3 carry some; 60 s); while a fighter with cargo
  tumbles (`PlayerFighter::update`: KIPlayer+0x48 = cargoAvailable()) it has the crate markers (`Radar::draw`: the
  bracket 0x4f2 near, the diamond 0x4f1 far, the box off screen; a dying ship without cargo has none;
  `NpcShip.DyingWithCargo`; remake option, Options > Gameplay "Salvage markers on dying ships",
  `Settings.DyingCargoMarkers`, on by default: off hides the markers, the salvage lock stays) and is a salvage target:
  the tractor makes its crate at the ship (`TractorBeam::update`, `CreateStealCrate`) and the capture takes the whole
  first cargo entry with no crate left at the explosion (`KIPlayer::captureCrate`, `NpcShip.LootTaken`); a crate still
  being pulled when it explodes becomes its crate (the remake had no marker and no salvage for dying ships until
  2026-10); freighters play their wreck animation then a ×6 explosion. The player: camera freezes, explosion at 3 s,
  "Game Over" at 8 s, "Tap to load last savegame." after 7 s more (remake: it stays on, the original blinks it) → the
  last docked state (the auto-save slot, `SaveGame`), or the main menu without one.
- **Radar** (`CombatRadar`, only with a scanner): ship lock in the crosshair box after the scanner's attr 29 (sound 26),
  sticky until the ship dies or another lock completes; homing missiles use it. The original takes the first ship of the
  list in the box, any faction, and any completed lock replaces the old one (`Radar::draw`), so a neutral crossing the
  box stole the lock and the missiles; remake (players' feedback; Options > Gameplay "Target lock": Smart (default) /
  Original, `Settings.OriginalTargetLock`): hostile ships first, then the one nearest the crosshair, and (the hold, its
  own option since a player asked for the two apart: Options > Gameplay "Keep enemy lock", `Settings.KeepEnemyLock`, on
  by default, shown only with Smart; off, a neutral ship can be locked and scanned while an enemy is locked) no neutral
  or friendly candidate while a hostile ship is locked and alive (rules in `TargetLockRules`, plain C#); a crate in the
  box beats the ship already locked and the non-hostile ones (#76: the locked enemy kept the tractor off the container
  it dropped), a new hostile ship still comes first. **Unlock target** (remake, the same suggestion; the original has
  none: only the ship's death or another lock ended one): the "Unlock target" binding (U, the controller's B: in flight
  B is only the menus' back, the flight's presses wait meanwhile, `Navigation.PressesBlocked`), or on touch a tap on the
  lock plate (pickable only while a ship is locked) drops the ship lock (`CombatRadar.Unlock`, "Target unlocked"; the
  missiles homing on it fly straight on, `WeaponSystem.HomingLock`), and that ship is no candidate again until it has
  left the crosshair box (`CombatRadar.Released`, both lock modes), so the priority doesn't take it straight back and
  any other ship there can be locked; the HUD's key hints show UNLOCK while a ship is locked. A ship (or salvage) in the
  crosshair box beats the asteroid lock: it suspends and drops it (`Navigation.ShipLockActive`, `Radar::draw` 0x1574c0),
  and a locked asteroid never stops a ship lock (only the approach / mining does, `isDockingToAsteroid`; the remake had
  it the other way round until #27). Crates: salvage lock (ring after 500 ms, tractor attr 24), the beam pulls at 10
  u/ms and captures within 400 units (the first cargo entry, capped to free cargo); without a tractor beam "No tractor
  beam." (540; remake, #45, like the drill: once, the ring drops and that crate is ignored until it leaves the crosshair
  box). **Full hold** (remake, #176; the original's crate was gone with its loot): with no room nothing is taken and the
  crate stays in space ("Cargo hold is full.", 322; no standing lost; ignored like the 540 case until it leaves the box;
  a living ship's steal takes nothing), the auto modes and extra beams leave crates alone while the hold is full (a mod
  blueprint's data crate needs no room); with room for part of it the rest stays in the crate (`keepRest`: single
  player; in a session it is lost as before, the other players' copies hold the loot the crate spawned with).
- **HUD** (`CombatView`): shield and hull/armor bars top-left, the selected secondary "<name> (<amount>)" on the plate
  0x4c2 at the bottom centre in every input mode (its text
  centred below the plate's top frame line, `.touch-plate-text`: the interface rework's body size sat at the top, a
  player's report 2026-10-22) (`Hud::draw` redraws it opaque while the PC version's mouse steers; hidden at 0 ammo and
  in the turret view; remake: no speed / boost readout; a tap / click cycles); remake (#81 / #82), the **weapon strip**
  (`TouchControls`, `.sec-strip` in FlightHud.uss): with two or more secondaries mounted, a row of their shop icons with
  the ammo (the selected one framed amber, an empty one dimmed): holding the touch secondary button 350 ms fans it out
  left of the button, a slide onto an icon and lifting picks it (no shot; lifting elsewhere picks nothing; the hovered
  name above it, a light haptic per icon), a short tap still fires; any other change of the selection (G / D-pad right,
  a tap on the plate, the quick menu's list) shows it for 2 s (touch: at the button; a plate tap and keys / controller:
  above the plate), its icons taking a tap / click meanwhile; not in VR; the touch throttle gauge 0x548 under the
  crosshair (2 s fade) also shows in keyboard / controller flight when the throttle moves (`PlayerEgo::draw` calls
  `drawThrottle` in every mode); the faint radar ellipse (image 0x4c7, the top-left quarter drawn four times mirrored
  around the centre, 657 x 491; remake: x900 / 1080 in the phone layout (`FlightHud.RadarScale`, the markers and hit
  arcs on it too), whose 900-high panel it overflowed, so the off-screen dots at the top / bottom weren't seen
  (2026-10-16, players' report); not while drilling: `MGame::OnRender2D` skips `Radar::draw`); HUD messages
  (`MiningView.ShowMessage`, `Hud::drawEventQueue` / `updateQueue`, ship_combat.md 7.7) one at a time for 4 s of game
  time (a queue of 20; remake: a text already showing or waiting isn't queued again; remake, a player's report
  2026-10-17, `HudMessageQueue` (plain C#): the cargo scan readout (`CombatRadar.ReadCargo`), the "Target: X" notices
  (`Navigation`, `Mining`) and cargo gains (captures, the mining payout, gas clouds) are transient kinds
  (`HudMessageKind`): a new one takes the plate at once from any transient one showing and drops a waiting one of its
  own kind, so ship after ship scans without a queue; normal messages (warnings, mission / story texts, "Hold your
  fire!", hold full) still queue and are never cut short, and a transient one doesn't jump ahead of one waiting; gains
  within 1 s of the first (or still waiting) merge into one line, the same item summed, at most 3 parts: "8t Gold, 1t
  Gold Core") on the plate 0x4c3 at the top centre, y −8, or 56 while the lock plate shows (the original's 42 + the
  remake plate's 14 px lower position), white / red / green / orange; remake: with keys or a controller the booster's,
  the cloak's and the time extender's shop icons under the status bars show their recharge (`CooldownView`: a
  left-to-right sweep, the part not yet recharged see-through, opaque when ready, a flash then; while it runs the lit
  part drains with what is left, a cyan edge; touch shows them on its buttons); ship markers red / green / yellow: off
  screen a dot on the radar ellipse, far a dot (ring + distance when locked), near a hull bar (+ bracket when locked);
  remake (players' request, 2026-10-17: the far dot hid the ship), Options > Gameplay "Far ship markers"
  (`Settings.FarShipMarkers`, per player): Original (default) / Ring (the lock ring's arcs with the centre dot cleared,
  `GoF2Hud/ship_ring_open_*` from `Reference/tools/hud/make_open_rings.py`) / Small dot (the dot x0.6, 26 px above the
  ship), a locked far ship in either the faction bracket around it (`FarShipMarkers`, plain C#; off screen the
  original's dot / ring in every style), and "Distance on ship markers" (`Settings.MarkerDistance`, off by default): the
  distance (`Navigation.FormatDistance`) under every far on-screen marker, 16 px in the faction's colour
  (`.combat-label--far`), the locked one's as the original's; each label is written only when its text changes
  (`FarShipMarkers.DistanceKey`); a mod's `textures/<name>` replaces any of `CombatView`'s GoF2Hud images by name
  (`ModTextures.Hud`, read when the image is first drawn: the markers, lock ring frames, status bars, hit arcs; not the
  crosshair, navigation markers or touch controls); crate markers (the hit arcs, centred on the radar ellipse, are kept
  inside the safe area: the 374-unit top / bottom images reached past a 1080-unit screen's edge,
  `CombatView.ArcLimits`); lock plate "<race> NN%" with the race icon; hit arcs; the orange crosshair 0x4ce for 200 ms
  on a player bullet's hit (Level+0x30), the player turrets' hits included, aimed in the turret view or on auto fire
  (`Level::createGun` makes the turret guns player guns too, case 0x23; #48, then a player's report 2026-10-16).
- **Music** (`Radar::draw` 0x157c6c hostile counter, scanner only; active ships only, so a level's script-held ship
  doesn't count until it wakes; `Traffic.UpdateMusic`): 0 hostile ships → the calm track (`Traffic.CalmClip`: the alien
  orbit / a Void-attacked station 145, campaign 1 143, Kaamo 146, 101 147, the supernova system the mission target's
  2241 (< 0x6a) / 2242 else 148, the deep science orbits 10 / 100 152, else the system race's), 1-2 / 3-4 / 5+ →
  Space_Battle_Low / Medium / Full (136 in the alien orbit, at an attacked station and at campaign 0x10; 151 for an
  uncovered criminal, 149 / 150 for Specters). A battle track that plays is kept until the orbit is calm (only 151 takes
  over). Hostiles also block fast-forward. Docked: station 10 at 0x9f plays 144.
- **Music at 102** (the dropships): the level plays 0x8c0 over its opening cutscene, then hands the music to `Traffic`
  (`Radar::draw` 0x157c6c: the calm track at the target is the 2241 loop, and the attacking Specters' 149 / 150 replace it,
  since the loop isn't in the battle set); the level kept its loop through the attacks.
- **Music in a level's cutscene** (`Traffic.UpdateMusic`): the track isn't switched while the cutscene flag holds, but its
  fade-in goes on: a level in its cutscene from the start (the Supernova's "Meanwhile..." orbits: 152 Deep Science at
  Thynome, `MGame::OnInitialize`'s `playMusicAndFadeOutCurrent(1)`) started its track at volume 0 and kept it there.
- **Music under conversations** (`Navigation.MusicPaused` / `SyncMusic`; `MGame::pauseSounds` 0x1a8304 =
  `FModSound::pauseAllPlayingSoundFXEvents`, effects category only): a conversation, hint, map or the autopilot menu pauses the
  sound effects and engines but not the music (the remake paused the whole listener); only the pause menu stops the music too
  (`MGame::OnTouchEnd` `pauseAllPlaying`). The flight music sources (`Traffic`, `CampaignLevel`) ignore the listener pause.
- **Mesh collision** (remake, 2026-10; an option with three modes, Mixed by default, below; `HullCollision` +
  `HullBody`, Unity physics): every ship (NPC, player, other players, proxies, mods' glTF ships), station, jumpgate,
  turret and story / static object gets MeshColliders on its own LOD 0 meshes (not the additive / light / glow layers)
  on layer 24, under a kinematic Rigidbody on the model's root (no simulation is needed: the queries sync the transforms
  once a frame); the hull models are imported with Read/Write for it (`AssetImport.IsHullModel`: ships, stations,
  jumpgates, turrets, misc; a mod's glTF is readable), each mesh cooked once (`Physics.BakeMesh`). Shots
  (`Gun.TestHits`): a 3 m sphere cast along each shot's path this frame hits the nearest hull whose target the gun may
  hit (stations' hulls have no target: shots pass, as the original), so a turret in front of its hull takes the shot and
  gaps let shots through; scatter guns keep their proximity cube for fighters but burst on a big object's real hull
  (`Gun.BigTarget`: hit boxes, or a hit radius over 2000 units like the story's static objects; 102's carrier, +-6000,
  had a fuse cube up to 900 m across), asteroids and crates their cube; the original's +-1000-unit (50 m) cube around a
  fighter is gone, so near misses miss. The player's ship: a probe sphere (a third of the model's largest extent, 3..60
  m) pushed out of every landmark's / freighter's / other player's hull it overlaps (`PlayerCollision.CheckHulls`,
  `Physics.ComputePenetration`; it only resolves while the sphere's centre is outside, which flight never crosses),
  sliding along it, flying through its gaps; fighters stay pass-through. An obstacle without volumes (a mod station's
  `"collision": "none"`) has no hull contact either. From a contributor's precise-collision version (2026-10-11,
  ported): the probe is also swept from last frame's place each frame (`PlayerCollision.SweepHulls`,
  `HullCollision.Sweep`: half the probe as a sphere, which a ship resting on a hull doesn't start inside, and the centre
  as a ray; the nearer stop wins, not over 300 m, a teleport), so a fast ship (boost, fast-forward) can't pass a thin
  part between frames (checked: 792 single-frame paths through a jumpgate's structure all stopped at the entry face, the
  paths through its opening untouched); a part that can't be seen (renderer off, scaled to nothing, faded out by
  `extra`: `HullCollision.Shown`) stops neither shots nor the ship; the main menu's backdrop builds no hulls
  (`HullCollision.Suppressed`); the asteroids' cube is grown by the probe; Kothar's station exploding (0x50) or damaged
  collides by collision 1005 (`PlayerStation::PlayerStation`; the remake used 100, `OrbitBuilder.KotharDamaged`). NPC
  avoidance keeps the volumes below. **Ship collision option** (Options > Gameplay "Ship collision",
  `Settings.ShipCollision`: `SessionRules.CollisionExact` / `Mixed` / `Original`; a session rule carried by two bits,
  `PreciseShipCollision` = Exact, `OriginalCollision` = Original, neither = Mixed, `SessionRules.CollisionOf` /
  `WithCollision`; server option `-shipcollision exact|mixed|original` / setting `shipcollision`, the old
  `-precisecollision` and a saved `precisecollision` on = exact; the old toggle's PlayerPrefs key `preciseShipCollision`
  is read until the mode is set; players' report 2026-10-17: the fighters were hard to hit on their real shapes, then
  the user: "Original" read as if the big objects had the original's collision too): Exact is the above; Mixed (default)
  leaves the flyable ships' hulls out (`HullBody.flyableShip`, set by `HullCollision.Attach`'s 4th argument: an NPC
  fighter, not a freighter / fixed object / turret, and `HullCollision.FlyableShip`: not 13, 14, 15; the player's own
  ship; another player's; a NetProxy whose model is a ship that leaves no wreck): shots hit those on the original's cube
  again and the player meets another player's ship on its sphere volume, while freighters, the battleship, capital
  ships, stations, gates, turrets and objects keep their hulls; Original (Android's default, 2026-10-22) leaves every
  hull out, and none is built while it is on
(`HullCollision.Attach` notes the model and builds it when the mode changes: no cooking at spawns, no bodies to sync): the
original's volumes everywhere (`CollisionVolume` obstacles, the cubes and hit boxes of `Target`). `HullBody.Collides` =
`HullCollision.Takes(flyableShip)` (the mode read once a frame, `HullCollision.Mode`), so `Target.HasHull` /
`Obstacle.UsesHull`, `Cast`, `Sweep` and `PlayerCollision.Solid` skip what the mode leaves out; the hulls are still
built, so switching applies at once. Original not run in Unity yet. Measured (Editor, a fleet battle of 39 ships, A/B in
one session): no frame-time difference (4.5 ms both), physics 0.06 ms fixed step + 0.08 ms sync per frame; the fleet
battle spawns in 156 ms with its hulls.
- **Collisions** (`PlayerEgo::calcCollision`, ship_combat.md 2.9; the remake's mesh collision above replaces this for
  the player and shots): the station, the visible jumpgate and freighters are `Obstacle`s with the original's
  axis-aligned bounding volumes (`CollisionVolume`, never rotated). Inside one, the player is put on the nearest face /
  the sphere surface each frame (two passes) and keeps flying, so it slides along; camera shake, no damage. The player
  flies through fighters and fighters through each other; touching an asteroid destroys it and costs 20. Off during the
  launch / arrival camera, the jump scenes and mining; the gate is skipped while the autopilot flies into it. The
  autopilot to the station also docks on touching it (`MGame::dockEvent`). Volumes: stations from `collision.json` by
  index (no entry: Vossk 1000; 109 / 110 static 2002), centre (-a, c, b) with the station's (0, pi, 0) turn baked in,
  box half (|d|, |f|, |e|), sphere r |d| / 2 (alien orbit x0.9 / x0.4); the gate a sphere of its radius (7500 / 11250)
  with the cube +-radius as contact; freighter boxes from `Level::createShip` constants (also the bullet hit boxes);
  after the death animation the wreck stays put (state 4) with `wreck_collisions.json` boxes (battleship 0 x2, Midorian
  1, Nivelian 2, Terran 3, Vossk 4; x1.1 / 0.6). The wreck mesh takes the hull's transform as it is
  (`AEGeometry::setMatrix`: same heading, the battleship's x2; checked against all five intact models). NPC fighters
  (§5.7): inside the first landmark's, then the first ship's volumes they turn away from the volume's centre (`dir += (away - fwd) * speed * 0.03`)
  with an extra step. Verified in Play mode: station 78 / 80 volumes match the models, sliding, docking by contact, a
  freighter carrying the player on its bow, the wreck boxes, the gate sphere, a fighter leaving the station.
- **Radio chatter** (`Traffic.Radio`, `Level::createRadioMessage`): friendly fire, alarm, "He's back!", pirate base and
  hint lines show in the flight HUD's radio box (the campaign radio wins) with a random face of the race
  (`AgentGenerator.CreatePortrait`) or the Pirate Boss (speaker 9), name 1597 + image, 2 s delay, `lines·2000 + 1500`
  ms; blocks fast-forward. A new line replaces the pending ones (the queue is cleared) and the face is always a male
  one; kinds 0 / 1 are skipped at the story target. **Alice's Void chatter** (`SetupVoidChatter`,
  `Level::createRadioMessage(8)`): after the Valkyrie add-on, in the alien orbit before campaign 0x93, one of eight
  voiced Alice / Keith talks (tables 0x2541c8 / 0x2543a0). **Loma's toll** (`SetupLoma` / `UpdateLomaToll`, radio kinds
  9-0xd, 448-456): the pirates ask cargo value × 2 / 5 / 10 / 20 % (448, #P / #C) in a ChoiceWindow (`PauseMenu.Ask`,
  the game paused); paid = neutral pirates for the visit (`Session.LomaTollPaid`), refused or a pirate hit = hostile.
- **Docking fine** (`StationMenu.CheckDockingFine`, `ModStation::OnInitialize`; never at a station the pilot's own
  multiplayer faction holds, `NetFactionsClient.LocalHolds`, players' feedback 2026-10-22): an enemy race's station asks |standing|
  / 100 · 2800 ± 100 (205), a station whose forces the player attacked rank · 150 + 1000 (206), ×10 Extreme; No =
  straight back into space, not enough credits = 203. Not after a load (`ModStation::setGameLoaded` +0xac,
  `Session.ArrivedByLoad`, set by `SaveGame.Load`; #131: the fine came at every load of a save made there). The attack
  is also forgotten once the station falls off the original's 3-station stack (`Session.RecentOrbits` / `ArriveInOrbit`
  from `SpaceLevel`, `Status::addStationToStack`: the flag lives on the stacked Station object, a station pushed off is
  reloaded clean): 3 new distinct orbits later; revisits don't reorder; not saved (after a load the stack refills before
  anything is forgotten). The race's standing itself stays (an enemy race's ships stay hostile after a 205 fine).
- **Station dialogs and Esc / B** (`StationMenu.Back`): the original's ChoiceWindow can't be dismissed (`ModStation::OnKeyPress`
  ignores every key while one is open), so back = its No button or a message's OK and the dialog's action always runs (Esc on
  the docking fine used to close it unpaid). Remake: with hangar flights the station's conversations, fines and medals wait
  1 s after the landing (`ArrivalSettleMs`); meanwhile, until the docking checks have had their first go, the Hangar,
  Lounge, Map, Launch and the system menu wait too (`StationMenu.ArrivalPending`, #37 / #46: the checks don't run while
  the hangar window or system menu is open, so opening one first traded and saved without the fine, or sold a loaner
  before its conversation).
- **Signatures** (items 189-192, `Standing.SignatureRace`): the race is a friend (+100), its rival an enemy, the others
  neutral whatever the axes say; friendly fire on that race (33 % / 10 % Extreme of a hull) or any race (50 % / 25 %)
  removes it (324, delict 100). Mission bonuses use `Standing.RawToward`.
- EMP-disabled NPCs stop moving and show the lightning of records 17 / 18 (`EmpSparks`, khador_jump bolts); remake:
  their engines go dark meanwhile (`NpcShip.UpdateEmpEngines`: the glow / `_engine_add` parts off, so a mod ship's
  flames too, the scripts' `SetExhaust` kept; the other players' copies from `NetProxy.empDisabled`), the exhaust
  particles already stopped; near ships have an EMP bar under the hull bar while EMP < max. The player's EMP
  (`NpcShip.OnPlayerEmp` / `OnPlayerDisabled`): a Wanted ship attacks back (`attackWanted`); a system-race ship past a
  third of its EMP turns hostile; disabling one alarms its race and is a delict. NPC-vs-NPC EMP from the gun item's attr
  10.
- **NPC guns** (`NpcTables.GunDamage`, `Level::assignGuns`): campaign 4 → damage 1; Wanted (freelance type 6, not
  friends) and the Challenge rival rank + base at speed 28; Void ×2 at 0x10, else ×0.8; 0x31-0x34 / 0x38 → 5; turrets at
  0x50 ×1.7; at 0x46 the non-wingmen fire the Disruptor (183) ×2.5; Specters ×0.7; campaign 7 pirates ×0.5; hull 270 at
  49-52 (`IsHull270Mission`). NPC guns start loaded; a stray hit on the player does ×0.2, ×0.75 while docked at an
  object (`ObjectDocking.PlayerDocked`: fighters circle at ±12000 and hold fire while the player is above them).
- **Fighter turrets** (`Traffic.AttachFighterTurret`, `Level::createFighterTurrets`): ships 45 / 51 carry
  `turret_002_static` (invulnerable, untargetable, gun 22 ×0.5) at (0, 172.25, −460.7) / (0, 470, −83), synced with the
  host (`NpcShip.SyncTurret`). Untargetable: no HUD marker and no ship lock (the remake's ship loops missed the flag:
  the Rhinos at 102 had a second green dot and the lock jumped between the two), no hull (`HullCollision.Detach`: radius
  0 in the original, the mesh had stopped the shots at the Rhino), and it dies with its host (`DestroyAsTurret` clears
  `invulnerable` first: it stayed in space). Turrets, the player's auto turrets and sentry guns don't aim at a cloaked
  ship, a cloaked Specter included (remake: `handleTurret` reads Player+0x5e, which only the player's cloak sets, so the
  original's turrets kept shooting cloaked Specters; see "Specters"). NPC **sentry guns** 211-213 (`sn_sentry_gun_00N`);
  at 0x9e damage ×1.5 · 0.3, speed ×1.2, hull ×5.
- **Wreck explosions** (`WreckBurn`, `PlayerFixedObject::update` 0x17f6b4): a freighter or fixed object dying gets an
  explosion with fire streaks at once (sound 20) and record 22 SET_EXPLOSION_CARGO (23 SET_EXPLOSION_BATTLESHIP for the
  battleship and the Pirate Outpost) on the wreck until its animation ends; record 24 is level 80's deep science
  station. 140 ms into the death the wreck mesh takes its damaged material (`MaterialCreate(0x8248 + kind)` +
  `MeshChangeMaterial`: the charred `*_dmg` textures, 33352-33357 for the Midorian / Nivelian / Terran / Vossk
  freighters, the battleship and the pirate outpost; `CombatAssets.wreckIntact` / `wreckDamaged`, `ApplyWreckDamage`; a
  multiplayer copy's hull takes it too); the remake kept the intact textures on every wreck until 2026-10-11 (a player's
  report at Kaamo). The outpost wreck uses volume id 5 (`CollisionVolume.ForWreckId`); crates wait for the wreck
  (`Crate.DelayExpiry`).
- **Space junk** (the freelance Junk removal): `Target.RadarObjects`, radar dots and lockable after the ships; 10 %
  leave a container of 1-10 t Space Waste (99).
- Wanted criminals, NPC cloaking (Specters): see "Supernova add-on".
- **NPC rockets** (`NpcShip.MakeGun` / `SetSecondaryGun`, `Level::assignGuns`): rocket / missile items (sorts 4 / 5 /
  40) become RocketGuns (speed 8, 10 000 ms, reload 3000; sorts 5 / 40 home on the ship's current target after 1000 ms,
  `RocketGun::seekEnemy`); a second gun slot toggles every 20 000 ms (`PlayerFighter+0x2e0`): the Wanted flying ships
  45-48 (G'liissk x4), Harval at 157 / 158 (item 7 x3 + Shesha x4).
- **Missile warning and evasion** (remake; the original has neither): any homing missile (Missile, ClusterMissile,
  Thermo kinds) locked on a boosting or cloaked player loses its lock for good and flies straight on (`Gun.Update`:
  `Bullet.lockLost`, `Target.ShakesMissiles` = `boosting` || `cloaked`, set by `ShipController` / `PlayerCloak` for the
  local ship and by `NetPlayer` from the owner's boost / cloak for the other players; a lock that went away while its
  ship was cloaked (the radar dropping it, an NPC's `HomingTarget` going null) shakes them off too, so they never home
  again on the next lock or when the cloak ends), whoever fired it: NPC rockets, the capital ships' salvos, other
  players' missiles (their real missile in the shooter's game and its mirror in the target's); NPC ships never evade.
  `Gun.LockShaken` -> "Missiles evaded!" (green, at most every 1.5 s). The missiles homing on the local player
  (`IncomingMissiles`, reported by `Gun.Update`, so mirrors count) drive `MissileWarningView` in the flight HUD: a
  pulsing red banner "INCOMING MISSILE" (with the count) over the boost key and "BOOST TO EVADE" (only with a booster,
  `FlightModel.HasBooster`), a red diamond on each missile (off screen on the radar ellipse; not in VR), and a two-tone
  beep made in code, every quarter of the nearest missile's time to impact (110..650 ms); hidden in cinematics, the
  launch camera and death. Multiplayer: `NetPlayer` writes its boost back to exactly 0 when it ends (it stuck just above
  0, so the others kept seeing a boost); `Gun.LastLock` goes with a shot (`NetShotSender`), so a capital ship's salvo is
  mirrored at its own target; a capital ship picks and hits the other players it is hostile to (`NpcShip.RemotePlayers`
  / `HostileToRemote`, `NpcShip.HitTargets`, a stray hit x0.2); an NPC's mirrored shots pass through its own side
  (`NetProxy.SameSide`: the battleship's missile copies "hit" its own turrets' copies at the launchers, the warning
  flashed for a frame). Rocket / missile / cluster missile hits emit record 11 SET_EXPLOSION_MANUALLY_BIG at the bullet
  (`GunRig.ShowImpact` -> `ShipBurn.ManualBurst`, the original's rocket impact; before, no rocket hit showed anything).
  Verified in Play mode with a hostile Vossk battleship's salvos: the banner, the markers, a boost with three in flight
  (all lost, "Missiles evaded!"), the bursts. Multiplayer (Editor host, Windows build client): the host's salvo locked
  on the client and its boost value synced; the NetProxy.SameSide fix is not yet verified in a build.
- **Cargo stealing** (`CombatRadar`, `TractorBeam::update` / `KIPlayer::captureCrate`): an EMP-disabled ship with cargo
  (`NpcShip.HasCargo`) in the crosshair box is a salvage candidate, not a ship lock (same ring, the beam's attr 24);
  locked, a container of its cargo appears at the ship (`CreateStealCrate`, createCrate(0)) and is pulled in; the
  capture takes rnd(amount) of the first entry (at least 1, capped to the free cargo) off the ship (`StealFrom`),
  `Standing::applyStealCargo` (delict 2), a friend's cargo sets `Traffic.FriendCargoStolen` (Level::stealFriendCargo,
  Objective 0x13); the beam lets go when the ship dies. Multiplayer (Discord report 2026-10-21: only the player whose
  game ran the orbit's NPCs could steal; the others' radar took an EMP'd NPC, another game's `NetProxy`, for a plain
  ship lock): a proxy is a steal candidate too while its owner reports it disabled and flying with cargo (`NetProxy`
  publishes its first cargo entry, `Target.NetStealable`, `TargetLockRules.RemoteStealable`; a mission ship's only for
  the mission's team); the crate is made in the stealer's game (`Crate.stolenFromRemote`, never shared: `Crate.Stolen`)
  and the capture's units leave the owner's ship through the server (`Target.RemoteSteal` -> `NetProxy.StealUpRpc`: from
  that orbit, while disabled, at most what it holds -> `StealRpc` -> `NpcShip.StealFrom`). Single player was checked in
  Play mode in both lock modes and with another enemy held: it steals as described; the proxy path with a stand-in proxy
  Target (ring, crate, capture, the units through RemoteSteal), the server round trip not yet with a second game.
  **Tractor auto modes** (attr 23, `Radar::Radar` +0x1aa / +0x1ab): 1 (AB-3 Kingfisher) the nearest crate on screen at
  once, 2 (AB-4 Octopus) any crate at once, even off screen and on the autopilot; crates only, never ships. Remake, for
  mods: `tractorTargets` (attr 122) pulls several crates at once without the lock (see "Mods", A modder's requests).
- **Disruptor vs shields** (remake, a players' request 2026-10-16; its description says it destabilizes shields, which the
  original never made true): a player weapon's hit on a shield counts x `Gun.ShieldFactor` (items.json stat `shieldDamage`,
  attr 124, a percentage; the Disruptor Laser 183 and items based on it 200), the part the shield doesn't absorb going on
  to the armor and hull at its plain value (`Hitpoints.Damage(dmg, shieldFactor)`, `Target.Damage`, `RemoteDamage` and
  `NetPlayer.HitUpRpc` carry it, clamped 0.01..10, 1 for an NPC's shot); the NPCs' generic guns (step 0x46's Disruptors
  too) stay at 1, and the NPCs have no shield, so it shows in multiplayer between players; the item window's row "Damage to
  shields" (`ItemInfo.ItemStats`, Extra `statShieldDamage`).
- **Static-object volumes** (`Level::getBoundingVolume` in `createStaticObject`, `SpawnSpec.collisionId`,
  `CollisionVolume.ForStaticObject`: centre (a, c, -b), boxes x1.2, spheres x0.6, never rotated): pirate outposts 1002,
  Valkyrie 1003, the mining plant 2000, the plasma array stages 2001, 0x5279 2002, the secure containers 2003, the
  carrier 2005, the Vossk battleship 2006, the burning Luur platform collision.json 111; none for the wrecks, the junk
  field or cargo_001. Remake: the object being docked at doesn't block its own approach (`PlayerCollision`), so a ship
  starting beside it isn't pinned on a hull face.
- **Scanner cargo readout** (`CombatRadar.ReadCargo`, `Radar+0x1a5`): with a scanner of attr 31 = 1 (Ecoscan / Proscan /
  Ultrascan) a new ship lock within 24000 units per axis shows the ship's first cargo entry ("<n>t <item>", white; a
  capture is green) or "Nothing to salvage." (542) when it carries no cargo list.
- **Voices of generic speakers** (`GenericVoice`, `Globals::getDialogueSoundId`): the text -> voice table 0x255210
  (`Resources/GoF2Data/voice_table.json` from `Reference/tools/dialogue/build_voice_table.py`), else the agent's GENERIC
  set by race / gender / face (Terran male / female, Vossk, Nivelian (Midorians of body 2), Multipod, Bobolan, Grey,
  pirates) and the text's line: the radio chatter (426-431, 445-447, the pirate base 435-440, the Nivelian hints 443 /
  444), the freelance briefing / success / failure / return (370-389) and the wingmen's goodbye (313). The GENERIC bank
  is in the story assets (**GoF2 > Build > Story Assets**). A set without the recording stays silent (Vossk 313).
