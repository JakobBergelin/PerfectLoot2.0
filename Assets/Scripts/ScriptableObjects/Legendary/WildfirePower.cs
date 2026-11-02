using UnityEngine;

namespace Equipment
{
    /// <summary>When a burning enemy dies, the burn damage it had left spreads to the enemies around it.</summary>
    [CreateAssetMenu(fileName = "Wildfire", menuName = "Items/Legendary Powers/Wildfire")]
    public class WildfirePower : LegendaryPower<Wildfire>
    {
        [Tooltip("Share of the dying enemy's remaining burn damage given to each enemy around it")]
        public float SpreadFraction = 1f;
        [Tooltip("Meters around the dying enemy that catch fire, scaled by the wearer's area of effect")]
        public float Radius = 4f;

        protected override Wildfire Configure(Wildfire component, bool isNew)
        {
            component.SpreadFraction = SpreadFraction;
            component.Radius = Radius;
            return component;
        }
    }
}
