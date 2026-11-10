using System.Collections.Generic;
using Enemies;
using EnemySkills;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using Unity.Rendering;
using static UnitEntitySetup;

/// <summary>
/// Adds every gameplay component an enemy needs to a spawned visual-only entity:
/// the shared unit combat components plus its AI, its skills (Swarm: a fixed melee attack) and its passives, all from its EnemyDefinition.
/// </summary>
public static class EnemyEntitySetup
{
    public static void AddGameplayComponents(EntityManager entityManager, Entity entity, EnemyDefinition definition)
    {
        AddOrSet(entityManager, entity, new Enemy());
        AddOrSet(entityManager, entity, new UnitInfo { Source = definition });
        AddOrSet(entityManager, entity, new ExperienceReward { Value = definition.Experience });

        // Stats, health, damage buffers, status effects, knockback, smoothing, rotation lock
        AddCombatComponents(entityManager, entity, definition.Stats);

        // Size (the collider scales with the transform) and weight
        var transform = entityManager.GetComponentData<LocalTransform>(entity);
        transform.Scale = math.max(0.01f, definition.Scale);
        entityManager.SetComponentData(entity, transform);
        AddOrSet(entityManager, entity, new BodyMass { InverseMass = 1f / math.max(0.01f, definition.Mass) });

        // Spacing in crowds
        AddOrSet(entityManager, entity, new Steering { Radius = definition.BodyRadius, BodyType = definition.BodyType });

        // Big enemies the camera zooms out for
        if (definition.CameraFocus) AddOrSet(entityManager, entity, new CameraFocus { Radius = definition.BodyRadius });

        // Without AI (training dummies) it just stands there
        if (definition.HasAI)
        {
            bool ranged = definition.BodyType == BodyType.Physics && definition.DesiredDistance > 0f;
            AddOrSet(entityManager, entity, new NpcBehavior
            {
                TurnSpeed = definition.TurnSpeed,
                TurnAcceleration = definition.TurnAcceleration,
                // Ranged: holds its ground anywhere up to the far end of its distance
                AttackRadius = ranged ? definition.DesiredDistance + definition.DistanceTolerance : definition.AttackRadius,
                DesiredDistance = ranged ? definition.DesiredDistance : 0f,
                DistanceTolerance = definition.DistanceTolerance,
                DesiredDirection = quaternion.identity,
            });
            // Who it chases, and how it finds them (the cheap version for swarms)
            AddOrSet(entityManager, entity, new EnemyTarget());
            if (definition.BodyType == BodyType.Swarm)
                AddOrSet(entityManager, entity, new SwarmDetection { Radius = definition.DetectionRadius, NearMissMargin = definition.NearMissMargin });
            else
                AddOrSet(entityManager, entity, new EnemyDetection
                {
                    SightRadius = definition.DetectionRadius,
                    ProximityRadius = definition.ProximityRadius,
                    AlertRadius = definition.AlertRadius,
                    NearMissMargin = definition.NearMissMargin,
                    ForgetTime = definition.ForgetTime,
                });
            if (definition.BodyType == BodyType.Swarm)
                AddOrSet(entityManager, entity, new EnemyMeleeAttack
                {
                    Damage = definition.MeleeDamage,
                    AttackSpeed = definition.SecondsBetweenAttacks,
                    TurnSpeed = definition.AttackTurnSpeed,
                    FacingAngle = definition.AttackFacingAngle,
                });
            else
                AddSkills(entityManager, entity, definition);
        }

        AddPassives(entityManager, entity, definition.Passives);

        if (definition.OverrideColor) ApplyTint(entityManager, entity, definition.Tint);
    }

    static void AddSkills(EntityManager entityManager, Entity entity, EnemyDefinition definition)
    {
        // Its amount and regeneration come from the stats
        if (definition.Stats.Resource > 0f)
        {
            AddOrSet(entityManager, entity, new Resource());
            AddBuffer<ResourceChanger>(entityManager, entity);
        }

        AddSkillList(entityManager, entity, definition.Skills);
    }

    /// <summary>What EnemySkillCastSystem needs to use these skills on an enemy or a companion.</summary>
    public static void AddSkillList(EntityManager entityManager, Entity entity, List<EnemySkill> skills)
    {
        AddOrSet(entityManager, entity, new EnemySkillCast());
        AddBuffer<EnemySkillFire>(entityManager, entity);
        AddBuffer<EnemySkillSlot>(entityManager, entity);

        foreach (var skill in skills)
            if (skill != null) skill.Apply(entityManager, entity);
        // After the structural changes above
        var slots = entityManager.GetBuffer<EnemySkillSlot>(entity);
        slots.Clear();
        foreach (var skill in skills)
            if (skill != null) slots.Add(skill.ToSlot());
    }

    // Writes the tint as the base color, and as the color the hit flash returns to
    public static void ApplyTint(EntityManager entityManager, Entity entity, UnityEngine.Color tint)
    {
        var linear = tint.linear;
        var color = new float4(linear.r, linear.g, linear.b, linear.a);
        AddOrSet(entityManager, entity, new URPMaterialPropertyBaseColor { Value = color });
        if (entityManager.HasComponent<HitFlash>(entity))
        {
            var flash = entityManager.GetComponentData<HitFlash>(entity);
            flash.BaseColor = color;
            entityManager.SetComponentData(entity, flash);
        }
    }
}
