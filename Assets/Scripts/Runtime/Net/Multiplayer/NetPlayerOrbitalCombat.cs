using GoF2Remake.Flight;
using Unity.Netcode;
using UnityEngine;

namespace GoF2Remake.Multiplayer
{
    public sealed partial class NetPlayer
    {
        public void ReportOrbitalStructureContact(int id, int item, uint shotId, Vector3 point, Vector3 surface, bool blast)
        {
            if (IsOwner && IsSpawned && InSpace && shotId != 0 && NetGuard.Position(point) && NetGuard.Position(surface))
                OrbitalStructureContactRpc(id, item, shotId, point, surface, blast);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void OrbitalStructureContactRpc(int id, int item, uint shotId, Vector3 point, Vector3 surface, bool blast, RpcParams rpc = default)
        {
            if (!IsServer || rpc.Receive.SenderClientId != OwnerClientId || !InSpace ||
                !NetRateLimit.Allow(OwnerClientId, NetRateLimit.Kind.Hit) ||
                !NetGuard.Item(item) || !HasWeaponItem(item) || NetPlayerStations.StructuralWeaponEligible?.Invoke(item) != true)
                return;
            var weapon = NetGame.Db.Item(item);
            if (weapon == null) return;
            var structure = id > 0 ? NetPlayerStations.FindOrbitalStructure(id) : null;
            bool module = NetPlayerStations.StructuralModuleContactKind(id) != null;
            Vector3 centre;
            float radius;
            if (module)
            {
                if (!NetPlayerStations.TryGetStructuralModuleContact(Station, id, out centre, out radius)) return;
            }
            else
            {
                if (structure == null || !structure.operational || structure.orbit != Station) return;
                centre = NetPlayerStations.OrbitalStructurePosition(structure);
                radius = structure.hitRadius;
            }
            float damage = Mathf.Max(0f, weapon.Attr(9));
            if (damage <= 0) return;
            float blastRadius = 0;
            if (blast)
            {
                var kind = (Gun.Kind)weapon.categoryId;
                if (kind is not (Gun.Kind.Nuke or Gun.Kind.Ionizing or Gun.Kind.Mine or Gun.Kind.ShockBlast)) return;
                blastRadius = Mathf.Max(0, weapon.Attr(14)) * World.OrbitLayout.MetersPerUnit;
                if (blastRadius <= 0) return;
            }
            if (!NetShotRegistry.TryConsumeOrbitalStructureHit(NetworkObjectId, shotId, item, point, surface,
                centre, radius, blastRadius, module ? id : 0)) return;
            if (blast) damage *= Mathf.Clamp01(1f - Vector3.Distance(point, surface) / blastRadius);
            if (module) NetPlayerStations.DamageStructuralModule(this, Station, id, damage);
            else NetPlayerStations.DamageOrbitalStructure(this, structure, damage);
        }

        internal void ApplyOrbitalDefenseDamage(int orbit, float amount, Vector3 centre)
        {
            if (IsServer && InSpace && Station == orbit && NetGuard.Damage(amount))
                OrbitalDefenseHitRpc(orbit, amount, centre);
        }

        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        void OrbitalDefenseHitRpc(int orbit, float amount, Vector3 centre)
        {
            var own = LocalTarget;
            if (IsOwner && InSpace && Station == orbit && own != null)
                own.Damage(amount, true, own.transform.position - centre);
        }

        internal void FireOrbitalDefenseAtNpc(PlayerOrbitalStructure structure, int race, Vector3 centre)
        {
            if (IsServer && RunsOrbitActive && RunsOrbit == structure.orbit)
                OrbitalDefenseNpcRpc(structure.orbit, race, centre, structure.range, structure.damage);
        }

        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        void OrbitalDefenseNpcRpc(int orbit, int race, Vector3 centre, float range, float damage)
        {
            if (!IsOwner || !RunsOrbitActive || RunsOrbit != orbit || level?.Layout?.stationIndex != orbit ||
                level.Traffic == null) return;
            Target target = null;
            float closest = range * range;
            foreach (var npc in level.Traffic.Ships)
            {
                if (npc == null || npc.Spec == null || npc.Target?.Targetable != true ||
                    npc.Spec.alwaysFriend || npc.Spec.alwaysNeutral ||
                    (!npc.Spec.alwaysEnemy && !Standing.RacesHostile(race, npc.Race))) continue;
                float distance = (npc.Target.transform.position - centre).sqrMagnitude;
                if (distance > closest) continue;
                closest = distance;
                target = npc.Target;
            }
            if (target != null) target.Damage(damage, true, target.transform.position - centre);
        }
    }
}
