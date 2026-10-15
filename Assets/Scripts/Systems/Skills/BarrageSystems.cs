using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.VisualScripting;
using UnityEngine;

[BurstCompile]
[UpdateInGroup(typeof(SkillSystemGroup))]
public partial class BarrageSystem : SystemBase
{

    private BeginInitializationEntityCommandBufferSystem _beginInitializationEntityCommandBufferSystem;
    [BurstCompile]
    protected override void OnCreate()
    {
        _beginInitializationEntityCommandBufferSystem = World.DefaultGameObjectInjectionWorld.GetExistingSystemManaged<BeginInitializationEntityCommandBufferSystem>();
    }

    [BurstCompile]
    protected override void OnUpdate()
    {
        var commandBuffer = _beginInitializationEntityCommandBufferSystem.CreateCommandBuffer();
        // SkillState: no cooldown or cost to report; IsActive set by SkillActivationSystem

        foreach (var barrage in SystemAPI.Query<RefRW<Barrage>>().WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
        {
            barrage.ValueRW.Elapsed += SystemAPI.Time.DeltaTime;
            barrage.ValueRW.Elapsed = Mathf.Min(barrage.ValueRW.Elapsed, barrage.ValueRW.AttackSpeed);
        }

        foreach (var (barrage, localTransform, entity) in SystemAPI.Query<RefRW<Barrage>, RefRO<LocalTransform>>().WithEntityAccess())
        {
            if(barrage.ValueRO.Elapsed >= barrage.ValueRO.AttackSpeed)
            {
                var newEntity = commandBuffer.CreateEntity();
                
                commandBuffer.AddComponent(newEntity, new LocalTransform{Position = localTransform.ValueRO.Position + new float3(0, 1, 0), Rotation = Quaternion.identity, Scale = 0.5f});
                commandBuffer.AddComponent(newEntity, new Projectile{Direction = new float3(0,1,0), Speed = barrage.ValueRO.ProjectileSpeed, DestroyOnHit = true, BounceCount = 0, PierceCount = 0});
                commandBuffer.AddComponent(newEntity, new Attack{Damage = barrage.ValueRO.Damage, Type = DamageType.Physical, DamageHierarchy = DamageHierarchy.Main, Radius = barrage.ValueRO.Radius});
                commandBuffer.AddComponent(newEntity, new HomingProjectile{TurnSpeed = barrage.ValueRO.HomingTurnSpeed, TurnAcceleration = barrage.ValueRO.HomingTurnAcceleration, HomingType = HomingType.Strongest});
                commandBuffer.AddComponent(newEntity, new Source{Entity = entity});
                commandBuffer.AddComponent(newEntity, new AttackTarget{TargetType = TargetType.Enemy});
                commandBuffer.AddComponent(newEntity, new InitializeFromSource{});

                barrage.ValueRW.Elapsed = 0.0f;
            }
        }
    }
}
