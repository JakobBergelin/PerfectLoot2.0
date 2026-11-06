using Unity.Entities;
using UnityEngine;

namespace Equipment
{
    /// <summary>Most of the damage the wearer takes is dealt over the next few seconds instead of at once.</summary>
    [CreateAssetMenu(fileName = "BorrowedTime", menuName = "Items/Legendary Powers/Borrowed Time")]
    public class BorrowedTimePower : LegendaryPower<BorrowedTime>
    {
        [Tooltip("Part of all damage taken that is delayed (0.8 = 80%)")]
        public float DelayedShare = 0.8f;
        [Tooltip("Seconds the delayed damage is spread over")]
        public float Duration = 5f;

        protected override BorrowedTime Configure(BorrowedTime component, bool isNew)
        {
            component.DelayedShare = DelayedShare;
            component.Duration = Duration;
            return component;
        }

        // Remove leaves the buffer: what's owed keeps ticking after the item comes off
        public override void Apply(EntityManager entityManager, Entity entity)
        {
            base.Apply(entityManager, entity);
            if (!entityManager.HasBuffer<DelayedDamage>(entity)) entityManager.AddBuffer<DelayedDamage>(entity);
        }
    }
}
