using System;
using System.Collections.Generic;

namespace GoF2Remake.Multiplayer
{
    public static partial class NetPlayerStations
    {
        public const long StationClaimPrice = 25_000_000;

        public static bool StationOfferedForClaim(int orbit) => StationAt(orbit)?.offeredForClaimUtc > 0;

        internal static string OfferStationForPublicClaim(NetPlayer pilot, PlayerStationRecord station)
        {
            if (!IsStationContext(pilot, station, out string account) || !IsOwnershipAuthority(station, account))
                return "Dock at this station as its owner or owning faction leader to open it for public claim.";
            if (station.underConstruction || station.reconstructing || station.wreckClaimOpen || station.destroyed || station.retired)
                return "Only an operational station can be offered for public claim.";

            var original = registry;
            int index = original.stations.IndexOf(station);
            var offer = UnityEngine.JsonUtility.FromJson<PlayerStationRecord>(UnityEngine.JsonUtility.ToJson(station));
            offer.offeredForClaimUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            offer.pendingTransfer = null;
            RecordHistory(offer, account, "opened-for-public-claim");
            ReplaceStationRecord(original, index, offer);
            if (!Save())
            {
                if (ReferenceEquals(registry, original)) ReplaceStationRecord(original, index, station);
                return "The station could not be opened for public claim.";
            }
            NetState.Instance?.BroadcastStock(offer.stationIndex);
            return $"{offer.name} is open to claim for {StationClaimPrice:N0} credits.";
        }

        internal static string ClaimOfferedStation(NetPlayer pilot, int id)
        {
            var offer = Find(id);
            if (!NetGame.IsServer || !sessionActive || offer == null || offer.offeredForClaimUtc <= 0)
                return "That station is not open for public claim.";
            if (pilot == null || !pilot.IsSpawned || !pilot.InHangar || pilot.Station != offer.stationIndex ||
                !NetProfiles.Controls(pilot.OwnerClientId))
                return "Dock at the station with a controlled profile to claim it.";
            string account = NetProfiles.AccountOf(pilot.OwnerClientId);
            if (string.IsNullOrEmpty(account) || NetProfiles.CreditAmount(account) < StationClaimPrice)
                return $"Claiming this station costs {StationClaimPrice:N0} credits.";

            var original = registry;
            int index = original.stations.IndexOf(offer);
            var station = UnityEngine.JsonUtility.FromJson<PlayerStationRecord>(UnityEngine.JsonUtility.ToJson(offer));
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            long paused = Math.Max(0, now - station.offeredForClaimUtc);
            bool relinquishBuilder = !string.IsNullOrEmpty(station.originalBuilderProfile) &&
                station.history?.FindLast(value => value != null && value.action == "opened-for-public-claim") is PlayerStationHistoryEntry offerHistory &&
                HasBuilderRights(station, offerHistory.actorAccount);
            if (relinquishBuilder)
            {
                station.builderRightsRelinquishedUtc = now;
                station.permissionOverrides?.Clear();
                station.bans?.Clear();
            }
            else
            {
                station.permissionOverrides?.RemoveAll(value => value == null ||
                    value.builderState == PlayerStationPermissionState.Inherit);
                foreach (var value in station.permissionOverrides ?? new List<PlayerStationPermissionOverride>())
                {
                    value.state = PlayerStationPermissionState.Inherit;
                    value.setBy = null;
                }
                station.bans?.RemoveAll(value => value == null || value.source == PlayerStationBanSource.OwnerAuthority);
            }
            station.roleAssignments?.Clear();
            station.individualSuccessorProfile = null;
            station.factionSuccessorProfile = null;
            station.factionSuccessorFactionId = null;
            station.factionSuccessorType = PlayerStationOwnershipType.Individual;
            station.factionSuccessorDelegateProfile = null;
            station.ownershipType = PlayerStationOwnershipType.Individual;
            station.ownerProfile = account;
            station.ownerFactionId = null;
            station.ownerName = pilot.DisplayName;
            station.factionStorageAccess = false;
            station.factionPermissions = NewIndividualFactionPermissionProfile(false);
            station.offeredForClaimUtc = 0;
            ClearOwnerVisitorRestrictions(station);

            if (station.dryDockBuilding)
            {
                station.dryDockStartedUtc += paused;
                station.dryDockCompletesUtc += paused;
            }
            if (station.factoryJobs != null)
                foreach (var job in station.factoryJobs)
                {
                    if (job == null) continue;
                    job.startedUtc += paused;
                    job.completesUtc += paused;
                }

            RecordHistory(station, account, "public-claim-completed");
            var rollbackGates = RestoreSponsoredGateOwnership(station);
            ReplaceStationRecord(original, index, station);
            foreach (var gate in original.jumpgates)
            {
                if (gate == null || gate.sponsorStationId != station.id || !gate.building ||
                    gate.permanentlyDestroyed || gate.startedUtc <= 0 || gate.completesUtc <= 0) continue;
                gate.startedUtc += paused;
                gate.completesUtc += paused;
            }
            SyncJumpgatesForStation(station);
            if (!CommitProfileTransaction(null, new List<(string account, long delta)> { (account, -StationClaimPrice) }, null))
            {
                if (ReferenceEquals(registry, original))
                {
                    ReplaceStationRecord(original, index, offer);
                    rollbackGates();
                }
                return "The claim could not be saved. Your credits were kept.";
            }

            NotifyProfileCreditChange(account, -StationClaimPrice, "Public station claim");
            EjectDeniedVisitors(station);
            NetState.Instance?.BroadcastStock(station.stationIndex);
            return $"You claimed {station.name} for {StationClaimPrice:N0} credits. Its facilities are open.";
        }

