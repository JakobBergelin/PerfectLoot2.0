using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

/// <summary>
/// Flamethrower while held: turns the unit toward the cursor, pays the start cost once, then drains resource and sprays
/// flames in a cone. Each flame is a ground-following projectile with a fire PeriodicAttack that grows (GrowingFlameSystem)
/// until its lifetime runs out; walls put it out, enemies don't. While spraying the unit is Channeling, turns only toward
/// the cursor (DisableTurning) and is slowed (FlamethrowerSlowSystem). Casts requested by effects are ignored.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(SkillSystemGroup))]
public partial class FlamethrowerSystem : SystemBase
{
    const float MaxAimAngle = 30f;      // Degrees off the cursor the unit may still be turned and spray
    const float SpawnDistance = 0.8f;   // Meters in front of the unit flames start at

    private Unity.Mathematics.Random _random;
    private BeginInitializationEntityCommandBufferSystem _beginInitializationEntityCommandBufferSystem;

    [BurstCompile]
    protected override void OnCreate()
    {
        _random = Unity.Mathematics.Random.CreateFromIndex((uint)System.Environment.TickCount);
        _beginInitializationEntityCommandBufferSystem = World.DefaultGameObjectInjectionWorld.GetExistingSystemManaged<BeginInitializationEntityCommandBufferSystem>();
        RequireForUpdate<MovementConfig>();
    }

    [BurstCompile]
    protected override void OnUpdate()
    {
        var commandBuffer = _beginInitializationEntityCommandBufferSystem.CreateCommandBuffer();
        var movementConfig = SystemAPI.GetSingleton<MovementConfig>();
        float deltaTime = SystemAPI.Time.DeltaTime;

        foreach (var (flamethrower, localTransform, desiredDirection, resource, resourceChanges, stats, entity) in
                 SystemAPI.Query<RefRW<Flamethrower>, RefRW<LocalTransform>, RefRO<DesiredDirection>, RefRO<Resource>, DynamicBuffer<ResourceChanger>, RefRO<CalculatedStats>>()
                          .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState).WithEntityAccess())
        {
            ref var f = ref flamethrower.ValueRW;
            var statBlock = stats.ValueRO.Value;
            bool held = SystemAPI.IsComponentEnabled<Flamethrower>(entity);

            // Starting costs the start cost on top of the first frame's drain; continuing only the drain
            float drain = SkillUtil.Cost(f.ResourceCostPerSecond, statBlock) * deltaTime;
            float needed = drain + (f.Spraying ? 0f : SkillUtil.Cost(f.ResourceCost, statBlock));
            bool spraying = held && resource.ValueRO.CurrentValue >= needed;

            if (spraying != f.Spraying)
            {
                f.Spraying = spraying;
                f.Elapsed = 0f;
                if (spraying)
                {
                    commandBuffer.AddComponent<Channeling>(entity);
                    commandBuffer.AddComponent<DisableTurning>(entity);
                }
                else
                {
                    commandBuffer.RemoveComponent<Channeling>(entity);
                    commandBuffer.RemoveComponent<DisableTurning>(entity);
                }
            }
            if (!spraying) continue;

            resourceChanges.Add(new ResourceChanger{Value = -needed});

            localTransform.ValueRW.Rotation = Quaternion.RotateTowards(localTransform.ValueRO.Rotation, desiredDirection.ValueRO.Quaternion, movementConfig.RotationSpeed * deltaTime);
            if (Quaternion.Angle(localTransform.ValueRO.Rotation, desiredDirection.ValueRO.Quaternion) > MaxAimAngle) continue;

            f.Elapsed += deltaTime;
            float interval = 1f / math.max(0.01f, f.FlamesPerSecond);
            bool sprayed = false;
            while (f.Elapsed >= interval)
            {
                f.Elapsed -= interval;
                Spawn(commandBuffer, entity, f, statBlock, localTransform.ValueRO.Position, desiredDirection.ValueRO.Direction, ref _random);
                sprayed = true;
            }
            if (sprayed && SystemAPI.HasBuffer<Skill>(entity)) SkillUtil.MarkUsed(SystemAPI.GetBuffer<Skill>(entity), SkillType.Flamethrower);
        }
    }

    /// <summary>One flame toward direction, turned up to Spread to either side. The AoE stat scales its size.</summary>
    static void Spawn(EntityCommandBuffer commandBuffer, Entity caster, in Flamethrower flamethrower, in StatBlock stats, float3 casterPosition, float3 direction, ref Unity.Mathematics.Random random)
    {
        var newEntity = commandBuffer.CreateEntity();

        direction.y = 0f;
        direction = math.normalizesafe(direction, new float3(0f, 0f, 1f));
        direction = math.rotate(quaternion.RotateY(random.NextFloat(-flamethrower.Spread, flamethrower.Spread)), direction);

        float size = math.max(0f, 1f + stats.AreaOfEffect);
        float startRadius = flamethrower.StartRadius * size;
        float endRadius = flamethrower.EndRadius * size;

        commandBuffer.AddComponent(newEntity, new LocalTransform{Position = casterPosition + direction * SpawnDistance, Rotation = quaternion.identity, Scale = 1f});
        commandBuffer.AddComponent(newEntity, new PeriodicAttack{DamagePerSecond = flamethrower.DamagePerSecond, Type = DamageType.Fire, Radius = startRadius, TimeRemaining = flamethrower.Lifetime,
            Skill = SkillType.Flamethrower, TickRate = flamethrower.TickRate, HitForce = flamethrower.HitForce});
        commandBuffer.AddComponent(newEntity, new GrowingFlame{StartRadius = startRadius, EndRadius = endRadius, Lifetime = flamethrower.Lifetime});
        commandBuffer.AddComponent(newEntity, new Source{Entity = caster});
        commandBuffer.AddComponent(newEntity, new AttackTarget{TargetType = TargetType.Enemy});
        // A projectile (so projectile bonuses apply) along the ground; passes through enemies, a wall puts it out
        commandBuffer.AddComponent(newEntity, new Projectile{Direction = direction, Speed = flamethrower.Speed, DestroyOnHit = false, FollowGround = true});
        commandBuffer.AddComponent(newEntity, new InitializeFromSource{});
    }
}

/// <summary>Grows each flame's PeriodicAttack radius with its age, before it deals this frame's damage.</summary>
[BurstCompile]
[UpdateInGroup(typeof(AttackSystemGroup))]
[UpdateBefore(typeof(PeriodicAttackSystem))]
public partial struct GrowingFlameSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (flame, periodicAttack) in SystemAPI.Query<RefRO<GrowingFlame>, RefRW<PeriodicAttack>>())
        {
            float age = flame.ValueRO.Lifetime > 0f ? math.saturate(1f - periodicAttack.ValueRO.TimeRemaining / flame.ValueRO.Lifetime) : 1f;
            periodicAttack.ValueRW.Radius = math.lerp(flame.ValueRO.StartRadius, flame.ValueRO.EndRadius, age);
        }
    }
}

/// <summary>Slows a unit while it sprays its Flamethrower (from the previous frame's spray state).</summary>
[BurstCompile]
[UpdateInGroup(typeof(FlatStatsSystemGroup))]
public partial struct FlamethrowerSlowSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (flamethrower, stats) in SystemAPI.Query<RefRO<Flamethrower>, RefRW<CalculatedStats>>().WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
        {
            if (flamethrower.ValueRO.Spraying) stats.ValueRW.Value.MovementSpeed -= flamethrower.ValueRO.Slow;
        }
    }
}
