using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace ActiveSkills
{
    /// <summary>
    /// A dark copy of the caster that moves like a companion and uses the caster's attacks where it stands. It can't be
    /// targeted and has no health. Skills tagged NotCopied aren't attacks, so it doesn't get them.
    /// </summary>
    [CreateAssetMenu(fileName = "MirrorImage", menuName = "Active Skills/Mirror Image")]
    public class MirrorImageSkill : ActiveSkill<MirrorImage>
    {
        const SkillTags NotCopied = SkillTags.Mobility | SkillTags.Companion | SkillTags.Buff | SkillTags.Invulnerable;

        [SerializeField] float cooldown = 80f;
        [SerializeField] float resourceCost = 30f;
        [Tooltip("Seconds the copy stays")]
        public float Duration = 10f;
        [Tooltip("Multiplies the color of the copy and its equipment")]
        public Color Tint = new Color(0.12f, 0.1f, 0.16f);
        [Tooltip("How far it sees enemies, in meters, all around it")]
        public float SightRadius = 12f;
        public BoidSettings Movement = new BoidSettings
        {
            Position = BoidPosition.Around, Distance = 3f, Spread = 2f, Leash = 8f, AttackDistance = 2f, CatchUpMultiplier = 1.5f, TeleportDistance = 20f,
        };

        public override SkillType Type => SkillType.MirrorImage;
        public override float Cooldown => cooldown;
        public override float ResourceCost => resourceCost;

        public override void Describe(SkillDescription description, StatBlock? stats)
        {
            description.Effects.Add(ValueLine(GameIcons.Stat(StatIcon.Duration), "Copy for", Duration, null, " s"));
            description.Effects.Add(new SkillLine().Text("Casting it again replaces the copy"));
        }

        protected override MirrorImage Configure(MirrorImage component, bool isNew)
        {
            component.Cooldown = cooldown;
            component.ResourceCost = resourceCost;
            component.Duration = Duration;
            component.SpawnDistance = Mathf.Max(Movement.Distance, Movement.Spread);
            component.Tint = Tint;
            if (isNew) component.Elapsed = cooldown;  // Ready as soon as it's equipped
            return component;
        }

        public override void Remove(EntityManager entityManager, Entity entity)
        {
            if (entityManager.HasComponent<MirrorImage>(entity))
            {
                var prefab = entityManager.GetComponentData<MirrorImage>(entity).Prefab;
                if (entityManager.Exists(prefab)) entityManager.DestroyEntity(prefab);
            }
            base.Remove(entityManager, entity);
        }

        /// <summary>
        /// Makes the owner's copy to summon, with the attacks among equipped on the same slots. ActiveSkillManager calls this
        /// whenever the skills change; a copy that's already out keeps what it has.
        /// </summary>
        public void SetUpCopy(EntityManager entityManager, Entity owner, List<(int SlotIndex, ActiveSkill Skill)> equipped)
        {
            var mirror = entityManager.GetComponentData<MirrorImage>(owner);
            if (entityManager.Exists(mirror.Prefab)) entityManager.DestroyEntity(mirror.Prefab);

            mirror.Prefab = CreatePrefab(entityManager);
            entityManager.SetComponentData(owner, mirror);
            CompanionEntitySetup.AddMirrorComponents(entityManager, mirror.Prefab, owner, SightRadius, Movement);

            var attacks = equipped.FindAll(e => (e.Skill.Tags & NotCopied) == 0);
            foreach (var (_, skill) in attacks)
                skill.Apply(entityManager, mirror.Prefab);

            // After the structural changes above
            var skills = entityManager.GetBuffer<Skill>(mirror.Prefab);
            foreach (var (slotIndex, skill) in attacks)
                skills.Add(ActiveSkillManager.ToSkill(slotIndex, skill));
        }

        // A copy of the player's body, with its whole group marked Prefab like a baked one, so nothing of it is in the world
        static Entity CreatePrefab(EntityManager entityManager)
        {
            using var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<PlayerPrefab>());
            var prefab = entityManager.Instantiate(query.GetSingleton<PlayerPrefab>().Value);
            using var group = entityManager.GetBuffer<LinkedEntityGroup>(prefab).Reinterpret<Entity>().ToNativeArray(Allocator.Temp);
            entityManager.AddComponent<Prefab>(group);
            return prefab;
        }
    }
}
