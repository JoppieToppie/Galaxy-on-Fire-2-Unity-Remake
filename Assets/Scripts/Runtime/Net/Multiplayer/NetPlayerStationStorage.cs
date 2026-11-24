using System;
using System.Collections.Generic;
using GoF2Remake.Data;

namespace GoF2Remake.Multiplayer
{
    public static partial class NetPlayerStations
    {
        const int MaxStorageItemTypes = 64;
        const int StorageCapacityPerLevel = 10000;

        static bool CanManage(NetPlayer by, PlayerStationRecord record, out string account)
        {
            account = by == null ? null : NetProfiles.AccountOf(by.OwnerClientId);
            return IsStationContext(by, record, out account) &&
                HasStationPermission(record, account, PlayerStationPermission.StationOperations);
        }

        internal static string Modules()
        {
            var lines = new List<string> { "Station module costs:" };
            foreach (var kind in ModuleKinds)
            {
                var items = new List<string>();
                foreach (var material in ConstructionCost(kind))
                    items.Add($"{material.required:N0} x {ConstructionItemName(material.item)}");
                lines.Add(ModuleName(kind) + " — " + string.Join(", ", items));
            }
            lines.Add("Factory and refinery require storage. Install from cargo or open a module project to procure its materials.");
            return string.Join("\n", lines);
        }


        const int MaxStoredShipsPerStation = 100;

        internal static string InstallHangar(NetPlayer by, int id) => InstallModuleFromCargo(by, id, "hangar");

        static bool CanManageHangar(NetPlayer by, PlayerStationRecord record, out string account)
        {
            return IsStationContext(by, record, out account) &&
                HasStationPermission(record, account, PlayerStationPermission.Hangar);
        }

        internal static string PersonalFleet(NetPlayer by)
        {
            if (by == null || !by.IsSpawned || !by.InHangar || !NetProfiles.Controls(by.OwnerClientId))
                return "Dock at a station and use a controlled player profile to list your stored fleet.";
            string account = NetProfiles.AccountOf(by.OwnerClientId);
            var ships = NetProfiles.StoredShipsOf(account, by.OwnerClientId);
            var lines = new List<string> { "Your personal stored fleet (use these indexes with /station store-ship):" };
            if (ships != null)
                for (int i = 0; i < ships.Count; i++)
                {
                    var ship = ships[i]; if (ship == null) continue;
                    lines.Add((i + 1) + ". " + (NetGame.Db?.Ship(ship.ship)?.name ?? ("Ship " + ship.ship)) +
                        " — " + (ship.mods?.Count ?? 0) + " hull upgrades, " + (ship.equipment?.Count ?? 0) + " fitted equipment entries");
                }
            if (lines.Count == 1) lines.Add("Your personal stored fleet is empty.");
            return string.Join("\n", lines);
        }

        internal static string ShipStorageStatus(NetPlayer by, int id)
        {
            var record = Find(id);
            if (record == null) return "No player station with that id exists.";
            if (!CanManageHangar(by, record, out _)) return "Ship hangar access is private to the owner and authorised faction members.";
            if (!(record.addons?.Exists(a => a != null && a.kind == "hangar" && a.level > 0) ?? false))
                return "This station has no ship hangar module.";
            record.storedShips ??= new List<StoredShip>();
            record.storedShipOwners ??= new List<string>();
            var lines = new List<string> { "Ship hangar at " + record.name + ":" };
            for (int i = 0; i < record.storedShips.Count; i++)
            {
                var ship = record.storedShips[i]; if (ship == null) continue;
                lines.Add((i + 1) + ". " + (NetGame.Db?.Ship(ship.ship)?.name ?? ("Ship " + ship.ship)) +
                    " — " + (ship.mods?.Count ?? 0) + " hull upgrades, bare hull");
            }
            if (lines.Count == 1) lines.Add("No ships are stored here.");
            lines.Add("Capacity: " + record.storedShips.Count + "/" + MaxStoredShipsPerStation);
            lines.Add("Factions share access, never ownership.");
            return string.Join("\n", lines);
        }

