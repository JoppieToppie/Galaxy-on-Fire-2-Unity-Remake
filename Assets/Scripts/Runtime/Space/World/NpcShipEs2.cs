// NpcShipEs2.cs
// NpcShip, part: EVERSPACE 2's aim and way of fighting (remake options, 2026-11-06; es2_ships_importer research/npc_ai.md).
// Aim (Settings.Es2Aiming, Flight.Es2Aim + Es2Director): a fighter's guns go at a reticule that slides onto the lead of an aim
// point taken every T seconds (the target's place + its velocity x T + a smooth miss); T by the difficulty against players.
// Steer turns the ship at the reticule and fires when the nose is on it (GoF2's fixed guns: ES2's swivel within a cone); of
// a crowd on one player only about 6 shoot at a time. For every fighter and wingman, not freighters, turrets, fixed objects
// or capital ships.
// Fighting (Settings.NpcDogfight "ES2 for all", or a hull's "traffic": { "ai": "es2" }; Flight.Es2Dogfight): runs past the
// target, gaining distance (orbiting, zig-zagging away), evading when tailed (zig-zag, barrel roll, turn and fight), fleeing
// once below 20 % hull. ES2's ships turn their guns while flying beside the target; GoF2's fire along the nose, so a run
// flies at the reticule until it is within 1.25 x the arrival distance, then swerves to its point beside the target, and
// the guns fire whenever the nose happens to be on the reticule (never in a barrel roll). Same exclusions as GoF3's
// (NpcShipDogfight); GoF2's circling inside the break-off box is off while it fights so.

using GoF2Remake.Data;
using GoF2Remake.Flight;
using UnityEngine;

namespace GoF2Remake.World
{
    public partial class NpcShip
    {
        const float Es2CountMs = 500f, Es2LegMs = 3000f, Es2SwerveFactor = 1.25f, Es2LoiterM = 200f;

        /// <summary>ES2's aim for fighters and wingmen while the option is on (not freighters, turrets, objects, capital ships).</summary>
        void SetupEs2Aim(SpawnSpec spec)
        {
            es2Aim = null;
            es2AimFor = null;
            if (spec.freighter || spec.fixedObject != null || spec.turretAssembly != null || spec.capital != 0 || spec.capitalPart || spec.modCapital) return;
            if (!Settings.Es2Aiming) return;
            es2Aim = new Es2Aim();
            es2Seed = Random.Range(int.MinValue, int.MaxValue);
        }

        /// <summary>ES2's way of fighting (SetupDogfight picks it): its roll chance by the difficulty.</summary>
        void SetupEs2Fight() => es2Fight = new Es2Dogfight(Es2Dogfight.RollChanceFor(Session.Difficulty), () => Random.value);

        /// <summary>It fights ES2's way this frame: attacking a target on its own (no route, order, wingman duty).</summary>
        bool Es2Fighting => es2Fight != null && !IsWingman && attacking && target != null && !followingWaypoint && order == null && mindControlMs <= 0f;

        bool Es2Aiming => es2Aim != null && attacking && !followingWaypoint && target != null;

        static bool PlayerTarget(Target t) => t != null && (t.isPlayer || t.netPilot);

        /// <summary>After the targeting (UpdateDogfight): the target's motion when GoF3's part doesn't track it, the attackers on
        /// a player target, the fight's mode.</summary>
        void UpdateEs2(float dtMs)
        {
            if (es2Aim == null && es2Fight == null) return;
            if (dogfight == null) TrackTarget(dtMs);
            if (es2Aim != null && PlayerTarget(target) && (es2CountMs -= dtMs) <= 0f)
            {
                es2CountMs = Es2CountMs;
                es2Attackers = 0;
                if (traffic != null)
                    foreach (var s in traffic.Ships)
                        if (s != null && !s.Gone && s.attacking && s.target == target && s.Current == State.Fly) es2Attackers++;
            }
            if (es2Fight == null || !Es2Fighting) return;
            var t = target.transform;
            var toTarget = t.position - transform.position;
            var goal = Es2RunPoint();
            bool tailed = Es2Dogfight.IsTailed(toTarget.x, toTarget.y, toTarget.z, transform.forward.x, transform.forward.y, transform.forward.z,
                                              t.forward.x, t.forward.y, t.forward.z, Target.radius);
            es2Fight.Step(dtMs, new Es2Dogfight.View
            {
                distance = toTarget.magnitude / M,
                arrived = (goal - transform.position).sqrMagnitude < Sq(Es2Dogfight.AttackArriveM * Es2Dogfight.UnitsPerMetre * M),
                tailed = tailed,
                hullRatio = Hp != null ? Hp.HullFraction : 1f,
            });
        }

