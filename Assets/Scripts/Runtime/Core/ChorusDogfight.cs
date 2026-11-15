// ChorusDogfight.cs
// Remake mods (2026-11-11, a mod hull's "traffic": { "ai": "chorus" }; the Chorus Ships mod's): an NPC fighter fights the way
// Chorus's fighters do (BP_NPC_Ship_Fighter's BT_Fighter_CombatRoam, its tasks' and decorators' values). World.NpcShip
// steers by it (NpcShipChorus):
//   Approach   BTTask_AttackApproach: at the target at LinearSpeedMultiplier 2, firing from FiringStartDistance (600 m) in,
//              for TaskDuration 10 s or until ClosestDistance (50 m), then a PassBy
//   PassBy     BTTask_Flee with EQQuery_PassByPosition: on past the target for 4 s, then the next approach after a
//              BTDecorator_Cooldown (2 s)
//   Evade      BTService_CheckForEnemyAttack (every 0.1 s, NeedsEvadeManeuver): the target aims at it: a 2 s break to its
//              side (BTTask_Flee 2 s), at most every 3 s (BTDecorator_Cooldown 3)
//   Retreat    BTDecorator_CompareHealthPercent < 0.5: a 5 s flight away (BTTask_Flee_14), at most every 15 s
//   Teleport   BTDecorator_CheckForOutOfView (2 s) with CheckInDistance beyond 1200 m: it jumps to 400 m from the target
//              (BTTask_Teleport TeleportToDistance), at most every 15 s
// Two more kinds (2026-11-11, "ai": "chorusScout" / "chorusGunship"):
//   Scout      BT_Scout_CombatRoam: the fighter's, its break also when the target's nose is within 25 deg of it within
//              500 m (BTService_CheckForTargetAligned TargetAimingAngle / TargetDist: bAlignedToPlayer), at x1.5 (the
//              "Very Challenging Flee", BTTask_Flee_4's LinearSpeedMultiplier), every 4 s at most (BTDecorator_Cooldown 4)
//   Gunship    BT_Gunship_CombatRoam_New: flies at the target at x2 until 600 m off (BTTask_FlyToTarget TargetReachedDistance
//              60000, LinearSpeedMultiplier 2), then holds there facing it and firing (BTT_TurretMode_Gunship,
//              BTTask_LookAtTarget, BTTask_AIAttackTargetWithWeapon) until it is past 800 m (BTDecorator_CompareDistance
//              80000): no break, run away or jump (its tree has none; the line-of-sight repositioning is left out)
// Two kiters (2026-11-11, "ai": "chorusDrone" / "chorusSwarm"): in to their attack distance (BTTask_FlyToTarget
// TargetReachedDistance), holding an attack position there facing the target and firing (BTTask_Flee "AttackPosition" at
// DistanceOfTargetPoint, BTTask_LookAtWithoutMercuna, BTTask_AIAttackTargetWithWeapon), away when it comes too close
// (BTDecorator_CompareDistance, BTTask_Flee MinAngleToTarget 135 x2):
//   Drone      BT_CombatRoam_OmenDrone: in at x2 to 200 m, its positions at 400 m (x0.75), away within 100 m; the
//              fighters' jump (out of view beyond 1.2 km)
//   Swarm      BT_BatSwarm_CombatRoam: in at x1.5 to 400 m, positions at 400 m, away within 150 m; below half its hull
//              the "Very Challenging Flee" (x2), every 25 s at most (BTDecorator_Cooldown 25); no jump
// Its distances are Chorus's centimetres x 0.72 game units (the Chorus ships' scale). The attack tokens (Gen1 / Gen11 / Gen14:
// how many may attack at once, the AI director's) are the remake's: at most MaxAttackers of a crowd on one player fire.
// Plain C#; 'random' gives 0..1.

namespace GoF2Remake.Flight
{
    public class ChorusDogfight
    {
        public enum Mode { Approach, PassBy, Evade, Retreat, Hold }
        public enum Kind { Fighter, Scout, Gunship, Drone, Swarm }

        public const float UnitsPerCm = 0.72f;
        public const float FireStartUnits = 60000f * UnitsPerCm, ClosestUnits = 5000f * UnitsPerCm;
        public const float ApproachMs = 10000f, ApproachSpeed = 2f, PassByMs = 4000f, ApproachCooldownMs = 2000f;
        public const float EvadeMs = 2000f, EvadeCooldownMs = 3000f, RetreatMs = 5000f, RetreatCooldownMs = 15000f, RetreatHull = 0.5f;
        public const float TeleportBeyondUnits = 120000f * UnitsPerCm, TeleportToUnits = 40000f * UnitsPerCm;
        public const float OutOfViewMs = 2000f, TeleportCooldownMs = 15000f;
        public const int MaxAttackers = 3;
        public const float AimConeDegrees = 10f, ScoutAimConeDegrees = 25f, ScoutAimUnits = 50000f * UnitsPerCm;
        public const float ScoutEvadeSpeed = 1.5f, ScoutEvadeCooldownMs = 4000f;
        public const float GunshipHoldUnits = 60000f * UnitsPerCm, GunshipReengageUnits = 80000f * UnitsPerCm, HoldSpeed = 0.1f;
        public const float KiterFleeMs = 3000f, KiterFleeMinAngle = 135f, KiterFleeSpeed = 2f, SwarmRetreatCooldownMs = 25000f;

