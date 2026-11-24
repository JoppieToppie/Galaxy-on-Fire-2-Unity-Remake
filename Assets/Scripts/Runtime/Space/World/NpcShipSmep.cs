// NpcShipSmep.cs
// NpcShip, part: the optional SMEP fighter flight mode (a mod hull's traffic.ai = "smep").
// Eligible NPCs use authoritative 6DOF translation, with native ES2 flight reserved for controller fallback.
// ES2 aim and combat assist, acceleration-aware lead and native collision avoidance remain integrated.
// Mobile small-ship targets use conventional flight; large player hulls have platform parity.

using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.Modding;
using GoF2Remake.UI;
using GoF2Remake.Visuals;
using UnityEngine;

namespace GoF2Remake.World
{
    /// <summary>Pure SMEP policy shared by runtime and regression tests.</summary>
    public static class SmepAiRules
    {
        /// <summary>An explicit per-hull SMEP request is independent of global dogfight presets.</summary>
        public static bool RequestedByMod(string ai)
            => string.Equals(ai, "smep", System.StringComparison.OrdinalIgnoreCase);

        /// <summary>SMEP is opt-in on mobile builds; desktop retains the mod hull's normal opt-in behaviour.</summary>
        public static bool EnabledOnPlatform(bool mobilePlatform, bool mobileOptIn) => !mobilePlatform || mobileOptIn;

        /// <summary>Mobile player targets always use conventional flight regardless of mod opt-in.</summary>
        public static bool EnabledForTarget(bool mobileTarget, bool mobileOptIn) => !mobileTarget;

        /// <summary>Excluded NPC hull classes; the target's class is never consulted.</summary>
        public static bool ExcludedNpcClass(string shipClass)
            => string.Equals(shipClass, ShipClassRules.Frigate, System.StringComparison.OrdinalIgnoreCase)
               || string.Equals(shipClass, ShipClassRules.Cruiser, System.StringComparison.OrdinalIgnoreCase)
               || string.Equals(shipClass, ShipClassRules.Carrier, System.StringComparison.OrdinalIgnoreCase)
               || string.Equals(shipClass, ShipClassRules.Freighter, System.StringComparison.OrdinalIgnoreCase);

        public static bool ProtectedMobile(bool mobile, string shipClass) => mobile && !ExcludedNpcClass(shipClass);

        /// <summary>Controller selection uses the protected-mobile classification.</summary>
        public static bool UseCustomFlight(bool eligibleNpc, bool mobileTarget, bool empDisabled, bool canTurn, bool drifting)
            => eligibleNpc && !mobileTarget && !empDisabled && canTurn && !drifting;

        /// <summary>Player-style directional acceleration with one speed cap for all movement axes.</summary>
        public static Vector3 StepVelocity(Vector3 velocity, Vector3 desired, Quaternion rotation, float topSpeed,
                                           float dtMs, float forwardAccel, float reverseAccel, float strafeAccel, float hoverAccel)
        {
            var local = Quaternion.Inverse(rotation) * velocity;
            var goal = Quaternion.Inverse(rotation) * Vector3.ClampMagnitude(desired, Mathf.Max(0f, topSpeed));
            float dt = Mathf.Max(0f, dtMs) / 1000f;
            float cap = Mathf.Max(0f, topSpeed);
            local.x = Mathf.MoveTowards(local.x, goal.x, cap * Mathf.Max(0f, strafeAccel) * dt);
            local.y = Mathf.MoveTowards(local.y, goal.y, cap * Mathf.Max(0f, hoverAccel) * dt);
            local.z = Mathf.MoveTowards(local.z, goal.z, cap * Mathf.Max(0f, goal.z >= local.z ? forwardAccel : reverseAccel) * dt);
            return Vector3.ClampMagnitude(rotation * local, cap);
        }

        /// <summary>Refresh stale or sharply redirected path predictions without per-frame acos.</summary>
        public static bool RefreshPath(bool initialized, float remainingMs, Vector3 wanted, Vector3 previousWanted)
            => !initialized || remainingMs <= 0f || Vector3.Dot(wanted, previousWanted) < 0.8660254f;

        /// <summary>Velocity after an entire frame, including collision correction. No additive impulses.</summary>
        public static Vector3 VelocityFromDisplacement(Vector3 start, Vector3 end, float dtMs, float scale, float maxSpeed)
        {
            if (dtMs <= 0f || scale <= 0f) return Vector3.zero;
            return Vector3.ClampMagnitude((end - start) / (dtMs * scale), Mathf.Max(0f, maxSpeed));
        }

        /// <summary>Classifies local and remote player targets using the owning player's platform, not the host's.</summary>
        public static bool IsMobileTarget(bool isPlayer, bool netPilot, bool remoteMobilePlatform, bool localMobilePlatform)
            => (isPlayer || netPilot) && (netPilot ? remoteMobilePlatform : localMobilePlatform);

