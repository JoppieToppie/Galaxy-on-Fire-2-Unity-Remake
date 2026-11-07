// Debuffs.cs
// Remake (2026-11-02, for mods): a ship's damage over time and its damage-taken increase from weapon hits, as plain rules
// (Flight.TargetDebuffs runs them on a ship; Modding/README.md "Weapons"), after EVERSPACE 2's debuff conditions
// (decompiled: BP_Condition_Debuff_Corrosion and _Infested, a timer dealing their intensity as damage past the shield;
// BP_Condition_Debuff_Destabilized, +0.2 incoming damage per token on the shield, armour and hull, stacking by intensity):
//   damage over time   each hit adds dotDamage every TickMs for dotMs (stacks: several hits at once deal their sum)
//   vulnerability      each hit adds vulnerablePercent % to every hit the ship takes for vulnerableMs (stacks, at most
//                      MaxVulnerablePercent: remake cap, ES2's has none but its tokens run out)

using System.Collections.Generic;

namespace GoF2Remake.Flight
{
    public class Debuffs
    {
        /// <summary>The tick of the damage over time (ES2's Corrosion timer 0.95 s; its Infested 1 s).</summary>
        public const float TickMs = 950f;
        /// <summary>The most a ship's stacked vulnerability adds (%).</summary>
        public const float MaxVulnerablePercent = 100f;
        /// <summary>At most this many running stacks of each kind (the oldest gives way).</summary>
        public const int MaxStacks = 16;

        struct Stack { public float amount, msLeft; }
        readonly List<Stack> dots = new List<Stack>(), vulns = new List<Stack>();
        float tickMs;

        public bool Any => dots.Count > 0 || vulns.Count > 0;

        public void AddDot(float damagePerTick, float ms) => Add(dots, damagePerTick, ms);
        public void AddVulnerability(float percent, float ms) => Add(vulns, percent, ms);

        static void Add(List<Stack> list, float amount, float ms)
        {
            if (amount <= 0f || ms <= 0f) return;
            if (list.Count >= MaxStacks) list.RemoveAt(0);
            list.Add(new Stack { amount = amount, msLeft = ms });
        }

        /// <summary>The damage taken now: x (1 + the stacked percent / 100), the stack capped at MaxVulnerablePercent.</summary>
        public float DamageFactor
        {
            get
            {
                float p = 0f;
                foreach (var v in vulns) p += v.amount;
                return 1f + System.Math.Min(p, MaxVulnerablePercent) / 100f;
            }
        }

        /// <summary>'dtMs' on: the damage over time due in it (the running stacks' sum at each tick passed); the stacks age.</summary>
        public float Step(float dtMs)
        {
            if (dtMs <= 0f) return 0f;
            float due = 0f;
            if (dots.Count > 0)
            {
                tickMs += dtMs;
                while (tickMs >= TickMs && dots.Count > 0)
                {
                    tickMs -= TickMs;
                    for (int i = dots.Count - 1; i >= 0; i--)
                    {
                        var d = dots[i];
                        due += d.amount;
                        d.msLeft -= TickMs;
                        if (d.msLeft <= 0f) dots.RemoveAt(i); else dots[i] = d;
                    }
                }
            }
            else tickMs = 0f;
            for (int i = vulns.Count - 1; i >= 0; i--)
            {
                var v = vulns[i];
                v.msLeft -= dtMs;
                if (v.msLeft <= 0f) vulns.RemoveAt(i); else vulns[i] = v;
            }
            return due;
        }

        public void Clear() { dots.Clear(); vulns.Clear(); tickMs = 0f; }
    }
}