        static float Sq(float x) => x * x;

        /// <summary>The target's smoothed velocity (metres a ms) for the leads, as GoF3's part keeps it.</summary>
        void TrackTarget(float dtMs)
        {
            if (target != dogTarget)
            {
                dogTarget = target;
                dogTargetVel = Vector3.zero;
                if (target != null) dogTargetLast = target.transform.position;
                es2Fight?.NewTarget();
            }
            else if (target != null && dtMs > 0f)
            {
                var p = target.transform.position;
                dogTargetVel = Vector3.Lerp(dogTargetVel, (p - dogTargetLast) / dtMs, 0.2f);
                dogTargetLast = p;
            }
        }

        /// <summary>The reticule (Unity space) for the aim spot 'spot' on the target: a new aim point every T, the lead for it, the
        /// reticule eased onto that lead.</summary>
        Vector3 Es2Reticule(Vector3 spot, float dtMs)
        {
            bool vsPlayer = PlayerTarget(target);
            var row = Role == NpcRoles.Role.Sniper ? Es2Aim.Sniper : Es2Aim.Projectile;
            var pos = transform.position;
            bool fresh = target != es2AimFor;
            if (fresh) { es2AimFor = target; es2Aim.Reset(); }
            if (es2Aim.Due)
            {
                float T = Es2Aim.InertiaS(row, vsPlayer, Session.Difficulty);
                var p = spot + dogTargetVel * (T * 1000f);   // where it will be when the reticule gets there
                float amp = Es2Aim.NoiseAmplitude(row, vsPlayer, Vector3.Distance(pos, p));
                if (amp > 0f)
                {
                    double now = Time.timeAsDouble;
                    p += new Vector3(Es2Aim.Noise(now, row.wavelengthS, es2Seed), Es2Aim.Noise(now, row.wavelengthS, es2Seed + 1),
                                     Es2Aim.Noise(now, row.wavelengthS, es2Seed + 2)) * amp;
                }
                es2AimPoint = p;
                es2Aim.Begin(T);
                if (fresh) es2Reticule = Es2Lead(es2AimPoint, pos);
            }
            float a = es2Aim.Blend(dtMs / 1000f);
            es2Reticule = Vector3.Lerp(es2Reticule, Es2Lead(es2AimPoint, pos), a);
            return es2Reticule;
        }

        /// <summary>AWeaponBase::CalcLeadingReticuleInWorld's role: where a shot from 'from' meets the aim point moving on.</summary>
        Vector3 Es2Lead(Vector3 aimPoint, Vector3 from)
        {
            var rel = aimPoint - from;
            float speed = Mathf.Max(0.1f, gunSpeed) * M;   // metres a ms
            var v = dogTargetVel;
            float ms = Es2Aim.InterceptTime(rel.x, rel.y, rel.z, v.x, v.y, v.z, speed);
            return aimPoint + v * Mathf.Min(ms, 20000f);
        }

        /// <summary>ES2's director: this ship holds fire now, one of a crowd on a player.</summary>
        bool Es2Suppressed => es2Aim != null && PlayerTarget(target) && Es2Director.HoldsFire(es2Attackers, Time.timeAsDouble, es2Seed ^ 0x5bd1e995);

