using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;

/// <summary>
/// Captain's Shield: while held, throws a shield toward the cursor whenever none of the caster's shields is out and
/// the cost can be paid. Its first target is the enemy closest to the cursor. ShieldProjectileSystem flies it.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(SkillSystemGroup))]
public partial struct CaptainsShieldSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<MovementConfig>();
        state.RequireForUpdate<CombatConfig>();
        state.RequireForUpdate<BeginInitializationEntityCommandBufferSystem.Singleton>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var commandBuffer = SystemAPI.GetSingleton<BeginInitializationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
        var movementConfig = SystemAPI.GetSingleton<MovementConfig>();
        float targetRange = SystemAPI.GetSingleton<CombatConfig>().HomingTargetRange;
        var physics = SystemAPI.GetSingleton<PhysicsWorldSingleton>();
        var targets = new ShieldTargets(ref state);

        // Casters with a shield out can't throw another
        var shieldOut = new NativeHashSet<Entity>(4, Allocator.Temp);
        foreach (var source in SystemAPI.Query<RefRO<Source>>().WithAll<ShieldProjectile>())
            shieldOut.Add(source.ValueRO.Entity);

        // Casts requested by effects (e.g. Echoing Blade): the normal cost, skipped with a shield out
        foreach (var (shield, requests, skills, resourceChanges, localTransform, stats, entity) in
                 SystemAPI.Query<RefRO<CaptainsShield>, DynamicBuffer<SkillCastRequest>, DynamicBuffer<Skill>, DynamicBuffer<ResourceChanger>, RefRO<LocalTransform>, RefRO<CalculatedStats>>()
                          .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState).WithEntityAccess())
        {
            while (SkillUtil.TryTakeRequest(requests, SkillType.CaptainsShield, out var request))
            {
                if (shieldOut.Contains(entity) || SystemAPI.GetComponent<Resource>(entity).CurrentValue < shield.ValueRO.ResourceCost) continue;
                var position = localTransform.ValueRO.Position;
                var aim = SystemAPI.HasComponent<InputsData>(entity)
                    ? SystemAPI.GetComponent<InputsData>(entity).MousePosition
                    : position + request.Direction * shield.ValueRO.NoTargetDistance;
                Throw(commandBuffer, entity, shield.ValueRO, stats.ValueRO.Value, position, request.Direction, aim, physics, targets, targetRange);
                shieldOut.Add(entity);
                resourceChanges.Add(new ResourceChanger{Value = -shield.ValueRO.ResourceCost});
                SkillUtil.MarkUsed(skills, SkillType.CaptainsShield, request.AutoCast);
            }
        }

        foreach (var (shield, skills, resourceChanges, localTransform, desiredDirection, inputs, entity) in
                 SystemAPI.Query<RefRO<CaptainsShield>, DynamicBuffer<Skill>, DynamicBuffer<ResourceChanger>, RefRW<LocalTransform>, RefRO<DesiredDirection>, RefRO<InputsData>>()
                          .WithEntityAccess())
        {
            localTransform.ValueRW.Rotation = Quaternion.RotateTowards(localTransform.ValueRO.Rotation, desiredDirection.ValueRO.Quaternion, movementConfig.RotationSpeed * SystemAPI.Time.DeltaTime);
            if (shieldOut.Contains(entity) || SystemAPI.GetComponent<Resource>(entity).CurrentValue < shield.ValueRO.ResourceCost) continue;
            if (Quaternion.Angle(localTransform.ValueRO.Rotation, desiredDirection.ValueRO.Quaternion) > (math.PI / 2)) continue;

            Throw(commandBuffer, entity, shield.ValueRO, SystemAPI.GetComponent<CalculatedStats>(entity).Value, localTransform.ValueRO.Position, desiredDirection.ValueRO.Direction,
                  inputs.ValueRO.MousePosition, physics, targets, targetRange);
            shieldOut.Add(entity);
            resourceChanges.Add(new ResourceChanger{Value = -shield.ValueRO.ResourceCost});
            SkillUtil.MarkUsed(skills, SkillType.CaptainsShield);
        }

        // No cooldown; active while a shield is out (after SkillActivationSystem's defaults)
        foreach (var (skills, entity) in SystemAPI.Query<DynamicBuffer<Skill>>().WithAll<CaptainsShield>()
                     .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState).WithEntityAccess())
        {
            int index = -1;
            if (SkillUtil.FindSkill(skills, SkillType.CaptainsShield, ref index))
                skills.ElementAt(index).State.IsActive = shieldOut.Contains(entity);
        }
    }

    /// <summary>
    /// One shield from the caster: toward the enemy closest to aim, or toward aim when there is none. Damage is scaled
    /// by the caster's block chance; projectile stats are added by InitializeFromSourceSystem.
    /// </summary>
    static void Throw(EntityCommandBuffer commandBuffer, Entity caster, in CaptainsShield shield, in StatBlock stats, float3 casterPosition, float3 direction,
                      float3 aim, in PhysicsWorldSingleton physics, in ShieldTargets targets, float targetRange)
    {
        var flatDirection = math.normalizesafe(new float3(direction.x, 0f, direction.z), new float3(0f, 0f, 1f));
        var start = casterPosition + flatDirection;

        var shieldProjectile = new ShieldProjectile
        {
            NoTargetDistance = shield.NoTargetDistance,
            BaseTurnSpeed = shield.TurnSpeed,
            TurnSpeed = shield.TurnSpeed,
            TurnAcceleration = shield.TurnAcceleration,
        };
        var target = targets.Closest(physics, aim, targetRange, caster, shieldProjectile.Targeted);
        if (target != Entity.Null)
        {
            shieldProjectile.Target = target;
            shieldProjectile.Targeted.Add(target);
            var toTarget = targets.Transforms[target].Position - start;
            flatDirection = math.normalizesafe(new float3(toTarget.x, 0f, toTarget.z), flatDirection);
        }
        else
        {
            var toAim = aim - casterPosition;
            flatDirection = math.normalizesafe(new float3(toAim.x, 0f, toAim.z), flatDirection);
        }

        var newEntity = commandBuffer.CreateEntity();
        commandBuffer.AddComponent(newEntity, new LocalTransform{Position = start, Rotation = quaternion.identity, Scale = 0.5f});
        commandBuffer.AddComponent(newEntity, new Projectile{Direction = flatDirection, Speed = shield.ProjectileSpeed, DestroyOnHit = false, BounceCount = shield.Bounces, PierceCount = 0});
        commandBuffer.AddComponent(newEntity, shieldProjectile);
        commandBuffer.AddComponent(newEntity, new Attack{Damage = shield.Damage * math.max(0f, 1f + stats.BlockChance), Type = DamageType.Physical, DamageHierarchy = DamageHierarchy.Main, Radius = shield.Radius, HitForce = shield.HitForce});
        commandBuffer.AddComponent(newEntity, new Source{Entity = caster});
        commandBuffer.AddComponent(newEntity, new AttackTarget{TargetType = TargetType.Enemy});
        commandBuffer.AddComponent(newEntity, new InitializeFromSource{});
    }
}

