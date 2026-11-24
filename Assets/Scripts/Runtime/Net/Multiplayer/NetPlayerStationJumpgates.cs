using System;
using System.Collections.Generic;
using UnityEngine;
using GoF2Remake.Data;

namespace GoF2Remake.Multiplayer
{
    public static partial class NetPlayerStations
    {
        const int MaxJumpgateBuildSeconds = 2592000;
        internal const long JumpgateRegenDelaySeconds = 900;
        // Provisional balancing values; weapon eligibility is independent of ship class.
        internal static float JumpgateMaxIntegrity = 10000f;
        internal static float JumpgateRegenPerSecond = 1f;
        internal static float JumpgateEmpThreshold = 100f;
        internal static long JumpgateEmpDisableSeconds = 120;
        // Fail closed until an explicit weapon-level structural damage policy is registered.
        // EMP effects are independent of this policy.
        internal const int StructuralWeaponAttr = 189;
        internal static Func<int, bool> StructuralWeaponEligible = weaponId =>
        {
            var item = NetGame.Db?.Item(weaponId);
            // Nuke-class ordnance is structural by default. Mods can explicitly opt in other
            // weapons with item attribute 189, independently of the firing ship's class.
            return item != null && (item.Attr(StructuralWeaponAttr) > 0 ||
                (GoF2Remake.Flight.Gun.Kind)item.categoryId == GoF2Remake.Flight.Gun.Kind.Nuke);
        };
        const long JumpgateDestinationCooldownSeconds = 86400;
        static long nextJumpgateRetryUtc;

        static void InitializeGateCombat(PlayerJumpgateRecord gate)
        {
            if (gate == null || gate.combatStateInitialized) return;
            gate.structuralIntegrity = JumpgateMaxIntegrity;
            gate.combatStateInitialized = true;
            gate.structuralIntegrityUpdatedUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }

        internal static float JumpgateScrapIntegrity(PlayerJumpgateRecord gate) =>
            gate.scrapIntegrityInitialized ? gate.structuralIntegrity : Modding.ModPlayerInfrastructure.Current.ScrapIntegrity;

        static void InitializeGateScrap(PlayerJumpgateRecord gate)
        {
            if (gate.scrapIntegrityInitialized) return;
            gate.structuralIntegrity = Modding.ModPlayerInfrastructure.Current.ScrapIntegrity;
            gate.scrapIntegrityInitialized = true;
        }

        static bool RegenerateGate(PlayerJumpgateRecord gate, long now)
        {
            if (gate == null || gate.building || gate.scrapped || gate.permanentlyDestroyed) return false;
            bool initialized = !gate.combatStateInitialized;
            InitializeGateCombat(gate);
            long start = Math.Max(gate.structuralIntegrityUpdatedUtc, gate.lastStructuralDamageUtc + JumpgateRegenDelaySeconds);
            if (now <= start || gate.structuralIntegrity >= JumpgateMaxIntegrity) return initialized;
            gate.structuralIntegrity = Mathf.Min(JumpgateMaxIntegrity,
                gate.structuralIntegrity + (now - start) * Mathf.Max(0f, JumpgateRegenPerSecond));
            gate.structuralIntegrityUpdatedUtc = now;
            return true;
        }

        /// <summary>Server-authoritative combat entry point. Call only after validating the weapon and exact mesh impact.</summary>
        internal static bool ApplyJumpgateCombatHit(int orbitStationIndex, int weaponId, float structuralDamage, float empDamage)
        {
            if (!NetGame.IsServer || !sessionActive || registry == null ||
                !NetGuard.Item(weaponId) || float.IsNaN(structuralDamage) || float.IsInfinity(structuralDamage) ||
                float.IsNaN(empDamage) || float.IsInfinity(empDamage) ||
                structuralDamage < 0f || empDamage < 0f) return false;
            var gate = FindJumpgate(orbitStationIndex);
            if (gate == null || gate.building || gate.permanentlyDestroyed || gate.explosionPending) return false;
            bool structuralEligible = structuralDamage > 0f && StructuralWeaponEligible != null && StructuralWeaponEligible(weaponId);
            bool empEligible = !gate.scrapped && empDamage > 0f && empDamage >= JumpgateEmpThreshold;
            if (!structuralEligible && !empEligible) return false;
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var originalRegistry = registry;
            int index = registry.jumpgates.IndexOf(gate);
            var previous = JsonUtility.FromJson<PlayerJumpgateRecord>(JsonUtility.ToJson(gate));
            if (gate.scrapped) InitializeGateScrap(gate);
            else RegenerateGate(gate, now);
            if (empEligible)
                gate.empShutdownUntilUtc = Math.Max(gate.empShutdownUntilUtc, now + JumpgateEmpDisableSeconds);
            if (structuralEligible)
            {
                gate.structuralIntegrity = Mathf.Max(0f, gate.structuralIntegrity - structuralDamage);
                gate.lastStructuralDamageUtc = now;
                gate.structuralIntegrityUpdatedUtc = now;
            }
            bool destroyed = structuralEligible && gate.structuralIntegrity <= 0f;
            if (destroyed) TransitionDestroyedGate(gate);
            if (Save())
            {
                // The durable shutdown applies immediately to active pilots; do not rely on
                // the next periodic registry snapshot to stop a local gate cinematic.
                if (empEligible && !destroyed)
                    NetPlayer.OrbitRunner(orbitStationIndex)?.BroadcastJumpgateEmp(
                        orbitStationIndex, gate.empShutdownUntilUtc);
                return true;
            }
            if (ReferenceEquals(registry, originalRegistry)) ReplaceGateRecord(originalRegistry, index, previous);
            return false;
        }

        static Registry indexedGateRegistry;
        static int indexedGateCount = -1;
        static readonly Dictionary<int, PlayerJumpgateRecord> gatesByOrbit = new Dictionary<int, PlayerJumpgateRecord>();

        static void ReplaceGateRecord(Registry target, int index, PlayerJumpgateRecord replacement)
        {
            target.jumpgates[index] = replacement;
            if (ReferenceEquals(target, indexedGateRegistry)) indexedGateCount = -1;
        }

        internal static PlayerJumpgateRecord FindJumpgate(int orbitStationIndex)
        {
            int count = registry?.jumpgates?.Count ?? 0;
            if (!ReferenceEquals(indexedGateRegistry, registry) || indexedGateCount != count)
            {
                gatesByOrbit.Clear();
                indexedGateRegistry = registry;
                indexedGateCount = count;
                if (registry?.jumpgates != null)
                    foreach (var gate in registry.jumpgates)
                        if (gate != null && !gatesByOrbit.ContainsKey(gate.orbitStationIndex))
                            gatesByOrbit.Add(gate.orbitStationIndex, gate);
            }
            return gatesByOrbit.TryGetValue(orbitStationIndex, out var result) ? result : null;
        }

        static PlayerStationRecord JumpgateAccessRecord(PlayerJumpgateRecord gate) => gate == null ? null : new PlayerStationRecord
        {
            stationIndex = -1,
            name = "Player jumpgate",
            ownershipType = gate.ownershipType,
            ownerProfile = gate.ownerProfile,
            ownerFactionId = gate.ownerFactionId,
            ownerName = gate.ownerName,
            factionStorageAccess = true
        };

        static bool CanViewJumpgate(NetPlayer by, PlayerJumpgateRecord gate)
        {
            if (by == null || gate == null || !NetProfiles.Enabled ||
                !NetProfiles.Controls(by.OwnerClientId)) return false;
            string account = NetProfiles.AccountOf(by.OwnerClientId);
            if (string.IsNullOrEmpty(account)) return false;
            if (gate.ownershipType == PlayerStationOwnershipType.Individual)
                return string.Equals(account, gate.ownerProfile, StringComparison.Ordinal);
            var faction = NetFactions.Of(account);
            if (faction != null && string.Equals(faction.id, gate.ownerFactionId, StringComparison.Ordinal)) return true;
            var sponsor = Find(gate.constructionStationId > 0 ? gate.constructionStationId : gate.sponsorStationId);
            return sponsor != null && HasRole(sponsor, account, PlayerStationRole.StationAdministrator);
        }

