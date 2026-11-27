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
            // The tag is a server-issued identity, never a cargo quantity supplied by an orbit client.
            public int FreightTag, Orbit = -1, TotalTicks;
            public bool Bulk;
            public int Hull = 65535;
        }
        static readonly Dictionary<string, Shipment> shipments = new Dictionary<string, Shipment>();
        sealed class FreightWreck
        {
            public int Orbit, Amount, Race;
            public string Item;
            public Vector3 Position;
            public long Expires;
        }
        static readonly Dictionary<int, FreightWreck> wrecks = new Dictionary<int, FreightWreck>();
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
            wrecks.Clear();
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
                    var debris = ReadWrecks(JObject.Parse(json));
                    foreach (var pair in parsed) balances.Add(pair.Key, pair.Value);
                    foreach (var pair in cargo) shipments.Add(pair.Key, pair.Value);
                    foreach (var pair in debris) wrecks.Add(pair.Key, pair.Value);
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
                int tag = row["freightTag"] == null ? 0 : (int)row["freightTag"];
                int orbit = row["orbit"] == null ? -1 : (int)row["orbit"];
                int totalTicks = row["totalTicks"] == null ? 0 : (int)row["totalTicks"];
                if ((tag != 0 && (!ModAdvancedEconomy.IsFreightTag(tag) || result.Values.Any(s => s.FreightTag == tag))) ||
                    orbit < -1 || totalTicks < 0 || totalTicks > 96 || (totalTicks != 0 && totalTicks < ticks))
                    throw new JsonException("Invalid physical freight identity or progress.");
                result.Add(key, new Shipment { Amount = (int)amount, Resale = (int)resale, Ticks = (int)ticks, Hops = hops,
                    FreightTag = tag, Orbit = orbit, TotalTicks = totalTicks,
                    Bulk = row["bulk"] == null ? amount > ModAdvancedEconomy.CormorantCapacity : (bool)row["bulk"],
                    Hull = row["hull"] == null ? 65535 : Math.Clamp((int)row["hull"], 1, 65535) });
            }
            return result;
        }

        static Dictionary<int, FreightWreck> ReadWrecks(JObject root)
        {
            var result = new Dictionary<int, FreightWreck>();
            if (root["freightWrecks"] == null) return result;
            if (!(root["freightWrecks"] is JArray rows)) throw new JsonException("Invalid freight wrecks.");
            foreach (var token in rows)
            {
                if (!(token is JObject row)) throw new JsonException("Invalid freight wreck.");
                int tag = (int)row["tag"], orbit = (int)row["orbit"], amount = (int)row["amount"], race = (int)row["race"];
                string item = (string)row["item"];
                long expiry = (long)row["expires"];
                var position = new Vector3((float)row["x"], (float)row["y"], (float)row["z"]);
                if (!ModAdvancedEconomy.IsFreightTag(tag) || orbit < 0 || amount <= 0 || amount > ModAdvancedEconomy.MaxStock ||
                    string.IsNullOrEmpty(item) || !NetGuard.Position(position) || expiry <= 0 || result.ContainsKey(tag))
                    throw new JsonException("Invalid freight wreck record.");
                result.Add(tag, new FreightWreck { Orbit = orbit, Amount = amount, Race = race, Item = item, Position = position, Expires = expiry });
            }
            return result;
        }

        static string RouteKey(ModAdvancedEconomy.Route route)
        {
            var a = StableKey(route.Source, route.Item);
            var b = StableKey(route.Destination, route.Item);
            return a.Station + "|" + b.Station + "|" + a.Item;
        }

        static bool Physical => Active && ModAdvancedEconomy.PhysicalFreight && NetProfiles.Enabled && CanJournal;

        static int NewFreightTag()
        {
            int tag;
            do { tag = UnityEngine.Random.Range(800000000, 900000000); }
            while (shipments.Values.Any(s => s.FreightTag == tag) || wrecks.ContainsKey(tag));
            return tag;
        }

        internal static int FreightLeg(int totalTicks, int ticks, int pathCount)
        {
            if (pathCount <= 1) return 0;
            return Math.Clamp((int)((long)Math.Max(0, totalTicks - ticks) * pathCount / Math.Max(1, totalTicks)), 0, pathCount - 1);
        }

        static void UpdateFreight(Database db, ModAdvancedEconomy.Route route, Shipment ship, IReadOnlyList<int> systems)
        {
            if (!Physical) return;
            if (ship.FreightTag == 0) { ship.FreightTag = NewFreightTag(); ship.Bulk = ship.Amount > ModAdvancedEconomy.CormorantCapacity; dirty = true; }
            if (ship.TotalTicks == 0) { ship.TotalTicks = Math.Max(1, ship.Ticks); dirty = true; }
            int leg = FreightLeg(ship.TotalTicks, ship.Ticks, systems.Count);
            int orbit = leg == 0 ? route.Source : leg == systems.Count - 1 ? route.Destination :
                (db.Systems.Find(s => s.index == systems[leg])?.jumpgateStation ?? -1);
            if (orbit < 0) orbit = db.Stations.Where(s => s.system == systems[leg] && !Shop.UsesPlayerMarketplace(s.index))
                .Select(s => s.index).DefaultIfEmpty(-1).Min();
            if (systems.Count == 1 && ship.Ticks < ship.TotalTicks) orbit = route.Destination;
            if (ship.Orbit != orbit) { ship.Orbit = orbit; dirty = true; }
        }

        // Only persisted, in-transit shipments are exposed; the amount is a manifest, not a grant.
        internal static string FreightManifest(int orbit)
        {
            if (!Physical || NetPlayerStations.ProfileWritesBlocked) return "";
            Flush(true);
            if (dirty) return "";
            var rows = new List<string>();
            foreach (var pair in shipments.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                var ship = pair.Value;
                if (ship.Orbit != orbit || ship.FreightTag == 0 || ship.Amount <= 0) continue;
                var keys = pair.Key.Split('|');
                if (keys.Length != 3 || !ModContent.TryResolveItem(keys[2], out int item) ||
                    !ModWorld.TryResolveStation(keys[0], out int source) || !ModWorld.TryResolveStation(keys[1], out int destination)) continue;
                rows.Add($"{ship.FreightTag},{item},{ship.Amount},{source},{destination},{(ship.Bulk ? 1 : 0)},{ship.Hull}");
                if (rows.Count >= ModAdvancedEconomy.FreightPerOrbit) break;
            }
            return string.Join(";", rows);
        }

        internal static void RecordFreightHull(int orbit, int tag, int hull)
        {
            if (!Physical || hull <= 0 || NetPlayerStations.ProfileWritesBlocked) return;
            var ship = shipments.Values.FirstOrDefault(s => s.FreightTag == tag && s.Orbit == orbit);
            if (ship == null) return;
            hull = Math.Clamp(hull, 1, 65535);
            if (ship.Hull == hull) return;
            ship.Hull = hull;
            dirty = true;
        }

        // A destroyed physical ship cannot later deliver. The absolute ledger is written before
        // accepting the loss; repeated reports and late reports from earlier orbits do nothing.
        internal static bool LoseFreight(int orbit, int tag, int race, Vector3 position)
        {
            if (!Physical || !ModAdvancedEconomy.IsFreightTag(tag) || NetPlayerStations.ProfileWritesBlocked) return false;
            var pair = shipments.FirstOrDefault(p => p.Value.FreightTag == tag && p.Value.Orbit == orbit);
            if (pair.Value == null) return false;
            string[] keys = pair.Key.Split('|');
            int recoverable = (int)((long)pair.Value.Amount * ModAdvancedEconomy.FreightSalvagePercent / 100);
            bool hasWreck = recoverable > 0 && keys.Length == 3 && NetGuard.Position(position);
            shipments.Remove(pair.Key);
            if (hasWreck)
                wrecks[tag] = new FreightWreck { Orbit = orbit, Item = keys[2], Amount = recoverable, Race = race,
                    Position = position, Expires = DateTime.UtcNow.AddMinutes(5).Ticks };
            dirty = true;
            Flush(true);
            if (!dirty) return true;
            if (hasWreck) wrecks.Remove(tag);
            shipments.Add(pair.Key, pair.Value);
            return false;
        }

        internal static string FreightWreckManifest(int orbit)
        {
            if (!Physical || NetPlayerStations.ProfileWritesBlocked) return "";
            long now = DateTime.UtcNow.Ticks;
            foreach (int tag in wrecks.Where(p => p.Value.Expires <= now).Select(p => p.Key).ToList())
            { wrecks.Remove(tag); dirty = true; }
            Flush(true);
            if (dirty) return "";
            var rows = new List<string>();
            foreach (var pair in wrecks.Where(p => p.Value.Orbit == orbit).OrderBy(p => p.Key).Take(16))
            {
                var w = pair.Value;
                if (!ModContent.TryResolveItem(w.Item, out int item)) continue;
                rows.Add(FormattableString.Invariant($"{pair.Key},{item},{w.Amount},{w.Race},{w.Position.x:R},{w.Position.y:R},{w.Position.z:R}"));
            }
            return string.Join(";", rows);
        }

        // Theft and wreck recovery use the same write-ahead journal as commodity purchases.
        // The client supplies only a requested amount; cargo is resolved from the live ledger.
        internal static int ClaimFreight(NetPlayer pilot, string account, int tag, NetProxy proxy, int requested, out int item)
        {
            item = -1;
            if (!Physical || NetPlayerStations.ProfileWritesBlocked || pilot == null || string.IsNullOrEmpty(account) ||
                requested <= 0 || !ModAdvancedEconomy.IsFreightTag(tag)) return 0;
            int orbit = pilot.Station;
            int room = NetProfiles.WreckSalvageCargoRoom(pilot, account);
            float reach = NetProfiles.WreckSalvageTractorReach(pilot);
            if (room <= 0 || reach <= 0f || !NetGuard.Position(pilot.Position)) return 0;
            int amount;
            if (proxy != null)
            {
                if (!proxy.IsSpawned || proxy.Station != orbit || proxy.EventTag != tag ||
                    !proxy.ScanVisible || !proxy.Status.Has(NetProxyStatus.EmpDisabled) ||
                    NetState.RunnerOf(orbit)?.OwnerClientId != proxy.OwnerClientId ||
                    !NetGuard.Position(proxy.WorldPosition) || (pilot.Position - proxy.WorldPosition).sqrMagnitude > reach * reach) return 0;
                var pair = shipments.FirstOrDefault(p => p.Value.FreightTag == tag && p.Value.Orbit == orbit);
                if (pair.Value == null) return 0;
                var keys = pair.Key.Split('|');
                if (keys.Length != 3 || !ModContent.TryResolveItem(keys[2], out item)) return 0;
                var ship = pair.Value;
                amount = Math.Min(ship.Amount, Math.Min(room, requested));
                int oldAmount = ship.Amount, oldResale = ship.Resale;
                ship.Amount -= amount; ship.Resale = Math.Min(ship.Resale, ship.Amount);
                if (ship.Amount == 0) shipments.Remove(pair.Key);
                dirty = true;
                if (!NetPlayerStations.CommitEconomyTrade(account, item, amount, 0, null, 0, Snapshot()))
                {
                    ship.Amount = oldAmount; ship.Resale = oldResale; shipments[pair.Key] = ship;
                    return 0;
                }
            }
            else
            {
                if (!wrecks.TryGetValue(tag, out var w) || w.Orbit != orbit || w.Expires <= DateTime.UtcNow.Ticks ||
                    (pilot.Position - w.Position).sqrMagnitude > reach * reach || !ModContent.TryResolveItem(w.Item, out item)) return 0;
                amount = Math.Min(w.Amount, Math.Min(room, requested));
                int oldAmount = w.Amount;
                w.Amount -= amount;
                if (w.Amount == 0) wrecks.Remove(tag);
                dirty = true;
                if (!NetPlayerStations.CommitEconomyTrade(account, item, amount, 0, null, 0, Snapshot()))
                {
                    w.Amount = oldAmount; wrecks[tag] = w;
                    return 0;
                }
            }
            return amount;
        }

        // A route carries at most one finite shipment. Debits, cargo in transit and arrivals live in the same snapshot.
        // A disabled or removed route freezes its shipment; it never silently redirects cargo to another endpoint.
        internal static Dictionary<(int station, int item), int> TickTrade(Database db, Func<int, bool> participates, Func<int, int> pressure, HashSet<int> changed, AdvancedEconomyRoutes graph)
        {
            if (!ModAdvancedEconomy.RegionalTrade) return new Dictionary<(int station, int item), int>();
            Load();
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
                        int hops = graph.Distance(Shop.SystemOf(db, source), destinationSystem);
                        if (hops < 0 || hops > ModAdvancedEconomy.MaxAutoHops || hops >= bestHops) continue;
                        best = source; bestHops = hops;
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
                    if (shipment.Ticks > 0 || shipment.TotalTicks > 0) UpdateFreight(db, route, shipment, path);
                    if (shipment.Ticks > 0) continue;
                    // Give a newly reached destination one full tick of visibility before unloading.
                    if (Physical && shipment.Orbit == route.Destination && shipment.TotalTicks > 0)
                    {
                        shipment.TotalTicks = 0;
                        dirty = true;
                        continue;
                    }
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
                if (Physical) send = ModAdvancedEconomy.FreightCapacity(send, ModAdvancedEconomy.RhinoCapacity, ModAdvancedEconomy.CormorantCapacity);
                if (send <= 0) continue;
                int resaleSent = Math.Min(send, source.Resale);
                int survive = ModAdvancedEconomy.SurvivingCargo(send, route.WarLossPercent,
                    routePressure);
                Set(route.Source, sourceRule, source.Amount - send, source.Resale - resaleSent);
                changed.Add(route.Source);
                if (survive > 0)
                {
                    int resaleSurvives = Math.Min(resaleSent, survive);
                    var dispatched = new Shipment { Amount = survive, Resale = resaleSurvives,
                        Ticks = Math.Min(96, Math.Max(route.TravelTicks, Math.Max(1, path.Count - 1) * ModAdvancedEconomy.TicksPerHop)),
                        Hops = Math.Min(96, path.Count - 1) };
                    if (Physical)
                    {
                        dispatched.Ticks = Math.Max(dispatched.Ticks, Math.Max(2, path.Count));
                        UpdateFreight(db, route, dispatched, path);
                    }
                    shipments[key] = dispatched;
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
                    ["resale"] = pair.Value.Resale, ["ticks"] = pair.Value.Ticks, ["hops"] = pair.Value.Hops, ["freightTag"] = pair.Value.FreightTag,
                    ["orbit"] = pair.Value.Orbit, ["totalTicks"] = pair.Value.TotalTicks, ["bulk"] = pair.Value.Bulk, ["hull"] = pair.Value.Hull });
            var debris = new JArray();
            foreach (var pair in wrecks)
            {
                var w = pair.Value;
                debris.Add(new JObject { ["tag"] = pair.Key, ["orbit"] = w.Orbit, ["amount"] = w.Amount, ["item"] = w.Item,
                    ["race"] = w.Race, ["expires"] = w.Expires, ["x"] = w.Position.x, ["y"] = w.Position.y, ["z"] = w.Position.z });
            }
            var root = new JObject { ["version"] = 1, ["rows"] = rows, ["shipments"] = cargo, ["freightWrecks"] = debris };
            return root.ToString(Formatting.None);
        }

        // The station/profile write-ahead journal includes the entire economy snapshot.
        // Recovery uses absolute targets and therefore cannot debit or credit stock twice.
        internal static bool ValidateJournalSnapshot(string json)
        {
            try { Read(json); ReadShipments(JObject.Parse(json)); ReadWrecks(JObject.Parse(json)); return true; }
            catch (Exception e) { Debug.LogWarning($"Advanced economy: invalid journal snapshot ({e.Message})."); return false; }
        }

        internal static bool PersistJournalSnapshot(string json, bool recover)
        {
            if (string.IsNullOrEmpty(NetProfiles.Folder)) return false;
            var parsed = Read(json);
            var cargo = ReadShipments(JObject.Parse(json));
            var debris = ReadWrecks(JObject.Parse(json));
            string target = Path.Combine(NetProfiles.Folder, "advanced-economy-stock.json");
            if (!NetProfiles.Write(target, json)) return false;
            if (recover && loaded)
            {
                balances.Clear(); live.Clear(); shipments.Clear(); wrecks.Clear();
                foreach (var pair in parsed) balances.Add(pair.Key, pair.Value);
                foreach (var pair in cargo) shipments.Add(pair.Key, pair.Value);
                foreach (var pair in debris) wrecks.Add(pair.Key, pair.Value);
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
