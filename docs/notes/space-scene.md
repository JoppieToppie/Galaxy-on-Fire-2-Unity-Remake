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
  **Size** (2026-11-16): `HangarCubemapCapture` captures 256 px faces (a 256 x 1536 strip, was 512); phones import every
  cubemap of the folder at 128 px faces (`ImportPostprocessor.ReflectionCube`, Android / iPhone Max Size 128: a cubemap's
  Max Size limits the face, not the strip).
- **Skies** (remake, 2026-11-14; Options > Graphics "Skies", `Settings.SkyStyle`, Original by default: the others invent
  detail; the user: "4 options. chorus enhanced on its own which is only a slight improvement whereas es2 is es2 styled then
  chorus and es2 combined should be the best of both worlds"): **Original**; **Enhanced** (below: the upscaled layers and
  sharp stars, and Chorus's way with a painted sky: its brightest part again as a sharper image patch); **EVERSPACE 2
  style** (as ES2 builds a location, "Nebula clouds" below: the sky sphere only ES2's clustered three-layer star field and,
  under the fuller skies, a faint sky-wide nebula; each bright region a distant 3D local nebula, local ones round the orbit;
  no painted nebula); **Combined** (Enhanced, ES2's faint star field behind GoF2's stars, ES2's local nebulae round the
  orbit). A first pass drew ES2 style as tinted masks and a band on the sky (fog walls: the user asked whether ES2 really
  looks so; it doesn't, its nebulae are 3D clouds). `World.GoF2SkyLayers.Build` makes each a layered sky (mods-world.md "Layered skies") with
  `"base"` (the game's nebula under the layers, its stars over them: `SpaceSkyCore` `Sky()` draws BaseNebula, ChorusSky with
  its star mask last, then BaseStars) except ES2 style; the layers need "Live mod skies" (else Enhanced's layers and stars
  alone). The remake's own assets (`Reference/tools/sky/sky_layers.py`, `Resources/GoF2SkyLayers`, named "gof2:<name>" by any
  sky's layers, mods' too): `star_mask.png` (tileable 4096, three gaussian star layers: 1800 bright, 7000 middle, 32000 faint),
  `nebula_masks.png` (tileable 2048 x 1024, domain-warped spectral noise: billows, laned filaments, haze), `patch_0XX.png`
  (each sky's brightest region from GoF2SkyHd's nebula upscaled 4x again from its 1024 shrink, sampled into the patch's own
  projection, ChorusLayer's mapping inverted; 1536 px over at most 40 deg, feathered alpha) and `layered_skies.json` (per sky:
  up to four bright regions by peak-finding on a smoothed panorama, their size, colour and brightness; the band's normal, the
  bright directions' least spread axis, and how flat they lie; main colour, coverage; the patch's heading / elevation).
  `GoF2SkyLayers.Toward`: a layer centred on a cube-frame direction w: heading atan2(w.x, w.y), elevation asin(w.z) / (pi / 2).
  Tests `GoF2SkyLayersTests`.
  **A start that hangs** (#371, 2026-11-27: EVERSPACE 2 style crashed a Realme RMX3370, Adreno 6xx class, Android 13, and
  every start after hung at the loading screen, the option out of reach; on an S24 Ultra, Adreno 750, it draws fine at
  60 fps): `Settings.GuardStartup` (Bootstrap, players only) marks each start (`startupPending`) and the main menu clears
  the mark after 180 frames (`Settings.StartupReached`); a start that finds the mark still set, with Skies not Original
  or Nebula clouds on, puts Skies back to Original and Nebula clouds off and logs a warning. Test
  `NebulaCloudsTests.AStartThatNeverReachedTheMenuPutsTheSkiesBack`. The crash itself not reproduced (no Adreno 6xx here).
- **Enhanced skies' layers and stars** (remake, 2026-11-14, the user's request to rebuild GoF2's skies "the same way Chorus
  does"; "Skies" Enhanced and Combined, `Settings.EnhancedSkies`): `OrbitBuilder.Enhance`
  puts `Resources/GoF2SkyHd` on the game's own layers of a sky (not a mod's images, not a Chorus sky): its stars_00X /
  nebula_0XX cubemaps (the same 2048 faces) and their star lists drawn by `GoF2/SpaceSky` (`World.SkyStars`, `SpaceSkyCore`
  `StarPoints`). `Reference/tools/sky/enhance_skies.py` makes them from `GoF2Sky`: every point star of the stars layers
  (~23 - 44 thousand each) and every speck of the nebulae (a 5 x 5 peak standing 10 and 8x its neighbourhood's grain over
  its 9 x 9 median, its light falling off fast; up to a few thousand in the dusty ones, 015 / 016 / 018) goes into a list
  (direction, gaussian size, amplitude) and out of the image (its ring's median in its place; what stays: the big stars'
  halos, the clouds); each nebula face is then shrunk to 512 (the art's real detail), padded with 32 px of its neighbours
  (the faces meet without a seam), upscaled 4x by Real-ESRGAN's anime model (`realesrgan-x4plus-anime`; the user picked
  it over `realesrgan-x4plus`, which came out waxy), trimmed and brought back to the original's colours at low frequency.
  The specks had to come out first: a 512 shrink averages a 1 - 2 px speck away. The stars are drawn as gaussians 0.6 of
  their source size down to 0.6 of a screen pixel, brighter by the area lost, at most 2x; found through a 512 x 512
  grid a cube face (two slots a cell, the brightest first; one within half a cell of a face's edge also in the edge cell
  beyond; a pixel reads its 2 x 2 cells), built at load per pair of layers (4 kept). Chorus's own sky is up to four placed
  image patches; GoF2's nebulae don't fit that (even the patchy ones cover 15 - 60 % of every face), so they stay cube
  faces. The crossfade between systems (`InterstellarField.LoadBlendSky`, `_BlendStarGrid`) and the Khador rift (its
  material copies the sky's) draw the other sky's stars too. Needs 32-bit float textures (`SkyStars.Supported`), else
  the original skies. Tests `SkyStarsTests`.
- **Nebula clouds** (remake, 2026-11-14, the user: EVERSPACE 2's sky methods in the base game, "this shouldnt require es2s
  files", then "a closer approximation" from ES2's decomp and shader dump): `World.NebulaClouds` (spawned by `SpaceLevel`
  after the sky layers, not between the systems), drawn by `GoF2/NebulaCloud` (its header: M_Nebula_Local_Parent's formula
  and defaults). How ES2 builds a location (read from its files): its sky level (Map_Skybox_S0XX) holds only the stars sphere
  (M_Skybox_Stars: Brightness 10, noise Param 2), a matte and in some sectors a sky-wide nebula (BP_Skybox_Nebula_Global,
  Intensity 0.2); `BP_EnvironmentGenerator.ProcessSpaceObjects` makes each nebula of the sector's map a
  `BP_Skybox_Nebula_Local` at its direction and distance (x SpaceObjectDistanceScale 2000) with its Radius, Seed, Color,
  UseSystemColor and Density, plus a volumetric fog field inside one ((1 - d / r)^0.5 x 0.05); a local nebula's construction
  script makes of 101 random points in its sphere those under HalfHeight 0.3 x radius x r^2 off its plane each a flat piece
  (SM_Skybox_Nebula_Local: a 5 km plane) at a random turn, scaled 1-3 x radius / 10 km, with random values and a mask channel.
  Here: near clouds (`NearClouds`: the option, or the styles ES2 style / Combined) 0-3 by the orbit's nebula sky's coverage,
  6-14 km (the far plane is 15 km) a few km out, seeded by the station; and in ES2 style (`RegionClouds`) each of the sky's
  bright regions (GoF2SkyLayers' layout) a distant cloud in its direction (the sky's turn), 45 km out, as wide on the sky as
  the region, its own colour; the material's system colour the sky's main colour. Pieces 0.25-0.75 x radius half-wide
  (ES2's). Light (2026-11-15, "fix the gaps"): ES2 lights its crushed opacity (a piece's mean 2.4e-4 with the remake's
  masks, 3.7e-4 with ES2's, measured) by Brightness 10000 under its auto exposure; here each cloud's light (its vertex
  colour, float) is set so its mean over its disc is its target, the region's own brightness (near clouds half the sky's),
  counting its pieces' overlap and their seen share at a random turn after the edge-on fade (0.268): ES2's exposure, per
  cloud. Colours: the regions' / the sky's main colours saturated 1.6x (GoF2's are pale averages, ES2's nebula colours
  vivid; 1.3x: 1.6 drifted mixed regions' hue). Colours (second pass): each region's two main hues (`sky_layers.py`
  `two_hues`: k-means by chromaticity, weighted by light; `colorA` / `colorB`): a distant cloud the heavier, its thin edges
  the other (per vertex, uv3; the average of 007's brown and teal region was a beige that saturated to yellow); a near cloud
  one of the regions' hues, its edges the sky's main colour (the material's `_SysColor`, ES2's system colour). Pieces dim
  from half the cloud's radius out (smoothstep), so a cloud fades out instead of ending at an edge. A piece fades within its
  own size of the camera (km-wide pieces filled the view as discs). ES2's fog inside a
  nebula: where the first near cloud's middle lies within its radius and the system has no fog of its own, the scene's fog
  in its colour, linear, fully hazed at 3 x its radius / (1 - d / r)^0.5 (6-40 km). The remake's own masks
  (`Reference/tools/sky/nebula_clouds.py`: tileable spectral noise, no detail under 12 px, smoothed, each channel's values
  mapped onto ES2's masks' distribution (their 5 % quantiles, numbers only: ES2's crushing curve shows only a mask's top,
  where ES2's have broad plateaus; a wider distribution showed as crumbly specks), before that each channel's mean
  brought to ES2's masks' ~0.5, which its crushing opacity needs) and `nebula_clouds.json` (each sky's coverage, brightness
  and three main colours); a mod's `textures/cloud_masks.png` replaces them (`ModTextures.SkyLayer`: the ES2 mod gives ES2's
  T_Skybox_Nebula_001 / 002 / 005). Compared in Unity renders, nebulae 005 / 007 / 008 / 016: with ES2's masks they read as
  ES2's do; the remake's come close. Tests `NebulaCloudsTests`.
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
  **The mods' features in the mods' variant alone** (2026-11-14, the user's "what can we do to improve performance for
  ios"): EVERSPACE 2's structure layers and rim, Chorus's layers, tissue, crystal, shell and far kicker, the glow wave,
  the lava and the hit flash are compiled into ForwardLit only under `_GOF2_PBR` (`#if defined(_GOF2_PBR)` round their
  calls in `HullPassFragment`): every mod material is a `ModMaterials` copy of a ModAssets template, all of which carry
  it, and the game's hulls never do, so the game's ships, stations and asteroids draw without that code (a mobile GPU
  sizes a shader's registers for its heaviest path, its branches taken or not). No new keyword and no new variant (a
  `shader_feature` set only on runtime materials would be stripped from builds). Wreck effects, the warp-in, decals,
  liveries and the sun's gamma tint stay in both: they reach the game's hulls. Tests `ModHullLightingTests.
  TheGameHullsVariantLeavesTheModFeaturesOut` (both variants compile), `ChorusLayersTests` (the Chorus, wave and lava
  materials carry `_GOF2_PBR`). Its gain is unmeasured: that needs a device.
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
- **Station scale** (remake, experimental, 2026-11-15, the user's request: "the stations are very small"; Options >
  Gameplay "Station scale (experimental)", `Settings.StationScale`, key `stationScale`: Original / 1.5x / 2x / 2.5x / 3x /
  4x / 5x / 6x, `OrbitScale.Choices`; not in the Game experience presets; single player only: `OrbitScale.Chosen` is 1 in
  anything of a session). `OrbitLayout.Build` stores the scale (`stationScale`) and the station's reach R
  (`stationReach`: the farthest point of its collision volumes + 15 %, a mod model 0.6 of its size, at least 10000); a
  layout is `Scaled` with a station object, a scale > 1 and outside interstellar space, else everything below is the
  identity. **Game coordinates stay the original's everywhere** (layout, story levels, traffic plans, event graphs); the
  map `OrbitLayout.Place` / `OrbitScale.Place` is applied where a station-anchored position becomes a Unity one
  (`OrbitScale.ToUnity`) and undone where a Unity position is read back for the original's coordinates
  (`OrbitScale.ToGame`). The map is radial about the station: r x F inside R (whatever stood against the hull stays
  there on the grown hull), eased between R and 2R, r + 1.5 (F - 1) R beyond (the orbit pushed out by the station's
  growth: distances along the line from the station stay, across it they grow by r' / r, e.g. 1.5x at 250000 units
  with R 30000 at 4x; a group far out keeps roughly its spacing, the gap between hull and gate / asteroids / a battle
  stays the original's). Not mapped: offsets from a ship or object, speeds, ship-to-ship and combat ranges; a distance
  from the station maps with `OrbitScale.Radius` (the dock range: `SpaceLevel.DockMeters`). `OrbitScale.Current` is the
  orbit in use (`SpaceLevel` and `MenuBackground` call `OrbitScale.Use`; the docked station scene clears it unless docked
  in orbit EVERSPACE 2's way). Structures grow by F about their own origin (`OrbitScale.Structure`, volumes
  `CollisionVolume.Scaled` / `OrbitScale.StructureVolumes`, points on their hull x F): the station
  (`OrbitBuilder.SpawnStation`, its volumes in `AddObstacles`; the Obstacle cube and the hull colliders follow the
  transform), the jumpgate (`SpawnJumpgate`, `OrbitLayout.JumpgateRadius` x F), the outposts, the Mining Plant, mobile
  stations, story platforms and wrecks; ships (capital ships too), asteroids, crates, mines, the wormhole and gas clouds
  keep their size. Each asteroid is placed on its own (`SpawnField`), so none lands inside the grown station. Also
  scaled: the fog's end, the flight camera's far plane (+ 3 (F - 1) R) and the menu camera's, the arrival poses
  (`ArrivalPose` / `PlanetArrivalPose`, Loma's + 40000 included), the undock point, Coromesk's start, a failed drive
  jump's spot. Coromesk (103, story, campaign > 0x54) counts as a station orbit with the Mining Plant at the origin
  (`OrbitLayout.originStructure`, its reach from static volume 2000). Per area:
  - `NpcShip` maps every spawn and route point itself (its `ToUnity` / `ToGame` are OrbitScale's): a `SpawnSpec.position`
    is always the original's, a spot taken from the scene goes back with `OrbitScale.ToGame`. `NpcShip.IsStructure`
    (`SpawnSpec.structure`, a station pad port, a fixed object named "station" (not "battlestation"), "platform" or
    "plasma_array") grows the model, its collisionId volumes, hit radius, wreck, explosion and docking points
    (`NpcShip.StructureScale`; its root transform stays 1); never call `OrbitScale.Structure` on such an NpcShip again.
    `SpawnSpec.placedOn` (and a capital part's host) puts a part at its host's spot + its offset, grown with a structure
    host (outpost turrets and guards, battleship turrets).
  - Story levels use `At(game)` (= `OrbitScale.ToUnity`) for absolute spots and keep `ToUnity` for offsets;
    `CutsceneCamera.LookAt(game)` stays the plain conversion (absolute cameras go through `LookAtUnity(At(...))`).
    Spots on a grown hull (Errkt's freighter on the mother ship's arm, the battlestation's turrets, 157's Valkyrie
    blasts) are hull offsets x Factor; 42's / 80's / 157's shots frame the grown hulls; 158's helper speed covers its
    longer path in the original 30 s.
  - Navigation: the gate landmark, the asteroid field and route / event waypoints mapped; `Navigation.ReachUnits` maps the
    fast-forward / lock stop distances from the station (Unradius) and the gate (/ Factor). The gate jump's shot (ship
    start, camera, trigger, back-out, speeds) is x Factor, timing unchanged. `Wormhole.SetPosition` maps, `GamePosition`
    gives the original's; its 40000 size and pull stay.
  - Events: `EventHost.ToGame` / `distance(...)` / `on near` read original coordinates; cutscene and order places map.
  - Free-flight traffic, freelance targets, loot, mines, gas clouds (their size kept), hazard zones, conductors (reach x
    the local stretch), mod lanes / pads / props map their points; Junk removal's time limit grows by the extra flight.
  - Expected at high scales: flights across the station take longer (73's approach about 3x at 4x; 91's wreck may break
    up before the player arrives, cosmetic); battlestation turrets, fires and blasts keep their own size on the grown
    hulls; the original's own NPCs started at the station (48's escorts) still start inside it and steer out.
  Tests `OrbitScaleTests`, play mode `StationScaleFlightTests` (five free-play orbits and the story spread at 4x: no
  error, the station 4x, nobody starting inside a grown structure).