        /// <summary>For a new target, desktop players outrank mobile players; distance breaks ties within a tier.</summary>
        public static bool PreferPlayerTarget(bool candidateMobile, float candidateDistanceSq,
                                              bool bestExists, bool bestMobile, float bestDistanceSq)
            => !bestExists || (bestMobile && !candidateMobile)
               || (bestMobile == candidateMobile && candidateDistanceSq < bestDistanceSq);

        /// <summary>The wide-fire fallback must honour the same hard stops as the native NPC fire path.</summary>
        public static bool CanFireFallback(bool shootingEnabled, bool radarHidden, bool holdFire, bool targetable, bool cloaked,
                                           bool followingWaypoint, bool attacking, bool roleFires, bool smepHoldFire,
                                           bool es2Suppressed, bool dockedPlayerAbove)
            => shootingEnabled && !radarHidden && !holdFire && targetable && !cloaked && !followingWaypoint && attacking
               && roleFires && !smepHoldFire && !es2Suppressed && !dockedPlayerAbove;
    }

    public partial class NpcShip
    {
        const float SmepDogfightRange = 1000f, SmepCloseLimit = 300f;
        const float SmepWideFireRange = 4000f, SmepMobileFireAngle = 12f;

        bool smepFighterAi, smepHoldFire = true;
        Vector3 smepFrameStartPosition;
        float smepFrameTopSpeed, smepFrameDtMs;
        int smepMovementFrame = -1;
        bool smepPathInitialized;
        float smepPathMs, smepPathSpeed = 1f;
        Vector3 smepPathWanted, smepPathDirection;
        bool smepPathStraight = true;
        readonly List<Obstacle> smepPathObstacles = new List<Obstacle>(16);
        float smepHullTopSpeed, smepAccelerationForward, smepAccelerationReverse, smepAccelerationStrafe, smepAccelerationHover;
        // Player flight rules are resolved once at spawn, never in the movement loop.
        void SetupSmepHullFlight(ShipData ship)
        {
            var stats = Database.BuildFlightStats(ship, null);
            var rules = stats.rules ?? FlightRules.Default;
            var bonus = stats.free?.bonus ?? new FlightBonus();
            float handling = Mathf.Max(5f, stats.handling / 100f * 20f);
            float handlingFactor = Mathf.Pow(handling / 20f, rules.freeAccelHandlingExponent)
                                   * FlightBonus.Scale(bonus.acceleration);
            smepHullTopSpeed = rules.baseSpeed * FlightBonus.Scale(bonus.topSpeed);
            smepAccelerationForward = rules.freeAccelForward * handlingFactor * FlightBonus.Scale(bonus.forwardAcceleration);
            smepAccelerationReverse = (rules.freeAccelReverse > 0f ? rules.freeAccelReverse : rules.freeAccelSide)
                                      * handlingFactor * FlightBonus.Scale(bonus.reverseAcceleration);
            smepAccelerationStrafe = (rules.freeAccelStrafe > 0f ? rules.freeAccelStrafe : rules.freeAccelSide)
                                     * handlingFactor * FlightBonus.Scale(bonus.strafeAcceleration);
            smepAccelerationHover = (rules.freeAccelHover > 0f ? rules.freeAccelHover : rules.freeAccelSide)
                                    * handlingFactor * FlightBonus.Scale(bonus.hoverAcceleration);
        }
        Vector3 smepPendingAvoidanceDirection;
        Vector3 smepPreviousSteerPosition, smepObservedVelocity;
        int smepPreviousSteerFrame = -1;
        SmepDogfightState smepDogfightState;
        Target smepMotionTarget;
        Vector3 smepMotionPosition, smepMotionVelocity, smepMotionAcceleration, smepMotionSampleVelocity;
        double smepMotionTime;
        bool smepMotionInitialized, smepMotionHasSample;
        Vector3 smepReciprocalLastPosition;
        bool smepReciprocalHasPosition;
        static PlayerCollision smepPlayerCollision;
        static readonly List<SmepColliderSnapshot> smepContactCandidates = new List<SmepColliderSnapshot>(64);
        static readonly List<SmepPlayerShapeSnapshot> smepPlayerShapes = new List<SmepPlayerShapeSnapshot>(4);
        static PlayerCollision smepPlayerShapeOwner;
        static int smepPlayerShapeFrame = -1;

        struct SmepPlayerShapeSnapshot
        {
            public Collider collider;
            public Bounds bounds;
            public Vector3 position;
            public Quaternion rotation;
        }

        struct SmepColliderSnapshot
        {
            public Collider collider;
            public Bounds bounds;
            public Vector3 position;
            public Quaternion rotation;
        }

        sealed class SmepDogfightState
        {
            public Target target;
            public Vector3 velocity, burstDirection;
            public float phase, hoverRate, wideOrbit, nextOrbitChange, lastCloseSample, nextRetreatCheck;
            public float nextBurstCheck, burstUntil;
            public int orbitSign, closeApproachCount;
            public bool retreating;
        }

