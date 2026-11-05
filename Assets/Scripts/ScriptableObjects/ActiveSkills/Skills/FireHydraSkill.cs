using System.Collections.Generic;
using EnemySkills;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;

namespace ActiveSkills
{
    /// <summary>Places a Hydra that can't move or be attacked. What it shoots is set up like an enemy's skills, in Skills.</summary>
    [CreateAssetMenu(fileName = "FireHydra", menuName = "Active Skills/Fire Hydra")]
    public class FireHydraSkill : ActiveSkill<FireHydra>
    {
        [SerializeField] float cooldown = 1f;
        [SerializeField] float resourceCost = 10f;
        [Tooltip("Furthest it can be placed from the caster, in meters")]
        public float Range = 12f;
        [Tooltip("Seconds the Hydra stays")]
        public float Duration = 15f;
        [Tooltip("How far the Hydra sees enemies, in meters, all around it")]
        public float SightRadius = 15f;
        [Tooltip("What the Hydra shoots, first ready one first. Their damage is scaled by the caster's stats.")]
        public List<EnemySkill> Skills = new List<EnemySkill>();

        public override SkillType Type => SkillType.FireHydra;
        public override float Cooldown => cooldown;
        public override float ResourceCost => resourceCost;

        public override void Describe(SkillDescription description, StatBlock? stats)
        {
            foreach (var skill in Skills)
            {
                if (skill is EnemyProjectileSkill projectile)
                    description.Damage.Add(DamageLine(projectile.DamageType, projectile.DisplayName, stats).Text(Num(projectile.Damage)));
            }
            description.Effects.Add(ValueLine(GameIcons.Stat(StatIcon.Distance), "Range", Range, null, " m"));
            description.Effects.Add(ValueLine(GameIcons.Stat(StatIcon.Duration), "Hydra for", Duration, null, " s"));
            description.Effects.Add(new SkillLine().Text("Casting it again replaces the Hydra"));
        }

        protected override FireHydra Configure(FireHydra component, bool isNew)
        {
            component.Cooldown = cooldown;
            component.ResourceCost = resourceCost;
            component.Range = Range;
            component.Duration = Duration;
            if (isNew) component.Elapsed = cooldown;  // Ready as soon as it's equipped
            return component;
        }

        public override void Apply(EntityManager entityManager, Entity entity)
        {
            base.Apply(entityManager, entity);

            var hydra = entityManager.GetComponentData<FireHydra>(entity);
            if (!entityManager.Exists(hydra.Prefab)) hydra.Prefab = entityManager.CreateEntity();
            SetUpPrefab(entityManager, hydra.Prefab, entity);
            entityManager.SetComponentData(entity, hydra);
        }

        public override void Remove(EntityManager entityManager, Entity entity)
        {
            if (entityManager.HasComponent<FireHydra>(entity))
            {
                var prefab = entityManager.GetComponentData<FireHydra>(entity).Prefab;
                if (entityManager.Exists(prefab)) entityManager.DestroyEntity(prefab);
            }
            base.Remove(entityManager, entity);
        }

        // One prefab per caster, so a Hydra copied from it already knows its owner. Also refreshes it when the skill is applied again.
        void SetUpPrefab(EntityManager entityManager, Entity prefab, Entity owner)
        {
            UnitEntitySetup.AddOrSet(entityManager, prefab, LocalTransform.Identity);
            UnitEntitySetup.AddOrSet(entityManager, prefab, new FireHydraUnit());
            CompanionEntitySetup.AddGameplayComponents(entityManager, prefab, owner, SightRadius, Skills);
            entityManager.AddComponent<Prefab>(prefab);
        }
    }
}
