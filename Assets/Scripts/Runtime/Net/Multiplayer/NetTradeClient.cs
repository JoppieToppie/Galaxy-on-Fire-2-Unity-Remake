// NetTradeClient.cs
// Remake-only, multiplayer: a player's side of the trades (NetTrade is the server's): the requests waiting for an answer,
// the open trade as the server last sent it, and this player's offer being edited (Draft: sent whole a moment after the
// last change, kept within what the hold and the wallet have). When the server asks, the game pays its offer (credits off,
// cargo out of the hold) and answers; it then gets the other side's offer, or its own back when the exchange fell through.
// Only cargo items and credits are traded: ships, mounted equipment and story items (Session.Unsaleable) never are.
// The UI: the Multiplayer window's Trade tab, the hangar's pilot list (SquadView), /trade.

using System;
using System.Collections.Generic;
using GoF2Remake.Data;
using UnityEngine;

namespace GoF2Remake.Multiplayer
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class NetTradeClient
    {
        /// <summary>A change to the offer goes to the server this long after the last one (a held "+" sends once).</summary>
        const float SendDelay = 0.3f;
        /// <summary>Accepting waits this long after the other side's offer changed (a last-moment swap stays visible).</summary>
        public const float ChangedHoldSeconds = 2f;

        public sealed class Request
        {
            public ulong from;
            public string name;
            public float time;
        }

        /// <summary>The open trade as the server last sent it.</summary>
        public sealed class View
        {
            public int id, station, version;
            public ulong partner;
            public string partnerName;
            public NetTrade.Offer mine = new NetTrade.Offer(), theirs = new NetTrade.Offer();
            public bool myAccepted, theirAccepted;
            public NetTrade.Phase phase;
            public float theirChangedAt = -100f;
        }

        static readonly List<Request> requests = new List<Request>();
        static readonly Dictionary<ulong, float> asked = new Dictionary<ulong, float>();
        static NetTrade.Offer draft = new NetTrade.Offer();
        static bool dirty;
        static float sendAt;

        /// <summary>The open trade (null = none).</summary>
        public static View Current { get; private set; }

        /// <summary>Counts every change (requests, the trade, the draft): the UI rebuilds when it moves.</summary>
        public static int Revision { get; private set; }

        /// <summary>A trade opened (the Multiplayer window shows its Trade tab).</summary>
        public static event Action Opened;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Clear(); Opened = null; }

        static string X(string key, string english) => Localization.Extra(key, english);

        static NetState State => NetState.Instance != null && NetState.Instance.IsSpawned ? NetState.Instance : null;

        static void Bump() => Revision++;

        /// <summary>The requests still waiting (expired ones and those of players gone dropped).</summary>
        public static IReadOnlyList<Request> Requests
        {
            get
            {
                int before = requests.Count;
                requests.RemoveAll(r => Time.unscaledTime - r.time > NetTrade.RequestSeconds || NetSquad.Find(r.from) == null);
                if (requests.Count != before) Bump();
                return requests;
            }
        }

        /// <summary>This player asked 'p' to trade a moment ago (the pilot list shows "Asked").</summary>
        public static bool WasAsked(NetPlayer p) => p != null && asked.TryGetValue(p.OwnerClientId, out float t) && Time.unscaledTime - t < NetTrade.RequestSeconds;

        /// <summary>This player's offer as being edited (shown in the Trade tab).</summary>
        public static NetTrade.Offer Draft => draft;

        /// <summary>The draft has changes the server hasn't confirmed yet (Accept waits for them).</summary>
        public static bool Sending => dirty || (Current != null && !Current.mine.Same(draft));

        /// <summary>A unit of 'item' in the hold may go into an offer.</summary>
        public static bool Tradeable(int item) => NetTrade.Tradeable(item) && Hangar.IsSaleable(item);

        // ---- requests ------------------------------------------------------------------------------------------------

        /// <summary>Asks 'p' (docked here) to trade.</summary>
        public static void Ask(NetPlayer p)
        {
            if (p == null || State == null) return;
            asked[p.OwnerClientId] = Time.unscaledTime;
            State.TradeRequestRpc(p.OwnerClientId);
            Bump();
        }

        internal static void OnRequested(ulong from, string name)
        {
            requests.RemoveAll(r => r.from == from);
            requests.Add(new Request { from = from, name = name, time = Time.unscaledTime });
            NetChat.Notice(string.Format(X("mpTradeRequested", "{0} wants to trade with you."), name));
            Bump();
        }

        public static void Accept(Request r)
        {
            if (r == null) return;
            requests.Remove(r);
            State?.TradeAcceptRpc(r.from);
            Bump();
        }

        public static void Decline(Request r)
        {
            if (r == null) return;
            requests.Remove(r);
            State?.TradeDeclineRpc(r.from);
            Bump();
        }

        // ---- the offer -----------------------------------------------------------------------------------------------

        public static int OfferedOf(int item) => draft.items.Find(s => s.item == item)?.amount ?? 0;

        /// <summary>The credits offered (0..what this player has).</summary>
        public static void SetCredits(int credits)
        {
            if (Current == null || Current.phase != NetTrade.Phase.Open) return;
            credits = (int)Math.Max(0L, Math.Min(credits, Session.Credits));
            if (credits == draft.credits) return;
            draft.credits = credits;
            Changed();
        }

        /// <summary>'delta' more (or fewer) units of 'item' in the offer, within what the hold has.</summary>
        public static void AddItem(int item, int delta)
        {
            if (Current == null || Current.phase != NetTrade.Phase.Open || delta == 0 || !Tradeable(item)) return;
            var row = draft.items.Find(s => s.item == item);
            int now = row?.amount ?? 0;
            int want = Mathf.Clamp(now + delta, 0, Mathf.Min(NetTrade.MaxUnits, Shop.CargoOf(item)));
            if (want == now) return;
            if (row == null)
            {
                if (draft.items.Count >= NetTrade.MaxKinds) { NetChat.Notice(X("mpTradeTooMany", "An offer can hold 16 kinds of goods at most.")); return; }
                draft.items.Add(row = new ItemStack(item, 0));
                draft.items.Sort((x, y) => x.item.CompareTo(y.item));
            }
            row.amount = want;
            if (want <= 0) draft.items.Remove(row);
            Changed();
        }

        static void Changed()
        {
            dirty = true;
            sendAt = Time.unscaledTime + SendDelay;
            Bump();
        }

        /// <summary>Accepts the trade as it is now (the server ignores it if either offer has changed since).</summary>
        public static void Confirm()
        {
            var v = Current;
            if (v == null || v.phase != NetTrade.Phase.Open || Sending || v.myAccepted) return;
            if (Time.unscaledTime - v.theirChangedAt < ChangedHoldSeconds) return;
            State?.TradeConfirmRpc(v.id, v.version);
        }

        /// <summary>Ends the open trade (before the payment).</summary>
        public static void Cancel()
        {
            var v = Current;
            if (v == null || v.phase != NetTrade.Phase.Open) return;
            State?.TradeCancelRpc(v.id);
        }

        // ---- from the server -----------------------------------------------------------------------------------------

        internal static void OnState(int id, ulong partner, string partnerName, int station, int version, int[] mine, int[] theirs,
                                     bool myAccepted, bool theirAccepted, byte phase)
        {
            bool opened = Current == null || Current.id != id;
            if (opened)
            {
                Current = new View { id = id, partner = partner, partnerName = partnerName, station = station };
                draft = new NetTrade.Offer();
                dirty = false;
                requests.RemoveAll(r => r.from == partner);
                asked.Remove(partner);
            }
            var v = Current;
            var theirOffer = NetTrade.Offer.Unpack(theirs);
            if (!opened && !theirOffer.Same(v.theirs)) v.theirChangedAt = Time.unscaledTime;
            v.version = version;
            v.partnerName = partnerName;
            v.mine = NetTrade.Offer.Unpack(mine);
            v.theirs = theirOffer;
            v.myAccepted = myAccepted;
            v.theirAccepted = theirAccepted;
            v.phase = (NetTrade.Phase)phase;
            if (!dirty) draft = Copy(v.mine);   // the server's word, unless this player is still editing
            Bump();
            if (opened)
            {
                NetChat.Notice(string.Format(X("mpTradeOpened", "Trading with {0}."), partnerName));
                Opened?.Invoke();
            }
        }

        static NetTrade.Offer Copy(NetTrade.Offer o)
        {
            var c = new NetTrade.Offer { credits = o.credits };
            foreach (var s in o.items) c.items.Add(s.Clone());
            return c;
        }

        /// <summary>The server asks for the offer: still there? Out of the wallet and the hold, and the answer.</summary>
        internal static void OnPay(int id, int[] packed)
        {
            var offer = NetTrade.Offer.Unpack(packed);
            bool ok = offer.credits <= Session.Credits;
            foreach (var s in offer.items) if (!Tradeable(s.item) || Shop.CargoOf(s.item) < s.amount) ok = false;
            if (ok)
            {
                Session.Credits -= offer.credits;
                foreach (var s in offer.items) Shop.RemoveFromCargo(s.item, s.amount);
                Refresh();
            }
            State?.TradePaidRpc(id, ok);
        }

        /// <summary>The other side's offer (or this player's own back: 'refund').</summary>
        internal static void OnDeliver(int id, int[] packed, bool refund, string partnerName)
        {
            var offer = NetTrade.Offer.Unpack(packed);
            Session.AddCredits(offer.credits);   // saturating at the 64-bit top
            foreach (var s in offer.items) if (NetGame.Db.Item(s.item) != null) Shop.AddToCargo(s.item, s.amount);
            if (refund) NetChat.Notice(X("mpTradeRefunded", "You got your offer back."));
            else NetChat.Notice(string.Format(X("mpTradeDone", "Trade with {0} complete: you got {1}."), partnerName, Describe(offer)));
            Refresh();
            NetProfileClient.Upload();   // the profile as it is now (the server's worth moved with the trade)
        }

        internal static void OnEnded(int id, string reason)
        {
            if (Current != null && Current.id == id)
            {
                Current = null;
                draft = new NetTrade.Offer();
                dirty = false;
            }
            if (!string.IsNullOrEmpty(reason)) NetChat.Notice(reason);
            Bump();
        }

        /// <summary>"5 000 credits, 12 t Gold" (nothing: "nothing").</summary>
        public static string Describe(NetTrade.Offer o)
        {
            var parts = new List<string>();
            if (o.credits > 0) parts.Add($"{o.credits:N0} " + X("mpCredits", "credits"));
            foreach (var s in o.items) parts.Add($"{s.amount} t {GameNames.Item(s.item)}");
            return parts.Count > 0 ? string.Join(", ", parts) : X("mpTradeNothing", "nothing");
        }

        /// <summary>The station's credits and hangar window after the hold or the wallet changed.</summary>
        static void Refresh() => UI.UiHooks.StationCargoChanged?.Invoke();

        // ---- upkeep ---------------------------------------------------------------------------------------------------

        /// <summary>Every frame on every game (NetState.Update): the edited offer goes out, kept within the hold and the
        /// wallet; a trade this player left the hangar of is cancelled.</summary>
        public static void Tick()
        {
            if (NetGame.TestTrade) TestTrade();
            var v = Current;
            if (v == null) return;
            if (v.phase == NetTrade.Phase.Open)
            {
                var me = NetPlayer.Local;
                if (me != null && (!me.InHangar || me.Station != v.station))
                {
                    if (cancelledId != v.id) { cancelledId = v.id; Cancel(); }   // once: the server's answer ends it
                    return;
                }
                // Sold or used meanwhile: the offer shrinks to what is left.
                bool trimmed = false;
                if (draft.credits > Session.Credits) { draft.credits = (int)Math.Max(0L, Session.Credits); trimmed = true; }
                for (int i = draft.items.Count - 1; i >= 0; i--)
                {
                    int have = Shop.CargoOf(draft.items[i].item);
                    if (draft.items[i].amount <= have) continue;
                    if (have <= 0) draft.items.RemoveAt(i); else draft.items[i].amount = have;
                    trimmed = true;
                }
                if (trimmed) Changed();
            }
            if (dirty && Time.unscaledTime >= sendAt && State != null)
            {
                dirty = false;
                if (v.phase == NetTrade.Phase.Open && !draft.Same(v.mine)) State.TradeOfferRpc(v.id, draft.Pack());
                Bump();
            }
        }

        static bool testOffered;
        static int cancelledId;

        /// <summary>Testing (development builds, -mptrade): the trade flow without a hand on this game.</summary>
        static void TestTrade()
        {
            var me = NetPlayer.Local;
            if (Current == null)
            {
                testOffered = false;
                if (me != null && me.InHangar && Requests.Count > 0) Accept(requests[requests.Count - 1]);
                return;
            }
            if (Current.phase != NetTrade.Phase.Open) return;
            if (!testOffered && Session.Credits >= 500) { testOffered = true; SetCredits(500); }
            if (Current.theirAccepted && !Current.myAccepted) Confirm();
        }

        /// <summary>The session ended (NetGame.Shutdown).</summary>
        public static void Clear()
        {
            requests.Clear();
            asked.Clear();
            Current = null;
            draft = new NetTrade.Offer();
            dirty = false;
            Revision++;
        }
    }
}
