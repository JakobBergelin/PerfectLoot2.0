using UnityEngine;

namespace Passives
{
    [CreateAssetMenu(fileName = "Balance", menuName = "Passives/Balance")]
    public class BalancePassive : PassiveSkill<Balance>
    {
        // Damage bonus per point of the lowest of Strength, Agility and Intelligence (0.01 = 1%)
        public float DamagePerLowestAttribute = 0.4f;

        public override PassiveType Type => PassiveType.Balance;

        protected override Balance CreateComponent() => new Balance { DamagePerLowestAttribute = DamagePerLowestAttribute };
    }
}
