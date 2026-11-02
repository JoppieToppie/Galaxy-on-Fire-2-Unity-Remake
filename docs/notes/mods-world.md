# Mods: world

Notes moved from `CLAUDE.md` (its index lists every notes file). Code comments citing `CLAUDE.md "<section>"` mean the section or bullet of that name here.

## Mods (remake-only)

- **Systems that come and go** (remake mods, 2026-10-18; a user's request: a system shown only while a mission runs):
  systems.json `"available"` (new systems and overrides; `ModUnlocks.SystemAvailable`, each condition read once a frame)
  shows the system while it holds and hides it while it doesn't, whatever was revealed; the command `/system [players]
  <system> <show | hide | reset>` (NetAdmin Order.System 32, `NetAdmin.ParseSystem`: number, "mod_id:id" or the whole
  name), the quest step `system` (field kind `QuestFieldKind.System`, the builder's system picker) and the graph node Show
  / Hide System (`EventSystemAction`) set `Session.SystemShown` (saved as `systemShown`, a mod's system by its key), which
  wins over the condition until reset (`GalaxyMap.SetShown`). `GalaxyMap.Visibility` returns the effective map: a copy of
  `Session.SystemVisible` (the revealed state, what is saved) with those applied (`GalaxyMap.Forced`), so a hidden system
  that comes back is as revealed as it was; a placeholder system (`ModWorld.IsMissingSystem`: past the original 34 and
  no mod that is on adds it, "?" at -1000 on every axis) is never shown, revealed or not, also on the builder's map (it
  zoomed the overview out to frame it, with no mod installed too: the registry keeps the numbers); routes, jumps, the
  lounge and the Wanted board follow it. The star map keeps its
  own copy and always shows the player's own system. A player in a system hidden on purpose (`GalaxyMap.ForcedHidden`) gets
  a HUD message and 4 s of game time later a stream-out arrival at `GalaxyMap.FallbackStation` (the nearest system on the
  map along the gate routes: its gate station, else its first; else the station last docked at, else the first visible
  system's, else Var Hastra) (`SpaceLevel.UpdateHiddenSystem`; not in cutscenes, jumps, the launch camera, death, the Void,
  an arena, a story orbit or the Mod Builder's own work); docked there nothing happens until the launch. The Mod Builder's
  Systems page has the condition ("On the map only when"). Stations have no condition of their own.
- **Systems and stations** (`ModWorld`, systems.json / stations.json): new systems after the original 34 (a race template,
  map position, sky, gates both ways, `gateStation`), new stations after the 135 (at most 7 per system, `looksLike` = the
  original station whose model / collision it uses, `StationLook` in `OrbitBuilder`), overrides; registry kinds System /
  Station; placeholders for a mod that is off (`ModSaves.FixWorld` docks a save there at Var Hastra). The Void's invasions,
  the ticker and random lounge stations skip mod / placeholder systems.
- **Station models, planets, suns** (`ModStations`, `ModBackdrop`; stations.json `model` / `modelSize` (largest extent, game
  units, default 40000) / `modelYaw` / `modelCentre` / `materials`, `collision` box | sphere | none or `volumes`, `interior`,
  `planetTexture`; systems.json `sunTexture` / `sunColor`; new or override, `ModWorld.StationModel`): the GLB loads with the
  ships in the background (`ModStations.LoadAll`, its own `TimeBudgetPerFrameDeferAgent`, textures through
  `ModMaterials.PreloadTexture`, the planet / sun PNGs too; `ModLoading` waits for it) into an always-visible AssembledObject
  template "modstation_NNN" (pack "mod", registered in `db.Assemblies`; `AssembledObject.LoadPrefab` hands it out); scaled to
  modelSize, turned, its bounds' centre at the origin. `OrbitBuilder.StationAssembly` / `AddObstacles` use it once built
  (`ModStations.Built`; else the looksLike original and its collision.json volumes): volumes from the placed model's bounds
  (`ModStations.Volumes`, axis-aligned) or the listed ones. `SpaceLevel.StationDockRange` = max(16000, radius + 6000) and
  `UndockPoint` z = max(10000, radius + 3000) game units for a mod model (radius = the obstacle's cubeHalf − 5000; NetTeleport
  uses it too). `StationLevel`: `ModWorld.InteriorRace` picks the hangar and bar. Backdrop: texture names "mod:<id>|<path>"
  (`ModWorld.PlanetTexture` / `SunTexture` in `OrbitLayout.BuildStarSystem`) make copies of planet_000_big / sun_000 with the
  PNG (`ModBackdrop.Material`); `sunColor` 1 = LIGHT0 1.5. `ModMaterials` clears its cache itself when the mods change
  (`Check`: ships, stations and backdrops share it). Example: `Modding/Examples/frontier_systems` (Kepler Prime's GLB from
  primitives, its banded planet, a Nivelian interior, the sun). Verified in Play mode: the model in its orbit at 2.5 km, the
  planet behind it, the sun, a ship placed inside pushed out to the box face, Dock offered there, the Nivelian hangar docked.
  A sun PNG with a bright glow out to its edge blows up when the sun swells near the screen centre: the originals fade to black
  well inside the edge (the guide says so).
  **System jumpgates** (remake, 2026-10-25, for GoF3's systems; systems.json `"jumpgate"`: model, size (default 15000),
  yaw, radius (default size / 2), materials; `ModWorld.GateModelOf` / `GateKey`): a `StationModel` with `gateSystem`,
  loaded by `ModStations` with the station models as "modgate_NNN" (category level; `Template`, `IsStationAssembly`);
  `OrbitLayout.ModJumpgateAssembly` / `JumpgateRadius` and `OrbitBuilder.SpawnJumpgate` (the race's gate while it isn't
  built). No `_jump_anim_add`, so `SystemJump.PlayGateJump` plays no flash. Tests `ModWorldTests.ASystemsOwnJumpgate`.
  **Star map planets** (remake, 2026-10-23, the user's request): stations.json `"mapTexture"` (an image wrapped round the
  map's sphere; `StarMap.ApplyModPlanet` swaps the planet mesh, whose UVs are one 0.2 x 0.2 cell of the
  galaxymap_planets atlas, for a generated UV sphere of the same radius (`WrappedSphere`, equirectangular) with a copy of
  the map material, still lit by `LightBySun`), else a `"planetTexture"` there as a disc facing the map camera
  (`FaceDiscs`, a copy of `ModAssets.fxAlpha`, the sphere's renderers off), else the game's planet; `"mapRing": true` adds
  `AddPlanetRing`'s copy of planet 9's ring (`ModWorld.MapTexture` / `MapRing`; the map image is preloaded with the other
  backdrops). The example's Kepler Prime has both (`textures/kepler_prime_map.png`).
  **Station lore** (remake, 2026-10-25, the user's request; stations.json `"subtitle"` / `"description"`, new or
  override, translated as `stations.<id>.subtitle` / `.description`; `ModWorld.Subtitle` / `Description` /
  `StationsWithLore`): the subtitle under the planet's name on the star map, the description in a panel at the map's
  left while that planet is picked in the system view (`StarMap.UpdatePlanetLore`, `.planet-lore`), and up to three
  stations' lore of the docked system in the station ticker in place of the game's two random items
  (`NewsTicker.Build`). The ES2 importer writes them from ES2's locations.CSV (Windows-1252, "|~"-separated). Tests
  `ModWorldTests.AStationsSubtitleAndLore`. Verified in Play mode at Union Bridge (the ticker, the map panel).
  **Pirate-held stations** (remake, 2026-11-02, a modder's suggestion "systems that belong to pirates like Shima at the
  start, but constantly"): systems.json `"pirateHeld": true` (`ModWorld.systemPirateHeld`) or a station's
  `"orbit": { "pirateHeld": true | false }` (wins) = `ModWorld.PirateHeld`; `Data.PirateHold.Held` (not the Kaamo Club, not
  for `PirateHold.FreedSeconds` 1800 of play after a win, not saved) makes `SpaceLevel` run `KaamoSiege.SetupHeld` instead
  of the traffic: the Kaamo siege's outposts and pirates, docking / gate / Khador refused, radio 435 at 5 s and 439 on the
  win (no Kaamo call, state or mission count); multiplayer as the Kaamo siege (one runner, `NetState.SiegeWonRpc` accepts a
  marked station, its own news). Builder: Systems "Held by pirates", a station's orbit "Held by pirates" (as the system /
  yes / no). Tests `ModWorldTests.StationPadsAndPirateHeldStations`.
  **Orbit layouts and map places** (remake, 2026-10-23, the user's request): stations.json `"orbit"` (`ModWorld.OrbitSpec`,
  `OrbitOf`; parsed with the rest, ores resolved in Apply) is applied by `OrbitLayout.ApplyMod` after the seeded layout:
  station (`hasStation` / `stationObject`: dock, `"nodock"`, none), docking pads outside (remake 2026-11-02, a modder's
  suggestion: `"pads"` like npcships.json's, `OrbitSpec.pads` as `SpacePoints` sets from `SpacePoints.StationPadBase`
  200000 + the station, `ModWorld.StationPads`; `SpaceLevel.SpawnStationPads` puts one model-less fixed `NpcShip` on the
  station, "station_pads", `ObjectDocking.StationPad` 5, `SpawnSpec.stationPort`: nobody's enemy, `Target.untargetable`,
  `LocalOnly` so every game builds its own; refused while the docking is restricted or pirates hold the station
  (`ObjectDocking.Refusal`); docked: `SpaceHooks.PadDocked`, the events' "on pad"), pirates holding it (`"pirateHeld"`,
  below), the visible jumpgate (a forced one at the spot
  `BuildGates` would roll for a gate orbit) and its position, `arrival` (`OrbitLayout.arrival`, first in
  `SpaceLevel.ArrivalPosition`), the asteroid field (`asteroidCount` 0 = none, also no autopilot entry; centre;
  `asteroidSpread` for `SpawnAsteroids`' cubes; type; `modOres` with weights: `OrbitBuilder.PickWeighted`, not the original's
  `PickOre`, which needs six entries), fog, the sun (`lightDirection` = -Direction(pitch, yaw), so the backdrop, light, flare,
  skybox `sunAligned` follow) and the orbit planet's size / yaw / pitch. The station stays the origin (everything around it
  assumes so); directions: yaw 0 behind a launching ship (the orbit planet's default), + to its right. `"mapPlace"`
  (`ModWorld.MapPlaceOf`) sets the planet's angle / radius in `GalaxyMap.SystemLayout` after its draws, so the other planets
  keep theirs. The builder's Stations page: Its own place on the star map, Its orbit (every field). The example's Rho
  Freeport has both. Tests `ModWorldTests.AnOrbitLaidOutByItsMod` / `AnOrbitWithoutStationOrAsteroids` and the errors. The builder's Stations page: the map
  image and the ring. Verified in Play mode: Kepler Prime's banded map planet lit from the sun with its ring, and with
  only planetTexture the disc.
- **Custom hangars and bars** (`ModInteriors`, a mod's `interiors.json`: id, type hangar | bar, model, scale, materials, fov
  (degrees), near / far (metres), ambient, lightIntensity, fog [r, g, b, end m]; hangar startYaw (Unity degrees), parkedMax,
  flights, cruise, gateSpan; stations.json `hangar` / `bar` = an interior key, `interior` still the race): the GLB loads with the
  station models (`ModStations.LoadInterior`), its glTF cameras / lights removed; marker nodes by name, positions only (room-root
  space, Unity metres): hangar `pad` (required), `camera` (required), `camera_target` (default the pad), `parked_N`, `gate` +
  `gate_out` (the flights' lane: `StationLevel.CustomLane`, gateSpan default gate y ± 30, cruise pad + 40), `light`; bar
  `camera` + `camera_target` (required), `camera_start` + `camera_start_target`, `visitor_N` (at least one), `light`. A missing
  required marker = a warning and the race's room. `StationLevel`: `customHangar` / `customBar` replace the room, the pads
  (`PadPosition` / `ParkedPosition`: marker + `StationTables.ShipY`, no lift table), the parked slots / max (`HangarTraffic`),
  the lane, the camera (drift around the marker, LookRotation to the target, `SetCustomLens`), the key light / ambient / fog
  (`SetCustomFog`), the visitor slots (`SlotFeet`) and the bar camera (A / B from the markers; the sway turns about the camera
  spot, not the room's origin); HangarIndex / BarRace stay the race's for the parked fighters, glows, billboards and music.
  Example: frontier_systems' kepler_hangar / kepler_bar (primitives at the originals' scale: hangar 600 x 200 x 600 m, bar
  300 x 70 x 300 m). Verified in Play mode: both rooms built, docked in the custom hangar (the pad, a parked ship, the camera),
  the fly-in through its gate, the custom bar (visitors on the markers with their plates, the windows showing space); the
  visitors read as backlit figures as in the original bars.
- **Skies** (systems.json `"skybox"`: nebula / stars images, stars 0-2 or "none", nebulaBrightness / starsBrightness,
  rotation [x, y, z] Unity degrees; `ModWorld.Skybox` / `SkyOf`): `OrbitBuilder.SetupSky` (not for a nebula override such as the
  prologue's) sets GoF2/SpaceSky's `_NebulaMap` / `_StarsMap` with the keywords `_NEBULA_PANORAMA` / `_NEBULA_STRIP` /
  `_STARS_*` (`multi_compile_local`, so a build keeps all nine variants) by the image's aspect (2:1 panorama, 6:1 cube-face
  strip; else a warning and the game's layer), `_NebulaGain` / `_StarsGain`, and a fixed `_SkyRotation` when given (else
  the per-station R_sky). The shader samples the images directly at mip 0 (no baking; no seam at the panorama's wrap, the
  strip's faces kept half a texel in): panorama u = atan2(x, z) / 2π + 0.5 (+Z in the middle, +X at 0.75), v =
  asin(y) / π + 0.5; strip order +X, -X, +Y, -Y, +Z, -Z, each as seen from inside (up face: front at its bottom, down face:
  at its top). Images through `ModBackdrop.Texture` / `ModMaterials` (preloaded with the backdrops, `ModWorld.BackdropTextures`).
  Example: frontier_systems' 2048 x 1024 Kepler nebula (generated, seamless) over the game's star layer 1. Verified in Play
  mode: a labelled debug strip with a zero rotation (each face on its axis, upright, the up / down faces as documented), the
  panorama with the per-station turn, no seam. `"sunAligned": true` (remake, 2026-10-23, the user's request) turns the sky
  in every orbit so its +Y points at that orbit's sun (`OrbitBuilder.SunAlignedSky`, the supernova system's R_sky; a sun on
  game x takes game z for its side axis instead of a zero vector), a `rotation` with it applied to the image first; the
  builder's Systems page has it. Test `ModWorldTests.ASkyFollowsTheSun`.
- **Characters** (`ModCharacters`, a mod's `characters.json`: id, name (per language), portrait PNG, race, gender, mirrored,
  background, frame, replaces): `NetAdmin.ResolveSpeaker` takes a character by key "mod_id:id" (before the story speakers),
  else after them by id or name (any language), "x as Name" renaming; the spec is "-3 US rename US key" (`Events.SpeakerSpec`,
  which also parses the others: -1 face, -2 reader, ≥ 0 story). `NetAdmin.SplitSpeaker` finds the speaker's colon by trying
  each colon in turn (a key holds one). Drawn by `Portrait.ShowCharacter` (the PNG covering the 160 x 200 layers, background
  cover, top-centred; the game's background under it and frame over it unless off (the "frame" `portrait_frame.png` is a
  faint full-box scanline overlay, alpha <= 27, not a border; the background a dark half-transparent fill, seen only
  through a transparent PNG; the builder calls them Scanlines / Dark backdrop); `mirrored` flips): DialogueView
  (`Page.character`), the radio box (`Traffic.Chatter.character`), EventScreen's question and mission card, the lounge
  client and the Missions window (`EventMissions.Offer.character`; the character's race / gender pick the bar figure).
  `replaces` (a story speaker by name / first name / number): `Portrait.ShowSpeaker` draws the character instead, story
  included, and `StoryTable.SpeakerName` gives its name when it has one (every header, the radio, `%player%`;
  `OriginalSpeakerName` for lookups: the replacement's own resolution, /dialog names, which find either).
  `TextReveal.Names` tints character names as people (rebuilt when `ModManager.Revision` changes). Portraits
  through `ModMaterials` (Preload from the main menu). Example: frontier_systems' Captain Vega. Verified in Play mode: a
  dialogue page by id, one by key with a rename, a replaced story speaker (mirrored), a radio call in flight.
- **Mod races** (remake, 2026-10-25, the user's request for the ES2 galaxy; called factions until 2026-10-28, renamed
  because the game itself only has races, and the multiplayer factions (`NetFactions`) are something else;
  `Modding.ModRaces`, a mod's `races.json`, an older `factions.json` read the same way): race numbers from
  `ModRaces.FirstRace` (32), numbered by the registry (`ModRegistry.Kind.Race`, "races" in mod_registry.json, the old
  "factions" read; afresh in a session), with a `look` race (0-3, 8, 9) for every table indexed by race (hangar, bar,
  visitors, music, NPC guns, freighter model, jumpgate: `Standing.Look`), an emblem (`ModRaces.Icon`), a standing of their
  own (`Session.ModRaceStanding`, key -> -100..100, saved as `modRaceStanding`, an old save's `factionStanding` read;
  hostile at -71, friend at 71; `hostile` = always an enemy) and relations (`ModRaces.Relation`, `Standing.RacesHostile`:
  unnamed = enemies if either is hostile or the other is pirates / Void / Specters, else neutral). `Standing` handles them
  everywhere the game races go (Toward / IsEnemy / IsFriend / ApplyKill / ApplyDelict / EnemyRaceOf / RaceName /
  RaceWord). systems.json `race` may name one, `"traffic": "mod"` (`ModWorld.ModTrafficOnly`: no game freighters, no
  pirate events), `locals`, `raiders` (`TrafficPlan`); ships.json `race` / `traffic.races` (resolved in
  `ModContent.ShipData`: JsonUtility can't read strings into ints; an unknown one is a mod warning, once:
  `ShipDef.refsReported`); a mod race's fighters always fly mods' ships (`ModTraffic.Pick` chance 1,
  `ModCapitalShips.ChanceFor`). Lounge diplomats in a mod race's system (`AgentGenerator.AddModRaceDiplomats`,
  `Agent.modRace` (saved as "faction" before: `FormerlySerializedAs`), `LoungeChat`: restores to 0). The Status window
  lists the non-hostile mod races in the game's reputation look (the race status bar, the emblem with a standing-coloured
  glow, `StatusWindow.RaceEmblem`). The station header, star map, lock plate and HUD use the mod race's name and emblem.
  campaign.json `raceStanding` { race: value } sets the start (`ModCampaigns.Start`, after the races are numbered);
  `/peace` (`Cheats.MakePeace`) sets the non-hostile ones to 0 too; `/standing` and `standing(race)` (event-graphs.md,
  chat-and-commands.md) change and read them. Multiplayer: each player's own standings
  (`NetModRaceStandings`, multiplayer.md "Standings per player"). Tests `ModRacesTests`.
