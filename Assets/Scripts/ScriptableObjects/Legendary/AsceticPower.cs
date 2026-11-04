using Unity.Entities;
using UnityEngine;

namespace Equipment
{
    /// <summary>Damage follows the last skill used: up after a Generator skill, down after a skill with a cost.</summary>
    [CreateAssetMenu(fileName = "Ascetic", menuName = "Items/Legendary Powers/Ascetic")]
    public class AsceticPower : LegendaryPower<Ascetic>
    {
        [Tooltip("Extra damage after using a skill tagged Generator (0.25 = +25%)")]
        public float GeneratorBonus = 0.25f;
        [Tooltip("Less damage after using any other skill with a resource cost (0.1 = -10%)")]
        public float CostPenalty = 0.1f;

        protected override Ascetic Configure(Ascetic component, bool isNew)
        {
            component.GeneratorBonus = GeneratorBonus;
            component.CostPenalty = CostPenalty;
            return component;
        }

        // Without the power nothing turns the buff off anymore
        public override void Remove(EntityManager entityManager, Entity entity)
        {
            base.Remove(entityManager, entity);
            if (entityManager.HasComponent<AsceticBuff>(entity))
                entityManager.SetComponentEnabled<AsceticBuff>(entity, false);
        }
    }
}
