// FlightModel.cs
// Clean re-implementation of the Galaxy on Fire 2 (Android 2.0.16) player flight behaviour,
// written from notes on the decompiled PlayerEgo / Ship / TargetFollowCamera code.
// Pure C# (only uses UnityEngine math types), so it can be unit-tested outside a scene.
//
// Units: the original works in milliseconds and "GoF units". This class keeps those units
// internally so every constant matches the original; the MonoBehaviour converts to Unity.
//
// Remake: a second flight style, free flight (Settings.FlightStyle, StepFree), after EVERSPACE 2: its numbers measured from
// recordings of the game and read from its asset values, its velocity step rebuilt from the game's decompiled
// UShipMovementComponent::ApplyControlInputToVelocity and BP_Ship_Player's tick (Reference/research/
// es2_flight_measurements.md, "The decompiled movement"). Thrust, strafe and up / down held instead of a throttle, the
// turn rates ramped toward the stick or the mouse's virtual joystick, a manual roll and no auto-level, a held boost on an
// energy pool, and inertial dampeners (on: the velocity is pulled toward the thrust's direction, eased out as it nears
// the top speed that way, and braked down to the speed asked for; off: the ship keeps its momentum and the thrust only
// adds to it up to the top speed, steering it beyond). The top speed is the original's 2 units/ms (100 m/s, also
// EVERSPACE 2's); the turn rate scales with the ship's handling, the boost recharge is its booster's.

using System;
using UnityEngine;

namespace GoF2Remake.Flight
{
    /// <summary>Per-ship inputs to the flight model. Values map 1:1 to data/ships.json + items.json.</summary>
    [Serializable]
    public class FlightStats
    {
        [Tooltip("ships.json 'handling' (e.g. Betty = 120). The game divides it by 100.")]
        public float handling = 120f;

        [Tooltip("Number of installed handling upgrades (bar-agent mods). Each adds +0.2 to base handling.")]
        public int handlingUpgrades = 0;

        [Tooltip("Agility bonus from an installed Steering Nozzle (items.json stats.agility, attribute 28). 0 = none.")]
        public float agility = 0f;

        [Tooltip("Booster items.json stats.boostSpeed (attribute 25). 0 = no booster.")]
        public int boostSpeed = 0;

        [Tooltip("Booster items.json stats.boostDurationMs (attribute 27).")]
        public int boostDurationMs = 0;

        [Tooltip("Booster items.json stats.boostRechargeMs (attribute 26). Booster is usable only if > 0.")]
        public int boostRechargeMs = 0;

        [Header("Hardcore mode: cargo weight reduces handling")]
        public bool cargoAffectsHandling = false;
        public float cargoLoad = 0f;
        public float cargoCapacity = 1f;

        /// <summary>Remake, for mods (Modding.ModFlight): what counts in each flight style, the booster and the Steering
        /// Nozzle (an item's flightScope can keep them to one style) and the items' and the ship's per-style bonuses. Null =
        /// the fields above for both styles, no bonuses (Database.BuildFlightStats always fills both).</summary>
        public FlightTuning original, free;

        public FlightTuning ForStyle(bool freeFlight) => (freeFlight ? free : original) ?? new FlightTuning
        {
            agility = agility, boostSpeed = boostSpeed, boostDurationMs = boostDurationMs, boostRechargeMs = boostRechargeMs,
        };
    }

    /// <summary>One flight style's share of the equipment (FlightStats.original / free).</summary>
    [Serializable]
    public class FlightTuning
    {
        public float agility;
        public int boostSpeed, boostDurationMs, boostRechargeMs;
        public FlightBonus bonus = new FlightBonus();
    }

    /// <summary>Remake, for mods: per-style flight bonuses (Modding.ModFlight: items' stats 111-121, ships.json "flight").
    /// Percentages add up (20 = +20 %, -10 = 10 % less; the total never below -90 %); boostFactor is free flight's boost top
    /// speed in hundredths of the top speed (350 = x3.5), 0 = the booster's own.</summary>
    [Serializable]
    public class FlightBonus
    {
        public float topSpeed, turnRate, strafeSpeed, acceleration;
        public int boostFactor;

        public FlightBonus Add(FlightBonus o)
        {
            if (o == null) return this;
            topSpeed += o.topSpeed; turnRate += o.turnRate; strafeSpeed += o.strafeSpeed; acceleration += o.acceleration;
            if (o.boostFactor > 0) boostFactor = o.boostFactor;
            return this;
        }

        public bool IsEmpty => topSpeed == 0f && turnRate == 0f && strafeSpeed == 0f && acceleration == 0f && boostFactor == 0;

        public static float Scale(float percent) => Mathf.Max(0.1f, 1f + percent / 100f);
    }

    public class FlightModel
    {
        // ---- constants recovered from the binary -------------------------------------------
        public const float BaseSpeed = 2.0f;                  // units per ms, identical for every ship
        const float RateToRadiansPerMs = 2f * Mathf.PI / 65536f * 0.033f; // turn-rate units -> rad/ms
        const float TargetRateScale = 750f;                   // target rate = trunc(input*750*H)/63
        const int TargetRateDivisor = 63;
        const float RampBase = 3.3f;                          // ramp step = dt*H / ((3.3 - sens)*20)
        const float PitchUpSensitivityScale = 1.25f;
        const float PitchDownSensitivityScale = 1.45f;
        const float DecayDivisor = 126f;                      // no input: rate -> 0 by dt*H/126
        const float CollisionPushDecay = 0.7f;                // per frame
        const float CollisionPushCutoff = 0.01f;

        // Auto-level ("align to horizon", triggered by a double tap in the original HUD)
        const float LevelDoneThreshold = 0.015f;              // |up.x| below this and up.y > 0 -> done
        const float LevelRateUpsideDown = 0.00075f;           // rad per ms
        const float LevelRateFar = 0.00035f;                  // |up.x| > 0.3
        const float LevelRateNear = 0.00025f;
        const int LevelMaxDtMs = 60;
        const float LevelRateOvershoot = 0.00035f, LevelRateFine = 0.0002f;   // PlayerEgo::roll's fine phase (+0x324)
        int levelSide;        // PlayerEgo+0x2a9: the sign of up.x last frame (1 negative, 2 positive, 0 none)
        bool levelFine;       // PlayerEgo+0x324: the lean changed sides once, the last part goes slow
        bool rollLevelled;    // the roll part of a level-out is done
        bool levelSteerArmed; // the stick was neutral since the level-out began: steering now ends it (remake)
        float levelNeutralMs; // how long the stick has been neutral during a level-out (arms it after LevelArmMs)
        float levelMs;        // how long the level-out has run (past LevelGraceMs steering ends it, armed or not)
        /// <summary>Remake: a level-out is armed by a neutral stick held this long (one stale neutral frame between a touch
        /// double tap's taps armed it, and the second tap on the stick cancelled it at once), and only a real push
        /// (LevelCancelInput of a full stick) ends it (a thumb resting past the dead zone did). Past LevelGraceMs a push
        /// ends it without the neutral stick: steered without letting go, it kept rolling and pulling the nose against the
        /// player (a re-grab of the touch stick within 0.5 s is a double tap too).</summary>
        const float LevelArmMs = 150f, LevelCancelInput = 0.3f, LevelGraceMs = 300f;
        /// <summary>Remake: the nose's world-up share past which a roll-only level-out ends (sin 65 deg).</summary>
        const float LevelSteepNose = 0.906f;
        /// <summary>Remake (the user's request, 2026-10-27): a level-out request (AlignToHorizon) waits this long before the
        /// ship levels, and steering meanwhile drops it until the next request: on the touch stick quick inputs in a fight
        /// made double taps, and the ship began to roll under the player's thumb. Steering = a push of LevelRequestInput
        /// (the touch stick's value is squared: about a fifth of its travel); a thumb resting on the stick (no dead zone)
        /// isn't, nor is the double tap's second tap in its first LevelRequestTapMs (it lands off the centre). Counting any
        /// value above zero, a thumb left on the stick after the double tap dropped every request.</summary>
        public const float LevelRequestDelayMs = 1000f;
        const float LevelRequestTapMs = 300f, LevelRequestInput = 0.05f;
        bool levelRequested;      // a level-out request waiting for its delay
        float levelRequestMs;     // how long it has waited
        /// <summary>A level-out request is waiting for its delay (LevelRequestDelayMs).</summary>
        public bool LevelRequested => levelRequested;
        /// <summary>Remake (#37, Settings.LevelPitch): a level-out also brings the nose to the horizon (world up, the
        /// orbit's and the station's up, like the autopilot's moveToPosition), not only the roll (the original).</summary>
        public bool LevelPitch;
        const float LevelPitchRate = 0.0006f;       // rad per ms (~34 deg/s), slower over the last 10 deg
        const float LevelPitchDone = 0.01f;         // rad

