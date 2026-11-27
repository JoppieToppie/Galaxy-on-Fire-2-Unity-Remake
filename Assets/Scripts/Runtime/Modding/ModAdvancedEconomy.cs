// Native opt-in commodity economy. No active configuration means no change to vanilla.
using System;
using System.Collections.Generic;
using GoF2Remake.Data;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace GoF2Remake.Modding
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class ModAdvancedEconomy
    {
        public const string File = "advanced-economy.json";
        public const int MaxStock = 100000;
        public static bool Enabled { get; private set; }
        public static bool PersistentInventory { get; private set; }
        public static bool SaveInventory { get; private set; }
        public static bool SimulateUnvisitedStations { get; private set; }
        public static bool Production { get; private set; }
        public static bool Consumption { get; private set; }
        public static int TickSeconds { get; private set; } = 900;

        public sealed class Rule
        {
            public readonly int Item, Initial, Target, Maximum, Produced, Consumed;
            public Rule(int item, int initial, int target, int maximum, int produced, int consumed)
            {
                Item = item;
                Maximum = Math.Clamp(maximum, 1, MaxStock);
                Target = Math.Clamp(target, 1, Maximum);
                Initial = Math.Clamp(initial, 0, Maximum);
                Produced = Math.Clamp(produced, 0, MaxStock);
                Consumed = Math.Clamp(consumed, 0, MaxStock);
            }

            // Output and consumption are explicit quantities, never an automatic refill to target.
            public int Advance(int current, bool production, bool consumption)
            {
                long result = Math.Clamp(current, 0, Maximum);
                if (production) result += Produced;
                if (consumption) result -= Consumed;
                return (int)Math.Clamp(result, 0L, Maximum);
            }
        }

        static readonly Dictionary<int, Dictionary<int, Rule>> rules = new Dictionary<int, Dictionary<int, Rule>>();
        public static IReadOnlyDictionary<int, Rule> At(int station) =>
            Enabled && PersistentInventory && rules.TryGetValue(station, out var rows) ? rows : null;

        public static IEnumerable<int> Stations => rules.Keys;

        internal static void Apply(Database db)
        {
            Enabled = PersistentInventory = SaveInventory = SimulateUnvisitedStations = Production = Consumption = false;
            TickSeconds = 900;
            rules.Clear();
            foreach (var mod in ModManager.Active)
            {
                if (mod == null || mod.IsBase) continue;
                try
                {
                    var token = ModJson.Read(mod.Source, File);
                    if (token == null) continue;
                    if (!(token is JObject o)) throw new ModJsonException(File + ": expected an object");
                    // A disabled config cannot contribute any settings or rows.
                    if (!ModJson.Bool(o, "enabled", false, File)) continue;
                    bool inventory = ModJson.Bool(o, "persistentInventory", false, File);
                    bool save = ModJson.Bool(o, "saveInventory", false, File);
                    bool unvisited = ModJson.Bool(o, "simulateUnvisitedStations", false, File);
                    bool production = ModJson.Bool(o, "stationProduction", false, File);
                    bool consumption = ModJson.Bool(o, "stationConsumption", false, File);
                    int interval = Math.Clamp(ModJson.Int(o, "tickIntervalSeconds", 900, File), 60, 86400);
                    var parsed = new Dictionary<(int station, int item), Rule>();
                    if (ModJson.Get(o, "stockRules") is JToken entries)
                    {
                        if (!(entries is JArray list)) throw new ModJsonException(File + ": stockRules must be an array");
                        foreach (var entry in list)
                        {
                            if (!(entry is JObject row)) throw new ModJsonException(File + ": stockRules entries must be objects");
                            string st = ModJson.Str(row, "station"), it = ModJson.Str(row, "item");
                            if (!ModWorld.TryResolveStation(st, out int station) &&
                                !ModWorld.TryResolveStation(mod.Id + ":" + st, out station))
                                throw new ModJsonException(File + ": unknown station " + st);
                            if (!ModContent.TryResolveItem(it, out int item) &&
                                !ModContent.TryResolveItem(mod.Id + ":" + it, out item))
                                throw new ModJsonException(File + ": unknown item " + it);
                            if (!db.Stations.Exists(s => s.index == station) || db.Item(item)?.TypeId != 4)
                                throw new ModJsonException(File + ": stockRules require a station and a commodity");
                            // Keep emergency fuel at deep-space stations under the existing safety rules.
                            if ((station == 10 || station == 100 || station == 101) && item == GalaxyMap.EnergyCellItem)
                                throw new ModJsonException(File + ": emergency energy cells cannot be managed here");
                            int target = ModJson.Int(row, "targetStock", 100, File);
                            int maximum = ModJson.Int(row, "maximumStock", target, File);
                            int initial = ModJson.Int(row, "initialStock", target, File);
                            int made = ModJson.Int(row, "productionPerTick", 0, File);
                            int used = ModJson.Int(row, "consumptionPerTick", 0, File);
                            if (target < 1 || maximum < target || maximum > MaxStock || initial < 0 || initial > maximum ||
                                made < 0 || made > MaxStock || used < 0 || used > MaxStock)
                                throw new ModJsonException(File + ": invalid stock limits or flow for " + st + "/" + it);
                            parsed[(station, item)] = new Rule(item, initial, target, maximum,
                                production ? made : 0, consumption ? used : 0);
                        }
                    }
                    // Publish only after the entire file has been validated.
                    Enabled = true;
                    if (!inventory) continue;
                    PersistentInventory = true;
                    SaveInventory |= save;
                    SimulateUnvisitedStations |= unvisited;
                    Production |= production;
                    Consumption |= consumption;
                    TickSeconds = interval;
                    foreach (var pair in parsed)
                    {
                        if (!rules.TryGetValue(pair.Key.station, out var rows))
                            rules[pair.Key.station] = rows = new Dictionary<int, Rule>();
                        rows[pair.Key.item] = pair.Value;
                    }
                }
                catch (ModJsonException e) { Debug.LogWarning($"[{mod.Id}] {e.Message}"); }
            }
        }

        public static bool Eligible(Database db, int item) =>
            db != null && db.Item(item)?.TypeId == 4 && !ModContent.IsMissingItem(item) &&
            ModCampaigns.ItemAllowed(db, item) && ModUnlocks.ItemAvailable(item) &&
            ModContent.ItemAvailableFor(item, "shops") && !Shop.NotInSession(db, item);
    }
}
