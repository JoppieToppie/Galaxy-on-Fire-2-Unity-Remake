// NpcRoles.cs
// Remake (2026-11-03, for mods): what an NPC ship of a mod does in a fight beside flying and shooting at its target, its
// "role" (ships.json "traffic": { "role" }; World.NpcShip runs them), after EVERSPACE 2's drone types (its EDroneType and the
// drone blueprints, decompiled: BP_Outlaw_SniperDrone, BP_Outlaw_DetonatorDrone, BP_WebberDrone_Base with the webbed
// condition, BP_ShieldChargeDrone_Base, BP_Outlaw_LinkDrone_Armor, BP_AntiMissileDrone_Base, BP_Outlaw_JumpSuppressorDrone):
//   sniper           fires from FireRangeFactor times as far (ES2's sniper: sensing 4000 m, sniper aiming)
//   detonator        doesn't fire or break off: flies into its target and, within TriggerM (ES2's 20 m trigger sphere x
//                    the ES2 ships' 3.6), blows up: BlastFactor x its gun's damage to every ship within BlastM (ES2's
//                    radial damage, 100 m) and itself (ES2's 1e8 to its own hull)
//   webber           its hits on the player slow the player's ship to WebSpeedFactor for WebMs (ES2's webbed: linear and
//                    angular damping, "reduced speed and handling"); the player only
//   shieldCharger    doesn't fire: repairs RepairShare of each allied ship's maximum hull a second within SupportM (ES2's
//                    shield charge drones beam their wing's shields; the remake's NPCs have no shields)
//   armorLink        doesn't fire: the allied ships within SupportM take LinkDamageFactor of their hits (ES2's armour link
//                    drones buff their leader's armour)
//   antiMissile      doesn't fire at ships: shoots down the player's missiles within AntiMissileM, one each
//                    AntiMissileReloadMs (ES2's anti-missile drone: sensing 800 m, perfect aiming)
//   jumpSuppressor   doesn't fire: while one flies, hostile, in the orbit, the player's Khador Drive won't charge (ES2's
//                    JumpSuppressorComponent, enabled from BeginPlay until the drone dies; its marker seen from 100 km)
//   lootDrone        doesn't fire: carries LootMin..LootMax pieces of loot (one crate each) and runs. Once it is hit or the
//                    player comes within its detection box it flees from the player; FleeDelayMinMs..FleeDelayMaxMs later it charges its jump drive for JumpChargeMs
//                    and is gone with what it still carries (an EMP stops the charge, which starts over once the EMP wears
//                    off). Each time its hull falls to one of DropThresholds a piece pops out with a small blast; what is left
//                    when it dies drops at once (ES2's BP_LootDrone: RandomIntegerInRange(4, 7), its DropPercentages, flee
//                    delay 5..10 s, OrbitJumpChargeDuration 6 s, ApplyEMP's AbortJumpFocus, BP_Explosion_ProximityMine with
//                    no damage)
// The numbers ES2 keeps in its NPC levelling data or runtime attributes (the blast and repair amounts, the slow's strength,
// the link's share) are the remake's picks.

namespace GoF2Remake.Flight
{
    public static class NpcRoles
    {
        public enum Role { None, Sniper, Detonator, Webber, ShieldCharger, ArmorLink, AntiMissile, JumpSuppressor, LootDrone }

        public const float FireRangeFactor = 2.5f;
        public const float TriggerM = 20f * 3.6f, BlastM = 100f * 3.6f, BlastFactor = 10f;
        public const float WebMs = 4000f, WebSpeedFactor = 0.5f;
        public const float SupportM = 1500f, RepairShare = 0.05f, LinkDamageFactor = 0.5f;
        public const float AntiMissileM = 800f * 3.6f, AntiMissileReloadMs = 1000f;
        public const int LootMin = 4, LootMax = 7;
        public const float FleeDelayMinMs = 5000f, FleeDelayMaxMs = 10000f, JumpChargeMs = 6000f;

        /// <summary>ships.json's role names (case-insensitive); anything else is None.</summary>
        public static Role Parse(string name)
        {
            switch ((name ?? "").Trim().ToLowerInvariant())
            {
                case "sniper": return Role.Sniper;
                case "detonator": return Role.Detonator;
                case "webber": return Role.Webber;
                case "shieldcharger": return Role.ShieldCharger;
                case "armorlink": case "armourlink": return Role.ArmorLink;
                case "antimissile": return Role.AntiMissile;
                case "jumpsuppressor": return Role.JumpSuppressor;
                case "lootdrone": case "loot": return Role.LootDrone;
                default: return Role.None;
            }
        }

        /// <summary>Whether a ship of this role fires its guns at its target.</summary>
        public static bool Fires(Role r) => r == Role.None || r == Role.Sniper || r == Role.Webber;

        /// <summary>A detonator's blast on a ship 'gunDamage' would hit for: BlastFactor x that.</summary>
        public static float BlastDamage(float gunDamage) => gunDamage * BlastFactor;

        /// <summary>A loot drone's 'n' hull shares (0..1) that each drop a piece, 'random' giving 0..1: the i-th (from 1) is
        /// i / n x a factor of 1 -+ 1 / n, at least 0.05 (the last at about the full hull: its first hit drops one).</summary>
        public static float[] DropThresholds(int n, System.Func<float> random)
        {
            if (n <= 0) return new float[0];
            var t = new float[n];
            float step = 1f / n;
            for (int i = 0; i < n; i++)
            {
                float f = (1f - step) + random() * 2f * step;
                t[i] = System.Math.Min(1f, System.Math.Max(0.05f, (i + 1) * step * f));
            }
            return t;
        }

        /// <summary>A shield charger's repair over 'dtMs' for a ship of 'maxHull': RepairShare of it a second.</summary>
        public static float Repair(int maxHull, float dtMs) => maxHull * RepairShare * dtMs / 1000f;
    }
}
