using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// Unstoppable: a wearer with at least EnemyCount living enemies within Radius is Active. While Active its chill is
/// cleared, so it isn't slowed and doesn't freeze from chill. Before ChillSystem turns full chill into Frozen and before
/// EffectMovementSystem works out the slow.
/// </summary>
[UpdateInGroup(typeof(EffectSystemGroup))]
[UpdateBefore(typeof(ChillSystem))]
[UpdateBefore(typeof(EffectMovementSystem))]
[BurstCompile]
public partial struct UnstoppableSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state) => state.RequireForUpdate<Unstoppable>();

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var enemies = SystemAPI.QueryBuilder().WithAll<Enemy, LocalTransform>().WithNone<RigidDeath>().Build()
            .ToComponentDataArray<LocalTransform>(Allocator.Temp);
        var chilled = SystemAPI.GetComponentLookup<Chilled>();

        foreach (var (unstoppable, transform, entity) in SystemAPI.Query<RefRW<Unstoppable>, RefRO<LocalTransform>>().WithEntityAccess())
        {
            ref var u = ref unstoppable.ValueRW;
            float radiusSq = u.Radius * u.Radius;
            int near = 0;
            for (int i = 0; i < enemies.Length && near < u.EnemyCount; i++)
                if (math.distancesq(enemies[i].Position.xz, transform.ValueRO.Position.xz) <= radiusSq) near++;
            u.Active = near >= u.EnemyCount;

            if (u.Active && chilled.HasComponent(entity) && chilled.IsComponentEnabled(entity))
                chilled.SetComponentEnabled(entity, false);
        }
    }
}

/// <summary>Unstoppable: hits on an Active wearer don't push it. Between working out the damage and dealing it (which applies the push).</summary>
[UpdateInGroup(typeof(DamageSystemGroup))]
[UpdateAfter(typeof(CalculateDamageSystem))]
[UpdateBefore(typeof(DamagingSystem))]
[BurstCompile]
public partial struct UnstoppableKnockbackSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state) => state.RequireForUpdate<Unstoppable>();

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (unstoppable, taken) in SystemAPI.Query<RefRO<Unstoppable>, DynamicBuffer<CalculatedDamageInstance>>())
        {
            if (!unstoppable.ValueRO.Active) continue;
            for (int i = 0; i < taken.Length; i++) taken.ElementAt(i).HitForce = 0f;
        }
    }
}
