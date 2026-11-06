using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

[BurstCompile]
[UpdateInGroup(typeof(SkillSystemGroup))]
public partial class FistsOfThunderSystem : SystemBase
{
    private Unity.Mathematics.Random _random;

    private BeginInitializationEntityCommandBufferSystem _beginInitializationEntityCommandBufferSystem;
    [BurstCompile]
    protected override void OnCreate()
    {
        _random = Unity.Mathematics.Random.CreateFromIndex((uint)System.Environment.TickCount);
        _beginInitializationEntityCommandBufferSystem = World.DefaultGameObjectInjectionWorld.GetExistingSystemManaged<BeginInitializationEntityCommandBufferSystem>();
        RequireForUpdate<MovementConfig>();
    }

    [BurstCompile]
    protected override void OnUpdate()
    {
        var commandBuffer = _beginInitializationEntityCommandBufferSystem.CreateCommandBuffer();
        var movementConfig = SystemAPI.GetSingleton<MovementConfig>();
        foreach (var (fistsOfThunder, entity) in SystemAPI.Query<RefRW<FistsOfThunder>>().WithEntityAccess().WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
        {
            if(!SystemAPI.IsComponentEnabled<FistsOfThunder>(entity) || fistsOfThunder.ValueRO.PunchCount >= fistsOfThunder.ValueRO.Hits.Length)
            {
                // Released, or the combo got shorter (asset edited): start over
                fistsOfThunder.ValueRW.PunchCount = 0;
            }
        }

        foreach (var (fistOfThunder, localTransform, desiredDirection, stats, recovery, entity) in SystemAPI.Query<RefRW<FistsOfThunder>, RefRW<LocalTransform>, RefRO<DesiredDirection>, RefRO<CalculatedStats>, RefRW<AttackRecovery>>().WithEntityAccess())
        {
            bool facing = SkillUtil.TurnToward(ref localTransform.ValueRW, desiredDirection.ValueRO, movementConfig, SystemAPI.Time.DeltaTime);

            var hits = fistOfThunder.ValueRO.Hits;
            if(hits.Length == 0 || recovery.ValueRO.Remaining > 0f)
            {
                continue;
            }

            if(!facing)
            {
                continue;
            }

            // Punches as soon as it faces the cursor; the punch's Cooldown comes after (shared with the other repeated attacks)
            var hit = hits[fistOfThunder.ValueRO.PunchCount];
            var newEntity = commandBuffer.CreateEntity();

            var weaponDamage = stats.ValueRO.Value.WeaponDamage + _random.NextFloat(-stats.ValueRO.Value.WeaponDamageRange, stats.ValueRO.Value.WeaponDamageRange);
            var damage = hit.Damage + weaponDamage * fistOfThunder.ValueRO.WeaponDamageMultiplier;
            var position = localTransform.ValueRO.Position + desiredDirection.ValueRO.Direction * stats.ValueRO.Value.WeaponReach;
            commandBuffer.AddComponent(newEntity, new LocalTransform{Position = position, Rotation = Quaternion.identity, Scale = 1f});
            commandBuffer.AddComponent(newEntity, new Attack{Origin = DamageLabel.FistsOfThunder, Damage = damage, Type = DamageType.Lightning, Radius = fistOfThunder.ValueRO.Radius, DamageHierarchy = DamageHierarchy.Main, HitForce = fistOfThunder.ValueRO.HitForce});
            commandBuffer.AddComponent(newEntity, new Source{Entity = entity});
            commandBuffer.AddComponent(newEntity, new AttackTarget{TargetType = TargetType.Enemy});
            commandBuffer.AddComponent(newEntity, new InitializeFromSource{});
            // single frame damage
            commandBuffer.AddComponent(newEntity, new DestroyWithChildren{});

            fistOfThunder.ValueRW.PunchCount++;
            if(fistOfThunder.ValueRW.PunchCount >= hits.Length)
            {
                fistOfThunder.ValueRW.PunchCount = 0;
            }
            recovery.ValueRW.Remaining += hit.Cooldown;
            SystemAPI.GetBuffer<ResourceChanger>(entity).Add(new ResourceChanger{Value = fistOfThunder.ValueRO.ResourceGeneration});
            // Each punch is a use (tagged Melee, so it also raises MeleeAttackUsed)
            if(SystemAPI.HasBuffer<Skill>(entity)) SkillUtil.MarkUsed(SystemAPI.GetBuffer<Skill>(entity), SkillType.FistOfThunder);
        }
    }
}
