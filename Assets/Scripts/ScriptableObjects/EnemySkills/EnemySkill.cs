using Unity.Entities;
using UnityEngine;

namespace EnemySkills
{
    /// <summary>
    /// A skill an enemy uses by itself, listed on its EnemyDefinition. EnemySkillCastSystem decides when (the fields here);
    /// the skill's own system decides what happens (its EnemySkillFire).
    /// </summary>
    public abstract class EnemySkill : ScriptableObject
    {
        public string Name;

        [Header("When it can be used")]
        [Tooltip("Seconds before it can be used again")]
        public float Cooldown = 5f;
        [Tooltip("The cooldown runs faster or slower with the enemy's attack speed stat")]
        public bool CooldownUsesAttackSpeed;
        [Tooltip("The cooldown only counts down while the target is in range, and starts over when it leaves: a wind-up before the first use")]
        public bool CooldownOnlyInRange;
        [Tooltip("Resource it costs. The enemy needs Resource in its stats.")]
        public float ResourceCost;
        [Tooltip("Not used on a target closer than this. For a skill that waits for the player to get far away.")]
        public float MinRange;
        [Tooltip("Not used on a target farther away than this. For a skill that waits for the player to get close.")]
        public float MaxRange = 3f;
        public bool NeedsLineOfSight = true;
        public EnemySkillConditions Conditions;

        [Header("Aiming")]
        [Tooltip("Degrees per second it turns toward the target before using it")]
        public float TurnSpeed = 360f;
        [Tooltip("Used only once it faces within this many degrees of the target")]
        public float FacingAngle = 60f;

        [Header("Telegraph")]
        public EnemyTelegraph Telegraph;

        public abstract EnemySkillType Type { get; }

        public string DisplayName => string.IsNullOrEmpty(Name) ? name : Name;

        /// <summary>Adds the skill's component with this asset's values.</summary>
        public abstract void Apply(EntityManager entityManager, Entity entity);

        public EnemySkillSlot ToSlot() => new EnemySkillSlot
        {
            Type = Type,
            Cooldown = Cooldown,
            CooldownUsesAttackSpeed = CooldownUsesAttackSpeed,
            CooldownOnlyInRange = CooldownOnlyInRange,
            ResourceCost = ResourceCost,
            MinRange = MinRange,
            MaxRange = MaxRange,
            NeedsLineOfSight = NeedsLineOfSight,
            TurnSpeed = TurnSpeed,
            FacingAngle = FacingAngle,
            Conditions = Conditions,
            Telegraph = Telegraph,
        };
    }

    /// <summary>A skill backed by one ECS component.</summary>
    public abstract class EnemySkill<T> : EnemySkill where T : unmanaged, IComponentData
    {
        protected abstract T Configure();

        public override void Apply(EntityManager entityManager, Entity entity) => UnitEntitySetup.AddOrSet(entityManager, entity, Configure());
    }
}
