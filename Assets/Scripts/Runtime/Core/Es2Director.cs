// Es2Director.cs
// Remake option (2026-11-06, with Es2Aim: Settings.Es2Aiming): EVERSPACE 2's AI director keeps a crowd from all shooting at
// a player at once (FBehaviourTreeTaskLib::Attack, FAiDirectorParameter's defaults: AttackerCountSuppress 3..12,
// AverageSumUnsuppressedAttackersAtMax 6, SuppressShootWavelength 8 s). With n NPCs attacking one player, each holds fire
// while its own smooth noise + 0.5 is at most pSuppress = clamp(1 - unsuppressed(n) / n, 0, 0.9), unsuppressed(n) running
// from 3 at 3 attackers to 6 at 12: up to 3 all shoot, a dozen about 6 at a time, in bursts. Only against players. ES2's
// gamepad set (4 of 10) and Nightmare Rifts' (8 of 12) are left out: the mouse set for everyone. Plain C#.

namespace GoF2Remake.Flight
{
    public static class Es2Director
    {
        public const int SuppressFrom = 3, SuppressTo = 12;
        public const float UnsuppressedAtMax = 6f, MaxSuppress = 0.9f, WavelengthS = 8f;

        /// <summary>How many of 'attackers' shoot at once on average.</summary>
        public static float Unsuppressed(int attackers)
        {
            float k = (attackers - SuppressFrom) / (float)(SuppressTo - SuppressFrom);
            k = k < 0f ? 0f : k > 1f ? 1f : k;
            return SuppressFrom + (UnsuppressedAtMax - SuppressFrom) * k;
        }

        /// <summary>The share of the time each of 'attackers' holds fire.</summary>
        public static float SuppressChance(int attackers)
        {
            if (attackers <= 0) return 0f;
            float p = 1f - Unsuppressed(attackers) / attackers;
            return p < 0f ? 0f : p > MaxSuppress ? MaxSuppress : p;
        }

        /// <summary>Whether the NPC with 'seed' holds fire at 'timeS' with 'attackers' on the same player.</summary>
        public static bool HoldsFire(int attackers, double timeS, int seed)
        {
            float p = SuppressChance(attackers);
            return p > 0f && Es2Aim.Noise(timeS, WavelengthS, seed) + 0.5f <= p;
        }
    }
}
