using Unity.Burst;
using Unity.Entities;
using UnityEngine;
using Unity.Collections;
using Unity.Transforms;
using Unity.Mathematics;

public partial class ResourceUISystem : SystemBase
{
    protected override void OnUpdate()
    {
        Vector3 cameraForward = Vector3.zero;
        if(Camera.main != null)
        {
            cameraForward = Camera.main.transform.forward;
        }

        foreach (var (resourceBar, resource, localTransform) in SystemAPI.Query<RefRO<ResourceBarUI>, RefRO<Resource>, RefRO<LocalTransform>>())
        {
            float resourceNormalized = resource.ValueRO.CurrentValue / resource.ValueRO.MaxValue;
            var stickTransform = SystemAPI.GetComponentRW<LocalTransform>(resourceBar.ValueRO.StickEntity);
            var pivotMatrix = SystemAPI.GetComponentRW<PostTransformMatrix>(resourceBar.ValueRO.PivotEntity);
            pivotMatrix.ValueRW.Value = float4x4.Scale(resourceNormalized, 1, 1);
            

            stickTransform.ValueRW.Rotation = localTransform.ValueRO.InverseTransformRotation(quaternion.LookRotation(cameraForward, math.up()));
        }
    }
}


[BurstCompile]
public partial struct UpdateResourcesSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (resource, entity) in SystemAPI.Query<RefRW<Resource>>().WithEntityAccess())
        {
            var instances = SystemAPI.GetBuffer<ResourceChanger>(entity);
            foreach(var instance in instances)
            {
                resource.ValueRW.CurrentValue += instance.Value;
            }
            resource.ValueRW.CurrentValue = Mathf.Clamp(resource.ValueRO.CurrentValue, 0.0f, resource.ValueRO.MaxValue);
            instances.Clear();
        }
    }
}

partial struct ResourceRegenerationSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (resource, resourceChanger) in SystemAPI.Query<RefRO<Resource>, DynamicBuffer<ResourceChanger>>())
        {
            resourceChanger.Add(new ResourceChanger{Value = resource.ValueRO.Regeneration * SystemAPI.Time.DeltaTime});
        }
    }
}

[BurstCompile]
[UpdateInGroup(typeof(CalculateStatsSystemGroup), OrderLast = true)]
public partial struct MultiplyResourceSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var resource in SystemAPI.Query<RefRW<Resource>>())
        {
            resource.ValueRW.Regeneration *= 1.0f + resource.ValueRO.RegenerationMultiplier;
        }
    }
}
