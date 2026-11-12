using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Every stat a unit can have. Shared by BaseStats, EquipmentStats and CalculatedStats.
/// Percentages are fractions: 0.1 = 10%.
/// </summary>
[System.Serializable]
public struct StatBlock
{
    public float Strength;
    public float Agility;
    public float Intelligence;

    public float WeaponDamage;
    public float WeaponDamageRange;     // Weapon damage rolls are WeaponDamage +- this
    public float WeaponReach;           // How far in front of the unit melee attacks land; CombatSettings.UnarmedWeaponReach when 0
    public float WeaponCooldown;        // Multiplies the time between attacks (folded into AttackSpeed when stats are finalized); 1 when 0
    public float AttackSpeed;
    public float Damage;
    public float ElementalDamage;       // Extra damage on every non-Physical hit; adds to Damage, see DamageMultiplier
    public float FireDamage;
    public float ColdDamage;
    public float LightningDamage;
    public float PoisonDamage;

    public float Health;
    public float HealthRegeneration;
    public float Resource;
    public float ResourceRegeneration;
    public float ResourceCostReduction;
    public float ResourceOnHit;
    public float LifeOnHit;

    public int Pierce;
    public int Bounces;
    public float ProjectileSpeed;       // Projectiles this unit spawns fly this much faster (negative: slower)
    public int Armor;
    public float ArmorPierce;           // Armor ignored on the targets of this unit's physical hits (flat, before their multipliers)

    public float MovementSpeed;
    public float AreaOfEffect;
    public float CooldownReduction;     // Shorter skill cooldowns (Dash, Teleport...); repeated attacks (Fireball...) use AttackSpeed. See SkillUtil.Cooldown
    public float BuffDuration;          // Buffs received last this much longer (negative: shorter); see EffectApply.BuffDuration

    public float ColdResistance;
    public float FireResistance;
    public float PoisonResistance;
    public float LightningResistance;
    public float AllResistance;

    public float CriticalChance;
    public float CriticalDamage;
    public float BlockChance;
    public float DodgeChance;
    public float ProjectileDamageReduction;    // Less damage from projectiles' hits: 0.2 = -20% (no cap; 1 or more takes none)
    public float MeleeDamageReduction;         // Less damage from enemies' melee hits, the same way
    public float AgilityForStrength;           // Share of Agility that also counts toward Strength requirements (Ambidextrous: 0.5); not an affix

    // Elemental conversion legendary powers: every skill hit and area of this unit deals ConvertDamageTo instead.
    // Set each frame by ElementalConversionSystem, not by items' stats (Add leaves them alone).
    [System.NonSerialized] public bool ConvertsDamage;
    [System.NonSerialized] public DamageType ConvertDamageTo;

    // Channeled skills cost this much more (0.3 = 30% more, negative: cheaper), see SkillUtil.ChannelCost.
    // Set each frame by CrossfireStatsSystem, not by items' stats (Add leaves it alone).
    [System.NonSerialized] public float ChannelCostChange;

    /// <summary>The damage type this unit's skills deal instead of type (type itself without a conversion).</summary>
    public DamageType ConvertedType(DamageType type) => ConvertsDamage ? ConvertDamageTo : type;

    /// <summary>What the radius of this unit's attacks and areas is multiplied by: 1 + AreaOfEffect, never below 0.</summary>
    public float AreaMultiplier => math.max(0f, 1f + AreaOfEffect);

    /// <summary>
    /// What a hit of this damage type is multiplied by: 1 + Damage, plus ElementalDamage for non-Physical types, plus
    /// the type's own damage %. All additive.
    /// </summary>
    public float DamageMultiplier(DamageType type)
    {
        float bonus = Damage + type switch
        {
            DamageType.Fire => ElementalDamage + FireDamage,
            DamageType.Cold => ElementalDamage + ColdDamage,
            DamageType.Lightning => ElementalDamage + LightningDamage,
            DamageType.Poison => ElementalDamage + PoisonDamage,
            _ => 0f
        };
        return 1f + bonus;
    }

