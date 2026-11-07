// Gof3Dogfight.cs
// Remake option (2026-11-05, the GoF3 roadmap's G6; Options > Gameplay "NPC dogfighting", Settings.NpcDogfight, a session
// rule): an NPC fighter fights the way Galaxy on Fire 3's do (libgofm.so's AIJob_Fight, AIJob_Tease, AIJob_ChasePrey,
// NPCBankingSystem; data.pdb's AI and ConfigNpc records, a T2-T7 fighter's, Ship_NPC_Terran_Fighter_Argus_T6) instead of
// GoF2's (PlayerFighter::update: straight at the target, circling away inside 8000). World.NpcShip steers by it:
//   Approach  AIJob_AttackApproach: at the target, firing, ApproachMin..MaxMs (GoF3's attackApproachTime up to 13 s), until
//             it is inside the break-off range
//   VeerOff   AIJob_VeerOff: away from the target, a quarter along its heading, VeerOffMin..MaxMs (veerOffTime 5-6 s);
//             IdleChance of the runs (AIJob_Fight's 0.3) end in Idle instead: IdleMs at a distance (AIJob_FightIdle, ~2 s)
//   Tease     AIJob_Tease: close (within TeaseRange) and in front of the target's nose (its forward . toward us > 0.75),
//             every TeaseEveryMs a TeaseChance of flying just ahead of its nose for TeaseMin..MaxMs, holding fire (bait)
//   Chase     AIJob_ChasePrey: behind the target, on its tail at ChaseSpeed x for ChaseMs, firing; ChaseCooldownMs before
//             the next
// The evasive roll (NPCBankingComponent::hit / updateEvade): on a hit, by its chance, a barrel roll to a side
// (ship_animation_barrelroll_anim_left / _right stretched to evadeDuration 1.8 s) that leaves it invulnerable for its first
// 1.5 s (invincibleBuffDuration); RollLockMs before the next. GoF3's chance: evadeChanceBase 0.33 (gunships 1.0) x the
// difficulty's multiplier, capped by it (ConfigNpc: Easy 0, Medium 0.15, Hard 0.25, Boss 0.66).
// The aim (ArmingAgentBehaviorSalvo::calculateInaccuracy_): it leads the target (ECS::aimingPoint) with an error settling
// from 1 to 0 over AimSettleMs (aimingTimeMax 0.5 s) after each new approach.
// GoF3's distances are in its own units; the remake's (game units) take them x DistanceScale, the factor the GoF3 devices
// use (combat-equipment.md "Devices"). Plain C#; 'random' gives 0..1.

namespace GoF2Remake.Flight
{
    public class Gof3Dogfight
    {
        public enum Mode { Approach, VeerOff, Idle, Tease, Chase }

        public const float DistanceScale = 11.4f;
        public const float ApproachMinMs = 8000f, ApproachMaxMs = 13000f, VeerOffMinMs = 5000f, VeerOffMaxMs = 6000f;
        public const float IdleMs = 2000f, IdleChance = 0.3f;
        public const float TeaseMinMs = 4000f, TeaseMaxMs = 6000f, TeaseEveryMs = 10000f, TeaseChance = 0.4f, TeaseFacing = 0.75f;
        public const float TeaseRange = 800f * DistanceScale, TeaseAhead = 180f * DistanceScale, TeaseSide = 95f * DistanceScale;
        public const float ChaseMs = 8000f, ChaseSpeed = 1.6f, ChaseCooldownMs = 15000f, ChaseRange = 1500f * DistanceScale;
        public const float ChaseBehind = 3000f;   // game units: GoF3's 100 behind (x11.4) sat inside a GoF2 hull's hit cube
        public const float RollMs = 1800f, RollInvulnerableMs = 1500f, RollLockMs = 3000f;
        public const float AimSettleMs = 500f;

        readonly System.Func<float> random;
        public readonly float evadeChance;
        /// <summary>-1 left, 1 right of the target's nose: where it teases (one side per fighter).</summary>
        public readonly int teaseSide;
        float modeMs, teaseMs = TeaseEveryMs, chaseCooldownMs, rollMs, rollLockMs, aimMs = AimSettleMs;

        public Mode Current { get; private set; } = Mode.Approach;
        /// <summary>-1 left, 1 right: the side of the roll under way.</summary>
        public int RollSide { get; private set; } = 1;

        public Gof3Dogfight(float evadeChance, System.Func<float> random)
        {
            this.evadeChance = evadeChance;
            this.random = random;
            teaseSide = random() < 0.5f ? -1 : 1;
            modeMs = Range(ApproachMinMs, ApproachMaxMs);
        }

