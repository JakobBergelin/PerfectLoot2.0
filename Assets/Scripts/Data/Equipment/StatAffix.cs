using Unity.Mathematics;

namespace Equipment
{
    /// <summary>Maps an Affix onto its StatBlock field. Shared by item stats and effect stat modifiers; Burst-compatible.</summary>
    public static class StatAffix
    {
        public static void Add(ref StatBlock block, Affix affix, float value)
        {
            switch (affix)
            {
                case Affix.Strength: block.Strength += value; break;
                case Affix.Agility: block.Agility += value; break;
                case Affix.Intelligence: block.Intelligence += value; break;
                case Affix.MovementSpeed: block.MovementSpeed += value; break;
                case Affix.AllResistance: block.AllResistance += value; break;
                case Affix.BlockChance: block.BlockChance += value; break;
                case Affix.CooldownReduction: block.CooldownReduction += value; break;
                case Affix.ResourceCostReduction: block.ResourceCostReduction += value; break;
                case Affix.DodgeChance: block.DodgeChance += value; break;
                case Affix.AttackSpeed: block.AttackSpeed += value; break;
                case Affix.WeaponDamage: block.WeaponDamage += value; break;
                case Affix.WeaponDamageRange: block.WeaponDamageRange += value; break;
                case Affix.Lifesteal: block.Lifesteal += value; break;
                case Affix.ProjectilePierces: block.Pierce += (int)math.round(value); break;
                case Affix.ProjectileBounces: block.Bounces += (int)math.round(value); break;
                case Affix.Damage: block.Damage += value; break;
                case Affix.AreaOfEffect: block.AreaOfEffect += value; break;
                case Affix.ColdResistance: block.ColdResistance += value; break;
                case Affix.FireResistance: block.FireResistance += value; break;
                case Affix.LightningResistance: block.LightningResistance += value; break;
                case Affix.CriticalChance: block.CriticalChance += value; break;
                case Affix.CriticalDamage: block.CriticalDamage += value; break;
                case Affix.Armor: block.Armor += (int)math.round(value); break;
                case Affix.ResourceSteal: block.ResourceSteal += value; break;
                case Affix.Health: block.Health += value; break;
                case Affix.HealthRegeneration: block.HealthRegeneration += value; break;
                case Affix.Resource: block.Resource += value; break;
                case Affix.ResourceRegeneration: block.ResourceRegeneration += value; break;
                case Affix.PoisonResistance: block.PoisonResistance += value; break;
                case Affix.WeaponReach: block.WeaponReach += value; break;
                case Affix.ArmorPierce: block.ArmorPierce += value; break;
            }
        }
    }
}
