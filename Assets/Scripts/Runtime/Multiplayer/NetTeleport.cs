// NetTeleport.cs
// Admin teleports (/tp, /tphere, the dedicated server console's tp): players (names, client ids, @a / @s / @p / @r) are sent to another player (beside their
// ship in their orbit, or into the hangar they are docked in), into an orbit (at the launch spot, or at game coordinates
// about the station: /pos shows a player's own), or into a station's hangar ("dock"). Destinations:
//   <player>                  where that player is now (a name, a client id, or @s / @p / @r)
//   <station> [x y z | dock]  a station by index, by name or "void" (the Void's orbit); coordinates in game units
// The server checks the rights (NetState.TeleportRpc) and tells that player's game, which moves its own ship (Go): within
// the same orbit in place, else by loading the orbit / the station like a jump (Session.StationIndex), the pose taken by
// SpaceLevel.SpawnPlayer (TakePose), no launch camera.

using System;
using System.Collections.Generic;
using System.Globalization;
using GoF2Remake.Data;
using GoF2Remake.World;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GoF2Remake.Multiplayer
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class NetTeleport
    {
        public enum Kind : byte { Player = 0, Orbit = 1, Hangar = 2 }

        public struct Destination
        {
            public Kind kind;
            public ulong player;    // Kind.Player: the client id
            public int station;     // Kind.Orbit / Hangar
            public bool hasPos;     // Kind.Orbit: at 'pos' (game units) instead of the launch spot
            public Vector3 pos;
        }

        const float M = OrbitLayout.MetersPerUnit;
        // Beside another player's ship: 150 m behind it, 40 m to its right, facing their way.
        const float BehindMeters = 150f, SideMeters = 40f;

        static bool pending;
        static Vector3 pendingPos;
        static Quaternion pendingRot;

        static string X(string key, string english) => Localization.Extra(key, english);

        public static string Usage => "[players] <player | station [x y z | dock]>";

        /// <summary>SpaceLevel.SpawnPlayer: the pose a teleport into this orbit asked for (once).</summary>
        public static bool TakePose(out Vector3 position, out Quaternion rotation)
        {
            position = pendingPos;
            rotation = pendingRot;
            bool had = pending;
            pending = false;
            return had;
        }

        // ---- the command (server: NetCommands.RunOnServer, for the chat and the console alike) ----------------

        /// <summary>tp / tphere on the server. tp: "[players] destination" (a chat player alone = themselves; the console
        /// always names the players); tphere: the players come to the issuer (a chat player). Players: names, client ids or
        /// selectors (@a, @s, @p, @r: NetCommands.FindTargets); a destination player is one (@s, @p, @r or a name). Returns
        /// the issuer's answer.</summary>
        public static string Command(string args, NetPlayer issuer, bool here)
        {
            Destination d;
            List<NetPlayer> who;
            string error = null;
            if (here)
            {
                if (issuer == null) return X("mpTpHereConsole", "tphere needs a player to bring them to: tp <players> <player> instead.");
                who = NetCommands.FindTargets(args, issuer, out _, out error);
                if (who.Count == 0) return error;
                d = new Destination { kind = Kind.Player, player = issuer.OwnerClientId };
            }
            else if (issuer != null && Parse(args, issuer, out d, out error))
                who = new List<NetPlayer> { issuer };
            else
            {
                who = NetCommands.FindTargets(args, issuer, out string rest, out string error1);
                if (who.Count == 0 || rest.Length == 0) return (error ?? error1) + " tp " + Usage;
                if (!Parse(rest, issuer, out d, out string error2)) return error2 + " tp " + Usage;
            }
            var by = NetCommands.IssuerName(issuer);
            var answers = new List<string>();
            foreach (var p in who)
            {
                if (who.Count > 1 && d.kind == Kind.Player && p.OwnerClientId == d.player) continue;   // @a to one of them
                string a = Send(p, d, by, issuer);
                if (!string.IsNullOrEmpty(a)) answers.Add(a);
            }
            return string.Join("\n", answers);
        }

        /// <summary>Server: checks and sends the order to the player's game; the issuer's answer.</summary>
        static string Send(NetPlayer who, Destination d, string by, NetPlayer issuer)
        {
            if (d.kind == Kind.Player)
            {
                var t = NetSquad.Find(d.player);
                if (t == null) return X("mpWhisperNobody", "That player isn't in the session.");
                if (t == who) return X("mpTpSelf", "A player can't be teleported to themselves.");
                if (t.Where == NetPlayer.Place.None) return string.Format(X("mpTpTargetLoading", "{0} is loading, try again."), t.DisplayName);
            }
            if (who.Where == NetPlayer.Place.None) return string.Format(X("mpTpTargetLoading", "{0} is loading, try again."), who.DisplayName);
            NetState.Instance.SendTeleport(who.OwnerClientId, d, by);
            Debug.Log($"Server: {by} teleported {who.DisplayName} ({who.OwnerClientId}) to {Describe(d)}");
            return who == issuer ? "" : string.Format(X("mpTpSent", "Teleporting {0} to {1}."), who.DisplayName, Describe(d));
        }

        // ---- parsing ----------------------------------------------------------------------------------------

        /// <summary>A destination: one player (a whole name, a client id, or @s / @p / @r for 'issuer'), else a station
        /// (index, name or "void") with optional coordinates or "dock". 'error' says what didn't parse.</summary>
        public static bool Parse(string args, NetPlayer issuer, out Destination d, out string error)
        {
            d = default;
            error = null;
            args = (args ?? "").Trim();
            string rest;
            if (args.StartsWith("@"))
            {
                var t = NetCommands.FindTarget(args, issuer, out rest, out error);
                if (t == null) return false;
                if (rest.Length > 0) { error = X("mpTpOneDestination", "Only one destination."); return false; }
                d.kind = Kind.Player;
                d.player = t.OwnerClientId;
                return true;
            }
            var p = NetCommands.MatchPlayer(args, out rest);
            if (p != null && rest.Length == 0)
            {
                d.kind = Kind.Player;
                d.player = p.OwnerClientId;
                return true;
            }
            if (!ParseStation(args, out d.station, out rest))
            {
                error = string.Format(X("mpTpNoDestination", "No player or station \"{0}\"."), args);
                return false;
            }
            d.kind = Kind.Orbit;
            var tokens = rest.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0) return true;
            if (tokens.Length == 1 && (tokens[0].Equals("dock", StringComparison.OrdinalIgnoreCase) || tokens[0].Equals("hangar", StringComparison.OrdinalIgnoreCase)))
            {
                if (d.station == Session.VoidOrbit) { error = X("mpTpNoVoidHangar", "The Void has no hangar."); return false; }
                d.kind = Kind.Hangar;
                return true;
            }
            if (tokens.Length == 3 && Num(tokens[0], out float x) && Num(tokens[1], out float y) && Num(tokens[2], out float z))
            {
                d.hasPos = true;
                d.pos = new Vector3(x, y, z);
                return true;
            }
            error = X("mpTpBadRest", "After the station: x y z (game units, /pos shows yours) or \"dock\".");
            return false;
        }

        static bool Num(string s, out float v) =>
            float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) && !float.IsNaN(v) && !float.IsInfinity(v) && Mathf.Abs(v) < 1e7f;

        /// <summary>"void", a station index, or the longest station name 'args' starts with (whole words, any case).</summary>
        internal static bool ParseStation(string args, out int station, out string rest)
        {
            station = 0;
            rest = "";
            int space = args.IndexOf(' ');
            string first = space < 0 ? args : args.Substring(0, space);
            string after = space < 0 ? "" : args.Substring(space + 1).Trim();
            if (first.Equals("void", StringComparison.OrdinalIgnoreCase)) { station = Session.VoidOrbit; rest = after; return true; }
            if (int.TryParse(first, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n))
            {
                if (!NetGame.Db.Stations.Exists(s => s.index == n)) return false;
                station = n;
                rest = after;
                return true;
            }
            StationData best = null;
            foreach (var s in NetGame.Db.Stations)
            {
                string name = s.name;
                if (string.IsNullOrEmpty(name) || !args.StartsWith(name, StringComparison.OrdinalIgnoreCase)) continue;
                if (args.Length > name.Length && args[name.Length] != ' ') continue;
                if (best == null || name.Length > best.name.Length) best = s;
            }
            if (best == null) return false;
            station = best.index;
            rest = args.Substring(best.name.Length).Trim();
            return true;
        }

        /// <summary>The destination in words, for the notices and the server log.</summary>
        public static string Describe(Destination d)
        {
            string station = DedicatedServer.StationName(d.station);
            switch (d.kind)
            {
                case Kind.Player:
                    var p = NetSquad.Find(d.player);
                    return p != null ? p.DisplayName : X("mpTpAPlayer", "a player");
                case Kind.Hangar:
                    return string.Format(X("mpTpHangarOf", "the hangar of {0}"), station);
                default:
                    return d.hasPos
                        ? string.Format(CultureInfo.InvariantCulture, X("mpTpCoordsAt", "{0} ({1:0}, {2:0}, {3:0})"), station, d.pos.x, d.pos.y, d.pos.z)
                        : string.Format(X("mpTpOrbitOf", "the orbit of {0}"), station);
            }
        }

        /// <summary>/pos: where the local player is, with the game coordinates /tp takes.</summary>
        public static string Position()
        {
            var level = UnityEngine.Object.FindAnyObjectByType<SpaceLevel>();
            if (level != null && level.Player != null && level.Layout != null)
            {
                var u = level.Player.transform.position / M;
                return string.Format(CultureInfo.InvariantCulture, X("mpPosSpace", "In space at {0} ({1}): {2:0} {3:0} {4:0}"),
                    DedicatedServer.StationName(level.Layout.stationIndex), level.Layout.stationIndex, u.x, u.y, -u.z);
            }
            var dock = UnityEngine.Object.FindAnyObjectByType<StationLevel>();
            if (dock != null && dock.Layout != null)
                return string.Format(X("mpPosDocked", "Docked at {0} ({1})."), DedicatedServer.StationName(dock.Layout.stationIndex), dock.Layout.stationIndex);
            return X("mpPosNowhere", "Not in an orbit or a station right now.");
        }

        // ---- going there (the teleported player's game) -------------------------------------------------------

        /// <summary>The server's order (NetState.TeleportToRpc): this game's player goes to 'd'. 'by' = the admin.</summary>
        public static void Go(Destination d, string by)
        {
            var level = UnityEngine.Object.FindAnyObjectByType<SpaceLevel>();
            var dock = level == null ? UnityEngine.Object.FindAnyObjectByType<StationLevel>() : null;
            if ((level == null || level.Layout == null) && (dock == null || dock.Layout == null)) { Refused(X("mpTpLoading", "the game is loading")); return; }
            int here = level != null ? level.Layout.stationIndex : dock.Layout.stationIndex;
            string where = Describe(d);

            bool hasPose = false;
            Vector3 pose = default;
            Quaternion rot = Quaternion.identity;
            if (d.kind == Kind.Player)
            {
                var p = NetSquad.Find(d.player);
                if (p == null || p.Where == NetPlayer.Place.None) { Refused(X("mpTpTargetGone", "that player isn't in an orbit or a station")); return; }
                d.station = p.Station;
                if (p.InHangar) d.kind = Kind.Hangar;
                else
                {
                    d.kind = Kind.Orbit;
                    rot = p.Rotation;
                    pose = p.Position - rot * Vector3.forward * BehindMeters + rot * Vector3.right * SideMeters;
                    hasPose = true;
                }
            }
            else if (d.kind == Kind.Orbit && d.hasPos)
            {
                pose = OrbitLayout.ToUnity(d.pos);
                // Facing the station (a spot at the station itself: the launch heading, game -z).
                rot = pose.sqrMagnitude > 1f ? Quaternion.LookRotation(-pose.normalized, Vector3.up) : Quaternion.identity;
                hasPose = true;
            }

            if (d.kind == Kind.Hangar)
            {
                if (dock != null && here == d.station) { Arrived(by, where); return; }
                if (level != null)
                {
                    if (!level.TeleportOut(d.station, true)) { Refused(X("mpTpBusy", "the ship can't leave right now")); return; }
                }
                else
                {
                    Session.StationIndex = d.station;
                    Session.DockedFromSpace = false;
                    Session.LaunchedFromStation = false;
                    SceneManager.LoadScene(dock.gameObject.scene.name);
                }
                Arrived(by, where);
                return;
            }

            // An orbit: in place when already there, else the orbit loads with the pose.
            if (level != null && here == d.station)
            {
                if (!hasPose) { pose = OrbitLayout.ToUnity(OrbitLayout.UndockPosition); rot = level.Player != null ? level.Player.transform.rotation : Quaternion.identity; }
                if (level.MoveForTeleport(pose, rot)) { Arrived(by, where); return; }
            }
            pending = hasPose;
            pendingPos = pose;
            pendingRot = rot;
            if (level != null)
            {
                if (!level.TeleportOut(d.station, false)) { pending = false; Refused(X("mpTpBusy", "the ship can't leave right now")); return; }
            }
            else
            {
                Session.PreviousStationIndex = Session.StationIndex;
                if (d.station == Session.VoidOrbit && Session.StationIndex != Session.VoidOrbit) Session.VoidReturnStation = Session.StationIndex;
                Session.StationIndex = d.station;
                Session.ArrivedByTravel = false;
                Session.LaunchedFromStation = false;
                Session.ProgrammedStation = -1;
                SceneManager.LoadScene(dock.spaceScene);
            }
            Arrived(by, where);
        }

        /// <summary>An event's respawn (NetEventRespawn): the ship repaired and the orbit loaded with it at 'gamePos' (null:
        /// RespawnSpot, 3 km in front of the station: the launch spot is within the bigger stations' hulls) moved up to
        /// 'spread' game units away, facing the station; also while destroyed.</summary>
        public static void Respawn(int station, Vector3? gamePos, float spread)
        {
            Cheats.Repair();   // the ship comes back whole (the next level reads the Session's hull, shield, armor)
            var spot = gamePos ?? RespawnSpot;
            spot += UnityEngine.Random.insideUnitSphere * spread;
            pendingPos = OrbitLayout.ToUnity(spot);
            pendingRot = pendingPos.sqrMagnitude > 1f ? Quaternion.LookRotation(-pendingPos.normalized, Vector3.up) : Quaternion.identity;
            pending = true;
            if (station == Session.VoidOrbit && Session.StationIndex != Session.VoidOrbit) Session.VoidReturnStation = Session.StationIndex;
            Session.PreviousStationIndex = Session.StationIndex;
            Session.StationIndex = station;
            Session.ArrivedByTravel = false;
            Session.LaunchedFromStation = false;
            Session.DockedFromSpace = false;
            Session.ProgrammedStation = -1;
            SceneManager.LoadScene(SpaceScene);
        }

        const string SpaceScene = "Space";

        /// <summary>The default respawn spot (game units): in front of the station on the launch side, clear of its hull.</summary>
        public static readonly Vector3 RespawnSpot = new Vector3(0f, 0f, 60000f);

        static void Arrived(string by, string where)
        {
            NetChat.Notice(string.IsNullOrEmpty(by) || (NetPlayer.Local != null && by == NetPlayer.Local.DisplayName)
                ? string.Format(X("mpTpDone", "Teleported to {0}."), where)
                : string.Format(X("mpTpDoneBy", "{0} teleported you to {1}."), by, where));
        }

        static void Refused(string why) => NetChat.Notice(string.Format(X("mpTpRefused", "Teleport refused: {0}."), why));
    }
}
