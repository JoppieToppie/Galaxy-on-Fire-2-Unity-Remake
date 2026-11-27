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
        public static bool RegionalTrade { get; private set; }
        public static bool WarPressure { get; private set; }
        public static bool TradeDisruption { get; private set; }
        public static int TickSeconds { get; private set; } = 900;
        public static int FrontlinePressure { get; private set; } = 50;
        public static int BattlePressure { get; private set; } = 100;
        public static bool DynamicPricing { get; private set; }
        public static bool PlayerMarketTrade { get; private set; }
        public static bool NpcConstructionSupply { get; private set; }
        public static int NpcConstructionUnits { get; private set; } = 100;
        public static int PlayerMarketBudget { get; private set; }
        public static int PlayerMarketUnits { get; private set; } = 100;
        public static bool TransportCosts { get; private set; }
        public static int ScarcityMarkup { get; private set; } = 100;
        public static int SurplusDiscount { get; private set; } = 30;
        public static int WarMarkup { get; private set; } = 50;
        public static int TransportPerHop { get; private set; } = 5;
        public static int SellSpread { get; private set; } = 10;
        public static bool PlayerGateRouting { get; private set; }
        public static bool AutomaticRoutes { get; private set; }
        public static int AutoCapacity { get; private set; } = 100;
        public static int AutoWarLoss { get; private set; }
        public static int TicksPerHop { get; private set; } = 1;
        public static int MaxAutoHops { get; private set; } = 8;

        public sealed class Rule
        {
            public readonly int Item, Initial, Target, Maximum, Produced, Consumed, WarDemand;
            public Rule(int item, int initial, int target, int maximum, int produced, int consumed, int warDemand = 0)
            {
                Item = item;
                Maximum = Math.Clamp(maximum, 1, MaxStock);
                Target = Math.Clamp(target, 1, Maximum);
                Initial = Math.Clamp(initial, 0, Maximum);
                Produced = Math.Clamp(produced, 0, MaxStock);
                Consumed = Math.Clamp(consumed, 0, MaxStock);
                WarDemand = Math.Clamp(warDemand, 0, MaxStock);
            }

            // Output and consumption are explicit quantities, never an automatic refill to target.
            public int Advance(int current, bool production, bool consumption, int pressure = 0)
            {
                long result = Math.Clamp(current, 0, Maximum);
                if (production) result += Produced;
                if (consumption) result -= Consumed;
                result -= (long)WarDemand * Math.Clamp(pressure, 0, 100) / 100;
                return (int)Math.Clamp(result, 0L, Maximum);
            }
        }

        public sealed class Route
        {
            public readonly int Source, Destination, Item, Capacity, TravelTicks, WarLossPercent;
            public Route(int source, int destination, int item, int capacity, int travelTicks, int warLossPercent)
            {
                Source = source; Destination = destination; Item = item;
                Capacity = capacity; TravelTicks = travelTicks; WarLossPercent = warLossPercent;
            }
        }

        static readonly Dictionary<(int source, int destination, int item), Route> routes =
            new Dictionary<(int, int, int), Route>();
        public static IEnumerable<Route> Routes => routes.Values;

        // One bounded list-price formula for the host and tests. Price is never taken from a client.
        public static (int buy, int sell) Quote(int basis, int amount, int target, int pressure, int hops,
            int scarcity, int surplus, int war, int freight, int spread)
        {
            if (basis <= 0) return (0, 0);
            double ratio = (double)Math.Max(0, amount) / Math.Max(1, target);
            double multiplier = 1 + Math.Max(0, 1 - ratio) * scarcity / 100.0
                - Math.Min(1, Math.Max(0, ratio - 1)) * surplus / 100.0
                + Math.Clamp(pressure, 0, 100) * war / 10000.0 + Math.Max(0, hops) * freight / 100.0;
            int buy = (int)Math.Clamp(Math.Round(basis * Math.Clamp(multiplier, 0.1, 20)), 1, int.MaxValue);
            int sell = (int)Math.Clamp(Math.Floor((long)buy * (100 - Math.Clamp(spread, 1, 99)) / 100.0), 0, buy);
            return (buy, sell);
        }

        // Price the same stock boundary in both directions. A round trip crosses
        // identical boundaries in reverse order, with the sell spread on every unit.
        public static int TradeUnitPrice(int basis, int stock, int target, int pressure, int hops,
            int scarcity, int surplus, int war, int freight, int spread, bool buy)
        {
            int boundary = buy ? Math.Max(0, stock - 1) : Math.Max(0, stock);
            var quote = Quote(basis, boundary, target, pressure, hops, scarcity, surplus, war, freight, spread);
            return buy ? quote.buy : quote.sell;
        }

        public static int DispatchAmount(int source, int sourceTarget, int destination, int destinationTarget, int capacity) =>
            Math.Min(Math.Max(0, capacity), Math.Min(Math.Max(0, source - sourceTarget), Math.Max(0, destinationTarget - destination)));

        public static int ConstructionSupplyAmount(int stock, int target, int orderRemaining, int projectRemaining, int budget) =>
            Math.Min(500, Math.Min(Math.Max(0, stock - target), Math.Min(Math.Max(0, orderRemaining),
                Math.Min(Math.Max(0, projectRemaining), Math.Max(0, budget)))));

        public static int SurvivingCargo(int amount, int lossPercent, int pressure) =>
            Math.Max(0, amount) - (int)((long)Math.Max(0, amount) * Math.Clamp(lossPercent, 0, 100) *
                Math.Clamp(pressure, 0, 100) / 10000);

        public static bool GateLinked(Database db, int sourceSystem, int destinationSystem)
        {
            if (sourceSystem == destinationSystem) return true;
            var a = db.Systems.Find(s => s.index == sourceSystem);
            var b = db.Systems.Find(s => s.index == destinationSystem);
            return a != null && b != null && (a.jumpRoutesTo?.Contains(b.index) == true ||
                b.jumpRoutesTo?.Contains(a.index) == true);
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
            routes.Clear();
            RegionalTrade = WarPressure = TradeDisruption = PlayerGateRouting = AutomaticRoutes = false;
            FrontlinePressure = 50; BattlePressure = 100;
            DynamicPricing = TransportCosts = PlayerMarketTrade = NpcConstructionSupply = false;
            NpcConstructionUnits = 100;
            PlayerMarketBudget = 0; PlayerMarketUnits = 100;
            ScarcityMarkup = 100; SurplusDiscount = 30; WarMarkup = 50; TransportPerHop = 5; SellSpread = 10;
            AutoCapacity = 100; AutoWarLoss = 0; TicksPerHop = 1; MaxAutoHops = 8;
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
                    bool trade = ModJson.Bool(o, "regionalTrade", false, File);
                    bool war = ModJson.Bool(o, "warPressure", false, File);
                    bool disruption = ModJson.Bool(o, "tradeDisruption", false, File);
                    bool pricing = ModJson.Bool(o, "dynamicPricing", false, File);
                    bool freight = ModJson.Bool(o, "transportCosts", false, File);
                    bool market = ModJson.Bool(o, "playerMarketTrade", false, File);
                    bool construction = ModJson.Bool(o, "npcConstructionSupply", false, File);
                    int constructionUnits = Math.Clamp(ModJson.Int(o, "npcConstructionUnitsPerTick", 100, File), 1, MaxStock);
                    int marketBudget = Math.Clamp(ModJson.Int(o, "playerMarketBudgetPerTick", 0, File), 0, 1000000000);
                    int marketUnits = Math.Clamp(ModJson.Int(o, "playerMarketUnitsPerTick", 100, File), 1, MaxStock);
                    int scarcity = Math.Clamp(ModJson.Int(o, "scarcityMarkupPercent", 100, File), 0, 500);
                    int surplus = Math.Clamp(ModJson.Int(o, "surplusDiscountPercent", 30, File), 0, 90);
                    int warPrice = Math.Clamp(ModJson.Int(o, "warMarkupPercent", 50, File), 0, 500);
                    int freightPrice = Math.Clamp(ModJson.Int(o, "transportCostPerHopPercent", 5, File), 0, 100);
                    int spread = Math.Clamp(ModJson.Int(o, "sellSpreadPercent", 10, File), 1, 99);
                    bool playerGates = ModJson.Bool(o, "playerGateRouting", false, File);
                    bool automatic = ModJson.Bool(o, "automaticRoutes", false, File);
                    int frontline = Math.Clamp(ModJson.Int(o, "frontlinePressurePercent", 50, File), 0, 100);
                    int battle = Math.Clamp(ModJson.Int(o, "battlePressurePercent", 100, File), 0, 100);
                    int capacityAuto = Math.Clamp(ModJson.Int(o, "automaticRouteCapacity", 100, File), 1, MaxStock);
                    int lossAuto = Math.Clamp(ModJson.Int(o, "automaticRouteWarLossPercent", 0, File), 0, 100);
                    int hopTicks = Math.Clamp(ModJson.Int(o, "ticksPerHop", 1, File), 1, 12);
                    int maxHops = Math.Clamp(ModJson.Int(o, "maxAutomaticHops", 8, File), 1, 8);
                    int interval = Math.Clamp(ModJson.Int(o, "tickIntervalSeconds", 900, File), 60, 86400);
                    var parsed = new Dictionary<(int station, int item), Rule>();
                    var systemDefaults = new Dictionary<(int station, int item), Rule>();
                    if (ModJson.Get(o, "stockRules") is JToken entries)
                    {
                        if (!(entries is JArray list)) throw new ModJsonException(File + ": stockRules must be an array");
                        foreach (var entry in list)
                        {
                            if (!(entry is JObject row)) throw new ModJsonException(File + ": stockRules entries must be objects");
                            string st = ModJson.Str(row, "station"), sys = ModJson.Str(row, "system"), it = ModJson.Str(row, "item");
                            if (string.IsNullOrEmpty(st) == string.IsNullOrEmpty(sys))
                                throw new ModJsonException(File + ": a stock rule requires exactly one station or system");
                            var stations = new List<int>();
                            if (!string.IsNullOrEmpty(st))
                            {
                                if (!ModWorld.TryResolveStation(st, out int station) &&
                                    !ModWorld.TryResolveStation(mod.Id + ":" + st, out station))
                                    throw new ModJsonException(File + ": unknown station " + st);
                                if (!db.Stations.Exists(s => s.index == station))
                                    throw new ModJsonException(File + ": unknown station " + st);
                                stations.Add(station);
                            }
                            else
                            {
                                if (!ModWorld.TryResolveSystem(sys, out int system) &&
                                    !ModWorld.TryResolveSystem(mod.Id + ":" + sys, out system))
                                    throw new ModJsonException(File + ": unknown system " + sys);
                                foreach (var station in db.Stations)
                                    if (station.system == system) stations.Add(station.index);
                            }
                            if (!ModContent.TryResolveItem(it, out int item) &&
                                !ModContent.TryResolveItem(mod.Id + ":" + it, out item))
                                throw new ModJsonException(File + ": unknown item " + it);
                            if (db.Item(item)?.TypeId != 4)
                                throw new ModJsonException(File + ": stockRules require a commodity");
                            int target = ModJson.Int(row, "targetStock", 100, File);
                            int maximum = ModJson.Int(row, "maximumStock", target, File);
                            int initial = ModJson.Int(row, "initialStock", target, File);
                            int made = ModJson.Int(row, "productionPerTick", 0, File);
                            int used = ModJson.Int(row, "consumptionPerTick", 0, File);
                            int demand = ModJson.Int(row, "warDemandPerTick", 0, File);
                            if (target < 1 || maximum < target || maximum > MaxStock || initial < 0 || initial > maximum ||
                                made < 0 || made > MaxStock || used < 0 || used > MaxStock || demand < 0 || demand > MaxStock)
                                throw new ModJsonException(File + ": invalid stock limits or flow for " + st + "/" + it);
                            foreach (int station in stations)
                            {
                                // System-wide profiles leave emergency fuel at special stations under vanilla rules.
                                if ((station == 10 || station == 100 || station == 101) && item == GalaxyMap.EnergyCellItem)
                                {
                                    if (st != null) throw new ModJsonException(File + ": emergency energy cells cannot be managed here");
                                    continue;
                                }
                                var rule = new Rule(item, initial, target, maximum,
                                    production ? made : 0, consumption ? used : 0, war ? demand : 0);
                                (st != null ? parsed : systemDefaults)[(station, item)] = rule;
                            }
                        }
                    }
                    foreach (var pair in systemDefaults)
                        if (!parsed.ContainsKey(pair.Key)) parsed.Add(pair.Key, pair.Value);
                    var parsedRoutes = new Dictionary<(int source, int destination, int item), Route>();
                    if (trade && ModJson.Get(o, "tradeRoutes") is JToken routeEntries)
                    {
                        if (!(routeEntries is JArray list)) throw new ModJsonException(File + ": tradeRoutes must be an array");
                        foreach (var entry in list)
                        {
                            if (!(entry is JObject row)) throw new ModJsonException(File + ": tradeRoutes entries must be objects");
                            string from = ModJson.Str(row, "source"), to = ModJson.Str(row, "destination"), it = ModJson.Str(row, "item");
                            if ((!ModWorld.TryResolveStation(from, out int source) && !ModWorld.TryResolveStation(mod.Id + ":" + from, out source)) ||
                                (!ModWorld.TryResolveStation(to, out int destination) && !ModWorld.TryResolveStation(mod.Id + ":" + to, out destination)) ||
                                (!ModContent.TryResolveItem(it, out int item) && !ModContent.TryResolveItem(mod.Id + ":" + it, out item)))
                                throw new ModJsonException(File + ": unknown trade route reference");
                            var a = db.Stations.Find(s => s.index == source);
                            var b = db.Stations.Find(s => s.index == destination);
                            if (source == destination || a == null || b == null || db.Item(item)?.TypeId != 4)
                                throw new ModJsonException(File + ": trade route requires distinct stations and a commodity");
                            int capacity = ModJson.Int(row, "capacity", 100, File);
                            int ticks = ModJson.Int(row, "travelTicks", 1, File);
                            int loss = ModJson.Int(row, "warLossPercent", 0, File);
                            if (capacity < 1 || capacity > MaxStock || ticks < 1 || ticks > 96 || loss < 0 || loss > 100)
                                throw new ModJsonException(File + ": invalid trade route capacity, travel time or loss");
                            parsedRoutes[(source, destination, item)] = new Route(source, destination, item, capacity, ticks,
                                disruption ? loss : 0);
                        }
                    }
                    // Publish only after the entire file has been validated.
                    Enabled = true;
                    if (!inventory) continue;
                    PersistentInventory = true;
                    SaveInventory |= save;
                    if (pricing && save)
                    {
                        DynamicPricing = true;
                        ScarcityMarkup = scarcity; SurplusDiscount = surplus; WarMarkup = warPrice;
                        SellSpread = spread; TransportCosts = freight; TransportPerHop = freightPrice;
                    }
                    else if (pricing) Debug.LogWarning($"[{mod.Id}] {File}: dynamic pricing requires saveInventory; disabled.");
                    if (market && pricing && save) { PlayerMarketTrade = true; PlayerMarketBudget = marketBudget; PlayerMarketUnits = marketUnits; }
                    else if (market) Debug.LogWarning($"[{mod.Id}] {File}: player market trade requires dynamic pricing and saved inventory; disabled.");
                    if (construction && pricing && save) { NpcConstructionSupply = true; NpcConstructionUnits = constructionUnits; }
                    else if (construction) Debug.LogWarning($"[{mod.Id}] {File}: NPC construction supply requires dynamic pricing and saved inventory; disabled.");
                    SimulateUnvisitedStations |= unvisited;
                    Production |= production;
                    Consumption |= consumption;
                    RegionalTrade |= trade;
                    WarPressure |= war;
                    TradeDisruption |= disruption;
                    if (war) { FrontlinePressure = frontline; BattlePressure = battle; }
                    if (trade)
                    {
                        PlayerGateRouting |= playerGates;
                        AutomaticRoutes |= automatic;
                        TicksPerHop = hopTicks;
                        if (automatic) { AutoCapacity = capacityAuto; AutoWarLoss = disruption ? lossAuto : 0; MaxAutoHops = maxHops; }
                    }
                    foreach (var pair in parsedRoutes) routes[pair.Key] = pair.Value;
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
            foreach (var route in routes.Values)
                if (At(route.Source)?.ContainsKey(route.Item) != true || At(route.Destination)?.ContainsKey(route.Item) != true)
                    Debug.LogWarning($"{File}: route {route.Source} -> {route.Destination}, item {route.Item}, requires managed stock at both endpoints; ignored until configured.");
        }

        public static int Pressure(Database db, int station)
        {
            if (!WarPressure || !ModNpcFactionWar.Enabled || NpcFactionWar.NetworkCampaign == null) return 0;
            int system = Shop.SystemOf(db, station);
            var data = db.Systems.Find(s => s.index == system);
            if (NpcFactionWar.TryGetBattleStatus(system, station, out _)) return BattlePressure;
            return SystemPressure(db, system);
        }

        public static int SystemPressure(Database db, int system)
        {
            if (!WarPressure || !ModNpcFactionWar.Enabled || NpcFactionWar.NetworkCampaign == null) return 0;
            var data = db.Systems.Find(s => s.index == system);
            if (data == null) return 0;
            if (data.stations != null)
                foreach (int station in data.stations)
                    if (NpcFactionWar.TryGetBattleStatus(system, station, out _)) return BattlePressure;
            return NpcFactionWar.NetworkCampaign.IsFrontline(db, data) ? FrontlinePressure : 0;
        }

        public static bool Eligible(Database db, int item) =>
            db != null && db.Item(item)?.TypeId == 4 && !ModContent.IsMissingItem(item) &&
            ModCampaigns.ItemAllowed(db, item) && ModUnlocks.ItemAvailable(item) &&
            ModContent.ItemAvailableFor(item, "shops") && !Shop.NotInSession(db, item);
    }
}
