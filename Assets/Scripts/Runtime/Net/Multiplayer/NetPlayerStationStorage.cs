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
            return NetGame.IsServer && sessionActive && registry != null &&
                by != null && by.IsSpawned && by.InHangar && by.Station == record?.stationIndex && NetProfiles.Enabled &&
                NetProfiles.Controls(by.OwnerClientId) && !string.IsNullOrEmpty(account) &&
                string.Equals(account, record?.ownerProfile, StringComparison.Ordinal);
        }

        internal static string Modules()
        {
            return "Station modules:\nStorage — 50 Iron, 25 Titanium, 20 Electronics. Holds 10,000 units.\nRefinery — 100 Iron, 50 Titanium, 30 Electronics, 20 Microchips. Requires storage; converts 10 ore into 1 matching ore core.\nFactory — 150 Iron, 100 Titanium, 50 Electronics, 50 Microchips. Requires storage; manufactures trade commodities from stored inputs.\nShip hangar — 100 Iron, 100 Titanium, 80 Electronics, 50 Microchips. Stores up to 100 small/medium bare hulls.\nDry dock — 200 Iron, 150 Titanium, 100 Electronics, 80 Microchips. Stores up to 20 large hulls and constructs dry-dock-restricted ship blueprints from station storage. Large classes (frigates, freighters, cruisers and carriers) always require a dry dock. Both hangar types transfer removable equipment and cargo into station storage.\nInstall with /station install <station id> storage|refinery|factory|hangar|drydock.";
        }


        const int MaxStoredShipsPerStation = 100;

        internal static string InstallHangar(NetPlayer by, int id)
        {
            var record = Find(id);
            if (record == null) return "No player station with that id exists.";
            if (!CanManage(by, record, out string account)) return "Dock at this station and use its controlling profile to manage it.";
            if (record.underConstruction) return "Finish station construction before installing modules.";
            record.addons ??= new List<PlayerStationAddon>();
            if (record.addons.Exists(a => a != null && a.kind == "hangar")) return "This station already has a ship hangar.";
            if (record.addons.Count >= MaxAddonsPerStation) return "This station has reached its module limit.";
            var costs = new[] { (item: 156, amount: 100), (item: 155, amount: 100), (item: 118, amount: 80), (item: 127, amount: 50) };
            foreach (var cost in costs)
                if (NetProfiles.CargoAmount(account, cost.item) < cost.amount)
                    return "Insufficient " + (NetGame.Db?.Item(cost.item)?.name ?? ("item " + cost.item)) + "; no hangar was installed.";
            record.addons.Add(new PlayerStationAddon { kind = "hangar", level = 1 });
            if (!CommitCargoTransaction(account, (156, -100), (155, -100), (118, -80), (127, -50)))
            { record.addons.RemoveAt(record.addons.Count - 1); return "The hangar installation transaction could not be journalled; no materials were taken."; }
            if (!(NetGame.PersistentHost && by.OwnerClientId == NetGame.LocalId))
                foreach (var cost in costs) NetState.Instance?.SendAdmin(by.OwnerClientId, NetAdmin.Order.Take, cost.item, cost.amount, 0, "", "Station hangar");
            return "Ship hangar installed at " + record.name + ". Use /station ships to manage stored hulls.";
        }

        static bool CanManageHangar(NetPlayer by, PlayerStationRecord record, out string account)
        {
            account = by == null ? null : NetProfiles.AccountOf(by.OwnerClientId);
            if (CanManage(by, record, out account)) return true;
            if (!NetGame.IsServer || !sessionActive || record == null || !record.factionStorageAccess ||
                by == null || !by.IsSpawned || !by.InHangar || by.Station != record.stationIndex ||
                !NetProfiles.Enabled || !NetProfiles.Controls(by.OwnerClientId) ||
                string.IsNullOrEmpty(account) || string.IsNullOrEmpty(record.ownerProfile)) return false;
            var ownerFaction = NetFactions.Of(record.ownerProfile);
            var memberFaction = NetFactions.Of(account);
            return ownerFaction != null && memberFaction != null && string.Equals(ownerFaction.id, memberFaction.id, StringComparison.Ordinal);
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

        // A parked hull is only an owned hull and its permanent upgrades. Removable equipment and hold
        // contents belong to station storage, never to the stored ship record.
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
            foreach (var stack in ship.cargo ?? new List<ItemStack>())
            {
                if (stack == null || stack.amount <= 0) continue;
                if (NetGame.Db?.Item(stack.item) == null) return false;
                items[stack.item] = (items.TryGetValue(stack.item, out long old) ? old : 0) + stack.amount;
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

        static void AddStationItems(PlayerStationRecord record, Dictionary<int, long> items)
        {
            record.storage ??= new List<PlayerStationStorage>();
            foreach (var pair in items)
            {
                if (pair.Value <= 0) continue;
                var entry = record.storage.Find(x => x != null && x.item == pair.Key);
                if (entry == null) record.storage.Add(entry = new PlayerStationStorage { item = pair.Key });
                entry.amount += (int)pair.Value;
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
            if (StorageCapacity(record) <= 0) return "Install a station storage module before storing ships; their equipment and cargo must be transferred into station storage.";
            record.storedShips ??= new List<StoredShip>();
            if (record.storedShips.Count >= MaxStoredShipsPerStation) return "This station's ship hangar is full.";
            var owned = NetProfiles.StoredShipsOf(account, by.OwnerClientId);
            if (owned == null || fleetIndex < 1 || fleetIndex > owned.Count) return "That index is not in your personal stored fleet. Use /station fleet.";
            var candidate = owned[fleetIndex - 1];
            if (candidate == null) return "That ship record is invalid.";
            if (IsDryDockHull(candidate)) return "Frigates, freighters, cruisers and carriers must use a station dry dock.";
            var items = new Dictionary<int, long>();
            if (!CollectShipInventory(candidate, items)) return "The ship contains an invalid equipment or cargo item; nothing was transferred.";
            if (!CanFitStationItems(record, items)) return "Station storage lacks the capacity or item-type space for this ship's equipment and cargo; nothing was transferred.";

            if (!NetProfiles.RemoveStoredShip(account, by.OwnerClientId, fleetIndex - 1, out StoredShip ship))
                return "The ship could not be removed from your personal fleet.";
            var previousStorage = new List<PlayerStationStorage>();
            foreach (var entry in record.storage ?? new List<PlayerStationStorage>())
                if (entry != null) previousStorage.Add(new PlayerStationStorage { item = entry.item, amount = entry.amount });
            AddStationItems(record, items);
            record.storedShips.Add(BareHull(ship));
            if (!Save())
            {
                record.storedShips.RemoveAt(record.storedShips.Count - 1);
                record.storage = previousStorage;
                if (!NetProfiles.AddStoredShip(account, by.OwnerClientId, ship))
                    Debug.LogError("NetPlayerStations: failed to roll back ship storage transfer for profile " + account);
                return "The station could not save the transfer; the ship and its inventory were returned to your personal fleet.";
            }
            if (!NetGame.PersistentHost || by.OwnerClientId != NetGame.LocalId) NetProfiles.RefreshProfile(by.OwnerClientId);
            return "Stored " + (NetGame.Db?.Ship(ship.ship)?.name ?? ("ship " + ship.ship)) + " at " + record.name +
                ". Its removable equipment and cargo are now in station storage; hull upgrades remain with the ship.";
        }

        internal static string RetrieveShip(NetPlayer by, int id, int hangarIndex)
        {
            var record = Find(id);
            if (record == null) return "No player station with that id exists.";
            if (!CanManageHangar(by, record, out string account)) return "Dock at this station with an authorised profile to use its ship hangar.";
            if (record.underConstruction) return "Finish station construction before using the ship hangar.";
            if (!(record.addons?.Exists(a => a != null && a.kind == "hangar" && a.level > 0) ?? false)) return "This station has no ship hangar module.";
            record.storedShips ??= new List<StoredShip>();
            if (hangarIndex < 1 || hangarIndex > record.storedShips.Count) return "That ship index is not in this station's hangar.";
            var ship = record.storedShips[hangarIndex - 1];
            if (ship == null) return "That stored ship record is invalid.";
            // Older registry records may still contain fitted equipment or cargo. Migrate it to station
            // storage before returning a bare hull, rather than silently putting the old behaviour back.
            var items = new Dictionary<int, long>();
            if (!CollectShipInventory(ship, items)) return "The stored ship contains an invalid equipment or cargo item; nothing was withdrawn.";
            if (items.Count > 0 && !CanFitStationItems(record, items))
                return "Station storage cannot accommodate this legacy ship inventory; nothing was withdrawn.";
            var bare = BareHull(ship);
            if (!NetProfiles.AddStoredShip(account, by.OwnerClientId, bare)) return "Your personal stored fleet is full or could not be saved.";
            var previousStorage = new List<PlayerStationStorage>();
            foreach (var entry in record.storage ?? new List<PlayerStationStorage>())
                if (entry != null) previousStorage.Add(new PlayerStationStorage { item = entry.item, amount = entry.amount });
            AddStationItems(record, items);
            record.storedShips.RemoveAt(hangarIndex - 1);
            if (!Save())
            {
                record.storedShips.Insert(hangarIndex - 1, ship);
                record.storage = previousStorage;
                var personal = NetProfiles.StoredShipsOf(account, by.OwnerClientId);
                if (personal == null || personal.Count == 0 || !NetProfiles.RemoveStoredShip(account, by.OwnerClientId, personal.Count - 1, out _))
                    Debug.LogError("NetPlayerStations: failed to roll back ship retrieval for profile " + account);
                return "The station could not save the withdrawal; the ship remains in station storage.";
            }
            if (!NetGame.PersistentHost || by.OwnerClientId != NetGame.LocalId) NetProfiles.RefreshProfile(by.OwnerClientId);
            return "Returned a bare " + (NetGame.Db?.Ship(ship.ship)?.name ?? ("ship " + ship.ship)) +
                " to your personal stored fleet. Its equipment and cargo remain in station storage.";
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

        internal static string InstallDryDock(NetPlayer by, int id)
        {
            var record = Find(id);
            if (record == null) return "No player station with that id exists.";
            if (!CanManage(by, record, out string account)) return "Dock at this station and use its controlling profile to manage it.";
            if (record.underConstruction) return "Finish station construction before installing modules.";
            if (record.addons?.Exists(a => a != null && a.kind == "drydock") == true) return "This station already has a dry dock.";
            if (record.addons?.Count >= MaxAddonsPerStation) return "This station has reached its module limit.";
            var costs = new[] { (item: 156, amount: 200), (item: 155, amount: 150), (item: 118, amount: 100), (item: 127, amount: 80) };
            foreach (var cost in costs)
                if (NetProfiles.CargoAmount(account, cost.item) < cost.amount)
                    return "Insufficient " + (NetGame.Db?.Item(cost.item)?.name ?? ("item " + cost.item)) + "; no dry dock was installed.";
            record.addons ??= new List<PlayerStationAddon>();
            record.addons.Add(new PlayerStationAddon { kind = "drydock", level = 1 });
            if (!CommitCargoTransaction(account, (156, -200), (155, -150), (118, -100), (127, -80)))
            {
                record.addons.RemoveAt(record.addons.Count - 1);
                return "The dry dock installation transaction could not be journalled; no materials were taken.";
            }
            if (!(NetGame.PersistentHost && by.OwnerClientId == NetGame.LocalId))
                foreach (var cost in costs) NetState.Instance?.SendAdmin(by.OwnerClientId, NetAdmin.Order.Take, cost.item, cost.amount, 0, "", "Station dry dock");
            return "Dry dock installed at " + record.name + ". It accepts frigates, freighters, cruisers and carriers with a configured ship class.";
        }

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
            if (StorageCapacity(record) <= 0) return "Install station storage first; ship equipment and cargo are transferred there.";
            record.dryDockShips ??= new List<StoredShip>();
            if (record.dryDockShips.Count + (record.dryDockBuilding ? 1 : 0) >= MaxDryDockShipsPerStation) return "This station's dry dock is full or its remaining slot is reserved for the active build.";
            var fleet = NetProfiles.StoredShipsOf(account, by.OwnerClientId);
            if (fleet == null || fleetIndex < 1 || fleetIndex > fleet.Count) return "That index is not in your personal stored fleet. Use /station fleet.";
            var candidate = fleet[fleetIndex - 1];
            if (!IsDryDockHull(candidate)) return "The dry dock only accepts ships classified as Frigate, Freighter, Cruiser or Carrier.";
            var items = new Dictionary<int, long>();
            if (!CollectShipInventory(candidate, items)) return "The ship contains an invalid equipment or cargo item; nothing was transferred.";
            if (!CanFitStationItems(record, items)) return "Station storage lacks the capacity or item-type space for this ship's equipment and cargo; nothing was transferred.";
            if (!NetProfiles.RemoveStoredShip(account, by.OwnerClientId, fleetIndex - 1, out StoredShip ship))
                return "The ship could not be removed from your personal fleet.";
            var previousStorage = new List<PlayerStationStorage>();
            foreach (var entry in record.storage ?? new List<PlayerStationStorage>())
                if (entry != null) previousStorage.Add(new PlayerStationStorage { item = entry.item, amount = entry.amount });
            AddStationItems(record, items);
            record.dryDockShips.Add(BareHull(ship));
            if (!Save())
            {
                record.dryDockShips.RemoveAt(record.dryDockShips.Count - 1);
                record.storage = previousStorage;
                if (!NetProfiles.AddStoredShip(account, by.OwnerClientId, ship))
                    Debug.LogError("NetPlayerStations: failed to roll back dry dock transfer for profile " + account);
                return "The dry dock could not save the transfer; the ship and its inventory were returned to your personal fleet.";
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
            if (dockIndex < 1 || dockIndex > record.dryDockShips.Count) return "That index is not in this station's dry dock.";
            var ship = record.dryDockShips[dockIndex - 1];
            if (ship == null) return "That stored ship record is invalid.";
            var items = new Dictionary<int, long>();
            if (!CollectShipInventory(ship, items)) return "The stored ship contains an invalid equipment or cargo item; nothing was withdrawn.";
            if (items.Count > 0 && !CanFitStationItems(record, items)) return "Station storage cannot accommodate this legacy ship inventory; nothing was withdrawn.";
            var bare = BareHull(ship);
            if (!NetProfiles.AddStoredShip(account, by.OwnerClientId, bare)) return "Your personal stored fleet is full or could not be saved.";
            var previousStorage = new List<PlayerStationStorage>();
            foreach (var entry in record.storage ?? new List<PlayerStationStorage>())
                if (entry != null) previousStorage.Add(new PlayerStationStorage { item = entry.item, amount = entry.amount });
            AddStationItems(record, items);
            record.dryDockShips.RemoveAt(dockIndex - 1);
            if (!Save())
            {
                record.dryDockShips.Insert(dockIndex - 1, ship);
                record.storage = previousStorage;
                var personal = NetProfiles.StoredShipsOf(account, by.OwnerClientId);
                if (personal == null || personal.Count == 0 || !NetProfiles.RemoveStoredShip(account, by.OwnerClientId, personal.Count - 1, out _))
                    Debug.LogError("NetPlayerStations: failed to roll back dry dock retrieval for profile " + account);
                return "The dry dock could not save the withdrawal; the ship remains stored.";
            }
            if (!NetGame.PersistentHost || by.OwnerClientId != NetGame.LocalId) NetProfiles.RefreshProfile(by.OwnerClientId);
            return "Returned a bare " + (NetGame.Db?.Ship(ship.ship)?.name ?? ("ship " + ship.ship)) + " to your personal stored fleet.";
        }

        internal static string Install(NetPlayer by, int id, string kind)
        {
            var record = Find(id);
            if (record == null) return "No player station with that id exists.";
            if (!CanManage(by, record, out string account))
                return "Dock at this station and use its controlling profile to manage it.";
            if (record.underConstruction) return "Finish station construction before installing modules.";

            kind = (kind ?? "").Trim().ToLowerInvariant();
            if (kind != "storage") return "Available module: storage. Use /station modules for its cost.";
            record.addons ??= new List<PlayerStationAddon>();
            if (record.addons.Exists(a => a != null && string.Equals(a.kind, kind, StringComparison.OrdinalIgnoreCase)))
                return "This station already has a storage module.";
            if (record.addons.Count >= MaxAddonsPerStation) return "This station has reached its module limit.";

            var costs = new[] { (item: 156, amount: 50), (item: 155, amount: 25), (item: 118, amount: 20) };
            foreach (var cost in costs)
                if (NetProfiles.CargoAmount(account, cost.item) < cost.amount)
                    return "Insufficient " + (NetGame.Db?.Item(cost.item)?.name ?? ("item " + cost.item)) + "; no module was installed.";

            record.addons.Add(new PlayerStationAddon { kind = kind, level = 1 });
            if (!CommitCargoTransaction(account, (156, -50), (155, -25), (118, -20)))
            {
                record.addons.RemoveAt(record.addons.Count - 1);
                return "The module transaction could not be journalled; no materials were taken.";
            }

            if (!(NetGame.PersistentHost && by.OwnerClientId == NetGame.LocalId))
                foreach (var cost in costs)
                    NetState.Instance?.SendAdmin(by.OwnerClientId, NetAdmin.Order.Take, cost.item, cost.amount, 0, "", "Station module");
            return "Storage module installed at " + record.name + ". It can hold " + StorageCapacityPerLevel.ToString("N0") + " units.";
        }

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
            if (!CanManageStorage(by, record, out _)) return "Storage contents are private to the owner and any faction members granted access.";
            if (StorageCapacity(record) <= 0) return "This station has no storage module.";

            var lines = new List<string> { "Storage at " + record.name + ":" };
            long total = 0;
            if (record.storage != null)
                foreach (var stack in record.storage)
                {
                    if (stack == null || stack.amount <= 0) continue;
                    total += stack.amount;
                    string itemName = NetGame.Db?.Item(stack.item)?.name ?? ("Item " + stack.item);
                    lines.Add("  " + itemName + ": " + stack.amount.ToString("N0"));
                }
            if (lines.Count == 1) lines.Add("Empty.");
            lines.Add("Capacity: " + total.ToString("N0") + "/" + StorageCapacity(record).ToString("N0"));
            return string.Join("\n", lines);
        }

        internal static string MoveStorage(NetPlayer by, int id, int item, int amount, bool deposit)
        {
            var record = Find(id);
            if (record == null) return "No player station with that id exists.";
            if (!CanManageStorage(by, record, out string account))
                return "Dock at this station and use its controlling profile to manage storage.";
            if (record.underConstruction) return "Finish station construction before using storage.";
            if (amount < 1 || amount > 500 || !NetGuard.Item(item))
                return "Move between 1 and 500 units of a valid item.";
            int capacity = StorageCapacity(record);
            if (capacity <= 0) return "Install a storage module first.";

            record.storage ??= new List<PlayerStationStorage>();
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
                if (stack == null)
                    record.storage.Add(stack = new PlayerStationStorage { item = item });
                stack.amount += amount;
                if (!CommitCargoTransaction(account, (item, -amount)))
                {
                    stack.amount -= amount;
                    if (stack.amount == 0) record.storage.Remove(stack);
                    return "Storage transaction could not be journalled; no cargo was taken.";
                }
                if (!(NetGame.PersistentHost && by.OwnerClientId == NetGame.LocalId))
                    NetState.Instance?.SendAdmin(by.OwnerClientId, NetAdmin.Order.Take, item, amount, 0, "", "Station storage");
                return "Stored " + amount + " x " + (NetGame.Db?.Item(item)?.name ?? ("item " + item)) + " at " + record.name + ".";
            }

            if (stack == null || stack.amount < amount)
                return "The station does not have that much of this item.";
            stack.amount -= amount;
            if (stack.amount == 0) record.storage.Remove(stack);
            if (!CommitCargoTransaction(account, (item, amount)))
            {
                if (stack.amount == 0) record.storage.Add(stack);
                stack.amount += amount;
                return "Storage transaction could not be journalled; the withdrawal was cancelled.";
            }
            if (!(NetGame.PersistentHost && by.OwnerClientId == NetGame.LocalId))
                NetState.Instance?.SendAdmin(by.OwnerClientId, NetAdmin.Order.Give, item, amount, 0, "", "Station storage");
            return "Withdrew " + amount + " x " + (NetGame.Db?.Item(item)?.name ?? ("item " + item)) + " from " + record.name + ".";
        }
    }
}
