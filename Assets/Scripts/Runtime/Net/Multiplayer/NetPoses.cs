// NetPoses.cs
// The pose stream (docs/notes/multiplayer-sync.md "Bandwidth"): where the ships and crates of each orbit are, instead of
// Netcode's per-object NetworkVariables at the 30 Hz tick (about 60 bytes an object, reliable, to every player of the
// session). Each game sends what it owns (its player's ship with its look, its NetProxy ships and junk, its NetCrates) in
// small unreliable batches (Data.PoseBatch: ~15 bytes an NPC) to the server, each object as often as PoseSchedule says:
// 10 Hz near another player, less far off, every 2 s when it hasn't moved, a player's own ship 20 Hz. The server checks
// the batch (every entry an object the sender owns, in the sender's orbit), keeps the poses (NetPlayer.Position, a handover,
// a player arriving: INetPosed.OnSynchronize) and passes the bytes on to the other players in that orbit only. A newcomer
// in the orbit gets everything at once (the senders see them arrive). Receivers drop a batch older than the last one from
// that sender and smooth each object on the sender's clock (NetSmoothing).

using System.Collections.Generic;
using GoF2Remake.Data;
using Unity.Netcode;
using UnityEngine;

namespace GoF2Remake.Multiplayer
{
    /// <summary>A network object in the pose stream (NetPlayer, NetProxy, NetCrate).</summary>
    public interface INetPosed
    {
        NetworkObject NetworkObject { get; }
        /// <summary>The orbit it is in (a player's: where they fly).</summary>
        int PoseStation { get; }
        /// <summary>Owner: its pose now (Unity), false = nothing to send (no ship yet, docked, an orphan wreck).</summary>
        bool CurrentPose(out Vector3 position, out Quaternion rotation, out bool hasLook, out PoseLook look);
        /// <summary>A pose from the stream: 'gap' = the sender's seconds since the previous one (0 = unknown); 'show' false on
        /// the server's own copy of the value (no smoothing).</summary>
        void ReceivePose(in PoseEntry entry, float gap, bool show);
        /// <summary>The owner's send bookkeeping.</summary>
        NetPoses.SendState PoseSend { get; }
    }

    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class NetPoses
    {
        public sealed class SendState
        {
            public Vector3 position;
            public Quaternion rotation = Quaternion.identity;
            public PoseLook look;
            public float sentAt = -100f;
            public bool sent;
            /// <summary>Receiving: the sender's clock of the last pose (ms, 16 bits), -1 = none.</summary>
            public int lastClock = -1;
        }

        static readonly HashSet<INetPosed> objects = new HashSet<INetPosed>();
        static readonly List<PoseEntry> entries = new List<PoseEntry>();
        static readonly List<PoseEntry> decoded = new List<PoseEntry>();
        static readonly HashSet<ulong> othersHere = new HashSet<ulong>(), othersBefore = new HashSet<ulong>();
        static readonly List<Vector3> otherPositions = new List<Vector3>();
        static readonly Dictionary<ulong, int> lastSequence = new Dictionary<ulong, int>();
        static int sequence;
        static float nextSend;
        /// <summary>The send clock: everything due goes in one batch at most this often (each packet costs ~40 bytes of
        /// transport header: unaligned, a player's 20 Hz and the NPCs' 10 Hz made ~45 packets a second).</summary>
        const float SendSeconds = 0.05f;
        /// <summary>An interval counts as up a little early (the send clock's ticks fall between frames).</summary>
        const float Slack = 0.02f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            objects.Clear();
            lastSequence.Clear();
            othersBefore.Clear();
            sequence = 0;
            nextSend = 0f;
        }

        public static void Register(INetPosed o) { if (o != null) objects.Add(o); }
        public static void Unregister(INetPosed o) { if (o != null) objects.Remove(o); }

        static int Clock => (int)(Time.unscaledTime * 1000f) & 0xffff;

        // ---- sending (every game, from its NetPlayer's Update) ---------------------------------------------------

