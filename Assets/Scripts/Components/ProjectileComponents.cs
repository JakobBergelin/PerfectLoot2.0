using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

public struct Projectile : IComponentData
{
    public float3 Direction;
    public float Speed;
    public bool DestroyOnHit;
    public int PierceCount;
    public int BounceCount;
    /// <summary>
    /// Moves flat and keeps the height above the ground it was spawned at, following slopes and hills. The ground never
    /// stops it; walls still do (unless PassThroughWalls). Off: flies freely in 3D and hits the ground (e.g. homing missiles).
    /// </summary>
    public bool FollowGround;
    public bool PassThroughWalls;
    /// <summary>Height above the ground, measured on the first move (FollowGround). Negative when there was no ground under it.</summary>
    public float GroundHeight;
    public bool GroundHeightSet;
    /// <summary>Where it was fired from. Set by InitializeFromSourceSystem.</summary>
    public float3 StartPosition;
    /// <summary>Fraction of its speed lost right now, 0 to 1. Set every frame by TimezoneFieldSystem.</summary>
    public float Slow;
    /// <summary>How fast it moves right now. Move projectiles with this; Speed is its speed outside any slow.</summary>
    public float CurrentSpeed => Speed * (1f - Slow);
}

/// <summary>
/// Destroys the projectile once it has flown MaxDistance meters (ProjectileRangeSystem). Counts the path actually flown,
/// so bounces, homing and orbiting use it up too. Set LastPosition to the spawn position.
/// </summary>
public struct ProjectileRange : IComponentData
{
    public float MaxDistance;
    public float Traveled;
    public float3 LastPosition;
}

public enum HomingType
{
    Closest,
    Strongest,
    Weakest,
}

public struct HomingProjectile : IComponentData
{
    public Entity Target;
    public float TurnSpeed;
    public float TurnAcceleration;

    public HomingType HomingType;
}

/// <summary>
/// Picks a new random flat direction every DirectionDuration seconds and sways in a small circle around its path
/// (e.g. Tornado). Steers Projectile.Direction before ProjectileSystem moves it.
/// </summary>
public struct WanderingProjectile : IComponentData
{
    public float DirectionDuration;
    public float Elapsed;
    public float SwayRadius;
    /// <summary>Radians per second around the sway circle.</summary>
    public float SwaySpeed;
    public float SwayAngle;
    /// <summary>The sway offset applied last frame, replaced by the new one each frame.</summary>
    public float3 SwayOffset;
}

/// <summary>
/// A projectile orbiting Center (Event Horizon): OrbitSteeringSystem turns its direction around the center each frame,
/// keeping its speed. Order is when it was captured (the oldest is released first). Removing this releases it on its
/// current heading.
/// </summary>
public struct Orbiting : IComponentData
{
    public Entity Center;
    public float Radius;
    public uint Order;
}

/// <summary>
/// Captain's Shield in flight. A Projectile, so projectile stats and effects apply (BounceCount is how many more
/// targets it goes for, Speed its speed), but ShieldProjectileSystem moves it and ProjectileSystem skips it. Systems
/// that move or redirect projectiles should decide whether to skip it too.
/// </summary>
public struct ShieldProjectile : IComponentData
{
    public Entity Target;           // Null: none (flying out toward the cursor, or returning)
    public FixedList128Bytes<Entity> Targeted;  // Enemies already homed on, not picked again
    public bool Returning;
    public float Distance;          // Flown so far without a target
    public float NoTargetDistance;
    public float BaseTurnSpeed;
    public float TurnSpeed;
    public float TurnAcceleration;
}

/// <summary>
/// A PeriodicAttack whose radius grows from StartRadius to EndRadius over Lifetime (its TimeRemaining counts down from
/// Lifetime). Flamethrower's flames.
/// </summary>
public struct GrowingFlame : IComponentData
{
    public float StartRadius;
    public float EndRadius;
    public float Lifetime;
}
