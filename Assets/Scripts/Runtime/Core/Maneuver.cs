// Maneuver.cs
// The dodge: a quick horizontal swipe slides the ship sideways for 1200 ms (the original's only reachable maneuver).
//   MGame::maneuverTouchEnd 0x1a7eac   a touch on no control, at most 600 ms, horizontal distance > w/480 * 70 px (vertical
//                                      drift over h/320 * 90 px cancels it, MGame::OnTouchMove), chase camera, no HUD menu:
//                                      initManeuver(1) when the finger went left, else (2)
//   PlayerEgo::initManeuver 0xae1c0    volatile goods aboard: +0.17 (even when one is already running); starts when none runs
//   PlayerEgo::updateManeuver 0xa8230  per frame, t += dt, H = PlayerEgo+0x154, k = 1 - t / 1200:
//                                      slide dt * 4 * k * H * 0.05 units toward the swipe (type 1 the ship's left);
//                                      heading about the ship's up by k * 0.006283186 rad per original frame (OriginalFrame.Ms), away from the
//                                      slide; yaw rate (+0x27c, the bank) = sin(pi t / 1200) * H * 0.05 * 750 * 0.4, the turn
//                                      that side; handleShip is skipped meanwhile (no steering), the ship flies on at its speed
//   t >= 1200                          over (LevelScript::resetCamera); the yaw rate then decays as usual
// Type 3 (a U-turn) exists but nothing starts it. No sound, no cooldown. Plain C#: ShipController applies the outputs.

using UnityEngine;

namespace GoF2Remake.Flight
{
    public class Maneuver
    {
        public const float DurationMs = 1200f;
        const float FrameMs = OriginalFrame.Ms;

        /// <summary>0 none, 1 dodge left, 2 dodge right.</summary>
        public int Type { get; private set; }
        public bool Active => Type != 0;
        float t;

        public bool Start(int type)
        {
            if (Active || (type != 1 && type != 2)) return false;
            Type = type;
            t = 0f;
            return true;
        }

        public void Cancel() => Type = 0;

        /// <summary>One frame. slideUnits: toward the ship's left (+) / right (-); headingRad: Unity yaw (+ = right);
        /// modelYawRate: FlightModel's rate (+ = left).</summary>
        public void Step(float dtMs, float handling, out float slideUnits, out float headingRad, out float modelYawRate)
        {
            slideUnits = headingRad = modelYawRate = 0f;
            if (!Active) return;
            float k = Mathf.Max(0f, 1f - t / DurationMs);
            float side = Type == 1 ? 1f : -1f;
            slideUnits = side * dtMs * 4f * k * handling * 0.05f;
            headingRad = side * k * 0.006283186f * (dtMs / FrameMs);        // swipe left: the nose turns right
            modelYawRate = -side * Mathf.Sin(Mathf.PI * t / DurationMs) * handling * 0.05f * 750f * 0.4f;
            t += dtMs;
            if (t >= DurationMs) Type = 0;
        }
    }
}
