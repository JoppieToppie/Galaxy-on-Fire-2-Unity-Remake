# Editor menu items

Notes moved from `CLAUDE.md` (its index lists every notes file). Code comments citing `CLAUDE.md "<section>"` mean the section or bullet of that name here.

## Layout

- **GoF2 > Scenes > Splash Scene**: `Assets/Scenes/Splash.unity` (build index 0), the startup logos (`StartupSplash`, see
  "UI and platforms"); turns Unity's own splash screen off. Main Menu Scene makes it too.
- **GoF2 > Scenes > Main Menu Scene**: `Assets/Scenes/MainMenu.unity` (build index 1; Space 2, Station 3).
- **GoF2 > Scenes > Space Scene**: `Assets/Scenes/Space.unity`, the flight level (see "Space scene"). Also (re)makes
  `Resources/GoF2Backdrop` and bakes the space skies if missing.
- **GoF2 > Scenes > Station Scene**: `Assets/Scenes/Station.unity`, the docked station (see "Station scene"). Wires the
  bar visitor prefabs, glow materials, music, ambience and language tables.
- **GoF2 > Scenes > Add Post Processing To Open Scene**: global Volume with `Assets/Settings/GoF2_VolumeProfile.asset`
  (Bloom, threshold 1) + camera post-processing on.
- **GoF2 > Build > Modding AI Reference** (`ModdingReferenceBuilder`): `Modding/ai/gof2-modding/reference.md`, every
  number and name of the original game a mod or event graph refers to (Android economy): items with every stat / raw
  attribute, item categories, attribute numbers, blueprint recipes, ships (model, hangar height), systems (sky),
  stations (race, planet, model), story steps, story speakers, every assembled model with the texture names a skin
  replaces, backdrop textures, music, sound effects, the event sound / music words, the built-in event graphs; ship hull
  sizes and every original ship's hardpoints as `mounts` JSON, the Default economy's differences, what each sky / sun /
  planet looks like (hand-written tables `SkyLooks` / `SunLooks` / `PlanetLooks`: update them when a texture changes);
  `voices.md` (every voice clip with its speaker and English line, for `[voice <clip>]`); `missions.md` +
  `mission.schema.json` (`Events.QuestSchema`, made from `QuestSteps` and `QuestGraph.ValueTypes`: every `.gof2mission` step,
  field, kind and default, a JSON Schema 2020-12 with shared `$defs` who / text / value / wire; a new step row there is in
  them at the next run); `events.md` (`ModdingEventsReference`: every event graph node's options and ports, read from
  `EventGraphNodes.cs` by a small tokenizer, so keep the node classes in that file's style, plus every command from
  `NetCommands.Reference`); and `guide.md`, a copy of `Modding/README.md`. For the AI modding guide (modders kept asking
  for ids). Run it again after changing the game data; headless: `Unity.exe -batchmode -nographics -projectPath . -executeMethod GoF2Remake.EditorTools.ModdingReferenceBuilder.Batch -quit`.
- **GoF2 > Build > Combat Assets**: `Resources/GoF2Combat/CombatAssets` (`CombatAssets`). Also run by Create Space Scene.
- **GoF2 > Build > Star Map Assets**: `Resources/GoF2StarMap/StarMapAssets` (`StarMapAssets`). Also run by Create Space
  Scene, and by Create Station Scene when missing.