        // Moving a stored hull into a station transfers its removable equipment into station storage.
        static bool CollectShipInventory(StoredShip ship, Dictionary<int, long> items)
        {
            if (ship == null || items == null) return false;
            foreach (var stack in ship.equipment ?? new List<ItemStack>())
            {
                if (stack == null) continue;
                int units = Hangar.UnitsOf(NetGame.Db, stack);
                if (units == 0) continue;
                if (NetGame.Db?.Item(stack.item) == null) return false;
                items[stack.item] = (items.TryGetValue(stack.item, out long old) ? old : 0) + units;
            }
            return true;
        }

        static bool CanFitStationItems(PlayerStationRecord record, Dictionary<int, long> items)
        {
            if (StorageCapacity(record) <= 0) return false;
            long stored = 0;
            var types = new HashSet<int>();
            foreach (var entry in record.storage ?? new List<PlayerStationStorage>())
            {
                if (entry == null || entry.amount <= 0) continue;
                stored += entry.amount;
                types.Add(entry.item);
            }
            foreach (var pair in items)
            {
                if (pair.Value <= 0 || pair.Value > int.MaxValue) return false;
                if (!types.Contains(pair.Key)) types.Add(pair.Key);
                if (pair.Value > int.MaxValue - (record.storage?.Find(x => x != null && x.item == pair.Key)?.amount ?? 0))
                    return false;
            }
            long incoming = 0;
            foreach (var amount in items.Values) incoming += amount;
            return stored + incoming <= StorageCapacity(record) && types.Count <= MaxStorageItemTypes;
        }

        static void EnsureStorageLots(PlayerStationRecord record)
        {
            if (record == null) return;
            record.storage ??= new List<PlayerStationStorage>();
            record.storageLots ??= new List<PlayerStationStorageLot>();
            NormalizeStorageLots(record);
        }

        static int OwnedStorageAmount(PlayerStationRecord record, string account, int item)
        {
            EnsureStorageLots(record);
            long total = 0;
            foreach (var lot in record.storageLots)
                if (lot != null && lot.item == item && string.Equals(lot.ownerProfile, account, StringComparison.Ordinal))
                    total += Math.Max(0, lot.amount);
            return (int)Math.Min(int.MaxValue, total);
        }

        static void AddStorageLot(PlayerStationRecord record, string account, int item, int amount)
        {
            if (amount <= 0) return;
            // Callers normalize legacy lots before changing aggregate stock. Normalizing
            // here would credit the newly added stock to the station owner a second time.
            record.storageLots ??= new List<PlayerStationStorageLot>();
            var lot = record.storageLots.Find(x => x != null && x.item == item &&
                string.Equals(x.ownerProfile, account, StringComparison.Ordinal));
            if (lot == null) record.storageLots.Add(lot = new PlayerStationStorageLot { ownerProfile = account, item = item });
            lot.amount = checked(lot.amount + amount);
        }

        static bool RemoveStorageLots(PlayerStationRecord record, string account, int item, int amount)
        {
            if (amount <= 0 || OwnedStorageAmount(record, account, item) < amount) return false;
            int remaining = amount;
            for (int i = record.storageLots.Count - 1; i >= 0 && remaining > 0; i--)
            {
                var lot = record.storageLots[i];
                if (lot == null || lot.item != item || !string.Equals(lot.ownerProfile, account, StringComparison.Ordinal)) continue;
                int take = Math.Min(remaining, lot.amount);
                lot.amount -= take;
                remaining -= take;
                if (lot.amount <= 0) record.storageLots.RemoveAt(i);
            }
            return remaining == 0;
        }

        static List<PlayerStationStorageLot> CloneStorageLots(PlayerStationRecord record)
        {
            EnsureStorageLots(record);
            var copy = new List<PlayerStationStorageLot>();
            foreach (var lot in record.storageLots)
                if (lot != null) copy.Add(new PlayerStationStorageLot { ownerProfile = lot.ownerProfile, item = lot.item, amount = lot.amount });
            return copy;
        }

        static void AddStationItems(PlayerStationRecord record, Dictionary<int, long> items, string ownerProfile)
        {
            record.storage ??= new List<PlayerStationStorage>();
            foreach (var pair in items)
            {
                if (pair.Value <= 0) continue;
                var entry = record.storage.Find(x => x != null && x.item == pair.Key);
                if (entry == null) record.storage.Add(entry = new PlayerStationStorage { item = pair.Key });
                entry.amount += (int)pair.Value;
                AddStorageLot(record, ownerProfile, pair.Key, (int)pair.Value);
            }
        }

