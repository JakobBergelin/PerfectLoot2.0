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
