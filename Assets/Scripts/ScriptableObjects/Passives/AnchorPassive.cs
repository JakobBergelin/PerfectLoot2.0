using Unity.Entities;
using UnityEngine;

namespace Passives
{
    /// <summary>Standing still builds up armor; moving loses it all.</summary>
    [CreateAssetMenu(fileName = "Anchor", menuName = "Passives/Anchor")]
    public class AnchorPassive : PassiveSkill<Anchor>
    {
        [Tooltip("Armor for each stack")]
        [Min(1)] public int ArmorPerStack = 2;
        [Tooltip("Most stacks at once")]
        [Min(1)] public int MaxStacks = 10;
        [Tooltip("Seconds of standing still for each stack")]
        public float SecondsPerStack = 1f;

        public override PassiveType Type => PassiveType.Anchor;

        protected override Anchor CreateComponent() => new Anchor
        {
            ArmorPerStack = ArmorPerStack,
            MaxStacks = MaxStacks,
            SecondsPerStack = SecondsPerStack,
        };

        // Without the passive nothing turns the buff off anymore
        public override void Remove(EntityManager entityManager, Entity entity)
        {
            base.Remove(entityManager, entity);
            if (entityManager.HasComponent<AnchorBuff>(entity))
                entityManager.SetComponentEnabled<AnchorBuff>(entity, false);
        }
    }
}
