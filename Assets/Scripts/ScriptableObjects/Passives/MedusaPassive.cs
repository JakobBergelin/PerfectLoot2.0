using UnityEngine;

namespace Passives
{
    /// <summary>Damage is taken from resource before health.</summary>
    [CreateAssetMenu(fileName = "Medusa", menuName = "Passives/Medusa")]
    public class MedusaPassive : PassiveSkill<Medusa>
    {
        public override PassiveType Type => PassiveType.Medusa;

        protected override Medusa CreateComponent() => new Medusa();
    }
}
