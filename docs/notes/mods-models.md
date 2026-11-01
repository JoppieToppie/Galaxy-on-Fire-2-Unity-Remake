# Mods: models and textures

Notes moved from `CLAUDE.md` (its index lists every notes file). Code comments citing `CLAUDE.md "<section>"` mean the section or bullet of that name here.

## Mods (remake-only)

- **Ship models** (`ModShips`, `ModShipBuilder`, `ModMaterials`, `ModGltfMaterials`; glTFast 6.20, `com.unity.cloud.gltfast`):
  each new ship's GLB loads with glTFast (asynchronous: `Preload` from the main menu, the menu's Leave and `NetGame.EnterWorld`
  wait for `Ready` / `WhenReady`, at most 30 s) into an AssembledObject template under a hidden DontDestroyOnLoad holder
  (the run-time port of PR #34's CustomShipBuilder: hull turned by modelYaw and scaled to modelLength (a skinned mesh
  baked into a plain one in its drawn pose first,
  `ModShipBuilder.FlattenSkinned`: the sizing, collision, shadows and the Mod Builder read the bind-pose mesh, which the
  bones drew elsewhere and at another size, so bone animations don't play; the GLB's node animations, parts moving on
  their own, loop: `ModGltf.LoopAnimations`, glTFast's legacy Animation at the default wrap mode played its clip once),
  engine glow discs at the
  slot-3 mounts on mat_34813 (a mount's `glowSize` [half width, half height, flame length] makes an ellipse, its
  `glowShape` another outline in that box: rounded / square / hexagon / diamond / triangle, `EngineGlowShape`, the
  2026-11-01 request of a modder whose flame clipped a wide nozzle; the sprite read along each point's direction, so
  corners take the rim's glow, and the exhaust styles take it as the nozzle's outline, `ModExhausts.Glow`; the builder's
  Hardpoints page: shape buttons, half width, half height, length; checked in Play mode on a .glb ship: round, square,
  hexagon, triangle and a 70 x 18 rectangle; a `glowColor`
  or the ship's `engineGlowColor` switches the glow to `ModAssets.engineGlowTint`, the sprite in white
  (`Resources/GoF2Mods/ModEngineGlowWhite.png`, Build Mod Assets; the glow sprite there made round and soft, its ring of
  dashes averaged away) tinted by vertex colour, white-hot at the centre, and
  `ShipExhaust` tints the exhaust particles with it, on the particles.png cell whose product comes closest and
  brightest), throttle glows from their masks (`ThrottleGlow` with its trails), one LOD culled at 80000);
  `AssembledObject.LoadPrefab` hands it out for pack "mod" (while loading the game's own model of that ship stands in,
  else the Phantom's, always the original prefab: `Database.ShipAssembly(..., original: true)`; a project replacing the
  Phantom's model looped Template / LoadPrefab into a stack overflow on the builder's Exit), `NetProxy` for an
  "Assembled/mod/..." path. Materials are always copies of the URP Lit templates in `Resources/GoF2Mods/ModAssets`
  (**GoF2 > Build > Mod Assets**, `ModAssetsBuilder`: opaque / cutout / transparent, with and without detail maps; the
  templates hold placeholder textures and a faint emission so URP keeps their keywords and a build keeps the variants;
  a platform switch can drop their `_EMISSION` (seen switching to Android), so `ModAssetsGuard` (Editor) puts it back before
  every build: without it Linux / Android shipped without the emission variants, mod ships' glowing parts dark): the
  GLB's own glTF materials (`ModGltfMaterials`; metallic-roughness converted by `Hidden/GoF2/MetallicRoughnessToGloss`,
  the material's `metallicFactor` / `roughnessFactor` baked in, also on the CPU path, per factor pair
  (`ModGlbImages.Image.mrFactors`, `ModMaterials.RoleKey`): they were taken as 1, and the GoF3 Ships mod's maps are all
  metal in B with metallicFactor 0, so its hulls were mirrors, black against space) or
  the entry's `materials` (`ModMaterials.FromSpec`: the PR's fields plus `doubleSided`). Textures load from the mod's PNGs,
  compressed with mipmaps. Normal maps (ships / stations / rooms `normal` / `detailNormal`, glTF normal textures, `*_normal`
  texture replacements) are loaded with `normal: true`: Android's normal map encoding is "DXT5nm-style" (Player settings),
  so URP there reads X from alpha and Y from green (`UNITY_ASTC_NORMALMAP_ENCODING`); the game's Normal Map imports are
  re-encoded by Unity, a mod's RGB PNG wasn't, and every mod hull came out nearly black on Android (and in the Editor with
  the Android target). On Android `ModMaterials.NormalToAlpha` copies red into alpha before compressing (the glTF ones by
  `Hidden/GoF2/NormalToAlpha`, `Resources/GoF2Mods`); desktop reads either layout and is unchanged. **A GLB's embedded
  images** (`ModGltf.Load`, used by ships, stations, rooms and weapon fx) don't go through glTFast: `ModGlbImages.Strip` takes
  them out on a worker thread (each pointed at one shared 1 x 1 PNG, the indices kept, the other bufferViews packed into a
  new BIN; not for several / external buffers or meshopt) and `ModMaterials.PreloadImage` makes each different image once per
  mod by its MD5 (`ModInfo.LocalImage` writes it out for the decoder and it is deleted afterwards; `ModTextureCache.FileForContent`),
  compressed like the PNGs, the role's conversion on the CPU before compression (metallic-roughness to metallic / smoothness,
  Android's normal layout), and `ModGltfMaterials` / `FromGltf` take those (glTFast's own texture and the GPU conversions only
  for an image not taken out). glTFast kept every model's images uncompressed, one copy per model, plus an uncompressed
  render texture per metallic map (and per normal map on Android): the GoF3 Ships mod (79 GLBs, 1452 images, 251 different)
  crashed phones (2026-10; measured in the Editor: driver memory 4.9 GB -> 1.3 GB, its textures ~195 MB of DXT, 84 ships
  10 s from the cache). Textures load once per key however many ask at once (`ModMaterials.Once`), at most 6 decodes at once
  (2 on phones, `DecodeSlots`), phones compress in Texture2D.Compress's fast mode; at most 8 ships load at once (3 on phones,
  `ModShips.ShipSlots`), each holding its GLB until glTFast has read it. Shop icon: the entry's PNG
  (`ItemInfo.ShipIcon`), else the Phantom's; the dialogue tints mod
  ship names (no sprite). Code that sizes a ship by its renderers must skip trails (empty at the origin): the item window and
  `NetPlayer` use mesh renderers only. Another player's ship built before the session's mods were on (they join before
  NetState applies them) is built again on `ModShips.ModelsChanged`.
