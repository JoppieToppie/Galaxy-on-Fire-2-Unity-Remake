// FlightRules.cs
// Remake, for mods: every number the player's ship movement runs on (FlightModel, the dodge), by name, so a mod can change
// any of them: for every ship (a mod's flight.json) or for one (ships.json "flight": { "rules": { ... } }; Modding.ModFlight).
// The defaults are the original's (and free flight's measured EVERSPACE 2 values): a ship without a mod's rules flies
// exactly as before. [Range] keeps a mod's value where the flight still works (no divisions by zero); [Tooltip] is what
// the Mod Builder shows beside each field.

using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace GoF2Remake.Flight
{
    [Serializable]
    public class FlightRules
    {
        [Header("Original flight")]
        [Range(0.1f, 50f), Tooltip("Top speed of every ship, game units per ms (2 = 100 m/s).")]
        public float baseSpeed = FlightModel.BaseSpeed;
        [Range(0f, 5f), Tooltip("Handling each handling upgrade adds (0.2 = +20 handling).")]
        public float handlingPerUpgrade = 0.2f;
        [Range(1f, 10000f), Tooltip("Top turn rate: full stick x this x handling / the divisor below.")]
        public float turnRateScale = FlightModel.TargetRateScale;
        [Range(1f, 10000f), Tooltip("Divides the top turn rate (whole numbers, like the original).")]
        public float turnRateDivisor = FlightModel.TargetRateDivisor;
        [Range(0.5f, 20f), Tooltip("Turn rate build-up: dt x handling / ((this - sensitivity) x the scale below). Lower = snappier.")]
        public float turnRampBase = FlightModel.RampBase;
        [Range(0.1f, 1000f), Tooltip("Turn rate build-up scale (see above). Lower = snappier.")]
        public float turnRampScale = 20f;
        [Range(1f, 100000f), Tooltip("Turn rate fall-off with the stick let go: dt x handling / this. Lower = stops turning sooner.")]
        public float turnDecayDivisor = FlightModel.DecayDivisor;
        [Range(0f, 3f), Tooltip("Tilt steering: the sensitivity's share pitching up.")]
        public float tiltPitchUpScale = FlightModel.PitchUpSensitivityScale;
        [Range(0f, 3f), Tooltip("Tilt steering: the sensitivity's share pitching down.")]
        public float tiltPitchDownScale = FlightModel.PitchDownSensitivityScale;
        [Range(0f, 100f), Tooltip("Boost speed = this x the booster's boost speed / 100 + the top speed.")]
        public float boostSpeedScale = 2f;
        [Tooltip("The boost speed in whole units per ms (the original's: Linear and Cyclotron boost alike).")]
        public bool truncateBoostSpeed = true;
        [Range(0f, 1f), Tooltip("Hardcore: the handling a full hold takes away (0.4 = 40 %).")]
        public float cargoHandlingLoss = 0.4f;
        [Range(0f, 0.99f), Tooltip("A collision's push left after each frame.")]
        public float collisionPushDecay = FlightModel.CollisionPushDecay;

        [Header("Original flight: strafe")]
        [Range(0f, 10f), Tooltip("Strafe speed = handling x this (units per ms), at most the maximum below.")]
        public float strafeHandlingFactor = 0.06f;
        [Range(0f, 50f), Tooltip("Top strafe speed, units per ms.")]
        public float strafeMaxSpeed = 2f;
        [Range(0.001f, 1f), Tooltip("Strafe speed share at the first frame.")]
        public float strafeRampStart = 0.1f;
        [Range(1f, 10f), Tooltip("Strafe speed share growth per original frame (to full).")]
        public float strafeRampGrowth = 1.5f;
        [Range(0f, 0.99f), Tooltip("Strafe speed left after each original frame once the key is let go.")]
        public float strafeDecay = 0.7f;

        [Header("Dodge")]
        [Range(100f, 10000f), Tooltip("How long a dodge lasts, ms.")]
        public float dodgeDurationMs = 1200f;
        [Range(0f, 10f), Tooltip("The dodge's slide: dt x this x handling units, fading over the dodge.")]
        public float dodgeSlide = 0.2f;
        [Range(0f, 0.1f), Tooltip("The dodge's nose turn, radians per original frame, fading over the dodge.")]
        public float dodgeTurn = 0.006283186f;
        [Range(0f, 5f), Tooltip("The dodge's bank.")]
        public float dodgeBank = 0.4f;

        [Header("Level out")]
        [Range(0f, 10000f), Tooltip("A level-out request waits this long (ms); steering meanwhile drops it.")]
        public float levelRequestDelayMs = FlightModel.LevelRequestDelayMs;
        [Range(0f, 0.05f), Tooltip("Roll rate close to level, radians per ms (0.00025 = 14 deg/s).")]
        public float levelRateNear = FlightModel.LevelRateNear;
        [Range(0f, 0.05f), Tooltip("Roll rate far from level, radians per ms.")]
        public float levelRateFar = FlightModel.LevelRateFar;
        [Range(0f, 1f), Tooltip("The lean (0..1) past which the far rate rolls.")]
        public float levelFarLean = 0.3f;
        [Range(0f, 0.05f), Tooltip("Roll rate upside down, radians per ms.")]
        public float levelRateUpsideDown = FlightModel.LevelRateUpsideDown;
        [Range(0f, 0.05f), Tooltip("Roll rate the frame the lean overshoots, radians per ms.")]
        public float levelRateOvershoot = FlightModel.LevelRateOvershoot;
        [Range(0f, 0.05f), Tooltip("Roll rate after an overshoot, radians per ms.")]
        public float levelRateFine = FlightModel.LevelRateFine;
        [Range(0f, 0.5f), Tooltip("The lean (0..1) that counts as level.")]
        public float levelDoneLean = FlightModel.LevelDoneThreshold;
        [Range(0f, 0.05f), Tooltip("Option \"Level out also levels the nose\": its pitch rate, radians per ms.")]
        public float levelPitchRate = FlightModel.LevelPitchRate;
        [Range(0f, 1f), Tooltip("A nose this close to vertical (its share of world up) ends a roll-only level-out.")]
        public float levelSteepNose = FlightModel.LevelSteepNose;
        [Range(0f, 10000f), Tooltip("A neutral stick this long (ms) lets steering end a level-out.")]
        public float levelArmMs = FlightModel.LevelArmMs;
        [Range(0f, 1f), Tooltip("The stick push (0..1) that ends a level-out.")]
        public float levelCancelInput = FlightModel.LevelCancelInput;
        [Range(0f, 10000f), Tooltip("After this long (ms) a push ends a level-out without the neutral stick.")]
        public float levelGraceMs = FlightModel.LevelGraceMs;

        [Header("Free flight: turning")]
        [Range(1f, 2000f), Tooltip("Top turn rate at handling 100, degrees per second.")]
        public float freeTurnRateDeg = 125f;
        [Range(0f, 3f), Tooltip("The turn rate grows with (handling / 100) to this power.")]
        public float freeTurnExponent = FlightModel.FreeTurnExponent;
        [Range(0f, 1f), Tooltip("The share of the turn rate above the top rate at handling 100 that counts.")]
        public float freeTurnAboveShare = FlightModel.FreeTurnAboveShare;
        [Range(1f, 10000f), Tooltip("Time from no turn to the top turn rate and back, ms.")]
        public float freeTurnRampMs = FlightModel.FreeTurnRampMs;
        [Range(0f, 5f), Tooltip("Roll rate, of the turn rate.")]
        public float freeRollShare = 1f;
        [Range(0f, 2f), Tooltip("Turn rate while boosting, of the turn rate.")]
        public float freeBoostTurnShare = FlightModel.FreeBoostTurnShare;

        [Header("Free flight: moving")]
        [Range(0f, 5f), Tooltip("Top speed backward, of the forward one.")]
        public float freeReverseShare = FlightModel.FreeReverseShare;
        [Range(0f, 5f), Tooltip("Top speed sideways, of the forward one.")]
        public float freeStrafeShare = FlightModel.FreeStrafeShare;
        [Range(0f, 5f), Tooltip("Top speed up and down, of the forward one.")]
        public float freeHoverShare = FlightModel.FreeHoverShare;
        [Range(0.01f, 100f), Tooltip("Forward acceleration, top speeds per second (at handling 100).")]
        public float freeAccelForward = 132f / 95.3f;
        [Range(0.01f, 100f), Tooltip("Backward, sideways and up / down acceleration, their top speeds per second.")]
        public float freeAccelSide = 90f / 47.65f;
        [Range(0f, 3f), Tooltip("The acceleration grows with (handling / 100) to this power.")]
        public float freeAccelHandlingExponent = 0.5f;
        [Range(0.1f, 20f), Tooltip("Dampeners on: the acceleration eases out as 1 - (speed / top speed) to this power.")]
        public float freeEaseExponent = FlightModel.FreeEaseExponent;
        [Range(0f, 20f), Tooltip("Dampeners on: braking, of the forward acceleration.")]
        public float freeBrakeShare = FlightModel.FreeBrakeShare;
        [Range(0f, 10f), Tooltip("Above the top speed the braking grows with (speed / top speed) to this power.")]
        public float freeBrakeAboveTopExponent = FlightModel.FreeBrakeAboveTopExp;
        [Range(0f, 1f), Tooltip("Thrusting the way the ship moves brakes up to this much less.")]
        public float freeInheritInertia = FlightModel.FreeInheritInertia;
        [Range(0f, 100f), Tooltip("Dampeners on: how hard the velocity turns toward the thrust (1 = EVERSPACE 2's).")]
        public float freeTurningBoost = 1f;

        [Header("Free flight: boost")]
        [Range(0f, 10f), Tooltip("Boost top speed = the booster's original boost x this (of the top speed).")]
        public float freeBoostScale = FlightModel.FreeBoostScale;
        [Range(1f, 50f), Tooltip("Boost top speed of a booster without a boost speed, of the top speed.")]
        public float freeBoostFallback = FlightModel.FreeBoostFallback;
        [Range(0f, 50f), Tooltip("Acceleration while boosting, of the forward one.")]
        public float freeBoostAccel = FlightModel.FreeBoostAccel;
        [Range(100f, 600000f), Tooltip("A full boost pool lasts this long (ms) for a booster without a duration.")]
        public float freeBoostDurationMs = FlightModel.FreeBoostDurationMs;
        [Range(0f, 1f), Tooltip("An emptied boost pool boosts again from this share.")]
        public float boostRestartEnergy = FlightModel.BoostRestartEnergy;

        [Header("Free flight: auto-roll")]
        [Range(0f, 1f), Tooltip("The auto-roll's strongest roll input.")]
        public float autoRollMaxInput = FlightModel.AutoRollMaxInput;
        [Range(0.1f, 180f), Tooltip("It rolls once the ship leans past this, degrees.")]
        public float autoRollAngleDeg = FlightModel.AutoRollAngleDeg;
        [Range(0f, 180f), Tooltip("It stops within this of level, degrees.")]
        public float autoRollToleranceDeg = FlightModel.AutoRollToleranceDeg;
        [Range(0f, 90f), Tooltip("A nose pitched past this (degrees) holds it back.")]
        public float autoRollPitchAngleDeg = FlightModel.AutoRollPitchAngleDeg;
        [Range(0f, 1f), Tooltip("A pitch input past this holds it back.")]
        public float autoRollPitchInput = FlightModel.AutoRollPitchInput;
        [Range(1f, 60000f), Tooltip("Held back, it eases in again over this long, ms.")]
        public float autoRollEaseInMs = FlightModel.AutoRollEaseInMs;

        [Header("Speed passives")]
        [Range(0f, 10f), Tooltip("Below this speed (units per ms) the ship counts as stopped (full-stop damage).")]
        public float fullStopSpeed = FlightModel.FullStopUnits;
        [Range(0f, 50f), Tooltip("At or below this speed (units per ms; 0.6 = 30 m/s) the low-speed resistance counts...")]
        public float lowSpeedSpeed = FlightModel.LowSpeedUnits;
        [Range(0f, 60000f), Tooltip("...once the ship has flown so slowly this long, ms.")]
        public float lowSpeedDelayMs = FlightModel.LowSpeedDelayMs;

        /// <summary>The defaults (never change it: a ship without a mod's rules flies with this one).</summary>
        public static readonly FlightRules Default = new FlightRules();

        /// <summary>Every rule (the public fields), in the order above.</summary>
        public static readonly FieldInfo[] Fields = typeof(FlightRules).GetFields(BindingFlags.Public | BindingFlags.Instance);

        public static FieldInfo Field(string name) => Array.Find(Fields, f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));

        public FlightRules Clone() => (FlightRules)MemberwiseClone();

        /// <summary>A mod's values by rule name (a bool rule: 0 off, else on), clamped to each rule's range; unknown names
        /// are skipped (ModFlight warns about them).</summary>
        public FlightRules Apply(IReadOnlyDictionary<string, float> values)
        {
            if (values == null) return this;
            foreach (var kv in values)
            {
                var f = Field(kv.Key);
                if (f == null) continue;
                if (f.FieldType == typeof(bool)) { f.SetValue(this, kv.Value != 0f); continue; }
                var r = f.GetCustomAttribute<RangeAttribute>();
                f.SetValue(this, r != null ? Mathf.Clamp(kv.Value, r.min, r.max) : kv.Value);
            }
            return this;
        }
    }
}