        internal static bool CanViewJumpgateManagement(NetPlayer by, PlayerJumpgateRecord gate) =>
            NetGame.IsServer ? CanViewJumpgate(by, gate) :
                gate != null && by != null && by.CanViewManagedJumpgate(gate.orbitStationIndex);

        static bool CanIssueJumpgateOrder(NetPlayer by, PlayerStationRecord sponsor, out string account)
        {
            account = null;
            if (!IsStationContext(by, sponsor, out account)) return false;
            if (HasBuilderRights(sponsor, account)) return true;
            if (sponsor.ownershipType == PlayerStationOwnershipType.Individual)
                return string.Equals(account, sponsor.ownerProfile, StringComparison.Ordinal) ||
                    HasRole(sponsor, account, PlayerStationRole.StationAdministrator);
            var faction = NetFactions.Of(account);
            return faction != null && string.Equals(faction.id, sponsor.ownerFactionId, StringComparison.Ordinal) &&
                (string.Equals(faction.leader, account, StringComparison.Ordinal) ||
                 HasRole(sponsor, account, PlayerStationRole.StationAdministrator));
        }

        internal static long JumpgateConstructionPausedAt(PlayerJumpgateRecord gate) =>
            gate != null && gate.sponsorStationId > 0 ? Find(gate.sponsorStationId)?.offeredForClaimUtc ?? 0 : 0;

        static bool CanManageJumpgate(NetPlayer by, PlayerJumpgateRecord gate, out string account)
        {
            account = by == null ? null : NetProfiles.AccountOf(by.OwnerClientId);
            if (!NetGame.IsServer || !sessionActive || registry == null || by == null || !by.IsSpawned ||
                !by.InHangar || !NetProfiles.Enabled || !NetProfiles.Controls(by.OwnerClientId) ||
                string.IsNullOrEmpty(account) || gate == null || gate.scrapped || gate.permanentlyDestroyed ||
                JumpgateConstructionPausedAt(gate) > 0) return false;
            if (gate.ownershipType == PlayerStationOwnershipType.Individual)
                return string.Equals(account, gate.ownerProfile, StringComparison.Ordinal);
            var faction = NetFactions.Of(account);
            if (faction == null || !string.Equals(faction.id, gate.ownerFactionId, StringComparison.Ordinal)) return false;
            if (string.Equals(faction.leader, account, StringComparison.Ordinal)) return true;
            var sponsor = Find(gate.constructionStationId > 0 ? gate.constructionStationId : gate.sponsorStationId);
            if (sponsor != null && sponsor.ownershipType == PlayerStationOwnershipType.Faction &&
                string.Equals(sponsor.ownerFactionId, gate.ownerFactionId, StringComparison.Ordinal) &&
                HasRole(sponsor, account, PlayerStationRole.StationAdministrator)) return true;
            string managerAccount = account;
            return registry?.stations?.Exists(station => station != null &&
                station.ownershipType == PlayerStationOwnershipType.Faction &&
                string.Equals(station.ownerFactionId, gate.ownerFactionId, StringComparison.Ordinal) &&
                HasRole(station, managerAccount, PlayerStationRole.StationAdministrator)) == true;
        }

        internal static bool ConstructionGateAllowed(PlayerJumpgateRecord gate, string account, string factionId) => JumpgateAccessAllowed(gate, account, factionId);

        static bool JumpgateAccessAllowed(PlayerJumpgateRecord gate, string account, string factionId)
        {
            if (gate == null || gate.scrapped || gate.permanentlyDestroyed || gate.building || gate.explosionPending ||
                (gate.combatStateInitialized && gate.structuralIntegrity <= 0f) || gate.destinationSystem < 0 ||
                gate.empShutdownUntilUtc > DateTimeOffset.UtcNow.ToUnixTimeSeconds()) return false;
            // A faction owns the infrastructure, but individual members remain subject to the
            // blacklist. Only an individually owned gate's actual owner bypasses its blacklist.
            bool owner = gate.ownershipType == PlayerStationOwnershipType.Individual &&
                !string.IsNullOrEmpty(account) && string.Equals(account, gate.ownerProfile, StringComparison.Ordinal);
            if (owner) return true;
            if (gate.accessMode == PlayerJumpgateAccessMode.OwnerOnly) return false;
            if (gate.blacklist != null)
                foreach (var blocked in gate.blacklist)
                {
                    if (blocked == null || string.IsNullOrEmpty(blocked.id)) continue;
                    if (blocked.type == PlayerJumpgateBlockType.Profile &&
                        string.Equals(blocked.id, account, StringComparison.Ordinal)) return false;
                    if (blocked.type == PlayerJumpgateBlockType.Faction &&
                        string.Equals(blocked.id, factionId, StringComparison.Ordinal)) return false;
                }
            return gate.accessMode != PlayerJumpgateAccessMode.OwningFaction ||
                (!string.IsNullOrEmpty(factionId) && string.Equals(factionId, gate.accessFactionId, StringComparison.Ordinal));
        }

        internal static readonly string[] JumpgateModelNames = { "Terran", "Vossk", "Nivelian", "Midorian" };

        const int ModGatePresetBase = 1000;

        internal static IEnumerable<(int id, string label)> JumpgateModelChoices()
        {
            var settings = Modding.ModPlayerInfrastructure.Current;
            var db = NetGame.Db;
            if (settings.IncludeStockJumpgates)
                for (int i = 0; i < JumpgateModelNames.Length; i++)
                    if (db.AssemblyByName(GoF2Remake.World.OrbitLayout.JumpgatePresetAssembly(i + 1)) != null)
                        yield return (i + 1, JumpgateModelNames[i]);

            var seen = new HashSet<int>();
            foreach (var reference in settings.JumpgateModels)
            {
                var model = ResolveJumpgateModel(reference);
                if (model == null || db.AssemblyByName(model.Assembly) == null || !seen.Add(model.gateSystem)) continue;
                var system = db.Systems.Find(value => value != null && value.index == model.gateSystem);
                yield return (ModGatePresetBase + model.gateSystem,
                    (string.IsNullOrWhiteSpace(model.label) ? system?.name ?? reference : model.label) + " (mod gate)");
            }
        }

        internal static Modding.ModWorld.StationModel ResolveJumpgateModel(string reference) =>
            !string.IsNullOrEmpty(reference) && Modding.ModWorld.TryResolveSystem(reference, out int system)
                ? Modding.ModWorld.GateModelOf(system) : null;

        static bool TrySelectJumpgateModel(int preset, out string reference)
        {
            reference = null;
            if (preset == 0) return Modding.ModPlayerInfrastructure.Current.IncludeStockJumpgates;
            foreach (var choice in JumpgateModelChoices())
            {
                if (choice.id != preset) continue;
                if (preset < ModGatePresetBase) return true;
                foreach (var candidate in Modding.ModPlayerInfrastructure.Current.JumpgateModels)
                    if (ResolveJumpgateModel(candidate)?.gateSystem == preset - ModGatePresetBase)
                    {
                        reference = candidate;
                        return true;
                    }
            }
            return false;
        }

        internal static string JumpgateModelLabel(PlayerJumpgateRecord gate)
        {
            var model = ResolveJumpgateModel(gate.modelReference);
            if (model != null)
                return string.IsNullOrWhiteSpace(model.label)
                    ? NetGame.Db.Systems.Find(system => system != null && system.index == model.gateSystem)?.name ?? gate.modelReference
                    : model.label;
            return gate.modelPreset >= 1 && gate.modelPreset <= JumpgateModelNames.Length
                ? JumpgateModelNames[gate.modelPreset - 1] : "Orbit default";
        }

        internal static bool CanBuildGateInOrbit(int systemIndex, int orbit)
        {
            if (!ValidOrbit(systemIndex, orbit, out var system) || system.jumpgateStation == orbit ||
                Modding.ModWorld.OrbitOf(orbit)?.jumpgate == true) return false;
            var gate = FindJumpgate(orbit);
            return gate == null || gate.permanentlyDestroyed && !gate.explosionPending;
        }

        static bool ValidOrbit(int systemIndex, int orbitStationIndex, out SystemData system)
        {
            system = NetGame.Db?.Systems?.Find(x => x != null && x.index == systemIndex);
            if (system?.stations == null || !system.stations.Contains(orbitStationIndex)) return false;
            return NetGame.Db.Stations.Find(x => x != null && x.index == orbitStationIndex && x.system == systemIndex) != null;
        }

