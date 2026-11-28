// NetStateStock.cs
// NetState: the shared shop stock and the sector's news (part of the partial NetState; its fields are in NetState.cs).

using System.Collections.Generic;
using GoF2Remake.Data;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using GoF2Remake.Events;

namespace GoF2Remake.Multiplayer
{
    public partial class NetState
    {
        [Rpc(SendTo.Server)]
        internal void RequestFreightRpc(int orbit, uint epoch, bool ships, bool wrecks, bool initial, RpcParams rpc = default)
        {
            ulong client = rpc.Receive.SenderClientId;
            if (!Modding.ModAdvancedEconomy.PhysicalFreight || !NetRateLimit.Allow(client, NetRateLimit.Kind.FreightRequest) ||
                !NetGuard.InOrbit(client, orbit) || NetArena.IsArenaOrbit(orbit)) return;
            if (ships && RunnerOf(orbit)?.OwnerClientId == client)
            {
                var manifest = AdvancedEconomyLedger.FreightManifest(orbit);
                if (manifest != null) FreightManifestRpc(orbit, epoch, initial, manifest, RpcTarget.Single(client, RpcTargetUse.Temp));
            }
            // Salvage remains visible to every player; do not rebuild it for every initial ship retry.
            if (wrecks)
            {
                var manifest = AdvancedEconomyLedger.FreightWreckManifest(orbit);
                if (manifest != null) FreightWreckManifestRpc(orbit, epoch, manifest, RpcTarget.Single(client, RpcTargetUse.Temp));
            }
        }

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void FreightManifestRpc(int orbit, uint epoch, bool initial, string manifest, RpcParams rpc = default)
        {
            if (NetOrbit.Current?.Station == orbit) NetOrbit.Current.ReceiveFreightManifest(epoch, initial, manifest);
        }

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void FreightWreckManifestRpc(int orbit, uint epoch, string manifest, RpcParams rpc = default)
        {
            if (NetOrbit.Current?.Station == orbit) NetOrbit.Current.ReceiveFreightWreckManifest(epoch, manifest);
        }

        [Rpc(SendTo.Server)]
        internal void RequestFreightClaimRpc(int tag, ulong proxyId, int room, int sequence, RpcParams rpc = default)
        {
            ulong client = rpc.Receive.SenderClientId;
            int amount = 0, item = -1;
            var pilot = NetSquad.Find(client);
            if (sequence > 0 && NetRateLimit.Allow(client, NetRateLimit.Kind.Claim) &&
                NetProfiles.Controls(client) && pilot != null && pilot.InSpace && pilot.Hull > 0f &&
                pilot.HasCurrentWreckClaimPose && !NetArena.IsArenaOrbit(pilot.Station) &&
                (NetGame.PersistentHost && client == NetGame.LocalId || NetProfiles.ConsumeActionUpload(client, sequence)))
            {
                NetProxy proxy = null;
                bool valid = proxyId == ulong.MaxValue;
                if (!valid && NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(proxyId, out var obj))
                {
                    proxy = obj.GetComponent<NetProxy>();
                    valid = proxy != null;
                }
                if (valid)
                    amount = AdvancedEconomyLedger.ClaimFreight(pilot, NetProfiles.AccountOf(client), tag, proxy, room, out item);
            }
            FreightClaimResultRpc(sequence, item, amount, RpcTarget.Single(client, RpcTargetUse.Temp));
        }

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void FreightClaimResultRpc(int sequence, int item, int amount, RpcParams rpc = default) =>
            NetProfileClient.ApplyFreightClaim(sequence, item, amount);

