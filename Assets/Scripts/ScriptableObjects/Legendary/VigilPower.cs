using UnityEngine;

namespace Equipment
{
    /// <summary>Every hit the wearer dodges heals it a little.</summary>
    [CreateAssetMenu(fileName = "Vigil", menuName = "Items/Legendary Powers/Vigil")]
    public class VigilPower : LegendaryPower<Vigil>
    {
        [Tooltip("Health restored for each dodged hit")]
        public float HealAmount = 1f;

        protected override Vigil Configure(Vigil component, bool isNew)
        {
            component.HealAmount = HealAmount;
            return component;
        }
    }
}
