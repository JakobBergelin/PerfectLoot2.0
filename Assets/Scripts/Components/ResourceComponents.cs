using Unity.Entities;

public struct Resource : IComponentData
{
    public float MaxValue;
    public float CurrentValue;
    public float Regeneration;
    public float RegenerationMultiplier;
}

public struct ResourceBarUI : IComponentData
{
    public Entity PivotEntity;
    public Entity StickEntity;
}

public struct ResourceChanger : IBufferElementData
{    
    public float Value;
}