- **GoF2 > Build > Network Prefabs**: `Resources/GoF2Net`, the multiplayer network prefabs (see "Multiplayer").
- **GoF2 > Build > Linux Dedicated Server** (`LinuxServerBuild`): `Build/LinuxServer/GoF2Server.x86_64`, Unity's
  Dedicated Server build (Linux, subtarget Server, IL2CPP, the dedicated server optimizations on: no texture / shader
  data; no audio either: `ServerAudioStrip` clears the AudioClip references of the Resources assets for the build, 4346
  clips / 171 MB, backed up in `Library/GoF2ServerAudioBackup` and put back afterwards (also on the next Editor load
  after a crash), the scene's clips in the build's copy (`AssetPostprocessor.OnProcessScene`); Minimal managed stripping
  (the server target had it off) and IL2CPP's OptimizeSize code generation, restored after the build like the target:
  GameAssembly.so 206 -> 82 MB, the zip 323 -> 126 MB), only the first scene; it starts as a server by itself
  (`UNITY_SERVER` in `DedicatedServer.Enabled`), `start-server.sh` beside it. Needs the Hub module "Linux Dedicated
  Server Build Support"; switches the active target for the build and back: the menu items switch, wait for the scripts
  to recompile and build after the domain reload (SessionState carries the job), since the toolchain packages only join
  the Linux build while the scripts are compiled for Linux (`UnityEditor.Sysroot` behind `UNITY_STANDALONE_LINUX_API`;
  built in the switch's own call: "No Linux sysroot found for x64"); `LinuxServerBuild.Busy` / `LastResult` for scripts.
  The game's code is 10 % of the server's generated C++ (packages, UI Toolkit, .NET and IL2CPP's generics the rest), so
  splitting client code into assemblies the server leaves out was judged not worth its refactor (2026-10-14).
  `LinuxServerBuild.Batch` is its command line (exits 1 on a failed build). Docker (`docker/`, SERVER.md "Docker"):
  `build-server.sh` builds it in GameCI's base image with the Editor of `ProjectVersion.txt` (the account only for the
  licence calls, never in the Editor's environment); **GoF2 > Build > Linux Dedicated Server (Release Zip)**
  (`LinuxServerBuild.ReleaseZip`) is the same build as `Build/Release/GoF2Remake-LinuxServer-<tag>.zip` (top folder
  GoF2Server, the tag = GOF2_BUILD_VERSION's date; the entries marked as made on Unix with their modes,
  `MarkMadeOnUnix`: .NET writes the host it runs on and unzip ignored the modes), built with a release's other platforms
  and attached to it. Every published release runs `.github/workflows/server-image.yml`: with that zip in the release it
  unpacks it (only `docker/` checked out, no licence) into the image, so the image is the release's own build; without
  it it builds the release's commit (textures, Reference/, Modding/ left as LFS pointers; secrets UNITY_EMAIL /
  UNITY_PASSWORD; a manual run takes a tag and optionally the shown version, GOF2_BUILD_VERSION) and attaches that zip
  to the release; either way it checks the server starts and pushes `ghcr.io/joppietoppie/gof2-server:<tag>` and
  `:latest` (not for a pre-release). The release tag must be the commit the release was built from, or the fingerprints
  differ.
- **GoF2 > Build > Backend Build Profiles** (`BuildProfilesBuilder`): `Assets/Settings/Build Profiles`, a profile per graphics
  backend, copies of the platform's profile with one API as a Player Settings override, and one each for macOS and iOS, Metal
  alone (`platforms.md` "Build times"). Run again after changing the Player Settings (an override is a frozen copy of them) or
  installing a platform's Editor module.
- **GoF2 > Build > Event Audio**: `Resources/GoF2Events/EventAudio`, the sounds and music the event graphs play (see "Multiplayer", Events).
- **GoF2 > Build > Hangar Heights**: `Resources/GoF2Data/hangar_heights.json`, how far each ship is lifted off each
  hangar pad (see "Station scene"). Run it again after changing a hangar room or a ship model.
- **GoF2 > Build > Nozzle Shapes** (`NozzleShapesBuilder`): `Resources/GoF2Data/nozzle_shapes.json`, each game ship's
  (0..63) exhaust nozzles as their outline (radius at 32 angles), extents and centre, measured on its player engine glow
  mesh (vertices to the nearest slot-3 mount; the game's meshes are only readable in the Editor), for the mods' exhaust
  styles (see "Mods", Exhaust styles). Run it again after changing a ship model or its exhaust mounts.
- **GoF2 > Build > Void Station Collision** (`VoidStationCollisionBuilder`):
  `Resources/GoF2Data/void_station_extra.json`, extra collision boxes for the Void station's outer blades and lower
  spire (see "Space scene", Alien orbit). Run it again after changing the station's model or animation.
- **GoF2 > Build > Hangar Shadows**: `Resources/GoF2Station/ShipShadows` (+ `ShipShadows.asset`,
  `HangarShipShadow.mat`), each ship's contact shadow in the hangars (see "Station scene"). Run it again after changing
  a ship model.
- **GoF2 > Build > HUD Images**: `Resources/GoF2Hud`, the HUD / star map images and the alien font glyphs cut from the
  original interface atlases (rects in `Reference/research/mining.md`, `autopilot_travel.md`, `starmap_travel.md`). Also
  run by Create Space Scene.
- **GoF2 > Build > Item Icons**: `Resources/GoF2Icons`, one icon per item and ship cut from the original atlases per
  `Reference/research/item_icons.json` (see "Shop").
- **GoF2 > Build > Hull Icons** (`HullIconBuilder`): `Resources/GoF2Icons/hull_<key>.png`, shop-style icons for the
  hulls without a shop image (the Debug Ships tab's "Not normally flyable": the freighters, the Terran battleship, the
  carrier, the Vossk battleship, the Valkyrie battlestation, the Void mother ship; 13-15's own images are a "?"): each
  model rendered in a preview scene from its left, above and a little ahead, the nose to the bottom left like the
  original's ship icons, a wide or tall hull turned to fill the icon, fitted to the pixels drawn (alpha from a render on
  black and one on white), over the ship frame 1276; read by `ItemInfo.HullIcon`. Run it again after changing those
  models.
- **GoF2 > Build > Text Icons**: `Resources/Sprite Assets/gof2_text_icons` (a TextCore sprite asset + its 64 px atlas,
  `TextIconsBuilder`): the dialogue's inline icons: a remake-drawn coin (and a heart, last in the atlas, for the main
  menu's credit line), the race emblems (GoF2Hud race_0/1/2/3/8/9, trimmed to their opaque part), the jumpgate /
  wormhole / blueprint (map_products) / ore core / container (crate_off) / autopilot icons, every item and ship shop
  icon (the centre square of the plate). The runtime panel text settings find it by name under `Sprite Assets/`, so no
  PanelTextSettings asset exists.
- **GoF2 > Build > Sky Layers**: `Resources/GoF2Backdrop/SkyLayerAssets` + the `sky_*` materials (see "Space scene"). Also run by Create Space Scene.
- **GoF2 > Build > Space Skies**: stars (3) and nebula (19, incl. Valkyrie/Supernova) layers as separate cubemaps for
  the flight levels (2048 px faces, Android 1365: at 1024 the 2048 px sky textures were magnified on a 1080p screen and
  the stars blurred).
- **GoF2 > Build > Materials And Prefabs**: one material per game material and one prefab per game mesh, from `resources.json`.
- **GoF2 > Build > Assembled Prefabs**: one prefab per game object, from `assemblies.json` (see "Assembled prefabs").
- **GoF2 > Build > Missing Prefabs Only** (`PrefabBuilder.BuildMissing` + `AssemblyBuilder.BuildMissing`): the per-mesh
  and assembled prefabs that don't exist yet, every existing one left alone (a model added by hand, e.g. the Terran
  hangar's bot e: remake mesh ids 99004 / 99005 in `resources.json`, assembly `hangar_terran_bot_e`, spawned on the room
  by `StationLevel.BuildHangar`, see "Station scene", Hangar service bots). It reads `resources.json` and
  `assemblies.json` as they are, so a hand-added model needs its entries in both first, and in the generators too or
  their next run drops it: `build_assemblies.py`'s MODEL_OVERRIDE (a remake-only id; 99001-99005 are taken) and a rule
  in `rules/assemblies_*.json` (or a `name_groups.json` member). The `resources.json` entries (the per-mesh prefab's
  material) are in no generator yet: `Reference/tools/asset_conversion/build_resources.py` would drop 99004 / 99005.
  Then run the menu item (an existing prefab is never updated: delete it first to rebuild it, or use Assembled
  Prefabs).
- **GoF2 > Import > Apply Emissive Glow To Materials**: `_Glow` = 4 on emissive/lights materials, 2.5 on additive ones,
  1 elsewhere (skyboxes never bloom; lit alpha-test layers neither, whatever the mesh is called: the wrecked station's
  `*_alpha_emissive` girders bloomed the red of their cut-away texels). Constants in `PostProcessing.cs`.
- **GoF2 > Import > Reimport Models Only**
- **GoF2 > Import > Reapply Import Settings**
- **GoF2 > Setup > Install Asset Pack**: one-time setup, already done.
- **GoF2 > Legacy > Flight Test Scene**: saves `Assets/Scenes/FlightTest.unity` (assembled Betty + chase camera), an old
  test scene: not kept in the repo or the build.
- **GoF2 > Legacy > Bake Combined Skyboxes**: the old combined sky bakes (`Skyboxes/`, stars layer not matched to the
  system); only the Flight Test scene uses them.
- **Assets > Create > GoF2 > Event Graph** / **Event Graph From Template**: a multiplayer event graph (see "Multiplayer", Events).