        /// <summary>This game's owned objects whose pose is due, in batches to the server.</summary>
        public static void Tick(NetPlayer local)
        {
            if (local == null || !local.IsSpawned || !local.InSpace) { othersBefore.Clear(); return; }
            int station = local.Station;
            othersHere.Clear();
            otherPositions.Clear();
            unplaced = false;
            foreach (var p in NetPlayer.All)
                if (p != null && p != local && p.IsSpawned && p.InSpace && p.Station == station)
                {
                    othersHere.Add(p.OwnerClientId);
                    if (p.HasPose) otherPositions.Add(p.Position);
                    else unplaced = true;   // just arrived: as if near
                }
            // Somebody new in the orbit: everything goes now (they have no pose of anything here yet).
            bool newcomer = false;
            foreach (ulong id in othersHere) if (!othersBefore.Contains(id)) { newcomer = true; break; }
            othersBefore.Clear();
            othersBefore.UnionWith(othersHere);

            float now = Time.unscaledTime;
            if (!newcomer && now < nextSend) return;
            nextSend = Mathf.Max(nextSend + SendSeconds, now);
            entries.Clear();
            foreach (var o in objects)
            {
                var obj = o.NetworkObject;
                if (obj == null || !obj.IsSpawned || !obj.IsOwner || o.PoseStation != station) continue;
                if (!o.CurrentPose(out var pos, out var rot, out bool hasLook, out var look)) continue;
                var s = o.PoseSend;
                bool changed = !s.sent || PoseSchedule.Changed(s.position, s.rotation, pos, rot);
                bool lookChanged = hasLook && (!s.sent || !look.Equals(s.look));
                bool player = o is NetPlayer;
                float interval = PoseSchedule.Interval(player, changed || lookChanged, Nearest(pos));
                // A look change (a hit, the boost, the cloak) goes at once, at most at the near rate.
                if (lookChanged) interval = Mathf.Min(interval, 0.05f);
                if (!newcomer && now - s.sentAt < interval - Slack) continue;
                s.position = pos;
                s.rotation = rot;
                s.look = look;
                s.sentAt = now;
                s.sent = true;
                entries.Add(new PoseEntry { id = obj.NetworkObjectId, position = pos, rotation = rot, hasLook = hasLook, look = look });
            }
            if (entries.Count == 0) return;
            sequence = (sequence + 1) & 0xffff;
            foreach (var batch in PoseBatch.Encode(sequence, Clock, entries)) local.SendPoses(batch);
        }

        static bool unplaced;

        static float Nearest(Vector3 pos)
        {
            if (unplaced) return 0f;
            float best = float.PositiveInfinity;
            foreach (var p in otherPositions)
            {
                float d = (p - pos).sqrMagnitude;
                if (d < best) best = d;
            }
            return float.IsInfinity(best) ? best : Mathf.Sqrt(best);
        }

        // ---- the server ----------------------------------------------------------------------------------------

        /// <summary>Server: a batch from 'sender' (flying in 'station'): every entry an object they own in that orbit and a
        /// sane pose, else the whole batch is dropped. Kept here (the server's copies), then the players in that orbit get it.
        /// False = refused.</summary>
        public static bool ServerReceive(ulong sender, int station, byte[] data, List<ulong> recipients)
        {
            recipients.Clear();
            var manager = NetworkManager.Singleton;
            if (manager == null || data == null || data.Length > PoseBatch.MaxBytes + 64) return false;
            decoded.Clear();
            if (!PoseBatch.Decode(data, out _, out int clock, decoded) || decoded.Count == 0) return false;
            foreach (var e in decoded)
            {
                if (!manager.SpawnManager.SpawnedObjects.TryGetValue(e.id, out var obj) || obj == null || obj.OwnerClientId != sender) return false;
                var o = obj.GetComponent<INetPosed>();
                if (o == null || o.PoseStation != station || !NetGuard.Position(e.position)) return false;
            }
            foreach (var e in decoded)
            {
                var o = manager.SpawnManager.SpawnedObjects[e.id].GetComponent<INetPosed>();
                o.ReceivePose(e, 0f, false);
            }
            foreach (var p in NetPlayer.All)
                if (p != null && p.IsSpawned && p.OwnerClientId != sender && p.InSpace && p.Station == station) recipients.Add(p.OwnerClientId);
            return true;
        }

        // ---- receiving (every game in that orbit) ------------------------------------------------------------------

        /// <summary>A batch passed on by the server: the newest per sender only (they come unreliably, maybe out of order).</summary>
        public static void ClientReceive(ulong sender, byte[] data)
        {
            var manager = NetworkManager.Singleton;
            if (manager == null || data == null) return;
            decoded.Clear();
            bool ok = PoseBatch.Decode(data, out int seq, out int clock, decoded);
            if (!ok) return;
            // Batches of one send share a sequence number (a big orbit's list is split): equal is fine, older is dropped.
            if (lastSequence.TryGetValue(sender, out int last) && PoseBatch.Newer(last, seq)) return;
            lastSequence[sender] = seq;
            foreach (var e in decoded)
            {
                if (!manager.SpawnManager.SpawnedObjects.TryGetValue(e.id, out var obj) || obj == null || obj.IsOwner) continue;
                var o = obj.GetComponent<INetPosed>();
                if (o == null) continue;
                var s = o.PoseSend;
                float gap = s.lastClock >= 0 ? PoseBatch.ClockGap(clock, s.lastClock) : 0f;
                s.lastClock = clock;
                o.ReceivePose(e, gap, true);
            }
        }

        /// <summary>A sender left the session: its sequence starts over if it comes back.</summary>
        public static void Forget(ulong sender) => lastSequence.Remove(sender);
    }
}
