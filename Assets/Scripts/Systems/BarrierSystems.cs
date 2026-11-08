using Unity.Burst;
using Unity.Entities;

/// <summary>Counts barriers down; one that runs out falls off with whatever it had left.</summary>
[UpdateInGroup(typeof(HealthAndResourceSystemGroup))]
[BurstCompile]
public partial struct BarrierSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float deltaTime = SystemAPI.Time.DeltaTime;
        foreach (var barrier in SystemAPI.Query<RefRW<Barrier>>())
        {
            if (barrier.ValueRO.Value <= 0f) continue;
            barrier.ValueRW.TimeRemaining -= deltaTime;
            if (barrier.ValueRO.TimeRemaining <= 0f) barrier.ValueRW = default;
        }
    }
}

/// <summary>Clears last frame's dodged and blocked hits before this frame's damage is worked out.</summary>
[UpdateInGroup(typeof(DamageSystemGroup), OrderFirst = true)]
[BurstCompile]
public partial struct HitsAvoidedResetSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var avoided in SystemAPI.Query<RefRW<HitsAvoided>>())
            avoided.ValueRW.Count = 0;
    }
}
