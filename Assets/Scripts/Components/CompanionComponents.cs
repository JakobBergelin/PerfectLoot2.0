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

/// <summary>A companion that was an enemy, raised by Raise Thrall. Its UnitInfo is that enemy's definition.</summary>
public struct Thrall : IComponentData
{
    public double RaisedAt;     // The oldest dies when there are too many
}

/// <summary>Sets its EnemyTarget to the nearest enemy in sight, in any direction (CompanionDetectionSystem).</summary>
public struct CompanionDetection : IComponentData
{
    public float SightRadius;
}

/// <summary>Where a Boid's area is, seen from the way its owner walks. Around: a ring around the owner.</summary>
public enum BoidPosition
{
    Behind,
    Left,
    Right,
    Front,
    Around,
}

[Serializable]
public struct BoidSettings
{
    public BoidPosition Position;
    [Tooltip("Meters from the player to the middle of its area. Around: how close to the player it comes.")]
    public float Distance;
    [Tooltip("Radius of its area in meters, where it picks its spots. Around: how much further out than Distance it goes.")]
    public float Spread;
    [Tooltip("It chases a target only while that target is this close to the player, in meters")]
    public float Leash;
    [Tooltip("Meters from its target where it stops. Keep this within its skills' range.")]
    public float AttackDistance;
    [Tooltip("Enemies this close send it to the safe side of its area, in meters. 0: it doesn't flee.")]
    public float FleeDistance;
    [Tooltip("Its top speed when far from where it wants to be, as a multiple of its movement speed")]
    public float CatchUpMultiplier;
    [Tooltip("Farther than this from the player, in meters, it teleports back")]
    public float TeleportDistance;
}

/// <summary>A companion that moves: flocks around its owner and chases targets close to them (BoidSystem).</summary>
public struct Boid : IComponentData
{
    public BoidSettings Settings;
    public float3 Heading;      // Turns toward the way its owner walks; zero until they do
    public float3 Spot;         // Where it wants to be, from the middle of its area
    public float SpotTime;      // Seconds until it picks a new spot
    public float Slack;         // Meters its spot gets away before it sets off
    public float3 Velocity;     // What it walks at, changing gradually
    public bool Moving;         // On its way to its spot
    public bool Chasing;        // Out of the flock, after its target
    public bool Fleeing;        // Enemies are close
    public Unity.Mathematics.Random Random;     // Seeded by BoidSystem
}
