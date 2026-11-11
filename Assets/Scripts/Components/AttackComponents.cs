using Unity.Entities;
using Unity.Mathematics;

public struct Attack : IComponentData
{
    public float Damage;
    public DamageType Type;
    public DamageHierarchy DamageHierarchy;
    public float Radius;
    // Push on hit, away from the attack's position (knockback now; particles and ragdoll can use it too). 0 = no push.
    public float HitForce;
    // Added to its Source's critical chance: a companion's own bonus, as its hits use its owner's stats
    public float CriticalChanceBonus;
    public DamageOrigin Origin;
    // Seconds before it can hit the same target again (e.g. long for one hit per target). 0 = DefaultGracePeriod.
    public float GracePeriod;
    public const float DefaultGracePeriod = 0.3f;
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
    public DamageOrigin Origin;
    public float TickRate;
    // Push on each damage tick, see Attack
    public float HitForce;
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

/// <summary>
/// An attack that sweeps along an arc around Center (a unit), Distance meters out: from StartAngle to EndAngle (degrees
/// from Forward, positive to the right) in Duration seconds, then goes away (SweepingAttackSystem). Melee swings like
/// Swipe. Give its Attack a GracePeriod of at least Duration so each target is hit once per sweep.
/// </summary>
public struct SweepingAttack : IComponentData
{
    public Entity Center;
    public float3 Forward;      // Flat, normalized
    public float StartAngle;
    public float EndAngle;
    public float Distance;
    public float Duration;
    public float Elapsed;
}

/// <summary>
/// An attack waiting to happen: after Delay seconds this entity gets Attack (with TargetType) for one frame, where it is,
/// then goes away (DelayedAttackSystem). Needs a LocalTransform and a Source. Its source's area of effect and damage % apply
/// when it goes off, like any attack (InitializeFromSource). E.g. Upheaval's quake.
/// </summary>
public struct DelayedAttack : IComponentData
{
    public Attack Attack;
    public TargetType TargetType;
    public float Delay;
}

public enum AttackShapeKind
{
    Rectangle,
    Cone,
}

/// <summary>
/// On an Attack or PeriodicAttack that isn't a sphere (without it, it's a sphere of Radius). Turned with the attack's
/// LocalTransform, forward along +Z, and it reaches as high as its height. Rectangle: a box of HalfExtents around the
/// attack's position (Radius isn't used). Cone: from the attack's position forward, Radius long, HalfAngle degrees to
/// each side, flat (any height). Area of effect scales the whole shape. Hits push away from its centre line, the
/// forward line through its position (AttackShapes).
/// </summary>
public struct AttackShape : IComponentData
{
    public AttackShapeKind Kind;
    public float3 HalfExtents;      // Rectangle: half its width (x), height (y) and length (z)
    public float HalfAngle;         // Cone: degrees to each side of forward
}

/// <summary>An attack whose radius already includes its source's area of effect: InitializeFromSourceSystem leaves it (e.g. Swipe counts area twice).</summary>
public struct OwnAreaScaling : IComponentData
{
}
