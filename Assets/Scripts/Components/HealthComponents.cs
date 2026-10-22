using Unity.Entities;
using Unity.Mathematics;

public struct Health : IComponentData
{
    public float MaxValue;
    public float CurrentValue;
    public float MaxHealthMultiplier;
}

public struct HealthBarUI : IComponentData
{
    public Entity PivotEntity;
    public Entity StickEntity;
}

public struct HealthRegeneration : IComponentData
{
    public float Value;
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
}

public struct HealingInstance : IBufferElementData
{    
    public float Value;
    public Entity Source;
}

public struct PeriodicDamageInstance : IBufferElementData
{    
    public float AccumilatedDamage;
    public DamageType Type;
    public DamageHierarchy Hierarchy;
    public SkillType Skill;
    public float Tick;
    public Entity Source;
    public float3 HitDirection;     // Away from the attack, updated every frame it overlaps
    public float HitForce;
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
}