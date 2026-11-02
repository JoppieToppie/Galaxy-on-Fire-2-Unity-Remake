# Star map and system travel

Notes moved from `CLAUDE.md` (its index lists every notes file). Code comments citing `CLAUDE.md "<section>"` mean the section or bullet of that name here.

## Star map and system travel

Research: `Reference/research/starmap_travel.md` (+ `Reference/tools/starmap/starmap_tables.py`: sun positions, gate
paths, energy cells, exact system layouts). Rules in `GalaxyMap` (plain C#), the map in `StarMap` (UI, created at
runtime by `Open()`), the flight side in `SystemJump` (on the player ship).

- **Star map** (`StarMap`): one fullscreen map for the station's Map button, the jumpgate and the Khador Drive. Its own
  3D scene on layer 31 with its own camera (the level's cameras and lights are switched off while open, flat ambient, no
  fog) plus the `UI/StarMap` overlay on a higher panel sort order; the owner pauses its game.
  - Galaxy view: additive sun sprites of the visible systems (`Session.SystemVisible`, from `initiallyVisible`; hidden
    systems aren't drawn), faint route lines and the pulse toward the gate neighbours (not with a drive); tap selects
    (sound 103), the camera glides it to the centre, a second tap zooms in (2184.5 ms sine ease, 106): the current
    system or a gate neighbour, any visible system with a Khador Drive, else 420. Drag pans with inertia and spring
    limits (not while a system is being centred: `StarMap::update`'s centring overwrites the spring, so Skor Terpa past
    the limit can be reached; remake: the limits take in every visible sun, `StarMap.PanMin` / `PanMax`: Loma, Talidor,
    Ginoya, Skor Terpa, Skavac and Me'enkk lie past the original's box, and once centred the spring dragged them back
    off the centre, up to ~280 px, so a second tap missed them). Remake: the full-map overview, one zoom level further
    out fitting every visible sun (the Full map / Close up button, Q, the controller's X, the mouse wheel, a pinch; Esc
    leaves it first): no panning there, a tap or Enter on a system zooms back in centred on it; the zoom in / out of it
    takes the galaxy <-> system zoom's 2184.5 ms with its sine ease (`StarMap.OverviewMs`; it was 650 ms, over long
    before its Map_Zoom_In / Out sound, 2.68 / 2.32 s).
  - System view: the planets on their orbits (`java.util.Random(system * 1000)`, same layout every visit), lit from the
    sun's position like `StarMap::initStarSystem`'s light (`LightSetLightPosition`, a fixed-function point light without
    falloff: `Resources/GoF2StarMap/MapPlanet.shader`, ambient 0.2 + diffuse toward the global `_StarMapSunPos`; the
    remake had a directional light that lit every planet from one side), drag turns it (pitch +-45 deg), tap selects a
    planet (104), release turns it to the front (105), a second tap confirms: 419 on the current station, 579 / 582 /
    581 on energy cells with a drive, else "Destination: X / Travel to this station?" (574 + 421). Before those,
    `StarMap::OnTouchEnd`'s campaign checks on the step's target: `Story.RequirementRefusal` (Supernova notes "Story
    rules") and step 24's scanner + tractor beam (532; #186).
  - Overlay: rings, names (selected orange), race and security lines, "Tech level", visited tick
    (`Session.VisitedStations`, set on docking, and in space for the orbits without docking: `SpaceLevel.VisitedInSpace`,
    MGame::OnInitialize's Station::visit for 109, 110, 132-134, 102-104 and 111 from step 0x5e; #188), gate icon, "you
    are here" pulse, system block with the race logo,
    "Energy cells needed: N / M" (578), Key legend (400).
  - Header (#175, remake): as tall as the station's top bar through an invisible copy of its Menu button
    (`StarMap.uss .map-header-spacer`, added by `StarMap`), so the title sits at the station name's height whatever the
    interface font; shown where the station shows Menu (touch, keys and mouse; with a controller neither shows), and
    on phones at the Menu button's 64-unit touch height (`.layout-phone .map-header-spacer`). The footer's key hints are
    the small ones (UI "Small key hints").
  - Input: touch / mouse drag and tap; keyboard arrows select, WASD move / turn, Enter zoom in / confirm, K key, Esc
    back; controller D-pad, left stick, A, Y key, B back.
- **Station Map button** (177; keyboard M, controller Y; refused with an overloaded hold, 204): station mode (jump mode
  with a drive). Picking a station leaves at once (`StarMap::depart`): the launch sequence, then the autopilot to the
  programmed station, or for another system with a drive the Khador charge.
- **Jumpgate** (`MGame::dockEvent`): the autopilot to the gate entering its sphere (7500, Vossk 11250) hides the HUD and
  pauses; with a programmed station "Destination: X / Travel to this station?" (Yes preselected), No or no destination
  opens the map in gate mode (only the current system and its gate neighbours, any station there; remake: any system on
  a gate route, "Jumpgate route planning"). Backing out puts the
  ship past the gate with the controls back. Remake (#37): a destroyed ship takes no gate and its Khador charge stops
  (shot down entering the gate it still jumped, repaired).
- **Gate jump scene** (`MGame::startJumpScene` / `updateJumpScene`, ~6.3 s): ship at gate - (0, 0, 10000) facing game +Z
  at 2 u/ms, fixed look-at camera at gate + (-2000, 300, -6000) drifting (5, 2, -3) u/ms; when it passes gate z - 10000
  the gate's idle `_anim_add` is swapped for the `_jump_anim_add` child (5000 ms, once) and sound 31 (Jumpgate_3b / 4c /
  1b / 2b at random) plays; 1000 ms in the ship vanishes; at the end `Space` reloads in the target station's orbit.
  Every gate layer opens with a one-off key (t 0, the loop from 50 ms; the Midorian ring lights at the centre there):
  looped from 0 the ring parts jumped 17.8 m on every 2.5 s wrap; now skipped like every animation's (see "Known open
  items", `PartAnimation`). The gate is held at LOD 0 while it jumps (`SystemJump.PlayGateJump`,
  `LODGroup.ForceLOD`; `ResetGate` lets go; #167): its made levels keep the idle layers but not the jump layer, so on a
  phone's lower LOD bias (Medium detail, 0.6) the gate fell to level 1 0.4 s into the flash and the jump played unseen.
- **Khador Drive** (item 85, or ships 37 / 38 / 40): the autopilot menu entry opens the map in jump mode; a jump to
  another system (instant jump) charges 5000 ms (sound 33, cells removed at the start, "-N t Energy Cells", the charge
  bar in the HUD: `Hud::draw` 0x1933f6's frame 0x53a with the striped fill 0x539 revealed from the centre outward,
  half-width min(1, rate · 1.05) · 194, and 318 "Drive charging" (the cloak's 317) in its lower panel;
  `GoF2Hud/charge_frame` / `charge_fill`, #48) once the level is 5 s old, then the `khador_jump` fx (15026, scale 2)
  3000 units ahead, camera at fx + R(-2000, 300, -2000) drifting (5, 2, -5), ship hidden at 1700 ms, reload at the fx
  end (~4 s). Cells = gate jumps on the shortest route through visible systems, 4 without one, x2 on Extreme.
- **Arrival** from another system: the hidden gate in the gate orbit, else (0, 0, 100000), facing the station, with the
  7 s fly-in camera; the orbit information (race logo, station, "<System> System", security level in its colour) shows
  during it. The programmed station is cleared: no autopilot leg follows a system jump (remake: but a gate route's hop,
  "Jumpgate route planning").
- **Loma** (system 25, the Valkyrie add-on's black market; gates to Aquila, Union and Shima): on the map from a new game's
  start and after every load (`Status::resetGame` / `GameRecord::load` show it whenever the Valkyrie add-on is owned, and
  the remake owns both; `GalaxyMap.Visibility`, `SaveGame`), not only in the add-on campaigns. In the orbit: 6-9 pirates
  (+ the raider waves, security 0), disarmed until the toll 448 is answered (see "Radio chatter"), maximum prices at all
  three stations (`calcCargoPrices`: Status+0x78 is a placeholder Station, so every real one), no seen-price records
  (`HangarWindow::initialize`), signatures (sort 29) only rolled into Loma stock, kills count but change no standing
  (`Player::damage`), no ticker, no docking fine; arriving by travel 40000 further out (`Level::init`).
- **Remake-only (free play, no campaign):** the campaign takes the player out of gateless Mido; without it, a ship in a
  system without a jumpgate counts as having a Khador Drive (it still needs energy cells), and Var Hastra always stocks
  energy cells.
- **Mission maps** (`StarMapMode.Mission`, Missions window / lounge offers / Most Wanted): view only in the original
  (remake, players' suggestion: the Missions window's map travels, `StarMap.Open`'s allowTravel,
  `StationMenu.CanTravelFromMap` / `TravelFromMap`: confirming a station is the Map button's departure with the station
  programmed, the Khador rules with a drive; without one a target past the gate neighbours is reached along the gate
  route: the first gate's prompt names the destination and jumps to the next system's gate orbit (`GalaxyMap.NextHop`;
  `Arrive` keeps the programmed station on a hop, so the autopilot flies on to the next gate; the original's gate jumped
  straight to a far programmed station, which the Distress Help used); the lounge's offer maps stay view only), centred
  on the target, the yellow route along the gate path (finished segments opaque, the current one grows with alpha 255·t
  on the 0.99 s pulse, the next when t wraps); the Wanted map starts it at the criminal's last-seen system (`setStart`).
  A gateless start system (Mido) has no route, like the original.
- **Jumpgate route planning** (remake, 2026-11-02, players' suggestion on Discord 2026-10-17: "build a route through
  multiple systems and let autopilot just fly it all along from Pan to Aquila"): the station's Map and the gate's map
  take any system the gates reach (`StarMap.GateRoutes`, `GalaxyMap.GateReachable`), not only the gate neighbours; the
  Khador map keeps the original's rules. A selected system past the neighbours shows the yellow route (`PlanRoute`, drawn
  as the Mission map's) and, without a drive, "Jumpgate jumps: N" on the energy line (Extra `mapRouteJumps`); the
  destination dialog names the jumps when there is more than one. With a drive, where the gates reach the target, the
  dialog's answers are KHADOR DRIVE / JUMPGATES (Extra `mapRouteGates`, a third button made at runtime,
  `StarMap.dialogAlt`) / NO, and Esc / B close it unanswered (`dialogBackCancels`); short of cells, 582 offers the gates
  for any system they reach (the original only for a neighbour). **Fastest or safest** (the user, 2026-11-02, chosen when
  travelling, no option): the routes are A* (`GalaxyMap.RoutePath`; `SystemPath`, the original's BFS until then, is its
  fewest-jumps run, the same jump counts and, on the base map, the same routes); the safest adds each entered system's
  `GalaxyMap.Danger`: 1 per security level below Secure (Dangerous 3 .. Secure 0) and 3 more in an enemy race's system
  (`EnemyTerritory`: an owned system whose race `Standing.IsEnemy`, the user's addition: its fighters attack, its
  stations fine). Where it differs from the fastest, the map
  draws it in the Secure green under the yellow one, the energy line adds "Safest route: N jumps", and the travel dialog
  (`StarMap.TravelByGates`, also behind the drive dialog's JUMPGATES and 582) asks FASTEST ROUTE / SAFEST ROUTE / NO (Extra
  `mapRouteFastest` / `mapRouteSafest` / `mapRouteJumpCount`). The pick rides `StarMapResult.safestRoute` into
  `Session.RouteSafest` (never saved), which every gate's `NextHop` takes. The ship then goes gate by gate
  (`GalaxyMap.NextHop`,
  see "Mission maps"): the first gate asks as ever, every later one jumps on unasked while the programmed station is the
  one the hop kept (`Session.RouteDestination`, set in `SystemJump.Arrive`, never saved); the arrival camera still plays
  in each system. Tests `GalaxyMapTests` (route planning).
- **Reveal** (the lounge's bought coordinates, `revealSystem`): the camera on the new system, hidden (no label, lines or
  input) for 4000 ms while its sun grows (remake: to the normal size), then selected; view only.
  The coordinates and the story's steps reveal through `GalaxyMap.Reveal` (`Status::setSystemVisibility`), which writes
  `Session.SystemVisible`. `Visibility`'s result is a copy while something overrides the map (an event's "system show",
  a mod's "available" condition, a missing mod system), so never write into it: the purchase did, and bought systems
  stayed hidden while the story's showed (Discord "Bug with buying a hidden systems", 2026-10-30; test
  `GalaxyMapTests.BoughtCoordinatesRevealTheSystemWhileTheMapIsOverridden`). A save from then had paid and the seller
  out of stock: `SaveGame` reveals every paid coordinates seller's system on load
  (`SaveVersionTests.APaidCoordinatesSellerRevealsItsSystemOnLoad`).
- **Volatile goods** (`GalaxyMap.HasVolatileGoods`: 209 / 204 in the hold): the Khador menu entry and a map target
  outside the gate routes give 612; a gate neighbour picked on the Khador map goes by the jumpgate. Mission blocks: 525
  at the gate and on the Khador entry.
- **Interstellar space** (`InterstellarSpace`, 2026-10-31, the user's idea; no original behaviour): a mounted cruise drive
  with attr 135 (`CruiseDrive.InterstellarAttr`, the base game's Khador Slipstream, "Base content" in mods.md, one drive for both cruises:
  Khador sells the blueprint in Thynome's lounge, from step 84 or in free play, as Kothar has none, `Story.LoungeUnlocked`) adds a quick menu entry
  (`Navigation.Kind.Slipstream`, `NavigationInterstellar`); no option, the item is the switch. The Khador charge and scene
  play (`SystemJump.toInterstellar`), then `Session.EnterInterstellar` reloads Space, read once in `SpaceLevel.Awake`:
  `InterstellarSpace.Clear` empties the orbit left (`OrbitLayout.interstellar`), which stays `Session.StationIndex`, so a
  save or load is that orbit and the flag is never saved. No wingmen, traffic, story (`StorySpace`), encounters, wormholes;
  `plainOrbit` in `Awake` marks what an arena and this space both leave out. Multiplayer: everyone here shares
  `InterstellarSpace.OrbitId` (`SpaceLevel.NetOrbitId`). Each visible system is a `Kind.System` target and a sun
  (`InterstellarField`: the map's sun material, kept inside the far plane); `GalaxyMap.Visibility` decides, every frame,
  so bought coordinates, the story, an event's hide and a mod's `available` all hold here. Scale 240 units a map unit
  (the average hop, 2510 on the map, is 30 km; the galaxy +-66 km around the origin, centred for floats' sake). A leg
  is `LegUnits`, the average hop, so `InterstellarSpace.CellsFor` is about the Khador Drive's `GalaxyMap.EnergyCells`
  (`InterstellarSpaceTests`: 0.5 to 1.25 x over every pair); `CruiseDrive.keepLeg` keeps a paid leg over a stop. The
  autopilot holds the drive there (`Navigation.UpdateDrive`), `CanFastForward` is false. Within `ArriveUnits` (8 km):
  `SystemJump.LeaveInterstellar` into `ArrivalStation` (the gate orbit; an orbit a script sets up on
  arrival, `Navigation.ScriptedArrival`, is loaded as after a jump with its arrival camera, not in the warp); a story refusal
  (`PlanetJumpRefused`) holds until the ship is back outside 1.5 x that. The leg that empties the hold says so (Extra `interstellarLastLeg`); no cells for the next leg: `FallOutOfSlipstream`
  at the nearest system, nobody strands. Under the autopilot only its own target's edge is an arrival, the others are flown
  past (a flight off the galaxy's disc curved out of its start through Weymire's edge, 2026-11-01).
  **The Void** (2026-11-01, the user: "make the Slipstream go to the alien void as well", then "void should not be one
  point you go to, distance should also be unknown/hidden as its not a system in the universe we know", 10-15 s): no place
  here, no marker, no distance. The autopilot menu's last entry, 415 "Void" (`Navigation.VoidDive`, a `Kind.System` target
  kept out of `Targets`, so never drawn, locked or measured; offered in free play and past the wormhole ride,
  `InterstellarSpace.VoidShown`, index > 24), is a dive: a new way each time, off the galaxy's disc (thin along game z) on
  the ship's side leaning its heading (`DiveWay`), and a random 10-15 s of the drive (`DiveMinMs` / `DiveMaxMs`,
  `VoidDiveMs`, counting only while it cruises; the autopilot off ends it). The target sits ahead at the edge plus what the
  time left takes at the drive's speed (`DiveUnits`), so the Void's sky and orbit seep in (`InterstellarField`, no sun) and
  every system's edge is flown past; at 0 the ship falls in (`LeaveAlong` to `Session.VoidOrbit`). One leg: a cell, under
  the Khador Drive's two. The arrival point is random (`OrbitLayout` landmark 2), so the approach keeps the one it built
  around (`Session.WarpArrivalPoint`, read once in `SpaceLevel.Awake`). The quick menu offers the drive in the Void too:
  the space starts beside the system the Void was entered from (`InterstellarSpace.PositionOf`, `Session.VoidReturnStation`;
  that system counts as the one left in `InterstellarField`), `Clear` drops `alienOrbit` (its no-autopilot rule held every
  system target) and the station stays -1, so a save there is the Void. An arrival in the Void keeps `VoidReturnStation`
  the orbit the crossing left (not -1). Story 78 / 80 refuse the drive as ever (`ForcedKhadorTarget`). Tests
  `InterstellarSpaceTests` (the dive's), `InterstellarFlightTests.TheMenusDiveFallsIntoTheVoidUnseen`,
  `TurningTheAutopilotOffEndsTheDive`, `TheDriveFliesOutOfTheVoid`; recording
  `InterstellarRecording.FlyToTheVoidAndBackAndRecordIt`.
  The Khador scene plays going in only, drawn anew as a **hole in space** (every other Khador jump, other players' and a capital
  ship's retreat keep the original fx, `SpawnKhadorFx`, the user's call; `KhadorFx.OpenRift`,
  `GoF2/KhadorRift`, `Core.KhadorTimeline`, the user, 2026-11-01, replacing the whiteout, the side view and the glide; some of it
  after a reference clip of theirs: the tear, the fire inside it, the arcs): the camera stays held where it was behind the ship
  (`SystemJump.heldView`: the chase camera's lag rubber-banded the ship on screen as its speed changed), the animation stands
  7500 units ahead at 0.8 x its speed (`RiftFocusUnits`, `RiftRate`; half speed read as too slow), the ship eases back to 1.5 u/ms while
  it forms and from 1.3 s is drawn in at up to 8 (`RiftSpeed`), the warp starting over the orbit's own sky as it goes (`RiftWarp`, `WarpSky`, the flow too), the web
  drawing back so that shows. The ring is a tear (`KhadorRim`), its rim shared by the hole, the ring and the web's cut-out, with blue
  fire inside it and arcs thrown out of it as the ship is drawn in. Inside it is already the next level: `Warp` and `WarpWeb` are the
  same code as the level's (`SpaceSkyCore.hlsl`, `WarpWebCore.hlsl`), turned by `_RiftTurn` to the heading the level opens on
  (`InterstellarSpace.StartRotation`). The hole draws first, before every opaque, with the depth of a plane past the ship
  (`_RiftDepth`), so the orbit behind that plane fails the depth test and the ship still draws over it after flying through. When it
  fills the view (`KhadorFx.RiftFilled`) the frame is the next level's and `SpaceLevel.SwapScene` goes (`Session.RiftEntry`: the view
  carried, the plume primed, no light to fade); the HUD and the suns come back after it (`WarpSky.HudShown`). What would give it
  away goes before the swap: the star, beam, sparks and flash by the crossing, the orbit's fog (it lies in front of the hole). The
  jump's sound plays on over the swap at the slowed rate, its blast at the crossing (`SystemJump.PlayCarried`). VR, or a missing
  shader, keeps the old way: the whiteout (`KhadorWhiteoutMs`), the glide and `WarpSky.OnGUI`. An arrival
  is `Session.WarpArrival`, at 8 km (`ArriveUnits`): the level changes at full warp, no scene, no fly-in camera, and the ship
  starts `CarryMeters` further out, flying in through the real orbit with the speed (`CruiseDrive.CarrySpeed`) and warp easing
  out over `WarpSky.ArrivalCarryMs`, to end at the normal arrival point. **Seamless by building the orbit on the way in**
  (`SystemApproach`, the user: "load in as you approach", "it needs to be seamless"; an orbit is one scene, it can't stream).
  From 20 km out a root maps the orbit onto this space, the carried-out arrival point onto the system's edge and the arrival's
  facing (`OrbitLayout.ArrivalPose`) onto the way there: the real station, gate and asteroids (the same `Random` seed as the
  arrival's, `OrbitBuilder.SpawnAsteroids`) stand on it, built a frame apart, with no colliders or `Target`s. A `Backdrop` of its
  own fades in, turned the same way (`Backdrop.Turn` / `Fade`): the sun takes over from the star ahead (`SystemApproach.SunShown`)
  and draws off the nose to its place in the orbit, the other stars shrink away within the first eighth of the blend
  (`SystemApproach.HandedOver`, the user, 2026-11-01: they went only near the arrival), and the lights and the hull's gamma globals move
  to the orbit's. The ship's heading against its course and the chase view in the ship's frame carry over (`Session.WarpHeading`,
  `WarpView`, `ChaseCamera.CarryView`), the boost look too (`FlightModel.CarryCruiseLook`: it fell to 0, a 20 degree zoom at the
  swap), the warp's flow and its sound (`WarpSky.CarrySound`). So frames either side of the swap match but for the HUD and the
  first frames of the flames (checked frame by frame, 2026-10-31). A level's first frame draws before its first LateUpdate and
  its Update: `WarpSky.Setup` writes the arrival's full warp itself and `ShipExhaust.Prime` seeds the plume along a frame's flight (the
  particles come per distance), as the engines went dark for that frame; the plume is still a little smaller there (2026-11-01). A fall out of the slipstream has no approach: the old ice-blue
  dissolve (`WarpSky.Setup`, 'unseen'). Not carried: `SkyLayers` (ring skies, storms). The lens flare eases in over the arrival (`Backdrop.flareShown`): this space has none, so its glare greyed the screen in one frame at the swap.
  **The swap's stall** (`SpaceLevel.SwapScene`, Editor, with the player's mods): a plain `LoadScene` held ~870 ms, ~600 of it
  the unused-asset sweep after `Awake`, so it loads additively (no sweep), destroys the old roots first thing in `Awake`
  (`ClearReplaced`; the two levels never run together) and sets the sky and lights again once the scene is active, as
  `RenderSettings` go by the active scene. Additive loads don't restart the level clock (`SystemJump.levelStart`). The
  tables are read on the way in (`SpaceLevel.PrepareArrival`) and traffic, 90-250 ms of `Awake`, comes in a ship a frame
  (`Traffic.Setup` 'oneAFrame', not with a mission here or in multiplayer): 146 ms now, frames after it normal. The HUD
  goes on across the swap (`FlightHud.KeepForSwap`, `SpaceHooks.KeepHud` / `DropNewHud` / `SettleHud`, not in a session or VR): a
  new panel's first styling and layout was 54 ms of 621 elements, now ~9. It leaves the old scene before the load, the new scene's HUD
  goes in `ClearReplaced` before it builds a panel (`SpaceLevel` runs first), it drops its level refs to real nulls so its Update
  rebinds as a new HUD does, and it joins the new scene in `OnSwapped`, so a docking's scene change still takes it. Measured
  2026-11-01, Editor, the player's mods: 127-200 ms before, 90-137 ms after; of that ~30 ms is the Editor loop, plus the Editor-only
  `SinglePassBuildProcessScene`. A Linux Vulkan player, no mods, 4K, 60 Hz: 21-27 ms on eight hops, cold ones too, no frame over 33 ms. Destroying the old level is only ~16 ms (Traffic ~10). Other
  stalls are the first use of a mod ship after the swap: `Traffic.Update`'s collision baking (~50 ms), a synchronous model load (~50). The look (`Visuals.WarpSky`, the sky
  shader `GoF2/SpaceSky`, 2026-10-31, from the shader data and the user's clip of the game): EVERSPACE 2's
  `M_TravelMode_WarpTunnel` uses distortion, its colours are the sky's, so no mesh: the sky is resampled in polar coordinates
  about the nose, squeezed into radial streaks in two crossfading phases, level 0 being the plain sky, so nothing cuts; ships,
  planets and suns draw over it sharp. Between the systems only (`_WarpKhador`; the user: not in the orbit's cruise) it keeps
  the Khador animation's blue vein web (the fx's own sheet) and ice blue: the beams, star sparks, core and ring were tried and
  removed. The bands only shade the sky and nothing is added to it, in an orbit too: the user rejected the white streaks and
  kept the stretching. Those were thin lines from a mod's image in the sky shader, spread into wide beams by bloom, not the
  stretched sky: a sky with the stretch removed still showed them. The planets grow from a speck, half at the swap, full as the arrival eases
  out (`ApproachSize`, `ArrivalSize`, `Backdrop.planetSize`; the user: fading in at full size was jarring). The flight in faces
  as a jump's arrival does, the station (the planet dead ahead was tried and dropped once it scaled). The station, gate, asteroids, ships and sun start small too and grow the same
  way (`ArrivalScale`, each about its own centre, so positions, hit boxes and routes stay real; `sunScaleFactor`). The warp holds
  all (`SpaceLevel.WarpHolds`, the user): no firing, nobody fires or is hurt, and no drive or autopilot till an arrival's has
  ended, so nothing can meet the scaled objects. Normal traffic is built ahead, one a frame
  (`Traffic.Carried`, on a root that survives the load), and `NpcShip.Setup` takes over the same models, so none pops in; all else
  of the plan is built at the swap as ever (a Wanted criminal moves on arrival). An arrival's enemies wait for the warp's end (`Traffic.ReleaseEnemies`,
  `Traffic.Enemy`, the user), friendly traffic still one a frame, and neither ships nor the station and gate show on the radar or as markers till then
  (`NpcShip.RadarHidden`, `Navigation.RadarHidden`: the user called them the biggest tell); a mission's or a multiplayer orbit's ships aren't held. The web is a layer of its own (`GoF2/WarpWeb`, the
  user: planets stood over it inside the sky): added after the planets, at the far plane, behind all with depth. No fog between the
  systems (`OrbitBuilder.SpawnDust`, the user: it should feel like crossing vast distances): an arrival's `SpaceFog` eases in
  over `ArrivalCarryMs` (`WarpSky`, `SpaceDust.Fade`), as its sprites are placed at random, so none built on the approach could
  match the orbit's. Always 1 between the systems; in an orbit the drive's ramp (any cruise drive). Its two band images are
  the game's own (`Data.WarpBands`, 2026-11-01: random phases through an inverse FFT, made once a run; the veins are field
  zero-crossings, as plain noise with ES2's spectrum came out clumpy), so the warp uses no ES2 art; a mod's `warp.json`
  (`ModWarp`) can replace them. The warp has no sound of its own, only a mod's. Nearing a
  system its sky blends in, `InterstellarField` loading it as the arrival orbit builds it (`OrbitBuilder.MakeSky`,
  `_SkyBlend`, runtime layer modes for a mod's images), so an arrival finds the same sky. **The sky shows the distance flown**
  (`InterstellarSpace.SkyShare`, the user, 2026-11-01): it opens on the sky of the system left and the next system's crossfades in over
  the whole leg, edge to edge, what the autopilot flies to (a dive into the Void too, whose sky a dive called off doesn't keep), else the
  nearest, and a system the flight passes within 20 km of on the way (`NextSystem`; under the autopilot only its sky, not its orbit). A system flown past stays (`TakeOver`: more than half in as the flight goes on to another, it finishes and its
  sky becomes the sky, turned as it was seen), less fades back. Past the closest point the blended sky holds still, as the way to
  the system swings round (`skyTurn`). The orbit comes with the old ramp, 20 km to 8 km (`approachBlend`). The traffic ahead goes by what a ship is, never its source, so a mod's are in it (`Traffic.Carried`; the arrival takes the
  approach's whole plan, as escorts name their hulls); what it can't build grows out of nothing (`ArrivalScale.Add` growIn), and
  LODGroups never cull what it scales (2026-11-01: they hid shrunk ships until they popped in). Stand-ins sleep
  (`SystemApproach.Sleep` / `Wake`: every MonoBehaviour, trail and particle system on them, a mod's or not): ES2's world-space
  engine ribbons drew kilometres of flame from the root's sweep, only where the run lay along the ship's heading. `ChaseCamera` carries a cruise's extra movement so the follow's lag stays as at the ship's own speed.
- **Sounds** (the FEV's LGCY data): 31 Jumpgate = one of Jumpgate_3b / 4c / 1b / 2b at random, 32 KhadorDrive =
  Jumpgate_5c. 102 Map_Whoosh is a drag odometer (`StarMap.AddWhoosh`, StarMap+0x1c0: + min(|dx + dy|, 10) per galaxy
  drag move, + min(|yaw + pitch velocity| · 0.01, 10) per frame in the system view, 0 past 100): entering the parameter
  regions 0 / 25 / 49.8 / 75 plays Map_Click_01 (event volume 0.439); a touch-down restarts it, the back button stops
  it.
