using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// A spawning enemy rising out of the ground. While it has this its AI doesn't act and UnitMoverSystem leaves it alone;
/// SpawnRiseSystem moves it up and removes this at the end.
/// </summary>
public struct SpawnRise : IComponentData
{
    public float TargetY;       // Where it ends up (its spawn position)
    public float Depth;         // How far below TargetY it starts
    public float Duration;
    public float Elapsed;
}

[UpdateInGroup(typeof(MovementSystemGroup))]
[UpdateBefore(typeof(EnemyMovementSystem))]
[BurstCompile]
public partial struct SpawnRiseSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state) => state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float deltaTime = SystemAPI.Time.DeltaTime;
        var commandBuffer = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);

        foreach (var (rise, transform, entity) in SystemAPI.Query<RefRW<SpawnRise>, RefRW<LocalTransform>>().WithEntityAccess())
        {
            rise.ValueRW.Elapsed += deltaTime;
            float t = rise.ValueRO.Duration > 0f ? math.saturate(rise.ValueRO.Elapsed / rise.ValueRO.Duration) : 1f;

            // Ease out: fast out of the ground, settling at the top
            float remaining = 1f - t;
            transform.ValueRW.Position.y = rise.ValueRO.TargetY - rise.ValueRO.Depth * remaining * remaining * remaining;

            if (t >= 1f) commandBuffer.RemoveComponent<SpawnRise>(entity);
        }
    }
}
