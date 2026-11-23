using System;
using System.Collections.Generic;
using UnityEngine;
using GoF2Remake.Data;

namespace GoF2Remake.Multiplayer
{
    public static partial class NetPlayerStations
    {
        const int MaxJumpgateBuildSeconds = 2592000;
        const long JumpgateDestinationCooldownSeconds = 86400;
        static long nextJumpgateRetryUtc;

        static PlayerJumpgateRecord FindJumpgate(int orbitStationIndex) =>
            registry?.jumpgates?.Find(x => x != null && x.orbitStationIndex == orbitStationIndex);

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
            var sponsor = gate == null ? null : Find(gate.sponsorStationId);
            return sponsor != null
                ? CanViewStationRecord(by, sponsor)
                : CanViewStationRecord(by, JumpgateAccessRecord(gate));
        }

        internal static bool CanViewJumpgateManagement(NetPlayer by, PlayerJumpgateRecord gate) =>
            CanViewJumpgate(by, gate);

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

        static bool CanManageJumpgate(NetPlayer by, PlayerJumpgateRecord gate, out string account)
        {
            account = by == null ? null : NetProfiles.AccountOf(by.OwnerClientId);
            if (!NetGame.IsServer || !sessionActive || registry == null || by == null || !by.IsSpawned ||
                !by.InHangar || !NetProfiles.Enabled || !NetProfiles.Controls(by.OwnerClientId) ||
                string.IsNullOrEmpty(account) || gate == null || gate.scrapped || gate.permanentlyDestroyed) return false;
            if (gate.ownershipType == PlayerStationOwnershipType.Individual)
                return string.Equals(account, gate.ownerProfile, StringComparison.Ordinal);
            var faction = NetFactions.Of(account);
            if (faction == null || !string.Equals(faction.id, gate.ownerFactionId, StringComparison.Ordinal)) return false;
            if (string.Equals(faction.leader, account, StringComparison.Ordinal)) return true;
            var sponsor = Find(gate.constructionStationId > 0 ? gate.constructionStationId : gate.sponsorStationId);
            if (sponsor != null && HasRole(sponsor, account, PlayerStationRole.StationAdministrator)) return true;
            return registry?.stations?.Exists(station => station != null &&
                station.ownershipType == PlayerStationOwnershipType.Faction &&
                string.Equals(station.ownerFactionId, gate.ownerFactionId, StringComparison.Ordinal) &&
                HasRole(station, account, PlayerStationRole.StationAdministrator)) == true;
        }

        static bool JumpgateAccessAllowed(PlayerJumpgateRecord gate, string account, string factionId)
        {
            if (gate == null || gate.scrapped || gate.permanentlyDestroyed || gate.building) return false;
            // A faction owns the infrastructure, but individual members remain subject to the
            // blacklist. Only an individually owned gate's actual owner bypasses its blacklist.
            bool owner = gate.ownershipType == PlayerStationOwnershipType.Individual &&
                string.Equals(account, gate.ownerProfile, StringComparison.Ordinal);
            if (owner) return true;
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
            gate.ownershipType = station.ownershipType;
            gate.ownerProfile = station.ownerProfile;
            gate.ownerFactionId = station.ownerFactionId;
            gate.ownerName = station.ownerName;
            gate.accessFactionId = station.ownershipType == PlayerStationOwnershipType.Faction
                ? station.ownerFactionId : NetFactions.Of(station.ownerProfile)?.id;
            return changed;
        }

