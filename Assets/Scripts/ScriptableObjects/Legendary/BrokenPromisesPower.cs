using UnityEngine;

namespace Equipment
{
    /// <summary>After a streak of non-critical hits, the wearer gets the Broken Promises buff: extra critical chance for a while.</summary>
    [CreateAssetMenu(fileName = "BrokenPromises", menuName = "Items/Legendary Powers/Broken Promises")]
    public class BrokenPromisesPower : LegendaryPower<BrokenPromises>
    {
        [Tooltip("Non-critical hits in a row needed to trigger")]
        public int HitsRequired = 10;
        [Tooltip("Seconds the buff lasts")]
        public float Duration = 2f;
        [Tooltip("Critical chance added while the buff is active (1 = +100%)")]
        public float CriticalChanceBonus = 1f;

        protected override BrokenPromises Configure(BrokenPromises component, bool isNew)
        {
            component.HitsRequired = HitsRequired;
            component.Duration = Duration;
            component.CriticalChanceBonus = CriticalChanceBonus;
            return component;
        }
    }
}
