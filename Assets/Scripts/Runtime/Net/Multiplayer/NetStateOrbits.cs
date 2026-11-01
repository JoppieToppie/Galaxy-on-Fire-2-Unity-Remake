// NetStateOrbits.cs
// NetState: who runs each orbit, hits, asteroids, the orbit authority's objects, a hangar's NPC ships (part of the partial NetState; its fields are in NetState.cs).

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
        /// <summary>Server, every RunnerSeconds: one player runs each orbit someone flies in. The runner stays while they fly
        /// there; when they leave (dock, jump, disconnect) the next one is picked: a player whose game built the traffic (the
        /// first to arrive; of two at once the lower client id, the other stands down, NetOrbit), else the lowest client id.
        /// The ships the old runner's game ran are handed to the new one (HandOver), so nothing vanishes in between.</summary>
        void UpdateOrbitRunners()
        {
            if ((runnerTimer -= Time.unscaledDeltaTime) > 0f) return;
            runnerTimer = RunnerSeconds;
            var here = new Dictionary<int, List<NetPlayer>>();
            foreach (var p in NetPlayer.All)
            {
                if (p == null || !p.IsSpawned || !p.InSpace || p.Station < 0) continue;
                if (!here.TryGetValue(p.Station, out var list)) here[p.Station] = list = new List<NetPlayer>();
                list.Add(p);
            }
            List<int> empty = null;
            foreach (int orbit in orbitRunners.Keys) if (!here.ContainsKey(orbit)) (empty ??= new List<int>()).Add(orbit);
            if (empty != null) foreach (int orbit in empty) orbitRunners.Remove(orbit);   // its objects go with the sweep
            foreach (var pair in here)
            {
                NetPlayer runner = null;
                if (orbitRunners.TryGetValue(pair.Key, out ulong id))
                    foreach (var p in pair.Value) if (p.OwnerClientId == id) { runner = p; break; }
                if (runner != null) continue;
                foreach (var p in pair.Value)
                    if (p.OrbitAuthority && (runner == null || p.OwnerClientId < runner.OwnerClientId)) runner = p;
                if (runner == null)
                    foreach (var p in pair.Value)
                        if (runner == null || p.OwnerClientId < runner.OwnerClientId) runner = p;
                orbitRunners[pair.Key] = runner.OwnerClientId;
            }
            foreach (var p in NetPlayer.All)
            {
                if (p == null || !p.IsSpawned) continue;
                int orbit = p.InSpace && orbitRunners.TryGetValue(p.Station, out ulong id) && id == p.OwnerClientId ? p.Station : -1;
                p.SetRunsOrbit(orbit);
            }
            UpdateInterest();
            HandOver(here);
        }

        /// <summary>Server: an orbit's NetProxy / NetCrate objects only for the players flying there and their owner
        /// (NetInterest): a player arriving gets them spawned with their state and the server's last pose (OnSynchronize),
        /// one leaving has them despawned. Before, every player of the session got every orbit's objects and updates.</summary>
        void UpdateInterest()
        {
            var manager = NetworkManager;
            interestObjects.Clear();
            interestObjects.AddRange(manager.SpawnManager.SpawnedObjectsList);
            foreach (var obj in interestObjects)
            {
                if (obj == null || !obj.IsSpawned) continue;
                var proxy = obj.GetComponent<NetProxy>();
                var crate = proxy == null ? obj.GetComponent<NetCrate>() : null;
                if (proxy == null && crate == null) continue;
                int station = proxy != null ? proxy.Station : crate.Station;
                foreach (ulong client in manager.ConnectedClientsIds)
                {
                    if (client == NetworkManager.ServerClientId) continue;   // the host has everything
                    bool want = NetInterest.Sees(client, obj.OwnerClientId, station), has = obj.IsNetworkVisibleTo(client);
                    if (want == has) continue;
                    try
                    {
                        if (want) obj.NetworkShow(client);
                        else obj.NetworkHide(client);
                    }
                    catch (System.Exception e) { Debug.LogWarning($"NetState: showing object {obj.NetworkObjectId} to {client}: {e.Message}"); }
                }
            }
        }

        readonly List<NetworkObject> interestObjects = new List<NetworkObject>();
        readonly Dictionary<ulong, float> handOverWait = new Dictionary<ulong, float>();
        /// <summary>Server: every orbit object (NetProxy: ships, fixed objects, turrets, capital ships and their parts, junk,
        /// wrecks; NetCrate) whose game left its orbit (docked, jumped, disconnected: the host holds those) goes to the orbit's
        /// runner, which rebuilds it from its description where it is and drives the same proxy from then on
        /// (NetOrbit.AdoptHandedOver, NetCrate's takeover), so the players still there see no change. Not a player's wingmen
        /// (they go with their player, the sweep); a squad mission's ships wait a moment for a squadmate's promotion.</summary>
        void HandOver(Dictionary<int, List<NetPlayer>> here)
        {
            if (orbitRunners.Count == 0) return;
            float now = Time.unscaledTime;
            foreach (var obj in interestObjects)
            {
                if (obj == null || !obj.IsSpawned) continue;
                var proxy = obj.GetComponent<NetProxy>();
                var crate = proxy == null ? obj.GetComponent<NetCrate>() : null;
                if (proxy == null && crate == null) continue;
                int station = proxy != null ? proxy.Station : crate.Station;
                if (!orbitRunners.TryGetValue(station, out ulong runner) || !here.TryGetValue(station, out var players)) continue;
                bool ownerHere = players.Exists(p => p.OwnerClientId == obj.OwnerClientId);
                // A disconnected player's object the host inherited while flying there: its game has no ship for it.
                bool inherited = proxy != null && proxy.Creator != obj.OwnerClientId && !proxy.HandedOver;
                bool mission = proxy != null && (proxy.IsMissionShip || proxy.IsJunk) && players.Exists(p => NetSquad.SameClient(proxy.Creator, p));
                float waited = mission && handOverWait.TryGetValue(obj.NetworkObjectId, out float since) ? now - since : 0f;
                var act = NetHandOverRule.Decide(ownerHere, crate != null, inherited, proxy != null && proxy.IsWingman, proxy == null || proxy.HandOverable, mission, waited);
                if (act == NetHandOverRule.Act.Wait && !handOverWait.ContainsKey(obj.NetworkObjectId)) handOverWait[obj.NetworkObjectId] = now;
                if (act != NetHandOverRule.Act.HandOver) continue;   // its game runs it, a squadmate's promotion, or the sweep's
                handOverWait.Remove(obj.NetworkObjectId);
                if (proxy != null) proxy.MarkHandedOver();
                if (obj.OwnerClientId != runner)
                {
                    if (runner != NetworkManager.ServerClientId && !obj.IsNetworkVisibleTo(runner)) obj.NetworkShow(runner);
                    obj.ChangeOwnership(runner);
                }
                staleSince.Remove(obj.NetworkObjectId);
            }
        }
        /// <summary>Server: 'shooter' hit the network object 'target' (a NetPlayer or a NetProxy) now.</summary>
        internal void NoteHit(ulong target, ulong shooter)
        {
            if (IsServer) hits[(target, shooter)] = Time.unscaledTime;
        }
        /// <summary>Server: 'shooter' hit 'target' within the last 'seconds'.</summary>
        internal bool HitRecently(ulong target, ulong shooter, float seconds) =>
            hits.TryGetValue((target, shooter), out float t) && Time.unscaledTime - t <= seconds;
        // ---- asteroids --------------------------------------------------------------------------------------
        /// <summary>A player destroyed asteroid 'index' of 'station' (shot, mined, rammed): only a player flying there (else
        /// one client could empty every field of the session).</summary>
        [Rpc(SendTo.Server)]
        public void AsteroidDestroyedRpc(int station, int index, bool mined, RpcParams rpc = default)
        {
            ulong client = rpc.Receive.SenderClientId;
            if (!NetRateLimit.Allow(client, NetRateLimit.Kind.Asteroid)) return;
            if (index < 0 || index >= MaxAsteroids || !NetGuard.InOrbit(client, station)) { NetRateLimit.Reject(client, $"asteroid {index} of orbit {station}"); return; }
            if (!destroyed.TryGetValue(station, out var set)) destroyed[station] = set = new HashSet<int>();
            var by = NetSquad.Find(client);
            if (set.Add(index)) AsteroidGoneRpc(station, index, by != null ? by.DisplayName : "", mined);
        }
        /// <summary>'by': the pilot who destroyed it, 'mined': drilled out (else shot / rammed): a miner's message (Mining).</summary>
        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
        void AsteroidGoneRpc(int station, int index, string by, bool mined) => NetOrbit.Current?.OnAsteroidGone(station, index, by, mined);
        /// <summary>A player arrived in 'station': the asteroids already gone there.</summary>
        [Rpc(SendTo.Server)]
        public void RequestDestroyedRpc(int station, RpcParams rpc = default)
        {
            ulong client = rpc.Receive.SenderClientId;
            if (!NetRateLimit.Allow(client, NetRateLimit.Kind.Request) || !NetGuard.Orbit(station)) return;
            var list = destroyed.TryGetValue(station, out var set) ? new List<int>(set).ToArray() : new int[0];
            DestroyedListRpc(station, list, RpcTarget.Single(client, RpcTargetUse.Temp));
        }
        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void DestroyedListRpc(int station, int[] indices, RpcParams rpc = default) => NetOrbit.Current?.OnDestroyedList(station, indices);
        /// <summary>An orbit authority shows its ship 'localId' (NetOrbit's list) of 'station' to the others.</summary>
        [Rpc(SendTo.Server)]
        public void SpawnProxyRpc(int station, int localId, RpcParams rpc = default) => RequestSpawn(rpc.Receive.SenderClientId, station, localId, false);
        /// <summary>An orbit authority shows its crate 'localId' of 'station' to the others.</summary>
        [Rpc(SendTo.Server)]
        public void SpawnCrateRpc(int station, int localId, RpcParams rpc = default) => RequestSpawn(rpc.Receive.SenderClientId, station, localId, true);
        /// <summary>A spawn request: limited per client (a flood of objects for everyone), only for the orbit the sender flies
        /// in (no objects planted in other orbits), at most MaxProxies / MaxCrates owned at once.</summary>
        void RequestSpawn(ulong client, int station, int localId, bool crate)
        {
            if (!NetRateLimit.Allow(client, crate ? NetRateLimit.Kind.CrateSpawn : NetRateLimit.Kind.ProxySpawn)) return;
            if (localId < 0 || !NetGuard.Orbit(station) || NetSquad.Find(client) == null) { NetRateLimit.Reject(client, $"a spawn {localId} in orbit {station}"); return; }
            owned.TryGetValue(client, out var n);
            if (crate ? n.crates >= MaxCrates : n.proxies >= MaxProxies) { NetRateLimit.Reject(client, crate ? "too many crates" : "too many ships"); return; }
            var request = new SpawnRequest { client = client, station = station, localId = localId, crate = crate, time = Time.unscaledTime };
            if (NetGuard.InOrbit(client, station)) Spawn(request);
            else if (pendingSpawns.Count < MaxPendingSpawns) pendingSpawns.Add(request);
        }
        void Spawn(SpawnRequest r)
        {
            owned.TryGetValue(r.client, out var n);
            if (r.crate)
            {
                if (cratePrefab == null) return;
                var go = Instantiate(cratePrefab);
                go.GetComponent<NetCrate>().Init(r.station, r.localId);
                var obj = go.GetComponent<NetworkObject>();
                obj.DontDestroyWithOwner = true;   // like the ships: the runner takes it over (HandOver)
                int station = r.station;
                obj.CheckObjectVisibility = client => NetInterest.Sees(client, obj.OwnerClientId, station);   // only that orbit's players
                obj.SpawnWithOwnership(r.client, false);
                n.crates++;
            }
            else
            {
                if (proxyPrefab == null) return;
                var go = Instantiate(proxyPrefab);
                go.GetComponent<NetProxy>().Init(r.station, r.localId);
                var obj = go.GetComponent<NetworkObject>();
                obj.DontDestroyWithOwner = true;   // an owner who disconnects leaves it to the host: the others can take it over
                int station = r.station;
                obj.CheckObjectVisibility = client => NetInterest.Sees(client, obj.OwnerClientId, station);   // only that orbit's players
                obj.SpawnWithOwnership(r.client, false);
                n.proxies++;
            }
            owned[r.client] = n;
        }
        /// <summary>The waiting spawn requests: their senders arrived in that orbit, or they go.</summary>
        void UpdatePendingSpawns()
        {
            if (pendingSpawns.Count == 0) return;
            float now = Time.unscaledTime;
            pendingSpawns.RemoveAll(r =>
            {
                if (NetGuard.InOrbit(r.client, r.station)) { Spawn(r); return true; }
                if (now - r.time < SpawnWaitSeconds) return false;
                NetRateLimit.Reject(r.client, $"a spawn in orbit {r.station}, where it isn't");
                return true;
            });
        }
        /// <summary>A player took a ship / junk / crate of another game over (NetOrbit, FreelanceOrbit.Promote): its old copy
        /// goes for everyone, and when its creator is still in that orbit their own ship goes too (TakenOverRpc). Only a
        /// player flying in that orbit takes its objects over, and from a creator still there only a squadmate the creator's
        /// mission objects (a mission orbit's takeover): no wiping other players' ships from anywhere.</summary>
        [Rpc(SendTo.Server)]
        public void AdoptedRpc(ulong objectId, RpcParams rpc = default)
        {
            ulong client = rpc.Receive.SenderClientId;
            if (!NetRateLimit.Allow(client, NetRateLimit.Kind.Adopt)) return;
            if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(objectId, out var obj) || obj == null || !obj.IsSpawned) return;
            var proxy = obj.GetComponent<NetProxy>();
            var crate = proxy == null ? obj.GetComponent<NetCrate>() : null;
            if (proxy == null && crate == null) { NetRateLimit.Reject(client, "a takeover of no ship or crate"); return; }
            int station = proxy != null ? proxy.Station : crate.Station;
            ulong creator = proxy != null ? proxy.Creator : obj.OwnerClientId;
            if (creator == client) return;
            if (!NetGuard.InOrbit(client, station)) { NetRateLimit.Reject(client, $"a takeover in orbit {station}, where it isn't"); return; }
            if (NetOrbit.InOrbit(creator, station) && obj.OwnerClientId == creator)
            {
                // A mission orbit's takeover (FreelanceOrbit.Promote): the runner's squadmate, or anyone once the runner no
                // longer runs that mission (it left the squad and with it the mission).
                var owner = NetSquad.Find(creator);
                bool missionObject = proxy != null ? proxy.IsMissionShip || proxy.IsJunk : crate.IsMissionCrate;
                if (!missionObject || owner == null || (!NetSquad.SameClient(client, owner) && owner.MissionRun != 0))
                {
                    NetRateLimit.Reject(client, "a takeover of a ship its game still runs");
                    return;
                }
                if (proxy != null) proxy.TakenOverRpc();
                else crate.TakenOverRpc();
                return;   // the creator's game drops its own (NetOrbit's scan despawns it)
            }
            obj.Despawn();
        }
        // ---- a hangar's NPC ships (NetHangar) -------------------------------------------------------------------
        /// <summary>The game running 'station's hangar (docked there): an NPC ship lands (key, ship, yaw) or takes off (key):
        /// the others docked there do the same.</summary>
        [Rpc(SendTo.Server)]
        public void HangarNpcRpc(int station, bool landing, int key, int ship, float yaw, RpcParams rpc = default)
        {
            ulong client = rpc.Receive.SenderClientId;
            if (!NetRateLimit.Allow(client, NetRateLimit.Kind.Hangar)) return;
            if (!NetGuard.Station(station) || !NetGuard.DockedAt(client, station) || (landing && (!NetGuard.Ship(ship) || !NetGuard.Finite(yaw))))
            {
                NetRateLimit.Reject(client, $"a hangar ship at {station}");
                return;
            }
            foreach (var p in NetPlayer.All)
                if (p != null && p.IsSpawned && p.OwnerClientId != client && p.InHangar && p.Station == station)
                    HangarNpcToRpc(landing, key, ship, yaw, RpcTarget.Single(p.OwnerClientId, RpcTargetUse.Temp));
        }
        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void HangarNpcToRpc(bool landing, int key, int ship, float yaw, RpcParams rpc = default) => NetHangar.Current?.OnRemoteNpc(landing, key, ship, yaw);
        /// <summary>A player docked at 'station' wants its NPC ships as they are: the running game sends them.</summary>
        [Rpc(SendTo.Server)]
        public void HangarSnapshotRequestRpc(int station, RpcParams rpc = default)
        {
            ulong client = rpc.Receive.SenderClientId;
            if (!NetRateLimit.Allow(client, NetRateLimit.Kind.Request) || !NetGuard.Station(station)) return;
            NetPlayer runner = null;
            foreach (var p in NetPlayer.All)
                if (p != null && p.IsSpawned && p.OwnerClientId != client && p.InHangar && p.Station == station && p.HangarRun
                    && (runner == null || p.OwnerClientId < runner.OwnerClientId)) runner = p;
            if (runner != null) HangarSnapshotAskRpc(client, RpcTarget.Single(runner.OwnerClientId, RpcTargetUse.Temp));
        }
        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void HangarSnapshotAskRpc(ulong requester, RpcParams rpc = default) => NetHangar.Current?.SendSnapshot(requester);
        /// <summary>The running game's answer: only for a player docked where the sender is, bounded in length.</summary>
        [Rpc(SendTo.Server)]
        public void HangarSnapshotRpc(ulong requester, string data, RpcParams rpc = default)
        {
            ulong client = rpc.Receive.SenderClientId;
            if (!NetRateLimit.Allow(client, NetRateLimit.Kind.Hangar)) return;
            var to = NetSquad.Find(requester);
            if (to == null || to.OwnerClientId == client || !to.InHangar || !NetGuard.DockedAt(client, to.Station)
                || (data != null && data.Length > NetGuard.MaxSnapshot))
            {
                NetRateLimit.Reject(client, "a hangar snapshot");
                return;
            }
            HangarSnapshotToRpc(data ?? "", RpcTarget.Single(requester, RpcTargetUse.Temp));
        }
        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void HangarSnapshotToRpc(string data, RpcParams rpc = default) => NetHangar.Current?.OnSnapshot(data);
        /// <summary>The Kaamo siege won in a player's game (flying in the Kaamo Club's orbit): the others there (their siege
        /// view) win it too.</summary>
        [Rpc(SendTo.Server)]
        public void SiegeWonRpc(int station, RpcParams rpc = default)
        {
            ulong client = rpc.Receive.SenderClientId;
            if (!NetRateLimit.Allow(client, NetRateLimit.Kind.Siege)) return;
            if (station != KaamoClub.Station || !NetGuard.InOrbit(client, station)) { NetRateLimit.Reject(client, $"a siege won at {station}"); return; }
            // The news: everyone fighting there broke it.
            var heroes = new List<string>();
            foreach (var p in NetPlayer.All)
                if (p != null && p.IsSpawned && p.InSpace && p.Station == station) heroes.Add(p.DisplayName);
            NetNews.Post(NetNews.Kind.Defense, $"{NetNews.Names(heroes)} {(heroes.Count == 1 ? "breaks" : "break")} the pirate siege of the Kaamo Club ({NetNews.Place(station)})",
                         station, "kaamo", 1800f);
            foreach (var p in NetPlayer.All)
                if (p != null && p.IsSpawned && p.OwnerClientId != client && p.InSpace && p.Station == station)
                    SiegeWonToRpc(RpcTarget.Single(p.OwnerClientId, RpcTargetUse.Temp));
        }
        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void SiegeWonToRpc(RpcParams rpc = default) => World.KaamoSiege.Current?.OnRemoteWin();
    }
}
