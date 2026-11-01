// NetStateCoop.cs
// NetState: the co-op campaign's messages (NetCoop, CoopCampaign; docs/notes/multiplayer-coop.md; part of the partial
// NetState, its fields are in NetState.cs). The leader is the host, so the leader's word goes out from the server: its
// story (the coopStory variable), a won step, a failed level, its story level's state, route, radio and cutscene camera
// (to that orbit's players only) and the moves its level makes. The guests' reports come in: a step their game found
// won, their own advance, their progress on the step's counters, a freelance mission done, their story conversation
// open or closed. Every report is checked against the leader's own step (CoopCampaign) and rate limited.

using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.World;
using Unity.Netcode;
using UnityEngine;

namespace GoF2Remake.Multiplayer
{
    public sealed partial class NetState
    {
        // ---- reports to the leader ----------------------------------------------------------------------------

        /// <summary>A guest's game found the current step won.</summary>
        [Rpc(SendTo.Server)]
        public void CoopWonRpc(int index, RpcParams rpc = default)
        {
            ulong client = rpc.Receive.SenderClientId;
            if (!Coop || !NetRateLimit.Allow(client, NetRateLimit.Kind.Request)) return;
            CoopWon(index);
        }

        /// <summary>Server: step 'index' won (by the leader or a guest): the whole crew opens its success conversation.</summary>
        internal void CoopWon(int index)
        {
            if (!IsServer || !Coop || index != Story.Index) return;
            CoopStepWonRpc(index);
        }

        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
        void CoopStepWonRpc(int index) => CoopCampaign.OnStepWon(index);

        /// <summary>A guest's game advanced from 'from' by itself (not a won step): the leader follows.</summary>
        [Rpc(SendTo.Server)]
        public void CoopAdvancedRpc(int from, RpcParams rpc = default)
        {
            ulong client = rpc.Receive.SenderClientId;
            if (!Coop || !NetRateLimit.Allow(client, NetRateLimit.Kind.Request)) return;
            if (CoopCampaign.FollowGuestAdvance(from, NetGame.Db)) Debug.Log($"NetCoop: the leader follows {NetSquad.Find(client)?.DisplayName}'s advance from step {from}");
        }

        /// <summary>A guest's progress on the step's shared counters (CoopCampaign.Progress).</summary>
        [Rpc(SendTo.Server)]
        public void CoopProgressRpc(string json, RpcParams rpc = default)
        {
            ulong client = rpc.Receive.SenderClientId;
            if (!Coop || !NetRateLimit.Allow(client, NetRateLimit.Kind.Request) || json == null || json.Length > 2000) return;
            CoopCampaign.Merge(json);
        }

        /// <summary>A guest finished a freelance mission (its id; 0 = a mission of its own).</summary>
        [Rpc(SendTo.Server)]
        public void CoopFreelanceRpc(long missionId, RpcParams rpc = default)
        {
            ulong client = rpc.Receive.SenderClientId;
            if (!Coop || !NetRateLimit.Allow(client, NetRateLimit.Kind.Request)) return;
            CoopCampaign.CountCrewMission(missionId);
        }

        /// <summary>A crew member's story conversation opened / closed (the leader's level holds while one is open in its orbit).</summary>
        [Rpc(SendTo.Server)]
        public void CoopDialogRpc(bool open, RpcParams rpc = default) => SetCoopDialog(rpc.Receive.SenderClientId, open);

        internal void SetCoopDialog(ulong client, bool open)
        {
            if (!IsServer || !Coop) return;
            if (open) coopDialogs[client] = Time.unscaledTime; else coopDialogs.Remove(client);
        }

        /// <summary>A conversation open longer than this no longer holds the level (a game that never said it closed).</summary>
        const float CoopDialogHoldSeconds = 180f;

        /// <summary>Server: a crew member flying in 'station' has a story conversation open.</summary>
        internal bool CoopHoldIn(int station)
        {
            if (!Coop || coopDialogs.Count == 0) return false;
            foreach (var pair in coopDialogs)
            {
                var p = NetSquad.Find(pair.Key);
                if (p != null && p.InSpace && p.Station == station && Time.unscaledTime - pair.Value < CoopDialogHoldSeconds) return true;
            }
            return false;
        }

