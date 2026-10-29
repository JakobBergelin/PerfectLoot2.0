using System.Collections.Generic;

namespace Equipment
{
    /// <summary>
    /// The order affixes are listed in on items, grouped like the character panel. The Affix enum can't be reordered
    /// (item assets store its numbers), so the display order lives here. Affixes missing from the list sort last.
    /// </summary>
    public static class AffixOrder
    {
        static readonly Affix[] Order =
        {
            // Weapon
            Affix.WeaponDamage, Affix.WeaponDamageRange, Affix.WeaponReach, Affix.AttackSpeed,
            // Attributes
            Affix.Strength, Affix.Agility, Affix.Intelligence,
            // Vitals
            Affix.Health, Affix.HealthRegeneration, Affix.Resource, Affix.ResourceRegeneration,
            // Offense
            Affix.Damage, Affix.CriticalChance, Affix.CriticalDamage, Affix.ArmorPierce, Affix.AreaOfEffect, Affix.CooldownReduction,
            Affix.ResourceCostReduction, Affix.Lifesteal, Affix.ResourceSteal, Affix.ProjectilePierces, Affix.ProjectileBounces,
            Affix.ProjectileSpeed,
            // Defense
            Affix.Armor, Affix.BlockChance, Affix.DodgeChance,
            // Resistances
            Affix.AllResistance, Affix.FireResistance, Affix.ColdResistance, Affix.LightningResistance, Affix.PoisonResistance,
            // Other
            Affix.MovementSpeed, Affix.BuffDuration,
        };

        static readonly Dictionary<Affix, int> _index = BuildIndex();

        static Dictionary<Affix, int> BuildIndex()
        {
            var index = new Dictionary<Affix, int>();
            for (int i = 0; i < Order.Length; i++) index[Order[i]] = i;
            return index;
        }

        /// <summary>Sort key: position in the display order, or after all listed affixes (in enum order) when missing.</summary>
        public static int Index(Affix affix) => _index.TryGetValue(affix, out int i) ? i : Order.Length + (int)affix;
    }
}
