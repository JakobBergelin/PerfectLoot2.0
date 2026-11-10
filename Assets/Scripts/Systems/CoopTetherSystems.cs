using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

/// <summary>Singleton: couch co-op's rules (CoopSettings, pushed by CoopManager).</summary>
public struct CoopRules : IComponentData
{
    public float MaxDistance;
    public bool KeepInCameraView;
}

/// <summary>On players in co-op: where they were after last frame's tether, a spot known to be free.</summary>
public struct CoopTether : IComponentData
{
    public float3 LastPosition;
}

/// <summary>
/// Couch co-op: living players can't get further apart than CoopRules allow (a distance, and what the camera fits at its
/// widest). After everything that moved them this frame (walking, movement skills), a player whose move took them too
/// far from the other is put on the edge of that circle around them, or back where they were last frame when the edge
/// isn't free (a teleport into a wall). Alone, or with the other player dead, nothing is limited.
/// </summary>
[UpdateInGroup(typeof(MovementSystemGroup), OrderLast = true)]
public partial struct CoopTetherSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<CoopRules>();
        state.RequireForUpdate<PhysicsWorldSingleton>();
        state.RequireForUpdate<MovementConfig>();
    }

    public void OnUpdate(ref SystemState state)
    {
        var rules = SystemAPI.GetSingleton<CoopRules>();
        float limit = rules.MaxDistance;
        if (rules.KeepInCameraView)
            foreach (var follow in SystemAPI.Query<RefRO<CameraFollow>>())
                limit = math.min(limit, 2f * follow.ValueRO.ComfortRadius * math.max(follow.ValueRO.MaxZoomOut, 1f));

        // Living players; a player seen for the first time starts where they are
        var players = new NativeList<Entity>(Allocator.Temp);
        var fresh = new NativeList<Entity>(Allocator.Temp);
        foreach (var (health, entity) in SystemAPI.Query<RefRO<Health>>().WithAll<Player, PlayerIndex, LocalTransform>().WithEntityAccess())
        {
            if (health.ValueRO.CurrentValue <= 0f) continue;
            if (SystemAPI.HasComponent<CoopTether>(entity)) players.Add(entity);
            else fresh.Add(entity);
        }
        foreach (var entity in fresh)
            state.EntityManager.AddComponentData(entity, new CoopTether { LastPosition = SystemAPI.GetComponent<LocalTransform>(entity).Position });

        if (players.Length == 2)
        {
            var world = SystemAPI.GetSingleton<PhysicsWorldSingleton>().CollisionWorld;
            float minWalkableNormalY = math.cos(math.radians(SystemAPI.GetSingleton<MovementConfig>().MaxSlope));
            for (int i = 0; i < 2; i++)
                Hold(ref state, players[i], players[1 - i], limit, world, minWalkableNormalY);
        }

        foreach (var entity in players)
            SystemAPI.SetComponent(entity, new CoopTether { LastPosition = SystemAPI.GetComponent<LocalTransform>(entity).Position });
    }

    // Keeps the player within limit of the other one, when their own move took them too far (a player who stood still
    // is never pulled along: the other one is held back instead)
    void Hold(ref SystemState state, Entity player, Entity other, float limit, in CollisionWorld world, float minWalkableNormalY)
    {
        var transform = SystemAPI.GetComponent<LocalTransform>(player);
        float3 otherPosition = SystemAPI.GetComponent<LocalTransform>(other).Position;
        float2 away = transform.Position.xz - otherPosition.xz;
        float distance = math.length(away);
        float before = math.distance(SystemAPI.GetComponent<CoopTether>(player).LastPosition.xz, otherPosition.xz);
        if (distance <= limit || distance <= before) return;

        float2 edge = otherPosition.xz + away / distance * limit;
        var position = new float3(edge.x, transform.Position.y, edge.y);
        var collider = SystemAPI.HasComponent<PhysicsCollider>(player) ? SystemAPI.GetComponent<PhysicsCollider>(player) : default;
        if (!TeleportSystem.Fits(world, collider, position, transform.Rotation, transform.Scale, player, minWalkableNormalY))
            position = SystemAPI.GetComponent<CoopTether>(player).LastPosition;
        transform.Position = position;
        SystemAPI.SetComponent(player, transform);
    }
}
