using Unity.Entities;
using UnityEngine;
using Unity.Mathematics;

public class NpcBehaviorAuthoring : MonoBehaviour
{
    public GameObject TargetPlayer;
    public float TurnSpeed;
    public float TurnAcceleration;
    public Vector3 Direction;
    public quaternion DesiredDirection;
    public float DetectionRadius;
    public float AttackRadius;
    public bool IsAttacking;

    public class Baker : Baker<NpcBehaviorAuthoring>
    {
        public override void Bake(NpcBehaviorAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new NpcBehavior
            {
                TargetPlayer = GetEntity(authoring.TargetPlayer, TransformUsageFlags.Dynamic),
                TurnSpeed = authoring.TurnSpeed,
                TurnAcceleration = authoring.TurnAcceleration,
                Direction = new float3(authoring.Direction.x, authoring.Direction.y, authoring.Direction.z),
                DesiredDirection = authoring.DesiredDirection,
                DetectionRadius = authoring.DetectionRadius,
                AttackRadius = authoring.AttackRadius,
                IsAttacking = authoring.IsAttacking,
            });
            AddComponent(entity, new EnemyMeleeAttack
            {
                Damage = 0.5f,
                AttackSpeed = 1.0f,
                Elapsed = 0.0f,
            });
        }
    }
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