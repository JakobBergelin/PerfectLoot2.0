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
            var sourceEntity = source.ValueRO.Entity;
            var stats = SystemAPI.GetComponentRO<CalculatedStatsPercentages>(sourceEntity);
            attack.ValueRW.Damage *= 1.0f + stats.ValueRO.Damage;
        }

        foreach (var (source, attack, entity) in SystemAPI.Query<RefRO<Source>, RefRW<PeriodicAttack>>().WithPresent<InitializeFromSource>().WithEntityAccess())
        {
            var sourceEntity = source.ValueRO.Entity;
            var stats = SystemAPI.GetComponentRO<CalculatedStatsPercentages>(sourceEntity);
            attack.ValueRW.DamagePerSecond *= 1.0f + stats.ValueRO.Damage;
        }

        foreach (var (source, projectile, entity) in SystemAPI.Query<RefRO<Source>, RefRW<Projectile>>().WithPresent<InitializeFromSource>().WithEntityAccess())
        {
            var sourceEntity = source.ValueRO.Entity;
            var stats = SystemAPI.GetComponentRO<CalculatedStats>(sourceEntity);
            projectile.ValueRW.BounceCount += stats.ValueRO.Bounces;
            projectile.ValueRW.PierceCount += stats.ValueRO.Pierce;
        }

        var commandBuffer = _endSimulationEntityCommandBufferSystem.CreateCommandBuffer();

        // TODO remove at end of frame
        commandBuffer.RemoveComponent<InitializeFromSource>(SystemAPI.QueryBuilder().WithPresent<InitializeFromSource>().Build(), EntityQueryCaptureMode.AtPlayback);
    }
}
