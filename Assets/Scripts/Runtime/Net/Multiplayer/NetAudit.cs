// NetAudit.cs
// Remake-only: the dedicated server's money log, for spotting profiles that grow faster than play allows (the client runs
// the economy, so the server sees only what each game says: NetProfiles' uploads, the shop trades sent for the shared
// stock, faction deposits / withdrawals / tolls). All of it goes to the server log with an "Server: audit" prefix, so
//   grep "Server: audit" (or journalctl -u gof2 | grep audit)
// lists it.
//   Uploads (NetProfiles.Receive): an upload whose worth grew by LogGrowth or more since the last accepted one (or that was
//     refused) logs the growth per part (credits, ship, equipment, cargo, Kaamo Club, parked ship), the minutes since,
//     the rate per minute, the ship change and the biggest item gains; LogWarning from SuspectPerMinute up.
//   Shop trades (NetState.StockItemRpc): each player's buys and sells of one item at one station are summed while they
//     come and logged once they stop for FlushSeconds ("sold 120 x Gold at Festus for 72,000, 600 each"); a unit price
//     outside the item's price range (the station's price lies between its min and max, the faction cut and the VAT a
//     little beyond) is a warning: the game claimed a price no station has.
//   Faction money (NetFactions): every deposit, withdrawal and toll with the bank after it.
// Nothing here changes what the server accepts.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using GoF2Remake.Data;
using UnityEngine;

