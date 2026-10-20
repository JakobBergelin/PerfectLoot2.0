using Unity.Entities;
using Unity.Mathematics;

public struct Projectile : IComponentData
{
    public float3 Direction;
    public float Speed;
    public bool DestroyOnHit;
    public int PierceCount;
    public int BounceCount;
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