        const long TransferProposalLifetimeSeconds = 7 * 24 * 60 * 60;

        static bool IsOwnershipAuthority(PlayerStationRecord record, string account)
        {
            if (record == null || string.IsNullOrEmpty(account)) return false;
            if (record.ownershipType == PlayerStationOwnershipType.Individual)
                return string.Equals(record.ownerProfile, account, StringComparison.Ordinal);
            var faction = NetFactions.Of(account);
            return faction != null && string.Equals(faction.id, record.ownerFactionId, StringComparison.Ordinal) &&
                string.Equals(faction.leader, account, StringComparison.Ordinal);
        }

        internal static string RenameStation(NetPlayer by, int id, string name)
        {
            var record = Find(id);
            if (record == null) return "No player station with that id exists.";
            if (!CanManage(by, record, out string account) || !IsOwnershipAuthority(record, account))
                return "Only the individual owner or owning faction leader can rename a station while docked there.";
            name = (name ?? "").Trim();
            if (!ValidStationName(name)) return StationNameRules;
            string previous = record.name;
            if (string.Equals(name, previous, StringComparison.Ordinal)) return "The station already has that name.";

            record.name = name;
            if (!Save())
            {
                record.name = previous;
                return "The station name could not be saved.";
            }
            Apply(NetGame.Db);
            return "Station renamed to " + name + ".";
        }

        /// <summary>
        /// Propose an ownership change. targetAccount is either the receiving player's account ID,
        /// or an account belonging to the receiving faction (its current leader must accept).
        /// </summary>
        internal static string ProposeTransfer(NetPlayer by, int id, string targetKind, string targetAccount)
        {
            var record = Find(id);
            if (record == null) return "No player station with that id exists.";
            if (!CanManage(by, record, out string account) || !IsOwnershipAuthority(record, account))
                return "Only the individual owner or owning faction leader can propose a station transfer while docked there.";
            targetKind = (targetKind ?? "").Trim().ToLowerInvariant();
            targetAccount = ResolveStationAccount(targetAccount);
            if (targetKind != "player" && targetKind != "faction")
                return "Choose a recipient type: player or faction.";
            if (string.IsNullOrEmpty(targetAccount)) return "Supply the receiving player's or faction leader's profile account ID.";
            var previousProposal = record.pendingTransfer;
            if (targetKind == "player")
            {
                if (string.Equals(account, targetAccount, StringComparison.Ordinal))
                    return "The station is already owned by that profile.";
                record.pendingTransfer = new PlayerStationTransferProposal
                {
                    targetType = PlayerStationOwnershipType.Individual,
                    targetProfile = targetAccount,
                    proposedBy = account,
                    expiresUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + TransferProposalLifetimeSeconds
                };
            }
            else
            {
                var faction = NetFactions.Of(targetAccount);
                if (faction == null || string.IsNullOrEmpty(faction.id) || string.IsNullOrEmpty(faction.leader))
                    return "The supplied account must belong to the receiving faction.";
                if (record.ownershipType == PlayerStationOwnershipType.Faction &&
                    string.Equals(record.ownerFactionId, faction.id, StringComparison.Ordinal))
                    return "The station already belongs to that faction.";
                record.pendingTransfer = new PlayerStationTransferProposal
                {
                    targetType = PlayerStationOwnershipType.Faction,
                    targetFactionId = faction.id,
                    targetProfile = faction.leader,
                    proposedBy = account,
                    expiresUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + TransferProposalLifetimeSeconds
                };
            }
            if (!Save())
            {
                record.pendingTransfer = previousProposal;
                return "The transfer proposal could not be saved.";
            }
            string recipient = record.pendingTransfer.targetProfile;
            foreach (ulong client in NetProfiles.ClientsOf(recipient))
                NetState.Instance?.Notify(client, "A station ownership transfer for " + record.name +
                    " is awaiting your acceptance. Dock there and use /station transfer-accept " + id + ".");
            return "Transfer proposal recorded. The receiving player or faction leader must dock at this station and use /station transfer-accept " + id + " within seven days.";
        }

