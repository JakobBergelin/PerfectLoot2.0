using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

[BurstCompile]
[UpdateInGroup(typeof(SkillSystemGroup))]
public partial class FistsOfThunderSystem : SystemBase
{
    private const float ResourceGeneration = 1.0f;
    private float[] HitCooldowns;
    public float[] HitDamage;

    private const float HitWeaponDamage = 0.1f;

    private BeginInitializationEntityCommandBufferSystem _beginInitializationEntityCommandBufferSystem;
    [BurstCompile]
    protected override void OnCreate()
    {
        HitCooldowns = new float[7]{0.30f, 0.24f, 0.24f, 0.52f, 0.26f, 0.24f, 0.45f};
        HitDamage = new float[7]{0.5f, 0.5f, 0.6f, 0.7f, 0.4f, 0.6f, 0.9f};
        _beginInitializationEntityCommandBufferSystem = World.DefaultGameObjectInjectionWorld.GetExistingSystemManaged<BeginInitializationEntityCommandBufferSystem>();
    }

    [BurstCompile]
    protected override void OnUpdate()
    {
        var commandBuffer = _beginInitializationEntityCommandBufferSystem.CreateCommandBuffer();
        foreach (var (fistsOfThunder, stats, entity) in SystemAPI.Query<RefRW<FistsOfThunder>, RefRO<CalculatedStats>>().WithEntityAccess().WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
        {
            if(!SystemAPI.IsComponentEnabled<FistsOfThunder>(entity))
            {
                fistsOfThunder.ValueRW.PunchCount = 0;
                fistsOfThunder.ValueRW.TimeSinceLastPunch = 0.0f;
            }
            fistsOfThunder.ValueRW.TimeSinceLastPunch += SystemAPI.Time.DeltaTime * stats.ValueRO.AttackSpeed;
            fistsOfThunder.ValueRW.TimeSinceLastPunch = Mathf.Min(fistsOfThunder.ValueRW.TimeSinceLastPunch, HitCooldowns[fistsOfThunder.ValueRO.PunchCount]);
        }

        foreach (var (fistOfThunder, localTransform, desiredDirection, stats, entity) in SystemAPI.Query<RefRW<FistsOfThunder>, RefRW<LocalTransform>, RefRO<DesiredDirection>, RefRO<CalculatedStats>>().WithEntityAccess())
        {
            localTransform.ValueRW.Rotation = Quaternion.RotateTowards(localTransform.ValueRO.Rotation, desiredDirection.ValueRO.Quaternion, Constants.RotationSpeed * SystemAPI.Time.DeltaTime);

            if(Quaternion.Angle(localTransform.ValueRW.Rotation, desiredDirection.ValueRO.Quaternion) > (math.PI / 2))
            {
                continue;
            }

            if(fistOfThunder.ValueRO.TimeSinceLastPunch >= HitCooldowns[fistOfThunder.ValueRO.PunchCount])
            {
                var newEntity = commandBuffer.CreateEntity();
                
                UnityEngine.Random.InitState(System.DateTime.Now.Millisecond);
                var weaponDamage = stats.ValueRO.WeaponDamage + UnityEngine.Random.Range(-stats.ValueRO.WeaponDamageRange, stats.ValueRO.WeaponDamageRange);
                var damage = HitDamage[fistOfThunder.ValueRO.PunchCount] + weaponDamage * HitWeaponDamage;
                commandBuffer.AddComponent(newEntity, new LocalTransform{Position = localTransform.ValueRO.Position + desiredDirection.ValueRO.Direction, Rotation = Quaternion.identity, Scale = 1f});
                commandBuffer.AddComponent(newEntity, new Attack{Damage = damage, Type = DamageType.Lightning, Radius = 0.5f, DamageHierarchy = DamageHierarchy.Main});
                commandBuffer.AddComponent(newEntity, new Source{Entity = entity});
                commandBuffer.AddComponent(newEntity, new AttackTarget{TargetType = TargetType.Enemy});
                commandBuffer.AddComponent(newEntity, new InitializeFromSource{});
                // single frame damage
                commandBuffer.AddComponent(newEntity, new DestroyWithChildren{});

                fistOfThunder.ValueRW.PunchCount++;
                if(fistOfThunder.ValueRW.PunchCount >= 7)
                {
                    fistOfThunder.ValueRW.PunchCount = 0;
                }
                fistOfThunder.ValueRW.TimeSinceLastPunch = 0.0f;
                SystemAPI.GetBuffer<ResourceChanger>(entity).Add(new ResourceChanger{Value = ResourceGeneration});
            }
        }
    }
}
