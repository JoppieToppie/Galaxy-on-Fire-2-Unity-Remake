using System;
using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.World;
using UnityEngine;

namespace GoF2Remake.Multiplayer
{
    public static partial class NetPlayerStations
    {
        const float StationMaxIntegrity = 300000f;
        const float WreckMaxIntegrity = 120000f;
        const float LegacyStationMaxIntegrity = 30000f;
        const float LegacyWreckMaxIntegrity = 12000f;
        const int ReconstructionPercent = 50;

        internal static PlayerStationRecord StationAt(int orbit)
        {
            EnsureStationIndex();
            if (!stationsByOrbit.TryGetValue(orbit, out var station)) return null;
            if (station.existingOrbit && NetGame.Db != null &&
                (!OrbitLayout.VacantStationSite(orbit, Session.WorldIndex) ||
                 NetGame.Db.Stations.Find(anchor => anchor != null && anchor.index == orbit)?.system != station.system)) return null;
            return station;
        }

        public static Vector3 StationPosition(int orbit) =>
            new Vector3(StationAt(orbit)?.stationOffsetX ?? 0f, 0f, 0f);

        public static bool StationClosedByCombat(int orbit)
        {
            var station = StationAt(orbit);
            return station != null && (station.wreckClaimOpen || station.reconstructing || station.destroyed);
        }

        public static bool StationSceneryPresent(int orbit)
        {
            var station = StationAt(orbit);
            return station != null && !station.destroyed && (!station.underConstruction || station.reconstructing);
        }

        internal static bool CanDamagePlayerStation(int orbit) => StationSceneryPresent(orbit) && !StationOfferedForClaim(orbit);

        internal static bool ApplyStationCombatHit(int orbit, int weaponItem, float damage)
        {
            if (!NetGame.IsServer || !sessionActive || !CanDamagePlayerStation(orbit) ||
                !NetGuard.Item(weaponItem) || StructuralWeaponEligible?.Invoke(weaponItem) != true ||
                !NetGuard.Finite(damage) || damage <= 0f) return false;

            var station = StationAt(orbit);
            bool vulnerable = station.wreckClaimOpen || station.reconstructing;
            float capacity = vulnerable ? WreckMaxIntegrity : StationMaxIntegrity;
            float savedCapacity = station.stationIntegrityCapacity > 0f ? station.stationIntegrityCapacity :
                vulnerable ? LegacyWreckMaxIntegrity : LegacyStationMaxIntegrity;
            // Preserve the remaining fraction when a saved station's capacity changes.
            float integrity = station.stationIntegrityInitialized
                ? Mathf.Clamp01(station.stationIntegrity / savedCapacity) * capacity
                : capacity;
            if (damage >= integrity) return DestroyStation(station);

            bool initialized = station.stationIntegrityInitialized;
            float previous = station.stationIntegrity;
            float previousCapacity = station.stationIntegrityCapacity;
            station.stationIntegrityInitialized = true;
            station.stationIntegrity = integrity - damage;
            station.stationIntegrityCapacity = capacity;
            if (Save()) return true;
            station.stationIntegrityInitialized = initialized;
            station.stationIntegrity = previous;
            station.stationIntegrityCapacity = previousCapacity;
            return false;
        }

        // A new record drops live assets and governance while retaining the stable orbit and project identity.
        static PlayerStationRecord SiteOf(PlayerStationRecord station) => new PlayerStationRecord
        {
            id = station.id,
            stationIndex = station.stationIndex,
            existingOrbit = station.existingOrbit,
            system = station.system,
            looksLike = station.looksLike,
            stationModelReference = station.stationModelReference,
            stationOffsetX = station.stationOffsetX,
            name = station.name,
            createdUtc = station.createdUtc,
            nextShipListingId = station.nextShipListingId,
            nextItemListingId = station.nextItemListingId,
            donatedConstructionStock = station.donatedConstructionStock
        };

