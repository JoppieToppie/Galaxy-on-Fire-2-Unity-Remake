using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.Visuals;
using GoF2Remake.World;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace GoF2Remake.Multiplayer
{
    public sealed partial class NetPlayer
    {
        readonly NetworkVariable<int> managedStationOrbit = new NetworkVariable<int>(-1,
            NetworkVariableReadPermission.Owner, NetworkVariableWritePermission.Server);
        readonly NetworkVariable<FixedString4096Bytes> managedJumpgates = new NetworkVariable<FixedString4096Bytes>(
            default, NetworkVariableReadPermission.Owner, NetworkVariableWritePermission.Server);
        internal int ManagedStationOrbit => managedStationOrbit.Value;
        // The public gate record deliberately omits private ownership and blacklist data.
        // Replicate only the server's access decision for the pilot's current orbit.
        readonly NetworkVariable<int> usablePlayerJumpgate = new NetworkVariable<int>(-1,
            NetworkVariableReadPermission.Owner, NetworkVariableWritePermission.Server);
        internal bool HasPlayerJumpgateAccess(int orbit) => IsOwner && orbit >= 0 && usablePlayerJumpgate.Value == orbit;
        internal void SetPlayerJumpgateAccess(int orbit)
        {
            if (IsServer && usablePlayerJumpgate.Value != orbit) usablePlayerJumpgate.Value = orbit;
        }

        internal bool CanViewManagedJumpgate(int orbit)
        {
            if (!IsOwner) return false;
            foreach (var entry in managedJumpgates.Value.ToString().Split(','))
                if (int.TryParse(entry, out int allowed) && allowed == orbit) return true;
            return false;
        }

        internal void SetManagedJumpgates(string orbits)
        {
            if (IsServer && managedJumpgates.Value.ToString() != orbits)
                managedJumpgates.Value = new FixedString4096Bytes(orbits);
        }
        internal void SetManagedStationOrbit(int orbit)
        {
            if (IsServer && managedStationOrbit.Value != orbit) managedStationOrbit.Value = orbit;
        }

        public void ReportPlayerStationContact(int orbit, int item, uint shotId, Vector3 point, Vector3 surface, bool blast)
        {
            if (IsOwner && IsSpawned && InSpace && Station == orbit && shotId != 0 &&
                NetGuard.Position(point) && NetGuard.Position(surface))
                PlayerStationContactRpc(orbit, item, shotId, point, surface, blast);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void PlayerStationContactRpc(int orbit, int item, uint shotId, Vector3 point, Vector3 surface, bool blast, RpcParams rpc = default)
        {
            if (!IsServer || rpc.Receive.SenderClientId != OwnerClientId || !InSpace || Station != orbit ||
                !NetRateLimit.Allow(OwnerClientId, NetRateLimit.Kind.Hit) || shotId == 0 ||
                !NetGuard.Item(item) || !HasWeaponItem(item) || !NetGuard.Position(point) ||
                !NetGuard.Position(surface) || !NetPlayerStations.CanDamagePlayerStation(orbit) ||
                NetPlayerStations.StructuralWeaponEligible?.Invoke(item) != true) return;

            var weapon = NetGame.Db.Item(item);
            if (weapon == null) return;
            float damage = Mathf.Max(0, weapon.Attr(9));
            if (damage <= 0) return;
            // The firing client supplies its forced-exact contact. The server checks a
            // registered shot and an independent bound for the station's current model.
            var layout = OrbitLayout.Build(NetGame.Db, orbit);
            if (layout == null || !layout.stationObject) return;
            float radius = layout.stationReach * layout.StationScale * OrbitLayout.MetersPerUnit;
            if (blast)
            {
                var kind = (Gun.Kind)weapon.categoryId;
                if (kind is not (Gun.Kind.Nuke or Gun.Kind.Ionizing or Gun.Kind.Mine or Gun.Kind.ShockBlast)) return;
                float blastRadius = Mathf.Max(0, weapon.Attr(14)) * OrbitLayout.MetersPerUnit;
                if (blastRadius <= 0f ||
                    !NetShotRegistry.TryConsumeGateBlast(NetworkObjectId, shotId, item, point, surface, NetPlayerStations.StationPosition(orbit), radius, blastRadius, station: true))
                    return;
                damage *= Mathf.Clamp01(1f - Vector3.Distance(point, surface) / blastRadius);
            }
            else if (!NetShotRegistry.TryConsumeGateHit(NetworkObjectId, shotId, item, point, NetPlayerStations.StationPosition(orbit), radius, station: true))
                return;
            if (NetPlayerStations.ApplyStationCombatHit(orbit, item, damage))
                NetPlayerStations.NoteOrbitalAggression(this, orbit);
        }

        internal void BroadcastPlayerStationExplosion(int orbit)
        {
            if (IsServer && IsSpawned && RunsOrbitActive && RunsOrbit == orbit)
                PlayerStationExplosionRpc(orbit);
        }

        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
        void PlayerStationExplosionRpc(int orbit)
        {
            if (Local != null && Local.InSpace && Local.Station == orbit && Local.level?.Layout?.stationIndex == orbit)
                Explosion.Spawn(NetPlayerStations.StationPosition(orbit), 15f);
        }

        public void RequestWreckSalvage(int orbit, int room, bool jumpgate)
        {
            if (!IsOwner || !IsSpawned || !InSpace || Station != orbit || room <= 0) return;
            if (!IsServer)
            {
                NetProfileClient.RequestWreckSalvage(orbit, room, jumpgate);
                return;
            }
            if (NetRateLimit.Allow(OwnerClientId, NetRateLimit.Kind.Claim))
                NetPlayerStations.CollectWreckSalvage(this, orbit, room, jumpgate);
        }
    }
}