        bool SmepMobileOptIn => Spec != null && (db?.Ship(Spec.ship) as CustomShipData)?.traffic?.mobileAi == true;

        // A mod opted-in fighter keeps its flight controller even when it is an
        // escort, scripted ship or under an order. Fixed structures, freighters
        // and capital hulls were excluded at initialization.
        bool SmepHullEligible => smepFighterAi && Spec != null && !IsFreighter && !IsFixed && !IsTurret
                                 && Spec.capital == 0 && !Spec.capitalPart && !Spec.modCapital && !Spec.stationary
                                 && !Spec.structure && !Spec.stationPort && !Spec.eventWreck && Spec.eventTag == 0
                                 && !IsStructure(Spec);
        // Mobile protection applies only to non-capital player hulls.
        bool SmepEnabledForTarget => SmepHullEligible && SmepAiRules.EnabledForTarget(ProtectedMobileTarget(target), SmepMobileOptIn);
        bool SmepEligible => SmepEnabledForTarget;

        static bool MobilePlatformTarget(Target t)
            => t != null && SmepAiRules.IsMobileTarget(t.isPlayer, t.netPilot, t.mobilePlatform, Application.isMobilePlatform);

        bool ProtectedMobileTarget(Target t)
        {
            if (!MobilePlatformTarget(t)) return false;
            int index = t.netShipIndex >= 0 ? t.netShipIndex : t.shipType;
            return SmepAiRules.ProtectedMobile(true, index >= 0 ? ShipClassRules.ClassOf(db?.Ship(index)) : null);
        }

        bool MobileTouchTarget(Target t)
        {
            if (!ProtectedMobileTarget(t)) return false;
            return t.netPilot ? t.mobileTouchControls
                              : Application.isMobilePlatform && InputMode.Current == InputKind.Touch;
        }

        /// <summary>Find an eligible hostile remote player, optionally restricted to desktop.</summary>
        Target FindSmepRemotePlayer(bool desktopOnly)
        {
            if (!SmepHullEligible || IsWingman || MissionShip || StoryShip || ScriptEnemy != null || order != null || mindControlMs > 0f || !NetSession.Active || RemotePlayers == null || HostileToRemote == null)
                return null;
            var origin = transform.position;
            float detect = (detectRange > 0f ? detectRange : NpcTables.DetectRange) * M;
            float detectSq = detect * detect;
            Target best = null;
            float bestDistance = float.MaxValue;
            bool bestMobile = false;
            foreach (var candidate in RemotePlayers)
            {
                if (!Pursuable(candidate) || !HostileToRemote(this, candidate)) continue;
                bool mobile = ProtectedMobileTarget(candidate);
                if (desktopOnly && mobile) continue;
                float distance = (candidate.transform.position - origin).sqrMagnitude;
                if (distance > detectSq || !SmepAiRules.PreferPlayerTarget(mobile, distance, best != null, bestMobile, bestDistance))
                    continue;
                best = candidate;
                bestMobile = mobile;
                bestDistance = distance;
            }
            return best;
        }

        void SetSmepRemoteTarget(Target picked)
        {
            remoteTarget = picked;
            target = picked;
            targetPos = picked.transform.position;
            attacking = true;
            followingWaypoint = false;
        }

        /// <summary>Called before normal target acquisition; never displaces a valid active opponent.</summary>
        bool TrySmepDesktopRemoteTarget()
        {
            if (!SmepHullEligible || (attacking && target != null && Pursuable(target) && InBox(target)
                                       && (!target.netPilot || HostileToRemote != null && HostileToRemote(this, target))))
                return false;
            var desktop = FindSmepRemotePlayer(true);
            if (desktop == null) return false;
            SetSmepRemoteTarget(desktop);
            return true;
        }

        /// <summary>Keep valid combat targets; otherwise prefer desktop remotes, then mobile remotes.</summary>
        void UpdateSmepTargetPriority()
        {
            if (!SmepHullEligible) return;
            if (attacking && target != null && Pursuable(target) && InBox(target)
                && (!target.netPilot || HostileToRemote != null && HostileToRemote(this, target))) return;
            var picked = FindSmepRemotePlayer(false);
            if (picked != null) SetSmepRemoteTarget(picked);
        }

        /// <summary>SMEP's acceleration-aware ballistic lead. Target samples are rate-limited and smoothed per fighter.</summary>
        Vector3 SmepAccelerationLead(Vector3 aimPoint, Vector3 from, Vector3 nativeLead)
        {
            if (target == null || !target.Alive) return nativeLead;
            SampleSmepTargetMotion(out var velocity, out var acceleration);
            // Until two valid positions exist, native lead is more informative than a zero-motion estimate.
            if (!smepMotionHasSample) return nativeLead;
            var firing = gun;
            if (firing == null || firing.isBeam || firing.IsMissile || firing.Homing || firing.Guided) return nativeLead;
            float projectileSpeed = firing.speedUnitsPerMs * M * 1000f;
            if (projectileSpeed <= 1e-3f) return nativeLead;
            var relative = aimPoint - from;
            float linear = Es2Aim.InterceptTime(relative.x, relative.y, relative.z,
                                                 velocity.x, velocity.y, velocity.z, projectileSpeed);
            if (linear <= 0f || linear > 6f) return nativeLead;
            float time = SmepAccelerationIntercept(relative, velocity, acceleration, projectileSpeed, linear);
            if (time <= 0f) return nativeLead;
            return aimPoint + velocity * time + acceleration * (0.5f * time * time);
        }

