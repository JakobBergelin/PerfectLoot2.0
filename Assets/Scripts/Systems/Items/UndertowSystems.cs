using Unity.Burst;
using Unity.Entities;

/// <summary>Undertow legendary power, added by UndertowPower while the item is equipped.</summary>
public struct Undertow : IComponentData
{
    public float PullStrength;  // Times the hit's knockback, 2 = pulls twice as hard as it would have pushed
}

/// <summary>
/// Undertow: the wearer's hits on enemies pull them toward where the hit landed instead of pushing them away. A negative
/// HitForce rather than a flipped HitDirection, so killing blows still fall the same way. Hits without knockback don't
/// pull. Between working out the damage and dealing it.
/// </summary>
[UpdateInGroup(typeof(DamageSystemGroup))]
[UpdateAfter(typeof(CalculateDamageSystem))]
[UpdateBefore(typeof(DamagingSystem))]
[BurstCompile]
public partial struct UndertowSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state) => state.RequireForUpdate<Undertow>();

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var undertows = SystemAPI.GetComponentLookup<Undertow>(true);

        foreach (var taken in SystemAPI.Query<DynamicBuffer<CalculatedDamageInstance>>().WithAll<Enemy>())
        {
            for (int i = 0; i < taken.Length; i++)
            {
                ref var instance = ref taken.ElementAt(i);
                if (undertows.HasComponent(instance.Source)) instance.HitForce *= -undertows[instance.Source].PullStrength;
            }
        }
    }
}
