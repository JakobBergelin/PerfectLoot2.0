using System;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>Crossfire legendary power, added by CrossfirePower while the item is equipped.</summary>
public struct Crossfire : IComponentData
{
    public float CostIncrease;      // Channeled skills cost this much more with Flamethrower equipped (0.3 = 30%)
    public float CostReduction;     // Channeled skills cost this much less without it (0.2 = 20%)

    public float Elapsed;           // Toward the next pair of side flames
}

/// <summary>Crossfire: makes Channeled skills dearer with Flamethrower equipped, cheaper without it.</summary>
[UpdateInGroup(typeof(FlatStatsSystemGroup))]
[BurstCompile]
public partial struct CrossfireStatsSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (crossfire, stats, entity) in SystemAPI.Query<RefRO<Crossfire>, RefRW<CalculatedStats>>().WithEntityAccess())
        {
            bool hasFlamethrower = SystemAPI.HasComponent<Flamethrower>(entity);
            stats.ValueRW.Value.ChannelCostChange = hasFlamethrower ? crossfire.ValueRO.CostIncrease : -crossfire.ValueRO.CostReduction;
        }
    }
}

/// <summary>
/// Crossfire: while the wearer is Channeling anything with Flamethrower equipped, flames also leave its left and right
/// sides, at the rate and strength of the equipped Flamethrower. They follow the way it faces, so they sweep around during
/// Whirlwind. They aren't uses of Flamethrower, and cost nothing beyond the channel's higher cost.
/// </summary>
[UpdateInGroup(typeof(SkillSystemGroup))]
[BurstCompile]
public partial struct CrossfireFlameSystem : ISystem
{
    Unity.Mathematics.Random _random;

    public void OnCreate(ref SystemState state)
    {
        _random = Unity.Mathematics.Random.CreateFromIndex((uint)Environment.TickCount);
        state.RequireForUpdate<BeginInitializationEntityCommandBufferSystem.Singleton>();
        state.RequireForUpdate<Crossfire>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float deltaTime = SystemAPI.Time.DeltaTime;
        var commandBuffer = SystemAPI.GetSingleton<BeginInitializationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);

        // Flamethrower is only enabled while its key is held; equipped is enough here
        foreach (var (crossfire, flamethrower, stats, transform, entity) in
                 SystemAPI.Query<RefRW<Crossfire>, RefRO<Flamethrower>, RefRO<CalculatedStats>, RefRO<LocalTransform>>()
                          .WithAll<Channeling>().WithOptions(EntityQueryOptions.IgnoreComponentEnabledState).WithEntityAccess())
        {
            ref var c = ref crossfire.ValueRW;
            var position = transform.ValueRO.Position;
            float3 right = math.mul(transform.ValueRO.Rotation, math.right());

            c.Elapsed += deltaTime;
            float interval = 1f / math.max(0.01f, flamethrower.ValueRO.FlamesPerSecond);
            while (c.Elapsed >= interval)
            {
                c.Elapsed -= interval;
                FlamethrowerSystem.Spawn(commandBuffer, entity, flamethrower.ValueRO, stats.ValueRO.Value, position, right, ref _random);
                FlamethrowerSystem.Spawn(commandBuffer, entity, flamethrower.ValueRO, stats.ValueRO.Value, position, -right, ref _random);
            }
        }
    }
}
