# VR

Notes moved from `CLAUDE.md` (its index lists every notes file). Code comments citing `CLAUDE.md "<section>"` mean the section or bullet of that name here.

## VR (remake-only, PC VR through OpenXR; in progress)

Packages `com.unity.xr.openxr` (+ XR Plug-in Management, XR Core Utils). **GoF2 > Setup > Configure VR (OpenXR)** (`VrSetup`,
idempotent): the OpenXR loader for Standalone with Initialize on Startup off, the controller profiles (Oculus / Meta Touch,
Index, Vive, WMR, Reverb G2, Khronos simple). Code in `Scripts/Runtime/App/Vr` (`GoF2Remake.Vr`).

- **Only when asked for** (`VrMode`): `-vr` starts OpenXR before the first scene (`InitializeLoaderSync`, seated: tracking
  origin Device, recentred); without a headset / runtime it logs why and runs flat. `-vrsim` is the same VR layout on the
  desktop without a headset (right mouse drag looks around) for testing; in the Editor `GOF2_VR` = on / sim or PlayerPrefs
  `debug_vr` 1 / 2. `VrMode.Enabled` / `Headset` / `Simulated`.
- **Rig** (`VrRig`, one per scene, attached on sceneLoaded and after the first scene): the scene's camera stays the logical
  camera (`Camera.main`: projections, HUD markers, cutscenes, the chase camera unchanged) but renders nothing (culling 0, no
  post, desktop only, under the rest); the VR eye (`Camera.CopyFrom` of it, the eye's pose reset) on a rig that follows it
  after every camera script (`DefaultExecutionOrder(10000)`): its whole pose in flight, its position and yaw elsewhere;
  head (Input System `TrackedPoseDriver`, `<XRHMD>/centerEye*`), hands (`<XRController>{Left/RightHand}/pointer*`, small
  handles), the AudioListener moves to the head.
- **UI** (`VrPanels`): every PanelRenderer's PanelSettings renders into its own RenderTexture of the window's size (so the
  UI's screen <-> panel math stays exact) on a quad of the floating screen (URP Unlit transparent, layer 29, stacked by
  sorting order); assets get their target back when the scene ends. The screen: 1.9 m wide, 2 m ahead with a headset; in
  flight and in the simulation it fills the logical camera's view at 2 m (the HUD markers sit on what they mark; the
  desktop mouse lines up in the simulation). The star map's camera draws into a texture on the screen (the eye sees only the
  VR layer meanwhile; StarMap switches every camera off). A panel's quad shows only while a renderer of it runs
  (`VrPanels.Runs`; PanelRenderer is a Renderer, not a Behaviour): the texture keeps the last frame, and the star map's
  overlay, whose object goes on Close while its PanelSettings stays, stayed over the main menu (#205; `VrPanelsTests`).
- **Controllers** (`VrPad`, a headset): the two controllers as one virtual Gamepad fed in `InputSystem.onBeforeUpdate`, so
  every control, its rebinding and the menus' controller navigation work unchanged: sticks, triggers, grips = LB / RB,
  right A / B, left X / Y, left menu = Menu, left stick click = View (autopilot menu), right stick click = D-pad left
  (actions menu); the right controller's laser is a virtual Mouse placed where it hits the screen (trigger clicks).
- In VR: the menu backdrop's camera doesn't turn and starts facing the station (`MenuCamera`), no launch / arrival camera
  (`Settings.LaunchCamera`), no mouse steering.
- **Cockpit** (`VrCockpit`, flight): `ChaseCamera` puts the scene camera rigidly on the seat (`VrCockpit.Seat`, 1 m above
  the ship's pivot, the ship's rotation; no lag, slide, shake or boost zoom), so the rig, the head and the cockpit (a child
  of the rig) ride with the ship; the player's hull is hidden (its renderers on layer 28, `VrRig.HiddenLayer`, which the
  eye doesn't draw; back while the cockpit is off). The same code-built cockpit for every ship
  (primitives, URP Lit dark metal: dashboard, front panel, hood, floor, walls, seat back, side consoles, canopy struts) until
  a modelled one replaces it. The flight HUD is split by region of its texture (`VrPanels.Crop` / `TextureOf`, panel pixels
  at 1920 x 1080): the canopy HUD shows only the middle (x 326..1594, y 0..1026: crosshair, markers, lock plate, messages,
  menus; `.hud-vr` centres the autopilot menu), the displays the corners (status bars + recharge icons left, cargo readout
  right, the secondary plate centre; the control hints aren't shown in the cockpit). The laser works in flight only while
  `Navigation.InputHalted` (a menu, conversation or map; else the trigger fires). Meanwhile the screen would sit behind
  the dashboard: with a headset it rises above it (1.6 m wide, 2 m out, its bottom edge 2 deg below straight ahead, tilted
  toward the eye), the simulation brings it 0.3 m out, in front of the cockpit (still filling the view). `VrRig.DebugLook`
  fixes the simulation's look for tests.
- **Cutscenes** (`VrRig.Cinematic`): while a level script's camera, the launch / arrival fly-in, a gate / Khador / planet
  jump scene or the death camera holds the scene camera and it has left the seat: stabilised, the rig at the cinematic
  camera's position with a level horizon (only its yaw: within 20 deg of the camera's heading it doesn't turn, beyond that
  at most 40 deg/s; never its pitch or roll, so a shot looking down has its subject below), the cockpit switched off (the
  hull shows), the screen at the 1.9 m size; every cut (60 m or 30 deg in one frame, entering or leaving) re-faces the
  camera's heading behind a 0.3 s fade from black (a quad at the eye).
- **Station** (`StationLevel.VrCamera`, `VrStation`): standing, still (no drift, sway or intro). The hangar: on the floor
  between the original camera's spot and the ship, facing it, the eyes 1.7 m above the hull's bottom, 5 m past its radius
  (the ships are ~60 m across: they tower over you); the bar: the original's view point B, level; the visitors turn to the
  head (`UpdateBillboards`, upright). The pointer past the UI (`VrRig.WorldPointer`: `VrPanels.UiAt` = a UI element under
  it, not a full-screen layer) picks a visitor (a 0.45 m capsule feet-head; the trigger / a click opens their chat,
  `StationLevel.VisitorPicked` -> `LoungePanel.OpenChat`), and with a headset the grip on the ship grabs it: pulling the
  hand right / left turns it (4 rad per metre along the head's right), letting go flings it (`FlingShip`).
- **Stick, throttle, radar** (`VrControls`, `VrRadar`, in the cockpit): a small side-stick on the right console and a speed
  lever (a shaft with a crosswise grip) in a slot on the left console, always there: not held they show the ship's inputs (the stick tilts with
  `ShipController.SteerInput`, the handle sits at the throttle). Options > Controls "VR flight: grab the stick and throttle"
  (`Settings.VrGrabControls`, VR only, off by default: the controllers alone as a gamepad): grabbed with that hand's grip
  within 12 cm, the stick follows the hand's travel (6 cm = full; forward = nose down, right = yaw right; `VrControls.Steer`
  replaces the touch stick in FlightHud) and its twist rolls (45 deg = full, `SetRoll`); the handle slides along its 28 cm
  slot (`SetThrottle`); the grips aren't LB / RB meanwhile (`VrPad.GripsAsShoulders`). The radar scope in the middle of
  the front panel: top-down, forward up, ships within 3 km (square-root scaled, the rest on the rim) red / green / yellow by
  `hostileToPlayer` / `friendToPlayer`, only with a scanner (`CombatRadar.HasScanner`), the station cyan, the player white,
  a dot above bigger / below smaller. In VR the HUD's radar ellipse and its off-screen dots / boxes / gate icons are hidden
  (they reached onto the displays; the scope has them).
- **Shaders and materials in VR builds** (#115): the VR materials come from templates in `Resources/GoF2Vr` (VrScreen,
  VrGlass, VrFrame; `VrPanels.FromTemplate`): `Shader.Find("Universal Render Pipeline/Unlit")` was null in the Windows
  build (no material uses Unlit, so it was stripped), `VrRig.Init` threw and the VR UI never appeared (`LateUpdate` now
  stops instead of throwing every frame). The hand-written shaders the scene cameras draw (SpaceSky, Backdrop, SkyLayer,
  SpaceDust, HangarShadow, Cloak, ShieldBubble, ModEngineFlame, ModHologram, ModNozzleShimmer) carry the single-pass
  instanced stereo macros (UNITY_VERTEX_INPUT_INSTANCE_ID, UNITY_VERTEX_OUTPUT_STEREO, the setup in vert / frag,
  `multi_compile_instancing`): without them they drew into the left eye only, so the right eye had no sky. A new
  hand-written shader drawn by a scene camera needs them too. Not verified with a headset.