        // ---- configuration ------------------------------------------------------------------
        /// <summary>Options-menu steering sensitivity. Must stay well below 3.3/1.45 (~2.27). Default is a guess.</summary>
        public float Sensitivity = 1.0f;
        /// <summary>Tilt steering (options[0x11] = 0): PlayerEgo::down / up ramp the pitch with 1.45 / 1.25 x the tilt
        /// sensitivity; the touch stick, keys and pads use the sensitivity as it is.</summary>
        public bool TiltMode;

        /// <summary>
        /// True: turn rates and the cosmetic model tilt follow partial stick input (needed for analog sticks and
        /// touch). False: the original's rules (rates only grow toward the target while a direction is held; the tilt
        /// uses the input's sign). Identical for full-deflection (keyboard) input.
        /// </summary>
        public bool TrackAnalogInput = true;

        /// <summary>Reproduce the original's integer truncation of boost speed (see Configure).</summary>
        public bool TruncateBoostSpeed = true;

        // ---- remake debug: the weight of the hulls the player can't normally fly (PlayerHull.MassOf) ----------------------
        /// <summary>0 = an ordinary ship, flown exactly like the original; up to 1 = the largest hulls (the carrier, the Vossk
        /// battleship, the battlestation, the Void ship), set from the hull's size. It lowers the top turn rate, makes the
        /// turn rates slow to build up and to die away, eases the speed toward the throttle instead of jumping to it, and
        /// slows the level-out and the strafe: a capital ship that answers like one.</summary>
        public float Mass;
        /// <summary>The top turn rate's share.</summary>
        public float TurnScale => Mathf.Lerp(1f, 0.15f, Mass);
        /// <summary>The turn rates' ramp and decay share (their inertia): below TurnScale, so a heavy hull also takes longer to
        /// reach its (lower) top rate and to stop turning (~2 s at the carrier's 0.91, against 0.55 s for any ordinary ship).</summary>
        public float Inertia => TurnScale * Mathf.Lerp(1f, 0.18f, Mass);
        /// <summary>The auto-level's and the manual roll's share.</summary>
        public float RollScale => Mathf.Lerp(1f, 0.25f, Mass);
        /// <summary>The strafe's and the dodge's share.</summary>
        public float SideScale => Mathf.Lerp(1f, 0.2f, Mass);
        /// <summary>The time the engines take between a standstill and the base speed (ms); 0 = at once (the original). A boost
        /// surges 2.5x as fast (it would take far longer than the boost lasts otherwise).</summary>
        public float AccelMs => Mass * 7000f;
        /// <summary>The speed the ship actually flies at (units/ms): Throttle x CurrentSpeed at once for an ordinary ship,
        /// eased toward it for a heavy one (AccelMs).</summary>
        public float MoveSpeed { get; private set; } = BaseSpeed;

        float StepMoveSpeed(float dtMs)
        {
            float target = Braking ? 0f : Throttle * CurrentSpeed;
            if (AccelMs <= 1f) return MoveSpeed = target;
            float rate = TopSpeed / AccelMs;   // units/ms per ms
            if (target > TopSpeed || MoveSpeed > TopSpeed) rate *= 2.5f;   // the boost's surge and its run-down
            return MoveSpeed = Mathf.MoveTowards(MoveSpeed, target, rate * dtMs);
        }

        /// <summary>What one flight style flies with (Configure fills both; FreeFlight picks): the handling with the
        /// agility counting in it, the booster, and the mods' bonuses as factors (FlightBonus).</summary>
        sealed class StyleState
        {
            public float handling, boostSpeedValue = 5f;
            public int boostDurationMs = 5000, boostRechargeMs = 20000;
            public bool hasBooster;
            public float speedScale = 1f, turnScale = 1f, strafeScale = 1f, accelScale = 1f;
            public int boostFactor;   // free flight's boost x100 (0 = the booster's)
        }
        readonly StyleState styleOriginal = new StyleState(), styleFree = new StyleState();
        StyleState Style => FreeFlight ? styleFree : styleOriginal;

        public float Handling => Style.handling;               // "H" in the notes (0x154)
        float boostSpeedValue => Style.boostSpeedValue;
        int boostDurationMs => Style.boostDurationMs;
        int boostRechargeMs => Style.boostRechargeMs;
        bool hasBooster => Style.hasBooster;
        /// <summary>The top speed (units/ms): the base 2 for every ship, x a mod's top speed bonus for this style.</summary>
        public float TopSpeed => BaseSpeed * Style.speedScale;
        FlightStats stats;

        // ---- state ----------------------------------------------------------------------------
        public float Throttle { get; private set; } = 1f;      // 0xbc, 0..1
        public float CurrentSpeed { get; private set; } = BaseSpeed; // 0xb8, units per ms
        public float YawRate { get; private set; }             // 0x27c
        public float PitchRate { get; private set; }           // 0x278
        public bool IsBoosting { get; private set; }
        public bool IsLeveling { get; private set; }
        int boostTimerMs;                                       // 0x138: counts up; negative while recharging
        float collisionPush;                                    // 0x37c

        /// <summary>Visual banking amount: +/-H while steering, 0 otherwise (0x25c / 0x258).</summary>
        public float VisualYawBank { get; private set; }
        public float VisualPitchBank { get; private set; }

        // ---- remake: free flight (Settings.FlightStyle; see the header) ----------------------------------------------
        /// <summary>The player's free-flight input for one frame (ShipController).</summary>
        public struct FreeInput
        {
            /// <summary>-1 back .. 1 forward; -1 left .. 1 right; -1 down .. 1 up; -1 roll left .. 1 roll right.</summary>
            public float thrust, strafe, hover, roll;
            /// <summary>Pitch / yaw in Step's convention: x = yaw (-1 right .. +1 left), y = pitch (+1 down .. -1 up).</summary>
            public Vector2 aim;
            public bool dampeners;
        }

