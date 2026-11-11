using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

/// <summary>
/// Tesla Coil while held: the unit is Channeling and can't move or turn (CharacterMovementSystem), and a TeslaCoilField
/// stands around it. The drain starts at ResourceCostPerSecond and rises by ResourceCostIncrease for every second of the
/// channel. Letting go, running out of resource or being frozen ends it and starts the cooldown. After WhirlwindSystem:
/// that system takes Channeling off whenever it isn't spinning, so it's added back here every frame.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(SkillSystemGroup))]
[UpdateAfter(typeof(WhirlwindSystem))]
public partial struct TeslaCoilSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state) => state.RequireForUpdate<BeginInitializationEntityCommandBufferSystem.Singleton>();

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float deltaTime = SystemAPI.Time.DeltaTime;
        var commandBuffer = SystemAPI.GetSingleton<BeginInitializationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);

        foreach (var (coil, transform, resource, resourceChanges, stats, entity) in
                 SystemAPI.Query<RefRW<TeslaCoil>, RefRO<LocalTransform>, RefRO<Resource>, DynamicBuffer<ResourceChanger>, RefRO<CalculatedStats>>()
                          .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState).WithEntityAccess())
        {
            ref var c = ref coil.ValueRW;
            var statBlock = stats.ValueRO.Value;
            bool held = SystemAPI.IsComponentEnabled<TeslaCoil>(entity);
            float available = resource.ValueRO.CurrentValue;

            if (!c.Channeling)
            {
                float cooldown = SkillUtil.Cooldown(c.Cooldown, statBlock);
                c.Elapsed = math.min(c.Elapsed + deltaTime, cooldown);
                if (!held || c.Elapsed < cooldown || available < SkillUtil.ChannelCost(c.ResourceCostPerSecond, statBlock) * deltaTime) continue;

                c.Channeling = true;
                c.ChannelTime = 0f;
                Spawn(commandBuffer, entity, c, stats.ValueRO, transform.ValueRO.Position);
                if (SystemAPI.HasBuffer<Skill>(entity)) SkillUtil.MarkUsed(SystemAPI.GetBuffer<Skill>(entity), SkillType.TeslaCoil);
            }

            float drain = SkillUtil.ChannelCost(c.ResourceCostPerSecond + c.ResourceCostIncrease * c.ChannelTime, statBlock) * deltaTime;
            if (!held || available < drain)
            {
                // TeslaCoilFieldSystem removes the field
                c.Channeling = false;
                c.Elapsed = 0f;
                commandBuffer.RemoveComponent<Channeling>(entity);
                continue;
            }

            commandBuffer.AddComponent<Channeling>(entity);
            resourceChanges.Add(new ResourceChanger{Value = -drain});
            c.ChannelTime += deltaTime;
        }

        // Report cooldown and channeling (after SkillActivationSystem's defaults)
        foreach (var (coil, skillBuffer, stats) in SystemAPI.Query<RefRW<TeslaCoil>, DynamicBuffer<Skill>, RefRO<CalculatedStats>>().WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
        {
            int index = -1;
            if (SkillUtil.FindSkill(skillBuffer, SkillType.TeslaCoil, ref index))
            {
                ref var skill = ref skillBuffer.ElementAt(index);
                SkillUtil.ApplyCooldownReduction(ref skill, ref coil.ValueRW.Elapsed);
                float cooldown = SkillUtil.Cooldown(coil.ValueRO.Cooldown, stats.ValueRO.Value);
                skill.State.CooldownDuration = cooldown;
                skill.State.CooldownRemaining = coil.ValueRO.Channeling ? 0f : math.max(0f, cooldown - coil.ValueRO.Elapsed);
                skill.State.IsActive = coil.ValueRO.Channeling;
            }
        }
    }

    /// <summary>The field: a lightning PeriodicAttack that lasts until TeslaCoilFieldSystem removes it.</summary>
    static void Spawn(EntityCommandBuffer commandBuffer, Entity caster, in TeslaCoil coil, in CalculatedStats stats, float3 position)
    {
        var newEntity = commandBuffer.CreateEntity();
        commandBuffer.AddComponent(newEntity, new LocalTransform{Position = position, Rotation = quaternion.identity, Scale = 1f});
        commandBuffer.AddComponent(newEntity, new PeriodicAttack{Origin = DamageLabel.TeslaCoil, DamagePerSecond = SkillUtil.CooldownDamage(coil.DamagePerSecond, coil.Cooldown, stats), Type = DamageType.Lightning, Radius = coil.Radius,
            TimeRemaining = float.MaxValue, Skill = SkillType.TeslaCoil, TickRate = coil.TickRate});
        commandBuffer.AddComponent(newEntity, new Source{Entity = caster});
        commandBuffer.AddComponent(newEntity, new AttackTarget{TargetType = TargetType.Enemy});
        commandBuffer.AddComponent(newEntity, new InitializeFromSource{});
        commandBuffer.AddComponent(newEntity, new TeslaCoilField{});
    }
}