        static bool DestroyStation(PlayerStationRecord station)
        {
            var site = SiteOf(station);
            bool wasOperational = !station.wreckClaimOpen && !station.reconstructing;
            site.destroyed = !wasOperational;
            site.stationExplosionPending = true;
            if (wasOperational)
            {
                site.materials = station.materials;
                site.wreckBlueprintSalvage = BlueprintSalvage(station);
                OpenWreckClaim(site);
            }
            else if (station.wreckClaimOpen)
            {
                site.stationSalvage = RollStationSalvage(station.materials);
                foreach (var part in station.wreckBlueprintSalvage ?? new List<PlayerStationMaterial>())
                {
                    if (part == null || part.required <= 0 || !NetGuard.Item(part.item)) continue;
                    var existing = site.stationSalvage.Find(material => material.item == part.item);
                    if (existing == null) site.stationSalvage.Add(part);
                    else existing.required = (int)Math.Min(int.MaxValue, (long)existing.required + part.required);
                }
                site.stationSalvagePending = site.stationSalvage.Count > 0;
            }
            // An interrupted reconstruction leaves no claim, salvage, or compensation.

            int index = registry.stations.IndexOf(station);
            var originalRegistry = registry;
            ReplaceStationRecord(originalRegistry, index, site);
            long payout = wasOperational ? StationCompensation(station) : 0;
            bool factionOwned = station.ownershipType == PlayerStationOwnershipType.Faction;
            string recipient = factionOwned ? station.ownerFactionId : station.ownerProfile;
            bool paysOwner = payout > 0 && !string.IsNullOrEmpty(recipient);
            var credits = new List<(string account, long delta)>();
            var banks = new List<(string factionId, long delta)>();
            var refunds = new List<(string account, long amount)>();
            if (paysOwner)
            {
                if (factionOwned) banks.Add((recipient, payout));
                else credits.Add((recipient, payout));
            }
            bool validOrders = StageConstructionEscrowRefunds(station.buyOrders, credits, banks, refunds);
            bool saved = validOrders && (credits.Count == 0 && banks.Count == 0 ? Save() : CommitProfileTransaction(null, credits, banks));
            if (!saved)
            {
                if (ReferenceEquals(registry, originalRegistry)) ReplaceStationRecord(originalRegistry, index, station);
                return false;
            }

            if (!System.IO.File.Exists(TransactionPath))
            {
                if (paysOwner && !factionOwned)
                    NotifyProfileCreditChange(recipient, payout, "Station destruction compensation");
                foreach (var refund in refunds)
                    NotifyProfileCreditChange(refund.account, refund.amount, "Construction buy order escrow refund");
            }
            wreckControl.Remove(station.id);
            Apply(NetGame.Db);
            EjectDeniedVisitors(site);
            return true;
        }

        static long StationCompensation(PlayerStationRecord station)
        {
            var db = NetGame.Db;
            long value = 0;
            void AddValue(int amount, int unitPrice)
            {
                long line = (long)Math.Max(0, amount) * Math.Max(0, unitPrice);
                value += Math.Min(long.MaxValue - value, line);
            }

            foreach (var material in station.materials)
                if (material != null) AddValue(material.delivered, db.Item(material.item)?.minPrice ?? 0);
            foreach (var stack in station.storage)
                if (stack != null) AddValue(stack.amount, db.Item(stack.item)?.minPrice ?? 0);
            foreach (var ship in station.storedShips)
                if (ship != null) AddValue(1, db.Ship(ship.ship)?.price ?? 0);
            foreach (var ship in station.dryDockShips)
                if (ship != null) AddValue(1, db.Ship(ship.ship)?.price ?? 0);
            return value / 2;
        }

