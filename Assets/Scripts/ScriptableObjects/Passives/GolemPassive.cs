using UnityEngine;

namespace Passives
{
    [CreateAssetMenu(fileName = "Golem", menuName = "Passives/Golem")]
    public class GolemPassive : PassiveSkill<Golem>
    {
        // Added to the multipliers, 1 = +100%. Golem also stops health regeneration.
        public float MaxHealthMultiplier = 1f;
        public float PhysicalResistanceMultiplier = 1f;

        public override PassiveType Type => PassiveType.Golem;

        protected override Golem CreateComponent() => new Golem
        {
            MaxHealthMultiplier = MaxHealthMultiplier,
            PhysicalResistanceMultiplier = PhysicalResistanceMultiplier
        };
    }
}
