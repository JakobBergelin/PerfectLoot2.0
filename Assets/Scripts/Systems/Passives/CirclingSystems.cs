using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>Circling passive: instead of walking up to its target, the enemy runs around it at Radius.</summary>
public struct Circling : IComponentData
{
    public float Radius;
    public float Lead;          // Radians ahead along the circle it heads for
    public float Sign;          // 1 or -1: which way round. 0: not picked yet
    public float FlipCooldown;  // Seconds before it may turn around again
}

/// <summary>
/// Circling enemies, after EnemyMovementSystem: once within a few meters of Radius from their target they head for a
/// point Lead ahead of them on the circle around it, so they keep running round it, closing in or backing out to Radius.
/// Something in the way of that point turns them around. Farther away, still casting, frozen or without a target,
/// EnemyMovementSystem's chase is kept. Only writes the wanted horizontal velocity (UnitMotion) and turns the enemy.
/// </summary>
[UpdateInGroup(typeof(MovementSystemGroup))]
[UpdateAfter(typeof(EnemyMovementSystem))]
[UpdateBefore(typeof(UnitMoverSystem))]
[BurstCompile]
public partial struct CirclingSystem : ISystem
{
    const float JoinDistance = 3f;  // Meters beyond Radius at which it stops chasing and starts circling
    const float FlipTime = 1f;

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<Circling>();
        state.RequireForUpdate<UnitSpatialGrid>();
        state.RequireForUpdate<FlowFields>();
        state.RequireForUpdate<EnemyTargets>();
        state.RequireForUpdate<MovementConfig>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        state.Dependency = new CirclingJob
        {
            Targets = SystemAPI.GetSingleton<EnemyTargets>().Units.AsArray(),
            Grid = SystemAPI.GetSingleton<UnitSpatialGrid>().Units,
            Fields = SystemAPI.GetSingleton<FlowFields>(),
            Config = SystemAPI.GetSingleton<MovementConfig>(),
            DeltaTime = SystemAPI.Time.DeltaTime,
        }.ScheduleParallel(state.Dependency);
    }

    [BurstCompile]
    [WithNone(typeof(SpawnRise), typeof(Boid))]
    partial struct CirclingJob : IJobEntity
    {
        [ReadOnly] public NativeArray<EnemyTargetUnit> Targets;
        [ReadOnly] public NativeParallelMultiHashMap<int, GridUnit> Grid;
        [ReadOnly] public FlowFields Fields;
        public MovementConfig Config;
        public float DeltaTime;

        void Execute(Entity entity, ref Circling circling, ref NpcBehavior npc, ref LocalTransform transform, ref UnitMotion motion,
            in MovementSpeed movementSpeed, in Steering steering, in MovementModifiers modifiers, in EnemyTarget target)
        {
            circling.FlipCooldown -= DeltaTime;
            if (modifiers.PreventsActions || npc.IsCasting) return;
            int targetIndex = EnemyTargets.Find(Targets, target.Unit);
            if (targetIndex < 0) return;
            var unit = Targets[targetIndex];
            var fromTarget = transform.Position - unit.Position;
            fromTarget.y = 0f;
            float distance = math.length(fromTarget);
            if (distance > circling.Radius + JoinDistance || distance < 1e-4f) return;

            if (circling.Sign == 0f) circling.Sign = (entity.Index & 1) == 0 ? 1f : -1f;
            var field = Fields[unit.FieldSlot];
            var goal = Goal(unit.Position, fromTarget / distance, circling.Radius, circling.Lead * circling.Sign);
            if (!field.Clear(transform.Position, goal) && circling.FlipCooldown <= 0f)
            {
                circling.Sign = -circling.Sign;
                circling.FlipCooldown = FlipTime;
                goal = Goal(unit.Position, fromTarget / distance, circling.Radius, circling.Lead * circling.Sign);
            }

            var direction = math.normalizesafe(new float3(goal.x - transform.Position.x, 0f, goal.z - transform.Position.z));
            var lookRotation = quaternion.LookRotationSafe(direction, math.up());
            npc.Direction = direction;
            npc.DesiredDirection = lookRotation;
            transform.Rotation = math.slerp(transform.Rotation, lookRotation, math.saturate(math.max(npc.TurnSpeed, 4f) * DeltaTime));

            float speed = movementSpeed.Value * modifiers.SpeedMultiplier;
            var desired = direction * speed;
            desired += UnitSpatialGrid.Separation(Grid, entity, transform.Position, steering.Radius, Config.SeparationSpacing) * Config.SeparationStrength(steering.BodyType);
            float maxSpeed = math.max(speed, 1f) * 1.5f;
            float desiredSpeed = math.length(desired);
            if (desiredSpeed > maxSpeed) desired *= maxSpeed / desiredSpeed;
            motion.Velocity = new float3(desired.x, 0f, desired.z);
        }

        // The point on the circle of radius round center, angle radians on from where outward points
        static float3 Goal(float3 center, float3 outward, float radius, float angle)
        {
            math.sincos(angle, out float s, out float c);
            var turned = new float3(outward.x * c - outward.z * s, 0f, outward.x * s + outward.z * c);
            return center + turned * radius;
        }
    }
}
