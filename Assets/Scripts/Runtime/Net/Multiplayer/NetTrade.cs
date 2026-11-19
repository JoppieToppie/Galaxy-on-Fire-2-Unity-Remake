// NetTrade.cs
// Remake-only, multiplayer, the server: trades between two players docked in the same hangar (NetTradeClient is a
// player's side, the Multiplayer window's Trade tab and the hangar's pilot list its UI). The server keeps every request
// and trade, but not the goods: credits and cargo are each player's own game's (like the shop's), so a trade runs in steps:
//   a request (the pilot list's Trade, /trade <pilot>): only to a player docked at the same station, answered within 60 s;
//   accepted: the trade is open; each side puts credits and cargo items into its offer (NetTradeClient sends the whole
//     offer when it changes); every change makes a new version and takes back both accepts, so nobody can swap an offer
//     after the other side looked at it;
//   both accept the same version: each game is asked to pay its own offer (TradePayRpc: it checks it still has it, takes it
//     out of the hold and answers); only once both have paid does each get the other's offer (TradeDeliverRpc); a side
//     that can't pay (sold meanwhile), doesn't answer within 15 s or leaves ends it, and whoever paid gets their own offer
//     back (a payment arriving after that is sent back too);
//   either side cancels, takes off, leaves the station or the session: the trade ends (before the payment).
// A profile's recorded worth moves with what it gave and got (NetProfiles.AdjustWorth), so the upload check neither turns
// the next upload away nor lets a trade nobody paid for through; the credits move in its saved game too
// (NetProfiles.MoveCredits): each side's offer out as its payment comes in (Debit), back if the trade fails, the other
// side's in when it completes. Story items (Session.Unsaleable) are refused by the paying
// game; the Courier's secure containers and the passenger cabins by the server too.

using System.Collections.Generic;
using GoF2Remake.Data;
using UnityEngine;

