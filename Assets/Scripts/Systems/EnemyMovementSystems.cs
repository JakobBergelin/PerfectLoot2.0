using Unity.Burst;
using Unity.Entities;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;
partial struct EnemyMovementSystem : ISystem
{
    public quaternion lookRotation;
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var physicsWorldSingleton = SystemAPI.GetSingleton<PhysicsWorldSingleton>();
        NativeList<DistanceHit> hits = new NativeList<DistanceHit>(Allocator.Temp);
        NativeList<DistanceHit> attackRadius = new NativeList<DistanceHit>(Allocator.Temp);
        var collisionFilter = new CollisionFilter
        {
            BelongsTo = ~0u,
            CollidesWith = (uint)CollisionLayerHelper.Player,
            GroupIndex = 0,
        };

        foreach (var (npcBehavior, localTransform, entity) in SystemAPI.Query<RefRW<NpcBehavior>, RefRW<LocalTransform>>().WithEntityAccess())
        {         
            hits.Clear(); 
            physicsWorldSingleton.OverlapSphere(localTransform.ValueRO.Position, npcBehavior.ValueRO.DetectionRadius, ref hits, collisionFilter);
            if(!hits.IsEmpty)
            {
                foreach(var hit in hits)
                {
                    if(SystemAPI.HasComponent<Player>(hit.Entity))
                    {
                        npcBehavior.ValueRW.TargetPlayer = hit.Entity;
                    }
                }
            }          
        }  

        foreach (var (npcBehavior, localTransform) in SystemAPI.Query<RefRW<NpcBehavior>, RefRW<LocalTransform>>())
        {

            var target = npcBehavior.ValueRO.TargetPlayer; 
            if(target != Entity.Null && SystemAPI.HasComponent<LocalTransform>(target))
            {
                var targetDirection = SystemAPI.GetComponentRO<LocalTransform>(target).ValueRO.Position - localTransform.ValueRO.Position;
                npcBehavior.ValueRW.Direction = Vector3.Normalize(targetDirection);
                var newDirection = Vector3.RotateTowards(npcBehavior.ValueRO.Direction, targetDirection, npcBehavior.ValueRO.TurnSpeed * SystemAPI.Time.DeltaTime, 0.0f);
                npcBehavior.ValueRW.Direction = newDirection;
                npcBehavior.ValueRW.DesiredDirection = Quaternion.LookRotation(newDirection);
                npcBehavior.ValueRW.TurnSpeed += npcBehavior.ValueRO.TurnAcceleration * SystemAPI.Time.DeltaTime;
                lookRotation = Quaternion.LookRotation(newDirection);
                localTransform.ValueRW.Rotation = Quaternion.Slerp(localTransform.ValueRO.Rotation, lookRotation, npcBehavior.ValueRO.TurnSpeed * SystemAPI.Time.DeltaTime);
            }
        }     
        foreach (var (localTransform, npcBehavior, movementSpeed, physicsVelocity) in SystemAPI.Query<RefRW<LocalTransform>, RefRW<NpcBehavior>, RefRO<MovementSpeed>, RefRW<PhysicsVelocity>>())
        {
            attackRadius.Clear();
            physicsWorldSingleton.OverlapSphere(localTransform.ValueRO.Position, npcBehavior.ValueRO.AttackRadius, ref attackRadius, collisionFilter);
            float speed = math.remap(0.0f, 180.0f, movementSpeed.ValueRO.Value, 0.0f, Quaternion.Angle(localTransform.ValueRO.Rotation, lookRotation));
            npcBehavior.ValueRW.IsAttacking = false;
            if (!attackRadius.IsEmpty)
            {
                foreach(var hit in attackRadius)
                {
                    if(SystemAPI.HasComponent<Player>(hit.Entity))
                    {
                        npcBehavior.ValueRW.IsAttacking = true;
                    }
                }
                physicsVelocity.ValueRW.Linear = float3.zero;
                physicsVelocity.ValueRW.Angular = float3.zero;
            }
            if (!npcBehavior.ValueRO.IsAttacking)
            {
                physicsVelocity.ValueRW.Linear = npcBehavior.ValueRO.Direction * speed;
                physicsVelocity.ValueRW.Angular = float3.zero;
            }
        }

    }
}