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
        foreach (var (fistsOfThunder, stats, entity) in SystemAPI.Query<RefRW<FistsOfThunder>, RefRO<CalculatedStats>>().WithEntityAccess().WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
        {
            var hits = fistsOfThunder.ValueRO.Hits;
            if(!SystemAPI.IsComponentEnabled<FistsOfThunder>(entity) || fistsOfThunder.ValueRO.PunchCount >= hits.Length)
            {
                // Released, or the combo got shorter (asset edited): start over
                fistsOfThunder.ValueRW.PunchCount = 0;
                fistsOfThunder.ValueRW.TimeSinceLastPunch = 0.0f;
            }
            if(hits.Length == 0)
            {
                continue;
            }
            fistsOfThunder.ValueRW.TimeSinceLastPunch += SystemAPI.Time.DeltaTime * stats.ValueRO.Value.AttackSpeed;
            fistsOfThunder.ValueRW.TimeSinceLastPunch = Mathf.Min(fistsOfThunder.ValueRW.TimeSinceLastPunch, hits[fistsOfThunder.ValueRO.PunchCount].Cooldown);
        }

        foreach (var (fistOfThunder, localTransform, desiredDirection, stats, entity) in SystemAPI.Query<RefRW<FistsOfThunder>, RefRW<LocalTransform>, RefRO<DesiredDirection>, RefRO<CalculatedStats>>().WithEntityAccess())
        {
            localTransform.ValueRW.Rotation = Quaternion.RotateTowards(localTransform.ValueRO.Rotation, desiredDirection.ValueRO.Quaternion, movementConfig.RotationSpeed * SystemAPI.Time.DeltaTime);

            if(Quaternion.Angle(localTransform.ValueRW.Rotation, desiredDirection.ValueRO.Quaternion) > (math.PI / 2))
            {
                continue;
            }

            var hits = fistOfThunder.ValueRO.Hits;
            if(hits.Length == 0)
            {
                continue;
            }

            var hit = hits[fistOfThunder.ValueRO.PunchCount];
            if(fistOfThunder.ValueRO.TimeSinceLastPunch >= hit.Cooldown)
            {
                var newEntity = commandBuffer.CreateEntity();

                var weaponDamage = stats.ValueRO.Value.WeaponDamage + _random.NextFloat(-stats.ValueRO.Value.WeaponDamageRange, stats.ValueRO.Value.WeaponDamageRange);
                var damage = hit.Damage + weaponDamage * fistOfThunder.ValueRO.WeaponDamageMultiplier;
                var position = localTransform.ValueRO.Position + desiredDirection.ValueRO.Direction * stats.ValueRO.Value.WeaponReach;
                commandBuffer.AddComponent(newEntity, new LocalTransform{Position = position, Rotation = Quaternion.identity, Scale = 1f});
                commandBuffer.AddComponent(newEntity, new Attack{Damage = damage, Type = DamageType.Lightning, Radius = fistOfThunder.ValueRO.Radius, DamageHierarchy = DamageHierarchy.Main, HitForce = fistOfThunder.ValueRO.HitForce});
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
                fistOfThunder.ValueRW.TimeSinceLastPunch = 0.0f;
                SystemAPI.GetBuffer<ResourceChanger>(entity).Add(new ResourceChanger{Value = fistOfThunder.ValueRO.ResourceGeneration});
            }
        }
    }
}
