using UnityEngine;

namespace Equipment
{
    /// <summary>Multishot fires in a full circle around the wearer, with a few more arrows.</summary>
    [CreateAssetMenu(fileName = "ArrowRing", menuName = "Items/Legendary Powers/Arrow Ring")]
    public class ArrowRingPower : LegendaryPower<ArrowRing>
    {
        [Tooltip("Arrows added to each Multishot volley")]
        [Min(0)] public int ExtraArrows = 4;

        protected override ArrowRing Configure(ArrowRing component, bool isNew)
        {
            component.ExtraArrows = ExtraArrows;
            return component;
        }
    }
}
