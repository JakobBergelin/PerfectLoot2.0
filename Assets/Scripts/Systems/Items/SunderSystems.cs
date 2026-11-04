using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>Sunder legendary power, added by SunderPower while the item is equipped.</summary>
public struct Sunder : IComponentData
{
    public float DamagePerStack;    // Added to the wearer's hit for each Sundered stack on the target, after its resistances
    public int MaxStacks;
    public float StackDuration;     // Seconds Sundered lasts after the last hit
}

/// <summary>
/// Sunder: every main hit of the wearer that deals damage is raised by DamagePerStack for each Sundered stack on the
/// target, then adds a stack, so each hit lands harder than the one before. The bonus is flat: after resistances, not
/// multiplied by crits. Between working out the damage and dealing it, so hits that were stopped don't count.
/// </summary>
[UpdateInGroup(typeof(DamageSystemGroup))]
[UpdateAfter(typeof(CalculateDamageSystem))]
[UpdateBefore(typeof(DamagingSystem))]
[BurstCompile]
public partial struct SunderSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state) => state.RequireForUpdate<Sunder>();

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var sunderLookup = SystemAPI.GetComponentLookup<Sunder>(true);
        var sunderedLookup = SystemAPI.GetComponentLookup<Sundered>();
        state.CompleteDependency();  // The lookup is used here on the main thread; TimedEffectSystem ticks it in a job

        foreach (var (taken, entity) in SystemAPI.Query<DynamicBuffer<CalculatedDamageInstance>>().WithEntityAccess())
        {
            if (!sunderedLookup.HasComponent(entity)) continue;

            for (int i = 0; i < taken.Length; i++)
            {
                ref var instance = ref taken.ElementAt(i);
                if (instance.Hierarchy != DamageHierarchy.Main || instance.Damage <= 0f || !sunderLookup.HasComponent(instance.Source)) continue;

                var sunder = sunderLookup[instance.Source];
                int stacks = sunderedLookup.IsComponentEnabled(entity) ? sunderedLookup[entity].Stacks : 0;
                instance.Damage += stacks * sunder.DamagePerStack;
                EffectApply.Refresh(sunderedLookup, entity, sunder.StackDuration).ValueRW.Stacks = math.min(stacks + 1, math.max(1, sunder.MaxStacks));
            }
        }
    }
}
