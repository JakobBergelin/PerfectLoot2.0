using System;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Rendering;
using UnityEngine;

public enum EnemySkillType
{
    Melee,
    Projectile,
    SelfDestruct,
    GroundSlam,
    Beam,
    HomingVolley,
}

public enum TargetMotion
{
    Any,
    Moving,
    Still,
}

/// <summary>What an enemy's target has to be doing for a skill to be used. All of them must hold.</summary>
[Serializable]
public struct EnemySkillConditions
{
    [Tooltip("Only while the target is using a skill")]
    public bool TargetUsingSkill;
    [Tooltip("With Target Using Skill: only a skill with one of these tags. Nothing: any skill.")]
    public SkillTags SkillTags;
    public TargetMotion TargetMotion;
    [Range(0f, 1f), Tooltip("Only while the target's health is at or below this share of its max. 0: at any health.")]
    public float TargetHealthBelow;
}

public enum TelegraphShape
{
    None,
    Circle,
    Line,
    Cone,
}

/// <summary>The marker on the ground before a skill goes off. None: it goes off at once.</summary>
[Serializable]
public struct EnemyTelegraph
{
    public TelegraphShape Shape;
    [Tooltip("Seconds from the marker appearing to the skill going off")]
    public float Duration;
    [Tooltip("Circle: radius around the target. Line: length from the enemy. Cone: reach from the enemy.")]
    public float Size;
    [Tooltip("Line: width in meters. Cone: full angle in degrees. Circle: unused.")]
    public float Width;
    [Tooltip("Circle: around the enemy itself instead of its target (e.g. an explosion)")]
    public bool OnSelf;
}

/// <summary>One skill of an enemy, in priority order, from its EnemySkill asset. EnemySkillCastSystem picks and starts them.</summary>
[InternalBufferCapacity(4)]
public struct EnemySkillSlot : IBufferElementData
{
    public EnemySkillType Type;
    public float Cooldown;
    public float CooldownRemaining;
    public bool CooldownUsesAttackSpeed;
    public bool CooldownOnlyInRange;
    public float ResourceCost;
    public float MinRange;
    public float MaxRange;
    public bool NeedsLineOfSight;
    public float TurnSpeed;
    public float FacingAngle;
    public EnemySkillConditions Conditions;
    public EnemyTelegraph Telegraph;
}

/// <summary>The telegraphed skill an enemy is casting. It stands still until TimeRemaining runs out or being frozen cancels it.</summary>
public struct EnemySkillCast : IComponentData
{
    public bool Active;
    public int Slot;
    public int CastId;          // New for every cast, so a marker knows it belongs to this one
    public float TimeRemaining;
    public float Duration;
    public float3 Position;     // Where the target was when the cast started
    public float3 Direction;    // Flat, toward Position
}

/// <summary>A skill going off this frame, for its own system. Added by EnemySkillCastSystem, which clears it next frame.</summary>
[InternalBufferCapacity(2)]
public struct EnemySkillFire : IBufferElementData
{
    public EnemySkillType Type;
    public float3 Position;
    public float3 Direction;
}

public struct EnemyMelee : IComponentData
{
    public float Damage;
    public DamageType Type;
    public float Reach;     // The hit has half this radius, half this far in front of the enemy
    public float HitForce;
}

/// <summary>On an enemy's melee Attack, so effects can tell melee hits from ranged ones (Wyrmfire).</summary>
public struct EnemyMeleeHit : IComponentData
{
}

public struct EnemyProjectile : IComponentData
{
    public float Damage;
    public DamageType Type;
    public float ProjectileSpeed;
    public float Radius;
    public float HitForce;
    public float MaxDistance;
}

/// <summary>Self Destruct: when it goes off, Damage to everything on the other side within Radius of the unit, then the unit dies.</summary>
public struct EnemySelfDestruct : IComponentData
{
    public float Damage;
    public DamageType Type;
    public float Radius;
    public float HitForce;
}

/// <summary>Ground Slam: when it goes off, Damage to everything within Radius of the spot its target stood on when the cast began.</summary>
public struct EnemyGroundSlam : IComponentData
{
    public float Damage;
    public DamageType Type;
    public float Radius;        // Its Circle telegraph's size, so the hit matches the marker
    public float HitForce;
}

/// <summary>Beam: when it goes off, Damage to everything in a Length by Width rectangle from the unit toward its target.</summary>
public struct EnemyBeam : IComponentData
{
    public float Damage;
    public DamageType Type;
    public float Length;        // Its Line telegraph's size and width, so the hit matches the marker
    public float Width;
    public float Height;        // How high it reaches, to catch units of any size
    public float HitForce;
}

/// <summary>Homing Volley: Count missiles fanned over Spread degrees that home in on the unit's target.</summary>
public struct EnemyHomingVolley : IComponentData
{
    public int Count;
    public float Spread;        // Degrees from one side of the fan to the other
    public float Damage;
    public DamageType Type;
    public float ProjectileSpeed;
    public float Radius;
    public float TurnSpeed;     // Radians per second, growing by TurnAcceleration
    public float TurnAcceleration;
    public float MaxDistance;
    public float HitForce;
}

/// <summary>A telegraph on the ground. Follows its cast's progress and goes away with it (TelegraphMarkerSystem, on the host).</summary>
public struct TelegraphMarker : IComponentData
{
    public Entity Caster;
    public int CastId;
}

/// <summary>
/// What the marker's shader draws. x: how full, 0 to 1. y: TelegraphShape. z: Line half width (1 = Size) or Cone half angle in
/// radians. w: how far above and below its center it reaches (1 = Size). Sent to the other machines.
/// </summary>
[MaterialProperty("_Telegraph")]
public struct TelegraphParams : IComponentData
{
    [GhostField(Quantization = 1000, Smoothing = SmoothingAction.Interpolate)] public float4 Value;
}
