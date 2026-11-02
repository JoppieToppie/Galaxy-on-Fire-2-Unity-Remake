# Mod Builder

Notes moved from `CLAUDE.md` (its index lists every notes file). Code comments citing `CLAUDE.md "<section>"` mean the section or bullet of that name here.

## Mods (remake-only)

- **Mod Builder** (remake, experimental; every kind of content, see "Its other kinds" below): the main menu's Mods button opens a hub
  (`MainMenu.BuildModsHub`, `modsHubPanel`: Mod Browser / Mod Builder; Back from either returns to it). The projects page
  (`ModBuilderProjects`, `modBuilderPanel`, styles `.mb-*` in MainMenu.uss): `persistentDataPath/ModProjects/<id>`
  (`Modding.ModProject`: a mod folder in the normal format, mod.json / ships.json / models / textures, plus builder.json
  (never exported): each ship's test loadout, the test cheats, the last ship), the field above the list both searches it
  (every word in the name, id, author or description) and names a new project (Create / Enter), each row with what it
  holds ("1 ship · 1 skin",
  else Empty) and a picture (the first ship's icon, its own or the original's, else a skinned ship's, else an item's),
  New / name / author / version / description,
  the content kinds (Ships, Items, Characters, Skins open the builder on that kind, `ModBuilder.OpenKind`; the others greyed
  "Coming soon"), Open in hangar, Export as mod (zips it into the Mods folder as "<project name>.zip"; the mod id
  follows the name like a GitHub repository's: shown in grey under the Name field ("Mod id: x (was y)"), taken when the
  name is saved or left (`ModProject.Slug` / `IdFromName` / `ChangeId`: mod.json, the project's own "id:" keys, its
  folder, the old id kept in builder.json "previousIds" so the next export replaces the mod installed under it and keeps
  it on); zip mods are opened shared for delete, `ZipSource`, so the Editor no longer locks the old zip,
  replacing an installed mod of that id after asking, `ModImport.Delete`), Open folder, Delete. Opening one
  (`Modding.ModBuilder.Begin`): a throwaway free-play game (completed world) docked at the project's station (Dis, 70, until
  the top bar's Hangar button picks one station per hangar type: Terran Dis, Vossk, Nivelian, Midorian Var Hastra, deep
  science; builder.json "station", `ModBuilder.SwitchStation` reloads the scene, `CanHost` leaves out empty orbits, the Kaamo
  Club, the pirate bases and Loma; the test flights fly from that orbit), the project the only active
  mod (`ModManager.SetBuilderProject`: Active = the project and its dependencies, the enabled mods aside), nothing saved
  (`SaveGame.Save` / `AutoSave` refuse while `ModBuilder.Active`). The Station scene then shows `ModBuilderView`
  (`Resources/GoF2ModBuilder/ModBuilder.uss`) instead of the station menu (StationMenu hides its children and hands Update
  over): ships list (+ new ship from .glb via `ModImport.Pick(title, filter, exts)`, + change a game ship, remove), tabs
  Ship (name, description, race, stats, equipment slots, model; its length / scale +-10 %, turns about all three axes
  (ships.json `modelYaw` + the new `modelPitch` / `modelRoll`, `ModShipBuilder`: Euler(pitch, yaw, roll); a turn keeps the
  model's size and centre, `ModBuilderView.TurnModel`: modelLength and modelOffset follow it, the hardpoints stay; the
  builder sizes by the extent along the nose and turns about the file's origin, so a model standing up shrank as it was
  turned nose forward and one made far from its origin swung out of the hangar, a player's video 2026-10-25; modelLength in
  hundredths so turning back gives the size back; a new import whose box misses the pivot is centred on it, `AutoLayout`),
  position (the new
  `modelOffset`, game units in the ship's frame, nudges of 2 % and Centre on pivot; moves and resizes carry the hardpoints
  along, every change re-measures the hangar height), hangar height + Auto, hull textures: one
  `materials` entry for all meshes, for GLBs without their own), Hardpoints (add by clicking the hull, list, X / Y / Z, mirror,
  centre, delete, slot counts following the points, a turret's facing (see "Missile turrets and salvos"), engine flame
  shape / half width / half height / length / particles: `glowShape`, see mods-models.md "engine glow discs"), Loadout
  (per-slot item pickers, Auto-fill, god mode / infinite ammo). The 3D editor is `World.ModBuilderGizmo` (balls drawn over the
  hull by `Hidden/GoF2/BuilderGizmo`, Unity-style move arrows along the ship's axes (X = game +x = the pilot's left), a drag on
  a ball slides it over the hull (MeshColliders on layer 22), a flat arrow on the floor toward the nose); the camera is
  Inspect ship's orbit (`StationLevel.BeginInspect(minRadii)`; a middle-mouse drag pans its point, `HangarInspect.Pan`, at
  most 2 x the ship's radius off its centre, kept through rebuilds; a middle click centres it again). The view panel
  over the 3D view's top left (rework 2026-10-22, the user's request): a VIEW header bar that folds it
  to one bar and opens it again (PlayerPrefs `mb_view_panel`), then ENGINES and CAMERA sections; the camera buttons a
  3-column grid in the compact secondary-button look. Its Engines slider (off at the left, throttle up to 90 %,
  then the boost) lights the glow and a standing exhaust plume (`ShipExhaust.stationary` / `stationaryBoost`: a rate over
  time, the plume normally comes per distance flown). Below 1.58:1 / 1.4:1 the root takes `mbv-narrow` / `mbv-compact`
  (narrower panels, smaller text); the kinds strip scrolls sideways so Save / Exit stay on screen (rework 2026-10-22:
  the kinds are the game's tabs, dim with an amber underline, and so are the panels' own Ship / Hardpoints / Loadout
  tabs; the strip is sized to its tabs, the open one scrolled into view, and while it overflows a More button lists
  every kind in the options' drop-down, `ModBuilderScreen.OpenKindList`; each kind's Remove is greyed out while nothing
  in its list is picked, `UpdateRemoveButtons`; over the star map the panels are opaque, `.mbv-on-map`; a field label's
  long bracketed help goes on a dim line under it, `FieldLabel`). The panels fly out as the ship takes off and back in
  once it
  has landed, like the station menu (`ModBuilderView.UpdateUiAnimation`: 450 ms, ease-out cubic, each panel to its own
  edge and fading, one after another; no input meanwhile). Edits save at once; model-shaping ones (model, size, turn,
  engine points, hangar height, textures) re-read the project (`ModBuilder.Reload`) and rebuild the turntable ship when its
  model is ready. A new ship without hardpoints gets a first set from its bounds (two guns, a secondary, an engine) and its
  hangar height. A new ship gets its shop icon rendered once its model is built (`Modding.ShipIconMaker`, the Hull Icons
  builder's look at runtime: left side from above, orthographic, alpha from a black and a white render, over
  `Resources/GoF2Icons/frame_ship.png`, which **GoF2 > Build > Ship Icon Frame** / Hull Icons write) into `icons/<id>.png`
  and ships.json `icon`; builder.json keeps the file and the look it was made from (model file, turn, hull textures), so it
  is made again only when that changes, and an icon the modder set by hand is never replaced. The model is never turned for
  it (2026-10-23, the user's report: the Hull Icons builder's rule laying a hull wider than long along the icon turned
  wide-winged fighters such as the Betty, 39 x 28 m, nose away from the camera; that rule stays for the Editor's capital
  hulls; `IconKey`'s leading version 2 redraws the icons drawn before). The Ship tab's **Shop icon** row: the icon,
  Recreate icon (`MaybeMakeIcon(force)`, also over the modder's own; undoable) and -90 / +90 deg (`ModProject.IconTurn`,
  builder.json "iconTurns", in the key) for a model whose nose comes out elsewhere. Test fly: the project read again,
  the loadout mounted, the cheats granted (`Cheats.Grant`, honoured while
  `ModBuilder.Active`), the normal take-off (`StationLevel.Depart` now waits for the orbit camera's blend back first: the
  flight's view-edge shrink is computed from the camera as it starts) into the builder station's orbit: `SpaceLevel` passive (no traffic,
  wingmen), jumps / gate refused, no tutorial popups, `World.ModBuilderRange` keeps 3 hostile pirates coming (6 s after a
  death); docking, the pause menu's "Back to Mod Builder" or a death returns to the builder (`ModBuilder.EndTestFlight`).
  **Undo / redo** (`Modding.ModProjectHistory`, `ModBuilder.History`, made in `Begin`, gone in `End`): a step is the
  project's JSON (`ModProject.CaptureState` / `RestoreState`: the manifest but its id, ships, items, characters,
  builder.json but "station" / "lastShip" / "previousIds") and its models / textures / icons / portraits by content hash,
  each content kept once in `persistentDataPath/ModBuilderHistory/<project>_<ticks>` (a crash's leftovers pruned at the next
  Begin), so an overwritten skin texture or a removed model comes back; recorded at the save points (`SaveNow`'s debounced
  save, `ModBuilder.Reload`; nothing while unchanged, 100 steps), a ship's generated icon joins the last step
  (`Absorb`). The top bar's Undo / Redo (enabled by `CanUndo` / `CanRedo`; an unsaved edit counts) and Ctrl+Z / Ctrl+Y /
  Ctrl+Shift+Z (not while typing: a text field keeps its own) put the step back, save it, re-pick the ship / item /
  character by `Ref` and read the project again. Verified in Play mode (a Veteran override's name and a hardpoint, undone
  and redone, on disk too); the keys not through simulated input.
  The top bar's Menu (`Localization.Get(172)`, also Esc with nothing else open; `ModBuilderView.ShowMenu`): Resume, Options
  (the game's `OptionsView` in the builder's modal, Q / E switch tabs, Esc back to the Menu), Save, Exit the Mod Builder
  (`ModBuilder.End`: the main menu's projects page). The orbit camera turns and pans while the preview rebuilds (only the
  hardpoints wait for the ship), a re-begun orbit keeps its zoom (`HangarInspect.Begin`), and a drag whose release was missed
  ends on the next move; `ModTextures.Preload` resets its loading count (an earlier revision's loads left it above 0 for good,
  so the preview waited forever and the camera refused new drags). The top bar's kinds swap the side panels (`ModBuilderView.SelectKind`;
  the gizmo only with Ships): **Items** (`ModBuilderViewItems`, items.json as `ProjectItem`): new items from a game item
  (a copy: type, category, look) or changes to one, name, description, its own icon PNG, tech level, occurrence, the units
  a shop stocks (`stock`: 0 = the amount by item type, with a second row for a random range), prices
  and the base's stats by readable name (`ItemStats`; a new item's value equal to the base's isn't written), the
  remake-only stats of its kind and a turret's own model (see "Missile turrets and salvos"), "Fit to the
  test ship" (every slot of a weapon's kind, equipment in place of its category; a new item waits for the project to be
  read and numbered: `afterReload`); item edits reach the game when the kind is left or at Test fly. **Characters**
  (`ModBuilderViewCharacters`, characters.json as `ProjectCharacter`), edited in the station's Space Lounge (no ship,
  Test fly or Engines there): a new one from a PNG portrait, the conversation-style preview (`Portrait.ShowCharacter`),
  name, portrait, race, gender, mirrored / frame / background, and the optional story speaker it stands in for (a picker
  of the speakers' own portraits, `Portrait.ShowSpeaker(..., original: true)`); without one it is the mod's own character
  for its quests and events. **Skins** (`ModBuilderViewSkins`): the game ships the project re-skins
  (`ModProject.SkinShips`: builder.json "skins" + any `textures/(v_|sn_)ship_NNN_*` file), the ship on the turntable and
  in Test fly (`ModBuilder.SkinShip`, its own default loadout), its textures off the model's materials (the lights and
  engine glow layers too, marked Shared when other ships use them; a freighter's / the battleship's burnt wreck set,
  `CombatAssets.wreckDamaged`, which `ApplyWreckDamage` now gives the mod's textures through `ModTextures.OriginalOf` /
  `Replace`),
  each replaced by a PNG under the texture's exact name (`textures/<name>.png`, ModTextures; the project is
  read again) or saved as a template (`templates/`, never exported: blitted off the GPU, a normal map decoded from its
  BC5 / DXT5nm-style X Y to an RGB normal map). Overrides missing stats or slots get the game ship's when a project opens
  (`ProjectShip.FillFromGame`: a hand-written one had no slots). `ModProject.AddFile` never hands out a name twice in a
  run (ModMaterials keeps textures by path until the project is read again). Verified in Play mode: projects, open, a .glb ship (auto hardpoints,
  textures), a Phantom override, placing / dragging points, the loadout, test flight, dock back, exit, export. Not yet: a
  real file-picker run, controller use of the gizmo. Touch (2026-10-18): with touch input the gizmo's balls and arrows
  are 1.25x larger (1.5x until 2026-10-21) and picked within a fingertip (`ModBuilderGizmo.Reach`: 0.17 / 0.12 inch at
  `Screen.dpi`; 16 / 12 px at 1080 p were about a millimetre on a phone), the arrows from 45 % of their length so a tap
  on the ball moves the ball; the number fields (IntegerField / FloatField) take a tap's focus like text fields
  (`TextFieldKeys.InTextField`: the menus' touch handling had taken it away, so they couldn't be typed into on Android).
  Two fingers (2026-10-21, `UI.TwoFingerTouch`) pinch to zoom and move the
  camera in the hangar, space and room views, the middle button's pan (a phone could only turn them); under the Engines
  slider View buttons Front / Back / Left / Right / Top turn the hangar orbit to the ship's own sides (`ModBuilderView.ShowAngle`,
  `ModelViewer.Angles`, the pan centred, the distance kept; Top at the orbit's ~83 deg limit from a little behind, the
  nose up on screen, no view from below the pad) and
  Orbit puts the orbit back around the ship's centre after a pan (`CentreOrbit`, the middle click's `ResetPan`).
  **Its other kinds** (2026-10-15): the kinds are `ModBuilder.Kind` (12; 15 with Kits, Races and AiTools) and each is
  edited where it lives in the game (`ModBuilder.PlaceOf`: Ships / Items / Skins / Blueprints / Campaign in the hangar,
  Characters / Quests / Audio in the Space Lounge, Systems and Races on the star map, Stations / Weapon fx in space, Hangars
  & bars in the room itself); a kind of the other
  scene loads it (`ModBuilder.GoTo`, `ReloadSpace`, `ReloadStation`). The screens share `ModBuilderScreen` (top bar with all
  kinds, Undo / Redo, Menu with Dependencies, the modal pickers, the save / reload cycle, the status line):
  `ModBuilderView` over the Station scene (partials per kind: ...Blueprints, ...Campaign, ...Interiors, ...Quests, ...Audio,
  ...Systems, ...Kinds the dispatch) and `ModBuilderSpaceView` over the Space scene (FlightHud hands it Update while
  `ModBuilder.InSpace`; `SpaceLevel.HoldForBuilder`: the player's ship held, no traffic, the chase camera off; its own orbit
  camera; ...SpaceViewFx). Project files: `ProjectSystem` / `ProjectStation` / `ProjectInterior` (ProjectWorld.cs),
  `ProjectRace` (ProjectWorld.cs, races.json; see "Races kind" below),
  `ProjectBlueprint` / `ProjectCampaign` / `ProjectGameOption` (ProjectGame.cs), `ProjectQuest` (events/*.gof2mission), audio as
  files (music/, sounds/, voices/); all in the undo steps. **References** (`ModRefs`): every picker of other content
  (`PickRef` / `RefField`) lists the game's, the project's own (by key) and every other installed mod's (read from its files,
  on or off); another mod's pick makes it a dependency (`ModProject.AddDependency`, with the installed version as the lowest),
  `ModsNamed` the mods the files name. **Test fly** only in Ships, Items and Skins (`ModBuilderView.Flies`); Blueprints /
  Campaign show their ship on the turntable (`ModBuilder.PreviewShip` / `PreviewLoadout`). **Play** (`ModBuilderPlay`,
  `ModBuilder.PlayTesting`): a play test, the game with the project on (the station menu, traffic, quests:
  `LocalEvents` runs no quests in the builder otherwise), from the builder's station, the campaign's start or a quest under
  way (a bar mission instead: `EventMissions.PlayTestOffer`, offered in every bar while the play test runs, whatever its
  chance, stations and pilot count, taken in the lounge like a player; `EventRunner.IsBarMission`; the MCP's play_test
  too); the pause / station menu's Back to Mod Builder, a death or a failed quest end it (`EndPlayTest`: the builder's
  game
  as it opened). Blueprints: product, ingredients, skin ship, autocomplete, condition, lounge / derelict / drops. Campaign:
  the card, start, equipment, cargo, standing, story quest, mod-only galaxy / items / ships, traffic ships; new-game options.
  Missions (called quests until 2026-10-23, the user's request: "Mission" everywhere a player or modder reads it; the
  code keeps its Quest names): `.gof2mission` files with the format `gof2mission` (`QuestFile.Extension` / `Format`,
  `ProjectQuest`'s copies a layer below), the old `.gof2quest` / `gof2quest` still read (`LegacyExtension` /
  `LegacyFormat`) and written back as `.gof2mission` by the builder (the old file deleted, `ModProject.WriteQuests`); the
  kinds are Story mission (the file's kind "quest", `EventKind.StoryMission`), Bar mission and Event; the MCP tools
  `compile_mission` / `get_mission_schema`, the entry kind `missions` and the argument `mission`, the old names mapped in
  `ModBuilderMcp.FromOldNames`; the expressions `quest(name)`, the steps `startquest` / `questorbit` and campaign.json's
  `"quest"` unchanged (mods use them). `.gof2mission` step lists compiled by `Events.QuestFile` (EventRunner reads the extension; steps dialog, radio,
  objective, goto, fight, cargo, reward, give, wait, waituntil, checkpoint, music, title, waypoint, startquest, complete,
  fail, command; the expressions `docked(station)` / `inorbit(station)` added for them). Audio: game tracks / sounds
  replaced (`Resources/GoF2ModBuilder/SoundNames.txt`, Assets/Audio's SFX folders), new tracks, voice lines, ▶ previews.
  Systems: `StarMap.OpenBuilder` (every system, the builder's own amber and dragged: `MoveSun`, picks reported, no travel,
  drawn under the builder's panels); name, owner, security, position, gates, gate station, sky, nebula / stars / sun images,
  sunlight, music, its stations. Stations: system, tech level, planet, the look (like a game station, a .glb or a game model,
  size, turn, centre), collision, interior race / own hangar / bar, planet image, music; Play docks there. Hangars & bars:
  `ModBuilder.PreviewInterior` makes `StationLevel` build the project's room, `StationLevel.FreeCamera` the orbit camera,
  markers as balls clicked into the room (MeshColliders on layer 21) into interiors.json "markers" (`ModInteriors`: they win
  over the model's nodes); a room on a game room (`gof2:hangar_terran`, `ModStations.LoadInterior`) gets its markers from
  `StationLevel.GameRoomMarkers`. Weapon fx: per part the base's, a game part (another item's, scale, recoloured), a sprite,
  a model or none; shot / blast sounds; the trail (items.json fx "trail": none, or colour / width / length, also on a weapon
  without one: `WeaponFx.trail` (`TrailLook`, not serialized), `RocketTrail.For(gun, look)`, `GunRig.EnableTrails`); a
  range where the parked ship fires at a target ship 300-450 m off, seen from the side (Gun + GunRig like NetShotMirror; the
  target's hit cube 35 m: at 900 m every shot hit as it left the gun). Tests: `ModBuilderKindsTests`.
  **A modder's suggestions** (2026-11-02, le zoink's list on Discord): Stations' orbit section has "Held by pirates" and
  "Docking pads outside the station" (a list: where the ship rests, its own approach point, its own facing, + Docking pad;
  mods-world.md "Pirate-held stations" / the pads); in the shown orbit (Show its orbit) the jumpgate (cyan), arrivals
  (amber) and pads (red) are `ModBuilderGizmo` balls bound to the station itself (`ModBuilderSpaceViewOrbit`: its 180 deg
  turn makes `WeaponSystem.MountToLocal` + the station's rotation = `OrbitLayout.ToUnity`, so the balls' game units are the
  orbit's), dragged over the hull or along the arrows (Ctrl: 100 units) and saved on release (the orbit built again).
  Systems: "Held by pirates". Missions: Take an item, Bring cargo's "Hand it in", Restrict travel's "The Khador Drive only",
  Spawn's hull x / gun damage x, Spawn an NPC ship type (an NPC ship type picker, `RefKind.NpcShip`: the game's five, the
  project's, other mods'), When something happens "docks at a station's pad outside" (event-graphs.md). A bar mission's "Who
  can take it" got a note: "One squad in the universe" (the default) was already the suggested "not while another squad
  runs it" (`EventMissions.Offers` / `OnAccept`).
  **NPC ships** (2026-11-02, `ModBuilder.Kind.NpcShips` before AiTools, KindCount 16, `Place.Space`;
  `ModBuilderSpaceViewNpc`, `ProjectNpcShip` (npcships.json, `ModProject.NpcShips`, undo key "npcShips", MCP entry kind
  `npcShips`, select_entry too): + New NPC ship (a capital ship on the battleship's / carrier's / Vossk battleship's hull,
  a freighter, a boss, or any of them on a ship's hull, `PickRef(RefKind.Ship)`), Change a game NPC ship (the five game
  keys, another mod's by key), Remove. The page: name, kind, hull (a ship's or a game look), race, side, hull / hull x /
  guns x / speed / explosion, missiles, retreat; hardpoints (the game hull's turrets copied in, back to the hull's own,
  per point kind / XYZ / turret item / upside down / model / hull / name / a hangar's ships and timing); escorts; loot
  (the game's / its own / nothing); where and how often (chance, count, races, systems, stations, security, ranks, story
  steps, free play / story only, the game's capital ship's place, condition, distance, a place of its own; a game type
  its chance, the freighters' count); dock and shop (pads, the game's pads copied in, standing, race ship, signature,
  repair and its price, markup, cells, ammo, gear, sells); launches. The hull is built beside the orbit's station
  (`PreviewSpec` -> `Traffic.AssemblyOf` -> `AssembledObject.LoadPrefab`, a mod hull retried every 0.5 s until built)
  and the hangar's `ModBuilderGizmo` binds to it (hardpoints cyan / amber, pads red): a ball drags over the hull (Ctrl:
  100-unit snap) or along its arrows, "Click the hull to add" a turret or a pad; every edit saves and re-reads the project
  (the page and the preview follow). Not yet tried in Play mode.
  **Ship kits** (2026-10-16, #87's customizable ships as a builder kind; `ModBuilder.Kind.Kits`, before AiTools, KindCount
  14; `ModBuilderViewKits`, `ProjectKit` / `ProjectKitPart` in ProjectKits.cs, `ModProject.Kits`, file shipkits.json, in the
  undo steps as "kits", MCP entry kind `kits`): the kit (name, scale, yaw), its slots (add, rename, reorder, remove; a
  build text's own keys refused, `ModShipKits.Build.Reserved`), parts per slot from a .glb (copied to models/), a part's
  name, slot, ship types, model, tier models, stats, weapon slots and hardpoints (the hangar gizmo edits
  `ModBuilderView.EditedMounts`: the part's in this kind, else the ship's; slot counts follow the points only for ships,
  `SlotsFollow`), paint / lights slots (`ProjectKit.AddColorSlot`, a "paint" palette made once); colours, presets, skins,
  hardpoint models by Edit as JSON. The turntable shows the kit's first ship with the selected part (and preview tier) in
  its slot: `ModBuilder.PreviewBuild` written into `Session.ShipBuilds` by `ApplyShip` only in this kind (cleared for the
  builder's ships otherwise, so Ships shows the default build); the station rebuilding the ship once the parts load is
  picked up by `ModBuilderView.RebindShip`. Ships kind: "+ New ship from a kit", a kit ship's section (kit from the project
  or a dependency, kit type, default build per slot, tier; `ProjectShip.Kit` / `KitType` / `BuildPart` / `BuildTier`), no
  model section, no auto layout; its icon from the default build. A kit with `"parts": []` is a warning and left out
  (it was an error that broke the mod). `ProjectShip.MountJson` / `Copy` keep #87's mount fields (outline, builtIn,
  direction, rotation, nodes, fxScale): the builder used to drop them. Tests: `ModBuilderKitsTests`. Verified in Play mode
  (a two-part test kit): the kind's page, the part swap on the turntable, a turret point added to a part, the Ships page's
  kit section and icon, a test flight in the kit ship.
  **Quest graph** (2026-10-15, user's request "a full graph in the game just like in Unity"): every step type is a row of
  `Events.QuestSteps` (type, picker group, fields with their kind, the line it makes; the steps needing more, blocks
  included, are written by `QuestFile`), so a new step there is editable at once. Blocks (if / while / repeat / parallel /
  every / on / ask / vote / random) hold nested step lists; `who` on every step; quest `variables` (param / set), kinds
  quest / mission / event, `autoComplete`; `{variable}` kept in texts (`QuestFile.Clean` with the known names). The graph
  (`Events.QuestGraph`, plain): flow wires ARE the step tree (Next = the next in its list, a block output its list, Start
  the quest's `steps`; one wire into a node, like Graph Toolkit), unwired chains in `loose`, places in `pos` / `startPos`,
  value nodes in `values` wired into fields as `{ "$value": id }` (`QuestReader.E` / `Arg` / `S` / `Ref` resolve them,
  `QuestGraph.Resolve`). `UI.QuestGraphView` draws it (UI Toolkit: pan, zoom, bezier wires by Painter2D on a 100 km layer,
  drag to wire / cut / move, double-click for a step, Delete); `ModBuilderViewQuests`' Graph view (default; PlayerPrefs
  `mb_quest_graph`, Wider hides the list) with the inspector on the right, or List view. Newtonsoft trap: `new JArray(tokens)`
  COPIES tokens that still have a parent (`AddLooseChain` removes them first). **Unity graphs**: `QuestFile.FromScript` turns
  an event graph's script into steps (the lines it has no step for stay `command` steps; every editor template comes back
  line for line), `FromGraphFile` places them where the Unity nodes stood (`EventGraphFile` now reads `m_Position`, the
  script's `#@id` markers); the Quests kind imports a .gof2event converted or as it is, and converts a project's graph.
  Also: **Edit as JSON…** on every entry page (`ModBuilderScreen.EditJson`, in place: `ProjectEntry.Json`,
  `ProjectShip.Json` / `ReadMounts`), the items' and ships' less common fields (`ModBuilderViewMore`: price systems, Vossk
  only, always sold at, available, attributes by number, Default Economy prices; available, dealer, lounge, npcTurrets,
  priceDefault, engine flame), a system's stars brightness / fixed sky turn, Menu > **Mod details** (website, credits,
  preview.png) and **Translations** (`ModProject.TranslatableTexts` / `Texts` / `SetText`, text/<lang>.json), Skins >
  **Other textures…** (any game texture, the GoF2Backdrop list, `backdrop.json`). Tests: `QuestGraphTests`. Verified in
  Play mode: every kind's page, a quest compiled and played (its
  conversation in the station, back to the builder), a system on the map with its route, a game hangar as the project's
  room with its markers, a project station in its own orbit, a laser recoloured and a project missile hitting the target
  ship. Not yet: a system dragged by hand, a marker clicked by hand, the campaign play test.
- **Races kind** (remake, 2026-10-28; the mods' own races, `ModRaces`, called factions until that day; `ModBuilder.Kind.Races`
  before AiTools, KindCount 15, on the star map beside Systems: `ModBuilderView.OnMap` keeps the map open between the two,
  only Systems drags suns, a tapped system picks the project's race that owns it and the status line names its owner).
  Model: `ProjectRace` (ProjectWorld.cs: name, description, look, icon, colour, standing, hostile, diplomats, gun,
  relations; unknown fields kept), `ModProject.Races` (races.json; a project's old factions.json, `ModRaces.OldFile`, is
  read when there is no races.json and deleted at the next save), in the undo steps as "races", its texts in
  Translations as `races.<id>.name` / `.description`; `ModProjectRaces.cs`: `AddRace`, `OwnRace` (an id or the project's
  key), `RaceUses`, `RemoveRace` (systems it owned and ships it built back to its look race, a system to Terran for a
  pirates / Void look; locals, raiders, traffic races and other races' relations naming it removed). A race reference is
  kept as the string it was: `ProjectSystem.Race` / `ProjectShip.Race` read -1 for a mod race (`ModRace` the string; a
  race word reads as its game race), `Locals` / `Raiders` / `TrafficRaces` lists of strings (numbers stay numbers),
  `ModTraffic` ("traffic": "mod"); only a pick writes them (`ProjectSystem.Race` used to read "race" as an int clamped to
  0-3: a mod race's system showed as Terran and an edit overwrote its owner). `ModRefs`' `RefKind.Race`: the game's races
  by word (`RaceWords`, Specters too), the project's and other mods' by key (another mod's makes it a dependency);
  `RaceValue` maps a number / word / own id to the entries' values. UI (`ModBuilderViewRaces`, fields in the main file):
  list (emblem framed in its colour, look, standing or hostile, systems owned), + New race, Remove (says how many
  references change); page: name, description, look (segments), emblem (a PNG into `icons/race_<id>`), colour, start
  standing (number, -100..100), hostile, diplomats, gun (an item picker), relations (friend / neutral / enemy segments, ×,
  + Relation to a race), its systems (Edit opens Systems on one). Elsewhere: Ships' Built by and Systems' Owned by
  (`RaceOrModRace`: the game races' segments plus "Mod race…", picked from the list and named under it), Systems' Patrolled
  by / Raided by and "Only the mods' ships fly here" (`SystemRaceLists`, also for overrides), the Ship tab's traffic races
  under NPC turrets (`RaceListField`, game races written as numbers), and a mission step's Race field (spawn, provoke,
  standing): the field's own values first, then the project's races and those of the mods that are on (`ModRaces.All`) by
  key, in the picker instead of segments once there are any (`RaceFieldChoices`). MCP: see mod-builder-mcp.md "Races".
  Tests: `ModBuilderRacesTests` (round trip, the old file, a race-owned system and ship through an edit, removal). Not
  checked in Play mode yet.
- **Leaving Play mode inside the builder** (Editor `ModBuilderPlayModeReset`, `ModBuilder.CloseAfterPlayMode`): Play mode runs
  without a domain reload and the builder's statics reset only when Play starts again, so after stopping Play mode with a
  project open it stayed the active mod and saving stayed refused: edit-mode tests run then saw its ships (65 instead of 64)
  and failed every save test (47). On entering Edit mode the session is dropped (the project saved, no scene change), the MCP
  server stopped and `McpInput`'s devices removed.
- **Builder star map under the panels** (`StarMap.OnEnable` / `baseSortingOrder`): the builder's map set its panel to "its own
  order - 1", but by then the panel settings were the map's runtime copy, 20 above the shared asset's, so the map (its names,
  frames) drew over the builder's panels; now the shared asset's order - 1.
- Orbit drags (remake, players' request): a vertical drag takes the camera down / up the other way round (a drag up tips the
  ship up) in Inspect ship, Action Freeze (`PhotoMode`, the original's y flip dropped) and the Mod Builder; keys and sticks
  unchanged.
- **Mods into the builder** (remake, 2026-10-22, the user's request: a downloaded mod as a project): the projects page's
  Import (`ModBuilderProjects.ToggleImport`: "From a file…" (`ModImport.Pick`, a zip read through `ModImport.Inspect` /
  `ZipSource`) and every installed mod, "has a project" on those whose id a project already has) and the Mod Browser's
  "Edit in Mod Builder" (`ModBrowser.EditInBuilder`, `ModBuilderProjects.ImportInstalled`). `ModProject.CopyMod` (worker
  thread, polled from the panel's scheduler) copies the mod's files (`ModSource.Files`; never its builder.json or a path
  out of the folder) into `ModProjects/.<id>.import`, then moves it into place (a project of that id is replaced after the
  menu's question, else `<id>_N`); `List` skips dot folders. builder.json `keepId` (`ModProject.KeepsId`): the project's id
  stays the mod's whatever its name (`IdFromName`), so Export replaces the installed mod. Esc / B closes the import list
  first (`CancelImport`). Tests in `ModBuilderKindsTests`.
- **Builder screen fixes** (2026-10-22): the view panel, the Test fly button, the placing hint and the quest graph carry
  `SafeArea.EdgeClass` (`KeepClear` gave them margins every 500 ms: they sat off their places on wide windows); a button
  text starting "+ " (or a lone + / − stepper) gets its sign drawn by Painter2D (`ModBuilderScreen.DrawSign`: the font's
  + snapped to unequal 1 / 2 px bars at the UI's non-integer scale; cyan for adding, white on hover, the rest of the text in
  `mbv-button-label`); since 2026-11-01 only buttons that add carry the + ("+ Import an event graph…", the dependencies'
  "+ Add" too) and those changing what the game or the mod already has (class `mbv-change`: Change a game ship / item /
  station / system / weapon's look, Replace a game track / sound / line / character, Skin a game ship, the other
  textures, Make this mod a campaign, Change it) a filled pencil (`DrawPencil`), their texts without "+ " in every
  language; Weapon fx gained "+ New weapon effect" (2026-11-01: a weapon effect of weaponfx.json, `ProjectWeaponEffect`,
  `ModProject.WeaponEffects`, based on a game weapon picked; its page: name, base, the weapons using it, then the same
  parts / trail / sounds editor through `Fx(make)` / `FxBase` / `FxFileId`; the range fires it on its base weapon,
  `ModWeapons.Effect`; Remove effect clears the items' links, `RemoveWeaponEffect`). A weapon taking an effect or a game
  look (`ProjectItem.EffectId` / `GameLook` / `TakesLook`) shows which, with "Edit the effect" / "Its own look instead";
  the Items page's "Weapon effect" picks one of the project's effects or any game weapon's look (`EffectChoices`; a
  turret taking one hides its turret model section: the effect's applies). AI tools: entry kind `weaponEffects`,
  select_entry `weaponEffects`, the range's item `effect:<id>`; `ScrollBars` puts the bar at the viewport's edge and `.gof-scroll-bar` centres itself with
  `translate: -50%` (subtracting the snapped half width each layout fed back into the next: the bar's width flickered by a
  pixel twice a second).
- **The builder translated** (2026-10-22): its 875 `Localization.Extra` texts in all 13 `extra_<lang>.json` (the kinds,
  panels, pickers, tooltips, the AI tools page; races by each table's own names: Russian Земляне / Восски / Нивелийцы /
  Мидорианцы / Войд, Chinese Terran人 ..., Japanese テラ人 ..., Korean and Hindi in English). Checked in Play mode in German
  (the kinds strip overflows into its More button) and Japanese (the OS font fallback).
- **Optimize models and textures** (remake, 2026-10-22, the user's request: a 61.7 MB ship model had been made 4 MB by hand
  before; `Modding.ModOptimizer` + `MeshSimplifier`, the window `UI.ModBuilderScreenOptimize`: the Menu's entry and the Ship
  tab's **Optimize…** beside the model): the project's `.glb` models and image files (`Candidates`: not templates/ or
  preview.png) with their sizes and a model's triangles (`CountTriangles`, the JSON chunk only), ticked from 1 MB; the
  largest texture 1K / 2K / 4K, JPEG quality high / normal / small (92 / 85 / 70), the model textures as JPEG where possible
  or kept PNG, the triangles kept all / 75 / 50 / 25 % (PlayerPrefs `mb_opt_*`). Each embedded PNG / JPG image is decoded on
  the main thread (`Texture2D.LoadImage`, one per frame), halved with a 2 x 2 box filter until it fits and encoded on a worker
  thread (`ImageConversion.EncodeArrayToJPG` / `PNG`, thread safe), a JPEG unless its alpha is used; a project image file
  keeps its format (the JSON names it). Polygons: `MeshSimplifier` (quadric edge collapse onto an existing neighbour vertex,
  so UVs / normals / skins stay valid; quadrics summed per position; seams (several vertices at one point) and open /
  non-manifold edges locked; a collapse turning a triangle by more than 60 degrees refused) per indexed float-position
  primitive without morph targets, its unused vertices dropped when it owns its attributes (`ReducePrimitive`, POSITION
  min / max recomputed, other min / max removed, 16- or 32-bit indices), a view another accessor reads left for a new one,
  views nothing reads shrunk to 4 bytes; not for Draco / meshopt / quantized models. The GLB written again with every
  bufferView packed in order (ModGlbImages' layout rules); a result that isn't smaller leaves the file alone. Vertices
  alike in every attribute are joined first, at any
  triangle setting (`ModOptimizer.Weld`; the simplifier counts only the vertices a triangle uses as a seam): a model
  with three vertices per triangle (an .aem import, which `AemImport.Model.Welded` now joins itself too) was all seams
  and lost nothing; the Narn now 79 865 -> 19 965 triangles at 25 %, 8.6 -> 0.7 MB. The window shows the whole mod's
  size (`ModProject.ModSize`: the files `ExportedFiles` hands Export, before zipping). History: a
  commit before and the reload after, so Undo puts the originals back (checked). Measured on SR2_Normandy.glb (nine 4096 px
  PNGs, 18 856 triangles): 61.7 MB -> 2.7 MB at 1K in 2.5 s; at 50 % triangles 15 436 (its many seams locked). WebP / KTX2
  aren't offered: neither Unity's decoders nor the mods' loaders read them, and the game compresses every texture to DXT /
  ETC2 on load (ModTextureEncoder) whatever the file was. Tests `ModOptimizerTests`.
- **Original-game mods in the builder** (remake, 2026-10-22, the user's request with Kinzer's Narn Bin'Tak dreadnought for
  ship 16): `Modding.AemImport` reads the game's `.aem` (the C# of `Reference/tools/asset_conversion/aem.py`: AEMesh /
  V2-V5, every part and child joined in its rest pose, animations dropped) and writes a `.glb` (one mesh, material "hull",
  the engine's (x, y, z) as glTF's: glTFast's x mirror gives the remake's ship-relative (-x, y, z); metres; V flipped: the
  game samples memory row 0 at v = 0; 32-bit indices). `ModBuilderScreen.PickGlb`, every model picker (ships, items'
  turrets, stations, fx, rooms, kit parts), takes `.aem` too and converts it first; a ship's `modelLength` becomes the
  `.aem`'s own length (`pickedAemLength`). `Modding.OriginalModImport`: a zip with `.aem` / `.aei` and no mod.json
  (`Looks`) is made into a project from the projects page's Import > From a file... (`ModBuilderProjects.
  StartOriginalImport`, a worker thread; the Mod Browser's Import points there): a ship whose assembly name
  (`ShipsByModel`, not 13-15) has a `<name>.aem` becomes an override with that model at its length (its stats and
  hardpoints copied) and the `<name>_diffuse` / `_normal_specular` `.aei` as its hull textures (RGB normal map; the alpha,
  the game's specular, as the smoothness: `split_spec.py`'s rule); its additive layers become add parts (2026-10-23, the
  user's request with a Full HD and a first-release Betty: ships.json `addParts`, `CustomAddPart` model / role / texture,
  `ModShipBuilder.BuildAddPart`: each part's glTF in a holder that takes the hull model's place, turn and size (the
  original's parts share the hull's origin; no transforms in their files), its renderers named with the game's ending
  (`_lights_add` ... `CustomAddPart.Suffix`, which collision, shadows, LODs, aim spots and the icon skip), white vertex
  colours where missing (GoF2/Additive multiplies them), the game's material for the role (`GameAddMaterial`: that part
  on the overridden ship's own prefab, else on a game ship of its race: lights / NPC engine 3460x glow_<race>, player glow
  34813), a copy with the part's `texture`, else its model's own base colour texture (2026-10-30, a player's report: a
  custom engine glow showed the race's glow sprite; `ModShipBuilder.OwnTexture`: the parts' glTF `_BaseMap`, empty for
  the `.aem` imports' untextured parts, which keep the game's; never read off the mod Lit templates, whose first property
  read in the Editor drops their `_EMISSION` and dirties them), set or cleared on the builder's Glowing parts row (Texture… /
  No texture, `ProjectShip.SetPartTexture`); lights always on, `engineGlow` under the engine glow part in place of the
  generated discs, `engine` the NPC variant (`npcVariantParts`); `ModShips.LoadAddParts` loads them with the hull;
  checked: the Full HD Betty's parts come out with the game prefab's exact vertices, bounds and UVs): HD names
  `_lights_add` / `_engine_glow_add` / `_engine_add`, the first release's `ship_NN` + `_lights` / `_add` (its engine
  glow) with its race atlas `tex_<race>_ships.aei` (`RaceAtlas`) as the hull's diffuse and the parts' texture; copies of the
  hull (byte-identical, or `SameShape`: the same triangle count and size within a unit) and `_lod_N` left out; the builder's
  Ship tab lists them (Glowing parts: remove, + Lights / Engine glow / NPC engine glow from a .glb / .aem), a switch to a kit
  or a game model drops them. Not yet: the first release's fume samples panels of `tex_mido_ships` (its UVs point
  elsewhere: probably another texture of that release, not in the files we have); a zip only (no .7z). Any other `.aei` is a skin under its own
  name (`ModTextures`; a `_normal_specular` one with its `_metallic_smoothness`); other `.aem` files are reported unused.
  `ModShipBuilder` now makes tangents (`RecalculateTangents`) for a readable mesh without them under a ships.json normal
  map (glTFast makes them only for a glTF normal texture, so these maps shaded wrong). Checked in Play mode with the Narn
  zip (79 865 triangles, ~100 m, 2048 px textures): the project, the report, the textured hull on the turntable, nose
  forward. Tests `OriginalModImportTests`. Not yet: a station's or weapon's `.aem` as its replacement.
