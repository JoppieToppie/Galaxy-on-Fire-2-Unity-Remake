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
            return "Station modules:\nStorage — 50 Iron, 25 Titanium, 20 Electronics. Holds 10,000 units.\nRefinery — 100 Iron, 50 Titanium, 30 Electronics, 20 Microchips. Requires storage; converts 10 ore into 1 matching ore core.\nInstall with /station install <station id> storage or /station install <station id> refinery.";
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
            int total = 0;
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
                int stored = 0;
                foreach (var entry in record.storage)
                    if (entry != null) stored += Math.Max(0, entry.amount);
                if (stored + amount > capacity)
                    return "Storage has room for only " + Math.Max(0, capacity - stored) + " more units.";
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
                return "Stored " + amount + " x " + NetGame.Db.Item(item).name + " at " + record.name + ".";
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
            return "Withdrew " + amount + " x " + NetGame.Db.Item(item).name + " from " + record.name + ".";
        }
    }
}