        static StoredShip BareHull(StoredShip ship) =>
            new StoredShip(ship.ship, ship.race, ship.mods != null ? new List<int>(ship.mods) : new List<int>());

        internal static string StoreShip(NetPlayer by, int id, int fleetIndex)
        {
            var record = Find(id);
            if (record == null) return "No player station with that id exists.";
            if (!CanManageHangar(by, record, out string account)) return "Dock at this station with an authorised profile to use its ship hangar.";
            if (record.underConstruction) return "Finish station construction before using the ship hangar.";
            if (!(record.addons?.Exists(a => a != null && a.kind == "hangar" && a.level > 0) ?? false)) return "Install a ship hangar module first.";
            if (StorageCapacity(record) <= 0) return "Install a station storage module before storing ships; their equipment must be transferred into station storage.";
            record.storedShips ??= new List<StoredShip>();
            if (record.storedShips.Count >= MaxStoredShipsPerStation) return "This station's ship hangar is full.";
            var owned = NetProfiles.StoredShipsOf(account, by.OwnerClientId);
            if (owned == null || fleetIndex < 1 || fleetIndex > owned.Count) return "That index is not in your personal stored fleet. Use /station fleet.";
            var candidate = owned[fleetIndex - 1];
            if (candidate == null) return "That ship record is invalid.";
            if (IsDryDockHull(candidate)) return "Frigates, freighters, cruisers and carriers must use a station dry dock.";
            var items = new Dictionary<int, long>();
            if (!CollectShipInventory(candidate, items)) return "The ship contains an invalid equipment item; nothing was transferred.";
            if (!CanFitStationItems(record, items)) return "Station storage lacks the capacity or item-type space for this ship's equipment; nothing was transferred.";

            var targetShips = new List<StoredShip>(owned);
            StoredShip ship = candidate;
            targetShips.RemoveAt(fleetIndex - 1);
            var previousStorage = new List<PlayerStationStorage>();
            foreach (var entry in record.storage ?? new List<PlayerStationStorage>())
                if (entry != null) previousStorage.Add(new PlayerStationStorage { item = entry.item, amount = entry.amount });
            var previousLots = CloneStorageLots(record);
            AddStationItems(record, items, account);
            record.storedShips.Add(BareHull(ship));
            record.storedShipOwners.Add(account);
            if (!CommitStoredShipTransaction(account, targetShips))
            {
                record.storedShips.RemoveAt(record.storedShips.Count - 1);
                record.storedShipOwners.RemoveAt(record.storedShipOwners.Count - 1);
                record.storage = previousStorage;
                record.storageLots = previousLots;
                return "The ship transfer could not be journalled; the ship and its inventory remain in your personal fleet.";
            }
            if (!NetGame.PersistentHost || by.OwnerClientId != NetGame.LocalId) NetProfiles.RefreshProfile(by.OwnerClientId);
            return "Stored " + (NetGame.Db?.Ship(ship.ship)?.name ?? ("ship " + ship.ship)) + " at " + record.name +
                ". Its removable equipment are now in station storage; hull upgrades remain with the ship.";
        }

