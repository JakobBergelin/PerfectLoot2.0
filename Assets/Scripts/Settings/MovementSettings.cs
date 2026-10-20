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
}
