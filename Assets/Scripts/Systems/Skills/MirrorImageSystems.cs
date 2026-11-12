using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Rendering;
using Unity.Transforms;

/// <summary>
/// Mirror Image: summons a dark copy of the caster in front of it (on the same spots Teleport can land on; with none it
/// isn't cast), wearing what the caster wears right now and replacing its old copy. The cooldown starts at the cast.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(SkillSystemGroup))]
public partial struct MirrorImageSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<PhysicsWorldSingleton>();
        state.RequireForUpdate<MovementConfig>();
        state.RequireForUpdate<BeginInitializationEntityCommandBufferSystem.Singleton>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var world = SystemAPI.GetSingleton<PhysicsWorldSingleton>().CollisionWorld;
        var config = SystemAPI.GetSingleton<MovementConfig>();
        var commandBuffer = SystemAPI.GetSingleton<BeginInitializationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
        float minWalkableNormalY = math.cos(math.radians(config.MaxSlope));
        float deltaTime = SystemAPI.Time.DeltaTime;

        foreach (var (mirror, transform, collider, resource, skills, entity) in
                 SystemAPI.Query<RefRW<MirrorImage>, RefRO<LocalTransform>, RefRO<PhysicsCollider>, RefRO<Resource>, DynamicBuffer<Skill>>().WithEntityAccess())
        {
            var calculatedStats = SystemAPI.GetComponent<CalculatedStats>(entity);
            var stats = calculatedStats.Value;
            if (mirror.ValueRO.Elapsed < SkillUtil.Cooldown(mirror.ValueRO.Cooldown, stats)) continue;
            float cost = SkillUtil.Cost(mirror.ValueRO.ResourceCost, stats);
            if (!SkillUtil.CanAfford(resource.ValueRO.CurrentValue, cost, calculatedStats)) continue;

            float distance = mirror.ValueRO.SpawnDistance;
            var inFront = transform.ValueRO.Position + math.forward(transform.ValueRO.Rotation) * distance;
            if (!TeleportSystem.TryFindLanding(world, collider.ValueRO, transform.ValueRO, entity, inFront, distance,
                                               minWalkableNormalY, config.StepHeight, UnitMovement.GroundLayer, out var spot))
                continue;

            foreach (var (oldUnit, companion) in SystemAPI.Query<RefRW<MirrorImageUnit>, RefRO<Companion>>())
            {
                if (companion.ValueRO.Owner == entity) oldUnit.ValueRW.TimeRemaining = 0f;
            }

            var unit = commandBuffer.Instantiate(mirror.ValueRO.Prefab);
            commandBuffer.SetComponent(unit, transform.ValueRO.WithPosition(spot));
            commandBuffer.SetComponent(unit, new MirrorImageUnit{TimeRemaining = mirror.ValueRO.Duration, Tint = mirror.ValueRO.Tint});
            if (SystemAPI.HasBuffer<EquippedVisual>(entity))
            {
                commandBuffer.SetBuffer<EquippedVisual>(unit).AddRange(SystemAPI.GetBuffer<EquippedVisual>(entity).AsNativeArray());
                commandBuffer.AddComponent<EquipmentVisualsChanged>(unit);
            }

            mirror.ValueRW.Elapsed = 0f;
            SystemAPI.GetBuffer<ResourceChanger>(entity).Add(new ResourceChanger{Value = -cost});
            SkillUtil.MarkUsed(skills, SkillType.MirrorImage);
        }

        // Count the cooldown and report it (after SkillActivationSystem's defaults)
        foreach (var (mirror, skillBuffer, stats) in SystemAPI.Query<RefRW<MirrorImage>, DynamicBuffer<Skill>, RefRO<CalculatedStats>>().WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
        {
            float cooldown = SkillUtil.Cooldown(mirror.ValueRO.Cooldown, stats.ValueRO.Value);
            mirror.ValueRW.Elapsed = math.min(mirror.ValueRO.Elapsed + deltaTime, cooldown);

            int index = -1;
            if (SkillUtil.FindSkill(skillBuffer, SkillType.MirrorImage, ref index))
            {
                ref var skill = ref skillBuffer.ElementAt(index);
                SkillUtil.ApplyCooldownReduction(ref skill, ref mirror.ValueRW.Elapsed);
                skill.State.CooldownDuration = cooldown;
                skill.State.CooldownRemaining = math.max(0f, cooldown - mirror.ValueRO.Elapsed);
            }
        }
    }
}

