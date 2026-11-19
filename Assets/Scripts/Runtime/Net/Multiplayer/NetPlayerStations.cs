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
        /// <summary>The station index assigned in database order; kept stable while the server's mod set stays the same.</summary>
        public int stationIndex = -1;
        public string name;
        public string ownerProfile;
        public string ownerName;
        public int system;
        /// <summary>Existing station index whose look is used for this station's model and collision preset.</summary>
        public int looksLike = -1;
        public long createdUtc;
        /// <summary>True until all construction materials have been deposited.</summary>
        public bool underConstruction;
        public List<PlayerStationMaterial> materials = new List<PlayerStationMaterial>();
        public List<PlayerStationAddon> addons = new List<PlayerStationAddon>();
        public List<PlayerStationListing> listings = new List<PlayerStationListing>();
        public List<PlayerStationStorage> storage = new List<PlayerStationStorage>();
    }

    [Serializable]
    public sealed class PlayerStationMaterial
    {
        public int item;
        public int required;
        public int delivered;
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
    public static partial class NetPlayerStations
    {
        const int MaxStations = 256, MaxAddonsPerStation = 64, MaxListingsPerStation = 512;
        const int MaxStationsPerSystem = 8;
        const string FileName = "player-stations.json";

        [Serializable]
        sealed class Registry
        {
            public int nextId = 1;
            public List<PlayerStationRecord> stations = new List<PlayerStationRecord>();
        }

        static Registry registry;
        static string path;
        static bool sessionActive;

        /// <summary>Current server registry; empty until a persistent world starts.</summary>
        public static IReadOnlyList<PlayerStationRecord> Stations => registry?.stations;

        /// <summary>The current registry for NetState's initial session snapshot.</summary>
        internal static string Snapshot => registry != null ? JsonUtility.ToJson(registry) : "";

        /// <summary>Load station state before the world starts accepting player actions.</summary>
        public static void Start()
        {
            registry = null;
            path = null;
            sessionActive = NetGame.PersistentHost || NetGame.Dedicated;
            if (!sessionActive) return;

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
            if (registry.stations.Count > MaxStations)
                Debug.LogWarning($"NetPlayerStations: saved registry contains {registry.stations.Count} stations (limit {MaxStations}); preserving all records and refusing new construction.");
            int highest = 0;
            foreach (var station in registry.stations)
            {
                if (station == null) continue;
                highest = Math.Max(highest, station.id);
                station.addons ??= new List<PlayerStationAddon>();
                station.materials ??= new List<PlayerStationMaterial>();
                station.listings ??= new List<PlayerStationListing>();
                station.storage ??= new List<PlayerStationStorage>();
                if (station.addons.Count > MaxAddonsPerStation || station.listings.Count > MaxListingsPerStation)
                    Debug.LogWarning($"NetPlayerStations: station {station.id} exceeds a module or listing limit; preserving all saved records.");
            }
            registry.nextId = Math.Max(highest + 1, Math.Max(1, registry.nextId));
        }

        /// <summary>Apply the server's station snapshot on a joining game. Clients keep a read-only copy and never save it.</summary>
        internal static void ReceiveSnapshot(string json)
        {
            sessionActive = true;
            if (string.IsNullOrEmpty(json)) { registry = new Registry(); return; }
            try
            {
                registry = JsonUtility.FromJson<Registry>(json) ?? new Registry();
                registry.stations ??= new List<PlayerStationRecord>();
                if (registry.stations.Count > MaxStations)
                    Debug.LogWarning($"NetPlayerStations: received registry contains {registry.stations.Count} stations (limit {MaxStations}); preserving all records.");
                foreach (var station in registry.stations)
                {
                    if (station == null) continue;
                    station.addons ??= new List<PlayerStationAddon>();
                    station.materials ??= new List<PlayerStationMaterial>();
                    station.listings ??= new List<PlayerStationListing>();
                    if (station.addons.Count > MaxAddonsPerStation || station.listings.Count > MaxListingsPerStation)
                        Debug.LogWarning($"NetPlayerStations: received station {station.id} exceeds a module or listing limit; preserving all records.");
                }
                // The snapshot can change during a live session. Append any newly built stations to this game's
                // database before the player next enters their orbit.
                if (NetGame.Active) Apply(NetGame.Db);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"NetPlayerStations: server snapshot unreadable ({e.Message}).");
                registry = new Registry();
            }
        }

        /// <summary>Find a station by its persistent server-assigned id.</summary>
        public static PlayerStationRecord Find(int id) => registry?.stations.Find(s => s != null && s.id == id);

        internal static string SystemList()
        {
            if (!NetGame.Active) return "Join a multiplayer session first.";
            var systems = NetGame.Db.Systems;
            var lines = new List<string> { "Systems available for station projects (use the number):" };
            foreach (var system in systems)
                if (system != null && system.stations != null && system.stations.Count > 0)
                    lines.Add($"{system.index}: {system.name}");
            return string.Join("\n", lines);
        }

        /// <summary>Show the persistent state and construction progress of one player station.</summary>
        internal static string Status(int id)
        {
            if (!NetGame.Active || registry?.stations == null) return "Join a multiplayer session first.";
            var record = Find(id);
            if (record == null) return $"No player station with project id {id} exists in this world.";

            var system = NetGame.Db?.Systems?.Find(s => s != null && s.index == record.system);
            var lines = new List<string>
            {
                $"Station #{record.id}: {record.name}",
                $"System: {(system != null ? system.name : $"Unknown ({record.system})")}",
                $"Owner: {(string.IsNullOrWhiteSpace(record.ownerName) ? "Unknown" : record.ownerName)}",
                $"Status: {(record.underConstruction ? "Under construction" : "Operational")}",
            };

            if (record.materials != null && record.materials.Count > 0)
            {
                lines.Add("Construction materials:");
                foreach (var material in record.materials)
                {
                    if (material == null) continue;
                    string itemName = NetGame.Db?.Item(material.item)?.name ?? $"Item {material.item}";
                    lines.Add($"  {itemName}: {Math.Max(0, material.delivered)}/{Math.Max(0, material.required)}");
                }
            }
            lines.Add($"Installed modules: {record.addons?.Count ?? 0}");
            lines.Add($"Marketplace listings: {record.listings?.Count ?? 0}");
            return string.Join("\n", lines);
        }

        /// <summary>Station model/collision preset inherited by a player station from its chosen orbital anchor.</summary>
        internal static int LooksLike(int stationIndex)
        {
            if (!sessionActive || registry?.stations == null) return -1;
            foreach (var station in registry.stations)
                if (station != null && station.stationIndex == stationIndex) return station.looksLike;
            return -1;
        }

        internal static bool IsPlayerStation(int stationIndex) =>
            sessionActive && registry?.stations?.Exists(s => s != null && s.stationIndex == stationIndex) == true;

        internal static bool UnderConstruction(int stationIndex) =>
            registry?.stations?.Find(s => s != null && s.stationIndex == stationIndex)?.underConstruction == true;

        /// <summary>Deposit one of the project's required cargo resources from a docked controlled profile.</summary>
        internal static string Contribute(NetPlayer by, int id, int item, int amount)
        {
            if (!NetGame.IsServer || !sessionActive || registry == null) return "Player construction needs a persistent server.";
            if (by == null || !by.IsSpawned || !by.InHangar) return "Dock at any station to deposit construction materials.";
            if (!NetProfiles.Enabled || !NetProfiles.Controls(by.OwnerClientId)) return "Use a controlled player profile to contribute materials.";
            if (amount < 1 || amount > 500 || !NetGuard.Item(item)) return "Deposit between 1 and 500 units of a valid item.";
            var record = Find(id);
            if (record == null || !record.underConstruction) return "That construction project is not active.";
            var resource = record.materials?.Find(m => m != null && m.item == item);
            if (resource == null) return "That item is not required for this station project.";
            int remaining = resource.required - resource.delivered;
            if (amount > remaining) return $"The project needs only {remaining} more units of that material.";
            string account = NetProfiles.AccountOf(by.OwnerClientId);
            if (string.IsNullOrEmpty(account) || !NetProfiles.MoveCargo(by.OwnerClientId, account, item, -amount))
                return "Your saved cargo does not have that many units. Dock again to sync your hold.";

            resource.delivered += amount;
            bool complete = true;
            foreach (var material in record.materials)
                if (material != null && material.delivered < material.required) { complete = false; break; }
            if (complete) record.underConstruction = false;
            if (!Save())
            {
                resource.delivered -= amount;
                if (complete) record.underConstruction = true;
                NetProfiles.MoveCargo(by.OwnerClientId, account, item, amount);
                return "The project could not be saved; your materials were returned.";
            }

            if (!(NetGame.PersistentHost && by.OwnerClientId == NetGame.LocalId))
                NetState.Instance?.SendAdmin(by.OwnerClientId, NetAdmin.Order.Take, item, amount, 0, "", "Station construction");
            string result = $"Deposited {amount} x {NetGame.Db.Item(item).name} into {record.name}.";
            return record.underConstruction ? result : result + " Construction is complete; the station is now open.";
        }

        /// <summary>Reserve a new orbital site and start its long-running, server-owned material project.</summary>
        internal static string StartConstruction(NetPlayer by, int systemIndex, string name)
        {
            if (!NetGame.IsServer || !sessionActive || registry == null)
                return "Player-built stations need a persistent host or dedicated server.";
            if (by == null || !by.IsSpawned)
                return "You must be connected in the game to plan station construction.";
            if (!NetProfiles.Enabled || !NetProfiles.Controls(by.OwnerClientId))
                return "Sign in to a controlled player profile before building a station.";
            name = (name ?? "").Trim();
            if (name.Length < 3 || name.Length > 40 || name.IndexOfAny(new[] { '\\r', '\\n', '\\t', '\\0', '<', '>' }) >= 0)
                return "Choose a station name between 3 and 40 characters.";
            var db = NetGame.Db;
            var system = db?.Systems?.Find(s => s != null && s.index == systemIndex);
            if (system == null || system.stations == null || system.stations.Count == 0)
                return "Choose an existing system with at least one orbital location.";
            if (registry.stations.Count >= MaxStations)
                return "This world has reached its player station limit.";
            int inSystem = 0;
            foreach (var station in registry.stations) if (station != null && station.system == system.index) inSystem++;
            if (inSystem >= MaxStationsPerSystem)
                return "This system has reached its player station limit.";

            string owner = NetProfiles.AccountOf(by.OwnerClientId);
            if (string.IsNullOrEmpty(owner)) return "Your controlled profile has no account identity.";

            int look = system.jumpgateStation >= 0 ? system.jumpgateStation : system.stations[0];
            if (db.Stations.Find(s => s != null && s.index == look) == null) look = system.stations[0];
            int stationIndex = db.Stations.Count;
            foreach (var station in registry.stations)
                if (station != null && station.stationIndex >= stationIndex) stationIndex = station.stationIndex + 1;

            var record = new PlayerStationRecord
            {
                id = registry.nextId++,
                stationIndex = stationIndex,
                name = name,
                ownerProfile = owner,
                ownerName = by.DisplayName,
                system = system.index,
                looksLike = look,
                createdUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                underConstruction = true,
                materials = RequiredMaterials(),
            };
            registry.stations.Add(record);
            if (!Save())
            {
                registry.stations.Remove(record);
                registry.nextId--;
                return "The construction plan could not be saved.";
            }
            Apply(db);
            NetGuard.RefreshStations();
            return $"Construction project {record.id} started for {name} in {system.name}. Dock at any station to deposit materials over time with /station contribute {record.id} <item id> <amount>.";
        }

        static List<PlayerStationMaterial> RequiredMaterials() => new List<PlayerStationMaterial>
        {
            new PlayerStationMaterial { item = 156, required = 200 },  // Iron
            new PlayerStationMaterial { item = 155, required = 100 },  // Titanium
            new PlayerStationMaterial { item = 118, required = 80 },   // Electronics
            new PlayerStationMaterial { item = 127, required = 50 },   // Microchips
        };

        /// <summary>End the replicated session view; saved server data is reloaded next time.</summary>
        internal static void EndSession()
        {
            registry = null;
            path = null;
            sessionActive = false;
        }

        /// <summary>Add saved player stations to a freshly loaded game database, after mods have added their stations.</summary>
        internal static void Apply(Database db)
        {
            if (!sessionActive || registry?.stations == null || db?.Stations == null || db.Systems == null) return;
            foreach (var record in registry.stations)
            {
                if (record == null || record.stationIndex < 0) continue;
                // Station indices also index several legacy tables. Append contiguously so the original index/count
                // invariant remains true, and refuse a stale or colliding record rather than corrupting another station.
                if (record.stationIndex != db.Stations.Count)
                {
                    if (db.Stations.Find(s => s.index == record.stationIndex) is StationData existing && existing.name == record.name) continue;
                    Debug.LogError($"NetPlayerStations: station {record.id} expected index {record.stationIndex}, but the database ends at {db.Stations.Count}; check that the server and client use the same mods.");
                    continue;
                }
                var system = db.Systems.Find(s => s != null && s.index == record.system);
                if (system == null || system.stations == null)
                {
                    Debug.LogError($"NetPlayerStations: station {record.id} has an invalid system; it was not loaded.");
                    continue;
                }
                var look = db.Stations.Find(s => s != null && s.index == record.looksLike);
                if (look == null)
                {
                    int fallback = system.jumpgateStation >= 0 ? system.jumpgateStation : system.stations[0];
                    look = db.Stations.Find(s => s != null && s.index == fallback);
                }
                if (look == null)
                {
                    Debug.LogError($"NetPlayerStations: station {record.id} has no valid model preset; it was not loaded.");
                    continue;
                }
                if (record.looksLike < 0) record.looksLike = look.index;
                int count = 0;
                foreach (var station in registry.stations) if (station != null && station.system == record.system) count++;
                if (count > MaxStationsPerSystem)
                {
                    Debug.LogError($"NetPlayerStations: system {record.system} exceeds the player station limit.");
                    continue;
                }
                db.Stations.Add(new StationData
                {
                    index = record.stationIndex,
                    name = string.IsNullOrWhiteSpace(record.name) ? $"{record.ownerName}'s Station" : record.name,
                    system = record.system,
                    systemName = system.name,
                    techLevel = look.techLevel,
                    textureIndex = look.textureIndex,
                });
                if (!system.stations.Contains(record.stationIndex)) system.stations.Add(record.stationIndex);
            }
            NetGuard.RefreshStations();
        }

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
