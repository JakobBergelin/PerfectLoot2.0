using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Life on Hit and Resource on Hit: every main hit that deals damage heals its attacker by a flat LifeOnHit and moves up to
/// ResourceOnHit from the target to the attacker, if the target has any resource. Flat, so they don't grow with damage.
/// Each target of an area hit counts. A companion's hits give its owner nothing unless the owner has Blood Pact. Between
/// working out the damage and dealing it.
/// </summary>
[UpdateInGroup(typeof(DamageSystemGroup))]
[UpdateAfter(typeof(CalculateDamageSystem))]
[UpdateBefore(typeof(DamagingSystem))]
[BurstCompile]
public partial struct OnHitSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var companions = SystemAPI.GetComponentLookup<Companion>(true);
        var bloodPacts = SystemAPI.GetComponentLookup<BloodPact>(true);
        var stats = SystemAPI.GetComponentLookup<CalculatedStats>(true);
        var resources = SystemAPI.GetComponentLookup<Resource>();
        var healing = SystemAPI.GetBufferLookup<HealingInstance>();
        var resourceChanges = SystemAPI.GetBufferLookup<ResourceChanger>();
        state.CompleteDependency();  // The lookups are used here on the main thread

        foreach (var (taken, target) in SystemAPI.Query<DynamicBuffer<CalculatedDamageInstance>>().WithEntityAccess())
        {
            foreach (var hit in taken)
            {
                if (hit.Hierarchy != DamageHierarchy.Main || hit.Damage <= 0f) continue;

                // Its Source is its owner, or the companion itself for what tracks its caster (Mirror Image)
                var hitter = companions.HasComponent(hit.Origin.Unit) ? hit.Origin.Unit : hit.Source;
                var attacker = hitter;
                if (companions.HasComponent(hitter))
                {
                    attacker = companions[hitter].Owner;
                    if (!bloodPacts.HasComponent(attacker)) continue;
                }
                if (!stats.HasComponent(attacker)) continue;

                var attackerStats = stats[attacker].Value;
                if (attackerStats.LifeOnHit > 0f && healing.HasBuffer(attacker))
                    healing[attacker].Add(new HealingInstance { Value = attackerStats.LifeOnHit, Source = attacker, Label = DamageLabel.LifeOnHit });

                if (attackerStats.ResourceOnHit > 0f && resources.HasComponent(target) && resourceChanges.HasBuffer(attacker))
                {
                    var resource = resources.GetRefRW(target);
                    float stolen = math.min(attackerStats.ResourceOnHit, resource.ValueRO.CurrentValue);
                    if (stolen <= 0f) continue;
                    resource.ValueRW.CurrentValue -= stolen;
                    resourceChanges[attacker].Add(new ResourceChanger { Value = stolen });
                }
            }
        }
    }
}
