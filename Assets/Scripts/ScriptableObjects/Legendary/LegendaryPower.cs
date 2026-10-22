using System;
using System.Reflection;
using System.Text.RegularExpressions;
using Unity.Entities;
using UnityEngine;

namespace Equipment
{
    /// <summary>
    /// The special effect of a Legendary or Unique item. One asset per power; InventoryManager applies it to the player
    /// while an item carrying it is equipped.
    /// </summary>
    public abstract class LegendaryPower : ScriptableObject
    {
        public string Name;
        [Tooltip("Can contain the power's own fields: {FieldName} or {FieldName:format}, e.g. {Duration} or {CriticalChanceBonus:P0}")]
        [TextArea] public string Description;

        public string DisplayName => string.IsNullOrEmpty(Name) ? name : Name;

        /// <summary>Description with every {Field} / {Field:format} replaced by this power's current value.</summary>
        public string FormattedDescription => Placeholder.Replace(Description ?? "", match =>
        {
            var field = GetType().GetField(match.Groups[1].Value, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null) return match.Value;
            var value = field.GetValue(this);
            string format = match.Groups[2].Success ? match.Groups[2].Value : "0.##";
            return value is IFormattable formattable ? formattable.ToString(format, null) : value?.ToString() ?? "";
        });

        static readonly Regex Placeholder = new Regex(@"\{(\w+)(?::([^}]+))?\}");

        /// <summary>Adds this power's component to the entity, or refreshes its values if already present.</summary>
        public abstract void Apply(EntityManager entityManager, Entity entity);

        public abstract void Remove(EntityManager entityManager, Entity entity);
    }

    /// <summary>A power backed by a single ECS component built from the asset's values.</summary>
    public abstract class LegendaryPower<T> : LegendaryPower where T : unmanaged, IComponentData
    {
        /// <summary>The component with this asset's values. isNew is false when refreshing an applied power (keep its runtime state).</summary>
        protected abstract T Configure(T component, bool isNew);

        public override void Apply(EntityManager entityManager, Entity entity)
        {
            if (!entityManager.HasComponent<T>(entity))
                entityManager.AddComponentData(entity, Configure(default, true));
            else
                entityManager.SetComponentData(entity, Configure(entityManager.GetComponentData<T>(entity), false));
        }

        public override void Remove(EntityManager entityManager, Entity entity)
        {
            if (entityManager.HasComponent<T>(entity))
                entityManager.RemoveComponent<T>(entity);
        }
    }

    /// <summary>An item type that can carry a LegendaryPower (Legendary and Unique items).</summary>
    public interface ILegendaryItem
    {
        LegendaryPower Power { get; }
    }
}
