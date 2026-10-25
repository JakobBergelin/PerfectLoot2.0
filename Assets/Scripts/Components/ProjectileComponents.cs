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
