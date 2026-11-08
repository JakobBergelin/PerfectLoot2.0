using Unity.Entities;

/// <summary>A player's level and its experience toward the next one (ExperienceSystem levels it up).</summary>
public struct Level : IComponentData
{
    public int Value;
    public int Experience;
}

/// <summary>Experience every player gets when this enemy dies (EnemyDefinition.Experience).</summary>
public struct ExperienceReward : IComponentData
{
    public int Value;
}

/// <summary>An enemy whose experience was handed out (dying enemies can stay at 0 health for a while).</summary>
public struct ExperienceGiven : IComponentData
{
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
    Bloodrush,
    Prism,
    Anchor,
    Brawler,
    Brittle,
    Meditation,
    Afterglow,
    PackInstinct,
    LifeLink,
    BoneWard,
    FesteringHorde,
    Quarry,
    Bulwark,
    BloodPact,
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

/// <summary>The wearer's companions' hits use its Life on Hit and Resource on Hit (OnHitSystem).</summary>
public struct BloodPact : IComponentData { }

public struct Bulwark : IComponentData
{
    public float FrontReduction;    // Hits from in front, 0.4 = -40%
    public float BackIncrease;      // Hits from behind, 0.5 = +50%
}

public struct Bloodrush : IComponentData
{
    public float AttackSpeedPerStack;   // 0.03 = +3%
    public int MaxStacks;
    public float Duration;              // Seconds BloodrushBuff lasts after the last death
}

public struct Prism : IComponentData
{
    public float DamageBonus;           // Of PrismBuff, 0.2 = +20%
    public float Duration;              // Seconds PrismBuff lasts after the last change of element
    public bool HasElement;             // False until the first hit
    public DamageType LastElement;
}

public struct Anchor : IComponentData
{
    public int ArmorPerStack;
    public int MaxStacks;
    public float SecondsPerStack;       // Of standing still
    public float Elapsed;               // Toward the next stack
}

public struct Brawler : IComponentData
{
    public float DamageBonus;           // 0.2 = +20%
    public float Radius;
}

public struct Brittle : IComponentData
{
    public float DamageBonus;           // 0.3 = +30%
}

public struct Meditation : IComponentData
{
    public float DamageReduction;       // 0.2 = -20%
    public float HealthRegeneration;
}

public struct Afterglow : IComponentData
{
    public float DamageBonus;           // Of AfterglowBuff, 0.15 = +15%
    public float Duration;              // Seconds AfterglowBuff lasts after the last skill tagged Cooldown
}

public struct LifeLink : IComponentData
{
    public float Range;     // Meters from the wearer to a player it can link to
    public float TickRate;  // Seconds between payouts of its regeneration
    public Entity Target;   // The player it's linked to, Entity.Null: none (LifeLinkSystem)
}

public struct PackInstinct : IComponentData
{
    public float Radius;                // Meters from the wearer
    public float CriticalChanceBonus;   // Of PackInstinctBuff, 0.1 = +10%
}

public struct BoneWard : IComponentData
{
    public float Radius;            // Meters from the wearer
    public float ColdResistance;    // Added to ResistanceAuraBuff by each wearer
}

public struct FesteringHorde : IComponentData
{
    public float Radius;            // Meters from the wearer
    public float PoisonResistance;  // Added to ResistanceAuraBuff by each wearer
}

public struct Quarry : IComponentData
{
    public float Radius;            // Meters from the wearer a target is picked in and kept in
    public float DamagePenalty;     // To enemies other than the target, 0.3 = -30%
    public float ResourceRefund;    // Part of maximum resource when the target dies, 0.2 = 20%
    public Entity Target;           // Entity.Null: none (QuarrySystem)
    public Unity.Mathematics.Random Random;
}