/// <summary>
/// Flies Captain's Shields. It homes on its target (turning faster the longer it turns) and reaching it counts as a
/// bounce: then it goes for the closest enemy it hasn't homed on yet, while Projectile.BounceCount lasts. Without a
/// target it flies NoTargetDistance toward the cursor. After that, or on hitting a wall, it returns to the caster
/// through walls and is gone when it gets there. It hits every enemy it passes on the way (Attack). Runs after
/// OrbitSteeringSystem, so Event Horizon pulls on it but the shield's growing turn speed wins in the end.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(MovementSystemGroup))]
[UpdateAfter(typeof(OrbitSteeringSystem))]
[UpdateBefore(typeof(ProjectileSystem))]
public partial struct ShieldProjectileSystem : ISystem
{
    // Flat distance to a target's center (beyond the shield's radius) that counts as reaching it
    const float ReachPadding = 0.5f;
    // Flat distance to the caster at which a returning shield is caught
    const float CatchDistance = 1f;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<CombatConfig>();
        state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        // The lookups are used here on the main thread: UnitMoverSystem's job may still be writing LocalTransform
        state.CompleteDependency();
        var commandBuffer = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
        var physics = SystemAPI.GetSingleton<PhysicsWorldSingleton>();
        float targetRange = SystemAPI.GetSingleton<CombatConfig>().HomingTargetRange;
        var targets = new ShieldTargets(ref state);
        var transforms = SystemAPI.GetComponentLookup<LocalTransform>();
        var wallFilter = ProjectileUtil.WallFilter;
        float deltaTime = SystemAPI.Time.DeltaTime;

