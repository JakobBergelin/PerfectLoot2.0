using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>Momentum legendary power, added by MomentumPower while the item is equipped.</summary>
public struct Momentum : IComponentData
{
    public float DamagePerStack;    // 0.03 = +3%
    public int MaxStacks;
    public float SecondsPerStack;   // Of moving
    public float Elapsed;           // Toward the next stack
}

/// <summary>
/// Momentum: every SecondsPerStack the wearer keeps moving adds a stack of MomentumBuff, up to MaxStacks; stopping
/// (StandingStill) removes them all at once.
/// </summary>
[UpdateInGroup(typeof(EffectSystemGroup))]
[BurstCompile]
public partial struct MomentumSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state) => state.RequireForUpdate<Momentum>();

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float deltaTime = SystemAPI.Time.DeltaTime;
        var buffLookup = SystemAPI.GetComponentLookup<MomentumBuff>();

        foreach (var (momentum, entity) in SystemAPI.Query<RefRW<Momentum>>().WithEntityAccess())
        {
            if (!buffLookup.HasComponent(entity)) continue;
            ref var m = ref momentum.ValueRW;

            if (SystemAPI.HasComponent<StandingStill>(entity))
            {
                m.Elapsed = 0f;
                buffLookup.SetComponentEnabled(entity, false);
                continue;
            }

            m.Elapsed += deltaTime;
            if (m.Elapsed < m.SecondsPerStack) continue;
            m.Elapsed = 0f;

            int stacks = buffLookup.IsComponentEnabled(entity) ? buffLookup[entity].Stacks : 0;
            buffLookup[entity] = new MomentumBuff { Stacks = math.min(stacks + 1, math.max(1, m.MaxStacks)), DamagePerStack = m.DamagePerStack };
            buffLookup.SetComponentEnabled(entity, true);
        }
    }
}

/// <summary>Adds the Momentum buff's damage while it is active.</summary>
[UpdateInGroup(typeof(FlatStatsSystemGroup))]
[BurstCompile]
public partial struct MomentumBuffStatsSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (buff, stats) in SystemAPI.Query<RefRO<MomentumBuff>, RefRW<CalculatedStats>>())
            stats.ValueRW.Value.Damage += buff.ValueRO.Stacks * buff.ValueRO.DamagePerStack;
    }
}
