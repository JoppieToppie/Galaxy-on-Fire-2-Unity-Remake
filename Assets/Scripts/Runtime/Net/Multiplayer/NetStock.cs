// NetStock.cs
// Multiplayer: every station's shop stock (the items and the dealer's ships) is shared by all players. The host keeps one
// list per station, made with the single-player rules (Shop.GenerateItems / GenerateShips) the first time a player docks
// there, and made again every ResetSeconds (instead of the single-player re-roll after 3 other stations and the nibbling
// on re-docking); every player docked there gets it (NetState.StockRpc) and it replaces their own list in place
// (StationStock.items / ships; the bar's agents stay each player's). A trade is sent to the host (a bought or sold unit, a
// dealer row swapped), which applies it and sends the list to everyone docked there, so they see it at once (the hangar
// window rebuilds, StationMenu). Not shared: the owned Kaamo Club's storage (108) and a Stolen goods mission's documents
// (only its team sees them, Freelance.OnEnterStation; one member buying them takes them for the squad).
// The host decides: a unit it no longer has (another player bought the last one) is taken back from the buyer and paid
// back (ItemRefused); a dealer ship is reserved with the host before the trade (ReserveShip). Until the host's list is
// here the docked player's list is empty (no trade against a list of their own). The host's lists get the stations'
// docking extras (HostExtras: energy cells at the deep science stations). A co-op campaign shares none (Shared). Lists arriving
// are applied between frames (Flush), never in the middle of a trade. The host checks each trade (NetState: the trader is
// docked there, one unit of an item that exists, a dealer row only as reserved: the reserved one back or the trader's old
// hull), sends the list once per frame however many units changed, and caps a row (MaxRowAmount) and the dealer list
// (MaxShips), so no client grows the list everyone docked there receives.
// Rare goods (players' report: Buskat at Sao Perula was back after one other system): commodities worth RarePrice or more
// (Buskat, Vossk Organs, Implants, the add-ons' rare goods) don't come back with the 15-minute list. Each list's roll sets
// how many of each the station should have (Entry.rareTarget: the roll's amount, 0 when the roll has none), what is left
// is kept, and every RareStepSeconds a sold-out or short row gets a third of its target back: empty to full in 30 minutes.
// Remake (players' request): the list carries the seconds to the next re-roll and to the next rare-goods step (Encode), so
// the hangar window's footer can count them down (RestockLeft); the clocks run per station from its first docking.
// Shipments, not a clock (the user's request): each new list and each rare step really comes after a delay drawn anew
// (ResetSeconds x ShipmentEarly..ShipmentLate, RareStepSeconds the same: Entry.due / rareDue), while the list sends the
// nominal schedule (made + ResetSeconds, rareNext), so the footer's estimate is sometimes early, sometimes late.

using System;
using System.Collections.Generic;
using System.Text;
using GoF2Remake.Data;
using GoF2Remake.Modding;
using UnityEngine;

