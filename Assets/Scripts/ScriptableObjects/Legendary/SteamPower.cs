using UnityEngine;

namespace Equipment
{
    /// <summary>Cold hits turn the wearer's burns on an enemy into one burst of their remaining damage.</summary>
    [CreateAssetMenu(fileName = "Steam", menuName = "Items/Legendary Powers/Steam")]
    public class SteamPower : LegendaryPower<Steam>
    {
        [Tooltip("Share of the burns' remaining damage dealt at once (1 = all of it)")]
        public float DamageFraction = 1f;

        protected override Steam Configure(Steam component, bool isNew)
        {
            component.DamageFraction = DamageFraction;
            return component;
        }
    }
}
