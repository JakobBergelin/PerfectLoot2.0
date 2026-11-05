using System.Collections.Generic;
using EnemySkills;
using Unity.Entities;
using static UnitEntitySetup;

/// <summary>Adds what a companion needs to an entity: being on its owner's side and using skills, and for one that moves, its body and Boid.</summary>
public static class CompanionEntitySetup
{
    const float BodyRadius = 0.8f;

    /// <summary>Its skills, what it shoots at and whose side it's on. Enough for one that stands still. Add movement after this.</summary>
    public static void AddGameplayComponents(EntityManager entityManager, Entity entity, Entity owner, float sightRadius, List<EnemySkill> skills)
    {
        AddOrSet(entityManager, entity, new Companion { Owner = owner });
        AddOrSet(entityManager, entity, new CompanionDetection { SightRadius = sightRadius });
        AddOrSet(entityManager, entity, new CalculatedStats());
        // What EnemySkillCastSystem works with
        AddOrSet(entityManager, entity, new EnemyTarget());
        AddOrSet(entityManager, entity, new NpcBehavior());
        EnemyEntitySetup.AddSkillList(entityManager, entity, skills);
    }

    /// <summary>Enemies can go after and hit it, walking its owner's FlowField. For one with health, after its other components.</summary>
    public static void AddTargetable(EntityManager entityManager, Entity entity, Entity owner, float threat)
    {
        int fieldSlot = entityManager.GetComponentData<EnemyTargetable>(owner).FieldSlot;
        AddOrSet(entityManager, entity, new EnemyTargetable { FieldSlot = fieldSlot, Threat = threat });
    }

    /// <summary>For a companion with a collider that moves: the unit components and Boid. Swarm: only the world blocks it.</summary>
    public static void AddMovementComponents(EntityManager entityManager, Entity entity, StatBlock baseStats, BoidSettings settings)
    {
        AddCombatComponents(entityManager, entity, baseStats);
        AddOrSet(entityManager, entity, new Steering { Radius = BodyRadius, BodyType = BodyType.Swarm });
        AddOrSet(entityManager, entity, new Boid { Settings = settings });
    }
}
