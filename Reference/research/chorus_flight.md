# Chorus flight: the decompiled movement

The spec for a "Chorus flight" style next to the EVERSPACE 2 free flight (`es2_flight_measurements.md`). Everything here
comes from the player's own installation of Chorus (Deep Silver FISHLABS, UE 4.26, `Chorus-Win64-Shipping.exe`, no symbols):
- the code: Ghidra, names from a Dumper-7 dump of the same build;
- the values: the player ship blueprints and curve assets read with CUE4Parse.

The workspace (`~/Downloads/Archive/chorus-re`, docs/CHORUS_RE_NOTES.md) holds the addresses and evidence. It stays
outside this repository. Only numbers and formulas are here. Nothing has been measured in the running game yet (no
recordings), so the timings below are computed from the formulas, not observed.

Units: Chorus works in cm and degrees; this file uses **m/s, m/s² and °/s**. The player ship is the Forsaken
(`BP_Player_Ship_Phoenix`, which overrides nothing in movement except the strafe deceleration) on top of
`BP_Player_Ship_Base`. Values the blueprints don't set are the C++ constructor defaults (`USteeringComponent`'s ctor,
read 2026-11-11).

## The model in one paragraph

Chorus is an **arcade "always forward" model**, not a 6-DOF one:
- **Thrust:** only forward / back. A trigger or stick asks for full speed or a stop, and the ship gets there along a
  speed-dependent acceleration curve. Player strafing is off (`MaxLinearStrafingSpeed` 0); sideways movement is the dodge.
- **Turning:** pitch and yaw rates scale with forward speed: slow at rest, full at cruise, sluggish when boosting.
  They ramp up in 0.2 s and **stop dead** on release. Roll has no ramp.
- **Boost:** doubles the top speed. It is held, has no energy cost and no time limit, and turns slower.
- **Drift:** a held button. While held, there is no thrust and the velocity keeps its world direction while the nose
  turns freely, a bit faster than normal. On release, a recovery phase pulls the speed back up while the sideways part
  bleeds off.

## Per frame (`USteeringComponent`)

Velocity `V` is kept **in the ship's frame** (X forward, Y right, Z up) and moved by the ship's rotation each frame.

### 1. Forward speed (`UpdateLinearVelocity`), when not drifting

1. **Top speed:** `max = 140 m/s × LinearSpeedX modifiers` (boost: ×2, see 4).
2. **Target**, as a fraction of `max` (`JetModeLinearSpeedModifier` = [Lower 0, Upper 1]):
   - forward input > 0 → 1;
   - reverse input < 0 → 0 (Chorus has no reverse flight);
   - no input → 0: the ship **coasts to a stop**. Above `max` (after a boost) the target is 1, so it first settles at
     `max` and then keeps coasting down.
   - Drift recovery: target = the recovery speed (5).
   Target speed = fraction × `max`.
3. **State and curve**, evaluated at the current forward speed `v` (all keys input m/s → output m/s²; linear between
   keys, clamped outside):

   | state | when | curve |
   |---|---|---|
   | ACCELERATING | target ≥ v | 0 → 70, 70 → 175, 140 → 175 |
   | BOOSTING | target ≥ v and boost on | −280 → 560, 0 → 240, 1800 → 770 |
   | BRAKING | target < v and the brake held (thrust axis < −0.05) | 0 → 131.25, 70 → 131.25, 280 → 210 |
   | DECELERATING | target < v, brake not held | 0 → 70, 210 → 70, 770 → 210, × a SpeedFactor ramp (1 for the Forsaken) |
   | DRIFT RECOVERING | after a drift | 220 flat |

4. **Step:** `v` moves toward the target by at most `curve(v) × LinearAccelX × |input| × dt` (|input| = 1 with no input).
   This is a **constant-rate approach** (MoveTowards), not an exponential ease. The input's size scales the rate, not
   the target: a half-pressed trigger reaches the same top speed, slower.
5. **Sideways / vertical** (Y, Z, left over from a drift or a hit): their combined magnitude drops by
   `LateralVerticalDeceleration(|V|)` × dt along their own direction. That curve is 0 → 105, 140 → 105, 280 → 175,
   **keyed by the total speed |V|**, not the sideways speed.

Computed from the curves (240 Hz steps):