        /// <summary>Free flight is the style in use (ShipController, every frame): the boost runs on its energy pool.</summary>
        public bool FreeFlight;
        /// <summary>Free flight: the boost key is held (ShipController).</summary>
        public bool BoostHeld;
        /// <summary>Free flight: the ship's velocity, world space, units per ms. Kept in step with the original's forward
        /// flight while another mode moves the ship (the autopilot, the launch camera), so switching is seamless.</summary>
        public Vector3 FreeVelocity;
        /// <summary>Free flight: what this frame's keys ask for in ship space (x strafe, y up / down, z thrust; -1..1), for
        /// the effects that show it (Modding.LateralThrusters). Set by StepFree.</summary>
        public Vector3 ThrustInput;
        /// <summary>Free flight: no forward thrust held and no boost, so the main engines are off (EVERSPACE 2: a ship at rest or
        /// coasting burns nothing; strafing and reversing fire only its manoeuvring jets). The engine effects read it
        /// (EngineDrive.Off: flames, nozzle glow, throttle glow; ShipExhaust). Always false in the original flight.</summary>
        public bool EnginesOff { get; private set; }

        // EVERSPACE 2's player ship (es2_flight_measurements.md): top speed 100 m/s forward (the cockpit gauge read 343 km/h =
        // 95.3), backward, strafe and up / down half of it (172 km/h; the game sets them from the forward one,
        // SetStrafeHoverBackwardFromForwardSpeed, so they grow with it while boosting); its velocity step is
        // UShipMovementComponent::ApplyControlInputToVelocity (decompiled, see StepFree): an acceleration along the thrust's
        // direction of A (1 - p^3) (p the share of the top speed reached that way, EaseAccelerationExponent 3), forward
        // A = 132 m/s^2 (fitted to the gauge and the distance readout, rms 1 m/s), the others 90 m/s^2 over their half top
        // speed; braking at a quarter of the forward acceleration (BP_Ship_Player's DefaultDeceleration = AccelerationForward
        // x 0.25: the measured 33 m/s^2); a boost to x3.2 the top speed (the gauge: 1110 km/h) at x1.63 the acceleration
        // (fitted: 215 m/s^2), lasting as long as the equipment allows (measured: 4.5 s held; the blueprint's 3.5 s is
        // overridden); every axis turns at ~125 deg/s, reached in ~0.4 s (the cockpit view), at any speed; ~0.85 of it
        // boosting (measured).
        const float FreeReverseShare = 0.5f;     // backward top speed, of the forward one (measured)
        const float FreeStrafeShare = 0.5f;      // sideways (measured)
        const float FreeHoverShare = 0.5f;       // up / down (measured)
        const float FreeAccelFwd = 132f / 95.3f / 1000f;   // forward acceleration, per ms x the forward top speed (handling 100,
                                                           // sqrt-scaled by the handling; 132 m/s^2 for 95.3 m/s)
        const float FreeAccelSide = 90f / 47.65f / 1000f;  // backward, strafe, up / down: the same over their own top speed
        const float FreeEaseFwdMs = 1750f;       // the cubic ease the acceleration was measured as (the gauge's fit): a heavy
        const float FreeEaseSideMs = 1270f;      // hull's AccelMs lengthens it, so the acceleration shrinks by their ratio
        const float FreeEaseExponent = 3f;       // BP_Ship_Player's EaseAccelerationExponent
        const float FreeBrakeShare = 0.25f;      // DefaultDeceleration = AccelerationForward x 0.25 (BP_Ship_Player's tick)
        const float FreeBrakeAboveTopExp = 1.67f;   // DecelerationMaxSpeedExponent: above the top speed the braking grows
                                                    // with (speed / top)^1.67 (a boost's speed runs down fast)
        const float FreeInheritInertia = 0.9f;   // InputInheritInertia: thrusting the way the ship moves brakes up to 90 %
                                                 // less (x 1 - 0.9 cos^2, the thrust's direction against the velocity's)
        const float FreeTurningBoostCm = 6000f;  // DefaultTurningBoost = AccelerationForward (cm/s^2) / 6000, per second
        const float FreeBoostAccel = 1.63f;      // the boost's forward acceleration (BoostFactorAccelerationForward, fitted)
        const float UnitsToCm = 5f;              // 0.05 m per game unit
        const float FreeTurnRate = 125f * Mathf.Deg2Rad / 1000f;   // rad per ms at full input, handling 20 (H of handling 100)
        const float FreeTurnRampMs = 400f;       // standstill to the top turn rate (and back), linear (measured in the cockpit
                                                 // view, which follows the ship rigidly: 10-85 % in 0.31 s)
        const float FreeBoostScale = 3.2f / 3f;  // EVERSPACE 2's boost (the gauge: 1110 of 343 km/h, x3.2) given to the Me'al,
                                                 // whose original boost is x3; every booster keeps its original ratio to it
        const float FreeBoostFallback = 3.2f;    // a booster without a boost speed (a mod's)
        const float FreeTurnExponent = 0.75f;    // the turn rate grows with H^0.75: between EVERSPACE 2's single ship and the
                                                 // original's linear spread (Rhino 51, handling 100 125 deg/s)
        const float FreeTurnAboveShare = 0.1f;   // past EVERSPACE 2's 125 deg/s only a tenth of the rest counts (an Inflict 127
                                                 // instead of 148, a tuned Dace 146 instead of 335: players' feedback, the
                                                 // nimble ships whipped round too sharply)
        const float FreeBoostDurationMs = 3500f; // a full energy pool without a booster duration (the blueprint's value); with
                                                 // one, the booster's duration (attribute 27), refilled in its recharge time
        const float FreeBoostTurnShare = 0.85f;  // boosting turns slower (measured 0.82-0.9: the boost's wider view makes it read low)
        const float BoostRestartEnergy = 0.2f;   // an emptied pool boosts again from a fifth full
        float boostEnergy = 1f, freeBoostVisual, freeYaw, freePitch, freeRoll;   // the free rates in rad/ms
        bool boostSpent;   // the pool ran empty while the key was held

        /// <summary>Free flight: the boost's top speed, of the forward one, by the booster: its original boost speed
        /// (PlayerEgo's int(2 b / 100) + 2 u/ms, over the base 2) x 3.2 / 3, so the Me'al boosts like EVERSPACE 2's
        /// measured x3.2: Linear / Cyclotron x1.6, Synchrotron x2.67, Polytron x4.27.</summary>
        float FreeBoostFactor => Style.boostFactor > 0 ? Style.boostFactor / 100f   // a mod's (ModFlight)
            : boostSpeedValue > BaseSpeed ? boostSpeedValue / BaseSpeed * FreeBoostScale : FreeBoostFallback;

        /// <summary>Free flight: the top turn rate (rad / ms) for this ship: EVERSPACE 2's 125 deg/s at handling 100,
        /// scaled by (H / 20)^0.75, softly capped there (a tenth of what lies above, FreeTurnAboveShare), then the
        /// steering sensitivity option x0.6..1.6 (1 at its default). The option itself, not Sensitivity: that one is the
        /// original's ramp setting, a fixed 2.7 under mouse steering.</summary>
        float FreeTopTurnRate(float he)
        {
            float f = Mathf.Pow(Mathf.Max(1f, he) / 20f, FreeTurnExponent);
            if (f > 1f) f = 1f + (f - 1f) * FreeTurnAboveShare;
            return FreeTurnRate * f * Style.turnScale * TurnScale * Mathf.Clamp(0.5f + 0.5f * Data.Settings.Sensitivity, 0.6f, 1.6f);
        }

