using Unity.Burst;
using Unity.Entities;

/// <summary>Brace: a wearer that made a melee attack this frame gets BlockBonus for Duration seconds; counts it down. A skill reaction.</summary>
[UpdateInGroup(typeof(SkillReactionSystemGroup))]
[BurstCompile]
public partial struct BraceSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state) => state.RequireForUpdate<Brace>();

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float deltaTime = SystemAPI.Time.DeltaTime;
        var melee = SystemAPI.GetComponentLookup<MeleeAttackUsed>(true);
        foreach (var (brace, entity) in SystemAPI.Query<RefRW<Brace>>().WithEntityAccess())
        {
            ref var b = ref brace.ValueRW;
            if (melee.HasComponent(entity) && melee.IsComponentEnabled(entity)) b.TimeRemaining = b.Duration;
            else if (b.TimeRemaining > 0f) b.TimeRemaining -= deltaTime;
        }
    }
}

/// <summary>Adds Brace's block chance while it lasts.</summary>
[UpdateInGroup(typeof(FlatStatsSystemGroup))]
[BurstCompile]
public partial struct BraceStatsSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (brace, stats) in SystemAPI.Query<RefRO<Brace>, RefRW<CalculatedStats>>())
            if (brace.ValueRO.TimeRemaining > 0f) stats.ValueRW.Value.BlockChance += brace.ValueRO.BlockBonus;
    }
}
