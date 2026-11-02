using Unity.Entities;
using UnityEngine;

namespace Equipment
{
    /// <summary>While the wearer is below a share of its max health it gets the Death's Door buff: extra critical chance.</summary>
    [CreateAssetMenu(fileName = "DeathsDoor", menuName = "Items/Legendary Powers/Death's Door")]
    public class DeathsDoorPower : LegendaryPower<DeathsDoor>
    {
        [Tooltip("Share of max health below which the buff is on")]
        [Range(0f, 1f)] public float HealthThreshold = 0.25f;
        [Tooltip("Critical chance added while the buff is on (1 = +100%)")]
        public float CriticalChanceBonus = 1f;

        protected override DeathsDoor Configure(DeathsDoor component, bool isNew)
        {
            component.HealthThreshold = HealthThreshold;
            component.CriticalChanceBonus = CriticalChanceBonus;
            return component;
        }

        // Without the power nothing turns the buff off anymore
        public override void Remove(EntityManager entityManager, Entity entity)
        {
            base.Remove(entityManager, entity);
            if (entityManager.HasComponent<DeathsDoorBuff>(entity))
                entityManager.SetComponentEnabled<DeathsDoorBuff>(entity, false);
        }
    }
}
