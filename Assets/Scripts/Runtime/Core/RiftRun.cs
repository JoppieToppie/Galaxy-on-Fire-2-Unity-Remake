// RiftRun.cs
// Remake (2026-11-04, for mods): a rift run's rules as plain rules (World.RiftEncounter runs one; the cruise drive's "rift"
// encounter), after EVERSPACE 2's Ancient Rifts (decompiled BP_Encounter_Rift; its mutators.csv):
//   stages     Stages of them, the last the boss's; a fight stage ends at its kill points: StageKillPoints by stage (ES2's
//              GetTargetKillPointCount: 15, 21, 26, 30, 35, 35, 40, 40, 45, 45) + 0..KillPointsSpread, while at least
//              MinAliveShare of them are alive as enemies (MinEnemiesAlive = trunc(target x 0.4))
//   kills      by the ship's class (ES2's EPawnType): KillPoints (drone 0.5, fighter 1, cruiser 1.5, capital 2) toward the
//              target, and ResolveGain (0.10, 0.12, 0.15, 0.20) of the resolve back while any is left
//   resolve    1 at a stage's start, from ResolveDelayS on it drains ResolveDrainPerS a second (ES2's
//              RESOLVE_DECREASE_PER_SECOND -0.016, RESOLVE_DELAY 10; not on the boss's stage); a stage won with resolve left
//              is mastered (ES2's SetStageMastered); run out, the stage goes on, unmastered
//   loot       the boss's crate x LootFactor: 0.5 + 0.5 x the mastered share of the fight stages (ES2's GetLootQuantityFactor:
//              its stage-resolve percentage x 0.5 over the lunacy's base)
//   mutators   from the second stage on, one a stage, uniformly among the rift's mutators not yet in play, the boss's stage one
//              of the boss's (decompiled UGameplayLib::GetRandomRiftMutatorForNextStage over mutators.csv's bAllowedInRifts /
//              bAllowedOnlyForRiftBosses; ES2's three portals each offer one and the pilot flies through one, here the rift
//              picks): Mutators / BossMutators, those of ES2's the remake has; their values ES2's (its Signal Decoder items'
//              mutator attributes)
// The newer mutators' numbers below (Mines on Death to Apocalypse) are ES2's as well; World.RiftEncounter / RiftHazards do them.

using System;
using System.Collections.Generic;

namespace GoF2Remake.Flight
{
    public class RiftRun
    {
        public static readonly int[] StageKillPoints = { 15, 21, 26, 30, 35, 35, 40, 40, 45, 45 };
        public const int KillPointsSpread = 5, DefaultStages = 4;
        public const float ResolveDrainPerS = 0.016f, ResolveDelayS = 10f, MinAliveShare = 0.4f;

        public enum Kind { Drone, Fighter, Cruiser, Capital }

        public static float KillPoints(Kind k) => k == Kind.Drone ? 0.5f : k == Kind.Fighter ? 1f : k == Kind.Cruiser ? 1.5f : 2f;
        public static float ResolveGain(Kind k) => k == Kind.Drone ? 0.10f : k == Kind.Fighter ? 0.12f : k == Kind.Cruiser ? 0.15f : 0.20f;

