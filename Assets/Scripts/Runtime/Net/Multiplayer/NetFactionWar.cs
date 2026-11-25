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
            public ulong creator;
            public float startedAt, endsAt;
            public readonly Dictionary<int, ulong> ships = new Dictionary<int, ulong>();
        }

        readonly Dictionary<int, FleetRoster> fleetByOrbit = new Dictionary<int, FleetRoster>();
        bool factionWarDirty;
        float retryWarSaveAt;

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

        [Rpc(SendTo.Server)]
        internal void RequestFactionBattleRpc(int orbit, RpcParams rpc = default)
        {
            ulong client = rpc.Receive.SenderClientId;
            if (!ModNpcFactionWar.Enabled || !NetRateLimit.Allow(client, NetRateLimit.Kind.Request) ||
                !NetGuard.InOrbit(client, orbit) || RunnerOf(orbit)?.OwnerClientId != client ||
                NetArena.IsArenaOrbit(orbit) || NetPlayerStations.StationClosedByCombat(orbit)) return;
            EnsureFactionWar();
            if (factionWar == null || !factionWar.TryStart(NetGame.Db, orbit, NpcFactionWar.Now, out var battle)) return;
            float started = Time.unscaledTime;
            fleetByOrbit[orbit] = new FleetRoster { battle = battle, creator = client, startedAt = started,
                endsAt = started + ModNpcFactionWar.BattleSeconds };
            factionWarDirty = true;
            FlushFactionWar();
            StartFactionBattleRpc(battle.id, battle.orbit, battle.system, battle.defender, battle.attacker, battle.shipsPerSide,
                battle.expires, RpcTarget.Single(client, RpcTargetUse.Temp));
            ModScripts.Raise("npcFactionBattleStarted", battle.orbit, battle.system, battle.defender, battle.attacker, battle.shipsPerSide);
        }

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void StartFactionBattleRpc(int id, int orbit, int system, int defender, int attacker, int ships, long expires,
            RpcParams rpc = default) =>
            NetOrbit.Current?.BeginFactionBattle(new NpcFactionWar.Battle(id, orbit, system, defender, attacker, ships, expires));

        internal bool IsFactionWarShip(NetProxy ship, int race)
        {
            if (!IsServer || !ModNpcFactionWar.Enabled || !fleetByOrbit.TryGetValue(ship.Station, out var roster) ||
                roster.creator != ship.Creator || ship.SpawnedAt < roster.startedAt || ship.SpawnedAt > roster.endsAt) return false;
            var battle = roster.battle;
            int slot = battle.Slot(ship.EventTag);
            if (slot < 0 || slot >= 2 * battle.shipsPerSide || race != (slot < battle.shipsPerSide ? battle.defender : battle.attacker))
                return false;
            if (roster.ships.TryGetValue(slot, out ulong id)) return id == ship.NetworkObjectId;
            roster.ships.Add(slot, ship.NetworkObjectId);
            return true;
        }

        internal void RecordFactionWarCasualty(NetProxy ship)
        {
            if (factionWar == null || !IsFactionWarShip(ship, ship.Race)) return;
            var battle = fleetByOrbit[ship.Station].battle;
            if (!factionWar.RecordCasualty(battle, ship.EventTag, ship.Race, NpcFactionWar.Now, out int winner)) return;

            bool captured = winner == battle.attacker;
            factionWarDirty = true;
            FlushFactionWar();
            if (captured) BroadcastFactionWarRpc(factionWar.Snapshot());
            string notice = captured ? $"{Standing.RaceName(winner)} captured this orbit." :
                $"{Standing.RaceName(winner)} repelled the attack on this orbit.";
            foreach (var pilot in NetPlayer.All)
                if (pilot != null && pilot.IsSpawned && pilot.InSpace && pilot.Station == battle.orbit)
                    Notify(pilot.OwnerClientId, notice);
            ModScripts.Raise("npcFactionBattleResolved", battle.orbit, battle.system, winner, captured);
        }
    }
}