    public void Add(in StatBlock other)
    {
        Strength += other.Strength;
        Agility += other.Agility;
        Intelligence += other.Intelligence;

        WeaponDamage += other.WeaponDamage;
        WeaponDamageRange += other.WeaponDamageRange;
        WeaponReach += other.WeaponReach;
        WeaponCooldown += other.WeaponCooldown;
        AttackSpeed += other.AttackSpeed;
        Damage += other.Damage;
        ElementalDamage += other.ElementalDamage;
        FireDamage += other.FireDamage;
        ColdDamage += other.ColdDamage;
        LightningDamage += other.LightningDamage;
        PoisonDamage += other.PoisonDamage;

        Health += other.Health;
        HealthRegeneration += other.HealthRegeneration;
        Resource += other.Resource;
        ResourceRegeneration += other.ResourceRegeneration;
        ResourceCostReduction += other.ResourceCostReduction;
        ResourceOnHit += other.ResourceOnHit;
        LifeOnHit += other.LifeOnHit;

        Pierce += other.Pierce;
        Bounces += other.Bounces;
        ProjectileSpeed += other.ProjectileSpeed;
        Armor += other.Armor;
        ArmorPierce += other.ArmorPierce;

        MovementSpeed += other.MovementSpeed;
        AreaOfEffect += other.AreaOfEffect;
        CooldownReduction += other.CooldownReduction;
        BuffDuration += other.BuffDuration;

        ColdResistance += other.ColdResistance;
        FireResistance += other.FireResistance;
        PoisonResistance += other.PoisonResistance;
        LightningResistance += other.LightningResistance;
        AllResistance += other.AllResistance;

        CriticalChance += other.CriticalChance;
        CriticalDamage += other.CriticalDamage;
        BlockChance += other.BlockChance;
        DodgeChance += other.DodgeChance;
        ProjectileDamageReduction += other.ProjectileDamageReduction;
        MeleeDamageReduction += other.MeleeDamageReduction;
        AgilityForStrength += other.AgilityForStrength;
    }

    /// <summary>Undoes Add (the character panel's swap preview takes one set of equipment stats out). Keep in step with Add.</summary>
    public void Subtract(in StatBlock other)
    {
        Strength -= other.Strength;
        Agility -= other.Agility;
        Intelligence -= other.Intelligence;

        WeaponDamage -= other.WeaponDamage;
        WeaponDamageRange -= other.WeaponDamageRange;
        WeaponReach -= other.WeaponReach;
        WeaponCooldown -= other.WeaponCooldown;
        AttackSpeed -= other.AttackSpeed;
        Damage -= other.Damage;
        ElementalDamage -= other.ElementalDamage;
        FireDamage -= other.FireDamage;
        ColdDamage -= other.ColdDamage;
        LightningDamage -= other.LightningDamage;
        PoisonDamage -= other.PoisonDamage;

        Health -= other.Health;
        HealthRegeneration -= other.HealthRegeneration;
        Resource -= other.Resource;
        ResourceRegeneration -= other.ResourceRegeneration;
        ResourceCostReduction -= other.ResourceCostReduction;
        ResourceOnHit -= other.ResourceOnHit;
        LifeOnHit -= other.LifeOnHit;

        Pierce -= other.Pierce;
        Bounces -= other.Bounces;
        ProjectileSpeed -= other.ProjectileSpeed;
        Armor -= other.Armor;
        ArmorPierce -= other.ArmorPierce;

        MovementSpeed -= other.MovementSpeed;
        AreaOfEffect -= other.AreaOfEffect;
        CooldownReduction -= other.CooldownReduction;
        BuffDuration -= other.BuffDuration;

        ColdResistance -= other.ColdResistance;
        FireResistance -= other.FireResistance;
        PoisonResistance -= other.PoisonResistance;
        LightningResistance -= other.LightningResistance;
        AllResistance -= other.AllResistance;

        CriticalChance -= other.CriticalChance;
        CriticalDamage -= other.CriticalDamage;
        BlockChance -= other.BlockChance;
        DodgeChance -= other.DodgeChance;
        ProjectileDamageReduction -= other.ProjectileDamageReduction;
        MeleeDamageReduction -= other.MeleeDamageReduction;
        AgilityForStrength -= other.AgilityForStrength;
    }
}

public struct BaseStats : IComponentData
{
    public StatBlock Value;
}

/// <summary>Summed stats of all equipped items. Written by InventoryManager when equipment changes.</summary>
public struct EquipmentStats : IComponentData
{
    public StatBlock Value;
}

/// <summary>
/// Reset to BaseStats every frame, then built up by the systems in CalculateStatsSystemGroup. A player's are sent to every
/// machine, for its character panel and the unit panel.
/// </summary>
public struct CalculatedStats : IComponentData
{
    [Unity.NetCode.GhostField] public StatBlock Value;
    [Unity.NetCode.GhostField] public bool PierceOnCrit;
    [Unity.NetCode.GhostField] public float AlwaysAffordableCost;  // One-off skill costs up to this can be paid without enough resource (Bottomless)
    [Unity.NetCode.GhostField] public float CooldownDamage;        // Extra damage per second of a skill's base cooldown (Patience), see SkillUtil.CooldownDamage
}

public struct MovementSpeed : IComponentData
{
    public float Value;
}

public struct Armor : IComponentData
{
    public int Value;
    public float PhysicalResistanceMultiplier;

    public float PhysicalResistance;
}
