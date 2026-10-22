using UnityEngine;

namespace Equipment
{
    /// <summary>Incoming main physical hits deal a flat amount less, after armor. Strong against many weak hits.</summary>
    [CreateAssetMenu(fileName = "Vanguard", menuName = "Items/Legendary Powers/Vanguard")]
    public class VanguardPower : LegendaryPower<Vanguard>
    {
        [Tooltip("Removed from every incoming main physical hit, after armor (a hit can go down to 0)")]
        public float DamageReduction = 1f;

        protected override Vanguard Configure(Vanguard component, bool isNew)
        {
            component.DamageReduction = DamageReduction;
            return component;
        }
    }
}
