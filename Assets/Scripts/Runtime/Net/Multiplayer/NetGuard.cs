// NetGuard.cs
// Multiplayer, the server: the checks on what a client sends (NetState's server RPCs, the hit / shot relays of NetPlayer
// and NetProxy, NetCrate's claims). The server doesn't run the players' games: where a player is (NetPlayer's station and
// place), their ship's pose, hull and credits are their own game's word. What it does check: the sender exists and acts
// where it says it is (a hit on a ship in its own orbit, a trade at the station it is docked at), and every number, index
// and text is one an honest game sends (finite and in range, an item / ship / station that exists, a bounded length), so
// a modified client can't reach into other orbits, corrupt the others' games with NaNs or bad indices, or grow the
// server's tables without end.

using System.Collections.Generic;
using GoF2Remake.Data;
using UnityEngine;

namespace GoF2Remake.Multiplayer
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class NetGuard
    {
        /// <summary>The farthest a position may be from its orbit's origin per axis (Unity metres): the orbits span ~10 km,
        /// story flights and the Void's arrival ~25 km; anything beyond is no position a game sends.</summary>
        public const float MaxCoordinate = 1e6f;
        /// <summary>A shot's or a mine's life left (ms): rockets 10 s + 2 s coasting, mines drifting; well above all.</summary>
        public const float MaxLifetimeMs = 600000f;
        /// <summary>The longest mission JSON (FreelanceMission) and hangar snapshot a game sends.</summary>
        public const int MaxMissionJson = 4096, MaxSnapshot = 4096;
        /// <summary>A server command's name and arguments (the chat line is 160 characters).</summary>
        public const int MaxCommandName = 32, MaxCommandArgs = 512;

        /// <summary>The biggest single hit a game sends: a nuke's or a boss gun's is far below; the session's debug menu (one-hit
        /// kills, 99 999 999) only where the session allows it.</summary>
        public static float MaxDamage => NetGame.DebugAllowed ? 1e8f : 1e6f;

        static HashSet<int> stations;

        public static bool Finite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);
        public static bool Finite(Vector3 v) => Finite(v.x) && Finite(v.y) && Finite(v.z);

        /// <summary>A position in an orbit (Unity metres): finite and within MaxCoordinate per axis.</summary>
        public static bool Position(Vector3 v) =>
            Finite(v) && Mathf.Abs(v.x) <= MaxCoordinate && Mathf.Abs(v.y) <= MaxCoordinate && Mathf.Abs(v.z) <= MaxCoordinate;

        public static bool Rotation(Quaternion q) => Finite(q.x) && Finite(q.y) && Finite(q.z) && Finite(q.w);

        /// <summary>A hit's damage: finite, positive, at most MaxDamage.</summary>
        public static bool Damage(float amount) => Finite(amount) && amount > 0f && amount <= MaxDamage;

        /// <summary>A station that exists (stations.json).</summary>
        public static bool Station(int station)
        {
            if (stations == null)
            {
                stations = new HashSet<int>();
                foreach (var s in NetGame.Db.Stations) stations.Add(s.index);
            }
            return stations.Contains(station);
        }

        /// <summary>An orbit: a station's, or the Void's (Session.VoidOrbit).</summary>
        public static bool Orbit(int station) => station == Session.VoidOrbit || Station(station);

        public static bool Item(int item) => item >= 0 && NetGame.Db.Item(item) != null;
        public static bool Ship(int ship) => ship >= 0 && NetGame.Db.Ship(ship) != null;

        /// <summary>'client' is a player of the session flying in 'station's orbit.</summary>
        public static bool InOrbit(ulong client, int station) => NetOrbit.InOrbit(client, station);

        /// <summary>'p' and 'q' fly in the same orbit.</summary>
        public static bool SameOrbit(NetPlayer p, NetPlayer q) => p != null && q != null && p.InSpace && q.InSpace && p.Station == q.Station;

        /// <summary>'client' is a player of the session docked at 'station' (taking off included).</summary>
        public static bool DockedAt(ulong client, int station)
        {
            var p = NetSquad.Find(client);
            return p != null && p.InHangar && p.Station == station;
        }

        /// <summary>A shot as NetShotSender sends it: a weapon item, finite values in range.</summary>
        public static bool Shot(int item, Vector3 position, Vector3 velocity, Vector3 up, float lifetimeMs, float homingDelayMs) =>
            Item(item) && Position(position) && Finite(velocity) && velocity.sqrMagnitude < 1e12f && Finite(up)
            && Finite(lifetimeMs) && lifetimeMs >= -5000f && lifetimeMs <= MaxLifetimeMs   // a coasting rocket's timer runs below 0
            && Finite(homingDelayMs) && homingDelayMs >= 0f && homingDelayMs <= MaxLifetimeMs;
    }
}
