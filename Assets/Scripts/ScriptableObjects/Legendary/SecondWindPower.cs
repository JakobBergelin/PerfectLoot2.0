using UnityEngine;

namespace Equipment
{
    /// <summary>Health regeneration rises as the wearer's health falls: the full bonus at FullBonusHealth or below, none at full health.</summary>
    [CreateAssetMenu(fileName = "SecondWind", menuName = "Items/Legendary Powers/Second Wind")]
    public class SecondWindPower : LegendaryPower<SecondWind>
    {
        [Tooltip("Health regeneration multiplier added at FullBonusHealth or below (1 = +100%, doubled)")]
        public float MaxRegenerationBonus = 1f;
        [Tooltip("Share of max health at or below which the full bonus applies; it fades out linearly toward full health")]
        [Range(0f, 0.99f)] public float FullBonusHealth = 0.1f;

        protected override SecondWind Configure(SecondWind component, bool isNew)
        {
            component.MaxRegenerationBonus = MaxRegenerationBonus;
            component.FullBonusHealth = FullBonusHealth;
            return component;
        }
    }
}
