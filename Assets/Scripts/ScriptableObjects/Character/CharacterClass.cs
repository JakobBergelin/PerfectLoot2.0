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

        /// <summary>Adds this class's ClassAttributes to the entity, or refreshes them if already present.</summary>
        public void Apply(EntityManager entityManager, Entity entity)
        {
            var attributes = new ClassAttributes
            {
                StrengthPerLevel = StrengthPerLevel,
                AgilityPerLevel = AgilityPerLevel,
                IntelligencePerLevel = IntelligencePerLevel
            };
            if (entityManager.HasComponent<ClassAttributes>(entity)) entityManager.SetComponentData(entity, attributes);
            else entityManager.AddComponentData(entity, attributes);
        }

        public static void Remove(EntityManager entityManager, Entity entity)
        {
            if (entityManager.HasComponent<ClassAttributes>(entity))
                entityManager.RemoveComponent<ClassAttributes>(entity);
        }
    }
}