        void SampleSmepTargetMotion(out Vector3 velocity, out Vector3 acceleration)
        {
            var motionTarget = target;
            var position = motionTarget.hull != null ? motionTarget.hull.transform.position : motionTarget.transform.position;
            double now = Time.timeAsDouble;
            if (!smepMotionInitialized || smepMotionTarget != motionTarget)
            {
                smepMotionInitialized = true;
                smepMotionHasSample = false;
                smepMotionTarget = motionTarget;
                smepMotionPosition = position;
                smepMotionTime = now;
                smepMotionVelocity = smepMotionAcceleration = smepMotionSampleVelocity = Vector3.zero;
                velocity = acceleration = Vector3.zero;
                return;
            }

            double elapsedD = now - smepMotionTime;
            if (elapsedD < 0.04)
            {
                velocity = smepMotionVelocity;
                acceleration = smepMotionAcceleration;
                return;
            }
            float elapsed = (float)elapsedD;
            var sampleVelocity = Vector3.ClampMagnitude((position - smepMotionPosition) / elapsed, 1500f);
            if (elapsedD > 0.5)
            {
                smepMotionHasSample = false;
                smepMotionVelocity = Vector3.zero;
                smepMotionAcceleration = Vector3.zero;
                smepMotionSampleVelocity = Vector3.zero;
                smepMotionPosition = position;
                smepMotionTime = now;
                velocity = smepMotionVelocity;
                acceleration = Vector3.zero;
                return;
            }
            var sampleAcceleration = Vector3.ClampMagnitude((sampleVelocity - smepMotionSampleVelocity) / elapsed, 3000f);
            smepMotionVelocity = Vector3.Lerp(smepMotionVelocity, sampleVelocity, 0.65f);
            smepMotionAcceleration = Vector3.Lerp(smepMotionAcceleration, sampleAcceleration, 0.3f);
            smepMotionSampleVelocity = sampleVelocity;
            smepMotionHasSample = true;
            smepMotionPosition = position;
            smepMotionTime = now;
            velocity = smepMotionVelocity;
            acceleration = smepMotionAcceleration;
        }

        static float SmepAccelerationIntercept(Vector3 relative, Vector3 velocity, Vector3 acceleration, float speed, float fallback)
        {
            float searchEnd = Mathf.Min(6f, Mathf.Max(0.5f, fallback * 1.5f));
            float sampleStep = searchEnd / 12f;
            float previousTime = 0f;
            float low = 0f, high = 0f;
            bool bracketed = false;
            for (int i = 1; i <= 12; i++)
            {
                float time = i * sampleStep;
                float travel = speed * time;
                float error = (relative + velocity * time + acceleration * (0.5f * time * time)).sqrMagnitude - travel * travel;
                if (error <= 0f)
                {
                    low = previousTime;
                    high = time;
                    bracketed = true;
                    break;
                }
                previousTime = time;
            }
            if (!bracketed) return 0f;
            for (int i = 0; i < 10; i++)
            {
                float middle = (low + high) * 0.5f;
                float travel = speed * middle;
                float error = (relative + velocity * middle + acceleration * (0.5f * middle * middle)).sqrMagnitude - travel * travel;
                if (error > 0f) low = middle; else high = middle;
            }
            float result = (low + high) * 0.5f;
            return result > 0f && result <= 6f ? result : 0f;
        }

