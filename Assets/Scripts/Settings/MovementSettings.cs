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

    public float SeparationStrength(BodyType bodyType) => bodyType == BodyType.Swarm ? SwarmSeparationStrength : PhysicsSeparationStrength;
}