/// <summary>
/// Tesla Coil fields: reflect every projectile aimed at the player inside one, at any height, whether it flew in or was
/// fired from inside: it becomes the caster's (Reflect). The player's own projectiles aren't touched. Removes a field
/// once its caster stops channeling.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(MovementSystemGroup))]
[UpdateAfter(typeof(ProjectileSystem))]
public partial struct TeslaCoilFieldSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<TeslaCoilField>();
        state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var commandBuffer = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);

        // The fields still up: centre and radius (x, y, z) and their caster
        var fields = new NativeList<float3>(Allocator.Temp);
        var casters = new NativeList<Entity>(Allocator.Temp);
        foreach (var (attack, transform, source, entity) in
                 SystemAPI.Query<RefRO<PeriodicAttack>, RefRO<LocalTransform>, RefRO<Source>>().WithAll<TeslaCoilField>().WithNone<DestroyWithChildren>().WithEntityAccess())
        {
            var caster = source.ValueRO.Entity;
            if (!SystemAPI.HasComponent<TeslaCoil>(caster) || !SystemAPI.GetComponent<TeslaCoil>(caster).Channeling)
            {
                commandBuffer.AddComponent<DestroyWithChildren>(entity);
                continue;
            }
            fields.Add(new float3(transform.ValueRO.Position.xz, attack.ValueRO.Radius));
            casters.Add(caster);
        }
        if (fields.IsEmpty) return;

        foreach (var (target, projectileTransform, projectile, projectileAttack, projectileSource, entity) in
                 SystemAPI.Query<RefRW<AttackTarget>, RefRW<LocalTransform>, RefRW<Projectile>, RefRW<Attack>, RefRW<Source>>()
                     .WithNone<DestroyWithChildren>().WithEntityAccess())
        {
            if (target.ValueRO.TargetType != TargetType.Player) continue;
            for (int i = 0; i < fields.Length; i++)
            {
                var offset = projectileTransform.ValueRO.Position.xz - fields[i].xy;
                if (math.lengthsq(offset) > fields[i].z * fields[i].z) continue;
                Reflect(ref state, entity, ref projectile.ValueRW, ref projectileTransform.ValueRW, ref projectileAttack.ValueRW,
                        ref projectileSource.ValueRW, ref target.ValueRW, offset, casters[i]);
                break;
            }
        }
    }

    // The projectile becomes the caster's: it hits enemies now (so the field leaves it alone), crits with the caster's
    // stats and is credited to Tesla Coil, keeping the damage it was fired with. Coming in, it bounces off the field's edge
    // like a mirror; fired from inside, it keeps flying out. A homing one turns back to the unit that fired it. It gets its
    // full range again.
    void Reflect(ref SystemState state, Entity entity, ref Projectile projectile, ref LocalTransform transform, ref Attack attack,
                 ref Source source, ref AttackTarget target, float2 offset, Entity caster)
    {
        var shooter = attack.Origin.Unit != Entity.Null ? attack.Origin.Unit : source.Entity;

        var normal = math.normalizesafe(new float3(offset.x, 0f, offset.y), -projectile.Direction);
        float into = math.dot(projectile.Direction, normal);
        if (into < 0f) projectile.Direction -= 2f * into * normal;
        transform.Rotation = quaternion.LookRotationSafe(projectile.Direction, math.up());

        bool hasRange = SystemAPI.HasComponent<ProjectileRange>(entity);
        var range = hasRange ? SystemAPI.GetComponent<ProjectileRange>(entity) : default;
        ProjectileUtil.TakeOver(ref attack, ref source, ref target, ref range, hasRange, transform.Position, caster, DamageLabel.TeslaCoil);
        if (hasRange) SystemAPI.SetComponent(entity, range);

        if (SystemAPI.HasComponent<HomingProjectile>(entity))
        {
            var homing = SystemAPI.GetComponent<HomingProjectile>(entity);
            homing.Target = shooter;
            SystemAPI.SetComponent(entity, homing);
        }
    }
}
