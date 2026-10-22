using Unity.Entities;
using Unity.Mathematics;
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
        AddBuffer<PeriodicDamageInstance>(entityManager, entity);
        AddBuffer<CalculatedDamageInstance>(entityManager, entity);
        AddOrSet(entityManager, entity, new Knockback());
        AddBuffer<BurnInstance>(entityManager, entity);
        AddBuffer<BleedInstance>(entityManager, entity);
        AddBuffer<ChargedInstance>(entityManager, entity);
        AddDisabled(entityManager, entity, new Chilled());
        AddDisabled(entityManager, entity, new Frozen());
        AddDisabled(entityManager, entity, new Burning());
        AddDisabled(entityManager, entity, new Bleeding());
        AddDisabled(entityManager, entity, new Poisioned());

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
