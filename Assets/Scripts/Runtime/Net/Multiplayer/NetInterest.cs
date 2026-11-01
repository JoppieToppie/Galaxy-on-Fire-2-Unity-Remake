// NetInterest.cs
// Who gets an orbit's traffic (docs/notes/multiplayer-sync.md "Bandwidth"): only the players flying in that orbit. The
// server shows an orbit's NetProxy / NetCrate objects only to them and to their owner (Netcode's observers: a player
// arriving gets them spawned with their state, one leaving has them despawned; NetState.UpdateInterest), and the shots,
// blasts and pose batches of an orbit go to them alone (OrbitGroup). Before, every player of the session got everything,
// docked or anywhere else.

using System.Collections.Generic;
using Unity.Netcode;

namespace GoF2Remake.Multiplayer
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class NetInterest
    {
        static readonly List<ulong> ids = new List<ulong>();

        /// <summary>Whether 'client' should have an object of 'station' (owned by 'owner'): its owner always, else a player
        /// flying there.</summary>
        public static bool Sees(ulong client, ulong owner, int station)
        {
            if (client == owner) return true;
            var p = NetSquad.Find(client);
            return p != null && p.InSpace && p.Station == station;
        }

        /// <summary>Server: the players in 'station' who have 'sender's object (except 'except'), as an RPC target; false =
        /// nobody (send nothing).</summary>
        public static bool OrbitGroup(NetworkBehaviour sender, int station, ulong except, out BaseRpcTarget target)
        {
            ids.Clear();
            var obj = sender.NetworkObject;
            foreach (var p in NetPlayer.All)
            {
                if (p == null || !p.IsSpawned || p.OwnerClientId == except || !p.InSpace || p.Station != station) continue;
                if (!obj.IsNetworkVisibleTo(p.OwnerClientId)) continue;
                ids.Add(p.OwnerClientId);
            }
            target = ids.Count > 0 ? sender.RpcTarget.Group(ids, RpcTargetUse.Temp) : null;
            return target != null;
        }
    }
}
