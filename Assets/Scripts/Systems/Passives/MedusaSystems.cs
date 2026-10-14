using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

// Runs on the damage after armor, resistances and block. After Rage and Luminosity so fully absorbed hits still trigger them.
[BurstCompile]
[UpdateAfter(typeof(CalculateDamageSystem))]
[UpdateAfter(typeof(RageSystem))]
[UpdateAfter(typeof(LuminositySystem))]
[UpdateBefore(typeof(DamagingSystem))]
[UpdateBefore(typeof(UpdateResourcesSystem))]
public partial struct MedusaSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (medusa, damageInstance, resourcechanger, resource) in SystemAPI.Query<RefRO<Medusa>, DynamicBuffer<CalculatedDamageInstance>, DynamicBuffer<ResourceChanger>, RefRO<Resource>>())
        {
            var remainingResource = resource.ValueRO.CurrentValue;
            for(int i = 0; i < damageInstance.Length && remainingResource > 0.0f; i++)
            {
                ref var instance = ref damageInstance.ElementAt(i);
                var absorbed = math.min(instance.Damage, remainingResource);
                if(absorbed <= 0.0f)
                {
                    continue;
                }

                // Keep the instance; DamagingSystem skips damage <= 0
                resourcechanger.Add(new ResourceChanger{Value = -absorbed});
                remainingResource -= absorbed;
                instance.Damage -= absorbed;
            }
        }
    }
}
