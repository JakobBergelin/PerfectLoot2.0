using Unity.Entities;
using UnityEngine;

namespace Equipment
{
    /// <summary>Every few hits the wearer takes, it spins and deals physical damage to the enemies around it.</summary>
    [CreateAssetMenu(fileName = "CounterStrike", menuName = "Items/Legendary Powers/Counter Strike")]
    public class CounterStrikePower : LegendaryPower<CounterStrike>
    {
        [Tooltip("Hits taken (that deal damage) that set off the strike")]
        public int HitsNeeded = 5;
        [Tooltip("Physical damage to each enemy around the wearer, before its damage %")]
        public float Damage = 4f;
        [Tooltip("Meters around the wearer that are hit, scaled by its area of effect")]
        public float Radius = 4f;
        [Tooltip("Seconds for the wearer's full turn")]
        public float SpinDuration = 0.3f;

        protected override CounterStrike Configure(CounterStrike component, bool isNew)
        {
            component.HitsNeeded = HitsNeeded;
            component.Damage = Damage;
            component.Radius = Radius;
            component.SpinDuration = SpinDuration;
            return component;
        }

        // Without the power nothing resets the hit count on the HUD anymore
        public override void Remove(EntityManager entityManager, Entity entity)
        {
            base.Remove(entityManager, entity);
            if (entityManager.HasComponent<CounterStrikeCharge>(entity))
                entityManager.SetComponentEnabled<CounterStrikeCharge>(entity, false);
        }
    }
}