        /// <summary>Free flight: how many times faster than the original this ship turns at full input (its top turn rate over
        /// the original's full-stick 750 H / 63 units); 1 in the original flight. The chase camera follows that much faster
        /// (ChaseCamera): tuned for the original's rates it fell behind and the ship swung out of the picture.</summary>
        public float FreeTurnSpeedup
        {
            get
            {
                if (!FreeFlight) return 1f;
                float he = EffectiveHandling, original = 750f * Mathf.Max(1f, he) / 63f * RateToRadiansPerMs;
                return Mathf.Max(1f, FreeTopTurnRate(he) / original);
            }
        }

        /// <summary>Free flight: one frame. The rotation to apply (degrees, local, as Step), the world move in units
        /// (FrameResult.move) and the collision push.</summary>
        public FrameResult StepFree(FreeInput input, float dtMs, Quaternion rotation, Vector3 shipUp, Vector3 shipRight)
        {
            float he = EffectiveHandling;
            UpdateBoost(dtMs);

            // ---- turning: the rates ramp toward the aim (the stick's or the virtual joystick's deflection x the top rate)
            float maxRate = FreeTopTurnRate(he) * (IsBoosting ? FreeBoostTurnShare : 1f);
            float step = maxRate / (FreeTurnRampMs / Mathf.Max(0.05f, Inertia)) * dtMs;
            freeYaw = Mathf.MoveTowards(freeYaw, Mathf.Clamp(input.aim.x, -1f, 1f) * maxRate, step);
            freePitch = Mathf.MoveTowards(freePitch, Mathf.Clamp(input.aim.y, -1f, 1f) * maxRate, step);
            float roll = Mathf.Clamp(input.roll, -1f, 1f);
            if (Mathf.Abs(roll) > 0.01f) StopLeveling();
            UpdateLevelRequest(input.aim, dtMs);
            SteeringEndsLevelOut(input.aim, dtMs);
            float rollTop = FreeTopTurnRate(he) * RollScale;   // EVERSPACE 2 rolls as fast as it turns
            freeRoll = Mathf.MoveTowards(freeRoll, roll * rollTop, rollTop / (FreeTurnRampMs / Mathf.Max(0.05f, Inertia)) * dtMs);
            float rollRad = -freeRoll * dtMs;
            rollLevelled = false;
            if (IsLeveling) { rollRad += AutoLevelRoll(dtMs, shipUp, shipRight) * RollScale; if (rollLevelled) IsLeveling = false; }
            // The original's rate units for whatever reads them (a full-stick rate = 750 H / 63).
            YawRate = freeYaw / RateToRadiansPerMs;
            PitchRate = freePitch / RateToRadiansPerMs;

            // ---- moving: UShipMovementComponent::ApplyControlInputToVelocity (decompiled), in the ship's frame (x strafe,
            // y up / down, z thrust; units / ms)
            float top = TopSpeed;
            float fwdTop = IsBoosting ? top * FreeBoostFactor : top;   // the boost's speed modifier on MaxSpeedForward
            float otherTop = fwdTop * Style.strafeScale;               // strafe and up / down follow the forward top speed
            float handlingScale = Mathf.Sqrt(20f / Mathf.Max(5f, he)) / Style.accelScale;   // a mod's acceleration bonus
            float fwdShare = FreeEaseFwdMs / (FreeEaseFwdMs * handlingScale + AccelMs);     // a heavy hull accelerates slower
            float sideShare = FreeEaseSideMs / (FreeEaseSideMs * handlingScale + AccelMs);
            float accelFwd = FreeAccelFwd * top * fwdShare;   // unboosted: the braking and turning boost derive from it
            float thrust = Mathf.Clamp(input.thrust, -1f, 1f), strafe = Mathf.Clamp(input.strafe, -1f, 1f), hover = Mathf.Clamp(input.hover, -1f, 1f);
            ThrustInput = new Vector3(strafe, hover, thrust);
            if (IsBoosting) thrust = 1f;   // the boost drives forward whatever is held
            var fwd = rotation * Vector3.forward;
            var v = Quaternion.Inverse(rotation) * FreeVelocity;
            var inp = new Vector3(strafe, hover, thrust);
            if (inp.sqrMagnitude > 1f) inp.Normalize();   // the input vector is clamped to length 1
            float inpMag = inp.magnitude;
            var tops = new Vector3(otherTop * FreeStrafeShare, otherTop * FreeHoverShare, thrust < 0f ? fwdTop * FreeReverseShare : fwdTop);
            var target = Vector3.Scale(inp, tops);   // GetTargetVelocityFromInput
            float targetSpeed = target.magnitude;
            var targetDir = targetSpeed > 1e-9f ? target / targetSpeed : Vector3.zero;
            var inpDir = inpMag > 1e-6f ? inp / inpMag : Vector3.zero;
            bool damp = input.dampeners;
            // Turning boost (dampeners on; TurningBoost = DefaultTurningBoost, else 0): the velocity is turned toward the
            // thrust's direction at its own speed, by ((1 + cos) / 2)^TurningBoostExp (1) x dt x TurningBoost x |input|.
            float speed = v.magnitude;
            if (damp && inpMag > 0f && speed > 0f && targetSpeed > 1e-4f * top)
            {
                float turningBoost = accelFwd * UnitsToCm * 1e6f / FreeTurningBoostCm / 1000f;   // per ms
                float pull = Mathf.Clamp01((Vector3.Dot(v / speed, targetDir) + 1f) * 0.5f * dtMs * turningBoost * inpMag);
                v += (targetDir * speed - v) * pull;
            }
            // Braking (Deceleration: 0 with the dampeners off): while faster than asked, the speed drops linearly (faster above
            // the top speed) but not below what is asked; thrusting the way the ship moves brakes less (InputInheritInertia).
            bool exceeding = v.sqrMagnitude > targetSpeed * targetSpeed * 1.01f;   // UMovementComponent::IsExceedingMaxSpeed
            if (damp && exceeding)
            {
                speed = v.magnitude;
                float decel = accelFwd * FreeBrakeShare;
                if (inpMag > 0f) { float c = Mathf.Clamp01(Vector3.Dot(inpDir, v / speed)); decel *= 1f - FreeInheritInertia * c * c; }
                if (speed > fwdTop) decel *= Mathf.Pow(speed / fwdTop, FreeBrakeAboveTopExp);
                v *= Mathf.Max(targetSpeed, speed - decel * dtMs) / speed;
                exceeding = v.sqrMagnitude > targetSpeed * targetSpeed * 1.01f;
            }
            // Thrust: along the asked direction, each axis's acceleration weighted by it; with the dampeners on eased out by
            // 1 - p^3 as the speed that way nears the top speed that way. The speed then stays within what is asked, or
            // within what it was when already faster (the thrust only steers it there).
            if (inpMag > 0f && targetSpeed > 0f)
            {
                // (the accelerations are attributes of their own: the boost raises only the forward one)
                float sideTop = top * Style.strafeScale;
                var accels = new Vector3(FreeAccelSide * sideTop * FreeStrafeShare * sideShare, FreeAccelSide * sideTop * FreeHoverShare * sideShare,
                                         thrust < 0f ? FreeAccelSide * top * FreeReverseShare * sideShare
                                                     : accelFwd * (IsBoosting ? FreeBoostAccel : 1f));
                float accel = inpMag * Vector3.Scale(accels, targetDir).magnitude;
                float ease = 1f;
                if (damp)
                {
                    float topThatWay = Vector3.Scale(inpDir, tops).magnitude;
                    float p = Mathf.Clamp01(Vector3.Dot(v, targetDir) / Mathf.Max(1e-6f, topThatWay));
                    ease = 1f - Mathf.Pow(p, FreeEaseExponent);
                }
                float cap = exceeding ? v.magnitude : targetSpeed;
                v += targetDir * (accel * ease * dtMs);
                if (v.sqrMagnitude > cap * cap) v = v.normalized * cap;
            }
            FreeVelocity = rotation * v;

            // What the rest of the game reads: the forward thrust held as the throttle (the gauge, the engine effects; 0 with
            // no key held, whatever the ship's momentum), the forward speed as MoveSpeed.
            float forwardSpeed = Vector3.Dot(FreeVelocity, fwd);
            MoveSpeed = Mathf.Max(0f, forwardSpeed);
            Throttle = Mathf.Clamp01(thrust);
            EnginesOff = thrust <= 0f && !IsBoosting;
            Braking = false;

            // Cosmetic: the model banks into turns and strafes, tilts with the nose and up / down.
            VisualYawBank = Mathf.Clamp(input.aim.x * 0.8f - strafe * 0.6f, -1f, 1f) * he;
            VisualPitchBank = Mathf.Clamp(input.aim.y * 0.8f - hover * 0.4f, -1f, 1f) * he;

            float pushUnits = 0f;
            if (Mathf.Abs(collisionPush) > CollisionPushCutoff) { pushUnits = collisionPush * dtMs; collisionPush *= CollisionPushDecay; }
            else collisionPush = 0f;

            return new FrameResult
            {
                pitchDeg = freePitch * dtMs * Mathf.Rad2Deg,
                yawDeg = freeYaw * dtMs * Mathf.Rad2Deg,
                rollDeg = rollRad * Mathf.Rad2Deg,
                move = FreeVelocity * dtMs,
                forwardUnits = forwardSpeed * dtMs,
                sidePushUnits = pushUnits,
            };
        }

