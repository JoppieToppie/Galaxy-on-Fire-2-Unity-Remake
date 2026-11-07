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
// 1.5 s (invincibleBuffDuration); RollLockMs (the component's 10 s lock, which GoF3's templates keep) before the next. GoF3's
// chance (NPCFactory::setHordeModeModifiers, ConfigNpc): the hull's evadeChanceBase (scouts and T1-T2 hulls 0, T3+ fighters
// 0.33, T3+ gunships 1.0, the bosses 0.1; ships.json "traffic": { "evade" }) x the difficulty's evadeChanceBaseMultiplier,
// capped by its evadeChanceMax: Easy 0 / 0, Medium 1 / 0.15, Hard 1 / 0.25, Boss 1 / 0.66 (GoF3's per-wave 0.02 left out:
// GoF2 has no waves). GoF2's four levels: Easy, Normal = Medium, Hard, Extreme = GoF3's boss missions' column.
// Easy's other rules (MissionController::configureSteeringAgent, SteeringAgentSystem::limitSpeed; NPCFactory::
// getSecondaryWeaponNPCAmmount): a fighter crossing the player's sights within 1200 slows to |its heading . the player's|
// of its speed (0.33 at least); half as many carry missiles (secondaryWeaponNPCCapMultiplier 0.5).
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
        public const float RollMs = 1800f, RollInvulnerableMs = 1500f, RollLockMs = 10000f;
        /// <summary>evadeChanceBase: a T3+ fighter's (the default for a hull that names none).</summary>
        public const float FighterEvadeBase = 0.33f;
        /// <summary>Easy's limitSpeed: the player's reach (GoF3 units x DistanceScale), its sights (dot), the slowest share.</summary>
        public const float EasySlowRange = 1200f * DistanceScale, EasySlowCone = 0.3f, EasySlowMin = 0.33f;
        /// <summary>Easy's secondaryWeaponNPCCapMultiplier: the share of fighters that carry their missiles.</summary>
        public const float EasySecondaryShare = 0.5f;
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
        /// Hard, 1.5 Extreme) and the hull's evadeChanceBase ('baseChance'; below 0 = a fighter's 0.33): the base x the
        /// difficulty's multiplier, capped by its maximum (Easy 0, Normal 0.15, Hard 0.25, Extreme 0.66). A boss (a Most Wanted
        /// criminal, a pirate event's boss) on GoF3's boss missions' column on every difficulty but Easy.</summary>
        public static float EvadeChance(float difficulty, bool boss, float baseChance = -1f)
        {
            if (difficulty < 0.25f) return 0f;
            float b = baseChance < 0f ? FighterEvadeBase : baseChance;
            float cap = boss || difficulty >= 1.25f ? 0.66f : difficulty < 0.75f ? 0.15f : 0.25f;
            return System.Math.Min(b, cap);
        }

        /// <summary>Easy's limitSpeed: the speed factor of a fighter 'distance' (units) from the player, 'inSights' = the player's
        /// forward . (toward the fighter), 'headings' = the player's forward . the fighter's; 1 outside the player's sights.</summary>
        public static float EasySlowdown(float distance, float inSights, float headings)
        {
            if (distance > EasySlowRange || inSights <= EasySlowCone) return 1f;
            return System.Math.Max(EasySlowMin, System.Math.Abs(headings));
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
