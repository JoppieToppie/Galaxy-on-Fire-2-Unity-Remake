// NetPlayerStations.cs
// The durable server-side registry for stations players establish in persistent multiplayer worlds. Runtime station spawning,
// construction and marketplace transactions build on these records; the server remains the authority for ownership, location,
// installed add-ons and listings. Direct pilot-to-pilot trade is already handled by NetTrade.

using System;
using System.Collections.Generic;
using System.IO;
using GoF2Remake.Data;
using UnityEngine;

namespace GoF2Remake.Multiplayer
{
    [Serializable]
    public sealed class PlayerStationRecord
    {
        public int id;
        public string name;
        public string ownerProfile;
        public string ownerName;
        public int system;
        public int orbitStation;
        public long createdUtc;
        public List<PlayerStationAddon> addons = new List<PlayerStationAddon>();
        public List<PlayerStationListing> listings = new List<PlayerStationListing>();
    }

    [Serializable]
    public sealed class PlayerStationAddon
    {
        public string kind;
        public int level;
    }

    [Serializable]
    public sealed class PlayerStationListing
    {
        public int item;
        public int amount;
        public long unitPrice;
        public string sellerProfile;
    }

    /// <summary>Server-owned, durable state for player-built stations in persistent multiplayer worlds.</summary>
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class NetPlayerStations
    {
        const int MaxStations = 256, MaxAddonsPerStation = 64, MaxListingsPerStation = 512;
        const string FileName = "player-stations.json";

        [Serializable]
        sealed class Registry
        {
            public int nextId = 1;
            public List<PlayerStationRecord> stations = new List<PlayerStationRecord>();
        }

        static Registry registry;
        static string path;

        /// <summary>Current server registry; empty until a persistent world starts.</summary>
        public static IReadOnlyList<PlayerStationRecord> Stations => registry?.stations;

        /// <summary>The current registry for NetState's initial session snapshot.</summary>
        internal static string Snapshot => registry != null ? JsonUtility.ToJson(registry) : "";

        /// <summary>Load station state before the world starts accepting player actions.</summary>
        public static void Start()
        {
            registry = null;
            path = null;
            if (!NetGame.PersistentHost && !NetGame.Dedicated) return;

            string folder = NetProfiles.Folder;
            if (string.IsNullOrEmpty(folder)) folder = NetGame.HostedWorldFolder;
            path = Path.Combine(folder, FileName);
            try
            {
                Directory.CreateDirectory(folder);
                if (File.Exists(path)) registry = JsonUtility.FromJson<Registry>(File.ReadAllText(path));
            }
            catch (Exception e)
            {
                Debug.LogError($"NetPlayerStations: {FileName} unreadable ({e.Message}); starting an empty registry.");
            }

            registry ??= new Registry();
            registry.stations ??= new List<PlayerStationRecord>();
            if (registry.stations.Count > MaxStations) registry.stations.RemoveRange(MaxStations, registry.stations.Count - MaxStations);
            int highest = 0;
            foreach (var station in registry.stations)
            {
                if (station == null) continue;
                highest = Math.Max(highest, station.id);
                station.addons ??= new List<PlayerStationAddon>();
                station.listings ??= new List<PlayerStationListing>();
                if (station.addons.Count > MaxAddonsPerStation) station.addons.RemoveRange(MaxAddonsPerStation, station.addons.Count - MaxAddonsPerStation);
                if (station.listings.Count > MaxListingsPerStation) station.listings.RemoveRange(MaxListingsPerStation, station.listings.Count - MaxListingsPerStation);
            }
            registry.nextId = Math.Max(highest + 1, Math.Max(1, registry.nextId));
        }

        /// <summary>Apply the server's station snapshot on a joining game. Clients keep a read-only copy and never save it.</summary>
        internal static void ReceiveSnapshot(string json)
        {
            if (string.IsNullOrEmpty(json)) { registry = new Registry(); return; }
            try
            {
                registry = JsonUtility.FromJson<Registry>(json) ?? new Registry();
                registry.stations ??= new List<PlayerStationRecord>();
                if (registry.stations.Count > MaxStations) registry.stations.RemoveRange(MaxStations, registry.stations.Count - MaxStations);
                foreach (var station in registry.stations)
                {
                    if (station == null) continue;
                    station.addons ??= new List<PlayerStationAddon>();
                    station.listings ??= new List<PlayerStationListing>();
                    if (station.addons.Count > MaxAddonsPerStation) station.addons.RemoveRange(MaxAddonsPerStation, station.addons.Count - MaxAddonsPerStation);
                    if (station.listings.Count > MaxListingsPerStation) station.listings.RemoveRange(MaxListingsPerStation, station.listings.Count - MaxListingsPerStation);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"NetPlayerStations: server snapshot unreadable ({e.Message}).");
                registry = new Registry();
            }
        }

        /// <summary>Find a station by its persistent server-assigned id.</summary>
        public static PlayerStationRecord Find(int id) => registry?.stations.Find(s => s != null && s.id == id);

        /// <summary>Persist a server-side change atomically, retaining the prior registry as a backup.</summary>
        public static bool Save()
        {
            if (registry == null || string.IsNullOrEmpty(path)) return false;
            if (!NetProfiles.Write(path, JsonUtility.ToJson(registry, true))) return false;
            NetState.Instance?.PublishPlayerStations(Snapshot);
            return true;
        }
    }
}