/// <summary>
/// Makes each copy do what its owner does: it gets the owner's skill keys and cursor, but not its walking (BoidSystem moves
/// it), and the owner's stats, speed and resource. Its skills then work as on a player, from where it stands; its own
/// spending is overwritten here, so they're free and stop when the owner can't pay. It keeps walking while it attacks,
/// and stands still while channeling. When it runs out, or its owner dies or unequips the skill, it lets go of the keys
/// for a frame, so Whirlwind and Tesla Coil clean up, and then goes away.
/// </summary>
[UpdateInGroup(typeof(InputSystemGroup))]
[UpdateAfter(typeof(InputsDataSystem))]
[BurstCompile]
[WorldSystemFilter(GameWorlds.Server)]
public partial struct MirrorImageInputSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<MirrorImageUnit>();
        state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var commandBuffer = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
        float deltaTime = SystemAPI.Time.DeltaTime;

        foreach (var (unitRef, companion, inputs, behavior, skills, entity) in
                 SystemAPI.Query<RefRW<MirrorImageUnit>, RefRO<Companion>, RefRW<InputsData>, RefRW<NpcBehavior>, DynamicBuffer<Skill>>().WithNone<DestroyWithChildren>().WithEntityAccess())
        {
            ref var unit = ref unitRef.ValueRW;
            if (unit.Ending)
            {
                commandBuffer.AddComponent<DestroyWithChildren>(entity);
                continue;
            }

            unit.TimeRemaining -= deltaTime;
            var owner = companion.ValueRO.Owner;
            bool ownerAlive = SystemAPI.HasComponent<MirrorImage>(owner) && SystemAPI.HasComponent<Health>(owner) && SystemAPI.GetComponent<Health>(owner).CurrentValue > 0f;
            if (!ownerAlive || unit.TimeRemaining <= 0f)
            {
                unit.Ending = true;
                inputs.ValueRW = default;
                continue;
            }

            var ownerInputs = SystemAPI.GetComponent<InputsData>(owner);
            ownerInputs.Move = float2.zero;
            inputs.ValueRW = ownerInputs;
            SystemAPI.SetComponent(entity, SystemAPI.GetComponent<CalculatedStats>(owner));
            SystemAPI.SetComponent(entity, SystemAPI.GetComponent<MovementSpeed>(owner));
            SystemAPI.SetComponent(entity, SystemAPI.GetComponent<Resource>(owner));

            bool aiming = false;
            foreach (var skill in skills)
                aiming |= ownerInputs.SkillHeld[skill.SlotIndex];
            behavior.ValueRW.IsAiming = aiming;
            behavior.ValueRW.IsCasting = SystemAPI.HasComponent<Channeling>(entity);
        }
    }
}

/// <summary>
/// A copy's attacks count as its owner's, as companions' do: on-hit effects and legendary powers apply. Before their stats
/// are applied. Captain's Shield and Tesla Coil's field stay the copy's: they track their caster.
/// </summary>
[UpdateBefore(typeof(InitializeFromSourceSystem))]
[BurstCompile]
public partial struct MirrorImageSourceSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state) => state.RequireForUpdate<MirrorImageUnit>();

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var units = SystemAPI.GetComponentLookup<MirrorImageUnit>(true);
        var companions = SystemAPI.GetComponentLookup<Companion>(true);
        var attacks = SystemAPI.GetComponentLookup<Attack>();
        var periodicAttacks = SystemAPI.GetComponentLookup<PeriodicAttack>();
        state.CompleteDependency();  // The lookups are used here on the main thread

        foreach (var (source, entity) in SystemAPI.Query<RefRW<Source>>().WithPresent<InitializeFromSource>().WithNone<ShieldProjectile, TeslaCoilField>().WithEntityAccess())
        {
            var caster = source.ValueRO.Entity;
            if (!units.HasComponent(caster)) continue;

            source.ValueRW.Entity = companions[caster].Owner;
            if (attacks.HasComponent(entity)) attacks.GetRefRW(entity).ValueRW.Origin.Unit = caster;
            if (periodicAttacks.HasComponent(entity)) periodicAttacks.GetRefRW(entity).ValueRW.Origin.Unit = caster;
        }
    }
}

/// <summary>Darkens a new copy and its equipment, once EquipmentVisualSystem has made the parts. Before it's first drawn.</summary>
[UpdateInGroup(typeof(FeedbackSystemGroup))]
[UpdateAfter(typeof(EquipmentVisualSystem))]
public partial class MirrorImageTintSystem : SystemBase
{
    protected override void OnCreate() => RequireForUpdate<MirrorImageUnit>();

    protected override void OnUpdate()
    {
        var untinted = new NativeList<Entity>(Allocator.Temp);
        foreach (var (unit, entity) in SystemAPI.Query<RefRO<MirrorImageUnit>>().WithEntityAccess())
        {
            if (!unit.ValueRO.Tinted) untinted.Add(entity);
        }

        foreach (var entity in untinted)
        {
            var unit = EntityManager.GetComponentData<MirrorImageUnit>(entity);
            Tint(entity, unit.Tint);
            if (EntityManager.HasBuffer<EquipmentPartInstance>(entity))
            {
                var parts = EntityManager.GetBuffer<EquipmentPartInstance>(entity).ToNativeArray(Allocator.Temp);
                foreach (var part in parts) Tint(part.Value, unit.Tint);
            }

            unit.Tinted = true;
            EntityManager.SetComponentData(entity, unit);
        }
    }

    // Everything drawn of root
    void Tint(Entity root, UnityEngine.Color tint)
    {
        var drawn = new NativeList<Entity>(Allocator.Temp);
        drawn.Add(root);
        if (EntityManager.HasBuffer<LinkedEntityGroup>(root))
            drawn.AddRange(EntityManager.GetBuffer<LinkedEntityGroup>(root).Reinterpret<Entity>().AsNativeArray());

        foreach (var entity in drawn)
        {
            if (EntityManager.HasComponent<MaterialMeshInfo>(entity)) EnemyEntitySetup.ApplyTint(EntityManager, entity, tint);
        }
    }
}
