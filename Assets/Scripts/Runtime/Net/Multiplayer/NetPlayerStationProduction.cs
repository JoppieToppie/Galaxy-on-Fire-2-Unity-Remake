using System;
using System.Collections.Generic;
using GoF2Remake.Data;

namespace GoF2Remake.Multiplayer
{
    public static partial class NetPlayerStations
    {
        // GoF2's paired ore/core commodities. A refinery converts ten units of raw ore into one core.
        static readonly int[,] RefineryRecipes =
        {
            { 154, 165 }, { 155, 166 }, { 156, 167 }, { 157, 168 },
            { 158, 169 }, { 159, 170 }, { 160, 171 }, { 161, 172 },
            { 162, 173 }, { 163, 174 }, { 164, 175 }, { 217, 218 },
        };

        internal static string InstallRefinery(NetPlayer by, int id)
        {
            var record = Find(id);
            if (record == null) return "No player station with that id exists.";
            if (!CanManage(by, record, out string account))
                return "Dock at this station and use its controlling profile to manage it.";
            if (record.underConstruction) return "Finish station construction before installing modules.";
            if (StorageCapacity(record) <= 0) return "Install a storage module first; refinery output is placed in station storage.";
            record.addons ??= new List<PlayerStationAddon>();
            if (record.addons.Exists(a => a != null && a.kind == "refinery"))
                return "This station already has a refinery.";
            if (record.addons.Count >= MaxAddonsPerStation) return "This station has reached its module limit.";

            var costs = new[] { (item: 156, amount: 100), (item: 155, amount: 50), (item: 118, amount: 30), (item: 127, amount: 20) };
            foreach (var cost in costs)
                if (NetProfiles.CargoAmount(account, cost.item) < cost.amount)
                    return "Insufficient " + (NetGame.Db?.Item(cost.item)?.name ?? ("item " + cost.item)) + "; no refinery was installed.";

            record.addons.Add(new PlayerStationAddon { kind = "refinery", level = 1 });
            if (!CommitCargoTransaction(account, (156, -100), (155, -50), (118, -30), (127, -20)))
            {
                record.addons.RemoveAt(record.addons.Count - 1);
                return "The refinery transaction could not be journalled; no materials were taken.";
            }
            if (!(NetGame.PersistentHost && by.OwnerClientId == NetGame.LocalId))
                foreach (var cost in costs)
                    NetState.Instance?.SendAdmin(by.OwnerClientId, NetAdmin.Order.Take, cost.item, cost.amount, 0, "", "Station refinery");
            return "Refinery installed at " + record.name + ". Use /station refine <id> <ore item id> <amount>.";
        }

        internal static string Refine(NetPlayer by, int id, int oreItem, int amount)
        {
            var record = Find(id);
            if (record == null) return "No player station with that id exists.";
            if (!CanManage(by, record, out string account))
                return "Dock at this station and use its controlling profile to operate the refinery.";
            if (record.underConstruction) return "Finish station construction before refining materials.";
            if (!(record.addons?.Exists(a => a != null && a.kind == "refinery" && a.level > 0) ?? false))
                return "Install a refinery module first.";
            if (StorageCapacity(record) <= 0) return "The station needs a storage module for refinery input and output.";
            if (amount < 10 || amount > 500 || amount % 10 != 0)
                return "Refine between 10 and 500 ore, in multiples of 10.";

            int outputItem = -1;
            for (int i = 0; i < RefineryRecipes.GetLength(0); i++)
                if (RefineryRecipes[i, 0] == oreItem) { outputItem = RefineryRecipes[i, 1]; break; }
            if (outputItem < 0) return "That commodity cannot be refined into a core.";

            record.storage ??= new List<PlayerStationStorage>();
            var input = record.storage.Find(x => x != null && x.item == oreItem);
            if (input == null || input.amount < amount) return "The station does not have enough of that ore in storage.";
            int batches = amount / 10;
            var output = record.storage.Find(x => x != null && x.item == outputItem);
            int stored = 0;
            foreach (var stack in record.storage) if (stack != null) stored += Math.Max(0, stack.amount);
            // Ten units become one: refining frees nine units of capacity per batch.
            if (output == null && record.storage.Count >= MaxStorageItemTypes)
                return "Storage has reached its item-type limit.";
            if (stored - amount + batches > StorageCapacity(record))
                return "Station storage has insufficient room for the refined cores.";

            input.amount -= amount;
            if (input.amount == 0) record.storage.Remove(input);
            if (output == null) record.storage.Add(output = new PlayerStationStorage { item = outputItem });
            output.amount += batches;
            if (!Save())
            {
                output.amount -= batches;
                if (output.amount == 0) record.storage.Remove(output);
                if (input.amount == 0) record.storage.Add(input);
                input.amount += amount;
                return "The refining operation could not be saved; the input ore remains in storage.";
            }
            string oreName = NetGame.Db?.Item(oreItem)?.name ?? ("Item " + oreItem);
            string coreName = NetGame.Db?.Item(outputItem)?.name ?? ("Item " + outputItem);
            return "Refined " + amount + " " + oreName + " into " + batches + " " + coreName + " at " + record.name + ".";
        }
    }
}
