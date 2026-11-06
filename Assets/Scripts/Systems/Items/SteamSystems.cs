using Unity.Burst;
using Unity.Entities;

/// <summary>Steam legendary power, added by SteamPower while the item is equipped.</summary>
public struct Steam : IComponentData
{
    public float DamageFraction;    // Of the burns' remaining damage, 1 = all of it
}

/// <summary>
/// Steam: a main cold hit of the wearer that deals damage ends the wearer's burns on the target and deals what they had
/// left at once, as burn damage (it counts for the combat text like a burn tick, and resistances apply next frame).
/// Between working out the damage and dealing it, so hits that were stopped don't count.
/// </summary>
[UpdateInGroup(typeof(DamageSystemGroup))]
[UpdateAfter(typeof(CalculateDamageSystem))]
[UpdateBefore(typeof(DamagingSystem))]
[BurstCompile]
public partial struct SteamSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state) => state.RequireForUpdate<Steam>();

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var steamLookup = SystemAPI.GetComponentLookup<Steam>(true);

        foreach (var (taken, burns, damage, text, entity) in
                 SystemAPI.Query<DynamicBuffer<CalculatedDamageInstance>, DynamicBuffer<BurnInstance>, DynamicBuffer<DamageInstance>, RefRW<EffectDamageText>>()
                          .WithAll<Enemy, Burning>().WithEntityAccess())
        {
            foreach (var instance in taken)
            {
                if (instance.Hierarchy != DamageHierarchy.Main || instance.Type != DamageType.Cold || instance.Damage <= 0f || !steamLookup.HasComponent(instance.Source)) continue;

                float remaining = 0f;
                for (int i = burns.Length - 1; i >= 0; i--)
                {
                    if (burns[i].Source != instance.Source) continue;
                    remaining += burns[i].DamagePerSecond * burns[i].TimeRemaining;
                    burns.RemoveAt(i);
                }

                float amount = remaining * steamLookup[instance.Source].DamageFraction;
                if (amount <= 0f) continue;
                damage.Add(new DamageInstance { Damage = amount, Type = DamageType.Fire, Hierarchy = DamageHierarchy.Sub, Origin = DamageLabel.Steam, Source = instance.Source });
                text.ValueRW.Burn += amount;
            }
            if (burns.IsEmpty) SystemAPI.SetComponentEnabled<Burning>(entity, false);
        }
    }
}
