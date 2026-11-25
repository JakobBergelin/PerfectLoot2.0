using UnityEngine;

namespace Equipment
{
    /// <summary>Physical damage has more critical chance against poisoned enemies.</summary>
    [CreateAssetMenu(fileName = "FesteringWounds", menuName = "Items/Legendary Powers/Festering Wounds")]
    public class FesteringWoundsPower : LegendaryPower<FesteringWounds>
    {
        [Tooltip("Added to critical chance: 0.3 = +30%")]
        public float CriticalChanceBonus = 0.3f;

        protected override FesteringWounds Configure(FesteringWounds component, bool isNew)
        {
            component.CriticalChanceBonus = CriticalChanceBonus;
            return component;
        }
    }
}
