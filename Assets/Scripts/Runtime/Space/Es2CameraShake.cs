// Es2CameraShake.cs
// Remake, free flight (Settings.Es2FreeFlightCamera): EVERSPACE 2's camera shakes, plain C# for ChaseCamera (and the tests).
// Every player shake of ES2 is a legacy Matinee shake (BP_CamShake_Base_C <- ES2.ESMatineeCameraShake,
// LegacyCameraShakePattern), played camera-local through LIB_F_General.PlayCameraShake: scale = Scale x falloff (1: the
// player's calls pass R = -1) x the ship class's (1 / 1 / 0.8 / 0.6), started only above 0.001. An oscillator is
// Amp x sin(phase) (Perlin: Amp x PerlinNoise1D(phase)), phase += dt x Freq (rad/s), starting at a random phase
// (EOO_OffsetRandom) or 0 (EOO_OffsetZero); location in cm (camera X forward, Y right, Z up), rotation in degrees; the weight
// rises over BlendInTime and falls over the last BlendOutTime of Duration. A single-instance shake played again while it
// runs keeps going with the new scale and its full duration again (the ship re-issues the idle, boost and cruise shakes
// every frame, so they last while they are asked for, then blend out). In the cockpit views the location amplitudes are x
// FirstPersonScale (BP_CamShake_Base: 0.05, the acceleration sway's 1). Values: the dumped /Game/Blueprints/Effects/
// CameraShakes assets (docs/notes/free-flight.md "Free flight camera").

using UnityEngine;

namespace GoF2Remake.Flight
{
    public sealed class Es2CameraShake
    {
        /// <summary>One axis: amplitude (cm or degrees), frequency (rad/s), Perlin instead of a sine, starting at phase 0.</summary>
        public readonly struct Osc
        {
            public readonly float amp, freq;
            public readonly bool perlin, zero;
            public Osc(float amp, float freq, bool perlin = false, bool zero = false) { this.amp = amp; this.freq = freq; this.perlin = perlin; this.zero = zero; }
        }

        public readonly string name;
        public readonly float duration, blendIn, blendOut, firstPersonScale;
        public readonly bool single;
        // pitch, yaw, roll (degrees), x forward, y right, z up (cm)
        readonly Osc[] osc;
        readonly float[] phase = new float[6];
        readonly float[] seed = new float[6];
        readonly float[] v = new float[6];
        float time, remaining, scale;
        bool playing;

        public Es2CameraShake(string name, float duration, float blendIn, float blendOut, bool single, float firstPersonScale,
            Osc pitch, Osc yaw, Osc roll, Osc x, Osc y, Osc z)
        {
            this.name = name; this.duration = duration; this.blendIn = blendIn; this.blendOut = blendOut;
            this.single = single; this.firstPersonScale = firstPersonScale;
            osc = new[] { pitch, yaw, roll, x, y, z };
        }

        public bool Playing => playing;
        public float Scale => scale;

        /// <summary>StartCameraShake at 'shakeScale' (nothing at 0.001 or less). Again while it runs: the new scale and its
        /// whole duration once more, the phases and the blend-in going on (a single instance; ES2's other shakes start over).</summary>
        public void Play(float shakeScale)
        {
            if (shakeScale <= 0.001f) return;
            if (!playing || !single)
            {
                for (int i = 0; i < 6; i++)
                {
                    phase[i] = osc[i].zero ? 0f : Random.Range(0f, 2f * Mathf.PI);
                    seed[i] = Random.Range(0f, 100f);
                }
                time = 0f;
            }
            playing = true;
            remaining = duration;
            scale = shakeScale;
        }

        public void Stop() { playing = false; }

        /// <summary>The weight at 'time' with 'remaining' left: the blend in, then the blend out over the last BlendOutTime.</summary>
        public static float Weight(float time, float remaining, float duration, float blendIn, float blendOut)
        {
            float w = 1f;
            if (blendIn > 0f && time < blendIn) w *= time / blendIn;
            if (duration > 0f && blendOut > 0f && remaining < blendOut) w *= Mathf.Max(0f, remaining) / blendOut;
            return w;
        }

