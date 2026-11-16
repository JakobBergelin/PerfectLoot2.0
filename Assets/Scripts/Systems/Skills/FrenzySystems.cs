using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

/// <summary>
/// Frenzy: pressing its key switches it on or off, even while another skill is held, and its slot stays lit while it's
/// on. While on, the buff starts when the resource is full, drains DrainPerSecond of max resource every second (lowered
/// by ResourceCostReduction, SkillUtil.Cost), and ends when the resource drops below EndThreshold of max. It starts again the next
/// time the resource is full. Switching it off ends the buff.
/// </summary>
[UpdateInGroup(typeof(SkillSystemGroup))]
[BurstCompile]
public partial struct FrenzySystem : ISystem
{
    public void OnCreate(ref SystemState state) => state.RequireForUpdate<Frenzy>();

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float deltaTime = SystemAPI.Time.DeltaTime;

        // Never enabled: the key is read here, so that a skill in a lower slot can't hold it back
        foreach (var (frenzy, inputs, modifiers, skills, resource, resourceChanges, stats) in
                 SystemAPI.Query<RefRW<Frenzy>, RefRO<InputsData>, RefRO<MovementModifiers>, DynamicBuffer<Skill>, RefRO<Resource>, DynamicBuffer<ResourceChanger>, RefRO<CalculatedStats>>()
                     .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
        {
            int index = -1;
            if (!SkillUtil.FindSkill(skills, SkillType.Frenzy, ref index)) continue;
            // Requirements not met in a dungeon: off, and nothing happens
            if (!skills[index].State.Usable)
            {
                frenzy.ValueRW.On = false;
                frenzy.ValueRW.Active = false;
                skills.ElementAt(index).State.IsActive = false;
                continue;
            }

            int slot = skills[index].SlotIndex;
            if (slot >= 0 && slot < 4 && inputs.ValueRO.SkillPressedThisFrame[slot] && !modifiers.ValueRO.PreventsActions)
            {
                frenzy.ValueRW.On = !frenzy.ValueRO.On;
                SkillUtil.MarkUsed(skills, SkillType.Frenzy);
            }
            skills.ElementAt(index).State.IsActive = frenzy.ValueRO.On;

            float max = resource.ValueRO.MaxValue;
            float current = resource.ValueRO.CurrentValue;
            if (!frenzy.ValueRO.On || max <= 0f)
            {
                frenzy.ValueRW.Active = false;
                continue;
            }

            if (!frenzy.ValueRO.Active && current >= max) frenzy.ValueRW.Active = true;
            else if (frenzy.ValueRO.Active && current < max * frenzy.ValueRO.EndThreshold) frenzy.ValueRW.Active = false;

            if (frenzy.ValueRO.Active)
                resourceChanges.Add(new ResourceChanger { Value = -SkillUtil.Cost(max * frenzy.ValueRO.DrainPerSecond, stats.ValueRO.Value) * deltaTime });
        }
    }
}

/// <summary>
/// Frenzy's extra strike: each melee attack (MeleeAttackUsed) a caster makes while the buff is active also strikes the
/// second nearest living enemy within StrikeRange, the nearest being the one the attack is most likely aimed at. A
/// one-frame hit of weapon damage * WeaponDamageMultiplier around that enemy, in the element of the melee skill used. The
/// strike isn't a skill use itself.
/// </summary>
[UpdateInGroup(typeof(SkillReactionSystemGroup))]
[BurstCompile]
public partial struct FrenzyStrikeSystem : ISystem
{
    Random _random;

    public void OnCreate(ref SystemState state)
    {
        _random = Random.CreateFromIndex((uint)System.Environment.TickCount);
        state.RequireForUpdate<PhysicsWorldSingleton>();
        state.RequireForUpdate<Frenzy>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var physicsWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>();
        var commandBuffer = SystemAPI.GetSingleton<BeginInitializationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
        var filter = new CollisionFilter { BelongsTo = ~0u, CollidesWith = (uint)CollisionLayerHelper.AnyEnemy, GroupIndex = 0 };
        var hits = new NativeList<DistanceHit>(Allocator.Temp);
        var enemies = SystemAPI.GetComponentLookup<Enemy>(true);
        var healths = SystemAPI.GetComponentLookup<Health>(true);
        var dying = SystemAPI.GetComponentLookup<RigidDeath>(true);

        foreach (var (frenzy, used, stats, transform, entity) in
                 SystemAPI.Query<RefRO<Frenzy>, RefRO<MeleeAttackUsed>, RefRO<CalculatedStats>, RefRO<LocalTransform>>()
                     .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState).WithEntityAccess())
        {
            if (!frenzy.ValueRO.Active || !SystemAPI.IsComponentEnabled<MeleeAttackUsed>(entity)) continue;
            var f = frenzy.ValueRO;

            // The two nearest living enemies; the strike goes to the second
            hits.Clear();
            var position = transform.ValueRO.Position;
            physicsWorld.OverlapSphere(position, f.StrikeRange, ref hits, filter);
            Entity nearest = Entity.Null, second = Entity.Null;
            float nearestDistance = float.MaxValue, secondDistance = float.MaxValue;
            foreach (var hit in hits)
            {
                var enemy = hit.Entity;
                if (enemy == nearest || enemy == second || !enemies.HasComponent(enemy) || !healths.HasComponent(enemy)
                    || healths[enemy].CurrentValue <= 0f || dying.HasComponent(enemy)) continue;
                if (hit.Distance < nearestDistance)
                {
                    second = nearest;
                    secondDistance = nearestDistance;
                    nearest = enemy;
                    nearestDistance = hit.Distance;
                }
                else if (hit.Distance < secondDistance)
                {
                    second = enemy;
                    secondDistance = hit.Distance;
                }
            }
            if (second == Entity.Null) continue;

            var s = stats.ValueRO.Value;
            float weaponDamage = s.WeaponDamage + _random.NextFloat(-s.WeaponDamageRange, s.WeaponDamageRange);
            var strike = commandBuffer.CreateEntity();
            commandBuffer.AddComponent(strike, new LocalTransform { Position = SystemAPI.GetComponent<LocalTransform>(second).Position, Rotation = quaternion.identity, Scale = 1f });
            commandBuffer.AddComponent(strike, new Attack { Origin = DamageLabel.Frenzy, Damage = math.max(0f, weaponDamage * f.WeaponDamageMultiplier), Type = used.ValueRO.Type, Radius = f.StrikeRadius, DamageHierarchy = DamageHierarchy.Main });
            commandBuffer.AddComponent(strike, new Source { Entity = entity });
            commandBuffer.AddComponent(strike, new AttackTarget { TargetType = TargetType.Enemy });
            commandBuffer.AddComponent(strike, new InitializeFromSource());
            commandBuffer.AddComponent(strike, new DestroyWithChildren());   // A single-frame hit
        }
    }
}