        /// <summary>The run's point beside the target (Unity space): its offset off the line to it at the run's angle.</summary>
        Vector3 Es2RunPoint()
        {
            var t = target.transform.position;
            var los = t - transform.position;
            if (los.sqrMagnitude < 1e-6f) los = transform.forward;
            var n = los.normalized;
            var side = Vector3.Cross(n, Mathf.Abs(Vector3.Dot(n, Vector3.up)) > 0.95f ? Vector3.right : Vector3.up).normalized;
            var off = Quaternion.AngleAxis(es2Fight.OffsetAngle * Mathf.Rad2Deg, n) * side;
            return t + off * (es2Fight.OffsetUnits * M);
        }

        /// <summary>Steer's point to fly at (Unity space) for ES2's mode, given the reticule; whether to hold fire.</summary>
        Vector3 Es2FightGoal(Vector3 reticule, float dtMs, out bool holdFire)
        {
            holdFire = !es2Fight.Fires;
            var pos = transform.position;
            var t = target.transform.position;
            float U = Es2Dogfight.UnitsPerMetre * M;
            switch (es2Fight.Current)
            {
                case Es2Dogfight.Mode.Attack:
                {
                    // GoF2's guns fire along the nose: at the reticule, swerving to the point beside it for the pass
                    float swerve = Es2Dogfight.AttackArriveM * U * Es2SwerveFactor;
                    return (t - pos).sqrMagnitude > swerve * swerve ? reticule : Es2RunPoint();
                }
                case Es2Dogfight.Mode.TurnAndFight:
                    return reticule;
                case Es2Dogfight.Mode.Orbit:
                {
                    var flat = pos - t;
                    flat.y = 0f;
                    if (flat.sqrMagnitude < 1e-6f) flat = -transform.forward;
                    var dir = Quaternion.AngleAxis(es2Fight.Side * Es2Dogfight.OrbitStepDeg, Vector3.up) * flat.normalized;
                    return t + dir * (Es2Dogfight.OrbitRadiusM * U);
                }
                case Es2Dogfight.Mode.BarrelRoll:
                    return pos + transform.forward * 1000f;   // straight on through the roll
                case Es2Dogfight.Mode.FleeIdle:
                    if (es2Fight.NewLeg || (es2LegMs -= dtMs) <= 0f || (es2Leg - pos).sqrMagnitude < Sq(Es2LoiterM * U * 0.25f))
                    {
                        es2Fight.NewLeg = false;
                        es2LegMs = Es2LegMs * 2f;
                        es2Leg = pos + Random.insideUnitSphere * (Es2LoiterM * U);
                    }
                    return es2Leg;
                default:   // FlyAway, Zigzag, Flee: zig-zag legs away from the target (BTT_FlyAwayFrom)
                {
                    var (min, max, dev) = es2Fight.Leg;
                    if (es2Fight.NewLeg || (es2LegMs -= dtMs) <= 0f || (es2Leg - pos).sqrMagnitude < Sq(min * M * 0.25f))
                    {
                        es2Fight.NewLeg = false;
                        es2LegMs = Es2LegMs;
                        var away = pos - t;
                        if (away.sqrMagnitude < 1e-6f) away = transform.forward;
                        var axis = Vector3.Cross(away, Random.onUnitSphere);
                        if (axis.sqrMagnitude < 1e-6f) axis = transform.up;
                        var leg = Quaternion.AngleAxis(Random.Range(0f, dev), axis) * away.normalized;
                        es2Leg = pos + leg * (Random.Range(min, max) * M);
                    }
                    return es2Leg;
                }
            }
        }

        /// <summary>ES2's barrel roll under way (BTT_BarrelRollEvasiveMove: rotation and strafe x 3.5 for 1.5 s): the model's
        /// turn about its nose (degrees) and the side step this frame (Unity metres).</summary>
        float Es2Roll(float dtMs, out Vector3 step)
        {
            step = Vector3.zero;
            if (es2Fight == null || !es2Fight.Rolling) return 0f;
            float p = es2Fight.RollProgress;
            float eased = p * p * (3f - 2f * p);
            step = transform.right * (es2Fight.Side * speed * RollSideStep * dtMs * M * Mathf.Sin(p * Mathf.PI) * 1.57f);
            return -es2Fight.Side * 360f * eased;
        }
    }
}
