// NetFactionWar.cs
using System.Collections.Generic;
using System.IO;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.Modding;
using Unity.Netcode;
using UnityEngine;

namespace GoF2Remake.Multiplayer
{
    public partial class NetState
    {
        NpcFactionWar.Campaign factionWar;
        sealed class FleetRoster
        {
            public NpcFactionWar.Battle battle;
            public ulong creator = ulong.MaxValue;
            public ulong casualties;
            public long nextLossAt;
            public int lossInterval;
            public bool finished;
            public float startedAt, endsAt;
            public readonly Dictionary<int, ulong> ships = new Dictionary<int, ulong>();
        }

        readonly Dictionary<int, FleetRoster> fleetByOrbit = new Dictionary<int, FleetRoster>();
        bool factionWarDirty;
        float retryWarSaveAt;
        float nextWarScheduleAt;
        readonly List<int> retiredWarOrbits = new List<int>();

        string WarPath => NetProfiles.Enabled ? Path.Combine(NetProfiles.Folder, "npc-faction-war.json") : null;

        void EnsureFactionWar()
        {
            if (!IsServer || !ModNpcFactionWar.Enabled || factionWar != null) return;
            string snapshot = null;
            try
            {
                if (WarPath != null && File.Exists(WarPath)) snapshot = File.ReadAllText(WarPath);
            }
            catch (System.Exception e) when (e is IOException || e is System.UnauthorizedAccessException)
            {
                Debug.LogError($"NPC faction war could not be loaded: {e.Message}");
                return;
            }
            factionWar = NpcFactionWar.Campaign.Read(snapshot);
            NpcFactionWar.SetNetworkCampaign(factionWar);
        }

        void FlushFactionWar()
        {
            if (!factionWarDirty || Time.unscaledTime < retryWarSaveAt) return;
            if (WarPath == null || NetProfiles.Write(WarPath, factionWar.Snapshot())) factionWarDirty = false;
            else retryWarSaveAt = Time.unscaledTime + 10f;
        }

        [Rpc(SendTo.Server)]
        void RequestFactionWarSnapshotRpc(RpcParams rpc = default)
        {
            ulong client = rpc.Receive.SenderClientId;
            if (!NetRateLimit.Allow(client, NetRateLimit.Kind.Request)) return;
            EnsureFactionWar();
            if (factionWar != null)
                FactionWarSnapshotRpc(factionWar.Snapshot(), RpcTarget.Single(client, RpcTargetUse.Temp));
        }

        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
        void BroadcastFactionWarRpc(string json)
        {
            if (!IsServer) NpcFactionWar.SetNetworkCampaign(NpcFactionWar.Campaign.Read(json));
        }

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void FactionWarSnapshotRpc(string json, RpcParams rpc = default)
        {
            if (!IsServer) NpcFactionWar.SetNetworkCampaign(NpcFactionWar.Campaign.Read(json));
        }

