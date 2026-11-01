// NpcShipSteering.cs
// Part of NpcShip: movement: the shock blast's push, aiming and steering (fire, turn, bank, move), the event graphs'
// scripted orders (NpcOrder), the roll, the boost, avoidance and the jump out.

using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.Visuals;
using UnityEngine;

namespace GoF2Remake.World
{
    public partial class NpcShip
    {
        /// <summary>The shock blast: 'center' and 'radius' in Unity metres.</summary>
        public void InitPush(Vector3 center, float radius)
        {
            // KIPlayer::initPush is empty and PlayerTurret inherits it; only PlayerFighter / PlayerAsteroid push. The turret
            // assemblies on battleships and carriers stay mounted.
            if (IsFixed || IsFreighter || IsTurret || radius <= 0f) return;
            var away = transform.position - center;
            float d = away.magnitude;
            pushTotalMs = pushMs = (1f - Mathf.Min(d / radius, 1f)) * PushMaxMs;
            pushDir = d > 1e-3f ? away / d : Random.onUnitSphere;
            pushTumble = Random.onUnitSphere * 0.2f;
        }

        /// <summary>PlayerFighter::push 0xf3c50 (checked in the disassembly): f = the time left / T; each frame the ship moves
        /// dir x dt x its speed (+0x1e0) x 3 (2 - f) x T / 5000 (a full push 6 -> 12 u/ms) and turns by f x the tumble
        /// (a random unit vector x 0.2 rad per original frame).</summary>
        void UpdatePush(float dtMs)
        {
            if (pushMs <= 0f) return;
            pushMs -= dtMs;
            float T = Mathf.Max(1f, pushTotalMs);
            float f = Mathf.Max(0f, pushMs) / T;
            transform.position += pushDir * (dtMs * speed * 3f * (2f - f) * (T / PushMaxMs)) * M;
            transform.Rotate(pushTumble.normalized, f * pushTumble.magnitude * Mathf.Rad2Deg * dtMs / OriginalFrame.Ms, Space.World);
        }

        Vector3 AimAt(Target t, float dtMs)
        {
            if (t != aimFor || (aimMs -= dtMs) <= 0f)
            {
                aimFor = t;
                aimSpot = UnityEngine.Random.Range(0, int.MaxValue);
                aimMs = UnityEngine.Random.Range(1500f, 3500f);
            }
            return t.AimSpot(aimSpot);
        }

