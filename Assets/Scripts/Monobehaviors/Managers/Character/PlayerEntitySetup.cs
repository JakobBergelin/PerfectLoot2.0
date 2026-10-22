using Unity.Entities;
using static UnitEntitySetup;

/// <summary>
/// Adds every gameplay component the player needs to a spawned visual-only entity:
/// the shared unit combat components plus input, skills, resource, class and passives.
/// </summary>
public static class PlayerEntitySetup
{
    public static void AddGameplayComponents(EntityManager entityManager, Entity entity, StatBlock baseStats)
    {
        AddOrSet(entityManager, entity, new Player());
        // In the spatial grid, so enemies keep a little distance (capsule radius 0.5)
        AddOrSet(entityManager, entity, new Steering { Radius = 0.8f, BodyType = BodyType.Physics });
        // Its active buffs and debuffs, for the HUD (EffectDisplaySystem)
        AddBuffer<EffectDisplay>(entityManager, entity);

        // Stats, health, damage buffers, status effects, knockback, smoothing
        AddCombatComponents(entityManager, entity, baseStats);

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
    }
}