        // Called from the server sweep even on an empty dedicated server. No Unity ship is created until an orbit runner arrives.
        void TickFactionWar()
        {
            if (!IsServer || !ModNpcFactionWar.Enabled) return;
            long now = NpcFactionWar.Now;
            if (Time.unscaledTime >= nextWarScheduleAt)
            {
                nextWarScheduleAt = Time.unscaledTime + 20f;
                EnsureFactionWar();
                if (factionWar != null && factionWar.TryStartAny(NetGame.Db, now, orbit =>
                    !fleetByOrbit.ContainsKey(orbit) && !NetArena.IsArenaOrbit(orbit) &&
                    !NetPlayerStations.StationClosedByCombat(orbit), out var battle))
                {
                    int interval = Mathf.Max(1, ModNpcFactionWar.BattleSeconds / (2 * battle.shipsPerSide));
                    fleetByOrbit[battle.orbit] = new FleetRoster { battle = battle, lossInterval = interval,
                        nextLossAt = now + interval, endsAt = Time.unscaledTime + ModNpcFactionWar.BattleSeconds };
                    factionWarDirty = true;
                    NetNews.Post(NetNews.Kind.War, $"{Standing.RaceName(battle.attacker)} and {Standing.RaceName(battle.defender)} fleets are fighting at {NetNews.Place(battle.orbit)}.", battle.orbit);
                    ModScripts.Raise("npcFactionBattleStarted", battle.orbit, battle.system, battle.defender, battle.attacker, battle.shipsPerSide);
                }
            }
            if (factionWar == null || fleetByOrbit.Count == 0) return;

            retiredWarOrbits.Clear();
            foreach (var pair in fleetByOrbit)
            {
                var roster = pair.Value;
                var battle = roster.battle;
                if (battle.expires < now)
                {
                    RetireWarFleet(roster, true);
                    retiredWarOrbits.Add(pair.Key);
                    continue;
                }
                if (roster.finished) continue;
                bool occupied = false;
                foreach (var pilot in NetPlayer.All)
                    if (pilot != null && pilot.IsSpawned && pilot.InSpace && pilot.Station == battle.orbit)
                    { occupied = true; break; }
                if (roster.creator != ulong.MaxValue)
                {
                    // Keep physical combat when players remain; ordinary orbit-runner handover preserves the proxies.
                    if (occupied) continue;
                    RetireWarFleet(roster, false);
                    roster.creator = ulong.MaxValue;
                    roster.ships.Clear();
                    roster.nextLossAt = now + roster.lossInterval;
                }
                if (now < roster.nextLossAt) continue;
                roster.nextLossAt = now + roster.lossInterval;
                // Symmetric abstract attrition, with occasional quiet intervals. Unresolved engagements still time out.
                if (UnityEngine.Random.value < 0.25f) continue;
                int selected = -1, count = 0;
                for (int slot = 0; slot < 2 * battle.shipsPerSide; slot++)
                    if ((roster.casualties & (1UL << slot)) == 0 && UnityEngine.Random.Range(0, ++count) == 0) selected = slot;
                if (selected >= 0) RecordWarLoss(roster, selected, now);
            }
            foreach (int orbit in retiredWarOrbits) fleetByOrbit.Remove(orbit);
        }

        void RetireWarFleet(FleetRoster roster, bool notify)
        {
            var battle = roster.battle;
            if (notify)
                foreach (var pilot in NetPlayer.All)
                    if (pilot != null && pilot.IsSpawned && pilot.InSpace && pilot.Station == battle.orbit)
                        EndFactionBattleRpc(battle.id, battle.orbit, battle.shipsPerSide,
                            RpcTarget.Single(pilot.OwnerClientId, RpcTargetUse.Temp));
            // Drop the old proxies when the orbit empties. A returning runner must only spawn the surviving slots.
            var remove = new List<NetworkObject>();
            foreach (var obj in NetworkManager.SpawnManager.SpawnedObjectsList)
            {
                if (obj == null || !obj.IsSpawned) continue;
                var ship = obj.GetComponent<NetProxy>();
                if (ship == null || ship.Station != battle.orbit) continue;
                int slot = battle.Slot(ship.EventTag);
                if (slot >= 0 && slot < 2 * battle.shipsPerSide) remove.Add(obj);
            }
            foreach (var obj in remove)
                if (obj.IsSpawned) obj.Despawn();
        }

