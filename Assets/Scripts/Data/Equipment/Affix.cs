namespace Equipment
{
    /// <summary>
    /// A stat an item can roll; maps onto a StatBlock field (StatAffix). Item assets store the number, so keep every
    /// value as it is and add new ones at the end. StatAffixCheck (editor) reports StatBlock fields without an affix.
    /// </summary>
    public enum Affix
    {
        Strength = 0,
        Agility = 1,
        Intelligence = 2,
        MovementSpeed = 3,
        AllResistance = 4,
        BlockChance = 5,
        CooldownReduction = 6,
        ResourceCostReduction = 7,
        DodgeChance = 8,
        AttackSpeed = 9,
        WeaponDamage = 10,
        WeaponDamageRange = 11,
        Lifesteal = 12,
        ProjectilePierces = 13,
        ProjectileBounces = 14,
        Damage = 15,
        AreaOfEffect = 16,
        ColdResistance = 17,
        FireResistance = 18,
        LightningResistance = 19,
        CriticalChance = 20,
        CriticalDamage = 21,
        Armor = 22,
        ResourceSteal = 23,
        Health = 24,
        HealthRegeneration = 25,
        Resource = 26,
        ResourceRegeneration = 27,
        PoisonResistance = 28,
        WeaponReach = 29,
        ArmorPierce = 30,
    }
}
