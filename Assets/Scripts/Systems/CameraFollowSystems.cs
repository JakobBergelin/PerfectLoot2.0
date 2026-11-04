using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

/// <summary>
/// Moves the camera toward the player plus its offset, smoothed (SmoothTime) and no faster than MaxSpeed, so teleports
/// and dashes glide instead of jumping. Snaps there when the gap is over SnapDistance, or when it first finds a player.
/// Follows the rendered position (LocalToWorld, which includes physics smoothing), so it runs after the transforms.
/// </summary>
[UpdateAfter(typeof(TransformSystemGroup))]
public partial struct CameraFollowSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        // Get player position
        float3 playerPosition = float3.zero;
        bool hasPlayer = false;

        foreach (var (player, transform) in SystemAPI.Query<RefRO<Player>, RefRO<LocalToWorld>>().WithNone<TestPlayer>())
        {
            playerPosition = transform.ValueRO.Position;
            hasPlayer = true;
        }

        float deltaTime = SystemAPI.Time.DeltaTime;
        foreach (var (cameraTransform, follow) in SystemAPI.Query<RefRW<LocalTransform>, RefRW<CameraFollow>>())
        {
            ref var f = ref follow.ValueRW;
            // Dead / not spawned yet: keep the camera where it is, and snap to the player once there is one again
            if (!hasPlayer)
            {
                f.HasTarget = false;
                continue;
            }

            float3 target = playerPosition + f.Offset;
            float3 current = cameraTransform.ValueRO.Position;
            if (!f.HasTarget || math.distance(current, target) > f.SnapDistance)
            {
                current = target;
                f.Velocity = float3.zero;
                f.HasTarget = true;
            }
            else
            {
                Vector3 velocity = f.Velocity;
                current = Vector3.SmoothDamp(current, target, ref velocity, math.max(f.SmoothTime, 1e-4f), f.MaxSpeed, deltaTime);
                f.Velocity = velocity;
            }

            cameraTransform.ValueRW.Position = current;
            cameraTransform.ValueRW.Rotation = f.Rotation;
        }
    }
}