        /// <summary>§5.4 fire, turn, bank, move.</summary>
        void Steer(float dtMs)
        {
            // Turned and fired at a spot on the target's model (AimAt); the break-off range measured to its centre.
            var aimPos = attacking && !followingWaypoint && target != null ? AimAt(target, dtMs) : targetPos;
            var dir = aimPos - transform.position;
            // PlayerFighter::update's fly state: the player docked at an object (PlayerEgo::isDockedToDockingPoint) is circled
            // at +-12000 instead of +-8000, and not fired at while it is above the ship (0xf2cfa).
            bool dockedPlayer = target != null && target.isPlayer && ObjectDocking.PlayerDocked;
            if (target != null && !followingWaypoint)
            {
                float r = (dockedPlayer ? 12000f : NpcTables.BreakOffRange) * M;
                var toCentre = targetPos - transform.position;
                if (Mathf.Abs(toCentre.x) < r && Mathf.Abs(toCentre.y) < r && Mathf.Abs(toCentre.z) < r) dir = transform.right;
            }
            if (dir.sqrMagnitude < 1e-8f) dir = transform.forward;
            var dirN = dir.normalized;
            var local = transform.InverseTransformDirection(dirN);

            if (attacking && !followingWaypoint && target != null)
            {
                if (target.untargetable || target.cloaked) attacking = false;   // a cloaked ship: chased, never fired at
                else
                {
                    var d = aimPos - transform.position;
                    float fr = NpcTables.FireRange * M;
                    if (Mathf.Abs(local.x) < NpcTables.FireCone && Mathf.Abs(local.y) < NpcTables.FireCone
                        && Mathf.Abs(d.x) < fr && Mathf.Abs(d.y) < fr && Mathf.Abs(d.z) < fr)
                    {
                        var firing = useEmp && empGun != null ? empGun : useSecond && secondGun != null ? secondGun : gun;
                        if (firing == null || !target.Targetable) attacking = false;
                        // PlayerFighter::update's fire block checks the target's cloak (+0x5e), not its own: a cloaked Specter
                        // fires (handleCloaking only sets the look and the radar flag); a script's hidden ship doesn't.
                        else if (shootingEnabled && !HoldFire && !radarHidden && !(dockedPlayer && target.transform.position.y > transform.position.y)
                                 && firing.TryFire(transform) >= 0)
                        {
                            int shot = NpcTables.ShotSound(Race);
                            // The rigs' own fx (WeaponFx.Load was a formatted path and a Resources lookup per shot).
                            var clip = firing == empGun ? (empRig?.fx ?? WeaponFx.Load(18))?.Shot
                                     : firing == secondGun ? (secondRig?.fx ?? WeaponFx.Load(secondGun.itemIndex))?.Shot
                                     : assets != null && assets.shots != null && shot < assets.shots.Length ? assets.shots[shot] : null;
                            if (clip != null) ShotVoices.PlayAt(clip, transform.position, 0.8f);   // shared voices (FEV max_playbacks)
                        }
                    }
                }
            }

            var fwd = transform.forward;
            // LevelScript::process: during the launch / arrival camera every wingman drifts (KIPlayer+0x129, no steering).
            bool noSteer = drift || (IsWingman && traffic != null && traffic.LaunchCameraRunning != null && traffic.LaunchCameraRunning());
            if (!noSteer && !Hp.empDisabled)
            {
                var delta = dirN - fwd;
                var h = delta.sqrMagnitude > 1e-10f ? (fwd + delta.normalized * (dtMs * 48f / 65536f)).normalized : dirN;
                var diff = h - dirN;
                if (Mathf.Abs(diff.x) + Mathf.Abs(diff.y) + Mathf.Abs(diff.z) < 0.0625f) h = dirN;
                float a = Mathf.Acos(Mathf.Clamp(Vector3.Dot(fwd, h), -1f, 1f));
                if (Vector3.Dot(transform.right, h) > 0f) a = -a;
                turnRing[ringIndex] = a * OriginalFrame.Ms / dtMs;   // per original frame, like the original's samples
                ringIndex = (ringIndex + 1) % turnRing.Length;
                if (ringFilled < turnRing.Length) ringFilled++;
                float avg = 0f;
                for (int i = 0; i < ringFilled; i++) avg += turnRing[i];
                avg /= ringFilled;
                bankTarget = Mathf.Clamp(avg * 750f * 15.139f, -750f, 750f);
                transform.rotation = Quaternion.LookRotation(h, transform.up);
            }
            else { bankTarget = 0f; ringFilled = 0; }

            // Bank toward the target at dt * 1.25 / 3.9 per ms; after 750 ms at the target the roll levels out.
            float step = dtMs * 1.25f / 3.9f;
            float before = bank;
            bank = Mathf.MoveTowards(bank, bankTarget, step);
            if (Mathf.Approximately(bank, before)) { levelTimer -= dtMs; if (levelTimer <= 0f) levelling = true; }
            else levelTimer = 750f;
            if (model != null) model.localRotation = Quaternion.AngleAxis(bank * Mathf.PI / 4096f * Mathf.Rad2Deg, Vector3.forward);
            if (levelling) Roll(dtMs);

            if (!Hp.empDisabled) transform.position += transform.forward * speed * dtMs * M;
        }

        /// <summary>Gives the ship an order (null: back to its own AI). Wakes a sleeper, lets a parked ship fly; "hold" is
        /// SetHold, "jump" JumpOut.</summary>
        public void SetOrder(NpcOrder o)
        {
            order = o;
            orderMs = 0f;
            leaderSeen = false;
            speed = baseSpeed;
            if (o == null) return;
            if (Asleep) Wake();
            parked = frozen = false;
            scriptedSpeed = -1f;
        }

        /// <summary>Holds still where it is (a target that neither flies nor shoots) until another order or "resume".</summary>
        public void SetHold() { order = null; parked = true; }

        /// <summary>Back to its own AI (its route, its targets), flying.</summary>
        public void Resume() { order = null; parked = false; speed = baseSpeed; }

        /// <summary>State 6: accelerates away and is gone (the jumpers' exit). Nobody's kill (an event counts it as gone).</summary>
        public void JumpOut()
        {
            order = null;
            parked = false;
            Target.killedByNpc = true;
            if (Current == State.Fly) Current = State.JumpingOut;
        }

        /// <summary>Gone without a fight (an order's dock / vanish): nobody's kill.</summary>
        void LeaveQuietly()
        {
            order = null;
            Target.killedByNpc = true;
            Vanish();
        }