        static bool CopyStationOwnershipToGate(PlayerStationRecord station, PlayerJumpgateRecord gate)
        {
            if (station == null || gate == null) return false;
            bool changed = gate.ownershipType != station.ownershipType ||
                !string.Equals(gate.ownerProfile, station.ownerProfile, StringComparison.Ordinal) ||
                !string.Equals(gate.ownerFactionId, station.ownerFactionId, StringComparison.Ordinal) ||
                !string.Equals(gate.ownerName, station.ownerName, StringComparison.Ordinal);
            string accessFaction = station.ownershipType == PlayerStationOwnershipType.Faction
                ? station.ownerFactionId : NetFactions.Of(station.ownerProfile)?.id;
            changed |= !string.Equals(gate.accessFactionId, accessFaction, StringComparison.Ordinal);
            gate.ownershipType = station.ownershipType;
            gate.ownerProfile = station.ownerProfile;
            gate.ownerFactionId = station.ownerFactionId;
            gate.ownerName = station.ownerName;
            gate.accessFactionId = accessFaction;
            return changed;
        }

        /// <summary>Synchronise every gate sponsored by this station as part of the same registry save.</summary>
        internal static bool SyncJumpgatesForStation(PlayerStationRecord station)
        {
            if (station == null || station.offeredForClaimUtc > 0 || station.wreckClaimOpen || station.destroyed || station.reconstructing ||
                registry?.jumpgates == null) return false;
            bool changed = false;
            foreach (var gate in registry.jumpgates)
                if (gate != null && gate.sponsorStationId == station.id && !gate.permanentlyDestroyed)
                    changed |= CopyStationOwnershipToGate(station, gate);
            return changed;
        }

        static Action RestoreSponsoredGateOwnership(PlayerStationRecord station)
        {
            var saved = new List<(PlayerJumpgateRecord Gate, PlayerStationOwnershipType Type, string Profile,
                string Faction, string Name, string AccessFaction, long Started, long Completes)>();
            foreach (var gate in registry.jumpgates)
                if (gate != null && gate.sponsorStationId == station.id && !gate.permanentlyDestroyed)
                    saved.Add((gate, gate.ownershipType, gate.ownerProfile, gate.ownerFactionId, gate.ownerName, gate.accessFactionId, gate.startedUtc, gate.completesUtc));
            return () =>
            {
                foreach (var entry in saved)
                {
                    entry.Gate.ownershipType = entry.Type;
                    entry.Gate.ownerProfile = entry.Profile;
                    entry.Gate.ownerFactionId = entry.Faction;
                    entry.Gate.ownerName = entry.Name;
                    entry.Gate.accessFactionId = entry.AccessFaction;
                    entry.Gate.startedUtc = entry.Started;
                    entry.Gate.completesUtc = entry.Completes;
                }
            };
        }

        static bool MigrateLegacyJumpgates()
        {
            if (registry?.stations == null) return false;
            bool changed = false;
            registry.jumpgates ??= new List<PlayerJumpgateRecord>();
            foreach (var station in registry.stations)
            {
                if (station == null || station.jumpgateDestinationSystem < 0 ||
                    registry.jumpgates.Exists(g => g != null && g.orbitStationIndex == station.stationIndex)) continue;
                registry.jumpgates.Add(new PlayerJumpgateRecord
                {
                    orbitStationIndex = station.stationIndex,
                    sponsorStationId = station.id,
                    constructionStationId = station.id,
                    constructionIssuerProfile = station.ownerProfile,
                    system = station.system,
                    destinationSystem = station.jumpgateDestinationSystem,
                    ownershipType = station.ownershipType,
                    ownerProfile = station.ownerProfile,
                    ownerFactionId = station.ownerFactionId,
                    ownerName = station.ownerName,
                    accessFactionId = station.ownershipType == PlayerStationOwnershipType.Faction
                        ? station.ownerFactionId : NetFactions.Of(station.ownerProfile)?.id,
                    building = station.jumpgateBuilding,
                    startedUtc = station.jumpgateStartedUtc,
                    completesUtc = station.jumpgateCompletesUtc,
                    buildSeconds = station.jumpgateBuildSeconds,
                    destinationChangedUtc = station.jumpgateBuilding ? 0 :
                        (station.jumpgateCompletesUtc > 0 ? station.jumpgateCompletesUtc : station.createdUtc),
                    materials = station.jumpgateMaterials ?? new List<PlayerStationMaterial>(),
                    contributions = station.jumpgateContributions ?? new List<PlayerStationContribution>()
                });
                changed = true;
            }

            // Older independent gate records did not retain their sponsoring station. Link them only
            // when exactly one station in the same system has the same owner; guessing among multiple
            // candidates could silently transfer somebody else's gate.
            foreach (var gate in registry.jumpgates)
            {
                if (gate == null) continue;
                PlayerStationRecord sponsor = gate.sponsorStationId > 0 ? Find(gate.sponsorStationId) : null;
                if (sponsor == null && gate.sponsorStationId <= 0)
                {
                    PlayerStationRecord candidate = null;
                    int matches = 0;
                    foreach (var station in registry.stations)
                    {
                        if (station == null || station.system != gate.system ||
                            station.ownershipType != gate.ownershipType) continue;
                        bool sameOwner = gate.ownershipType == PlayerStationOwnershipType.Faction
                            ? string.Equals(station.ownerFactionId, gate.ownerFactionId, StringComparison.Ordinal)
                            : string.Equals(station.ownerProfile, gate.ownerProfile, StringComparison.Ordinal);
                        if (!sameOwner) continue;
                        candidate = station;
                        matches++;
                    }
                    if (matches == 1)
                    {
                        sponsor = candidate;
                        gate.sponsorStationId = sponsor.id;
                        changed = true;
                    }
                }
                if (sponsor != null && gate.constructionStationId <= 0)
                {
                    changed |= CopyStationOwnershipToGate(sponsor, gate);
                    gate.constructionStationId = sponsor.id;
                    gate.constructionIssuerProfile = gate.ownerProfile;
                    changed = true;
                }
            }
            return changed;
        }

        /// <summary>Scrap keeps the gate mesh in the orbit until rebuilding or final destruction.</summary>
        public static bool HasJumpgateWreckAtStation(int stationIndex)
        {
            var gate = FindJumpgate(stationIndex);
            return gate != null && gate.scrapped && !gate.permanentlyDestroyed;
        }

        internal static bool HasAttackableJumpgateAtStation(int orbit) =>
            HasJumpgateAtStation(orbit) || HasJumpgateWreckAtStation(orbit);

