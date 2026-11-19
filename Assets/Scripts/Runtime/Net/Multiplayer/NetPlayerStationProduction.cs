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

        sealed class FactoryRecipe
        {
            public int id, output, outputAmount;
            public (int item, int amount)[] inputs;
        }

        // Basic industrial commodities assembled from the game's existing trade goods.
        // Recipe identifiers are stable command-facing IDs; item IDs are resolved against the active database.
        static readonly FactoryRecipe[] FactoryRecipes =
        {
            new FactoryRecipe { id = 1, output = 120, outputAmount = 1, inputs = new[] { (119, 2) } }, // Plastics
            new FactoryRecipe { id = 2, output = 125, outputAmount = 1, inputs = new[] { (156, 2), (155, 1) } }, // Mechanical Supplies
            new FactoryRecipe { id = 3, output = 129, outputAmount = 1, inputs = new[] { (118, 1), (126, 1) } }, // Optics
            new FactoryRecipe { id = 4, output = 128, outputAmount = 1, inputs = new[] { (118, 1), (127, 1) } }, // Com. Devices
        };

        internal static string InstallFactory(NetPlayer by, int id)
        {
            var record = Find(id);
            if (record == null) return "No player station with that id exists.";
            if (!CanManage(by, record, out string account))
                return "Dock at this station and use its controlling profile to manage it.";
            if (record.underConstruction) return "Finish station construction before installing modules.";
            if (StorageCapacity(record) <= 0) return "Install a storage module first; factory inputs and outputs use station storage.";
            record.addons ??= new List<PlayerStationAddon>();
            if (record.addons.Exists(a => a != null && a.kind == "factory"))
                return "This station already has a factory.";
            if (record.addons.Count >= MaxAddonsPerStation) return "This station has reached its module limit.";

            var costs = new[] { (item: 156, amount: 150), (item: 155, amount: 100), (item: 118, amount: 50), (item: 127, amount: 50) };
            foreach (var cost in costs)
                if (NetProfiles.CargoAmount(account, cost.item) < cost.amount)
                    return "Insufficient " + (NetGame.Db?.Item(cost.item)?.name ?? ("item " + cost.item)) + "; no factory was installed.";

            record.addons.Add(new PlayerStationAddon { kind = "factory", level = 1 });
            if (!CommitCargoTransaction(account, (156, -150), (155, -100), (118, -50), (127, -50)))
            {
                record.addons.RemoveAt(record.addons.Count - 1);
                return "The factory transaction could not be journalled; no materials were taken.";
            }
            if (!(NetGame.PersistentHost && by.OwnerClientId == NetGame.LocalId))
                foreach (var cost in costs)
                    NetState.Instance?.SendAdmin(by.OwnerClientId, NetAdmin.Order.Take, cost.item, cost.amount, 0, "", "Station factory");
            return "Factory installed at " + record.name + ". Use /station recipes to view recipes and /station manufacture to produce goods.";
        }

        internal static string FactoryRecipeList()
        {
            var lines = new List<string> { "Factory recipes (one batch each):" };
            foreach (var recipe in FactoryRecipes)
            {
                string output = NetGame.Db?.Item(recipe.output)?.name ?? ("Item " + recipe.output);
                var inputs = new List<string>();
                foreach (var input in recipe.inputs)
                    inputs.Add(input.amount + " x " + (NetGame.Db?.Item(input.item)?.name ?? ("Item " + input.item)));
                lines.Add("#" + recipe.id + ": " + string.Join(" + ", inputs) + " -> " +
                    recipe.outputAmount + " x " + output);
            }
            lines.Add("Produce with /station manufacture <station id> <recipe id> <batches> (1–100).");
            return string.Join("\n", lines);
        }

        internal static string Manufacture(NetPlayer by, int id, int recipeId, int batches)
        {
            var record = Find(id);
            if (record == null) return "No player station with that id exists.";
            if (!CanManage(by, record, out _))
                return "Dock at this station and use its controlling profile to operate the factory.";
            if (record.underConstruction) return "Finish station construction before manufacturing.";
            if (!(record.addons?.Exists(a => a != null && a.kind == "factory" && a.level > 0) ?? false))
                return "Install a factory module first.";
            int capacity = StorageCapacity(record);
            if (capacity <= 0) return "The station needs a storage module for factory inputs and output.";
            if (batches < 1 || batches > 100) return "Manufacture between 1 and 100 batches at a time.";

            FactoryRecipe recipe = null;
            foreach (var candidate in FactoryRecipes)
                if (candidate.id == recipeId) { recipe = candidate; break; }
            if (recipe == null) return "Unknown recipe. Use /station recipes to list available recipes.";
            if (!NetGuard.Item(recipe.output) || NetGame.Db?.Item(recipe.output)?.type != "commodity")
                return "The recipe output is unavailable in this game's item database.";
            record.storage ??= new List<PlayerStationStorage>();

            var inputStacks = new List<(PlayerStationStorage stack, int amount)>();
            foreach (var input in recipe.inputs)
            {
                if (input.amount > int.MaxValue / batches) return "That batch count exceeds the supported input quantity.";
                int required = input.amount * batches;
                var stack = record.storage.Find(x => x != null && x.item == input.item);
                if (stack == null || stack.amount < required)
                    return "Insufficient " + (NetGame.Db?.Item(input.item)?.name ?? ("item " + input.item)) +
                        " in station storage; required: " + required + ".";
                inputStacks.Add((stack, required));
            }

            if (recipe.outputAmount > int.MaxValue / batches)
                return "That batch count exceeds the supported output quantity.";
            int produced = recipe.outputAmount * batches;
            var outputStack = record.storage.Find(x => x != null && x.item == recipe.output);
            if (outputStack != null && outputStack.amount > int.MaxValue - produced)
                return "The factory output stack has reached its maximum supported quantity.";
            if (outputStack == null && record.storage.Count >= MaxStorageItemTypes)
                return "Storage has reached its item-type limit.";

            long stored = 0;
            foreach (var stack in record.storage)
                if (stack != null) stored += Math.Max(0, stack.amount);
            long consumed = 0;
            foreach (var input in inputStacks) consumed += input.amount;
            if (stored - consumed + produced > capacity)
                return "Station storage has insufficient room for the factory output.";

            foreach (var input in inputStacks)
            {
                input.stack.amount -= input.amount;
                if (input.stack.amount == 0) record.storage.Remove(input.stack);
            }
            if (outputStack == null)
                record.storage.Add(outputStack = new PlayerStationStorage { item = recipe.output });
            outputStack.amount += produced;

            if (!Save())
            {
                outputStack.amount -= produced;
                if (outputStack.amount == 0) record.storage.Remove(outputStack);
                foreach (var input in inputStacks)
                {
                    if (input.stack.amount == 0 && !record.storage.Contains(input.stack))
                        record.storage.Add(input.stack);
                    input.stack.amount += input.amount;
                }
                return "Manufacturing could not be saved; all input materials remain in storage.";
            }

            return "Factory produced " + produced + " x " + (NetGame.Db?.Item(recipe.output)?.name ?? ("item " + recipe.output)) +
                " at " + record.name + ".";
        }

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
            long stored = 0;
            foreach (var stack in record.storage) if (stack != null) stored += Math.Max(0, stack.amount);
            if (output != null && output.amount > int.MaxValue - batches)
                return "The refined core stack has reached its maximum supported quantity.";
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
