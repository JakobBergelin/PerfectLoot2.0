using Unity.Burst;
using Unity.Entities;

/// <summary>Brittle: all the wearer's damage to Frozen enemies is raised by DamageBonus, damage over time included. Between working out the damage and dealing it.</summary>
[UpdateInGroup(typeof(DamageSystemGroup))]
[UpdateAfter(typeof(CalculateDamageSystem))]
[UpdateBefore(typeof(DamagingSystem))]
[BurstCompile]
public partial struct BrittleSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state) => state.RequireForUpdate<Brittle>();

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var brittles = SystemAPI.GetComponentLookup<Brittle>(true);

        foreach (var taken in SystemAPI.Query<DynamicBuffer<CalculatedDamageInstance>>().WithAll<Enemy, Frozen>())
        {
            for (int i = 0; i < taken.Length; i++)
            {
                ref var instance = ref taken.ElementAt(i);
                if (instance.Damage > 0f && brittles.HasComponent(instance.Source))
                    instance.Damage *= 1f + brittles[instance.Source].DamageBonus;
            }
        }
    }
}
