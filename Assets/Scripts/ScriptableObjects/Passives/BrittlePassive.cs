using UnityEngine;

namespace Passives
{
    /// <summary>Frozen enemies take more damage.</summary>
    [CreateAssetMenu(fileName = "Brittle", menuName = "Passives/Brittle")]
    public class BrittlePassive : PassiveSkill<Brittle>
    {
        [Tooltip("Extra damage to Frozen enemies (0.3 = +30%)")]
        public float DamageBonus = 0.3f;

        public override PassiveType Type => PassiveType.Brittle;

        protected override Brittle CreateComponent() => new Brittle
        {
            DamageBonus = DamageBonus,
        };
    }
}
