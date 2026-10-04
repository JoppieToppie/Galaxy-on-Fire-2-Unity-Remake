// NetEventRespawn.cs
// Remake multiplayer: an event's respawn point on this player's game (/respawn, the event graphs' Set Respawn Point;
// NetAdmin.Order.Respawn): while set, a destroyed ship comes back in space after the delay (counted from the destruction,
// at least past the explosion), repaired, in that orbit at the spot (else 3 km in front of the station) moved up to 'spread' game
// units away (NetTeleport.Respawn), instead of the game-over screen's "Tap to respawn at the station". Cleared by
// "/respawn off", the event's end (NetEvents) and the session's end.

using System.Globalization;
using UnityEngine;

namespace GoF2Remake.Multiplayer
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class NetEventRespawn
    {
        static int station = -1;
        static Vector3? spot;
        static float spread, delay = 5f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => station = -1;

        public static bool Active => station != -1 && NetGame.Active;

        /// <summary>The server's order: "off", or "station|x y z|spread|delay" (an empty spot = the launch spot).</summary>
        internal static void Apply(string text)
        {
            var f = (text ?? "").Split('|');
            if (f.Length < 4 || !int.TryParse(f[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int s)) { station = -1; return; }
            spot = null;
            var c = f[1].Split(' ');
            if (c.Length == 3 && float.TryParse(c[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
                && float.TryParse(c[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y)
                && float.TryParse(c[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
                spot = new Vector3(x, y, z);
            float.TryParse(f[2], NumberStyles.Float, CultureInfo.InvariantCulture, out spread);
            if (!float.TryParse(f[3], NumberStyles.Float, CultureInfo.InvariantCulture, out delay)) delay = 5f;
            station = s;
        }

        /// <summary>PlayerHealth's death: time to come back ('deathMs' since the destruction)?</summary>
        public static bool Due(float deathMs) => Active && deathMs >= Mathf.Max(3500f, delay * 1000f);

        public static void Go()
        {
            if (!Active) return;
            NetTeleport.Respawn(station, spot, spread);
        }
    }
}
