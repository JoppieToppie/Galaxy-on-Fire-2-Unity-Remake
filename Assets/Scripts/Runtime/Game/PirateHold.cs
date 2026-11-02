// PirateHold.cs
// Remake mods (a modder's suggestion: "systems that belong to pirates like Shima at the start, but constantly"): a station
// its mod marks held by pirates (systems.json "pirateHeld": every station of the system; stations.json "orbit.pirateHeld"
// for one, which wins) is besieged on every visit like the Kaamo Club before it was freed (World.KaamoSiege's held mode:
// pirate outposts, pirates coming back while one stands, docking, the gate and the Khador Drive refused). Driven out, the
// pirates come back after FreedSeconds of playing time (not saved: a load besieges it again). Plain C#.

using System.Collections.Generic;

namespace GoF2Remake.Data
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class PirateHold
    {
        /// <summary>How long a station stays free once its pirates were driven out (playing time, s).</summary>
        public const float FreedSeconds = 1800f;

        static readonly Dictionary<int, float> freedAt = new Dictionary<int, float>();

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => freedAt.Clear();

        /// <summary>Its mod marks the station held by pirates (whether or not they were driven out lately).</summary>
        public static bool Marked(int station, int system) => station != KaamoClub.Station && Modding.ModWorld.PirateHeld(station, system);

        /// <summary>Pirates hold the station now: marked, and not driven out within FreedSeconds.</summary>
        public static bool Held(int station, int system) =>
            Marked(station, system) && !(freedAt.TryGetValue(station, out float at) && Session.PlaySeconds - at < FreedSeconds && Session.PlaySeconds >= at);

        /// <summary>The pirates were driven out of the station's orbit.</summary>
        public static void Freed(int station) => freedAt[station] = Session.PlaySeconds;
    }
}