        /// <summary>Free flight: another mode moved the ship this frame (forward at 'unitsPerMs'): the free state follows, so
        /// free flight picks up from there.</summary>
        public void SyncFree(Vector3 forward, float unitsPerMs)
        {
            EnginesOff = false;   // another mode flies the ship: its engines run
            FreeVelocity = forward * unitsPerMs;
            freeYaw = freePitch = freeRoll = 0f;
        }

        /// <summary>The held boost on its energy pool: drains in the booster's duration (FreeBoostDurationMs without one),
        /// refills in its recharge time; an emptied pool boosts again from BoostRestartEnergy on a fresh press.</summary>
        void UpdateFreeBoost(float dtMs)
        {
            if (!hasBooster) { IsBoosting = false; CurrentSpeed = TopSpeed; return; }
            if (Data.Cheats.NoBoostCooldown) boostEnergy = 1f;
            // An emptied pool doesn't restart while the key is still held (EVERSPACE 2 answers a boost on an empty pool
            // with a refusal sound): release and press again.
            if (!BoostHeld) boostSpent = false;
            bool can = IsBoosting ? boostEnergy > 0f : boostEnergy >= BoostRestartEnergy && !boostSpent;
            if (BoostHeld && can)
            {
                IsBoosting = true;
                boostEnergy = Mathf.Max(0f, boostEnergy - dtMs / (boostDurationMs > 0 ? boostDurationMs : FreeBoostDurationMs));
                if (boostEnergy <= 0f) { IsBoosting = false; boostSpent = true; }
            }
            else
            {
                IsBoosting = false;
                boostEnergy = Mathf.Min(1f, boostEnergy + (boostRechargeMs > 0 ? dtMs / boostRechargeMs : 1f));
            }
            CurrentSpeed = IsBoosting ? TopSpeed * FreeBoostFactor : TopSpeed;
            freeBoostVisual = Mathf.MoveTowards(freeBoostVisual, IsBoosting ? 1f : 0f, dtMs / 200f);
        }

        public void Configure(FlightStats s)
        {
            stats = s;
            FillStyle(styleOriginal, s, s.ForStyle(false));
            FillStyle(styleFree, s, s.ForStyle(true));
            CurrentSpeed = TopSpeed;
            MoveSpeed = TopSpeed;
            Throttle = 1f;
            EnginesOff = false;
            boostTimerMs = 0;
            IsBoosting = false;
            boostEnergy = 1f;
            boostSpent = false;
        }

        void FillStyle(StyleState st, FlightStats s, FlightTuning t)
        {
            // Ship::getHandling() = handling/100 + 0.2 per handling upgrade
            float baseH = s.handling / 100f + 0.2f * s.handlingUpgrades;
            // PlayerEgo ctor: H = (h + h*agility/100) * 20
            st.handling = (baseH + baseH * (t.agility / 100f)) * 20f;

            // PlayerEgo ctor: boost speed = int(2*boostSpeed/100) + 2. The original stores this as an int,
            // so e.g. Linear Boost (60) gives 3.0 (1.5x) although the shop UI implies 1.6x, and
            // Cyclotron (80) ends up equal to Linear. Set TruncateBoostSpeed = false for the "intended" values.
            float boost = 2f * t.boostSpeed / 100f + 2f;
            st.boostSpeedValue = TruncateBoostSpeed ? (int)(2f * t.boostSpeed / 100f) + 2 : boost;
            st.boostDurationMs = t.boostDurationMs;
            st.boostRechargeMs = t.boostRechargeMs;
            st.hasBooster = t.boostRechargeMs > 0;

            // Remake, for mods (ModFlight): the style's bonuses.
            var b = t.bonus ?? new FlightBonus();
            st.speedScale = FlightBonus.Scale(b.topSpeed);
            st.turnScale = FlightBonus.Scale(b.turnRate);
            st.strafeScale = FlightBonus.Scale(b.strafeSpeed);
            st.accelScale = FlightBonus.Scale(b.acceleration);
            st.boostFactor = Mathf.Max(0, b.boostFactor);
        }

        /// <summary>Handling after the hardcore-mode cargo penalty: H*(0.6 + 0.4*(1 - load/max)).</summary>
        public float EffectiveHandling
        {
            get
            {
                if (stats == null || !stats.cargoAffectsHandling || stats.cargoCapacity <= 0f) return Handling;
                float free = 1f - Mathf.Clamp01(stats.cargoLoad / stats.cargoCapacity);
                return Handling * 0.6f + Handling * free * 0.4f;
            }
        }

        /// <summary>Maximum turn rate in degrees per second, handy for tuning/UI.</summary>
        public float MaxTurnRateDegPerSec =>
            (int)(TargetRateScale * EffectiveHandling) / TargetRateDivisor * RateToRadiansPerMs * 1000f * Mathf.Rad2Deg;

        public void ChangeThrottle(float delta) => Throttle = Mathf.Clamp01(Throttle + delta);
        /// <summary>The PC version's "Brake" (binding 3361, apart from "Throttle down" 3360): the engines stop while it is
        /// held; the throttle is kept, so releasing it flies on at once. The phone original has no brake.</summary>
        public bool Braking;

