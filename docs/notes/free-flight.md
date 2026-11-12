# Free flight

Notes moved from `CLAUDE.md` (its index lists every notes file). Code comments citing `CLAUDE.md "<section>"` mean the section or bullet of that name here.

## UI and platforms

- **Free flight** (remake, Options > Controls toggle "Free flight (EVERSPACE 2 style)", `Settings.FlightStyle`,
  `FlightStyles`; off by default, and off is the original flight and key layout unchanged; single player and sessions,
  it only changes how the player's own ship flies): built
  after EVERSPACE 2: its numbers come from recordings of the game measured by `Reference/tools/es2flight/es2measure.py`
  and from its asset values, its velocity step (2026-10-25) from the game's decompiled
  `UShipMovementComponent::ApplyControlInputToVelocity` and `BP_Ship_Player`'s tick
  (`Reference/research/es2_flight_measurements.md`, "The decompiled movement"). `FlightModel.StepFree` (plain C#):
  thrust / strafe / up-down held instead of a throttle, in the ship's frame: top speeds forward the original's 2 u/ms
  (100 m/s; EVERSPACE 2's cockpit speed gauge read 343 km/h = 95.3), backward, strafe and up / down x0.5 (the gauge: 172
  km/h each; they follow the forward one, so a boost raises them too); the input clamped to length 1, so thrust and
  strafe together share one top speed in their direction (79 m/s, not 100 and 50 apart); the thrust accelerates along
  the asked direction at A (1 - p^3) (p the speed that way over the top speed that way, EaseAccelerationExponent 3;
  forward A = 132 m/s^2, the others 90 m/s^2 over their half top speed: fitted to the gauge and the distance readout
  within ~1 m/s; at handling 100, sqrt-scaled by the handling, shrunk by `AccelMs`); the speed then capped to what is
  asked, or to what it was when already faster. Turning: every axis (roll too) at
  EVERSPACE 2's 125 deg/s at handling 100 (scaled by (H / 20)^0.75, `FreeTurnExponent`: a bare Rhino 51; softly capped
  there, only a tenth of the rest counting, `FreeTurnAboveShare`: an Inflict 127 instead of 148, a Dace with the Pulsed
  Plasma Thrust 146 instead of 335, players' feedback 2026-10-16: the nimble ships whipped round; the steering
  sensitivity option x0.6..1.6, 1 at its default, read from `Settings.Sensitivity`: `FlightModel.Sensitivity` is the
  original's ramp setting, a fixed 2.7 under mouse steering, which turned free flight x1.6 too fast; x0.85 boosting,
  measured), reached by a linear 400 ms ramp (`FreeTurnRampMs`, measured in
  EVERSPACE 2's cockpit view, which follows the ship rigidly); speed doesn't change it. No auto-level (Level out still
  works). Inertial dampeners (`ShipController.Dampeners`, toggled by
  `GameControls.Dampeners`, on at start): the velocity is turned toward the asked direction (the game's TurningBoost =
  forward acceleration / 6000 cm/s^2 = 2.3 per second, x (1 + cos) / 2, so a turning ship's velocity follows its nose)
  and a speed above what is asked brakes along the velocity at a quarter of the forward acceleration (34.6 m/s^2: the
  measured 33), up to 90 % less while thrusting the way the ship moves (InputInheritInertia, x 1 - 0.9 cos^2) and faster
  above the top speed by (speed / top)^1.67 (a boost's speed runs down in about a second); off: no braking, no turning
  boost and no ease, so the ship keeps its momentum and the thrust adds to it up to the top speed, only steering it
  beyond. A
  held boost on an energy pool: to the booster's original boost speed x 3.2 / 3 (`FreeBoostFactor`: the Me'al's x3 is
  EVERSPACE 2's measured x3.2, the gauge's 1110 of 343 km/h; Linear / Cyclotron x1.6, Synchrotron x2.67, Polytron x4.27;
  a booster without a boost speed x3.2), at x1.63 the forward acceleration (fitted: 215 m/s^2), a full pool lasts the
  booster's duration (attribute 27; 3.5 s without one: EVERSPACE 2's boost lasted 4.5 s held, so its equipment sets it)
  and refills in its recharge time, restarts from 20 % and only on a fresh press after running empty (`BoostHeld`,
  `BoostRechargePercent` = the pool). The forward
  thrust held is `Throttle` (0 with no key, whatever the momentum) and the forward speed `MoveSpeed`; with no forward
  thrust and no boost the main engines are off (`FlightModel.EnginesOff` -> `EngineDrive.Off`: mod flames shrink into the
  nozzles, the nozzle / throttle glows go dark, `ShipExhaust` stops, `ShipController.UpdateEngineParts` stops drawing the
  model's player engine parts (Renderer.enabled, not the jets), `NetPlayer`'s engine flag), so a ship at rest or coasting
  burns nothing; after the launch / arrival fly-in the ship comes to rest (`ShipController.freeSettle`: braked like the
  dampeners even with them off, until the player thrusts). With the dampeners off the thrust never pushes past the top
  speed (the game's cap: what is asked, or the speed it already had). The sideways part of the
  move goes to `StrafeSlide`, so the chase camera stays square behind a sliding ship. `ShipController.FreeFlightActive`: the
  option with keyboard / mouse or a controller (touch, tilt and VR keep the original); the autopilot, the launch / arrival
  camera, the turret view, the dodge and computer control (mining, object docking) run the original path and hand back
  the velocity (`SyncFree`). Mods can tune each style or set the style (see "Mods", Flight styles). The mouse: EVERSPACE
  2's virtual joystick (`ReadVirtualStick`: the mouse moves a reticle
  inside a circle of 0.5 x the screen height, `StickRadius`, EVERSPACE 2's measured ~0.52: the rate grows linearly with the
  reticle's offset; it stays where the mouse leaves it; beyond the mouse steering dead zone (a hard one, not rescaled, like
  the game's ~8 % of the screen height) the deflection is the whole offset over the radius (measured to 478 px: 0.229 deg/s
  per px, linear); still during free look, centred while the autopilot steers; the HUD
  draws the circle, `.mouse-stick`, faded in as the aim dot nears it (from 60 % of the radius, full at 90 %, 150 ms in,
  400 ms out; always shown it read as a second radar ellipse, shown for any steering it came up for every small
  correction; `FlightHud.UpdateStickRing`). The chase camera follows free flight faster by `FlightModel.FreeTurnSpeedup`
  (the free top turn rate over the original's full-stick rate, ~2.4 for an Inflict) and keeps the turn slide at the
  original's size (`ChaseCamera`): tuned for the original's rates it fell behind and the ship swung to the edge of the
  picture. Free look while held
  (`GameControls.FreeLookHold`, `FreeLookCamera`; both styles; in free flight the aim is ignored meanwhile). Its own key
  layout (`GameControls.FreeFlightDefaults`, overrides under `controls_bindings_free`, `ApplyStyle` on the option's
  change): W / S thrust, A / D strafe, Space / L-Ctrl up / down, Q / E roll, L-Shift boost, Z dampeners, L-Alt free
  look, LMB / RMB fire, R autopilot menu, Tab actions menu, H fast-forward; controller: left stick thrust / strafe, right
  stick aims, LB / RB roll (up / down unbound), the rest as before; the original-only rows (throttle, brake, strafe
  left / right) and the free-only rows hide in the other style's bindings list (`ControlRow.visible`); the Key bindings
  page's first line names the layout shown ("Key layout: ...", `bindingsLayout`), and its Reset resets only that layout.
  EVERSPACE 2's own defaults (its Input.ini) match the layout but for Z (its photo mode; its dampener toggle has no
  default key). Verified in Play mode before the measured numbers (keyboard events and a virtual controller): thrust, the
  dampened stop, the drift with the dampeners off, strafe and up / down, the roll, the right stick's turn, the boost's
  pool, the hints and the circle; the measured numbers checked on `FlightModel` alone (125 deg/s in 0.23 s, forward / strafe
  against the gauge within 0.04 of the top speed at every sampled time, the 3.0 s stop, reverse half the forward speed).
  Played in Play mode with the measured numbers (2026-10-10, the Phantom, handling 150; keys held by
  re-queueing the keyboard state before every Input System update, the Input System set to ignore focus for the test): 100
  m/s forward reached as the cubic ease predicts (90 % 0.77 s, 99 % 1.12 s), stopped in 2.96 s; strafe and up 50 m/s,
  stopped in 1.48 s; the boost 209 / 306 / 320 m/s at +1 / +2 / +3 s, braking 33 m/s^2 after it; the heading turning
  153.1 deg/s (125 x sqrt(1.5), the square root of then; now 169); the mouse's yaw the offset's horizontal share of the radius x the top rate (94.1 / 55.6
  deg/s for 232 / 137 px of 377), nothing back in the dead zone.
- **Auto-roll** (remake, 2026-11-02, Options > Controls "Auto-roll to the horizon (free flight)", `Settings.AutoRoll`, off by
  default; `FlightModel.AutoRoll` / `AutoRollInput`, after EVERSPACE 2's `UShipMovementComponent::GenerateAutoRollInput`,
  decompiled, with the numbers from the component's constructor named by the SDK dump's field layout: `MaxAutoRollInput`
  0.15, `RollAngleThreshold` 15 deg, `RollAngleTolerance` 3 deg, `PitchAngleThreshold` 81 deg, `PitchInputThreshold` 0.9,
  `EaseInTime` 2 s; ES2 turns it on per input device, a controller yes, keyboard and mouse no): in free flight, with no roll
  held and no level-out running, the ship rolls itself toward the horizon with a roll input of at most 0.15 (a fifteenth of
  the top roll rate, 19 deg/s at handling 100) once it leans past 15 deg, until it is within 3 deg; the input shrinks with
  the lean below 15 deg; after the nose passes 81 deg or a pitch input beyond 0.9 it is held back and eases in again over
  2 s (ES2's timer reset to `EaseInTime` on either). The decompiled routine's vector part didn't survive Ghidra (its matrix
  math), so the bank is measured as `AutoLevelRoll` does (the right wing's drop against the horizon). The original flight
  keeps its level-out. Tests `FreeFlightTests` (a 40 deg bank levelled within 3 deg, never faster than 0.15 of the roll
  rate; off by default; the ease-in after a pitch).
- **ES2 travel-mode flames** (2026-11-02): `EngineFlame.Travel` = the player's `FlightModel.CruiseVisual` into the flame
  shader's `_Travel` (EVERSPACE 2's TravelMode custom primitive data 21: the flame 1 + 4 x longer and in `_TravelColor`,
  lit whatever the thrust), so an ES2 kit's flames cruise as in ES2 (the cruise drive, the fast-forward cruise). The
  cruise's wider view was already the boost's (`ChaseCamera`: `BoostVisualPercent` includes `CruiseVisual`).
- **ES2 contrails** (2026-11-02, `Modding.Es2Contrails`, mounts of `slotType` 6 from the ES2 importer's `SKT_Contrail`
  sockets): after `BP_ShipBase`'s `InitializeContrails` / `UpdateContrails` (P_Vehicle_Contrail attached at each socket with
  its scale) and the emitter and material read out of the export and the shader dump (`es2_re/out/M_Vehicle_Contrail`):
  opacity = the eased boost (`EngineFlame.Boost`, ES2's `EngineVFXBoostCurrent`) x MapRangeClamped(forward . velocity^, 0.7,
  1, 0, 1) x min(socket scale, 1); a ribbon of particles born 50/s at the socket, staying where born, 80 cm wide (x the
  scale), white, alpha 0.5 x the opacity, living the opacity in seconds (0.01 at least); the material (`GoF2/ModEs2Particle`
  kind 5) fades it along and across (b / (1 + 3 (1 - b)), (a / (1 + 8 (1 - a)))^2), hides it within 24 cm of the camera
  fading in over 20 m, and bars it (T_Pattern_Barcode_002 in ES2, the flame noise thresholded here). ES2 lights the ribbon
  (volumetric per-vertex translucency); the remake's unlit blend takes a light grey (`Es2Contrails.Colour`, by eye). On every
  ship with such mounts (NPCs too, as in ES2); `"es2Engine": { "contrails": false }` is ES2's `ContrailsEnabled`. Tests
  `Es2ShipEffectsTests` (opacity, scale cap, life).
- **ES2 headlights** (2026-11-02, `Flight.Headlights` on the player's ship, `Modding.Es2Headlights` on a mod ship's
  `slotType` 7 mounts from `SKT_HeadLight`; Options > Controls "Headlights on / off", L, `GameControls.Headlights`;
  `Settings.Headlights`, on by default as ES2's `bHeadlightsEnabled`): after `BP_Ship_Player`'s `HeadLight` (a spot light
  inner 20 / outer 22 deg, reach 50 000 cm, falloff exponent 4, IES Star_Burst4, at X = the bounds' sphere radius, Z = -the
  box extent: ahead of the nose, at the bottom) and `UpdateHeadLightBounce` (VFX quality 3+: a point light at the line
  trace's hit along the headlight, soft source radius eased 4/s to sqrt((d / cos 22)^2 - d^2), reach 4 x that, position
  eased 4/s, intensity eased 2/s to `CalculateLightIntensityFalloff(reach, 10, exponent 2)`, 4/s to 0 with no hit), and
  `BP_PawnBase`'s `VFX_UpdateHeadlights` (a `BP_PawnHeadlight` child actor per `SKT_HeadLight` socket: a screen-sized lens
  flare P_LensFlare_001 at Intensity = HeadlightsIntensity 1 / the socket count, a spot light that lights nothing; the
  toggle sets the pawn meshes' "Headlight" material scalar). Here: the spot light's cone and reach (x the kit's scale), URP
  intensities as tunables (`Headlights.Intensity` 3000, `BounceGain` 40: set headless, not matched by eye yet), the bounce
  ray against the hull colliders (`HullCollision.HullMask`; the asteroids' cubes have none), the falloff as Unreal's radial
  attenuation base x (1 - (d / reach)^2)^exponent (the LIB blueprint isn't dumped), flares as `GoF2/ModEs2Particle` kind 6
  (a fixed share 0.02 of the screen height) and the "Headlight" materials' emission off through property blocks. Off
  while docked and once dead. Tests `Es2ShipEffectsTests` (cone radius, falloff, InterpTo, the flare share, the defaults),
  `GameControlsTests` (43 rows).
- **ES2 collision damage** (2026-11-02, Options > Gameplay "Collision damage", `Settings.CollisionDamage`, off by default;
  `PlayerCollision.CollisionDamage` / `Ram`): after `BP_CollisionDamageComponent`'s hit graph (on every ES2 ship; the
  player's `CollisionDamageFactor` attribute 1): strength = |NormalImpulse| / mass / DamagePercentageDivisor 50 000 (the
  speed lost into the surface, cm/s, over 50 000); from MinDamagePercent 0.01 (5 m/s) and outside the 0.5 s
  GracePeriodDuration: damage = strength^1.5 x the ship's MaxHitpoints x DamageMultiplier 1 x the factor, dealt as a point hit
  (Kinetic half, Energy third); the camera shake scaled by strength x 10 and the scraping sound by strength / 0.02 aren't
  taken (the remake keeps the original's knock). Here the speed into the surface is the ship's velocity (free flight's own,
  the original's along the nose) against the push-out normal of a landmark's volume or a hull, or toward an asteroid's
  centre (which then costs this instead of the original's 20 and is still destroyed), and the maximum is the hull's and
  the armour's; 3 % of it at 50 m/s, a quarter at 200 m/s, all at 500 m/s. Tests `Es2ShipEffectsTests` (the threshold, the
  exponent, the full hull at strength 1).
- **ES2 boost feel** (2026-11-02; decompiled `UShipMovementComponent::StartBoosting` / `StopBoosting` / `TickComponent` /
  `ChangeBoostEnergy`, `AESPawn::StartBoosting`, `AESPlayerController::InputStartBoosting` / `InputStopBoosting`,
  `UESGameUserSettings::GetBoostAsToggle`, in `es2_re/ghidra/boost.c` / `boost2.c`): a boost on an empty pool is refused
  silently (StartBoosting returns when the energy is at 0, bUseEnergyForBoost and BoostEnergyConsumption > 0); the boost
  that runs dry is stopped by the tick, its energy set to 0, and `TryToBoostWithEnergyDepletedSound` plays (the remake:
  `FlightModel.BoostRanDry` the frame the pool empties, `PlayerEngine` plays a mod ship's "boostDepleted" sound beside
  `boostStop`; the ES2 importer writes ES2's cue as it). The boost-as-toggle setting (per device in ES2: a press while
  boosting stops, else starts): Options > Controls "Boost is a toggle (free flight)", `Settings.BoostToggle`, off by
  default; `ShipController.BoostInput` latches a press only when `FlightModel.CanStartBoost`, and a boost that ran dry
  unlatches. Not taken: `BoostRechargeDelay` (the timer StopBoosting sets before the regeneration resumes: no nonzero
  default found in the blueprint or the constructor dump, the remake refills at once). The "momentum on boost stop" of the
  candidate list is a legendary thruster's buildup condition (`BP_Condition_Buildup_Momentum`), an item perk, not flight.
  Landing gear: ES2's ships have none (no gear assets or animations; the landing dust is `Es2Dock`'s already).
