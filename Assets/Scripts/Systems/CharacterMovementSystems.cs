using Unity.Burst;
using Unity.Entities;
using UnityEngine;
using Unity.Transforms;
using Unity.Mathematics;
using Unity.Physics;

[BurstCompile]
[UpdateInGroup(typeof(MovementSystemGroup))]
public partial class CharacterMovementSystem : SystemBase
{
    // TODO use midframe commandbuffer
    private BeginInitializationEntityCommandBufferSystem _beginInitializationEntityCommandBufferSystem;
    
    [BurstCompile]
    protected override void OnCreate()
    {
        _beginInitializationEntityCommandBufferSystem = World.DefaultGameObjectInjectionWorld.GetExistingSystemManaged<BeginInitializationEntityCommandBufferSystem>();
        RequireForUpdate<MovementConfig>();
    }

    [BurstCompile]
    protected override void OnUpdate()
    {
        var commandBuffer = _beginInitializationEntityCommandBufferSystem.CreateCommandBuffer();
        var movementConfig = SystemAPI.GetSingleton<MovementConfig>();
        foreach (var(movementSpeed, inputs, transform, velocity, entity) in SystemAPI.Query<RefRO<MovementSpeed>, RefRO<InputsData>, RefRW<LocalTransform>, RefRW<PhysicsVelocity>>().WithEntityAccess())
        {
            float3 position = transform.ValueRO.Position;
            bool moving = false;
            if(inputs.ValueRO.Move.x != 0.0f || inputs.ValueRO.Move.y != 0.0f)
            {
                position.x += inputs.ValueRO.Move.x;
                position.z += inputs.ValueRO.Move.y;
                var direction = Vector3.Normalize(position - transform.ValueRW.Position);

                var newQuaternion = Quaternion.LookRotation(direction, Vector3.up);

                float speed = movementConfig.MovementSpeed * (1.0f + movementSpeed.ValueRO.Value);
                if(!SystemAPI.HasComponent<DisableTurning>(entity))
                {
                    transform.ValueRW.Rotation = Quaternion.RotateTowards(transform.ValueRO.Rotation, newQuaternion, movementConfig.RotationSpeed * SystemAPI.Time.DeltaTime);
                    speed = math.remap(0.0f, 180.0f, movementConfig.MovementSpeed * (1.0f + movementSpeed.ValueRO.Value) , 0.0f, Quaternion.Angle(transform.ValueRW.Rotation, newQuaternion));
                }

                /*transform.ValueRW.Position += (float3)direction * speed * Time.deltaTime;*/
                velocity.ValueRW.Linear = (float3)direction * speed;
                velocity.ValueRW.Angular = float3.zero;

                moving = true;
            }

            if(moving && !SystemAPI.HasComponent<StandingStill>(entity))
            {
                commandBuffer.AddComponent<StandingStill>(entity);
            }
            else if(!moving && SystemAPI.HasComponent<StandingStill>(entity))
            {
                commandBuffer.RemoveComponent<StandingStill>(entity);
            }
        }
    }
}