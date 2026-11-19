using System;
using System.Collections.Generic;
using UnityEngine;

namespace GoF2Remake.Multiplayer
{
    public static partial class NetPlayerStations
    {
        const int MaxJumpgateBuildSeconds = 2592000;

        /// <summary>Whether this orbit contains a completed player-built jumpgate.</summary>
        internal static bool HasJumpgateAtStation(int stationIndex)
        {
            var record = registry?.stations?.Find(x => x != null && x.stationIndex == stationIndex);
            if (record == null || record.jumpgateDestinationSystem < 0) return false;
            CompleteJumpgateIfDue(record);
            return record.jumpgateDestinationSystem >= 0 && !record.jumpgateBuilding;
        }

        /// <summary>Returns the fixed arrival orbit for this gate, or -1 when this is not an operational player gate.</summary>
        internal static int JumpgateDestinationStation(int stationIndex)
        {
            if (!HasJumpgateAtStation(stationIndex)) return -1;
            var gate = registry?.stations?.Find(x => x != null && x.stationIndex == stationIndex);
            var db = NetGame.Db;
            var target = db?.Systems?.Find(x => x != null && x.index == gate?.jumpgateDestinationSystem);
            if (target?.stations == null || target.stations.Count == 0) return -1;
            return target.stations.Contains(target.jumpgateStation) ? target.jumpgateStation : target.stations[0];
        }

        static void CompleteJumpgateIfDue(PlayerStationRecord record)
        {
            if (record == null || !record.jumpgateBuilding || record.jumpgateCompletesUtc <= 0 ||
                DateTimeOffset.UtcNow.ToUnixTimeSeconds() < record.jumpgateCompletesUtc) return;
            if (!NetGame.IsServer) return;
            record.jumpgateBuilding = false;
            if (!Save()) record.jumpgateBuilding = true;
        }

        static List<PlayerStationMaterial> RequiredJumpgateMaterials() => new List<PlayerStationMaterial>
        {
            new PlayerStationMaterial { item = 156, required = 500 },  // Iron
            new PlayerStationMaterial { item = 155, required = 250 },  // Titanium
            new PlayerStationMaterial { item = 118, required = 200 },  // Electronics
            new PlayerStationMaterial { item = 127, required = 150 },  // Microchips
        };

        internal static string StartJumpgate(NetPlayer by, int stationId, int destinationSystem, int buildSeconds)
        {
            if (!NetGame.IsServer || !sessionActive || registry == null)
                return "Player-built jumpgates need a persistent host or dedicated server.";
            var record = Find(stationId);
            if (record == null) return "No player station with that id exists.";
            if (!CanManage(by, record, out _))
                return "Only the station owner or an authorised faction manager can build a jumpgate in this orbit while docked here.";
            if (record.underConstruction) return "The station orbit must be operational before a jumpgate can be built.";
            var destination = NetGame.Db?.Systems?.Find(x => x != null && x.index == destinationSystem);
            if (destination == null || destination.stations == null || destination.stations.Count == 0)
                return "Choose a valid destination system with at least one orbital location.";
            if (destinationSystem == record.system) return "A jumpgate must connect to a different system.";
            if (buildSeconds < 0 || buildSeconds > MaxJumpgateBuildSeconds)
                return "Build time must be between 0 and 2,592,000 seconds (30 days). Use 0 for no build timer.";
            if (record.jumpgateDestinationSystem >= 0 || record.jumpgateBuilding)
                return "This orbit already has a player-built jumpgate or an active gate project. Only one gate can occupy an orbit.";
            var previousDestination = record.jumpgateDestinationSystem;
            var previousBuilding = record.jumpgateBuilding;
            var previousStart = record.jumpgateStartedUtc;
            var previousEnd = record.jumpgateCompletesUtc;
            var previousMaterials = record.jumpgateMaterials;
            var previousContributions = record.jumpgateContributions;
            record.jumpgateDestinationSystem = destinationSystem;
            record.jumpgateBuilding = true;
            record.jumpgateStartedUtc = 0;
            record.jumpgateCompletesUtc = 0;
            record.jumpgateMaterials = RequiredJumpgateMaterials();
            record.jumpgateContributions = new List<PlayerStationContribution>();
            // The optional timer begins once the full material requirement has been funded.
            record.jumpgateBuildSeconds = buildSeconds;
            if (!Save())
            {
                record.jumpgateDestinationSystem = previousDestination;
                record.jumpgateBuilding = previousBuilding;
                record.jumpgateStartedUtc = previousStart;
                record.jumpgateCompletesUtc = previousEnd;
                record.jumpgateBuildSeconds = previousBuildSeconds;
                record.jumpgateMaterials = previousMaterials;
                record.jumpgateContributions = previousContributions;
                return "The jumpgate construction plan could not be saved.";
            }
            return "Jumpgate project started in " + record.name + "'s orbit, linking to " + destination.name +
                ". Required materials: 500 Iron, 250 Titanium, 200 Electronics and 150 Microchips. " +
                (buildSeconds == 0 ? "The gate will become operational as soon as it is fully funded." :
                "After funding, construction will take " + TimeText(buildSeconds) + ".") +
                " Contribute with /station jumpgate-contribute " + stationId + " <item id> <amount>.";
        }