        /// <summary>Local travel refusal for a player gate that is offline, scrapped or being rebuilt.
        /// Server access and destination requests enforce the same restrictions authoritatively.</summary>
        public static bool JumpgateOffline(int stationIndex)
        {
            var gate = FindJumpgate(stationIndex);
            return gate != null && (gate.building || gate.scrapped || gate.permanentlyDestroyed || gate.explosionPending ||
                (gate.combatStateInitialized && gate.structuralIntegrity <= 0f) ||
                gate.empShutdownUntilUtc > DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        }

        /// <summary>Whether an orbital anchor contains an operational player-built jumpgate.</summary>
        internal static bool HasJumpgateAtStation(int stationIndex)
        {
            var gate = FindJumpgate(stationIndex);
            if (gate == null || gate.destinationSystem < 0 || gate.scrapped || gate.permanentlyDestroyed) return false;
            CompleteJumpgateIfDue(gate);
            return gate.destinationSystem >= 0 && !gate.building && !gate.scrapped && !gate.permanentlyDestroyed;
        }

        /// <summary>
        /// Live reciprocal system connection for emergency misjump routing.
        /// Original static galaxy connections remain valid; newly constructed connections
        /// must have a functioning gate at BOTH ends. This does not grant gate access.
        /// </summary>
        public static bool JumpgateLinkOperational(int fromSystem, int toSystem)
        {
            if (fromSystem < 0 || toSystem < 0 || fromSystem == toSystem) return false;
            var db = NetGame.Db;
            var origin = db?.Systems?.Find(s => s != null && s.index == fromSystem);
            var destination = db?.Systems?.Find(s => s != null && s.index == toSystem);
            if (origin == null || destination == null) return false;
            // Static routes are not managed by the player-station lifecycle.
            if (origin.jumpRoutesTo != null && destination.jumpRoutesTo != null &&
                origin.jumpRoutesTo.Contains(toSystem) && destination.jumpRoutesTo.Contains(fromSystem))
                return true;
            if (!NetGame.Active || !sessionActive || registry?.jumpgates == null) return false;
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            bool forward = false, backward = false;
            foreach (var gate in registry.jumpgates)
            {
                if (gate == null || gate.building || gate.startedUtc <= 0 ||
                    gate.scrapped || gate.permanentlyDestroyed || gate.explosionPending ||
                    gate.empShutdownUntilUtc > now ||
                    (gate.combatStateInitialized && gate.structuralIntegrity <= 0f)) continue;
                if (gate.system == fromSystem && gate.destinationSystem == toSystem) forward = true;
                if (gate.system == toSystem && gate.destinationSystem == fromSystem) backward = true;
                if (forward && backward) return true;
            }
            return false;
        }

        /// <summary>Returns the arrival orbit in the gate's fixed destination system.</summary>
        internal static int JumpgateDestinationStation(int stationIndex)
        {
            if (!HasJumpgateAtStation(stationIndex)) return -1;
            if (FindJumpgate(stationIndex)?.empShutdownUntilUtc > DateTimeOffset.UtcNow.ToUnixTimeSeconds()) return -1;
            var gate = FindJumpgate(stationIndex);
            var target = NetGame.Db?.Systems?.Find(x => x != null && x.index == gate?.destinationSystem);
            if (target?.stations == null || target.stations.Count == 0) return -1;
            return target.stations.Contains(target.jumpgateStation) ? target.jumpgateStation : target.stations[0];
        }

        static void CompleteJumpgateIfDue(PlayerJumpgateRecord gate)
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (gate == null || !gate.building || gate.completesUtc <= 0 ||
                JumpgateConstructionPausedAt(gate) > 0 || now < gate.completesUtc || now < nextJumpgateRetryUtc || !NetGame.IsServer) return;
            bool oldBuilding = gate.building;
            long oldChanged = gate.destinationChangedUtc;
            gate.building = false;
            if (gate.destructionCount == 0 && gate.destinationChangedUtc <= 0) gate.destinationChangedUtc = now;
            if (!Save())
            {
                gate.building = oldBuilding;
                gate.destinationChangedUtc = oldChanged;
                nextJumpgateRetryUtc = now + 5;
            }
            else
            {
                nextJumpgateRetryUtc = 0;
                NetPlayer.OrbitRunner(gate.orbitStationIndex)?.BroadcastJumpgateRestored(gate.orbitStationIndex);
            }
        }

        /// <summary>Called from the authoritative station update loop; due projects survive restarts.</summary>
        internal static void TickJumpgates()
        {
            if (!NetGame.IsServer || !sessionActive || registry?.jumpgates == null) return;
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (now < nextJumpgateRetryUtc) return;
            // Persist regeneration at a coarse cadence and roll back any unsaved heals.
            bool changedIntegrity = false;
            var snapshots = new List<(PlayerJumpgateRecord gate, float integrity, long updated, bool initialized)>();
            foreach (var activeGate in registry.jumpgates)
            {
                if (activeGate == null || (activeGate.structuralIntegrityUpdatedUtc != 0 &&
                    now - activeGate.structuralIntegrityUpdatedUtc < 60)) continue;
                float integrity = activeGate.structuralIntegrity;
                long updated = activeGate.structuralIntegrityUpdatedUtc;
                bool initialized = activeGate.combatStateInitialized;
                if (!RegenerateGate(activeGate, now)) continue;
                snapshots.Add((activeGate, integrity, updated, initialized));
                changedIntegrity = true;
            }
            if (changedIntegrity && !Save())
            {
                foreach (var snapshot in snapshots)
                {
                    snapshot.gate.structuralIntegrity = snapshot.integrity;
                    snapshot.gate.structuralIntegrityUpdatedUtc = snapshot.updated;
                    snapshot.gate.combatStateInitialized = snapshot.initialized;
                }
                nextJumpgateRetryUtc = now + 5;
                return;
            }
            // Explosion dispatch is durable: an orbit without an authority keeps the pending event,
            // and a failed save never broadcasts an uncommitted destruction.
            foreach (var gate in registry.jumpgates)
            {
                if (gate == null || !gate.explosionPending) continue;
                var runner = NetPlayer.OrbitRunner(gate.orbitStationIndex);
                if (runner == null) continue;
                gate.explosionPending = false;
                if (!Save())
                {
                    gate.explosionPending = true;
                    nextJumpgateRetryUtc = now + 5;
                    return;
                }
                runner.BroadcastJumpgateExplosion(gate.orbitStationIndex, gate.permanentlyDestroyed);
            }
            foreach (var gate in registry.jumpgates)
            {
                if (gate == null || !gate.building || gate.startedUtc <= 0 ||
                    gate.completesUtc <= 0 || gate.completesUtc > now || JumpgateConstructionPausedAt(gate) > 0) continue;
                CompleteJumpgateIfDue(gate);
                if (gate.building) { nextJumpgateRetryUtc = now + 5; return; }
            }
        }

        static List<PlayerStationMaterial> RequiredJumpgateMaterials() => ConstructionCost("jumpgate");

