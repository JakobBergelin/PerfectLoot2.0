using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

[BurstCompile]
[UpdateBefore(typeof(UpdateResourcesSystem))]
[UpdateBefore(typeof(CalculateDamageSystem))]
public partial struct MedusaSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (medusa, damageInstance, resourcechanger, resource) in SystemAPI.Query<RefRO<Medusa>, DynamicBuffer<DamageInstance>, DynamicBuffer<ResourceChanger>, RefRO<Resource>>())
        {
            var remainingResource = resource.ValueRO.CurrentValue;
            for(int i = 0; i < damageInstance.Length; i++)
            {
                if(damageInstance[i].Damage <= remainingResource)
                {
                    resourcechanger.Add(new ResourceChanger{Value = -1.0f * math.abs(damageInstance[i].Damage)});
                    remainingResource -= damageInstance[i].Damage;
                    damageInstance.RemoveAt(i);
                    i--;
                }
                else
                {
                    var toRemove = damageInstance[i].Damage - remainingResource;
                    resourcechanger.Add(new ResourceChanger{Value = -1.0f * math.abs(toRemove)});
                    remainingResource -= toRemove;
                }
                
            }
        }
    }
}
