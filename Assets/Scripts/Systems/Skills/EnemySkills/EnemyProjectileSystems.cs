using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>Enemy projectile skill: one projectile toward where the target was, or a burst of Count, ShotInterval apart, sweeping from SweepAngle left of it to SweepAngle right. An enemy's hits only players; a companion's is its owner's and hits only enemies.</summary>
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
        var buffs = SystemAPI.GetComponentLookup<PackInstinctBuff>(true);
        state.CompleteDependency();  // The lookup is used here on the main thread

        float deltaTime = SystemAPI.Time.DeltaTime;
        foreach (var (projectile, fires, transform, entity) in SystemAPI.Query<RefRW<EnemyProjectile>, DynamicBuffer<EnemySkillFire>, RefRO<LocalTransform>>().WithEntityAccess())
        {
            bool isCompanion = SystemAPI.HasComponent<Companion>(entity);
            var source = isCompanion ? SystemAPI.GetComponent<Companion>(entity).Owner : entity;
            var targetType = isCompanion ? TargetType.Enemy : TargetType.Player;
            float critBonus = isCompanion ? CompanionUtil.CriticalChanceBonus(buffs, entity, source) : 0f;

            ref var p = ref projectile.ValueRW;
            foreach (var fire in fires)
            {
                if (fire.Type != EnemySkillType.Projectile) continue;
                if (p.Count <= 1) Spawn(commandBuffer, entity, source, targetType, p, transform.ValueRO.Position + fire.Direction, fire.Direction, critBonus);
                else
                {
                    // A burst starts: its shots follow ShotInterval apart, sweeping across where the target was now
                    p.ShotsFired = 0;
                    p.ShotTimer = 0f;
                    p.AimDirection = fire.Direction;
                }
            }

            if (p.Count <= 1 || p.ShotsFired >= p.Count) continue;
            p.ShotTimer -= deltaTime;
            while (p.ShotTimer <= 0f && p.ShotsFired < p.Count)
            {
                // Negative turns left: the first shot left of the aim, the last right of it
                float angle = -p.SweepAngle + 2f * p.SweepAngle * p.ShotsFired / (p.Count - 1);
                var direction = math.rotate(quaternion.RotateY(angle), p.AimDirection);
                Spawn(commandBuffer, entity, source, targetType, p, transform.ValueRO.Position + direction, direction, critBonus);
                p.ShotsFired++;
                p.ShotTimer += p.ShotInterval;
            }
        }
    }

    static void Spawn(EntityCommandBuffer commandBuffer, Entity unit, Entity source, TargetType targetType, in EnemyProjectile skill, float3 position, float3 direction, float criticalChanceBonus)
    {
        var newEntity = commandBuffer.CreateEntity();

        commandBuffer.AddComponent(newEntity, new LocalTransform { Position = position, Rotation = quaternion.identity, Scale = 0.5f });
        commandBuffer.AddComponent(newEntity, new Projectile { Direction = direction, Speed = skill.ProjectileSpeed, DestroyOnHit = true, FollowGround = true });
        commandBuffer.AddComponent(newEntity, new ProjectileRange { MaxDistance = skill.MaxDistance, LastPosition = position });
        commandBuffer.AddComponent(newEntity, new Attack { Origin = new DamageOrigin { Unit = unit }, Damage = skill.Damage, Type = skill.Type, DamageHierarchy = DamageHierarchy.Main, Radius = skill.Radius, HitForce = skill.HitForce, CriticalChanceBonus = criticalChanceBonus });
        commandBuffer.AddComponent(newEntity, new Source { Entity = source });
        commandBuffer.AddComponent(newEntity, new AttackTarget { TargetType = targetType });
        commandBuffer.AddComponent(newEntity, new InitializeFromSource { });
    }
}
