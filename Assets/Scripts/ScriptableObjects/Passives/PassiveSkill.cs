using Unity.Entities;
using UnityEngine;

namespace Passives
{
    /// <summary>A selectable passive skill. One asset per PassiveType, equipped through PassiveSkillManager.</summary>
    public abstract class PassiveSkill : ScriptableObject
    {
        public string Name;
        public Sprite Icon;
        [TextArea] public string Description;

        public abstract PassiveType Type { get; }

        /// <summary>Adds this passive's component to the entity, or refreshes its values if already present.</summary>
        public abstract void Apply(EntityManager entityManager, Entity entity);

        public abstract void Remove(EntityManager entityManager, Entity entity);
    }

    /// <summary>A passive backed by a single ECS component built from the asset's values.</summary>
    public abstract class PassiveSkill<T> : PassiveSkill where T : unmanaged, IComponentData
    {
        protected abstract T CreateComponent();

        public override void Apply(EntityManager entityManager, Entity entity)
        {
            if (!entityManager.HasComponent<T>(entity))
                entityManager.AddComponentData(entity, CreateComponent());
            else if (!ComponentType.ReadWrite<T>().IsZeroSized)
                entityManager.SetComponentData(entity, CreateComponent());
        }

        public override void Remove(EntityManager entityManager, Entity entity)
        {
            if (entityManager.HasComponent<T>(entity))
                entityManager.RemoveComponent<T>(entity);
        }
    }
}
