using System;
using System.Collections.Generic;
using EnemySkills;
using Passives;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;

namespace ActiveSkills
{
    /// <summary>
    /// Summons a companion that walks with the caster and that enemies can attack. It has its own health and armor, its
    /// attacks are set up like an enemy's skills and its passives work as on a player. One at a time: the cooldown starts
    /// when it dies. CompanionSummonSystem does the summoning; a skill only needs a class giving its SkillType.
    /// </summary>
    public abstract class CompanionSkill : ActiveSkill
    {
        [SerializeField] float cooldown = 20f;
        [SerializeField] float resourceCost = 20f;
        [Tooltip("Visual-only prefab, listed in CompanionPrefabsAuthoring")]
        public GameObject Body;
        [Tooltip("Uniform size; the collider and health bar scale with it")]
        public float Scale = 1.3f;
        [Tooltip("Heavier units are pushed less by hits")]
        public float Mass = 5f;
        public Color Tint = new Color(0.55f, 0.55f, 0.6f);
        [Tooltip("Its own stats: Health, Armor and HealthRegeneration. MovementSpeed is a bonus, as for the player.")]
        public StatBlock Stats = new StatBlock { Health = 50f, HealthRegeneration = 1f };
        [Tooltip("Enemies choosing a target treat it as this many meters closer")]
        public float Threat = 8f;
        [Tooltip("How far it sees enemies, in meters, all around it")]
        public float SightRadius = 12f;
        public BoidSettings Movement = new BoidSettings
        {
            Position = BoidPosition.Front, Distance = 3f, Spread = 1.5f, Leash = 8f, AttackDistance = 2f, CatchUpMultiplier = 1.5f, TeleportDistance = 20f,
        };
        [Tooltip("What it attacks with, first ready one first. Their damage is scaled by the caster's stats.")]
        public List<EnemySkill> Skills = new List<EnemySkill>();
        [Tooltip("Its passives, the same assets players use")]
        public List<PassiveSkill> Passives = new List<PassiveSkill>();

        public override float Cooldown => cooldown;
        public override float ResourceCost => resourceCost;

        public override void Describe(SkillDescription description, StatBlock? stats)
        {
            foreach (var skill in Skills)
            {
                if (skill is EnemyMeleeSkill melee)
                    description.Damage.Add(DamageLine(melee.DamageType, melee.DisplayName, stats).Text(Num(melee.Damage)));
            }
            description.Effects.Add(new SkillLine().Text($"Health: {Num(Stats.Health)}"));
            description.Effects.Add(new SkillLine().Text("The cooldown starts when it dies"));
        }

        public override void Apply(EntityManager entityManager, Entity entity)
        {
            UnitEntitySetup.AddBuffer<CompanionSummon>(entityManager, entity);
            var summons = entityManager.GetBuffer<CompanionSummon>(entity);
            int index = CompanionSummon.Find(summons, Type);
            // Ready as soon as it's equipped
            var summon = index >= 0 ? summons[index] : new CompanionSummon { Type = Type, Elapsed = cooldown };

            if (!entityManager.Exists(summon.Prefab)) summon.Prefab = CreatePrefab(entityManager);
            SetUpPrefab(entityManager, summon.Prefab, entity);
            summon.Cooldown = cooldown;
            summon.ResourceCost = resourceCost;
            summon.SpawnDistance = Mathf.Max(Movement.Distance, Movement.Spread);
            summon.Scale = Scale;

            // After the structural changes above
            summons = entityManager.GetBuffer<CompanionSummon>(entity);
            if (index >= 0) summons[index] = summon;
            else summons.Add(summon);
        }

        public override void Remove(EntityManager entityManager, Entity entity)
        {
            if (!entityManager.HasBuffer<CompanionSummon>(entity)) return;
            var summons = entityManager.GetBuffer<CompanionSummon>(entity);
            int index = CompanionSummon.Find(summons, Type);
            if (index < 0) return;

            var prefab = summons[index].Prefab;
            summons.RemoveAt(index);
            if (entityManager.Exists(prefab)) entityManager.DestroyEntity(prefab);
        }

        // A copy of the baked body, with its whole group marked Prefab like a baked one, so nothing of it is in the world
        Entity CreatePrefab(EntityManager entityManager)
        {
            var prefab = entityManager.Instantiate(FindBody(entityManager));
            using var group = entityManager.GetBuffer<LinkedEntityGroup>(prefab).Reinterpret<Entity>().ToNativeArray(Allocator.Temp);
            entityManager.AddComponent<Prefab>(group);
            return prefab;
        }

        Entity FindBody(EntityManager entityManager)
        {
            var key = new FixedString64Bytes(Body.name);
            using var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<CompanionPrefabElement>());
            foreach (var element in entityManager.GetBuffer<CompanionPrefabElement>(query.GetSingletonEntity(), isReadOnly: true))
                if (element.Key == key) return element.Prefab;
            throw new InvalidOperationException($"{name}: {Body.name} isn't listed in CompanionPrefabsAuthoring.");
        }

        // One prefab per caster, so a companion copied from it already knows its owner. Also refreshes it when the skill is applied again.
        void SetUpPrefab(EntityManager entityManager, Entity prefab, Entity owner)
        {
            var transform = entityManager.GetComponentData<LocalTransform>(prefab);
            transform.Scale = Scale;
            entityManager.SetComponentData(prefab, transform);

            UnitEntitySetup.AddOrSet(entityManager, prefab, new SummonedCompanion { Type = Type });
            CompanionEntitySetup.AddGameplayComponents(entityManager, prefab, owner, SightRadius, Skills);
            CompanionEntitySetup.AddMovementComponents(entityManager, prefab, Stats, Movement);
            CompanionEntitySetup.AddTargetable(entityManager, prefab, owner, Threat);
            CompanionEntitySetup.AddPassives(entityManager, prefab, Passives);
            UnitEntitySetup.AddOrSet(entityManager, prefab, new BodyMass { InverseMass = 1f / Mathf.Max(0.01f, Mass) });
            EnemyEntitySetup.ApplyTint(entityManager, prefab, Tint);
        }
    }
}