        static List<PlayerStationMaterial> BlueprintSalvage(PlayerStationRecord station)
        {
            var totals = new Dictionary<int, long>();
            var recipesByShip = new Dictionary<int, ItemData>();
            foreach (var product in Blueprints.Products(NetGame.Db))
            {
                int ship = Modding.ModBlueprints.ShipOf(product.index);
                if (ship >= 0 && product.blueprint?.Count > 0)
                    recipesByShip.TryAdd(ship, product);
            }
            void AddHull(StoredShip hull)
            {
                if (hull == null || !recipesByShip.TryGetValue(hull.ship, out var product)) return;
                foreach (var ingredient in product.blueprint)
                    if (ingredient != null && ingredient.amount > 0 && NetGuard.Item(ingredient.item))
                    {
                        totals.TryGetValue(ingredient.item, out long previous);
                        totals[ingredient.item] = previous + ingredient.amount;
                    }
            }

            foreach (var hull in station.storedShips) AddHull(hull);
            foreach (var hull in station.dryDockShips) AddHull(hull);

            var manifest = new List<PlayerStationMaterial>();
            long remainder = 0;
            var items = new List<int>(totals.Keys);
            items.Sort();
            foreach (int item in items)
            {
                long numerator = totals[item] * 3 + remainder;
                int amount = (int)Math.Min(int.MaxValue, numerator / 10);
                remainder = numerator % 10;
                if (amount > 0) manifest.Add(new PlayerStationMaterial { item = item, required = amount });
            }
            return manifest;
        }

        static List<PlayerStationMaterial> RollStationSalvage(List<PlayerStationMaterial> materials)
        {
            // Draw a quarter of delivered construction units without replacement.
            var available = new List<PlayerStationMaterial>();
            int units = 0;
            foreach (var material in materials)
            {
                if (material == null || !NetGuard.Item(material.item)) continue;
                int count = Math.Clamp(material.delivered, 0, Math.Max(0, material.required));
                if (count == 0) continue;
                available.Add(new PlayerStationMaterial { item = material.item, delivered = count });
                units += count;
            }

            int salvageUnits = units / 4;
            for (int i = 0; i < salvageUnits; i++)
            {
                int pick = UnityEngine.Random.Range(0, units--);
                foreach (var material in available)
                {
                    if (pick < material.delivered)
                    {
                        material.delivered--;
                        material.required++;
                        break;
                    }
                    pick -= material.delivered;
                }
            }
            available.RemoveAll(material => material.required == 0);
            foreach (var material in available) material.delivered = 0;
            return available;
        }

        internal static string RebuildStation(NetPlayer pilot, int id, int appearance = -1)
        {
            if (!NetGame.IsServer || !sessionActive || registry?.stations == null)
                return "Reconstruction requires a persistent server.";
            var wreck = Find(id);
            string error = RebuildAuthorization(pilot, wreck, out string account, out var side);
            if (error != null) return error;
            bool factionOwned = side.FactionId != null;

            if (appearance >= 0 && !CanUseRebuildAppearance(wreck, appearance))
                return "Choose a station model from the wreck model's race. Use /station appearance-presets to list model indices.";
            var project = SiteOf(wreck);
            if (appearance >= 0)
            {
                project.looksLike = appearance;
                project.stationModelReference = StationModelReference(appearance);
            }
            project.stationOffsetX = wreck.stationOffsetX > 0f ? -1500f : 1500f;
            project.materials = ConstructionCost("reconstruction");
            project.reconstructing = project.underConstruction = true;
            project.createdUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            project.ownershipType = factionOwned ? PlayerStationOwnershipType.Faction : PlayerStationOwnershipType.Individual;
            project.ownerProfile = side.Profile;
            project.ownerFactionId = side.FactionId;
            project.ownerName = side.Name;
            project.originalBuilderProfile = account;
            project.governanceVersion = 1;
            project.factionPermissions = NewIndividualFactionPermissionProfile(false);
            RecordHistory(project, account, "station-reconstruction-started", null, "The exclusive claimant began rebuilding the wreck.");

            int index = registry.stations.IndexOf(wreck);
            var originalRegistry = registry;
            ReplaceStationRecord(originalRegistry, index, project);
            if (!Save())
            {
                if (ReferenceEquals(registry, originalRegistry)) ReplaceStationRecord(originalRegistry, index, wreck);
                return "The reconstruction project could not be saved.";
            }
            wreckControl.Remove(id);
            Apply(NetGame.Db);
            return $"Reconstruction started for {wreck.name}. Deposit materials with /station contribute {id} <item id> <amount>.";
        }

