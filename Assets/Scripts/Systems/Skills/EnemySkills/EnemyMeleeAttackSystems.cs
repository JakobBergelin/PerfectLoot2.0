using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

[BurstCompile]
[UpdateInGroup(typeof(SkillSystemGroup))]
public partial class EnemyMeleeAttackSystem : SystemBase
{
    private BeginInitializationEntityCommandBufferSystem _beginInitializationEntityCommandBufferSystem;
    [BurstCompile]
    protected override void OnCreate()
    {
        _beginInitializationEntityCommandBufferSystem = World.DefaultGameObjectInjectionWorld.GetExistingSystemManaged<BeginInitializationEntityCommandBufferSystem>();
        RequireForUpdate<MovementConfig>();
    }

    [BurstCompile]
    protected override void OnUpdate()
    {
        var commandBuffer = _beginInitializationEntityCommandBufferSystem.CreateCommandBuffer();
        var movementConfig = SystemAPI.GetSingleton<MovementConfig>();
        foreach (var (enemyMeleeAttack, npcBehavior, stats, entity) in SystemAPI.Query<RefRW<EnemyMeleeAttack>, RefRO<NpcBehavior>, RefRO<CalculatedStats>>().WithEntityAccess().WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
        {
            if (!npcBehavior.ValueRO.IsAttacking)
            {
                enemyMeleeAttack.ValueRW.Elapsed = 0;
            }
            enemyMeleeAttack.ValueRW.Elapsed += SystemAPI.Time.DeltaTime * stats.ValueRO.Value.AttackSpeed;
            enemyMeleeAttack.ValueRW.Elapsed = Mathf.Min(enemyMeleeAttack.ValueRW.Elapsed, enemyMeleeAttack.ValueRO.AttackSpeed);
        }

        foreach (var (enemyMeleeAttack, localTransform, npcBehavior, entity) in SystemAPI.Query<RefRW<EnemyMeleeAttack>, RefRW<LocalTransform>, RefRO<NpcBehavior>>().WithEntityAccess())
        {
            // Frozen and similar: no turning or attacking
            if (SystemAPI.HasComponent<MovementModifiers>(entity) && SystemAPI.GetComponent<MovementModifiers>(entity).PreventsActions)
            {
                continue;
            }

            localTransform.ValueRW.Rotation = Quaternion.RotateTowards(localTransform.ValueRO.Rotation, npcBehavior.ValueRO.DesiredDirection, movementConfig.RotationSpeed * SystemAPI.Time.DeltaTime);

            if (Quaternion.Angle(localTransform.ValueRW.Rotation, npcBehavior.ValueRO.DesiredDirection) > (math.PI / 2))
            {
                continue;
            }

            if (enemyMeleeAttack.ValueRO.Elapsed >= enemyMeleeAttack.ValueRO.AttackSpeed && npcBehavior.ValueRO.IsAttacking)
            {
                var newEntity = commandBuffer.CreateEntity();

                commandBuffer.AddComponent(newEntity, new LocalTransform { Position = localTransform.ValueRO.Position + npcBehavior.ValueRO.Direction * npcBehavior.ValueRO.AttackRadius/2, Rotation = Quaternion.identity, Scale = 1f });
                commandBuffer.AddComponent(newEntity, new Attack { Damage = enemyMeleeAttack.ValueRO.Damage, Type = DamageType.Physical, Radius = npcBehavior.ValueRO.AttackRadius/2, DamageHierarchy = DamageHierarchy.Main });
                commandBuffer.AddComponent(newEntity, new Source { Entity = entity });
                commandBuffer.AddComponent(newEntity, new AttackTarget { TargetType = TargetType.Player });
                // single frame damage
                commandBuffer.AddComponent(newEntity, new DestroyWithChildren { });

                enemyMeleeAttack.ValueRW.Elapsed = 0.0f;
            }
        }
    }
}
