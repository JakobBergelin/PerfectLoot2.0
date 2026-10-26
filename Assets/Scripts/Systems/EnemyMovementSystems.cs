using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

/// <summary>
/// Enemy AI movement, in parallel: notices the player within DetectionRadius, chases it, stops within AttackRadius,
/// and steers away from nearby units (UnitSpatialGrid) so crowds spread out around the player instead of piling up.
/// Only writes the wanted horizontal velocity (UnitMotion); UnitMoverSystem moves them.
/// </summary>
[UpdateInGroup(typeof(MovementSystemGroup))]
[UpdateAfter(typeof(UnitSpatialGridSystem))]
[BurstCompile]
public partial struct EnemyMovementSystem : ISystem
{
    EntityQuery _playerQuery;

    public void OnCreate(ref SystemState state)
    {
        _playerQuery = SystemAPI.QueryBuilder().WithAll<Player, LocalTransform>().Build();
        state.RequireForUpdate<UnitSpatialGrid>();
        state.RequireForUpdate<MovementConfig>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var player = Entity.Null;
        var playerPosition = float3.zero;
        if (_playerQuery.CalculateEntityCount() == 1)
        {
            player = _playerQuery.GetSingletonEntity();
            playerPosition = SystemAPI.GetComponent<LocalTransform>(player).Position;
        }

        state.Dependency = new EnemyMovementJob
        {
            Player = player,
            PlayerPosition = playerPosition,
            Grid = SystemAPI.GetSingleton<UnitSpatialGrid>().Units,
            Config = SystemAPI.GetSingleton<MovementConfig>(),
            DeltaTime = SystemAPI.Time.DeltaTime,
        }.ScheduleParallel(state.Dependency);

        // Enemies without AI (training dummies) stand still: without this, a push or knockback would keep them sliding
        state.Dependency = new StationaryEnemyJob().ScheduleParallel(state.Dependency);
    }

    [BurstCompile]
    [WithAll(typeof(Enemy))]
    [WithNone(typeof(NpcBehavior))]
    partial struct StationaryEnemyJob : IJobEntity
    {
        void Execute(ref UnitMotion motion)
        {
            motion.Velocity = float3.zero;
        }
    }

    // Enemies still rising out of the ground (SpawnRise) don't act yet
    [BurstCompile]
    [WithNone(typeof(SpawnRise))]
    partial struct EnemyMovementJob : IJobEntity
    {
        public Entity Player;
        public float3 PlayerPosition;
        [ReadOnly] public NativeParallelMultiHashMap<int, GridUnit> Grid;
        public MovementConfig Config;
        public float DeltaTime;

        void Execute(Entity entity, ref NpcBehavior npc, ref LocalTransform transform, ref UnitMotion motion,
            in MovementSpeed movementSpeed, in Steering steering, in MovementModifiers modifiers)
        {
            // Frozen and similar: stand still (knockback still pushes) and don't turn or attack
            if (modifiers.PreventsActions)
            {
                npc.IsAttacking = false;
                motion.Velocity = float3.zero;
                return;
            }

            var toPlayer = PlayerPosition - transform.Position;
            toPlayer.y = 0f;
            float distance = math.length(toPlayer);

            // Notice the player once it is close enough; forget a player that no longer exists (respawned)
            if (npc.TargetPlayer != Player) npc.TargetPlayer = Entity.Null;
            if (Player != Entity.Null && npc.TargetPlayer == Entity.Null && distance <= npc.DetectionRadius)
                npc.TargetPlayer = Player;

            bool chasing = npc.TargetPlayer != Entity.Null;
            npc.IsAttacking = chasing && distance <= npc.AttackRadius;

            float speed = movementSpeed.Value * modifiers.SpeedMultiplier;
            var desired = float3.zero;
            if (chasing && distance > 1e-4f)
            {
                var direction = toPlayer / distance;
                var lookRotation = quaternion.LookRotationSafe(direction, math.up());
                npc.Direction = direction;
                npc.DesiredDirection = lookRotation;
                npc.TurnSpeed += npc.TurnAcceleration * DeltaTime;
                transform.Rotation = math.slerp(transform.Rotation, lookRotation, math.saturate(npc.TurnSpeed * DeltaTime));

                // Slower while still turning toward the player, stopped while attacking
                float angle = math.degrees(Angle(transform.Rotation, lookRotation));
                if (!npc.IsAttacking)
                    desired = direction * math.remap(0f, 180f, speed, 0f, math.min(angle, 180f));
            }

            // Push away from overlapping neighbours (other enemies and the player)
            desired += Separation(entity, transform.Position, steering.Radius) * Config.SeparationStrength(steering.BodyType);

            // The push may exceed the walk speed a little, so a packed crowd can still spread
            float maxSpeed = math.max(speed, 1f) * 1.5f;
            float desiredSpeed = math.length(desired);
            if (desiredSpeed > maxSpeed) desired *= maxSpeed / desiredSpeed;

            // Horizontal only: UnitMoverSystem moves it, colliding and following the floor
            motion.Velocity = new float3(desired.x, 0f, desired.z);
        }

        float3 Separation(Entity self, float3 position, float radius)
        {
            var push = float3.zero;
            var cell = UnitSpatialGrid.Cell(position);
            for (int x = -1; x <= 1; x++)
            for (int z = -1; z <= 1; z++)
            {
                if (!Grid.TryGetFirstValue(UnitSpatialGrid.Hash(cell + new int2(x, z)), out var other, out var iterator)) continue;
                do
                {
                    if (other.Entity == self) continue;
                    var away = position - other.Position;
                    away.y = 0f;
                    float minDistance = (radius + other.Radius) * Config.SeparationSpacing;
                    float distanceSq = math.lengthsq(away);
                    if (distanceSq >= minDistance * minDistance) continue;

                    float distance = math.sqrt(distanceSq);
                    // Exactly on top of each other: pick a direction from the entity index so they still split
                    var direction = distance > 1e-4f ? away / distance : new float3(math.sin(self.Index), 0f, math.cos(self.Index));
                    push += direction * (1f - distance / minDistance);
                }
                while (Grid.TryGetNextValue(out other, ref iterator));
            }
            return push;
        }

        static float Angle(quaternion a, quaternion b)
        {
            float dot = math.abs(math.dot(a.value, b.value));
            return 2f * math.acos(math.min(dot, 1f));
        }
    }
}
