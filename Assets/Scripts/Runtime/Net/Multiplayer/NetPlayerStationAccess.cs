using System;

namespace GoF2Remake.Multiplayer
{
    public static partial class NetPlayerStations
    {
        static bool CanManageStorage(NetPlayer by, PlayerStationRecord record, out string account)
        {
            account = by == null ? null : NetProfiles.AccountOf(by.OwnerClientId);
            if (CanManage(by, record, out account)) return true;
            if (!NetGame.IsServer || !sessionActive || registry == null || record == null ||
                !record.factionStorageAccess || by == null || !by.IsSpawned || !by.InHangar ||
                by.Station != record.stationIndex || !NetProfiles.Enabled || !NetProfiles.Controls(by.OwnerClientId) ||
                string.IsNullOrEmpty(account) || string.IsNullOrEmpty(record.ownerProfile))
                return false;

            var ownerFaction = NetFactions.Of(record.ownerProfile);
            var memberFaction = NetFactions.Of(account);
            return ownerFaction != null && memberFaction != null &&
                string.Equals(ownerFaction.id, memberFaction.id, StringComparison.Ordinal);
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

            if (enabled && NetFactions.Of(account) == null)
                return "Join a faction before enabling faction storage access.";
            record.factionStorageAccess = enabled;
            if (!Save())
            {
                record.factionStorageAccess = !enabled;
                return "The storage access policy could not be saved.";
            }
            return enabled
                ? "Faction access enabled. Current members of your faction can use the station's authorised storage and ship hangar while docked here."
                : "Faction storage access disabled. Only you can use the station's storage and ship hangar.";
        }
    }
}
