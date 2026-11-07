// Hazards.cs
// Remake (2026-11-03, for mods): an orbit's hazard volumes as plain rules (stations.json "orbit.hazards"; World.HazardZones
// runs them on the player's ship), after EVERSPACE 2's (decompiled: BP_HeatDamageVolume, its sphere's damage
// DamagePerSecondMax 200 x (1 - d / radius) ^ DamageFalloffExponent 2 every DamageInterval 0.25 s; BP_RadiationField with
// BP_Condition_Buildup_Radiated (BuildupDuration 5 s) and BP_Condition_Debuff_Radiated (the maximum hull x 0.85 a token,
// TokenLimit 10, "Dock to a station to remove radiation", kept beyond the location)):
//   heat        inside the sphere: dps x (1 - d / radius)^2 each 0.25 s tick (through the shield as any hit)
//   radiation   every BuildupMs inside the sphere one more token; the hull can't be repaired past
//               maxHull x 0.85^tokens (at most 10); docking clears them (StationMenuDockingChecks)
// Remake (2026-11-06), EVERSPACE 2's BP_FrostField, BP_CorrosionField and BP_DamageField:
//   frost       BP_Condition_Buildup_Frost: BuildupDuration 5 s inside builds into BP_Condition_Debuff_Frozen for
//               DefaultBuildupFinishedConditionDuration 6 s ("the ship freezes and loses control": the web's slow,
//               ShipController's SlowBy, at FrozenFactor); outside the buildup drains over DrainDuration 60 s
//   corrosion   ConditionIntensity 5 for DebuffDuration 8 s after leaving (the rift's corrosion field's rule,
//               RiftRun.CorrosionTickS / CorrosionTickSeconds: shield-piercing kinetic ticks)
//   damage      DamagePerSecond 20 each ConditionInterval 1 s, no falloff (its capsule as a sphere)
// The damage is the mod's (ES2's 200 is on its own hit point scale).

using UnityEngine;

namespace GoF2Remake.Flight
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class Hazards
    {
        public const float HeatTickMs = 250f, HeatFalloff = 2f;
        public const float RadiationBuildupMs = 5000f, RadiationShare = 0.85f;
        public const int MaxRadiationTokens = 10;
        public const float FrostBuildupMs = 5000f, FrostDrainMs = 60000f, FrozenMs = 6000f, FrozenFactor = 0.15f;
        public const float DamageTickMs = 1000f;

        /// <summary>The player's radiation tokens (kept from orbit to orbit until docking).</summary>
        public static int RadiationTokens { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => RadiationTokens = 0;

        /// <summary>One more token (at most MaxRadiationTokens); true if it was added.</summary>
        public static bool AddRadiation()
        {
            if (RadiationTokens >= MaxRadiationTokens) return false;
            RadiationTokens++;
            return true;
        }

        /// <summary>Docking at a station clears the radiation.</summary>
        public static void ClearRadiation() => RadiationTokens = 0;

        /// <summary>The share of the hull's maximum the ship may have with 'tokens' radiation tokens.</summary>
        public static float RadiationFactor(int tokens) => Mathf.Pow(RadiationShare, Mathf.Clamp(tokens, 0, MaxRadiationTokens));

        /// <summary>The frost buildup after 'dtMs' (ms of the FrostBuildupMs that freeze): inside it grows, outside it drains
        /// over FrostDrainMs; true in 'frozen' when it reaches the full buildup (then it starts again).</summary>
        public static float FrostStep(float buildupMs, float dtMs, bool inside, out bool frozen)
        {
            buildupMs = inside ? buildupMs + dtMs : Mathf.Max(0f, buildupMs - dtMs * FrostBuildupMs / FrostDrainMs);
            frozen = buildupMs >= FrostBuildupMs;
            return frozen ? 0f : buildupMs;
        }

        /// <summary>A heat tick's damage at 'distance' from the centre of a volume of 'radius' and 'dps' (per second), over a
        /// tick of 'tickMs'.</summary>
        public static float HeatDamage(float dps, float distance, float radius, float tickMs = HeatTickMs)
        {
            if (radius <= 0f || distance >= radius) return 0f;
            return dps * Mathf.Pow(1f - distance / radius, HeatFalloff) * tickMs / 1000f;
        }
    }
}
