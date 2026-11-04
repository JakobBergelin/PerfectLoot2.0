using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Anchor: every SecondsPerStack the wearer stands still adds a stack of AnchorBuff, up to MaxStacks; moving (StandingStill
/// gone) removes them all at once.
/// </summary>
[UpdateInGroup(typeof(EffectSystemGroup))]
[BurstCompile]
public partial struct AnchorSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state) => state.RequireForUpdate<Anchor>();

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float deltaTime = SystemAPI.Time.DeltaTime;
        var buffLookup = SystemAPI.GetComponentLookup<AnchorBuff>();

        foreach (var (anchor, entity) in SystemAPI.Query<RefRW<Anchor>>().WithEntityAccess())
        {
            if (!buffLookup.HasComponent(entity)) continue;
            ref var a = ref anchor.ValueRW;

            if (!SystemAPI.HasComponent<StandingStill>(entity))
            {
                a.Elapsed = 0f;
                buffLookup.SetComponentEnabled(entity, false);
                continue;
            }

            a.Elapsed += deltaTime;
            if (a.Elapsed < a.SecondsPerStack) continue;
            a.Elapsed = 0f;

            int stacks = buffLookup.IsComponentEnabled(entity) ? buffLookup[entity].Stacks : 0;
            buffLookup[entity] = new AnchorBuff { Stacks = math.min(stacks + 1, math.max(1, a.MaxStacks)), ArmorPerStack = a.ArmorPerStack };
            buffLookup.SetComponentEnabled(entity, true);
        }
    }
}

/// <summary>Adds the Anchor buff's armor while it is active.</summary>
[UpdateInGroup(typeof(FlatStatsSystemGroup))]
[BurstCompile]
public partial struct AnchorBuffStatsSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (buff, stats) in SystemAPI.Query<RefRO<AnchorBuff>, RefRW<CalculatedStats>>())
            stats.ValueRW.Value.Armor += buff.ValueRO.Stacks * buff.ValueRO.ArmorPerStack;
    }
}