        /// <summary>Build at any orbital anchor in the sponsor station's system, not only at the sponsor station itself.</summary>
        internal static string StartJumpgate(NetPlayer by, int sponsorStationId, int orbitStationIndex, int destinationSystem, int buildSeconds, int modelPreset = 0)
        {
            if (!NetGame.IsServer || !sessionActive || registry == null)
                return "Player-built jumpgates need a persistent host or dedicated server.";
            var rules = Modding.ModPlayerInfrastructure.Current;
            if (!rules.Enabled || !rules.JumpgatesEnabled) return "New player jumpgate projects are disabled by the active mods.";
            var sponsor = Find(sponsorStationId);
            if (sponsor == null) return "No player station with that project id exists.";
            if (!CanIssueJumpgateOrder(by, sponsor, out string issuer))
                return "Only the station owner, faction leader, assigned station executive or original builder may issue a jumpgate construction order.";
            if (sponsor.underConstruction) return "The sponsoring station must be operational.";
            if (!ValidOrbit(sponsor.system, orbitStationIndex, out var origin))
                return "Choose an existing orbital location in the sponsoring station's system.";
            if (origin.jumpgateStation == orbitStationIndex || Modding.ModWorld.OrbitOf(orbitStationIndex)?.jumpgate == true)
                return "That orbit already contains a system or mod-defined jumpgate.";
            if (destinationSystem == sponsor.system) return "A jumpgate must connect to a different system.";
            var destination = NetGame.Db?.Systems?.Find(x => x != null && x.index == destinationSystem);
            if (destination?.stations == null || destination.stations.Count == 0)
                return "Choose a valid destination system with at least one orbital location.";
            var issuerFaction = NetFactions.Of(issuer);
            bool factionIssued = sponsor.ownershipType == PlayerStationOwnershipType.Faction &&
                issuerFaction != null && string.Equals(issuerFaction.id, sponsor.ownerFactionId, StringComparison.Ordinal) &&
                (string.Equals(issuerFaction.leader, issuer, StringComparison.Ordinal) ||
                 HasRole(sponsor, issuer, PlayerStationRole.StationAdministrator));
            bool discovered = factionIssued
                ? NetProfiles.HasFactionDiscoveredSystem(sponsor.ownerFactionId, destinationSystem)
                : NetProfiles.HasDiscoveredSystem(issuer, destinationSystem);
            if (!discovered)
                return factionIssued
                    ? "The destination must have been discovered by at least one member of the owning faction."
                    : "You must personally discover the destination system before connecting a gate to it.";
            if (buildSeconds < 0 || buildSeconds > MaxJumpgateBuildSeconds)
                return "Build time must be between 0 and 2,592,000 seconds (30 days). Use 0 for no build timer.";
            if (!TrySelectJumpgateModel(modelPreset, out string modelReference))
                return "Choose an enabled jumpgate model from the construction catalogue.";
            var existingGate = FindJumpgate(orbitStationIndex);
            bool stationRebuild = existingGate != null && existingGate.permanentlyDestroyed;
            if (existingGate != null && !stationRebuild)
                return "That orbit already has a player-built jumpgate, a scrapped gate awaiting reclamation, or an active gate project. Only one gate can occupy an orbit.";

            if (stationRebuild && existingGate.explosionPending)
                return "Visit the destroyed gate's orbit to dispatch its explosion before starting a new build.";

            var gate = new PlayerJumpgateRecord
            {
                orbitStationIndex = orbitStationIndex,
                sponsorStationId = sponsor.id,
                system = sponsor.system,
                destinationSystem = destinationSystem,
                ownershipType = factionIssued ? PlayerStationOwnershipType.Faction : PlayerStationOwnershipType.Individual,
                ownerProfile = factionIssued ? null : issuer,
                ownerFactionId = factionIssued ? sponsor.ownerFactionId : null,
                ownerName = factionIssued ? sponsor.ownerName : by.DisplayName,
                accessFactionId = factionIssued ? sponsor.ownerFactionId : issuerFaction?.id,
                constructionStationId = sponsor.id,
                constructionIssuerProfile = issuer,
                building = true,
                buildSeconds = buildSeconds,
                modelPreset = modelPreset,
                modelReference = modelReference,
                materials = RequiredJumpgateMaterials(),
                contributions = new List<PlayerStationContribution>()
            };
            CopyStationOwnershipToGate(sponsor, gate);
            if (stationRebuild && existingGate.salvageDropPending)
            {
                gate.salvageMaterials = existingGate.salvageMaterials;
                gate.salvageDropPending = true;
            }
            registry.jumpgates ??= new List<PlayerJumpgateRecord>();
            int existingIndex = stationRebuild ? registry.jumpgates.IndexOf(existingGate) : -1;
            var originalRegistry = registry;
            if (stationRebuild && existingIndex >= 0) ReplaceGateRecord(originalRegistry, existingIndex, gate);
            else registry.jumpgates.Add(gate);
            indexedGateCount = -1;
            if (!Save())
            {
                if (ReferenceEquals(registry, originalRegistry))
                {
                    if (stationRebuild && existingIndex >= 0) ReplaceGateRecord(originalRegistry, existingIndex, existingGate);
                    else originalRegistry.jumpgates.Remove(gate);
                    indexedGateCount = -1;
                }
                return "The jumpgate construction plan could not be saved.";
            }
            return "Jumpgate project started in orbit " + orbitStationIndex + " of " + origin.name +
                ", linking to " + destination.name + ". " + ConstructionCostSummary("jumpgate", "Required materials") + ". " +
                (buildSeconds == 0 ? "The gate will become operational as soon as it is fully funded." :
                "After funding, construction will take " + TimeText(buildSeconds) + ".") +
                " Deliver materials in the construction orbit with /station jumpgate-contribute " + orbitStationIndex + " <item id> <amount>.";
        }

        static bool CanUseJumpgateConstructionStock(PlayerJumpgateRecord gate, string account)
        {
            var sponsor = Find(gate.sponsorStationId);
            bool factionOwned = gate.ownershipType == PlayerStationOwnershipType.Faction;
            return CanUseStationConstructionStock(sponsor, account) && gate.ownershipType == sponsor.ownershipType &&
                string.Equals(factionOwned ? gate.ownerFactionId : gate.ownerProfile,
                    factionOwned ? sponsor.ownerFactionId : sponsor.ownerProfile, StringComparison.Ordinal);
        }

        internal static string ContributeJumpgate(NetPlayer by, int orbitStationIndex, int item, int amount, bool fromFactionStock = false)
        {
            if (!NetGame.IsServer || !sessionActive || registry == null)
                return "Player jumpgate construction needs a persistent server.";
            if (by == null || !by.IsSpawned || !by.InSpace || by.Station != orbitStationIndex)
                return "Fly to the jumpgate's construction orbit to deliver materials; contributions cannot be made remotely or while docked.";
            if (!NetProfiles.Enabled || !NetProfiles.Controls(by.OwnerClientId))
                return "Use a controlled player profile to contribute materials.";
            if (amount < 1 || amount > 500 || !NetGuard.Item(item))
                return "Deposit between 1 and 500 units of a valid item.";
            var gate = FindJumpgate(orbitStationIndex);
            if (gate == null || gate.destinationSystem < 0 || !gate.building || gate.startedUtc > 0)
                return "That jumpgate project is not accepting materials.";
            if (JumpgateConstructionPausedAt(gate) > 0)
                return "Jumpgate construction is paused until its sponsoring station is claimed.";
            var material = gate.materials?.Find(x => x != null && x.item == item);
            if (material == null) return "That item is not required for this jumpgate.";
            int remaining = material.required - material.delivered;
            if (amount > remaining) return "The project needs only " + remaining + " more units of that material.";
            string account = NetProfiles.AccountOf(by.OwnerClientId);
            if (string.IsNullOrEmpty(account)) return "Your controlled player profile is unavailable.";

            bool factionOwned = gate.ownershipType == PlayerStationOwnershipType.Faction;
            PlayerStationStorage factionMaterial = null;
            if (fromFactionStock)
            {
                var sponsor = Find(gate.sponsorStationId);
                if (!CanUseJumpgateConstructionStock(gate, account))
                    return "Only the sponsor's owner or owning faction may allocate its station construction stock.";
                factionMaterial = sponsor.donatedConstructionStock?.Find(stack => stack != null && stack.item == item);
                if (factionMaterial == null || factionMaterial.amount < amount)
                    return "The sponsoring station does not have enough of that donated material.";
            }
            bool paidContribution = factionOwned && !fromFactionStock;
            long unitValue = 0, payout = 0;
            if (paidContribution)
            {
                unitValue = Math.Max(1, NetGame.Db?.Item(item)?.minPrice ?? 0);
                if (unitValue > long.MaxValue / amount) return "The contribution value is outside the supported credit range.";
                payout = unitValue * amount;
                long bank = NetFactions.BankAmount(gate.ownerFactionId);
                if (bank < 0) return "The owning faction treasury is unavailable; no materials were taken.";
                if (bank < payout) return "The faction bank needs " + payout.ToString("N0") +
                    " credits to pay this contribution but only has " + bank.ToString("N0") + ". Your materials were not taken.";
            }

            gate.contributions ??= new List<PlayerStationContribution>();
            var contribution = gate.contributions.Find(x => x != null &&
                string.Equals(x.contributorProfile, account, StringComparison.Ordinal) && x.item == item);
            if (contribution == null)
                gate.contributions.Add(contribution = new PlayerStationContribution
                { contributorProfile = account, item = item, amount = 0, unitValue = unitValue,
                    contributedUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds() });
            if (contribution.amount < 0 || contribution.amount > int.MaxValue - amount ||
                contribution.paidCredits < 0 || (paidContribution && contribution.paidCredits > long.MaxValue - payout))
                return "The jumpgate contribution ledger has an invalid or overflowing total; administrator recovery is required.";
            int oldDelivered = material.delivered, oldAmount = contribution.amount;
            long oldUnitValue = contribution.unitValue;
            long oldPaid = contribution.paidCredits, oldPaidUtc = contribution.paidUtc;
            bool oldBuilding = gate.building;
            long oldStarted = gate.startedUtc, oldCompletes = gate.completesUtc, oldChanged = gate.destinationChangedUtc;
            material.delivered += amount;
            contribution.amount += amount;
            if (paidContribution)
            {
                contribution.unitValue = unitValue;
                contribution.paidCredits += payout;
                contribution.paidUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            }
            bool complete = gate.materials.TrueForAll(x => x == null || x.delivered >= x.required);
            if (complete)
            {
                long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                gate.startedUtc = now;
                gate.completesUtc = now + Math.Max(0, gate.buildSeconds);
                gate.building = gate.buildSeconds > 0;
                if (!gate.building && gate.destructionCount == 0) gate.destinationChangedUtc = now;
            }

            if (fromFactionStock) factionMaterial.amount -= amount;
            bool committed = fromFactionStock ? Save() : paidContribution
                ? CommitConstructionContribution(account, gate.ownerFactionId, item, amount, payout)
                : CommitCargoTransaction(account, (item, -amount));
            if (!committed)
            {
                if (fromFactionStock) factionMaterial.amount += amount;
                material.delivered = oldDelivered;
                contribution.amount = oldAmount;
                contribution.unitValue = oldUnitValue;
                contribution.paidCredits = oldPaid;
                contribution.paidUtc = oldPaidUtc;
                gate.building = oldBuilding;
                gate.startedUtc = oldStarted;
                gate.completesUtc = oldCompletes;
                gate.destinationChangedUtc = oldChanged;
                if (contribution.amount <= 0) gate.contributions.Remove(contribution);
                return "The contribution transaction could not be committed; no materials were taken.";
            }
            if (!fromFactionStock && !(NetGame.PersistentHost && by.OwnerClientId == NetGame.LocalId))
                NetState.Instance?.SendAdmin(by.OwnerClientId, NetAdmin.Order.Take, item, amount, 0, "", "Jumpgate construction");
            if (paidContribution) NotifyProfileCreditChange(account, payout, "Jumpgate construction contribution");
            if (complete && !gate.building)
                NetPlayer.OrbitRunner(orbitStationIndex)?.BroadcastJumpgateRestored(orbitStationIndex);
            string message = "Contributed " + amount + " x " + NetGame.Db.Item(item).name + " to jumpgate orbit " + orbitStationIndex + ".";
            if (paidContribution) message += " Paid " + payout.ToString("N0") + " credits from the faction bank.";
            if (complete) message += gate.building ? " Materials complete. Construction timer started." : " Jumpgate is now operational.";
            return message;
        }

