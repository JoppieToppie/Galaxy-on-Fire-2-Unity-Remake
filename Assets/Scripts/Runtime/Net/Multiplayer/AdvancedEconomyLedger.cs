// Host-owned inventory snapshot, independent of the vanilla shop list's lifetime.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Modding;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace GoF2Remake.Multiplayer
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class AdvancedEconomyLedger
    {
        public sealed class Balance
        {
            public int Amount { get; private set; }
            public int Resale { get; private set; }
            public int ImportHops { get; internal set; }
            public Balance(int amount, int resale, int maximum) => Set(amount, resale, maximum);
            public bool Set(int amount, int resale, int maximum)
            {
                amount = Math.Clamp(amount, 0, maximum);
                resale = Math.Clamp(resale, 0, amount);
                bool changed = amount != Amount || resale != Resale;
                Amount = amount;
                Resale = resale;
                return changed;
            }
        }

        // Stable mod keys, not allocated runtime indices; zero is a real saved balance.
        readonly struct Key : IEquatable<Key>
        {
            public readonly string Station, Item;
            public Key(string station, string item) { Station = station; Item = item; }
            public bool Equals(Key other) => Station == other.Station && Item == other.Item;
            public override bool Equals(object other) => other is Key key && Equals(key);
            public override int GetHashCode() => (Station, Item).GetHashCode();
        }

        static readonly Dictionary<Key, Balance> balances = new Dictionary<Key, Balance>();
        static readonly Dictionary<(int station, int item), Balance> live = new Dictionary<(int, int), Balance>();
        sealed class Shipment
        {
            public int Amount, Resale, Ticks, Hops;
        }
        static readonly Dictionary<string, Shipment> shipments = new Dictionary<string, Shipment>();
        static bool loaded, dirty, readFailed;
        static float nextSave;
        static string path;
        public static bool Active => ModAdvancedEconomy.Enabled && ModAdvancedEconomy.PersistentInventory &&
            NetGame.Active && !NetGame.CoopSession && !CoopCampaign.Active;

        internal static void Reset()
        {
            balances.Clear();
            live.Clear();
            shipments.Clear();
            loaded = dirty = readFailed = false;
            path = null;
            nextSave = 0;
        }

        static Key StableKey(int station, int item) => new Key(
            ModWorld.StationKey(station) ?? station.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ModContent.ItemKey(item) ?? item.ToString(System.Globalization.CultureInfo.InvariantCulture));

        static void Load()
        {
            if (loaded) return;
            loaded = true;
            if (!ModAdvancedEconomy.SaveInventory || !NetProfiles.Enabled) return;
            path = Path.Combine(NetProfiles.Folder, "advanced-economy-stock.json");
            if (!File.Exists(path) && !File.Exists(path + ".bak")) return;
            foreach (string candidate in new[] { path, path + ".bak" })
            {
                try
                {
                    if (!File.Exists(candidate)) continue;
                    string json = File.ReadAllText(candidate);
                    var parsed = Read(json);
                    var cargo = ReadShipments(JObject.Parse(json));
                    foreach (var pair in parsed) balances.Add(pair.Key, pair.Value);
                    foreach (var pair in cargo) shipments.Add(pair.Key, pair.Value);
                    if (candidate != path)
                    {
                        Debug.LogWarning("Advanced economy: recovered the backup snapshot.");
                        dirty = true;
                    }
                    return;
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException ||
                    e is JsonException || e is ArgumentException || e is OverflowException)
                {
                    Debug.LogError($"Advanced economy: cannot read {candidate}: {e.Message}");
                }
            }
            readFailed = true;
            Debug.LogError("Advanced economy: snapshots are unreadable. Disk saving is suspended for this session to preserve recovery data.");
        }

        static Dictionary<Key, Balance> Read(string json)
        {
            var root = JObject.Parse(json);
            if (root["version"]?.Type != JTokenType.Integer || (long)root["version"] != 1 ||
                !(root["rows"] is JArray rows))
                throw new JsonException("Unsupported advanced economy snapshot.");
            var result = new Dictionary<Key, Balance>();
            foreach (var token in rows)
            {
                if (!(token is JObject row) || row["station"]?.Type != JTokenType.String ||
                    row["item"]?.Type != JTokenType.String || row["amount"]?.Type != JTokenType.Integer ||
                    row["resale"]?.Type != JTokenType.Integer)
                    throw new JsonException("Invalid advanced economy stock row.");
                string station = (string)row["station"], item = (string)row["item"];
                long amount = (long)row["amount"], resale = (long)row["resale"];
                if (string.IsNullOrEmpty(station) || string.IsNullOrEmpty(item) || amount < 0 ||
                    amount > ModAdvancedEconomy.MaxStock || resale < 0 || resale > amount)
                    throw new JsonException("Invalid advanced economy stock balance.");
                var key = new Key(station, item);
                if (result.ContainsKey(key)) throw new JsonException("Duplicate advanced economy stock row.");
                int hops = row["importHops"] == null ? 0 : (int)row["importHops"];
                if (hops < 0 || hops > 96) throw new JsonException("Invalid import distance.");
                result.Add(key, new Balance((int)amount, (int)resale, ModAdvancedEconomy.MaxStock) { ImportHops = hops });
            }
            return result;
        }

        static Dictionary<string, Shipment> ReadShipments(JObject root)
        {
            var result = new Dictionary<string, Shipment>();
            if (root["shipments"] == null) return result; // old version-1 snapshots
            if (!(root["shipments"] is JArray rows)) throw new JsonException("Invalid shipments.");
            foreach (var token in rows)
            {
                if (!(token is JObject row) || row["key"]?.Type != JTokenType.String ||
                    row["amount"]?.Type != JTokenType.Integer || row["resale"]?.Type != JTokenType.Integer ||
                    row["ticks"]?.Type != JTokenType.Integer)
                    throw new JsonException("Invalid shipment.");
                string key = (string)row["key"];
                long amount = (long)row["amount"], resale = (long)row["resale"], ticks = (long)row["ticks"];
                if (string.IsNullOrEmpty(key) || amount < 1 || amount > ModAdvancedEconomy.MaxStock ||
                    resale < 0 || resale > amount || ticks < 0 || ticks > 96 || result.ContainsKey(key))
                    throw new JsonException("Invalid shipment balance.");
                int hops = row["hops"] == null ? 0 : (int)row["hops"];
                if (hops < 0 || hops > 96) throw new JsonException("Invalid shipment distance.");
                result.Add(key, new Shipment { Amount = (int)amount, Resale = (int)resale, Ticks = (int)ticks, Hops = hops });
            }
            return result;
        }

        static string RouteKey(ModAdvancedEconomy.Route route)
        {
            var a = StableKey(route.Source, route.Item);
            var b = StableKey(route.Destination, route.Item);
            return a.Station + "|" + b.Station + "|" + a.Item;
        }

        // A route carries at most one finite shipment. Debits, cargo in transit and arrivals live in the same snapshot.
        // A disabled or removed route freezes its shipment; it never silently redirects cargo to another endpoint.
        internal static Dictionary<(int station, int item), int> TickTrade(Database db, Func<int, bool> participates, Func<int, int> pressure, HashSet<int> changed)
        {
            if (!ModAdvancedEconomy.RegionalTrade) return new Dictionary<(int station, int item), int>();
            Load();
            var graph = AdvancedEconomyRoutes.Build(db);
            var routes = ModAdvancedEconomy.Routes.ToList();
            var keys = new HashSet<string>(routes.Select(RouteKey));
            // Restore previously dispatched automatic routes even if they no longer have
            // a surplus. A disconnected route keeps its cargo until a path reopens.
            if (ModAdvancedEconomy.AutomaticRoutes)
            {
                foreach (string key in shipments.Keys)
                {
                    var parts = key.Split('|');
                    if (parts.Length != 3 || !ModWorld.TryResolveStation(parts[0], out int source) ||
                        !ModWorld.TryResolveStation(parts[1], out int destination) ||
                        !ModContent.TryResolveItem(parts[2], out int item)) continue;
                    var route = new ModAdvancedEconomy.Route(source, destination, item,
                        ModAdvancedEconomy.AutoCapacity, 1, ModAdvancedEconomy.AutoWarLoss);
                    if (keys.Add(key)) routes.Add(route);
                }
                // Discover destinations with deficits and sources with surpluses, grouped
                // by commodity. Only one automatically selected supplier per deficit per tick.
                var suppliers = new Dictionary<int, List<int>>();
                var deficits = new List<(int station, int item)>();
                foreach (int station in ModAdvancedEconomy.Stations.OrderBy(s => s))
                {
                    if (!participates(station)) continue;
                    foreach (var rule in ModAdvancedEconomy.At(station).Values.OrderBy(r => r.Item))
                    {
                        if (!ModAdvancedEconomy.Eligible(db, rule.Item)) continue;
                        int amount = Get(station, rule).Amount;
                        if (amount > rule.Target)
                        {
                            if (!suppliers.TryGetValue(rule.Item, out var sources))
                                suppliers[rule.Item] = sources = new List<int>();
                            sources.Add(station);
                        }
                        else if (amount < rule.Target) deficits.Add((station, rule.Item));
                    }
                }
                foreach (var deficit in deficits)
                {
                    if (!suppliers.TryGetValue(deficit.item, out var sources)) continue;
                    int destinationSystem = Shop.SystemOf(db, deficit.station);
                    int best = -1, bestHops = int.MaxValue;
                    foreach (int source in sources)
                    {
                        var path = graph.Path(Shop.SystemOf(db, source), destinationSystem);
                        if (path == null || path.Count - 1 > ModAdvancedEconomy.MaxAutoHops || path.Count - 1 >= bestHops) continue;
                        best = source; bestHops = path.Count - 1;
                    }
                    if (best < 0) continue;
                    var route = new ModAdvancedEconomy.Route(best, deficit.station, deficit.item,
                        ModAdvancedEconomy.AutoCapacity, 1, ModAdvancedEconomy.AutoWarLoss);
                    if (keys.Add(RouteKey(route))) routes.Add(route);
                }
            }
            var incoming = new Dictionary<(int station, int item), int>();
            foreach (var route in routes)
            {
                if (!participates(route.Source) || !participates(route.Destination)) continue;
                if (!shipments.TryGetValue(RouteKey(route), out var shipment)) continue;
                var key = (route.Destination, route.Item);
                incoming.TryGetValue(key, out int total);
                incoming[key] = (int)Math.Min(ModAdvancedEconomy.MaxStock, (long)total + shipment.Amount);
            }
            var systemPressure = new Dictionary<int, int>();
            foreach (var route in routes)
            {
                if (!participates(route.Source) || !participates(route.Destination) ||
                    !ModAdvancedEconomy.Eligible(db, route.Item)) continue;
                var from = ModAdvancedEconomy.At(route.Source);
                var to = ModAdvancedEconomy.At(route.Destination);
                if (from == null || to == null || !from.TryGetValue(route.Item, out var sourceRule) ||
                    !to.TryGetValue(route.Item, out var destinationRule)) continue;
                var path = graph.Path(Shop.SystemOf(db, route.Source), Shop.SystemOf(db, route.Destination));
                if (path == null) continue;
                int routePressure = Math.Max(pressure(route.Source), pressure(route.Destination));
                foreach (int system in path)
                {
                    if (!systemPressure.TryGetValue(system, out int p))
                        systemPressure[system] = p = ModAdvancedEconomy.SystemPressure(db, system);
                    routePressure = Math.Max(routePressure, p);
                }
                var source = Get(route.Source, sourceRule);
                var destination = Get(route.Destination, destinationRule);
                string key = RouteKey(route);
                var incomingKey = (route.Destination, route.Item);
                incoming.TryGetValue(incomingKey, out int reserved);
                if (shipments.TryGetValue(key, out var shipment))
                {
                    if (shipment.Ticks > 0) { shipment.Ticks--; dirty = true; }
                    if (shipment.Ticks > 0) continue;
                    int delivered = Math.Min(shipment.Amount, destinationRule.Maximum - destination.Amount);
                    if (delivered > 0)
                    {
                        int resale = Math.Min(delivered, shipment.Resale);
                        int hops = (int)Math.Round(((long)destination.Amount * destination.ImportHops +
                            (long)delivered * shipment.Hops) / (double)(destination.Amount + delivered));
                        Set(route.Destination, destinationRule, destination.Amount + delivered, destination.Resale + resale);
                        destination.ImportHops = hops;
                        shipment.Amount -= delivered;
                        shipment.Resale -= resale;
                        incoming[incomingKey] = Math.Max(0, reserved - delivered);
                        changed.Add(route.Destination);
                        dirty = true;
                    }
                    if (shipment.Amount == 0) { shipments.Remove(key); dirty = true; }
                    // Full destination holds the undelivered cargo in transit; no goods are duplicated or discarded.
                    continue;
                }
                int send = ModAdvancedEconomy.DispatchAmount(source.Amount, sourceRule.Target,
                    destination.Amount + reserved, destinationRule.Target, route.Capacity);
                if (send <= 0) continue;
                int resaleSent = Math.Min(send, source.Resale);
                int survive = ModAdvancedEconomy.SurvivingCargo(send, route.WarLossPercent,
                    routePressure);
                Set(route.Source, sourceRule, source.Amount - send, source.Resale - resaleSent);
                changed.Add(route.Source);
                if (survive > 0)
                {
                    int resaleSurvives = Math.Min(resaleSent, survive);
                    shipments[key] = new Shipment { Amount = survive, Resale = resaleSurvives, Ticks = Math.Min(96, Math.Max(route.TravelTicks, Math.Max(1, path.Count - 1) * ModAdvancedEconomy.TicksPerHop)), Hops = Math.Min(96, path.Count - 1) };
                    incoming[incomingKey] = reserved + survive;
                    dirty = true;
                }
            }
            return incoming;
        }

        internal static Balance Get(int station, ModAdvancedEconomy.Rule rule)
        {
            Load();
            var runtimeKey = (station, rule.Item);
            if (live.TryGetValue(runtimeKey, out var balance)) return balance;
            var stable = StableKey(station, rule.Item);
            if (!balances.TryGetValue(stable, out balance))
            {
                balance = new Balance(rule.Initial, 0, rule.Maximum);
                balances.Add(stable, balance);
                dirty = true;
            }
            else if (balance.Set(balance.Amount, balance.Resale, rule.Maximum)) dirty = true;
            live[runtimeKey] = balance;
            return balance;
        }

        internal static void Set(int station, ModAdvancedEconomy.Rule rule, int amount, int resale)
        {
            if (Get(station, rule).Set(amount, resale, rule.Maximum)) dirty = true;
        }

        internal static bool CanJournal
        {
            get { Load(); return path != null && !readFailed; }
        }

        internal static string Snapshot()
        {
            var rows = new JArray();
            foreach (var pair in balances)
                rows.Add(new JObject { ["station"] = pair.Key.Station, ["item"] = pair.Key.Item,
                    ["amount"] = pair.Value.Amount, ["resale"] = pair.Value.Resale, ["importHops"] = pair.Value.ImportHops });
            var cargo = new JArray();
            foreach (var pair in shipments)
                cargo.Add(new JObject { ["key"] = pair.Key, ["amount"] = pair.Value.Amount,
                    ["resale"] = pair.Value.Resale, ["ticks"] = pair.Value.Ticks, ["hops"] = pair.Value.Hops });
            var root = new JObject { ["version"] = 1, ["rows"] = rows, ["shipments"] = cargo };
            return root.ToString(Formatting.None);
        }

        // The station/profile write-ahead journal includes the entire economy snapshot.
        // Recovery uses absolute targets and therefore cannot debit or credit stock twice.
        internal static bool PersistJournalSnapshot(string json, bool recover)
        {
            if (string.IsNullOrEmpty(NetProfiles.Folder)) return false;
            var parsed = Read(json);
            var cargo = ReadShipments(JObject.Parse(json));
            string target = Path.Combine(NetProfiles.Folder, "advanced-economy-stock.json");
            if (!NetProfiles.Write(target, json)) return false;
            if (recover && loaded)
            {
                balances.Clear(); live.Clear(); shipments.Clear();
                foreach (var pair in parsed) balances.Add(pair.Key, pair.Value);
                foreach (var pair in cargo) shipments.Add(pair.Key, pair.Value);
            }
            dirty = false;
            return true;
        }

        internal static void Flush(bool force = false)
        {
            if (!loaded || !dirty || readFailed || path == null || NetPlayerStations.ProfileWritesBlocked ||
                (!force && (!Active || !ModAdvancedEconomy.SaveInventory || Time.unscaledTime < nextSave))) return;
            nextSave = Time.unscaledTime + 10f;
            if (NetProfiles.Write(path, Snapshot())) dirty = false;
        }
    }
}
