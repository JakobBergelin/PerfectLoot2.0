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
        foreach (var skillBuffer in SystemAPI.Query<DynamicBuffer<Skill>>().WithPresent<Barrage>().WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
        {
            int index = -1;
            if (SkillUtil.FindSkill(skillBuffer, SkillType.Barrage, ref index))
            {
                ref var skill = ref skillBuffer.ElementAt(index);

                skill.UIInformation.MaxCooldown = 0.0f;
                skill.UIInformation.CurrentCooldown = 0.0f;
                skill.UIInformation.ResourceCost = 0.0f;
                skill.UIInformation.ResourceCostPerSecond = 5.0f;
            }
        }

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
                commandBuffer.AddComponent(newEntity, new Projectile{Direction = new float3(0,1,0), Speed = 7.0f, DestroyOnHit = true, BounceCount = 0, PierceCount = 0});
                commandBuffer.AddComponent(newEntity, new Attack{Damage = 3, Type = DamageType.Physical, DamageHierarchy = DamageHierarchy.Main, Radius = 0.25f});
                commandBuffer.AddComponent(newEntity, new HomingProjectile{TurnSpeed = 1.5f, TurnAcceleration = 1.7f, HomingType = HomingType.Strongest});
                commandBuffer.AddComponent(newEntity, new Source{Entity = entity});
                commandBuffer.AddComponent(newEntity, new AttackTarget{TargetType = TargetType.Enemy});
                commandBuffer.AddComponent(newEntity, new InitializeFromSource{});

                barrage.ValueRW.Elapsed = 0.0f;
            }
        }
    }
}
