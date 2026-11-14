// Single-player collection mode. All changes are conditional; original data tables stay untouched.
using System.Collections.Generic;
using UnityEngine;

namespace GoF2Remake.Data
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class ScavengerMode
    {
        public const int Fireworks = 232, ChromoPlasma = 210, Champion = 35, StartStation = 70;
        public const int ShopTechLimit = 5;
        public static bool Active => Session.Mode == GameMode.Scavenger && !NetSession.Active;
        public static string Name => Localization.Extra("scavengerName", "Scavenger");
        // Deliberately ordered: primary weapons, turrets, equipment; consumable ingredients and cores follow.
        static readonly int[] Collection = {
            228, 229, 183, 231, 230, 193, 178, 224, 182,
            225, 54, 59, 67, 194, 195, 188, 80, 84, 85, 90, 96, 184, 220, 200, 206, 208, 223, 227
        };
        static readonly HashSet<int> ExcludedLoot = new HashSet<int> {
            115, 116, 117, 131, 201, 202, 203, 204, 209, 210, 232
        };

        /// <summary>The original's eleven ordered Extreme rules: omit rule six, already true at every Scavenger difficulty.
        /// All fourteen original language tables share this order; Japanese uses a long-vowel mark as its bullet.</summary>
        public static string ExtremeDescription(string original)
        {
            if (string.IsNullOrEmpty(original)) return original;
            string text = original.Replace("\\r\\n", "\n").Replace("\\n", "\n");
            var rules = System.Text.RegularExpressions.Regex.Matches(text, @"(?m)^[ \t]*[-ー][^\r\n]*(?:\r?\n|$)");
            if (rules.Count != 11) return original;
            var shopRule = rules[5];
            return text.Remove(shopRule.Index, shopRule.Length);
        }
        public static bool BlueprintAllowed(int item) => item == ChromoPlasma || item == Fireworks;
        public static bool LootAllowed(ItemData item) => item != null && item.index >= 0 && item.index <= 232
            && !ExcludedLoot.Contains(item.index) && item.categoryId != 23 && item.categoryId != 24;
        public static bool ShopAllowed(ItemData item) => item != null && item.index != Fireworks
            && item.categoryId != 23 && item.categoryId != 24
            && (item.TypeId == 4 || item.TypeId == 1 || item.techLevel <= ShopTechLimit);

        public static void Start(bool ownKaamo)
        {
            Session.Mode = GameMode.Scavenger;
            SaveGame.SelectedMode = GameMode.Scavenger;
            Session.Economy = Economy.Default;
            Session.NoMedals = false;
            Session.UseCompletedWorld();
            Session.StationIndex = StartStation;
            Session.VisitedStations = new HashSet<int> { StartStation };
            Session.ShipIndex = 0; // Betty
            // No additional starter gear: a scanner, tractor beam, EMP blaster and ten EMP missiles.
            Session.Equipment = new List<ItemStack> {
                new ItemStack(16, 1), new ItemStack(41, 10), new ItemStack(68, 1), new ItemStack(82, 1)
            };
            Session.SelectedSecondary = 41;
            Session.Cargo.Clear();
            Session.Credits = 0; // Standard single-player starting balance; can be tuned independently.
            Session.KaamoState = ownKaamo ? 3 : 2;
            Session.HiddenBlueprintsFound = 31;
            Session.UnlockedBlueprints = new HashSet<int> { ChromoPlasma, Fireworks };
            var db = Database.Shared;
            GalaxyMap.Visibility(db);
            foreach (var system in db.Systems) GalaxyMap.Reveal(db, system.index);
        }

        public static void ApplyRecipes(Database db)
        {
            if (!Active) return;
            foreach (var item in db.Items)
                if (!BlueprintAllowed(item.index)) item.blueprint = new List<BlueprintPart>();
            var recipe = new List<BlueprintPart>();
            void Add(int id, int amount) => recipe.Add(new BlueprintPart { item = id, name = db.Item(id).name, amount = amount });
            foreach (int id in Collection) Add(id, 1);
            Add(ChromoPlasma, 1);
            Add(209, 1);
            foreach (var item in db.Items)
                if (item.index <= 232 && item.categoryId == 24) Add(item.index, 5);
            db.Item(Fireworks).blueprint = recipe;
        }

        public static List<ItemStack> RollLoot(Database db, bool freighter)
        {
            var result = new List<ItemStack>();
            var pool = db.Items.FindAll(LootAllowed);
            if (pool.Count == 0) return result;
            int n = Random.Range(0, 3);
            if (n == 0 && Random.Range(0, 3) == 0) return result; // Default economy's empty-hold probability.
            for (int i = 0; i < Mathf.Max(1, n); i++)
            {
                var item = pool[Random.Range(0, pool.Count)];
                int amount = item.TypeId == 4 ? Random.Range(1, 10) : Random.Range(1, 4);
                if (freighter) amount = Mathf.Max(amount * Random.Range(2, 6), Random.Range(8, 13));
                result.Add(new ItemStack(item.index, amount));
            }
            return result;
        }
    }
}
