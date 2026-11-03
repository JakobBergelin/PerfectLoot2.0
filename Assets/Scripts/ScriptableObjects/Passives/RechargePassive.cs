using UnityEngine;

namespace Passives
{
    /// <summary>Spending resource on skills cuts the remaining cooldown of a random skill on cooldown.</summary>
    [CreateAssetMenu(fileName = "Recharge", menuName = "Passives/Recharge")]
    public class RechargePassive : PassiveSkill<Recharge>
    {
        [Tooltip("Resource spent on skills for each cooldown cut")]
        [Min(1f)] public float ResourcePerTrigger = 10f;
        [Tooltip("Seconds taken off the remaining cooldown of one random equipped skill on cooldown")]
        public float CooldownReduction = 1f;

        public override PassiveType Type => PassiveType.Recharge;

        protected override Recharge CreateComponent() => new Recharge
        {
            ResourcePerTrigger = ResourcePerTrigger,
            CooldownReduction = CooldownReduction,
            Random = Unity.Mathematics.Random.CreateFromIndex((uint)System.Environment.TickCount),
        };
    }
}