        // ---- strafe (PlayerEgo::strafe 0xad838, the PC port's handler: no caller in the phone build; handleShip moves
        // the ship by it, PlayerEgo+0x37c / +0x380) -------------------------------------------------------------------
        const float StrafeFrameMs = 1000f / 30f;
        float strafeRamp = 0.1f;
        /// <summary>Sideways speed in units/ms, + = right.</summary>
        public float StrafeVelocity { get; private set; }

        /// <summary>Held strafe key, every frame: v = ramp * dir * min(H' * 30 * 0.002, 2) (H' = the cargo-reduced handling
        /// on Extreme), the ramp from 0.1 x1.5 a (30 fps) frame up to 1: full sideways speed after ~6 frames.</summary>
        public void Strafe(int dir, float dtMs)
        {
            StrafeVelocity = strafeRamp * dir * Mathf.Min(EffectiveHandling * 30f * 0.002f, 2f) * SideScale * Style.strafeScale;
            strafeRamp = Mathf.Min(strafeRamp * Mathf.Pow(1.5f, dtMs / StrafeFrameMs * Inertia), 1f);
        }

        /// <summary>handleShip: while |v| &gt; 0.01 the ship moves v * dt sideways and v drops x0.7 a (30 fps) frame;
        /// below it the ramp resets to 0.1. Returns this frame's sideways move in units.</summary>
        public float StepStrafe(float dtMs)
        {
            if (Mathf.Abs(StrafeVelocity) <= 0.01f) { StrafeVelocity = 0f; strafeRamp = 0.1f; return 0f; }
            float d = StrafeVelocity * dtMs;
            StrafeVelocity *= Mathf.Pow(0.7f, dtMs / StrafeFrameMs);
            return d;
        }
        public void SetThrottle(float value) => Throttle = Mathf.Clamp01(value);

        public bool HasBooster => hasBooster;
        public bool BoostReady => FreeFlight ? !IsBoosting && hasBooster && boostEnergy >= BoostRestartEnergy
                                             : !IsBoosting && hasBooster && boostTimerMs >= 0;

        /// <summary>0 at recharge start, 1 when ready (free flight: the energy pool).</summary>
        public float BoostRechargePercent => FreeFlight ? boostEnergy :
            boostTimerMs >= 0 || boostRechargeMs <= 0 ? 1f : 1f + (float)boostTimerMs / boostRechargeMs;

        /// <summary>What is left of a running boost, 1 at its start, 0 when it ends (or none runs).</summary>
        public float BoostLeftPercent => IsBoosting && boostDurationMs > 0 ? Mathf.Clamp01(1f - (float)boostTimerMs / boostDurationMs) : 0f;

        public void Boost()
        {
            if (FreeFlight || !BoostReady) return;   // free flight: the boost is held (BoostHeld)
            Throttle = 1f;   // MGame::OnTouchEnd HUD element 2: full throttle first, then the boost
            boostTimerMs = 0;
            CurrentSpeed = boostSpeedValue * Style.speedScale;
            IsBoosting = true;
        }

        /// <summary>
        /// Boost strength for visuals (engine flame, FOV, camera): ramps 0->1 over the first sixth of the
        /// boost, holds at 1, ramps back to 0 over the last sixth. Original flame scale = 1 + 0.5*this.
        /// </summary>
        public float BoostVisualPercent => Mathf.Max(CruiseVisual, OwnBoostVisual);

        float OwnBoostVisual
        {
            get
            {
                if (FreeFlight) return freeBoostVisual;
                if (!IsBoosting || boostDurationMs <= 0) return 0f;
                float p = boostTimerMs / (boostDurationMs / 6f);
                if (p < 1f) return p;
                if (p > 5f) return Mathf.Max(0f, 6f - p);
                return 1f;
            }
        }

        /// <summary>Remake, multiplayer: the cruise mode (Navigation.Cruise) looks and sounds like a boost, 0..1, eased in
        /// and out over CruiseEaseMs. Only the look: the boost's own rules (missiles, recharge, volatile goods) read IsBoosting.</summary>
        public float CruiseVisual { get; private set; }
        public const float CruiseEaseMs = 400f;
        /// <summary>Boosting or cruising: the boost's look and sound (camera rumble, the boost sound's start and stop).</summary>
        public bool LooksBoosting => IsBoosting || CruiseVisual > 0f;

        public void UpdateCruise(bool cruising, float dtMs)
        {
            CruiseVisual = Mathf.MoveTowards(CruiseVisual, cruising ? 1f : 0f, Mathf.Max(0f, dtMs) / (cruising || cruiseCarryMs <= 0f ? CruiseEaseMs : cruiseCarryMs));
            if (cruising || CruiseVisual <= 0f) cruiseCarryMs = 0f;
        }

        /// <summary>Remake: the ship comes out of a load at speed (SpaceLevel): the look starts full and eases out over 'ms'.</summary>
        public void CarryCruiseLook(float ms)
        {
            CruiseVisual = 1f;
            cruiseCarryMs = ms;
        }
        float cruiseCarryMs;

        /// <summary>The level-out request (PlayerEgo::alignToHorizon: the stick's double tap, the Level out binding). Remake:
        /// it levels LevelRequestDelayMs later unless the player steers meanwhile (UpdateLevelRequest).</summary>
        public void AlignToHorizon()
        {
            IsLeveling = false;
            levelRequested = true;
            levelRequestMs = 0f;
        }

        /// <summary>Starts levelling at once, without the request's delay (tests, scripts).</summary>
        public void LevelOutNow()
        {
            levelRequested = false;
            IsLeveling = true; levelSide = 0; levelFine = false; rollLevelled = false; levelSteerArmed = false; levelNeutralMs = 0f; levelMs = 0f;
        }

        /// <summary>A manual roll (remake) cancels the auto-level, and a request waiting for its delay.</summary>
        public void StopLeveling() { IsLeveling = false; levelRequested = false; }

        /// <summary>Remake: a waiting level-out request; steering drops it (not in the tap's first LevelRequestTapMs), else it
        /// starts the level-out once LevelRequestDelayMs have passed.</summary>
        void UpdateLevelRequest(Vector2 steer, float dtMs)
        {
            if (!levelRequested) return;
            levelRequestMs += dtMs;
            if (levelRequestMs > LevelRequestTapMs && Mathf.Max(Mathf.Abs(steer.x), Mathf.Abs(steer.y)) >= LevelRequestInput)
            {
                levelRequested = false;
                return;
            }
            if (levelRequestMs >= LevelRequestDelayMs) LevelOutNow();
        }

        /// <summary>Remake: steering ends a level-out once it is armed (a neutral stick after the press) or has run past the
        /// grace time; both styles (free flight's aim never ended it).</summary>
        void SteeringEndsLevelOut(Vector2 steer, float dtMs)
        {
            if (!IsLeveling) return;
            levelMs += dtMs;
            float push = Mathf.Max(Mathf.Abs(steer.x), Mathf.Abs(steer.y));
            if (push <= 1e-4f) { if ((levelNeutralMs += dtMs) >= LevelArmMs) levelSteerArmed = true; return; }
            levelNeutralMs = 0f;
            if ((levelSteerArmed || levelMs >= LevelGraceMs) && push >= LevelCancelInput) IsLeveling = false;
        }

        public void AddCollisionPush(float amount) => collisionPush += amount;

