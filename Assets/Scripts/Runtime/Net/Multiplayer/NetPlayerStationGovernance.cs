using System;
using System.Collections.Generic;

namespace GoF2Remake.Multiplayer
{
    public enum PlayerStationPermissionState { Inherit = 0, Allow = 1, Deny = 2 }
    public enum PlayerStationPermission
    {
        Visit = 0, Storage = 1, Hangar = 2, StationOperations = 3,
        Construction = 4, Appearance = 5, Finance = 6, Marketplace = 7, Security = 8
    }
    public enum PlayerStationRole
    {
        StationAdministrator = 0, Quartermaster = 1, ConstructionManager = 2,
        MarketManager = 3, SecurityOfficer = 4
    }
    public enum PlayerStationBanSource { Faction = 0, OriginalBuilder = 1 }

    [Serializable]
    public sealed class PlayerStationPermissionOverride
    {
        public string account;
        public PlayerStationPermission permission;
        public PlayerStationPermissionState state = PlayerStationPermissionState.Inherit;
    }

    [Serializable]
    public sealed class PlayerStationRoleAssignment
    {
        public string account;
        public PlayerStationRole role;
        public string assignedBy;
        public long assignedUtc;
    }

    [Serializable]
    public sealed class PlayerStationBan
    {
        public string account;
        public PlayerStationBanSource source;
        public string imposedBy;
        public string reason;
        public long createdUtc;
    }

    [Serializable]
    public sealed class PlayerStationFactionPermissionProfile
    {
        public bool visit = true;
        public bool storage;
        public bool hangar;
        public bool stationOperations;
        public bool construction;
        public bool appearance;
        public bool finance;
        public bool marketplace;
        public bool security;
    }

    [Serializable]
    public sealed class PlayerStationHistoryEntry
    {
        public int sequence;
        public long timestampUtc;
        public string actorAccount;
        public string action;
        public string subjectAccount;
        public string details;
    }

    public static partial class NetPlayerStations
    {
        static bool NormalizeGovernance(PlayerStationRecord record)
        {
            if (record == null) return false;
            bool changed = false;
            record.permissionOverrides ??= new List<PlayerStationPermissionOverride>();
            record.roleAssignments ??= new List<PlayerStationRoleAssignment>();
            record.bans ??= new List<PlayerStationBan>();
            record.history ??= new List<PlayerStationHistoryEntry>();
            record.factionPermissions ??= new PlayerStationFactionPermissionProfile();

            // Older individually owned stations predate builder identity. Their recorded owner is
            // the only safe migration source. Faction-owned legacy stations are deliberately left
            // without an invented builder identity.
            if (string.IsNullOrEmpty(record.originalBuilderProfile) &&
                record.ownershipType == PlayerStationOwnershipType.Individual &&
                !string.IsNullOrEmpty(record.ownerProfile))
            {
                record.originalBuilderProfile = record.ownerProfile;
                changed = true;
            }

            int next = Math.Max(1, record.nextHistorySequence);
            foreach (var entry in record.history)
                if (entry != null) next = Math.Max(next, entry.sequence + 1);
            if (record.nextHistorySequence != next)
            {
                record.nextHistorySequence = next;
                changed = true;
            }
            return changed;
        }

        internal static bool HasBuilderRights(PlayerStationRecord record, string account)
        {
            return record != null && !string.IsNullOrEmpty(account) &&
                !string.IsNullOrEmpty(record.originalBuilderProfile) &&
                record.builderRightsRelinquishedUtc <= 0 &&
                string.Equals(record.originalBuilderProfile, account, StringComparison.Ordinal);
        }

        static void RecordHistory(PlayerStationRecord record, string actor, string action,
            string subject = null, string details = null)
        {
            if (record == null || string.IsNullOrEmpty(action)) return;
            record.history ??= new List<PlayerStationHistoryEntry>();
            if (record.nextHistorySequence < 1) record.nextHistorySequence = 1;
            record.history.Add(new PlayerStationHistoryEntry
            {
                sequence = record.nextHistorySequence++,
                timestampUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                actorAccount = actor ?? "",
                action = action,
                subjectAccount = subject ?? "",
                details = details ?? ""
            });
        }

        internal static string GovernanceHistory(NetPlayer by, int stationId, int count = 50)
        {
            var record = Find(stationId);
            if (record == null) return "No player station with that id exists.";
            if (!CanViewStationRecord(by, record))
                return "Station governance history is private to the owner, original builder and authorised faction managers.";
            string account = by == null ? null : NetProfiles.AccountOf(by.OwnerClientId);
            if (!HasBuilderRights(record, account) && !CanManage(by, record, out _))
                return "You are not authorised to view this station's governance history.";
            var lines = new List<string> { "Governance history for " + record.name + ":" };
            var history = record.history ?? new List<PlayerStationHistoryEntry>();
            int start = Math.Max(0, history.Count - Math.Max(1, Math.Min(200, count)));
            for (int i = start; i < history.Count; i++)
            {
                var entry = history[i];
                if (entry == null) continue;
                string when = entry.timestampUtc > 0
                    ? DateTimeOffset.FromUnixTimeSeconds(entry.timestampUtc).ToString("u") : "unknown time";
                lines.Add("#" + entry.sequence + " [" + when + "] " +
                    (string.IsNullOrEmpty(entry.actorAccount) ? "System" : entry.actorAccount) +
                    " — " + entry.action +
                    (string.IsNullOrEmpty(entry.subjectAccount) ? "" : " — " + entry.subjectAccount) +
                    (string.IsNullOrEmpty(entry.details) ? "" : ": " + entry.details));
            }
            if (lines.Count == 1) lines.Add("No governance events have been recorded yet.");
            return string.Join("\n", lines);
        }
    }
}