        /// <summary>Synchronise every gate sponsored by this station as part of the same registry save.</summary>
        internal static bool SyncJumpgatesForStation(PlayerStationRecord station)
        {
            // Gate ownership is independent after its construction order is issued. Station transfers
            // must never silently transfer an already-built gate with the former sponsor.
            return false;
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

        /// <summary>Whether an orbital anchor contains an operational player-built jumpgate.</summary>
        internal static bool HasJumpgateAtStation(int stationIndex)
        {
            var gate = FindJumpgate(stationIndex);
            if (gate == null || gate.destinationSystem < 0 || gate.scrapped || gate.permanentlyDestroyed) return false;
            CompleteJumpgateIfDue(gate);
            return gate.destinationSystem >= 0 && !gate.building && !gate.scrapped && !gate.permanentlyDestroyed;
        }

        /// <summary>Returns the arrival orbit in the gate's fixed destination system.</summary>
        internal static int JumpgateDestinationStation(int stationIndex)
        {
            if (!HasJumpgateAtStation(stationIndex)) return -1;
            var gate = FindJumpgate(stationIndex);
            var target = NetGame.Db?.Systems?.Find(x => x != null && x.index == gate?.destinationSystem);
            if (target?.stations == null || target.stations.Count == 0) return -1;
            return target.stations.Contains(target.jumpgateStation) ? target.jumpgateStation : target.stations[0];
        }

        static void CompleteJumpgateIfDue(PlayerJumpgateRecord gate)
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (gate == null || !gate.building || gate.completesUtc <= 0 ||
                now < gate.completesUtc || now < nextJumpgateRetryUtc || !NetGame.IsServer) return;
            bool oldBuilding = gate.building;
            long oldChanged = gate.destinationChangedUtc;
            gate.building = false;
            if (gate.destinationChangedUtc <= 0) gate.destinationChangedUtc = now;
            if (!Save())
            {
                gate.building = oldBuilding;
                gate.destinationChangedUtc = oldChanged;
                nextJumpgateRetryUtc = now + 5;
            }
            else nextJumpgateRetryUtc = 0;
        }

        /// <summary>Called from the authoritative station update loop; due projects survive restarts.</summary>
        internal static void TickJumpgates()
        {
            if (!NetGame.IsServer || !sessionActive || registry?.jumpgates == null) return;
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (now < nextJumpgateRetryUtc) return;
            foreach (var gate in registry.jumpgates)
            {
                if (gate == null || !gate.salvageDropPending || gate.salvageMaterials == null ||
                    gate.salvageMaterials.Count == 0) continue;
                var runner = NetPlayer.OrbitRunner(gate.orbitStationIndex);
                if (runner == null) continue; // keep the salvage manifest until an orbit authority is present
                var items = new List<int>();
                var amounts = new List<int>();
                foreach (var material in gate.salvageMaterials)
                    if (material != null && material.required > 0 && NetGuard.Item(material.item))
                    { items.Add(material.item); amounts.Add(material.required); }
                if (items.Count == 0) { gate.salvageDropPending = false; Save(); continue; }
                gate.salvageDropPending = false;
                if (!Save())
                {
                    gate.salvageDropPending = true;
                    nextJumpgateRetryUtc = now + 5;
                    return;
                }
                runner.SpawnJumpgateSalvageRpc(gate.orbitStationIndex, items.ToArray(), amounts.ToArray());
            }
            foreach (var gate in registry.jumpgates)
            {
                if (gate == null || !gate.building || gate.startedUtc <= 0 ||
                    gate.completesUtc <= 0 || gate.completesUtc > now) continue;
                CompleteJumpgateIfDue(gate);
                if (gate.building) { nextJumpgateRetryUtc = now + 5; return; }
            }
        }

        static List<PlayerStationMaterial> RequiredJumpgateMaterials() => new List<PlayerStationMaterial>
        {
            new PlayerStationMaterial { item = 156, required = 500 },  // Iron
            new PlayerStationMaterial { item = 155, required = 250 },  // Titanium
            new PlayerStationMaterial { item = 118, required = 200 },  // Electronics
            new PlayerStationMaterial { item = 127, required = 150 },  // Microchips
        };