        internal static string ChangeJumpgateDestination(NetPlayer by, int orbitStationIndex, int destinationSystem)
        {
            var gate = FindJumpgate(orbitStationIndex);
            if (gate == null) return "No player-built jumpgate exists in that orbit.";
            if (!CanManageJumpgate(by, gate, out _))
                return "Only the gate owner or an authorised faction manager can change its destination while docked.";
            var currentStation = NetGame.Db?.Stations?.Find(x => x != null && x.index == by.Station);
            if (currentStation == null || currentStation.system != gate.system)
                return "Dock at any station in the jumpgate's origin system to change its destination.";
            CompleteJumpgateIfDue(gate);
            if (gate.building || gate.startedUtc <= 0) return "The jumpgate must be fully operational before changing its destination.";
            if (destinationSystem == gate.system) return "A jumpgate must connect to a different system.";
            if (destinationSystem == gate.destinationSystem) return "That jumpgate already points to the selected destination.";
            var destination = NetGame.Db?.Systems?.Find(x => x != null && x.index == destinationSystem);
            if (destination?.stations == null || destination.stations.Count == 0)
                return "Choose a valid destination system with at least one orbital location.";
            bool discovered = gate.ownershipType == PlayerStationOwnershipType.Faction
                ? NetProfiles.HasFactionDiscoveredSystem(gate.ownerFactionId, destinationSystem)
                : NetProfiles.HasDiscoveredSystem(gate.ownerProfile, destinationSystem);
            if (!discovered)
                return gate.ownershipType == PlayerStationOwnershipType.Faction
                    ? "At least one member of the owning faction must discover the destination before the gate can be redirected there."
                    : "The individual gate owner must discover the destination before redirecting this gate.";
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            long readyAt = gate.destinationChangedUtc + JumpgateDestinationCooldownSeconds;
            if (gate.destinationChangedUtc > 0 && now < readyAt)
                return "This gate's destination can next be changed in " + TimeText(readyAt - now) + ". Destinations can be changed once every 24 hours.";
            int previous = gate.destinationSystem;
            long previousChanged = gate.destinationChangedUtc;
            gate.destinationSystem = destinationSystem;
            gate.destinationChangedUtc = now;
            if (!Save())
            {
                gate.destinationSystem = previous;
                gate.destinationChangedUtc = previousChanged;
                return "The destination change could not be saved.";
            }
            return "Jumpgate in orbit " + orbitStationIndex + " now links to " + destination.name +
                ". Its next destination change is available in 24 hours.";
        }

        static bool JumpgateManagementDocked(NetPlayer by, PlayerJumpgateRecord gate)
        {
            var current = NetGame.Db?.Stations?.Find(x => x != null && x.index == by?.Station);
            return by != null && by.InHangar && current != null && current.system == gate.system;
        }

        internal static string SetJumpgateAccess(NetPlayer by, int orbitStationIndex, string mode)
        {
            var gate = FindJumpgate(orbitStationIndex);
            if (gate == null) return "No player-built jumpgate exists in that orbit.";
            if (!CanManageJumpgate(by, gate, out _) || !JumpgateManagementDocked(by, gate))
                return "Only the gate owner or assigned faction executive may change access while docked in its origin system.";
            mode = (mode ?? "").Trim().ToLowerInvariant();
            PlayerJumpgateAccessMode next;
            if (mode == "everyone") next = PlayerJumpgateAccessMode.Everyone;
            else if (mode == "faction" || mode == "owningfaction") next = PlayerJumpgateAccessMode.OwningFaction;
            else if (mode == "blacklist") next = PlayerJumpgateAccessMode.Blacklist;
            else if (mode == "owner")
            {
                if (gate.ownershipType != PlayerStationOwnershipType.Individual)
                    return "Owner-only access is for individually owned gates. Use faction access for a faction gate.";
                next = PlayerJumpgateAccessMode.OwnerOnly;
            }
            else return "Choose everyone, faction, blacklist or owner.";
            var previous = gate.accessMode;
            gate.accessMode = next;
            if (!Save())
            {
                gate.accessMode = previous;
                return "The jumpgate access policy could not be saved.";
            }
            return "Jumpgate access is now " + (next == PlayerJumpgateAccessMode.Everyone ? "open to everyone" :
                next == PlayerJumpgateAccessMode.OwningFaction ? "restricted to the owning faction" :
                next == PlayerJumpgateAccessMode.OwnerOnly ? "restricted to the individual owner" :
                "open except to blacklisted profiles and factions") + ".";
        }

        internal static string SetJumpgateBlacklist(NetPlayer by, int orbitStationIndex,
            string action, string targetType, string target)
        {
            var gate = FindJumpgate(orbitStationIndex);
            if (gate == null) return "No player-built jumpgate exists in that orbit.";
            if (!CanManageJumpgate(by, gate, out _) || !JumpgateManagementDocked(by, gate))
                return "Only the gate owner or assigned faction executive may edit its blacklist while docked in its origin system.";
            action = (action ?? "").Trim().ToLowerInvariant();
            targetType = (targetType ?? "").Trim().ToLowerInvariant();
            if (action != "add" && action != "remove") return "Choose add or remove.";
            PlayerJumpgateBlockType type;
            string id;
            if (targetType == "player")
            {
                type = PlayerJumpgateBlockType.Profile;
                id = ResolveStationAccount(target);
                if (string.IsNullOrEmpty(id)) return "Supply a valid player profile or account name.";
            }
            else if (targetType == "faction")
            {
                type = PlayerJumpgateBlockType.Faction;
                var faction = NetFactions.FindById((target ?? "").Trim());
                if (faction == null && (target ?? "").StartsWith("online:", StringComparison.OrdinalIgnoreCase))
                    faction = NetFactions.Of(ResolveStationAccount(target));
                id = faction?.id;
                if (string.IsNullOrEmpty(id)) return "Supply a valid faction ID.";
            }
            else return "Choose player or faction.";
            gate.blacklist ??= new List<PlayerJumpgateBlockEntry>();
            var existing = gate.blacklist.Find(x => x != null && x.type == type &&
                string.Equals(x.id, id, StringComparison.Ordinal));
            if (action == "add")
            {
                if (existing != null) return "That profile or faction is already blacklisted.";
                gate.blacklist.Add(new PlayerJumpgateBlockEntry { type = type, id = id });
            }
            else
            {
                if (existing == null) return "That profile or faction is not on this gate's blacklist.";
                gate.blacklist.Remove(existing);
            }
            if (!Save())
            {
                if (action == "add") gate.blacklist.RemoveAll(x => x != null && x.type == type &&
                    string.Equals(x.id, id, StringComparison.Ordinal));
                else gate.blacklist.Add(existing);
                return "The jumpgate blacklist change could not be saved.";
            }
            return action == "add" ? "Added " + targetType + " to the jumpgate blacklist." :
                "Removed " + targetType + " from the jumpgate blacklist.";
        }

