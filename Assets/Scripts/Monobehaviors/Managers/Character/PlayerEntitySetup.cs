using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Adds every gameplay component the player needs to a spawned visual-only entity.
/// Mirrors what BaseStats/Health/Resource/Player/InputsData/ActiveSkillSelection/Passives authorings bake.
/// </summary>
public static class PlayerEntitySetup
{
    public static void AddGameplayComponents(EntityManager entityManager, Entity entity, StatBlock baseStats)
    {
        AddOrSet(entityManager, entity, new Player());

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
        AddBuffer<BurnInstance>(entityManager, entity);
        AddBuffer<BleedInstance>(entityManager, entity);
        AddBuffer<ChargedInstance>(entityManager, entity);
        AddDisabled(entityManager, entity, new Chilled());
        AddDisabled(entityManager, entity, new Frozen());
        AddDisabled(entityManager, entity, new Burning());
        AddDisabled(entityManager, entity, new Bleeding());
        AddDisabled(entityManager, entity, new Poisioned());

        // Resource (amount/regen come from the resource type via CharacterManager)
        AddOrSet(entityManager, entity, new Resource());
        AddBuffer<ResourceChanger>(entityManager, entity);

        // Input and skills
        AddOrSet(entityManager, entity, new InputsData());
        AddOrSet(entityManager, entity, new DesiredDirection());
        AddBuffer<Skill>(entityManager, entity);

        // Class, resource type and passives (filled by CharacterManager and PassiveSkillManager)
        AddBuffer<Passive>(entityManager, entity);
        AddOrSet(entityManager, entity, new UnitResource());
        AddOrSet(entityManager, entity, new UnitClass());
        AddOrSet(entityManager, entity, new Level());

        // Fill health and resource once the stats have been calculated
        AddOrSet(entityManager, entity, new FillHealthAndResource());
    }

    static void AddOrSet<T>(EntityManager entityManager, Entity entity, T data) where T : unmanaged, IComponentData
    {
        if (!entityManager.HasComponent<T>(entity))
            entityManager.AddComponentData(entity, data);
        else if (!ComponentType.ReadWrite<T>().IsZeroSized)
            entityManager.SetComponentData(entity, data);
    }

    static void AddDisabled<T>(EntityManager entityManager, Entity entity, T data) where T : unmanaged, IComponentData, IEnableableComponent
    {
        AddOrSet(entityManager, entity, data);
        entityManager.SetComponentEnabled<T>(entity, false);
    }

    static void AddBuffer<T>(EntityManager entityManager, Entity entity) where T : unmanaged, IBufferElementData
    {
        if (!entityManager.HasBuffer<T>(entity))
            entityManager.AddBuffer<T>(entity);
    }
}
