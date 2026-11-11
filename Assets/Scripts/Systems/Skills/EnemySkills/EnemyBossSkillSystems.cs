using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>Whose side an enemy skill is on: an enemy's hits players; a companion's is its owner's, scaled by their stats, and hits enemies.</summary>
public static class EnemySkillSide
{
    public static void Of(Entity unit, bool isCompanion, Entity owner, out Entity source, out TargetType targetType)
    {
        source = isCompanion ? owner : unit;
        targetType = isCompanion ? TargetType.Enemy : TargetType.Player;
    }

    /// <summary>A one-frame Attack from the unit, its Source and target type set; a companion's also takes its owner's stats.</summary>
    public static Entity SpawnHit(EntityCommandBuffer commandBuffer, Entity unit, Entity source, TargetType targetType, bool isCompanion, in LocalTransform transform, in Attack attack)
    {
        var hit = commandBuffer.CreateEntity();
        commandBuffer.AddComponent(hit, transform);
        commandBuffer.AddComponent(hit, attack);
        commandBuffer.AddComponent(hit, new Source { Entity = source });
        commandBuffer.AddComponent(hit, new AttackTarget { TargetType = targetType });
        if (isCompanion) commandBuffer.AddComponent(hit, new InitializeFromSource());
        commandBuffer.AddComponent(hit, new DestroyWithChildren());   // A single-frame hit
        return hit;
    }
}

/// <summary>
/// Ground Slam and Beam: telegraphed areas that hit everything in them once their marker is full. Ground Slam is a
/// circle on the spot the target stood on when the cast began (its Circle telegraph); Beam a rectangle from the unit
/// toward the target (its Line telegraph). Their sizes come from the telegraphs, so the hits match the markers.
/// </summary>
[UpdateInGroup(typeof(SkillSystemGroup))]
[UpdateAfter(typeof(EnemySkillCastSystem))]
[BurstCompile]
public partial struct EnemyAreaSkillSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state) => state.RequireForUpdate<BeginInitializationEntityCommandBufferSystem.Singleton>();

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var commandBuffer = SystemAPI.GetSingleton<BeginInitializationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
        var companions = SystemAPI.GetComponentLookup<Companion>(true);

        foreach (var (slam, fires, entity) in SystemAPI.Query<RefRO<EnemyGroundSlam>, DynamicBuffer<EnemySkillFire>>().WithEntityAccess())
        {
            bool isCompanion = companions.HasComponent(entity);
            EnemySkillSide.Of(entity, isCompanion, isCompanion ? companions[entity].Owner : Entity.Null, out var source, out var targetType);
            var s = slam.ValueRO;
            foreach (var fire in fires)
            {
                if (fire.Type != EnemySkillType.GroundSlam) continue;
                var transform = new LocalTransform { Position = fire.Position, Rotation = quaternion.identity, Scale = 1f };
                EnemySkillSide.SpawnHit(commandBuffer, entity, source, targetType, isCompanion, transform,
                    new Attack { Origin = new DamageOrigin { Unit = entity }, Damage = s.Damage, Type = s.Type, Radius = s.Radius, DamageHierarchy = DamageHierarchy.Main, HitForce = s.HitForce });
            }
        }

        foreach (var (beam, fires, unitTransform, entity) in SystemAPI.Query<RefRO<EnemyBeam>, DynamicBuffer<EnemySkillFire>, RefRO<LocalTransform>>().WithEntityAccess())
        {
            bool isCompanion = companions.HasComponent(entity);
            EnemySkillSide.Of(entity, isCompanion, isCompanion ? companions[entity].Owner : Entity.Null, out var source, out var targetType);
            var b = beam.ValueRO;
            foreach (var fire in fires)
            {
                if (fire.Type != EnemySkillType.Beam) continue;
                var direction = math.normalizesafe(new float3(fire.Direction.x, 0f, fire.Direction.z), new float3(0f, 0f, 1f));
                var transform = new LocalTransform
                {
                    Position = unitTransform.ValueRO.Position + direction * (b.Length * 0.5f),
                    Rotation = quaternion.LookRotationSafe(direction, math.up()),
                    Scale = 1f
                };
                var hit = EnemySkillSide.SpawnHit(commandBuffer, entity, source, targetType, isCompanion, transform,
                    new Attack { Origin = new DamageOrigin { Unit = entity }, Damage = b.Damage, Type = b.Type, Radius = b.Length * 0.5f, DamageHierarchy = DamageHierarchy.Main, HitForce = b.HitForce });
                commandBuffer.AddComponent(hit, new AttackShape { Kind = AttackShapeKind.Rectangle, HalfExtents = new float3(b.Width * 0.5f, b.Height * 0.5f, b.Length * 0.5f) });
            }
        }
    }
}