        void FinishOrder()
        {
            var then = order != null ? order.then : NpcOrder.Then.Resume;
            order = null;
            speed = baseSpeed;
            switch (then)
            {
                case NpcOrder.Then.Hold: parked = true; break;
                case NpcOrder.Then.Vanish: LeaveQuietly(); break;
                case NpcOrder.Then.Jump: JumpOut(); break;
            }
        }

        /// <summary>One frame of the order; false = nothing done (the order ended: the ship's own update goes on).</summary>
        bool UpdateOrder(float dtMs)
        {
            orderMs += dtMs;
            var o = order;
            var pos = transform.position;
            float own = IsFreighter ? (Spec.speed > 0f ? Spec.speed : NpcTables.FreighterSpeed) : baseSpeed;
            float fly = o.speed > 0f ? o.speed : own;
            Vector3 goal;
            switch (o.kind)
            {
                case NpcOrder.Kind.Move:
                {
                    if (o.points.Count == 0) { FinishOrder(); return false; }
                    goal = o.points[Mathf.Clamp(o.index, 0, o.points.Count - 1)];
                    if ((goal - pos).sqrMagnitude < (o.radius * M) * (o.radius * M))
                    {
                        o.index++;
                        if (o.index >= o.points.Count)
                        {
                            if (!o.loop) { FinishOrder(); return order != null || parked; }
                            o.index = 0;
                        }
                        goal = o.points[o.index];
                    }
                    break;
                }
                case NpcOrder.Kind.Follow:
                {
                    var leader = o.leader;
                    if (leader == null || !leader.gameObject.activeInHierarchy) { FinishOrder(); return false; }
                    var slot = leader.position + leader.rotation * (o.offset * M);
                    // The leader's speed (units / ms) from its movement; aim ahead along its heading so the ship lines up with
                    // it, and close the gap along that heading by the speed.
                    float leaderSpeed = leaderSeen && dtMs > 0f ? (leader.position - leaderLast).magnitude / M / dtMs : own;
                    leaderLast = leader.position;
                    leaderSeen = true;
                    var lead = leader.forward;
                    goal = slot + lead * (3000f * M);
                    float behind = Vector3.Dot(slot - pos, lead) / M;   // units the ship trails its slot by
                    float far = (slot - pos).magnitude / M;
                    fly = far > 6000f ? Mathf.Max(own, NpcTables.BoostSpeed)
                        : Mathf.Clamp(leaderSpeed + behind * 0.002f, 0.2f, Mathf.Max(NpcTables.BoostSpeed, leaderSpeed * 1.5f));
                    if (far > 6000f) goal = slot;
                    break;
                }
                case NpcOrder.Kind.Attack:
                {
                    var t = o.target;
                    if (t == null || !t.Alive || !t.gameObject.activeInHierarchy) { FinishOrder(); return false; }
                    target = t;
                    targetPos = t.transform.position;
                    attacking = true;
                    followingWaypoint = false;
                    drift = false;
                    // Modified: not while it boosts (UpdateBoost's panic after losing 40 % of its hull, or its 5 % roll):
                    // set back to the order's speed every frame, the boost never built up and an attacking ship never fled.
                    if (!boosting) speed = fly;
                    UpdateBoost(dtMs);
                    Steer(dtMs);
                    Avoid(true, dtMs);
                    Avoid(false, dtMs);
                    return true;
                }
                case NpcOrder.Kind.Flee:
                {
                    if (orderMs >= o.seconds * 1000f) { order = null; JumpOut(); return Current == State.JumpingOut; }
                    var away = pos - o.from;
                    if (away.sqrMagnitude < 1e-6f) away = transform.forward;
                    goal = pos + away.normalized * (100000f * M);
                    fly = Mathf.Max(fly, NpcTables.BoostSpeed);
                    break;
                }
                case NpcOrder.Kind.Dock:
                {
                    goal = Vector3.zero;   // the station sits at the orbit's origin
                    bool inside = false;
                    foreach (var ob in Obstacle.All)
                        if (ob != null && ob.landmark && ob.Active && ob.Touches(pos, out _)) { inside = true; break; }
                    if (inside || pos.sqrMagnitude < (o.radius * M) * (o.radius * M)) { LeaveQuietly(); return true; }
                    break;
                }
                default:
                    FinishOrder();
                    return false;
            }
            // An escort fights what it meets (its own targeting), then goes on with the order.
            if (o.fight && !IsFreighter && gun != null)
            {
                UpdateTargeting();
                if (attacking && target != null && !followingWaypoint)
                {
                    UpdateBoost(dtMs);
                    Steer(dtMs);
                    Avoid(true, dtMs);
                    Avoid(false, dtMs);
                    return true;
                }
            }
            target = null;
            attacking = false;
            followingWaypoint = true;
            drift = false;   // the AI's 5 s "drift" (no steering) left over from its own targeting
            targetPos = goal;
            // Remake: slower while facing away from the goal, so the turn (the original's fixed turn rate: a circle of
            // speed / 0.00073 units) stays tight; full speed once lined up.
            var toGoal = goal - pos;
            float align = toGoal.sqrMagnitude > 1e-6f ? Vector3.Dot(transform.forward, toGoal.normalized) : 1f;
            fly = Mathf.Max(Mathf.Min(fly, 0.6f), fly * Mathf.Lerp(0.35f, 1f, Mathf.Clamp01((align + 0.2f) / 0.9f)));
            speed = fly;
            Steer(dtMs);
            if (o.kind != NpcOrder.Kind.Dock) Avoid(true, dtMs);   // docking flies into the station on purpose
            Avoid(false, dtMs);
            return true;
        }

