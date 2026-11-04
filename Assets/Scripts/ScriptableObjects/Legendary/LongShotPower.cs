using UnityEngine;

namespace Equipment
{
    /// <summary>Projectile hits deal more damage the further they are from where they were fired.</summary>
    [CreateAssetMenu(fileName = "LongShot", menuName = "Items/Legendary Powers/Long Shot")]
    public class LongShotPower : LegendaryPower<LongShot>
    {
        [Tooltip("Extra damage per meter from where the projectile was fired (0.02 = +2%)")]
        public float DamagePerMeter = 0.02f;
        [Tooltip("Most extra damage (0.3 = +30%)")]
        public float MaxBonus = 0.3f;

        protected override LongShot Configure(LongShot component, bool isNew)
        {
            component.DamagePerMeter = DamagePerMeter;
            component.MaxBonus = MaxBonus;
            return component;
        }
    }
}