/// <summary>
/// Homing Volley: Count missiles fanned evenly over Spread degrees toward the target, each homing in on the unit's
/// current target (HomingProjectileSystem), turning faster the longer they fly.
/// </summary>
[UpdateInGroup(typeof(SkillSystemGroup))]
[UpdateAfter(typeof(EnemySkillCastSystem))]
[BurstCompile]
public partial struct EnemyHomingVolleySystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<EnemyHomingVolley>();
        state.RequireForUpdate<BeginInitializationEntityCommandBufferSystem.Singleton>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var commandBuffer = SystemAPI.GetSingleton<BeginInitializationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
        var companions = SystemAPI.GetComponentLookup<Companion>(true);

        foreach (var (volley, fires, transform, target, entity) in
                 SystemAPI.Query<RefRO<EnemyHomingVolley>, DynamicBuffer<EnemySkillFire>, RefRO<LocalTransform>, RefRO<EnemyTarget>>().WithEntityAccess())
        {
            bool isCompanion = companions.HasComponent(entity);
            EnemySkillSide.Of(entity, isCompanion, isCompanion ? companions[entity].Owner : Entity.Null, out var source, out var targetType);
            var v = volley.ValueRO;
            foreach (var fire in fires)
            {
                if (fire.Type != EnemySkillType.HomingVolley) continue;
                var aim = math.normalizesafe(new float3(fire.Direction.x, 0f, fire.Direction.z), new float3(0f, 0f, 1f));
                int count = math.max(1, v.Count);
                float half = math.radians(v.Spread * 0.5f);
                for (int i = 0; i < count; i++)
                {
                    float angle = count == 1 ? 0f : math.lerp(-half, half, i / (float)(count - 1));
                    var direction = math.rotate(quaternion.RotateY(angle), aim);
                    var position = transform.ValueRO.Position + direction;

                    var missile = commandBuffer.CreateEntity();
                    commandBuffer.AddComponent(missile, new LocalTransform { Position = position, Rotation = quaternion.LookRotationSafe(direction, math.up()), Scale = 0.5f });
                    commandBuffer.AddComponent(missile, new Projectile { Direction = direction, Speed = v.ProjectileSpeed, DestroyOnHit = true, FollowGround = true });
                    commandBuffer.AddComponent(missile, new HomingProjectile { Target = target.ValueRO.Unit, TurnSpeed = v.TurnSpeed, TurnAcceleration = v.TurnAcceleration, HomingType = HomingType.Closest });
                    commandBuffer.AddComponent(missile, new ProjectileRange { MaxDistance = v.MaxDistance, LastPosition = position });
                    commandBuffer.AddComponent(missile, new Attack { Origin = new DamageOrigin { Unit = entity }, Damage = v.Damage, Type = v.Type, DamageHierarchy = DamageHierarchy.Main, Radius = v.Radius, HitForce = v.HitForce });
                    commandBuffer.AddComponent(missile, new Source { Entity = source });
                    commandBuffer.AddComponent(missile, new AttackTarget { TargetType = targetType });
                    commandBuffer.AddComponent(missile, new InitializeFromSource());
                }
            }
        }
    }
}