        /// <summary>A kiter's numbers: where its approach ends, the distance of its attack position, too close (game units), its
        /// approach and hold speeds.</summary>
        public float KiterStopUnits => (Type == Kind.Drone ? 20000f : 40000f) * UnitsPerCm;
        public float KiterHoldUnits => 40000f * UnitsPerCm;
        public float KiterCloseUnits => (Type == Kind.Drone ? 10000f : 15000f) * UnitsPerCm;
        public bool Kiter => Type == Kind.Drone || Type == Kind.Swarm;

        public Kind Type { get; }
        /// <summary>How many times it has entered a mode (a new pass or run away asks for a new EQS point).</summary>
        public int Entries { get; private set; }

        // EQQuery_PassByPosition (EQQuery_FindFleePosition_2): points on spheres round the ship at the flee task's
        // DistanceOfTargetPoint (75000), scored ExclusionAngle x5 (away from the target past MinAngleToTarget: the pass 0, the
        // run away BTTask_Flee_14's 60), FleeDirection x10 (a turn within FleeDirectionAngleChange of its heading; the task's
        // range isn't in the assets: 0 - 60 deg here), SetPredefinedScores x2 (a random share); InCombatArea and
        // NearGeometry (the orbit has no combat area or level geometry in the remake) left out. The two native tests' curves
        // are the remake's reading: linear in the angle.
        public const float FleePointUnits = 75000f * UnitsPerCm, PassMinAngle = 0f, RetreatMinAngle = 60f, TurnMaxAngle = 60f;
        public const float ExclusionWeight = 5f, DirectionWeight = 10f, RandomWeight = 2f;

        /// <summary>One candidate's EQS score: 'awayDeg' its angle from the direction to the target, 'turnDeg' from its heading,
        /// 'random' 0..1, 'minAngle' the exclusion's.</summary>
        public static float FleeScore(float awayDeg, float turnDeg, float random, float minAngle)
        {
            float excl = awayDeg >= minAngle ? (awayDeg - minAngle) / System.Math.Max(1f, 180f - minAngle) : 0f;
            float turn = turnDeg <= TurnMaxAngle ? 1f : System.Math.Max(0f, 1f - (turnDeg - TurnMaxAngle) / 45f);
            return ExclusionWeight * excl + DirectionWeight * turn + RandomWeight * random;
        }

        /// <summary>What it sees this step: the target's distance (game units), whether the target's nose is on it, its hull
        /// share, whether it is out of the player's view.</summary>
        public struct View
        {
            public float distance;
            public bool aimedAt, outOfView;
            public float hullRatio;
        }

        public Mode Current { get; private set; } = Mode.Approach;
        /// <summary>The side of its pass, break or retreat (-1 left, 1 right), chosen as it starts.</summary>
        public int Side { get; private set; } = 1;
        float modeMs, waitMs, evadeCdMs, retreatCdMs, teleportCdMs, hiddenMs;
        readonly System.Func<float> random;

        public ChorusDogfight(System.Func<float> random, Kind kind = Kind.Fighter) { this.random = random ?? (() => 0.5f); Type = kind; }

        /// <summary>The kind for a traffic "ai" word: "chorus" a fighter, "chorusScout", "chorusGunship"; null = not Chorus's.</summary>
        public static Kind? KindOf(string ai)
        {
            if (string.IsNullOrEmpty(ai)) return null;
            if (string.Equals(ai, "chorus", System.StringComparison.OrdinalIgnoreCase)) return Kind.Fighter;
            if (string.Equals(ai, "chorusScout", System.StringComparison.OrdinalIgnoreCase)) return Kind.Scout;
            if (string.Equals(ai, "chorusGunship", System.StringComparison.OrdinalIgnoreCase)) return Kind.Gunship;
            if (string.Equals(ai, "chorusDrone", System.StringComparison.OrdinalIgnoreCase)) return Kind.Drone;
            if (string.Equals(ai, "chorusSwarm", System.StringComparison.OrdinalIgnoreCase)) return Kind.Swarm;
            return null;
        }

