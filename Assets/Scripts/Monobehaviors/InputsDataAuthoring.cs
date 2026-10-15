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
    // Per skill slot (Skill1..Skill4 actions)
    public bool4 SkillHeld;
    public bool4 SkillPressedThisFrame;

    public float3 MousePosition;
}

public struct DesiredDirection : IComponentData
{
    public float3 Direction;
    public quaternion Quaternion;
}