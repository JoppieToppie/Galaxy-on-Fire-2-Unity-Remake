using System;

namespace GoF2Remake.Multiplayer
{
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
            string account = NetProfiles.AccountOf(NetGame.LocalId);
            return HasStationPermission(record, account, PlayerStationPermission.StationOperations);
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
            if (!CanManage(by, record, out string account))
                return "Only the station owner can change storage access, while docked at the station.";
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
            record.factionPermissions.storage = enabled;
            if (!Save())
            {
                record.factionStorageAccess = !enabled;
                record.factionPermissions.storage = previousDefault;
                return "The storage access policy could not be saved.";
            }
            return enabled
                ? "Faction access enabled. Current members of your faction can use the station's authorised storage and ship hangar while docked here."
                : "Faction storage access disabled. Only you can use the station's storage and ship hangar.";
        }
    }
}
