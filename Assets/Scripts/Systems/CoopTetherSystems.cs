using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Physics;
using Unity.Transforms;

/// <summary>Singleton: couch co-op's rules (CoopSettings, pushed by CoopManager).</summary>
public struct CoopRules : IComponentData
{
    public float MaxDistance;
    public bool KeepInCameraView;

    /// <summary>How far apart a machine's players may get: the distance, and what the camera fits at its widest.</summary>
    public float Limit(in CameraFollow follow) =>
        KeepInCameraView ? math.min(MaxDistance, 2f * follow.ComfortRadius * math.max(follow.MaxZoomOut, 1f)) : MaxDistance;
}

/// <summary>On players in co-op: where they were before they moved (after last frame's tether), a spot known to be free.</summary>
public struct CoopTether : IComponentData
{
    public float3 LastPosition;
}

/// <summary>
/// Couch co-op: the living players of one machine (GhostOwner) share a camera, so they can't get further apart than
/// CoopRules allow. Players of different machines aren't held. After everything that moved them this frame (walking,
/// movement skills), a player whose move took them too far from another is put on the edge of that circle around them, or
/// back where they were last frame when the edge isn't free (a teleport into a wall). Alone, or with the others dead,
/// nothing is limited. On the host, for every machine's players; CoopTetherPredictionSystem does it ahead on a client.
/// </summary>
[UpdateInGroup(typeof(MovementSystemGroup), OrderLast = true)]
public partial struct CoopTetherSystem : ISystem
{
    TetherHold _hold;

    public void OnCreate(ref SystemState state)
    {
        _hold = new TetherHold(ref state);
        state.RequireForUpdate<CoopRules>();
        state.RequireForUpdate<CameraFollow>();
        state.RequireForUpdate<PhysicsWorldSingleton>();
        state.RequireForUpdate<MovementConfig>();
    }

    public void OnUpdate(ref SystemState state)
    {
        // Living players; a player seen for the first time starts where they are
        var players = new NativeList<Entity>(Allocator.Temp);
        var fresh = new NativeList<Entity>(Allocator.Temp);
        foreach (var (health, entity) in SystemAPI.Query<RefRO<Health>>().WithAll<Player, PlayerIndex>().WithAll<GhostOwner, LocalTransform>().WithEntityAccess())
        {
            if (health.ValueRO.CurrentValue <= 0f) continue;
            if (SystemAPI.HasComponent<CoopTether>(entity)) players.Add(entity);
            else fresh.Add(entity);
        }
        foreach (var entity in fresh)
            state.EntityManager.AddComponentData(entity, new CoopTether { LastPosition = SystemAPI.GetComponent<LocalTransform>(entity).Position });

        _hold.Update(ref state, SystemAPI.GetSingleton<CoopRules>().Limit(SystemAPI.GetSingleton<CameraFollow>()),
                     SystemAPI.GetSingleton<PhysicsWorldSingleton>().CollisionWorld, SystemAPI.GetSingleton<MovementConfig>().MaxSlope);
        _hold.HoldAll(players, players);

        foreach (var entity in players)
            SystemAPI.SetComponent(entity, new CoopTether { LastPosition = SystemAPI.GetComponent<LocalTransform>(entity).Position });
    }
}

/// <summary>
/// In someone else's game: holds this machine's players while predicting their walking, as the host will, so they don't
/// run past the limit and get pulled back. PlayerMoveSystem keeps where the tick started (CoopTether).
/// </summary>
[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
[UpdateInGroup(typeof(PredictedSimulationSystemGroup))]
[UpdateAfter(typeof(PlayerMoveSystem))]
public partial struct CoopTetherPredictionSystem : ISystem
{
    TetherHold _hold;

    public void OnCreate(ref SystemState state)
    {
        _hold = new TetherHold(ref state);
        // A host's own players are held with everyone else's (CoopTetherSystem)
        if (state.WorldUnmanaged.IsServer()) state.Enabled = false;
        state.RequireForUpdate<CoopRules>();
        state.RequireForUpdate<CameraFollow>();
        state.RequireForUpdate<PhysicsWorldSingleton>();
        state.RequireForUpdate<MovementConfig>();
    }

    public void OnUpdate(ref SystemState state)
    {
        // Only the ones this tick moves are held, but by all of them
        var players = new NativeList<Entity>(Allocator.Temp);
        var moved = new NativeList<Entity>(Allocator.Temp);
        foreach (var (health, entity) in SystemAPI.Query<RefRO<Health>>().WithAll<Player, CoopTether>().WithAll<GhostOwner, LocalTransform>()
                     .WithPresent<Simulate>().WithEntityAccess())
        {
            if (health.ValueRO.CurrentValue <= 0f) continue;
            players.Add(entity);
            if (SystemAPI.IsComponentEnabled<Simulate>(entity)) moved.Add(entity);
        }

        _hold.Update(ref state, SystemAPI.GetSingleton<CoopRules>().Limit(SystemAPI.GetSingleton<CameraFollow>()),
                     SystemAPI.GetSingleton<PhysicsWorldSingleton>().CollisionWorld, SystemAPI.GetSingleton<MovementConfig>().MaxSlope);
        _hold.HoldAll(moved, players);
    }
}

/// <summary>The co-op distance itself, for the host and for a client's prediction.</summary>
struct TetherHold
{
    ComponentLookup<LocalTransform> _transforms;
    [ReadOnly] ComponentLookup<CoopTether> _tethers;
    [ReadOnly] ComponentLookup<GhostOwner> _owners;
    [ReadOnly] ComponentLookup<PhysicsCollider> _colliders;
    CollisionWorld _world;
    float _limit, _minWalkableNormalY;

    public TetherHold(ref SystemState state)
    {
        this = default;
        _transforms = state.GetComponentLookup<LocalTransform>();
        _tethers = state.GetComponentLookup<CoopTether>(true);
        _owners = state.GetComponentLookup<GhostOwner>(true);
        _colliders = state.GetComponentLookup<PhysicsCollider>(true);
    }

    public void Update(ref SystemState state, float limit, in CollisionWorld world, float maxSlope)
    {
        _transforms.Update(ref state);
        _tethers.Update(ref state);
        _owners.Update(ref state);
        _colliders.Update(ref state);
        _limit = limit;
        _world = world;
        _minWalkableNormalY = math.cos(math.radians(maxSlope));
    }

    /// <summary>Each player is held back by the first other one of their machine their move took them too far from.</summary>
    public void HoldAll(NativeList<Entity> players, NativeList<Entity> others)
    {
        foreach (var player in players)
            foreach (var other in others)
                if (other != player && _owners[other].NetworkId == _owners[player].NetworkId && Hold(player, other)) break;
    }

    // Keeps the player within the limit of the other one, when their own move took them too far (a player who stood still
    // is never pulled along: the other one is held back instead). True when it held them.
    bool Hold(Entity player, Entity other)
    {
        var transform = _transforms[player];
        float3 otherPosition = _transforms[other].Position;
        float2 away = transform.Position.xz - otherPosition.xz;
        float distance = math.length(away);
        float3 last = _tethers[player].LastPosition;
        if (distance <= _limit || distance <= math.distance(last.xz, otherPosition.xz)) return false;

        float2 edge = otherPosition.xz + away / distance * _limit;
        var position = new float3(edge.x, transform.Position.y, edge.y);
        var collider = _colliders.HasComponent(player) ? _colliders[player] : default;
        if (!TeleportSystem.Fits(_world, collider, position, transform.Rotation, transform.Scale, player, _minWalkableNormalY)) position = last;
        transform.Position = position;
        _transforms[player] = transform;
        return true;
    }
}
