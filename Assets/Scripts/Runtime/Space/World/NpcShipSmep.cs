// NpcShipSmep.cs
// NpcShip, part: the optional SMEP fighter flight mode (a mod hull's traffic.ai = "smep").
// It uses the remake's native ES2 aim, dogfight and physics flight, then adds the Lua mode's range/strafe,
// acceleration-aware ballistic lead, remote-player priority, firing fallback and reciprocal player/hull contact.
// Mobile builds and mobile multiplayer targets require traffic.mobileAi: true; opted-in touch pilots retain reduced behaviour.

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
        /// <summary>SMEP is opt-in on mobile builds; desktop retains the mod hull's normal opt-in behaviour.</summary>
        public static bool EnabledOnPlatform(bool mobilePlatform, bool mobileOptIn) => !mobilePlatform || mobileOptIn;

        /// <summary>Mobile player targets always use conventional flight regardless of mod opt-in.</summary>
        public static bool EnabledForTarget(bool mobileTarget, bool mobileOptIn) => !mobileTarget;

        /// <summary>Classifies local and remote player targets using the owning player's platform, not the host's.</summary>
        public static bool IsMobileTarget(bool isPlayer, bool netPilot, bool remoteMobilePlatform, bool localMobilePlatform)
            => (isPlayer || netPilot) && (netPilot ? remoteMobilePlatform : localMobilePlatform);

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
        SmepDogfightState smepDogfightState;
        Target smepMotionTarget;
        Vector3 smepMotionPosition, smepMotionVelocity, smepMotionAcceleration, smepMotionSampleVelocity;
        double smepMotionTime;
        bool smepMotionInitialized;
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
        bool SmepHullEligible => smepFighterAi && Spec != null && !IsFreighter && !IsFixed && !IsTurret && !IsWingman
                                 && Spec.capital == 0 && !Spec.capitalPart && !Spec.modCapital && !Spec.stationary
                                 && !Spec.structure && !Spec.stationPort && !Spec.eventWreck && Spec.eventTag == 0
                                 && !IsStructure(Spec) && !MissionShip && !StoryShip && ScriptEnemy == null && !turnedEnemy
                                 && mindControlMs <= 0f
                                 && ShipClassRules.ClassOf(db?.Ship(Spec.ship)) != ShipClassRules.Cruiser
                                 && ShipClassRules.ClassOf(db?.Ship(Spec.ship)) != ShipClassRules.Carrier
                                 && ShipClassRules.ClassOf(db?.Ship(Spec.ship)) != ShipClassRules.Frigate
                                 && ShipClassRules.ClassOf(db?.Ship(Spec.ship)) != ShipClassRules.Freighter;
        // A mobile player target always forces conventional flight, even with mobileAi opt-in.
        bool SmepEnabledForTarget => SmepHullEligible && SmepAiRules.EnabledForTarget(MobilePlatformTarget(target), SmepMobileOptIn);
        bool SmepEligible => SmepEnabledForTarget;

        static bool MobilePlatformTarget(Target t)
            => t != null && SmepAiRules.IsMobileTarget(t.isPlayer, t.netPilot, t.mobilePlatform, Application.isMobilePlatform);

        static bool MobileTouchTarget(Target t)
        {
            if (t == null || (!t.isPlayer && !t.netPilot)) return false;
            return t.netPilot ? t.mobileTouchControls
                              : Application.isMobilePlatform && InputMode.Current == InputKind.Touch;
        }

        /// <summary>The Lua mode's multiplayer player pick: desktop first, mobile second, mobile cruisers first of all.</summary>
        void UpdateSmepTargetPriority()
        {
            if (!SmepHullEligible || !NetSession.Active || RemotePlayers == null || HostileToRemote == null) return;
            // Keep an existing valid hostile target instead of switching pilots every update.
            // Mobile pilots without explicit opt-in remain ineligible for SMEP priority selection.
            if (target != null && Pursuable(target) && InBox(target)
                && (target.netPilot ? HostileToRemote(this, target) : remoteTarget == null)
                && !MobilePlatformTarget(target)) return;
            Target best = null;
            int bestPriority = int.MaxValue;
            float bestDistance = float.MaxValue;
            var origin = transform.position;
            foreach (var candidate in RemotePlayers)
            {
                if (MobilePlatformTarget(candidate)) continue;
                if (!Pursuable(candidate) || !HostileToRemote(this, candidate)) continue;
                float detect = (detectRange > 0f ? detectRange : NpcTables.DetectRange) * M;
                if ((candidate.transform.position - origin).sqrMagnitude > detect * detect) continue;
                bool mobile = MobilePlatformTarget(candidate);
                bool cruiser = candidate.netShipIndex >= 0
                               && ShipClassRules.ClassOf(db?.Ship(candidate.netShipIndex)) == ShipClassRules.Cruiser;
                int priority = mobile ? (cruiser ? 0 : 2) : 1;
                float distance = (candidate.transform.position - origin).sqrMagnitude;
                if (priority > bestPriority || (priority == bestPriority && distance >= bestDistance)) continue;
                best = candidate;
                bestPriority = priority;
                bestDistance = distance;
            }
            if (best == null) return;
            remoteTarget = best;
            target = best;
            targetPos = best.transform.position;
            attacking = true;
            followingWaypoint = false;
        }

        /// <summary>SMEP's acceleration-aware ballistic lead. Target samples are rate-limited and smoothed per fighter.</summary>
        Vector3 SmepAccelerationLead(Vector3 aimPoint, Vector3 from, Vector3 nativeLead)
        {
            if (target == null || !target.Alive) return nativeLead;
            SampleSmepTargetMotion(out var velocity, out var acceleration);
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
                float error = (relative + velocity * time + acceleration * (0.5f * time * time)).magnitude - speed * time;
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
                float error = (relative + velocity * middle + acceleration * (0.5f * middle * middle)).magnitude - speed * middle;
                if (error > 0f) low = middle; else high = middle;
            }
            float result = (low + high) * 0.5f;
            return result > 0f && result <= 6f ? result : 0f;
        }

        /// <summary>SMEP's smooth range/strafe movement, applied after native ES2 steering and before obstacle avoidance.</summary>
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
            float distance = offset.magnitude;
            // Keep the controller and its velocity state alive through pursuit, not just close combat.

            if (smepDogfightState == null || smepDogfightState.target != target)
            {
                float now = Time.time;
                smepDogfightState = new SmepDogfightState
                {
                    target = target,
                    velocity = es2FlownFrame >= Time.frameCount - 1 ? es2Vel / 1000f : transform.forward * Mathf.Max(0f, effectiveTopSpeed),
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
            var away = distance > 0.01f ? offset / distance : transform.rotation * Vector3.back;
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
            if (!combatMotion) state.retreating = false;
            else if (distance <= SmepCloseLimit) state.retreating = true;
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
            Vector3 desired = !combatMotion ? (targetPoint - beforePosition).normalized
                : state.retreating
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
            bool bursting = nowTime < state.burstUntil;
            if (bursting) desired = state.burstDirection;
            if (desired.sqrMagnitude > 1f) desired.Normalize();
            // All thrust directions share the ship's top speed; only acceleration/response may differ.
            float topSpeed = Mathf.Max(0f, effectiveTopSpeed);
            desired *= topSpeed;

            float dt = Mathf.Max(0f, dtMs);
            float responseMs = bursting ? 90f : 240f;
            float blend = 1f - Mathf.Exp(-dt / responseMs);
            // Axis-specific acceleration in ship space; top speed remains shared across directions.
            var localCurrent = transform.InverseTransformDirection(state.velocity);
            var localDesired = transform.InverseTransformDirection(desired);
            float axisStep = topSpeed * blend;
            localCurrent.x = Mathf.MoveTowards(localCurrent.x, localDesired.x, axisStep * 0.65f);
            localCurrent.y = Mathf.MoveTowards(localCurrent.y, localDesired.y, axisStep * 0.55f);
            localCurrent.z = Mathf.MoveTowards(localCurrent.z, localDesired.z, axisStep * (localDesired.z < localCurrent.z ? 0.75f : 1f));
            state.velocity = Vector3.ClampMagnitude(transform.TransformDirection(localCurrent), topSpeed);
            float maxStep = topSpeed * dt * M;
            var step = state.velocity * dt * M;
            bool hardRetreat = state.retreating;
            // Retreat uses the same acceleration-limited velocity as all other movement.

            if (maxStep <= 0f) step = Vector3.zero;
            else if (step.magnitude > maxStep)
                step = hardRetreat ? away * maxStep : step.normalized * maxStep;
            transform.position = beforePosition + step;
            // Keep ES2's fallback velocity in sync with the authoritative SMEP displacement.
            es2Vel = dt > 0f && M > 0f ? step / (dt * M) * 1000f : Vector3.zero;
            es2FlownFrame = Time.frameCount;
        }

        void SyncSmepAfterAvoidance(Vector3 positionBeforeAvoidance, float dtMs)
        {
            if (!SmepEligible || smepDogfightState == null || dtMs <= 0f || M <= 0f) return;
            // Avoidance displacement is authoritative, including its collision response.
            var correctedVelocity = (transform.position - positionBeforeAvoidance) / (dtMs * M);
            smepDogfightState.velocity = Vector3.ClampMagnitude(smepDogfightState.velocity + correctedVelocity, Mathf.Max(0f, speed * slow.SpeedFactor * roleSpeedFactor * RolePace));
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
            int steps = Mathf.Clamp(Mathf.CeilToInt(travel.magnitude / 4f), 1, 16);

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

            // Broadphase each NPC collider once against the complete swept path and every player shape.
            smepContactCandidates.Clear();
            for (int i = 0; i < colliders.Count; i++)
            {
                var collider = colliders[i];
                if (collider == null || !HullCollision.Shown(collider)) continue;
                var bounds = collider.bounds;
                var swept = new Bounds(bounds.center - travel * 0.5f, bounds.size + absTravel);
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