        /// <summary>
        /// An administrator may authorize a nominated individual successor when the owner has
        /// genuinely lost access. The successor still has to dock and accept; no inactivity
        /// timer or automatic transfer is used.
        /// </summary>
        internal static string AuthorizeIndividualRecovery(NetPlayer by, int id, string reason)
        {
            var record = Find(id);
            if (record == null) return "No player station with that id exists.";
            if (record.offeredForClaimUtc > 0) return "Claim the station before changing its ownership.";
            if (!NetGame.IsServer || by == null || !by.IsSpawned || !by.InHangar ||
                by.Station != record.stationIndex || !NetProfiles.Enabled ||
                !NetProfiles.Controls(by.OwnerClientId) || !NetCommands.IsAdmin(by))
                return "Only a server admin docked at the affected station may authorize ownership recovery.";
            if (record.ownershipType != PlayerStationOwnershipType.Individual ||
                string.IsNullOrEmpty(record.ownerProfile))
                return "Administrative individual-owner recovery applies only to individually owned stations.";
            string successor = record.individualSuccessorProfile;
            if (string.IsNullOrEmpty(successor) || !string.Equals(NetProfiles.FindAccount(successor), successor, StringComparison.Ordinal))
                return "The station has no valid nominated individual successor. The owner must have nominated one before losing access.";
            if (record.pendingTransfer != null)
                return "Resolve or cancel the existing ownership transfer before authorizing recovery.";
            reason = (reason ?? "").Trim();
            if (reason.Length < 12 || reason.Length > 240 ||
                reason.IndexOfAny(new[] { '\r', '\n', '\t', '\0' }) >= 0)
                return "Provide a concise verification note between 12 and 240 characters.";
            string admin = NetProfiles.AccountOf(by.OwnerClientId);
            if (string.IsNullOrEmpty(admin)) return "The authorizing administrator has no verified profile account.";
            var proposal = new PlayerStationTransferProposal
            {
                targetType = PlayerStationOwnershipType.Individual,
                targetProfile = successor,
                proposedBy = record.ownerProfile,
                administrativeRecovery = true,
                recoveryAuthorizedBy = admin,
                recoveryReason = reason,
                expiresUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + TransferProposalLifetimeSeconds
            };
            record.pendingTransfer = proposal;
            int oldHistoryCount = record.history?.Count ?? 0;
            int oldSequence = record.nextHistorySequence;
            RecordHistory(record, admin, "individual-recovery-authorized", successor,
                "A server administrator verified an owner-access-loss case and authorized the pre-nominated successor. Verification note: " + reason);
            if (!Save())
            {
                record.pendingTransfer = null;
                if (record.history != null && record.history.Count > oldHistoryCount)
                    record.history.RemoveRange(oldHistoryCount, record.history.Count - oldHistoryCount);
                record.nextHistorySequence = oldSequence;
                return "Recovery authorization could not be saved; ownership is unchanged.";
            }
            foreach (ulong client in NetProfiles.ClientsOf(successor))
                NetState.Instance?.Notify(client, "An administrator authorized succession for " + record.name +
                    ". Dock there and use /station transfer-accept " + id + " within seven days.");
            return "Recovery authorized for the nominated successor. They must dock at the station and use /station transfer-accept " + id + " within seven days. No ownership changes until they accept.";
        }

