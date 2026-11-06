using UnityEngine;

namespace Passives
{
    /// <summary>Hits from in front of the wearer deal less damage, hits from behind it deal more.</summary>
    [CreateAssetMenu(fileName = "Bulwark", menuName = "Passives/Bulwark")]
    public class BulwarkPassive : PassiveSkill<Bulwark>
    {
        [Tooltip("Less damage from hits in front (0.4 = -40%)")]
        public float FrontReduction = 0.4f;
        [Tooltip("More damage from hits behind (0.5 = +50%)")]
        public float BackIncrease = 0.5f;

        public override PassiveType Type => PassiveType.Bulwark;

        protected override Bulwark CreateComponent() => new Bulwark
        {
            FrontReduction = FrontReduction,
            BackIncrease = BackIncrease,
        };
    }
}
