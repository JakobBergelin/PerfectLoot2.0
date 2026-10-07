using Unity.Entities;

public struct Chilled : IComponentData, IEnableableComponent
{
    public float Value;
}

public struct Frozen : IComponentData, IEnableableComponent
{
    public float TimeRemaining;
}

public struct BurnInstance : IBufferElementData
{
    public float DamagePerSecond;
    public float TimeRemaining;
}

public struct Burning : IComponentData, IEnableableComponent
{
    public float DamageToDisplay;
    public float DisplayCounter;
}

public struct BleedInstance : IBufferElementData
{
    public float TimeRemaining;
}

public struct Bleeding : IComponentData, IEnableableComponent
{
    public float DamageToDisplay;
    public float DisplayCounter;
}

public struct Poisioned : IComponentData, IEnableableComponent
{
    public float DisplayCounter;
    public float TimeRemaining;
}

public struct ChargedInstance : IBufferElementData
{
    public float Damage;
    public float Radius;
}