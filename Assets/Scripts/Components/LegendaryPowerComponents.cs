using Unity.Entities;

// Components added by legendary powers (LegendaryPower<T>) while the item is equipped, for powers that are only data.

/// <summary>Vanguard: main physical hits on the wearer deal a flat amount less, after armor (CalculateDamageSystem).</summary>
public struct Vanguard : IComponentData
{
    public float DamageReduction;
}

/// <summary>Vigil (Mantle of the Watcher): each hit the wearer dodges heals it (CalculateDamageSystem).</summary>
public struct Vigil : IComponentData
{
    public float HealAmount;
}

/// <summary>Ambush (Ambusher): hits on targets at or above HealthThreshold of their max health always crit (AttackSystem.CalculateCrit).</summary>
public struct Ambush : IComponentData
{
    public float HealthThreshold;   // 1 = full health
}

/// <summary>Refractionist: using a mobility skill grants Refraction stacks (RefractionistSystem).</summary>
public struct Refractionist : IComponentData
{
    public int RefractionStacks;
    public int MaxStacks;
}

/// <summary>
/// Echoing Blade (Master Sword): while the resource is at least ResourceThreshold full, each melee attack also casts a
/// random equipped projectile skill toward the cursor, paying its cost (EchoingBladeSystem).
/// </summary>
public struct EchoingBlade : IComponentData
{
    public float ResourceThreshold;
}

/// <summary>
/// Elemental conversion (Pyromancy, Frostbound, Stormborn, Venomous): every skill hit and area of the wearer deals Type
/// damage instead, status effect included (ElementalConversionSystem sets it on the stats, InitializeFromSourceSystem converts).
/// </summary>
public struct ElementalConversion : IComponentData
{
    public DamageType Type;
}

/// <summary>
/// Event Horizon (Gravity Orb): the wearer's projectiles orbit it, up to MaxProjectiles; each new one pushes the oldest
/// out. Faster projectiles orbit further out (EventHorizonSystems).
/// </summary>
public struct EventHorizon : IComponentData
{
    public int MaxProjectiles;
    public float RadiusPerSpeed;    // Orbit radius = projectile speed * this, clamped to Min/MaxRadius
    public float MinRadius;
    public float MaxRadius;
    public float PullStrength;      // How hard a projectile is steered toward its radius
    public float TurnSpeed;         // Radians per second the orbit can turn a projectile
    public uint NextOrder;          // Capture counter: lower Orbiting.Order is older
}
