using Unity.Entities;
using Unity.Mathematics;

public struct Attack : IComponentData
{
    public float Damage;
    public DamageType Type;
    public DamageHierarchy DamageHierarchy;
    public float Radius;
    // Push on hit (knockback now; particles and ragdoll can use it too). HitForce 0 = no push.
    public float3 HitDirection;
    public float HitForce;
    public HitDirectionMode DirectionMode;
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
    // Push on each damage tick, see Attack
    public float3 HitDirection;
    public float HitForce;
    public HitDirectionMode DirectionMode;
}

public enum HitDirectionMode
{
    Fixed,      // HitDirection
    Random,     // A new random horizontal direction for every hit
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


// On every unit that can be hit. DamagingSystem adds to it, KnockbackSystem moves the unit and decays it.
public struct Knockback : IComponentData
{
    public float3 Velocity;
}
