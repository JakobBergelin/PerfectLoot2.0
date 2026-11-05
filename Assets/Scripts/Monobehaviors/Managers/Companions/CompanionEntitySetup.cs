using System.Collections.Generic;
using Enemies;
using EnemySkills;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Physics.Extensions;
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
        // Stands still ones have no combat components, but Pack Instinct raises their hits too
        AddDisabled(entityManager, entity, new PackInstinctBuff());
    }

    /// <summary>Enemies can go after and hit it, walking its owner's FlowField. For one with health, after its other components.</summary>
    public static void AddTargetable(EntityManager entityManager, Entity entity, Entity owner, float threat)
    {
        int fieldSlot = entityManager.GetComponentData<EnemyTargetable>(owner).FieldSlot;
        AddOrSet(entityManager, entity, new EnemyTargetable { FieldSlot = fieldSlot, Threat = threat });
    }

    /// <summary>
    /// Turns a copy of an enemy's body into its owner's Thrall: that enemy's skills, stats and passives, with its movement
    /// speed as a bonus on baseSpeed, as for players.
    /// </summary>
    public static void AddThrallComponents(EntityManager entityManager, Entity entity, Entity owner, EnemyDefinition definition, in RaiseThrall raise, float baseSpeed, double time)
    {
        var stats = definition.Stats;
        stats.MovementSpeed = stats.MovementSpeed / baseSpeed - 1f;
        var movement = raise.Movement;
        movement.AttackDistance = definition.DesiredDistance > 0f ? definition.DesiredDistance : definition.AttackRadius;

        AddGameplayComponents(entityManager, entity, owner, raise.SightRadius, definition.Skills);
        AddMovementComponents(entityManager, entity, stats, movement);
        AddTargetable(entityManager, entity, owner, raise.Threat);
        AddPassives(entityManager, entity, definition.Passives);
        AddOrSet(entityManager, entity, new Thrall { RaisedAt = time });
        AddOrSet(entityManager, entity, new UnitInfo { Source = definition });
        AddOrSet(entityManager, entity, new Steering { Radius = definition.BodyRadius, BodyType = BodyType.Swarm });
        AddOrSet(entityManager, entity, new BodyMass { InverseMass = 1f / math.max(0.01f, definition.Mass) });

        var transform = entityManager.GetComponentData<Unity.Transforms.LocalTransform>(entity);
        transform.Scale = math.max(0.01f, definition.Scale);
        entityManager.SetComponentData(entity, transform);
        if (definition.OverrideColor) EnemyEntitySetup.ApplyTint(entityManager, entity, definition.Tint);

        // On its owner's layer instead of the Enemy one, so it doesn't block them. Its own collider: the baked one is shared.
        var collider = entityManager.GetComponentData<PhysicsCollider>(entity);
        collider.MakeUnique(entity, entityManager);
        collider.Value.Value.SetCollisionFilter(entityManager.GetComponentData<PhysicsCollider>(owner).Value.Value.GetCollisionFilter());
        entityManager.SetComponentData(entity, collider);
    }

    /// <summary>For a companion with a collider that moves: the unit components and Boid. Swarm: only the world blocks it.</summary>
    public static void AddMovementComponents(EntityManager entityManager, Entity entity, StatBlock baseStats, BoidSettings settings)
    {
        AddCombatComponents(entityManager, entity, baseStats);
        AddOrSet(entityManager, entity, new Steering { Radius = BodyRadius, BodyType = BodyType.Swarm });
        AddOrSet(entityManager, entity, new Boid { Settings = settings });
    }
}