        /// <summary>PlayerFighter::roll: brings right.y to 0 with up.y > 0 at 0.00075 rad/ms (0.00025 near level).</summary>
        void Roll(float dtMs)
        {
            float rx = transform.right.y, uy = transform.up.y;
            if (Mathf.Abs(rx) < 0.015f && uy > 0f) { levelling = false; return; }
            float rate = uy < 0f || Mathf.Abs(rx) > 0.3f ? 0.00075f : 0.00025f;
            if (rx >= 0f) rate = -rate;
            transform.Rotate(0f, 0f, rate * Mathf.Min(dtMs, 60f) * Mathf.Rad2Deg, Space.Self);
        }

        /// <summary>§5.5 boost and panic.</summary>
        void UpdateBoost(float dtMs)
        {
            float frames = dtMs / OriginalFrame.Ms;
            if (Hp.hull < lastHull)
            {
                damageSinceBoost += lastHull - Hp.hull;
                lastHull = Hp.hull;
                if (damageSinceBoost >= 0.4f * Hp.maxHull) { damageSinceBoost = 0; boostTimer = 10000f; panic = true; }
            }
            if (Spec.speed > 0f || evasionOff) return;   // a fixed-speed ship (setSpeed) / a wingman after "Fire at will" never boosts
            if (boostTimer > 5000f && !boosting)
            {
                boostTimer = 0f;
                if (panic || Random.Range(0, 100) < 5)
                {
                    boostDuration = 5000f + Random.Range(0, 3000);
                    boosting = true;
                    targetSpeed = NpcTables.BoostSpeed;
                }
            }
            if (!boosting) return;
            if (boostTimer > boostDuration) { boostTimer = 0f; panic = false; targetSpeed = NpcTables.BaseSpeed; }
            if (targetSpeed <= 0f) return;
            speed *= Mathf.Pow(speed < targetSpeed ? 1.05f : 0.95f, frames);
            if (speed >= NpcTables.BoostSpeed || speed < NpcTables.BaseSpeed)
            {
                speed = targetSpeed;
                if (Mathf.Approximately(targetSpeed, NpcTables.BaseSpeed)) { boosting = false; targetSpeed = 0f; }
            }
        }

        /// <summary>§5.7 (PlayerFighter::update, +0x13a): the first landmark / ship whose volumes contain the fighter turns it
        /// away (direction += (away - fwd) * speed * 0.03, up = world up) and moves it one extra step.</summary>
        void Avoid(bool landmarks, float dtMs)
        {
            var all = Obstacle.All;
            var pos = transform.position;
            int frame = Time.frameCount;
            for (int i = 0; i < all.Count; i++)
            {
                var o = all[i];
                // Obstacle.All holds only enabled obstacles; MayTouch is a distance test against a sphere measured once a frame.
                if ((object)o == null || o.landmark != landmarks || !o.MayTouch(pos, frame)) continue;
                if (!o.Touches(pos, out int index)) continue;
                var p = o.ProjectionVector(pos, index);
                if (p == Vector3.zero) continue;
                var fwd = transform.forward;
                var dir = (fwd + (p - fwd) * speed * 0.03f).normalized;
                transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
                transform.position += transform.forward * speed * dtMs * M;
                return;
            }
        }

        /// <summary>State 6: x1.1 per original frame, gone above 100 u/ms.</summary>
        void UpdateJumpOut(float dtMs)
        {
            speed *= Mathf.Pow(1.1f, dtMs / OriginalFrame.Ms);
            transform.position += transform.forward * speed * dtMs * M;
            if (speed > 100f) SetDead();
        }
    }
}
