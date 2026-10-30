using System;
using Unity.Entities;
using UnityEngine;

[CreateAssetMenu(fileName = "MovementSettings", menuName = "Settings/MovementSettings")]
public class MovementSettings : ScriptableObject
{
    public MovementConfig Values = new MovementConfig
    {
        MovementSpeed = 4.0f,
        RotationSpeed = 360f,
        SeparationSpacing = 1.2f,
        PhysicsSeparationStrength = 2f,
        SwarmSeparationStrength = 5f,
        StepHeight = 0.4f,
        MaxSlope = 50f,
        OverlapPushSpeed = 4f,
        FlowCellSize = 1f,
        FlowWindowSize = 64f,
        FlowClearance = 0.4f,
        FlowRecenterDistance = 8f,
    };
}

/// <summary>Baked from MovementSettings by GameSettingsAuthoring; read as a singleton.</summary>
[Serializable]
public struct MovementConfig : IComponentData
{
    [Tooltip("Base movement speed in units per second, before the MovementSpeed stat")]
    public float MovementSpeed;
    [Tooltip("Turn speed in degrees per second")]
    public float RotationSpeed;

    [Header("Enemy crowds")]
    [Tooltip("Enemies try to keep (radius + other radius) * this apart")]
    public float SeparationSpacing;
    [Tooltip("Speed (units per second) at which Physics-body enemies push away from overlapping neighbours")]
    public float PhysicsSeparationStrength;
    [Tooltip("Same for Swarm-body enemies; higher, since nothing else keeps them apart")]
    public float SwarmSeparationStrength;

    [Header("Unit movement (UnitMoverSystem)")]
    [Tooltip("Ledges and steps up to this high (meters) are walked up; bigger drops count as no floor")]
    public float StepHeight;
    [Tooltip("Steepest ground (degrees) units walk on; steeper ground blocks like a wall")]
    public float MaxSlope;
    [Tooltip("Meters per second units are pushed out of anything they overlap (gently, never a launch)")]
    public float OverlapPushSpeed;

    [Header("Enemy pathing (FlowFieldSystem)")]
    [Tooltip("Side of a flow-field cell in meters; smaller fits through narrower gaps but costs more")]
    public float FlowCellSize;
    [Tooltip("Side of the square window around the player that enemies path in (meters); outside it they walk straight at the player")]
    public float FlowWindowSize;
    [Tooltip("A cell is blocked when a wall, prop or water is within this many meters of its center (about an enemy's radius)")]
    public float FlowClearance;
    [Tooltip("The window re-centres on the player once the player is this many meters from its middle")]
    public float FlowRecenterDistance;
    [Tooltip("Draws the flow field in the Scene view: walkable cells point along their path, blocked cells are red crosses")]
    public bool DrawFlowField;

    public float SeparationStrength(BodyType bodyType) => bodyType == BodyType.Swarm ? SwarmSeparationStrength : PhysicsSeparationStrength;
}
