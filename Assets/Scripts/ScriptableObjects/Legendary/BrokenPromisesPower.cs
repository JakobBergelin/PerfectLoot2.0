using UnityEngine;

namespace Equipment
{
    /// <summary>After a streak of non-critical hits, critical chance is raised for a short time.</summary>
    [CreateAssetMenu(fileName = "BrokenPromises", menuName = "Items/Legendary Powers/Broken Promises")]
    public class BrokenPromisesPower : LegendaryPower<BrokenPromises>
    {
        [Tooltip("Non-critical hits in a row needed to trigger")]
        public int HitsRequired = 10;
        [Tooltip("Seconds the bonus lasts")]
        public float Duration = 2f;
        [Tooltip("Critical chance added while active (1 = +100%)")]
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
