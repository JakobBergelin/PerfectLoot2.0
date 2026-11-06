using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// Brawler: main hits of the wearer on units within Radius of it deal DamageBonus more, whatever the skill. Measured
/// flat, between working out the damage and dealing it.
/// </summary>
[UpdateInGroup(typeof(DamageSystemGroup))]
[UpdateAfter(typeof(CalculateDamageSystem))]
[UpdateBefore(typeof(DamagingSystem))]
[BurstCompile]
public partial struct BrawlerSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state) => state.RequireForUpdate<Brawler>();

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var brawlers = SystemAPI.GetComponentLookup<Brawler>(true);
        var transforms = SystemAPI.GetComponentLookup<LocalTransform>(true);

        foreach (var (taken, entity) in SystemAPI.Query<DynamicBuffer<CalculatedDamageInstance>>().WithAll<LocalTransform>().WithEntityAccess())
        {
            for (int i = 0; i < taken.Length; i++)
            {
                ref var instance = ref taken.ElementAt(i);
                if (instance.Hierarchy != DamageHierarchy.Main || instance.Damage <= 0f || !brawlers.HasComponent(instance.Source) || !transforms.HasComponent(instance.Source)) continue;

                var brawler = brawlers[instance.Source];
                float distanceSq = math.distancesq(transforms[entity].Position.xz, transforms[instance.Source].Position.xz);
                if (distanceSq <= brawler.Radius * brawler.Radius) instance.Damage *= 1f + brawler.DamageBonus;
            }
        }
    }
}
