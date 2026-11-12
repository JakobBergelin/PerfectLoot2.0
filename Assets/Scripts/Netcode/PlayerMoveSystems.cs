using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Physics;
using Unity.Transforms;

/// <summary>
/// On every player ghost (baked by NetReplicatedAuthoring): what its owner's machine needs to predict its walking, worked
/// out on the host from its stats and effects (PredictedMoveStateSystem) and sent with each snapshot.
/// </summary>
public struct PredictedMove : IComponentData
{
    [GhostField(Quantization = 100)] public float Speed;    // Meters per second at full stick, slows included
    [GhostField] public bool Locked;                        // Can't walk now: frozen, rising, dashing, flying, rooted...
    [GhostField] public bool FreeTurn;                      // Turns toward where it walks (not while DisableTurning)
}

/// <summary>
/// On the host, after this tick's stats and effects: each player's PredictedMove. Locked while frozen or otherwise
/// prevented, rising out of the ground, or while a skill moves or holds the player itself (Dash, Teleport, Ball
/// Lightning, Sonic Ball, Hookshot Slash, Phantom Edge, a channeled Tesla Coil).
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(HealthAndResourceSystemGroup), OrderLast = true)]
public partial struct PredictedMoveStateSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state) => state.RequireForUpdate<MovementConfig>();

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var config = SystemAPI.GetSingleton<MovementConfig>();
        foreach (var (move, speed, entity) in SystemAPI.Query<RefRW<PredictedMove>, RefRO<MovementSpeed>>().WithAll<Player>().WithEntityAccess())
        {
            var modifiers = SystemAPI.HasComponent<MovementModifiers>(entity) ? SystemAPI.GetComponent<MovementModifiers>(entity) : new MovementModifiers { SpeedMultiplier = 1f };
            bool locked = modifiers.PreventsActions || SystemAPI.HasComponent<SpawnRise>(entity)
                       || (SystemAPI.HasComponent<Dash>(entity) && SystemAPI.GetComponent<Dash>(entity).Dashing)
                       || (SystemAPI.HasComponent<Teleport>(entity) && SystemAPI.GetComponent<Teleport>(entity).Casting)
                       || (SystemAPI.HasComponent<BallLightning>(entity) && SystemAPI.GetComponent<BallLightning>(entity).Flying)
                       || (SystemAPI.HasComponent<SonicBall>(entity) && SystemAPI.GetComponent<SonicBall>(entity).Rolling)
                       || (SystemAPI.HasComponent<HookshotSlash>(entity) && SystemAPI.GetComponent<HookshotSlash>(entity).State != HookshotState.Idle)
                       || (SystemAPI.HasComponent<PhantomEdgeFlurry>(entity) && SystemAPI.GetComponent<PhantomEdgeFlurry>(entity).Active)
                       || (SystemAPI.HasComponent<TeslaCoil>(entity) && SystemAPI.GetComponent<TeslaCoil>(entity).Channeling);
            move.ValueRW = new PredictedMove
            {
                Speed = config.MovementSpeed * (1f + speed.ValueRO.Value) * modifiers.SpeedMultiplier,
                Locked = locked,
                FreeTurn = !SystemAPI.HasComponent<DisableTurning>(entity),
            };
        }
    }
}

/// <summary>
/// Players walk here, in Netcode's prediction loop: on the host for every player, and on the machine that owns a player
/// ghost, ahead of the host, replaying its inputs since the last snapshot, so walking feels instant. It turns toward
/// where it walks (slower while still turning) and moves with the same collide-and-slide as every unit (UnitStep).
/// Knockback is added on the host only, so the owner sees it when the snapshot corrects it. Everything else about the
/// player still runs on the host only; CharacterMovementSystem and UnitMoverSystem leave players to this.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(PredictedSimulationSystemGroup))]
[UpdateAfter(typeof(PredictedFixedStepSimulationSystemGroup))]
public partial struct PlayerMoveSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<PhysicsWorldSingleton>();
        state.RequireForUpdate<MovementConfig>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var config = SystemAPI.GetSingleton<MovementConfig>();
        float deltaTime = SystemAPI.Time.DeltaTime;
        var step = UnitStep.Create(SystemAPI.GetSingleton<PhysicsWorldSingleton>().CollisionWorld, config, deltaTime);
        float maxTurn = math.radians(config.RotationSpeed) * deltaTime;

        foreach (var (transform, input, move, collider, entity) in
                 SystemAPI.Query<RefRW<LocalTransform>, RefRO<NetPlayerInput>, RefRO<PredictedMove>, RefRO<PhysicsCollider>>()
                     .WithAll<Player, Simulate>().WithEntityAccess())
        {
            var m = move.ValueRO;
            var stick = input.ValueRO.Move;
            var velocity = float3.zero;
            if (!m.Locked && math.lengthsq(stick) > 0f)
            {
                var direction = math.normalize(new float3(stick.x, 0f, stick.y));
                float speed = m.Speed;
                if (m.FreeTurn)
                {
                    // Turn toward where it walks; the further it still has to turn, the slower it goes (as CharacterMovementSystem did)
                    var target = quaternion.LookRotationSafe(direction, math.up());
                    transform.ValueRW.Rotation = RotateTowards(transform.ValueRO.Rotation, target, maxTurn);
                    float angle = math.degrees(Angle(transform.ValueRO.Rotation, target));
                    speed = math.remap(0f, 180f, m.Speed, 0f, angle);
                }
                velocity = direction * speed;
            }
            if (SystemAPI.HasComponent<Knockback>(entity)) velocity += SystemAPI.GetComponent<Knockback>(entity).Velocity;

            bool phased = SystemAPI.HasComponent<Phased>(entity) && SystemAPI.IsComponentEnabled<Phased>(entity);
            uint blockLayers = UnitMovement.WorldLayers | (phased ? 0u : (uint)CollisionLayerHelper.Enemy);
            uint floorLayers = UnitMovement.GroundLayer |
                               (SystemAPI.HasComponent<WaterWalking>(entity) && SystemAPI.IsComponentEnabled<WaterWalking>(entity) ? (uint)CollisionLayerHelper.Water : 0u);
            var actual = step.Move(entity, ref transform.ValueRW, velocity, collider.ValueRO, blockLayers, floorLayers);

            if (SystemAPI.HasComponent<UnitMotion>(entity))
                SystemAPI.SetComponent(entity, new UnitMotion { Velocity = velocity, Actual = actual });
        }
    }

    static float Angle(quaternion a, quaternion b) => 2f * math.acos(math.min(math.abs(math.dot(a.value, b.value)), 1f));

    static quaternion RotateTowards(quaternion from, quaternion to, float maxRadians)
    {
        float angle = Angle(from, to);
        if (angle <= maxRadians || angle < 1e-5f) return to;
        return math.slerp(from, to, maxRadians / angle);
    }
}
