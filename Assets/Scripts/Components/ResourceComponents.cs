using Unity.Entities;

public struct Resource : IComponentData
{
    public float MaxValue;
    public float CurrentValue;
    public float Regeneration;
    public float RegenerationMultiplier;
}

/// <summary>A unit's resource bar, like HealthBarUI.</summary>
public struct ResourceBarUI : IComponentData
{
    public Entity BarEntity;
    public Entity StickEntity;
}

public struct ResourceChanger : IBufferElementData
{    
    public float Value;
}