        internal static string AcceptTransfer(NetPlayer by, int id)
        {
            var record = Find(id);
            if (record == null) return "No player station with that id exists.";
            if (record.offeredForClaimUtc > 0) return "Claim the station before changing its ownership.";
            if (!NetGame.IsServer || by == null || !by.IsSpawned || !by.InHangar ||
                by.Station != record.stationIndex || !NetProfiles.Enabled || !NetProfiles.Controls(by.OwnerClientId))
                return "The receiving owner must dock at the station using a controlled profile to accept.";
            var proposal = record.pendingTransfer;
            if (proposal == null) return "There is no pending transfer for this station.";
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (proposal.expiresUtc <= now)
            {
                record.pendingTransfer = null;
                if (!Save()) record.pendingTransfer = proposal;
                return "That transfer proposal expired.";
            }
            string account = NetProfiles.AccountOf(by.OwnerClientId);
            bool accepted = proposal.targetType == PlayerStationOwnershipType.Individual
                ? string.Equals(account, proposal.targetProfile, StringComparison.Ordinal)
                : IsFactionLeaderOf(account, proposal.targetFactionId);
            if (!accepted) return "This profile is not authorised to accept the pending transfer.";
            bool validRecovery = proposal.administrativeRecovery &&
                record.ownershipType == PlayerStationOwnershipType.Individual &&
                string.Equals(record.ownerProfile, proposal.proposedBy, StringComparison.Ordinal) &&
                string.Equals(record.individualSuccessorProfile, proposal.targetProfile, StringComparison.Ordinal) &&
                !string.IsNullOrEmpty(proposal.recoveryAuthorizedBy);
            if (proposal.administrativeRecovery && !validRecovery)
            {
                record.pendingTransfer = null;
                if (!Save()) record.pendingTransfer = proposal;
                return "The recovery authorization is no longer valid; the nomination or ownership changed.";
            }
            if (!validRecovery && !IsOwnershipAuthority(record, proposal.proposedBy))
            {
                record.pendingTransfer = null;
                if (!Save()) record.pendingTransfer = proposal;
                return "The original owner's authority changed; the proposal has been cancelled.";
            }

            var previousType = record.ownershipType;
            string previousOwner = record.ownerProfile, previousFaction = record.ownerFactionId, previousName = record.ownerName;
            bool previousFactionAccess = record.factionStorageAccess;
            var previousFactionPermissions = record.factionPermissions;
            long previousBuilderRelinquished = record.builderRightsRelinquishedUtc;
            var previousPermissionOverrides = ClonePermissionOverrides(record);
            var previousBans = CloneBans(record);
            var previousRoles = new List<PlayerStationRoleAssignment>();
            foreach (var role in record.roleAssignments ?? new List<PlayerStationRoleAssignment>())
                if (role != null) previousRoles.Add(new PlayerStationRoleAssignment
                { account = role.account, role = role.role, assignedBy = role.assignedBy, assignedUtc = role.assignedUtc });
            string previousIndividualSuccessor = record.individualSuccessorProfile;
            string previousFactionSuccessor = record.factionSuccessorProfile;
            string previousFactionSuccessorId = record.factionSuccessorFactionId;
            PlayerStationOwnershipType previousFactionSuccessorType = record.factionSuccessorType;
            string previousSuccessorDelegate = record.factionSuccessorDelegateProfile;
            int previousHistoryCount = record.history?.Count ?? 0;
            int previousHistorySequence = record.nextHistorySequence;
            if (proposal.targetType == PlayerStationOwnershipType.Individual)
            {
                record.ownershipType = PlayerStationOwnershipType.Individual;
                record.ownerProfile = proposal.targetProfile;
                record.ownerFactionId = null;
                record.ownerName = by.DisplayName;
                record.factionStorageAccess = false;
                record.factionPermissions = NewIndividualFactionPermissionProfile(false);
            }
            else
            {
                var faction = NetFactions.Of(account);
                record.ownershipType = PlayerStationOwnershipType.Faction;
                record.ownerFactionId = proposal.targetFactionId;
                record.ownerProfile = faction?.leader ?? account;
                record.ownerName = faction?.name ?? "Faction";
                record.factionStorageAccess = true;
                record.factionPermissions = NewFactionOwnedPermissionProfile();
            }
            // An intact voluntary handover starts a fresh governance regime. Previous owner
            // permissions, roles, bans and succession instructions do not silently become the
            // new owner's policy. Original-builder visitor permissions/bans survive only while
            // those reserved rights remain active.
            record.roleAssignments?.Clear();
            record.bans?.RemoveAll(x => x == null || x.source == PlayerStationBanSource.OwnerAuthority);
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
            record.individualSuccessorProfile = null;
            record.factionSuccessorProfile = null;
            record.factionSuccessorFactionId = null;
            record.factionSuccessorType = PlayerStationOwnershipType.Individual;
            record.factionSuccessorDelegateProfile = null;
            ClearOwnerVisitorRestrictions(record);
            record.pendingTransfer = null;
            // A transfer initiated by the original builder is their voluntary relinquishment
            // of builder authority. A faction leader or later owner cannot erase those rights.
            bool builderVoluntaryTransfer = !proposal.administrativeRecovery && HasBuilderRights(record, proposal.proposedBy);
            if (builderVoluntaryTransfer)
            {
                record.builderRightsRelinquishedUtc = now;
                ConvertBuilderAuthorityRecords(record);
                record.permissionOverrides?.Clear();
                record.bans?.RemoveAll(x => x == null || x.source == PlayerStationBanSource.OwnerAuthority);
            }
            string newOwner = proposal.targetType == PlayerStationOwnershipType.Faction
                ? "faction " + (proposal.targetFactionId ?? "")
                : "profile " + (proposal.targetProfile ?? "");
            RecordHistory(record, proposal.administrativeRecovery ? proposal.recoveryAuthorizedBy : proposal.proposedBy,
                "ownership-transferred", proposal.targetProfile,
                "Ownership changed from " + (previousType == PlayerStationOwnershipType.Faction
                    ? "faction " + (previousFaction ?? "") : "profile " + (previousOwner ?? "")) +
                " to " + newOwner +
                (proposal.administrativeRecovery
                    ? "; server-admin-authorized individual-owner recovery. Verification note: " + proposal.recoveryReason
                    : "; previous owner permissions, faction bans, station roles and succession nominations were cleared.") +
                (builderVoluntaryTransfer
                    ? " Original builder voluntarily relinquished builder rights."
                    : " Original builder rights and builder-imposed visitor bans were preserved."));
            // Gates are assets of the station that sponsored them. Keep their ownership in
            // the same registry transaction as the station transfer.
            var rollbackGateOwnership = RestoreSponsoredGateOwnership(record);
            SyncJumpgatesForStation(record);
            if (!Save())
            {
                record.ownershipType = previousType;
                record.ownerProfile = previousOwner;
                record.ownerFactionId = previousFaction;
                record.ownerName = previousName;
                record.factionStorageAccess = previousFactionAccess;
                record.factionPermissions = previousFactionPermissions;
                record.builderRightsRelinquishedUtc = previousBuilderRelinquished;
                record.permissionOverrides = previousPermissionOverrides;
                record.bans = previousBans;
                record.roleAssignments = previousRoles;
                record.individualSuccessorProfile = previousIndividualSuccessor;
                record.factionSuccessorProfile = previousFactionSuccessor;
                record.factionSuccessorFactionId = previousFactionSuccessorId;
                record.factionSuccessorType = previousFactionSuccessorType;
                record.factionSuccessorDelegateProfile = previousSuccessorDelegate;
                if (record.history != null && record.history.Count > previousHistoryCount)
                    record.history.RemoveRange(previousHistoryCount, record.history.Count - previousHistoryCount);
                record.nextHistorySequence = previousHistorySequence;
                record.pendingTransfer = proposal;
                rollbackGateOwnership();
                return "The ownership change could not be saved; the previous owner and pending proposal were restored.";
            }
            EjectDeniedVisitors(record);
            foreach (ulong client in NetProfiles.ClientsOf(proposal.proposedBy))
                NetState.Instance?.Notify(client, "Ownership of " + record.name + " has been transferred successfully.");
            return "Station ownership transferred successfully. Construction contributions were settled when deposited; no contributor compensation carries over. Personal ships, contributor-owned storage and existing marketplace seller claims remain separate from station ownership.";
        }

