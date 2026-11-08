using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

/// <summary>
/// Precision: a wearer that landed a critical hit this frame takes CooldownCut seconds off every equipped skill's cooldown
/// (Skill.CooldownReduction, used up by each cooldown skill's system). Once per frame, however many hits crit. Between
/// working out the damage and dealing it, while the crits are still in CalculatedDamageInstance.
/// </summary>
[UpdateInGroup(typeof(DamageSystemGroup))]
[UpdateAfter(typeof(CalculateDamageSystem))]
[UpdateBefore(typeof(DamagingSystem))]
[BurstCompile]
public partial struct PrecisionSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state) => state.RequireForUpdate<Precision>();

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var wearers = SystemAPI.GetComponentLookup<Precision>(true);
        var critted = new NativeHashSet<Entity>(4, Allocator.Temp);
        foreach (var taken in SystemAPI.Query<DynamicBuffer<CalculatedDamageInstance>>())
            foreach (var instance in taken)
                if (instance.IsCrit && instance.Damage > 0f && wearers.HasComponent(instance.Source)) critted.Add(instance.Source);
        if (critted.IsEmpty) return;

        foreach (var (precision, skills, entity) in SystemAPI.Query<RefRO<Precision>, DynamicBuffer<Skill>>().WithEntityAccess())
        {
            if (!critted.Contains(entity)) continue;
            for (int i = 0; i < skills.Length; i++) skills.ElementAt(i).CooldownReduction += precision.ValueRO.CooldownCut;
        }
    }
}
