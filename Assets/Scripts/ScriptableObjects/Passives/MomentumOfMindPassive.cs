using UnityEngine;

namespace Passives
{
    /// <summary>Intelligence for each of your skills on cooldown.</summary>
    [CreateAssetMenu(fileName = "MomentumOfMind", menuName = "Passives/Momentum Of Mind")]
    public class MomentumOfMindPassive : PassiveSkill<MomentumOfMind>
    {
        [Tooltip("Intelligence for each skill on cooldown right now")]
        public float IntelligencePerCooldown = 2f;

        public override PassiveType Type => PassiveType.MomentumOfMind;

        protected override MomentumOfMind CreateComponent() => new MomentumOfMind { IntelligencePerCooldown = IntelligencePerCooldown };
    }
}
