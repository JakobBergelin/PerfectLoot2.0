using Unity.Entities;

// Components added by legendary powers (LegendaryPower<T>) while the item is equipped, for powers that are only data.

/// <summary>Vanguard: main physical hits on the wearer deal a flat amount less, after armor (CalculateDamageSystem).</summary>
public struct Vanguard : IComponentData
{
    public float DamageReduction;
}

/// <summary>Refractionist: using a mobility skill grants Refraction stacks (RefractionistSystem).</summary>
public struct Refractionist : IComponentData
{
    public int RefractionStacks;
    public int MaxStacks;
}