        // ES2's rift mutators the remake has (World.RiftEncounter does them; attributes.CSV's names, its Signal Decoders' values):
        public const string HullRepair = "mutator_hull_repair",        // Enemy Regen: the enemies repair their hull
                            SpeedBonus = "mutator_speed_bonus",        // Evasive: +SpeedBonusShare speed
                            EmpResistance = "mutator_emp_resistance",  // No EMP
                            NamesHidden = "mutator_names_hidden",      // Jarred Sensors: the enemies' type names hidden
                            HpHidden = "mutator_hp_hidden",            // Hidden Health: their hit point bars hidden
                            ExplodeOnKill = "mutator_explodeonkill",   // Explosive Death: area damage when one dies
                            HealOnKill = "mutator_healonkill",         // Repair On Kill: the others repaired HealOnKillShare
                            BuffOnKill = "mutator_buffonkill",         // Strength in Death: the others +BuffOnKillShare, up to BuffOnKillMax
                            Nearsighted = "mutator_nearsighted",       // half the damage beyond NearsightedM
                            Farsighted = "mutator_farsighted",         // half the damage within FarsightedM
                            NoLock = "mutator_no_lock",                // the enemies can't be locked
                            Vengeance = "mutator_vengeance",           // a detonator drone for every kill, at the pilot
                            MineDrop = "mutator_mine_drop",            // Mines on Death: MinesMin..Max proximity mines from a dying enemy
                            Overheat = "mutator_overheat",             // Overheated Systems: Flight.Overheat every Overheat.IntervalS
                            CorrosionField = "mutator_corrosion_field",// Corrosive Death: a corrosion field where an enemy dies
                            BurstBulbs = "mutator_burst_bulbs";        // Burst Bulbs: a growing bulb near the pilot, shoot it or it bursts
        public static readonly string[] Mutators = { HullRepair, SpeedBonus, EmpResistance, NamesHidden, HpHidden, ExplodeOnKill, HealOnKill,
                                                     BuffOnKill, Nearsighted, Farsighted, NoLock, Vengeance, MineDrop, Overheat,
                                                     CorrosionField, BurstBulbs };
        // ... and of the boss's: Quantum Mend (teleports every BossTimerMin..Max s and repairs, invulnerable, until hit), Evasive
        // Drift (boosts away as often), Guardians (invulnerable while two guards it spawns live), Phase Barrier (a shell around it
        // that stops shots from outside: fly in to hurt it), Stasis Snares (web bubbles around the pilot), Apocalypse (safe
        // fields, then a blast over the whole stage). (ES2's mutators.csv marks no others for rift bosses.)
        public const string BossTeleportRepairs = "mutator_riftboss_teleportrepairs", BossBoost = "mutator_riftboss_boost",
                            BossBodyguards = "mutator_riftboss_bodyguards", BossEnergyShield = "mutator_riftboss_energyshield",
                            BossWebFields = "mutator_riftboss_webfields", BossApocalypse = "mutator_riftboss_apocalypse";
        public static readonly string[] BossMutators = { BossTeleportRepairs, BossBoost, BossBodyguards, BossEnergyShield, BossWebFields, BossApocalypse };
        public const float SpeedBonusShare = 0.5f, HealOnKillShare = 0.2f, BuffOnKillShare = 0.05f;
        public const int BuffOnKillMax = 10;
        /// <summary>Unity metres: ES2's 30000 / 50000 cm x the ES2 ships' 3.6.</summary>
        public const float NearsightedM = 300f * 3.6f, FarsightedM = 500f * 3.6f, SightedDamageFactor = 0.5f;
        public const float BossTimerMinS = 15f, BossTimerMaxS = 30f;

        // ES2's numbers for the newer mutators (decompiled BP_Encounter_Rift and the actors it spawns), ES2 centimetres turned
        // into Unity metres (x 0.01 x the ES2 ships' 3.6); damage as seconds of a raider's fire (ES2's Outlaw raider: 55 DPS).
        /// <summary>Mines on Death (DropMinesAfterDeath, BP_ProximityMine_Base): RandomIntegerInRange(2, 3) mines 800 cm ahead of the
        /// dying ship, flung at 10000 cm/s within 45 degrees of its heading (LinearDamping 0.2), armed after 1 s, gone after 9-11
        /// s; one within 10000 cm sets it off, 0.5 s later it bursts (its blast's bounds 4000 cm) for 40 + 40 at the dead ship's
        /// level; its hit sphere 800 cm.</summary>
        public const int MinesMin = 2, MinesMax = 3;
        public const float MineAheadM = 8f * 3.6f, MineFlingMs = 100f * 3.6f, MineConeDeg = 45f, MineDamping = 0.2f, MineArmS = 1f,
                           MineLifeMinS = 9f, MineLifeMaxS = 11f, MineTriggerM = 100f * 3.6f, MineFuseS = 0.5f, MineBlastM = 40f * 3.6f,
                           MineBlastSeconds = 80f / 55f, MineHitM = 8f * 3.6f;
        /// <summary>Corrosive Death (BP_CorrosionField): 4000 cm for 7 s where a fighter dies, 7000 cm for 10 s where a bigger ship
        /// does; inside, corrosion for 8 s (BP_Condition_Debuff_Corrosion: a tick every 0.95 s past the shield, intensity 5).</summary>
        public const float CorrosionSmallM = 40f * 3.6f, CorrosionBigM = 70f * 3.6f, CorrosionSmallS = 7f, CorrosionBigS = 10f,
                           CorrosionDebuffS = 8f, CorrosionTickS = 0.95f, CorrosionTickSeconds = 5f / 55f;
        /// <summary>Burst Bulbs (SpawnBurstBulb, BP_Rift_Burst_Bulb): the first 10-15 s into the stage, the next as often, 200-800
        /// m x 3.6 from the pilot; it grows from 1000 cm to GrowthFactor 10 times that over its Lifetime 8 s and bursts for 120 +
        /// 160 (the pilot's level) within its explosion radius (the mutator's 300000 cm), unless the pilot destroys it first
        /// (OnlyDestructibleByPlayer; GetScaledHitpoints(80), against a raider's hull 200 + armour 50 + shield 60).</summary>
        public const float BulbFirstMinS = 10f, BulbFirstMaxS = 15f, BulbNearM = 200f * 3.6f, BulbFarM = 800f * 3.6f,
                           BulbStartM = 10f * 3.6f, BulbGrowth = 10f, BulbLifeS = 8f, BulbBurstM = 3000f * 3.6f,
                           BulbBurstSeconds = 280f / 55f, BulbHullShare = 80f / 310f;
        /// <summary>Phase Barrier (BP_StasisBubble on the boss, fly-through, Vehicle_WeaponTargetOnly): its bounds' box x (2.6, 3.9,
        /// 3.9), the first along its length.</summary>
        public const float BarrierLength = 2.6f, BarrierWidth = 3.9f;
        /// <summary>Stasis Snares (BP_DynamicActorField_WebBubbles around the pilot: NumActorsMax 20 within SpawnRadius 30000 cm;
        /// BP_WebBubble 1500-3000 cm): a bubble touched webs the ship for 3 s (BP_Condition_Debuff_Webbed: the webber's slow) and
        /// pops.</summary>
        public const int SnaresMax = 20;
        public const float SnareFieldM = 300f * 3.6f, SnareMinM = 15f * 3.6f, SnareMaxM = 30f * 3.6f, SnareWebS = 3f;
        /// <summary>Apocalypse: every 12-22 s MinInvulnerableFields 8 to MaxInvulnerableFields 10 safe fields (BP_InvulnerableField,
        /// 10000 cm, inside 5 s to build up: invincible) around the boss (500-2000 m x 3.6), 0-0.4 s apart; 8 s after the last a
        /// blast from the boss (BP_ARC9000_Explosion_Apocalypse: 1000000 cm, no falloff) for 120 + 120 at its level; 7 s later the
        /// fields fade.</summary>
        public const int SafeFieldsMin = 8, SafeFieldsMax = 10;
        public const float ApocalypseMinS = 12f, ApocalypseMaxS = 22f, SafeFieldM = 100f * 3.6f, SafeBuildupS = 5f,
                           SafeNearM = 500f * 3.6f, SafeFarM = 2000f * 3.6f, SafeGapMaxS = 0.4f, ApocalypseCountdownS = 8f,
                           ApocalypseBlastM = 10000f * 3.6f, ApocalypseBlastSeconds = 240f / 55f, SafeFadeS = 7f;

