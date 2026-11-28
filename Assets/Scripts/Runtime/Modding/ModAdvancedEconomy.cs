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
        public static bool PhysicalFreight { get; private set; }
        public static int RhinoCapacity { get; private set; } = 2500;
        public static int CormorantCapacity { get; private set; } = 400;
        public static int FreightPerOrbit { get; private set; } = 4;
        public static int FreightSalvagePercent { get; private set; } = 25;
        public static int RhinoFreightSpeedPercent { get; private set; } = 75;
        public static int CormorantFreightSpeedPercent { get; private set; } = 125;
        public const int MaxFreightSeconds = 86400;
        public static int FreightDispatchSeconds { get; private set; } = 60;
        public const int DefaultFreightMinimumShipment = 50, DefaultFreightMinimumUtilization = 25, DefaultFreightCriticalStock = 15;
        public static int FreightMinimumShipment { get; private set; } = DefaultFreightMinimumShipment;
        public static int FreightMinimumUtilization { get; private set; } = DefaultFreightMinimumUtilization;
        public static bool FreightEmergencyDispatch { get; private set; } = true;
        public static bool FreightMixedShipments { get; private set; } = true;
        public const int MaxFreightCargoKinds = 16;
        public static int FreightCriticalStock { get; private set; } = DefaultFreightCriticalStock;
        public static bool FreightPirateRaids { get; private set; } = true;
        public static int FreightPirateAttemptPercent { get; private set; } = 80;
        public static int FreightPirateTheftPercent { get; private set; } = 40;
        public static int FreightPirateDestructionPercent { get; private set; } = 30;
        public static int FreightPirateKillRemainingPercent { get; private set; } = 25;
        public static bool FreightDistressCalls { get; private set; } = true;
        const string DefaultDistress = "Mayday! Pirates attacking us near {orbit}. Carrying {amount} t of {commodity} to {destination}. {impact}{rewardText}";
        static string distressTitle;
        static readonly Dictionary<int, string> factionDistressTitles = new Dictionary<int, string>();

        static string ReadDistressTitle(JToken token)
        {
            if (token.Type != JTokenType.String || string.IsNullOrWhiteSpace((string)token))
                throw new ModJsonException(File + ": distress title must be a nonblank string");
            string title = ((string)token).Trim();
            if (title.Length > 64 || title.IndexOfAny(new[] { '\r', '\n', '<', '>' }) >= 0)
                throw new ModJsonException(File + ": distress title must be at most 64 characters, single-line and plain text");
            return title;
        }

        public static string SelectDistressTitle(string defaultTitle, string factionTitle)
            => string.IsNullOrEmpty(factionTitle) ? defaultTitle ?? "" : factionTitle;

        // Empty means preserve the original channel-specific labels.
        public static string FreightDistressTitle(int race)
        {
            factionDistressTitles.TryGetValue(race, out string title);
            return SelectDistressTitle(distressTitle, title);
        }

        static string[] distressMessages = new[] { DefaultDistress };
        static readonly Dictionary<int, string[]> factionDistress = new Dictionary<int, string[]>();
        static readonly System.Text.RegularExpressions.Regex distressToken =
            new System.Text.RegularExpressions.Regex(@"\{([A-Za-z]+)\}");
        static readonly HashSet<string> distressFields = new HashSet<string>(StringComparer.Ordinal)
        { "faction", "orbit", "system", "origin", "destination", "commodity", "amount", "shortageWithout", "shortageWith", "impact", "reward", "rewardMode", "rewardText" };

        static string[] ReadDistressMessages(JToken token)
        {
            if (!(token is JArray list) || list.Count == 0 || list.Count > 32)
                throw new ModJsonException(File + ": distress messages require 1–32 strings");
            var result = new List<string>();
            foreach (var entry in list)
            {
                if (entry.Type != JTokenType.String || string.IsNullOrWhiteSpace((string)entry) || ((string)entry).Length > 512)
                    throw new ModJsonException(File + ": distress message must contain 1–512 characters");
                string value = (string)entry;
                foreach (System.Text.RegularExpressions.Match match in distressToken.Matches(value))
                    if (!distressFields.Contains(match.Groups[1].Value))
                        throw new ModJsonException(File + ": unknown distress field " + match.Value);
                if (distressToken.Replace(value, "").IndexOfAny(new[] { '{', '}' }) >= 0)
                    throw new ModJsonException(File + ": malformed distress field");
                result.Add(value);
            }
            return result.ToArray();
        }

        public static string SelectDistressMessage(string[] defaults, string[] faction, int raidTag)
        {
            var choices = faction != null && faction.Length > 0 ? faction :
                defaults != null && defaults.Length > 0 ? defaults : new[] { DefaultDistress };
            return choices[(int)((uint)raidTag / 4u % (uint)choices.Length)];
        }

        public static string FormatDistressMessage(string template, IReadOnlyDictionary<string, string> fields)
            => distressToken.Replace(template, match => fields.TryGetValue(match.Groups[1].Value, out string value) ? value : match.Value);

        public static string FreightDistressMessage(int race, int raidTag, IReadOnlyDictionary<string, string> fields)
        {
            factionDistress.TryGetValue(race, out var choices);
            return FormatDistressMessage(SelectDistressMessage(distressMessages, choices, raidTag), fields);
        }

        public static int FreightRescueReward { get; private set; } = 20000;
        public static string FreightRescueRewardMode { get; private set; } = "split";
        public static string NormalizeFreightRewardMode(string mode) => mode == "flat" || mode == "single" || mode == "weighted" ? mode : "split";
        // Dedicated raid identity range, separate from cargo, pirate-sighting and war tags.
        public const int FreightRaidTagStart = 200000000, FreightRaidTagEnd = 300000000, FreightRaidSize = 3;
        public static bool IsFreightRaidTag(int tag) => tag >= FreightRaidTagStart && tag < FreightRaidTagEnd && tag % 4 < FreightRaidSize;
        public static int FreightRaidBase(int tag) => tag - tag % 4;
        public static int FreightRaidOutcome(int roll, int theft, int destruction, int remainingPercent, int kills)
        {
            theft = Math.Clamp(theft, 0, 100);
            destruction = Math.Clamp(destruction, 0, 100 - theft);
            if (kills >= FreightRaidSize) return 0;
            double factor = Math.Pow(Math.Clamp(remainingPercent, 0, 100) / 100.0, Math.Max(0, kills));
            double chance = Math.Clamp(roll, 0, 9999) / 100.0;
            return chance < destruction * factor ? 2 : chance < (destruction + theft) * factor ? 1 : 0;
        }
        public static int FreightTravelMinSeconds { get; private set; } = 30;
        public static int FreightTravelMaxSeconds { get; private set; } = 90;
        public static int FreightUnloadMinSeconds { get; private set; } = 15;
        public static int FreightUnloadMaxSeconds { get; private set; } = 30;
        public static int FreightWreckLifetimeSeconds { get; private set; } = 300;

        public static (int min, int max) FreightInterval(int minimum, int maximum)
        {
            int min = Math.Clamp(minimum, 1, MaxFreightSeconds);
            return (min, Math.Clamp(maximum, min, MaxFreightSeconds));
        }
        public static int NativeFreighterCapacity { get; private set; } = 2500;
        public static int NativeFreighterSpeedPercent { get; private set; } = 100;
        public sealed class FreightHull
        {
            public readonly int Ship, Capacity, Weight, SpeedPercent;
            public readonly string Reference;
            public FreightHull(int ship, string reference, int capacity, int weight, int speedPercent)
            {
                Ship = ship; Reference = reference;
                Capacity = Math.Clamp(capacity, 1, MaxStock);
                Weight = Math.Clamp(weight, 1, 1000);
                SpeedPercent = Math.Clamp(speedPercent, 10, 500);
            }
        }

        static readonly Dictionary<int, List<FreightHull>> freightFleets = new Dictionary<int, List<FreightHull>>();
        public static int NativeFreighter(int race) => ModRaces.Look(race) == 1 ? 13 : 15;
        public static IReadOnlyList<FreightHull> FreightFleet(int race)
        {
            if (freightFleets.TryGetValue(race, out var fleet)) return fleet;
            var native = new FreightHull(NativeFreighter(race), "native", NativeFreighterCapacity, 1, NativeFreighterSpeedPercent);
            var defaults = new List<FreightHull> { native };
            if (race == 0)
            {
                if (CormorantShip >= 0) defaults.Add(new FreightHull(CormorantShip, ModContent.FreightShipKey(CormorantShip), CormorantCapacity, 1, CormorantFreightSpeedPercent));
                if (RhinoShip >= 0) defaults.Add(new FreightHull(RhinoShip, ModContent.FreightShipKey(RhinoShip), RhinoCapacity, 1, RhinoFreightSpeedPercent));
            }
            return defaults;
        }

        // Prefer a hull able to carry the requested load; otherwise use the largest available
        // capacity. Weights are relative among eligible ships, not across ineligible hulls.
        public static FreightHull PickFreightHull(IReadOnlyList<FreightHull> fleet, int requested, int roll)
        {
            int largest = 0;
            foreach (var h in fleet) largest = Math.Max(largest, h.Capacity);
            int needed = Math.Min(Math.Max(1, requested), largest);
            int total = 0;
            foreach (var h in fleet) if (h.Capacity >= needed) total += h.Weight;
            if (total == 0) return null;
            int choice = (int)((uint)roll % (uint)total);
            foreach (var h in fleet)
                if (h.Capacity >= needed && (choice -= h.Weight) < 0) return h;
            return null;
        }

        // Cap the threshold to what the route, destination target and hull can ever carry.
        // Otherwise a small commodity target or route cap could permanently block deliveries.
        public static int FreightDispatchMinimum(int hullCapacity, int routeCapacity, int destinationTarget,
            int minimumShipment, int utilizationPercent)
        {
            int possible = Math.Max(0, Math.Min(hullCapacity, Math.Min(routeCapacity, destinationTarget)));
            int utilization = (int)(((long)Math.Max(0, hullCapacity) * Math.Clamp(utilizationPercent, 0, 100) + 99) / 100);
            return Math.Min(possible, Math.Max(1, Math.Max(minimumShipment, utilization)));
        }

        public static bool FreightStockCritical(int stock, int incoming, int target, int criticalPercent) =>
            target > 0 && ((long)Math.Max(0, stock) + Math.Max(0, incoming)) * 100 <=
                (long)target * Math.Clamp(criticalPercent, 0, 100);

        // Apply the threshold to each hull's actual load, then use the existing weighted
        // selection among qualifying hulls. Only bypass thresholds for a genuine emergency.
        public static FreightHull PickDispatchHull(IReadOnlyList<FreightHull> fleet, int requested, int routeCapacity,
            int destinationTarget, int minimumShipment, int utilizationPercent, bool emergency, int roll)
        {
            if (fleet == null || requested <= 0 || routeCapacity <= 0 || destinationTarget <= 0) return null;
            requested = Math.Min(requested, Math.Min(routeCapacity, destinationTarget));
            var eligible = new List<FreightHull>();
            foreach (var hull in fleet)
                if (Math.Min(requested, hull.Capacity) >= FreightDispatchMinimum(hull.Capacity, routeCapacity,
                    destinationTarget, minimumShipment, utilizationPercent))
                    eligible.Add(hull);
            if (eligible.Count > 0) return PickFreightHull(eligible, requested, roll);
            if (!emergency || fleet.Count == 0) return null;

            // For undersized emergencies choose the smallest hull that fits.
            int smallestFit = int.MaxValue;
            foreach (var hull in fleet)
                if (hull.Capacity >= requested) smallestFit = Math.Min(smallestFit, hull.Capacity);
            if (smallestFit == int.MaxValue) return PickFreightHull(fleet, requested, roll);
            foreach (var hull in fleet)
                if (hull.Capacity == smallestFit) eligible.Add(hull);
            return PickFreightHull(eligible, requested, roll);
        }

        public static int RhinoShip { get; private set; } = 51;
        public static int CormorantShip { get; private set; } = 36;
        public static int FreightCapacity(int requested, int large, int small) =>
            Math.Min(Math.Max(0, requested), Math.Max(large, small));
        public const int FreightTagStart = 100000000, FreightTagEnd = 200000000;
        public static bool IsFreightTag(int tag) => tag >= FreightTagStart && tag < FreightTagEnd;
        public static int MigrateFreightTag(int tag) => tag >= 800000000 && tag < 900000000 ? tag - 700000000 : tag;
        public static int FreightSalvageAmount(int cargo, int percent) => (int)((long)Math.Max(0, cargo) * Math.Clamp(percent, 0, 100) / 100);
        public static int FreightClaimAmount(int cargo, int room, int requested) => Math.Max(0, Math.Min(cargo, Math.Min(room, requested)));
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
        public static bool RestrictedGateRouting { get; private set; }
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
            routes.Clear(); freightFleets.Clear();
            NativeFreighterCapacity = 2500; NativeFreighterSpeedPercent = 100;
            RegionalTrade = WarPressure = TradeDisruption = PlayerGateRouting = RestrictedGateRouting = AutomaticRoutes = false;
            FrontlinePressure = 50; BattlePressure = 100;
            DynamicPricing = TransportCosts = PlayerMarketTrade = NpcConstructionSupply = false;
            NpcConstructionUnits = 100;
            PlayerMarketBudget = 0; PlayerMarketUnits = 100;
            ScarcityMarkup = 100; SurplusDiscount = 30; WarMarkup = 50; TransportPerHop = 5; SellSpread = 10;
            AutoCapacity = 100; AutoWarLoss = 0; TicksPerHop = 1; MaxAutoHops = 8;
            PhysicalFreight = false; RhinoCapacity = 2500; CormorantCapacity = 400;
            FreightPerOrbit = 4; FreightSalvagePercent = 25; RhinoShip = 51; CormorantShip = 36;
            RhinoFreightSpeedPercent = 75; CormorantFreightSpeedPercent = 125;
            FreightPirateRaids = true; FreightPirateAttemptPercent = 80; FreightPirateTheftPercent = 40;
            FreightPirateDestructionPercent = 30; FreightPirateKillRemainingPercent = 25;
            distressMessages = new[] { DefaultDistress }; factionDistress.Clear();
            distressTitle = null; factionDistressTitles.Clear();
            FreightDistressCalls = true; FreightRescueReward = 20000; FreightRescueRewardMode = "split";
            FreightDispatchSeconds = 60; FreightTravelMinSeconds = 30; FreightTravelMaxSeconds = 90;
            FreightMinimumShipment = DefaultFreightMinimumShipment; FreightMinimumUtilization = DefaultFreightMinimumUtilization;
            FreightEmergencyDispatch = true; FreightCriticalStock = DefaultFreightCriticalStock;
            FreightMixedShipments = true;
            FreightUnloadMinSeconds = 15; FreightUnloadMaxSeconds = 30; FreightWreckLifetimeSeconds = 300;
            foreach (var mod in ModManager.Active)
            {
                if (mod == null || mod.IsBase) continue;
                try
                {
                    var token = ModJson.Read(mod.Source, File);
                    if (token == null) continue;
                    if (!(token is JObject o)) throw new ModJsonException(File + ": expected an object");
                    ApplyConfiguration(db, mod, o, false);
                }
                catch (ModJsonException e) { Debug.LogWarning($"[{mod.Id}] {e.Message}"); }
            }
            foreach (var route in routes.Values)
                if (At(route.Source)?.ContainsKey(route.Item) != true || At(route.Destination)?.ContainsKey(route.Item) != true)
                    Debug.LogWarning($"{File}: route {route.Source} -> {route.Destination}, item {route.Item}, requires managed stock at both endpoints; ignored until configured.");
        }

        // The exact same parser is used by the builder and runtime; validation never publishes settings.
        public static void ValidateConfiguration(Database db, ModInfo mod, JObject rules)
        {
            if (!ModJson.Bool(rules, "enabled", false, File)) return;
            if (db == null || mod == null) throw new ModJsonException(File + ": the builder database is not ready");
            ApplyConfiguration(db, mod, rules, true);
        }

        static void ApplyConfiguration(Database db, ModInfo mod, JObject o, bool validateOnly)
        {
            // A disabled config cannot contribute any settings or rows.
            if (!ModJson.Bool(o, "enabled", false, File)) return;
            bool inventory = ModJson.Bool(o, "persistentInventory", false, File);
            bool save = ModJson.Bool(o, "saveInventory", false, File);
            bool unvisited = ModJson.Bool(o, "simulateUnvisitedStations", false, File);
            bool production = ModJson.Bool(o, "stationProduction", false, File);
            bool consumption = ModJson.Bool(o, "stationConsumption", false, File);
            bool trade = ModJson.Bool(o, "regionalTrade", false, File);
            bool physical = ModJson.Bool(o, "physicalFreight", false, File);
            int smallFreight = Math.Clamp(ModJson.Int(o, "cormorantCapacity", 400, File), 1, MaxStock);
            int largeFreight = Math.Clamp(ModJson.Int(o, "rhinoCapacity", 2500, File), smallFreight, MaxStock);
            int freightLimit = Math.Clamp(ModJson.Int(o, "freightPerOrbit", 4, File), 1, 16);
            int freightSalvage = Math.Clamp(ModJson.Int(o, "freightSalvagePercent", 25, File), 0, 100);
            int rhinoSpeed = Math.Clamp(ModJson.Int(o, "rhinoFreightSpeedPercent", 75, File), 10, 500);
            int cormorantSpeed = Math.Clamp(ModJson.Int(o, "cormorantFreightSpeedPercent", 125, File), 10, 500);
            bool pirateRaids = ModJson.Bool(o, "freightPirateRaids", true, File);
            int raidAttempt = Math.Clamp(ModJson.Int(o, "freightPirateAttemptPercent", 80, File), 0, 100);
            int raidTheft = Math.Clamp(ModJson.Int(o, "freightPirateTheftPercent", 40, File), 0, 100);
            int raidDestroy = Math.Clamp(ModJson.Int(o, "freightPirateDestructionPercent", 30, File), 0, 100 - raidTheft);
            bool distress = ModJson.Bool(o, "freightDistressCalls", true, File);
            string parsedTitle = o["freightDistressTitle"] == null ? null : ReadDistressTitle(o["freightDistressTitle"]);
            var parsedFactionTitles = new Dictionary<int, string>();
            if (o["freightDistressFactionTitles"] != null)
            {
                if (!(o["freightDistressFactionTitles"] is JObject titles))
                    throw new ModJsonException(File + ": freightDistressFactionTitles must be an object");
                foreach (var property in titles.Properties())
                {
                    if (!ModRaces.TryResolve(property.Name, mod, out int race) || parsedFactionTitles.ContainsKey(race))
                        throw new ModJsonException(File + ": unknown or duplicate freight distress title faction " + property.Name);
                    parsedFactionTitles.Add(race, ReadDistressTitle(property.Value));
                }
            }
            var parsedDistress = o["freightDistressMessages"] == null ? null : ReadDistressMessages(o["freightDistressMessages"]);
            var parsedFactionDistress = new Dictionary<int, string[]>();
            if (o["freightDistressFactionMessages"] != null)
            {
                if (!(o["freightDistressFactionMessages"] is JObject factionMessages))
                    throw new ModJsonException(File + ": freightDistressFactionMessages must be an object");
                foreach (var property in factionMessages.Properties())
                {
                    if (!ModRaces.TryResolve(property.Name, mod, out int race) || parsedFactionDistress.ContainsKey(race))
                        throw new ModJsonException(File + ": unknown or duplicate freight distress faction " + property.Name);
                    parsedFactionDistress.Add(race, ReadDistressMessages(property.Value));
                }
            }
            int rescueReward = Math.Clamp(ModJson.Int(o, "freightRescueReward", 20000, File), 0, 1000000000);
            string rewardMode = NormalizeFreightRewardMode(ModJson.Str(o, "freightRescueRewardMode"));
            int raidKill = Math.Clamp(ModJson.Int(o, "freightPirateKillRemainingPercent", 25, File), 0, 100);
            int dispatchSeconds = Math.Clamp(ModJson.Int(o, "freightDispatchSeconds", 60, File), 1, MaxFreightSeconds);
            int minimumShipment = Math.Clamp(ModJson.Int(o, "freightMinimumShipment", DefaultFreightMinimumShipment, File), 1, MaxStock);
            int minimumUtilization = Math.Clamp(ModJson.Int(o, "freightMinimumUtilizationPercent", DefaultFreightMinimumUtilization, File), 0, 100);
            bool emergencyDispatch = ModJson.Bool(o, "freightEmergencyDispatch", true, File);
            bool mixedShipments = ModJson.Bool(o, "freightMixedShipments", true, File);
            int criticalStock = Math.Clamp(ModJson.Int(o, "freightCriticalStockPercent", DefaultFreightCriticalStock, File), 0, 100);
            var travelSeconds = FreightInterval(ModJson.Int(o, "freightTravelMinSeconds", 30, File),
                ModJson.Int(o, "freightTravelMaxSeconds", 90, File));
            var unloadSeconds = FreightInterval(ModJson.Int(o, "freightUnloadMinSeconds", 15, File),
                ModJson.Int(o, "freightUnloadMaxSeconds", 30, File));
            int wreckSeconds = Math.Clamp(ModJson.Int(o, "freightWreckLifetimeSeconds", 300, File), 1, MaxFreightSeconds);
            int ResolveFreighter(string key, int fallback)
            {
                string value = ModJson.Str(o, key);
                if (string.IsNullOrEmpty(value)) return fallback;
                if (value == "-1") return -1;
                if ((!ModContent.TryResolveShip(value, out int ship) && !ModContent.TryResolveShip(mod.Id + ":" + value, out ship)) || db.Ship(ship) == null)
                    throw new ModJsonException(File + ": unknown freight hull " + value);
                return ship;
            }
            int rhinoShip = physical ? ResolveFreighter("rhinoShip", 51) : 51;
            int cormorantShip = physical ? ResolveFreighter("cormorantShip", 36) : 36;
            int nativeCapacity = Math.Clamp(ModJson.Int(o, "nativeFreighterCapacity", largeFreight, File), 1, MaxStock);
            int nativeSpeed = Math.Clamp(ModJson.Int(o, "nativeFreighterSpeedPercent", 100, File), 10, 500);
            var parsedFleets = new Dictionary<int, List<FreightHull>>();
            if (physical && ModJson.Get(o, "freightFleets") is JToken fleetToken)
            {
                if (!(fleetToken is JArray fleetRows) || fleetRows.Count > 1024)
                    throw new ModJsonException(File + ": freightFleets must be an array of at most 1024 rows");
                foreach (var entry in fleetRows)
                {
                    if (!(entry is JObject row)) throw new ModJsonException(File + ": freight fleet row must be an object");
                    string raceRef = ModJson.Str(row, "race"), shipRef = ModJson.Str(row, "ship");
                    if (!ModRaces.TryResolve(raceRef, mod, out int race))
                        throw new ModJsonException(File + ": unknown freight race " + raceRef);
                    int ship;
                    if (string.Equals(shipRef, "native", StringComparison.OrdinalIgnoreCase))
                    { ship = NativeFreighter(race); shipRef = "native"; }
                    else
                    {
                        if (string.IsNullOrEmpty(shipRef) ||
                            (!ModContent.TryResolveShip(shipRef, out ship) && !ModContent.TryResolveShip(mod.Id + ":" + shipRef, out ship)) ||
                            db.Ship(ship) == null)
                            throw new ModJsonException(File + ": unknown freight hull " + shipRef);
                        shipRef = ModContent.FreightShipKey(ship);
                    }
                    var hull = new FreightHull(ship, shipRef, ModJson.Int(row, "capacity", nativeCapacity, File),
                        ModJson.Int(row, "weight", 1, File), ModJson.Int(row, "speedPercent", 100, File));
                    if (!parsedFleets.TryGetValue(race, out var fleet)) parsedFleets[race] = fleet = new List<FreightHull>();
                    if (fleet.Count >= 64) throw new ModJsonException(File + ": at most 64 freight hull entries per race");
                    fleet.Add(hull);
                }
            }
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
            bool restrictedGates = ModJson.Bool(o, "restrictedGateRouting", false, File);
            bool automatic = ModJson.Bool(o, "automaticRoutes", false, File);
            int frontline = Math.Clamp(ModJson.Int(o, "frontlinePressurePercent", 50, File), 0, 100);
            int battle = Math.Clamp(ModJson.Int(o, "battlePressurePercent", 100, File), 0, 100);
            int capacityAuto = Math.Clamp(ModJson.Int(o, "automaticRouteCapacity", physical ? largeFreight : 100, File), 1, MaxStock);
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
            if (validateOnly) return;
            Enabled = true;
            if (!inventory) return;
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
            if (physical && trade && save)
            {
                PhysicalFreight = true; RhinoCapacity = largeFreight; CormorantCapacity = smallFreight;
                FreightPerOrbit = freightLimit; FreightSalvagePercent = freightSalvage; RhinoShip = rhinoShip; CormorantShip = cormorantShip;
                RhinoFreightSpeedPercent = rhinoSpeed; CormorantFreightSpeedPercent = cormorantSpeed;
                FreightDispatchSeconds = dispatchSeconds;
                FreightMinimumShipment = minimumShipment; FreightMinimumUtilization = minimumUtilization;
                FreightEmergencyDispatch = emergencyDispatch; FreightCriticalStock = criticalStock;
                FreightMixedShipments = mixedShipments;
                FreightPirateRaids = pirateRaids; FreightPirateAttemptPercent = raidAttempt;
                FreightPirateTheftPercent = raidTheft; FreightPirateDestructionPercent = raidDestroy;
                FreightPirateKillRemainingPercent = raidKill;
                if (parsedTitle != null) distressTitle = parsedTitle;
                foreach (var pair in parsedFactionTitles) factionDistressTitles[pair.Key] = pair.Value;
                if (parsedDistress != null) distressMessages = parsedDistress;
                foreach (var pair in parsedFactionDistress) factionDistress[pair.Key] = pair.Value;
                FreightDistressCalls = distress; FreightRescueReward = rescueReward; FreightRescueRewardMode = rewardMode;
                FreightTravelMinSeconds = travelSeconds.min; FreightTravelMaxSeconds = travelSeconds.max;
                FreightUnloadMinSeconds = unloadSeconds.min; FreightUnloadMaxSeconds = unloadSeconds.max;
                FreightWreckLifetimeSeconds = wreckSeconds;
                NativeFreighterCapacity = nativeCapacity; NativeFreighterSpeedPercent = nativeSpeed;
                foreach (var fleet in parsedFleets) freightFleets[fleet.Key] = fleet.Value;
            }
            else if (physical) Debug.LogWarning($"[{mod.Id}] {File}: physical freight requires regional trade and saved inventory; disabled.");
            WarPressure |= war;
            TradeDisruption |= disruption;
            if (war) { FrontlinePressure = frontline; BattlePressure = battle; }
            if (trade)
            {
                PlayerGateRouting |= playerGates;
                RestrictedGateRouting |= playerGates && restrictedGates;
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
