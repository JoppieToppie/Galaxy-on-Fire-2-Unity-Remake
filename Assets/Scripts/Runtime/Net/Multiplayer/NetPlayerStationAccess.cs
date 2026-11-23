using System;
using System.Text;
using UnityEngine;

namespace GoF2Remake.Multiplayer
{
    [Flags]
    internal enum StationProjectActions : byte
    {
        None = 0,
        OfferForClaim = 1,
        RebuildWreck = 2,
        FundJumpgate = 4
    }

    public static partial class NetPlayerStations
    {
        static bool IsFactionManager(PlayerStationRecord record, string account)
        {
            if (record == null || string.IsNullOrEmpty(account)) return false;
            string factionId = record.ownershipType == PlayerStationOwnershipType.Faction
                ? record.ownerFactionId : NetFactions.Of(record.ownerProfile)?.id;
            if (string.IsNullOrEmpty(factionId)) return false;
            var faction = NetFactions.Of(account);
            return faction != null && string.Equals(faction.id, factionId, StringComparison.Ordinal) &&
                (string.Equals(faction.leader, account, StringComparison.Ordinal) ||
                 faction.officers?.Contains(account) == true);
        }

        static bool HasFactionFacilityAccess(NetPlayer by, PlayerStationRecord record, string account)
        {
            return IsStationContext(by, record, out account) &&
                (HasStationPermission(record, account, PlayerStationPermission.Storage) ||
                 HasStationPermission(record, account, PlayerStationPermission.Hangar));
        }

        internal static bool CanViewStationRecord(NetPlayer by, PlayerStationRecord record)
        {
            if (!NetGame.IsServer || !NetProfiles.Enabled || by == null || !by.IsSpawned ||
                !NetProfiles.Controls(by.OwnerClientId) || record == null) return false;
            string account = NetProfiles.AccountOf(by.OwnerClientId);
            return HasStationPermission(record, account, PlayerStationPermission.Visit);
        }

        internal static bool CanViewManagement(PlayerStationRecord record)
        {
            if (record == null) return false;
            if (!NetGame.IsServer) return NetPlayer.Local?.ManagedStationOrbit == record.stationIndex;
            string account = NetProfiles.AccountOf(NetGame.LocalId);
            return HasStationPermission(record, account, PlayerStationPermission.StationOperations);
        }

        static double nextGateManagementUpdate;

        static void PublishStationManagementAccess()
        {
            double now = Time.realtimeSinceStartupAsDouble;
            bool refreshGates = now >= nextGateManagementUpdate;
            if (refreshGates) nextGateManagementUpdate = now + 1;
            foreach (var pilot in NetPlayer.All)
            {
                if (pilot == null || !pilot.IsSpawned) continue;
                var station = pilot.InHangar ? StationAt(pilot.Station) : null;
                string account = NetProfiles.Controls(pilot.OwnerClientId) ? NetProfiles.AccountOf(pilot.OwnerClientId) : null;
                bool manages = station != null && !StationClosedByCombat(station.stationIndex) &&
                    HasStationPermission(station, account, PlayerStationPermission.StationOperations);
                pilot.SetManagedStationOrbit(manages ? station.stationIndex : -1);
                pilot.SetStationProjectActions(AvailableStationProjectActions(pilot, station, account));
                if (refreshGates) PublishJumpgateManagementAccess(pilot);
            }
        }

        static StationProjectActions AvailableStationProjectActions(NetPlayer pilot, PlayerStationRecord docked, string account)
        {
            if (string.IsNullOrEmpty(account)) return StationProjectActions.None;
            if (docked != null && !docked.underConstruction && !docked.reconstructing && !docked.wreckClaimOpen &&
                !docked.destroyed && !docked.retired && docked.offeredForClaimUtc <= 0 && IsOwnershipAuthority(docked, account))
                return StationProjectActions.OfferForClaim;
            if (!pilot.InSpace) return StationProjectActions.None;

            var actions = StationProjectActions.None;
            var wreck = StationAt(pilot.Station);
            if (wreck?.wreckClaimOpen == true && RebuildAuthorization(pilot, wreck, out _, out _) == null)
                actions |= StationProjectActions.RebuildWreck;
            var gate = FindJumpgate(pilot.Station);
            if (gate != null && gate.building && gate.startedUtc <= 0 && CanUseJumpgateConstructionStock(gate, account))
                actions |= StationProjectActions.FundJumpgate;
            return actions;
        }

