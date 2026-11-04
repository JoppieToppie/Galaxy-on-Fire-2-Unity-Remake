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
// The aim point: the target's position + its heading * 1500 units (the lead both use).

using System;
using System.Collections.Generic;
using UnityEngine;

namespace GoF2Remake.Flight
{
    public class TurretTargeting
    {
        const float M = Gun.MetersPerUnit;
        public const float PickMs = 3000f, LeadUnits = 1500f;

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

        /// <summary>The player's lead point: where the target will be (its position + heading * 1500 units).</summary>
        public static Vector3 LeadPoint(Target t) => t.transform.position + t.transform.forward * LeadUnits * M;

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
