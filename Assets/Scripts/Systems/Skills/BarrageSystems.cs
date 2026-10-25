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

        // Casts requested by effects (e.g. Echoing Blade): no wind-up; Barrage has no cost and homes on its own
        foreach (var (barrage, requests, skills, localTransform, entity) in
                 SystemAPI.Query<RefRO<Barrage>, DynamicBuffer<SkillCastRequest>, DynamicBuffer<Skill>, RefRO<LocalTransform>>()
                          .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState).WithEntityAccess())
        {
            while (SkillUtil.TryTakeRequest(requests, SkillType.Barrage, out var request))
            {
                Spawn(commandBuffer, entity, barrage.ValueRO, localTransform.ValueRO.Position);
                SkillUtil.MarkUsed(skills, SkillType.Barrage, request.AutoCast);
            }
        }

        foreach (var (barrage, stats) in SystemAPI.Query<RefRW<Barrage>, RefRO<CalculatedStats>>().WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
        {
            barrage.ValueRW.Elapsed += SystemAPI.Time.DeltaTime * stats.ValueRO.Value.AttackSpeed;
            barrage.ValueRW.Elapsed = Mathf.Min(barrage.ValueRW.Elapsed, barrage.ValueRW.AttackSpeed);
        }

        foreach (var (barrage, localTransform, entity) in SystemAPI.Query<RefRW<Barrage>, RefRO<LocalTransform>>().WithEntityAccess())
        {
            if(barrage.ValueRO.Elapsed >= barrage.ValueRO.AttackSpeed)
            {
                Spawn(commandBuffer, entity, barrage.ValueRO, localTransform.ValueRO.Position);
                barrage.ValueRW.Elapsed = 0.0f;
                if(SystemAPI.HasBuffer<Skill>(entity)) SkillUtil.MarkUsed(SystemAPI.GetBuffer<Skill>(entity), SkillType.Barrage);
            }
        }
    }

    /// <summary>
    /// One homing missile, launched straight up from the caster. Used by the normal cast and by effects that cast it
    /// (Echoing Blade). Barrage has no resource cost.
    /// </summary>
    public static void Spawn(EntityCommandBuffer commandBuffer, Entity caster, in Barrage barrage, float3 casterPosition)
    {
        var newEntity = commandBuffer.CreateEntity();

        commandBuffer.AddComponent(newEntity, new LocalTransform{Position = casterPosition + new float3(0, 1, 0), Rotation = quaternion.identity, Scale = 0.5f});
        commandBuffer.AddComponent(newEntity, new Projectile{Direction = new float3(0,1,0), Speed = barrage.ProjectileSpeed, DestroyOnHit = true, BounceCount = 0, PierceCount = 0});
        commandBuffer.AddComponent(newEntity, new Attack{Damage = barrage.Damage, Type = DamageType.Physical, DamageHierarchy = DamageHierarchy.Main, Radius = barrage.Radius});
        commandBuffer.AddComponent(newEntity, new HomingProjectile{TurnSpeed = barrage.HomingTurnSpeed, TurnAcceleration = barrage.HomingTurnAcceleration, HomingType = HomingType.Strongest});
        commandBuffer.AddComponent(newEntity, new Source{Entity = caster});
        commandBuffer.AddComponent(newEntity, new AttackTarget{TargetType = TargetType.Enemy});
        commandBuffer.AddComponent(newEntity, new InitializeFromSource{});
    }
}
