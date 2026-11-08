using Unity.Entities;
using Unity.Mathematics;

public struct Health : IComponentData
{
    public float MaxValue;
    public float CurrentValue;
    public float MaxHealthMultiplier;
}

/// <summary>A unit's health bar over its head: the stick turns it to the camera, the bar is the quad UnitBar.shader draws.</summary>
public struct HealthBarUI : IComponentData
{
    public Entity BarEntity;
    public Entity StickEntity;
}

/// <summary>What UnitBar.shader draws on a bar's quad. x: how full, 0 to 1. y: kind (UnitBarKind).</summary>
[Unity.Rendering.MaterialProperty("_UnitBar")]
public struct UnitBarParams : IComponentData
{
    public float4 Value;
}

/// <summary>The bar's colour in UnitBar.shader. Players' bars are hidden: UnitBarHUD draws theirs, with the values.</summary>
public static class UnitBarKind
{
    public const float Hidden = 0f, Enemy = 1f, Companion = 2f, Resource = 3f;
}

public struct HealthRegeneration : IComponentData
{
    public float Value;
    public float Multiplier;    // Added up each frame, 0.2 = +20%; reset by ResetStatsSystem
}

public struct DamageInstance : IBufferElementData
{    
    public float Damage;
    public DamageType Type;
    public DamageHierarchy Hierarchy;
    public Entity Source;
    public bool IsCrit;
    public float3 HitDirection;     // Normalized, away from the attack; with HitForce, pushes the target (knockback, effects)
    public float HitForce;
    public bool IsCenterTarget;     // The hit target closest to the attack's center (one per attack; lightning charges only it)
    public Entity Attack;           // The Attack that hit; Entity.Null for other damage (periodic attacks, damage over time)
    public DamageOrigin Origin;     // What the Damage Meter credits it to
}

/// <summary>On every unit: its latest main hit that dealt damage, written by DamagingSystem (e.g. which way a killing blow went).</summary>
public struct LastHit : IComponentData
{
    public float3 Direction;        // The hit's HitDirection: horizontally away from the attack
    public Entity Source;
    public double Time;             // SystemAPI.Time.ElapsedTime of the frame it landed; 0 = never hit
    public DamageType Type;
    public float HitForce;          // The hit's HitForce; negative pulls (Undertow)
}

public struct HealingInstance : IBufferElementData
{    
    public float Value;
    public Entity Source;
    public DamageLabel Label;
}

public struct PeriodicDamageInstance : IBufferElementData
{    
    public float AccumilatedDamage;
    public DamageType Type;
    public DamageHierarchy Hierarchy;
    public SkillType Skill;
    public DamageOrigin Origin;
    public float Tick;
    public Entity Source;
    public float3 HitDirection;     // Away from the attack, updated every frame it overlaps
    public float HitForce;
    public bool IsCenterTarget;     // Closest to the attack's center on any frame since the last tick
}

public struct CalculatedDamageInstance : IBufferElementData
{    
    public float Damage;
    public DamageType Type;
    public DamageHierarchy Hierarchy;
    public Entity Source;
    public bool IsCrit;
    public float3 HitDirection;
    public float HitForce;
    public bool IsCenterTarget;
    public Entity Attack;
    public DamageOrigin Origin;
}