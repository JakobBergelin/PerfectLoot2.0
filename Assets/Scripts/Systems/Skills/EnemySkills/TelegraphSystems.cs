using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

public static class TelegraphUtil
{
    const float ProbeUp = 2f, ProbeDown = 10f;
    // How far above and below position the marker reaches: a slope of up to 50 degrees all the way out to Size
    const float MinReach = 1.5f, ReachPerSize = 1.2f;

    /// <summary>
    /// A marker for telegraph at position, pointing along direction. The box reaches Size to every side of position; the
    /// shader cuts the shape out of the ground inside it.
    /// </summary>
    public static void Spawn(EntityCommandBuffer commandBuffer, Entity prefab, in EnemyTelegraph telegraph, Entity caster, int castId, float3 position, float3 direction)
    {
        var marker = commandBuffer.Instantiate(prefab);
        float size = math.max(telegraph.Size, 0.01f);
        float width = telegraph.Shape == TelegraphShape.Line ? telegraph.Width * 0.5f / size : math.radians(telegraph.Width * 0.5f);
        float reach = math.max(MinReach, size * ReachPerSize) / size;

        commandBuffer.SetComponent(marker, new LocalTransform { Position = position, Rotation = quaternion.LookRotationSafe(direction, math.up()), Scale = size * 2f });
        commandBuffer.SetComponent(marker, new TelegraphParams { Value = new float4(0f, (float)telegraph.Shape, width, reach) });
        commandBuffer.SetComponent(marker, new TelegraphMarker { Caster = caster, CastId = castId });
    }

    /// <summary>
    /// The shortest way out of a marker's area for point, along the ground: the direction times the meters to go, to margin
    /// past its edge. Zero when point is already that far outside. Circle: straight out from the middle. Line: out the
    /// nearer side. Cone: out the nearer edge or past its far end, whichever is shorter.
    /// </summary>
    public static float3 WayOut(in LocalTransform marker, in TelegraphParams parameters, float3 point, float margin)
    {
        float size = marker.Scale * 0.5f;
        var offset = new float3(point.x - marker.Position.x, 0f, point.z - marker.Position.z);
        var forward = math.forward(marker.Rotation);
        forward = math.normalizesafe(new float3(forward.x, 0f, forward.z), new float3(0f, 0f, 1f));
        var right = new float3(forward.z, 0f, -forward.x);
        float along = math.dot(offset, forward);
        float side = math.dot(offset, right);
        float sideSign = side >= 0f ? 1f : -1f;
        float distance = math.length(offset);

        switch ((TelegraphShape)(int)math.round(parameters.Value.y))
        {
            case TelegraphShape.Circle:
            {
                float needed = size + margin - distance;
                return needed > 0f ? math.normalizesafe(offset, right) * needed : float3.zero;
            }
            case TelegraphShape.Line:
            {
                if (along < -margin || along > size + margin) return float3.zero;
                float needed = parameters.Value.z * size + margin - math.abs(side);
                return needed > 0f ? right * sideSign * needed : float3.zero;
            }
            case TelegraphShape.Cone:
            {
                float halfAngle = parameters.Value.z;
                float outside = math.abs(math.atan2(side, along)) - halfAngle;  // Radians outside its edge, negative inside
                if (distance > size + margin || outside >= math.PI * 0.5f) return float3.zero;
                float sideways = margin - distance * math.sin(outside);        // Meters to margin past the nearer edge
                float outward = size + margin - distance;                       // Meters to margin past its far end
                if (sideways <= 0f) return float3.zero;
                // Square to the nearer edge, away from the middle of the cone
                var edgeNormal = forward * -math.sin(halfAngle) + right * sideSign * math.cos(halfAngle);
                return sideways <= outward ? edgeNormal * sideways : math.normalizesafe(offset, forward) * outward;
            }
            default:
                return float3.zero;
        }
    }

    /// <summary>WayOut of every marker, added up: zero when point is outside all of them.</summary>
    public static float3 WayOut(NativeArray<LocalTransform> markers, NativeArray<TelegraphParams> parameters, float3 point, float margin)
    {
        var way = float3.zero;
        for (int i = 0; i < markers.Length; i++) way += WayOut(markers[i], parameters[i], point, margin);
        return way;
    }

    /// <summary>The spot on the ground under position, or position itself without ground there.</summary>
    public static float3 OnGround(in PhysicsWorldSingleton physics, float3 position)
    {
        var ray = new RaycastInput
        {
            Start = position + new float3(0f, ProbeUp, 0f),
            End = position - new float3(0f, ProbeDown, 0f),
            Filter = ProjectileUtil.GroundFilter,
        };
        return physics.CastRay(ray, out var hit) ? hit.Position : position;
    }
}

/// <summary>
/// Fills each marker as its cast goes on and removes it when the cast ends, is cancelled or its caster is gone. On the host:
/// markers are ghosts, so the other machines get the fill and the removal from it.
/// </summary>
[UpdateInGroup(typeof(FeedbackSystemGroup))]
[WorldSystemFilter(GameWorlds.Server)]
[BurstCompile]
public partial struct TelegraphMarkerSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<TelegraphMarker>();
        state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var commandBuffer = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
        var casts = SystemAPI.GetComponentLookup<EnemySkillCast>(true);
        state.CompleteDependency();  // The lookup is used here on the main thread

        foreach (var (marker, parameters, entity) in SystemAPI.Query<RefRO<TelegraphMarker>, RefRW<TelegraphParams>>().WithEntityAccess())
        {
            var caster = marker.ValueRO.Caster;
            if (!casts.HasComponent(caster) || !casts[caster].Active || casts[caster].CastId != marker.ValueRO.CastId)
            {
                commandBuffer.DestroyEntity(entity);
                continue;
            }
            var cast = casts[caster];
            parameters.ValueRW.Value.x = 1f - math.saturate(cast.TimeRemaining / cast.Duration);
        }
    }
}
