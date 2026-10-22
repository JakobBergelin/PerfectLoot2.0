using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

[assembly: RegisterGenericJobType(typeof(TickTimedEffectJob<BrokenPromisesBuff>))]
[assembly: RegisterGenericJobType(typeof(TickTimedEffectJob<Invulnerable>))]
[assembly: RegisterGenericJobType(typeof(TickTimedEffectJob<Poisoned>))]
[assembly: RegisterGenericJobType(typeof(TickTimedEffectJob<Chilled>))]
[assembly: RegisterGenericJobType(typeof(TickTimedEffectJob<Frozen>))]

/// <summary>Counts a timed effect down on every unit that has it enabled, and disables it when it runs out.</summary>
[BurstCompile]
public struct TickTimedEffectJob<T> : IJobChunk where T : unmanaged, IComponentData, IEnableableComponent, ITimedEffect
{
    public ComponentTypeHandle<T> Handle;
    public float DeltaTime;

    public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
    {
        var effects = chunk.GetNativeArray(ref Handle);
        var enumerator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
        while (enumerator.NextEntityIndex(out int i))
        {
            var effect = effects[i];
            effect.TimeRemaining -= DeltaTime;
            effects[i] = effect;
            if (effect.TimeRemaining <= 0f) chunk.SetComponentEnabled(ref Handle, i, false);
        }
    }
}

/// <summary>Counts down every timed effect (in parallel; only units that have the effect are visited).</summary>
[UpdateInGroup(typeof(EffectSystemGroup), OrderLast = true)]
[BurstCompile]
public partial struct TimedEffectSystem : ISystem
{
    EntityQuery _brokenPromises, _invulnerable, _poisoned, _chilled, _frozen;
    ComponentTypeHandle<BrokenPromisesBuff> _brokenPromisesHandle;
    ComponentTypeHandle<Invulnerable> _invulnerableHandle;
    ComponentTypeHandle<Poisoned> _poisonedHandle;
    ComponentTypeHandle<Chilled> _chilledHandle;
    ComponentTypeHandle<Frozen> _frozenHandle;

    public void OnCreate(ref SystemState state)
    {
        _brokenPromises = SystemAPI.QueryBuilder().WithAllRW<BrokenPromisesBuff>().Build();
        _invulnerable = SystemAPI.QueryBuilder().WithAllRW<Invulnerable>().Build();
        _poisoned = SystemAPI.QueryBuilder().WithAllRW<Poisoned>().Build();
        _chilled = SystemAPI.QueryBuilder().WithAllRW<Chilled>().Build();
        _frozen = SystemAPI.QueryBuilder().WithAllRW<Frozen>().Build();
        _brokenPromisesHandle = state.GetComponentTypeHandle<BrokenPromisesBuff>();
        _invulnerableHandle = state.GetComponentTypeHandle<Invulnerable>();
        _poisonedHandle = state.GetComponentTypeHandle<Poisoned>();
        _chilledHandle = state.GetComponentTypeHandle<Chilled>();
        _frozenHandle = state.GetComponentTypeHandle<Frozen>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float deltaTime = SystemAPI.Time.DeltaTime;
        _brokenPromisesHandle.Update(ref state);
        _invulnerableHandle.Update(ref state);
        _poisonedHandle.Update(ref state);
        _chilledHandle.Update(ref state);
        _frozenHandle.Update(ref state);

        // One line per timed effect
        var dependency = state.Dependency;
        var a = new TickTimedEffectJob<BrokenPromisesBuff> { Handle = _brokenPromisesHandle, DeltaTime = deltaTime }.ScheduleParallel(_brokenPromises, dependency);
        var b = new TickTimedEffectJob<Invulnerable> { Handle = _invulnerableHandle, DeltaTime = deltaTime }.ScheduleParallel(_invulnerable, dependency);
        var c = new TickTimedEffectJob<Poisoned> { Handle = _poisonedHandle, DeltaTime = deltaTime }.ScheduleParallel(_poisoned, dependency);
        var d = new TickTimedEffectJob<Chilled> { Handle = _chilledHandle, DeltaTime = deltaTime }.ScheduleParallel(_chilled, dependency);
        var e = new TickTimedEffectJob<Frozen> { Handle = _frozenHandle, DeltaTime = deltaTime }.ScheduleParallel(_frozen, dependency);
        state.Dependency = JobHandle.CombineDependencies(JobHandle.CombineDependencies(a, b, c), JobHandle.CombineDependencies(d, e));
    }
}

