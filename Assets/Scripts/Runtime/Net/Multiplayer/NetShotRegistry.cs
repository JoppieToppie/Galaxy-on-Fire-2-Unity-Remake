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
            public bool canceled, interceptable, gateHitConsumed, stationHitConsumed, orbitalHitConsumed;
        }
        static readonly Dictionary<(ulong source, uint id), Shot> shots = new Dictionary<(ulong, uint), Shot>();
        static readonly List<(ulong source, uint id)> expired = new List<(ulong, uint)>();
        static float nextPruneAt;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() { shots.Clear(); expired.Clear(); nextPruneAt = 0f; }
        public static void Register(ulong source, uint id, int item, ulong target, float lifetimeMs,
            Vector3 origin = default, Vector3 velocity = default)
        {
            if (id == 0) return;
            var weapon = (NetGame.Db ?? Database.Shared)?.Item(item);
            bool interceptable = weapon != null && (weapon.Attr(105) > 0 ||
                (Gun.Kind)weapon.categoryId is Gun.Kind.Rocket or Gun.Kind.Missile or Gun.Kind.Nuke);
            Prune();
            // A duplicate fire report must not refresh a live shot or clear its consumed flags.
            if (shots.TryGetValue((source, id), out var existing) && Time.time < existing.expiresAt) return;
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
            Vector3 gateCentre, float gateRadius, bool station = false) =>
            TryConsumeStructureContact(source, id, item, impact, impact, gateCentre, gateRadius, station);

        /// <summary>The detonation position must be within the blast radius of the claimed
        /// gate mesh contact; the shot registry independently verifies its flight to the detonation.</summary>
        public static bool TryConsumeGateBlast(ulong source, uint id, int item, Vector3 detonation,
            Vector3 gateSurface, Vector3 gateCentre, float gateRadius, float blastRadius, bool station = false)
        {
            if (!NetGuard.Finite(blastRadius) || blastRadius <= 0f || blastRadius > 20000f ||
                Vector3.Distance(detonation, gateSurface) > blastRadius + 3f) return false;
            return TryConsumeStructureContact(source, id, item, detonation, gateSurface, gateCentre, gateRadius, station);
        }

        internal static bool TryConsumeOrbitalStructureHit(ulong source, uint id, int item, Vector3 point,
            Vector3 surface, Vector3 centre, float radius, float blastRadius)
        {
            if (!NetGuard.Finite(blastRadius) || blastRadius < 0f || blastRadius > 20000f ||
                Vector3.Distance(point, surface) > blastRadius + 3f) return false;
            return TryConsumeStructureContact(source, id, item, point, surface, centre, radius, false, orbital: true);
        }

        static bool TryConsumeStructureContact(ulong source, uint id, int item, Vector3 shotPoint,
            Vector3 gateSurface, Vector3 gateCentre, float gateRadius, bool station, bool orbital = false)
        {
            if (id == 0 || !NetGuard.Position(shotPoint) || !NetGuard.Position(gateSurface) ||
                !NetGuard.Position(gateCentre) || !NetGuard.Finite(gateRadius) ||
                gateRadius <= 0f) return false;
            Prune();
            if (!shots.TryGetValue((source, id), out var shot) || shot.item != item ||
                shot.canceled || (orbital ? shot.orbitalHitConsumed : station ? shot.stationHitConsumed : shot.gateHitConsumed) || Time.time >= shot.expiresAt ||
                shot.velocity.sqrMagnitude < 1e-6f) return false;
            if ((gateSurface - gateCentre).magnitude > gateRadius + 5f) return false;
            Vector3 travel = shotPoint - shot.origin;
            float along = Vector3.Dot(travel, shot.velocity.normalized);
            float elapsed = Mathf.Max(0f, Time.time - shot.startedAt);
            // Allow network/physics delay and collision radii, but not a hit behind its muzzle or
            // arbitrarily far from its recorded shot path. Beams encode their direction as unit velocity.
            float reach = shot.velocity.magnitude > 2f
                ? shot.velocity.magnitude * (elapsed + 0.75f) + 30f
                : 3000f;
            if (along < -10f || along > reach ||
                Vector3.Distance(shotPoint, shot.origin + shot.velocity.normalized * along) > 35f) return false;
            if (orbital) shot.orbitalHitConsumed = true;
            else if (station) shot.stationHitConsumed = true;
            else shot.gateHitConsumed = true;
            shots[(source, id)] = shot;
            return true;
        }

        public static bool TryCancel(ulong source, uint id, ulong defender)
        {
            Prune();
            if (!shots.TryGetValue((source, id), out var shot) || shot.canceled || !shot.interceptable ||
                shot.target != defender || Time.time >= shot.expiresAt) return false;
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
            // Every untargeted player/NPC shot is tracked for gate combat. Prune at most
            // twice per second instead of scanning the whole dictionary on every shot.
            if (Time.time < nextPruneAt) return;
            nextPruneAt = Time.time + 0.5f;
            expired.Clear();
            foreach (var pair in shots) if (Time.time >= pair.Value.expiresAt) expired.Add(pair.Key);
            foreach (var key in expired) shots.Remove(key);
        }
    }
}