        /// <summary>Predicts candidate 6DOF trajectories from actual velocity, not nose heading.</summary>
        void SmepPathTest(Vector3 currentVelocity, Vector3 wanted, float topSpeed)
        {
            smepPathStraight = true;
            smepPathSpeed = 1f;
            if (topSpeed <= 0f || M <= 0f) return;
            float r = Mathf.Max(1f, Target != null ? Target.radius : 10f);
            float reach = (Mathf.Max(topSpeed, currentVelocity.magnitude) * 1000f
                           * Es2NpcFlight.PredictionS * M) + r;
            Es2GatherAsteroids(transform.position, reach);
            smepPathObstacles.Clear();
            int frame = Time.frameCount;
            var origin = transform.position;
            foreach (var obstacle in Obstacle.All)
                if (obstacle != null && (target == null || obstacle.transform != target.transform) &&
                    obstacle.MayReach(origin, reach, frame)) smepPathObstacles.Add(obstacle);
            if (es2Near.Count == 0 && smepPathObstacles.Count == 0) return;
            if (SmepPathClear(currentVelocity, wanted, topSpeed, r)) return;

            var side = Vector3.Cross(wanted, Mathf.Abs(Vector3.Dot(wanted, Vector3.up)) > 0.95f
                                             ? Vector3.right : Vector3.up).normalized;
            foreach (var (cone, speedFactor) in Es2NpcFlight.Candidates)
            {
                if (cone < 0.01f && speedFactor >= 1f) continue; // already tested the direct route
                // A zero cone has only one distinct direction, even at reduced speed.
                int directions = cone == 0f ? 1 : 8;
                for (int i = 0; i < directions; i++)
                {
                    var axis = Quaternion.AngleAxis(i * 360f / directions, wanted) * side;
                    var candidate = Quaternion.AngleAxis(cone, axis) * wanted;
                    if (!SmepPathClear(currentVelocity, candidate * speedFactor, topSpeed, r)) continue;
                    smepPathStraight = false;
                    smepPathDirection = candidate;
                    smepPathSpeed = speedFactor;
                    return;
                }
            }
            // All candidate routes obstructed: command less thrust, keeping the same
            // physical speed cap and giving the next probe more time to find a detour.
            smepPathStraight = false;
            smepPathDirection = wanted;
            smepPathSpeed = 0.25f;
        }

        bool SmepPathClear(Vector3 currentVelocity, Vector3 candidate, float topSpeed, float radius)
        {
            var pos = transform.position;
            var velocity = currentVelocity;
            var targetVelocity = Vector3.ClampMagnitude(candidate * topSpeed, topSpeed);
            float dtMs = 1000f / Es2NpcFlight.SamplesPerS;
            int frames = Mathf.CeilToInt(Es2NpcFlight.PredictionS * Es2NpcFlight.SamplesPerS);
            int frame = Time.frameCount;
            var all = smepPathObstacles;
            var rotation = transform.rotation;
            for (int i = 0; i < frames; i++)
            {
                velocity = SmepAiRules.StepVelocity(velocity, targetVelocity, rotation, topSpeed,
                                                    dtMs, smepAccelerationForward, smepAccelerationReverse,
                                                    smepAccelerationStrafe, smepAccelerationHover);
                pos += velocity * dtMs * M;
                foreach (var asteroid in es2Near)
                {
                    float combined = radius + asteroid.radius;
                    if ((asteroid.transform.position - pos).sqrMagnitude < combined * combined) return false;
                }
                for (int k = 0; k < all.Count; k++)
                {
                    var obstacle = all[k];
                    if (obstacle == null || (target != null && obstacle.transform == target.transform)
                        || !obstacle.MayReach(pos, radius, frame)) continue;
                    // Keep legacy gate-cube contacts and account for the fighter's full clearance.
                    if (obstacle.Touches(pos, out _) || obstacle.TouchesSphere(pos, radius, out _)) return false;
                }
            }
            return true;
        }