        internal static bool CanUseJumpgate(NetPlayer by, int orbitStationIndex)
        {
            var gate = FindJumpgate(orbitStationIndex);
            if (gate == null || by == null || !by.IsSpawned) return false;
            if (!NetGame.IsServer)
                return by.HasPlayerJumpgateAccess(orbitStationIndex) && !gate.building && !gate.scrapped &&
                    !gate.permanentlyDestroyed && !gate.explosionPending &&
                    gate.empShutdownUntilUtc <= DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            string account = NetProfiles.AccountOf(by.OwnerClientId);
            return JumpgateAccessAllowed(gate, account, NetFactions.Of(account)?.id);
        }

        /// <summary>Server validation for the owner-only interaction RPC. Returns -1 unless the player is at an accessible, operational gate.</summary>
        internal static int JumpgateDestinationForInteraction(NetPlayer by, int orbitStationIndex)
        {
            if (!NetGame.IsServer || !sessionActive || ProfileWritesBlocked || by == null || !by.IsSpawned ||
                !by.InSpace || by.Hull <= 0f || by.Station != orbitStationIndex || !by.HasCurrentWreckClaimPose ||
                !NetProfiles.Controls(by.OwnerClientId) || !CanUseJumpgate(by, orbitStationIndex)) return -1;
            var layout = GoF2Remake.World.OrbitLayout.Build(NetGame.Db, orbitStationIndex, includePlayerGateLandmark: true);
            if (layout == null || !layout.hasJumpgate) return -1;
            var centre = GoF2Remake.World.OrbitLayout.ToUnity(layout.Place(layout.jumpgate));
            float radius = layout.JumpgateRadius * GoF2Remake.World.OrbitLayout.MetersPerUnit;
            if (!GateInteractionInRange(by.Position, centre, radius)) return -1;
            return FindJumpgate(orbitStationIndex)?.destinationSystem ?? -1;
        }

        // Allow a bounded margin for the most recent network pose, not arbitrary orbit-wide activation.
        internal static bool GateInteractionInRange(Vector3 position, Vector3 centre, float radius)
        {
            if (!NetGuard.Position(position) || !NetGuard.Position(centre) || !NetGuard.Finite(radius) || radius <= 0f) return false;
            float reach = radius + Mathf.Clamp(radius * 0.25f, 100f, 250f);
            return (position - centre).sqrMagnitude <= reach * reach;
        }

        internal static string JumpgateOrbits(int systemIndex)
        {
            var system = NetGame.Db?.Systems?.Find(x => x != null && x.index == systemIndex);
            if (system?.stations == null) return "No system with that index exists.";
            var lines = new List<string> { "Orbital locations in " + system.name + ":" };
            foreach (int orbit in system.stations)
            {
                var station = NetGame.Db.Stations.Find(x => x != null && x.index == orbit);
                var gate = FindJumpgate(orbit);
                string label = station?.name ?? ("Orbit " + orbit);
                if (system.jumpgateStation == orbit || Modding.ModWorld.OrbitOf(orbit)?.jumpgate == true) label += " [system/mod jumpgate]";
                else if (gate != null) label += gate.scrapped ? " [scrapped player gate]" :
                    gate.permanentlyDestroyed ? " [destroyed player gate]" :
                    gate.building ? " [player gate under construction]" : " [player jumpgate]";
                else label += " [available]";
                lines.Add("Orbit " + orbit + ": " + label);
            }
            return string.Join("\n", lines);
        }

        /// <summary>Every operational gate becomes scrap first. A separate hit can destroy exhausted scrap.</summary>
        static void TransitionDestroyedGate(PlayerJumpgateRecord gate)
        {
            if (gate == null || gate.permanentlyDestroyed) return;
            bool wasScrap = gate.scrapped;
            if (!wasScrap && gate.destructionCount < int.MaxValue)
                gate.destructionCount = Math.Max(1, gate.destructionCount + 1);
            gate.building = false;
            gate.startedUtc = gate.completesUtc = 0;
            gate.structuralIntegrity = 0f;
            gate.empShutdownUntilUtc = 0;
            gate.explosionPending = true;
            if (!wasScrap)
            {
                gate.scrapped = true;
                gate.scrapIntegrityInitialized = false;
                InitializeGateScrap(gate);
            }
            else
            {
                gate.scrapped = false;
                gate.permanentlyDestroyed = true;
                gate.destinationSystem = -1;
                // A prior replacement can leave uncollected salvage in this orbit.
                if (!gate.salvageDropPending || gate.salvageMaterials == null)
                    gate.salvageMaterials = new List<PlayerStationMaterial>();
                foreach (var material in gate.materials ?? new List<PlayerStationMaterial>())
                {
                    if (material == null || material.required <= 0 || !NetGuard.Item(material.item)) continue;
                    int delivered = Math.Clamp(material.delivered, 0, material.required);
                    int amount = (int)(((long)delivered + 3) / 4);
                    if (amount <= 0) continue;
                    var existing = gate.salvageMaterials.Find(stack => stack != null && stack.item == material.item);
                    if (existing == null) gate.salvageMaterials.Add(new PlayerStationMaterial { item = material.item, required = amount });
                    else existing.required = (int)Math.Min(int.MaxValue, (long)existing.required + amount);
                }
                gate.salvageDropPending = gate.salvageMaterials.Count > 0;
            }
        }

        /// <summary>
        /// An operational destruction event can leave scrap. Final destruction requires structural damage to that scrap.
        /// </summary>
        internal static bool MarkJumpgateDestroyed(int orbitStationIndex, out List<PlayerStationMaterial> salvage)
        {
            salvage = new List<PlayerStationMaterial>();
            if (!NetGame.IsServer || !sessionActive || registry == null) return false;
            var gate = FindJumpgate(orbitStationIndex);
            if (gate == null || gate.building || gate.scrapped || gate.permanentlyDestroyed) return false;
            var originalRegistry = registry;
            int index = registry.jumpgates.IndexOf(gate);
            var previous = JsonUtility.FromJson<PlayerJumpgateRecord>(JsonUtility.ToJson(gate));
            TransitionDestroyedGate(gate);
            if (gate.salvageDropPending)
                salvage = gate.salvageMaterials.ConvertAll(x =>
                    new PlayerStationMaterial { item = x.item, required = x.required, delivered = 0 });
            if (!Save())
            {
                if (ReferenceEquals(registry, originalRegistry)) ReplaceGateRecord(originalRegistry, index, previous);
                salvage.Clear();
                return false;
            }
            return true;
        }

        static bool OwnsOperationalStationForGateClaim(string account) =>
            registry.stations?.Exists(station => station != null && !station.underConstruction && station.offeredForClaimUtc <= 0 &&
                !station.retired && !station.destroyed && !station.wreckClaimOpen &&
                station.ownershipType == PlayerStationOwnershipType.Individual &&
                string.Equals(station.ownerProfile, account, StringComparison.Ordinal)) == true;

