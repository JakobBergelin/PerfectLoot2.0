using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>Insight legendary power, added by InsightPower while the item is equipped.</summary>
public struct Insight : IComponentData
{
    public int CastsNeeded; // Every this many paid casts, the last one is free

    public int Casts;       // Paid since the last free one
}

/// <summary>
/// Insight: counts the wearer's casts that paid a one-off resource cost; every CastsNeeded-th one has its cost taken
/// back out of this frame's ResourceChangers, so it is free. A cast is a use this frame with a matching cost: drains per
/// second and uses that pay nothing (a Hookshot Slash hit, a Whirlwind tick) don't count. Flamethrower's start cost is
/// paid together with its drain, so it doesn't count either. After the skills and before Recharge, so a free cast
/// doesn't count as resource spent, and before Huskar turns costs into damage.
/// </summary>
[UpdateInGroup(typeof(SkillSystemGroup), OrderLast = true)]
[UpdateBefore(typeof(RechargeSystem))]
[BurstCompile]
public partial struct InsightSystem : ISystem
{
    const float CostTolerance = 1e-4f;

    [BurstCompile]
    public void OnCreate(ref SystemState state) => state.RequireForUpdate<Insight>();

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (insight, resourceChanges, skills, stats) in
                 SystemAPI.Query<RefRW<Insight>, DynamicBuffer<ResourceChanger>, DynamicBuffer<Skill>, RefRO<CalculatedStats>>())
        {
            ref var n = ref insight.ValueRW;
            foreach (var skill in skills)
            {
                if (!skill.events.OnUsed) continue;
                float cost = SkillUtil.Cost(skill.State.ResourceCost, stats.ValueRO.Value);
                if (cost <= 0f) continue;

                int paid = -1;
                for (int i = 0; i < resourceChanges.Length; i++)
                {
                    if (math.abs(resourceChanges[i].Value + cost) > CostTolerance) continue;
                    paid = i;
                    break;
                }
                if (paid < 0) continue;

                n.Casts++;
                if (n.Casts < math.max(1, n.CastsNeeded)) continue;
                n.Casts = 0;
                resourceChanges.RemoveAt(paid);
            }
        }
    }
}
