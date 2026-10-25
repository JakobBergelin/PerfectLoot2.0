using Unity.Entities;
using UnityEngine;

namespace ActiveSkills
{
    /// <summary>A usable skill. One asset per SkillType, equipped in an ActiveSkillSlot through ActiveSkillManager.</summary>
    public abstract class ActiveSkill : ScriptableObject
    {
        public string Name;
        public Sprite Icon;
        [TextArea] public string Description;
        [Tooltip("What kind of skill it is: effects pick skills by tag, and Melee / Mobility uses raise their events")]
        public SkillTags Tags;

        public abstract SkillType Type { get; }

        // Shown in UI (copied into SkillState). Subclasses override the ones that apply to them.
        public virtual float Cooldown => 0f;
        public virtual float ResourceCost => 0f;
        public virtual float ResourceCostPerSecond => 0f;

        /// <summary>Adds the skill's component (disabled until its key is pressed), or refreshes its configured values.</summary>
        public abstract void Apply(EntityManager entityManager, Entity entity);

        public abstract void Remove(EntityManager entityManager, Entity entity);
    }

    /// <summary>A skill backed by one enableable ECS component.</summary>
    public abstract class ActiveSkill<T> : ActiveSkill where T : unmanaged, IComponentData, IEnableableComponent
    {
        /// <summary>Copies this asset's values into the component, keeping its runtime fields (timers etc.).</summary>
        protected abstract T Configure(T component, bool isNew);

        public override void Apply(EntityManager entityManager, Entity entity)
        {
            if (entityManager.HasComponent<T>(entity))
            {
                entityManager.SetComponentData(entity, Configure(entityManager.GetComponentData<T>(entity), isNew: false));
            }
            else
            {
                entityManager.AddComponentData(entity, Configure(default, isNew: true));
                entityManager.SetComponentEnabled<T>(entity, false);
            }
        }

        public override void Remove(EntityManager entityManager, Entity entity)
        {
            if (entityManager.HasComponent<T>(entity))
                entityManager.RemoveComponent<T>(entity);
        }
    }
}
