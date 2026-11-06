using Unity.Entities;
using UnityEngine;

namespace Equipment
{
    /// <summary>Enemies with passives drop a soul when they die. Walking over it steals that passive for a few seconds.</summary>
    [CreateAssetMenu(fileName = "SoulThief", menuName = "Items/Legendary Powers/Soul Thief")]
    public class SoulThiefPower : LegendaryPower<SoulThief>
    {
        [Tooltip("Seconds a soul stays on the ground")]
        public float SoulLifetime = 6f;
        [Tooltip("Meters from the wearer a soul is taken at")]
        public float PickupRadius = 1.5f;
        [Tooltip("Seconds a stolen passive lasts, scaled by Buff Duration")]
        public float Duration = 8f;
        [Tooltip("Different passives held at once; a new one replaces the oldest")]
        [Min(1)] public int MaxSouls = 10;

        protected override SoulThief Configure(SoulThief component, bool isNew)
        {
            component.SoulLifetime = SoulLifetime;
            component.PickupRadius = PickupRadius;
            component.Duration = Duration;
            component.MaxSouls = MaxSouls;
            return component;
        }

        // Remove leaves the buffer: what's stolen keeps running out after the item comes off
        public override void Apply(EntityManager entityManager, Entity entity)
        {
            base.Apply(entityManager, entity);
            if (!entityManager.HasBuffer<StolenPassive>(entity)) entityManager.AddBuffer<StolenPassive>(entity);
        }
    }
}
