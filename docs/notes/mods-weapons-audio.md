# Mods: weapons and audio

Notes moved from `CLAUDE.md` (its index lists every notes file). Code comments citing `CLAUDE.md "<section>"` mean the section or bullet of that name here.

## Mods (remake-only)

- **Weapon fx** (`ModWeapons`, items.json `"fx"` on a new item or an override; `ModFxPart`): projectile / muzzle / impact
  each the base item's, `false` (none), a `sprite` (PNG on a two-sided 1 x 1 quad, white vertex colours: the GoF2 Shader
  Graphs keep `_USEVERTEXCOLOR_ON` in every game material, so it stays on) or a `model` (glTF; with `texture` every
  renderer gets the fx material and white vertex colours, else its own glTF materials), or the base item's own part recoloured
  (only `color` / `glow`, `ModWeapons.Recolor`: the effect materials (GoF2/Additive / AlphaBlend; a missile's hull stays)
  copied with their texture made grey by `Resources/GoF2Mods/FxRecolor.shader` (each texel's brightest channel, alpha kept;
  one RenderTexture per atlas) and `_Color` = the colour, so the hue is replaced and the shape / brightness kept; a modder's
  colour-only projectile used to be refused whole, the original look left); colours `[r, g, b]` 0-1, 0-255 when a value is
  above 1, or "#rrggbb"; fields size (game units: the
  sprite's width / the model's largest extent; the N'saan's own bolt is ~1600 units, its impact ~6300), color, glow,
  additive (copies of `ModAssets.fxAdditive` = mat_27250 sprite_fire / `fxAlpha` = mat_20101 sprite_smoke, **GoF2 > Build >
  Mod Assets**), lifetime (muzzle 100 / impact 300 ms), grow, fade, spin, alongFlight + stretch (axial billboard about the
  flight direction, taken from its own movement: GunRig turns blaster roots to the camera), rotate; `shot` / `explosionSound`
  (files or lists, loaded decoded), `shotLoops`. Each item gets a WeaponFx of its own (`Instantiate` of the base item's with
  the parts / clips replaced); `WeaponFx.Load` returns `ModWeapons.Fx(item)` first (null while loading: the base look
  meanwhile; an override's item number resolves through `ModContent.TryResolveItem`, which now loads the table first: on
  the startup loading screen nothing had, and every override's fx was dropped), so every gun (player, NPCs, turrets,
  sentries, `NetShotMirror`) uses it. `ModFxPart` (on the sprite / model
  under the part's root, since GunRig sets the root's scale and rotation): camera facing, lifetime growth and fade through
  `_Color` (rgb additive, alpha otherwise); `PartAnimation.PlayOnce` restarts it (`ModFxPart.RestartAll`, also turning it
  to the camera at once) and `GunRig.MaxLength` counts its lifetime. A beam laser's projectile (`Gun.IsBeamLook`: 9-11 /
  228; a modder's report 2026-10-16: GunRig scales the beam's root (1, 1, length in game units), and a camera-facing sprite
  under that scale smeared across the screen, an `alongFlight` one took the ship's movement for its direction): a sprite
  is one game unit long from the root along +Z (`ModWeapons.FitBeamSprite`), `size` wide, turned about the root's +Z to
  face the camera (`ModFxPart.beam`; `grow` widens it only); a model sits on a "beam fit" node, `size` across and one
  unit long. Test `ModWeaponsTests.ABeamsSpriteRunsAlongTheBeamFacingTheCamera`; checked in an off-screen render. Loaded in the background with the rest
  (`ModLoading`, the menu's `Preload`). Verified in Play mode: the example's Plasma Lance (fired: violet bolts stretched
  along their flight, the flash at the gun, the ring impact, its own shot sound).
- **Weapon effects** (2026-11-01, the user's request: the Weapon fx kind adds whole effects and changes a game weapon's):
  `weaponfx.json` (`ModContent.WeaponEffectsFile`, `ModWeapons.Effects`): { "id", "name", "base", the fx fields }, keyed
  "mod id:id"; an item takes one with items.json `"fx": "<id>"` / `"<mod id>:<id>"` (`ParseEffect`: `File` is weaponfx.json
  meanwhile, for the messages; "id" / "name" / "base" no unknown fields), or a game weapon's whole look with `"fx": <item
  number>` (a Def with only `look`). `Def.look` = the effect's base: `LoadOne` copies that item's WeaponFx parts, shots and
  blast sounds over the item's own base copy before the effect's parts apply (so "the base's" part means the effect's
  base), keeping the item's turret model and explosion type. The builder project's effects are also built on their own on
  their base weapon (`Def.effect`, `ModWeapons.Effect(key)`) for the range. Tests `ModWeaponEffectsTests`.
- **Missile turrets and salvos** (remake, for mods; a modder's missile turret, 2026-10-14, whose own source patch was
  folded in this way): stats `missiles` 105 (a plain gun's / turret's shots are homing missiles, `Gun.missiles`: `Homing`,
  `IsMissile` = the rocket burst on a hit (`GunRig.ShowImpact`) and the rocket smoke trail (`RocketTrail.For`), no shrink;
  a turret's home on its auto target, else the radar lock, `PlayerTurret.MissileLock`), `salvoShots` 106 (side by side, 120
  units apart, each shot its own `Fired` for the multiplayer mirrors), `salvoCount` 107 / `salvoGapMs` 108 (a magazine:
  `Gun.WaitMs` is the gap inside it, else the reload; the cheats and mirrors reset to `WaitMs`), `turretRange` 109 (the auto
  turret's search range); not for beams, bombs, mines, cluster missiles, sentries, collectors. fx `"turret"`: the turret's
  own GLB (`ModWeapons.BuildTurret`: a `pivot` node holding a `*_gun` node, 1 unit = 1 m; `WeaponFx.modTurret`,
  `turretMuzzleUnits` / `turretShotUnits` for its `muzzle` / `shotStart`), used in flight and by `PlayerTurret.BuildStatic`
  (the hangar, other players' ships); a part without sprite / model takes `item` (another item's part, `ModWeapons.Copy`)
  and `scale` (on a node under a new root: GunRig sets the root's scale); `shot` / `explosionSound` an item's number (its
  sounds and shot looping). Same day, turret modding made whole: a turret GLB without `pivot` / `*_gun` turns as one piece
  (`BuildTurret` wraps it in a `pivot` and a `model_gun`); `"turret"` takes `scale` (its root) and `offset` [x, y, z] (game
  units in the mount's frame, on the pivot, undone by the root's scale and turn, `ModWeapons.Reshape`), and without `model`
  they resize / move the base item's turret (a copy of its `*_ship_mounted` prefab, so the hangar shows it too); `muzzle` /
  `shotStart` apply to the base model as well. **Turret mounts face any way** (`WeaponMount.turretUp` [x, y, z], game frame
  like `position_engine`; `TurretUpLocal` / `TurretTurn` = `FromToRotation(up, it)`, straight down the old `upsideDown`
  half turn about the length, which `upsideDown` still means; `TurretHangs` below the ship's horizon): the sides, the nose,
  any slant, in flight and the hangar (`PlayerTurret.PlaceOnMount`); the turret view's camera takes the turret's yaw axis as
  its up and sits on its outer side, upside down (hanging) the other way up with the stick mirrored, as the under-hull view
  was. Mod Builder: the Items page lists the remake-only stats the item's kind takes even when its base has none of them
  (`ModBuilderViewItems.ExtraStats`: missiles / salvos by `Gun.FiresPlainShots`, a turret's automatic / turn speed / range,
  the mining beam's, the phase cloak) and a turret's **Turret model** section (pick a .glb into `models/`, Size %, Offset X Y
  Z, and with its own model Muzzle / Shots start; `ProjectItem.TurretModel` / `TurretScale` / `TurretOffset` /
  `TurretMuzzle` / `TurretShotStart`, an empty `"turret"` dropped; each change reloads, so the turntable shows it); a turret
  hardpoint's **Turret faces** presets (Up / Down / Left / Right / Front / Back), **Match the hull here**
  (`ModBuilderGizmo.HullNormalAt`: short rays from just outside the point), and placing or dragging it over the hull faces it
  out of the surface (`WeaponMount.SetTurretUp(dir, snap)`: within 15 deg of a ship axis it takes the axis); Mirror flips its
  x; `ProjectShip.ToJson` writes `turretUp` only for a slant (`upsideDown` straight down, nothing straight up); turret mount
  edits reload the project. Verified in Play mode (the builder on a Veteran override): an exported turret_002 GLB and a
  plain ship GLB at 15 % on the turntable after the reload, the new sections, a mount faced Left sitting on the hull's left
  side (its up the pilot's left). Not tested: the turret view on a side mount in flight, a hand-picked file (the picker).
- **The game's own models** (remake, 2026-10-14, players' request; `Modding.GameModels`): wherever a mod takes a 3D model it
  may name a game assembly instead of a file, `"gof2:<assembly name>"` (reference.md's "Models and their textures"); also a
  game shop icon, `"icon": "gof2:item_046"` / `"gof2:ship_028"` (`GameModels.Icon`, Resources/GoF2Icons). Ships (new or
  override): no mod assembly or `ModShips` build (it only loads the icon), `Database.ShipModels[ship]` = the game assembly,
  which `ShipAssembly` hands out first (not with `original: true`, the stand-in); the model ship's hardpoints from
  `Database.GameMounts` (weapons_hd.json as shipped, cached; `ModContent.CopyModelMounts`) unless the entry has `mounts`;
  a new one's `CustomShipData.gameModelShip` (`GameModels.ShipOf`: "ship_NNN_*", v_ / sn_ too) gives its hangar height
  (`hangarHeight` -1 = that ship's, `StationTables.ModelShip` for `ShipY` and the pad lifts) and icon (`ItemInfo.ShipIcon`);
  `modelLength` / turns / offset / materials don't apply. Stations (`ModStations.LoadOne`): a copy of the prefab into
  `Build`, at its own size unless `modelSize` (`StationModel.size` 0). Turrets (`ModWeapons`): `"turret": "gof2:<name>"`
  (any model; `TurretRig` makes the missing `pivot` / `*_gun`, as for a plain GLB) or `"turret": { "item": n }` (that
  item's `*_ship_mounted` model and its `TurretCore.MuzzleUnits` / `ShotOffsetUnits`); `scale` / `offset` on both. Fx parts:
  `"model": "gof2:<name>"` (`GameModel`; with `texture` the meshes get copies for the white vertex colours, the game's
  own mesh assets untouched); also `"gof2:bra"` / `"gof2:beer"`, the ending's single-mesh props without an assembly, from
  `StoryAssets.menuStatics`, `GameModels.Prop`: the community's exploding bra, `Modding/Examples/exploding_bra`, a
  Fireworks-based bomb with the bra as its projectile). Mod Builder: **+ New ship on a game model** and the Ship tab's **Game model…**
  (`PickGameShip`, `NewShipOnGameModel` / `UseGameShipModel`, `ProjectShip.UseGameModel`: the model ship's stats, slots,
  race and hardpoints copied, the file-model fields dropped, no box-guessed first hardpoints (`NeedsLayout` false; the
  builder had put two guns, a secondary and an engine over the copied set), no auto hangar height or rendered icon; the
  model controls hidden; an override's Original model takes its own hardpoints back); the Items page's **Game item…** icon,
  **Game turret…** model (`ProjectItem.TurretItem`) and **Look and sound** (`BuildLook`: projectile / muzzle flash / impact
  from any game weapon with a size %, shot / blast sound; `ProjectItem.FxPartItem` / `FxPartScale` / `SoundItem`; parts a
  hand edit gave a sprite / model shown as "your own"). Tests `GameModelsTests`. Verified in Play mode: a new ship on the
  Veteran's model (its 12 hardpoints on the hull, slots 3 / 4 / 1 / 12, the hangar), an item with item 48's turret model,
  item 46's projectile and launch sound. Not tested: a `gof2:` station or fx part in flight.
- **Sound effects** (`ModSounds`, a mod's `sounds/<clip name>.ogg | .wav | .mp3`): replaces the game's clip of that name
  everywhere; `ModSounds.Get(clip)` wraps every `PlayOneShot` and `.clip =` site (57, not the music sources: ModMusic's),
  memoised per clip (AudioClip.name allocates), a no-op without mod sounds; later mods win; loaded decoded
  (`ModAudio.Load(..., compressed: false)`). Verified: `Target_Lock_v08` replaced. `ModAudio.Load` takes the type from
  the file's first bytes (`ModAudio.Sniff`: RIFF/WAVE, OggS, ID3 / an MPEG frame sync), the extension only when they
  show none: every platform's file picker copies to "picked.tmp", so the Mod Builder's `ModProject.SetAudio` saved every
  picked WAV / MP3 as .ogg and it "couldn't be played" (a player's report, 2026-10-24); `PickAudio` now hands it a copy
  named by its type. Tests `ModAudioTypeTests`. The content decides before the name is checked (`ModAudio.TypeOf(file,
  sniffed)`, #208): the Weapon fx page's "Sound file…" had saved a picked sound as `fx/<item>.tmp` (`AddFile` keeps the
  copy's extension), which the name alone refused, so the weapon kept its game sound on the new build too.
- **Ship and weapon sounds** (`ModShipSounds`): ships.json `"sounds"` (a new ship or an override; `ShipDef.sounds`,
  `ModContent.ShipSounds`) = engine / boostStart / boostLoop / boostStop / charger, each a file, a list (random) or { file(s),
  volume, pitch } with a value or [from, to]; a kit hardpoint rule's `"sound"` { shot, loop, release } (`Rule.sound`,
  `ModHardpoints.SoundOf` by the item's type as the slot). A file missing under its name is taken in another audio format of
  the same name (`Resolve`: the ES2 importer always names .ogg and keeps a .wav where it couldn't encode, and lists `audio/` in
  `multiplayerIgnore`, so builds on different computers still match). Files anywhere in the mod (not `sounds/`, ModSounds' replacements by
  name), loaded decoded with the rest (`Preload`, counted in `ModLoading`'s sounds). `PlayerEngine.UpdateOwn`: the engine by
  speed (standstill -> top speed: `MoveSpeed`, free flight `FreeVelocity`), boostStart / Stop on the boost's edges (random pitch
  in the range), boostLoop by `BoostVisualPercent`, the charger while recharging by `BoostRechargePercent`; a missing one keeps
  the game's. The engine also for `PlayerEngine.EngineClip` (hangar flights, other players) at 0.6 and NPCs flying the ship
  (`NpcShip`, event 46's voice slot) at 0.8. Weapons: `WeaponSystem` (`Rig.modSound`: `PlayShot` per shot via ShotVoices, a
  loop while held with the shot once at its start, released over `release` ms), `PlayerTurret`, `NetShotMirror` (the shooter's
  ship, `NetPlayer`; a looping rule keeps the weapon's own there). The ES2 importer writes them from EVERSPACE 2's cues
  (`tools/audio.py`, vgmstream through Wine). Verified in Play mode (the Gunship): every sound loads and resolves per
  weapon type, the engine plays, a boost runs the loop to pitch 1.5 and the stop, the charger climbs 0.7 -> 1.9 as it refills
  and stops when full. Not yet: the shots fired by hand, multiplayer.
- **New sounds, game lines replaced, voices per language** (2026-10-22, the user's requests): a `sounds/` file not named like
  a game clip is a new sound, played by name (`ModSounds.Named` / `Has`; `/sound <name>` sends Order.Sound a = -2 ->
  `EventScreen.PlayModSound`; the quest step's picker lists the project's own, the Play Sound node a Mod sound field); a
  `voices/` file named like one of the game's lines (`StoryAssets.IsVoice` / `VoiceNames`) replaces it, the story's
  included (`ModVoices.Replacement`, which `StoryAssets.Voice` asks first; loaded with the mods, `ModLoading`); mod lines
  and replacements in `voices/<language>/` for any of `LanguageTables.Offered` (`ModVoices.Languages`: with the voice option
  on auto the text language, then the game's bank de / en), `voices/` for every language. Builder: Audio's + New sound,
  + Voice line / Replace a game line with a language picker (each language's flag, a globe for every language:
  `UI.LanguageFlags`, painted in code, the game has no flag images); the pickers of the game's tracks, sounds and lines
  play each on ▶ (`ShowAudioPicker`: `Modding.GameAudioIndex`, Resources/GoF2ModBuilder, from GoF2 > Build > Mod Builder
  Audio Previews; `StoryAssets.OriginalVoice`; the quests' Sound step too), the station's music fading out meanwhile
  and back in 3 s after the last one (`StationLevel.MusicHeld`); Characters' Replace a game character (the speaker's own
  portrait rendered to a PNG, `Portrait.RenderSpeaker`, and name). Tests `ModAudioNamesTests`.
- **Voice-over** (`ModVoices`, `ModAudio`): "[voice <clip>]" in a Dialog page or a Radio line (`ModVoices.Take` in
  `NetAdmin.Apply`'s Dialog / Radio orders); the clip is an original voice line by its event name or a mod's
  `voices/<de|en>/<clip>.ogg|wav|mp3`, else `voices/<clip>.*`; loaded in the background (`Preload` when the order arrives;
  `ModAudio.Load`: UnityWebRequestMultimedia from the folder, a zip's file copied to temporaryCachePath/ModAudio first);
  `StoryAssets.Voice` falls back to `ModVoices.Get`, so the dialogue window and the radio box play it; `EventScreen` holds a
  queued dialogue up to 3 s while its clips load.
- **Music** (`ModMusic`): a mod's `music/<name>.*` named like an original clip (the asset names in Audio/MUSIC, DLC_MUSIC,
  DLC2_MUSIC) replaces it at every music start (`ModMusic.Replace` in Traffic, CampaignLevel.PlayMusic, StationLevel,
  MainMenu (swapped in when it loads, `ModMusic.Changed`), EndingCredits, StationMenu's Void alarm, EventScreen.PlayMusic);
  other names are new tracks: `/music <name>` (Order.Music a = -2, `EventScreen.PlayModMusic`), the Play Music node's Mod
  track, systems.json `spaceMusic` / `stationMusic` and stations.json `music` (`ModWorld.SpaceMusic` via
  `Traffic.RaceSpaceMusic`, `ModWorld.StationMusic`). All tracks load (kept compressed) when the mods change; the menu's
  Leave waits for `ModMusic.Ready` too.
- **One clip per audio file** (`ModAudio.Load`'s cache, 2026-10-30): music, sounds, voices, ship and weapon sounds share it;
  a new mod revision reads only files whose date / size changed (a zip mod: the zip's), and `ModAudio.Sweep` destroys the
  clips of changed or gone files and of mods turned off; a load ending after its file changed is dropped. Nobody else
  destroys these clips (ModWeapons doesn't own them, the builder's preview neither). Before, every reload (each Mod
  Builder edit) loaded every track and sound again and kept the old copies: a Discord report's crashes after replacing
  music. `ModProject.SetAudio` dates the copy now (File.Copy keeps the source's date). `ModAudioCacheTests`.
