using UnityEngine;

namespace Equipment
{
    /// <summary>Damage a killing hit had to spare carries over to the nearest enemy.</summary>
    [CreateAssetMenu(fileName = "Overkill", menuName = "Items/Legendary Powers/Overkill")]
    public class OverkillPower : LegendaryPower<Overkill>
    {
        [Tooltip("Meters to look for the next enemy, scaled by the wearer's area of effect")]
        public float Radius = 8f;

        protected override Overkill Configure(Overkill component, bool isNew)
        {
            component.Radius = Radius;
            return component;
        }
    }
}