        foreach (var (shield, projectile, attack, source, entity) in
                 SystemAPI.Query<RefRW<ShieldProjectile>, RefRW<Projectile>, RefRO<Attack>, RefRO<Source>>().WithNone<DestroyWithChildren>().WithEntityAccess())
        {
            var owner = source.ValueRO.Entity;
            if (!transforms.HasComponent(entity) || !transforms.HasComponent(owner))
            {
                commandBuffer.AddComponent<DestroyWithChildren>(entity);
                continue;
            }
            var position = transforms[entity].Position;
            ref var s = ref shield.ValueRW;

            if (!s.Returning)
            {
                if (s.Target != Entity.Null && !targets.IsAlive(s.Target))
                {
                    // Lost its target before reaching it: a new one, without using a bounce
                    s.Target = targets.Closest(physics, position, targetRange, owner, s.Targeted);
                    if (s.Target == Entity.Null) Return(ref s);
                    else Retarget(ref s, s.Target);
                }
                else if (s.Target != Entity.Null && FlatDistance(position, transforms[s.Target].Position) <= attack.ValueRO.Radius + ReachPadding)
                {
                    var next = projectile.ValueRO.BounceCount > 0 ? targets.Closest(physics, position, targetRange, owner, s.Targeted) : Entity.Null;
                    if (next == Entity.Null) Return(ref s);
                    else
                    {
                        projectile.ValueRW.BounceCount--;
                        Retarget(ref s, next);
                    }
                }
                else if (s.Target == Entity.Null)
                {
                    s.Distance += projectile.ValueRO.Speed * deltaTime;
                    if (s.Distance >= s.NoTargetDistance) Return(ref s);
                }
            }

            if (s.Returning && FlatDistance(position, transforms[owner].Position) <= CatchDistance)
            {
                commandBuffer.AddComponent<DestroyWithChildren>(entity);
                continue;
            }

            // Steer toward the target, or the caster when returning
            var goal = s.Returning ? owner : s.Target;
            var direction = projectile.ValueRO.Direction;
            if (goal != Entity.Null)
            {
                var toGoal = transforms[goal].Position - position;
                float speed = math.length(direction);
                direction = Vector3.RotateTowards(direction, math.normalizesafe(toGoal, direction) * speed, s.TurnSpeed * deltaTime, 0f);
                projectile.ValueRW.Direction = direction;
                s.TurnSpeed += s.TurnAcceleration * deltaTime;
            }

            var end = position + direction * projectile.ValueRO.Speed * deltaTime;
            // A wall ends the way out; the way back goes through walls
            if (!s.Returning && physics.CastRay(new RaycastInput{Start = position, End = end, Filter = wallFilter}, out var hit))
            {
                end = hit.Position + hit.SurfaceNormal * 0.01f;
                Return(ref s);
            }
            transforms.GetRefRW(entity).ValueRW.Position = end;
        }
    }

    static void Retarget(ref ShieldProjectile shield, Entity target)
    {
        shield.Target = target;
        if (shield.Targeted.Length < shield.Targeted.Capacity) shield.Targeted.Add(target);
        shield.TurnSpeed = shield.BaseTurnSpeed;
    }

    static void Return(ref ShieldProjectile shield)
    {
        shield.Returning = true;
        shield.Target = Entity.Null;
        shield.TurnSpeed = shield.BaseTurnSpeed;
    }

    static float FlatDistance(float3 a, float3 b) => math.length((a - b).xz);
}

/// <summary>Finding Captain's Shield targets: living enemies, closest to a point, not homed on yet.</summary>
public struct ShieldTargets
{
    [ReadOnly] public ComponentLookup<LocalTransform> Transforms;
    [ReadOnly] ComponentLookup<Enemy> _enemies;
    [ReadOnly] ComponentLookup<Health> _healths;

    public ShieldTargets(ref SystemState state)
    {
        Transforms = state.GetComponentLookup<LocalTransform>(true);
        _enemies = state.GetComponentLookup<Enemy>(true);
        _healths = state.GetComponentLookup<Health>(true);
    }

    public bool IsAlive(Entity entity) =>
        Transforms.HasComponent(entity) && _healths.HasComponent(entity) && _healths[entity].CurrentValue > 0f;

    public Entity Closest(in PhysicsWorldSingleton physics, float3 point, float range, Entity caster, FixedList128Bytes<Entity> exclude)
    {
        var hits = new NativeList<DistanceHit>(Allocator.Temp);
        var filter = new CollisionFilter{BelongsTo = ~0u, CollidesWith = (uint)CollisionLayerHelper.AnyEnemy, GroupIndex = 0};
        physics.OverlapSphere(point, range, ref hits, filter);

        Entity best = Entity.Null;
        float bestDistance = float.MaxValue;
        foreach (var hit in hits)
        {
            if (hit.Entity == caster || !_enemies.HasComponent(hit.Entity) || !IsAlive(hit.Entity) || exclude.Contains(hit.Entity)) continue;
            float distance = math.distancesq(Transforms[hit.Entity].Position, point);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = hit.Entity;
            }
        }
        hits.Dispose();
        return best;
    }
}