        static void PublishJumpgateManagementAccess(NetPlayer pilot)
        {
            var dock = pilot.InHangar ? NetGame.Db?.Stations?.Find(station => station != null && station.index == pilot.Station) : null;
            if (dock == null || registry?.jumpgates == null)
            {
                pilot.SetManagedJumpgates("");
                return;
            }
            var orbits = new StringBuilder();
            foreach (var gate in registry.jumpgates)
            {
                if (gate == null || gate.system != dock.system || !CanViewJumpgate(pilot, gate)) continue;
                if (orbits.Length >= 3900) break;
                if (orbits.Length > 0) orbits.Append(',');
                orbits.Append(gate.orbitStationIndex);
            }
            pilot.SetManagedJumpgates(orbits.ToString());
        }

        static bool CanManageStorage(NetPlayer by, PlayerStationRecord record, out string account)
        {
            return IsStationContext(by, record, out account) &&
                HasStationPermission(record, account, PlayerStationPermission.Storage);
        }

        internal static string SetStorageAccess(NetPlayer by, int id, string mode)
        {
            var record = Find(id);
            if (record == null) return "No player station with that id exists.";
            if (!IsStationAuthority(by, record, out string account) ||
                record.ownershipType != PlayerStationOwnershipType.Individual)
                return "Only the individual station owner can change this legacy faction-sharing setting.";
            bool hasHangar = record.addons?.Exists(a => a != null && a.kind == "hangar" && a.level > 0) ?? false;
            if (StorageCapacity(record) <= 0 && !hasHangar)
                return "Install a storage or ship hangar module before changing its access policy.";

            mode = (mode ?? "").Trim().ToLowerInvariant();
            if (mode != "owner" && mode != "faction")
                return "Choose owner or faction: /station access <station id> owner|faction.";
            bool enabled = mode == "faction";
            if (record.factionStorageAccess == enabled)
                return enabled ? "Faction storage access is already enabled." : "Storage is already owner-only.";

            if (record.ownershipType == PlayerStationOwnershipType.Faction)
                return "Faction-owned stations use faction membership permissions; individual faction-sharing cannot be changed here.";
            if (enabled && NetFactions.Of(account) == null)
                return "Join a faction before enabling faction storage access.";
            record.factionStorageAccess = enabled;
            record.factionPermissions ??= new PlayerStationFactionPermissionProfile();
            bool previousDefault = record.factionPermissions.storage;
            bool previousHangar = record.factionPermissions.hangar;
            bool previousVisit = record.factionPermissions.visit;
            bool previousOfficerVisit = record.factionPermissions.officerVisit;
            bool previousOfficerStorage = record.factionPermissions.officerStorage;
            bool previousOfficerHangar = record.factionPermissions.officerHangar;
            record.factionPermissions.visit = enabled;
            record.factionPermissions.storage = enabled;
            record.factionPermissions.hangar = enabled;
            record.factionPermissions.officerVisit = enabled;
            record.factionPermissions.officerStorage = enabled;
            record.factionPermissions.officerHangar = enabled;
            int oldHistoryCount = record.history?.Count ?? 0;
            int oldSequence = record.nextHistorySequence;
            RecordHistory(record, account, "legacy-faction-access-changed", null,
                "Legacy access command set member and officer visit, storage and hangar defaults to " +
                (enabled ? "allow" : "deny") + ".");
            if (!Save())
            {
                record.factionStorageAccess = !enabled;
                record.factionPermissions.visit = previousVisit;
                record.factionPermissions.storage = previousDefault;
                record.factionPermissions.hangar = previousHangar;
                record.factionPermissions.officerVisit = previousOfficerVisit;
                record.factionPermissions.officerStorage = previousOfficerStorage;
                record.factionPermissions.officerHangar = previousOfficerHangar;
                if (record.history != null && record.history.Count > oldHistoryCount)
                    record.history.RemoveRange(oldHistoryCount, record.history.Count - oldHistoryCount);
                record.nextHistorySequence = oldSequence;
                return "The storage access policy could not be saved.";
            }
            EjectDeniedVisitors(record);
            return enabled
                ? "Faction access enabled. Current members of your faction can use the station's authorised storage and ship hangar while docked here."
                : "Faction storage access disabled. Only you can use the station's storage and ship hangar.";
        }
    }
}
