// NetShotRegistry.cs
// Server-side identity for in-flight shots. Defense requests can only cancel a shot routed to that defender;
// later damage reports for a canceled shot are discarded.
using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using UnityEngine;

namespace GoF2Remake.Multiplayer
{
    public static class NetShotRegistry
    {
        struct Shot
        {
            public ulong target;
            public int item;
            public Vector3 origin, velocity;
            public float startedAt, expiresAt;
            public bool canceled, interceptable, gateHitConsumed;
        }
        static readonly Dictionary<(ulong source, uint id), Shot> shots = new Dictionary<(ulong, uint), Shot>();
        static readonly List<(ulong source, uint id)> expired = new List<(ulong, uint)>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() { shots.Clear(); expired.Clear(); }
        public static void Register(ulong source, uint id, int item, ulong target, float lifetimeMs,
            Vector3 origin = default, Vector3 velocity = default)
        {
            if (id == 0) return;
            var weapon = (NetGame.Db ?? Database.Shared)?.Item(item);
            bool interceptable = weapon != null && (weapon.Attr(105) > 0 ||
                (Gun.Kind)weapon.categoryId is Gun.Kind.Rocket or Gun.Kind.Missile or Gun.Kind.Nuke);
            Prune();
            shots[(source, id)] = new Shot
            {
                target = target, item = item, origin = origin, velocity = velocity,
                startedAt = Time.time, expiresAt = Time.time + Mathf.Max(0f, lifetimeMs) / 1000f,
                interceptable = interceptable
            };
        }
        /// <summary>Consume one recorded shot on a gate. The server checks item, lifetime, position
        /// and rough trajectory; the firing client's exact HullCollision hit supplies mesh contact.
        /// A consumed shot cannot deal repeated structural or EMP hits.</summary>
        public static bool TryConsumeGateHit(ulong source, uint id, int item, Vector3 impact,
            Vector3 gateCentre, float gateRadius)
        {
            if (id == 0 || !NetGuard.Position(impact) || !NetGuard.Position(gateCentre) ||
                !NetGuard.Finite(gateRadius) || gateRadius <= 0f) return false;
            Prune();
            if (!shots.TryGetValue((source, id), out var shot) || shot.item != item ||
                shot.canceled || shot.gateHitConsumed) return false;
            if ((impact - gateCentre).magnitude > gateRadius + 5f) return false;
            Vector3 travel = impact - shot.origin;
            float along = Vector3.Dot(travel, shot.velocity.normalized);
            float elapsed = Mathf.Max(0f, Time.time - shot.startedAt);
            // Allow network/physics delay and collision radii, but not a hit behind its muzzle or
            // arbitrarily far from its recorded shot path. Beams encode their direction as unit velocity.
            float reach = shot.velocity.magnitude > 2f
                ? shot.velocity.magnitude * (elapsed + 0.75f) + 30f
                : 3000f;
            if (along < -10f || along > reach ||
                Vector3.Distance(impact, shot.origin + shot.velocity.normalized * along) > 35f) return false;
            shot.gateHitConsumed = true;
            shots[(source, id)] = shot;
            return true;
        }

        public static bool TryCancel(ulong source, uint id, ulong defender)
        {
            Prune();
            if (!shots.TryGetValue((source, id), out var shot) || shot.canceled || !shot.interceptable || shot.target != defender) return false;
            shot.canceled = true;
            shots[(source, id)] = shot;
            return true;
        }
        public static bool IsCanceled(ulong source, uint id)
        {
            Prune();
            return id != 0 && shots.TryGetValue((source, id), out var shot) && shot.canceled;
        }
        static void Prune()
        {
            expired.Clear();
            foreach (var pair in shots) if (Time.time >= pair.Value.expiresAt) expired.Add(pair.Key);
            foreach (var key in expired) shots.Remove(key);
        }
    }
}