| | time |
|---|---|
| 0 → 140 m/s | 1.02 s (90 % at 0.93 s) |
| 140 → 0, released (coast) | 2.0 s |
| 140 → 0, brake held | 1.02 s |
| boost 140 → 280 / 0 → 280 | 0.47 s / 1.0 s |
| boost released, 280 → 140 | 1.9 s |
| sideways 140 → 0 | 1.33 s |

### 2. Turning (`UpdateAngularVelocity`, `ApplyAngularRotation`)

- **Top rate**, pitch and yaw: `108 °/s × MaxAngularSpeed modifiers × MaxAngularFromLinearSpeed(v)`. That curve is
  0 → 0.5, 70 → 1.0, 140 → 1.0, 210 → 0.7, 280 → 0.35, 1120 → 0.3. So the rate is:

  | speed | at rest | 35 m/s | 70–140 m/s | 210 m/s | 280 m/s (boost) | 770 m/s (travel boost) |
  |---|---|---|---|---|---|---|
  | rate | 54 °/s | 81 °/s | 108 °/s | 75.6 °/s | 37.8 °/s | 34.6 °/s |

  While drifting the factor is `DriftMaxAngularFromLinearSpeed(|V|)` instead: 0 → 0.5, 70 → 1.2, so 129.6 °/s at
  140 m/s.
