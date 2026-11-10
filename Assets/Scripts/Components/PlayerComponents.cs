using Unity.Entities;
using Unity.Mathematics;

public struct Player : IComponentData
{

}

/// <summary>
/// Which local player controls this player entity (0 is player 1): its input (GameInput.For, InputsDataSystem) and its
/// managers (CharacterManager.For and the others with the same PlayerIndex). Test players have none.
/// </summary>
public struct PlayerIndex : IComponentData
{
    public int Value;
}

/// <summary>A second player that only stands there, for testing several players (CharacterManager's context menu). The camera counts it like any player.</summary>
public struct TestPlayer : IComponentData
{
}

public struct InputsData : IComponentData
{
    public float2 Move;
    // Per skill slot (Skill1..Skill4 actions)
    public bool4 SkillHeld;
    public bool4 SkillPressedThisFrame;

    public float3 MousePosition;
    public Entity Hovered;  // The enemy or companion under the mouse, Entity.Null: none
}

public struct DesiredDirection : IComponentData
{
    public float3 Direction;
    public quaternion Quaternion;
}
