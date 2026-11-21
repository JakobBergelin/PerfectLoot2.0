using UnityEngine;

namespace Passives
{
    /// <summary>An enemy passive: it moves in hops, standing still between them, at the same average speed (HoppingSystem).</summary>
    [CreateAssetMenu(fileName = "Hopping", menuName = "Passives/Hopping")]
    public class HoppingPassive : PassiveSkill<Hopping>
    {
        [Tooltip("Seconds from one hop to the next")]
        public float Interval = 0.8f;
        [Tooltip("Seconds of each hop spent moving")]
        public float HopTime = 0.35f;

        public override PassiveType Type => PassiveType.Hopping;

        protected override Hopping CreateComponent() => new Hopping { Interval = Interval, HopTime = HopTime };
    }
}