namespace GoF2Remake.Multiplayer
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class NetTrade
    {
        /// <summary>How long a request waits for its answer; how long the payments may take; how long a failed trade is kept
        /// for late payments (they are sent back).</summary>
        public const float RequestSeconds = 60f, PaySeconds = 15f, FailedKeepSeconds = 60f;
        /// <summary>The most item kinds and units of one kind an offer may hold.</summary>
        public const int MaxKinds = 16, MaxUnits = 100000;

        public enum Phase : byte { Open, Paying, Done }

        public sealed class Offer
        {
            public long credits;
            public readonly List<ItemStack> items = new List<ItemStack>();

            public bool Empty => credits <= 0 && items.Count == 0;

            /// <summary>The credits (64-bit: low, high), then item / amount pairs (the RPCs' form).</summary>
            public int[] Pack()
            {
                var a = new int[2 + items.Count * 2];
                a[0] = (int)(credits & 0xFFFFFFFFL);
                a[1] = (int)(credits >> 32);
                for (int i = 0; i < items.Count; i++) { a[2 + i * 2] = items[i].item; a[3 + i * 2] = items[i].amount; }
                return a;
            }

            /// <summary>The 64-bit credits of a packed offer (0 when it is too short).</summary>
            public static long PackedCredits(int[] a) => a == null || a.Length < 2 ? 0L : ((long)a[1] << 32) | (uint)a[0];

            /// <summary>The packed form back (no checks: NetTradeClient's side, from the server).</summary>
            public static Offer Unpack(int[] a)
            {
                var o = new Offer();
                if (a == null || a.Length < 2) return o;
                o.credits = System.Math.Max(0L, PackedCredits(a));
                for (int i = 2; i + 1 < a.Length; i += 2) if (a[i + 1] > 0) o.items.Add(new ItemStack(a[i], a[i + 1]));
                return o;
            }

            public bool Same(Offer other)
            {
                if (other == null || other.credits != credits || other.items.Count != items.Count) return false;
                for (int i = 0; i < items.Count; i++)
                    if (items[i].item != other.items[i].item || items[i].amount != other.items[i].amount) return false;
                return true;
            }
        }

        sealed class Trade
        {
            public int id, station, version;
            public ulong a, b;
            public Offer offerA = new Offer(), offerB = new Offer();
            public bool acceptA, acceptB, answeredA, answeredB, paidA, paidB;
            /// <summary>The sides' profiles (taken when the payments start: a side leaving takes its login with it) and
            /// whether their offered credits are out of the saved game (Debit).</summary>
            public string accA, accB;
            public bool debitedA, debitedB;
            public Phase phase;
            public float payStart, failedAt;

            public bool Has(ulong client) => client == a || client == b;
            public ulong Other(ulong client) => client == a ? b : a;
            public Offer OfferOf(ulong client) => client == a ? offerA : offerB;
        }

        static readonly Dictionary<(ulong from, ulong to), float> requests = new Dictionary<(ulong, ulong), float>();
        static readonly Dictionary<int, Trade> trades = new Dictionary<int, Trade>();
        static readonly Dictionary<int, Trade> failed = new Dictionary<int, Trade>();   // late payments go back
        static int nextId = 1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Reset();

        /// <summary>A new session (NetState.OnNetworkSpawn on the server).</summary>
        public static void Reset()
        {
            requests.Clear();
            trades.Clear();
            failed.Clear();
            nextId = 1;
        }

        static string X(string key, string english) => Localization.Extra(key, english);

        static Trade Of(ulong client)
        {
            foreach (var t in trades.Values) if (t.Has(client)) return t;
            return null;
        }

        static bool Docked(NetPlayer p, int station) => p != null && p.IsSpawned && p.InHangar && p.Station == station;

        /// <summary>An item a trade may move: one that exists, not a Courier's container or a passenger cabin.</summary>
        public static bool Tradeable(int item) => NetGuard.Item(item) && item != Freelance.SecureContainer && item != Freelance.SecureCabin;

        // ---- requests -----------------------------------------------------------------------------------------

        /// <summary>'from' asks 'to' to trade (the pilot list's Trade, /trade): the answer for 'from'.</summary>
        internal static string Request(NetPlayer from, NetPlayer to)
        {
            if (from == null || to == null) return "";
            if (from == to) return X("mpTradeSelf", "You can't trade with yourself.");
            if (!from.InHangar || !to.InHangar || from.Station != to.Station)
                return X("mpTradeHangarOnly", "Trades are only possible while docked in the same hangar.");
            if (Of(from.OwnerClientId) != null) return X("mpTradeBusySelf", "You are trading already.");
            if (Of(to.OwnerClientId) != null) return string.Format(X("mpTradeBusyOther", "{0} is trading with someone else."), to.DisplayName);
            requests[(from.OwnerClientId, to.OwnerClientId)] = Time.unscaledTime;
            NetState.Instance?.TradeRequested(to.OwnerClientId, from.OwnerClientId, from.DisplayName);
            return string.Format(X("mpTradeSent", "Trade request sent to {0}."), to.DisplayName);
        }

        /// <summary>'client' accepted the request of 'from': the trade opens (both still docked there, neither trading).</summary>
        internal static void Accept(ulong client, ulong from)
        {
            var asker = NetSquad.Find(from);
            var me = NetSquad.Find(client);
            if (asker == null || me == null) return;
            if (!requests.TryGetValue((from, client), out float sent) || Time.unscaledTime - sent > RequestSeconds + 10f)
            {
                NetRateLimit.Reject(client, $"a trade with {asker.DisplayName} without a request");
                return;
            }
            requests.Remove((from, client));
            if (!asker.InHangar || !me.InHangar || asker.Station != me.Station)
            {
                Notify(client, X("mpTradeHangarOnly", "Trades are only possible while docked in the same hangar."));
                return;
            }
            if (Of(from) != null || Of(client) != null)
            {
                Notify(client, string.Format(X("mpTradeBusyOther", "{0} is trading with someone else."), asker.DisplayName));
                return;
            }
            requests.Remove((client, from));   // the same two asked each other: one trade
            var t = new Trade { id = nextId++, a = from, b = client, station = me.Station, version = 1 };
            trades[t.id] = t;
            SendState(t);
        }

        /// <summary>'client' said no to 'from''s request.</summary>
        internal static void Decline(ulong client, ulong from)
        {
            if (!requests.Remove((from, client))) return;
            var me = NetSquad.Find(client);
            if (me != null) Notify(from, string.Format(X("mpTradeDeclined", "{0} declined the trade."), me.DisplayName));
        }

        // ---- the open trade --------------------------------------------------------------------------------------

        /// <summary>'client''s whole offer (NetTradeClient): checked, a new version, both accepts taken back.</summary>
        internal static void SetOffer(ulong client, int id, int[] packed)
        {
            if (!trades.TryGetValue(id, out var t) || !t.Has(client) || t.phase != Phase.Open) return;
            if (!TryParse(packed, out var offer, out string problem)) { NetRateLimit.Reject(client, "a trade offer with " + problem); return; }
            var current = t.OfferOf(client);
            if (current.Same(offer)) return;
            if (client == t.a) t.offerA = offer; else t.offerB = offer;
            t.version++;
            t.acceptA = t.acceptB = false;
            SendState(t);
        }

        static bool TryParse(int[] packed, out Offer offer, out string problem)
        {
            offer = new Offer();
            problem = null;
            if (packed == null || packed.Length < 2 || packed.Length % 2 == 1 || packed.Length > 2 + MaxKinds * 2) { problem = "a bad length"; return false; }
            offer.credits = Offer.PackedCredits(packed);
            if (offer.credits < 0) { problem = "negative credits"; return false; }
            var seen = new HashSet<int>();
            for (int i = 2; i < packed.Length; i += 2)
            {
                int item = packed[i], amount = packed[i + 1];
                if (!Tradeable(item) || amount < 1 || amount > MaxUnits || !seen.Add(item)) { problem = $"item {item} x {amount}"; return false; }
                offer.items.Add(new ItemStack(item, amount));
            }
            offer.items.Sort((x, y) => x.item.CompareTo(y.item));
            return true;
        }

        /// <summary>'client' accepts the trade as it is at 'version' (an older version: the offers changed since, ignored).</summary>
        internal static void Confirm(ulong client, int id, int version)
        {
            if (!trades.TryGetValue(id, out var t) || !t.Has(client) || t.phase != Phase.Open || version != t.version) return;
            if (t.offerA.Empty && t.offerB.Empty) return;   // nothing to trade
            if (client == t.a) t.acceptA = true; else t.acceptB = true;
            if (t.acceptA && t.acceptB) StartPaying(t);
            else SendState(t);
        }

        /// <summary>'client' cancels (before the payment).</summary>
        internal static void Cancel(ulong client, int id)
        {
            if (!trades.TryGetValue(id, out var t) || !t.Has(client) || t.phase != Phase.Open) return;
            var me = NetSquad.Find(client);
            End(t, string.Format(X("mpTradeCancelled", "{0} cancelled the trade."), me != null ? me.DisplayName : "?"));
        }

        // ---- the exchange ------------------------------------------------------------------------------------------

        static void StartPaying(Trade t)
        {
            // Credits are checked against each profile's saved game (NetProfiles.MoveCredits): a game claiming credits it
            // never had can't hand them to another player.
            string who = NetProfiles.Enabled ? Unsaved(t.a, t.offerA) ?? Unsaved(t.b, t.offerB) : null;
            if (who != null)
            {
                End(t, string.Format(X("mpTradeUnsaved", "The trade fell through: {0}'s saved game doesn't hold the credits offered (dock again to save it)."), who));
                return;
            }
            t.phase = Phase.Paying;
            t.payStart = Time.unscaledTime;
            t.accA = NetProfiles.AccountOf(t.a);
            t.accB = NetProfiles.AccountOf(t.b);
            SendState(t);
            NetState.Instance?.TradePay(t.a, t.id, t.offerA.Pack());
            NetState.Instance?.TradePay(t.b, t.id, t.offerB.Pack());
        }

        /// <summary>'client''s game paid its offer (or couldn't).</summary>
        internal static void Paid(ulong client, int id, bool ok)
        {
            if (failed.TryGetValue(id, out var gone) && gone.Has(client))
            {
                // Paid after the trade had already failed: it goes back.
                if (ok && !(client == gone.a ? gone.paidA : gone.paidB))
                {
                    if (client == gone.a) gone.paidA = true; else gone.paidB = true;
                    Refund(gone, client);
                }
                return;
            }
            if (!trades.TryGetValue(id, out var t) || !t.Has(client) || t.phase != Phase.Paying) return;
            if (client == t.a) { if (t.answeredA) return; t.answeredA = true; t.paidA = ok; }
            else { if (t.answeredB) return; t.answeredB = true; t.paidB = ok; }
            if (!ok)
            {
                var me = NetSquad.Find(client);
                Fail(t, string.Format(X("mpTradeNotPaid", "The trade fell through: {0} no longer has everything offered."), me != null ? me.DisplayName : "?"));
                return;
            }
            if (!Debit(t, client))
            {
                // Spent since StartPaying's check: the game paid, so Fail gives it back.
                var me = NetSquad.Find(client);
                Fail(t, string.Format(X("mpTradeUnsaved", "The trade fell through: {0}'s saved game doesn't hold the credits offered (dock again to save it)."),
                                      me != null ? me.DisplayName : "?"));
                return;
            }
            if (t.answeredA && t.answeredB) Complete(t);
        }

        /// <summary>A side's offered credits out of its profile's saved game, as its payment comes in. Then, not at Complete:
        /// the answer and the game's uploads travel in order (the reliable pipeline), so an upload sent after paying (the
        /// periodic one, while the other side still pays) already has them out, and taking them at Complete took them twice.
        /// False when the saved game hasn't them.</summary>
        static bool Debit(Trade t, ulong client)
        {
            string account = client == t.a ? t.accA : t.accB;
            long credits = t.OfferOf(client).credits;
            if (account == null || credits <= 0) return true;
            if (!NetProfiles.MoveCredits(client, account, -credits)) return false;
            if (client == t.a) t.debitedA = true; else t.debitedB = true;
            return true;
        }

        static void Complete(Trade t)
        {
            t.phase = Phase.Done;
            trades.Remove(t.id);
            var pa = NetSquad.Find(t.a);
            var pb = NetSquad.Find(t.b);
            NetState.Instance?.TradeDeliver(t.a, t.id, t.offerB.Pack(), false, pb != null ? pb.DisplayName : "");
            NetState.Instance?.TradeDeliver(t.b, t.id, t.offerA.Pack(), false, pa != null ? pa.DisplayName : "");
            // The profiles: the goods' value in their recorded worth, the other side's credits into their saved games (their
            // own went out as they paid, Debit).
            long ga = Value(t.offerA) - t.offerA.credits, gb = Value(t.offerB) - t.offerB.credits;
            if (t.accA != null)
            {
                NetProfiles.AdjustWorth(t.accA, gb - ga);
                MoveCredits(t.a, t.accA, t.offerB.credits);
            }
            if (t.accB != null)
            {
                NetProfiles.AdjustWorth(t.accB, ga - gb);
                MoveCredits(t.b, t.accB, t.offerA.credits);
            }
            Debug.Log($"Server: trade {t.id} between {(pa != null ? pa.DisplayName : t.a.ToString())} and {(pb != null ? pb.DisplayName : t.b.ToString())} done "
                      + $"({t.offerA.credits:N0} cr + {t.offerA.items.Count} item(s) for {t.offerB.credits:N0} cr + {t.offerB.items.Count} item(s)).");
            NetState.Instance?.TradeEnded(t.a, t.id, "");
            NetState.Instance?.TradeEnded(t.b, t.id, "");
        }

        /// <summary>The exchange failed: whoever paid gets their offer back; kept a while for late payments.</summary>
        static void Fail(Trade t, string reason)
        {
            trades.Remove(t.id);
            t.phase = Phase.Done;
            t.failedAt = Time.unscaledTime;
            failed[t.id] = t;
            if (t.paidA) Refund(t, t.a);
            if (t.paidB) Refund(t, t.b);
            NetState.Instance?.TradeEnded(t.a, t.id, reason);
            NetState.Instance?.TradeEnded(t.b, t.id, reason);
        }

        static void Refund(Trade t, ulong client)
        {
            // Credits Debit took out of the saved game go back into it, also for a side that has left: its game's last
            // upload (leaving, after paying) or the saved game before the payment, either way it is owed them.
            bool debited = client == t.a ? t.debitedA : t.debitedB;
            if (debited)
            {
                if (client == t.a) t.debitedA = false; else t.debitedB = false;
                MoveCredits(client, client == t.a ? t.accA : t.accB, t.OfferOf(client).credits);
            }
            if (NetSquad.Find(client) == null) return;   // gone: their game never kept the payment (the profile has the last upload)
            NetState.Instance?.TradeDeliver(client, t.id, t.OfferOf(client).Pack(), true, "");
        }

        /// <summary>The trade ends before any payment (cancelled, a side left).</summary>
        static void End(Trade t, string reason)
        {
            trades.Remove(t.id);
            NetState.Instance?.TradeEnded(t.a, t.id, reason);
            NetState.Instance?.TradeEnded(t.b, t.id, reason);
        }

        /// <summary>The pilot's name when 'client''s saved game holds fewer credits than its offer, else null.</summary>
        static string Unsaved(ulong client, Offer offer)
        {
            if (offer.credits <= 0) return null;
            string account = NetProfiles.AccountOf(client);
            if (account == null || NetProfiles.SpendableCredits(client, account) >= offer.credits) return null;
            var p = NetSquad.Find(client);
            return p != null ? p.DisplayName : "?";
        }

        /// <summary>Credits a trade gives 'client' (received, or its own back) in its saved game; the worth alone if the saved
        /// game can't take them (unwritable: its next upload settles it).</summary>
        static void MoveCredits(ulong client, string account, long delta)
        {
            if (delta == 0 || NetProfiles.MoveCredits(client, account, delta)) return;
            Debug.LogWarning($"Server: trade credits {delta:N0} didn't fit profile {account}'s saved game: only its worth moved.");
            NetProfiles.AdjustWorth(account, delta);
        }

        /// <summary>What an offer is worth to the profiles' upload check (NetProfiles.Worth: items at their lowest price).</summary>
        static long Value(Offer o)
        {
            long v = o.credits;
            foreach (var s in o.items) v += (long)(NetGame.Db.Item(s.item)?.minPrice ?? 0) * s.amount;
            return v;
        }

        static void SendState(Trade t)
        {
            var pa = NetSquad.Find(t.a);
            var pb = NetSquad.Find(t.b);
            NetState.Instance?.TradeState(t.a, t.id, t.b, pb != null ? pb.DisplayName : "", t.station, t.version,
                                          t.offerA.Pack(), t.offerB.Pack(), t.acceptA, t.acceptB, (byte)t.phase);
            NetState.Instance?.TradeState(t.b, t.id, t.a, pa != null ? pa.DisplayName : "", t.station, t.version,
                                          t.offerB.Pack(), t.offerA.Pack(), t.acceptB, t.acceptA, (byte)t.phase);
        }

        static void Notify(ulong client, string text) => NetState.Instance?.Notify(client, text);

        // ---- upkeep -------------------------------------------------------------------------------------------------

        static readonly List<(ulong, ulong)> expired = new List<(ulong, ulong)>();
        static readonly List<Trade> ending = new List<Trade>();

        /// <summary>Server, every frame (NetState.Update): old requests go; a trade whose side left the hangar ends; payments
        /// that didn't come in time fail the exchange.</summary>
        public static void Tick()
        {
            float now = Time.unscaledTime;
            if (requests.Count > 0)
            {
                expired.Clear();
                foreach (var r in requests) if (now - r.Value > RequestSeconds + 10f) expired.Add(r.Key);
                foreach (var k in expired) requests.Remove(k);
            }
            if (failed.Count > 0)
            {
                ending.Clear();
                foreach (var t in failed.Values) if (now - t.failedAt > FailedKeepSeconds) ending.Add(t);
                foreach (var t in ending) failed.Remove(t.id);
            }
            if (trades.Count == 0) return;
            ending.Clear();
            ending.AddRange(trades.Values);
            foreach (var t in ending)
            {
                if (t.phase == Phase.Open)
                {
                    var pa = NetSquad.Find(t.a);
                    var pb = NetSquad.Find(t.b);
                    if (!Docked(pa, t.station)) End(t, Left(pa));
                    else if (!Docked(pb, t.station)) End(t, Left(pb));
                }
                else if (t.phase == Phase.Paying && now - t.payStart > PaySeconds)
                    Fail(t, X("mpTradeTimeout", "The trade fell through: no answer in time."));
            }
        }

        static string Left(NetPlayer p) =>
            string.Format(X("mpTradeLeft", "The trade ended: {0} left the hangar."), p != null ? p.DisplayName : X("mpTradeOtherPilot", "the other pilot"));

        /// <summary>A player left the session: their requests go, their trade ends (an exchange in progress fails).</summary>
        public static void OnDisconnect(ulong client)
        {
            expired.Clear();
            foreach (var r in requests.Keys) if (r.from == client || r.to == client) expired.Add(r);
            foreach (var k in expired) requests.Remove(k);
            var t = Of(client);
            if (t == null) return;
            string reason = X("mpTradeGone", "The trade ended: the other pilot left the session.");
            if (t.phase == Phase.Paying) Fail(t, reason);
            else End(t, reason);
        }
    }
}
