// ScreenFlash.cs
// Remake (2026-11-04, for mods, after Galaxy on Fire 3's boss Flash Grenade: "Pilots should try and turn their ship away from
// the flash ... this will wear off soon"): a white screen over the flight view (FlightHud), at its opacity at the start and
// fading out over its time (the square of what is left: the eyes come back slowly, then fast). A boss's device starts it
// (World.NpcDevices); it runs on scaled time, so a pause holds it.

using UnityEngine;

namespace GoF2Remake.Flight
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class ScreenFlash
    {
        static float start = -1f, length = 1f, peak;

        /// <summary>The white's opacity now (0..1).</summary>
        public static float Alpha
        {
            get
            {
                if (start < 0f) return 0f;
                float t = (Time.time - start) / length;
                if (t >= 1f) { start = -1f; return 0f; }
                float left = 1f - Mathf.Max(0f, t);
                return peak * left * left;
            }
        }

        /// <summary>A flash of 'opacity' (0..1) fading over 'ms'; a brighter one already showing is kept.</summary>
        public static void Start(float opacity, float ms)
        {
            if (opacity <= Alpha) return;
            peak = Mathf.Clamp01(opacity);
            length = Mathf.Max(0.05f, ms / 1000f);
            start = Time.time;
        }

        public static void Clear() => start = -1f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => start = -1f;
    }
}
