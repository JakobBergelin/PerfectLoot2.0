using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

/// <summary>
/// A spawning enemy rising out of the ground. While it has this it is immovable (InverseMass 0) and weightless, and
/// its AI doesn't act; SpawnRiseSystem moves it up and restores its mass and gravity at the end.
/// </summary>
public struct SpawnRise : IComponentData
{
    public float TargetY;       // Where it ends up (its spawn position)
    public float Depth;         // How far below TargetY it starts
    public float Duration;
    public float Elapsed;
    public float InverseMass;   // Restored when it has risen
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

        foreach (var (rise, transform, velocity, mass, entity) in SystemAPI
                     .Query<RefRW<SpawnRise>, RefRW<LocalTransform>, RefRW<PhysicsVelocity>, RefRW<PhysicsMass>>().WithEntityAccess())
        {
            rise.ValueRW.Elapsed += deltaTime;
            float t = rise.ValueRO.Duration > 0f ? math.saturate(rise.ValueRO.Elapsed / rise.ValueRO.Duration) : 1f;

            // Ease out: fast out of the ground, settling at the top
            float remaining = 1f - t;
            transform.ValueRW.Position.y = rise.ValueRO.TargetY - rise.ValueRO.Depth * remaining * remaining * remaining;
            velocity.ValueRW.Linear = float3.zero;
            velocity.ValueRW.Angular = float3.zero;

            if (t < 1f) continue;

            // Risen: a normal physics body again
            mass.ValueRW.InverseMass = rise.ValueRO.InverseMass;
            if (SystemAPI.HasComponent<PhysicsGravityFactor>(entity))
                SystemAPI.SetComponent(entity, new PhysicsGravityFactor { Value = 1f });
            commandBuffer.RemoveComponent<SpawnRise>(entity);
        }
    }
}
