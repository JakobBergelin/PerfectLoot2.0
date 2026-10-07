using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

[BurstCompile]
[UpdateBefore(typeof(UpdateResourcesSystem))]
[UpdateBefore(typeof(CalculateDamageSystem))]
public partial struct HuskarSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (huskar, damage, resource) in SystemAPI.Query<RefRO<Huskar>, DynamicBuffer<DamageInstance>, DynamicBuffer<ResourceChanger>>())
        {
            for(int i = 0; i < resource.Length; i++)
            {
                if(resource[i].Value < 0.0f)
                {
                    damage.Add(new DamageInstance{Damage = math.abs(resource[i].Value), Hierarchy = DamageHierarchy.Sub, Type = DamageType.Fire, Source = Entity.Null, IsCrit = false});
                    resource.RemoveAt(i);
                    i--;
                }
            }
        }

        foreach (var (huskar, health, resource) in SystemAPI.Query<RefRO<Huskar>, RefRO<Health>, RefRW<Resource>>())
        {
            resource.ValueRW.CurrentValue = health.ValueRO.CurrentValue;
            resource.ValueRW.MaxValue = health.ValueRO.MaxValue;
        }
    }
}

[BurstCompile]
[UpdateInGroup(typeof(CalculateStatsSystemGroup), OrderLast = true)]
public partial struct HuskarChangeResourceSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (huskar, health, resource) in SystemAPI.Query<RefRO<Huskar>, RefRO<Health>, RefRW<Resource>>())
        {
            resource.ValueRW.CurrentValue = health.ValueRO.CurrentValue;
            resource.ValueRW.MaxValue = health.ValueRO.MaxValue;
        }
    }
}
