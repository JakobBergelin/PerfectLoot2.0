using UnityEngine;

namespace Passives
{
    /// <summary>Changing the element of your hits gives a short damage buff.</summary>
    [CreateAssetMenu(fileName = "Prism", menuName = "Passives/Prism")]
    public class PrismPassive : PassiveSkill<Prism>
    {
        [Tooltip("Damage while the buff is on (0.2 = +20%)")]
        public float DamageBonus = 0.2f;
        [Tooltip("Seconds the buff lasts after the last change of element")]
        public float Duration = 3f;

        public override PassiveType Type => PassiveType.Prism;

        protected override Prism CreateComponent() => new Prism
        {
            DamageBonus = DamageBonus,
            Duration = Duration,
        };
    }
}
