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
}

public struct Mana : IComponentData
{
}
public struct Rage : IComponentData
{
}
public struct Focus : IComponentData
{
}
public struct Energy : IComponentData
{
}
public struct Arcane : IComponentData
{
}

public struct Warrior : IComponentData
{
    public int StrengthPerLevel;
    public int AgilityPerLevel;
    public int IntelligencePerLevel;
}
public struct Mage : IComponentData
{
    public int StrengthPerLevel;
    public int AgilityPerLevel;
    public int IntelligencePerLevel;
}
public struct Rogue : IComponentData
{
    public int StrengthPerLevel;
    public int AgilityPerLevel;
    public int IntelligencePerLevel;
}
public struct Monk : IComponentData
{
    public int StrengthPerLevel;
    public int AgilityPerLevel;
    public int IntelligencePerLevel;
}
public struct Fighter : IComponentData
{
    public int StrengthPerLevel;
    public int AgilityPerLevel;
    public int IntelligencePerLevel;
}
public struct Paladin : IComponentData
{
    public int StrengthPerLevel;
    public int AgilityPerLevel;
    public int IntelligencePerLevel;
}
public struct Caster : IComponentData
{
    public int StrengthPerLevel;
    public int AgilityPerLevel;
    public int IntelligencePerLevel;
}
public struct Cleric : IComponentData
{
    public int StrengthPerLevel;
    public int AgilityPerLevel;
    public int IntelligencePerLevel;
}
public struct Fencer : IComponentData
{
    public int StrengthPerLevel;
    public int AgilityPerLevel;
    public int IntelligencePerLevel;
}
public struct Savant : IComponentData
{
    public int StrengthPerLevel;
    public int AgilityPerLevel;
    public int IntelligencePerLevel;
}

public struct Golem : IComponentData
{
    // Can't regenerate hp
    // Double health and armor 
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
    
}

public struct RandomizeCharacter : IComponentData{}
