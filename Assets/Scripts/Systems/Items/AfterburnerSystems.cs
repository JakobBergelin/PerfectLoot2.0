using System;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>Afterburner legendary power, added by AfterburnerPower while the item is equipped.</summary>
public struct Afterburner : IComponentData
{
    public float CostIncrease;      // Channeled skills cost this much more with Flamethrower equipped (0.3 = 30%)
    public float CostReduction;     // Channeled skills cost this much less without it (0.2 = 20%)

    public float Elapsed;           // Toward the next backward flame
}

/// <summary>Afterburner: makes Channeled skills dearer with Flamethrower equipped, cheaper without it.</summary>
[UpdateInGroup(typeof(FlatStatsSystemGroup))]
[BurstCompile]
public partial struct AfterburnerStatsSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (afterburner, stats, entity) in SystemAPI.Query<RefRO<Afterburner>, RefRW<CalculatedStats>>().WithEntityAccess())
        {
            bool hasFlamethrower = SystemAPI.HasComponent<Flamethrower>(entity);
            stats.ValueRW.Value.ChannelCostChange = hasFlamethrower ? afterburner.ValueRO.CostIncrease : -afterburner.ValueRO.CostReduction;
        }
    }
}

/// <summary>
/// Afterburner: while the wearer is Channeling anything with Flamethrower equipped, a flame also leaves its back, at the rate
/// and strength of the equipped Flamethrower: one extra spray, behind it. It follows the way it faces, so it sweeps around
/// during Whirlwind. It isn't a use of Flamethrower, and costs nothing beyond the channel's higher cost.
/// </summary>
[UpdateInGroup(typeof(SkillSystemGroup))]
[BurstCompile]
public partial struct AfterburnerFlameSystem : ISystem
{
    Unity.Mathematics.Random _random;

    public void OnCreate(ref SystemState state)
    {
        _random = Unity.Mathematics.Random.CreateFromIndex((uint)Environment.TickCount);
        state.RequireForUpdate<BeginInitializationEntityCommandBufferSystem.Singleton>();
        state.RequireForUpdate<Afterburner>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float deltaTime = SystemAPI.Time.DeltaTime;
        var commandBuffer = SystemAPI.GetSingleton<BeginInitializationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);

        // Flamethrower is only enabled while its key is held; equipped is enough here
        foreach (var (afterburner, flamethrower, stats, transform, entity) in
                 SystemAPI.Query<RefRW<Afterburner>, RefRO<Flamethrower>, RefRO<CalculatedStats>, RefRO<LocalTransform>>()
                          .WithAll<Channeling>().WithOptions(EntityQueryOptions.IgnoreComponentEnabledState).WithEntityAccess())
        {
            ref var c = ref afterburner.ValueRW;
            var position = transform.ValueRO.Position;
            float3 back = math.mul(transform.ValueRO.Rotation, math.back());

            c.Elapsed += deltaTime;
            float interval = 1f / math.max(0.01f, flamethrower.ValueRO.FlamesPerSecond);
            while (c.Elapsed >= interval)
            {
                c.Elapsed -= interval;
                FlamethrowerSystem.Spawn(commandBuffer, entity, flamethrower.ValueRO, stats.ValueRO.Value, position, back, ref _random);
            }
        }
    }
}
