using UnityEngine;

namespace Equipment
{
    /// <summary>Dash hits every enemy it passes through, harder with more movement speed.</summary>
    [CreateAssetMenu(fileName = "Stampede", menuName = "Items/Legendary Powers/Stampede")]
    public class StampedePower : LegendaryPower<Stampede>
    {
        [Tooltip("Damage: weapon damage × this × the movement speed bonus (10: +20% movement speed deals 2× weapon damage)")]
        [Min(0f)] public float WeaponDamagePerMovementSpeed = 10f;
        [Tooltip("Meters around the wearer that the dash hits")]
        [Min(0f)] public float Radius = 1.5f;
        [Tooltip("Knockback speed of the hit, away from the wearer")]
        [Min(0f)] public float HitForce = 4f;

        protected override Stampede Configure(Stampede component, bool isNew)
        {
            component.WeaponDamagePerMovementSpeed = WeaponDamagePerMovementSpeed;
            component.Radius = Radius;
            component.HitForce = HitForce;
            return component;
        }
    }
}