namespace GoF2Remake.Multiplayer
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class NetStock
    {
        /// <summary>How long a station's stock lasts before the host makes it again.</summary>
        public const float ResetSeconds = 15f * 60f;
        /// <summary>Rare goods: the price that makes a commodity one, and how often their rows restock (a third each time).</summary>
        public const int RarePrice = 5000;
        public const float RareStepSeconds = 10f * 60f;
        const int RareSteps = 3;
        /// <summary>A shipment arrives between these shares of its nominal delay (15 min: 12.75 to 18).</summary>
        public const float ShipmentEarly = 0.85f, ShipmentLate = 1.2f;

        /// <summary>The real delay of a shipment whose nominal one is 'seconds'.</summary>
        static float Shipment(float seconds) => seconds * UnityEngine.Random.Range(ShipmentEarly, ShipmentLate);

        class Entry
        {
            public List<ItemStack> items;
            public List<int> ships;
            public float made;
            public float due;        // when this list is really replaced (made + a Shipment of ResetSeconds)
            public float rareDue;    // when the rare rows really restock next (rareNext is the nominal time sent)
            public readonly Dictionary<int, int> rareTarget = new Dictionary<int, int>();   // item -> the units it restocks to
            public readonly Dictionary<int, int> resale = new Dictionary<int, int>();       // item -> units players sold into it (no VAT bought back)
            public float rareNext;

        }

        static readonly Dictionary<int, Entry> stocks = new Dictionary<int, Entry>();   // the host's
        static Database db;
        static readonly Dictionary<(int station, int item), (int basis, int stock, int target, int pressure, int hops)> quotes =
            new Dictionary<(int, int), (int, int, int, int, int)>();
        static bool priceTradePending;
        internal static bool PriceTradePending => priceTradePending;

        public static bool ManagedPricing(int station, int item) =>
            ModAdvancedEconomy.DynamicPricing && NetState.Instance?.ProfilesOn == true &&
            Shared(station) && ModAdvancedEconomy.At(station)?.ContainsKey(item) == true;

        public static int? DisplayBuyQuote(int station, int item) =>
            TryPrice(station, item, true, out int price) ? price : (int?)null;

        public static bool TryPrice(int station, int item, bool buy, out int price)
        {
            price = 0;
            if (!ManagedPricing(station, item) || !quotes.TryGetValue((station, item), out var q)) return false;
            int listPrice = UnitPrice(q.basis, q.stock, q.target, q.pressure, q.hops, buy);
            price = buy ? NetFactionsClient.BuyPrice(station, listPrice) : NetFactionsClient.SellPrice(station, listPrice);
            return true;
        }

        static int UnitPrice(int basis, int stock, int target, int pressure, int hops, bool buy) =>
            ModAdvancedEconomy.TradeUnitPrice(basis, stock, target, pressure, hops,
                ModAdvancedEconomy.ScarcityMarkup, ModAdvancedEconomy.SurplusDiscount, ModAdvancedEconomy.WarMarkup,
                ModAdvancedEconomy.TransportCosts ? ModAdvancedEconomy.TransportPerHop : 0, ModAdvancedEconomy.SellSpread, buy);

        static bool TryTotal(int station, int item, int units, bool buy, out long total)
        {
            total = 0;
            if (!ManagedPricing(station, item) || !quotes.TryGetValue((station, item), out var q) ||
                units < 1 || units > 500 || (buy && units > q.stock)) return false;
            for (int i = 0; i < units; i++)
            {
                int list = UnitPrice(q.basis, q.stock + (buy ? -i : i), q.target, q.pressure, q.hops, buy);
                total += buy ? NetFactionsClient.BuyPrice(station, list) : NetFactionsClient.SellPrice(station, list);
            }
            return true;
        }

        public static string EconomyInfo(int station, int item)
        {
            if (!quotes.TryGetValue((station, item), out var q) ||
                !TryPrice(station, item, true, out int buy) || !TryPrice(station, item, false, out int sell)) return "";
            string supply = q.stock < q.target ? $"Shortage {q.target - q.stock:N0} t" : q.stock > q.target ? $"Surplus {q.stock - q.target:N0} t" : "Balanced";
            return $"Next unit: Buy {buy:N0} / Sell {sell:N0} cr • Stock {q.stock:N0}/{q.target:N0} t • {supply} • War {q.pressure}% • Import {q.hops} hops";
        }

        public static bool RequestPricedTrade(int station, int item, int units, bool buy)
        {
            units = Math.Min(500, units);
            if (!Ready || priceTradePending || !TryTotal(station, item, units, buy, out long quote)) return false;
            int sequence = NetGame.IsServer && NetGame.PersistentHost ? 0 : NetProfileClient.UploadForStationStorage();
            if (sequence == 0 && !(NetGame.IsServer && NetGame.PersistentHost)) return false;
            priceTradePending = true;
            NetState.Instance.EconomyTradeRpc(station, item, units, buy, quote, sequence);
            return true;
        }

        internal static void PriceTradeResult(int station, int item, int delta, long credits, string result)
        {
            if (!priceTradePending) return;
            priceTradePending = false;
            if (!(NetGame.IsServer && NetGame.PersistentHost) && delta != 0)
            {
                if (delta > 0) Shop.AddToCargo(item, delta); else Shop.RemoveFromCargo(item, -delta);
                Session.Credits = Math.Max(0L, Session.SaturatingAdd(Session.Credits, credits));
            }
            if (!string.IsNullOrEmpty(result)) NetChat.Notice(result);
            Changed?.Invoke(station);
            GoF2Remake.UI.UiHooks.RefreshStationCredits?.Invoke();
        }
        static float nextEconomyTick;
        static readonly List<Action> pending = new List<Action>();
        /// <summary>A player's: each station's clocks as the last list said (seconds left, -1 = none) and when it arrived.</summary>
        static readonly Dictionary<int, (float at, int reset, int rare)> clocks = new Dictionary<int, (float, int, int)>();
        static readonly Dictionary<long, (Action granted, Action refused)> reservations = new Dictionary<long, (Action, Action)>();
        static long Key(int station, int ship) => (long)station << 32 | (uint)ship;

        /// <summary>A station's stock changed here (another player's trade, the reset): its station.</summary>
        public static event Action<int> Changed;

        static bool Ready => NetGame.Active && NetState.Instance != null && NetState.Instance.IsSpawned;

        /// <summary>Is 'station's stock the shared one (in a session, not the owned Kaamo Club's storage)? Not in a co-op
        /// campaign: the story stocks one set for each player (Var Hastra's free starter gear before step 7, Kappa's free
        /// EMP bombs at step 20), so a shared list left the second pilot an empty shop (a Discord report, 2026-11-11).</summary>
        public static bool Shared(int station) => NetGame.Active && !CoopCampaign.Active &&
            !KaamoClub.StorageAt(station) && !Shop.UsesPlayerMarketplace(station);

        /// <summary>A new session: the host's lists start empty.</summary>
        public static void Reset()
        {
            stocks.Clear();
            quotes.Clear(); priceTradePending = false;
            nextEconomyTick = 0;
            db = null;
            AdvancedEconomyLedger.Reset();
            pending.Clear();
            reservations.Clear();
            clocks.Clear();
        }

        /// <summary>The hangar window's footer: seconds to the next re-roll of 'station's shared stock and to its rare goods'
        /// next restock step (-1 = no rare row is short), counted down from the last list; false = no list (yet).</summary>
        public static bool RestockLeft(int station, out float reset, out float rare)
        {
            reset = rare = -1f;
            if (!Shared(station) || !clocks.TryGetValue(station, out var c) || c.reset < 0) return false;
            float gone = Time.unscaledTime - c.at;
            reset = Mathf.Max(0f, c.reset - gone);
            if (c.rare >= 0) rare = Mathf.Max(0f, c.rare - gone);
            return true;
        }

        /// <summary>NetState, every frame: what arrived is applied now (not inside a trade).</summary>
        internal static void Flush()
        {
            if (pending.Count == 0) return;
            var now = pending.ToArray();
            pending.Clear();
            foreach (var a in now) a();
        }

        // ---- the players --------------------------------------------------------------------------------------

        /// <summary>Shop.EnterStation: ask the host for the shared stock (it replaces this list when it arrives).</summary>
        public static void OnEnterStation(StationStock stock)
        {
            if (!Ready || stock == null || !Shared(stock.station)) return;
            // Nothing to trade until the host's list is here (Apply); the bar's agents stay.
            stock.items.Clear();
            stock.ships?.Clear();   // (a traded-in row's mods wait for the list: Apply prunes against it)
            NetState.Instance.StockRequestRpc(stock.station);
        }

        /// <summary>Hangar.Buy / Sell: one unit of 'item' out (-1, bought for 'price') or in (+1).</summary>
        public static void ItemChanged(int station, int item, int delta, int price = 0)
        {
            if (!Ready) return;
            if (item == Freelance.Documents)
            {
                // The Stolen goods documents aren't stock: bought, they are gone for the squad (NetMissions.OnStatus).
                if (delta < 0 && Freelance.Active && Freelance.Mission.type == MissionType.StolenGoods && Freelance.Mission.status == 0)
                {
                    Freelance.Mission.status = 1;   // the buyer's own: no documents again in the next list either
                    NetMissions.AddStatus(Freelance.Mission, 1);
                }
                return;
            }
            if (Shared(station)) NetState.Instance.StockItemRpc(station, item, delta, price);
        }

        /// <summary>NetState: the host had no unit left of 'count' units this player bought at 'price' each (someone was first):
        /// back they go, paid back.</summary>
        internal static void ItemRefused(int station, int item, int price, int count = 1) => pending.Add(() =>
        {
            int back = 0;
            for (int n = 0; n < count; n++)
            {
                if (Story.CargoOf(item) > 0) Shop.RemoveFromCargo(item, 1);
                else
                {
                    var mounted = Session.Equipment.Find(e => e.item == item);
                    if (mounted == null) break;   // already sold on: nothing to take back
                    if (mounted.amount > 1) mounted.amount--; else Session.Equipment.Remove(mounted);
                }
                back++;
            }
            if (back == 0) return;
            Session.Credits += price * back;
            NetChat.Notice(string.Format(Localization.Extra("mpSoldOut", "Sold out: another pilot bought the last {0}."), UI.ItemInfo.ItemName(item)));
            Changed?.Invoke(station);
        });

        /// <summary>Hangar's ship trade: the dealer's row 'ship' is reserved with the host first (another pilot may be buying
        /// it); 'granted' trades, 'refused' tells. Outside a session: at once.</summary>
        public static void ReserveShip(int station, int ship, Action granted, Action refused)
        {
            if (!Ready || !Shared(station)) { granted(); return; }
            reservations[Key(station, ship)] = (granted, refused);
            NetState.Instance.ReserveShipRpc(station, ship);
        }

        /// <summary>NetState: the reservation's answer.</summary>
        internal static void ReserveResult(int station, int ship, bool ok) => pending.Add(() =>
        {
            if (!reservations.TryGetValue(Key(station, ship), out var r)) { if (ok) ShipChanged(station, -1, ship); return; }   // not wanted any more: back
            reservations.Remove(Key(station, ship));
            if (ok) r.granted(); else r.refused();
        });

        /// <summary>Hangar.BuyShip / KeepAndBuyShip: the dealer's row 'removed' became 'added' (-1 = none).</summary>
        public static void ShipChanged(int station, int removed, int added)
        {
            if (!Ready || !Shared(station)) return;
            NetState.Instance.StockShipRpc(station, removed, added);
        }

        /// <summary>NetState: the host's stock of 'station' (Encode): into this player's list for it (between frames).</summary>
        internal static void Apply(int station, string text) => pending.Add(() => ApplyNow(station, text));

        static void ApplyNow(int station, string text)
        {
            var stock = Session.RecentStations.Find(s => s.station == station);
            if (stock == null || !Shared(station)) return;
            Decode(text, out var items, out var ships, out int reset, out int rare);
            var oldQuotes = new List<(int station, int item)>();
            foreach (var key in quotes.Keys) if (key.station == station) oldQuotes.Add(key);
            foreach (var key in oldQuotes) quotes.Remove(key);
            var parts = text.Split('|');
            if (parts.Length >= 5)
                foreach (string entry in parts[4].Split(','))
                {
                    var fields = entry.Split(':');
                    if (fields.Length == 6 && int.TryParse(fields[0], out int item) && int.TryParse(fields[1], out int basis) &&
                        int.TryParse(fields[2], out int amount) && int.TryParse(fields[3], out int target) &&
                        int.TryParse(fields[4], out int pressure) && int.TryParse(fields[5], out int hops))
                        quotes[(station, item)] = (basis, amount, target, pressure, hops);
                }
            clocks[station] = (Time.unscaledTime, reset, rare);
            stock.items.Clear();
            stock.items.AddRange(items);
            if (stock.ships == null) stock.ships = new List<int>();
            stock.ships.Clear();
            stock.ships.AddRange(ships);
            stock.PruneShipMods();   // the shared list carries no mods: a modded row only this player traded in keeps them
            Freelance.OnEnterStation(stock);   // a Stolen goods mission's documents (only its team's)
            Changed?.Invoke(station);
        }

        /// <summary>NetMissions: a squadmate bought the documents: gone from this player's list too.</summary>
        internal static void RemoveDocuments(int station)
        {
            var stock = Session.RecentStations.Find(s => s.station == station);
            if (stock == null || stock.items.RemoveAll(r => r.item == Freelance.Documents) == 0) return;
            Changed?.Invoke(station);
        }

        // ---- the host -----------------------------------------------------------------------------------------

        static Entry Get(int station)
        {
            if (stocks.TryGetValue(station, out var e)) return e;
            if (db == null) db = Database.Load();
            e = Make(station, null);
            stocks[station] = e;
            return e;
        }

        /// <summary>A new list for 'station'. With the old one ('old'), its rare goods stay as they are (what is left) and
        /// restock gradually toward the new roll's amounts (UpdateRare) instead of coming back whole.</summary>
        static Entry Make(int station, Entry old)
        {
            var e = new Entry { items = Shop.GenerateItems(db, station), ships = Shop.GenerateShips(db, station) ?? new List<int>(), made = Time.unscaledTime,
                                due = Time.unscaledTime + Shipment(ResetSeconds),
                                rareNext = Time.unscaledTime + RareStepSeconds, rareDue = Time.unscaledTime + Shipment(RareStepSeconds) };
            HostExtras(station, e.items);
            ThinForVat(station, e);
            foreach (var r in e.items) if (IsRare(r.item)) e.rareTarget[r.item] = r.amount;
            if (old == null) { CarryEconomy(station, e); return e; }   // a first list: full
            e.items.RemoveAll(r => IsRare(r.item));
            foreach (var r in old.items)
            {
                if (!IsRare(r.item) || r.amount <= 0) continue;
                int at = e.items.FindIndex(x => x.item > r.item);
                e.items.Insert(at < 0 ? e.items.Count : at, new ItemStack(r.item, r.amount));
            }
            e.rareNext = old.rareNext;
            e.rareDue = old.rareDue;
            CarryEconomy(station, e);
            return e;
        }

        // Only explicitly managed commodity rows survive the normal shipment replacement.
        // The remaining goods and dealer ships still use the vanilla generator and rare-goods clock.
        static void CarryEconomy(int station, Entry entry)
        {
            if (!AdvancedEconomyLedger.Active) return;
            var rules = ModAdvancedEconomy.At(station);
            if (rules == null) return;
            foreach (var rule in rules.Values)
            {
                entry.rareTarget.Remove(rule.Item);
                entry.items.RemoveAll(r => r.item == rule.Item);
                entry.resale.Remove(rule.Item);
                if (!ModAdvancedEconomy.Eligible(db, rule.Item)) continue;
                var balance = AdvancedEconomyLedger.Get(station, rule);
                SetEconomyRow(entry, rule.Item, balance.Amount);
                if (balance.Resale > 0) entry.resale[rule.Item] = balance.Resale;
            }
        }

        static void SetEconomyRow(Entry entry, int item, int amount)
        {
            var row = entry.items.Find(r => r.item == item);
            if (amount <= 0)
            {
                if (row != null) entry.items.Remove(row);
                entry.resale.Remove(item);
                return;
            }
            if (row != null) row.amount = amount;
            else
            {
                int at = entry.items.FindIndex(r => r.item > item);
                entry.items.Insert(at < 0 ? entry.items.Count : at, new ItemStack(item, amount));
            }
            if (entry.resale.TryGetValue(item, out int resale) && resale > amount) entry.resale[item] = amount;
        }

        // One batch for the entire configured network; unopened shops need no generated dealer or item lists.
        static void TickEconomy(NetState state)
        {
            if (!AdvancedEconomyLedger.Active || NetPlayerStations.ProfileWritesBlocked) return;
            if (nextEconomyTick <= 0) nextEconomyTick = Time.unscaledTime + ModAdvancedEconomy.TickSeconds;
            if (Time.unscaledTime >= nextEconomyTick)
            {
                nextEconomyTick = Time.unscaledTime + ModAdvancedEconomy.TickSeconds;
                if (db == null) db = Database.Load();
                bool Participates(int station) => !Shop.UsesPlayerMarketplace(station) && !KaamoClub.StorageAt(station) &&
                    (ModAdvancedEconomy.SimulateUnvisitedStations || stocks.ContainsKey(station));
                var pressures = new Dictionary<int, int>();
                int Pressure(int station)
                {
                    if (!pressures.TryGetValue(station, out int p))
                        pressures[station] = p = ModAdvancedEconomy.Pressure(db, station);
                    return p;
                }
                var changed = new HashSet<int>();
                foreach (int station in ModAdvancedEconomy.Stations)
                {
                    if (!Participates(station)) continue;
                    foreach (var rule in ModAdvancedEconomy.At(station).Values)
                    {
                        if (!ModAdvancedEconomy.Eligible(db, rule.Item)) continue;
                        var balance = AdvancedEconomyLedger.Get(station, rule);
                        int next = rule.Advance(balance.Amount, ModAdvancedEconomy.Production, ModAdvancedEconomy.Consumption, Pressure(station));
                        if (next == balance.Amount) continue;
                        int before = balance.Amount;
                        AdvancedEconomyLedger.Set(station, rule, next, balance.Resale);
                        if (next > before) balance.ImportHops = (int)Math.Round((long)before * balance.ImportHops / (double)next);
                        changed.Add(station);
                    }
                }
                var graph = ModAdvancedEconomy.RegionalTrade ? AdvancedEconomyRoutes.Build(db) : null;
                var incoming = AdvancedEconomyLedger.TickTrade(db, Participates, Pressure, changed, graph);
                var autoGraph = ModAdvancedEconomy.AutomaticRoutes ? graph : null;
                TickPlayerMarket(Participates, changed, incoming, autoGraph);
                TickNpcConstruction(Participates, changed, autoGraph);
                if (ModAdvancedEconomy.DynamicPricing)
                    foreach (int station in ModAdvancedEconomy.Stations)
                        if (stocks.ContainsKey(station) && Participates(station)) changed.Add(station);
                foreach (int station in changed)
                    if (stocks.TryGetValue(station, out var entry))
                    {
                        CarryEconomy(station, entry);
                        state.BroadcastStock(station);
                    }
            }
            AdvancedEconomyLedger.Flush();
        }

        // Supply only from surplus, after ordinary NPC routes and procurement have run.
        static void TickNpcConstruction(Func<int, bool> participates, HashSet<int> changed, AdvancedEconomyRoutes graph)
        {
            if (!ModAdvancedEconomy.NpcConstructionSupply || !NetProfiles.Enabled ||
                NetPlayerStations.ProfileWritesBlocked || !AdvancedEconomyLedger.CanJournal ||
                NetPlayerStations.Stations == null) return;
            int remaining = ModAdvancedEconomy.NpcConstructionUnits;
            var sources = new List<int>(ModAdvancedEconomy.Stations);
            sources.Sort();
            var projects = new List<PlayerStationRecord>(NetPlayerStations.Stations);
            projects.Sort((a, b) => (a?.id ?? int.MaxValue).CompareTo(b?.id ?? int.MaxValue));
            foreach (var project in projects)
            {
                if (project?.buyOrders == null || project.retired || project.destroyed || project.wreckClaimOpen ||
                    project.offeredForClaimUtc > 0) continue;
                foreach (var order in new List<PlayerStationBuyOrder>(project.buyOrders))
                {
                    if (order == null || order.remaining <= 0 || order.unitPrice <= 0 ||
                        !ModAdvancedEconomy.Eligible(db, order.item)) continue;
                    foreach (int station in sources)
                    {
                        if (remaining <= 0 || NetPlayerStations.ProfileWritesBlocked) return;
                        if (order.remaining <= 0 || project.buyOrders == null || !project.buyOrders.Contains(order)) break;
                        if (!participates(station) || !ModAdvancedEconomy.At(station).TryGetValue(order.item, out var rule)) continue;
                        int system = Shop.SystemOf(db, station);
                        int distance = system == project.system ? 0 : graph?.Distance(system, project.system) ?? -1;
                        if (distance < 0 || distance > ModAdvancedEconomy.MaxAutoHops) continue;
                        var balance = AdvancedEconomyLedger.Get(station, rule);
                        int count = ModAdvancedEconomy.ConstructionSupplyAmount(balance.Amount, rule.Target,
                            order.remaining, NetPlayerStations.NpcConstructionRemaining(project, order.item), remaining);
                        if (count == 0) continue;
                        int basis = Shop.PriceList(db, station, new[] { rule.Item })[0];
                        int pressure = ModAdvancedEconomy.Pressure(db, station);
                        // Require the bid to cover the most expensive marginal unit, including delivery distance.
                        while (count > 0 && order.unitPrice < UnitPrice(basis, balance.Amount - count + 1,
                            rule.Target, pressure, balance.ImportHops + distance, true)) count--;
                        if (count == 0 || basis <= 0) continue;
                        int old = balance.Amount, resale = balance.Resale, hops = balance.ImportHops;
                        int shipped = NetPlayerStations.SupplyNpcConstruction(project, order, count,
                            () => AdvancedEconomyLedger.Set(station, rule, old - count, Math.Max(0, resale - count)),
                            () => { AdvancedEconomyLedger.Set(station, rule, old, resale); balance.ImportHops = hops; });
                        if (shipped > 0) { changed.Add(station); remaining -= shipped; }
                    }
                }
            }
        }

        // Player listings supply local NPC demand only after the ordinary shipment pass.
        // This cannot touch personal storage, factories, ships, or construction escrow.
        static void TickPlayerMarket(Func<int, bool> participates, HashSet<int> changed,
            IReadOnlyDictionary<(int station, int item), int> incoming, AdvancedEconomyRoutes graph)
        {
            if (!ModAdvancedEconomy.PlayerMarketTrade || !NetProfiles.Enabled ||
                NetPlayerStations.ProfileWritesBlocked || !AdvancedEconomyLedger.CanJournal ||
                NetPlayerStations.Stations == null) return;
            long budget = ModAdvancedEconomy.PlayerMarketBudget;
            int remainingUnits = ModAdvancedEconomy.PlayerMarketUnits;
            if (budget <= 0 || remainingUnits <= 0) return;
            // Regional automatic routing also opts in to abstract cross-system procurement.
            var markets = new List<PlayerStationRecord>(NetPlayerStations.Stations);
            markets.Sort((a, b) => (a?.id ?? int.MaxValue).CompareTo(b?.id ?? int.MaxValue));
            foreach (int station in ModAdvancedEconomy.Stations)
            {
                if (!participates(station)) continue;
                int system = Shop.SystemOf(db, station);
                foreach (var rule in ModAdvancedEconomy.At(station).Values)
                {
                    if (!ModAdvancedEconomy.Eligible(db, rule.Item)) continue;
                    var balance = AdvancedEconomyLedger.Get(station, rule);
                    incoming.TryGetValue((station, rule.Item), out int reserved);
                    if (balance.Amount + reserved >= rule.Target) continue;
                    foreach (var market in markets)
                    {
                        if (market == null || !market.npcEconomyPurchases || market.listings == null) continue;
                        int distance = market.system == system ? 0 : graph?.Distance(market.system, system) ?? -1;
                        if (distance < 0 || distance > ModAdvancedEconomy.MaxAutoHops) continue;
                        // Sort by ask price; the original escrow list is not reordered.
                        var listings = new List<PlayerStationListing>(market.listings);
                        listings.Sort((a, b) => (a?.unitPrice ?? long.MaxValue).CompareTo(b?.unitPrice ?? long.MaxValue));
                        foreach (var listing in listings)
                        {
                            if (listing == null || listing.item != rule.Item) continue;
                            int need = Math.Min(remainingUnits, rule.Target - balance.Amount - reserved);
                            if (need <= 0) break;
                            int ceiling = HostQuote(station, rule, balance).buy;
                            int old = balance.Amount, resale = balance.Resale, hops = balance.ImportHops;
                            int count = NetPlayerStations.PurchaseForNpcEconomy(market, listing, need, ceiling, budget,
                                units =>
                                {
                                    AdvancedEconomyLedger.Set(station, rule, old + units, resale + units);
                                    balance.ImportHops = (int)Math.Round(((long)old * hops + (long)units * distance) / (double)(old + units));
                                },
                                () =>
                                {
                                    AdvancedEconomyLedger.Set(station, rule, old, resale);
                                    balance.ImportHops = hops;
                                }, out long spent);
                            if (count > 0) { changed.Add(station); budget -= spent; remainingUnits -= count; }
                            if (NetPlayerStations.ProfileWritesBlocked || budget <= 0 || remainingUnits <= 0) return;
                        }
                    }
                }
            }
        }

        /// <summary>A faction station's VAT thins its goods: every row keeps (100 - rate) % of its units (at least 1), the dealer
        /// keeps that share of its ships (NetFactions.VatAt).</summary>
        static void ThinForVat(int station, Entry e)
        {
            int rate = NetFactions.VatAt(station);
            if (rate <= 0) return;
            float keep = (100 - rate) / 100f;
            foreach (var r in e.items) if (r.amount > 1) r.amount = Math.Max(1, Mathf.RoundToInt(r.amount * keep));
            int ships = Mathf.RoundToInt(e.ships.Count * keep);
            while (e.ships.Count > ships) e.ships.RemoveAt(UnityEngine.Random.Range(0, e.ships.Count));
        }

        static bool IsRare(int item)
        {
            var it = db != null ? db.Item(item) : null;
            return it != null && it.TypeId == 4 && it.maxPrice >= RarePrice;
        }

        /// <summary>Every RareStepSeconds: each rare row short of its target gets a third of the target back (at least 1).
        /// True = the list changed.</summary>
        static bool UpdateRare(Entry e)
        {
            if (Time.unscaledTime < e.rareDue) return false;
            e.rareNext = Time.unscaledTime + RareStepSeconds;
            e.rareDue = Time.unscaledTime + Shipment(RareStepSeconds);
            bool changed = false;
            foreach (var kv in e.rareTarget)
            {
                if (kv.Value <= 0) continue;
                var row = e.items.Find(r => r.item == kv.Key);
                int have = row != null ? row.amount : 0;
                if (have >= kv.Value) continue;
                int add = Math.Min(kv.Value - have, Math.Max(1, (kv.Value + RareSteps - 1) / RareSteps));
                if (row != null) row.amount += add;
                else
                {
                    int at = e.items.FindIndex(r => r.item > kv.Key);
                    e.items.Insert(at < 0 ? e.items.Count : at, new ItemStack(kv.Key, add));
                }
                changed = true;
            }
            return changed;
        }

        /// <summary>Story.OnDocked's stock extras that aren't about one player, in the shared list (a player's own copy would be
        /// replaced by it): the deep science stations' and the battlestation's energy cells (the single-player rule gives them for a nearly empty hold: here always, so
        /// nobody is stranded in a gateless system).</summary>
        static void HostExtras(int station, List<ItemStack> items)
        {
            void Add(int item, int amount)
            {
                var row = items.Find(r => r.item == item);
                if (row != null) { row.amount += amount; return; }
                int at = items.FindIndex(r => r.item > item);
                items.Insert(at < 0 ? items.Count : at, new ItemStack(item, amount));
            }
            if ((station == 10 || station == 100 || station == 101) && !items.Exists(r => r.item == GalaxyMap.EnergyCellItem)) Add(GalaxyMap.EnergyCellItem, 10);
        }

        /// <summary>A dealer ship's reservation: taken from the host's list (false = none left: another pilot has it).</summary>
        internal static bool HostReserveShip(int station, int ship)
        {
            if (Shop.UsesPlayerMarketplace(station)) return false;
            var e = Get(station);
            int row = e.ships.IndexOf(ship);
            if (row < 0) return false;
            e.ships.RemoveAt(row);
            return true;
        }

        /// <summary>The stock of 'station' (made now if there is none), encoded for the RPC.</summary>
        internal static string HostGet(int station)
        {
            if (Shop.UsesPlayerMarketplace(station)) return "||0|-1";
            var e = Get(station);
            if (ModAdvancedEconomy.DynamicPricing && AdvancedEconomyLedger.Active) CarryEconomy(station, e);
            return Encode(e, station);
        }

        internal static bool HostManagedPricing(int station, int item) =>
            AdvancedEconomyLedger.Active && NetProfiles.Enabled && ModAdvancedEconomy.DynamicPricing &&
            !Shop.UsesPlayerMarketplace(station) && !KaamoClub.StorageAt(station) &&
            ModAdvancedEconomy.At(station)?.ContainsKey(item) == true;

        static (int buy, int sell, int pressure) HostQuote(int station, ModAdvancedEconomy.Rule rule, AdvancedEconomyLedger.Balance balance)
        {
            int basis = Shop.PriceList(db, station, new[] { rule.Item })[0];
            int pressure = ModAdvancedEconomy.Pressure(db, station);
            var quote = ModAdvancedEconomy.Quote(basis, balance.Amount, rule.Target, pressure, balance.ImportHops,
                ModAdvancedEconomy.ScarcityMarkup, ModAdvancedEconomy.SurplusDiscount, ModAdvancedEconomy.WarMarkup,
                ModAdvancedEconomy.TransportCosts ? ModAdvancedEconomy.TransportPerHop : 0, ModAdvancedEconomy.SellSpread);
            return (quote.buy, quote.sell, pressure);
        }

        internal static string HostPricedTrade(ulong client, int station, int item, int units, bool buy, long expected,
            int uploadSequence, out int delta, out long credits)
        {
            delta = 0; credits = 0;
            if (!HostManagedPricing(station, item) || !NetProfiles.Controls(client) ||
                NetPlayerStations.ProfileWritesBlocked || !AdvancedEconomyLedger.CanJournal ||
                !(NetGame.PersistentHost && client == NetGame.LocalId) && !NetProfiles.ConsumeActionUpload(client, uploadSequence))
                return "Economy unavailable or profile not synchronized. Try again.";
            var pilot = NetSquad.Find(client);
            string account = NetProfiles.AccountOf(client);
            if (pilot == null || !NetGuard.DockedAt(client, station) || string.IsNullOrEmpty(account) ||
                units < 1 || units > 500) return "Invalid commodity trade.";
            var entry = Get(station);
            if (!ModAdvancedEconomy.Eligible(db, item) || !NetProfiles.EconomySaleable(client, item))
                return "This commodity cannot be traded.";
            var rule = ModAdvancedEconomy.At(station)[item];
            var balance = AdvancedEconomyLedger.Get(station, rule);
            int basis = Shop.PriceList(db, station, new[] { item })[0];
            int pressure = ModAdvancedEconomy.Pressure(db, station);
            if (buy && (units > balance.Amount || units > NetProfiles.WreckSalvageCargoRoom(pilot, account)))
                return "Not enough stock or cargo capacity.";
            if (!buy && (units > rule.Maximum - balance.Amount || units > NetProfiles.CargoAmount(account, item)))
                return "Not enough cargo or station capacity.";
            long total = 0, tax = 0;
            string faction = null;
            for (int i = 0; i < units; i++)
            {
                int list = UnitPrice(basis, balance.Amount + (buy ? -i : i), rule.Target, pressure, balance.ImportHops, buy);
                total += NetFactions.EconomyPrice(client, station, list, buy, buy && i >= balance.Resale ? 1 : 0,
                    out string owner, out long unitTax);
                if (unitTax > 0) { faction = owner; tax += unitTax; }
            }
            if (total != expected) return "The market price has changed. Review the new quote.";
            if (buy && NetProfiles.CreditAmount(account) < total) return "Not enough credits.";
            int old = balance.Amount, oldResale = balance.Resale;
            int moved = buy ? units : -units;
            long creditChange = buy ? -total : total;
            AdvancedEconomyLedger.Set(station, rule, old - moved, buy ? Math.Max(0, oldResale - units) : oldResale + units);
            if (!NetPlayerStations.CommitEconomyTrade(account, item, moved, creditChange, faction, tax, AdvancedEconomyLedger.Snapshot()))
            {
                AdvancedEconomyLedger.Set(station, rule, old, oldResale);
                return "Trade could not be journalled. No transfer was committed.";
            }
            CarryEconomy(station, entry);
            delta = moved; credits = creditChange;
            return $"{(buy ? "Purchased" : "Sold")} {units:N0} t for {total:N0} credits.";
        }

        /// <summary>One unit of 'item' bought (-1) or sold (+1) at 'station'; false = refused (no unit left to buy). (The
        /// clients only send what their own game shares: no Kaamo check here, the host's club isn't theirs.)</summary>
        /// <summary>The host's stock row changes by 'delta' units (a sale +, a purchase -; several at once from a shop's
        /// Buy all / Sell all, Hangar.EndBatch). Returns the units done: a purchase gets only what the row still has (another
        /// pilot may have bought some first); the caller refuses the rest.</summary>
        /// 'vatUnits' = the bought units that carry a faction station's VAT: not those players sold into the row (resale).
        internal static int HostItem(int station, int item, int delta, out int vatUnits)
        {
            vatUnits = 0;
            if (delta == 0 || Shop.UsesPlayerMarketplace(station)) return 0;
            var e = Get(station);
            var row = e.items.Find(r => r.item == item);
            if (delta < 0)
            {
                if (row == null || row.amount <= 0) return 0;   // another pilot bought the last one
                int granted = Math.Min(row.amount, -delta);
                row.amount -= granted;
                if (row.amount <= 0) e.items.Remove(row);
                e.resale.TryGetValue(item, out int resold);
                int fromResale = Math.Min(resold, granted);
                if (fromResale > 0) e.resale[item] = resold - fromResale;
                vatUnits = granted - fromResale;
                var buyRules = AdvancedEconomyLedger.Active ? ModAdvancedEconomy.At(station) : null;
                if (buyRules != null && buyRules.TryGetValue(item, out var buyRule))
                    AdvancedEconomyLedger.Set(station, buyRule, row.amount, resold - fromResale);
                return granted;
            }
            if (Shop.NotInSession(db, item)) return delta;   // a time extender sold: paid for, not on sale (Shop.NotInSession)
            var managed = AdvancedEconomyLedger.Active ? ModAdvancedEconomy.At(station) : null;
            if (managed != null && managed.TryGetValue(item, out var rule))
            {
                if (!ModAdvancedEconomy.Eligible(db, item)) return delta;
                int have = row?.amount ?? 0;
                int retained = (int)Math.Min((long)delta, Math.Max(0, rule.Maximum - have));
                if (retained > 0)
                {
                    SetEconomyRow(e, item, have + retained);
                    e.resale.TryGetValue(item, out int resoldUnits);
                    e.resale[item] = Math.Min(have + retained, resoldUnits + retained);
                    AdvancedEconomyLedger.Set(station, rule, have + retained, e.resale[item]);
                }
                // As with vanilla's row cap, overflow is sold but does not grow public stock.
                return delta;
            }
            e.resale.TryGetValue(item, out int sold);
            e.resale[item] = (int)Math.Min((long)sold + delta, MaxRowAmount);
            if (row != null) row.amount = Math.Min(row.amount + delta, MaxRowAmount);   // a seller can't grow a row without end
            else
            {
                int at = e.items.FindIndex(r => r.item > item);
                e.items.Insert(at < 0 ? e.items.Count : at, new ItemStack(item, Math.Min(delta, MaxRowAmount)));   // item index order, like Shop
            }
            return delta;
        }

        /// <summary>A dealer trade at 'station': the row 'removed' becomes 'added' (in place), or goes / comes.</summary>
        internal static bool HostShip(int station, int removed, int added)
        {
            if (Shop.UsesPlayerMarketplace(station)) return false;
            var e = Get(station);
            int row = removed >= 0 ? e.ships.IndexOf(removed) : -1;
            if (row >= 0 && added >= 0) e.ships[row] = added;
            else if (row >= 0) e.ships.RemoveAt(row);
            else if (added >= 0 && e.ships.Count < MaxShips) e.ships.Add(added);
            return true;
        }

        /// <summary>The most units of one item and the most dealer ships the host keeps for a station (the generator's are
        /// far fewer; trades beyond them don't grow the list sent to everyone docked there).</summary>
        const int MaxRowAmount = 100000, MaxShips = 32;

        /// <summary>NetState's sweep: every stock whose shipment is due (Entry.due) is made again (its rare goods kept, Make), the rare
        /// rows restock a step (UpdateRare); the changed lists go to the players docked there.</summary>
        internal static void HostTick(NetState state)
        {
            TickEconomy(state);
            if (stocks.Count == 0) return;
            List<int> expired = null, restocked = null, playerStations = null;
            foreach (var kv in stocks)
            {
                if (Shop.UsesPlayerMarketplace(kv.Key)) (playerStations ??= new List<int>()).Add(kv.Key);
                else if (Time.unscaledTime >= kv.Value.due) (expired ??= new List<int>()).Add(kv.Key);
                else if (UpdateRare(kv.Value)) (restocked ??= new List<int>()).Add(kv.Key);
            }
            if (playerStations != null)
                foreach (int station in playerStations) stocks.Remove(station);
            if (expired != null)
                foreach (int station in expired)
                {
                    stocks[station] = Make(station, stocks[station]);   // the rare and managed goods carried over
                    state.BroadcastStock(station);
                }
            if (restocked != null)
                foreach (int station in restocked) state.BroadcastStock(station);
        }

        // ---- the wire format: "item:amount,...|ship,..." --------------------------------------------------------

        static string Encode(Entry e, int station)
        {
            var sb = new StringBuilder();
            foreach (var r in e.items)
            {
                if (r.amount <= 0) continue;
                if (sb.Length > 0) sb.Append(',');
                sb.Append(r.item).Append(':').Append(r.amount);
            }
            sb.Append('|');
            for (int i = 0; i < e.ships.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(e.ships[i]);
            }
            // "|reset|rare": the seconds to the next re-roll and the next rare step as scheduled (-1 when no rare row is short);
            // the real ones (due, rareDue) vary, so a shipment can come early or late.
            int reset = Mathf.Max(0, Mathf.CeilToInt(e.made + ResetSeconds - Time.unscaledTime));
            bool rareShort = false;
            foreach (var kv in e.rareTarget)
            {
                if (kv.Value <= 0) continue;
                var row = e.items.Find(r => r.item == kv.Key);
                if (row == null || row.amount < kv.Value) { rareShort = true; break; }
            }
            int rare = rareShort ? Mathf.Max(0, Mathf.CeilToInt(e.rareNext - Time.unscaledTime)) : -1;
            sb.Append('|').Append(reset).Append('|').Append(rare);
            if (ModAdvancedEconomy.DynamicPricing && NetProfiles.Enabled && AdvancedEconomyLedger.Active)
            {
                sb.Append('|');
                var rules = ModAdvancedEconomy.At(station);
                bool first = true;
                if (rules != null)
                    foreach (var rule in rules.Values)
                    {
                        if (!ModAdvancedEconomy.Eligible(db, rule.Item)) continue;
                        var balance = AdvancedEconomyLedger.Get(station, rule);
                        int basis = Shop.PriceList(db, station, new[] { rule.Item })[0];
                        int pressure = ModAdvancedEconomy.Pressure(db, station);
                        if (!first) sb.Append(',');
                        first = false;
                        sb.Append(rule.Item).Append(':').Append(basis).Append(':').Append(balance.Amount).Append(':')
                            .Append(rule.Target).Append(':').Append(pressure).Append(':').Append(balance.ImportHops);
                    }
            }
            return sb.ToString();
        }

        static void Decode(string text, out List<ItemStack> items, out List<int> ships, out int reset, out int rare)
        {
            items = new List<ItemStack>();
            ships = new List<int>();
            reset = rare = -1;
            if (string.IsNullOrEmpty(text)) return;
            var parts = text.Split('|');
            foreach (var one in parts[0].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = one.Split(':');
                if (kv.Length == 2 && int.TryParse(kv[0], out int item) && int.TryParse(kv[1], out int amount)) items.Add(new ItemStack(item, amount));
            }
            if (parts.Length < 2) return;
            foreach (var one in parts[1].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                if (int.TryParse(one, out int ship)) ships.Add(ship);
            if (parts.Length >= 4 && int.TryParse(parts[2], out int r1) && int.TryParse(parts[3], out int r2)) { reset = r1; rare = r2; }
        }
    }
}
