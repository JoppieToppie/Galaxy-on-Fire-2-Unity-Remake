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
    public enum PlayerStationOwnershipType { Individual = 0, Faction = 1 }

    public enum PlayerJumpgateAccessMode { Everyone = 0, OwningFaction = 1, Blacklist = 2, OwnerOnly = 3 }
    public enum PlayerJumpgateBlockType { Profile = 0, Faction = 1 }

    [Serializable]
    public sealed class PlayerJumpgateBlockEntry
    {
        public PlayerJumpgateBlockType type;
        public string id;
    }

    [Serializable]
    public sealed class PlayerFactoryProductionJob
    {
        public int recipeId, batches, repeatOrderId = -1;
        public string operatorProfile;
        public long startedUtc, completesUtc;
        public List<PlayerStationMaterial> reservedInputs = new List<PlayerStationMaterial>();
        public int outputItem, outputAmount;
    }

    [Serializable]
    public sealed class PlayerFactoryRepeatOrder
    {
        public int id, recipeId;
        public bool continuous;
        public int remainingBatches;
        public int batchesPerCycle = 1;
        public string createdByProfile;
        public long createdUtc;
        public bool paused;
    }

    [Serializable]
    public sealed class PlayerJumpgateRecord
    {
        // The database station index is the stable orbital anchor and unique gate identifier.
        public int orbitStationIndex = -1;
        // Ownership follows the player station that authorised construction, not the orbital anchor.
        // -1 denotes a legacy gate whose sponsor could not be identified unambiguously.
        public int sponsorStationId = -1;
        // Zero preserves the original orbit model on older saves.
        public int modelPreset;
        public string modelReference;
        public bool scrapIntegrityInitialized;
        public int system;
        public int destinationSystem = -1;
        public PlayerStationOwnershipType ownershipType = PlayerStationOwnershipType.Individual;
        public string ownerProfile, ownerFactionId, ownerName;
        // Sponsor is retained for legacy construction provenance only. Ownership and recovery are gate-local.
        public int constructionStationId = -1;
        public string constructionIssuerProfile;
        public string accessFactionId;
        public PlayerJumpgateAccessMode accessMode = PlayerJumpgateAccessMode.Everyone;
        public List<PlayerJumpgateBlockEntry> blacklist = new List<PlayerJumpgateBlockEntry>();
        public bool building;
        // First destruction moves the gate into recoverable scrap. The second destroys it permanently
        // and drops 25% of the original build materials; subsequent rebuilding requires a station order.
        public bool scrapped, permanentlyDestroyed;
        public int destructionCount;
        public long startedUtc, completesUtc, destinationChangedUtc;
        public int buildSeconds;
        public List<PlayerStationMaterial> materials = new List<PlayerStationMaterial>();
        public List<PlayerStationContribution> contributions = new List<PlayerStationContribution>();
        // Uncollected final-destruction salvage (25% of delivered build materials, rounded up per item).
        public List<PlayerStationMaterial> salvageMaterials = new List<PlayerStationMaterial>();
        public bool salvageDropPending;
        // Durable combat state. Zero integrity on legacy records is migrated on the server.
        public float structuralIntegrity;
        public long lastStructuralDamageUtc, structuralIntegrityUpdatedUtc, empShutdownUntilUtc;
        public bool combatStateInitialized;
        public bool explosionPending;

        internal PlayerJumpgateRecord ToPublicRecord()
        {
            var visible = (PlayerJumpgateRecord)MemberwiseClone();
            visible.ownerProfile = null;
            visible.ownerFactionId = null;
            visible.accessFactionId = null;
            visible.ownerName = null;
            visible.constructionIssuerProfile = null;
            visible.blacklist = null;
            visible.contributions = null;
            return visible;
        }
    }

    [Serializable]
    public sealed class PlayerStationRecord
    {
        public int id;
        /// <summary>The station index assigned in database order; kept stable while the server's mod set stays the same.</summary>
        public int stationIndex = -1;
        // Legacy projects used appended database indices. New projects occupy real vacant orbits.
        public bool existingOrbit;
        public string name;
        // OwnershipType defaults to Individual for old JSON saves and remains backward compatible.
        public PlayerStationOwnershipType ownershipType = PlayerStationOwnershipType.Individual;
        public string ownerProfile;
        public string ownerFactionId;
        public string ownerName;
        // The first account to establish this station remains its builder identity. Rights are
        // relinquished only by a voluntary builder transfer or destruction through combat.
        public string originalBuilderProfile;
        public long builderRightsRelinquishedUtc;
        public string individualSuccessorProfile;
        public string factionSuccessorProfile;
        public string factionSuccessorFactionId;
        public PlayerStationOwnershipType factionSuccessorType = PlayerStationOwnershipType.Individual;
        public string factionSuccessorDelegateProfile;
        public PlayerStationFactionPermissionProfile factionPermissions = new PlayerStationFactionPermissionProfile();
        public int governanceVersion;
        public List<PlayerStationPermissionOverride> permissionOverrides = new List<PlayerStationPermissionOverride>();
        public List<PlayerStationRoleAssignment> roleAssignments = new List<PlayerStationRoleAssignment>();
        public List<PlayerStationBan> bans = new List<PlayerStationBan>();
        public List<PlayerStationHistoryEntry> history = new List<PlayerStationHistoryEntry>();
        public int nextHistorySequence = 1;
        public int system;
        /// <summary>Existing station index whose look is used for this station's model and collision preset.</summary>
        public int looksLike = -1;
        public string stationModelReference;
        public long createdUtc;
        /// <summary>True until all construction materials have been deposited.</summary>
        public bool underConstruction;
        public List<PlayerStationMaterial> materials = new List<PlayerStationMaterial>();
        public string moduleConstructionKind;
        public List<PlayerStationMaterial> moduleConstructionMaterials;
        public List<PlayerStationAddon> addons = new List<PlayerStationAddon>();
        public List<PlayerStationListing> listings = new List<PlayerStationListing>();
        public int nextItemListingId = 1;
        public List<PlayerStationShipListing> shipListings = new List<PlayerStationShipListing>();
        public int nextShipListingId = 1;
        public List<PlayerStationBuyOrder> buyOrders = new List<PlayerStationBuyOrder>();
        public List<PlayerStationStorage> storage = new List<PlayerStationStorage>();
        public List<PlayerStationStorage> donatedConstructionStock = new List<PlayerStationStorage>();
        public List<PlayerStationStorageLot> storageLots = new List<PlayerStationStorageLot>();
        // Persistent factory jobs and owner-controlled repeat orders. A factory remains one general-purpose module.
        public List<PlayerFactoryProductionJob> factoryJobs = new List<PlayerFactoryProductionJob>();
        public List<PlayerFactoryRepeatOrder> factoryRepeatOrders = new List<PlayerFactoryRepeatOrder>();
        public int nextFactoryRepeatOrderId = 1;
        public List<StoredShip> storedShips = new List<StoredShip>();
        // Parallel owner lists preserve the existing StoredShip save format while ensuring shared access never
        // grants ownership of another pilot's hull. Legacy entries are assigned to the station's individual owner.
        public List<string> storedShipOwners = new List<string>();
        /// <summary>Large hulls held in the station's dedicated dry dock.</summary>
        public List<StoredShip> dryDockShips = new List<StoredShip>();
        public List<string> dryDockShipOwners = new List<string>();
        // Set only in the transmitted public projection.
        public int visibleDryDockHullCount;

        internal PlayerStationRecord ToPublicRecord()
        {
            var visible = (PlayerStationRecord)MemberwiseClone();
            visible.visibleDryDockHullCount = dryDockShips?.Count ?? 0;
            visible.storage = null;
            visible.donatedConstructionStock = null;
            visible.storageLots = null;
            visible.storedShips = null;
            visible.storedShipOwners = null;
            visible.dryDockShips = null;
            visible.dryDockShipOwners = null;
            visible.dryDockBuilderProfile = null;
            visible.factoryJobs = null;
            visible.factoryRepeatOrders = null;
            visible.constructionContributions = null;
            visible.jumpgateContributions = null;
            visible.pendingTransfer = null;
            visible.history = null;
            visible.permissionOverrides = null;
            visible.roleAssignments = null;
            visible.bans = null;
            visible.individualSuccessorProfile = null;
            visible.factionSuccessorProfile = null;
            visible.factionSuccessorFactionId = null;
            visible.factionSuccessorDelegateProfile = null;
            visible.originalBuilderProfile = null;
            visible.ownerProfile = null;
            if (offeredForClaimUtc > 0)
            {
                visible.ownerName = null;
                visible.ownerFactionId = null;
                visible.moduleConstructionKind = null;
                visible.moduleConstructionMaterials = null;
            }
            visible.wreckBlueprintSalvage = null;
            if (wreckClaim != null)
                visible.wreckClaim = new PlayerStationWreckClaim
                {
                    claimantName = wreckClaim.claimantName,
                    claimedUtcMilliseconds = wreckClaim.claimedUtcMilliseconds,
                    exclusive = wreckClaim.exclusive
                };
            visible.shipListings = null;
            visible.listings = null; // public listing readout uses the server's marketplace command
            visible.buyOrders = offeredForClaimUtc > 0 ? null : buyOrders?.ConvertAll(order => order == null ? null : new PlayerStationBuyOrder
            {
                item = order.item, remaining = order.remaining, unitPrice = order.unitPrice,
                reservedCredits = order.reservedCredits
            });
            return visible;
        }
        /// <summary>One persistent in-progress dry-dock ship build. Completion is evaluated by the server clock.</summary>
        public bool dryDockBuilding;
        public int dryDockBlueprintItem = -1, dryDockShip = -1;
        public long dryDockStartedUtc, dryDockCompletesUtc;
        public bool dryDockConsumeBlueprint;
        public string dryDockBuilderProfile;
        /// <summary>When enabled, members of the owner's current faction may use station storage and hangar.</summary>
        public bool factionStorageAccess;
        public List<PlayerStationContribution> constructionContributions = new List<PlayerStationContribution>();
        public PlayerStationTransferProposal pendingTransfer;
        // JsonUtility can deserialize a null inline record as an empty object. Check the flag.
        public bool wreckClaimOpen;
        public bool reconstructing, destroyed, retired, stationIntegrityInitialized;
        public float stationIntegrity, stationIntegrityCapacity;
        public int marketplaceTaxPercent;
        public long offeredForClaimUtc;
        public float stationOffsetX;
        public List<PlayerStationMaterial> wreckBlueprintSalvage = new List<PlayerStationMaterial>();
        public bool stationExplosionPending, stationSalvagePending;
        public List<PlayerStationMaterial> stationSalvage = new List<PlayerStationMaterial>();
        public PlayerStationWreckClaim wreckClaim;
        // One fixed-destination jumpgate may be built in this station's orbit.
        public int jumpgateDestinationSystem = -1;
        public bool jumpgateBuilding;
        public long jumpgateStartedUtc, jumpgateCompletesUtc;
        public int jumpgateBuildSeconds;
        public List<PlayerStationMaterial> jumpgateMaterials = new List<PlayerStationMaterial>();
        public List<PlayerStationContribution> jumpgateContributions = new List<PlayerStationContribution>();
    }

    [Serializable]
    public sealed class PlayerStationBuyOrder
    {
        public int item, remaining;
        public long unitPrice, reservedCredits, createdUtc;
        public string fundedByProfile, fundedByFactionId, createdBy;
    }

    [Serializable]
    public sealed class PlayerStationStorage
    {
        public int item;
        public int amount;
    }

    [Serializable]
    public sealed class PlayerStationStorageLot
    {
        public string ownerProfile;
        public int item;
        public int amount;
    }

    [Serializable]
    public sealed class PlayerStationMaterial
    {
        public int item;
        public int required;
        public int delivered;
    }

    [Serializable]
    public sealed class PlayerStationContribution
    {
        public string contributorProfile;
        public int item;
        public int amount;
        /// <summary>Fixed catalogue unit value used when the contribution was paid.</summary>
        public long unitValue;
        /// <summary>Total credits actually paid to this contributor from the faction bank.</summary>
        public long paidCredits;
        public long contributedUtc;
        public long paidUtc;
    }

    [Serializable]
    public sealed class PlayerStationTransferProposal
    {
        public PlayerStationOwnershipType targetType;
        public string targetProfile;
        public string targetFactionId;
        public string proposedBy;
        // Set only after a server admin verifies an individual-owner recovery case.
        public bool administrativeRecovery;
        public string recoveryAuthorizedBy;
        public string recoveryReason;
        public long expiresUtc;
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
        public int id;
        public int item;
        public int amount;
        public long unitPrice;
        public string sellerProfile;
        public string payoutFactionId;
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
            public int nextOrbitalStructureId = 1;
            public List<PlayerOrbitalStructure> orbitalStructures = new List<PlayerOrbitalStructure>();
            public List<PlayerStationRecord> stations = new List<PlayerStationRecord>();
            public List<PlayerJumpgateRecord> jumpgates = new List<PlayerJumpgateRecord>();
            public List<int> consumedBlueprints = new List<int>();
            public List<PlayerFactionConstructionStock> factionConstructionStock = new List<PlayerFactionConstructionStock>();
        }

        static Registry registry;
        static string path;
        static bool sessionActive;
        static bool persistenceFaulted;

        /// <summary>Current server registry; empty until a persistent world starts.</summary>
        public static IReadOnlyList<PlayerStationRecord> Stations => registry?.stations;
        /// <summary>Independent player-built gates, each anchored to one system orbit.</summary>
        public static IReadOnlyList<PlayerJumpgateRecord> Jumpgates => registry?.jumpgates;

        static bool consumedBlueprintsReconciled;

        internal static bool IsBlueprintConsumed(int item)
        {
            if (!consumedBlueprintsReconciled) ReconcileConsumedBlueprints();
            if (registry?.consumedBlueprints?.Contains(item) != true) return false;
            // A later acquisition can unlock the same blueprint again. Startup reconciliation first removes
            // stale unlocks left behind by a crash; an unlock after that point is a genuinely new blueprint.
            if (consumedBlueprintsReconciled && GoF2Remake.Data.Blueprints.IsUnlocked(item))
            {
                registry.consumedBlueprints.Remove(item);
                if (Save()) return false;
                if (registry?.consumedBlueprints != null && !registry.consumedBlueprints.Contains(item))
                    registry.consumedBlueprints.Add(item);
            }
            return true;
        }

        static void ReconcileConsumedBlueprints()
        {
            // The registry is server-owned. Never mutate a joining client's local blueprint unlocks.
            if (!NetGame.IsServer || registry?.consumedBlueprints == null || GoF2Remake.Data.Session.UnlockedBlueprints == null) return;
            foreach (int item in registry.consumedBlueprints)
                GoF2Remake.Data.Session.UnlockedBlueprints.Remove(item);
            consumedBlueprintsReconciled = true;
        }

        /// <summary>The current registry for NetState's initial session snapshot.</summary>
        internal static string Snapshot
        {
            get
            {
                if (registry == null) return "";
                var visible = new Registry
                {
                    stations = registry.stations.ConvertAll(station => station?.ToPublicRecord()),
                    orbitalStructures = registry.orbitalStructures,
                    jumpgates = registry.jumpgates?.ConvertAll(gate => gate?.ToPublicRecord()) ?? new List<PlayerJumpgateRecord>(),
                    consumedBlueprints = registry.consumedBlueprints
                };
                return JsonUtility.ToJson(visible);
            }
        }

        /// <summary>Load station state before the world starts accepting player actions.</summary>
        public static void Start()
        {
            registry = null;
            path = null;
            nextDryDockRetryUtc = 0;
            nextSlowStationTick = nextWreckClaimTick = 0f;
            nextGateManagementUpdate = 0;
            ResetWreckControl();
            nextTransactionRetryUtc = 0;
            persistenceFaulted = false;
            consumedBlueprintsReconciled = false;
            economyCosts.Clear();
            economyRecipes.Clear();
            sessionActive = NetGame.PersistentHost || NetGame.Dedicated;
            if (!sessionActive) return;

            string folder = NetProfiles.Folder;
            if (string.IsNullOrEmpty(folder)) folder = NetGame.HostedWorldFolder;
            path = Path.Combine(folder, FileName);
            string backupPath = path + ".bak";
            try
            {
                Directory.CreateDirectory(folder);
                LoadStationEconomy(folder);
                if (File.Exists(path))
                {
                    try
                    {
                        registry = JsonUtility.FromJson<Registry>(File.ReadAllText(path));
                        if (registry == null) throw new InvalidDataException("registry JSON produced no data");
                    }
                    catch (Exception primaryError)
                    {
                        Debug.LogError($"NetPlayerStations: {FileName} is unreadable ({primaryError.Message}); attempting its backup.");
                        if (!File.Exists(backupPath)) throw new InvalidDataException("no registry backup exists", primaryError);
                        string backupJson = File.ReadAllText(backupPath);
                        registry = JsonUtility.FromJson<Registry>(backupJson);
                        if (registry == null) throw new InvalidDataException("registry backup produced no data");
                        // Preserve the damaged primary for diagnosis; do not let a recovery overwrite destroy evidence.
                        string damagedPath = path + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss");
                        File.Copy(path, damagedPath, true);
                        // Do not use NetProfiles.Write here: it would overwrite the known-good backup with the corrupt primary.
                        string recoveryPath = path + ".recovered";
                        if (File.Exists(recoveryPath)) File.Delete(recoveryPath);
                        File.WriteAllText(recoveryPath, backupJson);
                        if (File.Exists(path)) File.Delete(path);
                        File.Move(recoveryPath, path);
                        Debug.LogWarning($"NetPlayerStations: restored {FileName} from its backup. The damaged original was replaced; retain external backups as well.");
                    }
                }
                else if (File.Exists(backupPath))
                {
                    string backupJson = File.ReadAllText(backupPath);
                    registry = JsonUtility.FromJson<Registry>(backupJson);
                    if (registry == null) throw new InvalidDataException("registry backup produced no data");
                    string recoveryPath = path + ".recovered";
                    if (File.Exists(recoveryPath)) File.Delete(recoveryPath);
                    File.WriteAllText(recoveryPath, backupJson);
                    File.Move(recoveryPath, path);
                    Debug.LogWarning($"NetPlayerStations: restored missing {FileName} from its backup.");
                }
                else registry = new Registry();
            }
            catch (Exception e)
            {
                persistenceFaulted = true;
                registry = null;
                Debug.LogError($"NetPlayerStations: station persistence is disabled because neither the primary registry nor its backup could be loaded ({e.Message}). No station data will be overwritten.");
                return;
            }

            // Recover the durable snapshot before normalizing it, not the superseded pre-transaction registry.
            if (!RecoverPendingTransaction())
            {
                persistenceFaulted = true;
                registry = null;
                Debug.LogError("NetPlayerStations: station persistence is disabled because an interrupted transaction could not be recovered. Preserve the journal and registry for manual recovery.");
                return;
            }

            registry.stations ??= new List<PlayerStationRecord>();
            registry.jumpgates ??= new List<PlayerJumpgateRecord>();
            registry.consumedBlueprints ??= new List<int>();
            if (registry.stations.FindAll(station => station != null && !station.retired).Count > MaxStations)
                Debug.LogWarning($"NetPlayerStations: saved registry contains {registry.stations.Count} stations (limit {MaxStations}); preserving all records and refusing new construction.");
            int highest = 0;
            foreach (var station in registry.stations)
            {
                if (station == null) continue;
                highest = Math.Max(highest, station.id);
                station.addons ??= new List<PlayerStationAddon>();
                station.materials ??= new List<PlayerStationMaterial>();
                station.listings ??= new List<PlayerStationListing>();
                NormalizeItemListingIds(station);
                station.buyOrders ??= new List<PlayerStationBuyOrder>();
                station.constructionContributions ??= new List<PlayerStationContribution>();
                station.jumpgateMaterials ??= new List<PlayerStationMaterial>();
                station.jumpgateContributions ??= new List<PlayerStationContribution>();
                station.storage ??= new List<PlayerStationStorage>();
                NormalizeStorageLots(station);
                station.storedShips ??= new List<StoredShip>();
                station.dryDockShips ??= new List<StoredShip>();
                NormalizeOwnershipAndHullOwners(station);
                if (!station.dryDockBuilding) ClearDryDockJob(station);
                if (station.addons.Count > MaxAddonsPerStation || station.listings.Count > MaxListingsPerStation)
                    Debug.LogWarning($"NetPlayerStations: station {station.id} exceeds a module or listing limit; preserving all saved records.");
            }
            registry.nextId = Math.Max(highest == int.MaxValue ? int.MaxValue : highest + 1, Math.Max(1, registry.nextId));
            // Older buy-order completions could leave a funded rebuild marked as closed.
            foreach (var station in registry.stations)
                if (station != null && station.reconstructing && !station.underConstruction &&
                    !station.destroyed && !station.wreckClaimOpen && station.materials?.Count > 0 &&
                    station.materials.TrueForAll(material => material != null && material.delivered >= material.required))
                    CompleteStationConstruction(station);
            ReconcileConsumedBlueprints();
            registry.orbitalStructures ??= new List<PlayerOrbitalStructure>();
            registry.orbitalStructures.RemoveAll(structure => structure == null);
            registry.nextOrbitalStructureId = Math.Max(1, registry.nextOrbitalStructureId);
            foreach (var structure in registry.orbitalStructures)
                registry.nextOrbitalStructureId = Math.Max(registry.nextOrbitalStructureId,
                    structure.id < int.MaxValue ? structure.id + 1 : int.MaxValue);
            orbitalAggression.Clear();
            nextOrbitalStructureTick = 0;
            MigrateLegacyJumpgates();
            foreach (var gate in registry.jumpgates)
                if (gate != null && gate.scrapped && !gate.permanentlyDestroyed) InitializeGateScrap(gate);
            MigrateFactionConstructionStock();
            foreach (var station in registry.stations)
            {
                if (station == null) continue;
                SyncJumpgatesForStation(station);
                if (station.shipListings == null) continue;
                int next = Math.Max(1, station.nextShipListingId);
                foreach (var listing in station.shipListings)
                    if (listing != null && listing.id >= next)
                        next = listing.id == int.MaxValue ? int.MaxValue : listing.id + 1;
                foreach (var listing in station.shipListings)
                    if (listing != null && listing.id <= 0 && next < int.MaxValue) listing.id = next++;
                station.nextShipListingId = next;
            }
            // Persist one-time governance-field migrations for existing individual stations.
            Save();
        }

        /// <summary>Apply the server's station snapshot on a joining game. Clients keep a read-only copy and never save it.</summary>
        internal static void ReceiveSnapshot(string json)
        {
            var previous = registry;
            bool wasActive = sessionActive;
            sessionActive = true;
            if (string.IsNullOrEmpty(json)) { registry = new Registry(); return; }
            try
            {
                registry = JsonUtility.FromJson<Registry>(json);
                if (registry == null) throw new InvalidDataException("Station snapshot did not contain a registry.");
                registry.stations ??= new List<PlayerStationRecord>();
                registry.jumpgates ??= new List<PlayerJumpgateRecord>();
                registry.consumedBlueprints ??= new List<int>();
                if (registry.stations.FindAll(station => station != null && !station.retired).Count > MaxStations)
                    Debug.LogWarning($"NetPlayerStations: received registry contains {registry.stations.Count} stations (limit {MaxStations}); preserving all records.");
                foreach (var gate in registry.jumpgates)
                {
                    if (gate == null) continue;
                    gate.materials ??= new List<PlayerStationMaterial>();
                    gate.contributions ??= new List<PlayerStationContribution>();
                }
                foreach (var station in registry.stations)
                {
                    if (station == null) continue;
                    station.addons ??= new List<PlayerStationAddon>();
                    station.materials ??= new List<PlayerStationMaterial>();
                    station.listings ??= new List<PlayerStationListing>();
                    station.constructionContributions ??= new List<PlayerStationContribution>();
                    station.jumpgateMaterials ??= new List<PlayerStationMaterial>();
                    station.jumpgateContributions ??= new List<PlayerStationContribution>();
                    station.storage ??= new List<PlayerStationStorage>();
                    NormalizeStorageLots(station);
                    station.storedShips ??= new List<StoredShip>();
                    station.dryDockShips ??= new List<StoredShip>();
                    NormalizeOwnershipAndHullOwners(station);
                    if (!station.dryDockBuilding) ClearDryDockJob(station);
                    if (station.addons.Count > MaxAddonsPerStation || station.listings.Count > MaxListingsPerStation)
                        Debug.LogWarning($"NetPlayerStations: received station {station.id} exceeds a module or listing limit; preserving all records.");
                }
                // The snapshot can change during a live session. Append any newly built stations to this game's
                // database before the player next enters their orbit.
                if (NetGame.Active) Apply(NetGame.Db);
            }
            catch (Exception e)
            {
                registry = previous;
                sessionActive = wasActive;
                throw new InvalidDataException("Server station snapshot was unreadable.", e);
            }
        }

        static void NormalizeOwnershipAndHullOwners(PlayerStationRecord station)
        {
            if (station == null) return;
            // Missing ownershipType in pre-feature saves deserializes to Individual (zero).
            if (station.ownershipType != PlayerStationOwnershipType.Faction)
            {
                station.ownershipType = PlayerStationOwnershipType.Individual;
                station.ownerFactionId = null;
            }
            station.storedShipOwners ??= new List<string>();
            station.dryDockShipOwners ??= new List<string>();
            while (station.storedShipOwners.Count < (station.storedShips?.Count ?? 0))
                station.storedShipOwners.Add(station.ownerProfile ?? "");
            while (station.dryDockShipOwners.Count < (station.dryDockShips?.Count ?? 0))
                station.dryDockShipOwners.Add(station.ownerProfile ?? "");
            if (station.storedShipOwners.Count > (station.storedShips?.Count ?? 0))
                station.storedShipOwners.RemoveRange(station.storedShips?.Count ?? 0, station.storedShipOwners.Count - (station.storedShips?.Count ?? 0));
            if (station.dryDockShipOwners.Count > (station.dryDockShips?.Count ?? 0))
                station.dryDockShipOwners.RemoveRange(station.dryDockShips?.Count ?? 0, station.dryDockShipOwners.Count - (station.dryDockShips?.Count ?? 0));
            NormalizeGovernance(station);
        }

        static void NormalizeStorageLots(PlayerStationRecord station)
        {
            station.storageLots ??= new List<PlayerStationStorageLot>();
            // Discard empty/corrupt lots before any ownership arithmetic. Negative lots must never
            // participate in RemoveStorageLots, where subtracting a negative amount could increase it.
            station.storageLots.RemoveAll(lot => lot == null || lot.amount <= 0);
            // Legacy inventory has no depositor identity. Attribute it to the station's individual owner,
            // preserving the goods as that profile's property instead of gifting them to a later owner.
            foreach (var stack in station.storage ?? new List<PlayerStationStorage>())
            {
                if (stack == null || stack.amount <= 0) continue;
                long attributed = 0;
                foreach (var lot in station.storageLots)
                    if (lot.item == stack.item) attributed += lot.amount;
                if (attributed < stack.amount)
                    station.storageLots.Add(new PlayerStationStorageLot
                    { ownerProfile = station.ownerProfile ?? "", item = stack.item, amount = (int)(stack.amount - attributed) });
            }
        }

        /// <summary>Find a station by its persistent server-assigned id.</summary>
        static Registry indexedRegistry;
        static int indexedStationCount = -1;
        static readonly Dictionary<int, PlayerStationRecord> stationsById = new Dictionary<int, PlayerStationRecord>();
        static readonly Dictionary<int, PlayerStationRecord> stationsByOrbit = new Dictionary<int, PlayerStationRecord>();

        static void ReplaceStationRecord(Registry target, int index, PlayerStationRecord replacement)
        {
            target.stations[index] = replacement;
            if (ReferenceEquals(target, indexedRegistry)) indexedStationCount = -1;
        }

        static void EnsureStationIndex()
        {
            int count = registry?.stations?.Count ?? 0;
            if (ReferenceEquals(indexedRegistry, registry) && indexedStationCount == count) return;
            stationsById.Clear();
            stationsByOrbit.Clear();
            indexedRegistry = registry;
            indexedStationCount = count;
            if (registry?.stations == null) return;
            foreach (var station in registry.stations)
            {
                if (station == null) continue;
                // Preserve the first-match semantics of the original list lookup.
                if (!stationsById.ContainsKey(station.id)) stationsById.Add(station.id, station);
                if (station.existingOrbit && station.retired) continue;
                if (!stationsByOrbit.ContainsKey(station.stationIndex)) stationsByOrbit.Add(station.stationIndex, station);
            }
        }

        public static PlayerStationRecord Find(int id)
        {
            EnsureStationIndex();
            return stationsById.TryGetValue(id, out var station) ? station : null;
        }

        /// <summary>Show the persistent state and construction progress of one player station.</summary>
        internal static string Status(NetPlayer by, int id)
        {
            if (!NetGame.Active || registry?.stations == null) return "Join a multiplayer session first.";
            var record = Find(id);
            if (record == null) return $"No player station with project id {id} exists in this world.";
            if (record.offeredForClaimUtc > 0) return $"{record.name} is open to claim for {StationClaimPrice:N0} credits. Dock and use /station claim {id}.";
            if (record.wreckClaimOpen && record.wreckClaim != null) return WreckClaimStatus(record);
            if (record.destroyed) return $"Station #{id}: {record.name} was destroyed beyond recovery.";
            if (!CanViewStationRecord(by, record)) return "That station's management status is private to its owner and authorised faction members.";

            var system = NetGame.Db?.Systems?.Find(s => s != null && s.index == record.system);
            var lines = new List<string>
            {
                $"Station #{record.id}: {record.name}",
                $"System: {(system != null ? system.name : $"Unknown ({record.system})")}",
                $"Owner: {(record.ownershipType == PlayerStationOwnershipType.Faction ? "Faction " + (string.IsNullOrWhiteSpace(record.ownerName) ? (record.ownerFactionId ?? "unknown") : record.ownerName) : (string.IsNullOrWhiteSpace(record.ownerName) ? "Unknown" : record.ownerName))}",
                $"Status: {(record.reconstructing ? "Reconstructing" : record.underConstruction ? "Under construction" : "Operational")}",
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
            if (!string.IsNullOrEmpty(record.moduleConstructionKind))
            {
                lines.Add("Module project: " + ModuleName(record.moduleConstructionKind));
                if (record.moduleConstructionMaterials != null)
                    foreach (var material in record.moduleConstructionMaterials)
                        if (material != null) lines.Add($"  {ConstructionItemName(material.item)}: {material.delivered}/{material.required}");
            }
            lines.Add($"Installed modules: {record.addons?.Count ?? 0}");
            if (record.addons != null)
                foreach (var addon in record.addons)
                    if (addon != null) lines.Add($"  {addon.kind}: level {addon.level}");
            int storageCapacity = StorageCapacity(record);
            if (storageCapacity > 0)
            {
                long stored = 0;
                if (record.storage != null)
                    foreach (var stack in record.storage) if (stack != null) stored += Math.Max(0, stack.amount);
                lines.Add($"Storage: {stored:N0}/{storageCapacity:N0}");
                lines.Add($"Storage access: {(record.factionPermissions?.storage == true ? "Faction members" : "Owner only")}");
            }
            if (record.addons?.Exists(a => a != null && a.kind == "hangar" && a.level > 0) ?? false)
                lines.Add($"Ship hangar: {record.storedShips?.Count ?? 0}/100 hulls stored");
            if (record.addons?.Exists(a => a != null && a.kind == "drydock" && a.level > 0) ?? false)
                lines.Add($"Dry dock: {record.dryDockShips?.Count ?? 0}/20 large hulls stored");
            lines.Add($"Marketplace listings: {record.listings?.Count ?? 0}");
            return string.Join("\n", lines);
        }

        /// <summary>Station model/collision preset inherited by a player station from its chosen orbital anchor.</summary>
        internal static int LooksLike(int stationIndex)
        {
            if (!sessionActive || registry?.stations == null) return -1;
            var station = StationAt(stationIndex);
            return station != null ? StationModelIndex(station) : -1;
        }

        internal static bool IsPlayerStation(int stationIndex) =>
            sessionActive && StationAt(stationIndex) != null;

        /// <summary>Whether an existing station index belongs to a completed player station with an installed dry dock.</summary>
        internal static bool HasDryDockAtStation(int stationIndex)
        {
            if (!sessionActive || registry?.stations == null) return false;
            var station = StationAt(stationIndex);
            return station != null && station.offeredForClaimUtc == 0 && !station.underConstruction && !StationClosedByCombat(stationIndex) &&
                (station.addons?.Exists(a => a != null && a.kind == "drydock" && a.level > 0) ?? false);
        }

        internal static bool UnderConstruction(int stationIndex) =>
            StationAt(stationIndex)?.underConstruction == true;

        static string ConstructionDeliveryError(NetPlayer by, PlayerStationRecord record)
        {
            if (record.reconstructing)
            {
                if (!by.InSpace || by.Station != record.stationIndex || by.Hull <= 0 || !by.HasCurrentWreckClaimPose ||
                    (by.Position - StationPosition(record.stationIndex)).sqrMagnitude > WreckControlRadiusMetres * WreckControlRadiusMetres)
                    return "Fly within 5 km of the reconstruction site to deliver materials.";
            }
            else if (!by.InHangar || StationClosedByCombat(by.Station))
                return "Dock at any operational station to deposit construction materials.";
            return null;
        }

        // Callers stage this change in the same journal as materials and escrow.
        static Action CompleteStationConstruction(PlayerStationRecord record)
        {
            bool wasUnderConstruction = record.underConstruction;
            bool wasReconstructing = record.reconstructing;
            var rollbackGateOwnership = wasReconstructing ? RestoreSponsoredGateOwnership(record) : null;
            bool integrityInitialized = record.stationIntegrityInitialized;
            float previousIntegrity = record.stationIntegrity;
            float previousIntegrityCapacity = record.stationIntegrityCapacity;
            record.underConstruction = false;
            record.reconstructing = false;
            record.stationIntegrityInitialized = false;
            record.stationIntegrity = 0f;
            record.stationIntegrityCapacity = 0f;
            if (wasReconstructing) SyncJumpgatesForStation(record);
            return () =>
            {
                record.underConstruction = wasUnderConstruction;
                record.reconstructing = wasReconstructing;
                record.stationIntegrityInitialized = integrityInitialized;
                record.stationIntegrity = previousIntegrity;
                record.stationIntegrityCapacity = previousIntegrityCapacity;
                rollbackGateOwnership?.Invoke();
            };
        }

        /// <summary>Deposit cargo at the delivery location required by the project.</summary>
        internal static string Contribute(NetPlayer by, int id, int item, int amount)
        {
            if (!NetGame.IsServer || !sessionActive || registry == null) return "Player construction needs a persistent server.";
            if (by == null || !by.IsSpawned) return "Connect a pilot to deposit construction materials.";
            if (!NetProfiles.Enabled || !NetProfiles.Controls(by.OwnerClientId)) return "Use a controlled player profile to contribute materials.";
            if (amount < 1 || amount > 500 || !NetGuard.Item(item)) return "Deposit between 1 and 500 units of a valid item.";
            var record = Find(id);
            if (record == null || !record.underConstruction) return "That construction project is not active.";
            string locationError = ConstructionDeliveryError(by, record);
            if (locationError != null) return locationError;
            var resource = record.materials?.Find(m => m != null && m.item == item);
            if (resource == null) return "That item is not required for this station project.";
            long remaining = (long)resource.required - resource.delivered;
            if (remaining < 0) return "That construction material has an invalid delivered total; administrator recovery is required.";
            if (amount > remaining) return $"The project needs only {remaining} more units of that material.";
            string account = NetProfiles.AccountOf(by.OwnerClientId);
            if (string.IsNullOrEmpty(account)) return "Your player profile is unavailable.";

            bool factionOwned = record.ownershipType == PlayerStationOwnershipType.Faction;
            long unitValue = 0, payout = 0;
            if (factionOwned)
            {
                var itemData = NetGame.Db?.Item(item);
                // Use the static catalogue floor, not a live station quote or a player-controlled listing price.
                // This makes contribution valuation deterministic and prevents market-price manipulation.
                unitValue = Math.Max(1, itemData?.minPrice ?? 0);
                if (unitValue > long.MaxValue / amount) return "The contribution value is outside the supported credit range.";
                payout = unitValue * amount;
                long bank = NetFactions.BankAmount(record.ownerFactionId);
                if (bank < 0) return "The owning faction treasury is unavailable; no materials were taken.";
                if (bank < payout)
                    return $"The faction bank needs {payout:N0} credits to pay this contribution but only has {bank:N0}. Your materials were not taken.";
            }

            record.constructionContributions ??= new List<PlayerStationContribution>();
            var contribution = record.constructionContributions.Find(x => x != null &&
                string.Equals(x.contributorProfile, account, StringComparison.Ordinal) && x.item == item);
            bool newContribution = contribution == null;
            if (newContribution)
                contribution = new PlayerStationContribution
                { contributorProfile = account, item = item, amount = 0, unitValue = unitValue, paidCredits = 0,
                    contributedUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds() };
            if (contribution.amount < 0 || contribution.amount > int.MaxValue - amount ||
                contribution.paidCredits < 0 || (factionOwned && contribution.paidCredits > long.MaxValue - payout))
                return "The construction contribution ledger has an invalid or overflowing total; administrator recovery is required.";

            bool complete = true;
            foreach (var material in record.materials)
            {
                if (material == null) continue;
                long delivered = (long)material.delivered + (material == resource ? amount : 0);
                if (delivered < material.required) { complete = false; break; }
            }

            // If this contribution finishes the project, refund every unused buy-order escrow in
            // the same journal as the cargo debit, contributor payout and completed station record.
            var previousOrders = new List<PlayerStationBuyOrder>();
            bool hadOrders = record.buyOrders != null;
            var profileRefunds = new List<(string account, long amount)>();
            var credits = new List<(string account, long delta)>();
            var banks = new List<(string factionId, long delta)>();
            if (factionOwned)
            {
                credits.Add((account, payout));
                banks.Add((record.ownerFactionId, -payout));
            }
            if (complete && record.buyOrders != null)
            {
                foreach (var pending in record.buyOrders)
                {
                    if (pending == null) continue;
                    previousOrders.Add(new PlayerStationBuyOrder
                    {
                        item = pending.item, remaining = pending.remaining, unitPrice = pending.unitPrice,
                        reservedCredits = pending.reservedCredits, fundedByProfile = pending.fundedByProfile,
                        fundedByFactionId = pending.fundedByFactionId, createdBy = pending.createdBy,
                        createdUtc = pending.createdUtc
                    });
                    if (pending.reservedCredits < 0)
                        return "Construction cannot complete because an outstanding buy order has invalid negative escrow. The order was preserved for administrator recovery.";
                    if (pending.reservedCredits == 0) continue;
                    bool hasProfileFunder = !string.IsNullOrEmpty(pending.fundedByProfile);
                    bool hasFactionFunder = !string.IsNullOrEmpty(pending.fundedByFactionId);
                    if (hasProfileFunder == hasFactionFunder)
                        return "Construction cannot complete because an outstanding buy order has an ambiguous or missing escrow owner. The order was preserved for administrator recovery.";
                    if (hasProfileFunder)
                    {
                        credits.Add((pending.fundedByProfile, pending.reservedCredits));
                        profileRefunds.Add((pending.fundedByProfile, pending.reservedCredits));
                    }
                    else banks.Add((pending.fundedByFactionId, pending.reservedCredits));
                }
            }

            int previousAmount = contribution.amount;
            long previousUnitValue = contribution.unitValue;
            long previousPaid = contribution.paidCredits;
            long previousPaidUtc = contribution.paidUtc;
            int previousDelivered = resource.delivered;
            Action rollbackCompletion = null;
            if (newContribution) record.constructionContributions.Add(contribution);
            contribution.amount += amount;
            if (factionOwned)
            {
                contribution.unitValue = unitValue;
                contribution.paidCredits += payout;
                contribution.paidUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            }
            resource.delivered += amount;
            if (complete)
            {
                rollbackCompletion = CompleteStationConstruction(record);
                record.buyOrders?.Clear();
            }

            bool committed = CommitProfileTransaction(
                new List<(string account, int item, int delta)> { (account, item, -amount) },
                credits.Count > 0 ? credits : null,
                banks.Count > 0 ? banks : null);
            if (!committed)
            {
                resource.delivered = previousDelivered;
                rollbackCompletion?.Invoke();
                contribution.amount = previousAmount;
                contribution.unitValue = previousUnitValue;
                contribution.paidCredits = previousPaid;
                contribution.paidUtc = previousPaidUtc;
                if (newContribution) record.constructionContributions.Remove(contribution);
                if (complete && hadOrders) record.buyOrders = previousOrders;
                return factionOwned
                    ? "The contribution and faction-bank payout could not be committed; no materials were taken."
                    : "The project transaction could not be journalled; no materials were taken.";
            }

            if (complete) Apply(NetGame.Db);
            if (!(NetGame.PersistentHost && by.OwnerClientId == NetGame.LocalId))
                NetState.Instance?.SendAdmin(by.OwnerClientId, NetAdmin.Order.Take, item, amount, 0, "", "Station construction");
            if (factionOwned) NotifyProfileCreditChange(account, payout, "Station construction contribution");
            foreach (var refund in profileRefunds)
                NotifyProfileCreditChange(refund.account, refund.amount, "Construction buy order escrow refund");

            string result = $"Deposited {amount} x {NetGame.Db.Item(item).name} into {record.name}.";
            if (factionOwned) result += $" Paid {payout:N0} credits from the faction bank ({unitValue:N0} per unit).";
            if (complete)
            {
                result += " Construction is complete; the station is now open.";
                if (profileRefunds.Count > 0 || banks.Count > (factionOwned ? 1 : 0))
                    result += " Remaining construction buy-order escrow was returned to its original funders.";
            }
            return result;
        }

        internal static bool CanBuildStationInOrbit(int orbit)
        {
            var db = NetGame.Db;
            var anchor = db?.Stations?.Find(station => station != null && station.index == orbit);
            if (anchor == null || orbit < 0 || orbit == Session.VoidOrbit || StationAt(orbit) != null ||
                Modding.ModWorld.MobileOf(orbit) != null) return false;
            var system = db.Systems?.Find(entry => entry != null && entry.index == anchor.system);
            if (system?.stations?.Contains(orbit) != true) return false;
            return World.OrbitLayout.VacantStationSite(orbit, Session.WorldIndex);
        }

        internal static int[] AvailableConstructionOrbits(NetPlayer pilot)
        {
            if (!NetGame.IsServer || !sessionActive || registry?.stations == null || pilot == null || !pilot.IsSpawned ||
                !Modding.ModPlayerInfrastructure.Current.Enabled || ProfileWritesBlocked || persistenceFaulted ||
                registry.stations.FindAll(station => station != null && !station.retired).Count >= MaxStations)
                return Array.Empty<int>();
            var reachable = NetProfiles.ConstructionReachableSystems(pilot);
            var orbits = new List<int>();
            foreach (var system in NetGame.Db.Systems)
            {
                if (system == null || system.stations == null || !reachable.Contains(system.index)) continue;
                int count = registry.stations.FindAll(record => record != null && !record.retired && !record.destroyed && record.system == system.index).Count;
                if (count >= MaxStationsPerSystem) continue;
                foreach (int orbit in system.stations) if (CanBuildStationInOrbit(orbit)) orbits.Add(orbit);
            }
            return orbits.ToArray();
        }

        internal static HashSet<int> ReachableConstructionSystems(int origin, Func<int, bool> discovered, Func<PlayerJumpgateRecord, bool> gateAllowed)
        {
            var result = new HashSet<int>();
            var db = NetGame.Db;
            if (origin < 0 || db?.Systems == null) return result;
            var queue = new Queue<int>();
            result.Add(origin); queue.Enqueue(origin);
            void Add(int next)
            {
                if (next >= 0 && discovered(next) && db.Systems.Exists(system => system != null && system.index == next) && result.Add(next)) queue.Enqueue(next);
            }
            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                var system = db.Systems.Find(entry => entry != null && entry.index == current);
                if (system?.jumpRoutesTo != null)
                    foreach (int next in system.jumpRoutesTo) Add(next);
                if (registry?.jumpgates != null)
                    foreach (var gate in registry.jumpgates)
                        if (gate != null && gate.system == current && gateAllowed(gate)) Add(gate.destinationSystem);
            }
            return result;
        }

        /// <summary>Reserve a vacant orbital site and start its long-running, server-owned material project.</summary>
        internal static string StartConstruction(NetPlayer by, int systemIndex, string name, int modelPreset = -1, int requestedOrbit = -1)
        {
            if (!NetGame.IsServer || !sessionActive || registry == null)
                return "Player-built stations need a persistent host or dedicated server.";
            if (by == null || !by.IsSpawned)
                return "You must be connected in the game to plan station construction.";
            if (!NetProfiles.Enabled || !NetProfiles.Controls(by.OwnerClientId))
                return "Sign in to a controlled player profile before building a station.";
            if (!Modding.ModPlayerInfrastructure.Current.Enabled) return "New player station projects are disabled by the active mods.";
            name = (name ?? "").Trim();
            if (!ValidStationName(name)) return StationNameRules;
            var db = NetGame.Db;
            var system = db?.Systems?.Find(s => s != null && s.index == systemIndex);
            if (system == null || system.stations == null || system.stations.Count == 0)
                return "Choose an existing system with at least one orbital location.";
            if (registry.stations.FindAll(station => station != null && !station.retired).Count >= MaxStations)
                return "This world has reached its player station limit.";
            int inSystem = 0;
            foreach (var station in registry.stations) if (station != null && !station.retired && !station.destroyed && station.system == system.index) inSystem++;
            if (inSystem >= MaxStationsPerSystem)
                return "This system has reached its player station limit.";

            string owner = NetProfiles.AccountOf(by.OwnerClientId);
            if (string.IsNullOrEmpty(owner)) return "Your controlled profile has no account identity.";
            if (!NetProfiles.HasDiscoveredSystem(owner, systemIndex))
                return "Discover the selected system before planning station construction.";
            if (!NetProfiles.CanReachConstructionSystem(by, systemIndex))
                return "The selected system cannot be reached with your current gate access or drive.";
            int stationIndex = requestedOrbit >= 0 ? requestedOrbit : system.stations.FindIndex(CanBuildStationInOrbit);
            if (requestedOrbit < 0 && stationIndex >= 0) stationIndex = system.stations[stationIndex];
            if (stationIndex < 0 || !system.stations.Contains(stationIndex) || !CanBuildStationInOrbit(stationIndex))
                return "Select a vacant orbit. Existing stations, wrecks and reserved projects cannot be replaced.";
            if (registry.nextId >= int.MaxValue) return "The station ID limit has been reached.";

            int look = modelPreset;
            if (look < 0)
            {
                look = system.jumpgateStation;
                if (!IsStationModelPreset(look))
                {
                    int slot = system.stations.FindIndex(IsStationModelPreset);
                    look = slot >= 0 ? system.stations[slot] :
                        db.Stations.Find(station => station != null && IsStationModelPreset(station.index))?.index ?? -1;
                }
            }
            if (!IsStationModelPreset(look)) return "Choose an existing station model from the game catalogue.";

            var record = new PlayerStationRecord
            {
                id = registry.nextId++,
                stationIndex = stationIndex,
                existingOrbit = true,
                name = name,
                ownerProfile = owner,
                ownerName = by.DisplayName,
                originalBuilderProfile = owner,
                governanceVersion = 1,
                factionPermissions = NewIndividualFactionPermissionProfile(false),
                system = system.index,
                looksLike = look,
                stationModelReference = StationModelReference(look),
                createdUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                underConstruction = true,
                materials = RequiredMaterials(),
            };
            RecordHistory(record, owner, "station-construction-started", null,
                "Original builder established the station project.");
            var originalRegistry = registry;
            registry.stations.Add(record);
            indexedStationCount = -1;
            if (!Save())
            {
                if (ReferenceEquals(registry, originalRegistry))
                {
                    registry.stations.Remove(record);
                    indexedStationCount = -1;
                    registry.nextId--;
                }
                return "The construction plan could not be saved.";
            }
            Apply(db);
            NetGuard.RefreshStations();
            return $"Construction project {record.id} started for {name} in {system.name}. Dock at any station to deposit materials over time with /station contribute {record.id} <item id> <amount>.";
        }

        static List<PlayerStationMaterial> RequiredMaterials() => ConstructionCost("station");

        /// <summary>End the replicated session view; saved server data is reloaded next time.</summary>
        internal static void EndSession()
        {
            registry = null;
            path = null;
            sessionActive = false;
            indexedRegistry = null;
            indexedStationCount = -1;
            stationsById.Clear();
            stationsByOrbit.Clear();
            nextTransactionRetryUtc = 0;
            ResetWreckControl();
        }

        internal const int StationNameMaxLength = 40;
        internal const string StationNameRules =
            "Station names must be 3–40 characters. Use letters, digits, spaces, hyphens, underscores or apostrophes.";

        internal static bool ValidStationName(string name)
        {
            if (name == null || name.Length < 3 || name.Length > StationNameMaxLength) return false;
            bool hasLetterOrDigit = false;
            foreach (char c in name)
            {
                if (char.IsLetter(c) || c is >= '0' and <= '9') hasLetterOrDigit = true;
                else if (c is not (' ' or '-' or '_' or '\'')) return false;
            }
            return hasLetterOrDigit;
        }

        sealed class RuntimeStationIdentity
        {
            public int ProjectId;
            public string OriginalName;
            public int OriginalTechLevel;
        }

        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<StationData, RuntimeStationIdentity>
            runtimeStations = new System.Runtime.CompilerServices.ConditionalWeakTable<StationData, RuntimeStationIdentity>();

        static void AddRuntimeStation(Database db, PlayerStationRecord record, StationData station)
        {
            db.Stations.Add(station);
            runtimeStations.Add(station, new RuntimeStationIdentity { ProjectId = record.id });
        }

        /// <summary>Add saved player stations to a freshly loaded game database, after mods have added their stations.</summary>
        internal static void Apply(Database db)
        {
            if (!sessionActive || registry?.stations == null || db?.Stations == null || db.Systems == null) return;
            var countsBySystem = new Dictionary<int, int>();
            foreach (var station in registry.stations)
            {
                if (station == null || station.destroyed || station.retired) continue;
                countsBySystem.TryGetValue(station.system, out int existingCount);
                countsBySystem[station.system] = existingCount + 1;
            }
            foreach (var record in registry.stations)
            {
                if (record == null || record.stationIndex < 0) continue;
                var system = db.Systems.Find(s => s != null && s.index == record.system);
                int modelIndex = StationModelIndex(record);
                var look = db.Stations.Find(s => s != null && s.index == modelIndex);
                if (look == null && system?.stations != null && system.stations.Count > 0)
                {
                    int fallback = system.jumpgateStation >= 0 ? system.jumpgateStation : system.stations[0];
                    look = db.Stations.Find(s => s != null && s.index == fallback);
                }

                countsBySystem.TryGetValue(record.system, out int count);
                bool validOrbit = system != null && system.stations != null && system.stations.Count > 0;
                bool validPreset = look != null;
                bool available = count <= MaxStationsPerSystem && validOrbit && validPreset;
                if (record.existingOrbit)
                {
                    var anchor = db.Stations.Find(station => station != null && station.index == record.stationIndex && station.system == record.system);
                    if (anchor == null || system?.stations?.Contains(record.stationIndex) != true) continue;
                    if (record.retired)
                    {
                        if (runtimeStations.TryGetValue(anchor, out var old) && old.ProjectId == record.id)
                        {
                            anchor.name = old.OriginalName; anchor.techLevel = old.OriginalTechLevel;
                            runtimeStations.Remove(anchor);
                        }
                        continue;
                    }
                    // Never overlay a newly occupied base/mod orbit on a changed mod set.
                    if (!World.OrbitLayout.VacantStationSite(record.stationIndex, Session.WorldIndex)) continue;
                    if (!runtimeStations.TryGetValue(anchor, out var identity))
                    {
                        identity = new RuntimeStationIdentity { ProjectId = record.id, OriginalName = anchor.name, OriginalTechLevel = anchor.techLevel };
                        runtimeStations.Add(anchor, identity);
                    }
                    if (identity.ProjectId != record.id || !available) continue;
                    // Preserve the planet's original texture, location and membership. Only station metadata changes.
                    anchor.name = record.underConstruction && !record.reconstructing ? identity.OriginalName : record.name;
                    anchor.techLevel = record.underConstruction ? identity.OriginalTechLevel : look.techLevel;
                    continue;
                }
                if (record.retired)
                {
                    system?.stations?.Remove(record.stationIndex);
                    if (record.stationIndex == db.Stations.Count)
                        AddRuntimeStation(db, record, new StationData { index = record.stationIndex, system = record.system, name = record.name });
                    continue;
                }
                string stationName = string.IsNullOrWhiteSpace(record.name)
                    ? $"{record.ownerName}'s Station" : record.name;

                // A station's database index is also used by legacy tables. Reserve it even if its
                // system or appearance has become unavailable, otherwise every later player station
                // would shift into the wrong index. Existing entries are also revisited because a live
                // snapshot can change a project from construction to operational.
                if (record.stationIndex != db.Stations.Count)
                {
                    if (db.Stations.Find(s => s != null && s.index == record.stationIndex) is StationData existing &&
                        runtimeStations.TryGetValue(existing, out var identity) && identity.ProjectId == record.id)
                    {
                        if (available)
                        {
                            existing.name = stationName;
                            existing.system = record.system;
                            existing.systemName = system.name;
                            existing.techLevel = look.techLevel;
                            existing.textureIndex = look.textureIndex;
                            if (record.underConstruction && !record.reconstructing) system.stations.Remove(record.stationIndex);
                            else if (!system.stations.Contains(record.stationIndex)) system.stations.Add(record.stationIndex);
                        }
                        else
                        {
                            system?.stations?.Remove(record.stationIndex);
                            string reason = count > MaxStationsPerSystem ? "the system exceeds the player station limit" :
                                !validOrbit ? "the system is missing or has no orbital locations" : "no valid model preset exists";
                            Debug.LogError($"NetPlayerStations: station {record.id} is unavailable because {reason}; its database index remains reserved.");
                        }
                        continue;
                    }
                    Debug.LogError($"NetPlayerStations: station {record.id} expected index {record.stationIndex}, but the database ends at {db.Stations.Count}; check that the server and client use the same mods.");
                    continue;
                }

                if (!available)
                {
                    string reason = count > MaxStationsPerSystem ? "the system exceeds the player station limit" :
                        !validOrbit ? "the system is missing or has no orbital locations" : "no valid model preset exists";
                    Debug.LogError($"NetPlayerStations: station {record.id} is unavailable because {reason}; its index is reserved but it will not be added to the system.");
                    AddRuntimeStation(db, record, new StationData
                    {
                        index = record.stationIndex,
                        name = stationName,
                        system = record.system,
                        systemName = system?.name ?? "",
                        techLevel = look?.techLevel ?? 0,
                        textureIndex = look?.textureIndex ?? 0,
                    });
                    continue;
                }

                if (record.looksLike < 0) record.looksLike = look.index;
                AddRuntimeStation(db, record, new StationData
                {
                    index = record.stationIndex,
                    name = stationName,
                    system = record.system,
                    systemName = system.name,
                    techLevel = look.techLevel,
                    textureIndex = look.textureIndex,
                });
                if (record.underConstruction && !record.reconstructing) system.stations.Remove(record.stationIndex);
                else if (!system.stations.Contains(record.stationIndex)) system.stations.Add(record.stationIndex);
            }
            NetGuard.RefreshStations();
        }

        static bool CanPublishStationSnapshot(string snapshot)
        {
            if (NetState.FitsPlayerStationSnapshot(snapshot)) return true;
            Debug.LogWarning("NetPlayerStations: change would exceed the player station replication limit.");
            return false;
        }

        /// <summary>Persist a server-side change atomically, retaining the prior registry as a backup.</summary>
        public static bool Save() => Save(publish: true);

        static bool Save(bool publish)
        {
            if (persistenceFaulted || registry == null || string.IsNullOrEmpty(path)) return false;
            if (File.Exists(TransactionPath))
            {
                if (!RecoverPendingTransaction())
                {
                    persistenceFaulted = true;
                    registry = null;
                    Debug.LogError("NetPlayerStations: station persistence has been disabled because a pending transaction failed recovery.");
                }
                return false; // Do not save a caller's potentially stale in-memory mutation over the recovered snapshot.
            }
            string snapshot = publish ? Snapshot : null;
            if (publish && !CanPublishStationSnapshot(snapshot)) return false;
            if (!NetProfiles.Write(path, JsonUtility.ToJson(registry, true))) return false;
            if (publish) NetState.Instance?.PublishPlayerStations(snapshot);
            return true;
        }
    }
}
