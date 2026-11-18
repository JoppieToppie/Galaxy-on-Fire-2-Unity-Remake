using System;

// Hitpoints.cs
// The original's Player stat object (Reference/research/ship_combat.md 2): three pools, no damage reduction.
//   Player::damage 0xafa70     the hit comes off the shield (its fraction is truncated on every hit), the overflow off
//                              the armor, the rest off the hull; hull < 1 = dead. Armor (attr 20) is just a second pool.
//   Player::damageEmp 0xaf834  EMP points (NPCs only; the player has none, so EMP never affects it); at 0 the ship is
//                              disabled and recovers linearly over the recovery time (Player::update)
//   PlayerEgo::update          shield regen: every tick of >= 101 ms, + maxShield * 100 / attr19 (attr 19 = full
//                              recharge time), no delay after a hit; repair bot hull +1 per 600 / 420 ms, armor +2 per
//                              1000 / 700 ms once the hull is full (Ketar / Ketar II)
// Plain C#: Target (NPCs, asteroids) and the player's PlayerHealth own one.

namespace GoF2Remake.Flight
{
    /// <summary>Explicit mod-defined damage channel. None retains the original shield/armor/hull calculation.</summary>
    public enum DamageType { None = 0, Energy = 1, Ballistic = 2 }

    public class Hitpoints
    {
        public float shield;
        public int armor, hull;
        public int maxShield, maxArmor, maxHull;
        public int emp, maxEmp;
        public float empRecoveryMs;
        public bool empDisabled;
        public bool vulnerable = true;

        /// <summary>Optional shield resistances, expressed as fractions from 0 to 1. Zero preserves legacy behaviour.</summary>
        public float energyResistance, ballisticResistance;
        /// <summary>Maximum fraction of ballistic damage that resistance may prevent, default 1.</summary>
        public float ballisticPenetration = 1f;

        /// <summary>Which layer took the last hit (the player's hit sound: 25 shield, 23 armor, 24 hull).</summary>
        public bool shieldHit, armorHit, hullHit;

        float empTimer, shieldTickMs, hullTickMs, armorTickMs;

        public bool Alive => hull > 0;
        public float HullFraction => maxHull > 0 ? (float)hull / maxHull : 0f;
        public float ShieldFraction => maxShield > 0 ? shield / maxShield : 0f;
        public float ArmorFraction => maxArmor > 0 ? (float)armor / maxArmor : 0f;
        public float EmpFraction => maxEmp > 0 ? (float)emp / maxEmp : 1f;
        /// <summary>shield + armor + hull, the player's "was I hit" check (PlayerEgo+0x130).</summary>
        public float Combined => shield + armor + hull;

        public Hitpoints(int hull, int shield = 0, int armor = 0)
        {
            this.hull = maxHull = hull;
            this.shield = maxShield = shield;
            this.armor = maxArmor = armor;
        }

        /// <summary>Player::setEmpData.</summary>
        public void SetEmp(int points, float recoveryMs)
        {
            emp = maxEmp = points;
            empRecoveryMs = recoveryMs;
        }

        /// <summary>Player::damage: returns true when this hit killed it. Remake: 'shieldFactor' scales what the hit does to
        /// the shield (a weapon's shieldDamage, the Disruptor's x2: Flight.Gun.ShieldFactor); the part the shield doesn't
        /// absorb goes on to the armor and hull at its plain value. A factor of 0 bypasses the shield (a weapon's shieldBypass,
        /// GoF3's AbilityBypassShield): the hit goes on to the armor and hull, the shield untouched.</summary>
        public bool Damage(int dmg, float shieldFactor = 1f) => Damage(dmg, shieldFactor, DamageType.None);

