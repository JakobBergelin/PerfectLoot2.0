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

    public float SeparationStrength(BodyType bodyType) => bodyType == BodyType.Swarm ? SwarmSeparationStrength : PhysicsSeparationStrength;
}
