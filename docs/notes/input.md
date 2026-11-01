# Input and controls

Notes moved from `CLAUDE.md` (its index lists every notes file). Code comments citing `CLAUDE.md "<section>"` mean the section or bullet of that name here.

## UI and platforms

- **Keyboard** (flight): the PC / Mac version's defaults (Galaxy on Fire 2 Full HD: the PCGameControls wiki list, and
  the PC help texts in the text table: 17 / 18, 587, 638, 1731 / 1732, 1752, 1754, 1879 with their `#KEY_*` tokens,
  `FlightHints.KeyTokens`): arrows steer, W booster, S brake (the PC version's "Brake" 3361: the engines stop while
  held, the throttle is kept; a boost overrides it), `]` / `/` (and the wheel) thrust, Space / LMB primary, R / RMB
  secondary, A / D strafe (held; the PC binding screen's 3350 / 3351 "Strafe left / right", `PlayerEgo::strafe` 0xad838,
  never called on the phone: sideways speed ramp x min(H' x 0.06, 2) u/ms, the ramp 0.1 x1.5 a frame, x0.7 a frame after
  release, the camera moves with it; the dodge keeps the touch swipe and the right-stick flick and can be bound), 1 / 3
  roll ("Turn left / right", remake roll: the phone original only auto-levels; touch: the Level button slid sideways), F
  the action prompt (Dock: dock, the autopilot to a locked station, the planet jump; Enter too), Q autopilot (the target
  list, again = off) and E actions (the quick menu), 1-8 pick a menu entry, Tab fast-forward ("Speed up"), T view, V
  wingmen, K Khador Drive, C cloak, M / middle mouse toggle mouse steering, Esc pause. Strafe works on the autopilot too
  (remake). Remake keys: 2 level out, G switch secondary, U unlock target (see "NPCs and combat", Radar), X time
  extender, Y turret auto fire, F1 toggle HUD (Full / Minimal / Off, see **Options**), F2 cockpit view, F12 a screenshot
  anywhere (`ScreenshotKey`, saved like the photo mode's: PC Pictures/Galaxy on Fire 2, Android MediaStore). The phone
  binary has no flight keys (`MGame::OnKeyPress` is empty). These are the defaults: every flight
  control is rebindable (see **Key bindings**).
- **Key bindings** (remake, `GameControls` + `BindingRow`; their own Options tab "Key bindings" (`OptionPage.Bindings`)
  in the main, pause and station menus, the panels wide enough for its six tabs): the flight
  controls are one code-made InputActionMap ("Flight", always enabled; chat typing and a capture suspend it,
  `GameControls.Suspend`) of 39 rows (steer, throttle, brake, boost, roll, level out, strafe left / right, dodge left /
  right (no keyboard default), fire, fire secondary,
  switch secondary, camera / turret view, cockpit view, auto turret, unlock target, action, autopilot menu, actions
  menu, fast-forward, wingmen, Khador Drive, cloak,
  time extender, mouse steering, mining drill (read only while drilling, so its keys may overlap the flight's), chat,
  chat send, chat channel, screenshot, toggle HUD, multiplayer window (N), distress call (unbound)), each with two
  keyboard / mouse slots and a controller slot (binding
  groups Keyboard / Gamepad; steer's controller slot is a whole stick or the D-pad so the stick keeps its radial dead zone,
  the other controller slots also take a stick pushed one way past half way, "RS ↑";
  steer, throttle and roll are composites captured part by part; a later part can't take what an earlier part of that
  slot took, #24: the stick still pushed left for Roll's "left" was taken again for "right"). Defaults: the PC keys
  above and the controller buttons
  (LS steer, RB / LB throttle, A boost, Y level out and fast-forward (hold), RT / LT fire, D-pad right / up / down switch
  secondary / camera / auto turret, B unlock target, X action, View autopilot menu, D-pad left actions menu, RS / LS
  press cloak / time extender, RS ← / → dodge: a binding like any other (it was a fixed flick that the cloak's
  right-stick-press binding switched off; a stick binding is ignored in free look, where the stick turns the camera).
  Overrides in PlayerPrefs `controls_bindings`
  (by action name and binding index: the Input System's own JSON finds bindings by id, new every launch for code-made ones, so
  nothing saved applied after a restart; an empty override unbinds); "Reset key bindings" and Default settings (497) clear them. A
  capture (`GameControls.Rebind`, `PerformInteractiveRebinding`): Esc cancels, Backspace / Delete (or a right click on the
  cell) unbinds (a controller cell's capture takes the keyboard's Backspace / Delete too); remake (players couldn't find
  it, and a controller couldn't clear one): a bound cell's × (on hover, on the selected row's selected cell, always on
  touch) and Delete / the controller's X on the selected row clear the selected cell without a capture
  (`BindingRow.ClearSelected`, polled by the row itself in all three menus), the controller's Menu can't be taken; input
  of another kind cancels too (a keyboard cell: the controller's
  Menu / B or a tap; a controller cell: a mouse click or a tap; `CancelFromOtherDevice`), and so do 10 s without a match
  (a keyboard cell picked with a controller or on a phone waited for good with the menus blocked: a softlock); on a phone the
  keyboard cells only take a capture once a real keyboard was used this run (`InputMode.KeyboardSeen`; Android's back button
  arrives as Escape and doesn't count), Enter / A on a row then picks the controller cell; Reset key bindings needs no
  bound key; a key or button can be bound to several rows at once (no swapping:
  the controller has too few buttons for one each); meanwhile and on the frame after
  (`GameControls.BlocksMenus`) the menus ignore their keys and the panel's navigation events. Fixed on purpose: the menus'
  keys (arrows, Enter, Esc, controller A / B / Menu) and pause. The HUD hints (`InputGlyph.For`, rebuilt on
  `GameControls.Changed`; an unbound control's hint is left out), the action prompt's glyph and the hint texts' `#KEY_*`
  tokens (`FlightHints.KeyTokens`) show the current bindings, the controller's in the current button style (Xbox /
  PlayStation, see **Input modes**; the bindings stay `<Gamepad>/...` paths, so a binding is the same button on both;
  `GameControls.PadControl` also reads a capture of a pad-only control, e.g. `<DualSenseGamepadHID>/touchpadButton`).
- **Mouse cursor and controller** (remake, a player's request): on desktop the cursor hides while a controller is in use and
  comes back with the mouse (`InputMode.CursorShown`, applied in `InputMode.OnEvent`; `FlightHud.UpdateMouseSteering` keeps
  to it).
- **Input device log** (remake, `InputDeviceLog`; #194, a Steam Deck's controller "not working in Game Mode", no more
  details): a player's log names each controller the Input System sees, as a gamepad or as a joystick the `<Gamepad>`
  bindings ignore (an SDL device on Linux can come up as one), as it comes and goes; the window's focus changes (the
  Input System drops controller input while unfocused) and the Deck / gamescope environment variables. Players only.
- **Controllers on macOS** (remake, `UI.MacGameControllers` + `MacGameControllerRunner`, GoF2.Game; a player's report
  2026-10-22): the Input System reads Mac controllers at the HID level and knows only Xbox pads, DualShock 4 / DualSense,
  the Switch Pro Controller and the Nimbus+; others (8BitDo, Joy-Cons, MFi pads) came through as joysticks the `<Gamepad>`
  bindings ignore. Apple's GameController framework is read through the Objective-C runtime (P/Invoke of libobjc's
  `objc_msgSend`, the framework `dlopen`ed: no native plugin to compile on a Mac): every controller with an extended
  gamepad profile becomes a virtual `MacGameControllerGamepad` (interface "MacGameController", fed in
  `InputSystem.onBeforeUpdate` like `VrPad`; Apple's positional A / B / X / Y, buttonMenu = start, buttonOptions =
  select, the stick presses checked with respondsToSelector), except one per Input System gamepad of the same family
  (`FamilyOf` productCategory / vendorName against `FamilyOfLayout`), which keeps its own device and rumble; a Sony pad
  reports Sony as its maker (PlayStation glyphs, Share on a DualShock 4). The list is matched every 0.5 s, devices added
  outside the input update. No rumble on the virtual pads (GameController's haptics are CoreHaptics), no gyro. Pads in an
  Xbox 360 / XInput mode need a driver macOS doesn't have: neither path sees them. Tests `MacGameControllersTests`. Not run
  on a Mac yet.
- **D-pad taps** (`DpadTapNavigation`): UI Toolkit's runtime input samples UI/Navigate once a frame and drops a D-pad
  press released within the frame (the Steam controller's trackpad D-pad); the main and station menus send those taps
  (`wasPressedThisFrame` and already up) to the panel as NavigationMoveEvents.
- **Right stick scrolls the texts** (remake, #169, `StickScroll`): in the main and station menus the right stick, up and
  down, scrolls the shown scroll view that overflows and doesn't hold the focus (the lounge's dialogue, the shop's and
  the item window's descriptions; a focused list scrolls to its focus); of several, the last in the document. Pushed
  more sideways than up or down it scrolls nothing (the hangar's ship turning); not during Inspect ship (its camera), not
  in the flight HUD or the star map (steering, the map's turning).
- **Input modes:** `InputMode` tracks the last input kind (Touch / KeyboardMouse / Gamepad, switching on the first real
  input of another kind, on any platform). The flight HUD (`UI/Flight`, `FlightHud`) shows the touch controls (see
  **Touch controls** below) only for touch, keycap hints for keyboard and controller button hints for a controller, in
  the **Xbox** or the **PlayStation** style (`InputMode.Style`, remake; Options > Controls "Controller button icons",
  `Settings.PadGlyphs` Auto / Xbox / PlayStation, Auto by default: the controller last used picks it,
  `InputMode.StyleOf`: the Input System's DualShock / DualSense layouts (`DualShockGamepad` and every layout based on
  it, any platform) or a pad reporting Sony as its maker (or DualSense / DualShock / PlayStation in its product name) =
  PlayStation, anything else (XInput, Switch Pro, VR's `VrPad`) = Xbox; switched live, `InputMode.StyleChanged`, which
  `GameControls.Changed` passes on). Every controller glyph is a `PadGlyph` (`InputGlyph.Pad` / `For`), which redraws
  itself when the style changes, so the glyphs built once (dialogue buttons, Launch, the hangar's LT / RT, the mod
  browser's X) follow too; Xbox unchanged (coloured A / B / X / Y, LB / RB, LT / RT, LS / RS, VIEW / MENU), PlayStation
  dark face buttons with their symbols drawn by Painter2D (cross blue, circle red, square pink, triangle green: the UI
  font has no ✕), L1 / R1, L2 / R2, L / R sticks, L3 / R3 presses, OPTIONS, CREATE (SHARE on a DualShock 4,
  `InputMode.DualShock4`), TOUCHPAD. The text names (`GameControls.ShortName(path, style)`: binding cells, the `#KEY_*`
  tokens) use × ○ □ △ (Inter has them). Touch steering goes through `ShipController.SetSteer` (the stronger of touch and
  the built-in actions wins); touch mode also switches the chase camera to the original's fixed touch damping. Esc /
  Android back / controller Menu / the touch pause button open the **pause menu** (`PauseMenu`, MenuTouchWindow mode 1:
  40 Pause; Resume 41, Missions 129 (remake: also in the alien orbit, where the original hides it, and with the Most
  Wanted criminals on the move), Cargo hold 166, Options 31 (every option, the text language too: the Language tab's
  row, `OptionDef.inGameOnly`, see **Options**), Skip 395 during the prologue / rescue, Back to Main Menu 522 → 523):
  game time and sounds pause (`Navigation.PauseMenuOpen`, `AudioListener.pause`); after the player's death Esc goes
  straight to the main menu; remake: a click / tap beside the menu resumes (on the release, a press begun there; not on
  a question, nor when it only closed an Options drop-down or "?" window). **Action Freeze** (59, `PhotoMode`, the pause
  menu's state 0xd / `MGame::setCinematicMode`): the world stays frozen, the HUD hides, an orbit camera around the ship
  (drag −0.005 rad/px, the pitch free all the way round and clamped to ±200 px only by a fling (`MGame::OnUpdate`
  0x1aec2c; the in-flight free look clamps always), ×0.9 fling, the camera turning with the orbit (no flip over the
  top), pinch / wheel / triggers zoom 1500..20000 units, arrows / sticks), the logo 0x534 (the Full HD logo) and a
  footer with Back (170); the original's share buttons are dead code (no upload). Remake: the overlay is Inspect ship's
  (`OrbitViewUi`, `.orbit-view-*` in GoF2Common.uss, shared by both): "ACTION FREEZE" at the top left, the controls as
  hints at the top right (rotate, zoom, Hide UI H / Y, Screenshot Enter / P / A, Back; the last three clickable), Back
  and the title screen's remake logo (`logo_gof2_remake.png`, `.photo-logo`) in the footer; Screenshot is the original's
  60 "Save to library" (PNG to Pictures/Galaxy on Fire 2, Android MediaStore; 55 / 56); Hide UI clears the screen until
  a short click / tap or H / Y again; + / - (Page Up / Down) zoom too; the overlay fades while the camera is dragged.
  **Photo tools** (remake, players' suggestion "Everspace-style Action Freeze", 2026-10-17; `PhotoTools`, shared with
  Inspect ship): the **free camera** (F / the controller's X / its hint; `World.FreeCamera`: yaw about the world's up,
  pitch short of +-90 deg, moved in its own frame and up along the world's, within 5 km of the ship in flight, six ship
  radii and above the pad in the hangar; W A S D, E / Space up, Q / C down, Shift x4, Alt x0.25, a drag or the arrows
  look (the mouse toward the drag, a finger grabs), the wheel / + - / triggers zoom the lens; the left stick moves, the
  right looks, RB / LB up / down; touch: a pinch moves forward / back (spread / pinched together; the pinch back did
  nothing until 2026-10-18: `FreeCamera.Move` takes a direction and a positive distance), two fingers slide) and the
  **Camera settings** panel at the left (Tab / View / its hint; `PhotoSettings`, kept for the run, shared by both modes;
  rows are `OptionControl`s): orbit / free, field of view (from the camera's own at each entry), roll (-180..180,
  holding level and the quarter turns, `PhotoTools.SnapRoll`), the free camera's speed, depth of field (Default = the
  game's / Off / On the ship = Gaussian sharp over the hull / Manual = Bokeh at a focus distance, 1 m-3 km, Gaussian on
  phones) with its blur, bloom (Default = the game's option / Off / Custom with an amount x0..3: a URP Bloom override of
  the
  intensity only, so the threshold and scatter stay the game's; anything but Default also holds the original bloom pass back,
  `Visuals.ClassicBloomPass.Suppressed`, which no volume reaches), the sun's lens flare (Action Freeze only: Default = the
  Graphics option / Off / On, `PhotoSettings.FlareShown` read by `FlightHud`; players' request 2026-10-22: the flare stayed
  over the photos, `.hud-photo` hides the HUD but not the flare's layer), exposure (over `Settings.BrightnessExposure`),
  contrast, saturation, white balance (the temperature shown in kelvin like a photo editor's, `PhotoSettings.Kelvin`: URP's
  -100..100 through its white point and McCamy's formula, 6500 K neutral, about 2600-35 800 K, higher warmer; the tint
  -100..100, magenta +), vignette, film grain, chromatic aberration, lens distortion (-100..100 %, barrel +; the unclear
  rows with a "?" description), hide the ship (its renderers moved to layer 28, which the camera then leaves out: the
  engine parts are switched every frame), composition guides (`PhotoGuides`, plain C#, polylines in the frame's pixels
  drawn by Painter2D: thirds, quarters, the golden ratio grid, the golden spiral with its rectangles (the frame cut
  along its long side at 1 / phi, stretched to the frame; Guide direction flips it toward any corner), the golden
  triangles (either diagonal, right angles on the screen), diagonals with 45 deg lines from the corners, the centre), a
  frame (16:9 / 21:9 / 4:3 / 1:1 / 9:16: dimmed bars, the screenshot cut to it) and the screenshot's size (the screen's,
  x2 or x4: `ScreenCapture.CaptureScreenshotAsTexture` superSize, capped so the picture stays within 16384 px,
  `PhotoSettings.SuperSize`; a URP single camera request lost the post-processing), Reset. After a screenshot a notice
  shows where it went (`PhotoMode.StoreAt` returns the path) with Show in folder (O; desktop: Explorer with the file
  selected, `PhotoMode.ShowInFolder`). A press that begins on the panel, the notice or a button never turns the camera,
  even dragged off it (`PhotoTools.PressOverUi`, also the hosts' orbit drags). The panel flies in from the left and
  fades (0.45 s, ease-out cubic, USS transitions on `.photo-panel` / `--open`; closed it is hidden by `visibility`;
  `PhotoTools.Capture` hides it inline too, since the open class's `visibility: visible` beats the overlay's inherited
  hidden and the panel showed in screenshots until 2026-10-18). Inspect ship fades the station menu's `side-shade` /
  `bottom-shade` with the menu (`.station-inspecting`): they darkened the left of the view and its screenshots. The
  effects are a global volume of its own (layer 26, priority 1000, a profile made in code;
  `Assets/Settings/PhotoModeShaders.asset` keeps their shaders in builds, URP strips unused post-processing variants; it
  holds every override the panel uses: depth of field, colour adjustments, vignette, film grain, bloom, white balance,
  chromatic aberration, lens distortion). The hangar hands the free pose, roll and lens to `HangarInspect`
  (`SetFreePose`, `RollDeg`, `FovDeg`), which keeps them through the blend back. Not in the Customize screen. **Tilt
  steering** (`TiltSteering`, `MGame::handleAccelerometer`; Controls: 490 Touch / 491 Accelerometer when the device has
  one, 492 Steering Calibration (493, calibrates on first use), the tilt sensitivity 0..1): yaw clamp(Y·2.5)², pitch
  3·max(x' − cal1, Z − cal2) squared, no dead zone; in tilt mode the flight model's pitch ramps use 1.45 / 1.25 × the
  tilt sensitivity (the stick, keys and pads now use the plain sensitivity, like the original); the touch stick is
  squared per axis (`Hud::getAnalog`²); dimmed stick (alpha 50).
- **Touch controls** (`TouchControls`, `Reference/research/touch_hud.md`): the original's flight controls as UI Toolkit
  elements with the original images (`GoF2Hud/touch_*`, **GoF2 > Build > HUD Images**) and their own pointer events,
  placed like `Globals::setCoordsSteer` / `setCoordsFire` with the Android defaults S = 415 / F = 365 in the safe area
  (panel units = HD pixels): the left group (fast-forward / time extender and autopilot on their pill from
  `NavigationView`, the pill only behind a shown icon, the autopilot hidden at campaign 0 / 1; the fixed stick 0x4c1 /
  0x4b6 / 0x4b7 at centre (165, S + 277), travel 94, touch area ±112 × ±216, no dead zone, a double tap within 499 ms
  levels out; boost 0x4b2 / 0x4b3, alpha by the charge, the ready blink (the original's 2 s of one lit frame per 80 ms
  strobed at 120 Hz; remake: 5 flashes in 1 s, lit the last 100 of every 200 ms, in time at any frame rate)), the right
  cluster (background 0x6aa / 0x4c6 at (w − 321, F); fire 0x4b4 / 0x4b5, also the action button: autopilot / planet jump
  / docking target / asteroid approach / undock with the arrow 0x536 when fire acts, the rest is Mining's own fire
  handling; a second press within 249 ms latches autofire; quick menu 0x4ba (opens the quick menu when it has entries),
  camera (the next mode's icon), secondary 0x4bc (hidden at 0 ammo; held 350 ms: the weapon strip, see "NPCs and
  combat", HUD), the auto-turret toggle), pause 0x4b8 at (w − 121, 24) in the HUD root (also during cutscenes, the
  launch / arrival camera and jump scenes, there at 12 % opacity, opaque while held (remake, `Frame.cinematic`, for
  immersion), at its last place in flight: a cutscene hides the safe area (display: none), which zeroes the touch
  layer's size, and the width's 1920 fallback pushed it off a narrower HUD; sounds 124 / 123; the top-right readout
  moves left of it: set from the button's place each frame, `FlightHud.KeepReadoutClearOfPause`, after a phone showed
  the readout over it; the HUD root's input classes are re-applied whenever they disagree with `InputMode.Current`), the
  secondary plate 0x4c2 at the bottom centre ("<name> (<n>)", remake: a tap switches). Buttons act on release over them
  (sliding off cancels), fast-forward while held. The empty screen behind them (`.touch-gesture-zone`): a vertical drag
  sets the throttle after 160 px (400 px = 100 %, up = faster) with the gauge 0x548 under the crosshair (2 s fade), a
  sideways flick dodges, in free look the drag orbits. No Level button, throttle slider or action prompt on touch (the
  original has none). The original's Configure screen (Options > Controls, 494 / 495: S and F dragged up and down) is
  two sliders in Options > Controls (remake, #61; shown with touch input): "Touch controls height: stick / fire
  buttons", `Settings.TouchLeftHeight` / `TouchRightHeight` 0..1 between the highest (300) and lowest place the original
  allows (h − 425 / h − 311), -1 = the defaults 415 / 365 (`TouchControls.Height01`). Remake (the user's request,
  2026-10-27): Options > Controls "Touch stick follows your thumb" (`Settings.FloatingTouchStick`, off by default, shown
  with touch input): a touch in the stick's area (±112 × ±216 round its place) moves the base and knob under the thumb,
  that point the centre (no deflection), and they go back to the stick's place on release (`TouchControls.stickHome`,
  `PlaceStick`); the touch area stays where it is. Hidden under dialogues, the pause menu, the star map and death; only
  the pause button in cutscenes, jumps and the launch camera; only the stick under the autopilot menu. The autopilot /
  fast-forward block (`NavigationView`'s `navButtons`) goes with them (`TouchControls.Update` hides it in Off /
  PauseOnly, `FlightHud.touchNavShown`): it stayed over the pause menu and cutscenes.
- **Flight hints** (`FlightHints`, the original's hint chain: 596 / 590 / 593 / 584 / 637 / 648 / 619 / 1725 / 620 / 621
  / 617 / 636, the Nivelian 443 / 444 radio, 599-609, the volatile 3202 and red plasma 3161; keyboard variants +1,
  `KeyTokens`): each once (`Session.Hints`), in a ChoiceWindow that pauses the game. Remake: the tutorial popups (these,
  the stations' first-visit help 622 / 627 / 635 / 640, the map's 628 / 631, the hangar's 587-589, and the story
  conversations' Info pages that
  only teach the controls or menus, `StoryTable.Shown`: 1677 / 1680 (index 0's whole briefing), 1718, 1725, 1730, 1746, 1749,
  1750, 1753, 1816, 1862, 1878; the Info pages with story content (1782, 1896, 191 / 192, 2171, 2184) always show; not the
  medal hints) are an option, off by default (`Settings.TutorialHints`, Options > Gameplay "Tutorials"); a new game's
  Game options panel has it as a toggle (`MainMenu.RefreshTutorialToggle`; it was a question after the economy); while
  off nothing is marked shown.
- **HUD readout** (`HudReadout`, top right, `GoF2Hud/hud_*`): the level timer, cargo "load / max t", passengers (0xb8,
  the counter against the cabins), goods (0xae), the kill score (Challenge / 36) and the volatile bar.
- **Camera modes** (`FreeLookCamera`, `MGame::switchCamera`): T / D-pad up / the turret button cycle standard (or
  cockpit, see below), turret (manual turrets) and free look (217 / 219 / 218 / 220): an orbit around the ship (−0.005
  rad/px, pitch ±200 px, zoom 1500..20000, middle mouse / right stick / touch drag); no locks or tractor in the turret
  view.
- **Cockpit view** (remake, `FreeLookCamera.Mode.FirstPerson` = the original's mode 2,
  `TargetFollowCamera::enableFirstPersonCam`, which `switchCamera` / `nextCamId` always skip; its text 219 "Cockpit" and
  the camera button's eye icon 0x4be / 0x4bf, `GoF2Hud/touch_cam_2(_on)`, were already in the data):
  `ChaseCamera.firstPerson` puts the camera rigidly on the bow with the root's rotation (the guns' line, so the
  crosshair stays at the centre): `ChaseCamera.FirstPersonSeat` from the hull's meshes (`PlayerHull.HullBounds`, no glow
  layers; game ships, mods' glTF and kit builds, the debug capital hulls), on the guns' line (the pivot, which the
  crosshair, locks and beams aim from) a fifth of the half height above the middle but at most 1.5 m off it (a capital
  hull's 100 m dropped the crosshair 5 deg), 0.8 of the way to the bow (a hull over 300 m long 0.9) but behind the
  frontmost primary gun (not behind half way), so the muzzle flashes and shots start in view; the crosshair, the lock
  boxes and the beams' auto-aim take their aim line from the cockpit camera meanwhile (`ChaseCamera.AimOrigin`: a
  capital hull's bow camera sat past the pivot's 1.1 km aim point, which hid the crosshair); the model's renderers on
  layer 28 (VR's / Action Freeze's hidden layer, rescanned every 0.5 s and on a hull swap: turrets, kit weapons), the
  near plane 0.3 m; the hit shake and the explosions' / boost's rumble as small turns of the view (the original keeps
  x0.001 / x0.0015 of them), the boost's FOV at half; the FOV option as everywhere. Whatever takes the camera over turns
  the chase camera off (launch / arrival, cutscenes, jumps, mining's landing, object docking, death, Action Freeze): the
  hull shows and the camera starts from behind the ship (`ChaseCamera.OnDisable`, not inside a tumbling bow), and it
  comes back in the cockpit when the chase camera does; the turret view, free look (also the held key) and the Liberator
  leave and return to it. Not in the camera button's cycle (players' feedback, 2026-10-22: its first press went to the
  cockpit, and they wanted the orbit view at once): holding the camera button 0.5 s (T, D-pad up, the touch camera
  button: `FreeLookCamera.CameraDown` / `CameraUp`, a tap cycles on release) or the "Cockpit view on / off" binding
  (`GameControls.CockpitView`, F2, no controller default) switches between the chase and cockpit view (`ToggleCockpit`),
  and the cycle runs from whichever is the ship view: it -> turrets -> free look -> it; the HUD hints show COCKPIT (F2)
  or COCKPIT (HOLD) with D-pad up. The ship view picked last is kept for the next flights (PlayerPrefs
  `flight_camera_cockpit`, `FreeLookCamera.Base`); a turret view or free look ending by itself goes back to it. The
  dodge works in it (`FreeLookCamera.ShipView`). Not offered in VR (it flies from the seat). Local only: multiplayer is
  unaffected.
- **Free look with the mouse** (remake): with mouse steering the cursor stays captured in free look and plain mouse movement
  orbits the camera (`FreeLookCamera.mouseLook`, the mouse delta), the ship's mouse steering pauses; the middle button held
  works without mouse steering. Hangar tabs: Q / LB left, E / RB right (both used to go right).
- **Camera views' own inversion** (remake, #121; Options > Controls "Camera views: invert up / down" / "left / right",
  `Settings.InvertOrbitY` / `InvertOrbitX`, off by default; `Settings.OrbitTurn`): the cameras turned round the ship or
  looked around by a drag, a stick or keys (free look, Action Freeze, Inspect ship, the photo tools' free camera, the Mod
  Builder's hangar / space / room views), apart from the flight's steering inversion.
- **Controller gyro** (remake option, Windows, off by default; `ControllerGyro`, Options > Controls "Controller gyro" +
  "Gyro sensitivity"): DualSense / DualShock 4 / Switch Pro / Joy-Cons through JoyShockLibrary 3.0 (MIT,
  `Assets/Plugins/JoyShockLibrary/x86_64`, Editor + Windows x64 only; its notice on the About page): tilt steering from
  the accelerometer (gravity, like the phone's `TiltSteering`): tilting forward / back pitches, rolling like a steering
  wheel steers sideways, by the angle from the rest pose taken on connecting and on Level out (pitch in the Y-Z plane,
  roll as gravity's lean toward X, so any grip angle works; both reading conventions cancel against the rest pose), a
  full offset at 20 deg / sensitivity, a 2 deg dead zone, added to the other steering. The first version integrated the
  gyro's turn rate in player space: the uncalibrated bias walked the pitch while the controller lay still and the
  max-of-both rule held the stick off (#35). Steam Input / DS4Windows on for the game hides the motion data (a reading
  without gravity is logged once). The device scan (JslConnectDevices, ~360 ms) runs on a worker thread every 3 s while
  no motion controller is connected (#37: on the main thread it stuttered manual flight; the autopilot skips the gyro).
  Not tested with a real controller yet (the axis signs come from JSL's documented frame; the invert options cover a
  flipped axis).
- **Haptics** (remake option, Options > Controls "Vibration", `Settings.HapticsIntensity` 0..1, 0 = off, default 100 %; the
  original has none: `Engine::Vibrate` is an empty stub and `Globals::init` calls `VibrateEnable(false)`): `Haptics` (created
  by `Bootstrap`, kept for the run) sends to the controller last used while `InputMode` is Gamepad (the Input System's
  `SetMotorSpeeds`: XInput, DualShock 4 / DualSense; a no-op where the platform has no rumble) or to the phone while it is
  Touch (on Android also while it is Gamepad with a controller that reports no vibrators, #73: the device's motor stands
  in, the one built-in controls of handhelds like the Konkr Pocket FIT have; a controller that reports some, `vibratorCount`
  in its Android capabilities, rumbles itself, `Haptics.HasVibrators`, #138: the Input System does it from 6000.6 on, and
  from #73 to #138 every Android controller went to the phone) (`PhoneVibrator`, Android `Vibrator` over JNI:
  `VibrationEffect.createOneShot` with the amplitude when the motor
  has amplitude control, else shorter buzzes for weaker pulses; `AndroidVibratePermission` (Editor) adds the VIBRATE
  permission to the Gradle manifest). `HapticMixer` (plain C#) mixes the pulses (held for the first quarter, then a linear
  fade; the strongest per motor wins) and the frame's continuous rumble. Pulses (`Haptics.Play` with the presets in
  `Haptics`): primary shots (controller only), secondary launches, a ship lock, player hits by layer (shield / armor /
  hull), ramming an asteroid or a landmark, the boost's start, the hull giving out and the ship's explosion, gate /
  Khador jumps (with the big flash: 1300 / 1733 ms into the animation, `SystemJump.GateFlashMs` / `KhadorFlashMs`),
  planet jumps (with the sound's swell, 1250 ms in: no flash, `Navigation.PlanetJumpRumbleMs`), mining (landing, each
  ton, each layer, the drill off target every 180 ms, won / lost). Rumble
  (`Haptics.Rumble`, each frame): ChaseCamera's rumble before the camera shake option (explosions, the Liberator, the
  boost), CutsceneCamera's (Rumble × amplitude / 50), scraping a landmark (0.35), the wormhole's pull, the Khador charge,
  the drill on target; on the phone only from 0.2 (overlapping 110 ms one-shots). Everything stops while
  `Navigation.InputHalted`, `AudioListener.pause` or without focus (`StopAll` on focus loss, pause and destroy, so no
  motor keeps running after Play mode); the slider's preview (`Haptics.Preview`) plays in the pause menu too. Motors run on
  the real clock. Not tested on a device yet.
- **Haptics fix (#24)**: a stop is always sent (`HapticMixer.ShouldSend`; the 0.01 step left a fade's last few percent running
  on the Xbox until the guide was opened) as an explicit zero, and the motor speeds are sent again every 0.5 s while running
  and for 2 s after a stop.
- **Mouse steering** (remake option, `ShipController.mouseSteering`; desktop, and since 2026-10-24 Android / iOS with a
  mouse connected (`Mouse.current`; the lock is Android's pointer capture; the option rows and the cursor mode show only
  then, a keyboard alone steers at its own sensitivity; not yet tried on a device); M or the middle mouse button toggles
  it in flight, the PC version's text 18): the cursor locked and hidden, the virtual offset (±0.7 half screen,
  sensitivity 2.7) steers like the stick; remake: a round dead zone around the centre (Options > Controls "Mouse
  steering dead zone", `Settings.MouseDeadzone`, 0-30 % of the vertical range, default 8 %; the original has none) where
  the ship flies straight, the steering rescaled from 0 at its edge to full at the limit
  (`ShipController.ReadMouseSteer`, `MouseInDeadzone`); a small dot marks the offset once it leaves the dead zone (one
  crosshair only). Remake: "Mouse sensitivity" (`Settings.MouseSensitivity`, 0.1-3, default 1, `Settings.MouseMove`)
  scales the mouse's movement for the steering, free flight's virtual stick, the mining drill and free look. The
  original's Sensitivity doesn't apply to the mouse (the fixed 2.7 above), and its PC cursor moved at the system's
  pointer speed; the Input System reads the raw counts, so a high-DPI mouse reached full deflection on a flick and
  Sensitivity 0.2 changed nothing (Discord "Mouse Sensitivity seems to do nothing", 2026-10-30; test
  `SettingsTests.MouseSensitivityScalesTheMouseMovementOnly`). The wheel on phones and tablets (`MouseWheel`, Game; Discord,
  Egg, 2026-10-30): a USB mouse on a Samsung tablet goes through the `sec_touchpad` driver, which sends fractional scroll
  while the mouse only moves (at most 0.625 in 1,045 events, every real notch 1 or more), so the throttle drifted as the
  mouse steered; on mobile a frame's scroll under 0.75 is no notch, for the throttle and the free-look, photo mode and
  Inspect ship zooms alike (a smooth-scrolling wheel or a touchpad's scroll steps less there); desktops keep every scroll. Turned off, the PC version's cursor mode (`Globals::mouseCursorActivated` 0, the manual's "Mouse
  control menu / ship"; `FlightHud.cursorMode`, `TouchControls.Frame.cursor`): `Hud::draw`'s buttons show and take
  clicks (the `Globals::iPad == 0` path and the Full HD manual's screenshot): no stick or gestures, the cluster from the
  bottom-right corner (0x6aa at (w − 397, h − 406)), the autopilot / fast-forward pill and the boost at the bottom left,
  the pause button top right (the cargo readout left of it); the fire button's arrow replaces the action prompt and the
  hint column hides; a click on a button isn't also the mouse's fire / missile binding
  (`WeaponSystem.mouseOverControls`). Handling-dependent camera damping only for keyboard / mouse. Losing focus on
  mobile (or without `runInBackground`) opens the pause menu.