        /// <summary>SMEP's authoritative acceleration-limited translation, with ES2 reserved for fallback.</summary>
        void ApplySmepDogfightTranslation(float dtMs, Vector3 beforePosition, float effectiveTopSpeed)
        {
            if (!SmepEligible)
            {
                smepDogfightState = null;
                return;
            }
            bool combatMotion = target != null && target.Alive && attacking && !followingWaypoint;
            var targetPoint = combatMotion ? target.transform.position : targetPos;
            if (!combatMotion && (targetPoint - beforePosition).sqrMagnitude < 1f)
                targetPoint = beforePosition + transform.forward * 1200f;
            var offset = beforePosition - targetPoint;
            float distance = combatMotion ? offset.magnitude : 0f;
            // Keep the controller and its velocity state alive through pursuit, not just close combat.

            if (smepDogfightState == null || smepDogfightState.target != target)
            {
                smepPathInitialized = false;
                float now = Time.time;
                smepDogfightState = new SmepDogfightState
                {
                    target = target,
                    // Prefer measured recent motion when switching from the native controller.
                    velocity = Vector3.ClampMagnitude(smepObservedVelocity, Mathf.Max(0f, effectiveTopSpeed)),
                    phase = Random.Range(0f, Mathf.PI * 2f),
                    hoverRate = Random.Range(0.85f, 1.25f),
                    orbitSign = Random.value < 0.5f ? -1 : 1,
                    nextOrbitChange = now + Random.Range(12f, 20f),
                    nextRetreatCheck = now + Random.Range(2f, 5f),
                    nextBurstCheck = now + Random.Range(2.5f, 5f),
                    lastCloseSample = 0f,
                    wideOrbit = 0f,
                    burstUntil = 0f,
                    burstDirection = Vector3.zero,
                };
            }

            var state = smepDogfightState;
            float nowTime = Time.time;
            var away = Vector3.zero;
            Vector3 desired;
            bool bursting = false;
            if (!combatMotion)
            {
                // Navigation does not need orbit geometry, waves, random bursts or retreat bookkeeping.
                state.retreating = false;
                state.burstUntil = 0f;
                desired = (targetPoint - beforePosition).normalized;
            }
            else
            {
                away = distance > 0.01f ? offset / distance : transform.rotation * Vector3.back;
                if (distance < 520f && nowTime - state.lastCloseSample > 1.25f)
                {
                    state.closeApproachCount = Mathf.Min(8, state.closeApproachCount + 1);
                    state.lastCloseSample = nowTime;
                    state.wideOrbit = Mathf.Min(700f, state.closeApproachCount * 100f);
                }
                else if (distance > 1800f && nowTime - state.lastCloseSample > 4f)
                {
                    state.closeApproachCount = Mathf.Max(0, state.closeApproachCount - 1);
                    state.wideOrbit = Mathf.Max(0f, state.closeApproachCount * 100f);
                    state.lastCloseSample = nowTime;
                }

                float desiredRange = SmepDogfightRange + state.wideOrbit;
                if (distance <= SmepCloseLimit) state.retreating = true;
                else if (state.retreating && distance >= desiredRange + 350f)
                {
                    state.retreating = false;
                    state.nextRetreatCheck = nowTime + Random.Range(3f, 7f);
                }
                else if (!state.retreating && distance < desiredRange - 200f && nowTime >= state.nextRetreatCheck)
                {
                    if (Random.value < 0.22f) state.retreating = true;
                    state.nextRetreatCheck = nowTime + Random.Range(3f, 7f);
                }

                float radialShare;
                if (state.retreating) radialShare = -1f;
                else if (distance > desiredRange + 200f) radialShare = 1f;
                else if (distance > desiredRange) radialShare = Mathf.Clamp((distance - desiredRange) / 200f, 0.15f, 0.75f);
                else radialShare = state.wideOrbit > 0f ? 0f : 0.04f;

                var tangent = Vector3.Cross(Vector3.up, away);
                if (tangent.sqrMagnitude < 0.001f) tangent = transform.right;
                else tangent.Normalize();
                if (nowTime >= state.nextOrbitChange)
                {
                    if (Random.value < 0.35f) state.orbitSign = -state.orbitSign;
                    state.nextOrbitChange = nowTime + Random.Range(12f, 20f);
                }

                float verticalWave = Mathf.Sin(nowTime * state.hoverRate + state.phase * 1.7f) * (state.wideOrbit > 0f ? 0.48f : 0.34f)
                                   + Mathf.Sin(nowTime * state.hoverRate * 0.53f + state.phase) * (state.wideOrbit > 0f ? 0.18f : 0.13f);
                var vertical = Vector3.up * verticalWave;
                float orbitSpeed = state.wideOrbit > 0f ? 1f : (distance < 700f ? 0.92f : 0.82f);
                float sideWobble = Mathf.Sin(nowTime * 0.63f + state.phase) * (state.wideOrbit > 0f ? 0.015f : 0.04f);
                desired = state.retreating
                    ? (away + tangent * (state.orbitSign * 0.12f) + vertical * 0.08f).normalized
                    : away * -radialShare + tangent * (state.orbitSign * orbitSpeed + sideWobble) + vertical;

                if (nowTime >= state.nextBurstCheck)
                {
                    if (Random.value < (state.wideOrbit > 0f ? 0.05f : 0.12f))
                    {
                        int burstSide = Random.value < 0.5f ? -1 : 1;
                        state.burstDirection = (tangent * burstSide + away * Random.Range(-0.65f, 0.65f)
                                                + Vector3.up * Random.Range(-0.7f, 0.7f)).normalized;
                        state.burstUntil = nowTime + Random.Range(0.18f, 0.32f);
                    }
                    state.nextBurstCheck = nowTime + Random.Range(2.5f, 5f);
                }
                bursting = nowTime < state.burstUntil;
                if (bursting) desired = state.burstDirection;
            }
            if (desired.sqrMagnitude > 1f) desired.Normalize();

            // Look ahead along actual 6DOF velocity, accelerating toward each candidate
            // with the same flight-rule step as the real NPC. ES2's forward-only
            // trajectory simulator cannot accurately predict strafing or hovering.
            if (desired.sqrMagnitude > 0.0001f && M > 0f)
            {
                var wanted = desired.normalized;
                smepPathMs -= dtMs;
                if (SmepAiRules.RefreshPath(smepPathInitialized, smepPathMs, wanted, smepPathWanted))
                {
                    smepPathWanted = wanted;
                    SmepPathTest(state.velocity, wanted, Mathf.Max(0f, effectiveTopSpeed));
                    // Shorten (never extend) the existing refresh interval by a stable per-ship
                    // phase to distribute expensive trajectory probes across frames.
                    float phase = 0.85f + (unchecked((uint)GetInstanceID()) % 16u) * 0.01f;
                    smepPathMs = Es2NpcFlight.ForceUpdateS * 1000f * (smepPathStraight ? 2f : 1f) * phase;
                    smepPathInitialized = true;
                }
                if (!smepPathStraight)
                    desired = smepPathDirection * (desired.magnitude * smepPathSpeed);
            }
            // All thrust directions share the ship's top speed; only acceleration/response may differ.
            float topSpeed = Mathf.Max(0f, effectiveTopSpeed);
            desired *= topSpeed;

            float dt = Mathf.Max(0f, dtMs);
            state.velocity = SmepAiRules.StepVelocity(state.velocity, desired, transform.rotation,
                                                       topSpeed, dt, smepAccelerationForward, smepAccelerationReverse,
                                                       smepAccelerationStrafe, smepAccelerationHover);
            // The already-clamped velocity is the only movement source, including retreat.
            var step = state.velocity * dt * M;
            transform.position = beforePosition + step;
            // Keep ES2's fallback velocity in sync with the authoritative SMEP displacement.
            es2Vel = dt > 0f && M > 0f ? step / (dt * M) * 1000f : Vector3.zero;
            es2FlownFrame = Time.frameCount;
        }

