# Flight model

Notes moved from `CLAUDE.md` (its index lists every notes file). Code comments citing `CLAUDE.md "<section>"` mean the section or bullet of that name here.

## Recovered facts already implemented

- Flight (`FlightModel.cs`, from `PlayerEgo::handleShip / left / right / up / down / roll / update`):
  - base speed is 2 units/ms for every ship
  - handling `H = (handling/100 + 0.2*upgrades) * (1 + agility/100) * 20`
  - turn-rate target = `trunc(input*750*H)/63`
  - ramp = `dt*H/((3.3 - sens)*20)`; decay = `dt*H/126`
  - angle = `dt*rate*2π/65536*0.033`
  - boost speed = `int(2*boost/100)+2` (the integer truncation is intentional)
  - auto-level roll at 0.00025 to 0.00075 rad/ms, then a fine phase (overshoot 0.00035, then 0.0002); the lean is
    -right.y (#57: it was up along the horizontal part of 'right', nothing on a knife edge, so a ship on its side or
    upside down counted as level; checked from 800 random attitudes); the original levels only on request
    (`PlayerEgo::alignToHorizon` sets +0x2f4, only from the stick's double tap; `PlayerEgo::roll` runs from update until
    level and clears it) and only the roll, which steering doesn't stop; the remake by default too, but steering ends it
    (below) and so does a nose within 25 deg of vertical (`LevelSteepNose`: a roll about it can't make the ship upright,
    so it rolled on through climbs, dives and loops), a request (the double tap, the binding) levels only 1 s later and
    steering (a push of 0.05, the touch stick's squared value: a thumb resting on it isn't; nor the double tap's second
    tap in its first 0.3 s) or a roll meanwhile drops it until the next request (`FlightModel.LevelRequestDelayMs`,
    `UpdateLevelRequest`, the user's request 2026-10-27: quick stick inputs in a fight still made double taps; counting
    any value above zero, a thumb left on the stick dropped every request; `LevelOutNow` skips the wait), and the touch
    double tap needs a short first tap (`TouchControls.stickTapped`, released within 0.3 s: the original's second press
    within 499 ms of any stick press made grabbing the stick again mid-fight level the ship out); remake (#37, Options >
    Controls "Level out also levels the nose", `Settings.LevelPitch`, off by default since 2026-10-27 (the user's
    request: on, it pulled the nose back to the horizon as soon as pitch was let go; it was on)): the nose comes to the
    horizon too (world up = the station's, like the autopilot), a steep nose first, the roll re-checked until both are
    level, upside down the roll keeps its first direction; steering ends a level-out (the original keeps rolling under
    the stick), once the stick was neutral for 150 ms after the press and then pushed at least 30 % (the touch double
    tap's second tap is on the stick; a single stale neutral frame between the taps armed it and the tap cancelled it,
    and a thumb resting past the dead zone ended it: `LevelArmMs` / `LevelCancelInput`, Discord report 2026-10-18), or
    300 ms after the press by a 30 % push without letting go (`LevelGraceMs`, 2026-10-27: steered without a neutral
    stick, or after a re-grab of the touch stick within 0.5 s, which is a double tap, it kept fighting the player; free
    flight's aim ends it the same way, `SteeringEndsLevelOut`): with the nose levelling it pulled the nose back to the
    horizon whenever pitch was let go, and a steep nose never let it finish (`FlightModel.levelSteerArmed`)
  - a boost sets the throttle to 100 %; every shot kicks the camera (shake 50, 2)
- Chase camera: touch mode uses fixed damping (0.005 / 0.006). Mouse/controller mode depends on handling (position
  0.01h·0.011 + 0.001, look (1 − 0.01h)·0.015 + 0.003). The original moves the camera and its look point 1 − e^(−x·dt/2)
  of the way each frame (`ChaseCamera.Ease`: its fitted damping polynomial,
  `aproximateCooefficientsForAproximationOfDampingFunktion`, within 2-3 % up to 50 ms frames; `ChaseCameraTests`). Two
  follows, Graphics option "Chase camera" (`Settings.OriginalChaseCamera`): **Remake** (default) = 2026.10.09's,
  `ChaseCamera.Follow` = 1 − e^(−x·dt) (twice the original's rate) and a rotation slerped toward the look point as seen
  from where the camera should be (during a dodge from where it is, so it turns after the sliding ship); **Original** =
  #114's (the 2026.10.17 release): `Ease` and the eased look point aimed at from where the camera is (the crosshair off
  centre and the ship swinging to the side in a turn, as in the original's videos), which players found worse. Boosting
  widens the FOV by 0.35 rad × the boost percentage and rumbles (pct × clamp(speed, 2, 8) / 50). The hit / shot shake
  and the rumble (explosions, the boost, the Liberator, the cutscene camera's) take a new random offset once per
  original frame (`ChaseCamera.HeldJitter`, `OriginalFrame.Ms`, 16.7 ms at the iOS build's 60 Hz) and sit on top of the follow, taken off again the next frame
  (`UndoJitter`); players' reports 2026-10-18 of extreme shaking: drawn anew every rendered frame and into the eased
  pose, a full rumble turned the view 2.8 deg per frame at 144 fps (0.56 now), and the Original follow turned every hit
  and shot into a flicker of the view.
- The model's bank (`PlayerEgo::update` 0xaa5f4, `ShipController.UpdateVisualBank`): roll = −yawRate·2π/4096, tilt =
  pitchRate·0.25·2π/4096, straight from the turn rates, so it builds and settles with them (~0.5 s in, ~1.5 s out).
  #114: the remake eased toward an angle per stick input and snapped. A heavy debug hull eases it; free flight keeps its
  own bank.
- The dodge (`Maneuver`, `PlayerEgo::initManeuver` / `updateManeuver`, `MGame::maneuverTouchEnd`): a touch swipe off the
  controls (≤ 600 ms, > w/480·70 px sideways, < h/320·90 px up or down; A / D, the PC version's Move left / right;
  remake: a sideways flick of the right stick) slides the ship 1200 ms toward it (dt·4·(1 − t/1200)·H·0.05 units), turns
  the nose away (k·0.00628 rad per 30-fps frame) and banks by a yaw rate sin(πt/1200)·15H; no steering meanwhile, the
  autopilot included; each request adds 0.17 to the volatile goods. A player bomb's ignition adds 3 f to them
  (`addNukeVolatileForce`, f by the distance; the damage stays Extreme-only). The chase camera (`TargetFollowCamera`):
  the target offset, the look-at point, shifts x = −3 × the yaw rate, and the camera and that point both take back 0.9 ×
  the slide per frame (`dodgeLag`); the camera looks at the eased point from where it is (`MatrixGetLookAt`), so it
  turns about 40 deg after the sliding ship and keeps it in view (the point is only ~34 m ahead of the camera). Until
  2026-10-11 the remake put the shift on the camera position (scaled to 0.25) and eased a rotation aimed from where the
  camera should be, so the ship slid out of the frame (players compared videos).

- The original's frame (`Core/OriginalFrame.cs`, `OriginalFrame.Ms` = 1000 / 60): the rules the original ran once per frame
  without a dt (the death spin, the dodge's nose turn, flings, missile steering, the crates' drift, the cutscene and
  level-script per-frame factors, the camera's shake offsets) are converted with it. The iOS 1.1.19 build starts its
  display link at frame interval 1 (60 Hz) and feeds MGame::OnUpdate the real elapsed ms (capped 150); the Android
  build's cap was never determined. Until 2026-10-30 the remake used 30 fps (33.3 ms) for these, half the iPhone's rate.

Useful field offsets in the decompiled code:

- **PlayerEgo:** `0xb8` speed, `0xbc` throttle, `0x138` boost timer, `0x154` H, `0x278` pitch rate, `0x27c` yaw rate
- **Ship:** `0x4` armor, `0xc` cargo, `0x14` price, `0x18` handling/100, `0x1c` shield, `0x34` boostSpeed, `0x38`
  boostDelay, `0x3c` boostTime, `0x40` agility, `0x6c` equipment
- **Item:** `0x0` index, `0x4` type, `0x8` category, `0xc` tech level, `0x30` attributes
