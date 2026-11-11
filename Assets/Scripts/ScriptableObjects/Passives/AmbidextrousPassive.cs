using UnityEngine;

namespace Passives
{
    /// <summary>A share of Agility also counts toward the Strength that skills need.</summary>
    [CreateAssetMenu(fileName = "Ambidextrous", menuName = "Passives/Ambidextrous")]
    public class AmbidextrousPassive : PassiveSkill<Ambidextrous>
    {
        [Tooltip("Share of Agility that also counts toward Strength requirements (0.5 = half)")]
        public float AgilityForStrength = 0.5f;

        public override PassiveType Type => PassiveType.Ambidextrous;

        protected override Ambidextrous CreateComponent() => new Ambidextrous { AgilityForStrength = AgilityForStrength };
    }
}
