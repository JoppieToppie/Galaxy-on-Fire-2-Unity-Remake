// NetShotData.cs
// One shot as NetShotSender sends it and NetShotMirror draws it (docs/notes/multiplayer-sync.md "Bandwidth"): sent once,
// when fired; every other game in the orbit flies its own copy (a homing one toward the same target). Packed: the item
// (16 bits), the start position (Data.WireQuant: 0.05 m steps), the velocity (3 halves), the up vector (octahedral), the
// lifetime and homing delay (16-bit ms) and the target's NetworkObject id (a varint, 0 = none): about 27 bytes, where
// the plain parameters took 56 plus the RPC's.

using GoF2Remake.Data;
using Unity.Netcode;
using UnityEngine;

namespace GoF2Remake.Multiplayer
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public struct NetShotData : INetworkSerializable
    {
        public int item;
        public Vector3 position, velocity, up;
        public float lifetimeMs, homingDelayMs;
        public ulong targetId;

        static readonly WireWriter writer = new WireWriter(64);
        static byte[] buffer = new byte[64];

        public NetShotData(int item, Vector3 position, Vector3 velocity, Vector3 up, float lifetimeMs, float homingDelayMs, ulong targetId)
        {
            this.item = item;
            this.position = position;
            this.velocity = velocity;
            this.up = up;
            this.lifetimeMs = lifetimeMs;
            this.homingDelayMs = homingDelayMs;
            this.targetId = targetId;
        }

        public void Write(WireWriter w)
        {
            w.UShort(Mathf.Clamp(item, 0, 65535));
            WireQuant.WritePosition(w, position);
            // Halves reach 65504: a faster shot (none in the game) keeps its floats.
            bool halves = Mathf.Abs(velocity.x) < 60000f && Mathf.Abs(velocity.y) < 60000f && Mathf.Abs(velocity.z) < 60000f;
            w.Bool(halves);
            if (halves) { w.Half(velocity.x); w.Half(velocity.y); w.Half(velocity.z); }
            else { w.Float(velocity.x); w.Float(velocity.y); w.Float(velocity.z); }
            WireQuant.WriteDirection(w, up);
            // A coasting rocket's timer runs below 0 (NetGuard.Shot: from -5000 ms): offset by 5000.
            w.UShort(Mathf.Clamp(Mathf.RoundToInt(lifetimeMs) + 5000, 0, 65535));
            w.UShort(Mathf.Clamp(Mathf.RoundToInt(homingDelayMs), 0, 65535));
            w.VarULong(targetId == NetShots.NoTarget ? 0 : targetId + 1);
        }

        public static NetShotData Read(WireReader r)
        {
            var d = new NetShotData { item = r.UShort(), position = WireQuant.ReadPosition(r) };
            d.velocity = r.Bool() ? new Vector3(r.Half(), r.Half(), r.Half()) : new Vector3(r.Float(), r.Float(), r.Float());
            d.up = WireQuant.ReadDirection(r);
            d.lifetimeMs = r.UShort() - 5000;
            d.homingDelayMs = r.UShort();
            ulong t = r.VarULong();
            d.targetId = t == 0 ? NetShots.NoTarget : t - 1;
            return d;
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            if (serializer.IsWriter)
            {
                writer.Clear();
                Write(writer);
                var bytes = writer.ToArray();
                var w = serializer.GetFastBufferWriter();
                w.WriteValueSafe((byte)bytes.Length);
                w.WriteBytesSafe(bytes, bytes.Length);
                return;
            }
            var reader = serializer.GetFastBufferReader();
            reader.ReadValueSafe(out byte length);
            if (buffer.Length < length) buffer = new byte[length];
            reader.ReadBytesSafe(ref buffer, length);
            this = Read(new WireReader(buffer, 0, length));
        }
    }
}
