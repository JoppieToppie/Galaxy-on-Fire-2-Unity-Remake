// EventNames.cs
// Remake multiplayer: the names an event's commands take (ships, assembled objects, items, hulls, stations), looked up exactly
// as the server's commands do (NetAdmin.FindShip / FindAssembly / FindItem / FindHull, NetTeleport.ParseStation): the event
// compiler warns about a name the server wouldn't know (EventGraphScript), and the Editor's node pickers list them.

using System;
using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.World;

namespace GoF2Remake.Multiplayer
{
    public static class EventNames
    {
        /// <summary>Why a ship (or, for Spawn, an object) isn't known; null when it is.</summary>
        public static string CheckShip(string name, bool objects)
        {
            if (NetAdmin.FindShip(name, out string error) >= 0) return null;
            if (objects && NetAdmin.FindAssembly(name, out _) != null) return null;
            return error;
        }

        /// <summary>An assembled object's name, not a ship's (spawned as scenery: /spawn takes no race, count or behaviour).</summary>
        public static bool IsObject(string name) => NetAdmin.FindShip(name, out _) < 0 && NetAdmin.FindAssembly(name, out _) != null;

        public static string CheckItem(string name)
        {
            NetAdmin.FindItem(name, out string error);
            return error;
        }

        public static string CheckHull(string name) =>
            string.Equals(name, "own", StringComparison.OrdinalIgnoreCase) || NetAdmin.FindHull(name, out string error) != null ? null : error;

        public static string CheckStation(string name) =>
            NetTeleport.ParseStation(name, out _, out string rest) && rest.Length == 0 ? null : $"No station \"{name}\" (a number, a name or void).";

        public static List<string> Ships()
        {
            var db = NetGame.Db;
            var list = new List<string>();
            foreach (var s in db.Ships) list.Add(DebugSpawner.ShipName(db, s.index));
            return list;
        }

        public static List<string> Objects()
        {
            var list = new List<string>();
            foreach (var a in NetGame.Db.Assemblies) list.Add(a.name);
            return list;
        }

        public static List<string> Items()
        {
            var list = new List<string>();
            foreach (var it in NetGame.Db.Items)
            {
                string n = UI.ItemInfo.ItemName(it.index);
                list.Add(string.IsNullOrEmpty(n) ? it.name ?? ("#" + it.index) : n);
            }
            return list;
        }

        public static List<string> Hulls()
        {
            var list = new List<string> { "own" };
            foreach (var h in PlayerHull.All(NetGame.Db)) list.Add(NetAdmin.HullName(h));
            return list;
        }

        /// <summary>"name (system)" labels and the names themselves.</summary>
        public static List<(string label, string name)> Stations()
        {
            var list = new List<(string, string)>();
            foreach (StationData s in NetGame.Db.Stations)
                if (!string.IsNullOrEmpty(s.name)) list.Add(($"{s.name}  ({s.systemName}, {s.index})", s.name));
            list.Add(("Void (the alien orbit)", "void"));
            return list;
        }
    }
}
