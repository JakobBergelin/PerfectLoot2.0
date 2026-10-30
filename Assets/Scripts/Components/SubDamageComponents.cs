using Unity.Entities;

/// <summary>Lightning hits charge the unit; LightningSystem releases the charge as a lightning attack around it.</summary>
public struct ChargedInstance : IBufferElementData
{
    public float Damage;
    public float Radius;
    public Entity Source;   // Who landed the lightning hit: the discharge is theirs and never hits them
}
