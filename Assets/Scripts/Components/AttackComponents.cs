using Unity.Entities;
using Unity.Mathematics;

public struct Attack : IComponentData
{
    public float Damage;
    public DamageType Type;
    public DamageHierarchy DamageHierarchy;
    public float Radius;
    public struct Triggers
    {
        public bool OnHit;
        public float3 OnHitNormal;
        public bool IsCrit;
    }

    public Triggers Events;
}

public struct PeriodicAttack : IComponentData
{
    public float DamagePerSecond;
    public DamageType Type;
    public float Radius;
    public float TimeRemaining;
    public SkillType Skill;
    public float TickRate;
}

// Combined with attack component to determine who did it
public struct Source : IComponentData
{
    public Entity Entity;
}

public enum TargetType
{
    Player,
    Enemy,
    All
}
public struct AttackTarget : IComponentData
{
    public TargetType TargetType;
}

public struct IgnoreEntity : IBufferElementData
{
    public Entity Entity;
    public float GraceTimer;
}

