using System;
using System.Collections.Generic;

namespace GoF2Remake.Multiplayer
{
    public static partial class NetPlayerStations
    {
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
            targetAccount = NetProfiles.FindAccount((targetAccount ?? "").Trim());
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

        internal static string AcceptTransfer(NetPlayer by, int id)
        {
            var record = Find(id);
            if (record == null) return "No player station with that id exists.";
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
            if (!IsOwnershipAuthority(record, proposal.proposedBy))
            {
                record.pendingTransfer = null;
                if (!Save()) record.pendingTransfer = proposal;
                return "The original owner's authority changed; the proposal has been cancelled.";
            }

            var previousType = record.ownershipType;
            string previousOwner = record.ownerProfile, previousFaction = record.ownerFactionId, previousName = record.ownerName;
            bool previousFactionAccess = record.factionStorageAccess;
            if (proposal.targetType == PlayerStationOwnershipType.Individual)
            {
                record.ownershipType = PlayerStationOwnershipType.Individual;
                record.ownerProfile = proposal.targetProfile;
                record.ownerFactionId = null;
                record.ownerName = by.DisplayName;
                record.factionStorageAccess = false;
            }
            else
            {
                var faction = NetFactions.Of(account);
                record.ownershipType = PlayerStationOwnershipType.Faction;
                record.ownerFactionId = proposal.targetFactionId;
                record.ownerProfile = faction?.leader ?? account;
                record.ownerName = faction?.name ?? "Faction";
                record.factionStorageAccess = true;
            }
            record.pendingTransfer = null;
            // Gates are assets of the station that sponsored them. Keep their ownership in
            // the same registry transaction as the station transfer.
            SyncJumpgatesForStation(record);
            if (!Save())
            {
                record.ownershipType = previousType;
                record.ownerProfile = previousOwner;
                record.ownerFactionId = previousFaction;
                record.ownerName = previousName;
                record.factionStorageAccess = previousFactionAccess;
                record.pendingTransfer = proposal;
                SyncJumpgatesForStation(record);
                return "The ownership change could not be saved; the previous owner and pending proposal were restored.";
            }
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
            return registry.stations.Exists(station => station?.listings?.Exists(listing =>
                listing != null && string.Equals(listing.payoutFactionId, factionId, StringComparison.Ordinal)) == true);
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