        static string RebuildAuthorization(NetPlayer pilot, PlayerStationRecord wreck, out string account, out WreckClaimant side)
        {
            account = null;
            side = default;
            if (wreck == null || !wreck.wreckClaimOpen || wreck.wreckClaim == null)
                return "That project is not a reclaimable station wreck.";
            if (!wreck.wreckClaim.exclusive) return "Wait for the wreck claim to become exclusive.";
            if (wreck.stationExplosionPending) return "Wait for the destruction event to finish.";
            if (pilot == null || !pilot.IsSpawned || !pilot.InSpace || pilot.Station != wreck.stationIndex ||
                pilot.Hull <= 0 || !pilot.HasCurrentWreckClaimPose ||
                (pilot.Position - StationPosition(wreck.stationIndex)).sqrMagnitude > WreckControlRadiusMetres * WreckControlRadiusMetres ||
                !NetProfiles.Controls(pilot.OwnerClientId))
                return "Fly within 5 km of the wreck with a controlled profile to start reconstruction.";

            account = NetProfiles.AccountOf(pilot.OwnerClientId);
            if (string.IsNullOrEmpty(account)) return "Your controlled profile is unavailable.";
            var faction = NetFactions.Of(account);
            side = new WreckClaimant(account, faction?.id, faction?.name ?? pilot.DisplayName);
            if (!wreck.wreckClaim.BelongsTo(side)) return "Only the exclusive claimant can rebuild this wreck.";
            bool factionOwned = side.FactionId != null;
            if (factionOwned && !string.Equals(faction.leader, account, StringComparison.Ordinal) &&
                faction.officers?.Contains(account) != true)
                return "An officer or leader of the claiming faction must start reconstruction.";
            return null;
        }

        internal static bool CanUseRebuildAppearance(PlayerStationRecord wreck, int appearance)
        {
            var db = NetGame.Db;
            var target = db?.Stations?.Find(value => value != null && value.index == appearance);
            if (target == null || !IsStationModelPreset(appearance)) return false;
            int originalIndex = StationModelIndex(wreck);
            var original = db.Stations.Find(value => value != null && value.index == originalIndex);
            var originalSystem = db.Systems.Find(value => value != null && value.index == (original?.system ?? wreck.system));
            var targetSystem = db.Systems.Find(value => value != null && value.index == target.system);
            return originalSystem != null && targetSystem != null && originalSystem.raceId == targetSystem.raceId;
        }

        public static IReadOnlyList<PlayerStationMaterial> StationSalvageAt(int orbit)
        {
            var site = StationAt(orbit);
            return site?.stationSalvagePending == true ? site.stationSalvage : null;
        }

        public static IReadOnlyList<PlayerStationMaterial> JumpgateSalvageAt(int orbit)
        {
            var gate = FindJumpgate(orbit);
            return gate?.salvageDropPending == true ? gate.salvageMaterials : null;
        }

        public static Vector3 JumpgateSalvagePosition(int orbit)
        {
            var db = NetGame.Db;
            if (db == null) return Vector3.zero;
            var layout = OrbitLayout.Build(db, orbit, includePlayerGateLandmark: true);
            return OrbitLayout.ToUnity(layout.Place(layout.jumpgate)) + Vector3.forward * 30f;
        }

        internal static bool CollectWreckSalvage(NetPlayer pilot, int orbit, int room, bool jumpgate)
        {
            if (!NetGame.IsServer || !sessionActive || pilot == null || !pilot.IsSpawned ||
                !pilot.InSpace || pilot.Station != orbit || pilot.Hull <= 0 || !pilot.HasCurrentWreckClaimPose ||
                !NetProfiles.Controls(pilot.OwnerClientId) || room <= 0)
                return false;

            float reach = NetProfiles.WreckSalvageTractorReach(pilot);
            if (reach <= 0f) return false;
            float reachSquared = reach * reach;
            if (jumpgate)
            {
                var gate = FindJumpgate(orbit);
                return NetGame.Db != null && gate?.salvageDropPending == true && gate.salvageMaterials != null &&
                    (pilot.Position - JumpgateSalvagePosition(orbit)).sqrMagnitude <= reachSquared &&
                    TransferWreckSalvage(pilot, room, ref gate.salvageMaterials, ref gate.salvageDropPending);
            }

            var site = StationAt(orbit);
            return site?.stationSalvagePending == true && site.stationSalvage != null &&
                (pilot.Position - StationSalvagePosition(orbit)).sqrMagnitude <= reachSquared &&
                TransferWreckSalvage(pilot, room, ref site.stationSalvage, ref site.stationSalvagePending);
        }

