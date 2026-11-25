// NetPlayerPose.cs
// NetPlayer's part of the pose stream (NetPoses, docs/notes/multiplayer-sync.md "Bandwidth"): the ship's pose and look
// (engine glow, boost, cloak, phase, EMP lightning, hull / shield / armor) travel in the stream to the players in the same
// orbit (and the server), not as NetworkVariables to the whole session; this player's game sends its whole batch (its
// orbit's NPCs and crates too) through PoseUpRpc, the server passes it on with PoseRpc. Part of the partial NetPlayer
// (its fields and Unity messages are in NetPlayer.cs).

using System.Collections.Generic;
using GoF2Remake.Data;
using Unity.Netcode;
using UnityEngine;

namespace GoF2Remake.Multiplayer
{
    public sealed partial class NetPlayer
    {
        Vector3 posePosition;
        Quaternion poseRotation = Quaternion.identity;
        bool hasPose;
        PoseLook look = new PoseLook { engine = true, hull = 254, shield = 255, armor = 255 };
        readonly NetPoses.SendState poseSend = new NetPoses.SendState();
        static readonly List<ulong> poseRecipients = new List<ulong>();

        /// <summary>A pose of this ship is known here (the owner's own; elsewhere one from the stream).</summary>
        public bool HasPose => hasPose;

        // A remote pose is usable only in the orbit where the server received it. Stationary
        // ships still send a pose every two seconds, so brief packet loss can be tolerated.
        const double WreckClaimPoseTimeoutSeconds = 5;
        int lastClaimPoseOrbit;
        double lastClaimPoseTime;
        internal bool HasCurrentWreckClaimPose => hasPose && (IsOwner ||
            (lastClaimPoseOrbit == Station && Time.realtimeSinceStartupAsDouble - lastClaimPoseTime <= WreckClaimPoseTimeoutSeconds));
        /// <summary>The boost the others see (0..1).</summary>
        float Boost => look.boost / 255f;

        public NetPoses.SendState PoseSend => poseSend;
        public int PoseStation => Station;

        public bool CurrentPose(out Vector3 position, out Quaternion rotation, out bool withLook, out PoseLook shipLook)
        {
            position = posePosition;
            rotation = poseRotation;
            withLook = true;
            shipLook = look;
            return hasPose && InSpace;
        }

        public void ReceivePose(in PoseEntry entry, float gap, bool show)
        {
            posePosition = entry.position;
            poseRotation = entry.rotation;
            bool fatal = entry.hasLook && entry.look.hull == 0 && look.hull != 0;
            if (entry.hasLook) look = entry.look;
            if (IsServer && HasRecordedDeath && InSpace) look.hull = 0;
            hasPose = true;
            if (IsServer)
            {
                lastClaimPoseOrbit = Station;
                lastClaimPoseTime = Time.realtimeSinceStartupAsDouble;
                if (fatal && InSpace) NetState.Instance?.ObserveFatalHull(this);
            }
            if (show && !IsOwner) smoothing.Push(entry.position, gap);
        }

        // Reliable owner report supplements the unreliable pose stream. This is evidence
        // of the owner's own death only, not an authority to kill other pilots.
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void FatalHullRpc(int orbit, RpcParams rpc = default)
        {
            if (!IsServer || rpc.Receive.SenderClientId != OwnerClientId || !IsSpawned ||
                !InSpace || orbit != Station || !HasCurrentWreckClaimPose || NetArena.InMatch(OwnerClientId)) return;
            if (!NetRateLimit.Allow(OwnerClientId, NetRateLimit.Kind.Kill)) return;
            look.hull = 0;
            NetState.Instance?.ObserveFatalHull(this);
        }

        /// <summary>NetPoses: one batch of this game's poses to the server.</summary>
        internal void SendPoses(byte[] batch) => PoseUpRpc(batch);

        [Rpc(SendTo.Server, Delivery = RpcDelivery.Unreliable, InvokePermission = RpcInvokePermission.Owner)]
        void PoseUpRpc(byte[] batch)
        {
            if (!NetRateLimit.Allow(OwnerClientId, NetRateLimit.Kind.Pose)) return;
            if (!InSpace || !NetPoses.ServerReceive(OwnerClientId, Station, batch, poseRecipients))
            {
                if (InSpace) NetRateLimit.Reject(OwnerClientId, "a pose batch of objects it doesn't own");
                return;
            }
            if (poseRecipients.Count > 0) PoseRpc(batch, RpcTarget.Group(poseRecipients, RpcTargetUse.Temp));
        }

        [Rpc(SendTo.SpecifiedInParams, Delivery = RpcDelivery.Unreliable, InvokePermission = RpcInvokePermission.Server)]
        void PoseRpc(byte[] batch, RpcParams rpc = default) => NetPoses.ClientReceive(OwnerClientId, batch);
    }
}
