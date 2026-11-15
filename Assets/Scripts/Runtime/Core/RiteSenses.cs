// RiteSenses.cs
// Remake (2026-11-13, Chorus's Rite of the Senses: BP_ActivePower_Scan's short-range scan): while it lasts the ship sees as
// the game's best scanners do: the class-A asteroids marked (Mining.Ultrascan, the Hiroto Ultrascan's attr 30) and a locked
// ship's cargo read out (CombatRadar, attr 31). PlayerDevices sets it; its wave uncovers cloaked ships on its own.

using UnityEngine;

namespace GoF2Remake.Flight
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class RiteSenses
    {
        /// <summary>The Time.time the senses fade at.</summary>
        public static float Until = -1f;
        public static bool Active => Time.time < Until;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Until = -1f;
    }
}
