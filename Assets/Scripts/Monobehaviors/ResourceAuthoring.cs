using UnityEngine;
using Unity.Entities;
using Unity.Collections;
using System;

public class ResourceAuthoring : MonoBehaviour
{
    public GameObject ResourceBarPivotGameObject;
    public GameObject ResourceBarStickGameObject;
    public class Baker : Baker<ResourceAuthoring>
    {
        public override void Bake(ResourceAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new Resource{});
            AddComponent(entity, new ResourceBarUI{PivotEntity = GetEntity(authoring.ResourceBarPivotGameObject, TransformUsageFlags.NonUniformScale), 
                StickEntity = GetEntity(authoring.ResourceBarStickGameObject, TransformUsageFlags.NonUniformScale)});
            AddBuffer<ResourceChanger>(entity);
        }
    }
}