/// <summary>A unit chilled to the maximum stacks freezes: Chilled is disabled and Frozen applied.</summary>
[UpdateInGroup(typeof(EffectSystemGroup))]
[BurstCompile]
public partial struct ChillSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state) => state.RequireForUpdate<StatusEffectConfig>();

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var settings = SystemAPI.GetSingleton<StatusEffectConfig>();
        var frozenLookup = SystemAPI.GetComponentLookup<Frozen>();
        foreach (var (chilled, entity) in SystemAPI.Query<RefRO<Chilled>>().WithEntityAccess())
        {
            if (chilled.ValueRO.Stacks < settings.ChillMaxStacks) continue;
            SystemAPI.SetComponentEnabled<Chilled>(entity, false);
            if (frozenLookup.HasComponent(entity)) EffectApply.Refresh(frozenLookup, entity, settings.FrozenDuration);
        }
    }
}

/// <summary>
/// Damage over time, dealt as sub-damage: Bleeding (a fraction of current health per second, per bleed), Burning and
/// Poisoned (damage per second). Also adds it up for the combat text (EffectDamageTextSystem).
/// </summary>
[UpdateInGroup(typeof(EffectSystemGroup))]
[BurstCompile]
public partial struct EffectDamageSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state) => state.RequireForUpdate<StatusEffectConfig>();

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var settings = SystemAPI.GetSingleton<StatusEffectConfig>();
        float deltaTime = SystemAPI.Time.DeltaTime;

        foreach (var (bleeds, health, damage, text, entity) in SystemAPI.Query<DynamicBuffer<BleedInstance>, RefRO<Health>, DynamicBuffer<DamageInstance>, RefRW<EffectDamageText>>().WithAll<Bleeding>().WithEntityAccess())
        {
            for (int i = bleeds.Length - 1; i >= 0; i--)
            {
                float amount = health.ValueRO.CurrentValue * settings.BleedDamageHealthMultiplier * deltaTime;
                damage.Add(new DamageInstance { Damage = amount, Type = DamageType.Physical, Hierarchy = DamageHierarchy.Sub, Source = bleeds[i].Source });
                text.ValueRW.Bleed += amount;

                ref var bleed = ref bleeds.ElementAt(i);
                bleed.TimeRemaining -= deltaTime;
                if (bleed.TimeRemaining <= 0f) bleeds.RemoveAt(i);
            }
            if (bleeds.IsEmpty) SystemAPI.SetComponentEnabled<Bleeding>(entity, false);
        }

        foreach (var (burns, damage, text, entity) in SystemAPI.Query<DynamicBuffer<BurnInstance>, DynamicBuffer<DamageInstance>, RefRW<EffectDamageText>>().WithAll<Burning>().WithEntityAccess())
        {
            for (int i = burns.Length - 1; i >= 0; i--)
            {
                float amount = burns[i].DamagePerSecond * deltaTime;
                damage.Add(new DamageInstance { Damage = amount, Type = DamageType.Fire, Hierarchy = DamageHierarchy.Sub, Source = burns[i].Source });
                text.ValueRW.Burn += amount;

                ref var burn = ref burns.ElementAt(i);
                burn.TimeRemaining -= deltaTime;
                if (burn.TimeRemaining <= 0f) burns.RemoveAt(i);
            }
            if (burns.IsEmpty) SystemAPI.SetComponentEnabled<Burning>(entity, false);
        }

        // Poisoned's timer is counted down by TimedEffectSystem
        foreach (var (poisoned, damage, text) in SystemAPI.Query<RefRO<Poisoned>, DynamicBuffer<DamageInstance>, RefRW<EffectDamageText>>())
        {
            float amount = poisoned.ValueRO.DamagePerSecond * deltaTime;
            damage.Add(new DamageInstance { Damage = amount, Type = DamageType.Poison, Hierarchy = DamageHierarchy.Sub, Source = poisoned.ValueRO.Source });
            text.ValueRW.Poison += amount;
        }
    }
}

/// <summary>Works out how Chilled (slow per stack) and Frozen (no actions) limit each unit's movement and skills.</summary>
[UpdateInGroup(typeof(EffectSystemGroup))]
[BurstCompile]
public partial struct EffectMovementSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state) => state.RequireForUpdate<StatusEffectConfig>();

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float slowPerStack = SystemAPI.GetSingleton<StatusEffectConfig>().ChillSlowPerStack;
        foreach (var (modifiers, chilled, chilledEnabled, frozenEnabled) in SystemAPI
                     .Query<RefRW<MovementModifiers>, RefRO<Chilled>, EnabledRefRO<Chilled>, EnabledRefRO<Frozen>>()
                     .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
        {
            float speed = chilledEnabled.ValueRO ? 1f - chilled.ValueRO.Stacks * slowPerStack : 1f;
            modifiers.ValueRW.SpeedMultiplier = math.saturate(speed);
            modifiers.ValueRW.PreventsActions = frozenEnabled.ValueRO;
        }
    }
}

