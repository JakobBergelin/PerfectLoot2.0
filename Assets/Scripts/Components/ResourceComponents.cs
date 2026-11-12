using Unity.Entities;
using Unity.NetCode;

public struct Resource : IComponentData
{
    [GhostField(Quantization = 100)] public float MaxValue;          // Replicated to clients (SPIKE)
    [GhostField(Quantization = 100)] public float CurrentValue;
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