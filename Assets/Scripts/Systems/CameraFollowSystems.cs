using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

// Follows the rendered position (LocalToWorld, which includes physics smoothing), so it runs after the transforms
[UpdateAfter(typeof(TransformSystemGroup))]
public partial struct CameraFollowSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        // Get player position
        float3 playerPosition = float3.zero;
        bool hasPlayer = false;

        foreach (var (player, transform) in SystemAPI.Query<RefRO<Player>, RefRO<LocalToWorld>>())
        {
            playerPosition = transform.ValueRO.Position;
            hasPlayer = true;
        }

        // Dead / not spawned yet: keep the camera where it is
        if (!hasPlayer)
        {
            return;
        }

        // Move camera
       foreach (var (cameraTransform, follow) in SystemAPI.Query<RefRW<LocalTransform>, RefRO<CameraFollow>>())
        {        
            cameraTransform.ValueRW.Position = playerPosition + follow.ValueRO.Offset;  
            cameraTransform.ValueRW.Rotation = follow.ValueRO.Rotation;         
        }
    }
}