- **Ramp:** pitch and yaw move toward `input × top rate` at the angular acceleration (linear). That acceleration comes
  from the player's settings. "Steer acceleration" is on by default with a time of 0.2 s, giving `top rate / 0.2 s`
  (540 °/s², the same as the blueprint's authored value). With the setting off it is 10⁶ °/s², i.e. instant.
  Boost multiplies it by 0.66, travel boost by 0.5.
- **Release or reversal:** `bHasAngularDeceleration` is false, so the rate **snaps to 0** when the input is released
  or reverses. The ship stops turning exactly where the stick lets go.
- **Roll:** the rate is set directly to `roll input × max` (108 °/s), with no ramp.
- **No pitch clamp** for the player (`bEnablePitchClamping` false; ±30° otherwise).
- **Applied** as a local-space quaternion each frame: `ΔR = (AngularVelocity + AngularSpeedRotator modifiers) × dt`.
- **Visual banking:** the mesh rolls on its own; it doesn't change the flight path. The roll is:
  - by sideways speed: 0 → 0°, 280 m/s → 45°;
  - for steering: 37.5° → 70° max, 55° → 75° while drifting;
  - eased at 6 → 2 /s.
- **Camera:** the FOV multiplier goes 0.85 at rest, 0.9 at 140 m/s, 1.0 at 280, 1.15 at 770, 1.25 at 2240 m/s.

### 3. Drift (`TickComponent`, `StartDrift`, `StopDrift`; the player controller's drift button)

- **Pressing drift:** stops any boost and starts the drift, unless a `DisableDrift` modifier is active. While drifting,
  `UpdateLinearVelocity` returns early: **no thrust and no lateral damping**. The velocity is turned by the inverse
  of each frame's rotation (`V' = R_new⁻¹ · R_old · V`), so it keeps its **world** direction while the nose turns.
- **Speed while drifting:** it only drops above `max`, by `DriftDeceleration(|V|)` = 28 m/s² up to 280 m/s, then
  210 m/s² at 420 m/s (plus any DriftDecelAdd modifier). At or below 140 m/s a drift keeps its speed indefinitely.
- **Releasing drift:** recovery starts with recovery speed = the forward speed clamped to `max`. Boosting during
  recovery raises the recovery speed to the target instead. The forward speed then accelerates at 220 m/s² toward the
  recovery speed, while the sideways part decays as in 1.5. Reverse input ends the recovery, and so does reaching the
  speed. The strafe input is folded into V.Y when a drift starts.
- **Boost while drifting:** the boost button ends the drift (StopDrift) and starts a boost; a boost can't start while
  drifting.

### 4. Boost (`UBoostComponent`)

- **Regular boost:** held. Pressing it stops a drift. It runs at least 0.6 s (`MinBoostDuration`), with no energy
  cost and no limit. Each frame while active it sets these modifiers:
  - LinearSpeedX ×2 (× BoostMultiplier and RegularBoostSpeed modifiers, scaled by the trigger's boost power: an
    analog axis through `BoostPowerByAxisInput`, 1 on keys);
  - LinearAccelX ×1;
  - MaxAngularSpeed ×1;
  - AngularAccel ×0.66;
  - a camera offset.

  Acceleration uses the BOOSTING curve (state table above).
- **Travel boost** (out of combat, "afterburner" mode): speed ×5.5 (770 m/s), angular acceleration ×0.5. It breaks
  at ×5.0 and cancels below ×4.4 of normal speed. Max banking ×0.5.
- **Cooldown** (`FBoostCfg.Cooldown`): not set by the blueprint, and the C++ default (UBoostComponent's constructor,
  read 2026-11-11) is 0, so a boost can start again at once. The C++ MinBoostDuration is 2.0 s; the blueprint's 0.6 s
  replaces it.
- **Travel boost activation:** the player controller's TravelBoostConfig is Mode AfterBurner (blueprint) with
  ChargeDuration 0 and bHoldButton false (constructor), so a press toggles it, with no charge.

### 5. Dodge (`UDashComponent`), for later

A sideways dash:
- **Timing:** 0.25 s at speed, ramping in 0.15 s and out over 0.75 s.
- **Speed curves:** acceleration and deceleration ×0.25.
- **Stationary variant:** 40 m/s over 0.35 s.
- **Lateral damping:** ×1.5 for 1 s after.
- **Perfect dodge:** within 0.2 s of a hit, it slows time to 0.2× for 0.9 s and gives 2 energy.
- **Protection:** invulnerable for 0.6 s.
- **Repeats:** re-triggers inside 1.5 s are penalised.

The normal dash's **speed** isn't in the blueprint. The C++ default (FDashCfg's constructor, read 2026-11-11) is
7000 cm/s: 70 m/s sideways, half the top speed. Its CooldownTime is 0.

### 6. Input (`ASpacePlayerController`)

- **Thrust:** one axis. Forward = full-speed target, back = brake. Optional settings:
  - "relative thrust" (off by default) turns the stick into a throttle lever that moves at 1.5 /s with a 0.1 dead zone;
  - a joystick's thrust axis can map directly to a target speed fraction.
- **Steering:** stick or mouse. Defaults: sensitivity 0.5, stick dead zone 0.1, steer acceleration on at 0.2 s.
  `SwapBoostDrift` swaps the two buttons.

## Applied to the remake (2026-11-11)

`FlightModel.StepChorus` (Options > Controls "Flight style": Chorus style; docs/notes/free-flight.md "Chorus flight"),
scaled: every speed is a share of the ship's own top speed (the user's choice), so the timings above hold at the remake's
100 m/s. Choices made where Chorus and the remake differ:
- **Booster:** a booster is needed to boost, as everywhere in the remake. The boost is Chorus's ×2 unless a mod sets a
  `boostFactor`.
- **Turn rate:** 108 °/s, scaled by handling the same way as free flight's 125 °/s.
- **Easing a turn:** when the stick is eased back (a smaller input in the same direction), the rate drops straight to
  the new target. That is inferred from the missing deceleration; the decompile only shows release and reversal.
- **Keys:** drift on Space / LB, and Chorus's dash on the dodge keys (A / D).

`ChorusFlightTests` checks the timings.

## Mapping to the remake (for the flight style)

Open decisions for step 3, not settled here:
- **Scale.** The importer builds Chorus ships at 3.6× their own size so they match GoF2 hulls. Chorus speeds as-is
  would be 140 m/s cruise and 280 m/s boost, against the original's 100 m/s top. Pick one:
  - keep the m/s numbers, so the ship feels as fast as Chorus, but faster than every GoF2 ship;
  - or scale speeds and accelerations by the remake's top speed over 140, keeping every timing in the table.

  The timings are what define the feel.
- **Handling:** scale the turn rate like free flight does (at handling 100 it's Chorus's 108 °/s), and keep the
  speed curve shape.
- **Mapping to `FlightModel`:** `StepFree` already has dampeners off (drift) and a held boost. Chorus flight differs in:
  - a constant-rate approach on curves;
  - no strafe;
  - turn rate by speed;
  - a snap stop on release;
  - drift as a held button with a recovery phase;
  - boost without energy.
