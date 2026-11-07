// TargetDebuffs.cs
// Remake (2026-11-02): runs a ship's Flight.Debuffs (a weapon's dotDamage / vulnerablePercent hits, WeaponSystem.OnHit):
// deals the damage over time past the shield, as the player's (the weapon that put it on), and sets Target.vulnerability
// to the stacked damage-taken increase. Added to a ship on its first such hit; idle once the stacks run out.

using UnityEngine;

namespace GoF2Remake.Flight
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public class TargetDebuffs : MonoBehaviour
    {
        readonly Debuffs rules = new Debuffs();
        Target target;
        int item = -1;

        public static TargetDebuffs For(Target t)
        {
            if (t == null) return null;
            var d = t.GetComponent<TargetDebuffs>();
            if (d == null) { d = t.gameObject.AddComponent<TargetDebuffs>(); d.target = t; }
            d.enabled = true;
            return d;
        }

        public void AddDot(float damagePerTick, float ms, int fromItem) { rules.AddDot(damagePerTick, ms); item = fromItem; }

        public void AddVulnerability(float percent, float ms)
        {
            rules.AddVulnerability(percent, ms);
            if (target != null) target.vulnerability = rules.DamageFactor - 1f;
        }

        void Update()
        {
            if (target == null || !target.Alive) { rules.Clear(); Off(); return; }
            float due = rules.Step(Time.deltaTime * 1000f);
            target.vulnerability = rules.DamageFactor - 1f;
            if (due > 0f)
            {
                if (item >= 0) target.lastPlayerWeapon = item;
                target.Damage(due, false, default, 0f);   // past the shield (ES2's corrosion: shield-piercing)
            }
            if (!rules.Any) Off();
        }

        void Off()
        {
            if (target != null) target.vulnerability = 0f;
            enabled = false;
        }

        void OnDisable() { if (target != null) target.vulnerability = 0f; }
    }
}