        /// <summary>
        /// Advances one frame.
        /// </summary>
        /// <param name="steer">x = yaw (-1 right .. +1 left, as in the original), y = pitch (+1 down .. -1 up).
        /// Use the controller's invert options to change feel.</param>
        /// <param name="dtMs">Frame time in milliseconds.</param>
        /// <param name="shipUp">Ship's current up vector expressed in world space (for auto-level).</param>
        /// <param name="shipRight">Ship's current right vector in world space (for auto-level).</param>
        /// <returns>Rotation to apply this frame in local space (pitch around X, yaw around Y, roll around Z),
        /// in degrees, plus forward distance in GoF units and sideways collision push.</returns>
        public FrameResult Step(Vector2 steer, float dtMs, Vector3 shipUp, Vector3 shipRight)
        {
            EnginesOff = false;
            float he = EffectiveHandling;

            // ---- steering input: ramp turn rates toward the stick target ----------------------
            bool yawInput = Mathf.Abs(steer.x) > 1e-4f;
            bool pitchInput = Mathf.Abs(steer.y) > 1e-4f;
            // Cosmetic model tilt. The original uses the input's sign (digital keys); for analog input scale it
            // with the deflection, or a slight push past the centre flips the model to the full tilt.
            VisualYawBank = !yawInput ? 0f : (TrackAnalogInput ? Mathf.Clamp(steer.x, -1f, 1f) : Mathf.Sign(steer.x)) * he;
            VisualPitchBank = !pitchInput ? 0f : (TrackAnalogInput ? Mathf.Clamp(steer.y, -1f, 1f) : Mathf.Sign(steer.y)) * he;

            if (yawInput) YawRate = RampToward(YawRate, steer.x, he, dtMs, 1f);
            if (pitchInput)
                PitchRate = RampToward(PitchRate, steer.y, he, dtMs,
                    !TiltMode ? 1f : steer.y > 0 ? PitchDownSensitivityScale : PitchUpSensitivityScale);

            // The original (PlayerEgo::roll, from PlayerEgo::update while +0x2f4 is set) keeps rolling under the stick until
            // level; only alignToHorizon (the stick's double tap) starts it. Remake: steering ends it, as it fought the
            // player's flying (mid-fight it kept rolling the ship; with the nose levelled too, LevelPitch, it pulled the nose
            // back whenever pitch was let go). Only steering that starts after the stick was neutral counts at first: the
            // touch double tap's second tap lands on the stick, and keys may still be held. Armed by a neutral stick held
            // LevelArmMs, ended by a push of at least LevelCancelInput (Discord report, 2026-10-18: level out didn't work by
            // the touch double tap and ended early on a controller), or by such a push past LevelGraceMs.
            UpdateLevelRequest(steer, dtMs);
            SteeringEndsLevelOut(steer, dtMs);

            // ---- rotation for this frame --------------------------------------------------------
            float pitchRad = dtMs * PitchRate * RateToRadiansPerMs;
            float yawRad = dtMs * YawRate * RateToRadiansPerMs;
            // The roll is checked every frame until the whole level-out is done (pitching the nose moves the lean); with
            // LevelPitch a steep nose (more than ~45 deg) comes down first, where the lean means little.
            float noseUp = Mathf.Abs(Vector3.Cross(shipRight, shipUp).normalized.y);
            bool steep = LevelPitch && noseUp > 0.7f;
            // Remake: with the nose near vertical (roll only) a roll about it can't make the ship upright, so the original's
            // check (lean below 0.015 and up.y > 0) never passed and it rolled on as long as the nose stayed there (climbs,
            // dives and loops in a fight): the level-out ends instead.
            if (IsLeveling && !LevelPitch && noseUp > LevelSteepNose) IsLeveling = false;
            rollLevelled = false;
            float rollRad = IsLeveling && !steep ? AutoLevelRoll(dtMs, shipUp, shipRight) * RollScale : 0f;
            if (IsLeveling)
            {
                bool pitchLevelled = true;
                if (LevelPitch && !pitchInput)
                {
                    // The nose's angle above the horizon; a positive local-X turn lowers the nose.
                    var fwd = Vector3.Cross(shipRight, shipUp).normalized;
                    float above = Mathf.Asin(Mathf.Clamp(fwd.y, -1f, 1f));
                    if (Mathf.Abs(above) > LevelPitchDone)
                    {
                        pitchLevelled = false;
                        float rate = LevelPitchRate * Mathf.Clamp01(Mathf.Abs(above) / 0.17f + 0.25f) * RollScale;
                        // Upside down (the roll not done yet) the ship's own pitch axis turns the other way.
                        float sign = shipUp.y >= 0f ? 1f : -1f;
                        pitchRad += sign * Mathf.Sign(above) * Mathf.Min(Mathf.Abs(above), rate * Mathf.Min(dtMs, LevelMaxDtMs));
                    }
                }
                if (rollLevelled && pitchLevelled) { IsLeveling = false; rollLevelled = false; }
            }

            // ---- no input: rates drain linearly back to zero (uses base H, not the cargo-reduced one)
            if (!yawInput) YawRate = Mathf.MoveTowards(YawRate, 0f, dtMs * Handling / DecayDivisor * Inertia * Style.turnScale);
            if (!pitchInput) PitchRate = Mathf.MoveTowards(PitchRate, 0f, dtMs * Handling / DecayDivisor * Inertia * Style.turnScale);

            // ---- movement -------------------------------------------------------------------------
            float forward = dtMs * StepMoveSpeed(dtMs);

            float push = 0f;
            if (Mathf.Abs(collisionPush) > CollisionPushCutoff)
            {
                push = collisionPush * dtMs;
                collisionPush *= CollisionPushDecay;
            }
            else collisionPush = 0f;

            UpdateBoost(dtMs);

            return new FrameResult
            {
                pitchDeg = pitchRad * Mathf.Rad2Deg,
                yawDeg = yawRad * Mathf.Rad2Deg,
                rollDeg = rollRad * Mathf.Rad2Deg,
                forwardUnits = forward,
                sidePushUnits = push
            };
        }

        // PlayerEgo::moveToPosition 0xa8720 (the autopilot) returns a bank value: the signed turn of each frame (+0x290, five
        // samples, +0x2a4 / +0x2a8), x the limit +0x284 = H * 750 / 63 * 1.2 x +0x288 = 15.14, clamped to the limit;
        // PlayerEgo::update then moves +0x280 toward it by dt * H / 81 and makes it the yaw rate +0x27c, which banks the model.
        const float AutopilotBankGain = 15.139845f;   // 0x41723ace
        readonly float[] autopilotTurns = new float[5];
        int autopilotTurnCount, autopilotTurnIndex;
        float autopilotRate;
        float autopilotSampleTurn, autopilotSampleMs;   // the turn summed over the 33.3 ms sample being gathered

