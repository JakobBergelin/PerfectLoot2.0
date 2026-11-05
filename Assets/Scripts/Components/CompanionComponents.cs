using System;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

/// <summary>
/// A unit fighting on a player's side. It uses EnemySkills like an enemy does, but its skills hit enemies and count as its
/// Owner's: the owner's stats and on-hit effects apply, and enemies that get hit go after the owner.
/// </summary>
public struct Companion : IComponentData
{
    public Entity Owner;
}

/// <summary>Sets its EnemyTarget to the nearest enemy in sight, in any direction (CompanionDetectionSystem).</summary>
public struct CompanionDetection : IComponentData
{
    public float SightRadius;
}

/// <summary>Where a Boid wants to be, seen from the way its owner walks.</summary>
public enum BoidPosition
{
    Behind,
    Left,
    Right,
}

[Serializable]
public struct BoidSettings
{
    public BoidPosition Position;
    [Tooltip("Meters from the player it wants to keep")]
    public float Distance;
    [Tooltip("It chases a target only while that target is this close to the player, in meters")]
    public float Leash;
    [Tooltip("Meters from its target where it stops. Keep this within its skills' range.")]
    public float AttackDistance;
    [Tooltip("Its top speed when far from where it wants to be, as a multiple of its movement speed")]
    public float CatchUpMultiplier;
    [Tooltip("Farther than this from the player, in meters, it teleports back")]
    public float TeleportDistance;
}

/// <summary>A companion that moves: follows its owner and chases targets close to them (BoidSystem).</summary>
public struct Boid : IComponentData
{
    public BoidSettings Settings;
    public float3 Heading;      // The way its owner last walked; zero until they do
}