- **How a ship is seen** (`ModShipView`, 2026-10-24; GoF3's ship files): ships.json `"camera"` { distance, height,
  lookHeight, lookAhead } game units (`ShipData.chaseCamera`; `ModShipView.CameraOffsets` in `SpaceLevel`'s camera
  set-up and `PlayerHull.FitCamera`'s ordinary path, the original's 1338 / 600 / 600 / -650 for a field left out),
  `"cockpit"` [x, y, z] (`ShipData.cockpit`, mount axes; `ChaseCamera.FirstPersonSeat` takes it, still behind the
  frontmost primary, not for the debug Big hulls), `"smoke"` up to 4 points (`ShipData.smokePoints`; `ShipSmoke` makes a
  smoke / fire pair at each, the rate and pool shared, the fire on the point) and `"explosionScale"` 0.25..20
  (`NpcShip.FighterExplosionScale`, mirrored by `NetProxy`; the player's death, `PlayerHealth`, and other players'
  ships, `NetPlayer`). The Mod Builder's ship page, **Camera and damage** (`ModBuilderView.BuildShipView`). The GoF3
  builder writes the cockpit and smoke from the hulls' `_cam` / `_damaged_NNN` helpers, the explosion from EffectScale /
  1.5 and the camera from the hull's length past 1400 units x CameraThirdPerson's distance (field 89702: 7 fighters, 8
  gunships). Tests in `ModContentTests`.
