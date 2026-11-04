using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

[BurstCompile]
[UpdateAfter(typeof(SkillSystemGroup))]
[UpdateBefore(typeof(AttackSystemGroup))]

public partial class InitializeFromSourceSystem : SystemBase
{
    private EndSimulationEntityCommandBufferSystem _endSimulationEntityCommandBufferSystem;

    [BurstCompile]
    protected override void OnCreate()
    {
        _endSimulationEntityCommandBufferSystem = World.DefaultGameObjectInjectionWorld.GetExistingSystemManaged<EndSimulationEntityCommandBufferSystem>();
    }

    [BurstCompile]
    protected override void OnUpdate()
    {
        // TODO move to specific system file?
        foreach (var (source, attack, entity) in SystemAPI.Query<RefRO<Source>, RefRW<Attack>>().WithPresent<InitializeFromSource>().WithEntityAccess())
        {
            // The caster can be gone by now (an enemy killed the frame it attacked): the attack stays as it is
            var sourceEntity = source.ValueRO.Entity;
            if (!SystemAPI.HasComponent<CalculatedStats>(sourceEntity)) continue;
            var stats = SystemAPI.GetComponentRO<CalculatedStats>(sourceEntity);
            // Converted first, so the new element's damage % applies
            attack.ValueRW.Type = stats.ValueRO.Value.ConvertedType(attack.ValueRO.Type);
            attack.ValueRW.Damage *= stats.ValueRO.Value.DamageMultiplier(attack.ValueRO.Type);
            attack.ValueRW.Radius *= stats.ValueRO.Value.AreaMultiplier;
        }

        foreach (var (source, attack, entity) in SystemAPI.Query<RefRO<Source>, RefRW<PeriodicAttack>>().WithPresent<InitializeFromSource>().WithEntityAccess())
        {
            var sourceEntity = source.ValueRO.Entity;
            if (!SystemAPI.HasComponent<CalculatedStats>(sourceEntity)) continue;
            var stats = SystemAPI.GetComponentRO<CalculatedStats>(sourceEntity);
            attack.ValueRW.Type = stats.ValueRO.Value.ConvertedType(attack.ValueRO.Type);
            attack.ValueRW.DamagePerSecond *= stats.ValueRO.Value.DamageMultiplier(attack.ValueRO.Type);
            // Growing flames scale their own start and end radius (FlamethrowerSystem)
            if (!SystemAPI.HasComponent<GrowingFlame>(entity))
                attack.ValueRW.Radius *= stats.ValueRO.Value.AreaMultiplier;
        }

        foreach (var (source, heal) in SystemAPI.Query<RefRO<Source>, RefRW<Heal>>().WithPresent<InitializeFromSource>())
        {
            if (!SystemAPI.HasComponent<CalculatedStats>(source.ValueRO.Entity)) continue;
            var stats = SystemAPI.GetComponentRO<CalculatedStats>(source.ValueRO.Entity);
            heal.ValueRW.Radius *= stats.ValueRO.Value.AreaMultiplier;
        }

        foreach (var (source, projectile, transform, entity) in SystemAPI.Query<RefRO<Source>, RefRW<Projectile>, RefRO<LocalTransform>>().WithPresent<InitializeFromSource>().WithEntityAccess())
        {
            projectile.ValueRW.StartPosition = transform.ValueRO.Position;
            var sourceEntity = source.ValueRO.Entity;
            if (!SystemAPI.HasComponent<CalculatedStats>(sourceEntity)) continue;
            var stats = SystemAPI.GetComponentRO<CalculatedStats>(sourceEntity);
            projectile.ValueRW.BounceCount += stats.ValueRO.Value.Bounces;
            projectile.ValueRW.PierceCount += stats.ValueRO.Value.Pierce;
            projectile.ValueRW.Speed *= math.max(0f, 1.0f + stats.ValueRO.Value.ProjectileSpeed);
        }

        var commandBuffer = _endSimulationEntityCommandBufferSystem.CreateCommandBuffer();

        // TODO remove at end of frame
        commandBuffer.RemoveComponent<InitializeFromSource>(SystemAPI.QueryBuilder().WithPresent<InitializeFromSource>().Build(), EntityQueryCaptureMode.AtPlayback);
    }
}
