using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

/// <summary>
/// Lets the push of units that were hit die out. UnitMoverSystem moves units by it (so walls, water and edges stop it);
/// anything without UnitMotion is moved directly here.
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

            // Horizontal only, so a push never lifts a unit or sinks it into the ground. Units are moved by
            // UnitMoverSystem (walls, water and edges stop the push); anything else is moved directly.
            velocity.y = 0f;
            if (!SystemAPI.HasComponent<UnitMotion>(entity))
                localTransform.ValueRW.Position += velocity * deltaTime;
            knockback.ValueRW.Velocity = velocity * decay;
        }
    }
}
