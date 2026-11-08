using UnityEngine;

namespace Equipment
{
    /// <summary>
    /// Critical hits with Generator skills can also cast a random equipped Area skill, without paying its resource or
    /// touching its cooldown.
    /// </summary>
    [CreateAssetMenu(fileName = "Cascade", menuName = "Items/Legendary Powers/Cascade")]
    public class CascadePower : LegendaryPower<Cascade>
    {
        [Tooltip("Chance (0-1) per frame with a Generator crit to cast an Area skill")]
        [Range(0f, 1f)] public float Chance = 0.1f;

        protected override Cascade Configure(Cascade component, bool isNew)
        {
            component.Chance = Chance;
            return component;
        }
    }
}