        /// <summary>A player docked at 'station': the host's stock there (made now if it has none).</summary>
        [Rpc(SendTo.Server)]
        public void StockRequestRpc(int station, RpcParams rpc = default)
        {
            ulong client = rpc.Receive.SenderClientId;
            if (!NetRateLimit.Allow(client, NetRateLimit.Kind.Request) || !NetGuard.Station(station)) return;   // no lists for made-up stations
            StockRpc(station, NetStock.HostGet(station), RpcTarget.Single(client, RpcTargetUse.Temp));
        }
        /// <summary>A player bought (-1, for 'price') or sold (+1) one unit of an item at 'station', docked there; a unit no
        /// longer there goes back.</summary>
        [Rpc(SendTo.Server)]
        public void StockItemRpc(int station, int item, int delta, int price, RpcParams rpc = default)
        {
            ulong client = rpc.Receive.SenderClientId;
            if (!NetRateLimit.Allow(client, NetRateLimit.Kind.Trade)) return;
            // One unit a message, or several at once (a shop's Buy all / Sell all: Hangar.EndBatch); 'price' is one unit's.
            if (delta == 0 || Mathf.Abs(delta) > MaxTradeUnits || price < 0 || !NetGuard.Station(station) || !NetGuard.Item(item) || !NetGuard.DockedAt(client, station))
            {
                NetRateLimit.Reject(client, $"a trade of {delta} x item {item} at {station}");
                return;
            }
            if (NetStock.HostManagedPricing(station, item))
            {
                NetRateLimit.Reject(client, "legacy stock RPC for a server-priced commodity");
                return;
            }
            int done = NetStock.HostItem(station, item, delta, out int vatUnits);
            NetAudit.Trade(client, station, item, delta < 0 ? -done : done, price);   // the money log: what was granted
            if (delta < 0 && done < -delta)
                ItemRefusedRpc(station, item, price, -delta - done, RpcTarget.Single(client, RpcTargetUse.Temp));
            if (delta < 0 && vatUnits > 0 && NetProfiles.Enabled) NetFactions.OnPurchase(client, station, item, price, vatUnits);   // a faction station's VAT
            dirtyStock.Add(station);
        }
        [Rpc(SendTo.Server)]
        public void EconomyTradeRpc(int station, int item, int units, bool buy, long quote, int sequence, long request, RpcParams rpc = default)
        {
            ulong client = rpc.Receive.SenderClientId;
            int delta = 0; long credits = 0;
            string result = "Invalid or rate-limited commodity trade.";
            if (NetRateLimit.Allow(client, NetRateLimit.Kind.Trade) && NetGuard.Station(station) &&
                NetGuard.Item(item) && NetGuard.DockedAt(client, station))
            {
                result = NetStock.HostPricedTrade(client, station, item, units, buy, quote, sequence, out delta, out credits);
                dirtyStock.Add(station);
            }
            EconomyTradeResultRpc(station, item, delta, credits, result, request, RpcTarget.Single(client, RpcTargetUse.Temp));
        }

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void EconomyTradeResultRpc(int station, int item, int delta, long credits, string result, long request, RpcParams rpc = default) =>
            NetStock.PriceTradeResult(station, item, delta, credits, result, request);

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void ItemRefusedRpc(int station, int item, int price, int count, RpcParams rpc = default) => NetStock.ItemRefused(station, item, price, count);
        /// <summary>A player docked at 'station' wants the dealer's ship 'ship': theirs if it is still there (then off the list).</summary>
        [Rpc(SendTo.Server)]
        public void ReserveShipRpc(int station, int ship, RpcParams rpc = default)
        {
            ulong client = rpc.Receive.SenderClientId;
            if (!NetRateLimit.Allow(client, NetRateLimit.Kind.Reserve)) return;
            if (!NetGuard.Station(station) || !NetGuard.Ship(ship) || !NetGuard.DockedAt(client, station))
            {
                NetRateLimit.Reject(client, $"a reservation of ship {ship} at {station}");
                return;
            }
            bool ok = NetStock.HostReserveShip(station, ship);
            if (ok)
            {
                if (!reservations.TryGetValue((client, station), out var list)) reservations[(client, station)] = list = new List<int>();
                list.Add(ship);
            }
            ReserveResultRpc(station, ship, ok, RpcTarget.Single(client, RpcTargetUse.Temp));
            if (ok) dirtyStock.Add(station);
        }
        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void ReserveResultRpc(int station, int ship, bool ok, RpcParams rpc = default) => NetStock.ReserveResult(station, ship, ok);
        /// <summary>A player's ship trade at 'station': the dealer's row 'removed' became 'added' (-1 = none). Only a row this
        /// player reserved goes (it is off the list already), and only that row (not bought after all) or the player's own old
        /// hull (a trade-in) comes: no dealer ships made up or wiped for everyone.</summary>
        [Rpc(SendTo.Server)]
        public void StockShipRpc(int station, int removed, int added, RpcParams rpc = default)
        {
            ulong client = rpc.Receive.SenderClientId;
            if (!NetRateLimit.Allow(client, NetRateLimit.Kind.Reserve)) return;
            var from = NetSquad.Find(client);
            if (from == null || !NetGuard.Station(station) || !NetGuard.DockedAt(client, station)
                || (removed != -1 && !NetGuard.Ship(removed)) || (added != -1 && !NetGuard.Ship(added)))
            {
                NetRateLimit.Reject(client, $"a ship trade {removed} -> {added} at {station}");
                return;
            }
            reservations.TryGetValue((client, station), out var reserved);
            bool ok;
            if (removed >= 0) ok = reserved != null && reserved.Remove(removed) && (added < 0 || added == from.ShipIndex || added == from.PreviousShip);
            else ok = added >= 0 && reserved != null && reserved.Remove(added);   // the reserved row back: not bought after all
            if (!ok) { NetRateLimit.Reject(client, $"an unreserved ship trade {removed} -> {added} at {station}"); return; }
            if (NetStock.HostShip(station, removed, added)) dirtyStock.Add(station);
            if (removed >= 0 && NetProfiles.Enabled) NetFactions.OnShipBought(client, station, removed);   // a faction station's VAT
        }
        /// <summary>Host: the stock of 'station' for every player docked there.</summary>
        internal void BroadcastStock(int station)
        {
            string text = null;
            foreach (var p in NetPlayer.All)
                if (p != null && p.IsSpawned && p.InHangar && p.Station == station)
                {
                    // Build the shared payload only if a player is actually docked here.
                    text ??= NetStock.HostGet(station);
                    StockRpc(station, text, RpcTarget.Single(p.OwnerClientId, RpcTargetUse.Temp));
                }
        }
        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void StockRpc(int station, string text, RpcParams rpc = default) => NetStock.Apply(station, text);
        /// <summary>Server: a news item for everyone.</summary>
        internal void BroadcastNews(string packed)
        {
            if (IsServer) NewsRpc(packed);
        }
        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
        void NewsRpc(string packed) => NetNews.OnReceive(packed, false);
        /// <summary>Server: a news item to one player ('reset': their list starts over first).</summary>
        internal void SendNews(ulong client, string packed, bool reset)
        {
            if (IsServer) NewsToRpc(packed ?? "", reset, RpcTarget.Single(client, RpcTargetUse.Temp));
        }
        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void NewsToRpc(string packed, bool reset, RpcParams rpc = default) => NetNews.OnReceive(packed, reset);
        /// <summary>An orbit's authority: its raiders are all down, 'killers' downed them (NetOrbit). Checked: the sender runs
        /// that orbit, the pilots exist and are there, the numbers are sane; one item per orbit and raid (NetNews).</summary>
        [Rpc(SendTo.Server)]
        public void DefenseReportRpc(int station, int race, int kills, ulong[] killers, RpcParams rpc = default)
        {
            ulong client = rpc.Receive.SenderClientId;
            if (!NetRateLimit.Allow(client, NetRateLimit.Kind.Kill)) return;
            var from = NetSquad.Find(client);
            if (from == null || from.RunsOrbit != station || !NetGuard.InOrbit(client, station) || NetArena.IsArenaOrbit(station)) return;
            if (kills < NetNews.MinDefenseKills || kills > 60 || killers == null || killers.Length == 0 || killers.Length > 16)
            {
                NetRateLimit.Reject(client, $"a defence report of {kills} kills");
                return;
            }
            var names = new List<string>();
            foreach (ulong k in killers)
            {
                var p = NetSquad.Find(k);
                if (p != null && p.Station == station && !names.Contains(p.DisplayName)) names.Add(p.DisplayName);
            }
            if (names.Count > 0) NetNews.Defended(station, race, kills, names);
        }
        /// <summary>Its owner is done with a NetProxy / NetCrate (the ship left or died for good, the crate was taken). Only
        /// those: a client's own player object (or anything else) isn't its to remove.</summary>
        [Rpc(SendTo.Server)]
        public void DespawnRpc(ulong objectId, RpcParams rpc = default)
        {
            ulong client = rpc.Receive.SenderClientId;
            if (!NetRateLimit.Allow(client, NetRateLimit.Kind.Despawn)) return;
            if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(objectId, out var obj) || obj == null || !obj.IsSpawned
                || obj.OwnerClientId != client) return;
            if (obj.GetComponent<NetProxy>() == null && obj.GetComponent<NetCrate>() == null) { NetRateLimit.Reject(client, "a despawn of no ship or crate"); return; }
            obj.Despawn();
        }
        /// <summary>Host, each sweep: an orbit with destroyed asteroids and no player in it for AsteroidRespawnSeconds gets its whole
        /// field back (the list is dropped; whoever arrives next asks for it and finds it empty). Someone arriving first restarts the wait.</summary>
        void RespawnEmptyAsteroidFields(Dictionary<ulong, NetPlayer> owners)
        {
            if (destroyed.Count == 0) { emptySince.Clear(); return; }
            float now = Time.unscaledTime;
            List<int> respawn = null;
            foreach (var station in destroyed.Keys)
            {
                bool occupied = false;
                foreach (var p in owners.Values) if (p.InSpace && p.Station == station) { occupied = true; break; }
                if (occupied) { emptySince.Remove(station); continue; }
                if (!emptySince.TryGetValue(station, out float since)) { emptySince[station] = now; continue; }
                if (now - since >= AsteroidRespawnSeconds) (respawn ?? (respawn = new List<int>())).Add(station);
            }
            if (respawn == null) return;
            foreach (int station in respawn) { destroyed.Remove(station); emptySince.Remove(station); }
        }
        /// <summary>Host, every PruneSeconds: the bookkeeping of players who left and of the past goes (the hits, invitations,
        /// reservations and mission records would otherwise grow for the whole session).</summary>
        void Prune()
        {
            float now = Time.unscaledTime;
            var oldHits = new List<(ulong, ulong)>();
            foreach (var kv in hits) if (now - kv.Value > 60f) oldHits.Add(kv.Key);
            foreach (var k in oldHits) hits.Remove(k);
            PruneBountyCombat(now);
            var oldInvites = new List<(ulong, ulong)>();
            foreach (var kv in invites) if (now - kv.Value > NetSquad.InviteSeconds + InviteGraceSeconds) oldInvites.Add(kv.Key);
            foreach (var k in oldInvites) invites.Remove(k);
            var goneReservations = new List<(ulong, int)>();
            foreach (var kv in reservations) if (kv.Value.Count == 0 || NetSquad.Find(kv.Key.client) == null) goneReservations.Add(kv.Key);
            foreach (var k in goneReservations) reservations.Remove(k);
            var goneMissions = new List<long>();
            foreach (var kv in missions)
            {
                // A record goes a minute after its result (a second result of the same mission is dropped meanwhile), or
                // once nobody of its team is in the session any more.
                kv.Value.RemoveAll(r =>
                {
                    if (r.ended) return now - r.endedAt > 60f;
                    foreach (var id in r.members) if (NetSquad.Find(id) != null) return false;
                    return true;
                });
                if (kv.Value.Count == 0) goneMissions.Add(kv.Key);
            }
            foreach (var k in goneMissions) missions.Remove(k);
        }
    }
}
