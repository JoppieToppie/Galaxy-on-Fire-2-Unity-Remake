# Space scene

Notes moved from `CLAUDE.md` (its index lists every notes file). Code comments citing `CLAUDE.md "<section>"` mean the section or bullet of that name here.

## Space scene

`Space.unity` holds only a camera, two directional lights, post-processing and `SpaceLevel`, which builds the current
orbit at startup from `Session.StationIndex` (new game: station 78 Var Hastra, ship 10 Phantom, `Status::resetGame`).
Research: `Reference/research/space_level_setup.md`, `space_backdrop.md` (+ `space_backdrop_sim.py`), `space_props.md`.

- **One level = one station orbit.** Station at the origin, the other stations of the system are planets. Visible
  jumpgate only in the system's `jumpgateStation` orbit.
- **Empty orbits** (`OrbitLayout.IsEmptyOrbit`, `Status::inEmptyOrbit` 0xb8ee8; no docking, no station target): 102-104,
  109, 110, 132-134 always, 111 (Luur) above index 0x5d, 101 (Herjaza, the Valkyrie's orbit) from 0x54 (the
  battlestation is in the Void then), 78 at index 0 / 1 (the prologue, the rescue, a menu backdrop rolled there); the
  station's model stays in 27 / 110 / 111 (`Level::createSpace` 0xbc0f6, `OrbitLayout.stationObject`). Free play and
  sessions read index 20.
- **Stable layout:** the original's RNG is `java.util.Random` (`JavaRandom`), seeded per station: jumpgates and sky
  rotation `2 * station` (separate sequences), sun/planets `300 * station`, asteroid count/centre `station`.
  `OrbitLayout` reproduces the research tables exactly. Everything the original randomises per visit uses
  `UnityEngine.Random`.
- **Rotations:** game `setRotation(x, y, z)` = Rx*Ry*Rz; `OrbitLayout.RotationToUnity` mirrors it and undoes the
  import's 180 deg yaw, so game (0, pi, 0) (stations, gates) = identity and an undocking player faces Unity -Z.
