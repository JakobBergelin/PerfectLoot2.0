using Unity.Burst;
using Unity.Entities;

/// <summary>Refractionist: a unit that used a mobility skill this frame gains Refraction stacks. After every skill has run.</summary>
[UpdateInGroup(typeof(SkillSystemGroup), OrderLast = true)]
[BurstCompile]
public partial struct RefractionistSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var refractionLookup = SystemAPI.GetComponentLookup<Refraction>();
        foreach (var (refractionist, entity) in SystemAPI.Query<RefRO<Refractionist>>().WithAll<MobilitySkillUsed>().WithEntityAccess())
            EffectApply.AddRefraction(refractionLookup, entity, refractionist.ValueRO.RefractionStacks, refractionist.ValueRO.MaxStacks);
    }
}

/// <summary>Clears last frame's MobilitySkillUsed events before this frame's skills run.</summary>
[UpdateInGroup(typeof(SkillSystemGroup), OrderFirst = true)]
[BurstCompile]
public partial struct MobilitySkillEventSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var used in SystemAPI.Query<EnabledRefRW<MobilitySkillUsed>>())
            used.ValueRW = false;
    }
}