        /// <summary>Advances the shake by 'dt' seconds and adds its offset: 'rotDeg' (pitch, yaw, roll), 'locCm' (Unity axes: x
        /// right, y up, z forward), the location x FirstPersonScale in a cockpit view.</summary>
        public void Tick(float dt, bool firstPerson, ref Vector3 rotDeg, ref Vector3 locCm)
        {
            if (!playing || dt <= 0f) return;
            time += dt;
            remaining -= dt;
            if (duration > 0f && remaining <= 0f) { playing = false; return; }
            float w = Weight(time, remaining, duration, blendIn, blendOut) * scale;
            for (int i = 0; i < 6; i++)
            {
                var o = osc[i];
                v[i] = 0f;
                if (o.amp == 0f) continue;
                phase[i] += dt * o.freq;
                v[i] = o.amp * (o.perlin ? Mathf.PerlinNoise(phase[i], seed[i]) * 2f - 1f : Mathf.Sin(phase[i])) * w;
            }
            float fp = firstPerson ? firstPersonScale : 1f;
            rotDeg += new Vector3(v[0], v[1], v[2]);
            locCm += new Vector3(v[4], v[5], v[3]) * fp;   // UE X forward, Y right, Z up -> Unity
        }

        static Osc O(float a, float f, bool perlin = false, bool zero = false) => new Osc(a, f, perlin, zero);
        static readonly Osc None = default;

        /// <summary>BP_CamShake_Boost_001: every frame while boosting.</summary>
        public static Es2CameraShake Boost() => new Es2CameraShake("Boost_001", 0.5f, 0.2f, 0.2f, true, 0.05f,
            O(0.08f, 100f), O(0.08f, 100f), O(0.3f, 20f, true), O(6f, 20f, true), O(3f, 20f, true), O(3f, 20f, true));
        /// <summary>BP_CamShake_BoostInit: once as a boost starts.</summary>
        public static Es2CameraShake BoostInit() => new Es2CameraShake("BoostInit", 1f, 0.05f, 1f, true, 0.05f,
            O(0.3f, 80f, zero: true), O(0.3f, 80f, zero: true), O(0.3f, 40f, zero: true), O(10f, 80f, zero: true), O(5f, 80f, zero: true), O(5f, 80f, zero: true));
        /// <summary>BP_CamShake_IdleFlight: every frame at the speed's share of 100 m/s (eased), not in free look.</summary>
        public static Es2CameraShake IdleFlight() => new Es2CameraShake("IdleFlight", 1f, 1f, 1f, true, 0.05f,
            None, None, None, O(20f, 1.631611f), O(20f, 1.515439f), O(20f, 1.061546f));
        /// <summary>BP_CamShake_AccelerationSway_001: the cockpit view, at the acceleration's share of 100 m/s^2 (eased).</summary>
        public static Es2CameraShake AccelerationSway() => new Es2CameraShake("AccelerationSway_001", 1f, 0.5f, 0.5f, true, 1f,
            O(0.5f, 2f, true), O(0.5f, 2f, true), O(0.5f, 2f, true), O(1f, 2f, true), O(1f, 2f, true), O(1f, 2f, true));
        /// <summary>BP_CamShake_Hit_001: a hit on the hull.</summary>
        public static Es2CameraShake Hit() => new Es2CameraShake("Hit_001", 0.2f, 0f, 0.2f, false, 0.05f,
            O(1f, 25f), O(1f, 25f), O(1f, 25f), O(2f, 25f), O(2f, 25f), O(2f, 25f));
        /// <summary>BP_CamShake_CruiseMode_Start: once as the cruise starts.</summary>
        public static Es2CameraShake CruiseStart() => new Es2CameraShake("CruiseMode_Start", 0.4f, 0f, 0.4f, false, 0.05f,
            O(1f, 25f), O(1f, 25f), O(1f, 25f), O(2f, 25f), O(2f, 25f), O(2f, 25f));
        /// <summary>BP_CamShake_CruiseMode_Rumble: every frame of the cruise at 0.15.</summary>
        public static Es2CameraShake CruiseRumble() => new Es2CameraShake("CruiseMode_Rumble", 1f, 0f, 1f, true, 0.05f,
            O(1f, 80f), O(1f, 80f), O(2f, 20f), None, None, None);

        /// <summary>GetShipClassCameraShakeScale (1 Light / Medium, 0.8 Heavy, 0.6 the Okkar): the remake's heavy hulls
        /// (ShipController.mass 0..1) toward Heavy's.</summary>
        public static float ClassScale(float mass) => Mathf.Lerp(1f, 0.8f, Mathf.Clamp01(mass));
    }
}