        /// <summary>Build at any orbital anchor in the sponsor station's system, not only at the sponsor station itself.</summary>
        internal static string StartJumpgate(NetPlayer by, int sponsorStationId, int orbitStationIndex, int destinationSystem, int buildSeconds)
        {
            if (!NetGame.IsServer || !sessionActive || registry == null)
                return "Player-built jumpgates need a persistent host or dedicated server.";
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
            var existingGate = FindJumpgate(orbitStationIndex);
            bool stationRebuild = existingGate != null && existingGate.permanentlyDestroyed;
            if (existingGate != null && !stationRebuild)
                return "That orbit already has a player-built jumpgate, a scrapped gate awaiting reclamation, or an active gate project. Only one gate can occupy an orbit.";

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
                materials = RequiredJumpgateMaterials(),
                contributions = new List<PlayerStationContribution>(),
                destructionCount = stationRebuild ? existingGate.destructionCount : 0,
                salvageMaterials = new List<PlayerStationMaterial>(),
                salvageDropPending = false
            };
            registry.jumpgates ??= new List<PlayerJumpgateRecord>();
            int existingIndex = stationRebuild ? registry.jumpgates.IndexOf(existingGate) : -1;
            if (stationRebuild && existingIndex >= 0) registry.jumpgates[existingIndex] = gate;
            else registry.jumpgates.Add(gate);
            if (!Save())
            {
                if (stationRebuild && existingIndex >= 0) registry.jumpgates[existingIndex] = existingGate;
                else registry.jumpgates.Remove(gate);
                return "The jumpgate construction plan could not be saved.";
            }
            return "Jumpgate project started in orbit " + orbitStationIndex + " of " + origin.name +
                ", linking to " + destination.name + ". Required materials: 500 Iron, 250 Titanium, 200 Electronics and 150 Microchips. " +
                (buildSeconds == 0 ? "The gate will become operational as soon as it is fully funded." :
                "After funding, construction will take " + TimeText(buildSeconds) + ".") +
                " Deliver materials in the construction orbit with /station jumpgate-contribute " + orbitStationIndex + " <item id> <amount>.";
        }

        internal static string ContributeJumpgate(NetPlayer by, int orbitStationIndex, int item, int amount)
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
            var material = gate.materials?.Find(x => x != null && x.item == item);
            if (material == null) return "That item is not required for this jumpgate.";
            int remaining = material.required - material.delivered;
            if (amount > remaining) return "The project needs only " + remaining + " more units of that material.";
            string account = NetProfiles.AccountOf(by.OwnerClientId);
            if (string.IsNullOrEmpty(account)) return "Your controlled player profile is unavailable.";

            bool factionOwned = gate.ownershipType == PlayerStationOwnershipType.Faction;
            long unitValue = 0, payout = 0;
            if (factionOwned)
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
                contribution.paidCredits < 0 || (factionOwned && contribution.paidCredits > long.MaxValue - payout))
                return "The jumpgate contribution ledger has an invalid or overflowing total; administrator recovery is required.";
            int oldDelivered = material.delivered, oldAmount = contribution.amount;
            long oldUnitValue = contribution.unitValue;
            long oldPaid = contribution.paidCredits, oldPaidUtc = contribution.paidUtc;
            bool oldBuilding = gate.building;
            long oldStarted = gate.startedUtc, oldCompletes = gate.completesUtc, oldChanged = gate.destinationChangedUtc;
            material.delivered += amount;
            contribution.amount += amount;
            if (factionOwned)
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
                if (!gate.building) gate.destinationChangedUtc = now;
            }

            bool committed = factionOwned
                ? CommitConstructionContribution(account, gate.ownerFactionId, item, amount, payout)
                : CommitCargoTransaction(account, (item, -amount));
            if (!committed)
            {
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
            if (!(NetGame.PersistentHost && by.OwnerClientId == NetGame.LocalId))
                NetState.Instance?.SendAdmin(by.OwnerClientId, NetAdmin.Order.Take, item, amount, 0, "", "Jumpgate construction");
            if (factionOwned) NotifyProfileCreditChange(account, payout, "Jumpgate construction contribution");
            string message = "Contributed " + amount + " x " + NetGame.Db.Item(item).name + " to jumpgate orbit " + orbitStationIndex + ".";
            if (factionOwned) message += " Paid " + payout.ToString("N0") + " credits from the faction bank.";
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
            else return "Choose everyone, faction or blacklist.";
            var previous = gate.accessMode;
            gate.accessMode = next;
            if (!Save())
            {
                gate.accessMode = previous;
                return "The jumpgate access policy could not be saved.";
            }
            return "Jumpgate access is now " + (next == PlayerJumpgateAccessMode.Everyone ? "open to everyone" :
                next == PlayerJumpgateAccessMode.OwningFaction ? "restricted to the owning faction" :
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
                id = NetProfiles.FindAccount((target ?? "").Trim());
                if (string.IsNullOrEmpty(id)) return "Supply a valid player profile or account name.";
            }
            else if (targetType == "faction")
            {
                type = PlayerJumpgateBlockType.Faction;
                var faction = NetFactions.FindById((target ?? "").Trim());
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
            return JumpgateAccessAllowed(gate, by.ProfileAccount, by.FactionId);
        }

        /// <summary>Server validation for the owner-only interaction RPC. Returns -1 unless the player is at an accessible, operational gate.</summary>
        internal static int JumpgateDestinationForInteraction(NetPlayer by, int orbitStationIndex)
        {
            if (!NetGame.IsServer || !sessionActive || by == null || !by.IsSpawned ||
                !by.InSpace || by.Station != orbitStationIndex || !CanUseJumpgate(by, orbitStationIndex)) return -1;
            var gate = FindJumpgate(orbitStationIndex);
            CompleteJumpgateIfDue(gate);
            return gate != null && !gate.building && !gate.scrapped && !gate.permanentlyDestroyed
                ? gate.destinationSystem : -1;
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
                else if (gate != null) label += gate.building ? " [player gate under construction]" : " [player jumpgate]";
                else label += " [available]";
                lines.Add("Orbit " + orbit + ": " + label);
            }
            return string.Join("\n", lines);
        }

        /// <summary>
        /// Called by the gate's authoritative destruction hook. First destruction leaves reclaimable scrap;
        /// later destruction permanently disables reclamation and returns a 25% salvage manifest for world cargo spawning.
        /// </summary>
        internal static bool MarkJumpgateDestroyed(int orbitStationIndex, out List<PlayerStationMaterial> salvage)
        {
            salvage = new List<PlayerStationMaterial>();
            if (!NetGame.IsServer || !sessionActive || registry == null) return false;
            var gate = FindJumpgate(orbitStationIndex);
            if (gate == null || gate.scrapped || gate.permanentlyDestroyed) return false;
            int index = registry.jumpgates.IndexOf(gate);
            var previous = JsonUtility.FromJson<PlayerJumpgateRecord>(JsonUtility.ToJson(gate));
            if (gate.destructionCount <= 0)
            {
                gate.destructionCount = 1;
                gate.scrapped = true;
                gate.permanentlyDestroyed = false;
                gate.building = false;
                gate.startedUtc = gate.completesUtc = 0;
                gate.salvageMaterials = new List<PlayerStationMaterial>();
                gate.salvageDropPending = false;
            }
            else
            {
                gate.destructionCount = Math.Max(2, gate.destructionCount + 1);
                gate.scrapped = false;
                gate.permanentlyDestroyed = true;
                gate.building = false;
                gate.startedUtc = gate.completesUtc = 0;
                gate.destinationSystem = -1;
                gate.salvageMaterials = RequiredJumpgateMaterials().ConvertAll(x =>
                    new PlayerStationMaterial { item = x.item, required = Mathf.CeilToInt(x.required * 0.25f), delivered = 0 });
                gate.salvageDropPending = true;
                salvage = gate.salvageMaterials.ConvertAll(x =>
                    new PlayerStationMaterial { item = x.item, required = x.required, delivered = 0 });
            }
            if (!Save())
            {
                registry.jumpgates[index] = previous;
                salvage.Clear();
                return false;
            }
            return true;
        }

        internal static string ClaimScrappedJumpgate(NetPlayer by, int orbitStationIndex, string claimType, int destinationSystem = -1)
        {
            if (!NetGame.IsServer || !sessionActive || registry == null)
                return "Jumpgate reclamation needs a persistent host or dedicated server.";
            if (by == null || !by.IsSpawned || !by.InSpace || by.Station != orbitStationIndex ||
                !NetProfiles.Enabled || !NetProfiles.Controls(by.OwnerClientId))
                return "Fly to the scrapped gate's orbit to claim it using a controlled profile.";
            var gate = FindJumpgate(orbitStationIndex);
            if (gate == null || !gate.scrapped || gate.permanentlyDestroyed || gate.destructionCount != 1)
                return "That gate is not in its reclaimable scrapped state.";
            string account = NetProfiles.AccountOf(by.OwnerClientId);
            if (string.IsNullOrEmpty(account)) return "Your controlled player profile is unavailable.";
            var faction = NetFactions.Of(account);
            bool ownsStation = registry.stations?.Exists(x => x != null && !x.underConstruction &&
                x.ownershipType == PlayerStationOwnershipType.Individual &&
                string.Equals(x.ownerProfile, account, StringComparison.Ordinal)) == true;
            claimType = (claimType ?? "").Trim().ToLowerInvariant();
            bool factionClaim = claimType == "faction";
            if (claimType != "faction" && claimType != "individual")
                return "Choose faction or individual.";
            if (factionClaim && faction == null)
                return "Only a faction member may reclaim a gate on behalf of their faction.";
            if (!factionClaim && !ownsStation)
                return "An individual reclaim requires you to own an operational player station. Faction members may claim on behalf of their faction.";
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
            int index = registry.jumpgates.IndexOf(gate);
            var previous = JsonUtility.FromJson<PlayerJumpgateRecord>(JsonUtility.ToJson(gate));
            gate.ownershipType = factionClaim ? PlayerStationOwnershipType.Faction : PlayerStationOwnershipType.Individual;
            gate.ownerProfile = factionClaim ? null : account;
            gate.ownerFactionId = factionClaim ? faction.id : null;
            gate.ownerName = factionClaim ? faction.name : by.DisplayName;
            gate.accessFactionId = faction?.id;
            gate.destinationSystem = newDestination;
            gate.constructionStationId = -1;
            gate.sponsorStationId = -1;
            gate.constructionIssuerProfile = account;
            gate.scrapped = false;
            gate.permanentlyDestroyed = false;
            gate.building = true;
            gate.startedUtc = gate.completesUtc = 0;
            gate.materials = RequiredJumpgateMaterials();
            gate.contributions = new List<PlayerStationContribution>();
            gate.salvageMaterials = new List<PlayerStationMaterial>();
            gate.salvageDropPending = false;
            if (!Save())
            {
                registry.jumpgates[index] = previous;
                return "The reclaimed gate could not be saved; its scrapped state is unchanged.";
            }
            return "Jumpgate claimed " + (factionClaim ? "on behalf of " + faction.name : "by " + by.DisplayName) +
                ", connecting to " + destination.name + ". Deliver a fresh set of construction materials in this orbit to rebuild it.";
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
                        ? "Claim with /station jumpgate-claim " + orbitStationIndex + " faction|individual. Faction members may claim for their faction; individual claims require station ownership."
                        : "A new construction order from an operational player station is required."
                };
                if (gate.salvageDropPending) recovery.Add("25% construction-material salvage is awaiting its physical cargo drop.");
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
                "Access: " + (gate.accessMode == PlayerJumpgateAccessMode.Everyone ? "Everyone" :
                    gate.accessMode == PlayerJumpgateAccessMode.OwningFaction ? "Owning faction only" : "Blacklist mode"),
                "Blacklisted profiles/factions: " + (gate.blacklist?.Count ?? 0),
                "Owner: " + (gate.ownershipType == PlayerStationOwnershipType.Faction
                    ? (NetFactions.FindById(gate.ownerFactionId)?.tag ?? gate.ownerName ?? "Owning faction")
                    : (gate.ownerName ?? gate.ownerProfile ?? "Unknown")) };
            if (gate.materials != null)
                foreach (var m in gate.materials)
                    if (m != null) lines.Add((NetGame.Db?.Item(m.item)?.name ?? ("Item " + m.item)) +
                        ": " + m.delivered + "/" + m.required);
            if (gate.startedUtc <= 0) lines.Add("Status: awaiting materials");
            else if (gate.building)
            {
                long remaining = Math.Max(0, gate.completesUtc - DateTimeOffset.UtcNow.ToUnixTimeSeconds());
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
