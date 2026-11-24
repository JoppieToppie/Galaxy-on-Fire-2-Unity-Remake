using System;
using System.Collections.Generic;
using UnityEngine;

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
    public enum PlayerStationBanSource { OwnerAuthority = 0, OriginalBuilder = 1 }

    [Serializable]
    public sealed class PlayerStationPermissionOverride
    {
        public string account;
        public PlayerStationPermission permission;
        public PlayerStationPermissionState state = PlayerStationPermissionState.Inherit;
        public string setBy;
        public PlayerStationPermissionState builderState = PlayerStationPermissionState.Inherit;
        public string builderSetBy;
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
        // Officers inherit broad management authority by default. The leader can restrict each
        // capability independently for this station.
        public bool officerVisit = true;
        public bool officerStorage = true;
        public bool officerHangar = true;
        public bool officerStationOperations = true;
        public bool officerConstruction;
        public bool officerAppearance;
        public bool officerFinance = true;
        public bool officerMarketplace = true;
        public bool officerSecurity = true;
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
        static PlayerStationFactionPermissionProfile NewIndividualFactionPermissionProfile(bool shared = false)
        {
            return new PlayerStationFactionPermissionProfile
            {
                visit = shared, storage = shared, hangar = shared,
                officerVisit = shared, officerStorage = shared, officerHangar = shared,
                officerStationOperations = false, officerConstruction = false, officerAppearance = false,
                officerFinance = false, officerMarketplace = false, officerSecurity = false
            };
        }

        static PlayerStationFactionPermissionProfile NewFactionOwnedPermissionProfile()
        {
            return new PlayerStationFactionPermissionProfile { visit = true, storage = true, hangar = true };
        }

        static bool NormalizeGovernance(PlayerStationRecord record)
        {
            if (record == null) return false;
            bool changed = false;
            record.permissionOverrides ??= new List<PlayerStationPermissionOverride>();
            record.roleAssignments ??= new List<PlayerStationRoleAssignment>();
            record.bans ??= new List<PlayerStationBan>();
            record.history ??= new List<PlayerStationHistoryEntry>();
            record.factionPermissions ??= new PlayerStationFactionPermissionProfile();
            if (record.governanceVersion < 1)
            {
                record.factionPermissions = record.ownershipType == PlayerStationOwnershipType.Individual
                    ? NewIndividualFactionPermissionProfile(record.factionStorageAccess)
                    : NewFactionOwnedPermissionProfile();
                record.governanceVersion = 1;
                changed = true;
            }

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

        static bool IsStationContext(NetPlayer by, PlayerStationRecord record, out string account)
        {
            account = by == null ? null : NetProfiles.AccountOf(by.OwnerClientId);
            return NetGame.IsServer && sessionActive && registry != null && record != null &&
                by != null && by.IsSpawned && by.InHangar && by.Station == record.stationIndex &&
                record.offeredForClaimUtc == 0 && !StationClosedByCombat(record.stationIndex) &&
                NetProfiles.Enabled && NetProfiles.Controls(by.OwnerClientId) && !string.IsNullOrEmpty(account) &&
                HasStationPermission(record, account, PlayerStationPermission.Visit);
        }

        static bool IsStationOwnerAccount(PlayerStationRecord record, string account)
        {
            if (record == null || string.IsNullOrEmpty(account)) return false;
            return record.ownershipType == PlayerStationOwnershipType.Individual &&
                string.Equals(record.ownerProfile, account, StringComparison.Ordinal);
        }

        static void ClearOwnerVisitorRestrictions(PlayerStationRecord record)
        {
            if (record == null) return;
            string owner = record.ownershipType == PlayerStationOwnershipType.Individual
                ? record.ownerProfile : NetFactions.FindById(record.ownerFactionId)?.leader;
            if (string.IsNullOrEmpty(owner)) return;
            record.bans?.RemoveAll(x => x != null && string.Equals(x.account, owner, StringComparison.Ordinal));
            record.permissionOverrides?.RemoveAll(x => x != null &&
                string.Equals(x.account, owner, StringComparison.Ordinal) &&
                x.permission == PlayerStationPermission.Visit);
        }

        static string StationFactionId(PlayerStationRecord record)
        {
            return record == null ? null : record.ownershipType == PlayerStationOwnershipType.Faction
                ? record.ownerFactionId : NetFactions.Of(record.ownerProfile)?.id;
        }

        static PlayerStationPermissionOverride FindPermissionOverride(PlayerStationRecord record,
            string account, PlayerStationPermission permission)
        {
            return record?.permissionOverrides?.Find(x => x != null &&
                string.Equals(x.account, account, StringComparison.Ordinal) && x.permission == permission);
        }

        static bool HasRole(PlayerStationRecord record, string account, PlayerStationRole role)
        {
            return record?.roleAssignments?.Exists(x => x != null &&
                string.Equals(x.account, account, StringComparison.Ordinal) && x.role == role) == true;
        }

        static bool RoleGrants(PlayerStationRecord record, string account, PlayerStationPermission permission)
        {
            if (permission == PlayerStationPermission.StationOperations &&
                HasRole(record, account, PlayerStationRole.StationAdministrator)) return true;
            if (permission == PlayerStationPermission.Storage && HasRole(record, account, PlayerStationRole.Quartermaster)) return true;
            if (permission == PlayerStationPermission.Hangar && HasRole(record, account, PlayerStationRole.Quartermaster)) return true;
            if ((permission == PlayerStationPermission.Construction || permission == PlayerStationPermission.Appearance) &&
                HasRole(record, account, PlayerStationRole.ConstructionManager)) return true;
            if (permission == PlayerStationPermission.Marketplace && HasRole(record, account, PlayerStationRole.MarketManager)) return true;
            if (permission == PlayerStationPermission.Security && HasRole(record, account, PlayerStationRole.SecurityOfficer)) return true;
            return false;
        }

        static bool ProfilePermission(PlayerStationFactionPermissionProfile profile,
            PlayerStationPermission permission, bool officer)
        {
            if (profile == null) return false;
            if (officer)
            {
                switch (permission)
                {
                    case PlayerStationPermission.Visit: return profile.officerVisit;
                    case PlayerStationPermission.Storage: return profile.officerStorage;
                    case PlayerStationPermission.Hangar: return profile.officerHangar;
                    case PlayerStationPermission.StationOperations: return profile.officerStationOperations;
                    case PlayerStationPermission.Construction: return profile.officerConstruction;
                    case PlayerStationPermission.Appearance: return profile.officerAppearance;
                    case PlayerStationPermission.Finance: return profile.officerFinance;
                    case PlayerStationPermission.Marketplace: return profile.officerMarketplace;
                    case PlayerStationPermission.Security: return profile.officerSecurity;
                }
            }
            switch (permission)
            {
                case PlayerStationPermission.Visit: return profile.visit;
                case PlayerStationPermission.Storage: return profile.storage;
                case PlayerStationPermission.Hangar: return profile.hangar;
                case PlayerStationPermission.StationOperations: return profile.stationOperations;
                case PlayerStationPermission.Construction: return profile.construction;
                case PlayerStationPermission.Appearance: return profile.appearance;
                case PlayerStationPermission.Finance: return profile.finance;
                case PlayerStationPermission.Marketplace: return profile.marketplace;
                case PlayerStationPermission.Security: return profile.security;
                default: return false;
            }
        }

        static List<PlayerStationPermissionOverride> ClonePermissionOverrides(PlayerStationRecord record)
        {
            var copy = new List<PlayerStationPermissionOverride>();
            foreach (var item in record?.permissionOverrides ?? new List<PlayerStationPermissionOverride>())
                if (item != null) copy.Add(new PlayerStationPermissionOverride
                {
                    account = item.account, permission = item.permission, state = item.state, setBy = item.setBy,
                    builderState = item.builderState, builderSetBy = item.builderSetBy
                });
            return copy;
        }

        static List<PlayerStationBan> CloneBans(PlayerStationRecord record)
        {
            var copy = new List<PlayerStationBan>();
            foreach (var item in record?.bans ?? new List<PlayerStationBan>())
                if (item != null) copy.Add(new PlayerStationBan
                {
                    account = item.account, source = item.source, imposedBy = item.imposedBy,
                    reason = item.reason, createdUtc = item.createdUtc
                });
            return copy;
        }

        static void ConvertBuilderAuthorityRecords(PlayerStationRecord record)
        {
            if (record == null) return;
            foreach (var item in record.permissionOverrides ?? new List<PlayerStationPermissionOverride>())
            {
                if (item == null || item.builderState == PlayerStationPermissionState.Inherit) continue;
                item.state = item.builderState;
                item.setBy = item.builderSetBy;
                item.builderState = PlayerStationPermissionState.Inherit;
                item.builderSetBy = null;
            }
            foreach (var ban in record.bans ?? new List<PlayerStationBan>())
                if (ban != null && ban.source == PlayerStationBanSource.OriginalBuilder)
                    ban.source = PlayerStationBanSource.OwnerAuthority;
        }

        static bool HasBan(PlayerStationRecord record, string account, PlayerStationBanSource source)
        {
            return record?.bans?.Exists(x => x != null && x.source == source &&
                string.Equals(x.account, account, StringComparison.Ordinal)) == true;
        }

        internal static bool HasStationPermission(PlayerStationRecord record, string account,
            PlayerStationPermission permission)
        {
            if (record == null) return false;
            if (record.offeredForClaimUtc > 0) return permission == PlayerStationPermission.Visit;
            if (string.IsNullOrEmpty(account)) return false;
            if (IsStationOwnerAccount(record, account)) return true;
            bool builder = HasBuilderRights(record, account);
            var visitorOverride = FindPermissionOverride(record, account, PlayerStationPermission.Visit);
            PlayerStationPermissionState builderVisitState = visitorOverride?.builderState ?? PlayerStationPermissionState.Inherit;

            // Builder bans and explicit builder visitor denials always win. The current owner
            // cannot use a faction ban to strip the original builder of their reserved authority.
            if (HasBan(record, account, PlayerStationBanSource.OriginalBuilder)) return false;
            bool builderSecurity = builder && (permission == PlayerStationPermission.Security ||
                permission == PlayerStationPermission.Construction ||
                permission == PlayerStationPermission.Appearance);
            if (permission == PlayerStationPermission.Visit)
            {
                if (builderVisitState == PlayerStationPermissionState.Deny) return false;
                if (builderVisitState == PlayerStationPermissionState.Allow || builder) return true;
                if (HasBan(record, account, PlayerStationBanSource.OwnerAuthority)) return false;
            }
            else if (HasBan(record, account, PlayerStationBanSource.OwnerAuthority) && !builderSecurity)
                return false;

            if (record.ownershipType == PlayerStationOwnershipType.Individual &&
                string.Equals(record.ownerProfile, account, StringComparison.Ordinal)) return true;

            // The builder's reserved authority is deliberately narrow: visitor control, security,
            // module construction and appearance. It never grants the faction's treasury or assets.
            if (builderSecurity) return true;

            var explicitOverride = FindPermissionOverride(record, account, permission);
            if (explicitOverride != null && explicitOverride.state != PlayerStationPermissionState.Inherit)
            {
                if (explicitOverride.state == PlayerStationPermissionState.Deny) return false;
                // Explicit grants are durable individual decisions. A later faction change by
                // either the owner or recipient does not silently revoke them; the owner must
                // explicitly change the entry back to Inherit or Deny. New grants are still
                // checked against opposing-faction restrictions when they are created.
                return true;
            }

            var faction = NetFactions.Of(account);
            string stationFactionId = StationFactionId(record);
            bool sameFaction = faction != null && !string.IsNullOrEmpty(stationFactionId) &&
                string.Equals(faction.id, stationFactionId, StringComparison.Ordinal);
            if (faction != null && !string.IsNullOrEmpty(stationFactionId) && !sameFaction) return false;
            if (sameFaction && string.Equals(faction.leader, account, StringComparison.Ordinal)) return true;
            if (RoleGrants(record, account, permission)) return true;
            if (!sameFaction) return false;
            bool officer = faction.officers?.Contains(account) == true;
            return ProfilePermission(record.factionPermissions, permission, officer);
        }

        internal static bool CanVisitStation(int stationIndex, string account)
        {
            var record = StationAt(stationIndex);
            return record == null || (!StationClosedByCombat(stationIndex) && HasStationPermission(record, account, PlayerStationPermission.Visit));
        }

        // Ownership authority controls staffing and faction-wide station policy.
        // Station administrators manage day-to-day settings but cannot appoint staff or
        // rewrite the faction's permission profile.
        // A UI-selected online pilot uses its public session client ID, never its private account ID.
        static string ResolveStationAccount(string target)
        {
            target = (target ?? "").Trim();
            if (target.StartsWith("online:", StringComparison.OrdinalIgnoreCase) &&
                ulong.TryParse(target.Substring(7), out ulong client))
                return NetProfiles.AccountOf(client);
            return NetProfiles.FindAccount(target);
        }

        static bool IsStationAuthority(NetPlayer by, PlayerStationRecord record, out string account)
        {
            account = null;
            if (!IsStationContext(by, record, out account)) return false;
            if (record.ownershipType == PlayerStationOwnershipType.Individual)
                return string.Equals(record.ownerProfile, account, StringComparison.Ordinal);
            var faction = NetFactions.Of(account);
            return faction != null && string.Equals(faction.id, record.ownerFactionId, StringComparison.Ordinal) &&
                string.Equals(faction.leader, account, StringComparison.Ordinal);
        }

        internal static string SetStationRole(NetPlayer by, int stationId, string target, string roleName)
        {
            var record = Find(stationId);
            if (record == null) return "No player station with that id exists.";
            if (!IsStationAuthority(by, record, out string account))
                return "Only the station owner or faction leader may assign station roles.";
            string targetAccount = ResolveStationAccount(target);
            if (string.IsNullOrEmpty(targetAccount)) return "Supply a valid target account.";
            string targetFactionId = NetFactions.Of(targetAccount)?.id;
            string ownerFactionId = StationFactionId(record);
            if (!string.IsNullOrEmpty(targetFactionId) && !string.IsNullOrEmpty(ownerFactionId) &&
                !string.Equals(targetFactionId, ownerFactionId, StringComparison.Ordinal))
                return "Players from an opposing faction cannot be assigned station roles.";
            roleName = (roleName ?? "").Trim();
            PlayerStationRole role = PlayerStationRole.StationAdministrator;
            bool remove = string.Equals(roleName, "none", StringComparison.OrdinalIgnoreCase);
            if (!remove && (!Enum.TryParse(roleName, true, out role) || !Enum.IsDefined(typeof(PlayerStationRole), role)))
                return "Choose StationAdministrator, Quartermaster, ConstructionManager, MarketManager, SecurityOfficer or none.";
            if (role == PlayerStationRole.StationAdministrator && record.ownershipType == PlayerStationOwnershipType.Faction)
            {
                var faction = NetFactions.Of(account);
                if (faction == null || !string.Equals(faction.leader, account, StringComparison.Ordinal))
                    return "Only the faction leader can appoint a station administrator.";
            }
            record.roleAssignments ??= new List<PlayerStationRoleAssignment>();
            var previous = record.roleAssignments.Find(x => x != null && string.Equals(x.account, targetAccount, StringComparison.Ordinal));
            PlayerStationRoleAssignment savedPrevious = previous == null ? null : new PlayerStationRoleAssignment
            { account = previous.account, role = previous.role, assignedBy = previous.assignedBy, assignedUtc = previous.assignedUtc };
            int oldHistoryCount = record.history?.Count ?? 0, oldSequence = record.nextHistorySequence;
            if (previous != null) record.roleAssignments.Remove(previous);
            if (!remove) record.roleAssignments.Add(new PlayerStationRoleAssignment
            { account = targetAccount, role = role, assignedBy = account, assignedUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds() });
            RecordHistory(record, account, remove ? "station-role-revoked" : "station-role-assigned",
                targetAccount, remove ? "Station role removed." : "Assigned role " + role + ".");
            if (Save())
            {
                EjectDeniedVisitors(record);
                return remove ? "Station role removed from " + targetAccount + "." :
                    "Assigned " + role + " to " + targetAccount + ".";
            }
            record.roleAssignments.RemoveAll(x => x != null &&
                string.Equals(x.account, targetAccount, StringComparison.Ordinal));
            if (savedPrevious != null) record.roleAssignments.Add(savedPrevious);
            if (record.history != null && record.history.Count > oldHistoryCount)
                record.history.RemoveRange(oldHistoryCount, record.history.Count - oldHistoryCount);
            record.nextHistorySequence = oldSequence;
            return "The role change could not be saved; the previous assignment remains active.";
        }

        internal static string SetStationPermission(NetPlayer by, int stationId, string target,
            string permissionName, string stateName)
        {
            var record = Find(stationId);
            if (record == null) return "No player station with that id exists.";
            if (!IsStationContext(by, record, out string account))
                return "Dock at the station using a controlled profile to change permissions.";
            if (!Enum.TryParse(permissionName, true, out PlayerStationPermission permission) ||
                !Enum.IsDefined(typeof(PlayerStationPermission), permission))
                return "Unknown permission. Use visit, storage, hangar, stationOperations, construction, appearance, finance, marketplace or security.";
            if (!Enum.TryParse(stateName, true, out PlayerStationPermissionState state) ||
                !Enum.IsDefined(typeof(PlayerStationPermissionState), state))
                return "Choose inherit, allow or deny.";
            bool builder = HasBuilderRights(record, account);
            if (builder && permission != PlayerStationPermission.Visit)
                return "The original builder can only override individual visitor access; other permissions belong to station governance.";
            if (!builder && permission == PlayerStationPermission.Visit &&
                !HasStationPermission(record, account, PlayerStationPermission.Security))
                return "Only the station owner or an authorised station security role may change visitor permissions.";
            if (!builder && permission != PlayerStationPermission.Visit && !IsStationAuthority(by, record, out _))
                return "Only the individual owner or owning faction leader may grant or revoke non-visitor permissions. Station administrators cannot grant themselves or others additional authority.";
            string targetAccount = ResolveStationAccount(target);
            if (string.IsNullOrEmpty(targetAccount)) return "Supply a valid target account.";
            if (!builder && record.ownershipType == PlayerStationOwnershipType.Individual &&
                string.Equals(targetAccount, record.ownerProfile, StringComparison.Ordinal))
                return "The station owner cannot have their own permissions overridden.";
            var targetFaction = NetFactions.Of(targetAccount);
            if (!builder && record.ownershipType == PlayerStationOwnershipType.Faction &&
                targetFaction != null && string.Equals(targetFaction.id, record.ownerFactionId, StringComparison.Ordinal) &&
                string.Equals(targetFaction.leader, targetAccount, StringComparison.Ordinal))
                return "The faction leader's authority cannot be overridden by a station permission entry.";
            string targetFactionId = targetFaction?.id;
            string ownerFactionId = StationFactionId(record);
            if (!string.IsNullOrEmpty(targetFactionId) && !string.IsNullOrEmpty(ownerFactionId) &&
                !string.Equals(targetFactionId, ownerFactionId, StringComparison.Ordinal) &&
                !(builder && permission == PlayerStationPermission.Visit))
                return "Players from an opposing faction cannot be granted station permissions. The original builder may still individually allow, deny or inherit visitor access for a specific player.";
            record.permissionOverrides ??= new List<PlayerStationPermissionOverride>();
            var previous = FindPermissionOverride(record, targetAccount, permission);
            PlayerStationPermissionOverride savedPrevious = previous == null ? null : new PlayerStationPermissionOverride
            { account = previous.account, permission = previous.permission, state = previous.state, setBy = previous.setBy,
                builderState = previous.builderState, builderSetBy = previous.builderSetBy };
            int oldHistoryCount = record.history?.Count ?? 0, oldSequence = record.nextHistorySequence;
            var current = previous;
            if (current == null)
            {
                current = new PlayerStationPermissionOverride { account = targetAccount, permission = permission };
                record.permissionOverrides.Add(current);
            }
            if (builder)
            {
                current.builderState = state;
                current.builderSetBy = state == PlayerStationPermissionState.Inherit ? null : account;
            }
            else
            {
                current.state = state;
                current.setBy = state == PlayerStationPermissionState.Inherit ? null : account;
            }
            if (current.state == PlayerStationPermissionState.Inherit &&
                current.builderState == PlayerStationPermissionState.Inherit)
                record.permissionOverrides.Remove(current);
            RecordHistory(record, account, "station-permission-changed", targetAccount,
                permission + " set to " + state + (builder ? " by original builder." : "."));
            if (Save())
            {
                if (permission == PlayerStationPermission.Visit) EjectDeniedVisitors(record);
                return state == PlayerStationPermissionState.Inherit
                    ? "Permission override removed; the underlying permission default now applies."
                    : permission + " permission set to " + state + " for " + targetAccount + ".";
            }
            record.permissionOverrides.RemoveAll(x => x != null &&
                string.Equals(x.account, targetAccount, StringComparison.Ordinal) && x.permission == permission);
            if (savedPrevious != null) record.permissionOverrides.Add(savedPrevious);
            if (record.history != null && record.history.Count > oldHistoryCount)
                record.history.RemoveRange(oldHistoryCount, record.history.Count - oldHistoryCount);
            record.nextHistorySequence = oldSequence;
            return "The permission change could not be saved; the previous setting remains active.";
        }

        internal static string SetFactionPermissionDefault(NetPlayer by, int stationId,
            string groupName, string permissionName, string stateName)
        {
            var record = Find(stationId);
            if (record == null) return "No player station with that id exists.";
            if (!IsStationAuthority(by, record, out string account))
                return "Only the station owner or faction leader may change faction permission defaults.";
            if (!Enum.TryParse(permissionName, true, out PlayerStationPermission permission) ||
                !Enum.IsDefined(typeof(PlayerStationPermission), permission))
                return "Unknown permission.";
            if (!Enum.TryParse(stateName, true, out PlayerStationPermissionState state) ||
                !Enum.IsDefined(typeof(PlayerStationPermissionState), state) || state == PlayerStationPermissionState.Inherit)
                return "Choose allow or deny for a faction default.";
            bool officer = string.Equals((groupName ?? "").Trim(), "officer", StringComparison.OrdinalIgnoreCase);
            if (!officer && !string.Equals((groupName ?? "").Trim(), "member", StringComparison.OrdinalIgnoreCase))
                return "Choose member or officer.";
            string field = (officer ? "officer" : "") + permission.ToString();
            var profile = record.factionPermissions ?? (record.factionPermissions = new PlayerStationFactionPermissionProfile());
            bool previousFactionStorageAccess = record.factionStorageAccess;
            bool old;
            switch (field)
            {
                case "Visit": old = profile.visit; profile.visit = state == PlayerStationPermissionState.Allow; break;
                case "Storage": old = profile.storage; profile.storage = state == PlayerStationPermissionState.Allow; break;
                case "Hangar": old = profile.hangar; profile.hangar = state == PlayerStationPermissionState.Allow; break;
                case "StationOperations": old = profile.stationOperations; profile.stationOperations = state == PlayerStationPermissionState.Allow; break;
                case "Construction": old = profile.construction; profile.construction = state == PlayerStationPermissionState.Allow; break;
                case "Appearance": old = profile.appearance; profile.appearance = state == PlayerStationPermissionState.Allow; break;
                case "Finance": old = profile.finance; profile.finance = state == PlayerStationPermissionState.Allow; break;
                case "Marketplace": old = profile.marketplace; profile.marketplace = state == PlayerStationPermissionState.Allow; break;
                case "Security": old = profile.security; profile.security = state == PlayerStationPermissionState.Allow; break;
                case "officerVisit": old = profile.officerVisit; profile.officerVisit = state == PlayerStationPermissionState.Allow; break;
                case "officerStorage": old = profile.officerStorage; profile.officerStorage = state == PlayerStationPermissionState.Allow; break;
                case "officerHangar": old = profile.officerHangar; profile.officerHangar = state == PlayerStationPermissionState.Allow; break;
                case "officerStationOperations": old = profile.officerStationOperations; profile.officerStationOperations = state == PlayerStationPermissionState.Allow; break;
                case "officerConstruction": old = profile.officerConstruction; profile.officerConstruction = state == PlayerStationPermissionState.Allow; break;
                case "officerAppearance": old = profile.officerAppearance; profile.officerAppearance = state == PlayerStationPermissionState.Allow; break;
                case "officerFinance": old = profile.officerFinance; profile.officerFinance = state == PlayerStationPermissionState.Allow; break;
                case "officerMarketplace": old = profile.officerMarketplace; profile.officerMarketplace = state == PlayerStationPermissionState.Allow; break;
                case "officerSecurity": old = profile.officerSecurity; profile.officerSecurity = state == PlayerStationPermissionState.Allow; break;
                default: return "That permission cannot be configured.";
            }
            if (!officer && record.ownershipType == PlayerStationOwnershipType.Individual)
                record.factionStorageAccess = profile.storage && profile.hangar;
            int oldHistoryCount = record.history?.Count ?? 0, oldSequence = record.nextHistorySequence;
            RecordHistory(record, account, "faction-permission-default-changed", null,
                field + " default set to " + state + ".");
            if (Save())
            {
                if (permission == PlayerStationPermission.Visit) EjectDeniedVisitors(record);
                return field + " default set to " + state + ".";
            }
            switch (field)
            {
                case "Visit": profile.visit = old; break;
                case "Storage": profile.storage = old; break;
                case "Hangar": profile.hangar = old; break;
                case "StationOperations": profile.stationOperations = old; break;
                case "Construction": profile.construction = old; break;
                case "Appearance": profile.appearance = old; break;
                case "Finance": profile.finance = old; break;
                case "Marketplace": profile.marketplace = old; break;
                case "Security": profile.security = old; break;
                case "officerVisit": profile.officerVisit = old; break;
                case "officerStorage": profile.officerStorage = old; break;
                case "officerHangar": profile.officerHangar = old; break;
                case "officerStationOperations": profile.officerStationOperations = old; break;
                case "officerConstruction": profile.officerConstruction = old; break;
                case "officerAppearance": profile.officerAppearance = old; break;
                case "officerFinance": profile.officerFinance = old; break;
                case "officerMarketplace": profile.officerMarketplace = old; break;
                case "officerSecurity": profile.officerSecurity = old; break;
            }
            record.factionStorageAccess = previousFactionStorageAccess;
            if (record.history != null && record.history.Count > oldHistoryCount)
                record.history.RemoveRange(oldHistoryCount, record.history.Count - oldHistoryCount);
            record.nextHistorySequence = oldSequence;
            return "The faction default could not be saved; the previous setting remains active.";
        }

        internal static string SetStationBan(NetPlayer by, int stationId, string target,
            string reason, bool ban)
        {
            var record = Find(stationId);
            if (record == null) return "No player station with that id exists.";
            if (!IsStationContext(by, record, out string account))
                return "Dock at the station using a controlled profile to manage visitor bans.";
            bool builder = HasBuilderRights(record, account);
            if (!builder && !HasStationPermission(record, account, PlayerStationPermission.Security))
                return "Only the original builder or an authorised station security role may manage station bans.";
            string targetAccount = ResolveStationAccount(target);
            if (string.IsNullOrEmpty(targetAccount)) return "Supply a valid target account.";
            if (IsStationOwnerAccount(record, targetAccount))
                return "The current station owner cannot be banned from their own station.";
            if (!builder && HasBuilderRights(record, targetAccount))
                return "The original builder's reserved station access cannot be revoked by station governance.";
            var targetFaction = NetFactions.Of(targetAccount);
            if (!builder && record.ownershipType == PlayerStationOwnershipType.Faction &&
                targetFaction != null && string.Equals(targetFaction.id, record.ownerFactionId, StringComparison.Ordinal) &&
                string.Equals(targetFaction.leader, targetAccount, StringComparison.Ordinal))
                return "Only the original builder may impose a visitor ban on the owning faction leader.";
            if (string.Equals(targetAccount, account, StringComparison.Ordinal))
                return "You cannot ban yourself from the station.";
            record.bans ??= new List<PlayerStationBan>();
            var source = builder ? PlayerStationBanSource.OriginalBuilder : PlayerStationBanSource.OwnerAuthority;
            var existing = record.bans.Find(x => x != null && string.Equals(x.account, targetAccount, StringComparison.Ordinal) && x.source == source);
            if (ban)
            {
                if (existing != null) return "That player is already banned by this authority.";
                record.bans.Add(new PlayerStationBan
                { account = targetAccount, source = source, imposedBy = account, reason = (reason ?? "").Trim(),
                    createdUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds() });
            }
            else
            {
                if (existing == null)
                {
                    if (!builder && HasBan(record, targetAccount, PlayerStationBanSource.OriginalBuilder))
                        return "The original builder imposed that ban and only they can lift it.";
                    return "No ban imposed by your authority exists for that player.";
                }
                record.bans.Remove(existing);
            }
            int oldHistoryCount = record.history?.Count ?? 0, oldSequence = record.nextHistorySequence;
            RecordHistory(record, account, ban ? "visitor-banned" : "visitor-unbanned", targetAccount,
                (builder ? "Original builder" : "Station owner authority") + (ban ? " imposed" : " lifted") +
                " the station ban." + (string.IsNullOrEmpty(reason) ? "" : " Reason: " + reason.Trim()));
            if (Save())
            {
                if (ban) EjectDeniedVisitors(record);
                return ban ? "Station ban applied to " + targetAccount + "." : "Station ban lifted for " + targetAccount + ".";
            }
            if (ban) record.bans.RemoveAll(x => x != null && x.source == source && string.Equals(x.account, targetAccount, StringComparison.Ordinal));
            else record.bans.Add(existing);
            if (record.history != null && record.history.Count > oldHistoryCount)
                record.history.RemoveRange(oldHistoryCount, record.history.Count - oldHistoryCount);
            record.nextHistorySequence = oldSequence;
            return "The ban change could not be saved; the previous ban state remains active.";
        }

        static bool OriginalBuilderInSession(PlayerStationRecord record)
        {
            if (record == null || !HasBuilderRights(record, record.originalBuilderProfile)) return false;
            foreach (ulong client in NetProfiles.ClientsOf(record.originalBuilderProfile))
                if (NetProfiles.Controls(client)) return true;
            return false;
        }

        static bool HasConstructionRole(PlayerStationRecord record, string account)
        {
            return record?.roleAssignments?.Exists(x => x != null &&
                string.Equals(x.account, account, StringComparison.Ordinal) &&
                (x.role == PlayerStationRole.ConstructionManager ||
                 x.role == PlayerStationRole.StationAdministrator)) == true;
        }

        static bool CanManageConstruction(NetPlayer by, PlayerStationRecord record, out string account)
        {
            if (!IsStationContext(by, record, out account)) return false;

            if (HasBuilderRights(record, account)) return true;

            // The original builder has first say while their profile is in the session, even
            // when station ownership has since passed to another individual or faction. The
            // current owner and delegated construction managers may act only while the builder
            // is absent; a transfer by someone other than the builder does not remove this right.
            if (OriginalBuilderInSession(record)) return false;

            if (record.ownershipType == PlayerStationOwnershipType.Individual)
                return string.Equals(record.ownerProfile, account, StringComparison.Ordinal);

            return HasStationPermission(record, account, PlayerStationPermission.Construction);
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

        internal static string RelinquishBuilderRights(NetPlayer by, int stationId)
        {
            var record = Find(stationId);
            if (record == null) return "No player station with that id exists.";
            if (!NetGame.IsServer || by == null || !by.IsSpawned || !by.InHangar ||
                by.Station != record.stationIndex || !NetProfiles.Enabled || !NetProfiles.Controls(by.OwnerClientId))
                return "Dock at the station using your controlled builder profile to relinquish builder rights.";
            string account = NetProfiles.AccountOf(by.OwnerClientId);
            if (record.offeredForClaimUtc > 0 || !HasBuilderRights(record, account))
                return "Only the original builder of an owned station can relinquish these reserved rights.";
            long previous = record.builderRightsRelinquishedUtc;
            var previousOverrides = ClonePermissionOverrides(record);
            var previousBans = CloneBans(record);
            int oldHistoryCount = record.history?.Count ?? 0;
            int oldSequence = record.nextHistorySequence;
            record.builderRightsRelinquishedUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            ConvertBuilderAuthorityRecords(record);
            RecordHistory(record, account, "builder-rights-relinquished", null,
                "The original builder voluntarily relinquished their reserved station authority.");
            if (Save())
            {
                EjectDeniedVisitors(record);
                return "Your reserved original-builder rights have been relinquished permanently.";
            }
            record.builderRightsRelinquishedUtc = previous;
            record.permissionOverrides = previousOverrides;
            record.bans = previousBans;
            if (record.history != null && record.history.Count > oldHistoryCount)
                record.history.RemoveRange(oldHistoryCount, record.history.Count - oldHistoryCount);
            record.nextHistorySequence = oldSequence;
            return "Builder rights could not be relinquished because the change could not be saved.";
        }

        static int StationModelIndex(PlayerStationRecord station)
        {
            if (string.IsNullOrEmpty(station.stationModelReference)) return station.looksLike;
            return Modding.ModWorld.TryResolveStation(station.stationModelReference, out int index) ? index : -1;
        }

        static string StationModelReference(int index)
        {
            foreach (var entry in Modding.ModWorld.ActiveStationKeys())
                if (entry.Key == index) return entry.Value;
            return null;
        }

        internal static bool IsStationModelPreset(int index)
        {
            if (index < 0 || IsPlayerStation(index)) return false;
            var db = NetGame.Db;
            var station = db?.Stations?.Find(value => value != null && value.index == index);
            if (station == null || GoF2Remake.World.OrbitBuilder.StationPresetAssembly(db, station) == null) return false;
            var settings = Modding.ModPlayerInfrastructure.Current;
            if (!settings.RestrictStationModels) return true;
            foreach (var reference in settings.StationModels)
                if (Modding.ModWorld.TryResolveStation(reference, out int resolved) && resolved == index) return true;
            return settings.IncludeStockStations && Modding.ModWorld.ModelOf(index) == null && !Modding.ModWorld.IsModStation(index);
        }

        internal static string AppearancePresets()
        {
            var stations = NetGame.Db?.Stations;
            if (stations == null) return "The station model catalogue is unavailable.";
            var lines = new List<string> { "Station appearance presets (use the index):" };
            foreach (var model in stations)
                if (model != null && IsStationModelPreset(model.index))
                    lines.Add("  " + model.index + ": " + (string.IsNullOrWhiteSpace(model.name) ? "Unnamed station model" : model.name));
            return string.Join("\n", lines);
        }

        static bool CanManageAppearance(NetPlayer by, PlayerStationRecord record, out string account)
        {
            if (!IsStationContext(by, record, out account)) return false;
            if (HasBuilderRights(record, account)) return true;
            // The original builder keeps first say while present. Once absent, normal owner
            // and delegated appearance permissions become effective.
            if (OriginalBuilderInSession(record)) return false;
            return HasStationPermission(record, account, PlayerStationPermission.Appearance);
        }

        /// <summary>Change the station's visual/collision preset using an existing non-player station model.</summary>
        internal static string SetStationAppearance(NetPlayer by, int stationId, int modelStationIndex)
        {
            var record = Find(stationId);
            if (record == null) return "No player station with that id exists.";
            if (!CanManageAppearance(by, record, out string account))
                return "The original builder controls appearance while present; otherwise an authorised owner or construction manager must be docked here.";
            var db = NetGame.Db;
            var model = db?.Stations?.Find(x => x != null && x.index == modelStationIndex);
            if (model == null || !IsStationModelPreset(modelStationIndex))
                return "Choose an enabled station model from the construction catalogue.";
            var live = db.Stations.Find(x => x != null && x.index == record.stationIndex);
            if (live == null) return "The station's live model record is unavailable.";
            int previousLook = record.looksLike;
            string previousReference = record.stationModelReference;
            int previousTech = live.techLevel;
            int previousTexture = live.textureIndex;
            record.looksLike = modelStationIndex;
            record.stationModelReference = StationModelReference(modelStationIndex);
            live.techLevel = model.techLevel;
            if (!record.existingOrbit) live.textureIndex = model.textureIndex;
            RecordHistory(record, account, "station-appearance-changed", null,
                "Station visual/collision preset changed from " + previousLook + " to " + modelStationIndex + ".");
            if (Save())
            {
                NetGuard.RefreshStations();
                return "Station appearance preset updated. Model preset " + modelStationIndex + " is now active.";
            }
            record.looksLike = previousLook;
            record.stationModelReference = previousReference;
            live.techLevel = previousTech;
            live.textureIndex = previousTexture;
            record.history?.RemoveAt(record.history.Count - 1);
            record.nextHistorySequence = Math.Max(1, record.nextHistorySequence - 1);
            return "The appearance change could not be saved; the previous preset remains active.";
        }

        internal static string SetIndividualSuccessor(NetPlayer by, int stationId, string target)
        {
            var record = Find(stationId);
            if (record == null) return "No player station with that id exists.";
            if (record.ownershipType != PlayerStationOwnershipType.Individual)
                return "Individual successor nominations apply only to individually owned stations.";
            if (!IsStationAuthority(by, record, out string account) ||
                !string.Equals(account, record.ownerProfile, StringComparison.Ordinal))
                return "Only the individual station owner can nominate a successor.";
            string successor = null;
            if (!string.Equals((target ?? "").Trim(), "clear", StringComparison.OrdinalIgnoreCase))
            {
                successor = ResolveStationAccount(target);
                if (string.IsNullOrEmpty(successor))
                    return "Supply a valid player account or use clear to remove the nomination.";
                if (string.Equals(successor, record.ownerProfile, StringComparison.Ordinal))
                    return "The current owner cannot nominate themselves as successor.";
            }
            string previous = record.individualSuccessorProfile;
            int oldHistoryCount = record.history?.Count ?? 0;
            int oldSequence = record.nextHistorySequence;
            record.individualSuccessorProfile = successor;
            RecordHistory(record, account, successor == null ? "individual-successor-cleared" : "individual-successor-nominated",
                successor, successor == null ? "Individual successor nomination cleared." :
                "Individual successor nominated. The nomination grants no access or management authority while the owner remains active.");
            if (Save()) return successor == null ? "Individual successor nomination cleared." :
                "Successor nominated: " + successor + ". This grants no station access or authority while you remain the owner.";
            record.individualSuccessorProfile = previous;
            if (record.history != null && record.history.Count > oldHistoryCount)
                record.history.RemoveRange(oldHistoryCount, record.history.Count - oldHistoryCount);
            record.nextHistorySequence = oldSequence;
            return "The successor nomination could not be saved; the previous nomination remains active.";
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
                targetAccount = ResolveStationAccount(target);
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
                target = ResolveStationAccount(officer);
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

        static void ApplyFactionDissolution(string factionId)
        {
            foreach (var record in registry.stations)
            {
                if (record == null || record.ownershipType != PlayerStationOwnershipType.Faction ||
                    !string.Equals(record.ownerFactionId, factionId, StringComparison.Ordinal)) continue;

                string successor = record.factionSuccessorProfile;
                string successorFactionId = record.factionSuccessorFactionId;
                var nextFaction = NetFactions.FindById(successorFactionId);
                bool successorFactionValid = record.factionSuccessorType == PlayerStationOwnershipType.Faction &&
                    !string.IsNullOrEmpty(successorFactionId) && nextFaction != null &&
                    !string.Equals(nextFaction.id, factionId, StringComparison.Ordinal);

                if (successorFactionValid)
                {
                    record.ownershipType = PlayerStationOwnershipType.Faction;
                    record.ownerFactionId = nextFaction.id;
                    record.ownerProfile = nextFaction.leader;
                    record.ownerName = nextFaction.name;
                    record.factionStorageAccess = true;
                    record.factionPermissions = NewFactionOwnedPermissionProfile();
                }
                else
                {
                    // A nominated player remains eligible while inactive, but a deleted or
                    // otherwise unresolvable profile cannot receive ownership. In that case,
                    // fall back to the original builder only if that profile still exists.
                    string newOwner = record.factionSuccessorType == PlayerStationOwnershipType.Individual &&
                        !string.IsNullOrEmpty(successor) && NetProfiles.FindAccount(successor) != null
                        ? successor
                        : (!string.IsNullOrEmpty(record.originalBuilderProfile) &&
                           NetProfiles.FindAccount(record.originalBuilderProfile) != null
                            ? record.originalBuilderProfile : null);
                    record.ownershipType = PlayerStationOwnershipType.Individual;
                    record.ownerProfile = newOwner;
                    record.ownerFactionId = null;
                    record.ownerName = string.IsNullOrEmpty(newOwner) ? "Unclaimed" : newOwner;
                    record.factionStorageAccess = false;
                    record.factionPermissions = NewIndividualFactionPermissionProfile(false);
                }

                record.roleAssignments?.Clear();
                record.bans?.RemoveAll(x => x != null && x.source == PlayerStationBanSource.OwnerAuthority);
                ClearOwnerVisitorRestrictions(record);
                if (record.permissionOverrides != null)
                {
                    for (int i = record.permissionOverrides.Count - 1; i >= 0; i--)
                    {
                        var permission = record.permissionOverrides[i];
                        if (permission == null) { record.permissionOverrides.RemoveAt(i); continue; }
                        permission.state = PlayerStationPermissionState.Inherit;
                        permission.setBy = null;
                        if (permission.builderState == PlayerStationPermissionState.Inherit)
                            record.permissionOverrides.RemoveAt(i);
                    }
                }
                record.pendingTransfer = null;
                record.factionSuccessorProfile = null;
                record.factionSuccessorFactionId = null;
                record.factionSuccessorDelegateProfile = null;
                RecordHistory(record, factionId, "faction-dissolved-ownership-resolved", record.ownerProfile,
                    string.IsNullOrEmpty(record.ownerProfile)
                        ? "No valid successor or recoverable original builder was available; station remains an unowned recoverable orphan."
                        : "Station ownership resolved to " + (record.ownershipType == PlayerStationOwnershipType.Faction
                            ? "successor faction " + record.ownerFactionId : "individual profile " + record.ownerProfile) +
                          ". Dissolved-faction roles, permission overrides, bans and pending transfers were cleared; original-builder restrictions were retained.");
                SyncJumpgatesForStation(record);
            }
        }

        static void ApplyDissolvedFactionAssets(string factionId, string fallbackOwner, List<int> changedWrecks)
        {
            foreach (var station in registry.stations)
            {
                var claim = station?.wreckClaimOpen == true ? station.wreckClaim : null;
                if (claim == null) continue;
                if (string.Equals(claim.factionId, factionId, StringComparison.Ordinal))
                {
                    station.wreckClaim = new PlayerStationWreckClaim();
                    changedWrecks.Add(station.id);
                }
                else if (string.Equals(claim.challengerFactionId, factionId, StringComparison.Ordinal))
                {
                    claim.challengerFactionId = null;
                    claim.challengerProfile = null;
                    claim.challengerSeconds = 0;
                    changedWrecks.Add(station.id);
                }
            }

            var formerFaction = NetFactions.FindById(factionId);
            if (registry.jumpgates == null) return;
            foreach (var gate in registry.jumpgates)
            {
                if (gate == null) continue;
                if (gate.ownershipType == PlayerStationOwnershipType.Faction &&
                    string.Equals(gate.ownerFactionId, factionId, StringComparison.Ordinal))
                {
                    var sponsor = Find(gate.sponsorStationId);
                    if (sponsor != null && !sponsor.wreckClaimOpen && !sponsor.reconstructing && !sponsor.destroyed &&
                        (!string.IsNullOrEmpty(sponsor.ownerProfile) || !string.IsNullOrEmpty(sponsor.ownerFactionId)))
                        CopyStationOwnershipToGate(sponsor, gate);
                    else
                    {
                        gate.ownershipType = PlayerStationOwnershipType.Individual;
                        gate.ownerFactionId = null;
                        gate.ownerProfile = NetProfiles.FindAccount(formerFaction?.leader) != null ? formerFaction.leader : fallbackOwner;
                        gate.ownerName = gate.ownerProfile ?? "Unclaimed";
                    }
                }
                if (string.Equals(gate.accessFactionId, factionId, StringComparison.Ordinal)) gate.accessFactionId = null;
                gate.blacklist?.RemoveAll(entry => entry != null && entry.type == PlayerJumpgateBlockType.Faction &&
                    string.Equals(entry.id, factionId, StringComparison.Ordinal));
            }
        }

        internal static bool PrepareFactionDeparture(string account, string factionId, bool dissolving)
        {
            if (string.IsNullOrEmpty(factionId) || registry?.stations == null) return true;
            var individualStations = string.IsNullOrEmpty(account) ? new List<PlayerStationRecord>() :
                registry.stations.FindAll(x => x != null &&
                    x.ownershipType == PlayerStationOwnershipType.Individual &&
                    string.Equals(x.ownerProfile, account, StringComparison.Ordinal) &&
                    string.Equals(StationFactionId(x), factionId, StringComparison.Ordinal));
            var factionStations = dissolving ? registry.stations.FindAll(x => x != null &&
                x.ownershipType == PlayerStationOwnershipType.Faction &&
                string.Equals(x.ownerFactionId, factionId, StringComparison.Ordinal)) :
                new List<PlayerStationRecord>();
            if (!dissolving && individualStations.Count == 0 && factionStations.Count == 0) return true;

            // A pending transaction may replace the registry during Save.
            if (System.IO.File.Exists(TransactionPath))
            {
                RecoverPendingTransaction();
                return false;
            }
            var before = registry;
            string previousRegistry = JsonUtility.ToJson(registry, true);
            foreach (var record in individualStations)
            {
                record.factionStorageAccess = false;
                record.factionPermissions = NewIndividualFactionPermissionProfile(false);
                RecordHistory(record, account, "owner-left-faction", null,
                    "Former faction defaults were revoked; individually assigned permissions remain.");
            }
            var changedWrecks = new List<int>();
            if (dissolving)
            {
                MigrateFactionConstructionStock();
                ApplyFactionDissolution(factionId);
                ApplyDissolvedFactionAssets(factionId, account, changedWrecks);
            }

            if (Save())
            {
                foreach (int stationId in changedWrecks) wreckControl.Remove(stationId);
                foreach (var record in individualStations) EjectDeniedVisitors(record);
                foreach (var record in factionStations) EjectDeniedVisitors(record);
                return true;
            }
            if (ReferenceEquals(registry, before)) registry = JsonUtility.FromJson<Registry>(previousRegistry);
            if (registry == null)
            {
                persistenceFaulted = true;
                Debug.LogError("NetPlayerStations: faction departure rollback failed; station persistence is disabled.");
            }
            return false;
        }

        internal static bool OnFactionMemberLeaving(string account, string factionId) =>
            PrepareFactionDeparture(account, factionId, false);

        internal static bool ResolveFactionDissolution(string factionId) =>
            PrepareFactionDeparture(null, factionId, true);

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
