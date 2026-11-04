using UnityEngine;

namespace Passives
{
    /// <summary>Using a skill with a cooldown gives extra damage for a few seconds.</summary>
    [CreateAssetMenu(fileName = "Afterglow", menuName = "Passives/Afterglow")]
    public class AfterglowPassive : PassiveSkill<Afterglow>
    {
        [Tooltip("Extra damage (0.15 = +15%)")]
        public float DamageBonus = 0.15f;
        [Tooltip("Seconds the buff lasts after the last skill tagged Cooldown")]
        public float Duration = 4f;

        public override PassiveType Type => PassiveType.Afterglow;

        protected override Afterglow CreateComponent() => new Afterglow
        {
            DamageBonus = DamageBonus,
            Duration = Duration,
        };
    }
}