        static bool IsFactionLeaderOf(string account, string factionId)
        {
            if (string.IsNullOrEmpty(account) || string.IsNullOrEmpty(factionId)) return false;
            var faction = NetFactions.Of(account);
            return faction != null && string.Equals(faction.id, factionId, StringComparison.Ordinal) &&
                string.Equals(faction.leader, account, StringComparison.Ordinal);
        }

        internal static string CancelTransfer(NetPlayer by, int id)
        {
            var record = Find(id);
            if (record == null) return "No player station with that id exists.";
            if (!CanManage(by, record, out string account) || !IsOwnershipAuthority(record, account))
                return "Only the individual owner or owning faction leader can cancel a transfer proposal.";
            if (record.pendingTransfer == null) return "There is no pending transfer for this station.";
            var proposal = record.pendingTransfer;
            record.pendingTransfer = null;
            if (Save()) return "Transfer proposal cancelled.";
            record.pendingTransfer = proposal;
            return "The cancellation could not be saved; the proposal remains active.";
        }

        internal static string TransferStatus(NetPlayer by, int id)
        {
            var record = Find(id);
            if (record == null) return "No player station with that id exists.";
            var proposal = record.pendingTransfer;
            string account = by == null ? null : NetProfiles.AccountOf(by.OwnerClientId);
            bool recipient = proposal != null && (proposal.targetType == PlayerStationOwnershipType.Individual
                ? string.Equals(account, proposal.targetProfile, StringComparison.Ordinal)
                : IsFactionLeaderOf(account, proposal.targetFactionId));
            if (!CanViewStationRecord(by, record) && !recipient)
                return "Transfer details are private to the station owner and intended recipient.";
            if (proposal == null) return "No ownership transfer is pending.";
            long remaining = Math.Max(0, proposal.expiresUtc - DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            string target = proposal.targetType == PlayerStationOwnershipType.Faction
                ? "faction " + proposal.targetFactionId : "profile " + proposal.targetProfile;
            return "Pending transfer of " + record.name + " to " + target + ". Expires in " + remaining + " seconds.";
        }

        internal static bool HasFactionOwnedStations(string factionId) =>
            !string.IsNullOrEmpty(factionId) &&
            registry?.stations?.Exists(x => x != null && x.ownershipType == PlayerStationOwnershipType.Faction &&
                string.Equals(x.ownerFactionId, factionId, StringComparison.Ordinal)) == true;

        internal static bool HasFactionMarketplaceClaims(string factionId)
        {
            if (string.IsNullOrEmpty(factionId) || registry?.stations == null) return false;
            return registry.stations.Exists(station =>
                station?.listings?.Exists(listing =>
                    listing != null && string.Equals(listing.payoutFactionId, factionId, StringComparison.Ordinal)) == true ||
                station?.buyOrders?.Exists(order =>
                    order != null && string.Equals(order.fundedByFactionId, factionId, StringComparison.Ordinal)) == true);
        }

        internal static string ConstructionContributions(NetPlayer by, int id)
        {
            var record = Find(id);
            if (record == null) return "No player station with that id exists.";
            if (!CanManage(by, record, out _)) return "Only the station owner or owning faction managers can view construction contributions.";
            var lines = new List<string>
            {
                "Construction contributions for " + record.name + ":",
                record.ownershipType == PlayerStationOwnershipType.Faction
                    ? "Faction-funded contributions are paid immediately from the owning faction bank."
                    : "Individually owned station contributions are personal investments and are not paid from a faction bank."
            };
            if (record.constructionContributions == null || record.constructionContributions.Count == 0)
                lines.Add("No contribution ledger is available.");
            else
                foreach (var contribution in record.constructionContributions)
                {
                    if (contribution == null) continue;
                    string item = NetGame.Db?.Item(contribution.item)?.name ?? ("Item " + contribution.item);
                    string line = (contribution.contributorProfile ?? "Unknown profile") + ": " +
                        contribution.amount + " x " + item;
                    if (contribution.paidCredits > 0)
                    {
                        line += " | paid " + contribution.paidCredits.ToString("N0") +
                            " credits (" + Math.Max(0, contribution.unitValue).ToString("N0") + " per unit)";
                        if (contribution.paidUtc > 0)
                            line += " | last paid " + DateTimeOffset.FromUnixTimeSeconds(contribution.paidUtc).ToLocalTime().ToString("g");
                    }
                    else
                        line += " | no faction-bank payment recorded";
                    if (contribution.contributedUtc > 0)
                        line += " | first contributed " + DateTimeOffset.FromUnixTimeSeconds(contribution.contributedUtc).ToLocalTime().ToString("g");
                    lines.Add(line);
                }
            return string.Join("\n", lines);
        }
    }
}