        /// <summary>GoF3's chance of a roll per hit for the remake's difficulty (Session.Difficulty: 0 Easy, 0.5 Normal, 1
        /// Hard, 1.5 Extreme): Easy none, Normal 0.15, Hard 0.25 (GoF3's caps), Extreme the fighters' whole 0.33; a boss
        /// 0.66 on every difficulty but Easy.</summary>
        public static float EvadeChance(float difficulty, bool boss)
        {
            if (difficulty < 0.25f) return 0f;
            if (boss) return 0.66f;
            return difficulty < 0.75f ? 0.15f : difficulty < 1.25f ? 0.25f : 0.33f;
        }

        public bool Fires => Current == Mode.Approach || Current == Mode.Chase;
        public float SpeedFactor => Current == Mode.Chase ? ChaseSpeed : 1f;
        public bool Rolling => rollMs > 0f;
        public bool Invulnerable => rollMs > RollMs - RollInvulnerableMs;
        /// <summary>0..1 through the roll under way (0 when none).</summary>
        public float RollProgress => rollMs > 0f ? 1f - rollMs / RollMs : 0f;
        /// <summary>1 just after an approach began, 0 once the aim has settled (x the target's size: the miss).</summary>
        public float AimError => aimMs > 0f ? aimMs / AimSettleMs : 0f;

        /// <summary>What the fighter sees of its target this frame.</summary>
        public struct View
        {
            public float distance;        // game units
            public bool insideBreakOff;   // within GoF2's break-off box (NpcTables.BreakOffRange)
            public float targetFacing;    // the target's forward . (toward the fighter), -1..1: > 0 = in front of its nose
        }

        /// <summary>A new target: the next approach starts at once.</summary>
        public void NewTarget() => Begin(Mode.Approach);

        /// <summary>Every frame, fighting or not: the roll, the aim's settling and the chase's cooldown run.</summary>
        public void Tick(float dtMs)
        {
            if (rollMs > 0f) rollMs -= dtMs;
            if (rollLockMs > 0f) rollLockMs -= dtMs;
            if (aimMs > 0f) aimMs -= dtMs;
            if (chaseCooldownMs > 0f) chaseCooldownMs -= dtMs;
        }

        /// <summary>A frame of the fight against its target (after Tick): the mode goes on or changes.</summary>
        public void Step(float dtMs, View v)
        {
            modeMs -= dtMs;
            switch (Current)
            {
                case Mode.Approach:
                    if (v.insideBreakOff || modeMs <= 0f) { Begin(random() < IdleChance ? Mode.Idle : Mode.VeerOff); return; }
                    if (chaseCooldownMs <= 0f && v.targetFacing < -0.7f && v.distance <= ChaseRange) { Begin(Mode.Chase); return; }
                    if ((teaseMs -= dtMs) <= 0f)
                    {
                        teaseMs = TeaseEveryMs;
                        if (v.distance <= TeaseRange && v.targetFacing > TeaseFacing && random() < TeaseChance) Begin(Mode.Tease);
                    }
                    return;
                case Mode.Chase:
                    if (modeMs <= 0f) { chaseCooldownMs = ChaseCooldownMs; Begin(Mode.VeerOff); }
                    return;
                default:
                    if (modeMs <= 0f) Begin(Mode.Approach);
                    return;
            }
        }

        /// <summary>A hit on it: true when it starts a roll (its chance, not rolling nor locked).</summary>
        public bool Hit()
        {
            if (evadeChance <= 0f || rollMs > 0f || rollLockMs > 0f || random() >= evadeChance) return false;
            rollMs = RollMs;
            rollLockMs = RollMs + RollLockMs;
            RollSide = random() < 0.5f ? -1 : 1;
            return true;
        }

        void Begin(Mode m)
        {
            Current = m;
            switch (m)
            {
                case Mode.Approach: modeMs = Range(ApproachMinMs, ApproachMaxMs); aimMs = AimSettleMs; break;
                case Mode.VeerOff: modeMs = Range(VeerOffMinMs, VeerOffMaxMs); break;
                case Mode.Idle: modeMs = IdleMs; break;
                case Mode.Tease: modeMs = Range(TeaseMinMs, TeaseMaxMs); break;
                case Mode.Chase: modeMs = ChaseMs; break;
            }
        }

        float Range(float a, float b) => a + (b - a) * random();

        /// <summary>GoF3's V formation: follower 'index' (0, 1, ...) flies 'row' spacings behind the leader and 'side' (1
        /// right, -1 left) x row spacings out, the two wings filled in turn.</summary>
        public static (int row, int side) VSlot(int index) => (index / 2 + 1, index % 2 == 0 ? 1 : -1);

        /// <summary>A follower's speed x this to keep its slot: 'behind' how far the slot is ahead of it along the leader's
        /// heading (negative: it overshot), in slot spacings; 1.5 x catching up, 0.6 x easing off.</summary>
        public static float FormationSpeed(float behindSpacings) => System.Math.Clamp(1f + behindSpacings / 4f, 0.6f, 1.5f);
    }
}
