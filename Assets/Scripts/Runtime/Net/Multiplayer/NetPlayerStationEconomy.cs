using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace GoF2Remake.Multiplayer
{
    public static partial class NetPlayerStations
    {
        static readonly Dictionary<string, (int item, int amount)[]> DefaultConstructionCosts =
            new Dictionary<string, (int item, int amount)[]>(StringComparer.Ordinal)
            {
                { "station", new[] { (156, 200), (155, 100), (118, 80), (127, 50) } },
                { "jumpgate", new[] { (156, 500), (155, 250), (118, 200), (127, 150) } },
                { "storage", new[] { (156, 50), (155, 25), (118, 20) } },
                { "refinery", new[] { (156, 100), (155, 50), (118, 30), (127, 20) } },
                { "factory", new[] { (156, 150), (155, 100), (118, 50), (127, 50) } },
                { "hangar", new[] { (156, 100), (155, 100), (118, 80), (127, 50) } },
                { "drydock", new[] { (156, 200), (155, 150), (118, 100), (127, 80) } },
            };

        static readonly Dictionary<string, (int item, int amount)[]> economyCosts =
            new Dictionary<string, (int item, int amount)[]>(StringComparer.Ordinal);
        static readonly Dictionary<int, FactoryRecipe> economyRecipes = new Dictionary<int, FactoryRecipe>();

        [Serializable]
        sealed class StationEconomyFile
        {
            public int version;
            public EconomyCost[] constructionCosts;
            public EconomyRecipe[] factoryRecipes;
        }

        [Serializable]
        sealed class EconomyCost
        {
            public string project;
            public EconomyMaterial[] materials;
        }

        [Serializable]
        sealed class EconomyMaterial
        {
            public int item;
            public int amount;
        }

        [Serializable]
        sealed class EconomyRecipe
        {
            public int id;
            public int output;
            public int outputAmount;
            public EconomyMaterial[] inputs;
        }

        static List<PlayerStationMaterial> ConstructionCost(string project)
        {
            if (project == "reconstruction" && !economyCosts.ContainsKey(project))
            {
                var scaled = ConstructionCost("station");
                foreach (var material in scaled)
                    material.required = (material.required * ReconstructionPercent + 99) / 100;
                return scaled;
            }

            var costs = economyCosts.TryGetValue(project, out var configured) ? configured : DefaultConstructionCosts[project];
            var result = new List<PlayerStationMaterial>(costs.Length);
            foreach (var material in costs)
                result.Add(new PlayerStationMaterial { item = material.item, required = material.amount });
            return result;
        }

        internal static string ConstructionCostSummary(string project, string label)
        {
            var costs = new List<string>();
            foreach (var material in ConstructionCost(project))
                costs.Add($"{material.required:N0} x {ConstructionItemName(material.item)} [{material.item}]");
            return label + ": " + string.Join(", ", costs);
        }

        static string ConstructionItemName(int item) => NetGame.Db?.Item(item)?.name ?? ("Item " + item);

        static IEnumerable<FactoryRecipe> EffectiveFactoryRecipes()
        {
            foreach (var recipe in BuiltInFactoryRecipes)
                yield return economyRecipes.TryGetValue(recipe.id, out var configured) ? configured : recipe;
            foreach (var recipe in RegisteredFactoryRecipes) yield return recipe;
            foreach (var recipe in economyRecipes.Values)
                if (!BuiltInFactoryRecipes.Exists(builtIn => builtIn.id == recipe.id)) yield return recipe;
        }

        static bool EconomyItem(int item) => NetGuard.Item(item) && NetGame.Db?.Item(item) != null;

        static (int item, int amount)[] ValidateEconomyMaterials(EconomyMaterial[] materials)
        {
            if (materials == null || materials.Length < 1 || materials.Length > 64)
                throw new InvalidDataException("A material list must contain 1–64 entries.");
            var seen = new HashSet<int>();
            var result = new (int, int)[materials.Length];
            for (int i = 0; i < materials.Length; i++)
            {
                var material = materials[i];
                if (material == null || !EconomyItem(material.item) || material.amount < 1 ||
                    material.amount > 100_000 || !seen.Add(material.item))
                    throw new InvalidDataException("Materials require unique catalogue item IDs and quantities of 1–100,000.");
                result[i] = (material.item, material.amount);
            }
            return result;
        }

        static void LoadStationEconomy(string folder)
        {
            string file = Path.Combine(folder, "station-economy.json");
            if (!File.Exists(file)) return;
            try
            {
                var economy = JsonUtility.FromJson<StationEconomyFile>(File.ReadAllText(file));
                if (economy?.version != 1) throw new InvalidDataException("Expected station economy version 1.");
                foreach (var cost in economy.constructionCosts ?? Array.Empty<EconomyCost>())
                {
                    if (cost == null || string.IsNullOrEmpty(cost.project) || economyCosts.ContainsKey(cost.project) ||
                        (cost.project != "reconstruction" && !DefaultConstructionCosts.ContainsKey(cost.project)))
                        throw new InvalidDataException("Unknown or duplicate construction project.");
                    economyCosts.Add(cost.project, ValidateEconomyMaterials(cost.materials));
                }
                foreach (var recipe in economy.factoryRecipes ?? Array.Empty<EconomyRecipe>())
                {
                    if (recipe == null || recipe.id <= 0 || economyRecipes.ContainsKey(recipe.id) ||
                        !EconomyItem(recipe.output) || recipe.outputAmount < 1 || recipe.outputAmount > 100_000)
                        throw new InvalidDataException("Invalid or duplicate factory recipe.");
                    economyRecipes.Add(recipe.id, new FactoryRecipe { id = recipe.id, output = recipe.output,
                        outputAmount = recipe.outputAmount, inputs = ValidateEconomyMaterials(recipe.inputs) });
                }
                foreach (var registered in RegisteredFactoryRecipes) economyRecipes.Remove(registered.id);
            }
            catch (Exception error)
            {
                economyCosts.Clear();
                economyRecipes.Clear();
                Debug.LogWarning($"NetPlayerStations: station-economy.json was ignored ({error.Message}). Built-in prices and recipes apply.");
            }
        }
    }
}
