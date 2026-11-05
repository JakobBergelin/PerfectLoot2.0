using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>Enemy projectile skill: one projectile toward where the target was. An enemy's hits only players; a companion's is its owner's and hits only enemies.</summary>
[UpdateInGroup(typeof(SkillSystemGroup))]
[UpdateAfter(typeof(EnemySkillCastSystem))]
[BurstCompile]
public partial struct EnemyProjectileSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<EnemyProjectile>();
        state.RequireForUpdate<BeginInitializationEntityCommandBufferSystem.Singleton>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var commandBuffer = SystemAPI.GetSingleton<BeginInitializationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);

        foreach (var (projectile, fires, transform, entity) in SystemAPI.Query<RefRO<EnemyProjectile>, DynamicBuffer<EnemySkillFire>, RefRO<LocalTransform>>().WithEntityAccess())
        {
            bool isCompanion = SystemAPI.HasComponent<Companion>(entity);
            var source = isCompanion ? SystemAPI.GetComponent<Companion>(entity).Owner : entity;
            var targetType = isCompanion ? TargetType.Enemy : TargetType.Player;

            foreach (var fire in fires)
            {
                if (fire.Type == EnemySkillType.Projectile)
                    Spawn(commandBuffer, source, targetType, projectile.ValueRO, transform.ValueRO.Position + fire.Direction, fire.Direction);
            }
        }
    }

    static void Spawn(EntityCommandBuffer commandBuffer, Entity source, TargetType targetType, in EnemyProjectile skill, float3 position, float3 direction)
    {
        var newEntity = commandBuffer.CreateEntity();

        commandBuffer.AddComponent(newEntity, new LocalTransform { Position = position, Rotation = quaternion.identity, Scale = 0.5f });
        commandBuffer.AddComponent(newEntity, new Projectile { Direction = direction, Speed = skill.ProjectileSpeed, DestroyOnHit = true, FollowGround = true });
        commandBuffer.AddComponent(newEntity, new ProjectileRange { MaxDistance = skill.MaxDistance, LastPosition = position });
        commandBuffer.AddComponent(newEntity, new Attack { Damage = skill.Damage, Type = skill.Type, DamageHierarchy = DamageHierarchy.Main, Radius = skill.Radius, HitForce = skill.HitForce });
        commandBuffer.AddComponent(newEntity, new Source { Entity = source });
        commandBuffer.AddComponent(newEntity, new AttackTarget { TargetType = targetType });
        commandBuffer.AddComponent(newEntity, new InitializeFromSource { });
    }
}
