using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// The Self Destruct skill: when it goes off, a one-frame Attack of Radius around the unit, then the unit's health drops
/// to 0 so it dies the normal way this frame (experience, loot and death effects as for any death). An enemy's blast
/// hits the player side; a companion's is its owner's, scaled by their stats, and hits only enemies. Killed or frozen
/// during the telegraph, it never goes off (EnemySkillCastSystem).
/// </summary>
[UpdateInGroup(typeof(SkillSystemGroup))]
[UpdateAfter(typeof(EnemySkillCastSystem))]
[BurstCompile]
public partial struct EnemySelfDestructSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<EnemySelfDestruct>();
        state.RequireForUpdate<BeginInitializationEntityCommandBufferSystem.Singleton>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var commandBuffer = SystemAPI.GetSingleton<BeginInitializationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);

        foreach (var (selfDestruct, fires, transform, health, entity) in
                 SystemAPI.Query<RefRO<EnemySelfDestruct>, DynamicBuffer<EnemySkillFire>, RefRO<LocalTransform>, RefRW<Health>>().WithEntityAccess())
        {
            bool fired = false;
            foreach (var fire in fires)
                if (fire.Type == EnemySkillType.SelfDestruct) fired = true;
            if (!fired) continue;

            bool isCompanion = SystemAPI.HasComponent<Companion>(entity);
            var source = isCompanion ? SystemAPI.GetComponent<Companion>(entity).Owner : entity;
            var s = selfDestruct.ValueRO;

            var blast = commandBuffer.CreateEntity();
            commandBuffer.AddComponent(blast, new LocalTransform { Position = transform.ValueRO.Position, Rotation = quaternion.identity, Scale = 1f });
            commandBuffer.AddComponent(blast, new Attack { Origin = new DamageOrigin { Unit = entity }, Damage = s.Damage, Type = s.Type, Radius = s.Radius, DamageHierarchy = DamageHierarchy.Main, HitForce = s.HitForce });
            commandBuffer.AddComponent(blast, new Source { Entity = source });
            commandBuffer.AddComponent(blast, new AttackTarget { TargetType = isCompanion ? TargetType.Enemy : TargetType.Player });
            if (isCompanion) commandBuffer.AddComponent(blast, new InitializeFromSource());
            commandBuffer.AddComponent(blast, new DestroyWithChildren());   // A single-frame hit

            health.ValueRW.CurrentValue = 0f;
        }
    }
}
