using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Bloodrush: every enemy that dies adds a stack of BloodrushBuff to the wearer, whatever killed it (damage over time
/// too), and restarts its timer, up to MaxStacks. All stacks fall off together when it runs out. After all of this
/// frame's damage and before CheckDeathSystem removes the dead, so each death is counted once.
/// </summary>
[UpdateInGroup(typeof(DamageSystemGroup), OrderLast = true)]
[UpdateBefore(typeof(CheckDeathSystem))]
[BurstCompile]
public partial struct BloodrushSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state) => state.RequireForUpdate<Bloodrush>();

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var buffLookup = SystemAPI.GetComponentLookup<BloodrushBuff>();
        state.CompleteDependency();  // The lookup is used here on the main thread; TimedEffectSystem ticks it in a job

        int deaths = 0;
        foreach (var health in SystemAPI.Query<RefRO<Health>>().WithAll<Enemy>().WithNone<DestroyWithChildren>())
            if (health.ValueRO.CurrentValue <= 0f) deaths++;
        if (deaths == 0) return;

        foreach (var (bloodrush, stats, entity) in SystemAPI.Query<RefRO<Bloodrush>, RefRO<CalculatedStats>>().WithEntityAccess())
        {
            if (!buffLookup.HasComponent(entity)) continue;
            float duration = EffectApply.BuffDuration(bloodrush.ValueRO.Duration, stats.ValueRO.Value);
            if (duration <= 0f) continue;

            int stacks = buffLookup.IsComponentEnabled(entity) ? buffLookup[entity].Stacks : 0;
            var buff = EffectApply.Refresh(buffLookup, entity, duration);
            buff.ValueRW.Stacks = math.min(stacks + deaths, math.max(1, bloodrush.ValueRO.MaxStacks));
            buff.ValueRW.AttackSpeedPerStack = bloodrush.ValueRO.AttackSpeedPerStack;
        }
    }
}

/// <summary>Adds the Bloodrush buff's attack speed while it is active.</summary>
[UpdateInGroup(typeof(FlatStatsSystemGroup))]
[BurstCompile]
public partial struct BloodrushBuffStatsSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (buff, stats) in SystemAPI.Query<RefRO<BloodrushBuff>, RefRW<CalculatedStats>>())
            stats.ValueRW.Value.AttackSpeed += buff.ValueRO.Stacks * buff.ValueRO.AttackSpeedPerStack;
    }
}
