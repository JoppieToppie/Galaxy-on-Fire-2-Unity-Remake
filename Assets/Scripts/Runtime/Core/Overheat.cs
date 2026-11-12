// Overheat.cs
// Remake (2026-11-04, for mods): the player's overheated equipment, after EVERSPACE 2's Overheated Systems rift mutator
// (decompiled BP_Encounter_Rift: every IntervalS the timer calls UHealthComponent::OverheatSingleRandomEquippedItem(10.0): one
// of the ship's damageable equipped items at random, not already hot, works as a damaged item for DurationS
// (UItem::OverheatItem -> UItemLib::ApplyDamagedItemAttributes, itemdamage.csv's attributes)). What a damaged item loses, by
// itemdamage.csv, on the GoF2 equipment that has it:
//   Primary    the guns' fire rate x0.5 (cat_pulse_laser / cat_thermo_gun weapon_fire_rate 0.5)
//   Secondary  the missiles' damage x0.5 (cat_missile_launcher / cat_rocket_launcher hull and shield damage 0.5)
//   Shield     its recharge x0.5 (cat_shield shield_recharge_speed 0.5)
//   Booster    the boost's speed and its recharge x0.5 (cat_thrusters boost_speed / boost_recharge_speed 0.5)
// ES2's energy core, sensors and cargo unit have no GoF2 counterpart. World.RiftEncounter runs it; WeaponSystem,
// PlayerHealth and FlightModel read Factor.

using System;

namespace GoF2Remake.Flight
{
    public static class Overheat
    {
        public enum Slot { Primary, Secondary, Shield, Booster }
        public const int SlotCount = 4;
        public const float IntervalS = 10f, DurationS = 10f, DamagedFactor = 0.5f;

        static readonly float[] leftS = new float[SlotCount];

        /// <summary>Whether 'slot' is overheated now.</summary>
        public static bool Hot(Slot slot) => leftS[(int)slot] > 0f;

        /// <summary>What 'slot' works at: DamagedFactor while overheated, else 1.</summary>
        public static float Factor(Slot slot) => Hot(slot) ? DamagedFactor : 1f;

        /// <summary>'dt' seconds on: the overheats cool down.</summary>
        public static void Tick(float dt)
        {
            if (dt <= 0f) return;
            for (int i = 0; i < SlotCount; i++) leftS[i] = Math.Max(0f, leftS[i] - dt);
        }

        /// <summary>One of the slots 'has' says the ship has (not already hot) overheats for DurationS, picked by 'random01';
        /// returns it, or null when none can.</summary>
        public static Slot? HeatRandom(Func<Slot, bool> has, float random01)
        {
            var free = new System.Collections.Generic.List<Slot>();
            for (int i = 0; i < SlotCount; i++) if (!Hot((Slot)i) && (has == null || has((Slot)i))) free.Add((Slot)i);
            if (free.Count == 0) return null;
            var s = free[Math.Min(free.Count - 1, (int)(random01 * free.Count))];
            leftS[(int)s] = DurationS;
            return s;
        }

        /// <summary>Whether any slot is overheated.</summary>
        public static bool AnyHot { get { for (int i = 0; i < SlotCount; i++) if (leftS[i] > 0f) return true; return false; } }

        /// <summary>One overheated slot, picked by 'random01', cool again (ES2's Nano Repair Kit); returns it, null when none is.</summary>
        public static Slot? CoolRandom(float random01)
        {
            var hot = new System.Collections.Generic.List<Slot>();
            for (int i = 0; i < SlotCount; i++) if (leftS[i] > 0f) hot.Add((Slot)i);
            if (hot.Count == 0) return null;
            var s = hot[Math.Min(hot.Count - 1, (int)(random01 * hot.Count))];
            leftS[(int)s] = 0f;
            return s;
        }

        /// <summary>Everything cool again (the rift over, a new game).</summary>
        public static void Clear() => Array.Clear(leftS, 0, SlotCount);
    }
}