        internal static string RetrieveShip(NetPlayer by, int id, int hangarIndex)
        {
            var record = Find(id);
            if (record == null) return "No player station with that id exists.";
            if (!CanManageHangar(by, record, out string account)) return "Dock at this station with an authorised profile to use its ship hangar.";
            if (record.underConstruction) return "Finish station construction before using the ship hangar.";
            if (!(record.addons?.Exists(a => a != null && a.kind == "hangar" && a.level > 0) ?? false)) return "This station has no ship hangar module.";
            record.storedShips ??= new List<StoredShip>();
            record.storedShipOwners ??= new List<string>();
            if (hangarIndex < 1 || hangarIndex > record.storedShips.Count) return "That ship index is not in this station's hangar.";
            var ship = record.storedShips[hangarIndex - 1];
            if (ship == null) return "That stored ship record is invalid.";
            if (hangarIndex > record.storedShipOwners.Count ||
                !string.Equals(record.storedShipOwners[hangarIndex - 1], account, StringComparison.Ordinal))
                return "That hull belongs to another pilot and cannot be retrieved by this profile.";
            // Older registry records may still contain fitted equipment. Migrate it to station
            // storage before returning a bare hull, rather than silently putting the old behaviour back.
            var items = new Dictionary<int, long>();
            if (!CollectShipInventory(ship, items)) return "The stored ship contains an invalid equipment item; nothing was withdrawn.";
            if (items.Count > 0 && !CanFitStationItems(record, items))
                return "Station storage cannot accommodate this legacy ship inventory; nothing was withdrawn.";
            var bare = BareHull(ship);
            var personalShips = NetProfiles.StoredShipsOf(account, by.OwnerClientId);
            if (personalShips == null || personalShips.Count >= 100) return "Your personal stored fleet is full or unavailable.";
            var targetShips = new List<StoredShip>(personalShips) { bare };
            var previousStorage = new List<PlayerStationStorage>();
            foreach (var entry in record.storage ?? new List<PlayerStationStorage>())
                if (entry != null) previousStorage.Add(new PlayerStationStorage { item = entry.item, amount = entry.amount });
            var previousLots = CloneStorageLots(record);
            AddStationItems(record, items, account);
            record.storedShips.RemoveAt(hangarIndex - 1);
            record.storedShipOwners.RemoveAt(hangarIndex - 1);
            if (!CommitStoredShipTransaction(account, targetShips))
            {
                record.storedShips.Insert(hangarIndex - 1, ship);
                record.storedShipOwners.Insert(hangarIndex - 1, account);
                record.storage = previousStorage;
                record.storageLots = previousLots;
                return "The ship retrieval could not be journalled; the ship remains in station storage.";
            }
            if (!NetGame.PersistentHost || by.OwnerClientId != NetGame.LocalId) NetProfiles.RefreshProfile(by.OwnerClientId);
            return "Returned a bare " + (NetGame.Db?.Ship(ship.ship)?.name ?? ("ship " + ship.ship)) +
                " to your personal stored fleet. Its equipment remain in station storage.";
        }

        const int MaxDryDockShipsPerStation = 20;

