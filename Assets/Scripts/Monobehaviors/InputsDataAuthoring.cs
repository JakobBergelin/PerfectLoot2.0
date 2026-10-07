using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Collections;


public class InputsDataAuthoring : MonoBehaviour
{
    public class Baker : Baker<InputsDataAuthoring>
    {
        public override void Bake(InputsDataAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new InputsData());
            AddComponent(entity, new DesiredDirection());
        }
    }
}

public struct InputsData : IComponentData
{
    public float2 Move;
    public bool4 Skill; // Hittade inget sätt att ha en array

    public float3 MousePosition;
}

public struct DesiredDirection : IComponentData
{
    public float3 Direction;
    public quaternion Quaternion;
}