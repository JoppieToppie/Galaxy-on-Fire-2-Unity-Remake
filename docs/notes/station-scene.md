# Station scene

Notes moved from `CLAUDE.md` (its index lists every notes file). Code comments citing `CLAUDE.md "<section>"` mean the section or bullet of that name here.

## Station scene

`Station.unity` holds a camera, one light, post-processing, `StationLevel` and the station menu (`UI/Station`,
`StationMenu`). The level builds the current station's hangar and bar at startup. Switching between them is instant,
with no fades, like `ModStation`. Research: `Reference/research/station_interior.md`. All tables (camera positions,
slots, ship heights, fighters per race, music) are in `StationTables`.

- **Hangar index:** station 101 uses the battlestation hangar, 100 the deep science one, every other station its system's race.
  - Rooms: the assembled `hangar_*` prefabs sit at identity; the `bar_*` prefabs at Unity yaw 180 (game identity).
    Verified in Unity: ships sit on the pads and visitors stand on the floor.
  - Camera conversion: a game order-2 rotation (x, y, z) becomes `Quaternion.Euler(-x, -y, z)`. This frames the ship right of centre, like the original.
- **Hangar** (the main station view):
  - The player's ship sits on a turntable at (0, Y[ship], 0), with the NPC engine meshes and the exhaust off.
  - Turning the ship: drag (1 rad per quarter of the screen height, i.e. the original's 120 px of 480, with fling), keyboard A/D, or the right stick.
  - 0..N parked ships: 70 % of the hangar's race, 30 % a random race or pirates.
  - Camera: the phone table, relative to the ship; FOV 0.8 rad vertical; slow random position drift.
  - Lighting: one fixed light from the camera side, a flat per-race ambient, and fog in Vossk hangars.
  - Depth of field (remake option, Graphics "Hangar depth of field", `Settings.HangarDepthOfField`, off by default (it
    was on except on phones until 2026-10); `StationLevel.SetupDepthOfField` / `UpdateDepthOfField`): URP's DoF on a
    volume of its own (layer 26, only in the station camera's volume mask, so the star map and the item window stay
    sharp; an instance profile, which `Bootstrap.ApplyPostProcessing` leaves alone), focused each frame on the player's
    ship: Bokeh, a 300 mm lens at f/2 (the hangar cameras keep ~130 m from the turntable: the ~60 m ship sharp, the room
    behind soft), Gaussian on phones (focus + 25 .. + 140 m); off in the bar, under the star map and in VR. Meanwhile
    the room's URP Lit materials drop their specular highlights (`CollectRoomHighlights` / `SetRoomMatte`: copies with
    `_SPECULARHIGHLIGHTS_OFF`, swapped in while the blur is on): the key light's glints on the glossy parts (the Terran
    forcefield's top beam) popped in and out as the camera drifted, and the blur spread each into a flickering blob,
    worst at the screen's top edge (Bokeh or Gaussian, with or without DLSS; measured with the camera stepped like its
    drift: 0.7-1 % of the top strip flickering, 0 without them). URP strips the depth of field shaders from a build
    unless a volume profile asset has it on (the code-made profile isn't seen):
    `Assets/Settings/HangarDepthOfFieldShaders.asset` (Bokeh on, used by nothing) keeps them; without it the builds
    logged "BokehDepthOfField ... has been stripped" and showed no blur.
- **Space Lounge:**
  - 3-4 generic visitors (the race and gender rules of `Generator::createAgent`) on random slots, as camera-facing billboards with a glow and a floor shadow.
    Remake (#174, `StationLevel.UpdateBillboards`): upright (turned about the vertical only; the original's look-at with
    the camera's up tipped them with the camera's pitch and sway), the glow 100 units behind the figure's drawn centre
    (the quads aren't centred on their pivots, and the Terran bars turn the figure round), the shadow under that centre.
    The centres and the height are taken at spawn from the meshes' own bounds (`VisitorMeshBounds`), not
    `Renderer.bounds`: a multiplayer event's visitor (`AddVisitor`, by RPC) can come while the bar is hidden, and an
    inactive renderer's bounds are empty (its glow and shadow sat off the figure, its glow material wasn't set).
  - Camera: a 3 s intro on the first visit (a tap skips it), then a slow sway. Remake: while the player talks to a
    visitor (`LoungePanel.OpenChat` -> `StationLevel.FocusVisitor`) the view turns sideways only (the room camera's
    pitch and roll kept: tilted down to them it looked odd) so they stand beside the chat window, in the middle of the
    room left of it (`StationLevel.ChatFrameX`, set by `LoungePanel` from the window's place), and zooms in a quarter,
    eased over 0.8 s (smoothstep) on top of the sway, and back when the chat closes; not in VR. The chat window (#82) is
    940 wide at the bottom right (phones 900, 80 / 140 from the edges, clear of rounded corners and the hint row); it
    spanned the screen from the side panel's edge and covered the visitors. A visitor standing in the right half of the
    view at the click keeps to the right: the window goes to the bottom left (`.chat-window--left`,
    `LoungePanel.chatOnLeft`) and `ChatFrameX` frames the visitor in the middle of the room right of it (the user's
    request, 2026-10-18; it was always the window on the right).
  - Visitor list (#175, remake; review 2026-10-28): a drawer at the right edge (`LoungePanel.SetDrawer`, Lounge.uss
    `.lounge-list--closed`), closed until its arrow tab is clicked: open, it covered the bar's right side. A row taking
    the focus by keys or a controller opens it (any focus while not `InputMode.MousePointing` or touch: the station's
    first focus, `LoungePanel.FocusFirst`, and the arrows); a `NavigationMoveEvent` on the list never arrives
    (`StationMenu.OnNavigate`, trickle-down on the root, stops every one), so keyboard players walked hidden rows and
    Enter opened chats they couldn't see. Closed, the rows' first 40 units stay beside the tab at opacity 0: their
    picking is off then, and a row keeping the focus (closed with the mouse) gives it up.
  - Lighting: the system's sun, with skybox ambient.
  - Anti-aliasing (remake, `StationLevel.ApplyAntialiasing`, both rooms): URP's TAA on the station camera, SMAA when a temporal
    upscaler (DLSS / FSR 2+ / STP) is active or MSAA is on; the thin glossy parts (the Nivelian bar stools, the Midorian
    window frames) shimmered under SMAA as the camera swayed. Flight keeps SMAA.
  - The Terran bot loops; the Midorian prop replays at random.
  - Remake: the Vossk bar's bartender arms. The original ships `bar_vossk_bot_a/b/c_anim_alpha` without resource ids and
    never shows them (CutScene::process's Vossk branch restarts two slots, +0x30 every 20000 ms and +0x34 every 22000 ms,
    that only the Terran bar fills). Their tracks are two arms behind the counter: bot_b (15.5 s) the arm along the bar,
    bot_a (15 s, turning in place) and bot_c (20.5 s, down the bar and back) two animations of the arm at its end. They are
    in the bar_vossk assembly under remake mesh ids 99001-99003 (`build_assemblies.py` MODEL_OVERRIDE; per-mesh prefabs
    `Prefabs/main/bars/bar_vossk_bot_*` made by hand with the room's material 20059, as the Terran bot has its room's, the
    game prefabs' rotation signs -1 / -1 / -1; the bar_vossk entry was spliced into `assemblies.json`, whose freighter LOD
    lists were fixed by hand after the script, and only `bar_vossk.prefab` rebuilt). `VosskBarBots` (plain C#) plays the
    dead slots: bot_b every 20 s, the end arm bot_a or bot_c at random every 22 s (the other hidden), the first plays
    within 4 s; `StationLevel.UpdateVosskBots` holds them in their first pose the first time the bar shows (until then the
    models sit at the room's origin). Tests `VosskBarBotsTests`; checked in Play mode at V'ikka (all three playing, the
    swap, the rest poses).
  - Remake: the Vossk bar remade (2026-10-25, Blender with the AEM plugin): `bar_vossk`, `bar_vossk_anim_add` and the
    three arms replaced in place (same .meta GUIDs). `bar_vossk_alpha` is gone: its geometry is part of
    `bar_vossk_anim_add` now (the per-mesh prefab gives the new parts the layer's material; it had no resource id and
    was never shown). The arms are keyed heading first, so `StationLevel.BuildBar` sets `PartAnimation.headingFirst` on
    them, like the Terran hangar's bots (see "Hangar service bots").
  - Remake (`BarFlybys`): every 6-20 s a ship (30 % a small formation) flies past outside the windows. Once per visit, from the
    camera's rest pose, two tiny renders against different background colours find the see-through pixels and one with
    `Hidden/GoF2/BarDistance` the room's nearest surfaces; a path is flown only if no point of it is ever in front of the
    room from anywhere in the camera's sway (the nearest-surface limit widened over ±14 px). Vossk: its fog would hide them,
    so its flybys (layer 27, left out of the bar camera) are drawn by a render pass of its own (`BarFlybys.FlybyPass`, after
    the transparents, against the bar's depth, `unity_FogParams` set to no fog while it draws; only while a flyby is in the
    air). It was an overlay camera stacked on the bar camera (PR #109): a camera stack turns URP's temporal upscalers off
    (DLSS, FSR 3, STP), so the station dropped to the plain render scale from the first lounge visit on.
- **Menu:**
  - Layout: header; system, tech level and race; Hangar (opens the shop window, see "Shop"), Space Lounge and Map (the
    star map, see "Star map and system travel") buttons; **Launch**, refused while the cargo hold is overloaded (204);
    the original's "Depart the station?" (397) is left out (remake: it launches at once).
  - Credits and cargo (remake, players' request; `StationMenu.UpdateFunds`, `.station-funds` in StationMenu.uss): the
    hangar window footer's two plates ("CARGO 65 / 195 t" with text 184, red when overloaded; the credits in amber) at
    the top right under the bar, on the main view and in the Space Lounge (its visitor list moved down under them), read
    every frame and written when a value changes (lounge deals, payouts, medal rewards, fines, multiplayer trades,
    tolls); they fly up and away with the bar in the menu's UI animation; hidden under the hangar window (its own
    footer), the Missions and Status windows and the star map.
  - Input modes work like the flight HUD: touch; keyboard 1 / 2 / M / L / Esc (Launch shows its key: L / the
    controller's X glyph before its text); controller LB / RB / Y / X / B / Menu. The number keys are the PC version's
    "Menu button 1 - 9" (3356, its key table is still in the Android binary: a tap on `Globals::sub_menu_buttons`): on
    the main view 1 Hangar, 2 Lounge, 3 Map, 4 Missions, 5 Status; in the Space Lounge 1-9 (and the keypad's) pick its
    visitors in the list's order instead, numbered "1." on the rows with keys and mouse (remake, #75,
    `LoungePanel.NumberKey`); in the hangar window 1 / 2 / 3 = Ship / Shop (Store) / Blueprints
    (`HangarWindow::initialize`), 4 / 5 nothing; Q / E (LB / RB) cycle the tabs. No hint row on the main view (the
    buttons say it); toasts sit at the bottom, above the ticker.
  - Esc or B steps back: dialog, then lounge, then the system menu (Save game, Back to Main Menu).
  - The mouse takes the selection to the `station-button` it is over (`PointerOverEvent` on the root, like MainMenu's
    `HookFocusSound`): a menu opened with the mouse focused its first entry, which stayed lit beside the hovered one. The
    hover highlight itself (`.can-hover`) shows only while the mouse was the last input (`InputMode.MousePointing`, the
    main menu too): a cursor resting over a row kept it lit while the keys / a controller moved the focus, two rows at
    once at every station docked at (a player's report, 2026-10-18).
  - Music per race and station; ambience per screen as the FMOD cycle events (`CycleSound`): 122 Mainview, 95 Hangar
    while the hangar window is open (`StationLevel.SetHangarWindowOpen`, `ModStation::OnKeyPress`), 108 Lounge.
- Missions (129, unlocks with the Map) and Status (169): see "Bar and freelance".
- **System menu** (`StationMenu`, `SysPage` Main / Save / Load / Options): 30 Save game, 29 Load (the slot list), 31
  Options (the `OptionControl` rows), 522 Back to Main Menu; Back at the top right. 28 New game and 43 About are built
  and wired but hidden (`BuildSystemExtras`): the main menu has both, and `SystemMenuItems` skips what is hidden.
- **New medals** (`ShowNextMedal`, `checkForNewMedal`): the reward 5000 / 2500 / 1000 by grade (none on Extreme);
  remake: instead of the original's ChoiceWindow per medal that waits for OK, a toast at the top centre (`.medal-toast`
  in StationMenu.uss: the plate `StatusWindow.MedalPlate`, 353, the name, the reward; border, top band and title in the
  grade's colour `StatusWindow.MedalTint`; drops in and grows with a short flash; the remake's own sound
  `Resources/GoF2Sfx/MedalToast`, synthesized by `Reference/tools/audio/medal_sound`) for 4 / 5 / 6 / 7 s (bronze /
  silver / gold / elite; 3 s while more medals wait, the last one its full time; the pointer resting on it pauses the
  countdown), then it fades and the next medal follows; it doesn't hold up the station's other checks; a tap / click
  opens the Status window on that medal (`OpenMedal`, `StatusWindow.SelectMedal`); medal hints 649 / 650 / 651 and the
  blueprint hints 232 / 3233 (`CheckMedalHints`); first-visit hints (622 hangar, 627 lounge, 635 missions, 640 phones,
  628 / 631 on the map via `StarMap.ShowHint`); the hangar's selection hints 587-589.
- **Inspect ship** (remake, the side menu's "Inspect ship" under Status (hangar view only, not in VR), keys 6 / I, the
  controller's right stick press; `World.HangarInspect` (plain C#) + `StationLevel.BeginInspect` / `EndInspect`, input
  in `StationMenu.UpdateInspect`): the camera leaves its drifting hangar spot and orbits the player's ship like the
  flight's Action Freeze (`PhotoMode`): yaw / pitch around the hull's centre (its meshes' box in the ship's own frame,
  no glow layers), starting where the hangar camera is and zooming in to 2.1x the hull's radius (1.15x..4x), a 0.9 s
  eased blend in and back out, the pitch kept above the pad (the hull's lowest point + 1.5 m), the near plane pulled in
  to stay in front of the hull; drag / one finger turns (0.005 rad per 1080p pixel, the fling x0.9 a 30 fps frame), the
  wheel, a pinch, + / - (Page Up / Down) and the triggers zoom, the arrows / W A S D and both sticks turn. The menu
  flies out meanwhile (the UI animation, `UiBlocked`), a footer keeps Back and the hints row at the top right the
  controls; Hide UI (H, the controller's Y; a short click / tap or H / Y brings it back, a drag still turns) and
  Screenshot (Enter / P, the controller's A: `PhotoMode.Store`, no UI in it, 55 / 56) are clickable hints (taps on
  touch). Back: Esc, Backspace, B, 6 / I, the right stick press. The depth of field focuses on the hull's centre
  meanwhile and turns Gaussian (also in the Mod Builder's orbit): sharp to a little past the hull, the room behind
  blurred (300 mm at f/2 kept ~6 m sharp up close, a 50 mm lens at the orbit's ~80 m blurred nothing). Leaving the
  hangar view drops it (`HangarInspect.Reset`); a take-off ends it. The rooms are only modelled where the original
  camera looks, so some angles see past them. Action Freeze's photo tools work here too (the free camera and the Camera
  settings panel, see "UI and platforms"); the free camera's reach shows more of the rooms' unmodelled sides.
  - Camera collision (#190): the hangar rooms are imported readable (`AssetImport`: under `/hangars/`, not `_add` /
    `_anim`; `ModelOrientationPostprocessor.GetVersion` 2 reimports the models of a Library made before) and get
    MeshColliders when Inspect begins (`StationLevel.AddRoomColliders`: child objects on layer 25, not the additive /
    animated layers or the bots; made again when the room changed or any is gone; an unreadable mesh is logged once),
    the player's ship too (`AddShipColliders`, layer 20, not its glow, lights or engines). Layer 25, not 21: the Mod
    Builder's interiors view puts MeshColliders of its own on the room's meshes (`ModBuilderViewInteriors.RoomLayer` 21)
    and destroys them when it ends, and `AddRoomColliders` skipped the meshes that had one, so the room was left
    without walls (the Mod Builder also orbits with `BeginInspect(0.55f)`). The casts (`StationLevel.CameraCast`) use a
    0.4 m sphere: a sphere cast skips a collider it starts in and each room is one MeshCollider, so the 1.5 m sphere,
    starting in contact (the free camera's floor is the hull's lowest point + 1.5 m; a small ship's centre over its
    pad), skipped every wall; where the sphere starts touching, a ray along the move stands in too, stopped 0.4 m short.
    The orbit stops 0.5 m short of the sphere's contact (0.9 m from the wall; `HangarInspect.Blocked` ->
    `RoomClearance`): in at once, out at once where nothing held it (the zoom eases by itself), out from a wall eased
    by the time passed (~0.3 s; it was a per-frame 0.15 lerp: frame-rate bound, lagging every zoom-out); the free
    camera's moves stop at the walls and the hull and slide along them (`FreeCamera.Collide` -> `RoomMove`); the near
    plane stays under 0.25 m meanwhile (its corners within the 0.4 m sphere), or it cut through the wall it touched.
    The Mod Builder MCP's captures wait for `HangarInspect.Settled` (the blend, the zoom and the ease out done;
    `ModBuilderViewAi.CameraSettled`).
- The turret (`CutScene::checkForTurret`): the item's `hangar_turret_item_N` assembly on the turntable ship's slot-2
  mount, turned (0, pi, 0) except the plasma collectors, still; rebuilt when the turret item changes
  (`StationLevel.RefreshTurret`). Remake (`TurretCore.Display`, `StationLevel.AddNpcTurrets`): the NPC ships parked and
  landing there show their turrets the same way (their type's `Database.NpcTurrets`, else 45 / 51 their fighter turret
  `turret_002_static` at its spot), the Kaamo Club's stored hulls their own mounted turrets, other players' ships theirs
  (`NetHangar`); `HangarTraffic.NpcSpawned` tells NPC ships from guests.
- **Hangar flights** (remake-only, option "Hangar arrival and take-off", `Settings.HangarFlights`; the original cuts
  straight between space and the parked ship): `HangarFlight` (plain C#) flies a ship along its hangar's lane
  (`StationTables.HangarLanes`, Unity metres measured on the room meshes: the forcefield's centre and outward direction,
  the height span it can be passed at, a cruise height above the parked ships; Vossk: the emblem portal on −x, bays
  facing the ring's centre, so the ships swing through the centre (every bend is a wide fillet) and fly into the bay 5 m
  up; the remodelled room's central column stands there, so they go round it instead, see "Vossk hangar, remodel").
  Arrival: from 450 m outside (growing from nothing, so nothing pops in where the opening shows space) through the
  forcefield, level (the Nivelian hangar's 12 roof pillars, `StationTables.HangarLane.pillars` measured on its mesh,
  are flown round: `HangarFlight.PillarRoute`, an A* over a 4 m grid keeping each pillar's radius + the ship's half
  width + 10 m clear and favouring the middle of the gaps, string-pulled, cached per pad and width; a player's report
  2026-10-16, ships flew through the pillar in front of the right pad; checked with every hangar ship's outline: 10.9 m
  at the closest), at up to 200 m/s, braking hard, then gently over the last 25 m through a rounded corner with a slight
  flare to a stop over the pad, a turn on the spot (65 deg/s, a little bank) to the parking yaw, straight down.
  Departure: the reverse (straight up, the turn at the top, the corner, out, shrinking away; the ship stays hidden at
  the end, not back at full size for the frame before Space loads). Where the path leaves (enters) the hangar camera's
  view the ship shrinks to nothing over the last (grows over the first) 160 m in view (at most 60 % of the lane in
  view), so it never crosses the screen edge at full size (a pad just outside the view, the Midorian hangar's right one,
  isn't such an edge: the ship takes off there at full size and flies into the view; it vanished over the pad and
  crossed the hangar unseen); the engine loop takes the space engines' rolloff (`EngineVoices.Setup3D`, to 500 m) and
  fades with the ship's size. Landings and take-offs are always vertical. The camera keeps the original framing (the
  rooms are only modelled where the 16:9 hangar camera looks). Only the player's own docking flies in
  (`SpaceLevel.Dock(true)`: the autopilot, touching the station, the Dock prompt, `Session.DockedFromSpace`); the
  story's moves into a station (the rescue, arrests, "docked at ...") and loads don't. `Launch` / `Depart` take off
  first (the story's direct launches into a story orbit don't). The station menu is away meanwhile: it flies / fades out
  as the take-off starts and back in once the ship has landed (`StationMenu.UpdateUiAnimation`, 450 ms, ease-out cubic:
  the top bar slides up, Launch right, the ticker down, the safe area fades, and the side panel's entries (the station
  information, then each shown button) come in one by one like the main menu's buttons: 40 px from the left, 180 ms
  each, 50 ms apart, the last one first going away; while it is away or moving nothing takes input: a blocker over the
  panel, the navigation events stopped, `Update` returns early (`UiBlocked`); the station's first frame takes the state
  as it is, so a fly-in starts with the menu away and a ship simply on its pad shows it at once); its conversations,
  fines, medals and hints wait until the ship has landed; any key / tap / button skips the flight. `HangarTraffic`: the
  parked ships (the original's 0..max) come and go every 12-35 s (landings likelier the emptier the hangar, at a random
  parking yaw like the parked ones; not in the owned Kaamo Club, none at the battlestation, max 0); one flight in the
  air at a time, the player's included, so nothing collides: the traffic waits for the player, and a departing player
  holds over its pad while an NPC flight finishes.
- **Parking heights** (remake, `StationTables.PadPivotY`, `Resources/GoF2Data/hangar_heights.json` from **GoF2 > Build >
  Hangar Heights**): the original's floor y + ShipY, lifted only where the hull would cut into the pad, per hangar, slot
  (the
  turntable too: its largest lift), ship and heading (24 bins); never lowered (the Terran cradles are open funnels). The
  builder drops each ship's hull bottom (the lowest vertex per 0.1 m column; 0.6 m left up to 1 m of hull in the steep
  pad rims) onto the room's meshes (MeshColliders in a preview scene, the animated parts at their loop start, not the
  additive layers) at every heading, from 30 m above the table height (12 m under the Vossk bays' roofs: 12 m started
  inside the Midorian / deep science raised pedestals for the tall hulls, or took them for a wall, so 18 ships had no lift
  there and sat ~12 m deep in the pedestal, a player's report at Kaamo). A parked or landing ship that needs a lift takes
  the nearest measured heading (15 deg steps; in between a long hull swept into what both cleared), and one that fits
  within 1 m of the pad's least lift (`StationTables.FitParkingYaw`; at some headings the Rhino reached over the 22 m
  structures beside a Midorian pedestal and hung 10 m in the air). Checked: every ship on every Kaamo pad at random
  headings, no hull more than 0.1 m into the room. Before: the wide
  hulls in the Midorian pads' raised rims (the H'Soc's wings 1.9 m), everything inside the raised pedestals of Midorian /
  deep science slot 2. `HangarFlight` also scales its bank and pitch down near the floor (the Vossk bays are flown into 5 m
  up: a banked H'Soc's wing dipped ~3 m into it), keeping the hull 0.4 m above its parked bottom.
- **Ship shadows** (remake; the original's hangars have none, and the hangar light comes from the camera's side, so a real
  shadow falls behind the ship and out of URP's 50 m shadow distance): `HangarShipShadow` on every ship `SpawnShip` puts in
  the hangar (the turntable, the parked ships, the flights, other players' ships) projects a soft contact shadow like a decal:
  `GoF2/HangarShadow` on a box around the footprint (only the ship's yaw, 12 m below (6 until 2026-10-16: the Terran
  turntable's inner ring, sloping ~7 m under a low kit ship's bottom into its pit, stayed lit) to 8 m above the resting
  hull's lowest
  point, a little wider as it lifts) rebuilds each pixel's scene point from the station camera's depth texture (switched on
  by it) and darkens it by the ship's map from **GoF2 > Build > Hangar Shadows** (`ShipShadowSet`, `HangarShadowsBuilder`:
  the hull's triangles rasterised from above into 128 x 128 over a square 2.2 x the hull, linear, uncompressed; R the
  underside's height over each texel (spread outward), G the silhouette, A a wide soft halo): under the silhouette and in
  the halo around it (the part a camera looking down past the hull sees), only below the hull's underside there (so the
  hull never shades itself; 3 m above it past the hull, the rims around a sunk hull), only on surfaces facing up (normals
  from the depth's derivatives), full up to 2.4 m above the floor, fading out to 8 m (`_Hull.zw`). Shown at once for a
  ship spawned on its pad (the turntable, the parked ships, a guest), fading out
  over 15 m as it lifts off and staying on the pad; an arriving ship's fades in the same way as it comes down
  (`HangarFlight.Arrival` -> `ExpectLanding`: the pad's floor is known from the start, the hover over the pad isn't
  taken for the landing; verified: none during the approach at 32 m, 16 % at 12.6 m, full at touch-down). The bake is
  `ShipShadowBaker` (shared by the builder): the mods' ships
  are baked by it at run time (glTFast keeps their meshes readable; once per assembly and set of mods, the rasterising on a
  worker thread, the shadow hidden until it is done; their engine / throttle glows left out; kept for later plays in
  `persistentDataPath/ModCache/ShipShadows/<mod id>/<hull hash>.shadow` (the hull's triangles are its identity, no mod
  hash: hashing every file of a mod was most of the cached load), read and written on the worker thread: the loading
  screen's pass keeps only the files its ships used in their mods' folders (`PruneUnused`: a new version's old maps go),
  `ModManager.Scan` deletes an uninstalled mod's folder,
  `PruneCache`; `CacheVersion` re-bakes them all after a change to the bake; verified: 5 PR ships cached, read back
  identical in 0-30 ms, both clean-ups; made on the main menu's loading screen, `ModShipsBaked` polled by
  `ModLoading`: once the models are built every template's map is read or baked, one hull read per frame, "Hangar shadows
  N / M: <ship>"; the templates' inactive holder is why `Collect` ignores the root's own active state; measured: 0.25 s
  baking all five PR ships, 0.03 s from the cache) and never take the set's
  map (their "ship_NNN_mod" numbers differ between games: stale maps of other ships under those names gave the Falcon a
  wrong shadow; the builder skips pack "mod"); the debug capital hulls get a soft oval of their bounds (the game's
  meshes are imported non-readable, so a build can't rasterise them). Verified in Play mode in the Terran,
  Nivelian, Vossk, Midorian and deep science hangars. The Graphics option **Hangar ship shadows** (`Settings.HangarShadows`:
  Off / Player ship only / All ships, default Player ship only since 2026-10-21 (All before; performance complaints on
  phones)): every hangar ship still gets its
  `HangarShipShadow` (`Attach(..., player)`: `SpawnShip`'s "Player ship", the turntable's), the box is just hidden while the
  option doesn't allow it, so changing it docked applies at once; Off also gives the station camera's `requiresDepthOption`
  back its own value from before the first shadow (the depth texture is the bigger part of the cost), any other choice sets
  it On (again on every `Settings.Changed`).
- The room's animations all run (`CutScene::process` updates every geometry, not only the `_anim` meshes), skipping
  their one-off first key like every animation (`PartAnimation.loopStartMs`, see "Known open items"; played, the key
  flashed the Nivelian bar and the Midorian bar for a frame on every wrap, parked the Terran gutter lights on the
  player's pad and made the loops jump; the remade Vossk bar's streaks are keyed at 33 / 533 ms: `loopStartMs` 33, the
  first key (the original's started at 500 ms)); rotations swing back and forth
  (`pingPongRotation`: the Vossk ring lights' 57 deg sweep stays clear of the portal) and the `_anim` layers fade by
  their `extra` channel (the Vossk portal light).
- **Hangar service bots** (remake; the original never shows them: `CutScene+0x38` is never filled): the Terran hangar's
  five (`hangar_terran_bot_a..d_anim` from the beta, re-animated with the remodel, and the remake's bot e; each with its
  `_anim_add` glow layer) and the Nivelian hangar's three (`hangar_nivelian_bot_a..c`, re-exported with corrected tracks
  2026-10-26, their own assemblies spawned on the room's transform by `StationLevel.BuildHangar`) fly one at a time
  (`TerranHangarBots`, plain C#, `StationLevel.SetupTerranBots` / `UpdateTerranBots`): the first within 1.5 s of the
  hangar showing, then 0.5-1.5 s after the last one's animation has ended, picked at random but never the one that has
  just flown; a bot is hidden between flights (their tracks cross the hangar, so a parked one would hang in mid-air). The
  Terran bots are keyed heading first (`PartAnimation.headingFirst`, set in `BuildHangar`: bot_c's arms and body pitch
  down as it flies off, where the release files' order rolled them over). Bot e (2026-10-27) is a remake model:
  `Models/main/hangars/hangar_terran_bot_e_anim(_add).fbx` under the remake-only mesh ids 99004 / 99005 (spliced into
  `resources.json`, materials 24070 / 20078; `build_assemblies.py` MODEL_OVERRIDE and the `hangar_terran_bot_e` rule in
  `rules/assemblies_scenes.json`), its own assembly `hangar_terran_bot_e` (not in the room's, with a to d), spawned on
  the room's transform by `BuildHangar`; its prefabs were made by **GoF2 > Build > Missing Prefabs Only**. Tests
  `TerranHangarBotsTests`.
- The Kaamo Club parks its stored hulls: see "Kaamo Club".
- **Midorian hangar, contributed remodel** (remake, 2026-10; sources `Reference/contrib/immersive_hangars`, Blender OBJs):
  the room made whole (walls, ceiling, tanks and light strips where the original's hangar was open to space, a third row of
  pads, the props copied around the bigger floor) and station_078_midorian's hangar mouth (a new frame and glowing panel).
  They replace the converted originals in `Models/main/hangars/hangar_midorian*.fbx` and
  `Models/main/stations/station_078_midorian(_emissive).fbx` in place (same .meta GUIDs and mesh names, so the per-mesh
  prefabs and assemblies keep working; the old geometry is only in git history), written by
  `Reference/tools/asset_conversion/contrib_immersive_hangars.py` (run with Blender; it documents the OBJ -> FBX mapping,
  the per-file offsets and the fixes). The assembly's sixth prop slot (ids 14401/14402, which the original fills with x5 a
  second time) shows x6 (`build_assemblies.py` MODEL_OVERRIDE; `Prefabs/main/hangars/hangar_midorian_x6*.prefab` made by
  hand, x6 has no resource id). Its third row of pads, behind the turntable, are parked slots 3 / 4 (`StationTables.ParkedSlots`,
  game (0, 0, -4096) a raised pedestal like slot 2, (-4096, 0, -4096) a sunk pad like the turntable's; `ParkedMax` 5). The hangar
  heights were rebuilt for it.
- **Terran hangar, remodel** (remake, 2026-10-23, the user's files, already converted: `hangar_terran*.fbx` + their
  `.gof2mesh.json` sidecars, written by the remake's aem2fbx from new .aem files): the room made bigger (a second shell
  part, room bounds ~910 x 335 x 1090 m), the props copied around it (x2-x6 and their shadow layers up to 4 / 6 parts, the
  `_add` lights 7), the four robots re-animated for the room (new paths and timing) and the `_add` lights' blink on a
  4.6 s loop. They replace the 22 models and sidecars in `Models/main/hangars` in place (same .meta GUIDs, so the
  per-mesh prefabs and the assembly keep working; the old ones only in git history). The model importer brings no
  materials (`materialImportMode` None), so every part the prefabs didn't have showed Unity's default Lit: each per-mesh
  prefab's renderers were given their file's game material with PrefabUtility (`_add` 20078, `_alpha` / `_shadow_alpha`
  20077, the rest 24070, resources.json); the new shell's meshes are named `hangar_terran_v3` / `.001` (Blender names, not
  `_part0`), which nothing reads by name (the shell has no animation). Three new pads, the same pad model at the old ones'
  height (pit floor -62.5 m, ring -49.1 m), measured with rays on the assembled room in a preview scene: game (5459, 0,
  2891), (5459, 0, 5781) beside the original two and (0, 0, -2891) behind the turntable, parked slots 2-4
  (`StationTables.ParkedSlots[0]`, `ParkedMax` 2 -> 5): NPC ships park and land there. The flight lane is unchanged (the
  forcefield still at z -549; nothing under 90 m on the way to any pad but 14.5 m near the field, the 161 m girders well above
  the 45 m cruise). Hangar heights rebuilt (Terran 24 -> 48 entries, the same eight ships lifted on every pad; every other
  hangar identical). Checked in Play mode at Dis: parked ships on all five slots, centred; NPC landings on slots 2 and 3 and a
  take-off from slot 4.
- **Vossk hangar, remodel** (remake, 2026-10-24, the user's files: new `hangar_vossk.aem` / `hangar_vossk_anim_add.aem`,
  converted with `Reference/tools/asset_conversion/convert_remodel.py`: aem2fbx.py's FBX and the sidecar format
  PartAnimation reads; aem2fbx.py's own sidecar is the conversion's first format, which the game no longer reads): the
  room made whole, a cylinder ~1.4 km across with a ring of bays on three tiers all round (floors at 268 m, 0 and a new
  bottom tier at -270 m; hangar_vossk now three meshes), a column 100 m in radius at the hub (a base ring and two
  full-height struts on the x axis). Its animated layer was redone 2026-11-01 (the user's fixed
  `hangar_vossk_anim_add.aem`, same converter; 16 -> 21 parts): six ring lights (parts 3-8, raised 2986 units, turning
  full circles in opposite pairs, 3-6 dark for a moment round heading 0 (`extra`); no ping-pong for this room any more,
  `StationLevel.BuildHangar`), three `extra`-fading sweeps (parts 9-11, from 0 / 5.6 / 10.8 s), five static quads
  (12-16), the column's glowing core (17, pulsing in width) and three pulses running up it (18-20, peaks at 1.0 / 6.2 /
  11.4 s, parked at the column's ends between runs). One loop of 15.63 s for the lot: PartAnimation's length is its
  longest part, the others hold their last key until it wraps, so the 5.2 s stagger keeps. The old models replaced in
  place (same .meta GUIDs); the per-mesh prefabs' new parts got their file's material (hangar_vossk 24074, anim_add
  20071; no new textures). The portal and its lights are unchanged, so the lane's gate and span are too. Pads (measured
  with rays on the assembled room): every 20 deg on the original slots' ring (10856 game units round the hub, game (0,
  0, 10860)), none on the two lower tiers over the portal (70-110 deg): the original 9 slots kept, 40 more
  (`StationTables.ParkedSlots[1]`, 49 in all; `ParkedMax` 9 -> 18). The pad tops sit 1-2 m above the slot heights; the
  lifts correct it per ship (hangar heights rebuilt: Vossk 71 -> 2500 entries, every other hangar identical). The lane
  (`HangarLane.column` = 100): `HangarFlight.AroundColumn` takes the run from inside the portal to the point in front of
  the bay straight where that clears the column (the ship's half width + 10 m), else along the tangents onto a circle
  1.1 x that round it, in steps of at most 30 deg, at the bay's height (the far bays: closest ~150 m from the centre);
  the original's arc through the centre went through the column. Tests `HangarPillarTests`
  (`VosskFlightsGoRoundTheColumn`: every pad, three ship sizes, both ways). Checked in Play mode at V'ikka: the fly-in
  onto the turntable, NPC landings on far bays of all three tiers (round the column, parked at their heights).
- **Nivelian hangar, remodel** (remake, 2026-11-01, the user's files: a zip of every `hangar_nivelian*.aem` but the
  bots, plus `hangar_nivelian_alt.aem` and `hangar_nivelian_alt_alpha_add.aem`; converted with `convert_remodel.py`).
  The alt is the zip's main model with the six launch-pad tops cut out (2200-unit squares at y -46), which alt_alpha_add
  holds as a layer of its own: `hangar_nivelian.fbx` is the alt (the zip's own main model unused), alt_alpha_add is
  added beside the room's `_alpha` layer (not instead of it). The other 13 replace the originals in place (same .meta
  GUIDs; x2 gained a second part, given 24073 in its per-mesh prefab). The pads: remake mesh id 99006 (`resources.json`,
  `build_assemblies.py` MODEL_OVERRIDE, a `layer_alpha_add` child of the `hangar_nivelian` rule) with its own material
  99006, shading `bump_alpha_glow` (`PrefabBuilder.MakeLitAlphaGlow`: URP Lit, the room's normal and metallic maps,
  alpha-blended by the diffuse's alpha like the `_alpha` layers, the diffuse as emission for their glow; the GI flag
  `RealtimeDirectEmission`, without which URP 17.7's material validation drops `_EMISSION` whenever the Editor loads
  it). Drawn transparent the pads wrote no depth, so the ship shadows (a decal on the depth texture) fell on the pit 3.5
  m under them and the pads, drawn after the decals, covered them: `StationLevel.DrawPadsWithDepth` swaps in a copy at
  queue 2500 with depth (the validation turns it back off on the asset). Pads, measured with rays: the turntable and
  five around it (Unity (0, ±204.8), (204.8, 0 / ±204.8); tops at -2.3 m, the floor round them -5.9 m), parked slots 2-4
  game (0, 0, ±4096), (4096, 0, -4096) (`ParkedMax` 2 -> 5). Pillars, a slice of the mesh at 50 m: 12 in a 3 x 4 grid (x
  -102.6 / 102.3 / 307.2, z ±102.4 / ±307.2, 5.7 m round; the original's 6 were the right two columns). The forcefield
  is still where the lane's gate is (z -568, 4-152 m). Hangar heights rebuilt; the builder now counts `_alpha_add`
  layers as solid (it skipped every `_add` as light, and the hulls fell into the pit): every pad lifts the original's
  four ships as the original pads did. Tests `HangarPillarTests` (every pad, 12 pillars). Checked in Play mode at
  Nehébru (station 0): the room, the pads, the player ship's shadow on the turntable.
