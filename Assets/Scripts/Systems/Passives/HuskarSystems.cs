using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

[BurstCompile]
// Turns this frame's skill costs into fire damage: after the skills (SkillSystemGroup), before damage is calculated
// and before UpdateResourcesSystem (HealthAndResourceSystemGroup) would spend them
[UpdateInGroup(typeof(DamageSystemGroup), OrderFirst = true)]
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
    }
}
