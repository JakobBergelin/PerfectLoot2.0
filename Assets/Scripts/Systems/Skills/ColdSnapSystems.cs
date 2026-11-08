using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

/// <summary>
/// Cold Snap: when off cooldown, a burst around the caster. Enemies within Radius take Damage as a normal cold attack
/// (damage %, conversion and crits apply) and are chilled at once. The chill is topped up so that one cast leaves about
/// ChillStacks on an enemy: the hit's own chill (DamagingSystem) comes off it, or a cast would freeze by itself. It is
/// worked out before resistances and crits, so a resisted hit leaves a little less and a crit a little more. Conversion
/// to another element keeps the chill.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(SkillSystemGroup))]
public partial struct ColdSnapSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<PhysicsWorldSingleton>();
        state.RequireForUpdate<StatusEffectConfig>();
        state.RequireForUpdate<BeginInitializationEntityCommandBufferSystem.Singleton>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float deltaTime = SystemAPI.Time.DeltaTime;
        var physicsWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>();
        var settings = SystemAPI.GetSingleton<StatusEffectConfig>();
        var commandBuffer = SystemAPI.GetSingleton<BeginInitializationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
        var chilledLookup = SystemAPI.GetComponentLookup<Chilled>();
        state.CompleteDependency();  // The lookup is used here on the main thread; TimedEffectSystem ticks it in a job

        var hits = new NativeList<DistanceHit>(Allocator.Temp);
        var filter = new CollisionFilter
        {
            BelongsTo = ~0u,
            CollidesWith = (uint)CollisionLayerHelper.AnyEnemy,
            GroupIndex = 0,
        };

        foreach (var (coldSnap, stats) in SystemAPI.Query<RefRW<ColdSnap>, RefRO<CalculatedStats>>().WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
            coldSnap.ValueRW.Elapsed = math.min(coldSnap.ValueRO.Elapsed + deltaTime, SkillUtil.Cooldown(coldSnap.ValueRO.Cooldown, stats.ValueRO.Value));

        var enemies = SystemAPI.GetComponentLookup<Enemy>(true);

        // Casts requested by effects (e.g. Cascade): an extra burst, the cooldown untouched
        foreach (var (coldSnap, transform, skillBuffer, requests, stats, entity) in
                 SystemAPI.Query<RefRO<ColdSnap>, RefRO<LocalTransform>, DynamicBuffer<Skill>, DynamicBuffer<SkillCastRequest>, RefRO<CalculatedStats>>()
                          .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState).WithEntityAccess())
        {
            while (SkillUtil.TryTakeRequest(requests, SkillType.ColdSnap, out var request))
            {
                Burst(commandBuffer, physicsWorld, ref chilledLookup, enemies, hits, filter, settings, coldSnap.ValueRO, stats.ValueRO, transform.ValueRO.Position, entity);
                SkillUtil.MarkUsed(skillBuffer, SkillType.ColdSnap, request.AutoCast);
            }
        }

        foreach (var (coldSnap, transform, skillBuffer, stats, entity) in
                 SystemAPI.Query<RefRW<ColdSnap>, RefRO<LocalTransform>, DynamicBuffer<Skill>, RefRO<CalculatedStats>>().WithEntityAccess())
        {
            ref var c = ref coldSnap.ValueRW;
            if (c.Elapsed < SkillUtil.Cooldown(c.Cooldown, stats.ValueRO.Value)) continue;
            c.Elapsed = 0f;
            Burst(commandBuffer, physicsWorld, ref chilledLookup, enemies, hits, filter, settings, c, stats.ValueRO, transform.ValueRO.Position, entity);
            SkillUtil.MarkUsed(skillBuffer, SkillType.ColdSnap);
        }

        // Report cooldown (after SkillActivationSystem's defaults)
        foreach (var (coldSnap, skillBuffer, stats) in SystemAPI.Query<RefRW<ColdSnap>, DynamicBuffer<Skill>, RefRO<CalculatedStats>>().WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
        {
            int index = -1;
            if (SkillUtil.FindSkill(skillBuffer, SkillType.ColdSnap, ref index))
            {
                ref var skill = ref skillBuffer.ElementAt(index);
                SkillUtil.ApplyCooldownReduction(ref skill, ref coldSnap.ValueRW.Elapsed);
                float cooldown = SkillUtil.Cooldown(coldSnap.ValueRO.Cooldown, stats.ValueRO.Value);
                skill.State.CooldownDuration = cooldown;
                skill.State.CooldownRemaining = math.max(0f, cooldown - coldSnap.ValueRO.Elapsed);
            }
        }
    }

    // One burst around the caster: the cold attack, and the chill topped up to about ChillStacks
    static void Burst(EntityCommandBuffer commandBuffer, PhysicsWorldSingleton physicsWorld, ref ComponentLookup<Chilled> chilledLookup,
                      ComponentLookup<Enemy> enemies, NativeList<DistanceHit> hits, CollisionFilter filter, StatusEffectConfig settings,
                      in ColdSnap c, in CalculatedStats stats, float3 position, Entity entity)
    {
        var statBlock = stats.Value;
        float damage = SkillUtil.CooldownDamage(c.Damage, c.Cooldown, stats);
        var newEntity = commandBuffer.CreateEntity();
        commandBuffer.AddComponent(newEntity, new LocalTransform{Position = position, Rotation = quaternion.identity, Scale = 1f});
        commandBuffer.AddComponent(newEntity, new Attack{Origin = DamageLabel.ColdSnap, Damage = damage, Type = DamageType.Cold, Radius = c.Radius, DamageHierarchy = DamageHierarchy.Main});
        commandBuffer.AddComponent(newEntity, new Source{Entity = entity});
        commandBuffer.AddComponent(newEntity, new AttackTarget{TargetType = TargetType.Enemy});
        commandBuffer.AddComponent(newEntity, new InitializeFromSource{});
        commandBuffer.AddComponent(newEntity, new DestroyWithChildren{});

        // What the hit chills by itself, as DamagingSystem works it out; only cold hits chill
        var type = statBlock.ConvertedType(DamageType.Cold);
        int hitChill = type == DamageType.Cold ? math.max(1, (int)math.round(damage * statBlock.DamageMultiplier(type) * settings.ChilledDamageMultiplier)) : 0;
        int chill = c.ChillStacks - hitChill;
        if (chill <= 0) return;

        hits.Clear();
        physicsWorld.OverlapSphere(position, c.Radius * statBlock.AreaMultiplier, ref hits, filter);
        foreach (var hit in hits)
        {
            if (hit.Entity == entity || !enemies.HasComponent(hit.Entity)) continue;
            EffectApply.AddChill(chilledLookup, hit.Entity, chill, settings.ChillMaxStacks, settings.ChillDuration);
        }
    }
}