        void SyncSmepAfterAvoidance()
        {
            if (!SmepEligible || smepDogfightState == null || smepMovementFrame != Time.frameCount
                || smepFrameDtMs <= 0f || M <= 0f) return;
            // Reconstruct the movement once from the frame start, never add corrections twice.
            // Penetration resolution may move farther than normal thrust; cap the resulting
            // velocity rather than putting the ship back inside the collision volume.
            smepDogfightState.velocity = SmepAiRules.VelocityFromDisplacement(smepFrameStartPosition,
                                                                             transform.position, smepFrameDtMs, M, smepFrameTopSpeed);
            es2Vel = smepDogfightState.velocity * 1000f;
            es2FlownFrame = Time.frameCount;
        }

        void TurnSmepVelocityAwayFromObstacle(Vector3 direction, float dtMs)
        {
            if (!SmepEligible || smepDogfightState == null || direction.sqrMagnitude < 0.00001f) return;
            var velocity = smepDogfightState.velocity;
            if (velocity.sqrMagnitude < 0.00001f) return;
            // A bounded steering response, preserving speed rather than adding a second move.
            smepDogfightState.velocity = Vector3.RotateTowards(velocity, direction.normalized * velocity.magnitude,
                                                               Mathf.Max(0f, dtMs) * 0.001f, 0f);
            es2Vel = smepDogfightState.velocity * 1000f;
        }

        /// <summary>Fallback for a ready primary gun when the native ES2 scheduler did not fire this frame.</summary>
        void FireSmepReadyPrimaryGuns()
        {
            if (!SmepEligible || !PlayerTarget(target) || MobileTouchTarget(target) || target == null || !target.Alive) return;
            bool dockedPlayerAbove = target.isPlayer && ObjectDocking.PlayerDocked
                                     && target.transform.position.y > transform.position.y;
            if (!SmepAiRules.CanFireFallback(shootingEnabled, radarHidden, HoldFire, target.Targetable, target.cloaked,
                                             followingWaypoint, attacking, RoleFires, smepHoldFire, Es2Suppressed,
                                             dockedPlayerAbove)) return;
            if ((target.transform.position - transform.position).sqrMagnitude > SmepWideFireRange * SmepWideFireRange) return;
            var aim = Es2Lead(es2AimPoint, transform.position);
            var aimDirection = aim - transform.position;
            if (aimDirection.sqrMagnitude < 1e-6f || Vector3.Angle(transform.forward, aimDirection) > SmepMobileFireAngle) return;
            TryFireSmepPrimary(gun, false);
            if (secondGun != gun) TryFireSmepPrimary(secondGun, true);
        }

        void TryFireSmepPrimary(Gun firing, bool second)
        {
            if (firing == null || firing.isSecondary || !firing.Ready || firing.TryFire(transform) < 0) return;
            int shot = NpcTables.ShotSound(Race);
            var clip = second ? (secondRig?.fx ?? WeaponFx.Load(firing.itemIndex))?.Shot
                              : assets != null && assets.shots != null && shot < assets.shots.Length ? assets.shots[shot] : null;
            if (clip != null) ShotVoices.PlayAt(clip, transform.position, 0.8f);
        }

