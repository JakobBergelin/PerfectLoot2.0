using UnityEngine;

namespace Equipment
{
    /// <summary>While the wearer's resource is below a share of its max it has extra critical chance.</summary>
    [CreateAssetMenu(fileName = "KeenFocus", menuName = "Items/Legendary Powers/Keen Focus")]
    public class KeenFocusPower : LegendaryPower<KeenFocus>
    {
        [Tooltip("Share of max resource below which the bonus is on")]
        [Range(0f, 1f)] public float ResourceThreshold = 0.5f;
        [Tooltip("Critical chance added while it's on (0.06 = +6%)")]
        public float CriticalChanceBonus = 0.06f;

        protected override KeenFocus Configure(KeenFocus component, bool isNew)
        {
            component.ResourceThreshold = ResourceThreshold;
            component.CriticalChanceBonus = CriticalChanceBonus;
            return component;
        }
    }
}
