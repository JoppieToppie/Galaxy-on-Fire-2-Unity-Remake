// Es2NpcFlight.cs
// Remake option (2026-11-06, Options > Gameplay "EVERSPACE 2 NPC flight", Settings.Es2NpcFlight, a session rule): NPC ships fly
// on physics the way EVERSPACE 2's do (es2_ships_importer research/npc_ai.md; ES2-Win64-Shipping.exe's
// UBTT_FlyToAvoidCollision, FBehaviourTreeTaskLib::CalcAiRotationInput / CalcAiLinearInputStrength, the movement step the
// player's free flight ports, FlightModel.StepFree) instead of GoF2's (PlayerFighter::update: the heading nudged toward the
// goal, a constant speed, no momentum). World.NpcShip flies by it (NpcShipEs2Flight):
//   turning   each axis's input = sign(err) x clamp((|err| - the braking angle) / RotationEaseAngle, 0, 1), the braking
//             angle 1/2 w^2 / a while it already turns that way: it coasts out of a turn instead of overshooting; the
//             angular rate ramps to input x the top rate at a (FlightModel's 400 ms from rest to the top rate)
//   thrust    forward, eased as the speed nears the top (1 - p^3, EaseAccelerationExponent 3; ES2's 132 m/s^2 at 95.3 m/s);
//             near a goal it must stop at (formation slots) clamp((d - the stopping distance) / LinearEaseDistance, 0, 1),
//             eased; faster than asked it brakes at a quarter of the acceleration (DefaultDeceleration); the velocity
//             swings toward the nose by the turning boost (DefaultTurningBoost = AccelerationForward / 6000 a second), so a
//             turn drifts a little
//   avoiding  every ForceUpdateTime (2 s) a path test (GenerateNextWaypoint, TestPathByEulerIntegration): the flight toward
//             a candidate direction simulated MovementPredictionTime (4 s) ahead and checked against the orbit's
//             obstacles and asteroids; candidates in ES2's exterior table (the cone angle about the wanted direction and a
//             speed factor), TestDirectionCount (3) round each cone, nearest the wanted direction first; the first clear
//             one wins, the wanted one when none is
// GoF2's numbers stay: the top speed (2 u/ms, boost 5.5) and the top turn rate (GoF2's 48 / 65536 a ms, about 42 deg/s, so
// the fights keep GoF2's pace); ES2's are the ramps, the easing and the avoidance. Left out: ES2's steering noise (GoF2's
// guns fire along the nose, so it would be aim noise) and strafing. Plain C#; degrees and seconds unless said.

namespace GoF2Remake.Flight
{
    public static class Es2NpcFlight
    {
        /// <summary>GoF2's top turn rate (dt x 48 / 65536 radians a ms) in degrees a second.</summary>
        public const float TopTurnDegS = 48f / 65536f * 1000f * 57.29578f;
        /// <summary>FlightModel.FreeTurnRampMs: from rest to the top turn rate.</summary>
        public const float TurnRampS = 0.4f;
        public const float TurnAccelDegS2 = TopTurnDegS / TurnRampS;
        /// <summary>UBTT_FlyToAvoidCollision's defaults: RotationEaseAngle, LinearEaseDistance (m), ForceUpdateTime,
        /// MovementPredictionTime, TestDirectionCount.</summary>
        public const float RotationEaseDeg = 5f, LinearEaseM = 30f, ForceUpdateS = 2f, PredictionS = 4f;
        public const int TestDirectionCount = 3;
        /// <summary>ES2's forward acceleration a second over its top speed (132 m/s^2 at 95.3 m/s, FlightModel.FreeAccelFwd),
        /// the braking's share of it (DefaultDeceleration), the turning boost's divisor (cm/s^2), the ease exponent.</summary>
        public const float AccelPerS = 132f / 95.3f, BrakeShare = 0.25f, TurningBoostCm = 6000f, EaseExponent = 3f;
        /// <summary>ES2's player ship: 132 m/s^2 in cm/s^2, for the turning boost (AccelerationForward / 6000 a second).</summary>
        public const float AccelCm = 13200f;
        /// <summary>The path test's samples a second (fewer than ES2's TraceSamplingStepsPerSecond 20: a GoF2 hull is big).</summary>
        public const float SamplesPerS = 5f;

        /// <summary>ES2's exterior candidate table (GenerateNextWaypoint): the cone about the wanted direction (degrees) and the
        /// speed factor, tried in turn.</summary>
        public static readonly (float coneDeg, float speed)[] Candidates =
        {
            (0f, 1f), (30f, 1f), (60f, 1f), (90f, 1f),
            (0f, 0.33f), (30f, 0.33f), (60f, 0.33f), (90f, 0.33f),
            (135f, 1f), (135f, 0.33f), (180f, 1f), (180f, 0.66f), (180f, 0.33f),
        };

        /// <summary>CalcAiRotationInput: one axis's input (-1..1) for an error of 'errDeg' while turning at 'rateDegS' (positive
        /// the same way as a positive error) with an angular acceleration of 'accelDegS2'.</summary>
        public static float RotationInput(float errDeg, float rateDegS, float accelDegS2, float easeDeg = RotationEaseDeg)
        {
            float a = System.Math.Abs(errDeg);
            if (a < 1e-4f) return 0f;
            float s = errDeg > 0f ? 1f : -1f;
            // the braking angle counts only while it already turns toward the error
            float braking = rateDegS * s > 0f && accelDegS2 > 1e-6f ? 0.5f * rateDegS * rateDegS / accelDegS2 : 0f;
            float k = (a - braking) / System.Math.Max(1e-4f, easeDeg);
            return s * (k < 0f ? 0f : k > 1f ? 1f : k);
        }

        /// <summary>CalcAiLinearInputStrength: the thrust (0..1) at 'distance' from a goal it must stop at, with 'stopping' the
        /// distance it takes to stop and 'ease' LinearEaseDistance (the same unit); eased between 0 and 1.</summary>
        public static float LinearInput(float distance, float stopping, float ease)
        {
            float k = (distance - stopping) / System.Math.Max(1e-4f, ease);
            if (k <= 0f) return 0f;
            if (k >= 1f) return 1f;
            return k * k * (3f - 2f * k);
        }

        /// <summary>The distance to stop from 'speed' braking at 'decel' (units and time units alike).</summary>
        public static float StoppingDistance(float speed, float decel) => decel > 1e-9f ? speed * speed / (2f * decel) : float.MaxValue;

        /// <summary>The thrust's ease: 1 - p^3, p the speed over the top speed (0..1).</summary>
        public static float Ease(float speed, float top)
        {
            float p = top > 1e-9f ? speed / top : 1f;
            p = p < 0f ? 0f : p > 1f ? 1f : p;
            return 1f - p * p * p;
        }

        /// <summary>A rate (per second) ramped toward 'target' by at most 'accel' x 'dt'.</summary>
        public static float Ramp(float rate, float target, float accel, float dt)
        {
            float step = accel * dt;
            return rate < target ? System.Math.Min(target, rate + step) : System.Math.Max(target, rate - step);
        }
    }
}
