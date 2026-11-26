// TurretTargeting.cs
// Which target a turret aims at: the pick rules every turret shares (Reference/research/weapons_special.md 5.2, 6.2;
// npc_combat_specials.md 1). Plain C#; the owner hands in who may be picked (Candidate), how far each is (Measure) and the
// pool. The original has two rule sets, kept apart as two modes:
//   Retarget  PlayerEgo::handleAutoTurret 0xa8ae0 (the player's auto turrets) and PlayerTurret::update (the sentry guns):
//             a new pick every 3000 ms and at once when the target died, went inactive or cloaked; the nearest candidate
//             within range, the one that was out of the turret's reach (PlayerEgo+0x190) skipped and forgotten once nothing
//             else is there; running into a pitch limit drops the target and picks again at once.
//   Timed     PlayerTurret::handleTurret / pickEnemy 0x182e90 (the NPC turrets): a new pick only once more than 3000 ms
//             have passed (the first after 3 s); the nearest candidate within range, the unreachable one only when nothing
//             else is nearer; running into a limit keeps the target until the next pick, which then prefers another.
// The aim point: the target's position + its heading * 1500 units (the lead both use). Remake (#354, Settings.TurretLeadAim,
// off by default): ballistic lead from the class-appropriate combat aim point, by its measured speed and the shot's.

using System;
using System.Collections.Generic;
using GoF2Remake.Data;
using UnityEngine;

namespace GoF2Remake.Flight
{
    public class TurretTargeting
    {
        const float M = Gun.MetersPerUnit;
        public const float PickMs = 3000f, LeadUnits = 1500f;
        const float TeleportSpeed = 5000f, MaxVelocitySampleSeconds = 0.5f;
        sealed class Motion { public Vector3 position, velocity; public float time; public int frame; }
        // Native shared acceleration estimates, sampled at most every 80 ms per target.
        public const bool NativeAccelerationLead = true;
        const float AccelInterval = 0.08f, AccelMaxGap = 0.55f, AccelMax = 600f;
        sealed class AccelMotion
        {
            public Vector3 position, previousVelocity, velocity, acceleration;
            public double time;
            public float previousDt, confidence;
            public bool hasVelocity;
        }
        static readonly Dictionary<Target, AccelMotion> accelerations = new Dictionary<Target, AccelMotion>();
        static double nextAccelSweep;
        public bool accelerationLead;
        static readonly Dictionary<Target, Motion> motions = new Dictionary<Target, Motion>();
        static readonly List<Target> gone = new List<Target>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetMotion() { motions.Clear(); gone.Clear(); accelerations.Clear(); nextAccelSweep = 0f; }


        public enum Rule { Retarget, Timed }

        public readonly Rule rule;
        /// <summary>How far it looks (units): the player's auto turrets 60000 (a mod's attr 109), NPC turrets and sentries 50000.</summary>
        public float rangeUnits;
        public float intervalMs = PickMs;
        /// <summary>May 't' be picked at all (hostility, cloak, radar-hidden...). Null = any.</summary>
        public Func<Target, bool> Candidate;
        /// <summary>Its distance in metres from 'from' (default: to its position), given the best distance so far (metres);
        /// the enhanced turrets measure to a big hull's nearest point and weight a turret hunter's prey once it is nearer
        /// than the best so far (CapitalShips).</summary>
        public Func<Target, Vector3, float, float> Measure;
        /// <summary>Remake (performance): Measure is never below this share of the distance from the target's sphere (Target.
        /// BroadSphere: its centre less its reach), so a target that can't beat the best so far is passed over without
        /// Candidate's and Measure's engine calls (20 fleet battles: 250 turrets over ~430 enemies). 0 = every one measured.</summary>
        public float boundFactor;