        static bool TransferWreckSalvage(NetPlayer pilot, int room, ref List<PlayerStationMaterial> manifest, ref bool pending)
        {
            var account = NetProfiles.AccountOf(pilot.OwnerClientId);
            if (string.IsNullOrEmpty(account)) return false;
            var cargo = new List<(string account, int item, int delta)>();
            var remaining = new List<PlayerStationMaterial>();
            room = Math.Min(Math.Min(room, 500), NetProfiles.WreckSalvageCargoRoom(pilot, account));
            if (room <= 0) return false;
            foreach (var material in manifest)
            {
                if (material == null || material.required <= 0 || !NetGuard.Item(material.item)) continue;
                int current = NetProfiles.CargoAmount(account, material.item);
                int take = current >= 0 ? Math.Min(Math.Min(material.required, room), int.MaxValue - current) : 0;
                if (take > 0) cargo.Add((account, material.item, take));
                room -= take;
                if (take < material.required)
                    remaining.Add(new PlayerStationMaterial { item = material.item, required = material.required - take });
            }
            if (cargo.Count == 0) return false;
            var previous = manifest;
            manifest = remaining;
            pending = remaining.Count > 0;
            if (!CommitProfileTransaction(cargo, null, null))
            {
                manifest = previous;
                pending = true;
                return false;
            }
            if (!(NetGame.PersistentHost && pilot.OwnerClientId == NetGame.LocalId))
                foreach (var transfer in cargo)
                    NetState.Instance?.SendAdmin(pilot.OwnerClientId, NetAdmin.Order.Give, transfer.item, transfer.delta,
                        0, "", "Wreck salvage");
            return true;
        }

        public static Vector3 StationSalvagePosition(int orbit) => StationPosition(orbit) + Vector3.forward * 30f;

        internal static string RetireStation(NetPlayer pilot, int id)
        {
            if (!NetGame.IsServer || !sessionActive || registry?.stations == null || !NetCommands.IsAdmin(pilot))
                return "Retiring a destroyed player station requires a server administrator.";
            var record = Find(id);
            if (record == null || !record.destroyed || record.retired)
                return "Choose a destroyed player station that has not already been retired.";
            if (record.stationExplosionPending || record.stationSalvagePending)
                return "Wait for the station explosion and collect the remaining salvage before retiring the orbit.";
            var gate = FindJumpgate(record.stationIndex);
            if (gate != null && (!gate.permanentlyDestroyed || gate.explosionPending || gate.salvageDropPending))
                return "This orbit still supports a jumpgate, a gate wreck, or outstanding gate salvage.";
            if (registry.orbitalStructures?.Exists(structure => structure != null && structure.orbit == record.stationIndex) == true)
                return "Dismantle or destroy the remaining orbital structures before retiring this orbit.";
            record.retired = true;
            indexedStationCount = -1;
            if (!Save())
            {
                record.retired = false;
                indexedStationCount = -1;
                return "Retirement could not be saved.";
            }
            Apply(NetGame.Db);
            return $"Retired {record.name}. The station slot is available and existing orbit indices are preserved.";
        }

        static void TickStationDestruction()
        {
            foreach (var station in registry.stations)
            {
                if (station?.stationExplosionPending != true) continue;
                var runner = NetPlayer.OrbitRunner(station.stationIndex);
                if (runner == null) continue;
                station.stationExplosionPending = false;
                if (!Save())
                {
                    station.stationExplosionPending = true;
                    return;
                }
                runner.BroadcastPlayerStationExplosion(station.stationIndex);
            }
        }
    }
}
