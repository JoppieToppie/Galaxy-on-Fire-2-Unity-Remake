// NetEventWaypoint.cs
// Remake multiplayer: an event's waypoint on this player's game (/waypoint, the event graphs' Set Waypoint;
// NetAdmin.Order.Waypoint): in that station's orbit the navigation shows a lockable "Waypoint" (548) there, the autopilot
// flies to it (Navigation.SetRoute with a one-point Route), and reaching it says "Last waypoint reached." (544) and clears
// it. Applied again whenever this player enters that orbit; cleared by "/waypoint off", the event's end and the session's.

using GoF2Remake.Flight;
using GoF2Remake.World;
using UnityEngine;

namespace GoF2Remake.Multiplayer
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public sealed class NetEventWaypoint : MonoBehaviour
    {
        static NetEventWaypoint instance;
        int station = -1;
        Vector3 point;          // game units
        Route route;
        SpaceLevel appliedTo;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => instance = null;

        /// <summary>The server's order: "station x y z" (game units), or "off".</summary>
        internal static void Apply(string text)
        {
            if (instance == null)
            {
                var go = new GameObject("NetEventWaypoint");
                DontDestroyOnLoad(go);
                instance = go.AddComponent<NetEventWaypoint>();
            }
            var f = (text ?? "").Split(' ');
            if (f.Length == 4 && int.TryParse(f[0], out int s)
                && float.TryParse(f[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float x)
                && float.TryParse(f[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float y)
                && float.TryParse(f[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float z))
            {
                instance.station = s;
                instance.point = new Vector3(x, y, z);
            }
            else instance.station = -1;
            instance.Clear();
        }

        void Clear()
        {
            if (appliedTo != null && appliedTo.Navigation != null) appliedTo.Navigation.SetRoute(null);
            appliedTo = null;
            route = null;
        }

        void Update()
        {
            if (!NetGame.Active) { station = -1; if (appliedTo != null) Clear(); return; }
            if (station < 0) return;
            var level = appliedTo != null ? appliedTo : FindAnyObjectByType<SpaceLevel>();
            if (level == null || level.Navigation == null || level.Player == null || level.Layout == null) { appliedTo = null; return; }
            if (level.Layout.stationIndex != station) return;
            if (appliedTo != level)
            {
                route = new Route(false);
                route.points.Add(point);
                level.Navigation.SetRoute(route);
                appliedTo = level;
            }
            // Route::update: reaching it advances the route past its one point (544, the marker goes).
            var p = level.Player.transform.position / OrbitLayout.MetersPerUnit;
            route.Update(new Vector3(p.x, p.y, -p.z));
            if (route.Waypoint == null) station = -1;
        }
    }
}
