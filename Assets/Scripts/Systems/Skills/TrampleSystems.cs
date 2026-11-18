using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// Trample: while held the unit walks at SpeedMultiplier of its speed (TrampleSlowSystem), and every Stride meters it walks
/// it stomps: a one-frame physical hit in Radius around it of its current health * DamagePerCurrentHealth. Free; the
/// cooldown starts when it's released. Other skills still work meanwhile.
/// </summary>
public struct Trample : IComponentData, IEnableableComponent
{
    public float Cooldown;                  // After it's released
    public float SpeedMultiplier;           // While stomping (0.5 = half speed)
    public float Stride;                    // Meters walked between stomps
    public float DamagePerCurrentHealth;    // Damage per stomp as a share of the unit's current health (0.05 = 5%)
    public float Radius;
    public float HitForce;

    public float Elapsed;                   // Since it was released, up to Cooldown
    public bool Stomping;
    public float Walked;                    // Meters toward the next stomp
    public float3 LastPosition;
}

/// <summary>
/// Trample while held: counts the meters the unit walks (flat, at most one Stride a frame so a teleport isn't a run of
/// stomps) and stomps each Stride. A stomp's damage is taken from the unit's current health then. Releasing it ends the
/// stomping and starts the cooldown.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(SkillSystemGroup))]
public partial struct TrampleSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<Trample>();
        state.RequireForUpdate<BeginInitializationEntityCommandBufferSystem.Singleton>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var commandBuffer = SystemAPI.GetSingleton<BeginInitializationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
        float deltaTime = SystemAPI.Time.DeltaTime;

        foreach (var (trample, localTransform, health, stats, entity) in
                 SystemAPI.Query<RefRW<Trample>, RefRO<LocalTransform>, RefRO<Health>, RefRO<CalculatedStats>>()
                          .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState).WithEntityAccess())
        {
            ref var t = ref trample.ValueRW;
            bool held = SystemAPI.IsComponentEnabled<Trample>(entity);
            float cooldown = SkillUtil.Cooldown(t.Cooldown, stats.ValueRO.Value);
            var position = localTransform.ValueRO.Position;

            if (!t.Stomping)
            {
                t.Elapsed = math.min(t.Elapsed + deltaTime, cooldown);
                if (!held || t.Elapsed < cooldown) continue;
                t.Stomping = true;
                t.Walked = 0f;
                t.LastPosition = position;
                if (SystemAPI.HasBuffer<Skill>(entity)) SkillUtil.MarkUsed(SystemAPI.GetBuffer<Skill>(entity), SkillType.Trample);
                continue;
            }
            if (!held)
            {
                // Released (or blocked, e.g. frozen): the cooldown starts
                t.Stomping = false;
                t.Elapsed = 0f;
                continue;
            }

            var step = (position - t.LastPosition).xz;
            t.LastPosition = position;
            t.Walked += math.min(math.length(step), t.Stride);
            if (t.Walked < t.Stride) continue;
            t.Walked -= t.Stride;

            float damage = SkillUtil.CooldownDamage(health.ValueRO.CurrentValue * t.DamagePerCurrentHealth, t.Cooldown, stats.ValueRO);
            var stomp = commandBuffer.CreateEntity();
            commandBuffer.AddComponent(stomp, new LocalTransform { Position = position, Rotation = quaternion.identity, Scale = 1f });
            commandBuffer.AddComponent(stomp, new Attack { Origin = DamageLabel.Trample, Damage = math.max(0f, damage), Type = DamageType.Physical, DamageHierarchy = DamageHierarchy.Main, Radius = t.Radius, HitForce = t.HitForce });
            commandBuffer.AddComponent(stomp, new Source { Entity = entity });
            commandBuffer.AddComponent(stomp, new AttackTarget { TargetType = TargetType.Enemy });
            commandBuffer.AddComponent(stomp, new InitializeFromSource());
            commandBuffer.AddComponent(stomp, new DestroyWithChildren());   // A single-frame hit
        }

        // Report cooldown and stomping state (after SkillActivationSystem's defaults)
        foreach (var (trample, skillBuffer, stats) in SystemAPI.Query<RefRW<Trample>, DynamicBuffer<Skill>, RefRO<CalculatedStats>>().WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
        {
            int index = -1;
            if (!SkillUtil.FindSkill(skillBuffer, SkillType.Trample, ref index)) continue;
            ref var skill = ref skillBuffer.ElementAt(index);
            SkillUtil.ApplyCooldownReduction(ref skill, ref trample.ValueRW.Elapsed);
            float cooldown = SkillUtil.Cooldown(trample.ValueRO.Cooldown, stats.ValueRO.Value);
            skill.State.CooldownDuration = cooldown;
            skill.State.CooldownRemaining = trample.ValueRO.Stomping ? 0f : math.max(0f, cooldown - trample.ValueRO.Elapsed);
            skill.State.IsActive = trample.ValueRO.Stomping;
        }
    }
}

/// <summary>While stomping, the unit walks at Trample's SpeedMultiplier, on top of its slows (after EffectMovementSystem sets them).</summary>
[BurstCompile]
[UpdateInGroup(typeof(EffectSystemGroup))]
[UpdateAfter(typeof(EffectMovementSystem))]
public partial struct TrampleSlowSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state) => state.RequireForUpdate<Trample>();

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (trample, modifiers) in SystemAPI.Query<RefRO<Trample>, RefRW<MovementModifiers>>().WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
            if (trample.ValueRO.Stomping) modifiers.ValueRW.SpeedMultiplier *= trample.ValueRO.SpeedMultiplier;
    }
}