        /// <summary>Its speed now: x2 on an approach, a scout's break x1.5, all but still while a gunship holds.</summary>
        public float SpeedFactor => Kiter ? (Current == Mode.Approach ? (Type == Kind.Drone ? 2f : 1.5f)
                                             : Current == Mode.Hold ? (Type == Kind.Drone ? 0.75f : 1f) : KiterFleeSpeed)
            : Current == Mode.Hold ? HoldSpeed
            : Current == Mode.Approach && waitMs <= 0f ? ApproachSpeed
            : Current == Mode.Evade && Type == Kind.Scout ? ScoutEvadeSpeed : 1f;

        /// <summary>The target's nose counts as on it within this cone (degrees) and distance (units).</summary>
        public float AimCone => Type == Kind.Scout ? ScoutAimConeDegrees : AimConeDegrees;
        public float AimRange => Type == Kind.Scout ? ScoutAimUnits : FireStartUnits;

        /// <summary>It may fire now: on an approach within its firing distance; a gunship while it holds.</summary>
        public bool Fires(float distance) => Kiter ? Current == Mode.Hold && distance <= FireStartUnits
            : Type == Kind.Gunship ? Current == Mode.Hold && distance <= GunshipReengageUnits
            : Current == Mode.Approach && waitMs <= 0f && distance <= FireStartUnits;

        /// <summary>A new target: a fresh approach.</summary>
        public void NewTarget() { Enter(Mode.Approach); waitMs = 0f; hiddenMs = 0f; }

        void Enter(Mode m)
        {
            Entries++;
            Current = m;
            modeMs = 0f;
            Side = random() < 0.5f ? -1 : 1;
        }

        /// <summary>'dtMs' on. Returns true when it should jump to TeleportToUnits from the target now.</summary>
        public bool Step(float dtMs, View v)
        {
            modeMs += dtMs;
            evadeCdMs -= dtMs; retreatCdMs -= dtMs; teleportCdMs -= dtMs; waitMs -= dtMs;
            if (Type == Kind.Gunship)   // (BT_Gunship_CombatRoam_New: in to 600 m, hold, back in past 800 m)
            {
                if (Current != Mode.Hold && v.distance <= GunshipHoldUnits) Enter(Mode.Hold);
                else if (Current == Mode.Hold && v.distance > GunshipReengageUnits) Enter(Mode.Approach);
                else if (Current != Mode.Hold && Current != Mode.Approach) Enter(Mode.Approach);
                return false;
            }
            hiddenMs = v.outOfView && v.distance > TeleportBeyondUnits && Type != Kind.Swarm ? hiddenMs + dtMs : 0f;
            if (hiddenMs >= OutOfViewMs && teleportCdMs <= 0f)
            {
                teleportCdMs = TeleportCooldownMs; hiddenMs = 0f;
                Enter(Mode.Approach); waitMs = 0f;
                return true;
            }
            if (Kiter)   // (in, hold at range, away when too close; the swarm's run away below half its hull)
            {
                if (Type == Kind.Swarm && v.hullRatio < RetreatHull && retreatCdMs <= 0f && Current != Mode.Retreat)
                {
                    retreatCdMs = SwarmRetreatCooldownMs;
                    Enter(Mode.Retreat);
                    return false;
                }
                switch (Current)
                {
                    case Mode.Approach: if (v.distance <= KiterStopUnits) Enter(Mode.Hold); break;
                    case Mode.Hold:
                        if (v.distance < KiterCloseUnits) Enter(Mode.PassBy);
                        else if (v.distance > KiterHoldUnits * 2f) Enter(Mode.Approach);
                        break;
                    case Mode.PassBy: if (modeMs >= KiterFleeMs) Enter(Mode.Approach); break;
                    case Mode.Retreat: if (modeMs >= RetreatMs) Enter(Mode.Approach); break;
                    default: Enter(Mode.Approach); break;
                }
                return false;
            }
            if (v.hullRatio < RetreatHull && retreatCdMs <= 0f && Current != Mode.Retreat)
            {
                retreatCdMs = RetreatCooldownMs;
                Enter(Mode.Retreat);
                return false;
            }
            if (v.aimedAt && evadeCdMs <= 0f && (Current == Mode.Approach || Current == Mode.PassBy))
            {
                evadeCdMs = Type == Kind.Scout ? ScoutEvadeCooldownMs : EvadeCooldownMs;
                Enter(Mode.Evade);
                return false;
            }
            switch (Current)
            {
                case Mode.Approach:
                    if (waitMs <= 0f && (modeMs >= ApproachMs || v.distance <= ClosestUnits)) Enter(Mode.PassBy);
                    break;
                case Mode.PassBy:
                    if (modeMs >= PassByMs) { Enter(Mode.Approach); waitMs = ApproachCooldownMs; }
                    break;
                case Mode.Evade:
                    if (modeMs >= EvadeMs) Enter(Mode.Approach);
                    break;
                case Mode.Retreat:
                    if (modeMs >= RetreatMs) Enter(Mode.Approach);
                    break;
            }
            return false;
        }
    }
}
