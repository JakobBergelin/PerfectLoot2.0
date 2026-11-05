using System.Collections.Generic;
using EnemySkills;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;

namespace ActiveSkills
{
    /// <summary>
    /// Summons a Guardian that walks with the caster and draws enemies to itself. It has its own health and armor, and what
    /// it attacks with is set up like an enemy's skills, in Skills. One at a time: the cooldown starts when it dies.
    /// </summary>
    [CreateAssetMenu(fileName = "StoneGuardian", menuName = "Active Skills/Stone Guardian")]
    public class StoneGuardianSkill : ActiveSkill<StoneGuardian>
    {
        [SerializeField] float cooldown = 20f;
        [SerializeField] float resourceCost = 20f;
        [Tooltip("Uniform size; the collider and health bar scale with it")]
        public float Scale = 1.3f;
        [Tooltip("Heavier units are pushed less by hits")]
        public float Mass = 5f;
        public Color Tint = new Color(0.55f, 0.55f, 0.6f);
        [Tooltip("Its own stats: Health, Armor and HealthRegeneration. MovementSpeed is a bonus, as for the player.")]
        public StatBlock Stats = new StatBlock { Health = 50f, HealthRegeneration = 1f };
        [Tooltip("Enemies choosing a target treat it as this many meters closer")]
        public float Threat = 8f;
        [Tooltip("How far the Guardian sees enemies, in meters, all around it")]
        public float SightRadius = 12f;
        public BoidSettings Movement = new BoidSettings
        {
            Position = BoidPosition.Front, Distance = 3f, Spread = 1.5f, Leash = 8f, AttackDistance = 2f, CatchUpMultiplier = 1.5f, TeleportDistance = 20f,
        };
        [Tooltip("What the Guardian attacks with, first ready one first. Their damage is scaled by the caster's stats.")]
        public List<EnemySkill> Skills = new List<EnemySkill>();

        public override SkillType Type => SkillType.StoneGuardian;
        public override float Cooldown => cooldown;
        public override float ResourceCost => resourceCost;

        public override void Describe(SkillDescription description, StatBlock? stats)
        {
            foreach (var skill in Skills)
            {
                if (skill is EnemyMeleeSkill melee)
                    description.Damage.Add(DamageLine(melee.DamageType, melee.DisplayName, stats).Text(Num(melee.Damage)));
            }
            description.Effects.Add(new SkillLine().Text($"Guardian health: {Num(Stats.Health)}"));
            description.Effects.Add(new SkillLine().Text("Enemies prefer attacking the Guardian"));
            description.Effects.Add(new SkillLine().Text("The cooldown starts when it dies"));
        }

        protected override StoneGuardian Configure(StoneGuardian component, bool isNew)
        {
            component.Cooldown = cooldown;
            component.ResourceCost = resourceCost;
            component.SpawnDistance = Movement.Distance;
            component.Scale = Scale;
            if (isNew) component.Elapsed = cooldown;  // Ready as soon as it's equipped
            return component;
        }

        public override void Apply(EntityManager entityManager, Entity entity)
        {
            base.Apply(entityManager, entity);

            var guardian = entityManager.GetComponentData<StoneGuardian>(entity);
            if (!entityManager.Exists(guardian.Prefab)) guardian.Prefab = CreatePrefab(entityManager);
            SetUpPrefab(entityManager, guardian.Prefab, entity);
            entityManager.SetComponentData(entity, guardian);
        }

        public override void Remove(EntityManager entityManager, Entity entity)
        {
            if (entityManager.HasComponent<StoneGuardian>(entity))
            {
                var prefab = entityManager.GetComponentData<StoneGuardian>(entity).Prefab;
                if (entityManager.Exists(prefab)) entityManager.DestroyEntity(prefab);
            }
            base.Remove(entityManager, entity);
        }

        // A copy of the baked body, with its whole group marked Prefab like a baked one, so nothing of it is in the world
        static Entity CreatePrefab(EntityManager entityManager)
        {
            using var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<CompanionPrefabs>());
            var prefab = entityManager.Instantiate(query.GetSingleton<CompanionPrefabs>().Guardian);
            using var group = entityManager.GetBuffer<LinkedEntityGroup>(prefab).Reinterpret<Entity>().ToNativeArray(Allocator.Temp);
            entityManager.AddComponent<Prefab>(group);
            return prefab;
        }

        // One prefab per caster, so a Guardian copied from it already knows its owner. Also refreshes it when the skill is applied again.
        void SetUpPrefab(EntityManager entityManager, Entity prefab, Entity owner)
        {
            UnitEntitySetup.AddOrSet(entityManager, prefab, new StoneGuardianUnit());
            CompanionEntitySetup.AddGameplayComponents(entityManager, prefab, owner, SightRadius, Skills);
            CompanionEntitySetup.AddMovementComponents(entityManager, prefab, Stats, Movement);
            CompanionEntitySetup.AddTargetable(entityManager, prefab, owner, Threat);
            UnitEntitySetup.AddOrSet(entityManager, prefab, new BodyMass { InverseMass = 1f / Mathf.Max(0.01f, Mass) });
            EnemyEntitySetup.ApplyTint(entityManager, prefab, Tint);
        }
    }
}
