using Unity.Burst;
using Unity.Entities;

/// <summary>Patience legendary power, added by PatiencePower while the item is equipped.</summary>
public struct Patience : IComponentData
{
    public float DamagePerCooldownSecond;   // 0.15 = +15% damage per second of a skill's base cooldown
}

/// <summary>Patience: adds to the wearer's CooldownDamage, which skills with a cooldown apply through SkillUtil.CooldownDamage.</summary>
[UpdateInGroup(typeof(FlatStatsSystemGroup))]
[BurstCompile]
public partial struct PatienceSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (patience, stats) in SystemAPI.Query<RefRO<Patience>, RefRW<CalculatedStats>>())
            stats.ValueRW.CooldownDamage += patience.ValueRO.DamagePerCooldownSecond;
    }
}
