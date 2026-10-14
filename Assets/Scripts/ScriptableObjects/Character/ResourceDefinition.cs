using Unity.Entities;
using UnityEngine;

namespace Characters
{
    /// <summary>A resource type (mana, rage, ...). One asset per ResourceType, selected through CharacterManager.</summary>
    public abstract class ResourceDefinition : ScriptableObject
    {
        public string Name;
        public Sprite Icon;
        [TextArea] public string Description;

        public float MaxResource;
        public float Regeneration;  // Per second, can be negative (e.g. rage decays)

        public abstract ResourceType Type { get; }

        /// <summary>Adds this resource type's component to the entity, or refreshes its values if already present.</summary>
        public abstract void Apply(EntityManager entityManager, Entity entity);

        public abstract void Remove(EntityManager entityManager, Entity entity);
    }

    /// <summary>A resource type backed by a single ECS component built from the asset's values.</summary>
    public abstract class ResourceDefinition<T> : ResourceDefinition where T : unmanaged, IComponentData
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