        public int Stages { get; }
        public int Stage { get; private set; }
        public float Target { get; private set; }
        public float Points { get; private set; }
        public float Resolve { get; private set; } = 1f;
        public bool Finished { get; private set; }
        public bool IsBossStage => Stage >= Stages - 1;
        public readonly List<bool> mastered = new List<bool>();
        public readonly List<string> active = new List<string>();
        float stageS;

        public RiftRun(int stages = DefaultStages) { Stages = Math.Max(2, stages); }

        public static float StageTarget(int stage, float random01) =>
            StageKillPoints[Math.Min(Math.Max(0, stage), StageKillPoints.Length - 1)] + (float)Math.Floor(random01 * (KillPointsSpread + 0.999f));

        /// <summary>How many enemies are kept alive in a fight stage (ES2's MinEnemiesAlive).</summary>
        public int MinAlive => Math.Max(1, (int)(Target * MinAliveShare));

        public bool HasMutator(string id) => active.Contains(id);

        /// <summary>The current stage begins: its target ('random01' 0..1), full resolve; from the second on a mutator
        /// ('pick' 0..1 among those not yet active) is added.</summary>
        public void BeginStage(float random01, float pick)
        {
            Target = IsBossStage ? 0f : StageTarget(Stage, random01);
            Points = 0f;
            Resolve = 1f;
            stageS = 0f;
            if (Stage > 0)
            {
                var free = new List<string>();
                foreach (var m in IsBossStage ? BossMutators : Mutators) if (!active.Contains(m)) free.Add(m);
                if (free.Count > 0) active.Add(free[Math.Min(free.Count - 1, (int)(pick * free.Count))]);
            }
        }

        /// <summary>'dt' seconds on: the resolve drains after the delay (not on the boss's stage).</summary>
        public void Tick(float dt)
        {
            if (Finished || dt <= 0f) return;
            stageS += dt;
            if (!IsBossStage && stageS > ResolveDelayS) Resolve = Math.Max(0f, Resolve - ResolveDrainPerS * dt);
        }

        /// <summary>An enemy of 'k' destroyed: its points, its resolve while any is left. True when the fight stage is won.</summary>
        public bool Kill(Kind k)
        {
            if (Finished || IsBossStage) return false;
            Points += KillPoints(k);
            if (Resolve > 0f) Resolve = Math.Min(1f, Resolve + ResolveGain(k));
            return Points >= Target;
        }

        /// <summary>The stage is over (won, or the boss beaten): mastered with resolve left; the next begins with BeginStage, or
        /// the run is Finished after the boss.</summary>
        public void EndStage()
        {
            if (Finished) return;
            if (!IsBossStage) mastered.Add(Resolve > 0f);
            if (IsBossStage) Finished = true; else Stage++;
        }

        /// <summary>The mastered share of the fight stages so far (0..1).</summary>
        public float MasteredShare
        {
            get
            {
                int n = 0;
                foreach (var m in mastered) if (m) n++;
                return mastered.Count == 0 ? 0f : n / (float)(Stages - 1);
            }
        }

        public float LootFactor => 0.5f + 0.5f * MasteredShare;
    }
}
