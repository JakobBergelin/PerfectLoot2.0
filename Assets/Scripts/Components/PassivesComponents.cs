using Unity.Entities;

public struct Level : IComponentData
{
    public int Value;
    public int Experience;
}

public struct UnitResource : IComponentData
{
    public ResourceType Type;
}

public struct UnitClass : IComponentData
{
    public ClassType Type;
}

public struct Passive : IBufferElementData
{
    public PassiveType Type;
}

public enum ResourceType
{
    None,
    Mana,
    Rage,
    Focus,
    Energy,
    Arcane
}

public enum ClassType
{
    None,
    Warrior,
    Mage,
    Rogue,
    Monk,
    Fighter,
    Paladin,
    Caster,
    Cleric,
    Fencer,
    Savant
}

public enum PassiveType
{
    None,
    Golem,
    Huskar,
    Medusa,
    Balance,
    Luminosity,
    Ghost,
    PixieDust,
    Quickening,
    Recharge,
}

// Resource types, added by CharacterManager from a ResourceDefinition asset
public struct Mana : IComponentData
{
    public float MaxResource;
    public float Regeneration;
}
public struct Rage : IComponentData
{
    public float MaxResource;
    public float Regeneration;
    public float ResourcePerHit;
}
public struct Focus : IComponentData
{
    public float MaxResource;
    public float Regeneration;
    // Added to the regeneration multiplier while standing still or channeling, 0.2 = +20%
    public float StandingStillRegenerationMultiplier;
}
public struct Energy : IComponentData
{
    public float MaxResource;
    public float Regeneration;
    public float ResourceOnSkillUse;
}
public struct Arcane : IComponentData
{
    public float MaxResource;
    public float Regeneration;
}

// Class, added by CharacterManager from a CharacterClass asset
public struct ClassAttributes : IComponentData
{
    public int StrengthPerLevel;
    public int AgilityPerLevel;
    public int IntelligencePerLevel;
}

public struct Golem : IComponentData
{
    // Can't regenerate hp
    public float MaxHealthMultiplier;
    public float PhysicalResistanceMultiplier;
}

public struct Huskar : IComponentData
{
    //Skills costs health instead of mana
}

public struct Medusa : IComponentData
{
    //Damage removes mana before health
}

public struct Balance : IComponentData
{
    // Damage bonus per point of the lowest of Strength, Agility and Intelligence
    public float DamagePerLowestAttribute;
}

public struct Ghost : IComponentData
{
    // Phased while Invulnerable
}

public struct PixieDust : IComponentData
{
    // Added to the health and resource regeneration multipliers while Invulnerable, 2 = +200%
    public float RegenerationMultiplier;
}

public struct Quickening : IComponentData
{
    // Cooldown reduction from Agility: Agility × this / (1 + Agility × this), so it never reaches 100% on its own
    public float CooldownPerAgility;
}

public struct Recharge : IComponentData
{
    public float ResourcePerTrigger;    // Resource spent on skills for each cooldown cut
    public float CooldownReduction;     // Seconds taken off one random skill's remaining cooldown
    public float Spent;                 // Toward the next cut
    public Unity.Mathematics.Random Random;
}