namespace GoF2Remake.Multiplayer
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class NetAudit
    {
        /// <summary>An upload's worth growth that gets a line.</summary>
        const long LogGrowth = 100_000;
        /// <summary>Growth per minute that is a warning (honest trading with a full big hold rarely passes it).</summary>
        const long SuspectPerMinute = 150_000;
        /// <summary>A shop run ends this long after its last unit.</summary>
        const float FlushSeconds = 8f;

        // ---- what a save is worth, by part (NetProfiles.Worth's sum) -------------------------------------------

        internal struct Parts
        {
            public long credits, ship, equipment, cargo, kaamo, parked;
            public long Total => SumWorth(credits, ship, equipment, cargo, kaamo, parked);
        }

        internal static long SumWorth(params long[] values)
        {
            long result = 0;
            foreach (long value in values)
            {
                long positive = Math.Max(0L, value);
                if (result > long.MaxValue - positive) return long.MaxValue;
                result += positive;
            }
            return result;
        }

        internal static Parts Of(SaveData s)
        {
            var p = new Parts();
            if (s == null) return p;
            var db = NetGame.Db;
            long ShipPrice(int ship) => db.Ship(ship)?.price ?? 0;
            p.credits = s.credits;
            p.ship = s.deathLoaner ? 0 : ShipPrice(s.ship);
            p.equipment = Items(s.equipment);
            p.cargo = Items(s.cargo);
            p.kaamo = Items(s.kaamoItems);
            if (s.kaamoShips != null) foreach (var k in s.kaamoShips) if (k != null) p.kaamo = SumWorth(p.kaamo, ShipPrice(k.ship), Items(k.equipment));
            if (s.hasParkedShip && s.parkedShip != null) p.parked = SumWorth(ShipPrice(s.parkedShip.ship), Items(s.parkedShip.equipment), Items(s.parkedShip.cargo));
            return p;
        }

        static long Items(List<ItemStack> list)
        {
            long v = 0;
            if (list != null) foreach (var st in list) if (st != null && !st.builtIn) v = SumWorth(v, (long)Math.Max(0, NetGame.Db.Item(st.item)?.minPrice ?? 0) * Math.Max(0, st.amount));   // built-ins are the hull's
            return v;
        }

        /// <summary>Every item the save holds anywhere, item -> units.</summary>
        static Dictionary<int, long> Holdings(SaveData s)
        {
            var d = new Dictionary<int, long>();
            void Add(List<ItemStack> list)
            {
                if (list == null) return;
                foreach (var st in list)
                    if (st != null && st.amount > 0 && !st.builtIn) d[st.item] = (d.TryGetValue(st.item, out long n) ? n : 0) + st.amount;
            }
            if (s == null) return d;
            Add(s.equipment);
            Add(s.cargo);
            Add(s.kaamoItems);
            if (s.kaamoShips != null) foreach (var k in s.kaamoShips) if (k != null) Add(k.equipment);
            if (s.hasParkedShip && s.parkedShip != null) { Add(s.parkedShip.equipment); Add(s.parkedShip.cargo); }
            return d;
        }

        // Report-only provenance: receipts for server-granted shop purchases since the last accepted upload.
        // Station transactions already update the persisted profile, so their gains are in the old baseline.
        // Client-only loot, crafting and rewards may legitimately lack a receipt: never enforce on this report.
        static readonly Dictionary<(string account, int item), long> itemReceipts = new Dictionary<(string, int), long>();

        static void AuditItemGains(string id, string name, ulong client, SaveData old, SaveData now, bool accepted)
        {
            if (string.IsNullOrEmpty(id) || now == null) return;
            if (old != null)
            {
                var before = Holdings(old);
                var after = Holdings(now);
                foreach (var pair in after)
                {
                    long previous = before.TryGetValue(pair.Key, out long count) ? count : 0;
                    long gain = pair.Value - previous;
                    if (gain <= 0) continue;
                    long traced = itemReceipts.TryGetValue((id, pair.Key), out long receipt) ? Math.Min(gain, receipt) : 0;
                    if (gain <= traced) continue;
                    long fitted = 0;
                    if (now.equipment != null)
                        foreach (var stack in now.equipment)
                            if (stack != null && !stack.builtIn && stack.item == pair.Key && stack.amount > 0) fitted += stack.amount;
                    Debug.LogWarning($"Server: audit item UNVERIFIED {DateTime.UtcNow:O}: profile {id} ({name}, client {client}); {ItemName(pair.Key)}; previous {previous}, current {pair.Value}, gained {gain}, shop-traced {traced}, unexplained {gain - traced}, fitted {fitted}; upload {(accepted ? "accepted" : "refused")}; review only, acquisition source not verified.");
                }
            }
            // A refused upload cannot consume evidence. Accepted snapshots become the next baseline.
            if (accepted)
                foreach (var key in itemReceipts.Keys.Where(key => key.account == id).ToList())
                    itemReceipts.Remove(key);
        }

        // ---- uploads ------------------------------------------------------------------------------------------

        /// <summary>NetProfiles.Receive: an upload checked against the profile's last accepted one ('old', null = none on
        /// disk). Logs only big growth or a refusal ('refused' = the reason).</summary>
        internal static void Upload(string id, string name, ulong client, SaveData old, SaveData now, long oldWorth, float minutes, string refused)
        {
            AuditItemGains(id, name, client, old, now, refused == null);
            var b = Of(now);
            long growth = b.Total - oldWorth;
            if (refused == null && growth < LogGrowth) return;
            var a = Of(old);
            var sb = new StringBuilder();
            sb.Append($"Server: audit {id} ({name}, client {client}): worth {oldWorth:N0} -> {b.Total:N0} ({Signed(growth)}) in {minutes:0.#} min");
            if (minutes > 0.05f) sb.Append($", {Signed((long)(growth / minutes))}/min");
            if (old != null)
            {
                sb.Append($"; credits {Signed(b.credits - a.credits)}, ship {Signed(b.ship - a.ship)}, equipment {Signed(b.equipment - a.equipment)}, cargo {Signed(b.cargo - a.cargo)}");
                if (b.kaamo != a.kaamo) sb.Append($", Kaamo {Signed(b.kaamo - a.kaamo)}");
                if (b.parked != a.parked) sb.Append($", parked {Signed(b.parked - a.parked)}");
                if (old.ship != now.ship) sb.Append($"; ship {ShipName(old.ship)} -> {ShipName(now.ship)}");
                var before = Holdings(old);
                var gains = new List<(int item, long units, long value)>();
                foreach (var kv in Holdings(now))
                {
                    long units = kv.Value - (before.TryGetValue(kv.Key, out long n) ? n : 0);
                    if (units > 0) gains.Add((kv.Key, units, units * (long)(NetGame.Db.Item(kv.Key)?.minPrice ?? 0)));
                }
                if (gains.Count > 0)
                    sb.Append("; gained ").Append(string.Join(", ", gains.OrderByDescending(g => g.value).Take(5)
                        .Select(g => $"{g.units} x {ItemName(g.item)} ({g.value:N0})")));
            }
            else sb.Append($"; no earlier save on disk (credits {b.credits:N0}, ship {ShipName(now.ship)})");
            sb.Append($"; played {now.playSeconds / 60f:0} min in all");
            if (refused != null) sb.Append($"; REFUSED: {refused}");
            bool suspect = refused != null || (minutes > 0.05f && growth / minutes >= SuspectPerMinute) || (minutes <= 0.05f && growth >= LogGrowth * 10);
            if (suspect) Debug.LogWarning(sb.ToString());
            else Debug.Log(sb.ToString());
        }

        // ---- shop trades --------------------------------------------------------------------------------------

        class Run { public ulong client; public int station, item; public bool buy; public long units, value; public int min = int.MaxValue, max; public float last; }

        static readonly Dictionary<(ulong, int, int, bool), Run> runs = new Dictionary<(ulong, int, int, bool), Run>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { runs.Clear(); itemReceipts.Clear(); }

        /// <summary>NetState.StockItemRpc: 'units' of 'item' bought (delta &lt; 0) or sold at 'station', 'price' a unit (the
        /// game's own figure); only what the host granted.</summary>
        internal static void Trade(ulong client, int station, int item, int delta, int price)
        {
            if (delta == 0) return;
            bool buy = delta < 0;
            string account = NetProfiles.AccountOf(client);
            if (!string.IsNullOrEmpty(account) && buy)
            {
                var receiptKey = (account, item);
                long prior = itemReceipts.TryGetValue(receiptKey, out long existing) ? existing : 0;
                itemReceipts[receiptKey] = prior + Math.Abs((long)delta);
            }
            var key = (client, station, item, buy);
            if (!runs.TryGetValue(key, out var r)) runs[key] = r = new Run { client = client, station = station, item = item, buy = buy };
            long units = Math.Abs((long)delta);
            r.units += units;
            r.value += units * price;
            r.min = Math.Min(r.min, price);
            r.max = Math.Max(r.max, price);
            r.last = Time.realtimeSinceStartup;
        }

        /// <summary>NetState.Update (server): the runs that stopped are logged; 'all' = every run (the session ends).</summary>
        internal static void Tick(bool all = false)
        {
            if (runs.Count == 0) return;
            float now = Time.realtimeSinceStartup;
            List<(ulong, int, int, bool)> done = null;
            foreach (var kv in runs)
                if (all || now - kv.Value.last >= FlushSeconds) (done ??= new List<(ulong, int, int, bool)>()).Add(kv.Key);
            if (done == null) return;
            foreach (var key in done)
            {
                Log(runs[key]);
                runs.Remove(key);
            }
        }

        static void Log(Run r)
        {
            var it = NetGame.Db.Item(r.item);
            string who = PilotName(r.client);
            string each = r.min == r.max ? $"{r.min:N0} each" : $"{r.min:N0}..{r.max:N0} each";
            string line = $"Server: audit shop: {who} {(r.buy ? "bought" : "sold")} {r.units} x {ItemName(r.item)} at {StationName(r.station)} for {r.value:N0} ({each})";
            // A station's price lies between the item's min and max price; a faction member buys 10 % under it, a VAT
            // payer up to 2 % over it. Past that the game claimed a price no station asks.
            bool odd = it != null && it.maxPrice > 0 && (r.max > it.maxPrice * 1.05f || (r.buy && r.min < it.minPrice * 0.88f) || (!r.buy && r.min < it.minPrice * 0.5f));
            if (odd) Debug.LogWarning($"{line}; PRICE OUTSIDE the item's {it.minPrice:N0}..{it.maxPrice:N0}");
            else Debug.Log(line);
        }

        // ---- faction money ------------------------------------------------------------------------------------

        /// <summary>NetFactions: credits moved into (+) or out of (-) a faction's bank.</summary>
        internal static void Bank(string tag, string what, string pilot, long amount, long bank) =>
            Debug.Log($"Server: audit bank [{tag}]: {pilot} {what} {amount:N0}; bank {bank:N0}");

        // ---- names --------------------------------------------------------------------------------------------

        static string Signed(long v) => v >= 0 ? $"+{v:N0}" : v.ToString("N0");
        static string ItemName(int item) => NetGame.Db.Item(item)?.name is string n && n.Length > 0 ? $"{n} ({item})" : $"item {item}";
        static string ShipName(int ship) => NetGame.Db.Ship(ship)?.name is string n && n.Length > 0 ? $"{n} ({ship})" : $"ship {ship}";

        static string StationName(int station)
        {
            var st = NetGame.Db.Stations.Find(s => s.index == station);
            return st != null ? st.name : $"station {station}";
        }

        static string PilotName(ulong client)
        {
            var p = NetSquad.Find(client);
            string account = NetProfiles.AccountOf(client);
            string name = p != null ? p.DisplayName : $"client {client}";
            return account != null ? $"{name} ({account})" : name;
        }
    }
}
