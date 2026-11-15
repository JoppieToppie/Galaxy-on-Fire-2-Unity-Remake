// NpcShipChorus.cs
// NpcShip, part: Chorus's way of fighting (a mod hull's "traffic": { "ai": "chorus" }; Flight.ChorusDogfight, after
// BT_Fighter_CombatRoam). Steer flies at the goal it gives: the target on an approach (x2 speed, firing within 600 m), a
// point on past it on a pass, a break to its side when the target aims at it, a run away below half hull; out of the
// player's view beyond 1.2 km it jumps to 400 m from its target (materializing, HullWarpIn, as Chorus's warp). Of a crowd on
// one player at most ChorusDogfight.MaxAttackers fire at once (Chorus's attack tokens). For fighters only, like the others.
// "chorusScout" breaks also when the target's nose is near it, faster; "chorusGunship" holds 600 m off as a turret;
// "chorusDrone" / "chorusSwarm" keep at range, facing and firing, away when it comes too close.

using GoF2Remake.Data;
using GoF2Remake.Flight;
using UnityEngine;

namespace GoF2Remake.World
{
    public partial class NpcShip
    {
        /// <summary>It fights Chorus's way this frame: attacking a target on its own (no route, order, wingman duty).</summary>
        bool ChorusFighting => chorusFight != null && !IsWingman && attacking && target != null && !followingWaypoint && order == null && mindControlMs <= 0f;

        /// <summary>After the targeting: the mode, and its jump when it has fallen far behind out of view.</summary>
        void UpdateChorus(float dtMs)
        {
            if (chorusFight == null || !ChorusFighting) return;
            if (target != chorusTarget) { chorusTarget = target; chorusFight.NewTarget(); }
            var t = target.transform;
            var toMe = transform.position - t.position;
            float d = toMe.magnitude / M;
            // the target's nose on it, within its firing distance (BTService_CheckForEnemyAttack's question; a scout's
            // CheckForTargetAligned: 25 deg within 500 m)
            bool aimed = d <= chorusFight.AimRange && Vector3.Dot(t.forward, toMe.normalized) > Mathf.Cos(chorusFight.AimCone * Mathf.Deg2Rad);
            bool hidden = false;
            if (PlayerTarget(target) && Camera.main is Camera cam)
            {
                var vp = cam.WorldToViewportPoint(transform.position);
                hidden = vp.z <= 0f || vp.x < 0f || vp.x > 1f || vp.y < 0f || vp.y > 1f;
            }
            if (chorusFight.Step(dtMs, new ChorusDogfight.View { distance = d, aimedAt = aimed, outOfView = hidden, hullRatio = Hp != null ? Hp.HullFraction : 1f }))
                ChorusJump();
        }

        /// <summary>BTTask_Teleport: to TeleportToUnits from the target, off to a side and behind it (out of its view), facing it.</summary>
        void ChorusJump()
        {
            var t = target.transform;
            var off = (-t.forward + t.right * Random.Range(-1f, 1f) + t.up * Random.Range(-0.5f, 0.5f)).normalized;
            transform.position = t.position + off * ChorusDogfight.TeleportToUnits * M;
            transform.rotation = Quaternion.LookRotation(t.position - transform.position, t.up);
            var warp = (db?.Ship(Spec.ship) as CustomShipData)?.warpIn;
            HullWarpIn.Begin(gameObject, warp != null && warp.seconds > 0f ? warp : new CustomWarpIn { seconds = 1f });
        }

        /// <summary>Its goal (Unity space) and whether it holds fire: Steer's aim point by the mode.</summary>
        Vector3 ChorusFightGoal(Vector3 aimPos, out bool hold)
        {
            var t = target.transform.position;
            var me = transform.position;
            var toT = t - me;
            float d = toT.magnitude / M;
            var side = transform.right * chorusFight.Side;
            bool tooMany = PlayerTarget(target) && ChorusAttackers() > ChorusDogfight.MaxAttackers;
            hold = !chorusFight.Fires(d) || tooMany;
            switch (chorusFight.Current)
            {
                case ChorusDogfight.Mode.PassBy:   // on past it: its EQS point (a kiter's: away, 135 deg off the target)
                    return ChorusFleePoint(chorusFight.Kiter ? ChorusDogfight.KiterFleeMinAngle : ChorusDogfight.PassMinAngle);
                case ChorusDogfight.Mode.Evade:   // a hard break to the side
                    return me + (side * 2f + transform.forward).normalized * (ChorusDogfight.FireStartUnits * 0.5f * M);
                case ChorusDogfight.Mode.Retreat:   // away: its EQS point past 60 deg from the target
                    return ChorusFleePoint(ChorusDogfight.RetreatMinAngle);
                case ChorusDogfight.Mode.Hold:   // a gunship's turret mode: where it is, its nose on the target; a kiter's attack
                    if (chorusFight.Kiter)        // position: at its distance from the target on its own side
                    {
                        var fromT = me - t;
                        var dir = fromT.sqrMagnitude > 1e-8f ? fromT.normalized : -transform.forward;
                        var spot = t + dir * (chorusFight.KiterHoldUnits * M);
                        return (spot - me).sqrMagnitude < (chorusFight.KiterHoldUnits * 0.1f * M) * (chorusFight.KiterHoldUnits * 0.1f * M) ? t : spot;
                    }
                    return aimPos;
                default:
                    return aimPos;
            }
        }

        /// <summary>The flee task's point (ChorusDogfight's EQS reading): of 32 points on a sphere round the ship at
        /// FleePointUnits, the best scored by its angle away from the target, its turn from the heading and a random share;
        /// chosen once as the mode starts.</summary>
        Vector3 ChorusFleePoint(float minAngle)
        {
            if (chorusGoalEntry == chorusFight.Entries && chorusGoalMode == chorusFight.Current) return chorusGoal;
            chorusGoalEntry = chorusFight.Entries; chorusGoalMode = chorusFight.Current;
            var me = transform.position;
            var toT = target.transform.position - me;
            var dirT = toT.sqrMagnitude > 1e-8f ? toT.normalized : transform.forward;
            var heading = transform.forward;
            const int n = 32;
            float best = float.MinValue;
            var pick = heading;
            for (int i = 0; i < n; i++)   // (a Fibonacci sphere: evenly spread)
            {
                float y = 1f - 2f * (i + 0.5f) / n, r = Mathf.Sqrt(1f - y * y), a = i * 2.39996323f;
                var dir = new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r);
                float s = ChorusDogfight.FleeScore(Vector3.Angle(dir, dirT), Vector3.Angle(dir, heading), Random.value, minAngle);
                if (s > best) { best = s; pick = dir; }
            }
            chorusGoal = me + pick * (ChorusDogfight.FleePointUnits * M);
            return chorusGoal;
        }

        /// <summary>The ships on Chorus's way attacking this one's target (the attack tokens' count), ranked by their distance to
        /// it: its own place among them (1-based), so the nearest MaxAttackers fire.</summary>
        int ChorusAttackers()
        {
            if (traffic == null) return 1;
            var t = target.transform.position;
            float mine = (transform.position - t).sqrMagnitude;
            int place = 1;
            foreach (var s in traffic.Ships)
            {
                if (s == null || s == this || s.Gone || s.chorusFight == null || !s.attacking || s.target != target || s.Current != State.Fly) continue;
                if ((s.transform.position - t).sqrMagnitude < mine) place++;
            }
            return place;
        }
    }
}
