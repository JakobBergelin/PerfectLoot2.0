using Unity.Burst;
using Unity.Entities;

/// <summary>Wyrmfire legendary power, added by WyrmfirePower while the item is equipped.</summary>
public struct Wyrmfire : IComponentData
{
    public float Damage;    // Total burn damage over Duration, before the wearer's fire damage %
    public float Duration;  // Seconds the burn lasts
}

/// <summary>
/// Wyrmfire: every melee hit the wearer takes that still deals damage (after armor, block, dodge, Refraction and
/// Invulnerable) sets the attacker burning, as a burn of the wearer's. Between working out the damage and dealing it,
/// so stopped hits are already 0.
/// </summary>
[UpdateInGroup(typeof(DamageSystemGroup))]
[UpdateAfter(typeof(CalculateDamageSystem))]
[UpdateBefore(typeof(DamagingSystem))]
[BurstCompile]
public partial struct WyrmfireSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state) => state.RequireForUpdate<Wyrmfire>();

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var meleeLookup = SystemAPI.GetComponentLookup<EnemyMeleeAttack>(true);
        var burns = SystemAPI.GetBufferLookup<BurnInstance>();
        var burning = SystemAPI.GetComponentLookup<Burning>();
        state.CompleteDependency();  // The lookups are used here on the main thread

        foreach (var (wyrmfire, taken, stats, entity) in
                 SystemAPI.Query<RefRO<Wyrmfire>, DynamicBuffer<CalculatedDamageInstance>, RefRO<CalculatedStats>>().WithEntityAccess())
        {
            float duration = wyrmfire.ValueRO.Duration;
            if (duration <= 0f) continue;
            float damagePerSecond = wyrmfire.ValueRO.Damage * stats.ValueRO.Value.DamageMultiplier(DamageType.Fire) / duration;

            foreach (var instance in taken)
            {
                if (instance.Hierarchy != DamageHierarchy.Main || instance.Damage <= 0f || !meleeLookup.HasComponent(instance.Source)) continue;
                EffectApply.AddBurn(burns, burning, instance.Source, damagePerSecond, duration, entity);
            }
        }
    }
}
