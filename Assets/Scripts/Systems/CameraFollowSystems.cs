using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

/// <summary>
/// Moves the camera toward the middle of the players plus its offset, smoothed (SmoothTime) and no faster than MaxSpeed,
/// so teleports and dashes glide instead of jumping. Snaps there when the gap is over SnapDistance, or when it first finds
/// a player. Zooms out (moves back along its view, up to MaxZoomOut) when a player or a CameraFocus near a player reaches
/// further than ComfortRadius from that middle: quickly out, slowly back in after ZoomInDelay. Follows the rendered
/// position (LocalToWorld, which includes physics smoothing), so it runs after the transforms. Also wears off the
/// camera's shake (Trauma) and works out this frame's ShakeOffset.
/// </summary>
[UpdateAfter(typeof(TransformSystemGroup))]
public partial struct CameraFollowSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        // The players and their middle (test players count, so several players can be tried)
        var players = new NativeList<float3>(Allocator.Temp);
        float3 center = float3.zero;
        foreach (var transform in SystemAPI.Query<RefRO<LocalToWorld>>().WithAll<Player>())
        {
            players.Add(transform.ValueRO.Position);
            center += transform.ValueRO.Position;
        }
        bool hasPlayer = players.Length > 0;
        if (hasPlayer) center /= players.Length;

        float deltaTime = SystemAPI.Time.DeltaTime;
        foreach (var (cameraTransform, follow) in SystemAPI.Query<RefRW<LocalTransform>, RefRW<CameraFollow>>())
        {
            ref var f = ref follow.ValueRW;
            // Dead / not spawned yet: keep the camera where it is, and snap to the player once there is one again
            if (!hasPlayer)
            {
                f.HasTarget = false;
                f.Trauma = 0f;
                f.ShakeOffset = float3.zero;
                continue;
            }

            // How far the content reaches from the middle, along the ground: the players, and bosses near one of them
            float reach = 0f;
            foreach (var player in players)
                reach = math.max(reach, math.distance(player.xz, center.xz));
            foreach (var (focus, transform) in SystemAPI.Query<RefRO<CameraFocus>, RefRO<LocalToWorld>>().WithNone<RigidDeath>())
            {
                float3 position = transform.ValueRO.Position;
                if (!AnyPlayerWithin(players, position, f.FocusRange)) continue;
                reach = math.max(reach, math.distance(position.xz, center.xz) + focus.ValueRO.Radius);
            }
            UpdateZoom(ref f, reach, deltaTime);

            // Zooming moves the camera back from the point it aims at, so the view's angle stays the same
            var lookUp = new float3(0f, f.LookHeight, 0f);
            float3 target = center + lookUp + (f.Offset - lookUp) * f.Zoom;
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

            // Shake: smooth noise in the camera's own plane, trauma squared so small shakes stay small. Kept apart from the
            // position above so the smoothing never follows it.
            f.Trauma = math.max(0f, f.Trauma - f.ShakeDecay * deltaTime);
            float time = (float)SystemAPI.Time.ElapsedTime * f.ShakeFrequency;
            float2 wobble = new float2(noise.snoise(new float2(time, 0f)), noise.snoise(new float2(0f, time)));
            float3 right = math.mul(f.Rotation, math.right());
            float3 up = math.mul(f.Rotation, math.up());
            f.ShakeOffset = (right * wobble.x + up * wobble.y) * f.ShakeStrength * f.Trauma * f.Trauma;
        }
    }

    static bool AnyPlayerWithin(NativeList<float3> players, float3 position, float range)
    {
        foreach (var player in players)
            if (math.distancesq(player.xz, position.xz) <= range * range) return true;
        return false;
    }

    // The zoom that fits the reach, capped at MaxZoomOut: out right away, back in only after ZoomInDelay, and slower
    static void UpdateZoom(ref CameraFollow f, float reach, float deltaTime)
    {
        float wanted = math.clamp(reach / math.max(f.ComfortRadius, 0.01f), 1f, math.max(f.MaxZoomOut, 1f));
        if (!f.HasTarget || f.Zoom < 1f)
        {
            // Snapping to a player (or the first frame): no glide
            f.Zoom = wanted;
            f.ZoomVelocity = 0f;
            f.ZoomInWait = f.ZoomInDelay;
            return;
        }

        float time;
        if (wanted >= f.Zoom)
        {
            f.ZoomInWait = f.ZoomInDelay;
            time = f.ZoomOutTime;
        }
        else
        {
            f.ZoomInWait = math.max(0f, f.ZoomInWait - deltaTime);
            if (f.ZoomInWait > 0f)
            {
                f.ZoomVelocity = 0f;
                return;
            }
            time = f.ZoomInTime;
        }
        f.Zoom = Mathf.SmoothDamp(f.Zoom, wanted, ref f.ZoomVelocity, math.max(time, 0.01f), Mathf.Infinity, deltaTime);
    }
}
