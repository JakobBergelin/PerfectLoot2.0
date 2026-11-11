using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

/// <summary>
/// Raise Dead: right after damage is dealt, a unit that just died within Radius of a living Raise Dead wearer on its
/// side gets back up at full health and sinks into the ground to rise again (SpawnRise), Invulnerable while it rises,
/// then Risen for RisenDuration: a Risen unit isn't raised again. It runs before experience, loot, kill counts and
/// on-death effects (all OrderLast) and before CheckDeathSystem, so for them it never died; a raised player never
/// dies either (no respawn). Units with Raise Dead themselves aren't raised, so two wearers can't keep each other up.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(DamageSystemGroup))]
[UpdateAfter(typeof(DamagingSystem))]
public partial struct RaiseDeadSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<RaiseDead>();
        state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        // The living wearers: where, how far, which side, how they rise
        var wearers = new NativeList<float4>(Allocator.Temp);       // xyz position, w radius
        var rises = new NativeList<float3>(Allocator.Temp);         // x rise duration, y depth, z Risen duration
        var enemySide = new NativeList<bool>(Allocator.Temp);
        foreach (var (raise, transform, health, entity) in SystemAPI.Query<RefRO<RaiseDead>, RefRO<LocalTransform>, RefRO<Health>>().WithEntityAccess())
        {
            if (health.ValueRO.CurrentValue <= 0f) continue;
            wearers.Add(new float4(transform.ValueRO.Position, raise.ValueRO.Radius));
            rises.Add(new float3(raise.ValueRO.RiseDuration, raise.ValueRO.RiseDepth, raise.ValueRO.RisenDuration));
            enemySide.Add(SystemAPI.HasComponent<Enemy>(entity));
        }
        if (wearers.IsEmpty) return;

        var commandBuffer = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
        var risens = SystemAPI.GetComponentLookup<Risen>();
        var invulnerables = SystemAPI.GetComponentLookup<Invulnerable>();
        state.CompleteDependency();  // The lookups are used here on the main thread; TimedEffectSystem ticks them in jobs

        foreach (var (health, transform, entity) in SystemAPI.Query<RefRW<Health>, RefRO<LocalTransform>>()
                     .WithNone<RaiseDead, DestroyWithChildren, RigidDeath>().WithEntityAccess())
        {
            if (health.ValueRO.CurrentValue > 0f) continue;
            if (!risens.HasComponent(entity) || risens.IsComponentEnabled(entity)) continue;
            bool isEnemy = SystemAPI.HasComponent<Enemy>(entity);
            var position = transform.ValueRO.Position;
            for (int i = 0; i < wearers.Length; i++)
            {
                if (enemySide[i] != isEnemy || math.distancesq(position, wearers[i].xyz) > wearers[i].w * wearers[i].w) continue;
                health.ValueRW.CurrentValue = health.ValueRO.MaxValue;
                commandBuffer.AddComponent(entity, new SpawnRise { TargetY = position.y, Depth = rises[i].y, Duration = rises[i].x });
                risens[entity] = new Risen { TimeRemaining = rises[i].z, Duration = rises[i].z };
                risens.SetComponentEnabled(entity, true);
                // Untouchable while it rises (a longer invulnerability it already has is kept)
                if (invulnerables.HasComponent(entity))
                {
                    bool already = invulnerables.IsComponentEnabled(entity);
                    float remaining = already ? math.max(invulnerables[entity].TimeRemaining, rises[i].x) : rises[i].x;
                    invulnerables[entity] = new Invulnerable { TimeRemaining = remaining, Duration = already ? math.max(invulnerables[entity].Duration, remaining) : remaining };
                    invulnerables.SetComponentEnabled(entity, true);
                }
                break;
            }
        }
    }
}

/// <summary>
/// Frost Aura: every TickRate seconds, units on the other side within Radius of the wearer get ChillPerTick chill
/// stacks (enough of them freeze, as any chill does).
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(SkillSystemGroup))]
public partial struct FrostAuraSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<FrostAura>();
        state.RequireForUpdate<PhysicsWorldSingleton>();
        state.RequireForUpdate<StatusEffectConfig>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float deltaTime = SystemAPI.Time.DeltaTime;
        var physicsWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>();
        var settings = SystemAPI.GetSingleton<StatusEffectConfig>();
        var chilled = SystemAPI.GetComponentLookup<Chilled>();
        var enemies = SystemAPI.GetComponentLookup<Enemy>(true);
        var targetables = SystemAPI.GetComponentLookup<EnemyTargetable>(true);
        state.CompleteDependency();  // The lookups are used here on the main thread; TimedEffectSystem ticks Chilled in a job
        var hits = new NativeList<DistanceHit>(Allocator.Temp);
        var filter = new CollisionFilter { BelongsTo = ~0u, CollidesWith = (uint)CollisionLayerHelper.Unit, GroupIndex = 0 };

        foreach (var (aura, transform, entity) in SystemAPI.Query<RefRW<FrostAura>, RefRO<LocalTransform>>().WithNone<SpawnRise>().WithEntityAccess())
        {
            ref var a = ref aura.ValueRW;
            a.Elapsed += deltaTime;
            if (a.Elapsed < a.TickRate) continue;
            a.Elapsed -= a.TickRate;

            bool wearerIsEnemy = enemies.HasComponent(entity);
            physicsWorld.OverlapSphere(transform.ValueRO.Position, a.Radius, ref hits, filter);
            foreach (var hit in hits)
            {
                bool otherSide = wearerIsEnemy ? targetables.HasComponent(hit.Entity) : enemies.HasComponent(hit.Entity);
                if (!otherSide || !chilled.HasComponent(hit.Entity)) continue;
                EffectApply.AddChill(chilled, hit.Entity, a.ChillPerTick, settings.ChillMaxStacks, settings.ChillDuration);
            }
        }
    }
}