        internal static string ContributeJumpgate(NetPlayer by, int stationId, int item, int amount)
        {
            if (!NetGame.IsServer || !sessionActive || registry == null)
                return "Player jumpgate construction needs a persistent server.";
            if (by == null || !by.IsSpawned || !by.InHangar)
                return "Dock at any station to contribute jumpgate construction materials.";
            if (!NetProfiles.Enabled || !NetProfiles.Controls(by.OwnerClientId))
                return "Use a controlled player profile to contribute materials.";
            if (amount < 1 || amount > 500 || !NetGuard.Item(item))
                return "Deposit between 1 and 500 units of a valid item.";
            var record = Find(stationId);
            if (record == null || record.jumpgateDestinationSystem < 0 || !record.jumpgateBuilding ||
                record.jumpgateStartedUtc > 0)
                return "That jumpgate project is not accepting materials.";
            var material = record.jumpgateMaterials?.Find(x => x != null && x.item == item);
            if (material == null) return "That item is not required for this jumpgate.";
            int remaining = material.required - material.delivered;
            if (amount > remaining) return "The project needs only " + remaining + " more units of that material.";
            string account = NetProfiles.AccountOf(by.OwnerClientId);
            if (string.IsNullOrEmpty(account)) return "Your controlled player profile is unavailable.";

            bool factionOwned = record.ownershipType == PlayerStationOwnershipType.Faction;
            long unitValue = 0, payout = 0;
            if (factionOwned)
            {
                unitValue = Math.Max(1, NetGame.Db?.Item(item)?.minPrice ?? 0);
                if (unitValue > long.MaxValue / amount) return "The contribution value is outside the supported credit range.";
                payout = unitValue * amount;
                long bank = NetFactions.BankAmount(record.ownerFactionId);
                if (bank < 0) return "The owning faction treasury is unavailable; no materials were taken.";
                if (bank < payout) return "The faction bank needs " + payout.ToString("N0") +
                    " credits to pay this contribution but only has " + bank.ToString("N0") + ". Your materials were not taken.";
            }

            record.jumpgateContributions ??= new List<PlayerStationContribution>();
            var contribution = record.jumpgateContributions.Find(x => x != null &&
                string.Equals(x.contributorProfile, account, StringComparison.Ordinal) && x.item == item);
            if (contribution == null)
                record.jumpgateContributions.Add(contribution = new PlayerStationContribution
                { contributorProfile = account, item = item, amount = 0, unitValue = unitValue,
                    contributedUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds() });
            int oldDelivered = material.delivered;
            int oldAmount = contribution.amount;
            long oldPaid = contribution.paidCredits, oldPaidUtc = contribution.paidUtc;
            bool oldBuilding = record.jumpgateBuilding;
            long oldStarted = record.jumpgateStartedUtc, oldCompletes = record.jumpgateCompletesUtc;
            material.delivered += amount;
            contribution.amount += amount;
            if (factionOwned)
            {
                contribution.unitValue = unitValue;
                contribution.paidCredits += payout;
                contribution.paidUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            }
            bool complete = record.jumpgateMaterials.TrueForAll(x => x == null || x.delivered >= x.required);
            if (complete)
            {
                int duration = Math.Max(0, record.jumpgateBuildSeconds);
                long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                record.jumpgateStartedUtc = now;
                record.jumpgateCompletesUtc = now + duration;
                record.jumpgateBuilding = duration > 0;
            }

            bool committed = factionOwned
                ? CommitConstructionContribution(account, record.ownerFactionId, item, amount, payout)
                : CommitCargoTransaction(account, (item, -amount));
            if (!committed)
            {
                material.delivered = oldDelivered;
                contribution.amount = oldAmount;
                contribution.paidCredits = oldPaid;
                contribution.paidUtc = oldPaidUtc;
                record.jumpgateBuilding = oldBuilding;
                record.jumpgateStartedUtc = oldStarted;
                record.jumpgateCompletesUtc = oldCompletes;
                if (contribution.amount <= 0) record.jumpgateContributions.Remove(contribution);
                return "The contribution transaction could not be committed; no materials were taken.";
            }
            if (!(NetGame.PersistentHost && by.OwnerClientId == NetGame.LocalId))
                NetState.Instance?.SendAdmin(by.OwnerClientId, NetAdmin.Order.Take, item, amount, 0, "", "Jumpgate construction");
            string message = "Contributed " + amount + " x " + NetGame.Db.Item(item).name + " to the jumpgate in " + record.name + "'s orbit.";
            if (factionOwned) message += " Paid " + payout.ToString("N0") + " credits from the faction bank.";
            if (complete) message += record.jumpgateBuilding
                ? " Materials complete. Construction timer started."
                : " Jumpgate is now operational.";
            return message;
        }

        internal static string JumpgateStatus(NetPlayer by, int stationId)
        {
            var record = Find(stationId);
            if (record == null) return "No player station with that id exists.";
            if (!CanViewStationRecord(by, record)) return "Jumpgate project details are private to station managers.";
            CompleteJumpgateIfDue(record);
            if (record.jumpgateDestinationSystem < 0) return "No jumpgate is built or under construction in this orbit.";
            string destination = NetGame.Db?.Systems?.Find(x => x != null && x.index == record.jumpgateDestinationSystem)?.name
                ?? ("System " + record.jumpgateDestinationSystem);
            var lines = new List<string> { "Jumpgate in " + record.name + "'s orbit", "Destination: " + destination };
            if (record.jumpgateMaterials != null)
                foreach (var m in record.jumpgateMaterials)
                    if (m != null) lines.Add((NetGame.Db?.Item(m.item)?.name ?? ("Item " + m.item)) +
                        ": " + m.delivered + "/" + m.required);
            if (record.jumpgateStartedUtc <= 0) lines.Add("Status: awaiting materials");
            else if (record.jumpgateBuilding)
            {
                long remaining = Math.Max(0, record.jumpgateCompletesUtc - DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                lines.Add("Status: under construction, " + TimeText(remaining) + " remaining");
            }
            else lines.Add("Status: operational");
            lines.Add("One jumpgate per orbit. Each gate has one fixed destination system.");
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
