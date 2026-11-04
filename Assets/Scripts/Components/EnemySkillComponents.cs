using System;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using UnityEngine;

public enum EnemySkillType
{
    Melee,
    Projectile,
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

/// <summary>The telegraphed skill an enemy is casting. It stands still until TimeRemaining runs out or a stun cancels it.</summary>
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

/// <summary>Singleton: the marker to copy for a telegraph. Made at runtime by TelegraphPrefabSystem.</summary>
public struct TelegraphPrefab : IComponentData
{
    public Entity Value;
}

/// <summary>A telegraph on the ground. Follows its cast's progress and goes away with it (TelegraphMarkerSystem).</summary>
public struct TelegraphMarker : IComponentData
{
    public Entity Caster;
    public int CastId;
}

/// <summary>
/// What the marker's shader draws. x: how full, 0 to 1. y: TelegraphShape. z: Line half width (1 = Size) or Cone half angle in
/// radians. w: how far above and below its center it reaches (1 = Size).
/// </summary>
[MaterialProperty("_Telegraph")]
public struct TelegraphParams : IComponentData
{
    public float4 Value;
}
