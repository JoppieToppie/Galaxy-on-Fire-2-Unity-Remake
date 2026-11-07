// DamageTaken.cs
// Remake (2026-11-04, for mods, after Galaxy on Fire 3's StrengthAndWeaknessComponent): how much of the player's weapons'
// damage a ship takes, by the weapon. A mod's Most Wanted criminal (wanted.json "damageTaken", Modding/README.md "Most
// Wanted") lists percentages by a weapon's kind ("primary", "secondary", "turret"), by its category (items.json's, as the
// shop shows it: "Laser", "Blaster", "Auto-cannon"...) or by the item itself (its key); the most specific one counts, else
// 100. GoF3 (ECS::StrengthAndWeaknessSystem::modifyDamage) adds damage x (weakness - strength) per its DamageModifiers: a
// boss's "LaserWeakness" 0.3 and "AllPrimariesStrength" 0.3 leave its lasers at 100 % and its other guns at 70 %.

using System;
using System.Collections.Generic;

namespace GoF2Remake.Data
{
    public class DamageTaken
    {
        /// <summary>The key words for a weapon's kind (ItemData.TypeId 0-2).</summary>
        public static readonly string[] Kinds = { "primary", "secondary", "turret" };

        readonly float[] byKind = { 1f, 1f, 1f };
        readonly Dictionary<string, float> byCategory = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<int, float> byItem = new Dictionary<int, float>();

        public bool Any => byItem.Count > 0 || byCategory.Count > 0 || byKind[0] != 1f || byKind[1] != 1f || byKind[2] != 1f;

        /// <summary>A kind's factor (0 primary, 1 secondary, 2 turret).</summary>
        public void SetKind(int kind, float factor) { if (kind >= 0 && kind < byKind.Length) byKind[kind] = Math.Max(0f, factor); }
        public void SetCategory(string category, float factor) => byCategory[category] = Math.Max(0f, factor);
        public void SetItem(int item, float factor) => byItem[item] = Math.Max(0f, factor);

        /// <summary>The factor on a hit by 'item' (null = not a weapon of the player's: 1).</summary>
        public float Factor(ItemData item)
        {
            if (item == null) return 1f;
            if (byItem.TryGetValue(item.index, out float f)) return f;
            if (!string.IsNullOrEmpty(item.category) && byCategory.TryGetValue(item.category, out f)) return f;
            int kind = item.TypeId;
            return kind >= 0 && kind < byKind.Length ? byKind[kind] : 1f;
        }
    }
}
