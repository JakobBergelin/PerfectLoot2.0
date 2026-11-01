using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;

/// <summary>
/// During a dungeon run, records every enemy that died this frame in the DungeonRun's DungeonKill buffer (once per
/// enemy), with where it died and whether it was the boss. Runs just before CheckDeathSystem, which removes the Enemy
/// tag or the entity.
/// </summary>
[UpdateInGroup(typeof(DamageSystemGroup), OrderLast = true)]
[UpdateBefore(typeof(CheckDeathSystem))]
partial struct EnemyKillSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<DungeonRun>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var killed = new NativeList<Entity>(Allocator.Temp);
        var kills = SystemAPI.GetSingletonBuffer<DungeonKill>();
        var bosses = SystemAPI.GetComponentLookup<DungeonBoss>(true);
        foreach (var (health, transform, entity) in SystemAPI.Query<RefRO<Health>, RefRO<LocalToWorld>>()
                     .WithAll<Enemy>().WithNone<KillCounted>().WithEntityAccess())
        {
            if (health.ValueRO.CurrentValue > 0f) continue;
            killed.Add(entity);
            kills.Add(new DungeonKill { Position = transform.ValueRO.Position, Boss = bosses.HasComponent(entity) });
        }
        if (!killed.IsEmpty) state.EntityManager.AddComponent<KillCounted>(killed.AsArray());
    }
}
