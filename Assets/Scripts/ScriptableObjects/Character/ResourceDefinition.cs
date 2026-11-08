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

        /// <summary>What sets this resource apart, as one line (e.g. Rage: "+3 for each hit you take"); null for nothing.</summary>
        public virtual string Rule => null;

        /// <summary>
        /// How it works, from its values, so the text follows when they're tuned: "Max 40 · Drains 3 per second" and the Rule
        /// on the next line. Shown on the character screen and in the Skills panel.
        /// </summary>
        public string Explanation
        {
            get
            {
                string flow = Regeneration > 0f ? $"Regenerates {Number(Regeneration)} per second"
                            : Regeneration < 0f ? $"Drains {Number(-Regeneration)} per second"
                            : "No regeneration of its own";
                string text = $"Max {Number(MaxResource)} · {flow}";
                return string.IsNullOrEmpty(Rule) ? text : text + "\n" + Rule;
            }
        }

        protected static string Number(float value) => value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        protected static string Percent(float share) => Mathf.RoundToInt(share * 100f) + "%";

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
