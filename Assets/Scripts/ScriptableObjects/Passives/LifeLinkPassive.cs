using UnityEngine;

namespace Passives
{
    /// <summary>
    /// Links the wearer to the player with the lowest share of their max Health. That player also gets the wearer's Health
    /// regeneration and every heal the wearer receives, in full.
    /// </summary>
    [CreateAssetMenu(fileName = "LifeLink", menuName = "Passives/Life Link")]
    public class LifeLinkPassive : PassiveSkill<LifeLink>
    {
        [Tooltip("Meters from the wearer to a player it can link to")]
        public float Range = 30f;
        [Tooltip("Seconds between payouts of its regeneration, each shown as one combat text number")]
        public float TickRate = 0.5f;

        public override PassiveType Type => PassiveType.LifeLink;

        protected override LifeLink CreateComponent() => new LifeLink
        {
            Range = Range,
            TickRate = TickRate,
        };
    }
}
