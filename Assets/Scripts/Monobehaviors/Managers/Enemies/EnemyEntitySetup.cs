using Enemies;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using static UnitEntitySetup;

/// <summary>
/// Adds every gameplay component an enemy needs to a spawned visual-only entity:
/// the shared unit combat components plus its AI and melee attack, all from its EnemyDefinition.
/// </summary>
public static class EnemyEntitySetup
{
    public static void AddGameplayComponents(EntityManager entityManager, Entity entity, EnemyDefinition definition)
    {
        AddOrSet(entityManager, entity, new Enemy());

        // Stats, health, damage buffers, status effects, knockback, smoothing
        AddCombatComponents(entityManager, entity, definition.Stats);

        AddOrSet(entityManager, entity, new NpcBehavior
        {
            TurnSpeed = definition.TurnSpeed,
            TurnAcceleration = definition.TurnAcceleration,
            DetectionRadius = definition.DetectionRadius,
            AttackRadius = definition.AttackRadius,
            DesiredDirection = quaternion.identity,
        });
        AddOrSet(entityManager, entity, new EnemyMeleeAttack
        {
            Damage = definition.MeleeDamage,
            AttackSpeed = definition.SecondsBetweenAttacks,
        });

        if (definition.OverrideColor) ApplyTint(entityManager, entity, definition.Tint);
    }

    // Writes the tint as the base color, and as the color the hit flash returns to
    static void ApplyTint(EntityManager entityManager, Entity entity, UnityEngine.Color tint)
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
