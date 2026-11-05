using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

/// <summary>
/// Enemy AI movement, in parallel: chases its target (EnemyTarget, set by EnemyDetectionSystem) around walls, props,
/// water and cliffs along its FlowField, stops within AttackRadius, and steers away from nearby units (UnitSpatialGrid) so crowds
/// spread out around the player instead of piling up. Ranged enemies (DesiredDistance) walk until they see the target within
/// reach, and back away from one that gets too close; casting enemies stand still. Only writes the wanted horizontal velocity (UnitMotion);
/// UnitMoverSystem moves them.
/// </summary>
[UpdateInGroup(typeof(MovementSystemGroup))]
[UpdateAfter(typeof(UnitSpatialGridSystem))]
[UpdateAfter(typeof(FlowFieldSystem))]
[BurstCompile]
public partial struct EnemyMovementSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<UnitSpatialGrid>();
        state.RequireForUpdate<FlowFields>();
        state.RequireForUpdate<EnemyTargets>();
        state.RequireForUpdate<MovementConfig>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        state.Dependency = new EnemyMovementJob
        {
            Targets = SystemAPI.GetSingleton<EnemyTargets>().Units.AsArray(),
            Grid = SystemAPI.GetSingleton<UnitSpatialGrid>().Units,
            Fields = SystemAPI.GetSingleton<FlowFields>(),
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

    // Enemies still rising out of the ground (SpawnRise) don't act yet. Companions that move have BoidSystem.
    [BurstCompile]
    [WithNone(typeof(SpawnRise), typeof(Boid))]
    partial struct EnemyMovementJob : IJobEntity
    {
        [ReadOnly] public NativeArray<EnemyTargetUnit> Targets;
        [ReadOnly] public NativeParallelMultiHashMap<int, GridUnit> Grid;
        [ReadOnly] public FlowFields Fields;
        public MovementConfig Config;
        public float DeltaTime;

        void Execute(Entity entity, ref NpcBehavior npc, ref LocalTransform transform, ref UnitMotion motion,
            in MovementSpeed movementSpeed, in Steering steering, in MovementModifiers modifiers, in EnemyTarget target)
        {
            // Frozen and similar: stand still (knockback still pushes) and don't turn or attack
            if (modifiers.PreventsActions)
            {
                npc.IsAttacking = false;
                motion.Velocity = float3.zero;
                return;
            }

            int targetIndex = EnemyTargets.Find(Targets, target.Unit);
            bool chasing = targetIndex >= 0;
            var unit = chasing ? Targets[targetIndex] : default;
            var toTarget = unit.Position - transform.Position;
            toTarget.y = 0f;
            float distance = math.length(toTarget);

            npc.IsAttacking = chasing && distance <= npc.AttackRadius;
            var field = Fields[unit.FieldSlot];
            bool ranged = npc.DesiredDistance > 0f;
            if (ranged && npc.IsAttacking && !field.CanSee(transform.Position, unit.Position)) npc.IsAttacking = false;

            float speed = movementSpeed.Value * modifiers.SpeedMultiplier;
            var desired = float3.zero;
            if (chasing && distance > 1e-4f && !npc.IsCasting)
            {
                // Straight at the target when nothing is in the way, else along the flow field
                var direction = field.Direction(transform.Position, unit.Position);
                var lookRotation = quaternion.LookRotationSafe(direction, math.up());
                npc.Direction = direction;
                npc.DesiredDirection = lookRotation;
                npc.TurnSpeed += npc.TurnAcceleration * DeltaTime;
                transform.Rotation = math.slerp(transform.Rotation, lookRotation, math.saturate(npc.TurnSpeed * DeltaTime));

                // Slower while still turning toward the target, stopped while attacking
                float angle = math.degrees(Angle(transform.Rotation, lookRotation));
                if (!npc.IsAttacking)
                    desired = direction * math.remap(0f, 180f, speed, 0f, math.min(angle, 180f));
                else if (ranged && distance < npc.DesiredDistance - npc.DistanceTolerance)
                    desired = BackAway(field, transform.Position, toTarget / distance) * speed;
            }

            // Push away from overlapping neighbours (other enemies and the player)
            desired += UnitSpatialGrid.Separation(Grid, entity, transform.Position, steering.Radius, Config.SeparationSpacing) * Config.SeparationStrength(steering.BodyType);

            // The push may exceed the walk speed a little, so a packed crowd can still spread
            float maxSpeed = math.max(speed, 1f) * 1.5f;
            float desiredSpeed = math.length(desired);
            if (desiredSpeed > maxSpeed) desired *= maxSpeed / desiredSpeed;

            // Horizontal only: UnitMoverSystem moves it, colliding and following the floor
            motion.Velocity = new float3(desired.x, 0f, desired.z);
        }

        // Straight away from the target, or nowhere with something in the way
        static float3 BackAway(in FlowField field, float3 position, float3 toTarget)
        {
            return field.Clear(position, position - toTarget * field.CellSize * 2f) ? -toTarget : float3.zero;
        }

        static float Angle(quaternion a, quaternion b)
        {
            float dot = math.abs(math.dot(a.value, b.value));
            return 2f * math.acos(math.min(dot, 1f));
        }
    }
}
