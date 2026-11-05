using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// Pack Instinct: every unit on the wearer's side within Radius has PackInstinctBuff, the wearer included. Several wearers
/// don't stack: the unit gets the highest bonus. Runs without wearers too, so the buff goes when the last one does.
/// </summary>
[UpdateInGroup(typeof(EffectSystemGroup))]
[BurstCompile]
public partial struct PackInstinctSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var buffs = SystemAPI.GetComponentLookup<PackInstinctBuff>();
        var enemies = SystemAPI.GetComponentLookup<Enemy>(true);
        state.CompleteDependency();  // The lookups are used here on the main thread

        foreach (var buff in SystemAPI.Query<EnabledRefRW<PackInstinctBuff>>())
            buff.ValueRW = false;

        foreach (var (pack, wearerTransform, wearer) in SystemAPI.Query<RefRO<PackInstinct>, RefRO<LocalTransform>>().WithEntityAccess())
        {
            bool wearerIsEnemy = enemies.HasComponent(wearer);
            float radiusSq = pack.ValueRO.Radius * pack.ValueRO.Radius;
            float bonus = pack.ValueRO.CriticalChanceBonus;

            foreach (var (transform, unit) in SystemAPI.Query<RefRO<LocalTransform>>().WithPresent<PackInstinctBuff>().WithEntityAccess())
            {
                if (enemies.HasComponent(unit) != wearerIsEnemy) continue;
                if (math.distancesq(transform.ValueRO.Position.xz, wearerTransform.ValueRO.Position.xz) > radiusSq) continue;

                float current = buffs.IsComponentEnabled(unit) ? buffs[unit].CriticalChanceBonus : 0f;
                buffs[unit] = new PackInstinctBuff { CriticalChanceBonus = math.max(current, bonus) };
                buffs.SetComponentEnabled(unit, true);
            }
        }
    }
}

/// <summary>Adds the Pack Instinct buff's critical chance while it is active.</summary>
[UpdateInGroup(typeof(FlatStatsSystemGroup))]
[BurstCompile]
public partial struct PackInstinctBuffStatsSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (buff, stats) in SystemAPI.Query<RefRO<PackInstinctBuff>, RefRW<CalculatedStats>>())
            stats.ValueRW.Value.CriticalChance += buff.ValueRO.CriticalChanceBonus;
    }
}
