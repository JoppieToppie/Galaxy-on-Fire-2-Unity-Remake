// HapticMixer.cs
// Remake-only (the original has no haptics: AbyssEngine::Engine::Vibrate 0x8f22a is an empty stub and Globals::init calls
// ApplicationManager::VibrateEnable(false)). Plain C#, the part of Haptics that can be unit-tested: the haptic pulses that
// are still running and this frame's continuous rumble, mixed into the controller's two motor speeds.
//   pulse   full strength for the first quarter of its length, then a linear fade to 0 (a hit feels like a knock that
//           dies away, not a buzz that stops dead)
//   rumble  set every frame by whatever shakes the camera (explosions, boost, scraping a hull, the wormhole, cutscenes);
//           the strongest caller of the frame wins, it is used once and then cleared, like ChaseCamera.Rumble
//   mixing  the strongest source per motor wins (max, not a sum): four guns firing in the same frame are one tick
// Motor speeds are 0..1 before the intensity option (Haptics applies it).

using System.Collections.Generic;
using UnityEngine;

namespace GoF2Remake.Flight
{
    /// <summary>One haptic event: a controller pulse (low = the heavy left motor, high = the light right one, 0..1, for 'ms'
    /// milliseconds) and the phone's one-shot vibration (phoneMs 0 = the phone stays still: shots, ticks).</summary>
    public readonly struct HapticPulse
    {
        public readonly float low, high, ms, phoneMs, phoneAmplitude;

        public HapticPulse(float low, float high, float ms, float phoneMs = 0f, float phoneAmplitude = 0f)
        {
            this.low = Mathf.Clamp01(low);
            this.high = Mathf.Clamp01(high);
            this.ms = Mathf.Max(1f, ms);
            this.phoneMs = Mathf.Max(0f, phoneMs);
            this.phoneAmplitude = Mathf.Clamp01(phoneAmplitude);
        }

        /// <summary>The same pulse at a share of its strength (a distant blast); the length stays.</summary>
        public HapticPulse Scaled(float k)
        {
            k = Mathf.Clamp01(k);
            return new HapticPulse(low * k, high * k, ms, phoneMs, phoneAmplitude * k);
        }
    }

    public sealed class HapticMixer
    {
        /// <summary>Share of a pulse's length at full strength before the fade starts.</summary>
        public const float HoldShare = 0.25f;
        /// <summary>The light motor's share of the continuous rumble (a rumble is mostly the heavy motor).</summary>
        public const float RumbleHighShare = 0.6f;

        struct Running
        {
            public HapticPulse pulse;
            public float ageMs;
            public bool ui;
        }

        readonly List<Running> running = new List<Running>();
        float rumble;

        /// <summary>This frame's continuous rumble as the last Evaluate used it (0..1).</summary>
        public float LastRumble { get; private set; }

        /// <summary>Pulses still running (the continuous rumble not counted).</summary>
        public int Count => running.Count;

        /// <summary>Starts a pulse. 'ui' pulses (the options' preview) also play while the game is paused.</summary>
        public void Add(in HapticPulse pulse, bool ui = false) => running.Add(new Running { pulse = pulse, ui = ui });

        /// <summary>A continuous rumble for this frame (0..1); the strongest of the frame wins.</summary>
        public void Rumble(float level) => rumble = Mathf.Max(rumble, Mathf.Clamp01(level));

        /// <summary>The game paused: its pulses and rumble stop (the options' preview keeps playing).</summary>
        public void ClearGame()
        {
            running.RemoveAll(r => !r.ui);
            rumble = 0f;
        }

        public void Clear()
        {
            running.Clear();
            rumble = 0f;
            LastRumble = 0f;
        }

        /// <summary>The strength of a pulse 'ageMs' into it: 1 while held, then linear to 0 at its end.</summary>
        /// <summary>Whether new motor speeds are worth a command: a change of at least 0.01 on a motor, and a stop always
        /// (the 0.01 step alone left a fade's last few percent running when it ended within 0.01 of zero).</summary>
        public static bool ShouldSend(float low, float high, float sentLow, float sentHigh)
        {
            if (low <= 0f && high <= 0f) return sentLow != 0f || sentHigh != 0f;
            return Mathf.Abs(low - sentLow) >= 0.01f || Mathf.Abs(high - sentHigh) >= 0.01f;
        }

        public static float Envelope(float ageMs, float lengthMs)
        {
            float t = ageMs / Mathf.Max(1f, lengthMs);
            if (t >= 1f) return 0f;
            return t <= HoldShare ? 1f : 1f - (t - HoldShare) / (1f - HoldShare);
        }

        /// <summary>This frame's motor speeds (0..1, before the intensity option); then ages the pulses by dtMs and uses up
        /// the frame's rumble.</summary>
        public Vector2 Evaluate(float dtMs)
        {
            float low = 0f, high = 0f;
            for (int i = running.Count - 1; i >= 0; i--)
            {
                var r = running[i];
                float e = Envelope(r.ageMs, r.pulse.ms);
                low = Mathf.Max(low, r.pulse.low * e);
                high = Mathf.Max(high, r.pulse.high * e);
                r.ageMs += Mathf.Max(0f, dtMs);
                if (r.ageMs >= r.pulse.ms) running.RemoveAt(i);
                else running[i] = r;
            }
            LastRumble = rumble;
            low = Mathf.Max(low, rumble);
            high = Mathf.Max(high, rumble * RumbleHighShare);
            rumble = 0f;
            return new Vector2(low, high);
        }
    }
}