/// <summary>Adds the Broken Promises buff's critical chance while it is active.</summary>
[UpdateInGroup(typeof(FlatStatsSystemGroup))]
[BurstCompile]
public partial struct BrokenPromisesBuffStatsSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (buff, stats) in SystemAPI.Query<RefRO<BrokenPromisesBuff>, RefRW<CalculatedStats>>())
            stats.ValueRW.Value.CriticalChance += buff.ValueRO.CriticalChanceBonus;
    }
}

/// <summary>
/// Rebuilds the player's EffectDisplay list from its enabled effect components, for the HUD. One block per effect.
/// </summary>
[UpdateInGroup(typeof(FeedbackSystemGroup))]
[BurstCompile]
public partial struct EffectDisplaySystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state) => state.RequireForUpdate<StatusEffectConfig>();

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float slowPerStack = SystemAPI.GetSingleton<StatusEffectConfig>().ChillSlowPerStack;
        foreach (var (display, entity) in SystemAPI.Query<DynamicBuffer<EffectDisplay>>().WithEntityAccess())
        {
            display.Clear();

            if (IsActive<BrokenPromisesBuff>(ref state, entity))
            {
                var buff = SystemAPI.GetComponent<BrokenPromisesBuff>(entity);
                display.Add(new EffectDisplay { Type = EffectType.BrokenPromises, Stacks = 1, TimeRemaining = buff.TimeRemaining, Duration = buff.Duration, Magnitude = buff.CriticalChanceBonus });
            }
            if (IsActive<Refraction>(ref state, entity))
                display.Add(new EffectDisplay { Type = EffectType.Refraction, Stacks = SystemAPI.GetComponent<Refraction>(entity).Stacks });
            if (IsActive<Invulnerable>(ref state, entity))
            {
                var invulnerable = SystemAPI.GetComponent<Invulnerable>(entity);
                display.Add(new EffectDisplay { Type = EffectType.Invulnerable, Stacks = 1, TimeRemaining = invulnerable.TimeRemaining, Duration = invulnerable.Duration });
            }
            if (IsActive<Bleeding>(ref state, entity) && SystemAPI.HasBuffer<BleedInstance>(entity))
            {
                var bleeds = SystemAPI.GetBuffer<BleedInstance>(entity);
                var entry = new EffectDisplay { Type = EffectType.Bleeding, Stacks = bleeds.Length };
                foreach (var bleed in bleeds)
                    if (bleed.TimeRemaining > entry.TimeRemaining) { entry.TimeRemaining = bleed.TimeRemaining; entry.Duration = bleed.Duration; }
                display.Add(entry);
            }
            if (IsActive<Burning>(ref state, entity) && SystemAPI.HasBuffer<BurnInstance>(entity))
            {
                var burns = SystemAPI.GetBuffer<BurnInstance>(entity);
                var entry = new EffectDisplay { Type = EffectType.Burning, Stacks = burns.Length };
                foreach (var burn in burns)
                {
                    entry.Magnitude += burn.DamagePerSecond;
                    if (burn.TimeRemaining > entry.TimeRemaining) { entry.TimeRemaining = burn.TimeRemaining; entry.Duration = burn.Duration; }
                }
                display.Add(entry);
            }
            if (IsActive<Poisoned>(ref state, entity))
            {
                var poisoned = SystemAPI.GetComponent<Poisoned>(entity);
                display.Add(new EffectDisplay { Type = EffectType.Poisoned, Stacks = 1, TimeRemaining = poisoned.TimeRemaining, Duration = poisoned.Duration, Magnitude = poisoned.DamagePerSecond });
            }
            if (IsActive<Chilled>(ref state, entity))
            {
                var chilled = SystemAPI.GetComponent<Chilled>(entity);
                display.Add(new EffectDisplay { Type = EffectType.Chilled, Stacks = chilled.Stacks, TimeRemaining = chilled.TimeRemaining, Duration = chilled.Duration, Magnitude = math.saturate(chilled.Stacks * slowPerStack) });
            }
            if (IsActive<Frozen>(ref state, entity))
            {
                var frozen = SystemAPI.GetComponent<Frozen>(entity);
                display.Add(new EffectDisplay { Type = EffectType.Frozen, Stacks = 1, TimeRemaining = frozen.TimeRemaining, Duration = frozen.Duration });
            }
        }
    }

    static bool IsActive<T>(ref SystemState state, Entity entity) where T : unmanaged, IComponentData, IEnableableComponent
        => state.EntityManager.HasComponent<T>(entity) && state.EntityManager.IsComponentEnabled<T>(entity);
}