        [Rpc(SendTo.Server)]
        internal void RequestFactionBattleRpc(int orbit, RpcParams rpc = default)
        {
            ulong client = rpc.Receive.SenderClientId;
            if (!ModNpcFactionWar.Enabled || !NetRateLimit.Allow(client, NetRateLimit.Kind.Request) ||
                !NetGuard.InOrbit(client, orbit) || RunnerOf(orbit)?.OwnerClientId != client ||
                NetArena.IsArenaOrbit(orbit) || NetPlayerStations.StationClosedByCombat(orbit)) return;
            if (!fleetByOrbit.TryGetValue(orbit, out var roster) || roster.finished || roster.battle.expires < NpcFactionWar.Now) return;
            // A request attaches a local simulation to a server-started battle; it never starts a campaign engagement.
            if (roster.creator == ulong.MaxValue)
            {
                roster.creator = client;
                roster.startedAt = Time.unscaledTime;
            }
            else if (roster.creator != client) return;
            var battle = roster.battle;
            StartFactionBattleRpc(battle.id, battle.orbit, battle.system, battle.defender, battle.attacker, battle.shipsPerSide,
                battle.expires, roster.casualties, RpcTarget.Single(client, RpcTargetUse.Temp));
        }

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void StartFactionBattleRpc(int id, int orbit, int system, int defender, int attacker, int ships, long expires,
            ulong casualties, RpcParams rpc = default) =>
            NetOrbit.Current?.BeginFactionBattle(new NpcFactionWar.Battle(id, orbit, system, defender, attacker, ships, expires), casualties);

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void EndFactionBattleRpc(int id, int orbit, int ships, RpcParams rpc = default) =>
            NetOrbit.Current?.EndFactionBattle(new NpcFactionWar.Battle(id, orbit, -1, -1, -1, ships, 0));

        internal bool IsFactionWarShip(NetProxy ship, int race)
        {
            if (!IsServer || !ModNpcFactionWar.Enabled || !fleetByOrbit.TryGetValue(ship.Station, out var roster) ||
                roster.creator == ulong.MaxValue || roster.creator != ship.Creator || ship.SpawnedAt < roster.startedAt || ship.SpawnedAt > roster.endsAt) return false;
            var battle = roster.battle;
            int slot = battle.Slot(ship.EventTag);
            if (slot < 0 || slot >= 2 * battle.shipsPerSide || race != (slot < battle.shipsPerSide ? battle.defender : battle.attacker))
                return false;
            if (roster.ships.TryGetValue(slot, out ulong id)) return id == ship.NetworkObjectId;
            if ((roster.casualties & (1UL << slot)) != 0) return false;
            roster.ships.Add(slot, ship.NetworkObjectId);
            return true;
        }

        internal void RecordFactionWarCasualty(NetProxy ship)
        {
            if (factionWar == null || !IsFactionWarShip(ship, ship.Race)) return;
            var roster = fleetByOrbit[ship.Station];
            RecordWarLoss(roster, roster.battle.Slot(ship.EventTag), NpcFactionWar.Now);
        }

        void RecordWarLoss(FleetRoster roster, int slot, long now)
        {
            var battle = roster.battle;
            if (roster.finished || battle.expires < now || slot < 0 || slot >= 2 * battle.shipsPerSide ||
                (roster.casualties & (1UL << slot)) != 0) return;
            roster.casualties |= 1UL << slot;
            int race = slot < battle.shipsPerSide ? battle.defender : battle.attacker;
            if (!factionWar.RecordCasualty(battle, battle.Tag(slot), race, now, out int winner)) return;
            roster.finished = true;

            bool captured = winner == battle.attacker;
            factionWarDirty = true;
            FlushFactionWar();
            if (captured) BroadcastFactionWarRpc(factionWar.Snapshot());
            NetNews.Post(NetNews.Kind.War, captured
                ? $"{Standing.RaceName(winner)} captured {NetNews.Place(battle.orbit)}."
                : $"{Standing.RaceName(winner)} repelled the attack at {NetNews.Place(battle.orbit)}.", battle.orbit);
            string notice = captured ? $"{Standing.RaceName(winner)} captured this orbit." :
                $"{Standing.RaceName(winner)} repelled the attack on this orbit.";
            foreach (var pilot in NetPlayer.All)
                if (pilot != null && pilot.IsSpawned && pilot.InSpace && pilot.Station == battle.orbit)
                    Notify(pilot.OwnerClientId, notice);
            ModScripts.Raise("npcFactionBattleResolved", battle.orbit, battle.system, winner, captured);
        }
    }
}
