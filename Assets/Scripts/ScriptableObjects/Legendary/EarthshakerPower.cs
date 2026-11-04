using UnityEngine;

namespace Equipment
{
    /// <summary>Casting a skill with a cooldown sends out a shockwave that scales with Strength.</summary>
    [CreateAssetMenu(fileName = "Earthshaker", menuName = "Items/Legendary Powers/Earthshaker")]
    public class EarthshakerPower : LegendaryPower<Earthshaker>
    {
        [Tooltip("Physical damage to each enemy around the wearer, before Strength and its damage %")]
        public float Damage = 3f;
        [Tooltip("Damage added for each point of the wearer's Strength")]
        public float DamagePerStrength = 0.1f;
        [Tooltip("Meters around the wearer that are hit, scaled by its area of effect")]
        public float Radius = 4f;
        [Tooltip("Knockback speed on hit, away from the wearer")]
        public float HitForce = 6f;

        protected override Earthshaker Configure(Earthshaker component, bool isNew)
        {
            component.Damage = Damage;
            component.DamagePerStrength = DamagePerStrength;
            component.Radius = Radius;
            component.HitForce = HitForce;
            return component;
        }
    }
}
