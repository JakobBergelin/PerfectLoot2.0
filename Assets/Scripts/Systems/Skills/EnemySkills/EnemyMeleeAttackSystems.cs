using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

public static class EnemyMeleeUtil
{
    /// <summary>One melee hit: a single-frame Attack with half of reach as its radius, that far in front of the unit.</summary>
    public static Entity SpawnHit(EntityCommandBuffer commandBuffer, Entity unit, Entity source, TargetType targetType, float3 position, float3 direction, float reach, float damage, DamageType type, float hitForce, float criticalChanceBonus = 0f)
    {
        var newEntity = commandBuffer.CreateEntity();

        commandBuffer.AddComponent(newEntity, new LocalTransform { Position = position + direction * reach / 2, Rotation = quaternion.identity, Scale = 1f });
        commandBuffer.AddComponent(newEntity, new Attack { Origin = new DamageOrigin { Unit = unit }, Damage = damage, Type = type, Radius = reach / 2, DamageHierarchy = DamageHierarchy.Main, HitForce = hitForce, CriticalChanceBonus = criticalChanceBonus });
        commandBuffer.AddComponent(newEntity, new EnemyMeleeHit { });
        commandBuffer.AddComponent(newEntity, new Source { Entity = source });
        commandBuffer.AddComponent(newEntity, new AttackTarget { TargetType = targetType });
        // single frame damage
        commandBuffer.AddComponent(newEntity, new DestroyWithChildren { });
        return newEntity;
    }
}

/// <summary>The melee skill: one hit in front of the unit when it goes off. An enemy's hits only players; a companion's is its owner's, scaled by their stats, and hits only enemies.</summary>
[UpdateInGroup(typeof(SkillSystemGroup))]
[UpdateAfter(typeof(EnemySkillCastSystem))]
[BurstCompile]
public partial struct EnemyMeleeSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<EnemyMelee>();
        state.RequireForUpdate<BeginInitializationEntityCommandBufferSystem.Singleton>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var commandBuffer = SystemAPI.GetSingleton<BeginInitializationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
        var buffs = SystemAPI.GetComponentLookup<PackInstinctBuff>(true);
        state.CompleteDependency();  // The lookup is used here on the main thread

        foreach (var (melee, fires, transform, entity) in SystemAPI.Query<RefRO<EnemyMelee>, DynamicBuffer<EnemySkillFire>, RefRO<LocalTransform>>().WithEntityAccess())
        {
            bool isCompanion = SystemAPI.HasComponent<Companion>(entity);
            var source = isCompanion ? SystemAPI.GetComponent<Companion>(entity).Owner : entity;
            var targetType = isCompanion ? TargetType.Enemy : TargetType.Player;
            float critBonus = isCompanion ? CompanionUtil.CriticalChanceBonus(buffs, entity, source) : 0f;

            foreach (var fire in fires)
            {
                if (fire.Type != EnemySkillType.Melee) continue;
                var hit = EnemyMeleeUtil.SpawnHit(commandBuffer, entity, source, targetType, transform.ValueRO.Position, fire.Direction, melee.ValueRO.Reach, melee.ValueRO.Damage, melee.ValueRO.Type, melee.ValueRO.HitForce, critBonus);
                if (isCompanion) commandBuffer.AddComponent(hit, new InitializeFromSource { });
            }
        }
    }
}

/// <summary>Swarm enemies' cheap fixed melee attack: no skill list, just a timer while they're in range.</summary>
[BurstCompile]
[UpdateInGroup(typeof(SkillSystemGroup))]
public partial class EnemyMeleeAttackSystem : SystemBase
{
    private BeginInitializationEntityCommandBufferSystem _beginInitializationEntityCommandBufferSystem;
    [BurstCompile]
    protected override void OnCreate()
    {
        _beginInitializationEntityCommandBufferSystem = World.GetExistingSystemManaged<BeginInitializationEntityCommandBufferSystem>();
    }

    [BurstCompile]
    protected override void OnUpdate()
    {
        var commandBuffer = _beginInitializationEntityCommandBufferSystem.CreateCommandBuffer();
        foreach (var (enemyMeleeAttack, npcBehavior, stats, entity) in SystemAPI.Query<RefRW<EnemyMeleeAttack>, RefRO<NpcBehavior>, RefRO<CalculatedStats>>().WithEntityAccess().WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
        {
            if (!npcBehavior.ValueRO.IsAttacking)
            {
                enemyMeleeAttack.ValueRW.Elapsed = 0;
            }
            enemyMeleeAttack.ValueRW.Elapsed += SystemAPI.Time.DeltaTime * stats.ValueRO.Value.AttackSpeed;
            enemyMeleeAttack.ValueRW.Elapsed = Mathf.Min(enemyMeleeAttack.ValueRW.Elapsed, enemyMeleeAttack.ValueRO.AttackSpeed);
        }

        foreach (var (enemyMeleeAttack, localTransform, npcBehavior, entity) in SystemAPI.Query<RefRW<EnemyMeleeAttack>, RefRW<LocalTransform>, RefRW<NpcBehavior>>().WithEntityAccess())
        {
            // Frozen and similar, or still rising out of the ground: no turning or attacking
            if (SystemAPI.HasComponent<SpawnRise>(entity) ||
                (SystemAPI.HasComponent<MovementModifiers>(entity) && SystemAPI.GetComponent<MovementModifiers>(entity).PreventsActions))
            {
                continue;
            }

            localTransform.ValueRW.Rotation = Quaternion.RotateTowards(localTransform.ValueRO.Rotation, npcBehavior.ValueRO.DesiredDirection, enemyMeleeAttack.ValueRO.TurnSpeed * SystemAPI.Time.DeltaTime);

            if (Quaternion.Angle(localTransform.ValueRO.Rotation, npcBehavior.ValueRO.DesiredDirection) > enemyMeleeAttack.ValueRO.FacingAngle)
            {
                continue;
            }

            if (enemyMeleeAttack.ValueRO.Elapsed >= enemyMeleeAttack.ValueRO.AttackSpeed && npcBehavior.ValueRO.IsAttacking)
            {
                EnemyMeleeUtil.SpawnHit(commandBuffer, entity, entity, TargetType.Player, localTransform.ValueRO.Position, npcBehavior.ValueRO.Direction, npcBehavior.ValueRO.AttackRadius, enemyMeleeAttack.ValueRO.Damage, DamageType.Physical, 0f);
                enemyMeleeAttack.ValueRW.Elapsed = 0.0f;
                npcBehavior.ValueRW.AttackCount++;
            }
        }
    }
}
