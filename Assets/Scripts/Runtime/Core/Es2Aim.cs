// Es2Aim.cs
// Remake option (2026-11-06, Options > Gameplay "NPC aiming", Settings.Es2Aiming, a session rule): an NPC fighter aims the way
// EVERSPACE 2's do (es2_ships_importer research/npc_ai.md; ES2-Win64-Shipping.exe's FBehaviourTreeTaskLib::Attack and
// CalcLeadingReticuleWithAimingInertia, DT_AimingNoiseTable, UESGameInstance's DifficultyAimingInertias) instead of GoF2's
// (PlayerFighter::update: at the target's centre, no lead). World.NpcShip runs it (NpcShipEs2):
//   aim point  every "inertia" T seconds a new one: the target's place + its velocity x T + a smooth noise of amplitude
//              distance x tan(MaxDeviationAngle); T = the row's inertia x the difficulty's factor
//   reticule   each frame the lead for that aim point (where a shot meets the target moving on), and the reticule slides
//              onto it linearly over what is left of T (alpha = dt / remaining): the ship turns its guns, and fires on it
// The rows (DT_AimingNoiseTable): Projectile, every gun of GoF2's NPCs (inertia 0.2 s against a player, 0.6 s against an
// NPC; deviation 0.1 deg / 1.0 deg) and Sniper (0.3 s / 1.2 s, no deviation, the difficulty left out) for NpcRoles' snipers.
// The difficulty (DifficultyAimingInertias VeryEasy 1.5, Easy 1.3, Normal 1, Hard 0.5, VeryHard 0.25, Nightmare 0.25) only
// against players, GoF2's four on ES2's middle four: Easy 1.3, Normal 1, Hard 0.5, Extreme 0.25.
// Left out: the AI director's player-acceleration factor (GoF2's ships have no acceleration) and the NPC level factor
// (0.75 ^ levels above the player; GoF2's NPCs have no level). Plain C#; distances in any unit, times in seconds.

namespace GoF2Remake.Flight
{
    public sealed class Es2Aim
    {
        /// <summary>A DT_AimingNoiseTable row.</summary>
        public readonly struct Row
        {
            public readonly float inertiaVsPlayerS, inertiaVsAiS, deviationVsPlayerDeg, deviationVsAiDeg, wavelengthS;
            public readonly bool ignoreDifficulty;

            public Row(float inertiaVsPlayerS, float inertiaVsAiS, float deviationVsPlayerDeg, float deviationVsAiDeg, float wavelengthS, bool ignoreDifficulty)
            {
                this.inertiaVsPlayerS = inertiaVsPlayerS; this.inertiaVsAiS = inertiaVsAiS;
                this.deviationVsPlayerDeg = deviationVsPlayerDeg; this.deviationVsAiDeg = deviationVsAiDeg;
                this.wavelengthS = wavelengthS; this.ignoreDifficulty = ignoreDifficulty;
            }
        }

        public static readonly Row Projectile = new Row(0.2f, 0.6f, 0.1f, 1.0f, 1f, false);
        public static readonly Row Sniper = new Row(0.3f, 1.2f, 0f, 0f, 1f, true);

        /// <summary>The shortest re-aim (s): Extreme's 0.05 s of a projectile row stays above it.</summary>
        public const float MinInertiaS = 0.02f;

        /// <summary>ES2's DifficultyAimingInertias for the remake's difficulty (Session.Difficulty: 0 Easy, 0.5 Normal, 1 Hard,
        /// 1.5 Extreme): ES2's Easy, Normal, Hard and VeryHard.</summary>
        public static float DifficultyFactor(float difficulty) =>
            difficulty < 0.25f ? 1.3f : difficulty < 0.75f ? 1f : difficulty < 1.25f ? 0.5f : 0.25f;

        /// <summary>T: seconds between new aim points, against a player or an NPC.</summary>
        public static float InertiaS(in Row row, bool vsPlayer, float difficulty) =>
            vsPlayer ? row.inertiaVsPlayerS * (row.ignoreDifficulty ? 1f : DifficultyFactor(difficulty)) : row.inertiaVsAiS;

        public static float DeviationDeg(in Row row, bool vsPlayer) => vsPlayer ? row.deviationVsPlayerDeg : row.deviationVsAiDeg;

        /// <summary>The noise's amplitude at 'distance': distance x tan(the row's deviation).</summary>
        public static float NoiseAmplitude(in Row row, bool vsPlayer, float distance) =>
            distance * (float)System.Math.Tan(DeviationDeg(row, vsPlayer) * System.Math.PI / 180.0);

        float remainingS;

        /// <summary>Time for a new aim point.</summary>
        public bool Due => remainingS <= 0f;

        /// <summary>A new target: a new aim point at once.</summary>
        public void Reset() => remainingS = 0f;

        /// <summary>A new aim point was taken: it holds for 'inertiaS'.</summary>
        public void Begin(float inertiaS) => remainingS = System.Math.Max(MinInertiaS, inertiaS);

        /// <summary>This frame's share of the way from the reticule to the lead (CalcLeadingReticuleWithAimingInertia:
        /// dt / the time left, 1 once it is up).</summary>
        public float Blend(float dtS)
        {
            if (remainingS <= 0f) return 1f;
            float a = dtS >= remainingS ? 1f : dtS / remainingS;
            remainingS -= dtS;
            return a;
        }

        /// <summary>A smooth noise in -0.5..0.5 over time (UMathLib::Get3DNoise's role: one value a wavelength, eased between).</summary>
        public static float Noise(double timeS, float wavelengthS, int seed)
        {
            double x = timeS / System.Math.Max(1e-3, wavelengthS);
            double fl = System.Math.Floor(x);
            long i = (long)fl;
            double f = x - fl;
            float a = Hash(i, seed), b = Hash(i + 1, seed);
            double s = f * f * (3.0 - 2.0 * f);
            return (float)(a + (b - a) * s) - 0.5f;
        }

        static float Hash(long i, int seed)
        {
            unchecked
            {
                ulong h = (ulong)i * 0x9E3779B97F4A7C15UL ^ (ulong)(uint)seed * 0xC2B2AE3D27D4EB4FUL;
                h ^= h >> 33; h *= 0xFF51AFD7ED558CCDUL;
                h ^= h >> 33; h *= 0xC4CEB9FE1A85EC53UL;
                h ^= h >> 33;
                return (h >> 40) / 16777216f;
            }
        }

        /// <summary>When a shot from the origin at 'speed' meets a target at 'p' moving at 'v' (the smallest positive time;
        /// the straight distance / speed when it can't catch it). Units per time unit in, time units out.</summary>
        public static float InterceptTime(float px, float py, float pz, float vx, float vy, float vz, float speed)
        {
            float pp = px * px + py * py + pz * pz;
            float straight = speed > 1e-6f ? (float)System.Math.Sqrt(pp) / speed : 0f;
            float a = vx * vx + vy * vy + vz * vz - speed * speed;
            float b = 2f * (px * vx + py * vy + pz * vz);
            if (System.Math.Abs(a) < 1e-9f) return b < -1e-9f ? -pp / b : straight;
            float disc = b * b - 4f * a * pp;
            if (disc < 0f) return straight;
            float sq = (float)System.Math.Sqrt(disc);
            float t1 = (-b - sq) / (2f * a), t2 = (-b + sq) / (2f * a);
            float t = t1 > 0f && (t1 < t2 || t2 <= 0f) ? t1 : t2;
            return t > 0f ? t : straight;
        }
    }
}
