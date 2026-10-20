using Unity.Entities;
using Unity.Mathematics;

public struct Player : IComponentData
{

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