        // ---- the leader's word -----------------------------------------------------------------------------

        /// <summary>Server (the leader): its story level in 'station' failed.</summary>
        internal void CoopLevelFailed(int station)
        {
            if (IsServer && Coop) CoopFailedRpc(station);
        }

        [Rpc(SendTo.NotServer, InvokePermission = RpcInvokePermission.Server)]
        void CoopFailedRpc(int station) => NetCoop.OnLevelFailed(station);

        /// <summary>Server (the leader): its story level's state, to the crew in that orbit.</summary>
        internal void SendCoopLevel(CoopLevelState s)
        {
            if (!IsServer || !Coop || !NetInterest.OrbitGroup(this, s.station, NetworkManager.ServerClientId, out var target)) return;
            byte flags = (byte)((s.cutscene ? 1 : 0) | (s.invulnerable ? 2 : 0) | (s.hold ? 4 : 0));
            CoopLevelRpc(s.station, s.index, flags, s.missionMs, s.timeLimitMs, s.routeIndex, s.fadeAlpha, s.fadeColor, target);
        }

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server, Delivery = RpcDelivery.Unreliable)]
        void CoopLevelRpc(int station, int index, byte flags, float missionMs, float timeLimitMs, int routeIndex, float fadeAlpha, Color32 fadeColor, RpcParams rpc = default)
        {
            CampaignLevel.Current?.OnLeaderState(new CoopLevelState
            {
                station = station, index = index, cutscene = (flags & 1) != 0, invulnerable = (flags & 2) != 0, hold = (flags & 4) != 0,
                missionMs = missionMs, timeLimitMs = timeLimitMs, routeIndex = routeIndex, fadeAlpha = fadeAlpha, fadeColor = fadeColor,
            });
        }

        /// <summary>Server (the leader): its story level's route, to the crew in that orbit.</summary>
        internal void SendCoopRoute(int station, List<Vector3> points)
        {
            if (!IsServer || !Coop || !NetInterest.OrbitGroup(this, station, NetworkManager.ServerClientId, out var target)) return;
            CoopRouteRpc(station, points != null ? points.ToArray() : new Vector3[0], target);
        }

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void CoopRouteRpc(int station, Vector3[] points, RpcParams rpc = default) =>
            CampaignLevel.Current?.OnLeaderRoute(station, new List<Vector3>(points ?? new Vector3[0]));

        /// <summary>Server (the leader): its story level showed a radio line.</summary>
        internal void SendCoopRadio(int station, int index, int line)
        {
            if (!IsServer || !Coop || !NetInterest.OrbitGroup(this, station, NetworkManager.ServerClientId, out var target)) return;
            CoopRadioRpc(station, index, line, target);
        }

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void CoopRadioRpc(int station, int index, int line, RpcParams rpc = default) => CampaignLevel.Current?.OnLeaderRadio(station, index, line);

        /// <summary>Server (the leader): its cutscene camera.</summary>
        internal void SendCoopCamera(int station, Vector3 position, Quaternion rotation, float fov)
        {
            if (!IsServer || !Coop || !NetInterest.OrbitGroup(this, station, NetworkManager.ServerClientId, out var target)) return;
            CoopCameraRpc(station, position, rotation, fov, target);
        }

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server, Delivery = RpcDelivery.Unreliable)]
        void CoopCameraRpc(int station, Vector3 position, Quaternion rotation, float fov, RpcParams rpc = default) =>
            CampaignLevel.Current?.OnLeaderCamera(station, position, rotation, fov);

        /// <summary>Server (the leader): its story level moved it from 'from' (a ride, a jump, docked by the story).</summary>
        internal void SendCoopTravel(int from, int to, bool dock, bool streamOut)
        {
            if (!IsServer || !Coop || !NetInterest.OrbitGroup(this, from, NetworkManager.ServerClientId, out var target)) return;
            CoopTravelRpc(from, to, dock, streamOut, Story.Index, target);
        }

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void CoopTravelRpc(int from, int to, bool dock, bool streamOut, int leaderIndex, RpcParams rpc = default) =>
            NetCoop.OnLeaderTravel(from, to, dock, streamOut, leaderIndex);
    }
}