        /// <summary>Applies an explicitly typed hit. Energy resistance reduces shield effectiveness and carries overflow
        /// into armor/hull. Ballistic hits damage shield and hull together; hull penetration is reduced by ballistic
        /// resistance scaled by the shield fraction immediately before impact. Untyped hits retain the legacy algorithm.</summary>
        public bool Damage(int dmg, float shieldFactor, DamageType damageType)
        {
            if (!vulnerable || hull <= 0 || dmg <= 0) return false;
            if (damageType == DamageType.Energy && shield > 0f && energyResistance > 0f)
            {
                float factor = Math.Max(0f, shieldFactor) * (1f - Clamp01(energyResistance));
                float cost = dmg * factor;
                float overflow = factor > 0f ? Math.Max(0f, dmg - shield / factor) : dmg;
                shield = Math.Max(0f, shield - cost);
                if (cost > 0f) shieldHit = true;
                ApplyArmorHull(Math.Max(0, (int)overflow));
                return hull < 1;
            }
            if (damageType == DamageType.Ballistic && ballisticResistance > 0f)
            {
                float fraction = maxShield > 0 ? Clamp01(shield / maxShield) : 0f;
                float penetration = Clamp01(ballisticResistance) * fraction * Clamp01(ballisticPenetration);
                float hullDamage = dmg * (1f - penetration);
                float cost = dmg * Math.Max(0f, shieldFactor);
                if (shield > 0f && cost > 0f) { shield = Math.Max(0f, shield - cost); shieldHit = true; }
                ApplyArmorHull(Math.Max(0, (int)hullDamage));
                return hull < 1;
            }
            int s;
            if (shieldFactor <= 0f)
            {
                int a0 = armor - dmg;
                if (a0 >= 0) { armor = a0; armorHit = true; }
                else { armor = 0; hull += a0; hullHit = true; }
                if (hull < 1) { hull = 0; return true; }
                return false;
            }
            if (shieldFactor != 1f && shield > 0f)
            {
                int onShield = (int)(dmg * shieldFactor);
                if (onShield <= (int)shield) s = (int)shield - onShield;
                else s = -(dmg - (int)((int)shield / shieldFactor));   // the plain damage the shield didn't stop
            }
            else s = (int)shield - dmg;
            if (s >= 0) { shield = s; shieldHit = true; }
            else
            {
                shield = 0f;
                int a = s + armor;
                if (a >= 0) { armor = a; armorHit = true; }
                else { armor = 0; hull += a; hullHit = true; }
            }
            if (hull < 1) { hull = 0; return true; }
            return false;
        }

        static float Clamp01(float value) => Math.Max(0f, Math.Min(1f, value));

        void ApplyArmorHull(int damage)
        {
            if (damage <= 0) return;
            int remaining = armor - damage;
            if (remaining >= 0) { armor = remaining; armorHit = true; }
            else { armor = 0; hull += remaining; hullHit = true; }
            if (hull < 1) hull = 0;
        }

        /// <summary>Player::damageEmp: returns true when this hit disabled it.</summary>
        public bool DamageEmp(int amount)
        {
            if (!vulnerable || emp <= 0 || hull <= 0 || amount <= 0) return false;
            emp -= amount;
            if (emp >= 1) return false;
            emp = 0;
            empDisabled = true;
            empTimer = 0f;
            return true;
        }

        /// <summary>How long an EMP has kept it disabled (ms), 0 when it isn't.</summary>
        public float EmpElapsedMs => empDisabled ? empTimer : 0f;

        /// <summary>Multiplayer (a ship taken over from another game): disabled by an EMP 'elapsedMs' ago.</summary>
        public void RestoreEmpDisabled(float elapsedMs)
        {
            if (hull <= 0) return;
            empDisabled = true;
            empTimer = elapsedMs > 0f ? elapsedMs : 0f;
            emp = 0;
        }

        /// <summary>Player::update: EMP recovery.</summary>
        public void Update(float dtMs)
        {
            if (!empDisabled) return;
            empTimer += dtMs;
            emp = empRecoveryMs > 0f ? (int)(empTimer / empRecoveryMs * maxEmp) : maxEmp;
            if (emp > maxEmp || empRecoveryMs <= 0f) { emp = maxEmp; empDisabled = false; }
        }

        /// <summary>PlayerEgo::update shield regen: a tick when >= 101 ms have passed (the remainder is dropped, like the
        /// original), + maxShield / (attr19 / 100).</summary>
        public void RegenerateShield(float dtMs, int fullRechargeMs)
        {
            if (fullRechargeMs <= 0 || hull <= 0 || maxShield <= 0) return;
            shieldTickMs += dtMs;
            if (shieldTickMs < 101f) return;
            shieldTickMs = 0f;
            shield = System.Math.Min(shield + maxShield / (fullRechargeMs / 100f), maxShield);
        }

        /// <summary>PlayerEgo::update repair bot: hull +1 per 'hullMs'; once the hull is full armor +2 per 'armorMs'.</summary>
        public void Repair(float dtMs, float hullMs, float armorMs)
        {
            if (hull <= 0) return;
            hullTickMs += dtMs;
            armorTickMs += dtMs;
            if (hullTickMs >= hullMs) { hullTickMs = 0f; if (hull < maxHull) hull++; }
            if (armorTickMs >= armorMs) { armorTickMs = 0f; if (hull >= maxHull && armor < maxArmor) armor = System.Math.Min(armor + 2, maxArmor); }
        }
    }
}