        internal static string ClaimScrappedJumpgate(NetPlayer by, int orbitStationIndex, string claimType, int destinationSystem = -1,
            int modelPreset = 0)
        {
            if (!NetGame.IsServer || !sessionActive || registry == null)
                return "Jumpgate reclamation needs a persistent host or dedicated server.";
            if (by == null || !by.IsSpawned || !by.InSpace || by.Station != orbitStationIndex ||
                !NetProfiles.Enabled || !NetProfiles.Controls(by.OwnerClientId))
                return "Fly to the scrapped gate's orbit to claim it using a controlled profile.";
            var gate = FindJumpgate(orbitStationIndex);
            if (gate == null || !gate.scrapped || gate.permanentlyDestroyed)
                return "That gate is not in its reclaimable scrapped state.";
            if (JumpgateConstructionPausedAt(gate) > 0)
                return "This gate cannot change ownership until its sponsoring station is claimed.";
            if (gate.explosionPending) return "Wait for the gate's destruction to finish before claiming the wreck.";
            string account = NetProfiles.AccountOf(by.OwnerClientId);
            if (string.IsNullOrEmpty(account)) return "Your controlled player profile is unavailable.";
            var faction = NetFactions.Of(account);
            bool ownsStation = OwnsOperationalStationForGateClaim(account);
            claimType = (claimType ?? "").Trim().ToLowerInvariant();
            bool factionClaim = claimType == "faction";
            if (claimType != "faction" && claimType != "individual")
                return "Choose faction or individual.";
            if (factionClaim && faction == null)
                return "Only a faction member may reclaim a gate on behalf of their faction.";
            if (!factionClaim && !ownsStation)
                return "An individual reclaim requires you to own an operational player station. Faction members may claim on behalf of their faction.";
            string modelReference = null;
            if (modelPreset != 0 && !TrySelectJumpgateModel(modelPreset, out modelReference))
                return "Choose an enabled jumpgate model from the construction catalogue.";
            int newDestination = destinationSystem >= 0 ? destinationSystem : gate.destinationSystem;
            var destination = NetGame.Db?.Systems?.Find(x => x != null && x.index == newDestination);
            if (destination?.stations == null || destination.stations.Count == 0 || newDestination == gate.system)
                return "Choose a valid destination system different from the gate's origin.";
            bool destinationKnown = factionClaim
                ? NetProfiles.HasFactionDiscoveredSystem(faction.id, newDestination)
                : NetProfiles.HasDiscoveredSystem(account, newDestination);
            if (!destinationKnown)
                return factionClaim
                    ? "At least one member of the claiming faction must discover the selected destination."
                    : "You must personally discover the selected destination before reclaiming this gate.";
            var originalRegistry = registry;
            int index = registry.jumpgates.IndexOf(gate);
            var previous = JsonUtility.FromJson<PlayerJumpgateRecord>(JsonUtility.ToJson(gate));
            gate.ownershipType = factionClaim ? PlayerStationOwnershipType.Faction : PlayerStationOwnershipType.Individual;
            gate.ownerProfile = factionClaim ? null : account;
            gate.ownerFactionId = factionClaim ? faction.id : null;
            gate.ownerName = factionClaim ? faction.name : by.DisplayName;
            gate.accessFactionId = faction?.id;
            gate.accessMode = PlayerJumpgateAccessMode.Everyone;
            gate.blacklist = new List<PlayerJumpgateBlockEntry>();
            gate.destinationChangedUtc = 0;
            gate.destinationSystem = newDestination;
            if (modelPreset > 0)
            {
                gate.modelPreset = modelPreset;
                gate.modelReference = modelReference;
            }
            gate.constructionStationId = -1;
            gate.sponsorStationId = -1;
            gate.constructionIssuerProfile = account;
            gate.scrapped = false;
            gate.scrapIntegrityInitialized = false;
            gate.permanentlyDestroyed = false;
            gate.building = true;
            gate.startedUtc = gate.completesUtc = 0;
            gate.materials = RequiredJumpgateMaterials();
            gate.contributions = new List<PlayerStationContribution>();
            gate.combatStateInitialized = false;
            gate.structuralIntegrity = 0f;
            gate.lastStructuralDamageUtc = gate.structuralIntegrityUpdatedUtc = gate.empShutdownUntilUtc = 0;
            gate.explosionPending = false;
            if (!Save())
            {
                if (ReferenceEquals(registry, originalRegistry)) ReplaceGateRecord(originalRegistry, index, previous);
                return "The reclaimed gate could not be saved; its scrapped state is unchanged.";
            }
            return "Jumpgate claimed " + (factionClaim ? "on behalf of " + faction.name : "by " + by.DisplayName) +
                ", connecting to " + destination.name + ". Deliver a fresh set of construction materials in this orbit to rebuild it. " +
                "The new owner can set access while docked in this system with /station jumpgate-access " + orbitStationIndex + ".";
        }

        internal static string JumpgateStatus(NetPlayer by, int orbitStationIndex)
        {
            var gate = FindJumpgate(orbitStationIndex);
            if (gate == null) return "No player-built jumpgate exists in orbit " + orbitStationIndex + ".";
            if (!CanViewJumpgate(by, gate) && !gate.scrapped && !gate.permanentlyDestroyed)
                return "Jumpgate project details are private to its owner and authorised faction members.";
            if (gate.scrapped || gate.permanentlyDestroyed)
            {
                var recovery = new List<string>
                {
                    "Jumpgate in orbit " + orbitStationIndex,
                    "Status: " + (gate.scrapped ? "Scrapped and reclaimable" : "Destroyed beyond reclamation"),
                    "Destruction count: " + gate.destructionCount,
                    gate.scrapped
                        ? "Claim with /station jumpgate-claim " + orbitStationIndex + " faction|individual [destination system id]. Faction members may claim for their faction; individual claims require station ownership."
                        : "A new construction order from an operational player station is required."
                };
                if (gate.scrapped) recovery.Add($"Scrap integrity: {JumpgateScrapIntegrity(gate):N0}");
                if (gate.salvageDropPending) recovery.Add("Construction-material salvage remains available for tractor pickup near the destroyed gate.");
                return string.Join("\n", recovery);
            }
            CompleteJumpgateIfDue(gate);
            string destination = gate.destinationSystem >= 0
                ? NetGame.Db?.Systems?.Find(x => x != null && x.index == gate.destinationSystem)?.name ?? ("System " + gate.destinationSystem)
                : "Not assigned";
            string origin = NetGame.Db?.Systems?.Find(x => x != null && x.index == gate.system)?.name
                ?? ("System " + gate.system);
            var lines = new List<string> { "Jumpgate in orbit " + orbitStationIndex + " (" + origin + ")",
                "Destination: " + destination,
                "Model: " + JumpgateModelLabel(gate),
                "Access: " + (gate.accessMode == PlayerJumpgateAccessMode.Everyone ? "Everyone" :
                    gate.accessMode == PlayerJumpgateAccessMode.OwningFaction ? "Owning faction only" :
                    gate.accessMode == PlayerJumpgateAccessMode.OwnerOnly ? "Individual owner only" : "Blacklist mode"),
                "Blacklisted profiles/factions: " + (gate.blacklist?.Count ?? 0),
                "Owner: " + (gate.ownershipType == PlayerStationOwnershipType.Faction
                    ? (NetFactions.FindById(gate.ownerFactionId)?.tag ?? gate.ownerName ?? "Owning faction")
                    : (gate.ownerName ?? gate.ownerProfile ?? "Unknown")) };
            if (gate.materials != null)
                foreach (var m in gate.materials)
                    if (m != null) lines.Add((NetGame.Db?.Item(m.item)?.name ?? ("Item " + m.item)) +
                        ": " + m.delivered + "/" + m.required);
            long pausedAt = JumpgateConstructionPausedAt(gate);
            if (gate.building && pausedAt > 0) lines.Add("Construction is paused until the sponsoring station is claimed.");
            if (gate.startedUtc <= 0) lines.Add("Status: awaiting materials");
            else if (gate.building)
            {
                long remaining = Math.Max(0, gate.completesUtc - (pausedAt > 0 ? pausedAt : DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
                lines.Add("Status: under construction, " + TimeText(remaining) + " remaining");
            }
            else lines.Add("Status: operational");
            if (!gate.building && gate.destinationChangedUtc > 0)
            {
                long readyAt = gate.destinationChangedUtc + JumpgateDestinationCooldownSeconds;
                lines.Add("Destination can next be changed: " +
                    (readyAt <= DateTimeOffset.UtcNow.ToUnixTimeSeconds() ? "now" :
                    "in " + TimeText(readyAt - DateTimeOffset.UtcNow.ToUnixTimeSeconds())));
            }
            lines.Add("One gate per orbit. Destination changes are limited to once every 24 hours.");
            return string.Join("\n", lines);
        }

        static string TimeText(long seconds)
        {
            seconds = Math.Max(0, seconds);
            if (seconds >= 86400) return (seconds / 86400) + "d " + ((seconds % 86400) / 3600) + "h";
            if (seconds >= 3600) return (seconds / 3600) + "h " + ((seconds % 3600) / 60) + "m";
            if (seconds >= 60) return (seconds / 60) + "m " + (seconds % 60) + "s";
            return seconds + "s";
        }
    }
}
