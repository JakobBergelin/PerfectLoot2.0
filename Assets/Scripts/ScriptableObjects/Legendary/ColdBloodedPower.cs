using UnityEngine;

namespace Equipment
{
    /// <summary>The wearer's health regeneration no longer heals it; enemies around it take that regeneration as cold damage instead.</summary>
    [CreateAssetMenu(fileName = "ColdBlooded", menuName = "Items/Legendary Powers/Cold Blooded")]
    public class ColdBloodedPower : LegendaryPower<ColdBlooded>
    {
        [Tooltip("Cold damage per second for each point of health regeneration (1 = 100%)")]
        public float DamageMultiplier = 1f;
        [Tooltip("Meters around the wearer that are hit, scaled by its area of effect")]
        public float Radius = 3f;
        [Tooltip("Seconds between hits; each hit deals the damage of that many seconds")]
        public float TickRate = 0.5f;

        protected override ColdBlooded Configure(ColdBlooded component, bool isNew)
        {
            component.DamageMultiplier = DamageMultiplier;
            component.Radius = Radius;
            component.TickRate = TickRate;
            if (isNew) component.Timer = TickRate;
            return component;
        }
    }
}
