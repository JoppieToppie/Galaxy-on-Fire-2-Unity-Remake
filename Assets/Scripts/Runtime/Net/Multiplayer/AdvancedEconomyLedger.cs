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
            // -1 is a legacy tick-based shipment. Nonnegative values are active server seconds.
            public int RemainingSeconds = -1;
            public string FleetRace, FleetHull;
            public int FleetSpeed = 100;
            // Per-system raid state. No outcome is rolled until the cargo departs or unloads.
            public int RaidSystem = -1, RaidBase, RaidDead, RaidPlayerKills, RaidObservationSeconds;
            // Persistent delay notification state; ordinary travel time is not a delay.
            public int DelayReason, DelaySeconds;
            public readonly List<string> RaidContributors = new List<string>();
            public readonly HashSet<string> RaidThieves = new HashSet<string>(StringComparer.Ordinal);
        }
        static readonly Dictionary<string, Shipment> shipments = new Dictionary<string, Shipment>();
        static readonly List<(int station, int destination, string text)> freightNews = new List<(int, int, string)>();
        static readonly Dictionary<string, long> rescueRewards = new Dictionary<string, long>(StringComparer.Ordinal);
        static readonly Dictionary<int, HashSet<ulong>> distressSent = new Dictionary<int, HashSet<ulong>>();
        static readonly Dictionary<ulong, float> nextDistress = new Dictionary<ulong, float>();
        static float nextRescueSettlement;
        sealed class FreightWreck
        {
            public int Orbit, Amount, Race;
            public string Item;
            public Vector3 Position;
            public long Expires;
        }
        static readonly Dictionary<int, FreightWreck> wrecks = new Dictionary<int, FreightWreck>();
        static readonly Dictionary<(int orbit, int tag), (int race, Vector3 position)> pendingFreightLosses =
            new Dictionary<(int, int), (int, Vector3)>();
        static float nextLossRetry;
        static bool loaded, dirty, readFailed;
        static float nextSave;
        static string path;
        public static bool Active => ModAdvancedEconomy.Enabled && ModAdvancedEconomy.PersistentInventory &&
            NetGame.Active && !NetGame.CoopSession && !CoopCampaign.Active;

        internal static void Reset()
        {
            balances.Clear();
            live.Clear();
            shipments.Clear(); freightNews.Clear(); rescueRewards.Clear(); distressSent.Clear(); nextDistress.Clear(); nextRescueSettlement = 0;
            wrecks.Clear(); pendingFreightLosses.Clear(); nextLossRetry = 0;
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
                    var rewards = ReadRescueRewards(JObject.Parse(json));
                    ValidateFreightIdentities(cargo, debris);
                    foreach (var pair in parsed) balances.Add(pair.Key, pair.Value);
                    foreach (var pair in cargo) shipments.Add(pair.Key, pair.Value);
                    foreach (var pair in debris) wrecks.Add(pair.Key, pair.Value);
                    foreach (var pair in rewards) rescueRewards.Add(pair.Key, pair.Value);
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
                int tag = row["freightTag"] == null ? 0 : ModAdvancedEconomy.MigrateFreightTag((int)row["freightTag"]);
                int orbit = row["orbit"] == null ? -1 : (int)row["orbit"];
                int totalTicks = row["totalTicks"] == null ? 0 : (int)row["totalTicks"];
                if ((tag != 0 && (!ModAdvancedEconomy.IsFreightTag(tag) || result.Values.Any(s => s.FreightTag == tag))) ||
                    orbit < -1 || totalTicks < 0 || totalTicks > 96 || (totalTicks != 0 && totalTicks < ticks))
                    throw new JsonException("Invalid physical freight identity or progress.");
                int remainingSeconds = row["remainingSeconds"] == null ? -1 : (int)row["remainingSeconds"];
                if (remainingSeconds < -1 || remainingSeconds > ModAdvancedEconomy.MaxFreightSeconds)
                    throw new JsonException("Invalid freight travel countdown.");
                string fleetRace = (string)row["fleetRace"], fleetHull = (string)row["fleetHull"];
                int fleetSpeed = row["fleetSpeed"] == null ? 100 : (int)row["fleetSpeed"];
                if ((string.IsNullOrEmpty(fleetRace) != string.IsNullOrEmpty(fleetHull)) ||
                    fleetSpeed < 10 || fleetSpeed > 500 || (fleetRace?.Length ?? 0) > 256 || (fleetHull?.Length ?? 0) > 256)
                    throw new JsonException("Invalid freight fleet identity.");
                int hull = row["hull"] == null ? 65535 : (int)row["hull"];
                if (hull < 1 || hull > 65535) throw new JsonException("Invalid freight hull fraction.");
                int raidSystem = (int?)row["raidSystem"] ?? -1, raidBase = (int?)row["raidBase"] ?? 0;
                int raidDead = (int?)row["raidDead"] ?? 0, raidKills = (int?)row["raidPlayerKills"] ?? 0;
                int raidObserved = (int?)row["raidObservationSeconds"] ?? 0;
                int delayReason = (int?)row["delayReason"] ?? 0, delaySeconds = (int?)row["delaySeconds"] ?? 0;
                int deadCount = (raidDead & 1) + ((raidDead >> 1) & 1) + ((raidDead >> 2) & 1);
                if (delayReason < 0 || delayReason > 3 || delaySeconds < 0 || delaySeconds > 120 || (delayReason == 0 && delaySeconds != 0))
                    throw new JsonException("Invalid freight delay state.");
                if (raidSystem < -1 || raidDead < 0 || raidDead > 7 || raidKills < 0 || raidKills > deadCount ||
                    raidObserved < 0 || raidObserved > 20 || (raidBase == 0 && (raidDead != 0 || raidKills != 0 || raidObserved != 0)) ||
                    (raidBase != 0 && (raidSystem < 0 || !ModAdvancedEconomy.IsFreightRaidTag(raidBase) || raidBase % 4 != 0 ||
                    result.Values.Any(s => s.RaidBase == raidBase)))) throw new JsonException("Invalid freight raid state.");
                var contributors = new List<string>();
                if (row["raidContributors"] != null)
                {
                    if (!(row["raidContributors"] is JArray accounts) || accounts.Count > raidKills) throw new JsonException("Invalid raid contributors.");
                    foreach (var account in accounts)
                    {
                        string id = (string)account;
                        if (account.Type != JTokenType.String || string.IsNullOrEmpty(id) || id.Length > 256) throw new JsonException("Invalid raid contributor.");
                        contributors.Add(id);
                    }
                }
                result.Add(key, new Shipment { Amount = (int)amount, Resale = (int)resale, Ticks = (int)ticks, Hops = hops,
                    FreightTag = tag, Orbit = orbit, TotalTicks = totalTicks,
                    Bulk = row["bulk"] == null ? amount > ModAdvancedEconomy.CormorantCapacity : (bool)row["bulk"],
                    Hull = hull, RemainingSeconds = remainingSeconds, FleetRace = fleetRace, FleetHull = fleetHull, FleetSpeed = fleetSpeed,
                    RaidSystem = raidSystem, RaidBase = raidBase, RaidDead = raidDead, RaidPlayerKills = raidKills, RaidObservationSeconds = raidObserved,
                    DelayReason = delayReason, DelaySeconds = delaySeconds });
                result[key].RaidContributors.AddRange(contributors);
                if (row["raidThieves"] != null)
                {
                    if (!(row["raidThieves"] is JArray thieves)) throw new JsonException("Invalid raid thieves.");
                    foreach (var thief in thieves)
                    {
                        if (thief.Type != JTokenType.String || raidBase == 0) throw new JsonException("Invalid raid thief.");
                        string id = (string)thief;
                        if (string.IsNullOrEmpty(id) || id.Length > 256 || !result[key].RaidThieves.Add(id)) throw new JsonException("Invalid raid thief.");
                    }
                }
            }
            return result;
        }

        static Dictionary<string, long> ReadRescueRewards(JObject root)
        {
            var result = new Dictionary<string, long>(StringComparer.Ordinal);
            if (root["rescueRewards"] == null) return result;
            if (!(root["rescueRewards"] is JArray rows)) throw new JsonException("Invalid freight rescue rewards.");
            foreach (var token in rows)
            {
                if (!(token is JObject row) || row["account"]?.Type != JTokenType.String || row["credits"]?.Type != JTokenType.Integer) throw new JsonException("Invalid freight rescue reward.");
                string account = (string)row["account"]; long credits = (long)row["credits"];
                if (string.IsNullOrEmpty(account) || account.Length > 256 || credits <= 0 || result.ContainsKey(account)) throw new JsonException("Invalid freight rescue reward balance.");
                result.Add(account, credits);
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
                int tag = ModAdvancedEconomy.MigrateFreightTag((int)row["tag"]), orbit = (int)row["orbit"], amount = (int)row["amount"], race = (int)row["race"];
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

        static void ValidateFreightIdentities(Dictionary<string, Shipment> cargo, Dictionary<int, FreightWreck> debris)
        {
            foreach (var shipment in cargo.Values)
                if (shipment.FreightTag != 0 && debris.ContainsKey(shipment.FreightTag))
                    throw new JsonException("A freight identity cannot be both in transit and wrecked.");
        }

        static string RouteKey(ModAdvancedEconomy.Route route)
        {
            var a = StableKey(route.Source, route.Item);
            var b = StableKey(route.Destination, route.Item);
            return a.Station + "|" + b.Station + "|" + a.Item;
        }

        static bool Physical => Active && ModAdvancedEconomy.PhysicalFreight && NetProfiles.Enabled && CanJournal;
        internal static bool IndependentFreightClock => Physical;

        static int NewFreightTag()
        {
            int tag;
            do { tag = UnityEngine.Random.Range(ModAdvancedEconomy.FreightTagStart, ModAdvancedEconomy.FreightTagEnd); }
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

        static int OriginRace(Database db, int station)
        {
            int system = Shop.SystemOf(db, station);
            int race = NpcFactionWar.OrbitOwner(db.Systems.Find(s => s.index == system), station);
            return race >= 0 && race <= 10 || ModRaces.Get(race) != null ? race : 0;
        }

        static void AssignFleet(Shipment ship, int race, ModAdvancedEconomy.FreightHull hull)
        {
            ship.FleetRace = ModRaces.Get(race)?.key ?? race.ToString(System.Globalization.CultureInfo.InvariantCulture);
            ship.FleetHull = hull.Reference;
            ship.FleetSpeed = hull.SpeedPercent;
            dirty = true;
        }

        static bool ResolveFleet(Database db, Shipment ship, int source, out int race, out int hull)
        {
            race = OriginRace(db, source);
            hull = ModAdvancedEconomy.NativeFreighter(race);
            if (string.IsNullOrEmpty(ship.FleetHull))
            {
                var selected = ModAdvancedEconomy.PickFreightHull(ModAdvancedEconomy.FreightFleet(race), ship.Amount,
                    UnityEngine.Random.Range(0, int.MaxValue));
                if (selected == null) return false;
                AssignFleet(ship, race, selected); // old shipments keep all of their already reserved cargo
            }
            if (!ModRaces.TryResolve(ship.FleetRace, null, out race)) return false;
            if (ship.FleetHull == "native") hull = ModAdvancedEconomy.NativeFreighter(race);
            else if (!ModContent.TryResolveShip(ship.FleetHull, out hull)) return false;
            return db.Ship(hull) != null;
        }

        static int RandomSeconds(int min, int max) => UnityEngine.Random.Range(min, max + 1);
        static int TravelSeconds() => RandomSeconds(ModAdvancedEconomy.FreightTravelMinSeconds, ModAdvancedEconomy.FreightTravelMaxSeconds);
        static int UnloadSeconds() => RandomSeconds(ModAdvancedEconomy.FreightUnloadMinSeconds, ModAdvancedEconomy.FreightUnloadMaxSeconds);

        // Preserve the last saved orbit when migrating older shipments; do not re-debit stock.
        static void StartFreightClock(Database db, ModAdvancedEconomy.Route route, Shipment ship, IReadOnlyList<int> systems)
        {
            if (ship.RemainingSeconds >= 0) return;
            if (ship.FreightTag == 0 || ship.Orbit < 0)
            {
                if (ship.Ticks == 0 && ship.FreightTag == 0)
                {
                    ship.FreightTag = NewFreightTag();
                    ship.Bulk = ship.Amount > ModAdvancedEconomy.CormorantCapacity;
                    ship.Orbit = route.Destination;
                }
                else UpdateFreight(db, route, ship, systems);
            }
            ship.TotalTicks = 0;
            ship.RemainingSeconds = ship.Orbit == route.Destination ? UnloadSeconds() : TravelSeconds();
            dirty = true;
        }

        // One server pulse, no offline catch-up. Physical approach time replaces abstract travel time.
        internal static int FreightCountdown(int remaining, bool materialized, bool ready, bool unloading)
        {
            if (materialized && !ready) return remaining;
            if (materialized && !unloading) return 0;
            return Math.Max(0, remaining - 1);
        }

        static void EnsureFreightRaid(Database db, Shipment ship)
        {
            if (!Physical) return;
            if (!ModAdvancedEconomy.FreightPirateRaids)
            {
                if (ship.RaidBase != 0) { distressSent.Remove(ship.RaidBase); ship.RaidContributors.Clear(); ship.RaidThieves.Clear(); ship.RaidBase = ship.RaidDead = ship.RaidPlayerKills = ship.RaidObservationSeconds = 0; dirty = true; }
                return;
            }
            int system = db.Stations.Find(s => s.index == ship.Orbit)?.system ?? -1;
            if (system < 0 || ship.RaidSystem == system || !PirateReports.ServerHasPresence(system)) return;
            ship.RaidSystem = system; ship.RaidContributors.Clear(); ship.RaidThieves.Clear(); ship.RaidBase = ship.RaidDead = ship.RaidPlayerKills = ship.RaidObservationSeconds = 0;
            if (UnityEngine.Random.Range(0, 100) < ModAdvancedEconomy.FreightPirateAttemptPercent)
            {
                int tag;
                do { tag = UnityEngine.Random.Range(ModAdvancedEconomy.FreightRaidTagStart / 4, ModAdvancedEconomy.FreightRaidTagEnd / 4) * 4; }
                while (shipments.Values.Any(s => s.RaidBase == tag));
                ship.RaidBase = tag;
                Debug.Log($"[AdvancedEconomy] pirate raid begun: freight={ship.FreightTag}, system={system}, raid={tag}");
            }
            dirty = true;
        }

        // Record each confirmed NPC death once. NPC-on-NPC losses remove a fighter but do not count as player intervention.
        internal static void RecordFreightRaidDeath(int orbit, int tag, ulong killer)
        {
            if (!Physical || !ModAdvancedEconomy.IsFreightRaidTag(tag)) return;
            int root = ModAdvancedEconomy.FreightRaidBase(tag), bit = 1 << (tag - root);
            var ship = shipments.Values.FirstOrDefault(s => s.Orbit == orbit && s.RaidBase == root);
            if (ship == null || (ship.RaidDead & bit) != 0) return;
            ship.RaidDead |= bit;
            if (killer != ulong.MaxValue && NetGuard.InOrbit(killer, orbit))
            {
                ship.RaidPlayerKills++;
                string account = NetProfiles.AccountOf(killer);
                if (!string.IsNullOrEmpty(account) && NetProfiles.Controls(killer)) ship.RaidContributors.Add(account);
            }
            dirty = true;
            Debug.Log($"[AdvancedEconomy] pirate raid casualty: freight={ship.FreightTag}, playerKills={ship.RaidPlayerKills}, deadMask={ship.RaidDead}");
            Flush(true);
        }

        // Compare actual destination stock against the expected delivery, not a speculative price forecast.
        internal static int FreightLossShortfall(int stock, int target, int shipment) =>
            Math.Max(0, target - Math.Max(0, stock)) - (int)Math.Max(0L, (long)target - Math.Max(0, stock) - Math.Max(0, shipment));

        static void SendDistress(Database db, ModAdvancedEconomy.Route route, Shipment ship)
        {
            if (!ModAdvancedEconomy.FreightDistressCalls || ship.RaidBase == 0 || NetState.Instance == null || ship.RaidPlayerKills >= ModAdvancedEconomy.FreightRaidSize) return;
            var station = db.Stations.Find(s => s.index == ship.Orbit);
            var destination = db.Stations.Find(s => s.index == route.Destination);
            if (station == null || destination == null) return;
            if (!distressSent.TryGetValue(ship.RaidBase, out var sent)) distressSent[ship.RaidBase] = sent = new HashSet<ulong>();
            var recipients = new List<ulong>();
            foreach (var pilot in NetPlayer.All)
            {
                if (pilot == null || !pilot.IsSpawned || pilot.Station < 0) continue;
                ulong client = pilot.OwnerClientId;
                if (sent.Contains(client) || (nextDistress.TryGetValue(client, out float next) && Time.unscaledTime < next)) continue;
                if ((db.Stations.Find(s => s.index == pilot.Station)?.system ?? -1) == station.system) recipients.Add(client);
            }
            if (recipients.Count == 0) return;
            var rule = ModAdvancedEconomy.At(route.Destination);
            string commodity = GameNames.Item(route.Item);
            int shortageWithout = 0, shortageWith = 0;
            string impact = $"Losing this cargo will remove {ship.Amount} t from expected imports.";
            if (rule != null && rule.TryGetValue(route.Item, out var row))
            {
                int stock = Get(route.Destination, row).Amount;
                int lostRelief = FreightLossShortfall(stock, row.Target, ship.Amount);
                int deficit = Math.Max(0, row.Target - stock);
                shortageWithout = deficit; shortageWith = deficit - lostRelief;
                if (lostRelief > 0)
                    impact = $"Without us, {destination.systemName}\\u2019s destination market remains {deficit} t below target instead of {deficit - lostRelief} t; scarcity may raise prices.";
                else impact = $"Loss would cut expected imports by {ship.Amount} t; the destination currently has no additional target shortfall from this shipment alone.";
            }
            string reward = ModAdvancedEconomy.FreightRescueReward <= 0 ? "" :
                $" Rescue reward: {ModAdvancedEconomy.FreightRescueReward:N0} credits ({ModAdvancedEconomy.FreightRescueRewardMode}).";
            int race = ModRaces.TryResolve(ship.FleetRace, null, out int owner) ? owner : OriginRace(db, route.Source);
            string message = ModAdvancedEconomy.FreightDistressMessage(race, ship.RaidBase, new Dictionary<string, string>
            {
                ["faction"] = GameNames.Race(race), ["orbit"] = NetNews.Place(ship.Orbit),
                ["system"] = station.systemName ?? "", ["origin"] = NetNews.Place(route.Source),
                ["destination"] = NetNews.Place(route.Destination), ["commodity"] = commodity,
                ["amount"] = ship.Amount.ToString(), ["shortageWithout"] = shortageWithout.ToString(),
                ["shortageWith"] = shortageWith.ToString(), ["impact"] = impact,
                ["reward"] = ModAdvancedEconomy.FreightRescueReward.ToString("N0"),
                ["rewardMode"] = ModAdvancedEconomy.FreightRescueRewardMode, ["rewardText"] = reward
            });
            string title = ModAdvancedEconomy.FreightDistressTitle(race);
            foreach (ulong client in recipients)
            {
                NetState.Instance.SendFreightDistress(client, title, message);
                sent.Add(client); nextDistress[client] = Time.unscaledTime + 15f;
            }
        }

        internal static Dictionary<string, long> FreightRescueShares(IEnumerable<string> accounts, int reward, string mode)
        {
            var result = new Dictionary<string, long>(StringComparer.Ordinal);
            if (reward <= 0 || accounts == null) return result;
            var groups = accounts.Where(a => !string.IsNullOrEmpty(a)).GroupBy(a => a, StringComparer.Ordinal)
                .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal).ToList();
            if (groups.Count == 0) return result;
            mode = ModAdvancedEconomy.NormalizeFreightRewardMode(mode);
            if (mode == "single") { result[groups[0].Key] = reward; return result; }
            long weight = mode == "weighted" ? groups.Sum(g => g.Count()) : groups.Count;
            long remaining = reward;
            foreach (var g in groups)
            {
                long share = mode == "flat" ? reward : (long)reward * (mode == "weighted" ? g.Count() : 1) / weight;
                if (share > 0) result[g.Key] = share;
                remaining -= share;
            }
            if (mode != "flat" && remaining > 0)
                foreach (var g in groups.Take((int)remaining)) result[g.Key] = (result.TryGetValue(g.Key, out long share) ? share : 0) + 1;
            return result;
        }

        static void QueueRescueReward(Shipment ship)
        {
            foreach (var entry in FreightRescueShares(ship.RaidContributors.Where(a => !ship.RaidThieves.Contains(a)), ModAdvancedEconomy.FreightRescueReward, ModAdvancedEconomy.FreightRescueRewardMode))
            {
                rescueRewards.TryGetValue(entry.Key, out long prior);
                rescueRewards[entry.Key] = prior > long.MaxValue - entry.Value ? long.MaxValue : prior + entry.Value;
            }
        }

        // Remove the pending reward in the same absolute journal snapshot that credits the profile.
        // A restart cannot replay a paid reward, and disconnected contributors are still eligible.
        static void SettleRescueRewards()
        {
            if (rescueRewards.Count == 0 || NetPlayerStations.ProfileWritesBlocked || Time.unscaledTime < nextRescueSettlement) return;
            nextRescueSettlement = Time.unscaledTime + 10f;
            foreach (var entry in rescueRewards.ToList())
            {
                if (NetPlayerStations.ProfileWritesBlocked) break;
                rescueRewards.Remove(entry.Key);
                if (!NetPlayerStations.CommitFreightRescueReward(entry.Key, entry.Value, Snapshot()))
                {
                    rescueRewards[entry.Key] = entry.Value;
                    Debug.LogWarning("[AdvancedEconomy] freight rescue reward pending; profile settlement unavailable.");
                    continue;
                }
                NetPlayerStations.NotifyProfileCreditChange(entry.Key, entry.Value, "Freight rescue reward");
                Debug.Log($"[AdvancedEconomy] freight rescue reward committed: credits={entry.Value}");
            }
        }

        static void ObserveFreightDelay(ModAdvancedEconomy.Route route, Shipment ship, int reason)
        {
            if (reason == 0)
            {
                if (ship.DelayReason == 0) return;
                if (ship.DelaySeconds >= 120)
                    freightNews.Add((ship.Orbit, route.Destination, $"Delayed freight bound for {NetNews.Place(route.Destination)} is moving again near {NetNews.Place(ship.Orbit)}."));
                ship.DelayReason = ship.DelaySeconds = 0; dirty = true;
                return;
            }
            if (ship.DelayReason != reason) { ship.DelayReason = reason; ship.DelaySeconds = 0; dirty = true; }
            if (ship.DelaySeconds >= 120) return;
            ship.DelaySeconds++; dirty = true;
            if (ship.DelaySeconds == 120)
            {
                string cause = reason == 1 ? "disconnected trade route" : reason == 2 ? "disabled freighter" : "destination inventory full";
                freightNews.Add((ship.Orbit, route.Destination, $"Shipment of {ship.Amount} t bound for {NetNews.Place(route.Destination)} delayed near {NetNews.Place(ship.Orbit)}: {cause}."));
                Debug.Log($"[AdvancedEconomy] freight delayed: freight={ship.FreightTag}, reason={cause}");
            }
        }

        static void ResolveFreightRaid(ModAdvancedEconomy.Route route, Shipment ship)
        {
            if (ship.RaidBase == 0) return;
            distressSent.Remove(ship.RaidBase);
            int orbit = ship.Orbit, original = ship.Amount;
            int result = ModAdvancedEconomy.FreightPirateRaids ? ModAdvancedEconomy.FreightRaidOutcome(UnityEngine.Random.Range(0, 10000),
                ModAdvancedEconomy.FreightPirateTheftPercent, ModAdvancedEconomy.FreightPirateDestructionPercent,
                ModAdvancedEconomy.FreightPirateKillRemainingPercent, ship.RaidPlayerKills) : 0;
            if (result == 2) ship.Amount = 0;
            else if (result == 1) ship.Amount -= Math.Min(ship.Amount, Math.Max(1, ship.Amount * UnityEngine.Random.Range(40, 101) / 100));
            ship.Resale = Math.Min(ship.Resale, ship.Amount);
            if (result == 0 && ModAdvancedEconomy.FreightPirateRaids && ship.RaidPlayerKills > 0) QueueRescueReward(ship);
            ship.RaidContributors.Clear(); ship.RaidThieves.Clear();
            string outcome = result == 2 ? $"destroyed; {original} t lost" : result == 1 ? $"raided; {original - ship.Amount} t stolen, {ship.Amount} t remaining" : "escaped a pirate attack";
            freightNews.Add((orbit, ship.Amount < original ? route.Destination : -1, $"Freight bound for {NetNews.Place(route.Destination)} {outcome} near {NetNews.Place(orbit)}. Player pirate kills: {ship.RaidPlayerKills}."));
            Debug.Log($"[AdvancedEconomy] pirate raid resolved: freight={ship.FreightTag}, system={ship.RaidSystem}, outcome={result}, lost={original - ship.Amount}, playerKills={ship.RaidPlayerKills}");
            ship.RaidBase = ship.RaidDead = ship.RaidPlayerKills = ship.RaidObservationSeconds = 0;
            dirty = true;
        }

        static bool AdvanceFreight(Database db, ModAdvancedEconomy.Route route, Shipment ship,
            IReadOnlyList<int> systems, Dictionary<(int orbit, int tag), NetProxy> proxies)
        {
            if (ship.RemainingSeconds < 0)
            {
                StartFreightClock(db, route, ship, systems);
                return false;
            }
            // Freeze physical progress if the saved hull or race was removed by a mod change.
            if (!ResolveFleet(db, ship, route.Source, out _, out _)) return false;
            EnsureFreightRaid(db, ship);
            SendDistress(db, route, ship);
            bool unloading = ship.Orbit == route.Destination;
            bool materialized = proxies.ContainsKey((ship.Orbit, ship.FreightTag));
            bool ready = !materialized || FreightLegReady(db, ship, proxies);
            // The caller resolves the route from the current saved orbit, rather than the supplier.
            var remainingPath = systems;
            if (!unloading && (remainingPath == null || remainingPath.Count == 0)) return false;
            int next = FreightCountdown(ship.RemainingSeconds, materialized, ready, unloading);
            if (next != ship.RemainingSeconds) { ship.RemainingSeconds = next; dirty = true; }
            // An arriving pilot gets a brief opportunity to see the manifest and intervene even if
            // the abstract leg is nearly over. Leaving the orbit removes this hold immediately.
            bool observingRaid = ship.RaidBase != 0 && NetState.RunnerOf(ship.Orbit) != null && ship.RaidPlayerKills < ModAdvancedEconomy.FreightRaidSize;
            if (observingRaid && ship.RaidObservationSeconds < 20)
            {
                ship.RaidObservationSeconds++; dirty = true;
                return false;
            }
            if (next > 0 || !ready) return false;
            if (unloading) return true;

            int orbit = route.Destination;
            if (remainingPath.Count > 2)
            {
                int system = remainingPath[1];
                orbit = db.Systems.Find(s => s.index == system)?.jumpgateStation ?? -1;
                if (orbit < 0 || !db.Stations.Exists(s => s.index == orbit && s.system == system))
                    orbit = db.Stations.Where(s => s.system == system && !Shop.UsesPlayerMarketplace(s.index))
                        .Select(s => s.index).DefaultIfEmpty(-1).Min();
                if (orbit < 0) return false;
            }
            int oldSystem = db.Stations.Find(s => s.index == ship.Orbit)?.system ?? -1;
            int newSystem = db.Stations.Find(s => s.index == orbit)?.system ?? -1;
            if (oldSystem != newSystem)
            {
                ResolveFreightRaid(route, ship);
                if (ship.Amount == 0) return true;
                ship.RaidSystem = -1;
            }
            ship.Orbit = orbit;
            ship.Ticks = Math.Max(0, remainingPath.Count - 2); // fallback if physical freight is later disabled
            ship.RemainingSeconds = orbit == route.Destination ? UnloadSeconds() : TravelSeconds();
            dirty = true;
            return false;
        }

        // Only persisted, in-transit shipments are exposed; the amount is a manifest, not a grant.
        internal static string FreightManifest(int orbit)
        {
            if (!Physical || NetPlayerStations.ProfileWritesBlocked) return null;
            var rows = new List<string>();
            var db = NetGame.Db;
            if (db == null) return null;
            foreach (var pair in shipments.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                var ship = pair.Value;
                if (ship.Orbit != orbit || ship.FreightTag == 0 || ship.Amount <= 0) continue;
                var keys = pair.Key.Split('|');
                if (keys.Length != 3 || !ModContent.TryResolveItem(keys[2], out int item) ||
                    !ModWorld.TryResolveStation(keys[0], out int source) || !ModWorld.TryResolveStation(keys[1], out int destination)) continue;
                if (!ResolveFleet(db, ship, source, out int race, out int hull)) continue;
                EnsureFreightRaid(db, ship);
                rows.Add($"{ship.FreightTag},{item},{ship.Amount},{source},{destination},{(ship.Bulk ? 1 : 0)},{ship.Hull},{race},{hull},{ship.FleetSpeed},{ship.RaidBase},{ship.RaidDead}");
                if (rows.Count >= ModAdvancedEconomy.FreightPerOrbit) break;
            }
            Flush(true);
            return dirty ? null : string.Join(";", rows);
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

        // A death can arrive while a station journal blocks profile writes. Do not let
        // the economic clock deliver that cargo before the death can be settled.
        internal static void ReportFreightLoss(int orbit, int tag, int race, Vector3 position)
        {
            if (!ModAdvancedEconomy.PhysicalFreight || !ModAdvancedEconomy.IsFreightTag(tag)) return;
            pendingFreightLosses[(orbit, tag)] = (race, position);
            RetryFreightLosses();
        }

        internal static bool RetryFreightLosses()
        {
            if (pendingFreightLosses.Count == 0) return true;
            if (!Physical || NetPlayerStations.ProfileWritesBlocked || Time.unscaledTime < nextLossRetry) return false;
            nextLossRetry = Time.unscaledTime + 1f;
            foreach (var pair in pendingFreightLosses.ToList())
            {
                if (!shipments.Values.Any(s => s.FreightTag == pair.Key.tag && s.Orbit == pair.Key.orbit) ||
                    LoseFreight(pair.Key.orbit, pair.Key.tag, pair.Value.race, pair.Value.position))
                    pendingFreightLosses.Remove(pair.Key);
            }
            return pendingFreightLosses.Count == 0;
        }

        // A destroyed physical ship cannot later deliver. The absolute ledger is written before
        // accepting the loss; repeated reports and late reports from earlier orbits do nothing.
        static bool LoseFreight(int orbit, int tag, int race, Vector3 position)
        {
            if (!Physical || !ModAdvancedEconomy.IsFreightTag(tag) || NetPlayerStations.ProfileWritesBlocked) return false;
            var pair = shipments.FirstOrDefault(p => p.Value.FreightTag == tag && p.Value.Orbit == orbit);
            if (pair.Value == null) return false;
            string[] keys = pair.Key.Split('|');
            int recoverable = ModAdvancedEconomy.FreightSalvageAmount(pair.Value.Amount, ModAdvancedEconomy.FreightSalvagePercent);
            bool hasWreck = recoverable > 0 && keys.Length == 3 && NetGuard.Position(position);
            shipments.Remove(pair.Key);
            if (hasWreck)
                wrecks[tag] = new FreightWreck { Orbit = orbit, Item = keys[2], Amount = recoverable, Race = race,
                    Position = position, Expires = DateTime.UtcNow.AddSeconds(ModAdvancedEconomy.FreightWreckLifetimeSeconds).Ticks };
            dirty = true;
            Flush(true);
            if (!dirty)
            {
                if (keys.Length == 3 && ModWorld.TryResolveStation(keys[1], out int destination))
                    NetNews.Post(NetNews.Kind.Commerce, $"Freighter destroyed near {NetNews.Place(orbit)}: {pair.Value.Amount} t bound for {NetNews.Place(destination)} lost.", destination);
                return true;
            }
            if (hasWreck) wrecks.Remove(tag);
            shipments.Add(pair.Key, pair.Value);
            return false;
        }

        internal static string FreightWreckManifest(int orbit)
        {
            if (!Physical || NetPlayerStations.ProfileWritesBlocked) return null;
            long now = DateTime.UtcNow.Ticks;
            foreach (int tag in wrecks.Where(p => p.Value.Expires <= now).Select(p => p.Key).ToList())
            { wrecks.Remove(tag); dirty = true; }
            Flush(true);
            if (dirty) return null;
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
                amount = ModAdvancedEconomy.FreightClaimAmount(ship.Amount, room, requested);
                int oldAmount = ship.Amount, oldResale = ship.Resale;
                bool newThief = amount > 0 && ship.RaidBase != 0 && ship.RaidThieves.Add(account);
                ship.Amount -= amount; ship.Resale = Math.Min(ship.Resale, ship.Amount);
                if (ship.Amount == 0) shipments.Remove(pair.Key);
                dirty = true;
                if (!NetPlayerStations.CommitEconomyTrade(account, item, amount, 0, null, 0, Snapshot()))
                {
                    ship.Amount = oldAmount; ship.Resale = oldResale; shipments[pair.Key] = ship;
                    if (newThief) ship.RaidThieves.Remove(account);
                    return 0;
                }
                if (amount > 0 && ModWorld.TryResolveStation(keys[1], out int destination))
                    NetNews.Post(NetNews.Kind.Commerce, $"Pirates stole {amount} t of freight near {NetNews.Place(orbit)}, bound for {NetNews.Place(destination)}.", destination, $"freight-theft:{tag}", 30f);
            }
            else
            {
                if (!wrecks.TryGetValue(tag, out var w) || w.Orbit != orbit || w.Expires <= DateTime.UtcNow.Ticks ||
                    (pilot.Position - w.Position).sqrMagnitude > reach * reach || !ModContent.TryResolveItem(w.Item, out item)) return 0;
                amount = ModAdvancedEconomy.FreightClaimAmount(w.Amount, room, requested);
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

        // Occupied legs do not advance while a freighter is disabled or still approaching its
        // lane endpoint. Unmaterialized legs use the ordinary lightweight economic clock.
        static Dictionary<(int orbit, int tag), NetProxy> PhysicalFreightProxies()
        {
            var result = new Dictionary<(int, int), NetProxy>();
            if (!Physical) return result;
            var state = NetState.Instance;
            if (state == null || state.NetworkManager == null || state.NetworkManager.SpawnManager == null) return result;
            foreach (var obj in state.NetworkManager.SpawnManager.SpawnedObjectsList)
            {
                var proxy = obj.GetComponent<NetProxy>();
                if (proxy == null || !proxy.IsSpawned || !proxy.FlyingNow || !ModAdvancedEconomy.IsFreightTag(proxy.EventTag) ||
                    NetState.RunnerOf(proxy.Station)?.OwnerClientId != proxy.OwnerClientId) continue;
                result[(proxy.Station, proxy.EventTag)] = proxy;
            }
            return result;
        }

        static bool FreightLegReady(Database db, Shipment ship, Dictionary<(int orbit, int tag), NetProxy> proxies)
        {
            if (!proxies.TryGetValue((ship.Orbit, ship.FreightTag), out var proxy)) return true;
            if (proxy.Status.Has(NetProxyStatus.EmpDisabled)) return false;
            var route = proxy.Spec?.spec?.route;
            if (route == null || route.points.Count == 0 || !proxy.ScanVisible) return false;
            var layout = GoF2Remake.World.OrbitLayout.Build(db, ship.Orbit);
            Vector3 end = GoF2Remake.World.OrbitLayout.ToUnity(layout.Place(route.points[0]));
            return NetGuard.Position(proxy.WorldPosition) && (proxy.WorldPosition - end).sqrMagnitude < 500f * 500f;
        }

        // A route carries at most one finite shipment. Debits, cargo in transit and arrivals live in the same snapshot.
        // A disabled or removed route freezes its shipment; it never silently redirects cargo to another endpoint.
        internal static Dictionary<(int station, int item), int> TickTrade(Database db, Func<int, bool> participates, Func<int, int> pressure, HashSet<int> changed, AdvancedEconomyRoutes graph, bool advance = true, bool dispatch = true)
        {
            if (!ModAdvancedEconomy.RegionalTrade) return new Dictionary<(int station, int item), int>();
            Load();
            if (!dispatch && shipments.Count == 0) return new Dictionary<(int station, int item), int>();
            var routes = ModAdvancedEconomy.Routes.Where(r => dispatch || shipments.ContainsKey(RouteKey(r))).ToList();
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
                if (dispatch)
                {
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
            bool physical = Physical;
            var freightProxies = advance && physical ? PhysicalFreightProxies() : null;
            foreach (var route in routes)
            {
                if (!participates(route.Source) || !participates(route.Destination) ||
                    !ModAdvancedEconomy.Eligible(db, route.Item)) continue;
                var from = ModAdvancedEconomy.At(route.Source);
                var to = ModAdvancedEconomy.At(route.Destination);
                if (from == null || to == null || !from.TryGetValue(route.Item, out var sourceRule) ||
                    !to.TryGetValue(route.Item, out var destinationRule)) continue;
                string key = RouteKey(route);
                shipments.TryGetValue(key, out var shipment);
                if (shipment != null && !advance) continue;
                int origin = physical && shipment != null && shipment.RemainingSeconds >= 0 ? shipment.Orbit : route.Source;
                var originStation = db.Stations.Find(s => s.index == origin);
                var destinationStation = db.Stations.Find(s => s.index == route.Destination);
                if (originStation == null || destinationStation == null) continue;
                var path = graph.Path(originStation.system, destinationStation.system);
                if (path == null)
                {
                    if (physical && shipment != null) { EnsureFreightRaid(db, shipment); SendDistress(db, route, shipment); ObserveFreightDelay(route, shipment, 1); }
                    continue;
                }
                var source = Get(route.Source, sourceRule);
                var destination = Get(route.Destination, destinationRule);
                var incomingKey = (route.Destination, route.Item);
                incoming.TryGetValue(incomingKey, out int reserved);
                if (shipment != null)
                {
                    if (physical)
                    {
                        int delay = freightProxies.TryGetValue((shipment.Orbit, shipment.FreightTag), out var freightProxy) && freightProxy.Status.Has(NetProxyStatus.EmpDisabled) ? 2 :
                            shipment.Orbit == route.Destination && destination.Amount >= destinationRule.Maximum ? 3 : 0;
                        ObserveFreightDelay(route, shipment, delay);
                        int beforeRaid = shipment.Amount;
                        bool arrived = AdvanceFreight(db, route, shipment, path, freightProxies);
                        // A same-system destination counts as departure from transit only when unloading can start.
                        if (arrived && shipment.Amount > 0 && shipment.Orbit == route.Destination && destination.Amount < destinationRule.Maximum)
                            ResolveFreightRaid(route, shipment);
                        reserved = Math.Max(0, reserved - (beforeRaid - shipment.Amount));
                        incoming[incomingKey] = reserved;
                        if (shipment.Amount == 0) { shipments.Remove(key); dirty = true; continue; }
                        if (!arrived) continue;
                    }
                    else
                    {
                        if (shipment.RemainingSeconds >= 0) { shipment.RemainingSeconds = -1; dirty = true; }
                        if (shipment.Ticks > 0) { shipment.Ticks--; dirty = true; }
                        if (shipment.Ticks > 0) continue;
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
                if (!dispatch) continue;
                int routePressure = Math.Max(pressure(route.Source), pressure(route.Destination));
                foreach (int system in path)
                {
                    if (!systemPressure.TryGetValue(system, out int p))
                        systemPressure[system] = p = ModAdvancedEconomy.SystemPressure(db, system);
                    routePressure = Math.Max(routePressure, p);
                }
                int send = ModAdvancedEconomy.DispatchAmount(source.Amount, sourceRule.Target,
                    destination.Amount + reserved, destinationRule.Target, route.Capacity);
                if (send <= 0) continue;
                int fleetRace = -1;
                ModAdvancedEconomy.FreightHull fleetHull = null;
                if (physical)
                {
                    fleetRace = OriginRace(db, route.Source);
                    var available = ModAdvancedEconomy.FreightFleet(fleetRace).Where(hull => db.Ship(hull.Ship) != null).ToArray();
                    bool emergency = ModAdvancedEconomy.FreightEmergencyDispatch &&
                        ModAdvancedEconomy.FreightStockCritical(destination.Amount, reserved, destinationRule.Target,
                            ModAdvancedEconomy.FreightCriticalStock);
                    fleetHull = ModAdvancedEconomy.PickDispatchHull(available, send, route.Capacity, destinationRule.Target,
                        ModAdvancedEconomy.FreightMinimumShipment, ModAdvancedEconomy.FreightMinimumUtilization,
                        emergency, UnityEngine.Random.Range(0, int.MaxValue));
                    if (fleetHull == null) continue;
                    send = Math.Min(send, fleetHull.Capacity);
                }
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
                    if (physical)
                    {
                        dispatched.FreightTag = NewFreightTag();
                        dispatched.Bulk = survive > ModAdvancedEconomy.CormorantCapacity;
                        dispatched.Orbit = route.Source;
                        dispatched.RemainingSeconds = TravelSeconds();
                        AssignFleet(dispatched, fleetRace, fleetHull);
                        EnsureFreightRaid(db, dispatched);
                        SendDistress(db, route, dispatched);
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
                    ["orbit"] = pair.Value.Orbit, ["totalTicks"] = pair.Value.TotalTicks, ["bulk"] = pair.Value.Bulk, ["hull"] = pair.Value.Hull,
                    ["remainingSeconds"] = pair.Value.RemainingSeconds, ["fleetRace"] = pair.Value.FleetRace,
                    ["fleetHull"] = pair.Value.FleetHull, ["fleetSpeed"] = pair.Value.FleetSpeed,
                    ["raidSystem"] = pair.Value.RaidSystem, ["raidBase"] = pair.Value.RaidBase,
                    ["raidDead"] = pair.Value.RaidDead, ["raidPlayerKills"] = pair.Value.RaidPlayerKills,
                    ["raidContributors"] = new JArray(pair.Value.RaidContributors), ["raidThieves"] = new JArray(pair.Value.RaidThieves),
                    ["raidObservationSeconds"] = pair.Value.RaidObservationSeconds, ["delayReason"] = pair.Value.DelayReason, ["delaySeconds"] = pair.Value.DelaySeconds });
            var debris = new JArray();
            foreach (var pair in wrecks)
            {
                var w = pair.Value;
                debris.Add(new JObject { ["tag"] = pair.Key, ["orbit"] = w.Orbit, ["amount"] = w.Amount, ["item"] = w.Item,
                    ["race"] = w.Race, ["expires"] = w.Expires, ["x"] = w.Position.x, ["y"] = w.Position.y, ["z"] = w.Position.z });
            }
            var rewards = new JArray(rescueRewards.Select(pair => new JObject { ["account"] = pair.Key, ["credits"] = pair.Value }));
            var root = new JObject { ["version"] = 1, ["rows"] = rows, ["shipments"] = cargo, ["freightWrecks"] = debris, ["rescueRewards"] = rewards };
            return root.ToString(Formatting.None);
        }

        // The station/profile write-ahead journal includes the entire economy snapshot.
        // Recovery uses absolute targets and therefore cannot debit or credit stock twice.
        internal static bool ValidateJournalSnapshot(string json)
        {
            try { Read(json); var root = JObject.Parse(json); ValidateFreightIdentities(ReadShipments(root), ReadWrecks(root)); ReadRescueRewards(root); return true; }
            catch (Exception e) { Debug.LogWarning($"Advanced economy: invalid journal snapshot ({e.Message})."); return false; }
        }

        internal static bool PersistJournalSnapshot(string json, bool recover)
        {
            if (string.IsNullOrEmpty(NetProfiles.Folder)) return false;
            var parsed = Read(json);
            var cargo = ReadShipments(JObject.Parse(json));
            var debris = ReadWrecks(JObject.Parse(json));
            var rewards = ReadRescueRewards(JObject.Parse(json));
            ValidateFreightIdentities(cargo, debris);
            string target = Path.Combine(NetProfiles.Folder, "advanced-economy-stock.json");
            if (!NetProfiles.Write(target, json)) return false;
            if (recover && loaded)
            {
                balances.Clear(); live.Clear(); shipments.Clear(); wrecks.Clear(); rescueRewards.Clear();
                foreach (var pair in parsed) balances.Add(pair.Key, pair.Value);
                foreach (var pair in cargo) shipments.Add(pair.Key, pair.Value);
                foreach (var pair in debris) wrecks.Add(pair.Key, pair.Value);
                foreach (var pair in rewards) rescueRewards.Add(pair.Key, pair.Value);
            }
            dirty = false;
            return true;
        }

        internal static void Flush(bool force = false)
        {
            if (!loaded || readFailed || path == null || NetPlayerStations.ProfileWritesBlocked ||
                (!force && (!Active || !ModAdvancedEconomy.SaveInventory || (freightNews.Count == 0 && Time.unscaledTime < nextSave)))) return;
            if (dirty)
            {
                nextSave = Time.unscaledTime + 10f;
                if (NetProfiles.Write(path, Snapshot())) dirty = false;
            }
            if (dirty) return;
            foreach (var entry in freightNews) NetNews.PostFreight(entry.text, entry.station, entry.destination);
            freightNews.Clear();
            SettleRescueRewards();
            if (distressSent.Count > shipments.Count + 16)
            {
                var active = new HashSet<int>(shipments.Values.Where(s => s.RaidBase != 0).Select(s => s.RaidBase));
                foreach (int tag in distressSent.Keys.Where(t => !active.Contains(t)).ToList()) distressSent.Remove(tag);
            }
        }
    }
}
