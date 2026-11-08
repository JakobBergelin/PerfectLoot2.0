using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

namespace Characters
{
    /// <summary>A playable class. One asset per ClassType, selected through CharacterManager.</summary>
    [CreateAssetMenu(fileName = "Class", menuName = "Character/Class")]
    public class CharacterClass : ScriptableObject
    {
        public string Name;
        public Sprite Icon;
        [TextArea] public string Description;
        public ClassType Type;

        [Header("Attributes gained per level")]
        public int StrengthPerLevel;
        public int AgilityPerLevel;
        public int IntelligencePerLevel;

        [Tooltip("Always on for this class, outside the passive slots. Its PlayerSelectable should be off.")]
        public Passives.PassiveSkill ClassPassive;

        [Tooltip("Equipped (Normal) on a new character of this class: taken from the account database, or rolled into it")]
        public List<Equipment.RollableItem> StartingItems = new List<Equipment.RollableItem>();

        /// <summary>Adds this class's ClassAttributes and class passive to the entity, or refreshes them if already present.</summary>
        public void Apply(EntityManager entityManager, Entity entity)
        {
            if (ClassPassive != null) ClassPassive.Apply(entityManager, entity);

            var attributes = new ClassAttributes
            {
                StrengthPerLevel = StrengthPerLevel,
                AgilityPerLevel = AgilityPerLevel,
                IntelligencePerLevel = IntelligencePerLevel
            };
            if (entityManager.HasComponent<ClassAttributes>(entity)) entityManager.SetComponentData(entity, attributes);
            else entityManager.AddComponentData(entity, attributes);
        }

        /// <summary>Takes this class's passive off the entity (switching to another class).</summary>
        public void RemovePassive(EntityManager entityManager, Entity entity)
        {
            if (ClassPassive != null) ClassPassive.Remove(entityManager, entity);
        }

        public static void Remove(EntityManager entityManager, Entity entity)
        {
            if (entityManager.HasComponent<ClassAttributes>(entity))
                entityManager.RemoveComponent<ClassAttributes>(entity);
        }
    }
}
