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
        readonly Dictionary<int, (int id, ulong creator)> factionBattleCreators =
            new Dictionary<int, (int, ulong)>();
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
            catch (IOException e) { Debug.LogError($"NPC faction war could not be loaded: {e.Message}"); }
            factionWar = NpcFactionWar.Campaign.Read(snapshot);
            NpcFactionWar.SetNetworkCampaign(factionWar);
        }

        void FlushFactionWar()
        {
            if (!factionWarDirty || Time.unscaledTime < retryWarSaveAt) return;
            retryWarSaveAt = Time.unscaledTime + 10f;
            if (WarPath == null || NetProfiles.Write(WarPath, factionWar.Snapshot())) factionWarDirty = false;
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
            factionBattleCreators[orbit] = (battle.id, client);
            factionWarDirty = true;
            FlushFactionWar();
            StartFactionBattleRpc(battle.id, battle.orbit, battle.system, battle.defender, battle.attacker, battle.shipsPerSide,
                battle.expires, RpcTarget.Single(client, RpcTargetUse.Temp));
        }

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void StartFactionBattleRpc(int id, int orbit, int system, int defender, int attacker, int ships, long expires,
            RpcParams rpc = default) =>
            NetOrbit.Current?.BeginFactionBattle(new NpcFactionWar.Battle(id, orbit, system, defender, attacker, ships, expires));

        internal void RecordFactionWarCasualty(NetProxy ship)
        {
            if (!IsServer || !ModNpcFactionWar.Enabled || factionWar == null || ship == null ||
                !factionBattleCreators.TryGetValue(ship.Station, out var creator) || creator.creator != ship.Creator) return;
            long now = NpcFactionWar.Now;
            if (!factionWar.TryGetBattle(ship.Station, now, out var battle) || battle.id != creator.id ||
                !factionWar.RecordCasualty(battle, ship.EventTag, ship.Race, now, out int winner)) return;

            factionBattleCreators.Remove(battle.orbit);
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
