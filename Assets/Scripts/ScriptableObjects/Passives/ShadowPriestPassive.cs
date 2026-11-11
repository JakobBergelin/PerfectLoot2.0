using UnityEngine;

namespace Passives
{
    /// <summary>
    /// Damage turns into healing by the share of Intelligence in Strength + Intelligence: all damage is that much lower,
    /// and that share of the damage dealt heals the wearer and allies nearby, split, as dark healing orbs.
    /// </summary>
    [CreateAssetMenu(fileName = "ShadowPriest", menuName = "Passives/Shadow Priest")]
    public class ShadowPriestPassive : PassiveSkill<ShadowPriest>
    {
        [Tooltip("Meters around the wearer an ally can be to get a share")]
        public float Range = 12f;
        [Tooltip("Seconds healing gathers before the next orbs fly (the first damage sends at once)")]
        public float SendInterval = 0.25f;
        [Tooltip("Meters per second the orbs fly")]
        public float ProjectileSpeed = 9f;

        public override PassiveType Type => PassiveType.ShadowPriest;

        protected override ShadowPriest CreateComponent() => new ShadowPriest
        {
            Range = Range,
            SendInterval = SendInterval,
            ProjectileSpeed = ProjectileSpeed,
        };
    }
}
