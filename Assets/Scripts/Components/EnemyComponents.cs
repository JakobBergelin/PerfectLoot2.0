using Unity.Entities;
using Unity.Mathematics;

public struct Enemy : IComponentData
{

}
public struct NpcBehavior : IComponentData
{
    public Entity TargetPlayer;
    public float TurnSpeed;
    public float TurnAcceleration;
    public float3 Direction;
    public quaternion DesiredDirection;
    public float DetectionRadius;
    public float AttackRadius;
    public bool IsAttacking;
}

public struct EnemyMeleeAttack : IComponentData
{
    public float Damage;
    public float AttackSpeed;
    public float Elapsed;  
}

/// <summary>
/// How an enemy's body interacts. Physics: collides with other units and the player (body-blocks, pushes).
/// Swarm: only collides with the world (ground, walls, props); separation steering keeps them apart. Much cheaper.
/// </summary>
public enum BodyType
{
    Physics,
    Swarm,
}

/// <summary>On every unit in the spatial grid (player and enemies). Enemies steer away from nearby units.</summary>
public struct Steering : IComponentData
{
    public float Radius;        // Body radius used for spacing
    public BodyType BodyType;   // Picks the separation strength from MovementConfig
}
