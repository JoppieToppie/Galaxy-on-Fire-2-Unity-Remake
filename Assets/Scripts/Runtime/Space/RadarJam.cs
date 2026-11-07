// RadarJam.cs
// Remake (2026-11-04, for mods, after Galaxy on Fire 3's boss Radar Jammer: "All my scanning systems go haywire. The pilot
// must trust their inefficient organic eyes"): while jammed the player's radar locks nothing (Navigation.RadarOff) and the
// HUD draws no ship markers (CombatView). A boss's device sets it (World.NpcDevices); it runs on scaled time, so a pause
// holds it.

using UnityEngine;

namespace GoF2Remake.Flight
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class RadarJam
    {
        static float until = -1f;

        public static bool Active => Time.time < until;

        /// <summary>Jammed for 'ms' from now (a longer jam already running is kept).</summary>
        public static void Start(float ms) => until = Mathf.Max(until, Time.time + ms / 1000f);

        public static void Clear() => until = -1f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => until = -1f;
    }
}
