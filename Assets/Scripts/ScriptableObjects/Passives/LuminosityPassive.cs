using UnityEngine;

namespace Passives
{
    /// <summary>Critical hits heal nearby players.</summary>
    [CreateAssetMenu(fileName = "Luminosity", menuName = "Passives/Luminosity")]
    public class LuminosityPassive : PassiveSkill<Luminosity>
    {
        public float HealValue = 2f;
        public float HealRadius = 5f;

        public override PassiveType Type => PassiveType.Luminosity;

        protected override Luminosity CreateComponent() => new Luminosity { HealValue = HealValue, HealRadius = HealRadius };
    }
}