- **Sky:** stars `systemIndex % 3` + nebula `system textureIndex`, rotated per station (`_SkyRotation`); it is
  `RenderSettings.skybox`, so it also gives ambient light (`SkyReflection.Update` = DynamicGI.UpdateEnvironment wherever
  the sky is set). The
  environment reflection is the system's reflection cubemap, as the original binds it (`MGame::OnInitialize` /
  `ModMainMenu::OnInitialize`: `TextureCreate(0x2efe + textureIndex)`, the alien orbit 0x2f08, `ChangeCubeTexture`):
  `Resources/GoF2Reflections/cubemap_skybox_000..018` (glossy convolution) as Unity's custom environment reflection
  (`SkyReflection.SetSystem`, from `OrbitBuilder.SetupSky` and `StationLevel`'s lighting; a mod system its template's). No
  realtime probe (both quality levels have them off). `GoF2/Hull` and `GoF2/EmissiveCube` sample it as the global
  `_GoF2ReflectionCube` (set with it, PR #106): `unity_SpecCube0` came out empty in the Android build (Forward on the
  Mobile renderer). **Hangar cubemaps** (remake, PR #110, a departure: the original never binds 0x2ef4..0x2ef7): while
  the hangar is shown its race's own (`SkyReflection.SetHangar(HangarIndex)`, `Resources/GoF2Reflections/cubemap_hangar_terran
  / vossk / nivelian / midorian`, glossy convolution; the Supernova systems too), the Valkyrie hangars (7 / 8) a black one
  (no reflection, the rim stays), a mod's own hangar and the lounge the system's.
- **SRP Batcher** (2026-10-22): every shader the scenes draw is compatible, `GoF2/SpaceSky` too (its per-material values in
  `UnityPerMaterial`). A renderer with a MaterialPropertyBlock falls out of it: `PartAnimation.ownMaterials` (GunRig: the
  projectiles, muzzle flashes, impacts) writes their fades into a copy of the part's material made once instead (a fleet
  battle: SetPass calls 244 -> 114); the rest (stations, rooms, sky layers, explosions, pooled by material) keep the block.
- **Fog in builds**: Graphics > Fog Modes is Custom (Linear / Exp / Exp2 kept, `m_FogStripping: 1`, PR #106): on Automatic
  the fog variants were stripped, since no scene has fog on (the game turns it on from code).
- **Hull reflection** (`GoF2/Hull` = `Assets/Shaders/Hull.shader`, a copy of URP 17.7's Lit.shader with one change:
  ForwardLit's fragment is `HullForwardPass.hlsl`'s, URP's own plus the original's `BumpRimCubeShader` terms from the
  fragment source in libgof2hdaa.so: + cubemap(reflect) x clamp(smoothstep(0.6, 1, 1 - N.V), spec^2, 2 spec + rim) +
  rim x spec^2 x rimColor, spec = the specular map, `_MetallicGlossMap`'s alpha; sampled with the GPU's mip choice; x (1 -
  metallic)): the ships, stations, turrets and objects with a specular map (165 materials, `AssetImport`), not the hangar /
  bar rooms or the sky props (plain URP Lit, the original's have no cube reflection); globals `_GoF2HullReflection` (1) and
  `_GoF2HullRimColor` (3 x `DAT_00258d34[sysTex]`, the sun colour table but systems 11 / 15, `StarSystem::initLight`).
  URP Lit alone reflected ~4 % (a dielectric): the hulls had none of the original's shine. The mods' material templates
  (`ModAssetsBuilder`) use it with `_GOF2_PBR`, which leaves the GoF2 terms out (2026-10-17, the user's report: mod ships
  milky white in the hangars): a mod's `_MetallicGlossMap` alpha is PBR smoothness over the whole hull, not the original's
  sparse specular mask (the game's 39 ship maps: mean 0.21, mostly 0; GoF3's hulls 0.3-0.6 everywhere), so the terms laid
  the bright hangar cubemap over every panel on top of URP's own reflection. In a GoF2 hangar (`SkyReflection.SetHangar`;
  `SetSystem` clears both for space and the lounge) the mods' PBR hulls also lose 70 % of their indirect light
  (`_GoF2PbrIndirectDim`, through URP's occlusion: they have no occlusion map) and take their main light from above, the
  camera's left and toward it instead of the key light's camera side (`_GoF2PbrLight` / `_GoF2PbrLightColor`:
  `HullForwardPass` takes the main light's term out and puts that one in; `SkyReflection.HangarPbrLightToward` /
  `HangarPbrLightStrength`): front-lit the PBR hulls had no shading, the original's textures carry theirs painted in.
  Matched by eye to GoF3's own render of the Cronus (thumbs/ship_terran_020); the room and the game's ships are unchanged.
  `ModAssetsGuard` puts the keyword back before a build (else the variant is stripped). A mod's hangar / bar goes back to
  Lit (`ModMaterials.PlainLit`; it enables the material's keywords again after the shader switch, which dropped
  `_EMISSION`, so every mod room lost its glow until 2026-10-24). Tests `ModHullLightingTests`. Measured: no frame-time
  difference (a 4K frame filled with hull, on, off and plain Lit within ±0.1 ms). When URP is updated, copy its Lit.shader
  again and redo the two marked lines. Anything that picks hull renderers by shader name accepts "GoF2/Hull" (the cloaks,
  the star map). Its materials keep the MotionVectors pass on (2026-10-28): URP's LitShader editor, which the shader
  borrows, doesn't know "GoF2/Hull" and turns the pass on whenever the Editor loads one; `AssetImport` turned it off, so
  every Play mode session, batch run and build rewrote ~170 .mat files.
- **Sun/planets:** quads 1000 m from the camera, drawn at the far plane (`GoF2/Backdrop`; with the sky layers drawn by
  `BackdropPass` right after the skybox, LightMode `GoF2Backdrop`, so they are in the opaque texture the cloak and the shield
  bubble refract: in URP's transparent pass the cloak showed black over a planet); the sun at the texture's own
  brightness (additive, like the original), only its near-white core lifted into HDR (×1.6, `_CoreGlow`) under the
  remake's bloom; sun swells + streak near the screen centre (the supernova system, system 27 before 0x9e: the sun keeps
  game +x as its up and its size (also at Tadram 113 / Naneroh 109, whose sun slot 6 lies 0.02 deg off game x: a float
  "parallel" test gave them the camera's up, so it rolled with the view; `Backdrop.SupernovaSunRotation`), and a
  sn_sun_011 glow of 0.3 × the sun + the swell rolls with the camera and makes the streak; `StarSystem::render` with
  +0xc). Modified (#187): the streak (lifted by `_CoreGlow` like the sun) adds over the sun's disc per channel at most
  what the sun leaves below white (`GoF2/Backdrop` `_SunInStreak`, applied before the lift: the original adds in gamma
  space into 8 bits, where the white middle saturates; in linear HDR a line brighter than the sun ran through it); in
  the supernova system only against the glow quad (the sun under it has its own texture and a world-fixed roll the
  uv mapping can't follow, so the streak can still pass its white there); no swell or streak in the station
  (`Backdrop.NoSwell`). Planets mirrored so their lit rim faces the sun. Mission 0 (the prologue and the main menu's
  backdrop after `Status::resetGame`) halves the orbit planet unless a texture size rule replaces it; no camera zoom
  on the orbit planet in the planet ring orbits and the alien orbit; the alien orbit has its sun straight ahead and
  `planet_void_big` at yaw 5.2347 (60 deg to the right), unseeded size; K'ontrr (system 20) swaps 58's and 62's planet
  textures like the original (textures by the stations' file order, roles by the system's list).
- **No URP shadows** in space or the hangar (`OrbitBuilder.SetupLights`' sunShadows, `StationLevel.ApplyHangarLighting`;
  the Space Lounge keeps its room's): within URP's 50 m shadow distance the player's hull shaded itself as a photo
  camera came close (a player's report, 2026-10-17); the original draws none, the hangar has `HangarShipShadow`.
- **Lights:** LIGHT0 toward the sun (`clamp(15 * sunColour, 0, 2)`), LIGHT1 from the orbit planet (Unity +Z), linear fog
  in 6 systems. Camera: vertical FOV 1.22 rad, near 1 m, far 15 km, chase offsets (0, 600, -1338) / (0, 600, -650) game
  units.
- **Main menu background** (`MenuBackground`, like the original: `ModMainMenu::OnInitialize` 0x1a46dc, CutScene(2),
  Level type 2): `Status::resetGame` (`Session.ResetNewGame`; not while the ending is pending) and a random main-game
  station 0..99 (`Galaxy::getStation(nextInt(100))`; the ending keeps its station) on every menu entry, its orbit built
  with `OrbitBuilder` like a flight level, with the ordinary free-flight traffic (`Traffic` / `TrafficPlan` / `NpcShip`
  on the empty mission: patrols in the box in front of the station, raiders fighting the locals, freighters 1-4 km
  beside it, the static Terran battleship, jumpers, respawns) and the station / gate volumes
  (`OrbitBuilder.AddObstacles`) the fighters turn away from; the player exists but inactive at the origin (an
  untargetable dead `Target`). Camera (`MenuCamera`, CutScene::process mode 2): fixed at (rnd(20000) − 20000, 0,
  rnd(60000) + 40000), yaw from −π/4 by dt·5e-5 (a turn every ~126 s), fov 0.92, near 200, far 200000; the station is
  only in view for part of each turn. Remake: full-detail station; the stations are 1-5 km across (Tornard 57 reaches
  3.4 km out in front), so a camera spot inside or against the hull backs straight out (along game +z) until it is clear
  by a third of the station's size, at least 800 m (for the nearest spots about half the stations; at most ~5 km out);
  no asteroid within 150 m of the camera, measured from its surface (the biggest reach 390 m from their centre, Void
  crystals 670 m; a centre test put the camera inside one); no capital ship (battleship, carrier, Vossk battleship, a
  fleet battle's) with the camera inside or within 60 m of its boxes: its point is rolled again
  (`TrafficPlan.MenuCamera`, `AtMenuCamera`; a placement put the menu camera inside the battleship's hull); no traffic
  music under the menu theme (`Traffic.MenuBackdrop`).
- **Docking / launch:** within 16000 units of the station (`PlayerEgo::collidesWithStation`), the HUD offers **Dock**
  (tap, F / Enter, or controller X). It only appears after the player has left that range once, because the undock spawn
  is inside it; the original requires the autopilot instead. Inside the range Dock beats a lock on the station or on a
  planet behind it (`FlightHud.DockBeforeLock`, #24: the action after the Dock prompt showed was a planet jump when that
  lock completed meanwhile, which looked like a launch). Docking loads `Station` straight away with no animation, like
  `MGame::dockEvent`. Launching from the station sets `Session.LaunchedFromStation`: a fixed camera 9000 units ahead
  watches the ship fly past for 7 s (`LevelScript`), then the chase camera eases in. The player has no control meanwhile
  (`ShipController.inputLocked`, weapons blocked); any key, button, stick, click or tap except Esc / Menu and the flight
  menus' keys (Q / E / V / K, View, D-pad left) skips it (`LevelScript::skipSequence`: the fly-in ends like its natural
  end, the chase camera eases in; with the launch camera option off it snaps); the menu keys do nothing until the fly-in
  ends (players' report: the menu opened under the hidden HUD and its pause looked like a frozen game). Same for the
  arrival fly-in. Meanwhile the HUD, the radar and its ship markers are hidden, only the orbit information shows
  (`MGame::OnRender2D` skips `Hud::draw` / `Radar::draw` while the fly-in holds LevelScript's cutscene flag;
  `.hud-launch`).
- The HUD has one action prompt (tap, F / Enter, controller X): Autopilot / Jump / Autopilot off (see "Navigation"),
  Mine / Abort / Stop mining (see "Mining"), else Dock.
- Music: the system race's space track, battle tracks by the number of hostile ships (see "NPCs and combat").
- **Lens flare** (`LensFlareView` in the flight HUD, under the HUD; `StarSystem::render2D` / `LensFlare::render2D`): 7
  images of `gof2_interface.png` (1288-1290 → `GoF2Hud/flare_0..2`) on the line centre → sun at t 0.5 / 0.25 / 0.75 /
  0.125 / 1/11 / -0.75 / -0.2, alpha 70 + I (40 + I), plus a full-screen glare of alpha I, tinted by the system's flare
  colour; I = 64 (80 Ginoya) · (1 − d / (H/2)) (`Backdrop.FlareIntensity`, also the sun swelling); shown while the sun
  projects in front within −W..2W / −H..2H, no occlusion. Sizes assume a 768-high canvas (64 px images). The alphas are
  raised to 2.2 (`GammaAlpha`): the original blends in gamma space, the linear-space UI turned the glare's 25 % white
  into a 54 % grey.
- **Extra sky layers** (`SkyLayers`, `GoF2/SkyLayer` far-plane shader, assets in `Resources/GoF2Backdrop/SkyLayerAssets`
  from **GoF2 > Build > Sky Layers**): camera-centred, world-aligned meshes in the original's order: planet ring sky
  (stations 120 / 126 / 130 / 132, before the sun and planets), supernova flares (system 27, mission ≠ 89 and < 158,
  turned by the nebula's sun-aligned R_sky so the fire streams out of the supernova, the nasty texture from 106, ×1.5
  speed above 106), storms (mission ≥ 90 in system 27 or nebula 16 / 18, a new random rotation each loop), asteroid belt
  (systems 24-26, lit by LIGHT0, clamped to LDR). The storm / flare parts use `PartAnimation.applyMaterialChannels`:
  `extra` = opacity (`_Fade`), `v5_0` = UV scroll (`_UVOffset`, 100 = one texture width, assumed). Modified (the user's
  picks): the flares fade from 100 to a steady 75 instead of the sidecars' 50 (`SkyLayers.FlaresFade` on a copy of the
  sidecar before `PartAnimation` reads it; the converter's `.gof2mesh.json` stays as written), and their low-poly UV
  sphere (12 rings x 24 segments) is subdivided onto the sphere at load (`SkySphereSmoothing`, 3 levels, midpoints
  shared per edge; u and colour on a spline through the rings' values, v from the angle around, the seam kept): no
  facets, and the scroll's speed changes smoothly between rings instead of jumping at every row. Its meshes need
  Read/Write (`AssetImport`). Tests `SkySphereSmoothingTests`.
- **Wormhole** (`Wormhole`, landmark 3, `PlayerWormHole::update` / `PlayerEgo::calcCollision`, decoded this time, not in
  the research files): exists until the game is won; visible when coming out of the Void, at the station the Void attack
  (`Session.VoidInvasionStation`) or in the alien orbit, index < 43; random spot (rand(80000) − 40000, rand(40000) −
  20000, rand(40000) + 40000), radius 40000, turned to the camera (+0.5 on the direction's x, a slight tilt); its two
  layers spin clockwise about the facing axis (a turn per 20 s / 10 s; the old rotation map turned it backwards). Timer:
  grows 3 s, open 60 s, shrinks 3 s, then gone or (alien orbit / attacked station) reopens elsewhere (alien orbit x
  ±(30000..90000), y 20000..60000, z −60000..−100000; else ±(20000..60000) per axis; index 29 / 41: that × 2.7 from the
  player); the mission lock keeps it open at index 40 (not alien) and 42 (alien). Pull (`PlayerCollision`): visible, not
  shrinking, within 40000: loop sound 34, the ship moves toward it by (40000 − d) / 256 units per 30 fps frame, camera
  hit; within 1000 = inside. The ride (`SpaceLevel`, `MGame::OnUpdate`): an active campaign mission advances first
  (index < 41, not 29 / 40; 40 only after Errkt's freighter went through, its hull carried into 41), entering early at
  29 / 40 / 41 kills the player (remake: during a level's cutscene then, the wormhole neither pulls nor takes the
  player, `SpaceLevel.WormholeHeld`; at 40 until Errkt's freighter is through it doesn't pull at all, `WormholeNoPull`:
  Errkt's call comes 40 s in wherever the player is, the cutscene following him drew a player near the wormhole in, and
  its pull near the centre outruns any ship; flying in still kills), 42 in the alien orbit is the level script's; then
  into the alien orbit (this station remembered as `Session.VoidReturnStation`, Status+0x84) or back out to it, arrival
  as a stream-out with the wormhole 10000 behind the player closing after a second (`LevelScript` ctor 0x16056c). HUD:
  545 "Wormhole" near the centre, icon 0x450 (`GoF2Hud/wormhole_icon`) elsewhere, never locked.
- **Alien orbit** (`Session.VoidOrbit` = station −1, the Void's home, `OrbitLayout.alienOrbit`): the Void station
  (`station_void`, collision 1001, plus the remake's 400 m boxes for the outer blades and lower spire the 1001 volumes
  miss at its held 13.6 km pose, `CollisionVolume.VoidStationExtras`, #37; battlestation after the Valkyrie add-on,
  collision 1003 as the original's `PlayerStation::PlayerStation` (the remake gave it 1001); none at index 43-83 / ≥
  154), sky `nebula_010` + `stars_002`, the orbit planet `planet_void_big`, Void asteroids around (−30000, 0, 30000)
  (Void Crystals), fog tint 0x9274d4, arrival at (0, rand(20000) − 10000, rand(50000) + 170000) facing the station; the
  station is lockable as 415 "Void" (distance only, no autopilot, not in the menu), no docking, the Khador Drive
  straight back to `Session.VoidReturnStation` (the Khador Slipstream flies in and out too: star-map.md "Interstellar
  space") unless a mission blocks jumps (`MGame::UseKhadorDrive` 0x1a9480 has no
  Void rule; the remake refused it through the main story until #26); traffic 2+ Void fighters (`TrafficPlan`); music
  145 / 136 (`StoryAssets.voidMusic` / `voidBattle`).
- **Void invasion** (Status+0x7c / +0x80): at the attacked station (and coming out of the Void, not at index 42) 2-5
  Void raiders from the wormhole and ≥ 2 freighters; every 45 s (10 s at index 41) dead Void ships come back, at the
  wormhole at the attacked station, around the player in the alien orbit and coming out of the Void
  (`Traffic.UpdateAlienAttackers`); from index 32 to 44 every 10th departure elsewhere re-rolls the attacked station
  (`Story.OnDepart`, a random visible system, not 10 / 15); −10 from 42 / 45. Void ships drop 1-3 t Alien Remains.
- The index-43 menu backdrop (the ending's) adds the beer / bra statics 0x37d0 / 0x37d1 (`MenuBackground.SpawnStatics`,
  `StoryAssets.menuStatics`). The original puts them at the origin (`Level::createScene`: the PlayerStatic position args
  are 0 in the machine code; `PlayerStatic::update` moves nothing), inside the station and too small to see (the beer
  ~11 m, the bra ~26 m, the camera km away); remake pick (`EndingDrift`): they tumble across the camera's view on a
  camera-relative path, 45-70 m out, 22 s per crossing, the beer from 6 s, the bra 11 s later, again and again. The only
  prologue sky exception is index 0 in a story level (`Level::createSpace`), already built.
