// NetProxyStatus.cs
// What changes about an orbit object while it flies (NetProxy's one owner-written status value instead of a dozen
// NetworkVariables: a delta carries one change flag per variable, ~35 bytes of flags for the old NetProxy): race,
// standing toward the owner's player, hull, hit radius, hidden / always hostile / EMP-disabled / asleep / inactive /
// turned, its life, cloak, the cargo another player may steal, its mission role, who destroyed it, its route's waypoint
// and the EMP recovery left. A handover rebuilds the ship from it (NetOrbit.AdoptHandedOver).

using System;
using Unity.Netcode;

namespace GoF2Remake.Multiplayer
{
    public struct NetProxyStatus : INetworkSerializable, IEquatable<NetProxyStatus>
    {
        public const byte Hidden = 1, AlwaysHostile = 2, EmpDisabled = 4, Asleep = 8, Inactive = 16, Turned = 32;

        public short race;
        public byte relation;      // NetProxy.Hostile / Friend / Neutral toward the owner's player
        public ushort hull;        // 0..65535 = the hull fraction
        public float radius;
        public byte flags;
        public byte life;          // NetProxy.Flying / Dying / Dead
        public byte cloak;         // 0..100 %
        public short stealItem;    // -1 = none
        public int stealAmount;
        public int roleFlags;
        public ulong killer;
        public byte routeIndex;
        public ushort empMs;       // the EMP recovery left (x 10 ms)

        public bool Has(byte flag) => (flags & flag) != 0;
        public float HullFraction => hull / 65535f;
        public static ushort Hull(float fraction) => (ushort)UnityEngine.Mathf.Clamp(UnityEngine.Mathf.CeilToInt(fraction * 65535f), 0, 65535);

        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref race);
            s.SerializeValue(ref relation);
            s.SerializeValue(ref hull);
            s.SerializeValue(ref radius);
            s.SerializeValue(ref flags);
            s.SerializeValue(ref life);
            s.SerializeValue(ref cloak);
            s.SerializeValue(ref stealItem);
            s.SerializeValue(ref stealAmount);
            s.SerializeValue(ref roleFlags);
            s.SerializeValue(ref killer);
            s.SerializeValue(ref routeIndex);
            s.SerializeValue(ref empMs);
        }

        public bool Equals(NetProxyStatus o) => race == o.race && relation == o.relation && hull == o.hull && radius == o.radius
            && flags == o.flags && life == o.life && cloak == o.cloak && stealItem == o.stealItem && stealAmount == o.stealAmount
            && roleFlags == o.roleFlags && killer == o.killer && routeIndex == o.routeIndex && empMs == o.empMs;

        public override bool Equals(object obj) => obj is NetProxyStatus o && Equals(o);
        public override int GetHashCode() => race ^ hull << 8 ^ flags << 24 ^ life << 28;
    }
}
