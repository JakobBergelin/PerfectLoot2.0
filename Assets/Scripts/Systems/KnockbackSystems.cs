using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// Moves units that were pushed by a hit and lets the push die out. Moves the position directly instead of
/// PhysicsVelocity, because the movement systems overwrite the velocity every frame.
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

        foreach (var (knockback, localTransform) in SystemAPI.Query<RefRW<Knockback>, RefRW<LocalTransform>>())
        {
            var velocity = knockback.ValueRO.Velocity;
            if (math.lengthsq(velocity) < StopSpeed * StopSpeed)
            {
                knockback.ValueRW.Velocity = float3.zero;
                continue;
            }

            // Horizontal only, so a push never lifts a unit or sinks it into the ground
            velocity.y = 0f;
            localTransform.ValueRW.Position += velocity * deltaTime;
            knockback.ValueRW.Velocity = velocity * decay;
        }
    }
}
