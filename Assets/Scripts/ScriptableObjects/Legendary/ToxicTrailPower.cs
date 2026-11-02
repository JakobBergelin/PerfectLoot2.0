using UnityEngine;

namespace Equipment
{
    /// <summary>While the wearer runs, it leaves poison hazards behind it.</summary>
    [CreateAssetMenu(fileName = "ToxicTrail", menuName = "Items/Legendary Powers/Toxic Trail")]
    public class ToxicTrailPower : LegendaryPower<ToxicTrail>
    {
        [Tooltip("Poison damage per second to each enemy standing in the trail, before the wearer's damage %")]
        public float DamagePerSecond = 2f;
        [Tooltip("Seconds each hazard lingers")]
        public float Lifetime = 5f;
        [Tooltip("Meters, scaled by the wearer's area of effect")]
        public float Radius = 1f;
        [Tooltip("Meters moved between hazards")]
        public float Spacing = 1.5f;

        protected override ToxicTrail Configure(ToxicTrail component, bool isNew)
        {
            component.DamagePerSecond = DamagePerSecond;
            component.Lifetime = Lifetime;
            component.Radius = Radius;
            component.Spacing = Spacing;
            return component;
        }
    }
}
