using UnityEngine;

namespace Passives
{
    /// <summary>A random nearby enemy is the wearer's target. Other enemies take less damage from it; the target dying refunds resource.</summary>
    [CreateAssetMenu(fileName = "Quarry", menuName = "Passives/Quarry")]
    public class QuarryPassive : PassiveSkill<Quarry>
    {
        [Tooltip("Meters from the wearer a target is picked in and kept in")]
        public float Radius = 15f;
        [Tooltip("Less damage to enemies other than the target (0.3 = -30%)")]
        public float DamagePenalty = 0.3f;
        [Tooltip("Part of maximum resource refunded when the target dies (0.2 = 20%)")]
        public float ResourceRefund = 0.2f;

        public override PassiveType Type => PassiveType.Quarry;

        protected override Quarry CreateComponent() => new Quarry
        {
            Radius = Radius,
            DamagePenalty = DamagePenalty,
            ResourceRefund = ResourceRefund,
            Random = Unity.Mathematics.Random.CreateFromIndex((uint)System.Environment.TickCount),
        };
    }
}
