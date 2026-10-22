using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

/// <summary>
/// Pushes units that were hit and lets the push die out. Physics bodies get it added to their velocity after the
/// movement systems have written theirs, so walls and other bodies stop the push. Units without a body are moved directly.
/// </summary>
[UpdateInGroup(typeof(DamageSystemGroup))]
[UpdateAfter(typeof(DamagingSystem))]
[BurstCompile]
public partial struct KnockbackSystem : ISystem
{
    const float StopSpeed = 0.01f;

    [BurstCompile]
    public void OnCreate(ref SystemState state) => state.RequireForUpdate<CombatConfig>();

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float deltaTime = SystemAPI.Time.DeltaTime;
        float decay = math.exp(-SystemAPI.GetSingleton<CombatConfig>().KnockbackDamping * deltaTime);

        foreach (var (knockback, localTransform, entity) in SystemAPI.Query<RefRW<Knockback>, RefRW<LocalTransform>>().WithEntityAccess())
        {
            var velocity = knockback.ValueRO.Velocity;
            if (math.lengthsq(velocity) < StopSpeed * StopSpeed)
            {
                knockback.ValueRW.Velocity = float3.zero;
                continue;
            }

            // Horizontal only, so a push never lifts a unit or sinks it into the ground
            velocity.y = 0f;
            if (SystemAPI.HasComponent<PhysicsVelocity>(entity))
            {
                var physicsVelocity = SystemAPI.GetComponentRW<PhysicsVelocity>(entity);
                physicsVelocity.ValueRW.Linear += velocity;
            }
            else
            {
                localTransform.ValueRW.Position += velocity * deltaTime;
            }
            knockback.ValueRW.Velocity = velocity * decay;
        }
    }
}
