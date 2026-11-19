# Mods: customizable ships

Notes moved from `CLAUDE.md` (its index lists every notes file). Code comments citing `CLAUDE.md "<section>"` mean the section or bullet of that name here.

## Mods (remake-only)

- **Customizable ships** (`ModShipKits`, the guide's "Customizable ships"; EVERSPACE 2's ship modules): a mod's
  `shipkits.json` lists kits (id, slots in assembly order, `scale` = Unity metres per model unit for every build, `yaw`,
  `materials`, parts { id, slot, name, model, `types` (the ship types allowed, EVERSPACE 2's wing whitelist), `tiers`
  (extension meshes per build tier) }, all at one origin); a ships.json ship with `kit` (made "mod_id:kit_id" by
  `ModContent.ShipData`), `kitType` and `build` (`CustomShipData.kit` / `kitType` / `defaultBuild`, the build text
  "slot=part;...;tier=N") instead of `model`. `ModShips` loads each kit's part files once (`partTemplates`, the kit's materials
  applied, `ModShipBuilder.ApplyMaterials`), and only those a build uses: the kit ships' default builds and the player's saved
  builds at the start, any other when a build first needs it (`EnsureBuild` / `Template` start it; `KitPartsLoaded` and
  `ModelsChanged` follow, `StationLevel` / `SpaceLevel.OnModShipModels` rebuild the player's ship, the Customize screen keeps
  the last build with "Loading parts..." meanwhile; the ES2 kits: 39 of 205 files at the start), assembles the default
  build as the ship's "ship_NNN_mod" template and a player's
  build as "ship_NNN_mod#<build>" the first time `Template` asks (`variants`, the newest 48 kept; `Database.AssemblyByName`
  answers a variant name); `ModShipBuilder.Build`'s kitScale / kitYaw keep the kit's size. `Session.ShipBuilds` (by the ship's
  key, save v17 `shipBuilds` "key|build"), `ModShipKits.PlayerBuild` (normalized: every slot a part the type may fit, the tier
  clamped) -> `PlayerHull.Assembly` (the hangar turntable, flight). The hangar's **Customize ship** button (under Inspect ship,
  only for a kit ship, `StationMenu.BeginCustomize`) opens `UI.ShipCustomizePanel` over Inspect ship's orbit view (the
  orbit turned aside so the ship stands in the middle of the screen right of the panel, `StationLevel.InspectFrameX`;
  the footer's Back hidden, it lay under Apply; the rows scroll in a list sized to the screen, `FitToScreen`, so the
  stats, buttons and help stay on it): a row per
  slot and the tier, up / down / left / right (W A S D, D-pad, left stick) and the arrows' clicks, every change shown on the
  turntable (`ReplacePlayerShip`), Enter / A applies (autosave), Esc / B / Back cancels (the old build back). Verified in Play
  mode with a box-part test kit (Mods/kit_test): parsing, the default builds, a variant, the type whitelist and tier clamp, the
  panel changing the turntable, cancel, the build in flight. Colours: a kit's `palettes` / `colorSlots` (targets: a material
  name filter and base / emission / engine) / `presets`, the build's `colors` ("color.slot=value": a palette id or
  "#RRGGBB/metallic/smoothness", `ModShipKits.Resolve`); `ModShips.Assemble` recolours copies of the matching materials
  (`ApplyColors`) and passes an engine slot's colour as the build's engineGlowColor; kit `materials` entries are named by their
  `material` filter (`ModShipBuilder.ApplyMaterials`, `CustomShipMaterial.material`: matching the model's own material names)
  so colour targets find them; the panel's Colour scheme row (the matching preset, else Custom) and a row per slot with a
  swatch. Verified with the private EVERSPACE 2 kits built from the user's own export (git-ignored `Mods/es2_ships`,
  `tools/build.py` reads the exported glTF parts, textures and DT_ShipModules / DT_ShipColors / DT_ShipColorSets /
  DT_DebugShipsKS JSON: never committed or shared): Light / Medium / Heavy kits (37 / 39 / 29 parts), nine ships with ES2's named
  default builds, ES2's 4 palettes, 7 slots and 22 schemes, the hull split into Paint1-3 by its vertex-colour masks; its
  `tools/phone.py` makes a phone build (the same mod id; Blender decimation to a quarter, 2.9 M -> 0.73 M triangles, half-size
  maps; `Build/es2_ships_phone.zip`, 43 MB). Mounts per part: a part's `mounts` / `tierMounts` (`ModShipKits.MountsFor`: per slot type the
  build's parts', else the ship's own), `Database.MountsOf` -> `ModShipKits.BuildMounts` (the player's build for the player's
  ship, else the default build: guns, secondaries, turrets, exhaust particles, the mining beam), `ModShips.Assemble` passes them
  as the build's mounts (the engine glow). Weapon and turret models (`ModHardpoints`, a kit's `hardpoints`: `turret` + `weapons`
  rules by slot, items (also by the look) or categories; loaded with the start's parts as part templates, `ModShips.PartTemplate`):
  the turret is rebuilt in the ship's frame (`TurretModel`: "Turret" -> "pivot" -> "kit_gun" -> "muzzle", the meshes in holders at
  the kit's turn and scale, the item's turret weapon on the gun with its muzzle on the turret's) and replaces `PlayerTurret`'s
  stand-in on `builtIn` mounts (its muzzle for the shots, 40 units ahead, and a collector's stream; `BuildStatic` shows it in the
  hangar and on other players' ships); the primary / secondary models go on the mounts in `WeaponSystem`'s order
  (`AttachWeapons`, from `SpaceLevel` and `StationLevel.RefreshTurret`, rebuilt when the weapons change) and `WeaponSystem`
  moves each gun to its model's `muzzle` node (`MuzzleOffset`); a mount's `rotation` (`WeaponMount.rotation`, Euler degrees or
  a quaternion in the ship model's frame, `ModHardpoints.MountTurn`) turns its weapon model and muzzle (the GoF3 hulls'
  slanted hardpoints; the shots still fly along the nose), and a turret model without a `muzzle` node carries its weapon on
  the gun node and fires from the weapon's muzzle; the weapon and turret models take the build's colours
  (`ModHardpoints.Colour` -> `ModShips.ApplyColors` with a cache of copies per build's colours: ES2's missile pod lights its
  bay frame in Emissive 2 and is painted in the hull colours; they kept the template's white before); a primary /
  secondary mount's `nodes` (`WeaponMount.nodes`, ship model node
  names: the GoF3 hulls' pylons) show only while that mount carries a weapon (`ModHardpoints.ShowMountNodes`, from every
  `AttachWeapons`: flight, the hangar turntable, other players' ships and hangar guests). **Weapon models toggle** (the
  user's request for the GoF3 hulls, 2026-10-17; the kit's hardpoints `"toggle": true`, `ModHardpoints.Toggles`, which the
  GoF3 builder writes): the Customize panel's Weapon models row (Shown / Hidden, `Kind.WeaponModels`), `Build.weapons`
  ("weapons=off" / "on" over a default build's off; ships.json `"build"` `"weapons": false`; `Normalize` drops it for a kit
  without the toggle; kept out of `VariantAssembly`'s name: the model doesn't change); `ModHardpoints.Hidden(ship, shown)`
  (shown ?? the player's build): `AttachWeapons` attaches no models and hides every pylon node, `TurretModel` returns null
  (`PlayerTurret`'s invisible stand-in, nothing in the hangar), `MuzzleOffset` zero (the shots from the mount), `HangarHeight`
  without the weapons; other players through `NetPlayer.ShownBuild`. Tests `ModHardpointsToggleTests`; verified in Play mode
  (the GoF3 Cronus: guns and both kit turrets gone, the stand-ins kept; the hangar row and turntable). **Skins** (a
  kit's `skins`, `ModShipKits.Skin`; `Build.skin`, text
  "skin=id", "none" = the model's own look over a default build's skin, `Build.Reserved` keeps "skin" / "tier" / "color." out
  of the slot names): texture sets per material name (diffuse, emission, metallicSmoothness in URP's layout, normal, colours)
  put on copies of the build's materials (`ModShips.ApplySkin`, before the colours) and an optional `engineColor` for every
  exhaust; their textures load with the build's parts (`SkinTextures` / `SkinReady` in `PartsReady`, `LoadOnDemand` preloads
  them, a failed one falls back to `ModMaterials.Texture`; `ModMaterials.Has`); the Customize panel's Skin row
  ("Standard" + the type's skins). **Weapon colour** (`Build.weaponColor`, "weaponcolor=id|#RRGGBB", "none" = the weapons'
  own; the kit's palette "weapons" else `ModShipKits.WeaponPalette`; left out of the variant assembly's name, the model doesn't
  change): the Customize panel's Weapon colour row on every kit ship (so the Customize button shows for every kit ship);
  `Flight.WeaponTint` colours the energy weapons (categories 0 / 1 / 3 by the Look, and the blaster-bolt turrets 47 / 48 / 49 /
  224) through `GunRig.Tint` (projectiles, muzzle flash, impacts, and thermo trails when called before `EnableTrails`): cached
  copies of their materials with the main texture blitted by `Hidden/GoF2/WeaponTint` (`Resources/GoF2Weapons`: brightness x
  the colour, the hottest part kept white; a red laser can't just be multiplied blue) and a grey `_Color` at its own intensity,
  `PartAnimation.RefreshMaterials` re-reading the fades; the player's guns (`WeaponSystem.BuildRig`), turret (`PlayerTurret`)
  and other players' mirrored shots (`NetShotMirror`'s weaponColor, from `NetPlayer.ShownBuild`). Verified in Play mode: the
  classification (a Micro Gun kept its own), green and blue bolts, beams and impacts, the row, Apply into flight.
  **Customizations of ships without a kit** (the game's own, one-model mod ships): a ships.json entry's `skins` / `weaponColors`
  (`ModShipKits.CustomizationOf`: a kit with no parts per ship, merged from every mod that is on, `ReadShipCustomizations`;
  `Skin.mod` the mod whose files it uses); builds saved under `BuildKey` ("gof2:<n>" for the game's ships), `TypeOf` "",
  `DefaultBuildOf` none; a skin shows on a skinned copy of the model (`VariantAssembly` -> "<model>#ship=N;skin=id",
  `ModShips.SkinnedTemplate` from `AssembledObject.LoadPrefab` / `ModShips.Template`, `ParseSkinned`; `Database.AssemblyByName`
  keeps the model's own pack for "#" names); the Customize screen, weapon colour, the rebuild hooks (`SpaceLevel` /
  `StationLevel.OnModShipModels`) and other players' views (`NetPlayer` build text) take `CustomizationOf`. Verified in Play
  mode with throwaway skin mods: the Phantom's Customize screen (Skin + Weapon colour rows), its crimson skin on the
  turntable and in flight with a mod weapon colour, the game's Groza (22) skinned in the hangar, the mod turned off
  (plain). Used by the GoF3 Ships mod (version 2: one kit "gof3", a hull part per ship, its GoF3 paint schemes as
  skins, the GoF3 guns as hardpoint models by weapon, a GoF3-parts turret on every hull, GoF3's projectile colours as its
  "weapons" palette (Colorize_Color1 of the GoF3 weapon effects' materials); built by the gof3-ships-importer's
  `tools/build_gof3_mod.py`, which finds each hardpoint's pylon by its number (several hulls borrow another's pylons) and
  moves any gun or turret mount that would float onto the hull (`snap_floating_mounts`: the gap measured with every gun
  model it may carry; the Krotac's lower turret hung 10 m under the hull, the Cronus' wing guns 1.5 m above it)). The ES2 converter adds ES2's player turret
  (`SK_Turret_PlayerShip_001`, skinned, split by bone into rigid base / pivot / gun meshes) and its weapon models
  (`SM_PrimaryWeapon_*`, the missile pod) with their muzzle sockets, mapped onto GoF2's weapons by item / category. Verified in
  Play mode on the Gunship: the hangar turntable's guns, pod and turret, the guns firing from the models' muzzles, the turret
  aiming, tilting and firing from its barrel, a plasma collector's stream from the kit turret. Other players' ships too (`NetPlayer.weapons`,
  an owner-written "p,p|s,s" of the mounted primaries / secondaries, `PackWeapons` / `WeaponStacks`; `NetPlayer.BuildTurret` and
  `NetHangar.ShowTurret` build them with the turret). **Other players' builds** (`NetPlayer.build`, FixedString512, the owner's
  `PlayerBuild` text, "" for another ship or past 500 bytes; `ShownBuild` = `ModShipKits.RemoteBuild`, normalized against this
  game's kit): their ship is the build's variant (`VariantAssembly`; parts not loaded yet load through `ModShips.Template` /
  `EnsureBuild` and `ModelsChanged` builds it again), its turret, weapons and exhaust particles on that build's mounts
  (`ModShipKits.MountsShown(db, ship, slot, build)`; `Database.MountsOf` answers with the local player's build); parked in the
  hangar the same (`HangarTraffic.SpawningGuest` -> `NetHangar.GuestAssembly` in `StationLevel.SpawnShip`, the exhausts from
  `ModShipKits.BuildOfAssembly`; a guest is parked again when its build changes or its parts finish loading). Verified in a session (the Editor hosting
  with mods allowed, a Linux development build joining on 127.0.0.1): the host's custom Gunship (Gunship B wings, body 7, a
  yellow hull) parked in the client's hangar and flying in front of it in space in that build with its turret. Not yet: the
  other eight ES2 ships looked at; the hangar's NPC ships of a kit type show the local player's build (`PlayerHull.Assembly`).
  **Hangar height of kit ships** (`ModHardpoints.HangarHeight`, from `StationTables.ShipY` through `KitShipY`; a guest's own build
  through `NetHangar.GuestShipY` in `StationLevel.ParkedPosition`, `HangarTraffic.SpawningGuest` now also around the pad
  placement): the shown model's lowest point (its mesh boxes without the glow parts) and the weapon models under it, an
  upside-down turret too, `HoverGap` 1.5 m above the pad; kept per model and weapons once the build's parts are loaded; ships.json
  `hangarHeight` is ignored for kit ships (the converter's value ignored the wing tiers: four default builds sat 2-7.5 m in the
  floor, other wings up to 15 m). **Engine materials** (`CustomShipMaterial.engine`, `EngineMaterialGlow` on the template from
  `ModShips.Assemble` / `ModShipBuilder.Build`): their emission follows the engine glow part on ships a station spawns
  (`StationLevel.SpawnShip` sets `followEngine`: dark parked, lit in a hangar flight, 0.25 s fade, this ship's material copies);
  always lit in space. Verified in Play mode: the nine ships' heights, the Sentinel's default build 2.1 m clear of the Midorian
  turntable (7.5 m into the floor before). **Engine flames** (`CustomShipMaterial.flame` / `flameLength` / `flameIdle` /
  `boostColor` / `flameNoise`; `GoF2/ModEngineFlame` from `ModAssets.engineFlame` (the Mod Assets builder resets the
  template to the shader's defaults), `ModMaterials.Flame`; `EngineFlame` on the template, `EngineDrive` the throttle reader
  it shares with `ThrottleGlow` and `EngineMaterialGlow`). **Since 2026-10-24 drawn by EVERSPACE 2's own formula**
  (`_Es2`, set by `ModMaterials.Flame`; the user's request to decompile ES2): its `M_Vehicle_EngineExhaust`'s compiled
  shaders were read out of the game's IoStore (CUE4Parse with shader maps on: the material's maps are cooked inline, SM5
  / SM6, Oodle; DXBC disassembled with Wine's d3dcompiler_47; the ES2 importer's `tools/shaders` and
  `research/engine_exhaust.md`): each vertex moved u x Length (500 cm, the importer's `flameLength` 5) backward whatever
  the thrust; opacity = pow(sat(thrust x (R + G sat(boost))), OpacityExponent 6) x pow((1 - u) / (1 + 18 u), 4) x
  noise(red at (time x 2, v))^2, colour R x EngineColor + G x BoostColor x 2, x 1000 alpha-blended (here added x
  `ModMaterials.Es2FlameGain` 300, matched by eye to the user's ES2 screenshots of the Wraith: a short white-hot core);
  no view-angle term. Its inputs (the same day, decompiled: `BP_ShipBase`'s Kismet bytecode through CUE4Parse, and
  `AESPawn::UpdatePrimativeDataParamsNative` in the shipping exe, Ghidra with the shipped PDB's symbols; `EngineFlame`
  computes them, `Input` / `Thrust` / `Boost` / `Boosting` / `Velocity` / `Pilot` for the effects below): the player's
  ship (`EngineFlame.Pilot`, a ShipController's model) the movement input (free flight's `ThrustInput`, the original
  flight's throttle and strafe) eased like `EaseEngineVFX` (VInterpTo at 16 / s rising, 8 / s falling with
  BP_Ship_Player's fixed 0.05 s step: 80 % / 40 % of the way per 60 fps frame) and the boost (a boost, the cruise mode,
  a jump) the same; any other ship its velocity in its own frame over the top speed and its boost when faster, at once
  (BP_ShipBase's 0.3 s step: the target at once); the flames take the thrust's forward part; and the player's flames
  bend against the velocity (SteeringOffset: + u^2 x -0.008 x min(|v|, 10000 cm/s) in world space, x sat(thrust.x /
  |thrust|): at most 80 cm of a 5 m flame, here `_SteerBend`, 0.16 of the flame's length at 100 m/s; since 2026-11-17
  the remake adds the turn's own bend, `EngineFlame.TurnBend`: "GoF3's engines bend in turns" in mods-models.md). **EVERSPACE 2's
  other engine effects** (`Es2EngineVfx`, `GoF2/ModEs2Particle` in Resources/GoF2Mods, its kinds each a decompiled
  material; the Niagara systems' VM scripts disassembled by the importer's `tools/shaders/niagara.py`) at every engine
  socket (exhaust mount, slotType 3) and booster socket (slotType 5, ES2's `SKT_Booster`, which the importer now keeps),
  each sized by the socket's scale (`WeaponMount.socketScale`) in ES2's metres x the kit's scale, the engine colour (a
  booster's the boost colour), its opacity the thrust (a booster's boost x thrust): the player's ship a veil trail per
  socket (`NS_Vehicle_EngineTrail_Player_Default`: 50 particles / s in the ship's frame, born 1 m inside the nozzle
  moving back at 25 m/s and easing toward world-still by 1 - 0.5^(0.4 dt), 0.44 s, 50 cm wide, colour x 5, alpha 0.05 x
  a cosine over the life x the opacity; the alpha-blended veil material) and an engine light per socket
  (`CreateEngineLight`: radius 3 m x the socket's scale, intensity 20 x the thrust, a booster's x the boost; URP point
  lights x `Es2EngineVfx.LightGain`); any other ship a camera-facing glow per socket (100 m x the scale, (r / (1 + 98 (1
  - r)))^2: a tight core) and a world-space trail (a point every 10 m flown, 2 s, 5 m wide, alpha x pow(velocity .
  forward, 32): only flying nose first; its first 5 m 20 x as bright); none docked. Not used: `P_Vehicle_EngineGlow`
  (its haze, trail and lens-flare streak; no blueprint spawns it). The rest of this bullet is the earlier guess, still
  `_Es2` 0: EVERSPACE 2 draws its flames from small `M_Vehicle_EngineExhaust`
  sections of the rear / wing parts, flat cards with UV u from the nozzle to the tip that its material stretches with the
  thrust (its graph can't be exported: rebuilt from its parameters and matched against EVERSPACE 2 running, the player's
  Sentinel (Body_014, Rear_009, Nemesis tier 4) behind, beside and chased at full thrust, boosting, strafing in all four
  directions and reversing, through its photo mode). The cards' vertex colours say what they are: red the main flame (the
  thrust, the engine colour, also through a boost), green the boost jets (only while boosting, in the boost colour); the
  converter (`flame_across`) writes each card's coordinate across it into UV1.x and its kind into UV1.y, read as
  |y - 0.5| (glTFast flips V): 0 main, 0.25 boost, 0.5 none. The vertex shader moves each vertex u x the length backward
  (`_FlameDir`, the ship root's -Z in the renderer's object space, per renderer and submesh through a MaterialPropertyBlock,
  the renderer's bounds grown to match): main cards by thrust 0..1 (none at idle: below a drive of 0.02 the flames
  shrink into the nozzles like free flight's engines off; the user's ES2 shots of a ship at rest), +30 % at a full
  boost, boost cards by the
  boost alone, both x the engines' level (the engine glow part active, 0.25 s fade: shrunk into the nozzles and dark on a
  parked ship; in the station capped at full throttle). The fragment: additive, the colour gold near the nozzle (boost
  cards a tight near-white head, `_BoostCore`, then saturated in the boost colour at `_BoostBrightness` 1.6: matched to the
  user's ES2 customization-screen shots of the Wraith boosting; they were half as bright with a faint head), ES2's
  T_Noise_018_M stretched into fine streaks, faded hard toward the tip (`_Fade` 4), each card
  softened toward its long edges (UV1.x) and, for main cards, faded as it turns edge-on (|N.V|^2, ES2's OpacityExponent:
  from behind only thin rays remain, as in ES2; the remake had widened the cards toward the camera, `_Widen`, now 0, which
  turned Rear_009's boost cards into fans above and below the ring). A ship with flames keeps an empty `engine_glow` part
  (no discs) and has no exhaust particles (`ShipExhaust.Setup`). The nozzle glow (`M_Vehicles_Floaters_Emissive`'s
  engine quadrant, NozzleGlow, see the ES2 lights below) takes the engine colour slot, `engine`: `EngineMaterialGlow`
  lights it with the thrust, dark at idle (it kept 18 % at idle before; ES2's nozzles at rest are dark), dark parked.
  Intensities: flames 4, nozzles 1.6 (brighter clips to pale yellow under
  the remake's tone mapping; ES2 adds a bloom halo the remake's bloom can't without washing it out). **Manoeuvring jets**
  (`LateralThrusters`, mounts of slotType 4 with `WeaponMount.direction`; the converter reads every part's
  `SKT_LateralThruster` sockets, `thruster_mounts`: the Unreal rotator's forward axis): ES2 fires `P_Vehicle_LateralThruster`
  at them; since 2026-10-24 as ES2 draws and drives them (decompiled: the Cascade system, `M_Vehicle_LateralThruster`'s
  shader, BP_Ship_Player's tick): a 300 x 80 cm card along the jet turned toward the camera (only its half beyond the
  socket shows) and a 100 x 100 cm disc facing along it (`GoF2/ModEs2Particle` kinds 0 / 1; the disc whole, ES2's own
  half mask needs the socket's roll), shape s / (1 + 18 (1 - s)) ^ (2 + sqrt(noise)), colour x 20, alpha Opacity x (1 +
  3 Boost) with Opacity = sat(f . -input + sat(f . v^) x (no input)) (the jets pushing where the pilot asks; with no
  input those blowing along the velocity, the dampers) and Boost = f . -input x boosting, at once (no easing); only on
  the player's ship (EngineFlame.Pilot); not its 2 m point lights. Under the engine glow part (hidden with the engines,
  left out of the hangar heights and shadows); none in the station, except every jet at 0.35 while the Customize screen
  runs the engines (`Visuals.EnginePreview`), as ES2's customization screen shows them. `LateralThrusters.TestInput`
  overrides
  it for Editor tests. Colours like EVERSPACE 2's (TrailColorEngine / Boost / Lateral): a kit colour slot's target "property": "boost" sets a
  flame material's `_BoostColor` (`ModShips.ApplyColors`); the ES2 kits' slots Main thrusters (the flames and nozzles), Booster
  (the boost cards) and Side thrusters (the "lateral thruster" material: the jets and, through `LateralThrusters.Setup`, their
  discs; without a slot on it they take the ship's flames' boost colour; only the hue: normalised to its brightest
  channel 1, as ES2 scales a plain colour x 20, so a slot's x 8 intensity doesn't blind them; `GoF2/ModEs2Particle`
  declares `_BoostColor` for it, `ApplyColors` sets only what a shader has). The effect meshes (`lateral_thrusters_add`,
  Es2EngineVfx's parts) end in `_add`: every corner at the socket, PhysX failed to "clean" them as hull meshes
  (`HullCollision.IsHullMesh` skips `_add`). Strength slots (`ColorSlot.steps` / `scales`, no palette; the Customize
  panel's percentage rows; `ModShipKits.StrengthOf`) scale another slot's emission: the ES2 kits' Cybernetic glow (Emissive 3)
  and its intensity. Materials marked `"boost"` glow only while boosting (`EngineMaterialGlow.boostMaterials`, EngineDrive
  above 1); `"hologram"` materials are copies of `GoF2/ModHologram` (`ModAssets.hologram`, `ModMaterials.Hologram`): additive
  rings whose pattern turns and which bob, per ring by UV1 (x spin, y 1 - bob share), rebuilt from EVERSPACE 2's
  M_ShipModules_Holograms parameters (the converter's `holo_rings` reads the rings' vertex colours: red spin, blue bob);
  `"shimmer"` materials (`GoF2/ModNozzleShimmer`, `ModAssets.nozzleShimmer`, `ModMaterials.Shimmer`) draw additively over the
  nozzle glow's faces (the converter splits them twice, "NozzleShimmer"): the glow pattern times T_Noise_001_M scrolling at
  M_Vehicles_Floaters_Emissive's ScrollSpeed 0.3, driven by `EngineMaterialGlow` like the glow (which keeps 60 % then).
  `"damageEmission"` (a mask PNG, the emission of a
  material that has none): battle damage embers, `DamageEmbers` lighting them ember-orange with the ship's `Target.HullFraction`
  (a third lost to full at 90 %, a slow flicker; EVERSPACE 2's DamageAmount / Ember* parameters), only on materials
  tagged `ModMaterials.DamageEmbersTag` (their emission is the mask: until 2026-10-25 it took any material with an
  emission map and a damage layer and zeroed it, so the ES2 cyber strips and floater vents never glowed); the ES2
  converter builds
  the mask from T_Vehicles_Damage_C_R_M_H (the blurred height's burn blobs, cracks from its colour) for the hull and details.
  `"damageDetail"` / `"damageNormal"`: charring through URP Lit's detail layer (detail colour with the burn mask in its alpha as
  `_DetailMask`, the damage normals), `DamageEmbers` raising `_DetailAlbedoMapScale` / `_DetailNormalMapScale` from a fifth
  of the hull lost to full at 75 %; the mod detail templates use `_DETAIL_SCALED` (MULX2 ignores the scale). `Hitpoints.hull`
  is an int (an eval setting it needs a cast). The
  ES2 export's skeletal animations (AnimSequence JSON) carry no keyframes and none targets the ship modules. Kit parts'
  `anchors` / `anchor` (`ModShipKits.Part`): a part sits on a named point another part of the build offers
  (`ModShips.Assemble` offsets its models; the ES2 bodies offer their SKT_Cockpit socket to the canopy and interior). The ES2
  lights (converter): TechDetails lights by `T_ShipModules_TechDetails_00N_E`'s sector IDs into Lamp<N>_S4..S7 (S4
  Emissive 1, S5-S7 Emissive 2; matched against EVERSPACE 2's customization screen with Emissive 1 / 2 / 3 apart). Since
  2026-10-24 the floater glows follow their compiled shaders (the importer's `research/floater_emissive.md`; the user's
  request, the eyeballed rules before them took black and green faces for lights). `M_Vehicles_Floaters_Emissive`
  (`glow_split`): the UV quadrant picks the slot (u < 0.5, v < 0.5 Emissive 1 "Ring1"; u >= 0.5, v < 0.5 Emissive 2
  "Ring2", both always on; u < 0.5, v >= 0.5 the engine colour x the thrust "NozzleGlow"; u >= 0.5, v >= 0.5 the boost
  colour x the boost "BoostVent"), the mask is T_Vehicles_Floaters_Emissive_M's red x green at twice the UV
  (`floater_e.png` tiles it 2 x 2; the lights wherever it is above 0, the engine / boost reveal baked at 3/4 thrust),
  and the vertex colour's red multiplies it (red 0, the black / green / cyan faces, "NozzleOff", dark; 1/3 and 2/3 the
  "_R33" / "_R67" materials through dimmer masks). `M_ShipModules_Floaters_Parent` (`floater_split`): the faces showing
  a lit texel of R_MT_E's alpha are "CyberStrip" (the cybernetic glow, `cyberstrip_e.png` = that alpha), and the detail
  cards glow always through C_A x R_MT_E's blue x 30 (`floaters1_glow_e.png`: a few vents and grilles). The Wraith's
  blade and wing-tip lights, taken for Emissive 1 in its customization captures, are in the boost quadrant (ES2's Engine
  Colors page boosts, and the boost and Emissive 1 defaults are both light blue); its plates don't glow
  (MI_Vehicles_Okkar_TechDetail_PlayerShip's EmissiveColor is #100801). Verified in Play mode: thrust (chase, behind,
  side), a forced boost (the cyan slots in ES2's places),
  the jets in all four strafe directions and in reverse, parked in the hangar (flames, nozzles and jets off). The ES2
  converter reads the meshes' sockets (SKT_PWSlot / SWSlot / Turret / Engine,
  Unreal cm -> the export's glTF metres (X, Z, Y); at most 4 exhausts per part). Stats and slots per part (`Part.armor` / `cargo` /
  `handling` %, `equipment`, flight %, `primarySlots` / `secondarySlots` / `turretSlots`; `ModShipKits.EffectOf` /
  `BuildEffect`, the player's build for the player's ship): `ModShipKits.Armor` / `Cargo` / `Handling` / `Slots` /
  `Effective` feed `Shop.BaseHp` / `MaxLoad`, `PlayerHealth`, `StationLevel` (the arrival's max hull), `Hangar.SlotCount` /
  `FitToSlots`, `Database.BuildFlightStats` (handling, the flight bonuses in both styles) and the stat rows (`ItemInfo`,
  `ItemInfoWindow`); the Customize panel's stats line (`StationMenu.CustomStatsText`) and Apply's `FitToSlots`; the tier is
  cosmetic. The ES2 kits' numbers are the converter's design (ES2's modules carry none): bodies hull / cargo / equipment by
  size, rears speed / acceleration by nozzles, wings turning / armour by span and gun / missile slots +-1 around the type's
  base by their sockets, turret slots by their turret sockets. Not yet: other players' builds in multiplayer (they see the
  default build), decals.
- **Liveries, decals and engine trails** (remake, 2026-10-25, the user's request: "include everything the Supporter Pack
  adds", then "reproduce them accurately"; kit fields `liveries` / `liveryAxes` / `decals` / `trails`, skins' `colors` /
  `livery` / `decal`, Build `livery` / `decal` / `trail` / `boostTrail`; the guide's "Customizable ships").
  **Liveries**: EVERSPACE 2's projection decals (M_ShipModules_Hull_Parent's compiled pixel shader): a mask over the
  ship's (forward, right) at 1 / (2 x the bounds' largest half extent), mirrored per axis at 0.5, offset and scaled,
  mask^(1 + 1.2 lum(colour)), none on faces steeper than the clip angle, lerping the base colour (x the map), metallic
  and smoothness; on materials marked `"livery": true` (`CustomShipMaterial.livery`), `ModShips.ApplyLivery` sets
  `_Livery*` on copies (`GoF2/Hull`: its own `HullInput.hlsl`, a copy of URP's LitInput with the livery / decal values
  in UnityPerMaterial, and `GoF2LiveryMask` in `HullForwardPass.hlsl`). **Decals**: M_ShipModules_Decals_Parent: the
  mask's R x colour 1 + G x colour 2 + B x the border, A coverage, on materials marked `"decal": "insignia" | "type"`
  (`_DecalParams`, `_DecalColor1..3`; colour slot properties `decal1..3`, `livery`). **Engine trails**
  (`Modding.Es2Trails` + `GoF2/ModEs2Trail`, driven by `Es2EngineVfx`): EVERSPACE 2's 13 customization trails
  (DT_ShipExhaustParticles; BP_Ship_Player.UpdateEngineTrails: ThrustParticlesID at the engine sockets, BoostParticlesID
  at the booster sockets) decompiled from their Niagara VM scripts, curves and renderers and their materials' compiled
  pixel shaders (the ES2 importer's research/engine_trails.md): sprites (Stardust, Raw Combustion lit by the main light
  and SH, Coolant Leak with UE's texture-based simplex noise, Firestarter velocity-aligned), ribbons (Void Reverb,
  Thermal Blaze, Residuum, Voltaic Tendrils with UE's texture-based gradient noise and its arc sprites, Ion Veils), mesh
  particles (Rings of Nerea, Tetragons, Delta Surge), each in the socket's local space in ES2 cm, the particles easing
  to rest in the world. UE's View.PerlinNoiseGradientTexture / PerlinNoise3DTexture are rebuilt the way FSystemTextures
  makes them (FRandomStream; `Es2Trails.GradientTexture` / `Noise3D`; the bytes may differ, the noise is the same kind).
  Every ES2 emissive's x (1 + MPC_GlobalParameters.EmissiveExposureCompensation, 10) is in `_Gain` (Es2FlameGain /
  1000). Only the player's ship shows them, and the Customize screen's turntable while its engines run (EVERSPACE 2's
  HangarMode: no inherited velocity, colours x 4, alphas squared; the booster trail row shows a boost). The
  soft-particle fades read the camera depth texture where the URP asset has it (`_SoftParticles`). Not checked yet
  against EVERSPACE 2 side by side.