- **Engine effect** (`EngineFx`, ships.json `engineFx`, `CustomEngineFx`; the GoF3 Ships mod): Galaxy on Fire 3's engine at every
  exhaust mount in place of the glow discs (`BuildEngineGlow` without discs: the empty part marks the engines on / off) and the
  exhaust particles (`ShipExhaust` skips the ship), drawn as GoF3 draws it (next bullet). A mount's `fxScale` sizes it
  (GoF3's helper scale) and its `rotation` turns it (`ModHardpoints.MountTurn`; five Vossk hulls' engines 8.5-10 deg
  inward; an engine on the centreline keeps no sideways turn, the builder's `centreline_turn`). Removed 2026-10-24 (the
  user: "completely remove the imitations"): the remake's own imitation of GoF3's engines from PNGs, camera-facing flare
  and crossed cone cards (`coneProfile` / `coneFade` / `coneFlow` with `ModEngineFlow.shader`), a ribbon helix with a
  lightning texture and wavering ribbon streams, with per-part colours and the importer's tuning (`RACE_TRAIL`,
  `NOZZLE_FLARE`, `nozzle_tinted`...); old files' card fields are ignored. In git history before that date if ever
  needed. An engineFx still giving `glow` / `cone` / `spiral` / `trail` (a ship's, an exhaust style's) gets a mod warning
  naming the replacement (`ModContent.CheckEngineFx`: `meshes`, `trailParticles`; review of #168); a mod station's
  `engineFx` (`ModWorld.ParseMobile`, no mod to warn) isn't checked.
- **GoF3's engines as GoF3 draws them** (2026-10-24, the user's request: "decompile the GoF3 binary and use it";
  libgofm.so of the Android APK, Ghidra headless without auto analysis, `~/GOF2UnityRemakeMods/gof3_re`: the full
  analysis ran out of Java heap; the library is Thumb and Ghidra loads it at 0x10000). GoF3's resource ids
  (`ae::resource::getResourceId` 0x15fdad8): MurmurHash3_x86_128 of "/assets/<path>" with seed 1, the 64-bit halves
  xor-ed (importer `ae4_ids.py`), which resolves every texture a material names (the large flare's is
  `vfx_generic_lens_flare_000`, the core's `smoke_001`; before, "GUID only"). engineFx `"meshes":
  "fx/engines/<name>.json"` (+ `meshScale` 53.5, `meshGlow`; `Modding.EngineModel`, the importer's `engine_meshes.py`):
  each part of the engine scene (vfx/engines/<race>/vfx_engine_<race>_000, the capitals'
  vfx/engines/large/vfx_enigne_large_<race>) with its own mesh, place, textures, the options of the shader variant GoF3
  compiled for its material (read from the variant's GLSL in romfs shaders/: the material's own flags overstate them,
  e.g. the Vossk exhaust's DiffuseColor and the Terran exhaust's Colorize are compiled out) and its animations (the
  Nivelian spiral's turn, the Terran "ignite" burst on lighting and boosting, the capitals' "engines_off" flicker),
  drawn by `GoF2/ModEngineMesh` (`Resources/GoF2Mods`: GoF3's sprite, flow map, UV scroll, colourise 1 / 2, angle blend
  |N.E| / -N.E / N.E, pulsate, vertex wobble; computed on raw texture values, clamped and turned to linear as GoF3's
  8-bit gamma frame; the addition x the part's `intensity` before the clamp, `_Intensity`: GoF3's ship viewer shows them
  brighter than the formula (a bloom none of its shaders holds), matched by eye against clips of the Argus, Moornta and
  Shamash rotating there: 4 for the Terran and capital engines, the Nivelian / Inouac flare 1.5, plume 2, helix 5, the
  Vossk flare 1.5 and plume 5, the importer's `INTENSITY` table; linear-light blending was tried and dropped: it lost
  the dim helix; an exhaust style's colours as `_Tint` per renderer, each pixel's hue replaced and its smallest channel,
  its white, kept: the second colour on the helix (a part named "...spiral" or `"tint": "second"`) and the Fork
  particles, the first on the rest), in the engine frame (+z backward: the holder's z scale negative), each exhaust by
  its fxScale, warped to its outline (not sprites), 0.6 x long at idle. GoF3's trails / contrails / booster flash
  (`trailParticles` / `contrailParticles` + `contrails` / `boostParticles`, `Data.CustomForkEmitter`; importer
  `fork_psb.py`): the Fork Particle Studio .psb emitters each ship's VfxList names (Trail* at the engine helpers,
  Contrails at the contrail helpers; not the buffs), decoded from the Fork runtime (chunks: a 256-byte name, +0x100
  size, +0x104 data, +0x108 type; EData: colour keys +0x10.., key times +0x50 / 0x54, life +0x58 / 0x5c, speed +0x60 /
  0x64, sizes +0x6c.. / +0xb8.., mid time +0x78, rate +0xcc, max +0xe4) and run as Fork runs them
  (`EngineFx.UpdateFork`): still particles born at the rate along the path moved, quads facing the camera stretched
  along the wake, each x N^1.2 (N its overlap: GoF3 sums the streaks in its gamma frame, `_VertexGain`), none while the
  ship stands still (`EngineFx.MinWakeSpeed` 2 m/s: parked in the Customize preview they piled into a blot on the
  nozzle, which GoF3's viewer never shows). Checked in Play mode: the Manticore and a capital freighter (body, core,
  flare per nozzle), the Moornta and Argus flying with their wakes, the Argus, Moornta and Shamash in the Customize
  preview and the three races' capitals in space. Tests `EngineModelTests`.
- **Exhaust styles** (`ModExhausts`, a mod's `exhausts.json`; the guide's "Exhaust styles"): engine effects any ship can wear,
  picked in the hangar's Customize screen (`ShipCustomizePanel`'s Exhaust row and the style's two colour rows; with styles in
  the mods that are on every ship has the screen: `ModShipKits.CustomizationOf` gives a ship without other customizations a
  kit of its own with nothing else, `Kit.weaponColorRow` false). A style is a GoF3 `engineFx` (`EngineFx.Create`: GoF3's
  own engine meshes and particles; its colours, `_Tint` per renderer: every part but the helix, then the helix and the
  particles; "Original" GoF3's own) or a flame `model` with flame `materials` (an ES2 thruster: loaded with the mod
  ships as the one
  part of the style's own kit, `ModShips.LoadAll` / `EnsureExhaust`; per exhaust an instance named `exhaust_nozzle_N` at the
  mount, `MountTurn` x the style's `yaw`, scaled by its nozzle's size (below) against the style's `nozzle`, driven by an `EngineFlame` on
  the holder that blows each along its nozzle's -Z; colours: copies of the flame materials with `_EmissionColor` / `_BoostColor`
  at their own intensity; a flame style's boost cards are its own mod's ships' boost jets (`OwnShip`: the ship's key in the
  style's mod): on any other ship the holder's `EngineFlame.boostOverride` sets GoF2/ModEngineFlame's `_BoostOverride`, the boost
  cards stay hidden and the boost turns the main cards to the boost colour). The player's Exhaust size row (`exhaustsize=`,
  `ModExhausts.ParseSize`: 0.5..2 in 0.1 steps) multiplies each exhaust's fitted scale. Each exhaust takes its nozzle's outline
  (`ModExhausts.ShapesOf`, `NozzleShape`: the convex hull of the nozzle's geometry in the mount's plane, its area centroid as the
  centre, kept as `profile`, its radius at 32 angles; `FromPoints`): the mount's `outline` (game units, position_engine's axes:
  the GoF3 builder writes it from the nozzle's glowing surface, `engine_glows.engines`), else a mod ship's readable meshes with
  "nozzle" materials measured on the instance (`Measure`: vertices to the nearest mount; an ES2 hexagonal rear), else the mount's
  `glowSize` as an oval, else the game ship's measured glow (`nozzle_shapes.json`, x1.1: the glow sits inside the nozzle, median
  0.91 of the round size), else round (`NozzleRadius`). Sized by its mean radius; the shape itself: a flame model's meshes are
  copied per nozzle and bent to the outline (`Warp`: each vertex's distance from the axis x the profile's factor at its angle;
  freed with the holder's `EngineFlame.ownedMeshes`), an engineFx's parts (all but sprites) are spread to it (a mesh
  copy per shaped nozzle, `EngineFx.Warped`), centred on the nozzle. A GoF3 engine is also drawn longer than its width's
  fit by how much smaller the ship's nozzles are for its length than GoF3's hulls' (`ModExhausts.EngineLength`: GoF3's
  median nozzle is 0.062 of its hull, `Gof3NozzleToLength`; the Phantom's 25 on 940 game units, 0.027, so its plume
  x2.3; 1 on GoF3's own hulls, at most 3; a player's report on the Phantom, 2026-10-24: fitted across its small nozzles
  the plume was short for the ship. The stretch skewed GoF2/ModEngineMesh's angle blend while it was computed in object
  space: the glare and the body faded out seen a little from the side, only the helix left; it is computed in world
  space now. A sprite (the glare) is drawn `_SpriteBias` (1.6 GoF3 units x its scale) toward the camera: on the game's
  ships the near rim of the nozzle housing cut a dark crescent out of it seen at an angle (the Betty, the Phantom).
  Tested on a spread of ships (2026-10-24): the Betty, Phantom, Inflict, ship 7, Groza, Veteran (also with the Terran
  capital drive: large, very bright plumes), S'Kanarr, 45, 55, two ES2 ships. Its exhaust points do sit at the nozzle
  exits: measured, the hull ends 0.11 m in front of the Phantom's mount). Checked in Play mode: ship 7's wide nozzle
  blocks (both styles fill them), an ES2 Sentinel's hexagon (measured
  from its nozzle meshes) with a GoF3 drive, the Moornta's GoF3 outlines with an ES2 thruster. `NozzleRadius`: a mount's
  `glowSize`, a mod ship's `engineGlowRadius`, else 55 game units x the game
  ship's exhaust anchor scale (`turretAngles`). An `outline` is used only when plausible against the mount's `glowSize` (mean
  at least half of it, centre within 1.5 x it; `ModExhausts.Plausible`; the builder writes no others, `outline_plausible`):
  the Vossk hulls' outlines were slivers of a few lit texels of their nozzle rings (the Shaan'ra-Grth's 1 unit wide, 18 off
  its mount), which shrank any style on them to nothing. Sizes (2026-10-17): every source but one measures the whole nozzle
  (the game ships, ES2's nozzle meshes); the GoF3 builder's `glowSize` / `outline` are the hottest core of the nozzle's glow,
  1 to 5 x smaller (the Atauno Rheep's 16 units in its ~90-unit hexagon). An `engineFx` now says which nozzle it is drawn
  for (`nozzle`, game units, default 87; `CustomEngineFx.NozzleRadius`), measured by the builder on the hulls from
  behind (`ENGINE_NOZZLE`: Nivelian 60 (its exhaust's 87 fills its nozzles, but as a style on the Phantom it was the
  smallest of the three, its glare dimmer: 87 / 1.45 by eye, the user's report); Terran 100, the Argus' 75-110 under a
  138 exhaust; Vossk 45, the K'ssshar Ka's ~46 and the Shaan'ra-Grth's ~34 under a 138 exhaust; the others their exhaust
  mesh's radius, Inouac 138, the capital engines 108-117); on a ship with an `engineFx` of its own that gives a `nozzle`
  the shapes
  are scaled to it (`FitToOwnEffect`: the median of mean / fxScale becomes `nozzle`; without one, a mod written before
  the field, the shapes are kept, each keeping its shape and its
  size against the others), and an `engineFx` style's default `nozzle` is its `NozzleRadius`: a GoF3 hull wearing its own
  race's style shows it exactly as its own effect, and the styles fit game, GoF3 and ES2 hulls alike (on GoF3 hulls they
  were a fifth of the size, on the Vossk ones next to nothing; with the exhaust's width as every engine's nozzle the
  Vossk hulls' were three times too big). Offered (`ModExhausts.For`,
  `Offered`, `Find(key, ship)`; the Customize panel takes the ship, `Apply` ignores a style the ship isn't offered): every
  style but a flame style on its own mod's ships (ES2's thrusters are cut from the ES2 ships' own rear parts, which choose
  their exhaust there). Checked in Play mode (2026-10-17): the Phantom (Nivelian drive, ES2 thruster), the Cronus (Terran),
  the Argus (Vossk), the K'ssshar Ka (ES2 heavy thruster), the Shaan'ra-Grth (Nivelian), the ES2 Sentinel (Terran; its own
  thruster refused), the Customize screen's Exhaust row on the Sentinel, a Vossk hull and the Phantom. Groups (exhausts.json
  `group` / `groupName` / `scale`, `Style.lead` / `members` / `ForNozzle`): flame styles of one mod with the same group are
  one choice, its first (`For` lists only leads, `Find(key, ship)` turns a member's key, an older build, into its group);
  each nozzle wears the member whose `nozzle` x `scale` is nearest its size on a log scale (the player's Exhaust size
  counted; the instance is named `exhaust_nozzle_<i>_<member>`). The ES2 importer groups its three thrusters as "EVERSPACE
  2 thruster" (`scale` 3.6, the kit scale: nozzles of 1.38 / 1.53 / 2.21 m): the game's ships take the light thruster
  (the Phantom, the Inflict), larger nozzles the heavy one (ship 7, the GoF3 hulls; the Cronus both). The build carries
  it (`exhaust=mod_id:id;exhaustcolor=;exhaustcolor2=;exhaustsize=`,
  `ModShipKits.Build`, kept out of `VariantAssembly`'s name like the weapon colour) and `ModExhausts.Apply` puts it on each model
  instance (the player's in `SpaceLevel` spawn / swap, the turntable and hangar guests in `StationLevel.SpawnShip`, other players'
  ships in `NetPlayer.BuildModel`): a holder `exhaust_style` under the engine glow part aligned with the ship's root (it goes with
  the engines), the model's own exhaust hidden (the glow part's renderers `forceRenderingOff`: `ShipController.UpdateEngineParts`
  only toggles `enabled`; a built-in `EngineFx` disabled; the model's `EngineFlame` `suppressed`, its own flames dark; no
  particles: `ShipExhaust` finds the holder's effect), the manoeuvring jets kept. The Customize screen runs the turntable's
  engines (`StationLevel.PreviewEngines` / `PreviewEngineDrive`, `Visuals.EnginePreview` read by `EngineDrive`, past the
  station's full-throttle cap: boosting while a boost colour row is selected, `ShipCustomizePanel.BoostRowSelected`). The GoF3
  Ships mod's builder writes GoF3's Nivelian (helix), Terran and Vossk drives with their trails and booster flashes and
  its three capital drives (`exhaust_styles`, `STYLE_TRAIL`, GoF3's engine colours as the palette); the ES2 importer
  three thrusters cut from one-nozzle rear parts (`exhaust_module`: the flame cards with their UV1,
  the main cards' nozzle ring at the origin, ES2's +X nose turned to +Z; ES2's exhaust palette). Verified in Play mode: the
  Nivelian helix and an ES2 thruster on the Phantom, a GoF3 Terran drive on an ES2 Sentinel (its flames dark), an ES2 ring
  thruster in custom colours on the GoF3 Moornta (its engine effect off, the boost cards in the second colour), the Customize
  screen's rows and the turntable preview; after the imitation was removed (2026-10-24) all six GoF3 styles on the
  Phantom (Original and recoloured: gold with a red helix), the Moornta's own engine. Not tested in multiplayer.
- **Textures / skins** (`ModTextures`, a mod's `textures/<game texture name>.png | .jpg`, e.g. `ship_028_terran_diffuse` for
  the Veteran's hull; `textures/<assembly>/<name>` for one model only, e.g. `textures/ship_028_terran/glow_terran.png`: the
  shared lights / engine glow on the Veteran alone, before the every-object file; the scope is `AssembledObject.ModScope`,
  its prefab's name taken in Awake, and such objects get their own material copies, keyed (scope, material); the builder's
  Skins tab offers "This ship..." / "All ships..." on a shared texture): replaces that texture on every assembled object
  using it (`AssembledObject.Awake` -> `Apply`: the
  object's renderers get cached copies of their materials with the mod's texture in `_BaseMap` / `_MainTex` / `_BumpMap` /
  `_MetallicGlossMap` / `_EmissionMap`; the game's material assets are never touched; when the mods change in a scene, the
  Mod Builder's skins, the objects there get the game's materials back before the copies go and the new textures once
  loaded, `ModTextures.Revert` / `ReapplyAll`: a cleared skin drew the hangar's other ships of that model magenta); `_normal` / `_metallic` names load
  linear; loaded with the rest (`ModMaterials.PreloadTexture`, `ModLoading`). **Per ship** (a mod's new ship on a game model,
  `"model": "gof2:<model>"`): `textures/ships/<ship id>/<name>` replaces the texture on that ship alone, the game's ship of
  that model and the others on it keep theirs (2026-10-17). `ModContent.ApplyShips` marks such ships
  (`Database.ShipOwnTextures`, `ModContent.HasShipTextures`), `Database.ShipAssembly` then hands out a copy of the model of
  their own, "<model>#ship=N" (`KitRules.ShipToken`; `ModShipKits.ParseShipCopy`, ParseSkinned = one with a skin;
  `ModShips.SkinnedTemplate` makes a plain copy under the holder; `VariantAssembly`'s skins of such a ship go on the model's
  name), whose instances' `ModScope` leads `ModTextures` to the ship's key (`ShipScope`: "<mod id>:<ship id>", keys
  "<key>/<name>"), then the scope's own, its model's (`ModelScope`, so `textures/<model>/` still reaches the copies), then
  every object's; `HangarShipShadow.Attach` takes the model's map for "#" names. Builder: the Skins kind lists the project's
  new ships on a game model (`ModProject.OwnsSkinOf` / `OwnSkinShip` / `ShipSkinScope` "ships/<id>"; builder.json "skins"
  keeps them by id, their number being this run's), each texture has one Replace into the ship's folder and its ✕ leaves
  the project's every-ship file alone; the Ship tab's **Its own textures…** opens it. Tests: `GameModelsTests`
  (`ANewShipOnAGameModelWithTexturesOfItsOwn`, `TheBuilderGivesAShipOnAGameModelTexturesOfItsOwn`). Not run in
  Unity yet. The rest verified in Play mode: a BountyBot
  (/make_skin_texture) Veteran skin on the hangar turntable (`Mods/veteran_skin`, git-ignored). The backdrop's planets, suns
  and rings too (#66, `Backdrop.Load` -> `ModTextures.Replace(material)`: their Resources/GoF2Backdrop materials aren't on
  assembled objects; the copy's texture clamped). A mod's `backdrop.json` (`ModBackdrop.StarPlanets`, the last mod in the load
  order with one wins) `{ "planets": "star", "planetGlow": 2, "planetFlare": 0.35 }` draws the retextured planets as stars:
  facing the camera with its roll, not mirrored, no ring, their core at `_CoreGlow` planetGlow under the remake's bloom, and
  looked at, the sun's flare at planetFlare of its strength (a slight swell, an additive horizontal streak of their own
  texture, `Body.starStreak`).
- **Loading** (startup lag, `ModLoading`, `UI.ModLoadingView`): everything the mods bring loads at once in the background
  from the main menu's start (and again when the mods change): every ship together (`ModShips.LoadShip`: the files read on
  worker threads, the textures decoded by UnityWebRequestTexture off the main thread (`ModMaterials.PreloadTexture`, into
  the cache `ModMaterials.Texture` reads; zip files copied out once by `ModInfo.LocalFile`, cache folder by the zip's date and
  size), glTFast's main-thread work for all of them in one `TimeBudgetPerFrameDeferAgent` (0.6 of a frame; the old
  `UninterruptedDeferAgent` built each model in one go), the stations and rooms (`ModStations`), the weapon fx
  (`ModWeapons`), the music tracks and sound effects (`ModAudio`, `ModMusic`, `ModSounds`), the texture replacements
  (`ModTextures`), then the ships' hangar shadows
  (`World.ShipShadowBaker.ModShipsBaked`, see "Station scene"). Textures are compressed to DXT1 /
  DXT5 with their mipmaps by a Burst job (`ModTextureEncoder`, van Waveren's real-time encoder, native buffers: Texture2D.Compress
  took 110-145 ms per 2048 px texture on the main thread, a managed encoder's garbage a 2 s collection) where the GPU reads
  DXT; elsewhere (phones) Texture2D.Compress, one texture per frame. The result is kept on disk (`ModTextureCache`,
  `persistentDataPath/ModCache/Textures/<mod id>/<hash>.tex`, the hash of the file's path, size, change time, linear and the
  kind of compression): a later start reads it back on a worker thread instead of decoding and compressing (five PR ships
  2.3 s -> 1.3 s; the first start writes it, 126 MB for their 65 textures); an uninstalled mod's folder goes on `Scan`.
  They start loading with the startup logos (`StartupSplash`); the menu's black splash after them shows "Loading mods"
  with a progress bar and the ship / track in progress until it is done (`MainMenu.WaitForMods`, then the title; not shown when
  the mods that are on bring nothing to load, `ModLoading.HasWork`: no mods or only quests), so does the fade before a
  game scene when the mods changed in the menu (Leave). Measured in the Editor with the five PR ships: 5.3 s with frames of
  100-180 ms before, 0.9-1.9 s after with smooth frames (two of 83 / 164 ms right after the menu scene's load).
  Faster loading (2026-10-11, measured with the GoF3 Ships mod: 79 GLBs, 329 textures, 24-thread CPU, Editor, from the
  ships' start): cold 5.25 -> 2.92 s, warm 1.2 -> 1.0-1.1 s with the hangar shadows done 0.1 s after the ships (were up to a
  second, one hull per frame; now hull reads until 3 ms of the frame, `ShipShadowBaker.PrebakeReadMs`), GCs 25 -> 12 cold.
  The encoder spreads a texture over the job workers (`IJobParallelFor`: the role's conversion per pixel row, each mip level
  per row, every block row of every level at once; one serial job per texture before) and does the metallic-roughness /
  normal conversion itself (`ModTextureEncoder.Remap`; `ModMaterials.Convert` on the main thread is now only the fallback);
  output byte-identical to the old encoder (all 329 cached files compared). Cached textures don't wait for a decode slot
  (`CacheSlots` 16 / phones 4; `DecodeSlots` now processorCount / 2, 4..12, phones 2), their data and the encoder's output
  stay native (`ModTextureCache.Entry.data` is a NativeArray read straight from the file; the cache file written from
  `Work.Data` on a worker thread: the 1-5 MB managed copies drove the collections), and uploads share a 4 ms budget per
  frame (`ModMaterials.UploadTurn`). `ModShips.Ready` asks `ModContent.AnyShipModels` (ActiveShips rebuilt every entry: ~200
  KB and 2 ms per call for 79 ships, three calls a frame from the loading screen). Left: the GLBs are read whole into
  managed arrays for glTFast (~430 MB for this mod, most of the warm load's GCs).
  Phones (no DXT): the same jobs encode ETC2 (`ModTextureEncoder.Target.Etc2`, ETC2_RGB / ETC2_RGBA8, cache kind "etc2";
  every OpenGL ES 3 / Vulkan device reads it) instead of Texture2D.Compress on the main thread, one texture a frame: the colour
  in ETC1's individual / differential modes, never T / H / planar, the table and indices chosen by each pixel's brightness
  offset, the alpha as EAC. Checked in the Editor with Unity's own ETC2 decoder (2048 px ship maps): RGB 33.1 dB (Unity's
  fast ETC2 32.9, normal 36.6), a normal map with X in alpha 30.3 / 53.9 dB (Unity fast 30.1 / 54.1); with 4 job workers
  and Burst safety checks off 34-41 ms per opaque texture with its mipmaps, 104-135 ms with alpha, off the main thread (Unity
  fast: 31-51 / 363-418 ms on it, the top level only); the whole loader forced through it built all 79 GoF3 ships. On a
  phone (S24 Ultra): the 79 GoF3 ships cold in 9.5 s, nothing compressed on the main thread, the hulls
  checked up close. Texture2D.Compress stays for a GPU that reads neither; a phone's old "gpu" cache files are no longer read
  (nothing deletes them).
- **Image formats** (`ImageImport`): every mod image (ModMaterials' `Texture` and background `PreloadTexture`, so skins,
  portraits, icons, hull textures, planets / suns / skies; ModInfo's preview; ModTextures' file list) and the Mod Builder's
  pickers (which store a picked image as PNG) read PNG / JPG by Unity's decoders and TGA (plain / RLE), BMP (8 / 24 / 32-bit),
  DDS (DXT1 / 3 / 5, ATC "ATC " / "ATCA" / "ATCI", uncompressed; not DX10) and the game's `.aei` by its type byte, as the
  engine's loader maps it (the function before `TextureCreateFromFile`): raw RGBA 1 / 3 / 0x81, ETC1 0x14 / 0x16 / 0x17 /
  0x40 / 0x42 (Android), PVRTC 2bpp 0x0d / 0x0f and 4bpp 0x10 / 0x12 (iOS), ATC with explicit alpha 0x11 / 0x13, DXT1 0x20 /
  0x22, DXT3 0x21 / 0x23, DXT5 0x24 / 0x26 (PC); bit 1 = mipmaps, only the top level read; the engine has no ATC RGB (4bpp)
  kind. They are decoded by its own decoders (`DecodePixels`, worker-thread safe, by magic; TGA by extension). Until
  2026-10-23 the .aei types past ETC1 were guessed by the data's size, so an iOS PVRTC texture (a player's
  `tex_mido_ships.aei`) came out as DXT1 noise. Verified: each format against a reference image (size, orientation,
  colours, alpha), a PC `.aei` skin identical to an independent decode and shown on the turntable through the background
  loader; PVRTC 4bpp on that iOS file (the Midorian ships' atlas, identical to an independent Python decode); PVRTC 2bpp
  and ATC only against hand-built blocks (`ImageImportTests`), no real file.