        /// <summary>The autopilot's model bank (PlayerEgo::moveToPosition / update): 'turnLeft' = this frame's turn in radians,
        /// positive to the left; 'frameMs' = the frame's real length (unscaled); after Step, which left the bank at 0.</summary>
        public void AutopilotBank(float turnLeft, float dtMs, float frameMs)
        {
            float he = EffectiveHandling;
            // The original's samples are per frame (a 30 fps game): taken per 33.3 ms of real time here, so the bank doesn't
            // depend on the frame rate. Fast-forward runs the original's whole update with dt x 5 (autopilot_travel.md 4), so
            // each sample carries five times the turn and the ship banks hard into the turn (to the limit): per real time, not
            // per game time, keeps that (a player's report with the original's video; it was divided away).
            // A sample is the turn over 33.3 ms of real time (not one rendered frame scaled up): at 261 fps the five samples
            // spanned 19 ms instead of the original's 167, and the frame-time noise reached the yaw rate and the bank, so
            // the ship wobbled on the autopilot (Discord report, 2026-10-18).
            autopilotSampleTurn += turnLeft;
            autopilotSampleMs += Mathf.Max(0f, frameMs);
            if (autopilotSampleMs >= 33.333f)
            {
                autopilotTurns[autopilotTurnIndex] = autopilotSampleTurn * 33.333f / autopilotSampleMs;
                autopilotTurnIndex = (autopilotTurnIndex + 1) % autopilotTurns.Length;
                autopilotTurnCount = Mathf.Min(autopilotTurnCount + 1, autopilotTurns.Length);
                autopilotSampleTurn = autopilotSampleMs = 0f;
            }
            float sum = 0f;
            for (int i = 0; i < autopilotTurnCount; i++) sum += autopilotTurns[i];
            float limit = TargetRateScale * he / TargetRateDivisor * 1.2f;
            float target = autopilotTurnCount > 0 ? Mathf.Clamp(sum / autopilotTurnCount * limit * AutopilotBankGain, -limit, limit) : 0f;
            autopilotRate = Mathf.MoveTowards(autopilotRate, target, dtMs * he / 81f);
            VisualYawBank = autopilotRate / (TargetRateScale / TargetRateDivisor);
            // PlayerEgo::update: +0x280 becomes the yaw rate +0x27c, which banks the model (ShipController.UpdateVisualBank
            // reads the yaw rate; since it banks from the rates, the autopilot flew its turns flat again). The autopilot's
            // own turn doesn't rotate by it; handed back, the ship eases out of the turn as it decays (handleShip).
            YawRate = autopilotRate;
        }

        /// <summary>The autopilot let go (PlayerEgo::update: +0x2a4 / +0x2a8 cleared).</summary>
        public void ResetAutopilotBank() { autopilotTurnCount = 0; autopilotTurnIndex = 0; autopilotRate = 0f; autopilotSampleTurn = autopilotSampleMs = 0f; }

        /// <summary>PlayerEgo::updateManeuver: handleShip is skipped (no steering ramp, no turn from the rates); the yaw rate
        /// is the maneuver's (it banks the model and decays normally afterwards); the ship flies on at its speed.</summary>
        public FrameResult StepManeuver(float dtMs, float yawRate)
        {
            YawRate = yawRate;
            // The bank follows the rate: a full-stick rate (750 H / 63) banks like a full stick.
            VisualYawBank = yawRate / (TargetRateScale / TargetRateDivisor);
            VisualPitchBank = 0f;
            float forward = dtMs * StepMoveSpeed(dtMs);
            UpdateBoost(dtMs);
            return new FrameResult { forwardUnits = forward };
        }

        float RampToward(float rate, float input, float he, float dtMs, float sensitivityScale)
        {
            // target = trunc(input * 750 * H) / 63 (integer division, like the original)
            int raw = (int)(input * TargetRateScale * he);
            float target = raw / TargetRateDivisor * TurnScale * Style.turnScale;   // remake debug: a heavy hull's lower top rate (Mass); a mod's turn bonus
            float denom = (RampBase - Sensitivity * sensitivityScale) * 20f;
            if (denom < 1f) denom = 1f; // guard against extreme sensitivity values
            float step = dtMs * he / denom * Inertia * Style.turnScale;   // the same time to the (scaled) top rate

            // The original only accelerates toward the target in the input's direction and never slows an
            // over-target rate while the direction is held. That was fine for its digital input (the target is
            // always full deflection), but with an analog stick the rates stay saturated while the thumb moves
            // around, so steering snaps between 4 directions. Analog: track the target both ways at the same ramp.
            if (!TrackAnalogInput)
            {
                if (target > 0f && rate < target) return Mathf.Min(rate + step, target);
                if (target < 0f && rate > target) return Mathf.Max(rate - step, target);
                return rate;
            }
            return Mathf.MoveTowards(rate, target, step);
        }

        float AutoLevelRoll(float dtMs, Vector3 up, Vector3 right)
        {
            // "up.x" in the original = how much the ship's up vector leans sideways relative to the horizon; here: how far
            // the right wing points down (-right.y; for a small lean the same as up along the horizontal right). #57: it was
            // up along the horizontal part of 'right', which is nothing on a knife edge (the right wing straight up or down):
            // a ship on its side or upside down counted as level and stayed so.
            float lean = -right.y;
            float upY = up.y;

            if (Mathf.Abs(lean) < LevelDoneThreshold && upY > 0f)
            {
                rollLevelled = true;
                levelSide = 0;
                levelFine = false;
                return 0f;
            }

            // PlayerEgo::roll 0xa7c04: once the lean crosses zero (an overshoot) one step of 0.00035, then 0.0002 rad/ms until
            // level; before that 0.00075 upside down, else 0.00035 beyond 0.3, 0.00025 closer.
            int side = lean < 0f ? 1 : lean > 0f ? 2 : levelSide;
            // Remake: upside down the lean's sign is kept from the first frame (with the nose levelled at the same time,
            // LevelPitch, the ship could settle exactly inverted, where the lean flips sign every frame and the roll with it).
            if (upY < 0f && levelSide != 0) { side = levelSide; lean = side == 2 ? Mathf.Max(lean, 1e-4f) : Mathf.Min(lean, -1e-4f); }
            float rate;
            if (levelFine) rate = LevelRateFine;
            else if ((side == 2 && levelSide == 1) || (side == 1 && levelSide == 2)) { rate = LevelRateOvershoot; levelFine = true; }
            else if (upY < 0f) rate = LevelRateUpsideDown;
            else rate = Mathf.Abs(lean) > 0.3f ? LevelRateFar : LevelRateNear;
            levelSide = side;

            float dir = lean > 0f ? 1f : -1f; // roll so that the lean shrinks
            return dir * rate * Mathf.Min(dtMs, LevelMaxDtMs);
        }

        /// <summary>The boost timer alone, for frames another component moves the ship (ShipController.externalControl).</summary>
        public void TickBoost(float dtMs) => UpdateBoost(dtMs);

        void UpdateBoost(float dtMs)
        {
            if (FreeFlight) { UpdateFreeBoost(dtMs); return; }
            if (!IsBoosting) CurrentSpeed = TopSpeed;   // the style's (a mod's top speed bonus; switched in flight)
            // Timer counts up every frame; negative values mean "recharging" (PlayerEgo::update).
            int dt = Mathf.RoundToInt(dtMs);
            if (boostTimerMs < 0 && boostTimerMs + dt * 3 > 0) boostTimerMs = 0;
            boostTimerMs += dt;

            if (IsBoosting && boostTimerMs > boostDurationMs)
            {
                IsBoosting = false;
                CurrentSpeed = TopSpeed;
                boostTimerMs = Data.Cheats.NoBoostCooldown ? 0 : -boostRechargeMs;   // remake debug: no recharge
            }
        }

        public struct FrameResult
        {
            public float pitchDeg, yawDeg, rollDeg;
            public float forwardUnits;
            public float sidePushUnits;
            /// <summary>Free flight: this frame's move, world space, units.</summary>
            public Vector3 move;
        }
    }
}