        /// <summary>Reciprocal mesh contact with the local player. The non-landmark avoidance pass is intentionally
        /// suppressed for eligible SMEP fighters; landmark avoidance remains native.</summary>
        void SeparateSmepNpcFromPlayer()
        {
            var current = transform.position;
            if (!SmepEligible || Target == null || Target.hull == null || !Target.hull.Active)
            {
                smepReciprocalLastPosition = current;
                smepReciprocalHasPosition = true;
                return;
            }
            if (smepPlayerCollision == null || !smepPlayerCollision.isActiveAndEnabled)
                smepPlayerCollision = Object.FindAnyObjectByType<PlayerCollision>();
            var collision = smepPlayerCollision;
            if (collision == null || collision.off || (collision.cloak != null && collision.cloak.Phasing))
            {
                smepReciprocalLastPosition = current;
                smepReciprocalHasPosition = true;
                return;
            }

            var body = Target.hull;
            var colliders = body.colliders;
            if (colliders == null || colliders.Count == 0)
            {
                smepReciprocalLastPosition = current;
                smepReciprocalHasPosition = true;
                return;
            }
            var previous = smepReciprocalHasPosition ? smepReciprocalLastPosition : current;
            var travel = current - previous;
            var absTravel = new Vector3(Mathf.Abs(travel.x), Mathf.Abs(travel.y), Mathf.Abs(travel.z));
            // Snapshot the player's native collision boxes once per frame, shared by every SMEP fighter.
            // A probe sphere is the fallback on models without hull boxes.
            if (smepPlayerShapeOwner != collision || smepPlayerShapeFrame != Time.frameCount)
            {
                smepPlayerShapes.Clear();
                int shapeCount = collision.SmepShapeCount;
                for (int i = 0; i < shapeCount; i++)
                {
                    var shape = collision.SmepShapeAt(i);
                    if (shape == null || !shape.enabled || !shape.gameObject.activeInHierarchy) continue;
                    smepPlayerShapes.Add(new SmepPlayerShapeSnapshot
                    {
                        collider = shape,
                        bounds = shape.bounds,
                        position = shape.transform.position,
                        rotation = shape.transform.rotation,
                    });
                }
                smepPlayerShapeOwner = collision;
                smepPlayerShapeFrame = Time.frameCount;
            }
            if (smepPlayerShapes.Count == 0)
            {
                smepReciprocalLastPosition = current;
                smepReciprocalHasPosition = true;
                return;
            }

            // First reject against the aggregate player bounds. This is conservative:
            // it never drops a contact with an individual player collider.
            var playerBounds = smepPlayerShapes[0].bounds;
            for (int i = 1; i < smepPlayerShapes.Count; i++)
                playerBounds.Encapsulate(smepPlayerShapes[i].bounds);

            // Broadphase each NPC collider once against the complete swept path.
            smepContactCandidates.Clear();
            for (int i = 0; i < colliders.Count; i++)
            {
                var collider = colliders[i];
                if (collider == null || !HullCollision.Shown(collider)) continue;
                var bounds = collider.bounds;
                var swept = new Bounds(bounds.center - travel * 0.5f, bounds.size + absTravel);
                if (!swept.Intersects(playerBounds)) continue;
                bool possible = false;
                for (int shapeIndex = 0; shapeIndex < smepPlayerShapes.Count; shapeIndex++)
                    if (swept.Intersects(smepPlayerShapes[shapeIndex].bounds)) { possible = true; break; }
                if (!possible) continue;
                smepContactCandidates.Add(new SmepColliderSnapshot
                {
                    collider = collider,
                    bounds = bounds,
                    position = collider.transform.position,
                    rotation = collider.transform.rotation,
                });
            }
            if (smepContactCandidates.Count == 0)
            {
                smepReciprocalLastPosition = current;
                smepReciprocalHasPosition = true;
                return;
            }

            // Only calculate sweep resolution and run narrow-phase physics when
            // the full-path broadphase found a plausible collider pair.
            int steps = Mathf.Clamp(Mathf.CeilToInt(travel.magnitude / 2f), 1, 64);
            for (int stepIndex = 1; stepIndex <= steps; stepIndex++)
            {
                float t = (float)stepIndex / steps;
                var sampled = Vector3.Lerp(previous, current, t);
                var poseOffset = sampled - current;
                bool resolvedContact = false;
                for (int shapeIndex = 0; shapeIndex < smepPlayerShapes.Count; shapeIndex++)
                {
                    var shape = smepPlayerShapes[shapeIndex];
                    float bestDepth = 0f;
                    Vector3 bestDirection = Vector3.zero;
                    for (int i = 0; i < smepContactCandidates.Count; i++)
                    {
                        var candidate = smepContactCandidates[i];
                        var bounds = candidate.bounds;
                        bounds.center += poseOffset;
                        bounds.Expand(0.16f);
                        if (!bounds.Intersects(shape.bounds)) continue;
                        if (!Physics.ComputePenetration(shape.collider, shape.position, shape.rotation, candidate.collider,
                                                         candidate.position + poseOffset, candidate.rotation,
                                                         out var direction, out float depth) || depth <= bestDepth) continue;
                        bestDepth = depth;
                        bestDirection = direction;
                    }
                    if (bestDepth > 0.001f)
                    {
                        var resolved = sampled - bestDirection * (bestDepth + 0.08f);
                        transform.position = resolved;
                        Physics.SyncTransforms();
                        smepReciprocalLastPosition = resolved;
                        smepReciprocalHasPosition = true;
                        resolvedContact = true;
                        break;
                    }
                }
                if (resolvedContact) return;
            }
            smepReciprocalLastPosition = transform.position;
            smepReciprocalHasPosition = true;
        }
    }
}
