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

        internal static string SetFactionSuccessor(NetPlayer by, int stationId, string targetKind, string target)
        {
            var record = Find(stationId);
            if (record == null) return "No player station with that id exists.";
            if (record.ownershipType != PlayerStationOwnershipType.Faction || string.IsNullOrEmpty(record.ownerFactionId))
                return "Only faction-owned stations have faction succession nominations.";
            if (!CanManage(by, record, out string account))
                return "Dock at the station using a controlled faction profile to manage succession.";
            var faction = NetFactions.Of(account);
            bool leader = faction != null && string.Equals(faction.id, record.ownerFactionId, StringComparison.Ordinal) &&
                string.Equals(faction.leader, account, StringComparison.Ordinal);
            bool delegated = faction != null && string.Equals(faction.id, record.ownerFactionId, StringComparison.Ordinal) &&
                string.Equals(record.factionSuccessorDelegateProfile, account, StringComparison.Ordinal) &&
                faction.officers?.Contains(account) == true;
            if (!leader && !delegated)
                return "Only the faction leader, or an officer delegated for this station, may change its successor.";

            targetKind = (targetKind ?? "").Trim().ToLowerInvariant();
            string targetAccount = null, targetFactionId = null;
            PlayerStationOwnershipType targetType = PlayerStationOwnershipType.Individual;
            if (targetKind != "clear")
            {
                targetAccount = NetProfiles.FindAccount((target ?? "").Trim());
                if (string.IsNullOrEmpty(targetAccount))
                    return "Supply a valid player account or use clear to remove the nomination.";
                if (targetKind == "player")
                {
                    if (string.Equals(targetAccount, account, StringComparison.Ordinal) && leader)
                        return "The current faction leader cannot nominate themselves as the station's successor.";
                }
                else if (targetKind == "faction")
                {
                    var targetFaction = NetFactions.Of(targetAccount);
                    if (targetFaction == null || string.IsNullOrEmpty(targetFaction.id) ||
                        string.Equals(targetFaction.id, record.ownerFactionId, StringComparison.Ordinal))
                        return "Choose a different existing faction as successor.";
                    targetType = PlayerStationOwnershipType.Faction;
                    targetFactionId = targetFaction.id;
                    targetAccount = targetFaction.leader;
                }
                else return "Choose player, faction or clear.";
            }

            var oldProfile = record.factionSuccessorProfile;
            var oldFaction = record.factionSuccessorFactionId;
            var oldType = record.factionSuccessorType;
            int oldHistoryCount = record.history?.Count ?? 0;
            int oldSequence = record.nextHistorySequence;
            record.factionSuccessorProfile = targetAccount;
            record.factionSuccessorFactionId = targetFactionId;
            record.factionSuccessorType = targetType;
            string description = targetKind == "clear" ? "Faction successor nomination cleared." :
                targetType == PlayerStationOwnershipType.Faction ? "Successor faction " + targetFactionId :
                "Successor player " + targetAccount;
            RecordHistory(record, account, "faction-successor-changed", targetAccount, description);
            if (Save()) return description;
            record.factionSuccessorProfile = oldProfile;
            record.factionSuccessorFactionId = oldFaction;
            record.factionSuccessorType = oldType;
            if (record.history != null && record.history.Count > oldHistoryCount)
                record.history.RemoveRange(oldHistoryCount, record.history.Count - oldHistoryCount);
            record.nextHistorySequence = oldSequence;
            return "The successor change could not be saved; the previous nomination remains active.";
        }

        internal static string SetFactionSuccessorDelegate(NetPlayer by, int stationId, string officer)
        {
            var record = Find(stationId);
            if (record == null) return "No player station with that id exists.";
            if (record.ownershipType != PlayerStationOwnershipType.Faction || string.IsNullOrEmpty(record.ownerFactionId))
                return "Only faction-owned stations can delegate succession authority.";
            if (!CanManage(by, record, out string account))
                return "Dock at the station using a controlled faction profile to manage succession.";
            var faction = NetFactions.Of(account);
            if (faction == null || !string.Equals(faction.id, record.ownerFactionId, StringComparison.Ordinal) ||
                !string.Equals(faction.leader, account, StringComparison.Ordinal))
                return "Only the owning faction leader can delegate succession authority.";
            string target = null;
            if (!string.Equals((officer ?? "").Trim(), "none", StringComparison.OrdinalIgnoreCase))
            {
                target = NetProfiles.FindAccount((officer ?? "").Trim());
                if (string.IsNullOrEmpty(target) || faction.officers?.Contains(target) != true)
                    return "Choose an existing officer of the owning faction, or none.";
            }
            string previous = record.factionSuccessorDelegateProfile;
            int oldHistoryCount = record.history?.Count ?? 0;
            int oldSequence = record.nextHistorySequence;
            record.factionSuccessorDelegateProfile = target;
            RecordHistory(record, account, "faction-successor-delegate-changed", target,
                target == null ? "Station-specific succession delegation removed." :
                "Succession nomination authority delegated to officer " + target + ".");
            if (Save()) return target == null ? "Station-specific succession delegation removed." :
                "Succession nomination authority delegated to " + target + " for this station.";
            record.factionSuccessorDelegateProfile = previous;
            if (record.history != null && record.history.Count > oldHistoryCount)
                record.history.RemoveRange(oldHistoryCount, record.history.Count - oldHistoryCount);
            record.nextHistorySequence = oldSequence;
            return "The delegation change could not be saved; the previous delegation remains active.";
        }

        /// <summary>Resolve faction-owned stations before the faction record is deleted.</summary>
        internal static bool ResolveFactionDissolution(string factionId)
        {
            if (string.IsNullOrEmpty(factionId) || registry?.stations == null) return true;
            bool affected = registry.stations.Exists(x => x != null &&
                x.ownershipType == PlayerStationOwnershipType.Faction &&
                string.Equals(x.ownerFactionId, factionId, StringComparison.Ordinal));
            if (!affected) return true;
            string previousRegistry = JsonUtility.ToJson(registry, true);
            foreach (var record in registry.stations)
            {
                if (record == null || record.ownershipType != PlayerStationOwnershipType.Faction ||
                    !string.Equals(record.ownerFactionId, factionId, StringComparison.Ordinal)) continue;

                string successor = record.factionSuccessorProfile;
                string successorFactionId = record.factionSuccessorFactionId;
                bool successorFactionValid = record.factionSuccessorType == PlayerStationOwnershipType.Faction &&
                    !string.IsNullOrEmpty(successorFactionId) &&
                    NetFactions.Of(successor) is NetFactions.Faction nextFaction &&
                    string.Equals(nextFaction.id, successorFactionId, StringComparison.Ordinal);

                if (successorFactionValid)
                {
                    var nextFaction = NetFactions.Of(successor);
                    record.ownershipType = PlayerStationOwnershipType.Faction;
                    record.ownerFactionId = nextFaction.id;
                    record.ownerProfile = nextFaction.leader;
                    record.ownerName = nextFaction.name;
                    record.factionStorageAccess = true;
                }
                else
                {
                    string newOwner = record.factionSuccessorType == PlayerStationOwnershipType.Individual &&
                        !string.IsNullOrEmpty(successor) ? successor : record.originalBuilderProfile;
                    record.ownershipType = PlayerStationOwnershipType.Individual;
                    record.ownerProfile = newOwner;
                    record.ownerFactionId = null;
                    record.ownerName = string.IsNullOrEmpty(newOwner) ? "Unclaimed" : newOwner;
                    record.factionStorageAccess = false;
                }

                // Old faction appointments and bans cannot survive the faction's dissolution.
                record.roleAssignments?.Clear();
                record.bans?.RemoveAll(x => x != null && x.source == PlayerStationBanSource.Faction);
                record.permissionOverrides?.Clear();
                record.factionPermissions = new PlayerStationFactionPermissionProfile();
                record.pendingTransfer = null;
                record.factionSuccessorProfile = null;
                record.factionSuccessorFactionId = null;
                record.factionSuccessorDelegateProfile = null;
                RecordHistory(record, factionId, "faction-dissolved-ownership-resolved", record.ownerProfile,
                    string.IsNullOrEmpty(record.ownerProfile)
                        ? "No successor or recoverable original builder was available; station is unowned."
                        : "Station ownership resolved to " + (record.ownershipType == PlayerStationOwnershipType.Faction
                            ? "successor faction " + record.ownerFactionId : "individual profile " + record.ownerProfile) + ".");
                SyncJumpgatesForStation(record);
            }
            if (Save()) return true;
            registry = JsonUtility.FromJson<Registry>(previousRegistry);
            if (registry == null)
            {
                persistenceFaulted = true;
                Debug.LogError("NetPlayerStations: faction dissolution rollback failed; station persistence is disabled.");
            }
            return false;
        }

        internal static string GovernanceHistory(NetPlayer by, int stationId, int count = 50)
        {
            var record = Find(stationId);
            if (record == null) return "No player station with that id exists.";
            string account = by == null ? null : NetProfiles.AccountOf(by.OwnerClientId);
            bool builder = HasBuilderRights(record, account);
            if (!builder && !CanViewStationRecord(by, record))
                return "Station governance history is private to the owner, original builder and authorised faction managers.";
            if (!builder && !CanManage(by, record, out _))
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