        /// <summary>The target it aims at, null = none.</summary>
        public Target Current { get; set; }
        /// <summary>The last Update made a new pick (the owner re-reads what it needs about the target).</summary>
        public bool JustPicked { get; private set; }
        /// <summary>The target that was out of reach (PlayerEgo+0x190 / PlayerTurret's ignored enemy).</summary>
        public Target Unreachable { get; private set; }
        float pickMs;

        /// <param name="pickAtOnce">The first Update picks (the player's auto turret starts with a full timer).</param>
        public TurretTargeting(Rule rule, float rangeUnits, bool pickAtOnce = false)
        {
            this.rule = rule;
            this.rangeUnits = rangeUnits;
            if (pickAtOnce) pickMs = intervalMs;
        }

        /// <summary>Only the four explicitly classified large hulls use nearest-surface aim.</summary>
        public static bool UsesHullSurface(string shipClass) =>
            string.Equals(shipClass, Modding.ShipClassRules.Freighter, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(shipClass, Modding.ShipClassRules.Frigate, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(shipClass, Modding.ShipClassRules.Cruiser, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(shipClass, Modding.ShipClassRules.Carrier, StringComparison.OrdinalIgnoreCase);

        /// <summary>Shared offensive aim point for fighters and all turret owners, based on the target, not the shooter.
        /// Unclassified ships aim at their collision-hull centre; non-ship targets retain the turret's old hull aim.</summary>
        public static Vector3 CombatAimPoint(Target t, Vector3 from)
        {
            if (t == null) return Vector3.zero;
            if (!t.isShip) return t.NearestHullPoint(from);
            int index = t.netShipIndex >= 0 ? t.netShipIndex : t.isPlayer ? Session.ShipIndex : t.shipType;
            string shipClass = index >= 0 ? Database.Shared.Ship(index)?.shipClass : null;
            if (!UsesHullSurface(shipClass)) return t.CombatCenter();
            return t.hull != null && t.hull.colliders != null && t.hull.colliders.Count > 0
                ? t.NearestHullPoint(from) : t.NearestPoint(from);
        }

        /// <summary>The player's lead point: where the target will be (its position + heading * 1500 units).</summary>
        public static Vector3 LeadPoint(Target t) => t.transform.position + t.transform.forward * LeadUnits * M;

        /// <summary>Legacy heading lead from the class-appropriate aim point, for a particular turret muzzle.</summary>
        public static Vector3 LeadPoint(Target t, Vector3 from) => CombatAimPoint(t, from) + t.transform.forward * LeadUnits * M;

        /// <summary>Ballistic lead to the class-appropriate aim point, using projectile speed and lifetime.</summary>
        public Vector3 LeadPoint(Target t, Vector3 from, float projectileSpeedUnitsPerMs, float projectileLifetimeMs) =>
            t == null ? Vector3.zero : !Settings.TurretLeadAim ? (t.isShip ? LeadPoint(t, from) : LeadPoint(t))
            : LeadPoint(t, CombatAimPoint(t, from), from, projectileSpeedUnitsPerMs, projectileLifetimeMs);

        /// <summary>Ballistic lead to a chosen point on a target (e.g. an NPC turret hardpoint).</summary>
        public Vector3 LeadPoint(Target t, Vector3 aimPoint, Vector3 from, float projectileSpeedUnitsPerMs, float projectileLifetimeMs)
        {
            if (t == null) return aimPoint;
            float speed = projectileSpeedUnitsPerMs * M * 1000f; // Gun speed is Unity distance units per millisecond.
            if (speed <= 1e-5f) return aimPoint;                 // instant beam / stationary shot

            if (accelerationLead && TryPredictedLead(t, aimPoint, from, speed, projectileLifetimeMs * 0.001f, out var predicted))
                return predicted;
            Vector3 velocity = VelocityOf(t);
            float flightSeconds = InterceptSeconds(aimPoint - from, velocity, speed, projectileLifetimeMs * 0.001f);
            return aimPoint + velocity * flightSeconds;
        }

        /// <summary>One bounded motion estimate per target, shared by native turrets and SMEP fighters.
        /// A newly acquired or discontinuously moving target falls back to the caller's original aim.</summary>
        internal static bool TryPredictedLead(Target target, Vector3 aimPoint, Vector3 from, float speed, float lifetime, out Vector3 point)
        {
            point = aimPoint;
            if (target == null || speed <= 1e-5f) return false;
            var motion = SampleAcceleration(target);
            if (!motion.hasVelocity) return false;
            Vector3 acceleration = motion.confidence >= 0.25f ? motion.acceleration * motion.confidence : Vector3.zero;
            point = from + PredictIntercept(aimPoint - from, motion.velocity, acceleration, speed, lifetime);
            return true;
        }

        /// <summary>Pure ballistic prediction, in metres and seconds. Return the direct aim when no
        /// reachable linear intercept exists; use linear lead if the bounded acceleration solver fails.</summary>
        public static Vector3 PredictIntercept(Vector3 relative, Vector3 velocity, Vector3 acceleration, float speed, float lifetime)
        {
            if (!(speed > 1e-5f)) return relative;
            float linearTime = InterceptSeconds(relative, velocity, speed, lifetime);
            return PredictFromLinearTime(relative, velocity, acceleration, speed, lifetime, linearTime);
        }

        /// <summary>HUD prediction must distinguish a reachable intercept from the turret's direct-aim fallback.</summary>
        public static bool TryIntercept(Vector3 relative, Vector3 velocity, Vector3 acceleration, float speed, float lifetime, out Vector3 point)
        {
            point = relative;
            if (!(speed > 1e-5f) || !(lifetime > 0f)) return false;
            if (relative.sqrMagnitude <= 1e-8f) return true;
            float time = InterceptSeconds(relative, velocity, speed, lifetime);
            if (time <= 0f) return false;
            point = PredictFromLinearTime(relative, velocity, acceleration, speed, lifetime, time);
            return true;
        }

        public static bool TryHudLead(Target target, Vector3 aimPoint, Vector3 muzzle, float speed, float lifetime, out Vector3 point)
        {
            point = aimPoint;
            if (target == null) return false;
            var motion = SampleAcceleration(target);
            if (!motion.hasVelocity) return false;
            Vector3 acceleration = motion.confidence >= 0.25f ? motion.acceleration * motion.confidence : Vector3.zero;
            if (!TryIntercept(aimPoint - muzzle, motion.velocity, acceleration, speed, lifetime, out var relative)) return false;
            point = muzzle + relative;
            return true;
        }

        static Vector3 PredictFromLinearTime(Vector3 relative, Vector3 velocity, Vector3 acceleration, float speed, float lifetime, float linearTime)
        {
            Vector3 linear = relative + velocity * linearTime;
            if (linearTime < 0.12f || acceleration.sqrMagnitude < 4f) return linear;
            float limit = Mathf.Min(150f, relative.magnitude * 0.15f);
            float time = linearTime;
            for (int i = 0; i < 6; i++)
            {
                float horizon = Mathf.Min(time, 1.25f);
                Vector3 predicted = relative + velocity * time +
                    Vector3.ClampMagnitude(acceleration * (0.5f * horizon * horizon), limit);
                float next = predicted.magnitude / speed;
                if (!(next > 0f) || float.IsInfinity(next) || (lifetime > 0f && next > lifetime)) return linear;
                if (Mathf.Abs(next - time) < 0.002f)
                    return (predicted - linear).sqrMagnitude <= limit * limit ? predicted : linear;
                time = next;
            }
            return linear;
        }

        static AccelMotion SampleAcceleration(Target target)
        {
            double now = Time.timeAsDouble;
            if (now >= nextAccelSweep || now < nextAccelSweep - 3f)
            {
                gone.Clear();
                foreach (var pair in accelerations)
                    if (pair.Key == null || now - pair.Value.time > 3f || now < pair.Value.time) gone.Add(pair.Key);
                foreach (var key in gone) accelerations.Remove(key);
                nextAccelSweep = now + 3.0;
            }
            if (!accelerations.TryGetValue(target, out var m))
                accelerations[target] = m = new AccelMotion { position = target.transform.position, time = now };
            float dt = (float)(now - m.time);
            if (dt >= 0f && dt < AccelInterval) return m;
            Vector3 position = target.transform.position;
            Vector3 previous = m.position;
            m.position = position;
            m.time = now;
            if (dt < 0f || dt > AccelMaxGap)
            {
                m.hasVelocity = false;
                m.velocity = m.acceleration = Vector3.zero;
                m.confidence = 0f;
                return m;
            }
            Vector3 raw = (position - previous) / dt;
            if (!(raw.sqrMagnitude <= TeleportSpeed * TeleportSpeed))
            {
                m.hasVelocity = false;
                m.velocity = m.acceleration = Vector3.zero;
                m.confidence = 0f;
                return m;
            }
            if (m.hasVelocity)
            {
                Vector3 a = (raw - m.previousVelocity) / (0.5f * (dt + m.previousDt));
                float magnitude = a.magnitude;
                if (!(magnitude <= AccelMax * 2.5f))
                {
                    m.acceleration = Vector3.zero;
                    m.confidence = 0f;
                    m.velocity = raw;
                }
                else
                {
                    a = Vector3.ClampMagnitude(a, AccelMax);
                    float prior = m.acceleration.magnitude;
                    float coherence = prior > 2f && magnitude > 2f ? Vector3.Dot(a.normalized, m.acceleration / prior) : 0f;
                    bool stable = coherence > 0.25f || (prior < 2f && magnitude < 5f);
                    m.confidence = Mathf.Clamp01(m.confidence + (stable ? 0.2f : -0.35f));
                    m.acceleration = Vector3.Lerp(m.acceleration, a, 0.4f);
                    // Finite difference is the velocity at the middle of the sample interval.
                    m.velocity = raw + m.acceleration * (0.5f * dt * m.confidence);
                }
            }
            else m.velocity = raw; // first velocity is not acceleration from rest
            m.previousVelocity = raw;
            m.previousDt = dt;
            m.hasVelocity = true;
            return m;
        }

        static Vector3 VelocityOf(Target t)
        {
            float now = Time.time;
            if (!motions.TryGetValue(t, out var m))
            {
                motions[t] = new Motion { position = t.transform.position, time = now, frame = Time.frameCount };
                return Vector3.zero;
            }
            if (m.frame == Time.frameCount) return m.velocity;

            float elapsed = now - m.time;
            Vector3 position = t.transform.position;
            Vector3 previous = m.position;
            m.frame = Time.frameCount;
            m.position = position;
            m.time = now;
            if (elapsed <= 1e-5f) return m.velocity;

            Vector3 measured = (position - previous) / elapsed;
            if (elapsed > MaxVelocitySampleSeconds || measured.sqrMagnitude > TeleportSpeed * TeleportSpeed)
                m.velocity = Vector3.zero;
            else
                m.velocity = Vector3.Lerp(m.velocity, measured, 0.5f);

            if ((Time.frameCount & 63) == 0)
            {
                gone.Clear();
                foreach (var pair in motions) if (pair.Key == null) gone.Add(pair.Key);
                foreach (var target in gone) motions.Remove(target);
            }
            return m.velocity;
        }

        static float InterceptSeconds(Vector3 relative, Vector3 velocity, float projectileSpeed, float lifetimeSeconds)
        {
            // Solve in double precision and avoid subtracting nearly equal quadratic roots.
            double c = (double)relative.x * relative.x + (double)relative.y * relative.y + (double)relative.z * relative.z;
            if (c <= 1e-8 || !(projectileSpeed > 0f)) return 0f;
            double a = (double)velocity.x * velocity.x + (double)velocity.y * velocity.y +
                       (double)velocity.z * velocity.z - (double)projectileSpeed * projectileSpeed;
            double b = 2.0 * ((double)relative.x * velocity.x + (double)relative.y * velocity.y + (double)relative.z * velocity.z);
            double solution = 0.0;
            if (Math.Abs(a) < 1e-8)
            {
                if (b < -1e-8) solution = -c / b;
            }
            else
            {
                double discriminant = b * b - 4.0 * a * c;
                if (discriminant >= 0.0)
                {
                    double root = Math.Sqrt(discriminant);
                    double q = -0.5 * (b + (b >= 0.0 ? root : -root));
                    double first = q / a, second = q == 0.0 ? 0.0 : c / q;
                    if (first > 0.0) solution = first;
                    if (second > 0.0 && (solution <= 0.0 || second < solution)) solution = second;
                }
            }
            float time = (float)solution;
            // No positive root means the projectile cannot intercept this target under the
            // measured constant-velocity model. Do not invent a direct-flight-time lead.
            if (time <= 0f || float.IsNaN(time) || float.IsInfinity(time)) return 0f;

            // A solution beyond the projectile's lifetime is not a reachable intercept.
            // Return zero lead rather than aiming at a point the shot cannot reach in time.
            if (lifetimeSeconds > 0f && time > lifetimeSeconds) return 0f;
            return time;
        }



        /// <summary>The timer runs; a new pick when the rule says so. Returns the target.</summary>
        public Target Update(float dtMs, IReadOnlyList<Target> pool, Vector3 from)
        {
            JustPicked = false;
            pickMs += dtMs;
            bool pick = rule == Rule.Timed ? pickMs > intervalMs
                : pickMs >= intervalMs || Current == null || !Current.Alive || !Current.isActiveAndEnabled || Current.cloaked || Current.untargetable;   // Modified: untargetable (a jump scene) too
            if (pick)
            {
                pickMs = 0f;
                Current = Pick(pool, from);
                JustPicked = true;
            }
            return Current;
        }

        /// <summary>The turret ran into a pitch limit aiming at the target (TurretAim.LimitHit).</summary>
        public void LimitHit(float dtMs)
        {
            Unreachable = Current;
            if (rule == Rule.Timed) { pickMs += dtMs; return; }   // handleTurret: kept until the next pick
            Current = null;
            pickMs = intervalMs;   // handleAutoTurret: another one at once
        }

        /// <summary>Forget the target (a new owner, a switch off); the timer keeps running.</summary>
        public void Clear() { Current = null; }

        float Distance(Target t, Vector3 from, float bestD) => Measure != null ? Measure(t, from, bestD) : (t.transform.position - from).magnitude;

        Target Pick(IReadOnlyList<Target> pool, Vector3 from)
        {
            Target best = null, ignored = null;
            float bestD = rangeUnits * M;
            int frame = Time.frameCount;
            if (pool != null)
                for (int i = 0; i < pool.Count; i++)
                {
                    var t = pool[i];
                    if ((object)t == null) continue;
                    if (boundFactor > 0f)
                    {
                        float r = t.BroadSphere(frame, out var c);
                        if (r < 0f || Mathf.Max(0f, (c - from).magnitude - r) * boundFactor >= bestD) continue;   // destroyed, or can't win
                    }
                    if (t == null || (Candidate != null && !Candidate(t))) continue;
                    if (rule == Rule.Retarget)
                    {
                        if (t == Unreachable) continue;
                        float d = Distance(t, from, bestD);
                        if (d < bestD) { bestD = d; best = t; }
                    }
                    else
                    {
                        float d = Distance(t, from, bestD);
                        if (d >= bestD) continue;
                        if (t == Unreachable) { ignored = t; continue; }
                        bestD = d;
                        best = t;
                    }
                }
            if (rule == Rule.Timed) return best ?? ignored;
            if (best == null) Unreachable = null;
            return best;
        }
    }
}
