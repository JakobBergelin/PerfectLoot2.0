using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Physics.GraphicsIntegration;

/// <summary>
/// Adds the combat components every unit (player or enemy) needs to a spawned visual-only entity:
/// stats, health, damage buffers, status effects and knockback. PlayerEntitySetup and EnemyEntitySetup add the rest.
/// </summary>
public static class UnitEntitySetup
{
    public static void AddCombatComponents(EntityManager entityManager, Entity entity, StatBlock baseStats)
    {
        // Stats. CalculatedStats starts at the base values so the first frame before stats are calculated is sane.
        AddOrSet(entityManager, entity, new BaseStats { Value = baseStats });
        AddOrSet(entityManager, entity, new EquipmentStats());
        AddOrSet(entityManager, entity, new CalculatedStats { Value = baseStats });
        AddOrSet(entityManager, entity, new MovementSpeed { Value = baseStats.MovementSpeed });
        AddOrSet(entityManager, entity, new Armor { Value = baseStats.Armor });

        // Health. Starts at base health so CheckDeathSystem doesn't kill it before the first stat calculation;
        // FillHealthAndResource then fills it to the calculated max.
        float health = math.max(1f, baseStats.Health);
        AddOrSet(entityManager, entity, new Health { MaxValue = health, CurrentValue = health });
        AddOrSet(entityManager, entity, new HealthRegeneration { Value = baseStats.HealthRegeneration });
        AddBuffer<DamageInstance>(entityManager, entity);
        AddBuffer<HealingInstance>(entityManager, entity);
        AddBuffer<PeriodicHealingInstance>(entityManager, entity);
        AddBuffer<PeriodicDamageInstance>(entityManager, entity);
        AddBuffer<CalculatedDamageInstance>(entityManager, entity);
        AddOrSet(entityManager, entity, new Knockback());
        AddBuffer<ChargedInstance>(entityManager, entity);

        // Buffs and debuffs: every effect component is present but disabled until applied
        AddDisabled(entityManager, entity, new BrokenPromisesBuff());
        AddDisabled(entityManager, entity, new Refraction());
        AddDisabled(entityManager, entity, new Invulnerable());
        AddDisabled(entityManager, entity, new Bleeding());
        AddBuffer<BleedInstance>(entityManager, entity);
        AddDisabled(entityManager, entity, new Burning());
        AddBuffer<BurnInstance>(entityManager, entity);
        AddDisabled(entityManager, entity, new Poisoned());
        AddDisabled(entityManager, entity, new Chilled());
        AddDisabled(entityManager, entity, new Frozen());
        // One-frame event for mobility skills (Dash, ...), read by Refractionist and similar
        AddDisabled(entityManager, entity, new MobilitySkillUsed());
        AddDisabled(entityManager, entity, new MeleeAttackUsed());
        if (!entityManager.HasBuffer<SkillCastRequest>(entity)) entityManager.AddBuffer<SkillCastRequest>(entity);
        AddOrSet(entityManager, entity, new MovementModifiers { SpeedMultiplier = 1f });
        AddOrSet(entityManager, entity, new EffectDamageText());

        // Units never tip over. Rigidbody constraints don't bake into Unity Physics, so the rotation is locked here.
        if (entityManager.HasComponent<PhysicsMass>(entity))
        {
            var mass = entityManager.GetComponentData<PhysicsMass>(entity);
            mass.InverseInertia = float3.zero;
            entityManager.SetComponentData(entity, mass);
        }

        // Smooth the rendered motion between fixed physics steps. Extrapolation only (no PhysicsGraphicalInterpolationBuffer):
        // interpolation would render an older rotation than LocalTransform, which the health/resource bars counter-rotate against.
        AddOrSet(entityManager, entity, new PhysicsGraphicalSmoothing { ApplySmoothing = 1 });

        // Fill health (and resource) once the stats have been calculated
        AddOrSet(entityManager, entity, new FillHealthAndResource());
    }

    public static void AddOrSet<T>(EntityManager entityManager, Entity entity, T data) where T : unmanaged, IComponentData
    {
        if (!entityManager.HasComponent<T>(entity))
            entityManager.AddComponentData(entity, data);
        else if (!ComponentType.ReadWrite<T>().IsZeroSized)
            entityManager.SetComponentData(entity, data);
    }

    public static void AddDisabled<T>(EntityManager entityManager, Entity entity, T data) where T : unmanaged, IComponentData, IEnableableComponent
    {
        AddOrSet(entityManager, entity, data);
        entityManager.SetComponentEnabled<T>(entity, false);
    }

    public static void AddBuffer<T>(EntityManager entityManager, Entity entity) where T : unmanaged, IBufferElementData
    {
        if (!entityManager.HasBuffer<T>(entity))
            entityManager.AddBuffer<T>(entity);
    }
}