        static bool IsDryDockHull(StoredShip ship)
        {
            var data = ship == null ? null : NetGame.Db?.Ship(ship.ship);
            string kind = Modding.ShipClassRules.ClassOf(data);
            return string.Equals(kind, Modding.ShipClassRules.Frigate, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(kind, Modding.ShipClassRules.Freighter, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(kind, Modding.ShipClassRules.Cruiser, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(kind, Modding.ShipClassRules.Carrier, StringComparison.OrdinalIgnoreCase);
        }

        internal static string InstallDryDock(NetPlayer by, int id) => InstallModuleFromCargo(by, id, "drydock");

        internal static string DryDockStatus(NetPlayer by, int id)
        {
            var record = Find(id);
            if (record == null) return "No player station with that id exists.";
            if (!CanManageHangar(by, record, out _)) return "Dry dock access is private to the owner and authorised faction members.";
            if (!(record.addons?.Exists(a => a != null && a.kind == "drydock" && a.level > 0) ?? false))
                return "This station has no dry dock module.";
            record.dryDockShips ??= new List<StoredShip>();
            var lines = new List<string> { "Dry dock at " + record.name + ":" };
            if (record.dryDockBuilding)
            {
                long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                long total = Math.Max(1L, record.dryDockCompletesUtc - record.dryDockStartedUtc);
                long remaining = Math.Max(0L, record.dryDockCompletesUtc - now);
                float progress = Math.Max(0f, Math.Min(1f, 1f - remaining / (float)total));
                lines.Add("UNDER CONSTRUCTION: " + (NetGame.Db?.Ship(record.dryDockShip)?.name ?? ("Ship " + record.dryDockShip)));
                lines.Add("Blueprint item: " + record.dryDockBlueprintItem);
                lines.Add("Progress: " + (progress * 100f).ToString("0.0") + "%");
                lines.Add("Time remaining: " + remaining + " seconds");
                lines.Add("Completion time (UTC): " + DateTimeOffset.FromUnixTimeSeconds(record.dryDockCompletesUtc).ToString("u"));
                lines.Add("Blueprint consumed: " + (record.dryDockConsumeBlueprint ? "yes" : "no"));
            }
            else lines.Add("Construction status: idle");
            for (int i = 0; i < record.dryDockShips.Count; i++)
            {
                var ship = record.dryDockShips[i]; if (ship == null) continue;
                lines.Add((i + 1) + ". " + (NetGame.Db?.Ship(ship.ship)?.name ?? ("Ship " + ship.ship)) +
                    " — " + (ship.mods?.Count ?? 0) + " hull upgrades, bare hull");
            }
            if (lines.Count == 1) lines.Add("No ships are stored here.");
            lines.Add("Capacity: " + record.dryDockShips.Count + "/" + MaxDryDockShipsPerStation);
            lines.Add("Stored hull classes: Frigate, Freighter, Cruiser, Carrier. Blueprint-designated hulls can also be constructed here.");
            lines.Add("Ship construction: /station drydock-blueprints " + id + " (build from station storage).");
            return string.Join("\n", lines);
        }

        internal static string DryDockStoreShip(NetPlayer by, int id, int fleetIndex)
        {
            var record = Find(id);
            if (record == null) return "No player station with that id exists.";
            if (!CanManageHangar(by, record, out string account)) return "Dock at this station with an authorised profile to use its dry dock.";
            if (record.underConstruction) return "Finish station construction before using the dry dock.";
            if (!(record.addons?.Exists(a => a != null && a.kind == "drydock" && a.level > 0) ?? false)) return "Install a dry dock module first.";
            if (StorageCapacity(record) <= 0) return "Install station storage first; ship equipment are transferred there.";
            record.dryDockShips ??= new List<StoredShip>();
            if (record.dryDockShips.Count + (record.dryDockBuilding ? 1 : 0) >= MaxDryDockShipsPerStation) return "This station's dry dock is full or its remaining slot is reserved for the active build.";
            var fleet = NetProfiles.StoredShipsOf(account, by.OwnerClientId);
            if (fleet == null || fleetIndex < 1 || fleetIndex > fleet.Count) return "That index is not in your personal stored fleet. Use /station fleet.";
            var candidate = fleet[fleetIndex - 1];
            if (!IsDryDockHull(candidate)) return "The dry dock only accepts ships classified as Frigate, Freighter, Cruiser or Carrier.";
            var items = new Dictionary<int, long>();
            if (!CollectShipInventory(candidate, items)) return "The ship contains an invalid equipment item; nothing was transferred.";
            if (!CanFitStationItems(record, items)) return "Station storage lacks the capacity or item-type space for this ship's equipment; nothing was transferred.";
            var targetShips = new List<StoredShip>(fleet);
            StoredShip ship = candidate;
            targetShips.RemoveAt(fleetIndex - 1);
            var previousStorage = new List<PlayerStationStorage>();
            foreach (var entry in record.storage ?? new List<PlayerStationStorage>())
                if (entry != null) previousStorage.Add(new PlayerStationStorage { item = entry.item, amount = entry.amount });
            var previousLots = CloneStorageLots(record);
            AddStationItems(record, items, account);
            record.dryDockShips.Add(BareHull(ship));
            record.dryDockShipOwners ??= new List<string>();
            record.dryDockShipOwners.Add(account);
            if (!CommitStoredShipTransaction(account, targetShips))
            {
                record.dryDockShips.RemoveAt(record.dryDockShips.Count - 1);
                record.dryDockShipOwners.RemoveAt(record.dryDockShipOwners.Count - 1);
                record.storage = previousStorage;
                record.storageLots = previousLots;
                return "The dry dock transfer could not be journalled; the ship and its inventory remain in your personal fleet.";
            }
            if (!NetGame.PersistentHost || by.OwnerClientId != NetGame.LocalId) NetProfiles.RefreshProfile(by.OwnerClientId);
            return "Stored " + (NetGame.Db?.Ship(ship.ship)?.name ?? ("ship " + ship.ship)) + " in the dry dock at " + record.name + ".";
        }

        internal static string DryDockRetrieveShip(NetPlayer by, int id, int dockIndex)
        {
            var record = Find(id);
            if (record == null) return "No player station with that id exists.";
            if (!CanManageHangar(by, record, out string account)) return "Dock at this station with an authorised profile to use its dry dock.";
            if (record.underConstruction) return "Finish station construction before using the dry dock.";
            if (!(record.addons?.Exists(a => a != null && a.kind == "drydock" && a.level > 0) ?? false)) return "This station has no dry dock module.";
            record.dryDockShips ??= new List<StoredShip>();
            record.dryDockShipOwners ??= new List<string>();
            if (dockIndex < 1 || dockIndex > record.dryDockShips.Count) return "That index is not in this station's dry dock.";
            var ship = record.dryDockShips[dockIndex - 1];
            if (ship == null) return "That stored ship record is invalid.";
            if (dockIndex > record.dryDockShipOwners.Count ||
                !string.Equals(record.dryDockShipOwners[dockIndex - 1], account, StringComparison.Ordinal))
                return "That hull belongs to another pilot and cannot be retrieved by this profile.";
            var items = new Dictionary<int, long>();
            if (!CollectShipInventory(ship, items)) return "The stored ship contains an invalid equipment item; nothing was withdrawn.";
            if (items.Count > 0 && !CanFitStationItems(record, items)) return "Station storage cannot accommodate this legacy ship inventory; nothing was withdrawn.";
            var bare = BareHull(ship);
            var personalShips = NetProfiles.StoredShipsOf(account, by.OwnerClientId);
            if (personalShips == null || personalShips.Count >= 100) return "Your personal stored fleet is full or unavailable.";
            var targetShips = new List<StoredShip>(personalShips) { bare };
            var previousStorage = new List<PlayerStationStorage>();
            foreach (var entry in record.storage ?? new List<PlayerStationStorage>())
                if (entry != null) previousStorage.Add(new PlayerStationStorage { item = entry.item, amount = entry.amount });
            var previousLots = CloneStorageLots(record);
            AddStationItems(record, items, account);
            record.dryDockShips.RemoveAt(dockIndex - 1);
            record.dryDockShipOwners.RemoveAt(dockIndex - 1);
            if (!CommitStoredShipTransaction(account, targetShips))
            {
                record.dryDockShips.Insert(dockIndex - 1, ship);
                record.dryDockShipOwners.Insert(dockIndex - 1, account);
                record.storage = previousStorage;
                record.storageLots = previousLots;
                return "The dry dock retrieval could not be journalled; the ship remains in station storage.";
            }
            if (!NetGame.PersistentHost || by.OwnerClientId != NetGame.LocalId) NetProfiles.RefreshProfile(by.OwnerClientId);
            return "Returned a bare " + (NetGame.Db?.Ship(ship.ship)?.name ?? ("ship " + ship.ship)) + " to your personal stored fleet.";
        }

        internal static string Install(NetPlayer by, int id, string kind) =>
            InstallModuleFromCargo(by, id, (kind ?? "").Trim().ToLowerInvariant());

        static int StorageCapacity(PlayerStationRecord record)
        {
            int level = record?.addons?.Find(a => a != null && a.kind == "storage")?.level ?? 0;
            return Math.Max(0, level) * StorageCapacityPerLevel;
        }

        internal static string StorageStatus(NetPlayer by, int id)
        {
            if (!NetGame.Active) return "Join a multiplayer session first.";
            var record = Find(id);
            if (record == null) return "No player station with that id exists.";
            if (record.offeredForClaimUtc > 0) return "This facility is closed until the station is claimed.";
            if (by == null || !by.IsSpawned || !by.InHangar || by.Station != record.stationIndex ||
                !NetProfiles.Enabled || !NetProfiles.Controls(by.OwnerClientId))
                return "Dock at this station using a controlled profile to view storage.";
            string account = NetProfiles.AccountOf(by.OwnerClientId);
            bool manager = CanManageStorage(by, record, out _);
            EnsureStorageLots(record);
            if (!manager && !record.storageLots.Exists(x => x != null &&
                string.Equals(x.ownerProfile, account, StringComparison.Ordinal) && x.amount > 0))
                return "Storage contents are private to the owner and authorised faction members; this profile has no goods stored here.";
            if (StorageCapacity(record) <= 0) return "This station has no storage module.";

            var lines = new List<string> { "Storage at " + record.name + ":" };
            long total = 0;
            if (record.storage != null)
                foreach (var stack in record.storage)
                {
                    if (stack == null || stack.amount <= 0) continue;
                    int visibleAmount = manager ? stack.amount : OwnedStorageAmount(record, account, stack.item);
                    if (visibleAmount <= 0) continue;
                    total += visibleAmount;
                    string itemName = NetGame.Db?.Item(stack.item)?.name ?? ("Item " + stack.item);
                    lines.Add("  " + itemName + ": " + visibleAmount.ToString("N0"));
                }
            if (lines.Count == 1) lines.Add("Empty.");
            lines.Add("Capacity: " + total.ToString("N0") + "/" + StorageCapacity(record).ToString("N0"));
            return string.Join("\n", lines);
        }

        internal static string MoveStorage(NetPlayer by, int id, int item, int amount, bool deposit)
        {
            var record = Find(id);
            if (record == null) return "No player station with that id exists.";
            if (record.offeredForClaimUtc > 0) return "This facility is closed until the station is claimed.";
            if (!IsStationContext(by, record, out string account))
                return "Dock at this station with visitor permission using a controlled profile to manage storage.";
            if (deposit && !CanManageStorage(by, record, out _))
                return "Only the owner or authorised faction members may deposit into this station.";
            if (record.underConstruction) return "Finish station construction before using storage.";
            if (amount < 1 || amount > 500 || !NetGuard.Item(item))
                return "Move between 1 and 500 units of a valid item.";
            int capacity = StorageCapacity(record);
            if (capacity <= 0) return "Install a storage module first.";

            EnsureStorageLots(record);
            var stack = record.storage.Find(x => x != null && x.item == item);
            if (deposit)
            {
                long stored = 0;
                foreach (var entry in record.storage)
                    if (entry != null) stored += Math.Max(0, entry.amount);
                if (stored + amount > capacity)
                    return "Storage has room for only " + Math.Max(0L, capacity - stored) + " more units.";
                if (stack != null && stack.amount > int.MaxValue - amount)
                    return "That storage stack has reached its maximum supported quantity.";
                if (stack == null && record.storage.Count >= MaxStorageItemTypes)
                    return "Storage has reached its item-type limit.";
                var depositLots = CloneStorageLots(record);
                if (stack == null)
                    record.storage.Add(stack = new PlayerStationStorage { item = item });
                stack.amount += amount;
                AddStorageLot(record, account, item, amount);
                if (!CommitCargoTransaction(account, (item, -amount)))
                {
                    stack.amount -= amount;
                    record.storageLots = depositLots;
                    if (stack.amount == 0) record.storage.Remove(stack);
                    return "Storage transaction could not be journalled; no cargo was taken.";
                }
                if (!(NetGame.PersistentHost && by.OwnerClientId == NetGame.LocalId))
                    NetState.Instance?.SendAdmin(by.OwnerClientId, NetAdmin.Order.Take, item, amount, 0, "", "Station storage");
                return "Stored " + amount + " x " + (NetGame.Db?.Item(item)?.name ?? ("item " + item)) + " at " + record.name + ".";
            }

            if (stack == null || stack.amount < amount)
                return "The station does not have that much of this item.";
            if (OwnedStorageAmount(record, account, item) < amount)
                return "Those goods belong to another contributor and cannot be withdrawn by this profile.";
            // Keep withdrawal atomic: reject over-capacity transfers before editing storage lots.
            if (NetProfiles.WreckSalvageCargoRoom(by, account) < amount)
                return "Your ship does not have enough free cargo space for this withdrawal.";
            var previousLots = CloneStorageLots(record);
            RemoveStorageLots(record, account, item, amount);
            stack.amount -= amount;
            if (stack.amount == 0) record.storage.Remove(stack);
            if (!CommitCargoTransaction(account, (item, amount)))
            {
                if (stack.amount == 0) record.storage.Add(stack);
                stack.amount += amount;
                record.storageLots = previousLots;
                return "Storage transaction could not be journalled; the withdrawal was cancelled.";
            }
            if (!(NetGame.PersistentHost && by.OwnerClientId == NetGame.LocalId))
                NetState.Instance?.SendAdmin(by.OwnerClientId, NetAdmin.Order.Give, item, amount, 0, "", "Station storage");
            return "Withdrew " + amount + " x " + (NetGame.Db?.Item(item)?.name ?? ("item " + item)) + " from " + record.name + ".";
        }
    }
}
