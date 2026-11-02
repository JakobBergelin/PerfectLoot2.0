using Unity.Entities;

/// <summary>
/// A lingering patch of ground that damages enemies standing in it (HazardSystem), with a Source and a LocalTransform.
/// Hazards of the same source and type don't stack: an enemy in several of them is hit as if it stood in one.
/// </summary>
public struct Hazard : IComponentData
{
    public DamageType Type;         // Already converted by the source's stats
    public float DamagePerSecond;   // The source's damage % already applied
    public float Radius;            // The source's area of effect already applied
    public float TimeRemaining;
}
