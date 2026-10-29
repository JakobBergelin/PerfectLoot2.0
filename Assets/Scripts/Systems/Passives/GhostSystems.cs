using Unity.Burst;
using Unity.Entities;

/// <summary>Ghost: Phased exactly while Invulnerable. Before UnitMoverSystem, so it applies the same frame.</summary>
[BurstCompile]
[UpdateInGroup(typeof(MovementSystemGroup))]
[UpdateBefore(typeof(UnitMoverSystem))]
public partial struct GhostSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (invulnerable, phased) in SystemAPI.Query<EnabledRefRO<Invulnerable>, EnabledRefRW<Phased>>()
                     .WithAll<Ghost>().WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
            phased.ValueRW = invulnerable.ValueRO;
    }
}
